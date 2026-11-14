using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 매우 긴 이미지도 표시할 수 있는 간단한 뷰어.
    /// (PictureBox + AutoScroll 은 32767px 를 넘는 크기에서 문제가 생기므로 직접 그림)
    /// </summary>
    internal sealed class ImageViewer : Control
    {
        private readonly VScrollBar _v = new VScrollBar();
        private readonly HScrollBar _h = new HScrollBar();
        private Image _image;
        private bool _fitWidth = true;

        public ImageViewer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(64, 64, 64);

            _v.Dock = DockStyle.Right;
            _h.Dock = DockStyle.Bottom;
            _v.Scroll += (s, e) => Invalidate();
            _h.Scroll += (s, e) => Invalidate();
            _v.ValueChanged += (s, e) => Invalidate();
            _h.ValueChanged += (s, e) => Invalidate();
            Controls.Add(_v);
            Controls.Add(_h);
        }

        public Image Image
        {
            get { return _image; }
            set
            {
                _image = value;
                _v.Value = 0;
                _h.Value = 0;
                UpdateScrollBars();
                Invalidate();
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image == null) return;
            var bar = (ModifierKeys & Keys.Shift) != 0 ? (ScrollBar)_h : _v;
            if (!bar.Visible) return;
            int step = Math.Max(1, bar.LargeChange / 4);
            bar.Value = Clamp(bar.Value - Math.Sign(e.Delta) * step * Math.Max(1, Math.Abs(e.Delta) / 120), 0, MaxValue(bar));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image == null) return;

            float s = ScaleFactor;
            var vp = Viewport;
            int contentW = (int)Math.Ceiling(_image.Width * s);

            int offX = _h.Visible ? _h.Value : 0;
            int offY = _v.Visible ? _v.Value : 0;
            int originX = contentW < vp.Width ? (vp.Width - contentW) / 2 : -offX;

            // 화면에 보이는 부분만 원본에서 잘라 그림
            float srcY = offY / s;
            float srcH = Math.Min(_image.Height - srcY, vp.Height / s);
            float srcX = originX < 0 ? -originX / s : 0;
            float srcW = Math.Min(_image.Width - srcX, (vp.Width - Math.Max(0, originX)) / s);
            if (srcH <= 0 || srcW <= 0) return;

            var dest = new RectangleF(Math.Max(0, originX), 0, srcW * s, srcH * s);
            g.SetClip(vp);
            g.InterpolationMode = s < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, dest, new RectangleF(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);

            if (_v.Visible && _h.Visible)
            {
                using (var b = new SolidBrush(SystemColors.Control))
                {
                    g.ResetClip();
                    g.FillRectangle(b, vp.Right, vp.Bottom, _v.Width, _h.Height);
                }
            }
        }
    }
}
