using System;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Một điểm trong nét vẽ.
    /// KHÔNG dùng UnityEngine.Vector2 — tầng này phải thuần C# để unit test
    /// được mà không cần Unity Editor chạy.
    /// </summary>
    [Serializable]
    public struct Point
    {
        public float X;
        public float Y;

        /// <summary>
        /// Nét vẽ thứ mấy. Mỗi lần người chơi nhấc bút lên là StrokeID tăng 1.
        /// $P cần thông tin này để biết đâu là các nét rời nhau.
        /// </summary>
        public int StrokeID;

        /// <summary>
        /// Màu người chơi dùng khi vẽ điểm này (index trong palette).
        /// Dùng ở tầng cao hơn để gom điểm thành part, không dùng trong $P.
        /// </summary>
        public int ColorID;

        public Point(float x, float y, int strokeId, int colorId = 0)
        {
            X = x;
            Y = y;
            StrokeID = strokeId;
            ColorID = colorId;
        }

        public static float Distance(Point a, Point b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Bình phương khoảng cách — dùng khi chỉ cần so sánh, tránh Sqrt.</summary>
        public static float SqrDistance(Point a, Point b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public override string ToString() => $"({X:F1}, {Y:F1}) s{StrokeID}";
    }
}