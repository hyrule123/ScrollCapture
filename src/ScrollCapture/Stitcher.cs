using System;
using System.Collections.Generic;

namespace ScrollCapture
{
    internal enum AddResult
    {
        First,
        Appended,
        Identical,
        NoMatch,
    }

    /// <summary>
    /// 연속으로 캡처된 프레임들의 겹치는 부분을 찾아 하나의 긴 이미지로 이어 붙입니다.
    ///
    /// 알고리즘 요약
    ///  1. 각 행(row)마다 해시를 계산합니다. (오른쪽 스크롤바 영역은 해시에서 제외)
    ///  2. 이전 프레임(P)과 새 프레임(B)에서 같은 위치가 그대로인 위/아래 행은
    ///     고정 헤더/푸터로 간주하여 비교에서 제외합니다.
    ///  3. 가운데 영역에서 B[r] == P[r + dy] 가 가장 잘 성립하는 이동량 dy 를 찾습니다.
    ///     (단색 행은 어디에나 맞기 때문에 점수 계산에서 제외)
    ///  4. P 의 아직 반영되지 않은 행들을 결과에 붙이고, "어디까지 반영했는지"를 B 좌표로 옮깁니다.
    ///     마지막 프레임은 Finish() 에서 푸터까지 포함해 붙입니다.
    /// </summary>
    internal sealed partial class Stitcher
    {
        private sealed class Frame
        {
            public int Width;
            public int Height;
            public int[] Pixels;
            public ulong[] RowHash;
            public bool[] RowUniform;
        }

        private const double MatchThreshold = 0.85;

        private readonly List<int[]> _strips = new List<int[]>();
        private readonly int _width;
        private readonly int _height;
        private readonly int _hashLeft;
        private readonly int _hashRight;

        private Frame _last;
        private int _lastCommitted;   // _last 프레임 좌표 기준, 이 행 이전까지는 결과에 반영됨
        private int _committedHeight;
        private int _lastDy = -1;

        public Stitcher(int width, int height)
        {
            _width = width;
            _height = height;
            _hashLeft = 0;
            // 스크롤바 위치가 바뀌면 모든 행이 달라지므로 오른쪽 가장자리는 비교에서 제외
            int excludeRight = width >= 200 ? Math.Min(32, width / 10) : 0;
            _hashRight = width - excludeRight;
        }

        public int FrameCount { get; private set; }

        /// <summary>지금까지 이어 붙인 결과의 (예상) 높이.</summary>
        public int CurrentHeight
        {
            get { return _committedHeight + (_last != null ? _last.Height - _lastCommitted : 0); }
        }

        /// <summary>
        /// 새 프레임(0xAARRGGBB 픽셀, 행 우선)을 추가합니다. 배열은 Stitcher 가 소유하게 됩니다.
        /// </summary>
        public AddResult Add(int[] pixels, int width, int height)
        {
            Frame b = BuildFrame(pixels, width, height);

            if (b.Width != _width || b.Height != _height)
                throw new ArgumentException("Frame size mismatch.");

            if (_last == null)
            {
                _last = b;
                _lastCommitted = 0;
                FrameCount = 1;
                return AddResult.First;
            }

            Frame p = _last;
            int h = _height;

            if (AllRowsEqual(p, b)) return AddResult.Identical;

            // 고정 헤더(T) / 푸터(F) 탐지
            int t = 0;
            while (t < h && p.RowHash[t] == b.RowHash[t]) t++;
            while (t > 0 && b.RowUniform[t - 1]) t--;          // 헤더 끝의 여백은 본문으로 취급

            int f = 0;
            while (f < h - t && p.RowHash[h - 1 - f] == b.RowHash[h - 1 - f]) f++;
            while (f > 0 && b.RowUniform[h - f]) f--;

            int cap = h * 2 / 5;
            if (t > cap) t = cap;
            if (f > cap) f = cap;

            int mStart = t;
            int mEnd = h - f;
            int dy = FindShift(p, b, mStart, mEnd);

            AddResult result;
            if (dy > 0)
            {
                int committedEndInP = Math.Max(_lastCommitted, mEnd);
                Commit(p, _lastCommitted, mEnd);
                _lastCommitted = Math.Max(0, committedEndInP - dy);
                _lastDy = dy;
                result = AddResult.Appended;
            }
            else
            {
                // 겹치는 부분을 찾지 못함: 이전 프레임을 모두 반영하고 새 프레임은 헤더 이후부터 이어 붙임
                Commit(p, _lastCommitted, mEnd);
                _lastCommitted = mStart;
                result = AddResult.NoMatch;
            }

            _last = b;
            FrameCount++;
            return result;
        }

