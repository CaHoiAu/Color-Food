using System;
using System.Collections.Generic;
using System.Text;

namespace ColorFood.Recognition
{
    /// <summary>Một biến thể (góc độ) của mẫu: đám mây đường bao + lưới màu.</summary>
    public class SketchShape
    {
        public string Name;
        public PointCloud Cloud;
        public int[] Grid;
    }

    /// <summary>Điểm màu của một màu trong mẫu (vd: phần đỏ, phần xanh).</summary>
    public class ColorPartResult
    {
        public int ColorId;
        public int Cells;        // số ô mẫu có màu này
        public float Coverage;   // phần đã được tô (bất kỳ màu nào)
        public float Accuracy;   // độ đúng màu trên phần đã tô
        public float Score;
    }

    public class ColorCompareResult
    {
        public float Score, Accuracy, Coverage, Spill;
        public List<ColorPartResult> Parts = new List<ColorPartResult>();
    }

    public class SketchResult
    {
        public float Distance, Shape, Color = 1f, Total;
        public float Accuracy = 1f, Coverage = 1f, Spill;
        public bool HasColor, Capped;
        public string Variant = "";
        public List<ColorPartResult> Parts = new List<ColorPartResult>();
        public string Report = "";
    }

    /// <summary>
    /// Chấm điểm kiểu "ảnh màu" — KHÔNG phân biệt nét viền / nét tô, không phụ
    /// thuộc thứ tự vẽ, không cần luật bộ phận.
    ///
    ///   HÌNH: gộp mọi ô có màu → bỏ mảnh lạc nhỏ → đóng khe (vân sáp) → lấp lỗ
    ///         → lấy pixel đường bao ngoài → chọn 32 điểm trải đều (farthest point
    ///         sampling) → chuẩn hóa → so $P với đường bao của mẫu.
    ///   MÀU:  co bức vẽ (theo khung bao các ô có màu) về lưới GridSize×GridSize,
    ///         mỗi ô một màu → so từng ô với lưới của MẪU TÔ SẴN cùng vị trí (cho
    ///         lệch 1 ô). Nhờ so theo vị trí, lá phải xanh ở chỗ lá, thân đỏ ở chỗ
    ///         thân — không cần đoán đâu là thân, đâu là lá.
    ///
    ///   màu  = TRUNG BÌNH các phần màu của mẫu (mỗi màu một phần, trọng số NGANG
    ///          NHAU), mỗi phần = độ đúng màu × min(1, độ phủ / minCoverage)
    ///          — lá nhỏ tô sai chỗ cũng kéo điểm xuống như thân tô sai, không bị
    ///          "pha loãng" vì chỉ chiếm ít ô
    ///   tổng = shapeWeight × hình + (1 − shapeWeight) × màu   (mẫu không có màu → tổng = hình)
    /// Thử mọi biến thể góc độ của mẫu, lấy tổng cao nhất.
    /// </summary>
    public static class SketchScorer
    {
        public const int GridSize = 24;
        public const int CloseRadius = 2;
        public const float MinComponentShare = 0.03f;
        public const float CellMinFraction = 0.25f;

        // ------------------------------------------------------------------
        // Hình
        // ------------------------------------------------------------------

