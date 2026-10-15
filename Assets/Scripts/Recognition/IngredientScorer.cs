using System;
using System.Collections.Generic;
using System.Text;

namespace ColorFood.Recognition
{
    public class PartResult
    {
        public string Name;
        public PartRole Role;
        public int Count;
        public float AreaRatio;
        public float CountFactor;
        public float AreaFactor;
        public float ColorScore;
        public float Score;
        public bool Missing;
    }

    public class ScoreResult
    {
        public float Shape;          // điểm hình từ $P (0..1)
        public float Parts;          // điểm bộ phận gộp (0..1)
        public float Total;          // điểm cuối sau khi áp trần
        public bool Capped;          // có bị chặn trần vì thiếu bộ phận bắt buộc không
        public int OuterCount, InnerCount;
        public List<PartResult> PartResults = new List<PartResult>();
        public string Report = "";
    }

    /// <summary>
    /// Điểm cuối = hình ($P) + các bộ phận (số lượng, diện tích, màu).
    ///
    ///   total = (wShape·shape + Σ wᵢ·partᵢ) / (wShape + Σ wᵢ)
    ///   partᵢ = countFactor · areaFactor · colorScore
    ///
    /// Thiếu một bộ phận BẮT BUỘC (minCount ≥ 1) thì total bị chặn ở missingCap.
    /// Đây là chỗ sửa điểm yếu đã đo được: $P một mình không thấy thiếu lòng đỏ
    /// (d chỉ tăng ~0.01, nhỏ hơn dung sai nét vẽ tay).
    /// </summary>
    public static class IngredientScorer
    {
        public static ScoreResult Score(float shapeScore, RegionSet regions, int[] fillIds,
                                        ColorTable colors, float shapeWeight,
                                        IReadOnlyList<PartRule> rules, float missingCap)
        {
            var result = new ScoreResult { Shape = Clamp01(shapeScore) };
            var report = new StringBuilder();

            foreach (var r in regions.Regions)
            {
                if (r.Role == PartRole.Outer) result.OuterCount++; else result.InnerCount++;
            }

            if (rules == null || rules.Count == 0)
            {
                result.Parts = 1f;
                result.Total = result.Shape;
                report.AppendLine("Hình: " + Pct(result.Shape) + " (nguyên liệu không có luật bộ phận)");
                result.Report = report.ToString();
                return result;
            }

            // Histogram màu theo vùng: [vùng][0 = chưa tô, id+1 = màu id]
            int k = colors.Count;
            var hist = new int[regions.Regions.Count][];
            for (int i = 0; i < hist.Length; i++) hist[i] = new int[k + 1];
            for (int p = 0; p < regions.Labels.Length; p++)
            {
                int lab = regions.Labels[p];
                if (lab < 0) continue;
                int fid = fillIds[p];
                hist[lab][(fid >= 0 && fid < k) ? fid + 1 : 0]++;
            }

            float weightSum = shapeWeight;
            float weighted = shapeWeight * result.Shape;
            float partWeightSum = 0f, partWeighted = 0f;
            bool missing = false;

            report.AppendLine("Hình ($P): " + Pct(result.Shape));

            foreach (var rule in rules)
            {
                var pr = new PartResult { Name = rule.name, Role = rule.role };

                int groupArea = 0;
                var groupRegions = new List<Region>();
                foreach (var r in regions.Regions)
                    if (r.Role == rule.role) { groupRegions.Add(r); groupArea += r.Area; }

                pr.Count = groupRegions.Count;
                pr.AreaRatio = regions.TotalArea > 0 ? groupArea / (float)regions.TotalArea : 0f;

                // Số lượng
                if (pr.Count < rule.minCount) pr.CountFactor = 0f;
                else if (rule.maxCount > 0 && pr.Count > rule.maxCount)
                    pr.CountFactor = rule.maxCount / (float)pr.Count;
                else pr.CountFactor = 1f;

                // Diện tích
                pr.AreaFactor = rule.areaRatioMax > 0f
                    ? RangeScore(pr.AreaRatio, rule.areaRatioMin, rule.areaRatioMax) : 1f;

                // Màu: trung bình độ giống trên TỪNG pixel của nhóm (chưa tô = 0)
                pr.ColorScore = 1f;
                if (rule.targetColors != null && rule.targetColors.Length > 0)
                {
                    var targets = new List<int>();
                    foreach (string t in rule.targetColors)
                    {
                        int id = colors.IndexOf(t);
                        if (id >= 0) targets.Add(id);
                        else report.AppendLine("  ! Không có màu '" + t + "' trong palette");
                    }

                    double sum = 0; int area = 0;
                    foreach (var r in groupRegions)
                    {
                        for (int c = 1; c <= k; c++)
                        {
                            int cnt = hist[r.Id][c];
                            if (cnt == 0) continue;
                            float best = 0f;
                            foreach (int tid in targets)
                                best = Math.Max(best, colors.Similarity(c - 1, tid));
                            sum += best * cnt;
                        }
                        area += r.Area;
                    }
                    pr.ColorScore = area > 0 ? (float)(sum / area) : 0f;
                }

                pr.Score = pr.CountFactor * pr.AreaFactor * pr.ColorScore;
                pr.Missing = rule.Required && pr.Count < rule.minCount;
                if (pr.Missing) missing = true;

                weightSum += rule.weight;
                weighted += rule.weight * pr.Score;
                partWeightSum += rule.weight;
                partWeighted += rule.weight * pr.Score;

                report.AppendLine("  " + rule.name + ": " + pr.Count + " vùng, diện tích " + Pct(pr.AreaRatio) +
                                  ", màu " + Pct(pr.ColorScore) +
                                  (pr.AreaFactor < 1f ? ", diện tích lệch (" + Pct(pr.AreaFactor) + ")" : "") +
                                  " → " + Pct(pr.Score) + (pr.Missing ? "  THIẾU" : ""));
                result.PartResults.Add(pr);
            }

            result.Parts = partWeightSum > 0f ? partWeighted / partWeightSum : 1f;
            float total = weightSum > 0f ? weighted / weightSum : result.Shape;

            if (missing && total > missingCap)
            {
                total = missingCap;
                result.Capped = true;
                report.AppendLine("  Thiếu bộ phận bắt buộc → điểm bị chặn tối đa " + Pct(missingCap));
            }

            result.Total = Clamp01(total);
            result.Report = report.ToString();
            return result;
        }

        /// <summary>1 nếu nằm trong [min,max]; giảm dần tuyến tính khi lệch ra ngoài.</summary>
        public static float RangeScore(float v, float min, float max)
        {
            if (v >= min && v <= max) return 1f;
            if (v < min) return min > 0f ? Clamp01(v / min) : 0f;
            return max > 0f ? Clamp01(1f - (v - max) / max) : 0f;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static string Pct(float v) => Math.Round(v * 100f) + "%";
    }
}
