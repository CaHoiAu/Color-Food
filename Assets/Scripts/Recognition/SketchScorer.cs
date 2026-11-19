using System;
using System.Collections.Generic;
using System.Text;

namespace ColorFood.Recognition
{
    /// <summary>Một biến thể của mẫu (bản gốc / bản lật): đặc trưng hình thân, chi tiết, lưới màu.</summary>
    public class SketchShape
    {
        public string Name;
        /// <summary>Đường bao thân, ≤ BoundaryPoints điểm trải đều, đã chuẩn hóa (cho Chamfer).</summary>
        public Point[] Boundary;
        /// <summary>Bóng thân, MaskSize×MaskSize, chuẩn hóa theo trọng tâm + diện tích (cho IoU).</summary>
        public bool[] Mask;
        public int[] Grid;
        /// <summary>Độ thon dài của bóng mẫu (không đổi khi lật).</summary>
        public float Elongation = 1f;
        /// <summary>Hình riêng của từng phần màu không phải thân (cuống, lá…).</summary>
        public List<DetailShape> Details = new List<DetailShape>();
    }

    /// <summary>Hình của một phần chi tiết trong mẫu: các ô màu ColorId, chuẩn hóa riêng.</summary>
    public class DetailShape
    {
        public int ColorId;
        public bool[] Mask;
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

    /// <summary>Điểm hình của một phần chi tiết.</summary>
    public class DetailResult
    {
        public int ColorId;
        /// <summary>1 − IoU giữa hình phần đó của bức vẽ và của mẫu (0 = trùng).</summary>
        public float Distance;
        public float Score;
        /// <summary>Bức vẽ không có phần màu này.</summary>
        public bool Missing;
    }

    /// <summary>Thông số chấm (lấy từ IngredientTemplate.ToSketchParams).</summary>
    public class SketchParams
    {
        /// <summary>Khoảng cách hình thân D ứng với điểm hình = 0.</summary>
        public float ShapeMaxDistance = 0.8f;
        /// <summary>1 − IoU của một phần chi tiết ứng với điểm chi tiết = 0.</summary>
        public float DetailMaxDistance = 0.6f;
        public float ShapeWeight = 0.4f;
        public float DetailWeight = 0.2f;
        public float MinCoverage = 0.6f;
        public float MinShape = 0f;
        public float ShapeCap = 0.5f;
        public float RivalMargin = 0.05f;
    }

