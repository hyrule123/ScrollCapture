using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ScrollCapture
{
    internal sealed class CaptureOptions
    {
        public bool ManualScroll;
        public int WheelNotches = 3;
        public int DelayMs = 300;
        public int MaxHeight = 30000;
    }

    internal sealed class CaptureResult
    {
        public Bitmap Image;
        public int Frames;
        public int NoMatchCount;
        public bool HitMaxHeight;
    }

    /// <summary>선택된 영역을 반복 캡처하면서 스크롤하고, 프레임을 이어 붙입니다.</summary>
    internal sealed class CaptureSession
    {
        private readonly Rectangle _region;
        private readonly CaptureOptions _options;
        private volatile bool _stopRequested;

        public CaptureSession(Rectangle region, CaptureOptions options)
        {
            _region = region;
            _options = options;
        }

        public async Task<CaptureResult> RunAsync()
        {
            var stitcher = new Stitcher(_region.Width, _region.Height);
            var borders = BorderPieceForm.CreateAround(_region, 3, Color.FromArgb(255, 255, 60, 40));
            var status = new CaptureStatusForm(_options.ManualScroll);
            status.StopRequested += (s, e) => _stopRequested = true;

            NativeMethods.POINT savedCursor;
            bool cursorSaved = NativeMethods.GetCursorPos(out savedCursor);

            int noMatch = 0;
            bool hitMax = false;

            try
            {
                foreach (var b in borders) b.Show();
                status.PlaceOutside(_region);
                status.Show();
                status.SetStatus(_options.ManualScroll ? "스크롤하면 자동으로 이어 붙입니다" : "캡처 시작...");

                await Task.Delay(120); // 오버레이가 사라지고 화면이 갱신될 시간

                var region = _region;
                await Task.Run(() => stitcher.Add(ScreenCapture.Capture(region)));

                var center = new Point(_region.Left + _region.Width / 2, _region.Top + _region.Height / 2);
                int identicalStreak = 0;

                while (!_stopRequested)
                {
                    if (stitcher.CurrentHeight >= _options.MaxHeight)
                    {
                        hitMax = true;
                        break;
                    }

                    if (!_options.ManualScroll)
                    {
                        NativeMethods.SetCursorPos(center.X, center.Y);
                        NativeMethods.SendMouseWheel(-NativeMethods.WHEEL_DELTA * _options.WheelNotches);
                    }

                    await Task.Delay(_options.DelayMs);
                    if (_stopRequested) break;

                    var result = await Task.Run(() => stitcher.Add(ScreenCapture.Capture(region)));

                    if (result == AddResult.Identical)
                    {
                        identicalStreak++;
                        // 자동 모드에서는 더 이상 화면이 바뀌지 않으면 끝까지 스크롤된 것으로 판단
                        if (!_options.ManualScroll && identicalStreak >= 2) break;
                    }
                    else
                    {
                        identicalStreak = 0;
                        if (result == AddResult.NoMatch) noMatch++;
                    }

                    status.SetStatus(string.Format("프레임 {0}개 · 높이 {1:N0}px{2}",
                        stitcher.FrameCount, stitcher.CurrentHeight,
                        noMatch > 0 ? string.Format(" · 불일치 {0}", noMatch) : ""));
                }

                var image = await Task.Run(() => stitcher.Finish());
                return new CaptureResult
                {
                    Image = image,
                    Frames = stitcher.FrameCount,
                    NoMatchCount = noMatch,
                    HitMaxHeight = hitMax,
                };
            }
            finally
            {
                status.Close();
                status.Dispose();
                foreach (var b in borders)
                {
                    b.Close();
                    b.Dispose();
                }
                if (cursorSaved && !_options.ManualScroll)
                    NativeMethods.SetCursorPos(savedCursor.X, savedCursor.Y);
            }
        }
    }
}
