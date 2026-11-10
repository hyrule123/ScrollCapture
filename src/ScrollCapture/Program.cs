using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollCapture
{
    internal static class Program
    {
        public static Icon AppIcon { get; private set; }

        [STAThread]
        private static void Main()
        {
            NativeMethods.EnsureDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { AppIcon = null; }

            Application.Run(new MainForm());
        }
    }
}
