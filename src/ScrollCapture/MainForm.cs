using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScrollCapture
{
    internal sealed class MainForm : Form
    {
        private readonly Button _startButton;
        private readonly RadioButton _autoMode;
        private readonly RadioButton _manualMode;
        private readonly NumericUpDown _notches;
        private readonly NumericUpDown _delay;
        private readonly NumericUpDown _maxHeight;
        private readonly Label _delayLabel;
        private readonly Label _notchLabel;
        private bool _busy;
        private ResultForm _resultForm; // 결과 창은 하나만 유지

        // 모드별로 따로 기억하는 대기 시간 / 캡처 간격
        private int _autoDelayMs;
        private int _manualIntervalMs;
        private bool _lastModeManual;
        private bool _saveErrorShown;

        public MainForm()
        {
            Text = "ScrollCapture";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(12),
                Dock = DockStyle.Fill,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _startButton = new Button
            {
                Text = "스크롤 캡처 시작 (Ctrl+N)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(3, 3, 3, 10),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            };
            _startButton.Click += async (s, e) => await StartCaptureAsync();
            table.Controls.Add(_startButton, 0, 0);
            table.SetColumnSpan(_startButton, 2);

            var settings = AppSettings.Current;
            _autoDelayMs = settings.AutoDelayMs;
            _manualIntervalMs = settings.ManualIntervalMs;
            _lastModeManual = settings.ManualScroll;

            _autoMode = new RadioButton { Text = "자동 스크롤", Checked = !settings.ManualScroll, AutoSize = true };
            _manualMode = new RadioButton { Text = "직접 스크롤", Checked = settings.ManualScroll, AutoSize = true };
            _autoMode.CheckedChanged += (s, e) => UpdateModeUi();
            var modePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
            };
            modePanel.Controls.Add(_autoMode);
            modePanel.Controls.Add(_manualMode);
            AddRow(table, 1, MakeLabel("모드"), modePanel);

            _notchLabel = MakeLabel("스크롤 양 (휠 칸)");
            _notches = MakeNumber(1, 30, settings.WheelNotches, 1);
            AddRow(table, 2, _notchLabel, _notches);

            _delayLabel = MakeLabel("대기 시간 (ms)");
            _delay = MakeNumber(30, 5000, settings.ManualScroll ? settings.ManualIntervalMs : settings.AutoDelayMs, 10);
            AddRow(table, 3, _delayLabel, _delay);

            _maxHeight = MakeNumber(500, 200000, settings.MaxHeight, 1000);
            _maxHeight.ThousandsSeparator = true;
            AddRow(table, 4, MakeLabel("최대 높이 (px)"), _maxHeight);

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 10, 3, 0),
                MaximumSize = new Size(340, 0),
                Text = "영역 선택: 드래그로 사각형 선택, 클릭하면 마우스 아래 창/컨트롤 선택.\n" +
                       "캡처 중: Esc 또는 [완료] 버튼으로 종료합니다.\n" +
                       "고정 헤더/푸터는 자동으로 한 번만 포함됩니다.",
            };
            table.Controls.Add(hint, 0, 5);
            table.SetColumnSpan(hint, 2);

            Controls.Add(table);
            UpdateModeUi();
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 6, 12, 6),
            };
        }

        private static NumericUpDown MakeNumber(int min, int max, int value, int step)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Increment = step,
                Width = 110,
                TextAlign = HorizontalAlignment.Right,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
        }

        private static void AddRow(TableLayoutPanel table, int row, Control label, Control field)
        {
            table.Controls.Add(label, 0, row);
            table.Controls.Add(field, 1, row);
        }

        private void UpdateModeUi()
        {
            bool manual = _manualMode.Checked;
            _notches.Enabled = !manual;
            _notchLabel.Enabled = !manual;
            _delayLabel.Text = manual ? "캡처 간격 (ms)" : "대기 시간 (ms)";

            // 모드가 바뀌면 이전 모드의 값을 기억하고, 새 모드의 값을 불러옴
            if (manual != _lastModeManual)
            {
                if (_lastModeManual) _manualIntervalMs = (int)_delay.Value;
                else _autoDelayMs = (int)_delay.Value;
                _delay.Value = manual ? _manualIntervalMs : _autoDelayMs;
                _lastModeManual = manual;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 저장된 위치가 현재 화면 안에 있으면 복원
            var saved = AppSettings.Current.MainLocation;
            if (saved.HasValue)
            {
                var bounds = new Rectangle(saved.Value, Size);
                foreach (var screen in Screen.AllScreens)
                {
                    var visible = Rectangle.Intersect(screen.WorkingArea, bounds);
                    if (visible.Width >= 80 && visible.Height >= 40)
                    {
                        Location = saved.Value;
                        break;
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) SaveSettings();
        }

        /// <summary>현재 UI 값을 설정에 반영하고 ini 파일로 저장합니다.</summary>
        private void SaveSettings()
        {
            var s = AppSettings.Current;
            s.ManualScroll = _manualMode.Checked;
            s.WheelNotches = (int)_notches.Value;
            if (_manualMode.Checked) { _manualIntervalMs = (int)_delay.Value; }
            else { _autoDelayMs = (int)_delay.Value; }
            s.AutoDelayMs = _autoDelayMs;
            s.ManualIntervalMs = _manualIntervalMs;
            s.MaxHeight = (int)_maxHeight.Value;
            if (WindowState == FormWindowState.Normal) s.MainLocation = Location;

            if (!s.Save() && !_saveErrorShown)
            {
                _saveErrorShown = true;
                MessageBox.Show(this,
                    "설정 파일을 저장하지 못했습니다.\n" + AppSettings.FilePath + "\n\n" + s.LastError +
                    "\n\n쓰기 권한이 있는 폴더(예: 바탕화면, 문서)로 프로그램을 옮기면 설정이 저장됩니다.",
                    "ScrollCapture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.N)
            {
                e.Handled = true;
                var _ = StartCaptureAsync();
            }
        }

        private async Task StartCaptureAsync()
        {
            if (_busy) return;
            _busy = true;

            var options = new CaptureOptions
            {
                ManualScroll = _manualMode.Checked,
                WheelNotches = (int)_notches.Value,
                DelayMs = (int)_delay.Value,
                MaxHeight = (int)_maxHeight.Value,
            };

            SaveSettings();

            bool hidResult = false;
            try
            {
                Hide();
                // 기존 결과 창이 캡처/선택 화면에 찍히지 않도록 잠시 숨김
                if (_resultForm != null && _resultForm.Visible)
                {
                    _resultForm.Hide();
                    hidResult = true;
                }
                await Task.Delay(200); // 창이 화면에서 사라질 때까지 대기

                Rectangle region;
                var virtualBounds = SystemInformation.VirtualScreen;
                using (var shot = ScreenCapture.Capture(virtualBounds))
                using (var selector = new RegionSelectForm(shot, virtualBounds))
                {
                    if (selector.ShowDialog() != DialogResult.OK) return;
                    region = selector.SelectedRegion;
                }

                await Task.Delay(150); // 선택 오버레이가 사라질 때까지 대기

                var session = new CaptureSession(region, options);
                var result = await session.RunAsync();

                // 새 결과로 교체: 이전 결과 창은 닫음
                CloseResultForm();
                hidResult = false;

                Show();
                ShowResult(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show("캡처 중 오류가 발생했습니다.\n" + ex.Message, "ScrollCapture",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Show();
                _busy = false;

                // 캡처를 취소했으면 숨겨 두었던 이전 결과 창을 되돌림
                if (hidResult && _resultForm != null && !_resultForm.IsDisposed)
                {
                    _resultForm.Show();
                    _resultForm.Activate();
                }
            }
        }

        private void ShowResult(CaptureResult result)
        {
            var form = new ResultForm(result);
            form.FormClosed += (s, e) =>
            {
                bool wasCurrent = ReferenceEquals(_resultForm, form);
                if (wasCurrent) _resultForm = null;
                form.Dispose();
                // 사용자가 결과 창을 닫으면 메인 창으로 포커스를 돌려줌
                if (wasCurrent) FocusMainWindow();
            };
            _resultForm = form;
            form.Show();
            form.Activate();
        }

        private void CloseResultForm()
        {
            var old = _resultForm;
            _resultForm = null; // 교체로 닫는 경우에는 메인 창 포커스 이동 안 함
            if (old != null && !old.IsDisposed) old.Close();
        }

        private void FocusMainWindow()
        {
            if (_busy || IsDisposed) return;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }
    }
}
