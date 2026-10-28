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
        public float RoundnessFactor;
        /// <summary>Độ tròn thô đo được (chỉ có khi luật có minRoundness &gt; 0).</summary>
        public float Circularity;
        public float SpikeFactor;
        /// <summary>Độ gai đo được (chỉ có khi luật có maxSpikiness &gt; 0).</summary>
        public float Spikiness;
        public float ColorScore;
        public float Score;
        public bool Missing;
    }

    public class ScoreResult
    {
        public float Shape;          // điểm hình từ $P (0..1)
        public float Parts;          // điểm bộ phận gộp (0..1)
        public float Total;          // điểm cuối sau khi áp trần
        public bool Capped;          // có bị chặn trần vì thiếu bộ phận / hình quá tệ / outline không đủ tròn
        public int OuterCount, InnerCount;
        /// <summary>
        /// Độ tròn THÔ (trung bình theo diện tích) của nhóm vùng Outer / Inner; 0 nếu nhóm trống.
        /// Chỉ để ghi log và hiệu chỉnh minRoundness, không tham gia chấm điểm.
        /// </summary>
        public float OuterCircularity, InnerCircularity;
        /// <summary>Độ gai (trung bình theo diện tích) của nhóm vùng Outer / Inner — để ghi log.</summary>
        public float OuterSpikiness, InnerSpikiness;
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
    ///
    /// Điểm bộ phận (màu, đủ vùng) rất dễ đạt tối đa, nên nếu không chặn gì
    /// thêm, một nét vẽ SAI HÌNH RÕ RỆT vẫn có thể đạt nhờ tô đúng màu — ví dụ
    /// hình 0.42 (rớt) + bộ phận hoàn hảo vẫn ra tổng 0.77 với trọng số mặc định.
    /// minShape chặn đúng trường hợp này: hình dưới ngưỡng thì total cũng bị
    /// chặn ở shapeCap, giống hệt cơ chế missingCap nhưng xét điều kiện hình
    /// thay vì điều kiện bộ phận.
    ///
    /// Vì sao cần THÊM roundnessCap (không chỉ nhân RoundnessFactor vào điểm bộ
    /// phận): một bộ phận có trọng số nhỏ (ví dụ lòng trắng weight=0.2) thì dù
    /// RoundnessFactor tụt hẳn, phần đóng góp vào tổng cũng chỉ giảm tối đa
    /// bằng trọng số đó — không đủ kéo tổng xuống dưới ngưỡng đạt nếu hình
    /// ($P) và bộ phận còn lại vẫn tốt. Giống missingCap/shapeCap, roundnessCap
    /// là một điều kiện CHẶN TRẦN độc lập, không phụ thuộc trọng số riêng của
    /// bộ phận đó. Ba điều kiện (thiếu bộ phận / hình quá tệ / outline không đủ
    /// tròn) độc lập, cùng áp dụng một lượt, cap thấp nhất thắng.
    /// </summary>
    public static class IngredientScorer
    {
        public static ScoreResult Score(float shapeScore, RegionSet regions, int[] fillIds,
                                        ColorTable colors, float shapeWeight,
                                        IReadOnlyList<PartRule> rules, float missingCap,
                                        float minShape = 0f, float shapeCap = 0.5f,
                                        float roundnessCap = 0.5f)
        {
            var result = new ScoreResult { Shape = Clamp01(shapeScore) };
            var report = new StringBuilder();

            double outerC = 0, innerC = 0, outerS = 0, innerS = 0; int outerA = 0, innerA = 0;
            foreach (var r in regions.Regions)
            {
                if (r.Role == PartRole.Outer)
                { result.OuterCount++; outerC += r.Circularity() * r.Area; outerS += r.Spikiness * r.Area; outerA += r.Area; }
                else
                { result.InnerCount++; innerC += r.Circularity() * r.Area; innerS += r.Spikiness * r.Area; innerA += r.Area; }
            }
            result.OuterCircularity = outerA > 0 ? (float)(outerC / outerA) : 0f;
            result.InnerCircularity = innerA > 0 ? (float)(innerC / innerA) : 0f;
            result.OuterSpikiness = outerA > 0 ? (float)(outerS / outerA) : 0f;
            result.InnerSpikiness = innerA > 0 ? (float)(innerS / innerA) : 0f;

            bool shapeTooLow = minShape > 0f && result.Shape < minShape;

            if (rules == null || rules.Count == 0)
            {
                result.Parts = 1f;
                result.Total = result.Shape;
                report.AppendLine("Hình: " + Pct(result.Shape) + " (nguyên liệu không có luật bộ phận)");

                if (shapeTooLow && result.Total > shapeCap)
                {
                    result.Total = shapeCap;
                    result.Capped = true;
                    report.AppendLine("  Hình dưới ngưỡng tối thiểu " + Pct(minShape) +
                                      " → điểm bị chặn tối đa " + Pct(shapeCap));
                }

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
            bool roundnessTooLow = false;

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

                // Độ tròn — trung bình theo diện tích nếu nhóm có nhiều vùng.
                // avgCircularity dùng thang đo THÔ (4π·Area/Perimeter² theo cách đếm
                // cạnh pixel) — KHÔNG phải tỉ lệ % so với hình tròn lý tưởng (hình
                // tròn vẽ bằng nét cọ cũng chỉ ra khoảng 0.4–0.6, không gần 1.0, vì
                // mép brush-stamp vốn lượn sóng nhẹ ở mức pixel). minRoundness vì
                // vậy là một ngưỡng hiệu chỉnh theo đúng cách đo này (xem PartRule),
                // không phải "phải tròn trên X%".
                pr.RoundnessFactor = 1f;
                if (rule.minRoundness > 0f && groupRegions.Count > 0)
                {
                    double sumC = 0; int area = 0;
                    foreach (var r in groupRegions) { sumC += r.Circularity() * r.Area; area += r.Area; }
                    float avgCircularity = area > 0 ? (float)(sumC / area) : 0f;
                    pr.Circularity = avgCircularity;
                    pr.RoundnessFactor = Clamp01(avgCircularity / rule.minRoundness);
                    if (avgCircularity < rule.minRoundness) roundnessTooLow = true;
                }

                // Gai nhọn — trung bình theo diện tích. Không phạt viền lượn sóng/dẹt,
                // chỉ phạt phần nhô ra hẹp. Vượt ngưỡng: chặn trần; điểm bộ phận về 0 ở 3×ngưỡng.
                // Độ gai luôn được ĐO (để báo cáo/ghi CSV), nhưng chỉ bị CHẤM khi luật bật maxSpikiness > 0.
                pr.SpikeFactor = 1f;
                if (groupRegions.Count > 0)
                {
                    double sumS = 0; int area = 0;
                    foreach (var r in groupRegions) { sumS += r.Spikiness * r.Area; area += r.Area; }
                    pr.Spikiness = area > 0 ? (float)(sumS / area) : 0f;
                    if (rule.maxSpikiness > 0f && pr.Spikiness > rule.maxSpikiness)
                    {
                        roundnessTooLow = true;
                        pr.SpikeFactor = Clamp01(1f - (pr.Spikiness - rule.maxSpikiness) / (2f * rule.maxSpikiness));
                    }
                }

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

                pr.Score = pr.CountFactor * pr.AreaFactor * pr.RoundnessFactor * pr.SpikeFactor * pr.ColorScore;
                pr.Missing = rule.Required && pr.Count < rule.minCount;
                if (pr.Missing) missing = true;

                weightSum += rule.weight;
                weighted += rule.weight * pr.Score;
                partWeightSum += rule.weight;
                partWeighted += rule.weight * pr.Score;

                report.AppendLine("  " + rule.name + ": " + pr.Count + " vùng, diện tích " + Pct(pr.AreaRatio) +
                                  ", màu " + Pct(pr.ColorScore) +
                                  (pr.AreaFactor < 1f ? ", diện tích lệch (" + Pct(pr.AreaFactor) + ")" : "") +
                                  (rule.minRoundness > 0f
                                      ? ", độ tròn " + Pct(pr.RoundnessFactor) +
                                        " (đo " + pr.Circularity.ToString("0.00") + " / ngưỡng " +
                                        rule.minRoundness.ToString("0.00") + ")"
                                      : "") +
                                  (rule.maxSpikiness > 0f
                                      ? ", gai " + pr.Spikiness.ToString("0.0000") + " (tối đa " +
                                        rule.maxSpikiness.ToString("0.0000") + ")"
                                      : ", gai " + pr.Spikiness.ToString("0.0000") + " (luật gai TẮT: maxSpikiness = 0)") +
                                  " → " + Pct(pr.Score) + (pr.Missing ? "  THIẾU" : ""));
                result.PartResults.Add(pr);
            }

            result.Parts = partWeightSum > 0f ? partWeighted / partWeightSum : 1f;
            float total = weightSum > 0f ? weighted / weightSum : result.Shape;

            // Ba điều kiện độc lập — thiếu bộ phận HOẶC hình quá tệ HOẶC outline
            // không đủ tròn đều chặn trần. Áp tất cả (lấy cap thấp nhất) để một
            // lỗi không che mất lỗi khác trong log.
            float cap = float.MaxValue;
            if (missing) cap = Math.Min(cap, missingCap);
            if (shapeTooLow) cap = Math.Min(cap, shapeCap);
            if (roundnessTooLow) cap = Math.Min(cap, roundnessCap);

            if (cap < float.MaxValue && total > cap)
            {
                total = cap;
                result.Capped = true;
                if (missing)
                    report.AppendLine("  Thiếu bộ phận bắt buộc → điểm bị chặn tối đa " + Pct(missingCap));
                if (shapeTooLow)
                    report.AppendLine("  Hình dưới ngưỡng tối thiểu " + Pct(minShape) +
                                      " → điểm bị chặn tối đa " + Pct(shapeCap));
                if (roundnessTooLow)
                    report.AppendLine("  Có bộ phận outline không đạt (không đủ tròn / quá nhiều gai) → điểm bị chặn tối đa " + Pct(roundnessCap));
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
