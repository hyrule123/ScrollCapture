using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 매우 긴 이미지도 표시할 수 있는 간단한 뷰어. Ctrl+휠로 10%~800% 확대/축소.
    /// (PictureBox + AutoScroll 은 32767px 를 넘는 크기에서 문제가 생기므로 직접 그림)
    /// 크롭: 이미지 테두리(가장자리/모서리 손잡이)를 안쪽으로 드래그해 남길 영역을 조절합니다.
    /// 원본 이미지는 바뀌지 않고, 잘려 나갈 부분만 어둡게 표시됩니다. (실제 자르기는 저장/복사 시)
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

        private const int HandleSize = 8;      // 손잡이 크기 (px)
        private const int HitTolerance = 6;    // 테두리/손잡이 판정 여유 (px)
        private const int AutoScrollZone = 28; // 이 거리 안으로 들어오면 자동 스크롤

        private readonly VScrollBar _v = new VScrollBar();
        private readonly HScrollBar _h = new HScrollBar();
        private readonly Timer _autoScrollTimer = new Timer { Interval = 15 };
        private Image _image;
        private float _zoom = 1f;      // 표시 배율 (1 = 100%)

        public const float MinZoom = 0.1f;
        public const float MaxZoom = 8f;
        private const float ZoomStepRatio = 1.25f;

        // 크롭 상태 (이미지 좌표). 기본값은 이미지 전체
        private Rectangle _crop;
        private bool _dragging;
        private Edge _dragEdges;
        private Point _lastMouse;      // 클라이언트 좌표

        public event EventHandler CropChanged;

        /// <summary>표시 배율이 바뀌었을 때.</summary>
        public event EventHandler ZoomChanged;

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
                _crop = value != null ? new Rectangle(0, 0, value.Width, value.Height) : Rectangle.Empty;
                EndDrag();
                _v.Value = 0;
                _h.Value = 0;
                UpdateScrollBars();
                Invalidate();
                RaiseCropChanged();
            }
        }

        /// <summary>현재 표시 배율 (1 = 100%).</summary>
        public float Zoom
        {
            get { return _zoom; }
        }

        /// <summary>
        /// 시작 배율: 이미지 가로가 미리보기보다 넓으면 가로가 맞도록 축소(최소 10%), 아니면 100%.
        /// </summary>
        public void ResetZoomToFitWidth()
        {
            if (_image == null) return;
            // 세로가 길면 스크롤바가 생기므로 그만큼 뺀 너비 기준
            int availW = ClientSize.Width - _v.Width;
            float zoom = 1f;
            if (availW > 0 && _image.Width > availW)
                zoom = Math.Max(MinZoom, (float)availW / _image.Width);
            _zoom = zoom;
            _v.Value = 0;
            _h.Value = 0;
            UpdateScrollBars();
            Invalidate();
            RaiseZoomChanged();
        }

        /// <summary>한 단계 확대/축소 (direction &gt; 0 확대). 기준점(클라이언트 좌표) 아래의 이미지 위치를 유지.</summary>
        private void ZoomStep(int direction, Point anchor)
        {
            if (_image == null || direction == 0) return;
            float old = _zoom;
            float next = direction > 0 ? old * ZoomStepRatio : old / ZoomStepRatio;
            // 100% 를 지나가면 한 번 100% 에서 멈춤
            if ((old < 1f && next > 1f) || (old > 1f && next < 1f)) next = 1f;
            next = Math.Max(MinZoom, Math.Min(MaxZoom, next));
            if (Math.Abs(next - old) < 0.0001f) return;

            // 기준점 아래의 이미지 좌표
            var o = ImageOrigin;
            float ix = (anchor.X - o.X) / old;
            float iy = (anchor.Y - o.Y) / old;

            _zoom = next;
            UpdateScrollBars();
            if (_v.Visible) _v.Value = Clamp((int)Math.Round(iy * next - anchor.Y), 0, MaxValue(_v));
            if (_h.Visible) _h.Value = Clamp((int)Math.Round(ix * next - anchor.X), 0, MaxValue(_h));

            if (_dragging) UpdateDrag();
            Invalidate();
            RaiseZoomChanged();
        }

        private void RaiseZoomChanged()
        {
            var handler = ZoomChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>남길 영역 (이미지 좌표). 크롭하지 않았으면 이미지 전체.</summary>
        public Rectangle CropRect
        {
            get { return _crop; }
        }

        /// <summary>크롭 영역이 이미지 전체와 다른지 여부.</summary>
        public bool IsCropped
        {
            get { return _image != null && _crop != new Rectangle(0, 0, _image.Width, _image.Height); }
        }

        /// <summary>크롭을 해제하고 이미지 전체로 되돌립니다.</summary>
        public void ResetCrop()
        {
            if (_image == null) return;
            EndDrag();
            SetCrop(new Rectangle(0, 0, _image.Width, _image.Height));
        }

        private void RaiseCropChanged()
        {
            var handler = CropChanged;
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
            get { return _zoom; }
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
            if (_dragging) UpdateDrag();
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
            if ((ModifierKeys & Keys.Control) != 0)
            {
                // Ctrl + 휠: 마우스 위치를 기준으로 확대/축소
                ZoomStep(Math.Sign(e.Delta), e.Location);
                return;
            }
            var bar = (ModifierKeys & Keys.Shift) != 0 ? (ScrollBar)_h : _v;
            int step = Math.Max(1, bar.LargeChange / 4);
            ScrollBy(bar, -Math.Sign(e.Delta) * step * Math.Max(1, Math.Abs(e.Delta) / 120));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_image == null || e.Button != MouseButtons.Left) return;
            if (!Viewport.Contains(e.Location)) return;

            Edge edges = HitTestEdges(e.Location);
            if (edges == Edge.None) return;

            _dragging = true;
            _dragEdges = edges;
            _lastMouse = e.Location;
            Capture = true;
            _autoScrollTimer.Start();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_image == null) return;

            if (_dragging)
            {
                _lastMouse = e.Location;
                UpdateDrag();
                return;
            }

            Cursor = CursorFor(Viewport.Contains(e.Location) ? HitTestEdges(e.Location) : Edge.None);
        }

        private static Cursor CursorFor(Edge edges)
        {
            if (edges == (Edge.Left | Edge.Top) || edges == (Edge.Right | Edge.Bottom)) return Cursors.SizeNWSE;
            if (edges == (Edge.Right | Edge.Top) || edges == (Edge.Left | Edge.Bottom)) return Cursors.SizeNESW;
            if (edges == Edge.Left || edges == Edge.Right) return Cursors.SizeWE;
            if (edges == Edge.Top || edges == Edge.Bottom) return Cursors.SizeNS;
            return Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _lastMouse = e.Location;
            UpdateDrag();
            EndDrag();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _dragging) EndDrag();
        }

        private void EndDrag()
        {
            bool was = _dragging;
            _dragging = false;
            _dragEdges = Edge.None;
            _autoScrollTimer.Stop();
            if (Capture) Capture = false;
            if (was) Invalidate();
        }

        private Edge HitTestEdges(Point c)
        {
            if (_image == null || _crop.IsEmpty) return Edge.None;
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
            if (_image == null || !_dragging) return;
            var p = ClientToImage(_lastMouse);
            int left = _crop.Left, top = _crop.Top, right = _crop.Right, bottom = _crop.Bottom;

            // 반대편 가장자리를 넘지 않도록 최소 1px 유지
            if ((_dragEdges & Edge.Left) != 0) left = Math.Min(p.X, right - 1);
            if ((_dragEdges & Edge.Right) != 0) right = Math.Max(p.X, left + 1);
            if ((_dragEdges & Edge.Top) != 0) top = Math.Min(p.Y, bottom - 1);
            if ((_dragEdges & Edge.Bottom) != 0) bottom = Math.Max(p.Y, top + 1);

            SetCrop(Rectangle.FromLTRB(left, top, right, bottom));
        }

        private void SetCrop(Rectangle r)
        {
            if (_image == null) return;
            r = Rectangle.Intersect(r, new Rectangle(0, 0, _image.Width, _image.Height));
            if (r.Width <= 0 || r.Height <= 0) return;
            if (r == _crop) return;
            _crop = r;
            Invalidate();
            RaiseCropChanged();
        }

        /// <summary>드래그 중 마우스가 가장자리 근처(또는 바깥)에 있으면 그 방향으로 스크롤.</summary>
        private void AutoScrollTick()
        {
            if (!_dragging) { _autoScrollTimer.Stop(); return; }

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
                // 확대/축소 모두 보간 없이 그림
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_image, dest, new RectangleF(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
            }

            PaintCropOverlay(g, vp);

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
            var sel = ImageToClient(_crop);
            bool cropped = IsCropped;

            // 잘려 나갈 부분을 어둡게
            if (cropped)
            {
                using (var region = new Region(imageRect))
                using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                {
                    region.Exclude(sel);
                    g.FillRegion(dim, region);
                }
            }

            // 남길 영역 테두리: 크롭 중이면 강조, 아니면 손잡이 위치만 은은하게
            var r = Rectangle.Round(sel);
            if (cropped || _dragging)
            {
                using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1f))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            // 손잡이: 네 모서리 + 각 변의 가운데 (긴 이미지는 화면에 보이는 구간의 가운데)
            var visible = Rectangle.Intersect(r, vp);
            int midX = visible.Width > 0 ? visible.Left + visible.Width / 2 : r.Left + r.Width / 2;
            int midY = visible.Height > 0 ? visible.Top + visible.Height / 2 : r.Top + r.Height / 2;
            int x0 = r.Left, x1 = r.Right - 1, y0 = r.Top, y1 = r.Bottom - 1;
            var points = new[]
            {
                new Point(x0, y0), new Point(midX, y0), new Point(x1, y0),
                new Point(x0, midY), new Point(x1, midY),
                new Point(x0, y1), new Point(midX, y1), new Point(x1, y1),
            };
            using (var fill = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(0, 120, 215), 1f))
            {
                foreach (var p in points)
                {
                    // 화면 밖으로 반쯤 나가는 손잡이는 안쪽으로 붙여서 보이게
                    int hx = Clamp(p.X - HandleSize / 2, vp.Left, vp.Right - HandleSize);
                    int hy = Clamp(p.Y - HandleSize / 2, vp.Top, vp.Bottom - HandleSize);
                    if (p.Y < vp.Top - HandleSize || p.Y > vp.Bottom + HandleSize) continue;
                    if (p.X < vp.Left - HandleSize || p.X > vp.Right + HandleSize) continue;
                    var hr = new Rectangle(hx, hy, HandleSize, HandleSize);
                    g.FillRectangle(fill, hr);
                    g.DrawRectangle(border, hr);
                }
            }

            // 드래그 중에는 남길 크기 표시
            if (_dragging)
            {
                string text = string.Format("{0} × {1:N0} px", _crop.Width, _crop.Height);
                DrawLabel(g, text, new Point(vp.Left + 8, vp.Top + 8));
            }
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
