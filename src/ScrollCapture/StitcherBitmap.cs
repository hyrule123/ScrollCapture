using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScrollCapture
{
    // Stitcher 의 System.Drawing 연동 부분 (핵심 알고리즘은 Stitcher.cs)
    internal sealed partial class Stitcher
    {
        /// <summary>새 프레임을 추가합니다. 전달된 Bitmap 은 이 메서드 안에서 Dispose 됩니다.</summary>
        public AddResult Add(Bitmap bitmap)
        {
            int w, h;
            int[] pixels;
            using (bitmap) pixels = ReadPixels(bitmap, out w, out h);
            return Add(pixels, w, h);
        }

        /// <summary>최종 이미지를 생성합니다.</summary>
        public Bitmap Finish()
        {
            int height;
            int[] pixels = FinishPixels(out height);
            int w = Width;
            int h = Math.Max(1, height);

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            if (height == 0) return bmp;

            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(pixels, y * w, IntPtr.Add(data.Scan0, y * data.Stride), w);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        private static int[] ReadPixels(Bitmap bmp, out int w, out int h)
        {
            w = bmp.Width;
            h = bmp.Height;
            var pixels = new int[w * h];
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                if (data.Stride == w * 4)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * w, w);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return pixels;
        }
    }
}
