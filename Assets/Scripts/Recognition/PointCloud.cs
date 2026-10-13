using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Một nét vẽ đã được chuẩn hóa, sẵn sàng để so sánh.
    ///
    /// Ba bước chuẩn hóa (theo Vatavu, Anthony &amp; Wobbrock, ICMI 2012):
    ///   1. Resample  — nội suy lại thành đúng N điểm cách đều
    ///                  → bất biến với tốc độ vẽ
    ///   2. Scale     — co về khung vuông chuẩn
    ///                  → bất biến với kích thước
    ///   3. Translate — dời trọng tâm về gốc tọa độ
    ///                  → bất biến với vị trí
    ///
    /// Template cũng là PointCloud, được chuẩn hóa MỘT LẦN lúc load,
    /// không chuẩn hóa lại mỗi lần chấm điểm.
    /// </summary>
    public class PointCloud
    {
        /// <summary>Số điểm sau khi resample. 32 là giá trị bài báo gốc đề xuất.</summary>
        public const int SamplingCount = 32;

        public string Name { get; private set; }
        public Point[] Points { get; private set; }

        public PointCloud(string name, IReadOnlyList<Point> rawPoints)
        {
            if (rawPoints == null || rawPoints.Count < 2)
                throw new ArgumentException("PointCloud cần ít nhất 2 điểm.", nameof(rawPoints));

            Name = name;

            Point[] pts = Resample(rawPoints, SamplingCount);
            pts = Scale(pts);
            pts = TranslateToOrigin(pts);

            Points = pts;
        }

        /// <summary>Dùng khi điểm đã được chuẩn hóa sẵn (nạp template từ asset).</summary>
        private PointCloud(string name, Point[] normalized, bool _)
        {
            Name = name;
            Points = normalized;
        }

        public static PointCloud FromNormalized(string name, Point[] normalized)
            => new PointCloud(name, normalized, true);

        // ------------------------------------------------------------------
        // BƯỚC 1 — RESAMPLE
        // ------------------------------------------------------------------

        /// <summary>
        /// Nội suy lại nét vẽ thành đúng n điểm, RESAMPLE RIÊNG TỪNG NÉT.
        ///
        /// KHÁC BÀI BÁO GỐC (ghi rõ trong báo cáo): bài báo resample cả đám
        /// mây theo một bộ đếm chạy xuyên các nét, nên vị trí điểm lấy mẫu
        /// phụ thuộc thứ tự vẽ → hai người vẽ cùng một hình theo thứ tự nét
        /// khác nhau sẽ ra khoảng cách ~0.03 (ngang sai số của một hình tròn
        /// vẽ tay đúng). Ở đây:
        ///   1. Tách thành các stroke
        ///   2. Chia n điểm cho từng stroke theo tỉ lệ độ dài
        ///   3. Resample mỗi stroke độc lập, bắt đầu từ đầu stroke đó
        /// Kết quả không còn phụ thuộc thứ tự nét. Với hình MỘT nét thì
        /// giống hệt bản gốc.
        /// </summary>
        public static Point[] Resample(IReadOnlyList<Point> points, int n)
        {
            // 1. Tách stroke (theo thứ tự xuất hiện)
            var strokes = new List<List<Point>>();
            var current = new List<Point> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].StrokeID != points[i - 1].StrokeID)
                {
                    strokes.Add(current);
                    current = new List<Point>();
                }
                current.Add(points[i]);
            }
            strokes.Add(current);

            // 2. Độ dài từng stroke
            var lengths = new float[strokes.Count];
            float total = 0f;
            for (int k = 0; k < strokes.Count; k++)
            {
                lengths[k] = StrokeLength(strokes[k]);
                total += lengths[k];
            }

            // Chỉ là một chấm: lặp lại điểm đó n lần
            if (total < 1e-6f)
            {
                var dot = new Point[n];
                for (int i = 0; i < n; i++) dot[i] = points[0];
                return dot;
            }

            // 3. Chia điểm rồi resample từng stroke
            int[] alloc = AllocatePoints(lengths, total, n);

            var result = new List<Point>(n);
            for (int k = 0; k < strokes.Count; k++)
            {
                if (alloc[k] <= 0) continue;
                result.AddRange(ResampleStroke(strokes[k], lengths[k], alloc[k]));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Chia n điểm cho các stroke theo tỉ lệ độ dài (phương pháp phần dư
        /// lớn nhất) — tổng luôn đúng bằng n.
        /// </summary>
        private static int[] AllocatePoints(float[] lengths, float total, int n)
        {
            int count = lengths.Length;
            var alloc = new int[count];
            var fraction = new float[count];
            int used = 0;

            for (int k = 0; k < count; k++)
            {
                float raw = n * lengths[k] / total;
                alloc[k] = (int)Math.Floor(raw);
                fraction[k] = raw - alloc[k];
                used += alloc[k];
            }

            // Phân phần còn lại cho stroke có phần dư lớn nhất
            for (int left = n - used; left > 0; left--)
            {
                int best = 0;
                for (int k = 1; k < count; k++)
                    if (fraction[k] > fraction[best]) best = k;

                alloc[best]++;
                fraction[best] = -1f;   // đã nhận, không chọn lại
            }
            return alloc;
        }

        /// <summary>Resample MỘT stroke thành đúng m điểm cách đều (gồm cả hai đầu).</summary>
        private static List<Point> ResampleStroke(List<Point> stroke, float length, int m)
        {
            if (m == 1) return new List<Point> { stroke[0] };

            float interval = length / (m - 1);
            float accumulated = 0f;

            var result = new List<Point> { stroke[0] };
            var working = new List<Point>(stroke);

            for (int i = 1; i < working.Count; i++)
            {
                Point prev = working[i - 1];
                Point curr = working[i];
                float d = Point.Distance(prev, curr);

                if (d > 0f && accumulated + d >= interval)
                {
                    float t = (interval - accumulated) / d;
                    var inserted = new Point(
                        prev.X + t * (curr.X - prev.X),
                        prev.Y + t * (curr.Y - prev.Y),
                        curr.StrokeID,
                        curr.ColorID);

                    result.Add(inserted);
                    working.Insert(i, inserted);   // tiếp tục đo từ điểm vừa chèn
                    accumulated = 0f;
                }
                else
                {
                    accumulated += d;
                }
            }

            // Sai số làm tròn có thể thiếu 1 điểm: bổ sung điểm cuối của nét gốc
            while (result.Count < m)
                result.Add(working[working.Count - 1]);

            if (result.Count > m)
                result.RemoveRange(m, result.Count - m);

            return result;
        }

        private static float StrokeLength(List<Point> stroke)
        {
            float length = 0f;
            for (int i = 1; i < stroke.Count; i++)
                length += Point.Distance(stroke[i - 1], stroke[i]);
            return length;
        }


        // ------------------------------------------------------------------
        // BƯỚC 2 — SCALE
        // ------------------------------------------------------------------

        /// <summary>
        /// Co nét vẽ về khung vuông đơn vị, GIỮ NGUYÊN TỈ LỆ.
        ///
        /// Chia cho max(width, height) thay vì chia riêng từng chiều —
        /// nếu chia riêng thì hình chữ nhật dài sẽ bị bóp thành vuông và
        /// mất đặc trưng hình dạng.
        /// </summary>
        public static Point[] Scale(Point[] points)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            float size = Math.Max(maxX - minX, maxY - minY);
            if (size < 1e-6f) size = 1e-6f;   // tránh chia 0 khi vẽ một chấm

            var result = new Point[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Point(
                    (points[i].X - minX) / size,
                    (points[i].Y - minY) / size,
                    points[i].StrokeID,
                    points[i].ColorID);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // BƯỚC 3 — TRANSLATE
        // ------------------------------------------------------------------

        /// <summary>Dời trọng tâm (centroid) của đám mây về gốc tọa độ (0,0).</summary>
        public static Point[] TranslateToOrigin(Point[] points)
        {
            float cx = 0f, cy = 0f;
            foreach (var p in points) { cx += p.X; cy += p.Y; }
            cx /= points.Length;
            cy /= points.Length;

            var result = new Point[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Point(
                    points[i].X - cx,
                    points[i].Y - cy,
                    points[i].StrokeID,
                    points[i].ColorID);
            }
            return result;
        }
    }
}