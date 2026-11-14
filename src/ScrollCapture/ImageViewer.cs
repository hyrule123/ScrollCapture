using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 매우 긴 이미지도 표시할 수 있는 간단한 뷰어.
    /// (PictureBox + AutoScroll 은 32767px 를 넘는 크기에서 문제가 생기므로 직접 그림)
    /// 크롭 모드에서는 드래그로 사각형을 선택하고, 손잡이로 크기 조절/안쪽 드래그로 이동할 수 있습니다.
    /// 드래그 중 창 가장자리에 닿으면 자동으로 스크롤됩니다.
    /// </summary>
    internal sealed class ImageViewer : Control
    {
        [Flags]
        private enum Edge
        {
            None = 0,
            Left = 1,
            Top = 2,
            Right = 4,
            Bottom = 8,
        }

        private enum DragKind
        {
            None,
            New,
            Move,
            Resize,
        }

        private const int HandleSize = 8;      // 손잡이 크기 (px)
        private const int HitTolerance = 6;    // 테두리/손잡이 판정 여유 (px)
        private const int AutoScrollZone = 28; // 이 거리 안으로 들어오면 자동 스크롤

        private readonly VScrollBar _v = new VScrollBar();
        private readonly HScrollBar _h = new HScrollBar();
        private readonly Timer _autoScrollTimer = new Timer { Interval = 15 };
        private Image _image;
        private bool _fitWidth = true;

        // 크롭 상태 (이미지 좌표)
        private bool _cropMode;
        private Rectangle _crop;
        private DragKind _drag;
        private Edge _dragEdges;
        private Point _dragAnchor;     // New: 시작점, Move: 선택 영역 내 잡은 위치
        private Rectangle _dragStartRect;
        private Point _lastMouse;      // 클라이언트 좌표

        public event EventHandler CropSelectionChanged;

        public ImageViewer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(64, 64, 64);

            _v.Dock = DockStyle.Right;
            _h.Dock = DockStyle.Bottom;
            _v.ValueChanged += (s, e) => OnViewScrolled();
            _h.ValueChanged += (s, e) => OnViewScrolled();
            Controls.Add(_v);
            Controls.Add(_h);

            _autoScrollTimer.Tick += (s, e) => AutoScrollTick();
        }

        public Image Image
        {
            get { return _image; }
            set
            {
                _image = value;
                _crop = Rectangle.Empty;
                EndDrag();
                _v.Value = 0;
                _h.Value = 0;
                UpdateScrollBars();
                Invalidate();
                RaiseCropChanged();
            }
        }

        /// <summary>true 이면 창 너비에 맞춰 축소, false 이면 원본 크기(100%).</summary>
        public bool FitWidth
        {
            get { return _fitWidth; }
            set
            {
                if (_fitWidth == value) return;
                double ratio = _v.Maximum > 0 ? (double)_v.Value / _v.Maximum : 0;
                _fitWidth = value;
                UpdateScrollBars();
                _v.Value = Clamp((int)(ratio * _v.Maximum), 0, MaxValue(_v));
                Invalidate();
            }
        }

        /// <summary>크롭 모드 여부. 끄면 선택 영역이 지워집니다.</summary>
        public bool CropMode
        {
            get { return _cropMode; }
            set
            {
                if (_cropMode == value) return;
                _cropMode = value;
                _crop = Rectangle.Empty;
                EndDrag();
                Cursor = value ? Cursors.Cross : Cursors.Default;
                Invalidate();
                RaiseCropChanged();
            }
        }

        /// <summary>현재 크롭 선택 영역 (이미지 좌표). 선택이 없으면 Empty.</summary>
        public Rectangle CropSelection
        {
            get { return _crop; }
        }

        private void RaiseCropChanged()
        {
            var handler = CropSelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ───────────── 레이아웃 / 좌표 변환 ─────────────

        private Rectangle Viewport
        {
            get
            {
                int w = ClientSize.Width - (_v.Visible ? _v.Width : 0);
                int h = ClientSize.Height - (_h.Visible ? _h.Height : 0);
                return new Rectangle(0, 0, Math.Max(0, w), Math.Max(0, h));
            }
        }

        private float ScaleFactor
        {
            get
            {
                if (_image == null || !_fitWidth) return 1f;
                int availW = ClientSize.Width - _v.Width;
                if (availW <= 0) return 1f;
                return Math.Min(1f, (float)availW / _image.Width);
            }
        }

        /// <summary>이미지 왼쪽 위 모서리의 클라이언트 좌표.</summary>
        private PointF ImageOrigin
        {
            get
            {
                float s = ScaleFactor;
                var vp = Viewport;
                int contentW = (int)Math.Ceiling(_image.Width * s);
                int offX = _h.Visible ? _h.Value : 0;
                int offY = _v.Visible ? _v.Value : 0;
                float originX = contentW < vp.Width ? (vp.Width - contentW) / 2 : -offX;
                return new PointF(originX, -offY);
            }
        }

        private RectangleF ImageToClient(Rectangle r)
        {
            float s = ScaleFactor;
            var o = ImageOrigin;
            return new RectangleF(o.X + r.X * s, o.Y + r.Y * s, r.Width * s, r.Height * s);
        }

        /// <summary>클라이언트 좌표 → 이미지 좌표 (이미지 범위로 제한, 끝점 포함).</summary>
        private Point ClientToImage(Point c)
        {
            float s = ScaleFactor;
            var o = ImageOrigin;
            int x = (int)Math.Round((c.X - o.X) / s);
            int y = (int)Math.Round((c.Y - o.Y) / s);
            return new Point(Clamp(x, 0, _image.Width), Clamp(y, 0, _image.Height));
        }

        private static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }

        private static int MaxValue(ScrollBar sb) { return Math.Max(0, sb.Maximum - sb.LargeChange + 1); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollBars();
        }

        private void UpdateScrollBars()
        {
            if (_image == null)
            {
                _v.Visible = _h.Visible = false;
                return;
            }

            float s = ScaleFactor;
            int contentW = (int)Math.Ceiling(_image.Width * s);
            int contentH = (int)Math.Ceiling(_image.Height * s);

            int vw = ClientSize.Width, vh = ClientSize.Height;
            bool needV = contentH > vh;
            bool needH = contentW > vw - (needV ? _v.Width : 0);
            if (needH && !needV) needV = contentH > vh - _h.Height;

            _v.Visible = needV;
            _h.Visible = needH;

            var vp = Viewport;
            SetupBar(_v, contentH, vp.Height);
            SetupBar(_h, contentW, vp.Width);
        }

        private static void SetupBar(ScrollBar sb, int content, int viewport)
        {
            if (viewport <= 0) viewport = 1;
            sb.Minimum = 0;
            sb.Maximum = Math.Max(0, content - 1);
            sb.LargeChange = Math.Max(1, Math.Min(viewport, content));
            sb.SmallChange = Math.Max(1, viewport / 10);
            sb.Value = Clamp(sb.Value, 0, MaxValue(sb));
        }

        private void OnViewScrolled()
        {
            // 드래그 중 스크롤되면 마우스 아래의 이미지 위치가 바뀌므로 선택 영역도 갱신
            if (_drag != DragKind.None) UpdateDrag();
            Invalidate();
        }

        private static bool ScrollBy(ScrollBar bar, int delta)
        {
            if (!bar.Visible || delta == 0) return false;
            int v = Clamp(bar.Value + delta, 0, MaxValue(bar));
            if (v == bar.Value) return false;
            bar.Value = v;
            return true;
        }

        // ───────────── 마우스 / 키보드 ─────────────

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image == null) return;
            var bar = (ModifierKeys & Keys.Shift) != 0 ? (ScrollBar)_h : _v;
            int step = Math.Max(1, bar.LargeChange / 4);
            ScrollBy(bar, -Math.Sign(e.Delta) * step * Math.Max(1, Math.Abs(e.Delta) / 120));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (!_cropMode || _image == null || e.Button != MouseButtons.Left) return;
            if (!Viewport.Contains(e.Location)) return;

            _lastMouse = e.Location;
            _dragStartRect = _crop;
            Edge edges = HitTestEdges(e.Location);

            if (edges != Edge.None)
            {
                _drag = DragKind.Resize;
                _dragEdges = edges;
            }
            else if (!_crop.IsEmpty && ImageToClient(_crop).Contains(e.Location))
            {
                _drag = DragKind.Move;
                var p = ClientToImage(e.Location);
                _dragAnchor = new Point(p.X - _crop.X, p.Y - _crop.Y);
            }
            else
            {
                _drag = DragKind.New;
                _dragAnchor = ClientToImage(e.Location);
                SetCrop(Rectangle.Empty);
            }

            Capture = true;
            _autoScrollTimer.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_cropMode || _image == null) return;

            if (_drag != DragKind.None)
            {
                _lastMouse = e.Location;
                UpdateDrag();
                return;
            }

            // 커서 모양: 손잡이/테두리, 안쪽(이동), 바깥(새로 선택)
            Edge edges = HitTestEdges(e.Location);
            if (edges == (Edge.Left | Edge.Top) || edges == (Edge.Right | Edge.Bottom)) Cursor = Cursors.SizeNWSE;
            else if (edges == (Edge.Right | Edge.Top) || edges == (Edge.Left | Edge.Bottom)) Cursor = Cursors.SizeNESW;
            else if (edges == Edge.Left || edges == Edge.Right) Cursor = Cursors.SizeWE;
            else if (edges == Edge.Top || edges == Edge.Bottom) Cursor = Cursors.SizeNS;
            else if (!_crop.IsEmpty && ImageToClient(_crop).Contains(e.Location)) Cursor = Cursors.SizeAll;
            else Cursor = Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == DragKind.None || e.Button != MouseButtons.Left) return;
            _lastMouse = e.Location;
            UpdateDrag();
            EndDrag();

            // 너무 작은 선택은 취소
            if (_crop.Width < 2 || _crop.Height < 2) SetCrop(Rectangle.Empty);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _drag != DragKind.None) EndDrag();
        }

        private void EndDrag()
        {
            _drag = DragKind.None;
            _dragEdges = Edge.None;
            _autoScrollTimer.Stop();
            if (Capture) Capture = false;
        }

        private Edge HitTestEdges(Point c)
        {
            if (_crop.IsEmpty) return Edge.None;
            var r = ImageToClient(_crop);
            var outer = RectangleF.Inflate(r, HitTolerance, HitTolerance);
            if (!outer.Contains(c)) return Edge.None;

            Edge edges = Edge.None;
            if (Math.Abs(c.X - r.Left) <= HitTolerance) edges |= Edge.Left;
            else if (Math.Abs(c.X - r.Right) <= HitTolerance) edges |= Edge.Right;
            if (Math.Abs(c.Y - r.Top) <= HitTolerance) edges |= Edge.Top;
            else if (Math.Abs(c.Y - r.Bottom) <= HitTolerance) edges |= Edge.Bottom;
            return edges;
        }

        private void UpdateDrag()
        {
            if (_image == null) return;
            var p = ClientToImage(_lastMouse);
            int w = _image.Width, h = _image.Height;

            switch (_drag)
            {
                case DragKind.New:
                    SetCrop(Rectangle.FromLTRB(
                        Math.Min(_dragAnchor.X, p.X), Math.Min(_dragAnchor.Y, p.Y),
                        Math.Max(_dragAnchor.X, p.X), Math.Max(_dragAnchor.Y, p.Y)));
                    break;

                case DragKind.Move:
                {
                    int x = Clamp(p.X - _dragAnchor.X, 0, w - _crop.Width);
                    int y = Clamp(p.Y - _dragAnchor.Y, 0, h - _crop.Height);
                    SetCrop(new Rectangle(x, y, _crop.Width, _crop.Height));
                    break;
                }

                case DragKind.Resize:
                {
                    var r = _dragStartRect;
                    int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
                    if ((_dragEdges & Edge.Left) != 0) left = p.X;
                    if ((_dragEdges & Edge.Right) != 0) right = p.X;
                    if ((_dragEdges & Edge.Top) != 0) top = p.Y;
                    if ((_dragEdges & Edge.Bottom) != 0) bottom = p.Y;
                    // 반대편을 넘어가면 뒤집어서 정규화
                    SetCrop(Rectangle.FromLTRB(
                        Math.Min(left, right), Math.Min(top, bottom),
                        Math.Max(left, right), Math.Max(top, bottom)));
                    break;
                }
            }
        }

        private void SetCrop(Rectangle r)
        {
            if (_image != null && !r.IsEmpty)
                r = Rectangle.Intersect(r, new Rectangle(0, 0, _image.Width, _image.Height));
            if (r.Width <= 0 || r.Height <= 0) r = Rectangle.Empty;
            if (r == _crop) return;
            _crop = r;
            Invalidate();
            RaiseCropChanged();
        }

        /// <summary>드래그 중 마우스가 가장자리 근처(또는 바깥)에 있으면 그 방향으로 스크롤.</summary>
        private void AutoScrollTick()
        {
            if (_drag == DragKind.None) { _autoScrollTimer.Stop(); return; }

            var vp = Viewport;
            int dy = EdgeSpeed(_lastMouse.Y, vp.Top, vp.Bottom);
            int dx = EdgeSpeed(_lastMouse.X, vp.Left, vp.Right);
            bool scrolled = ScrollBy(_v, dy);
            scrolled |= ScrollBy(_h, dx);
            if (scrolled) UpdateDrag();
        }

        private static int EdgeSpeed(int pos, int min, int max)
        {
            if (pos < min + AutoScrollZone)
            {
                int dist = min + AutoScrollZone - pos;
                return -Math.Min(80, Math.Max(2, dist / 2));
            }
            if (pos > max - AutoScrollZone)
            {
                int dist = pos - (max - AutoScrollZone);
                return Math.Min(80, Math.Max(2, dist / 2));
            }
            return 0;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
                case Keys.Enter:
                    return _cropMode; // 크롭 적용 키가 대화상자 처리로 빠지지 않도록
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_v.Visible) return;
            int v = _v.Value;
            switch (e.KeyCode)
            {
                case Keys.Up: v -= _v.SmallChange; break;
                case Keys.Down: v += _v.SmallChange; break;
                case Keys.PageUp: v -= _v.LargeChange; break;
                case Keys.PageDown: v += _v.LargeChange; break;
                case Keys.Home: v = 0; break;
                case Keys.End: v = MaxValue(_v); break;
                default: return;
            }
            _v.Value = Clamp(v, 0, MaxValue(_v));
        }

        // ───────────── 그리기 ─────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image == null) return;

            float s = ScaleFactor;
            var vp = Viewport;
            var origin = ImageOrigin;

            // 화면에 보이는 부분만 원본에서 잘라 그림
            float srcY = -origin.Y / s;
            float srcH = Math.Min(_image.Height - srcY, vp.Height / s);
            float srcX = origin.X < 0 ? -origin.X / s : 0;
            float srcW = Math.Min(_image.Width - srcX, (vp.Width - Math.Max(0, origin.X)) / s);

            g.SetClip(vp);
            if (srcH > 0 && srcW > 0)
            {
                var dest = new RectangleF(Math.Max(0, origin.X), 0, srcW * s, srcH * s);
                g.InterpolationMode = s < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_image, dest, new RectangleF(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
            }

            if (_cropMode) PaintCropOverlay(g, vp);

            if (_v.Visible && _h.Visible)
            {
                using (var b = new SolidBrush(SystemColors.Control))
                {
                    g.ResetClip();
                    g.FillRectangle(b, vp.Right, vp.Bottom, _v.Width, _h.Height);
                }
            }
        }

        private void PaintCropOverlay(Graphics g, Rectangle vp)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            var imageRect = ImageToClient(new Rectangle(0, 0, _image.Width, _image.Height));

            if (_crop.IsEmpty)
            {
                using (var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    g.FillRectangle(dim, imageRect);
                DrawLabel(g, "드래그해서 남길 영역을 선택하세요  (Enter: 적용 · Esc: 취소)", new Point(vp.Left + 8, vp.Top + 8));
                return;
            }

            var sel = ImageToClient(_crop);

            // 선택 영역 바깥을 어둡게
            using (var region = new Region(imageRect))
            using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                region.Exclude(sel);
                g.FillRegion(dim, region);
            }

            var r = Rectangle.Round(sel);
            using (var white = new Pen(Color.White, 1f))
            using (var dash = new Pen(Color.FromArgb(0, 120, 215), 1f) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(white, r.X, r.Y, r.Width - 1, r.Height - 1);
                g.DrawRectangle(dash, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            // 손잡이 8개
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            var points = new[]
            {
                new Point(r.Left, r.Top), new Point(cx, r.Top), new Point(r.Right - 1, r.Top),
                new Point(r.Left, cy), new Point(r.Right - 1, cy),
                new Point(r.Left, r.Bottom - 1), new Point(cx, r.Bottom - 1), new Point(r.Right - 1, r.Bottom - 1),
            };
            using (var fill = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(0, 120, 215), 1f))
            {
                foreach (var p in points)
                {
                    var hr = new Rectangle(p.X - HandleSize / 2, p.Y - HandleSize / 2, HandleSize, HandleSize);
                    g.FillRectangle(fill, hr);
                    g.DrawRectangle(border, hr);
                }
            }

            // 크기 표시: 선택 영역 위쪽 (보이지 않으면 뷰 위쪽)
            string text = string.Format("{0} × {1:N0} px", _crop.Width, _crop.Height);
            int labelY = r.Top - 26;
            if (labelY < vp.Top + 4) labelY = Math.Max(vp.Top + 4, Math.Min(r.Top + 6, vp.Bottom - 30));
            DrawLabel(g, text, new Point(Math.Max(vp.Left + 4, r.Left), labelY));
        }

        private void DrawLabel(Graphics g, string text, Point pos)
        {
            var size = TextRenderer.MeasureText(g, text, Font);
            var bg = new Rectangle(pos.X, pos.Y, size.Width + 12, size.Height + 6);
            using (var brush = new SolidBrush(Color.FromArgb(210, 20, 20, 20)))
                g.FillRectangle(brush, bg);
            TextRenderer.DrawText(g, text, Font, new Point(pos.X + 6, pos.Y + 3), Color.White);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _autoScrollTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