        /// <summary>Ô có màu, đã bỏ các mảnh rời (8 hướng) nhỏ hơn minShare tổng số ô có màu.</summary>
        public static bool[] KeptPaint(ColorSketch s, float minShare = MinComponentShare)
        {
            int n = s.Size, total = s.PaintedCount;
            var kept = new bool[n * n];
            var seen = new bool[n * n];
            var comp = new List<int>();
            var stack = new Stack<int>();

            for (int start = 0; start < kept.Length; start++)
            {
                if (seen[start] || s.Cells[start] == ColorSketch.Empty) continue;
                comp.Clear();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    comp.Add(p);
                    int y = p / n, x = p - y * n;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                            int q = ny * n + nx;
                            if (seen[q] || s.Cells[q] == ColorSketch.Empty) continue;
                            seen[q] = true;
                            stack.Push(q);
                        }
                }
                if (comp.Count >= minShare * total)
                    foreach (int p in comp) kept[p] = true;
            }
            return kept;
        }

        /// <summary>Bóng đặc: đóng khe hẹp rồi lấp mọi lỗ không thông ra mép.</summary>
        public static bool[] Silhouette(bool[] kept, int size, int closeRadius = CloseRadius)
        {
            bool[] closed = closeRadius > 0 ? Morphology.Close(kept, size, size, closeRadius) : (bool[])kept.Clone();

            // Lấp lỗ: nền = phần trống thông ra mép; còn lại (hình + lỗ) = bóng
            var outside = new bool[size * size];
            var stack = new Stack<int>();
            for (int i = 0; i < size; i++)
            {
                Seed(i, 0); Seed(i, size - 1); Seed(0, i); Seed(size - 1, i);
            }
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int y = p / size, x = p - y * size;
                if (x > 0) Seed(x - 1, y);
                if (x < size - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < size - 1) Seed(x, y + 1);
            }

            var sil = new bool[size * size];
            for (int i = 0; i < sil.Length; i++) sil[i] = !outside[i];
            return sil;

            void Seed(int x, int y)
            {
                int q = y * size + x;
                if (closed[q] || outside[q]) return;
                outside[q] = true;
                stack.Push(q);
            }
        }

        /// <summary>Ô của bóng có ít nhất một ô kề (4 hướng) nằm ngoài bóng hoặc ngoài lưới.</summary>
        public static List<Point> Boundary(bool[] sil, int size)
        {
            var pts = new List<Point>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (!sil[y * size + x]) continue;
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1 ||
                                !sil[y * size + x - 1] || !sil[y * size + x + 1] ||
                                !sil[(y - 1) * size + x] || !sil[(y + 1) * size + x];
                    if (edge) pts.Add(new Point(x + 0.5f, y + 0.5f, 0));
                }
            return pts;
        }

        /// <summary>
        /// Chọn n điểm trải đều trên tập điểm (lần lượt lấy điểm XA NHẤT các điểm đã
        /// chọn). Với pixel đường bao, kết quả gần như cách đều dọc theo đường bao —
        /// không cần dò thứ tự đường bao. Xác định (cùng đầu vào → cùng kết quả).
        /// </summary>
        public static Point[] FarthestPointSample(IReadOnlyList<Point> pts, int n)
        {
            if (pts.Count <= n)
            {
                var all = new Point[pts.Count];
                for (int i = 0; i < pts.Count; i++) all[i] = pts[i];
                return all;
            }

            var result = new Point[n];
            var dist = new float[pts.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;

            int current = 0;
            for (int k = 0; k < n; k++)
            {
                result[k] = pts[current];
                int next = 0;
                float far = -1f;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = Point.SqrDistance(pts[i], pts[current]);
                    if (d < dist[i]) dist[i] = d;
                    if (dist[i] > far) { far = dist[i]; next = i; }
                }
                current = next;
            }
            return result;
        }

        /// <summary>Đám mây $P từ pixel đường bao: chọn 32 điểm trải đều, co + dời về gốc.</summary>
        public static PointCloud ToCloud(IReadOnlyList<Point> boundary, string name)
        {
            Point[] sample = FarthestPointSample(boundary, PointCloud.SamplingCount);
            return PointCloud.FromNormalized(name, PointCloud.TranslateToOrigin(PointCloud.Scale(sample)));
        }

        // ------------------------------------------------------------------
        // Màu
        // ------------------------------------------------------------------

        /// <summary>Mỗi ô đã giữ lại thành một điểm (tâm ô) mang màu của ô.</summary>
        public static List<Point> ColoredPoints(ColorSketch s, bool[] kept)
        {
            var pts = new List<Point>();
            for (int y = 0; y < s.Size; y++)
                for (int x = 0; x < s.Size; x++)
                {
                    int i = y * s.Size + x;
                    if (kept[i]) pts.Add(new Point(x + 0.5f, y + 0.5f, 0, s.Cells[i]));
                }
            return pts;
        }

        /// <summary>
        /// Co các điểm màu (mật độ ~1 điểm / đơn vị diện tích) về lưới g×g theo khung
        /// bao của chúng, giữ tỉ lệ, căn giữa. Ô có ≥ minFraction diện tích được phủ
        /// lấy màu chiếm đa số; còn lại Empty.
        /// </summary>
        public static int[] Grid(IReadOnlyList<Point> pts, int g = GridSize, float minFraction = CellMinFraction)
        {
            var grid = new int[g * g];
            for (int i = 0; i < grid.Length; i++) grid[i] = ColorSketch.Empty;
            if (pts.Count == 0) return grid;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int maxId = 0;
            foreach (Point p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                if (p.ColorID > maxId) maxId = p.ColorID;
            }
            // +1: mỗi điểm đại diện cho một ô 1×1 quanh nó
            float span = Math.Max(maxX - minX, maxY - minY) + 1f;
            float cell = span / g;
            float ox = minX - 0.5f - (span - (maxX - minX + 1f)) / 2f;
            float oy = minY - 0.5f - (span - (maxY - minY + 1f)) / 2f;

            var votes = new int[g * g, maxId + 3];   // chỉ số màu + 2 (NoColor → 0)
            var count = new int[g * g];
            foreach (Point p in pts)
            {
                int gx = Math.Min(g - 1, Math.Max(0, (int)((p.X - ox) / cell)));
                int gy = Math.Min(g - 1, Math.Max(0, (int)((p.Y - oy) / cell)));
                int c = gy * g + gx;
                votes[c, p.ColorID + 2]++;
                count[c]++;
            }

            float need = minFraction * cell * cell;
            for (int c = 0; c < grid.Length; c++)
            {
                if (count[c] < need || count[c] == 0) continue;
                int best = 0;
                for (int k = 1; k < maxId + 3; k++) if (votes[c, k] > votes[c, best]) best = k;
                grid[c] = best - 2;
            }
            return grid;
        }

        /// <summary>
        /// So lưới màu người chơi với lưới mẫu.
        ///   độ phủ    = ô mẫu có màu mà người chơi cũng tô (bất kỳ màu) / ô mẫu có màu
        ///   độ đúng   = trên các ô đó, độ giống tốt nhất với màu mẫu trong vùng 3×3 quanh ô
        ///               của người chơi (cho lệch 1 ô vì nét tay không khớp tuyệt đối)
        ///   tô lem    = ô người chơi tô mà quanh nó (3×3) mẫu không có màu / ô người chơi tô
        /// Ô mẫu NoColor (mẫu không rõ màu) chấp nhận mọi màu.
        /// </summary>
        public static ColorCompareResult Compare(int[] player, int[] template, int g, ColorTable colors,
                                                 float minCoverage)
        {
            var r = new ColorCompareResult();
            var parts = new Dictionary<int, ColorPartResult>();
            var partAcc = new Dictionary<int, double>();
            int cells = 0, covered = 0;
            double acc = 0;

            for (int y = 0; y < g; y++)
                for (int x = 0; x < g; x++)
                {
                    int t = template[y * g + x];
                    if (t == ColorSketch.Empty) continue;
                    cells++;

                    if (!parts.TryGetValue(t, out ColorPartResult part))
                    {
                        part = new ColorPartResult { ColorId = t };
                        parts[t] = part;
                        partAcc[t] = 0;
                    }
                    part.Cells++;

                    if (player[y * g + x] == ColorSketch.Empty) continue;
                    covered++;
                    part.Coverage++;

                    float best = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= g || ny >= g) continue;
                            int pc = player[ny * g + nx];
                            if (pc == ColorSketch.Empty) continue;
                            float s = t == ColorSketch.NoColor ? 1f : (pc >= 0 ? colors.Similarity(pc, t) : 0f);
                            if (s > best) best = s;
                        }
                    acc += best;
                    partAcc[t] += best;
                }

            int painted = 0, spill = 0;
            for (int y = 0; y < g; y++)
                for (int x = 0; x < g; x++)
                {
                    if (player[y * g + x] == ColorSketch.Empty) continue;
                    painted++;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < g && ny < g && template[ny * g + nx] != ColorSketch.Empty)
                                near = true;
                        }
                    if (!near) spill++;
                }

            float minCov = minCoverage > 0f ? minCoverage : 0.6f;
            r.Coverage = cells > 0 ? covered / (float)cells : 0f;
            r.Accuracy = covered > 0 ? (float)(acc / covered) : 0f;
            r.Spill = painted > 0 ? spill / (float)painted : 0f;

            float sum = 0f;
            foreach (var kv in parts)
            {
                ColorPartResult p = kv.Value;
                float hit = p.Coverage;
                p.Accuracy = hit > 0 ? (float)(partAcc[kv.Key] / hit) : 0f;
                p.Coverage = p.Cells > 0 ? hit / p.Cells : 0f;
                p.Score = p.Accuracy * Clamp01(p.Coverage / minCov);
                sum += p.Score;
                r.Parts.Add(p);
            }
            r.Score = r.Parts.Count > 0 ? sum / r.Parts.Count : 0f;
            r.Parts.Sort((a, b) => b.Cells.CompareTo(a.Cells));
            return r;
        }

        // ------------------------------------------------------------------
        // Mẫu + chấm điểm
        // ------------------------------------------------------------------

        /// <summary>
        /// Chuẩn bị mọi biến thể góc độ của một mẫu (bản gốc + xoay ±step..±maxAngle,
        /// + bản lật nếu mirror). Gọi một lần rồi cache.
        /// </summary>
        public static List<SketchShape> PrepareTemplate(ColorSketch s, string name,
                                                        float maxAngle, float step, bool mirror)
        {
            var result = new List<SketchShape>();
            if (s == null) return result;

            bool[] kept = KeptPaint(s);
            List<Point> boundary = Boundary(Silhouette(kept, s.Size), s.Size);
            if (boundary.Count < PointCloud.SamplingCount) return result;   // hình quá nhỏ
            List<Point> colored = ColoredPoints(s, kept);

            var shapes = PointTransform.Variants(boundary, maxAngle, step, mirror);
            var grids = PointTransform.Variants(colored, maxAngle, step, mirror);
            for (int i = 0; i < shapes.Count; i++)
                result.Add(new SketchShape
                {
                    Name = name + shapes[i].suffix,
                    Cloud = ToCloud(shapes[i].points, name + shapes[i].suffix),
                    Grid = Grid(grids[i].points)
                });
            return result;
        }

        /// <summary>Chấm bức vẽ người chơi với mọi biến thể của mẫu. null nếu chưa vẽ gì.</summary>
        public static SketchResult Score(ColorSketch player, IReadOnlyList<SketchShape> template,
                                         PDollarRecognizer recognizer, ColorTable colors,
                                         float maxDistance, float shapeWeight, float minCoverage,
                                         float minShape = 0f, float shapeCap = 0.5f)
        {
            if (player == null || template == null || template.Count == 0) return null;

            bool[] kept = KeptPaint(player);
            List<Point> boundary = Boundary(Silhouette(kept, player.Size), player.Size);
            if (boundary.Count < PointCloud.SamplingCount) return null;   // chưa vẽ gì / quá nhỏ

            PointCloud cloud = ToCloud(boundary, "drawn");
            int[] grid = Grid(ColoredPoints(player, kept));

            bool hasColor = false;
            foreach (int c in template[0].Grid) if (c >= 0) { hasColor = true; break; }

            SketchResult best = null;
            foreach (SketchShape v in template)
            {
                float d = recognizer.Match(cloud, v.Cloud);
                var r = new SketchResult
                {
                    Distance = d,
                    Shape = PDollarRecognizer.ScoreFromDistance(d, maxDistance),
                    HasColor = hasColor,
                    Variant = v.Name
                };
                if (hasColor)
                {
                    ColorCompareResult cc = Compare(grid, v.Grid, GridSize, colors, minCoverage);
                    r.Color = cc.Score;
                    r.Accuracy = cc.Accuracy;
                    r.Coverage = cc.Coverage;
                    r.Spill = cc.Spill;
                    r.Parts = cc.Parts;
                    r.Total = shapeWeight * r.Shape + (1f - shapeWeight) * r.Color;
                }
                else r.Total = r.Shape;

                if (best == null || r.Total > best.Total) best = r;
            }

            if (minShape > 0f && best.Shape < minShape && best.Total > shapeCap)
            {
                best.Total = shapeCap;
                best.Capped = true;
            }
            best.Report = BuildReport(best, colors, minShape, shapeCap);
            return best;
        }

        private static string BuildReport(SketchResult r, ColorTable colors, float minShape, float shapeCap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hình (đường bao): " + Pct(r.Shape) + "  — khớp nhất với " + r.Variant);
            if (!r.HasColor)
            {
                sb.AppendLine("Mẫu chưa có màu (vẽ lại mẫu có tô màu trong Template Recorder) → chỉ chấm hình");
            }
            else
            {
                sb.AppendLine("Màu: " + Pct(r.Color) + " = trung bình các phần (cả bức: đúng màu " +
                              Pct(r.Accuracy) + ", tô phủ " + Pct(r.Coverage) + ")");
                foreach (ColorPartResult p in r.Parts)
                {
                    string name = p.ColorId == ColorSketch.NoColor ? "(không rõ màu)" : colors.NameOf(p.ColorId);
                    sb.AppendLine("  phần " + name + ": " + Pct(p.Score) + " (đúng màu " + Pct(p.Accuracy) +
                                  ", tô phủ " + Pct(p.Coverage) + ", " + p.Cells + " ô)");
                }
                if (r.Spill > 0.05f) sb.AppendLine("  Tô lem ra ngoài: " + Pct(r.Spill) + " (không trừ điểm)");
            }
            if (r.Capped)
                sb.AppendLine("  Hình dưới ngưỡng tối thiểu " + Pct(minShape) + " → điểm bị chặn tối đa " + Pct(shapeCap));
            return sb.ToString();
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static string Pct(float v) => Math.Round(v * 100f) + "%";
    }
}