    public class SketchResult
    {
        /// <summary>Khoảng cách hình thân D = ½·chamfer/ChamferScale + ½·(1−IoU)/IouScale.</summary>
        public float Distance;
        public float Chamfer, IouDistance;
        public float Shape, Color = 1f, Total;
        /// <summary>Điểm chi tiết = trung bình các phần chi tiết; 1 nếu mẫu không có chi tiết.</summary>
        public float Detail = 1f;
        public List<DetailResult> DetailParts = new List<DetailResult>();
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
    ///   HÌNH THÂN: gộp mọi ô có màu → bỏ mảnh lạc nhỏ → đóng khe (vân sáp) → lấp lỗ
    ///         thành bóng đặc, rồi so với bóng của mẫu bằng HAI thước đo bổ sung nhau:
    ///           • Chamfer p95 giữa hai đường bao (chuẩn hóa theo khung bao): chỗ lệch
    ///             XA NHẤT (bỏ 5% điểm nhiễu) — bắt chi tiết cục bộ như múi đáy, góc vai.
    ///           • 1 − IoU giữa hai bóng (chuẩn hóa theo trọng tâm + diện tích): phần
    ///             diện tích không trùng — bắt khác biệt khối lớn (tròn / vuông, cao / dẹt).
    ///         D = ½·chamfer/ChamferScale + ½·(1−IoU)/IouScale;
    ///         hình = (1 − D / ShapeMaxDistance) × hệ số ĐỘ THON DÀI.
    ///         Đo trên mẫu ớt chuông / táo thật: tách tốt hơn $P (đã dùng trước đây).
    ///   HÌNH CHI TIẾT: mỗi phần màu KHÔNG PHẢI THÂN của mẫu (thân = màu nhiều ô nhất;
    ///         chi tiết = cuống, lá…) được so riêng: lấy các ô cùng màu (hoặc gần màu)
    ///         của bức vẽ, chuẩn hóa riêng phần đó, so bằng 1 − IoU. Quả có thân na ná
    ///         nhau (táo / ớt chuông / cà chua) khác nhau rõ nhất ở chỗ này.
    ///   MÀU:  co bức vẽ (theo khung bao các ô có màu) về lưới GridSize×GridSize,
    ///         mỗi ô một màu → so từng ô với lưới của MẪU TÔ SẴN cùng vị trí (cho
    ///         lệch 1 ô). Mỗi màu của mẫu là một phần, trọng số NGANG NHAU.
    ///
    ///   tổng = ShapeWeight × hình + DetailWeight × chi tiết + phần còn lại × màu
    ///          (mẫu không có chi tiết: DetailWeight dồn sang màu; mẫu không có màu: tổng = hình)
    /// Thử mọi biến thể của mẫu (bản gốc + bản lật), lấy tổng cao nhất. Không có bản
    /// xoay — xoay làm quả dài nằm nghiêng khớp được với quả tròn.
    ///
    /// SO VỚI NGUYÊN LIỆU KHÁC (rivals): chấm thêm bức vẽ với mẫu các nguyên liệu khác,
    /// CÙNG thông số của đề bài; nếu một nguyên liệu khác cao hơn đề bài quá
    /// RivalMargin thì bức vẽ "giống nó hơn" → tổng bị chặn ở ShapeCap.
    /// RivalMargin = 0: khắt khe nhất — đề bài phải là nguyên liệu giống nhất.
    /// </summary>
    public static class SketchScorer
    {
        public const int GridSize = 24;
        /// <summary>Số điểm đường bao tối thiểu (dưới mức này coi như chưa vẽ / quá nhỏ).</summary>
        public const int MinBoundary = 64;
        /// <summary>Số điểm đường bao dùng cho Chamfer (lấy trải đều).</summary>
        public const int BoundaryPoints = 256;
        /// <summary>Kích thước mặt nạ bóng / chi tiết dùng cho IoU.</summary>
        public const int MaskSize = 64;
        /// <summary>Khung mặt nạ, tính theo căn bậc hai diện tích: thân gọn → 2.6, chi tiết dài (lá) → 4.</summary>
        public const float BodySpan = 2.6f, DetailSpan = 4f;
        /// <summary>Thang chuẩn hóa hai thước đo hình về cùng cỡ (trị điển hình khi vẽ sai quả).</summary>
        public const float ChamferScale = 0.2f, IouScale = 0.4f;
        /// <summary>Một màu là "chi tiết" khi có ≥ tỉ lệ này số ô có màu của mẫu (và ≥ 4 ô).</summary>
        public const float DetailMinShare = 0.01f;
        /// <summary>Ô của bức vẽ được tính vào một phần chi tiết khi màu giống màu phần đó ≥ mức này.</summary>
        public const float DetailColorSimilarity = 0.5f;
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

        /// <summary>Bán kính đóng khe: 2 ô ở lưới 128 (~8 pixel canvas 512), tăng theo kích thước lưới.</summary>
        public static int CloseRadiusFor(int size) => Math.Max(1, (int)Math.Round(2f * size / 128f));

        /// <summary>Bóng đặc: đóng khe hẹp rồi lấp mọi lỗ không thông ra mép.</summary>
        public static bool[] Silhouette(bool[] kept, int size)
        {
            bool[] closed = Morphology.Close(kept, size, size, CloseRadiusFor(size));

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

        /// <summary>Các ô true của mặt nạ, mỗi ô một điểm (tâm ô).</summary>
        public static List<Point> Cells(bool[] mask, int size)
        {
            var pts = new List<Point>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (mask[y * size + x]) pts.Add(new Point(x + 0.5f, y + 0.5f, 0));
            return pts;
        }

        /// <summary>Co đường bao theo cạnh lớn nhất của khung bao, dời trọng tâm về 0.</summary>
        public static Point[] NormalizeBoundary(IReadOnlyList<Point> pts)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Point p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float size = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-6f);
            var q = new Point[pts.Count];
            double cx = 0, cy = 0;
            for (int i = 0; i < q.Length; i++)
            {
                q[i] = new Point((pts[i].X - minX) / size, (pts[i].Y - minY) / size, 0);
                cx += q[i].X; cy += q[i].Y;
            }
            cx /= Math.Max(1, q.Length); cy /= Math.Max(1, q.Length);
            for (int i = 0; i < q.Length; i++) q[i] = new Point((float)(q[i].X - cx), (float)(q[i].Y - cy), 0);
            return q;
        }

