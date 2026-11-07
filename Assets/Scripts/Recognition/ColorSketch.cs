using System;
using System.Collections.Generic;
using System.Text;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Bức vẽ thu nhỏ thành lưới vuông Size×Size, mỗi ô một màu (ColorID trong
    /// palette). Đây là đầu vào của cách chấm "ảnh màu" (SketchScorer): không
    /// cần biết nét nào là viền, nét nào là tô — chỉ cần chỗ nào có màu gì.
    ///
    /// Ô = Empty: chưa vẽ. Ô = NoColor: có nét nhưng không rõ màu (template cũ
    /// vẽ bằng nét đen, không lưu màu) — chỉ dùng cho HÌNH, không chấm màu.
    /// Thuần C#.
    /// </summary>
    public class ColorSketch
    {
        public const int Empty = -1;
        public const int NoColor = -2;
        public const int DefaultSize = 128;

        public readonly int Size;
        public readonly int[] Cells;

        public ColorSketch(int size)
        {
            Size = size;
            Cells = new int[size * size];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = Empty;
        }

        public bool HasColor
        {
            get
            {
                foreach (int c in Cells) if (c >= 0) return true;
                return false;
            }
        }

        public int PaintedCount
        {
            get
            {
                int n = 0;
                foreach (int c in Cells) if (c != Empty) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------
        // Tạo từ canvas
        // ------------------------------------------------------------------

        /// <summary>
        /// Thu nhỏ bản đồ màu canvas (paint[y*w+x] = ColorID, -1 = trống) về lưới
        /// size×size. Mỗi ô lấy màu chiếm đa số nếu ít nhất minFraction diện tích ô
        /// có màu; ít hơn thì coi là trống (vân giấy lác đác không tính).
        /// </summary>
        public static ColorSketch FromPaint(int[] paint, int w, int h, int size = DefaultSize,
                                            float minFraction = 0.2f)
        {
            var sketch = new ColorSketch(size);
            int maxId = 0;
            foreach (int c in paint) if (c > maxId) maxId = c;
            var counts = new int[maxId + 3];   // chỉ số = màu + 2 (NoColor → 0, Empty → 1)

            float scale = Math.Max(w, h) / (float)size;
            for (int cy = 0; cy < size; cy++)
            {
                int y0 = (int)(cy * scale), y1 = Math.Min(h, (int)((cy + 1) * scale));
                for (int cx = 0; cx < size; cx++)
                {
                    int x0 = (int)(cx * scale), x1 = Math.Min(w, (int)((cx + 1) * scale));
                    if (x1 <= x0 || y1 <= y0) continue;

                    Array.Clear(counts, 0, counts.Length);
                    int painted = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int c = paint[y * w + x];
                            if (c == Empty) continue;
                            counts[c + 2]++;
                            painted++;
                        }

                    if (painted < minFraction * (x1 - x0) * (y1 - y0)) continue;
                    int best = 0;
                    for (int i = 0; i < counts.Length; i++)
                        if (i != 1 && counts[i] > counts[best]) best = i;
                    sketch.Cells[cy * size + cx] = best - 2;
                }
            }
            return sketch;
        }

        /// <summary>
        /// Dựng từ danh sách điểm nét vẽ (template cũ chỉ có nét): co cả hình vào
        /// lưới (giữ tỉ lệ, chừa lề), vẽ các nét với bán kính radius ô.
        /// useColors = false → mọi ô là NoColor (chỉ dùng cho hình).
        /// </summary>
        public static ColorSketch FromStrokes(IReadOnlyList<Point> pts, int size = DefaultSize,
                                              int radius = 2, bool useColors = false)
        {
            var sketch = new ColorSketch(size);
            if (pts == null || pts.Count == 0) return sketch;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Point p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-3f);
            float usable = size - 2 * (radius + 4);
            float k = usable / span;
            float ox = (size - (maxX - minX) * k) / 2f, oy = (size - (maxY - minY) * k) / 2f;

            var mask = new bool[size * size];
            int i0 = 0;
            while (i0 < pts.Count)
            {
                int i1 = i0;
                while (i1 + 1 < pts.Count && pts[i1 + 1].StrokeID == pts[i0].StrokeID) i1++;

                Array.Clear(mask, 0, mask.Length);
                var stroke = new List<Point>(i1 - i0 + 1);
                for (int i = i0; i <= i1; i++)
                    stroke.Add(new Point(ox + (pts[i].X - minX) * k, oy + (pts[i].Y - minY) * k, 0));
                Raster.DrawPoints(mask, size, size, stroke, radius);

                int color = useColors && pts[i0].ColorID >= 0 ? pts[i0].ColorID : NoColor;
                for (int i = 0; i < mask.Length; i++) if (mask[i]) sketch.Cells[i] = color;
                i0 = i1 + 1;
            }
            return sketch;
        }

        // ------------------------------------------------------------------
        // Lưu gọn trong asset: run-length "màu:số ô" cách nhau dấu phẩy
        // ------------------------------------------------------------------

        public string ToRle()
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < Cells.Length)
            {
                int j = i;
                while (j + 1 < Cells.Length && Cells[j + 1] == Cells[i]) j++;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(Cells[i]).Append(':').Append(j - i + 1);
                i = j + 1;
            }
            return sb.ToString();
        }

        /// <summary>Đọc lại chuỗi ToRle. Chuỗi rỗng/hỏng → null.</summary>
        public static ColorSketch FromRle(int size, string rle)
        {
            if (size <= 0 || string.IsNullOrEmpty(rle)) return null;
            var sketch = new ColorSketch(size);
            int pos = 0;
            foreach (string run in rle.Split(','))
            {
                int colon = run.IndexOf(':');
                if (colon <= 0) return null;
                if (!int.TryParse(run.Substring(0, colon), out int color) ||
                    !int.TryParse(run.Substring(colon + 1), out int count)) return null;
                for (int k = 0; k < count && pos < sketch.Cells.Length; k++) sketch.Cells[pos++] = color;
            }
            return pos == sketch.Cells.Length ? sketch : null;
        }
    }
}
