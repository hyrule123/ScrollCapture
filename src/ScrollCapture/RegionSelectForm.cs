using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 영역 선택 오버레이.
    ///  - 드래그: 사각형 영역 선택 (모니터 경계를 넘어 선택 가능)
    ///  - 클릭: 마우스 아래의 창/컨트롤 영역 선택
    ///  - Esc / 우클릭: 취소
    ///
    /// 창 하나로 전체 가상 화면을 덮으면 다중 모니터(특히 모니터마다 배율이 다를 때)에서
    /// 창 크기가 줄어들거나 한 모니터에만 표시되는 문제가 있어, 모니터마다 오버레이 창을 하나씩 띄우고
    /// 상태(선택 영역 등)는 이 클래스가 화면 좌표로 공유합니다.
    /// </summary>
    internal sealed class RegionSelector : IDisposable
    {
        private readonly Bitmap _screen;      // 가상 화면 전체 스크린샷
        private readonly Bitmap _dimmed;
        private readonly Rectangle _virtual;
        private readonly List<OverlayForm> _forms = new List<OverlayForm>();
        private readonly TaskCompletionSource<Rectangle?> _result = new TaskCompletionSource<Rectangle?>();
        private readonly Font _font = new Font("Segoe UI", 10f, FontStyle.Regular);

        private bool _mouseDown;
        private bool _dragging;
        private Point _dragStart;          // 화면 좌표
        private Rectangle _selection;      // 화면 좌표
        private Rectangle _hover;          // 화면 좌표
        private Point _lastHoverPt = new Point(int.MinValue, int.MinValue);
        private bool _finished;

        public RegionSelector(Bitmap screenshot, Rectangle virtualBounds)
        {
            _screen = screenshot;
            _virtual = virtualBounds;
            _dimmed = CreateDimmed(screenshot);
        }

        /// <summary>오버레이를 띄우고 선택 결과(화면 좌표)를 기다립니다. 취소하면 null.</summary>
        public Task<Rectangle?> SelectAsync()
        {
            foreach (var screen in Screen.AllScreens)
                _forms.Add(new OverlayForm(this, screen.Bounds));

            foreach (var f in _forms) f.Show();

            // 마우스가 있는 모니터의 창을 활성화 (Esc 키 입력용)
            var cursor = Cursor.Position;
            var active = _forms.Find(f => f.Bounds.Contains(cursor)) ?? _forms[0];
            active.Activate();

            UpdateHover(cursor);
            return _result.Task;
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

        private Rectangle Current
        {
            get { return _dragging ? _selection : _hover; }
        }

        private void Finish(Rectangle? result)
        {
            if (_finished) return;
            _finished = true;
            foreach (var f in _forms) f.Close();
            _result.TrySetResult(result);
        }

        // ───────────── 입력 처리 (모두 화면 좌표) ─────────────

        internal void OnMouseDown(OverlayForm form, MouseButtons button, Point pt)
        {
            if (button == MouseButtons.Right)
            {
                Finish(null);
                return;
            }
            if (button != MouseButtons.Left) return;

            _mouseDown = true;
            _dragging = false;
            _dragStart = pt;
            form.Capture = true; // 다른 모니터로 드래그해도 이 창이 마우스 입력을 계속 받음
        }

        internal void OnMouseMove(Point pt)
        {
            if (_mouseDown)
            {
                if (!_dragging &&
                    (Math.Abs(pt.X - _dragStart.X) > 4 || Math.Abs(pt.Y - _dragStart.Y) > 4))
                {
                    _dragging = true;
                    InvalidateSelection(_hover, Rectangle.Empty);
                }

                if (_dragging)
                {
                    var old = _selection;
                    _selection = Rectangle.FromLTRB(
                        Math.Min(_dragStart.X, pt.X), Math.Min(_dragStart.Y, pt.Y),
                        Math.Max(_dragStart.X, pt.X) + 1, Math.Max(_dragStart.Y, pt.Y) + 1);
                    _selection = Rectangle.Intersect(_selection, _virtual);
                    InvalidateSelection(old, _selection);
                }
            }
            else
            {
                UpdateHover(pt);
            }
        }

        internal void OnMouseUp(OverlayForm form, MouseButtons button)
        {
            if (button != MouseButtons.Left || !_mouseDown) return;

            _mouseDown = false;
            form.Capture = false;

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

            Finish(chosen);
        }

        internal void OnKeyDown(Keys key)
        {
            if (key == Keys.Escape) Finish(null);
        }

        internal void OnFormClosedByUser()
        {
            // Alt+F4 등으로 오버레이가 닫히면 취소로 처리
            Finish(null);
        }

        private void UpdateHover(Point pt)
        {
            if (pt == _lastHoverPt) return;
            _lastHoverPt = pt;

            var r = ScreenCapture.FindWindowRectAt(pt);
            var old = _hover;
            _hover = r;
            InvalidateSelection(old, r);
        }

        private static Rectangle LabelArea(Rectangle rect)
        {
            // 크기 표시 라벨까지 포함한 다시 그릴 영역
            var r = Rectangle.Union(rect, new Rectangle(rect.Left, rect.Top - 30, 220, 34));
            r.Inflate(4, 4);
            return r;
        }

        private void InvalidateSelection(Rectangle oldRect, Rectangle newRect)
        {
            if (oldRect == newRect) return;
            if (oldRect.Width > 0 && oldRect.Height > 0) InvalidateScreenRect(LabelArea(oldRect));
            if (newRect.Width > 0 && newRect.Height > 0) InvalidateScreenRect(LabelArea(newRect));
        }

        private void InvalidateScreenRect(Rectangle screenRect)
        {
            foreach (var f in _forms) f.InvalidateScreenRect(screenRect);
        }

        // ───────────── 그리기 (화면 좌표 기준, 각 창이 자기 영역만 그림) ─────────────

        internal void Paint(Graphics g, Rectangle formBounds, Rectangle clip)
        {
            // clip: 다시 그릴 영역 (화면 좌표)
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.TranslateTransform(-formBounds.X, -formBounds.Y);

            DrawFromScreenshot(g, _dimmed, clip);

            var rect = Current;
            if (rect.Width > 0 && rect.Height > 0)
            {
                var visible = Rectangle.Intersect(rect, clip);
                if (visible.Width > 0 && visible.Height > 0)
                    DrawFromScreenshot(g, _screen, visible);

                using (var pen = new Pen(_dragging ? Color.FromArgb(255, 0, 120, 215) : Color.FromArgb(255, 255, 80, 40), 2f))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(pen, rect);
                }

                string sizeText = string.Format("{0} × {1}", rect.Width, rect.Height);
                DrawLabel(g, sizeText, new Point(rect.Left, rect.Top - 26 >= formBounds.Top ? rect.Top - 26 : rect.Top + 4));
            }

            // 안내 문구 (각 모니터 왼쪽 위)
            DrawLabel(g, "드래그: 영역 선택   ·   클릭: 창/컨트롤 선택   ·   Esc / 우클릭: 취소",
                new Point(formBounds.X + 16, formBounds.Y + 16));
        }

        private void DrawFromScreenshot(Graphics g, Bitmap source, Rectangle screenRect)
        {
            var src = new Rectangle(screenRect.X - _virtual.X, screenRect.Y - _virtual.Y, screenRect.Width, screenRect.Height);
            g.DrawImage(source, screenRect, src, GraphicsUnit.Pixel);
        }

        private void DrawLabel(Graphics g, string text, Point pos)
        {
            var size = TextRenderer.MeasureText(g, text, _font);
            var bg = new Rectangle(pos.X, pos.Y, size.Width + 12, size.Height + 6);
            using (var brush = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                g.FillRectangle(brush, bg);
            // TextRenderer 는 Graphics 의 변환을 따르지 않으므로 GDI+ 로 그림
            using (var textBrush = new SolidBrush(Color.White))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.DrawString(text, _font, textBrush, pos.X + 6, pos.Y + 4, format);
            }
        }

        public void Dispose()
        {
            Finish(null);
            foreach (var f in _forms) f.Dispose();
            _forms.Clear();
            _dimmed.Dispose();
            _font.Dispose();
        }

        /// <summary>모니터 하나를 덮는 오버레이 창.</summary>
        internal sealed class OverlayForm : Form
        {
            private readonly RegionSelector _owner;
            private readonly Rectangle _monitor;
            private bool _closingByOwner;

            public OverlayForm(RegionSelector owner, Rectangle monitorBounds)
            {
                _owner = owner;
                _monitor = monitorBounds;

                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                Cursor = Cursors.Cross;
                DoubleBuffered = true;
                Bounds = monitorBounds;
                Text = "ScrollCapture - 영역 선택";
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Bounds = _monitor;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                // 혹시 DPI 변경 등으로 위치/크기가 바뀌었으면 다시 맞춤
                if (Bounds != _monitor) Bounds = _monitor;
            }

            private Point ToScreen(Point client)
            {
                return new Point(client.X + Bounds.X, client.Y + Bounds.Y);
            }

            public void InvalidateScreenRect(Rectangle screenRect)
            {
                var r = Rectangle.Intersect(screenRect, Bounds);
                if (r.Width <= 0 || r.Height <= 0) return;
                r.Offset(-Bounds.X, -Bounds.Y);
                Invalidate(r);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // 깜빡임 방지: 배경은 OnPaint 에서 모두 그림
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var clip = e.ClipRectangle;
                clip.Offset(Bounds.X, Bounds.Y);
                _owner.Paint(e.Graphics, Bounds, clip);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                _owner.OnMouseDown(this, e.Button, ToScreen(e.Location));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                _owner.OnMouseMove(ToScreen(e.Location));
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _owner.OnMouseUp(this, e.Button);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                _owner.OnKeyDown(e.KeyCode);
            }

            public new void Close()
            {
                _closingByOwner = true;
                base.Close();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                base.OnFormClosed(e);
                if (!_closingByOwner) _owner.OnFormClosedByUser();
            }
        }
    }
}
