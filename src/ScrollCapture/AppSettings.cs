using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ScrollCapture
{
    /// <summary>
    /// 실행 파일과 같은 폴더의 ScrollCapture.ini 에 저장되는 설정.
    /// 파일이 없거나 값이 잘못되어 있으면 기본값을 사용합니다.
    /// </summary>
    internal sealed class AppSettings
    {
        public static AppSettings Current { get; private set; } = new AppSettings();

        public static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ScrollCapture.ini"); }
        }

        // [Capture]
        public bool ManualScroll;
        public int WheelNotches = 3;
        public int AutoDelayMs = 300;
        public int ManualIntervalMs = 100;
        public int MaxHeight = 30000;

        // [MainWindow]
        public Point? MainLocation;

        // [Save]
        public string LastDirectory = "";
        public int SaveFormatIndex = 1; // 1=PNG, 2=JPEG, 3=BMP (SaveFileDialog.FilterIndex)
        public int JpegQuality = 92;

        /// <summary>마지막 저장 실패 메시지 (성공 시 null).</summary>
        public string LastError { get; private set; }

        public static void Load()
        {
            var s = new AppSettings();
            try
            {
                if (File.Exists(FilePath))
                    s.Apply(Parse(File.ReadAllLines(FilePath, Encoding.UTF8)));
            }
            catch (Exception)
            {
                // 읽을 수 없으면 기본값으로 시작
            }
            Current = s;
        }

        public bool Save()
        {
            try
            {
                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialize(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                // Program Files 처럼 쓰기 권한이 없는 폴더 등
                LastError = ex.Message;
                try { File.Delete(FilePath + ".tmp"); } catch { }
                return false;
            }
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; ScrollCapture 설정 파일 (프로그램 종료 시 자동 저장)");
            sb.AppendLine();
            sb.AppendLine("[Capture]");
            sb.AppendLine("; Auto = 자동 스크롤, Manual = 직접 스크롤");
            sb.AppendLine("Mode=" + (ManualScroll ? "Manual" : "Auto"));
            sb.AppendLine("WheelNotches=" + I(WheelNotches));
            sb.AppendLine("AutoDelayMs=" + I(AutoDelayMs));
            sb.AppendLine("ManualIntervalMs=" + I(ManualIntervalMs));
            sb.AppendLine("MaxHeight=" + I(MaxHeight));
            sb.AppendLine();
            sb.AppendLine("[MainWindow]");
            sb.AppendLine("X=" + (MainLocation.HasValue ? I(MainLocation.Value.X) : ""));
            sb.AppendLine("Y=" + (MainLocation.HasValue ? I(MainLocation.Value.Y) : ""));
            sb.AppendLine();
            sb.AppendLine("[Save]");
            sb.AppendLine("LastDirectory=" + (LastDirectory ?? ""));
            sb.AppendLine("; PNG, JPEG, BMP");
            sb.AppendLine("Format=" + (SaveFormatIndex == 2 ? "JPEG" : SaveFormatIndex == 3 ? "BMP" : "PNG"));
            sb.AppendLine("JpegQuality=" + I(JpegQuality));
            return sb.ToString();
        }

        private void Apply(Dictionary<string, string> v)
        {
            string s;
            if (v.TryGetValue("capture.mode", out s))
                ManualScroll = s.Trim().Equals("Manual", StringComparison.OrdinalIgnoreCase);
            WheelNotches = GetInt(v, "capture.wheelnotches", 1, 30, WheelNotches);
            AutoDelayMs = GetInt(v, "capture.autodelayms", 30, 5000, AutoDelayMs);
            ManualIntervalMs = GetInt(v, "capture.manualintervalms", 30, 5000, ManualIntervalMs);
            MaxHeight = GetInt(v, "capture.maxheight", 500, 200000, MaxHeight);

            int x, y;
            if (TryInt(v, "mainwindow.x", out x) && TryInt(v, "mainwindow.y", out y))
                MainLocation = new Point(x, y);

            if (v.TryGetValue("save.lastdirectory", out s)) LastDirectory = s.Trim();
            if (v.TryGetValue("save.format", out s))
            {
                s = s.Trim().ToUpperInvariant();
                SaveFormatIndex = (s == "JPEG" || s == "JPG") ? 2 : s == "BMP" ? 3 : 1;
            }
            JpegQuality = GetInt(v, "save.jpegquality", 1, 100, JpegQuality);
        }

        /// <summary>"섹션.키"(소문자) → 값 사전으로 읽습니다.</summary>
        private static Dictionary<string, string> Parse(string[] lines)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                result[section + "." + key] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        private static string I(int value) { return value.ToString(CultureInfo.InvariantCulture); }

        private static bool TryInt(Dictionary<string, string> v, string key, out int value)
        {
            string s;
            value = 0;
            return v.TryGetValue(key, out s) &&
                   int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static int GetInt(Dictionary<string, string> v, string key, int min, int max, int fallback)
        {
            int value;
            if (!TryInt(v, key, out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
