using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>캡처 영역 바깥쪽에 표시되는 얇은 테두리 조각. 클릭이 통과하고 포커스를 가져가지 않습니다.</summary>
    internal sealed class BorderPieceForm : Form
    {
        public BorderPieceForm(Rectangle bounds, Color color)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = color;
            Bounds = bounds;
            Opacity = 0.85;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE |
                              NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED |
                              NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryExcludeFromCapture(Handle);
        }

        /// <summary>영역 바깥을 감싸는 테두리 4조각을 만듭니다.</summary>
        public static BorderPieceForm[] CreateAround(Rectangle r, int thickness, Color color)
        {
            return new[]
            {
                new BorderPieceForm(new Rectangle(r.Left - thickness, r.Top - thickness, r.Width + thickness * 2, thickness), color),
                new BorderPieceForm(new Rectangle(r.Left - thickness, r.Bottom, r.Width + thickness * 2, thickness), color),
                new BorderPieceForm(new Rectangle(r.Left - thickness, r.Top, thickness, r.Height), color),
                new BorderPieceForm(new Rectangle(r.Right, r.Top, thickness, r.Height), color),
            };
        }
    }

    /// <summary>캡처 진행 상태와 완료 버튼을 보여주는 작은 창. Esc 전역 단축키로도 완료할 수 있습니다.</summary>
    internal sealed class CaptureStatusForm : Form
    {
        private const int HotkeyId = 0x5C01;

        private readonly Label _label;
        private readonly Button _stopButton;
        private bool _hotkeyRegistered;

        public event EventHandler StopRequested;

        public CaptureStatusForm(bool manualMode)
        {
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ControlBox = false;
            Text = manualMode ? "ScrollCapture - 직접 스크롤하세요" : "ScrollCapture - 자동 스크롤 중";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(8);

            var layout = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Dock = DockStyle.Fill,
            };

            _label = new Label
            {
                AutoSize = true,
                Text = "준비 중...",
                Margin = new Padding(3, 8, 12, 3),
                MinimumSize = new Size(190, 0),
            };

            _stopButton = new Button
            {
                Text = "완료 (Esc)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 2, 8, 2),
            };
            _stopButton.Click += (s, e) => RaiseStop();

            layout.Controls.Add(_label);
            layout.Controls.Add(_stopButton);
            Controls.Add(layout);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void SetStatus(string text)
        {
            _label.Text = text;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryExcludeFromCapture(Handle);
            _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, 0, NativeMethods.VK_ESCAPE);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_hotkeyRegistered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                RaiseStop();
                return;
            }
            base.WndProc(ref m);
        }

        private void RaiseStop()
        {
            _stopButton.Enabled = false;
            _label.Text = "마무리 중...";
            var handler = StopRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>캡처 영역과 겹치지 않는 위치에 창을 배치합니다.</summary>
        public void PlaceOutside(Rectangle region)
        {
            if (!IsHandleCreated) CreateHandle();
            PerformLayout();
            var size = Size;
            var work = Screen.FromRectangle(region).WorkingArea;
            const int gap = 10;

            var candidates = new[]
            {
                new Point(region.Left + (region.Width - size.Width) / 2, region.Bottom + gap),
                new Point(region.Left + (region.Width - size.Width) / 2, region.Top - gap - size.Height),
                new Point(region.Right + gap, region.Top),
                new Point(region.Left - gap - size.Width, region.Top),
                new Point(work.Right - size.Width - gap, work.Bottom - size.Height - gap),
                new Point(work.Right - size.Width - gap, work.Top + gap),
                new Point(work.Left + gap, work.Bottom - size.Height - gap),
                new Point(work.Left + gap, work.Top + gap),
            };

            foreach (var c in candidates)
            {
                var r = new Rectangle(c, size);
                if (work.Contains(r) && !r.IntersectsWith(region))
                {
                    Location = c;
                    return;
                }
            }

            // 어쩔 수 없이 겹치는 경우: 화면 오른쪽 아래 (캡처에서는 제외됨, 스크롤 지점인 중앙은 피함)
            Location = new Point(work.Right - size.Width - gap, work.Bottom - size.Height - gap);
        }
    }
}
