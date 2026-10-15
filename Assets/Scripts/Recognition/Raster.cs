using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>Đoạn thẳng nối hai điểm — dùng cho nét "đóng hở".</summary>
    public struct Segment
    {
        public float X1, Y1, X2, Y2;
        public Segment(float x1, float y1, float x2, float y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
    }

    /// <summary>
    /// Vẽ nét lên mặt nạ bool[] (true = có mực / vật cản). Thuần C#.
    /// Canvas Unity dùng cùng hàm này, và test cũng dùng nó để dựng hình mẫu,
    /// nên hành vi trong test giống hành vi thật.
    /// </summary>
    public static class Raster
    {
        public static void Disc(bool[] mask, int w, int h, float cx, float cy, int radius)
        {
            int icx = (int)Math.Round(cx), icy = (int)Math.Round(cy);
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = icy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = icx + dx;
                    if (x < 0 || x >= w) continue;
                    mask[y * w + x] = true;
                }
            }
        }

        public static void DrawSegment(bool[] mask, int w, int h, Segment s, int radius)
            => DrawSegment(mask, w, h, s.X1, s.Y1, s.X2, s.Y2, radius);

        public static void DrawSegment(bool[] mask, int w, int h,
                                       float x1, float y1, float x2, float y2, int radius)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(len / Math.Max(1f, radius * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(mask, w, h, x1 + dx * t, y1 + dy * t, radius);
            }
        }

        /// <summary>Vẽ cả danh sách điểm: nối các điểm liên tiếp CÙNG StrokeID.</summary>
        public static void DrawPoints(bool[] mask, int w, int h, IReadOnlyList<Point> pts, int radius)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0 && pts[i].StrokeID == pts[i - 1].StrokeID)
                    DrawSegment(mask, w, h, pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, radius);
                else
                    Disc(mask, w, h, pts[i].X, pts[i].Y, radius);
            }
        }
    }
}
