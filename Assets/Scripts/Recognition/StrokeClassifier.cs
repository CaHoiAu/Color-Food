using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Đoán một nét sáp là NÉT VIỀN hay NÉT TÔ, để người chơi chỉ cần một công cụ
    /// (như tô màu thật: viền dâu màu đỏ, tô đỏ bên trong; viền lá xanh, tô xanh).
    ///
    /// Quy tắc: nét là TÔ khi
    ///   1. ít nhất minInside số điểm của nét nằm trong các vùng kín đã có, và
    ///   2. màu nét giống MỘT TRONG CÁC MÀU VIỀN của vùng chứa phần lớn nét
    ///      (độ giống ≥ minSimilarity). Màu viền = mọi màu chiếm ≥ minShare viền
    ///      vùng: vùng lá nằm giữa viền lá xanh và viền thân đỏ có CẢ HAI màu.
    /// Còn lại là VIỀN: vẽ ở chỗ trống, hoặc vẽ màu khác bên trong một vùng
    /// (lá xanh chồng lên thân đỏ, lòng đỏ vàng trong lòng trắng).
    ///
    /// Đoán lúc nhấc tay (IsFill) phụ thuộc thứ tự vẽ: tô TRƯỚC khi viền khép kín
    /// thì nét tô bị coi là viền. Reconcile xét lại mọi nét sau cùng để sửa việc đó.
    ///
    /// Giới hạn: kiểu vẽ viền đen rồi tô màu sẽ bị đoán sai (nét tô khác màu viền
    /// → thành viền). Kiểu đó dùng công cụ Draw (viền) + Fill (thùng sơn).
    /// </summary>
    /// <summary>Một nét đã vẽ, dùng cho bước xét lại (Reconcile).</summary>
    public class DrawnStroke
    {
        public List<Point> Points = new List<Point>();
        public int ColorId;
        /// <summary>Bán kính nét (pixel) — nét viền chặn vùng đúng bằng bề rộng này.</summary>
        public int Radius;
        public bool IsFill;
        /// <summary>true = nét sáp (được đoán viền/tô); false = nét bút, luôn là viền.</summary>
        public bool Auto;
    }

    public static class StrokeClassifier
    {
        public const float DefaultMinInside = 0.7f;
        public const float DefaultMinSimilarity = 0.35f;
        public const float DefaultMinShare = 0.25f;

        /// <summary>
        /// Các màu viền của từng vùng: mọi màu mực chiếm ≥ minShare số pixel mực
        /// giáp vùng, nhiều nhất đứng đầu. Mảng rỗng nếu vùng không giáp mực có màu.
        ///
        /// Không chỉ lấy màu đa số: vùng lá nằm trên thân được bao bởi viền lá
        /// (xanh) VÀ một đoạn viền thân (đỏ); nếu đoạn đỏ dài hơn, chỉ lấy đa số
        /// sẽ khiến mọi nét tô xanh trong lá bị đoán là viền.
        /// </summary>
        public static int[][] RegionOutlineColors(RegionSet regions, int[] inkColorIds, int colorCount,
                                                  float minShare = DefaultMinShare)
        {
            int[][] hist = IngredientScorer.InkHistogram(regions, inkColorIds, colorCount);
            var result = new int[regions.Regions.Count][];
            for (int r = 0; r < result.Length; r++)
            {
                int total = 0;
                for (int c = 1; c <= colorCount; c++) total += hist[r][c];

                var list = new List<int>();
                for (int c = 1; c <= colorCount; c++)
                    if (hist[r][c] > 0 && hist[r][c] >= minShare * total) list.Add(c - 1);
                list.Sort((a, b) => hist[r][b + 1].CompareTo(hist[r][a + 1]));
                result[r] = list.ToArray();
            }
            return result;
        }

        /// <param name="stroke">Các điểm của MỘT nét (tọa độ pixel canvas).</param>
        /// <param name="colorId">Màu của nét.</param>
        /// <param name="regionOutlineColors">Kết quả RegionOutlineColors cho đúng regions này.</param>
        /// <param name="barrier">
        /// Mặt nạ chặn đã dùng để tách regions (tùy chọn). Có nó thì điểm nằm TRÊN MỰC
        /// được bỏ qua thay vì tính là ở ngoài — nét tô đè lên nét khác (hay lấn ra
        /// viền) không bị thiệt. Nhưng nếu hơn nửa số điểm nằm trên mực (vẽ lại đè lên
        /// viền cũ) thì vẫn là viền.
        /// </param>
        public static bool IsFill(IReadOnlyList<Point> stroke, int colorId, RegionSet regions,
                                  int[][] regionOutlineColors, ColorTable colors,
                                  float minInside = DefaultMinInside,
                                  float minSimilarity = DefaultMinSimilarity,
                                  bool[] barrier = null)
        {
            if (stroke == null || stroke.Count == 0 || regions == null || regions.Regions.Count == 0)
                return false;

            var votes = new int[regions.Regions.Count];
            int inside = 0, considered = 0;
            foreach (Point p in stroke)
            {
                int x = (int)p.X, y = (int)p.Y;
                if (x < 0 || y < 0 || x >= regions.Width || y >= regions.Height) { considered++; continue; }
                int idx = y * regions.Width + x;
                if (barrier != null && barrier[idx]) continue;   // trên mực: không tính
                considered++;
                int lab = regions.Labels[idx];
                if (lab < 0) continue;
                votes[lab]++;
                inside++;
            }
            if (considered < 0.5f * stroke.Count) return false;
            if (inside < minInside * considered) return false;

            int region = 0;
            for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[region]) region = i;

            if (colorId < 0 || colorId >= colors.Count) return false;
            foreach (int outline in regionOutlineColors[region])
                if (colors.Similarity(colorId, outline) >= minSimilarity) return true;
            return false;
        }

        /// <summary>
        /// Xét lại TOÀN BỘ nét sáp đang là viền, không phụ thuộc thứ tự vẽ: bỏ tạm
        /// nét đó ra, dựng vùng kín từ các nét viền còn lại; nếu nét nằm trong một
        /// vùng kín cùng màu viền (IsFill) thì chuyển thành tô. Lặp đến khi không
        /// còn gì đổi (tối đa maxPasses lượt). Nét bút (Auto = false) không bao giờ đổi.
        ///
        /// Bắt được: tô trước rồi mới vẽ viền bao quanh; vẽ nửa viền, tô, rồi khép
        /// viền. Không đụng tới viền thật: bỏ một nét viền thật ra thì điểm của nó
        /// nằm trên mực của nét khác hoặc ngoài vùng, nên không đạt minInside.
        /// </summary>
        /// <returns>true nếu có nét đổi từ viền sang tô.</returns>
        public static bool Reconcile(List<DrawnStroke> strokes, int w, int h, float closeGap,
                                     int minArea, ColorTable colors, int maxPasses = 3)
        {
            bool any = false;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool changed = false;
                for (int i = 0; i < strokes.Count; i++)
                {
                    DrawnStroke s = strokes[i];
                    if (!s.Auto || s.IsFill || s.Points.Count == 0) continue;

                    int[] ink;
                    bool[] barrier = BuildOutlineBarrier(strokes, i, w, h, closeGap, out ink);
                    RegionSet regions = RegionAnalyzer.Analyze(barrier, w, h, minArea, measure: false);
                    if (regions.Regions.Count == 0) continue;

                    int[][] outlineColors = RegionOutlineColors(regions, ink, colors.Count);
                    if (IsFill(s.Points, s.ColorId, regions, outlineColors, colors, barrier: barrier))
                    {
                        s.IsFill = true;
                        changed = any = true;
                    }
                }
                if (!changed) break;
            }
            return any;
        }

        /// <summary>Mặt nạ chặn + màu mực từ mọi nét viền, trừ nét số skip (-1 = không trừ).</summary>
        public static bool[] BuildOutlineBarrier(List<DrawnStroke> strokes, int skip, int w, int h,
                                                 float closeGap, out int[] inkColorIds)
        {
            var barrier = new bool[w * h];
            inkColorIds = new int[w * h];
            for (int i = 0; i < inkColorIds.Length; i++) inkColorIds[i] = -1;

            var all = new List<Point>();
            var mask = new bool[w * h];
            int id = 0;
            for (int i = 0; i < strokes.Count; i++)
            {
                DrawnStroke s = strokes[i];
                if (i == skip || s.IsFill || s.Points.Count == 0) continue;

                var pts = new List<Point>(s.Points.Count);
                foreach (Point p in s.Points) pts.Add(new Point(p.X, p.Y, id, s.ColorId));
                id++;

                System.Array.Clear(mask, 0, mask.Length);
                Raster.DrawPoints(mask, w, h, pts, s.Radius);
                for (int k = 0; k < mask.Length; k++)
                    if (mask[k]) { barrier[k] = true; inkColorIds[k] = s.ColorId; }
                all.AddRange(pts);
            }

            if (closeGap > 0f && all.Count > 0)
                foreach (Segment seg in OutlineCloser.FindClosures(all, closeGap))
                    Raster.DrawSegment(barrier, w, h, seg, 2);
            return barrier;
        }
    }
}
