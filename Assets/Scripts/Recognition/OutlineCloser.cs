using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Tự đóng nét hở.
    ///
    /// Người chơi hay vẽ hở vài pixel ở chỗ nét khép lại, nên màu tô sẽ
    /// tràn ra ngoài. Hàm này tìm các ĐẦU NÚT (điểm đầu và điểm cuối của mỗi nét)
    /// gần một điểm nào đó của nét vẽ (cùng nét hoặc nét khác) trong khoảng
    /// maxGap, rồi trả về đoạn thẳng nối chúng.
    ///
    /// Các đoạn này chỉ dùng làm VẬT CẢN khi tô/đếm vùng. Chúng không bị
    /// vẽ ra màn hình và không ảnh hưởng điểm $P (đã chấm từ nét gốc).
    /// </summary>
    public static class OutlineCloser
    {
        /// <param name="maxGap">Khe hở tối đa được vá, tính theo pixel canvas.</param>
        public static List<Segment> FindClosures(IReadOnlyList<Point> pts, float maxGap)
        {
            var closures = new List<Segment>();
            int n = pts.Count;
            if (n < 2 || maxGap <= 0f) return closures;

            // Độ dài cung tích lũy theo từng nét: để biết hai điểm cùng nét
            // cách nhau bao xa DỌC theo nét (điểm hàng xóm không được tính là "khe hở")
            var cum = new float[n];
            var strokeStart = new List<int>();
            var strokeEnd = new List<int>();
            int s0 = 0;
            for (int i = 1; i <= n; i++)
            {
                if (i < n && pts[i].StrokeID == pts[i - 1].StrokeID)
                {
                    cum[i] = cum[i - 1] + Point.Distance(pts[i - 1], pts[i]);
                }
                else
                {
                    strokeStart.Add(s0);
                    strokeEnd.Add(i - 1);
                    s0 = i;
                }
            }

            float minArc = 3f * maxGap;

            for (int k = 0; k < strokeStart.Count; k++)
            {
                if (strokeStart[k] == strokeEnd[k]) continue;   // một chấm

                TryClose(pts, cum, strokeStart[k], maxGap, minArc, closures);
                TryClose(pts, cum, strokeEnd[k],   maxGap, minArc, closures);
            }
            return closures;
        }

        private static void TryClose(IReadOnlyList<Point> pts, float[] cum, int e,
                                     float maxGap, float minArc, List<Segment> output)
        {
            int best = -1;
            float bestD = float.MaxValue;
            int strokeId = pts[e].StrokeID;

            for (int j = 0; j < pts.Count; j++)
            {
                if (pts[j].StrokeID == strokeId && Math.Abs(cum[j] - cum[e]) < minArc)
                    continue;   // hàng xóm trên cùng nét

                float d = Point.Distance(pts[e], pts[j]);
                if (d < bestD) { bestD = d; best = j; }
            }

            // d <= 1.5: hai nét đã chạm nhau, không cần vá
            if (best >= 0 && bestD <= maxGap && bestD > 1.5f)
                output.Add(new Segment(pts[e].X, pts[e].Y, pts[best].X, pts[best].Y));
        }
    }
}
