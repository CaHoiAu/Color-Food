using System;
using System.Collections.Generic;
using System.Drawing;

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
        /// Nội suy lại nét vẽ thành đúng n điểm cách đều nhau.
        ///
        /// Ý tưởng: tính tổng chiều dài đường vẽ, chia cho (n-1) được khoảng
        /// cách chuẩn I. Đi dọc đường vẽ, cứ đủ I thì chèn một điểm mới.
        ///
        /// Lưu ý: KHÔNG nội suy giữa hai điểm thuộc hai stroke khác nhau
        /// (nhấc bút lên thì không có đường nối).
        /// </summary>
        public static Point[] Resample(IReadOnlyList<Point> points, int n)
        {
            float interval = PathLength(points) / (n - 1);
            float accumulated = 0f;

            var result = new List<Point> { points[0] };
            var working = new List<Point>(points);

            for (int i = 1; i < working.Count; i++)
            {
                Point prev = working[i - 1];
                Point curr = working[i];

                // Nhấc bút: bắt đầu stroke mới, không nội suy qua khoảng trống
                if (curr.StrokeID != prev.StrokeID)
                {
                    accumulated = 0f;
                    result.Add(curr);
                    continue;
                }

                float d = Point.Distance(prev, curr);

                if (accumulated + d >= interval)
                {
                    // Điểm mới nằm giữa prev và curr
                    float t = (interval - accumulated) / d;
                    var inserted = new Point(
                        prev.X + t * (curr.X - prev.X),
                        prev.Y + t * (curr.Y - prev.Y),
                        curr.StrokeID,
                        curr.ColorID);

                    result.Add(inserted);

                    // Chèn lại vào danh sách để tiếp tục đo từ điểm vừa tạo
                    working.Insert(i, inserted);
                    accumulated = 0f;
                }
                else
                {
                    accumulated += d;
                }
            }

            // Sai số làm tròn có thể khiến thiếu đúng 1 điểm
            while (result.Count < n)
                result.Add(result[result.Count - 1]);

            if (result.Count > n)
                result.RemoveRange(n, result.Count - n);

            return result.ToArray();
        }

        private static float PathLength(IReadOnlyList<Point> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].StrokeID == points[i - 1].StrokeID)
                    length += Point.Distance(points[i - 1], points[i]);
            }
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