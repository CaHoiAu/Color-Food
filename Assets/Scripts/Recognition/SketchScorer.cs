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
        /// <summary>Độ thon dài của bóng mẫu (không đổi khi xoay / lật).</summary>
        public float Elongation = 1f;
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
        /// <summary>Độ thon dài bức vẽ / của mẫu, và hệ số nhân vào điểm hình (1 = khớp).</summary>
        public float Elongation = 1f, TemplateElongation = 1f, ElongationFactor = 1f;
        public float Accuracy = 1f, Coverage = 1f, Spill;
        public bool HasColor, Capped;
        /// <summary>Bị chặn vì hình dưới minShape.</summary>
        public bool ShapeCapped;
        /// <summary>Bị chặn vì bức vẽ giống một nguyên liệu KHÁC rõ rệt hơn (xem Score).</summary>
        public bool RivalCapped;
        /// <summary>Nguyên liệu khác giống bức vẽ nhất (rỗng nếu không so) và điểm của nó.</summary>
        public string Rival = "";
        public float RivalTotal;
        /// <summary>Điểm nguyên liệu khác trừ điểm đề bài (âm = đề bài cao hơn), trước mọi mức chặn.</summary>
        public float RivalLead;
        public float RivalMargin;
        public string Variant = "";
        public List<ColorPartResult> Parts = new List<ColorPartResult>();
        public string Report = "";
    }

    /// <summary>
    /// Chấm điểm kiểu "ảnh màu" — KHÔNG phân biệt nét viền / nét tô, không phụ
    /// thuộc thứ tự vẽ, không cần luật bộ phận.
    ///
    ///   HÌNH: gộp mọi ô có màu → bỏ mảnh lạc nhỏ → đóng khe (vân sáp) → lấp lỗ
    ///         → lấy pixel đường bao ngoài → chọn CloudPoints (64) điểm trải đều
    ///         (farthest point sampling) → chuẩn hóa → so $P với đường bao của mẫu.
    ///         × hệ số ĐỘ THON DÀI (trục dài / trục ngắn của bóng): quả dài (ớt
    ///         chuông) và quả tròn (táo, cà chua) có độ thon dài khác hẳn nhau.
    ///         KHÔNG so với bản xoay nghiêng của mẫu — xoay làm quả dài nằm nghiêng
    ///         khớp được với quả tròn. Tư thế khác thì vẽ thành một mẫu riêng.
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
    /// Thử mọi biến thể của mẫu (bản gốc + bản lật), lấy tổng cao nhất.
    ///
    /// SO VỚI NGUYÊN LIỆU KHÁC (rivals): chỉ so với một mẫu thì hai nguyên liệu na
    /// ná nhau (cà chua / ớt chuông: cùng thân đỏ, lá xanh ở trên) vẫn đạt nhầm, vì
    /// điểm màu gần tối đa. Nên chấm thêm bức vẽ với mẫu của các nguyên liệu khác,
    /// CÙNG thông số của đề bài; nếu một nguyên liệu khác cao hơn đề bài quá
    /// rivalMargin thì bức vẽ "giống nó hơn" → tổng bị chặn ở rivalCap.
    /// rivalMargin = 0: khắt khe nhất — đề bài phải là nguyên liệu giống nhất.
    /// </summary>
    public static class SketchScorer
    {
        public const int GridSize = 24;
        /// <summary>
        /// Số điểm lấy trên đường bao. 64 thay vì 32 (như $P theo nét): đo trên mẫu ớt
        /// chuông / táo thật, khoảng cách táo→ớt so với độ lệch khi vẽ lại chính quả ớt
        /// tăng từ 1.11 lên 1.40 lần — tách hai quả na ná nhau tốt hơn. Đổi số này thì
        /// khoảng cách d nhỏ đi (≈ 0.7–0.9 lần): hiệu chỉnh lại sketchMaxDistance.
        /// Chi phí so $P tăng ≈ (64/32)^2.5 ≈ 5.7 lần — vẫn vài ms.
        /// </summary>
        public const int CloudPoints = 64;
        public const int CloseRadius = 2;
        public const float MinComponentShare = 0.03f;
        public const float CellMinFraction = 0.25f;
        /// <summary>Độ thon dài lệch mẫu tới tỉ lệ này: không phạt (nét tay tự nhiên).</summary>
        public const float ElongationTolerance = 1.2f;
        /// <summary>Lệch tới tỉ lệ này: điểm hình về 0 (giảm tuyến tính theo log ở giữa).</summary>
        public const float ElongationLimit = 1.6f;

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

        /// <summary>
        /// Độ thon dài của bóng = √(λ lớn / λ nhỏ) của ma trận hiệp phương sai các ô:
        /// tỉ lệ trục dài / trục ngắn theo HƯỚNG CHÍNH của hình — hình tròn ≈ 1, elip
        /// 2:1 ≈ 2. Không đổi khi xoay, lật hay co giãn đều.
        /// </summary>
        public static float Elongation(bool[] sil, int size)
        {
            double n = 0, sx = 0, sy = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (sil[y * size + x]) { n++; sx += x; sy += y; }
            if (n < 2) return 1f;
            double mx = sx / n, my = sy / n, cxx = 0, cyy = 0, cxy = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (!sil[y * size + x]) continue;
                    double dx = x - mx, dy = y - my;
                    cxx += dx * dx; cyy += dy * dy; cxy += dx * dy;
                }
            double half = (cxx + cyy) / 2, det = cxx * cyy - cxy * cxy;
            double root = Math.Sqrt(Math.Max(0, half * half - det));
            double big = half + root, small = half - root;
            return small <= 1e-9 ? 100f : (float)Math.Sqrt(big / small);
        }

        /// <summary>Hệ số nhân vào điểm hình: 1 khi độ thon dài lệch mẫu ≤ ElongationTolerance lần.</summary>
        public static float ElongationFactor(float player, float template)
        {
            if (player <= 0f || template <= 0f) return 1f;
            double e = Math.Abs(Math.Log(player / template));
            double lo = Math.Log(ElongationTolerance), hi = Math.Log(ElongationLimit);
            if (e <= lo) return 1f;
            return Clamp01((float)(1 - (e - lo) / (hi - lo)));
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

        /// <summary>Đám mây $P từ pixel đường bao: chọn CloudPoints điểm trải đều, co + dời về gốc.</summary>
        public static PointCloud ToCloud(IReadOnlyList<Point> boundary, string name)
        {
            Point[] sample = FarthestPointSample(boundary, CloudPoints);
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
        /// Chuẩn bị các biến thể của một mẫu: bản gốc + bản lật trái ↔ phải (nếu
        /// mirror). Không có bản xoay. Gọi một lần rồi cache.
        /// </summary>
        public static List<SketchShape> PrepareTemplate(ColorSketch s, string name, bool mirror)
        {
            var result = new List<SketchShape>();
            if (s == null) return result;

            bool[] kept = KeptPaint(s);
            bool[] sil = Silhouette(kept, s.Size);
            List<Point> boundary = Boundary(sil, s.Size);
            if (boundary.Count < CloudPoints) return result;   // hình quá nhỏ
            List<Point> colored = ColoredPoints(s, kept);
            float elongation = Elongation(sil, s.Size);

            var shapes = PointTransform.Variants(boundary, 0f, 0f, mirror);
            var grids = PointTransform.Variants(colored, 0f, 0f, mirror);
            for (int i = 0; i < shapes.Count; i++)
                result.Add(new SketchShape
                {
                    Name = name + shapes[i].suffix,
                    Cloud = ToCloud(shapes[i].points, name + shapes[i].suffix),
                    Grid = Grid(grids[i].points),
                    Elongation = elongation
                });
            return result;
        }

        /// <summary>Mẫu của một nguyên liệu khác, để so (xem Score).</summary>
        public struct Rival
        {
            public string Name;
            public IReadOnlyList<SketchShape> Shapes;
            public Rival(string name, IReadOnlyList<SketchShape> shapes) { Name = name; Shapes = shapes; }
        }

        public const float DefaultRivalMargin = 0.05f;

        /// <summary>
        /// Chấm bức vẽ người chơi với mọi biến thể của mẫu. null nếu chưa vẽ gì.
        /// rivals (tùy chọn): mẫu của các nguyên liệu khác — bức vẽ giống một nguyên
        /// liệu khác hơn đề bài quá rivalMargin thì tổng bị chặn ở rivalCap.
        /// </summary>
        public static SketchResult Score(ColorSketch player, IReadOnlyList<SketchShape> template,
                                         PDollarRecognizer recognizer, ColorTable colors,
                                         float maxDistance, float shapeWeight, float minCoverage,
                                         float minShape = 0f, float shapeCap = 0.5f,
                                         IReadOnlyList<Rival> rivals = null,
                                         float rivalMargin = DefaultRivalMargin, float rivalCap = 0.5f)
        {
            if (player == null || template == null || template.Count == 0) return null;

            bool[] kept = KeptPaint(player);
            bool[] sil = Silhouette(kept, player.Size);
            List<Point> boundary = Boundary(sil, player.Size);
            if (boundary.Count < CloudPoints) return null;   // chưa vẽ gì / quá nhỏ

            PointCloud cloud = ToCloud(boundary, "drawn");
            int[] grid = Grid(ColoredPoints(player, kept));
            float elongation = Elongation(sil, player.Size);

            SketchResult best = BestVariant(cloud, grid, elongation, template, recognizer, colors,
                                            maxDistance, shapeWeight, minCoverage);

            // So với nguyên liệu khác — cùng thông số của đề bài để điểm so được với nhau.
            // Đề bài có màu: chỉ so với nguyên liệu CŨNG có màu. Mẫu chỉ có nét (vd circle
            // viền đen) không đủ thông tin: so riêng hình thì đường bao tròn tròn "giống"
            // hầu hết mọi quả, trong khi màu — thứ phân biệt nguyên liệu — bị bỏ qua.
            // Đề bài không có màu: so điểm HÌNH với mọi nguyên liệu.
            float bestLead = float.MinValue;
            if (rivals != null)
                foreach (Rival rv in rivals)
                {
                    if (rv.Shapes == null || rv.Shapes.Count == 0) continue;
                    SketchResult r = BestVariant(cloud, grid, elongation, rv.Shapes, recognizer, colors,
                                                 maxDistance, shapeWeight, minCoverage);
                    if (best.HasColor && !r.HasColor) continue;
                    bool both = r.HasColor && best.HasColor;
                    float lead = both ? r.Total - best.Total : r.Shape - best.Shape;
                    if (lead > bestLead)
                    {
                        bestLead = lead;
                        best.RivalLead = lead;
                        best.Rival = rv.Name;
                        best.RivalTotal = both ? r.Total : r.Shape;
                    }
                }

            best.RivalMargin = rivalMargin;
            if (best.Rival.Length > 0 && bestLead > rivalMargin && best.Total > rivalCap)
            {
                best.Total = rivalCap;
                best.Capped = best.RivalCapped = true;
            }
            if (minShape > 0f && best.Shape < minShape && best.Total > shapeCap)
            {
                best.Total = shapeCap;
                best.Capped = best.ShapeCapped = true;
            }
            best.Report = BuildReport(best, colors, minShape, shapeCap, rivalCap);
            return best;
        }

        /// <summary>Biến thể có tổng cao nhất của một mẫu (chưa áp mức chặn nào).</summary>
        private static SketchResult BestVariant(PointCloud cloud, int[] grid, float elongation,
                                                IReadOnlyList<SketchShape> template,
                                                PDollarRecognizer recognizer, ColorTable colors,
                                                float maxDistance, float shapeWeight, float minCoverage)
        {
            bool hasColor = false;
            foreach (int c in template[0].Grid) if (c >= 0) { hasColor = true; break; }

            SketchResult best = null;
            foreach (SketchShape v in template)
            {
                float d = recognizer.Match(cloud, v.Cloud);
                float factor = ElongationFactor(elongation, v.Elongation);
                var r = new SketchResult
                {
                    Distance = d,
                    Shape = PDollarRecognizer.ScoreFromDistance(d, maxDistance) * factor,
                    Elongation = elongation,
                    TemplateElongation = v.Elongation,
                    ElongationFactor = factor,
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
            return best;
        }

        private static string BuildReport(SketchResult r, ColorTable colors, float minShape, float shapeCap,
                                          float rivalCap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hình (đường bao): " + Pct(r.Shape) + "  — khớp nhất với " + r.Variant);
            sb.AppendLine("  Độ thon dài: vẽ " + r.Elongation.ToString("0.00") + " / mẫu " +
                          r.TemplateElongation.ToString("0.00") +
                          (r.ElongationFactor < 1f
                              ? " → lệch quá " + Pct(ElongationTolerance - 1f) + ", điểm hình × " +
                                r.ElongationFactor.ToString("0.00")
                              : " → khớp"));
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
            if (r.Rival.Length > 0)
            {
                string verdict;
                if (r.RivalCapped)
                    verdict = "cao hơn đề bài " + Pct(r.RivalLead) + " (> " + Pct(r.RivalMargin) + ") → bức vẽ giống " +
                              r.Rival + " hơn, điểm bị chặn tối đa " + Pct(rivalCap);
                else if (r.RivalLead > 0f)
                    verdict = "cao hơn đề bài " + Pct(r.RivalLead) + " (≤ " + Pct(r.RivalMargin) +
                              ") → sát nút, vẫn tính là đề bài";
                else
                    verdict = "đề bài cao hơn " + Pct(-r.RivalLead) + " → không ảnh hưởng";
                sb.AppendLine("Nguyên liệu khác gần nhất: " + r.Rival + " " + Pct(r.RivalTotal) + " — " + verdict);
            }
            if (r.ShapeCapped)
                sb.AppendLine("  Hình dưới ngưỡng tối thiểu " + Pct(minShape) + " → điểm bị chặn tối đa " + Pct(shapeCap));
            return sb.ToString();
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static string Pct(float v) => Math.Round(v * 100f) + "%";
    }
}
