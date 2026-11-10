using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScrollCapture
{
    internal static class ScreenCapture
    {
        private static readonly uint CurrentPid = (uint)Process.GetCurrentProcess().Id;

        /// <summary>화면의 지정 영역(물리 픽셀 좌표)을 캡처합니다.</summary>
        public static Bitmap Capture(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdcDest = g.GetHdc();
                IntPtr hdcSrc = NativeMethods.GetDC(IntPtr.Zero);
                try
                {
                    NativeMethods.BitBlt(hdcDest, 0, 0, area.Width, area.Height,
                        hdcSrc, area.X, area.Y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcSrc);
                    g.ReleaseHdc(hdcDest);
                }
            }
            return bmp;
        }

        public static Bitmap CaptureVirtualScreen()
        {
            return Capture(SystemInformation.VirtualScreen);
        }

        /// <summary>
        /// 화면 좌표 아래에 있는 가장 안쪽의 보이는 창(자식 컨트롤 포함)의 영역을 찾습니다.
        /// 현재 프로세스의 창은 무시합니다.
        /// </summary>
        public static Rectangle FindWindowRectAt(Point screenPt)
        {
            uint myPid = CurrentPid;
            IntPtr top = IntPtr.Zero;

            for (IntPtr h = NativeMethods.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero;
                 h = NativeMethods.GetWindow(h, NativeMethods.GW_HWNDNEXT))
            {
                if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) continue;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) continue;

                int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
                if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0) continue;
                if (NativeMethods.IsCloaked(h)) continue;

                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(h, out r)) continue;
                if (!r.ToRectangle().Contains(screenPt)) continue;

                top = h;
                break;
            }

            if (top == IntPtr.Zero) return Rectangle.Empty;

            IntPtr cur = top;
            for (int depth = 0; depth < 32; depth++)
            {
                var p = new NativeMethods.POINT(screenPt.X, screenPt.Y);
                NativeMethods.ScreenToClient(cur, ref p);
                IntPtr child = NativeMethods.ChildWindowFromPointEx(cur, p,
                    NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPTRANSPARENT);
                if (child == IntPtr.Zero || child == cur) break;
                cur = child;
            }

            Rectangle rect;
            if (cur == top)
            {
                rect = NativeMethods.GetVisibleWindowBounds(top);
            }
            else
            {
                NativeMethods.RECT r;
                rect = NativeMethods.GetWindowRect(cur, out r) ? r.ToRectangle() : Rectangle.Empty;
            }

            return Rectangle.Intersect(rect, SystemInformation.VirtualScreen);
        }
    }
}
