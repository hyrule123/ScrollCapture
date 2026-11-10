using System;
using System.Collections.Generic;
using ScrollCapture;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("plain document, fixed step", () => Scenario(seed: 1, width: 300, view: 200, header: 0, footer: 0, docHeight: 2000, steps: Steps(60), scrollbar: false));
        Run("sticky header/footer + moving scrollbar", () => Scenario(seed: 2, width: 320, view: 240, header: 30, footer: 20, docHeight: 3000, steps: Steps(90), scrollbar: true));
        Run("large blank gaps", () => Scenario(seed: 3, width: 400, view: 300, header: 0, footer: 0, docHeight: 2500, steps: Steps(120), scrollbar: false, blankGaps: true));
        Run("variable steps with repeats (manual mode)", () => Scenario(seed: 4, width: 300, view: 250, header: 40, footer: 0, docHeight: 1800, steps: new[] { 0, 37, 0, 100, 5, 0, 150, 73, 200, 1, 0 }, scrollbar: true));
        Run("single frame", () => Scenario(seed: 5, width: 200, view: 150, header: 0, footer: 0, docHeight: 150, steps: Steps(50), scrollbar: false));

        Console.WriteLine(_failures == 0 ? "ALL PASSED" : _failures + " FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static int[] Steps(int step)
    {
        var list = new List<int>();
        for (int i = 0; i < 200; i++) list.Add(step);
        return list.ToArray();
    }

    private static void Run(string name, Func<string> test)
    {
        string err;
        try { err = test(); }
        catch (Exception ex) { err = ex.ToString(); }
        Console.WriteLine((err == null ? "[PASS] " : "[FAIL] ") + name + (err == null ? "" : ": " + err));
        if (err != null) _failures++;
    }

    private static int[] MakeDocument(Random rnd, int w, int h, bool blankGaps)
    {
        const int bg = unchecked((int)0xFFFFFFFF);
        var doc = new int[w * h];
        for (int i = 0; i < doc.Length; i++) doc[i] = bg;

        int y = 0;
        while (y < h)
        {
            if (blankGaps && y > 600 && rnd.Next(6) == 0) { y += rnd.Next(150, 280); continue; }
            int lineH = rnd.Next(8, 20);
            for (int r = y; r < Math.Min(h, y + lineH); r++)
                for (int x = 10; x < w - 40; x++)
                    if (rnd.Next(3) == 0) doc[r * w + x] = unchecked((int)0xFF000000) | rnd.Next(0x1000000);
            y += lineH + rnd.Next(3, 12);
        }
        return doc;
    }

    private static int[] Fixed(Random rnd, int w, int h, int color)
    {
        var a = new int[w * h];
        for (int i = 0; i < a.Length; i++) a[i] = (rnd.Next(4) == 0) ? (unchecked((int)0xFF000000) | rnd.Next(0x1000000)) : color;
        return a;
    }

    private static string Scenario(int seed, int width, int view, int header, int footer, int docHeight, int[] steps, bool scrollbar, bool blankGaps = false)
    {
        var rnd = new Random(seed);
        int body = view - header - footer;
        var doc = MakeDocument(rnd, width, docHeight, blankGaps);
        var head = Fixed(rnd, width, header, unchecked((int)0xFF3355AA));
        var foot = Fixed(rnd, width, footer, unchecked((int)0xFF222222));
        int maxOffset = Math.Max(0, docHeight - body);

        var st = new Stitcher(width, view);
        int offset = 0;
        st.Add(Render(doc, head, foot, width, view, header, footer, docHeight, offset, scrollbar), width, view);

        int identical = 0;
        foreach (int s in steps)
        {
            offset = Math.Min(maxOffset, offset + s);
            var r = st.Add(Render(doc, head, foot, width, view, header, footer, docHeight, offset, scrollbar), width, view);
            if (r == AddResult.NoMatch) return "NoMatch at offset " + offset + " step " + s;
            if (offset == maxOffset && r == AddResult.Identical && ++identical >= 2) break;
        }

        int height;
        var result = st.FinishPixels(out height);

        int shownDoc = Math.Min(docHeight, offset + body);
        int expectedH = header + shownDoc + footer;
        if (docHeight < body) expectedH = view;
        if (height != expectedH) return string.Format("height {0} != expected {1}", height, expectedH);

        int cmpW = scrollbar ? width - 32 : width;
        for (int y = 0; y < height; y++)
        {
            int[] srcArr; int srcRow;
            if (y < header) { srcArr = head; srcRow = y; }
            else if (y < header + shownDoc) { srcArr = doc; srcRow = y - header; }
            else { srcArr = foot; srcRow = y - header - shownDoc; }

            for (int x = 0; x < cmpW; x++)
                if ((result[y * width + x] | unchecked((int)0xFF000000)) != srcArr[srcRow * width + x])
                    return string.Format("pixel mismatch at row {0}", y);
        }
        return null;
    }

    private static int[] Render(int[] doc, int[] head, int[] foot, int w, int view, int header, int footer, int docH, int offset, bool scrollbar)
    {
        var f = new int[w * view];
        int body = view - header - footer;
        Array.Copy(head, 0, f, 0, header * w);
        for (int r = 0; r < body; r++)
        {
            int src = offset + r;
            if (src < docH) Array.Copy(doc, src * w, f, (header + r) * w, w);
            else for (int x = 0; x < w; x++) f[(header + r) * w + x] = unchecked((int)0xFFFFFFFF);
        }
        Array.Copy(foot, 0, f, (header + body) * w, footer * w);

        if (scrollbar)
        {
            // 오른쪽 12px 스크롤바, thumb 위치가 offset 에 따라 바뀜
            int thumbH = Math.Max(10, view * view / Math.Max(view, docH));
            int thumbY = docH > body ? (int)((long)offset * (view - thumbH) / (docH - body)) : 0;
            for (int y = 0; y < view; y++)
                for (int x = w - 12; x < w; x++)
                    f[y * w + x] = (y >= thumbY && y < thumbY + thumbH) ? unchecked((int)0xFF888888) : unchecked((int)0xFFEEEEEE);
        }
        return f;
    }
}