        /// <summary>
        /// Mặt nạ MaskSize×MaskSize của một tập ô, chuẩn hóa theo trọng tâm và căn bậc hai
        /// diện tích (không phụ thuộc vị trí và cỡ). Mỗi ô rải 4 điểm con để mặt nạ liền.
        /// span: khung mặt nạ tính theo √diện tích (hình dài cần span lớn hơn).
        /// </summary>
        public static bool[] ShapeMask(IReadOnlyList<Point> cells, float span)
        {
            var mask = new bool[MaskSize * MaskSize];
            if (cells.Count == 0) return mask;
            double cx = 0, cy = 0;
            foreach (Point p in cells) { cx += p.X; cy += p.Y; }
            cx /= cells.Count; cy /= cells.Count;
            double scale = Math.Sqrt(cells.Count) * span;
            foreach (Point p in cells)
                for (int sx = 0; sx < 2; sx++)
                    for (int sy = 0; sy < 2; sy++)
                    {
                        double ox = sx == 0 ? -0.25 : 0.25, oy = sy == 0 ? -0.25 : 0.25;
                        int gx = (int)Math.Floor(((p.X + ox - cx) / scale + 0.5) * MaskSize);
                        int gy = (int)Math.Floor(((p.Y + oy - cy) / scale + 0.5) * MaskSize);
                        if (gx >= 0 && gy >= 0 && gx < MaskSize && gy < MaskSize) mask[gy * MaskSize + gx] = true;
                    }
            return mask;
        }

