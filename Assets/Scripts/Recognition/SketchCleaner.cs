using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Biến bức vẽ sáp màu thành DẠNG HOÀN THIỆN: hình đặc, mỗi vùng một màu phẳng,
    /// không còn vân giấy / khe giữa các nét sáp / chấm lạc. Giữ nguyên hình và màu
    /// người chơi đã vẽ (không thay bằng hình mẫu).
    ///
    ///   1. Bóng = các ô có màu (bỏ mảnh lạc) → đóng khe → lấp lỗ (như SketchScorer).
    ///   2. Ô trong bóng chưa có màu (khe, lỗ) lấy màu của ô có màu GẦN NHẤT
    ///      (loang đều từ mọi ô có màu, 4 hướng).
    ///   3. Lọc đa số trong cửa sổ (2r+1)² để mịn các vệt lẫn màu và mép răng cưa.
    /// Thuần C#.
    /// </summary>
    public static class SketchCleaner
    {
        public const int DefaultSmoothRadius = 2;

        public static ColorSketch Clean(ColorSketch s, int smoothRadius = DefaultSmoothRadius)
        {
            int n = s.Size;
            bool[] kept = SketchScorer.KeptPaint(s);
            bool[] sil = SketchScorer.Silhouette(kept, n);

            // 2. Loang màu từ các ô có màu vào khe / lỗ trong bóng
            var col = new int[n * n];
            var queue = new Queue<int>();
            for (int i = 0; i < col.Length; i++)
            {
                col[i] = ColorSketch.Empty;
                if (sil[i] && kept[i]) { col[i] = s.Cells[i]; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                int y = p / n, x = p - y * n;
                if (x > 0) Spread(p, p - 1);
                if (x < n - 1) Spread(p, p + 1);
                if (y > 0) Spread(p, p - n);
                if (y < n - 1) Spread(p, p + n);
            }

            // 3. Lọc đa số
            int maxId = 0;
            foreach (int c in col) if (c > maxId) maxId = c;
            var counts = new int[maxId + 3];   // chỉ số = màu + 2 (NoColor → 0)
            var result = new ColorSketch(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!sil[i] || col[i] == ColorSketch.Empty) continue;
                    System.Array.Clear(counts, 0, counts.Length);
                    for (int dy = -smoothRadius; dy <= smoothRadius; dy++)
                        for (int dx = -smoothRadius; dx <= smoothRadius; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                            int c = col[ny * n + nx];
                            if (c != ColorSketch.Empty) counts[c + 2]++;
                        }
                    int best = col[i] + 2;
                    for (int k = 0; k < counts.Length; k++) if (counts[k] > counts[best]) best = k;
                    result.Cells[i] = best - 2;
                }
            return result;

            void Spread(int from, int to)
            {
                if (!sil[to] || col[to] != ColorSketch.Empty) return;
                col[to] = col[from];
                queue.Enqueue(to);
            }
        }
    }
}
