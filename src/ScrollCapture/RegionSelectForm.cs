using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 화면 전체를 덮는 영역 선택 오버레이.
    ///  - 드래그: 사각형 영역 선택
    ///  - 클릭: 마우스 아래의 창/컨트롤 영역 선택
    ///  - Esc / 우클릭: 취소
    /// </summary>
    internal sealed class RegionSelectForm : Form
    {
        private readonly Bitmap _screen;
        private readonly Bitmap _dimmed;
        private readonly Rectangle _virtual;
        private readonly Font _font = new Font("Segoe UI", 10f, FontStyle.Regular);

        private bool _mouseDown;
        private bool _dragging;
        private Point _dragStart;          // 클라이언트 좌표
        private Rectangle _selection;      // 클라이언트 좌표
        private Rectangle _hover;          // 클라이언트 좌표
        private Point _lastHoverPt = new Point(int.MinValue, int.MinValue);

        public Rectangle SelectedRegion { get; private set; } // 화면 좌표

        public RegionSelectForm(Bitmap screenshot, Rectangle virtualBounds)
        {
            _screen = screenshot;
            _virtual = virtualBounds;
            _dimmed = CreateDimmed(screenshot);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            Bounds = virtualBounds;
            Text = "ScrollCapture - 영역 선택";
        }

        private static Bitmap CreateDimmed(Bitmap src)
        {
            var bmp = new Bitmap(src.Width, src.Height, src.PixelFormat);
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                g.DrawImageUnscaled(src, 0, 0);
                g.FillRectangle(brush, 0, 0, src.Width, src.Height);
            }
            return bmp;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Bounds = _virtual;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            UpdateHover(PointToClient(Cursor.Position));
        }

        private Rectangle Current
        {
            get { return _dragging ? _selection : _hover; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 깜빡임 방지: 배경은 OnPaint 에서 모두 그림
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

            var rect = Current;
            if (rect.Width > 0 && rect.Height > 0)
            {
                var visible = Rectangle.Intersect(rect, e.ClipRectangle);
                if (visible.Width > 0 && visible.Height > 0)
                    g.DrawImage(_screen, visible, visible, GraphicsUnit.Pixel);

                using (var pen = new Pen(_dragging ? Color.FromArgb(255, 0, 120, 215) : Color.FromArgb(255, 255, 80, 40), 2f))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(pen, rect);
                }

                string sizeText = string.Format("{0} × {1}", rect.Width, rect.Height);
                DrawLabel(g, sizeText, new Point(rect.Left, rect.Top - 26 >= 0 ? rect.Top - 26 : rect.Top + 4));
            }

            // 안내 문구 (주 모니터 상단)
            var primary = Screen.PrimaryScreen.Bounds;
            var hintPos = new Point(primary.X - _virtual.X + 16, primary.Y - _virtual.Y + 16);
            DrawLabel(g, "드래그: 영역 선택   ·   클릭: 창/컨트롤 선택   ·   Esc / 우클릭: 취소", hintPos);
        }

        private void DrawLabel(Graphics g, string text, Point pos)
        {
            var size = TextRenderer.MeasureText(g, text, _font);
            var bg = new Rectangle(pos.X, pos.Y, size.Width + 12, size.Height + 6);
            using (var brush = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                g.FillRectangle(brush, bg);
            TextRenderer.DrawText(g, text, _font, new Point(pos.X + 6, pos.Y + 3), Color.White);
        }

        private Rectangle LabelArea(Rectangle rect)
        {
            // 크기 표시 라벨까지 포함한 다시 그릴 영역
            var r = Rectangle.Union(rect, new Rectangle(rect.Left, rect.Top - 30, 220, 34));
            r.Inflate(4, 4);
            return r;
        }

        private void InvalidateSelection(Rectangle oldRect, Rectangle newRect)
        {
            if (oldRect == newRect) return;
            if (oldRect.Width > 0 && oldRect.Height > 0) Invalidate(LabelArea(oldRect));
            if (newRect.Width > 0 && newRect.Height > 0) Invalidate(LabelArea(newRect));
        }

        private void UpdateHover(Point clientPt)
        {
            if (clientPt == _lastHoverPt) return;
            _lastHoverPt = clientPt;

            var screenPt = new Point(clientPt.X + _virtual.X, clientPt.Y + _virtual.Y);
            var r = ScreenCapture.FindWindowRectAt(screenPt);
            var client = r.IsEmpty ? Rectangle.Empty : new Rectangle(r.X - _virtual.X, r.Y - _virtual.Y, r.Width, r.Height);

            var old = _hover;
            _hover = client;
            InvalidateSelection(old, client);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _mouseDown = true;
            _dragging = false;
            _dragStart = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mouseDown)
            {
                if (!_dragging &&
                    (Math.Abs(e.X - _dragStart.X) > 4 || Math.Abs(e.Y - _dragStart.Y) > 4))
                {
                    _dragging = true;
                    InvalidateSelection(_hover, Rectangle.Empty);
                }

                if (_dragging)
                {
                    var old = _selection;
                    _selection = Rectangle.FromLTRB(
                        Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
                        Math.Max(_dragStart.X, e.X) + 1, Math.Max(_dragStart.Y, e.Y) + 1);
                    InvalidateSelection(old, _selection);
                }
            }
            else
            {
                UpdateHover(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_mouseDown) return;

            _mouseDown = false;
            Capture = false;

            Rectangle chosen = _dragging ? _selection : _hover;
            if (chosen.Width < 16 || chosen.Height < 16)
            {
                // 너무 작은 영역은 무시하고 다시 선택하게 함
                var old = _selection;
                _dragging = false;
                _selection = Rectangle.Empty;
                InvalidateSelection(old, Rectangle.Empty);
                return;
            }

            SelectedRegion = new Rectangle(chosen.X + _virtual.X, chosen.Y + _virtual.Y, chosen.Width, chosen.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dimmed.Dispose();
                _font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
