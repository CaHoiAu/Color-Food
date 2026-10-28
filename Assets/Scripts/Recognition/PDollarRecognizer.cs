using System;
using System.Collections.Generic;
using System.Drawing;

namespace ColorFood.Recognition
{
    public class PDollarRecognizer
    {
        public float Match(PointCloud drawn, PointCloud template)
        {
            return GreedyCloudMatch(drawn.Points, template.Points);
        }
        public float MatchScore(PointCloud drawn, PointCloud template, float maxDistance = 0.09f)
        {
            float d = Match(drawn, template);
            float score = 1f - (d / maxDistance);
            return score < 0f ? 0f : (score > 1f ? 1f : score);
        }
        public float MatchBest(PointCloud drawn, IReadOnlyList<PointCloud> templates, out int bestIndex)
        {
            bestIndex = -1;
            float best = float.MaxValue;
            if (templates == null) return best;

            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i] == null) continue;
                float d = Match(drawn, templates[i]);
                if (d < best) { best = d; bestIndex = i; }
            }
            return best;
        }

        /// <summary>Điểm 0..1 theo mẫu giống nhất trong danh sách (xem MatchBest).</summary>
        public float MatchBestScore(PointCloud drawn, IReadOnlyList<PointCloud> templates,
                                    float maxDistance, out int bestIndex)
        {
            float d = MatchBest(drawn, templates, out bestIndex);
            if (bestIndex < 0) return 0f;
            float score = 1f - (d / maxDistance);
            return score < 0f ? 0f : (score > 1f ? 1f : score);
        }
        public (string name, float score) Classify(
            PointCloud drawn, IEnumerable<PointCloud> templates, float maxDistance = 0.09f)
        {
            string best = null;
            float bestDist = float.MaxValue;

            foreach (var t in templates)
            {
                float d = Match(drawn, t);
                if (d < bestDist) { bestDist = d; best = t.Name; }
            }

            float score = 1f - (bestDist / maxDistance);
            return (best, score < 0f ? 0f : (score > 1f ? 1f : score));
        }
        public static float GreedyCloudMatch(Point[] a, Point[] b)
        {
            int n = a.Length;

            // epsilon quyết định số điểm khởi đầu được thử.
            // 0.5 theo bài báo gốc — cân bằng giữa độ chính xác và tốc độ.
            const float epsilon = 0.5f;
            int step = (int)Math.Floor(Math.Pow(n, 1f - epsilon));
            if (step < 1) step = 1;

            float min = float.MaxValue;

            for (int i = 0; i < n; i += step)
            {
                float d1 = CloudDistance(a, b, i);
                float d2 = CloudDistance(b, a, i);
                float d = Math.Min(d1, d2);
                if (d < min) min = d;
            }

            return min / n;   // chuẩn hóa theo số điểm
        }
        private static float CloudDistance(Point[] cloud1, Point[] cloud2, int startIndex)
        {
            int n = cloud1.Length;
            var matched = new bool[n];
            float sum = 0f;

            int i = startIndex;
            do
            {
                int minIndex = -1;
                float minDist = float.MaxValue;

                for (int j = 0; j < n; j++)
                {
                    if (matched[j]) continue;

                    float d = Point.SqrDistance(cloud1[i], cloud2[j]);
                    if (d < minDist) { minDist = d; minIndex = j; }
                }

                if (minIndex < 0) break;

                matched[minIndex] = true;

                // Trọng số tuyến tính giảm dần: 1 ở điểm đầu → gần 0 ở điểm cuối
                int matchedCount = CountMatched(matched);
                float weight = 1f - ((matchedCount - 1) / (float)n);

                sum += weight * (float)Math.Sqrt(minDist);

                i = (i + 1) % n;
            }
            while (i != startIndex);

            return sum;
        }

        private static int CountMatched(bool[] matched)
        {
            int count = 0;
            for (int i = 0; i < matched.Length; i++)
                if (matched[i]) count++;
            return count;
        }
    }
}
