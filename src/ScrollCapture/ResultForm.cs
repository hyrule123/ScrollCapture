using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ScrollCapture
{
    internal sealed class ResultForm : Form
    {
        private readonly Bitmap _image;
        private readonly ImageViewer _viewer;
        private readonly CheckBox _fit;
        private readonly Label _info;
        private readonly Button _resetCrop;
        private readonly Label _zoomLabel;
        private bool _syncingFit;
        private readonly string _infoSuffix;
        private readonly Color _infoColor;


        public ResultForm(CaptureResult result)
        {
            _image = result.Image;

            Text = "ScrollCapture - 결과";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Icon = Program.AppIcon;

            var settings = AppSettings.Current;
            var wa = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(480, 320);
            if (settings.ResultSize.HasValue)
            {
                // 저장된 크기 사용 (화면보다 크면 화면에 맞춤)
                var saved = settings.ResultSize.Value;
                Size = new Size(Math.Min(saved.Width, wa.Width), Math.Min(saved.Height, wa.Height));
            }
            else
            {
                Size = new Size(Math.Min(wa.Width * 2 / 3, Math.Max(640, _image.Width + 60)), wa.Height * 5 / 6);
            }
            if (settings.ResultMaximized) WindowState = FormWindowState.Maximized;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(6, 6, 6, 4),
            };

            var save = MakeButton("저장 (Ctrl+S)", (s, e) => Save());
            var copy = MakeButton("복사 (Ctrl+C)", (s, e) => CopyToClipboard());
            var close = MakeButton("닫기", (s, e) => Close());
            _resetCrop = MakeButton("크롭 해제", (s, e) => _viewer.ResetCrop());
            _resetCrop.Visible = false;

            _fit = new CheckBox
            {
                Text = "창 너비에 맞춤",
                Checked = settings.ResultFitWidth,
                AutoSize = true,
                Margin = new Padding(12, 8, 3, 3),
            };
            _fit.CheckedChanged += (s, e) =>
            {
                if (_syncingFit) return;
                _viewer.FitWidth = _fit.Checked;
                AppSettings.Current.ResultFitWidth = _fit.Checked; // 사용자가 직접 바꾼 경우만 기억
            };

            _zoomLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3),
                ForeColor = SystemColors.GrayText,
            };

            string suffix = string.Format(" · 프레임 {0}개", result.Frames);
            if (result.NoMatchCount > 0)
                suffix += string.Format(" · ⚠ 겹침 탐지 실패 {0}회 (스크롤 양을 줄여 보세요)", result.NoMatchCount);
            if (result.HitMaxHeight)
                suffix += " · 최대 높이 도달";
            _infoSuffix = suffix;
            _infoColor = result.NoMatchCount > 0 ? Color.DarkRed : SystemColors.ControlText;

            _info = new Label
            {
                AutoSize = true,
                Margin = new Padding(12, 8, 3, 3),
            };

            bar.Controls.AddRange(new Control[] { save, copy, close, _fit, _zoomLabel, _resetCrop, _info });

            _viewer = new ImageViewer { Dock = DockStyle.Fill, Image = _image, FitWidth = settings.ResultFitWidth };
            _viewer.CropChanged += (s, e) => UpdateCropInfo();
            _viewer.ZoomChanged += (s, e) => UpdateZoomUi();
            UpdateCropInfo();
            UpdateZoomUi();

            Controls.Add(_viewer);
            Controls.Add(bar);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 2, 6, 2),
            };
            b.Click += onClick;
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _viewer.Focus();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RememberWindowState();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // 최대화/복원 버튼은 ResizeEnd 를 발생시키지 않으므로 여기서도 기록
            if (IsHandleCreated && Visible) RememberWindowState();
        }

        private void RememberWindowState()
        {
            if (WindowState == FormWindowState.Minimized) return;
            var s = AppSettings.Current;
            s.ResultMaximized = WindowState == FormWindowState.Maximized;
            s.ResultSize = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)) { _viewer.ZoomTo(1f); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) { _viewer.ZoomStep(1); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) { _viewer.ZoomStep(-1); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { Save(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { CopyToClipboard(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        private void UpdateZoomUi()
        {
            // Ctrl+휠로 배율을 바꾸면 "창 너비에 맞춤" 체크를 해제 (ini 에는 반영하지 않음)
            if (_fit.Checked != _viewer.FitWidth)
            {
                _syncingFit = true;
                _fit.Checked = _viewer.FitWidth;
                _syncingFit = false;
            }
            _zoomLabel.Text = string.Format("{0:0}%", _viewer.Zoom * 100f);
        }

        private void UpdateCropInfo()
        {
            var c = _viewer.CropRect;
            bool cropped = _viewer.IsCropped;
            _resetCrop.Visible = cropped;
            _info.Text = cropped
                ? string.Format("{0} × {1:N0} px (원본 {2} × {3:N0}에서 크롭)", c.Width, c.Height, _image.Width, _image.Height) + _infoSuffix
                : string.Format("{0} × {1:N0} px", _image.Width, _image.Height) + _infoSuffix;
            _info.ForeColor = _infoColor;
        }

        /// <summary>
        /// 내보낼 이미지. 크롭했으면 잘라낸 새 이미지(호출한 쪽에서 Dispose), 아니면 원본.
        /// </summary>
        private Bitmap CreateOutputImage(out bool mustDispose)
        {
            if (!_viewer.IsCropped)
            {
                mustDispose = false;
                return _image;
            }
            mustDispose = true;
            return _image.Clone(_viewer.CropRect, _image.PixelFormat);
        }

        private void Save()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "캡처 이미지 저장";
                dlg.Filter = "PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg|BMP 이미지 (*.bmp)|*.bmp";
                var settings = AppSettings.Current;
                int format = settings.SaveFormatIndex >= 1 && settings.SaveFormatIndex <= 3 ? settings.SaveFormatIndex : 1;
                string defaultExt = format == 2 ? ".jpg" : format == 3 ? ".bmp" : ".png";
                dlg.FilterIndex = format;
                dlg.FileName = "ScrollCapture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + defaultExt;
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                string lastDir = settings.LastDirectory;
                if (!string.IsNullOrEmpty(lastDir) && Directory.Exists(lastDir))
                    dlg.InitialDirectory = lastDir;
                else
                    dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                bool mustDispose = false;
                Bitmap output = null;
                try
                {
                    output = CreateOutputImage(out mustDispose);
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        if (output.Height > 65500 || output.Width > 65500)
                        {
                            MessageBox.Show(this, "JPEG 은 65,500px 보다 큰 이미지를 저장할 수 없습니다. PNG 로 저장해 주세요.",
                                "ScrollCapture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        SaveJpeg(output, dlg.FileName, settings.JpegQuality);
                    }
                    else if (ext == ".bmp")
                    {
                        output.Save(dlg.FileName, ImageFormat.Bmp);
                    }
                    else
                    {
                        output.Save(dlg.FileName, ImageFormat.Png);
                    }
                    settings.LastDirectory = Path.GetDirectoryName(dlg.FileName);
                    settings.SaveFormatIndex = (ext == ".jpg" || ext == ".jpeg") ? 2 : ext == ".bmp" ? 3 : 1;
                    _info.Text = "저장됨: " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "저장하지 못했습니다.\n" + ex.Message, "ScrollCapture",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (mustDispose && output != null) output.Dispose();
                }
            }
        }

        private static void SaveJpeg(Bitmap image, string path, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }

            if (codec == null)
            {
                image.Save(path, ImageFormat.Jpeg);
                return;
            }

            using (var p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                image.Save(path, codec, p);
            }
        }

        private void CopyToClipboard()
        {
            try
            {
                bool mustDispose;
                var output = CreateOutputImage(out mustDispose);
                try { Clipboard.SetImage(output); }
                finally { if (mustDispose) output.Dispose(); }
                _info.Text = "클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "클립보드에 복사하지 못했습니다.\n" + ex.Message, "ScrollCapture",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _viewer.Image = null;
                _image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
