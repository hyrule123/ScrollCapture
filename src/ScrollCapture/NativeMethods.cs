using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ScrollCapture
{
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOUSEINPUT이 INPUT 공용체에서 가장 큰 멤버이므로 이것만으로 크기가 맞음
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const int WHEEL_DELTA = 120;

        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;

        public const uint GW_HWNDNEXT = 2;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint CWP_SKIPINVISIBLE = 0x0001;
        public const uint CWP_SKIPTRANSPARENT = 0x0004;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        public const int WM_HOTKEY = 0x0312;
        public const int VK_ESCAPE = 0x1B;

        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
            IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        /// <summary>매니페스트가 적용되지 않은 경우를 대비한 DPI 인식 설정.</summary>
        public static void EnsureDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } // PER_MONITOR_AWARE_V2
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { if (SetProcessDpiAwareness(2) == 0) return; } // PROCESS_PER_MONITOR_DPI_AWARE
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>창을 화면 캡처에서 제외 (Windows 10 2004 이상에서만 동작).</summary>
        public static void TryExcludeFromCapture(IntPtr hWnd)
        {
            try { SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE); }
            catch (EntryPointNotFoundException) { }
        }

        public static void SendMouseWheel(int delta)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.mouseData = delta;
            inputs[0].mi.dwFlags = MOUSEEVENTF_WHEEL;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                int cloaked;
                return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException) { return false; }
        }

        public static Rectangle GetVisibleWindowBounds(IntPtr hWnd)
        {
            try
            {
                RECT r;
                if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                {
                    var rect = r.ToRectangle();
                    if (rect.Width > 0 && rect.Height > 0) return rect;
                }
            }
            catch (DllNotFoundException) { }

            RECT wr;
            return GetWindowRect(hWnd, out wr) ? wr.ToRectangle() : Rectangle.Empty;
        }
    }
}