        /// <summary>1 − IoU (giao / hợp) của hai mặt nạ: 0 = trùng khớp, 1 = không chồng chút nào.</summary>
        public static float IouDistance(bool[] a, bool[] b)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] && b[i]) inter++;
                if (a[i] || b[i]) union++;
            }
            return union == 0 ? 1f : 1f - inter / (float)union;
        }

        /// <summary>
        /// Chamfer p95 đối xứng: với mỗi điểm của A, khoảng cách tới điểm gần nhất của B;
        /// lấy mức 95% (bỏ 5% điểm lệch nhất do nhiễu); làm cả hai chiều, lấy giá trị lớn hơn.
        /// </summary>
        public static float Chamfer95(Point[] a, Point[] b)
        {
            if (a.Length == 0 || b.Length == 0) return float.MaxValue;
            var da = new float[a.Length];
            var db = new float[b.Length];
            for (int j = 0; j < b.Length; j++) db[j] = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < b.Length; j++)
                {
                    float d = Point.SqrDistance(a[i], b[j]);
                    if (d < best) best = d;
                    if (d < db[j]) db[j] = d;
                }
                da[i] = best;
            }
            return (float)Math.Sqrt(Math.Max(Percentile95(da), Percentile95(db)));
        }

        private static float Percentile95(float[] v)
        {
            Array.Sort(v);
            return v[Math.Max(0, (int)Math.Ceiling(0.95 * v.Length) - 1)];
        }

        /// <summary>Gộp hai thước đo hình thân về một khoảng cách D (xem tài liệu lớp).</summary>
        public static float ShapeDistance(float chamfer, float iouDistance)
            => 0.5f * chamfer / ChamferScale + 0.5f * iouDistance / IouScale;

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

        /// <summary>Đặc trưng đã tách của một bức vẽ / mẫu (trước khi lật).</summary>
        private class Features
        {
            public List<Point> SilhouetteCells, Boundary, Colored;
            public float Elongation;
        }

        private static Features Extract(ColorSketch s)
        {
            bool[] kept = KeptPaint(s);
            bool[] sil = Silhouette(kept, s.Size);
            return new Features
            {
                SilhouetteCells = Cells(sil, s.Size),
                Boundary = Boundary(sil, s.Size),
                Colored = ColoredPoints(s, kept),
                Elongation = Elongation(sil, s.Size)
            };
        }

        /// <summary>
        /// Chuẩn bị các biến thể của một mẫu: bản gốc + bản lật trái ↔ phải (nếu
        /// mirror). Không có bản xoay. Gọi một lần rồi cache.
        /// Thân = màu có nhiều ô nhất; chi tiết = các màu khác có ≥ DetailMinShare số ô.
        /// </summary>
        public static List<SketchShape> PrepareTemplate(ColorSketch s, string name, bool mirror)
        {
            var result = new List<SketchShape>();
            if (s == null) return result;

            Features f = Extract(s);
            if (f.Boundary.Count < MinBoundary) return result;   // hình quá nhỏ

            // Ô theo từng màu → thân + các phần chi tiết
            var byColor = new Dictionary<int, List<Point>>();
            foreach (Point p in f.Colored)
            {
                if (p.ColorID < 0) continue;
                if (!byColor.TryGetValue(p.ColorID, out List<Point> list)) byColor[p.ColorID] = list = new List<Point>();
                list.Add(p);
            }
            int body = -1, total = 0;
            foreach (var kv in byColor)
            {
                total += kv.Value.Count;
                if (body < 0 || kv.Value.Count > byColor[body].Count) body = kv.Key;
            }
            var details = new List<int>();
            foreach (var kv in byColor)
                if (kv.Key != body && kv.Value.Count >= Math.Max(4, DetailMinShare * total)) details.Add(kv.Key);
            details.Sort();

            for (int m = 0; m < (mirror ? 2 : 1); m++)
            {
                bool flip = m == 1;
                var v = new SketchShape
                {
                    Name = name + (flip ? " lật" : ""),
                    Boundary = NormalizeBoundary(FarthestPointSample(Flip(f.Boundary, flip), BoundaryPoints)),
                    Mask = ShapeMask(Flip(f.SilhouetteCells, flip), BodySpan),
                    Grid = Grid(Flip(f.Colored, flip)),
                    Elongation = f.Elongation
                };
                foreach (int c in details)
                    v.Details.Add(new DetailShape { ColorId = c, Mask = ShapeMask(Flip(byColor[c], flip), DetailSpan) });
                result.Add(v);
            }
            return result;
        }

        /// <summary>Lật trái ↔ phải quanh trọng tâm của chính tập điểm (mỗi đặc trưng tự chuẩn hóa vị trí).</summary>
        private static IReadOnlyList<Point> Flip(List<Point> pts, bool flip)
            => flip && pts.Count > 0 ? (IReadOnlyList<Point>)PointTransform.MirrorX(pts) : pts;

        /// <summary>Mẫu của một nguyên liệu khác, để so (xem Score).</summary>
        public struct Rival
        {
            public string Name;
            public IReadOnlyList<SketchShape> Shapes;
            public Rival(string name, IReadOnlyList<SketchShape> shapes) { Name = name; Shapes = shapes; }
        }

        public const float DefaultRivalMargin = 0.05f;

        /// <summary>Đặc trưng bức vẽ người chơi, tính một lần cho mọi mẫu được so.</summary>
        private class PlayerData
        {
            public Features F;
            public Point[] Boundary;
            public bool[] Mask;
            public int[] Grid;
            public Dictionary<int, (bool[] mask, int count)> DetailMasks = new Dictionary<int, (bool[], int)>();
        }

        /// <summary>
        /// Chấm bức vẽ người chơi với mọi biến thể của mẫu. null nếu chưa vẽ gì.
        /// rivals (tùy chọn): mẫu của các nguyên liệu khác — bức vẽ giống một nguyên
        /// liệu khác hơn đề bài quá RivalMargin thì tổng bị chặn ở ShapeCap.
        /// </summary>
        public static SketchResult Score(ColorSketch player, IReadOnlyList<SketchShape> template,
                                         ColorTable colors, SketchParams p,
                                         IReadOnlyList<Rival> rivals = null)
        {
            if (player == null || template == null || template.Count == 0) return null;
            if (p == null) p = new SketchParams();

            Features f = Extract(player);
            if (f.Boundary.Count < MinBoundary) return null;   // chưa vẽ gì / quá nhỏ

            var pd = new PlayerData
            {
                F = f,
                Boundary = NormalizeBoundary(FarthestPointSample(f.Boundary, BoundaryPoints)),
                Mask = ShapeMask(f.SilhouetteCells, BodySpan),
                Grid = Grid(f.Colored)
            };

            SketchResult best = BestVariant(pd, template, colors, p);

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
                    SketchResult r = BestVariant(pd, rv.Shapes, colors, p);
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

            best.RivalMargin = p.RivalMargin;
            if (best.Rival.Length > 0 && bestLead > p.RivalMargin && best.Total > p.ShapeCap)
            {
                best.Total = p.ShapeCap;
                best.Capped = best.RivalCapped = true;
            }
            if (p.MinShape > 0f && best.Shape < p.MinShape && best.Total > p.ShapeCap)
            {
                best.Total = p.ShapeCap;
                best.Capped = best.ShapeCapped = true;
            }
            best.Report = BuildReport(best, colors, p);
            return best;
        }

        /// <summary>Mặt nạ phần chi tiết màu c của bức vẽ: các ô có màu giống c đủ mức (cache theo màu).</summary>
        private static (bool[] mask, int count) PlayerDetail(PlayerData pd, int c, ColorTable colors)
        {
            if (pd.DetailMasks.TryGetValue(c, out var cached)) return cached;
            var cells = new List<Point>();
            foreach (Point q in pd.F.Colored)
                if (q.ColorID >= 0 && colors.Similarity(q.ColorID, c) >= DetailColorSimilarity) cells.Add(q);
            var result = (ShapeMask(cells, DetailSpan), cells.Count);
            pd.DetailMasks[c] = result;
            return result;
        }

        /// <summary>Biến thể có tổng cao nhất của một mẫu (chưa áp mức chặn nào).</summary>
        private static SketchResult BestVariant(PlayerData pd, IReadOnlyList<SketchShape> template,
                                                ColorTable colors, SketchParams p)
        {
            bool hasColor = false;
            foreach (int c in template[0].Grid) if (c >= 0) { hasColor = true; break; }

            SketchResult best = null;
            foreach (SketchShape v in template)
            {
                float chamfer = Chamfer95(pd.Boundary, v.Boundary);
                float iou = IouDistance(pd.Mask, v.Mask);
                float d = ShapeDistance(chamfer, iou);
                float factor = ElongationFactor(pd.F.Elongation, v.Elongation);
                var r = new SketchResult
                {
                    Distance = d,
                    Chamfer = chamfer,
                    IouDistance = iou,
                    Shape = Clamp01(1f - d / p.ShapeMaxDistance) * factor,
                    Elongation = pd.F.Elongation,
                    TemplateElongation = v.Elongation,
                    ElongationFactor = factor,
                    HasColor = hasColor,
                    Variant = v.Name
                };

                // Hình từng phần chi tiết
                float detailSum = 0f;
                foreach (DetailShape ds in v.Details)
                {
                    var (mask, count) = PlayerDetail(pd, ds.ColorId, colors);
                    var dr = new DetailResult { ColorId = ds.ColorId, Missing = count < 3 };
                    dr.Distance = dr.Missing ? 1f : IouDistance(mask, ds.Mask);
                    dr.Score = dr.Missing ? 0f : Clamp01(1f - dr.Distance / p.DetailMaxDistance);
                    detailSum += dr.Score;
                    r.DetailParts.Add(dr);
                }
                r.Detail = r.DetailParts.Count > 0 ? detailSum / r.DetailParts.Count : 1f;

                if (hasColor)
                {
                    ColorCompareResult cc = Compare(pd.Grid, v.Grid, GridSize, colors, p.MinCoverage);
                    r.Color = cc.Score;
                    r.Accuracy = cc.Accuracy;
                    r.Coverage = cc.Coverage;
                    r.Spill = cc.Spill;
                    r.Parts = cc.Parts;
                    float wd = r.DetailParts.Count > 0 ? p.DetailWeight : 0f;
                    float wc = Math.Max(0f, 1f - p.ShapeWeight - wd);
                    r.Total = p.ShapeWeight * r.Shape + wd * r.Detail + wc * r.Color;
                }
                else r.Total = r.Shape;

                if (best == null || r.Total > best.Total) best = r;
            }
            return best;
        }

        private static string BuildReport(SketchResult r, ColorTable colors, SketchParams p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hình thân: " + Pct(r.Shape) + "  — khớp nhất với " + r.Variant +
                          " (D = " + r.Distance.ToString("0.000") + ": chamfer " + r.Chamfer.ToString("0.000") +
                          ", lệch diện tích " + Pct(r.IouDistance) + ")");
            sb.AppendLine("  Độ thon dài: vẽ " + r.Elongation.ToString("0.00") + " / mẫu " +
                          r.TemplateElongation.ToString("0.00") +
                          (r.ElongationFactor < 1f
                              ? " → lệch quá " + Pct(ElongationTolerance - 1f) + ", điểm hình × " +
                                r.ElongationFactor.ToString("0.00")
                              : " → khớp"));
            if (r.DetailParts.Count > 0)
            {
                sb.AppendLine("Hình chi tiết: " + Pct(r.Detail));
                foreach (DetailResult d in r.DetailParts)
                    sb.AppendLine("  phần " + colors.NameOf(d.ColorId) + ": " + Pct(d.Score) +
                                  (d.Missing ? " (không vẽ phần này)" : " (lệch hình " + Pct(d.Distance) + ")"));
            }
            if (!r.HasColor)
            {
                sb.AppendLine("Mẫu chưa có màu (vẽ lại mẫu có tô màu trong Template Recorder) → chỉ chấm hình");
            }
            else
            {
                sb.AppendLine("Màu: " + Pct(r.Color) + " = trung bình các phần (cả bức: đúng màu " +
                              Pct(r.Accuracy) + ", tô phủ " + Pct(r.Coverage) + ")");
                foreach (ColorPartResult part in r.Parts)
                {
                    string name = part.ColorId == ColorSketch.NoColor ? "(không rõ màu)" : colors.NameOf(part.ColorId);
                    sb.AppendLine("  phần " + name + ": " + Pct(part.Score) + " (đúng màu " + Pct(part.Accuracy) +
                                  ", tô phủ " + Pct(part.Coverage) + ", " + part.Cells + " ô)");
                }
                if (r.Spill > 0.05f) sb.AppendLine("  Tô lem ra ngoài: " + Pct(r.Spill) + " (không trừ điểm)");
            }
            if (r.Rival.Length > 0)
            {
                string verdict;
                if (r.RivalCapped)
                    verdict = "cao hơn đề bài " + Pct(r.RivalLead) + " (> " + Pct(r.RivalMargin) + ") → bức vẽ giống " +
                              r.Rival + " hơn, điểm bị chặn tối đa " + Pct(p.ShapeCap);
                else if (r.RivalLead > 0f)
                    verdict = "cao hơn đề bài " + Pct(r.RivalLead) + " (≤ " + Pct(r.RivalMargin) +
                              ") → sát nút, vẫn tính là đề bài";
                else
                    verdict = "đề bài cao hơn " + Pct(-r.RivalLead) + " → không ảnh hưởng";
                sb.AppendLine("Nguyên liệu khác gần nhất: " + r.Rival + " " + Pct(r.RivalTotal) + " — " + verdict);
            }
            if (r.ShapeCapped)
                sb.AppendLine("  Hình dưới ngưỡng tối thiểu " + Pct(p.MinShape) + " → điểm bị chặn tối đa " + Pct(p.ShapeCap));
            return sb.ToString();
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static string Pct(float v) => Math.Round(v * 100f) + "%";
    }
}