        /// <summary>최종 이미지의 픽셀 배열을 반환합니다. (너비는 생성자의 width)</summary>
        public int[] FinishPixels(out int height)
        {
            if (_last != null)
            {
                Commit(_last, _lastCommitted, _last.Height);
                _last = null;
            }

            height = _committedHeight;
            var result = new int[(long)_width * height];
            int offset = 0;
            foreach (var strip in _strips)
            {
                Array.Copy(strip, 0, result, offset, strip.Length);
                offset += strip.Length;
            }

            _strips.Clear();
            _committedHeight = 0;
            return result;
        }

        public int Width { get { return _width; } }

        private void Commit(Frame frame, int fromRow, int toRow)
        {
            if (fromRow < 0) fromRow = 0;
            if (toRow > frame.Height) toRow = frame.Height;
            if (toRow <= fromRow) return;

            int count = (toRow - fromRow) * frame.Width;
            var strip = new int[count];
            Array.Copy(frame.Pixels, fromRow * frame.Width, strip, 0, count);
            _strips.Add(strip);
            _committedHeight += toRow - fromRow;
        }

        private static bool AllRowsEqual(Frame a, Frame b)
        {
            for (int i = 0; i < a.Height; i++)
                if (a.RowHash[i] != b.RowHash[i]) return false;
            return true;
        }

        /// <summary>
        /// B[r] == P[r + dy] 를 가장 잘 만족하는 dy (&gt; 0) 를 찾습니다. 실패 시 -1.
        /// </summary>
        private int FindShift(Frame p, Frame b, int mStart, int mEnd)
        {
            int mLen = mEnd - mStart;
            int minOverlap = Math.Max(8, mLen / 20);
            if (mLen <= minOverlap) return -1;

            int bestDy = -1;
            double bestScore = -1;
            int bestMatched = 0;

            for (int dy = 1; dy <= mLen - minOverlap; dy++)
            {
                int matched = 0, considered = 0;
                int end = mEnd - dy;
                for (int r = mStart; r < end; r++)
                {
                    bool same = b.RowHash[r] == p.RowHash[r + dy];
                    // 같은 색의 단색 행끼리는 어느 위치에서나 일치하므로 판단 근거에서 제외
                    if (same && b.RowUniform[r] && p.RowUniform[r + dy]) continue;
                    considered++;
                    if (same) matched++;
                }

                if (considered < 4) continue;
                double score = (double)matched / considered;
                if (score < MatchThreshold) continue;

                bool better;
                if (bestDy < 0 || score > bestScore + 0.02)
                {
                    better = true;
                }
                else if (score < bestScore - 0.02)
                {
                    better = false;
                }
                else if (_lastDy > 0)
                {
                    // 점수가 비슷하면 직전 스크롤량에 가까운 쪽을 선택 (반복 패턴 대비)
                    better = Math.Abs(dy - _lastDy) < Math.Abs(bestDy - _lastDy);
                }
                else
                {
                    better = matched > bestMatched;
                }

                if (better)
                {
                    bestDy = dy;
                    bestScore = score;
                    bestMatched = matched;
                }
            }

            // 겹치는 부분이 전부 여백이라 판단할 근거가 없으면 직전 스크롤량을 그대로 사용
            // (예: 내용이 위로 사라지고 빈 화면만 남은 경우)
            if (bestDy < 0 && _lastDy > 0 && _lastDy <= mLen - minOverlap)
            {
                int considered = 0;
                for (int r = mStart; r < mEnd - _lastDy; r++)
                {
                    int q = r + _lastDy;
                    if (b.RowHash[r] == p.RowHash[q] && b.RowUniform[r] && p.RowUniform[q]) continue;
                    considered++;
                }
                if (considered < 4) return _lastDy;
            }

            return bestDy;
        }

        private Frame BuildFrame(int[] pixels, int w, int h)
        {
            if (pixels.Length != w * h) throw new ArgumentException("Pixel buffer size mismatch.");

            // 32bppRgb 의 알파 바이트는 정의되지 않으므로 고정값으로 정리
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] |= unchecked((int)0xFF000000);

            var hashes = new ulong[h];
            var uniform = new bool[h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                ulong hash = 14695981039346656037UL;
                int first = pixels[row + _hashLeft];
                bool uni = true;
                for (int x = _hashLeft; x < _hashRight; x++)
                {
                    int v = pixels[row + x];
                    if (v != first) uni = false;
                    hash ^= (uint)v;
                    hash *= 1099511628211UL;
                }
                hashes[y] = hash;
                uniform[y] = uni;
            }

            return new Frame { Width = w, Height = h, Pixels = pixels, RowHash = hashes, RowUniform = uniform };
        }
    }
}
