using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    public enum PartRole
    {
        /// <summary>Vùng không nằm trong vùng nào khác (thân, lòng trắng...).</summary>
        Outer,
        /// <summary>Vùng nằm bên trong một vùng khác (lòng đỏ, hạt...).</summary>
        Inner
    }

    public class Region
    {
        public int Id;
        public int Area;
        /// <summary>Số cạnh pixel giáp vật cản hoặc ngoài canvas — dùng tính Circularity.</summary>
        public int Perimeter;
        public int MinX, MaxX, MinY, MaxY;
        public float CentroidX, CentroidY;
        public PartRole Role;

        /// <summary>
        /// Độ "gai" của hình bao (0 = trơn, càng lớn càng nhiều gai nhọn): tỉ lệ diện tích
        /// bị cắt mất khi "mở" hình bằng một đĩa tròn nhỏ. Gai/mũi nhọn/sợi hẹp bị cắt,
        /// còn viền lượn sóng thoải (như lòng trắng trứng thật) gần như không mất gì.
        /// Tính trên hình bao ĐẶC của vùng (đã lấp lỗ do vùng Inner bên trong tạo ra).
        /// </summary>
        public float Spikiness;

        /// <summary>
        /// Độ tròn của đường bao: 4π·Area/Perimeter². Hình tròn hoàn hảo = 1,
        /// outline càng gồ ghề (nhiều chỗ lồi lõm) thì càng tụt xuống gần 0,
        /// vì chu vi tăng nhanh hơn diện tích rất nhiều khi có răng cưa.
        /// </summary>
        public float Circularity()
        {
            if (Perimeter <= 0) return 0f;
            float c = 4f * (float)Math.PI * Area / (Perimeter * Perimeter);
            return c > 1f ? 1f : c;   // discretize hóa pixel có thể vượt 1 chút ở hình rất nhỏ
        }
    }

    public class RegionSet
    {
        public int Width, Height;
        /// <summary>Nhãn vùng của từng pixel; -1 = mực / nền bên ngoài / lỗ quá nhỏ.</summary>
        public int[] Labels;
        public List<Region> Regions = new List<Region>();
        /// <summary>Tổng diện tích các vùng kín (không tính mực và nền ngoài).</summary>
        public int TotalArea;
    }

    /// <summary>
    /// Tìm các VÙNG KÍN trong hình vẽ. Đây là bước biến "bức tranh" thành
    /// "danh sách bộ phận" để áp luật (đếm lòng đỏ, đo diện tích, đọc màu).
    ///
    /// Cách làm: pixel không phải vật cản được gom thành các thành phần liên thông.
    /// Thành phần nào chạm mép canvas là nền bên ngoài; còn lại là vùng kín.
    /// </summary>
    public static class RegionAnalyzer
    {
        /// <param name="minArea">Vùng nhỏ hơn số pixel này bị bỏ (lỗ do hai nét chồng nhau).</param>
        public static RegionSet Analyze(bool[] barrier, int w, int h, int minArea = 40)
        {
            var set = new RegionSet { Width = w, Height = h, Labels = new int[w * h] };
            for (int i = 0; i < set.Labels.Length; i++) set.Labels[i] = -1;

            var visited = new bool[w * h];
            var pixels = new List<int>();

            for (int idx = 0; idx < w * h; idx++)
            {
                if (barrier[idx] || visited[idx]) continue;

                pixels.Clear();
                bool touches;
                FloodFill.Flood(barrier, visited, w, h, idx % w, idx / w, pixels, out touches);

                if (touches || pixels.Count < minArea) continue;

                var r = new Region { Id = set.Regions.Count, Area = pixels.Count,
                                     MinX = int.MaxValue, MinY = int.MaxValue,
                                     MaxX = int.MinValue, MaxY = int.MinValue };
                double sx = 0, sy = 0;
                foreach (int p in pixels)
                {
                    int y = p / w, x = p - y * w;
                    set.Labels[p] = r.Id;
                    if (x < r.MinX) r.MinX = x;
                    if (x > r.MaxX) r.MaxX = x;
                    if (y < r.MinY) r.MinY = y;
                    if (y > r.MaxY) r.MaxY = y;
                    sx += x; sy += y;
                }
                r.CentroidX = (float)(sx / pixels.Count);
                r.CentroidY = (float)(sy / pixels.Count);
                set.Regions.Add(r);
                set.TotalArea += r.Area;
            }

            // AssignRoles trước ComputePerimeters: ComputePerimeters cần biết
            // Outer/Inner để loại viền-lỗ-bên-trong (xem ghi chú dưới).
            AssignRoles(set);
            ComputePerimeters(barrier, set);
            ComputeSpikiness(set);
            return set;
        }

        /// <summary>Bán kính đĩa mở = phần này của bán kính tương đương √(S/π) (tối thiểu 3 pixel).</summary>
        public const float SpikeOpenFraction = 0.08f;
        private const int SpikePad = 30;

        /// <summary>
        /// Với mỗi vùng: dựng hình bao đặc (vùng + các lỗ bên trong), "mở" bằng đĩa
        /// bán kính SpikeOpenFraction·√(S/π), Spikiness = 1 − S_sau_mở / S.
        /// Chỉ làm việc trong khung bao của vùng (+ viền đệm) nên rẻ.
        /// </summary>
        private static void ComputeSpikiness(RegionSet set)
        {
            foreach (var r in set.Regions)
            {
                int bw = r.MaxX - r.MinX + 1 + 2 * SpikePad;
                int bh = r.MaxY - r.MinY + 1 + 2 * SpikePad;
                int ox = r.MinX - SpikePad, oy = r.MinY - SpikePad;

                // blocked = pixel của chính vùng này; còn lại (mực, vùng Inner, nền) đi qua được
                var blocked = new bool[bw * bh];
                for (int y = r.MinY; y <= r.MaxY; y++)
                    for (int x = r.MinX; x <= r.MaxX; x++)
                        if (set.Labels[y * set.Width + x] == r.Id)
                            blocked[(y - oy) * bw + (x - ox)] = true;

                // Lấp nền từ góc khung: những gì KHÔNG với tới được = hình bao đặc
                var outside = new bool[bw * bh];
                var stack = new System.Collections.Generic.Stack<int>();
                outside[0] = true; stack.Push(0);
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int y = idx / bw, x = idx - y * bw;
                    if (x > 0) Visit(blocked, outside, stack, idx - 1);
                    if (x < bw - 1) Visit(blocked, outside, stack, idx + 1);
                    if (y > 0) Visit(blocked, outside, stack, idx - bw);
                    if (y < bh - 1) Visit(blocked, outside, stack, idx + bw);
                }

                var solid = new bool[bw * bh];
                int area = 0;
                for (int i = 0; i < solid.Length; i++)
                {
                    solid[i] = !outside[i];
                    if (solid[i]) area++;
                }
                if (area == 0) { r.Spikiness = 0f; continue; }

                int radius = Math.Max(3, (int)Math.Round(SpikeOpenFraction * Math.Sqrt(area / Math.PI)));
                bool[] opened = Morphology.Open(solid, bw, bh, radius);
                int kept = 0;
                for (int i = 0; i < opened.Length; i++) if (opened[i] && solid[i]) kept++;
                r.Spikiness = 1f - kept / (float)area;
            }
        }

        private static void Visit(bool[] blocked, bool[] outside, System.Collections.Generic.Stack<int> stack, int idx)
        {
            if (blocked[idx] || outside[idx]) return;
            outside[idx] = true;
            stack.Push(idx);
        }

        /// <summary>
        /// Đếm chu vi TỪNG VÙNG — dùng cho Circularity() (kiểm tra outline tròn/méo).
        ///
        /// Vấn đề cần giải quyết: một vùng Outer (vd lòng trắng) CHỨA một vùng
        /// Inner (lòng đỏ) bên trong nó. Nếu đếm đơn giản "cạnh giáp nhãn khác =
        /// chu vi", thì chu vi của lòng trắng sẽ cộng CẢ viền quanh lỗ lòng đỏ ở
        /// giữa — biến lòng trắng thành một "cái nhẫn" (hình vành khuyên), mà
        /// vành khuyên vốn có chu vi/diện tích xấu hơn một hình đặc rất nhiều,
        /// kể cả khi outline bên ngoài vẽ hoàn toàn trơn tròn. Đã đo thực tế:
        /// trứng vẽ ĐÚNG HOÀN TOÀN cũng chỉ ra circularity lòng trắng ~0.18,
        /// còn trứng vẽ MÉO rõ rệt ra ~0.14 — gần nhau quá, không đủ để đặt
        /// ngưỡng phân biệt.
        ///
        /// Cách sửa: gom các pixel MỰC (nét vẽ) thành từng THÀNH PHẦN LIÊN THÔNG
        /// riêng (một nét khép kín = một thành phần). Với mỗi thành phần, xem nó
        /// có kề vùng Inner nào không. Khi tính chu vi cho một vùng R:
        ///   - Nếu cạnh giáp mực thuộc thành phần KHÔNG kề vùng Inner nào → đây
        ///     là viền thật của R (hoặc của R với nền ngoài) → TÍNH.
        ///   - Nếu cạnh giáp mực thuộc thành phần CÓ kề vùng Inner, VÀ R đang
        ///     xét là Outer → đây là viền LỖ bên trong (nét vẽ lòng đỏ, tình cờ
        ///     cũng kề lòng trắng) → KHÔNG tính vào chu vi của R.
        ///   - Vùng Inner (lòng đỏ) luôn được tính đầy đủ viền của riêng nó,
        ///     vì với BẢN THÂN nó, viền đó chính là outline thật, không phải lỗ.
        /// </summary>
        private static void ComputePerimeters(bool[] ink, RegionSet set)
        {
            int w = set.Width, h = set.Height, n = w * h;

            var compId = new int[n];
            for (int i = 0; i < n; i++) compId[i] = -1;
            var compHasInnerNeighbor = new List<bool>();
            var stack = new Stack<int>();

            for (int start = 0; start < n; start++)
            {
                if (!ink[start] || compId[start] >= 0) continue;

                int cid = compHasInnerNeighbor.Count;
                bool hasInner = false;
                compId[start] = cid;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int y = idx / w, x = idx - y * w;

                    if (x > 0) hasInner |= TouchesInner(set, idx - 1);
                    if (x < w - 1) hasInner |= TouchesInner(set, idx + 1);
                    if (y > 0) hasInner |= TouchesInner(set, idx - w);
                    if (y < h - 1) hasInner |= TouchesInner(set, idx + w);

                    PushInkNeighbor(ink, compId, cid, stack, x > 0, idx - 1);
                    PushInkNeighbor(ink, compId, cid, stack, x < w - 1, idx + 1);
                    PushInkNeighbor(ink, compId, cid, stack, y > 0, idx - w);
                    PushInkNeighbor(ink, compId, cid, stack, y < h - 1, idx + w);
                }
                compHasInnerNeighbor.Add(hasInner);
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int lab = set.Labels[idx];
                    if (lab < 0) continue;
                    var r = set.Regions[lab];

                    if (ShouldCountEdge(ink, compId, compHasInnerNeighbor, set, r, x > 0, idx - 1)) r.Perimeter++;
                    if (ShouldCountEdge(ink, compId, compHasInnerNeighbor, set, r, x < w - 1, idx + 1)) r.Perimeter++;
                    if (ShouldCountEdge(ink, compId, compHasInnerNeighbor, set, r, y > 0, idx - w)) r.Perimeter++;
                    if (ShouldCountEdge(ink, compId, compHasInnerNeighbor, set, r, y < h - 1, idx + w)) r.Perimeter++;
                }
            }
        }

        private static bool TouchesInner(RegionSet set, int neighborIdx)
        {
            int lab = set.Labels[neighborIdx];
            return lab >= 0 && set.Regions[lab].Role == PartRole.Inner;
        }

        private static void PushInkNeighbor(bool[] ink, int[] compId, int cid, Stack<int> stack,
                                            bool inBounds, int neighborIdx)
        {
            if (!inBounds || !ink[neighborIdx] || compId[neighborIdx] >= 0) return;
            compId[neighborIdx] = cid;
            stack.Push(neighborIdx);
        }

        /// <param name="inBounds">False nếu neighborIdx đã ra ngoài canvas.</param>
        private static bool ShouldCountEdge(bool[] ink, int[] compId, List<bool> compHasInnerNeighbor,
                                            RegionSet set, Region r, bool inBounds, int neighborIdx)
        {
            if (!inBounds) return true;   // ra ngoài canvas — luôn là viền thật

            if (ink[neighborIdx])
            {
                bool hasInner = compHasInnerNeighbor[compId[neighborIdx]];
                // Mực kề một vùng Inner, và R đang xét là Outer → viền lỗ bên
                // trong, không tính. Mọi trường hợp khác (R là Inner, hoặc mực
                // không kề Inner nào) → viền thật của R.
                return !(hasInner && r.Role == PartRole.Outer);
            }

            // Không phải mực nhưng khác nhãn (nền ngoài bị bỏ/lỗ quá nhỏ/vùng
            // khác không lồng nhau) — hiếm gặp, giữ hành vi cũ: luôn tính.
            return set.Labels[neighborIdx] != r.Id;
        }

        /// <summary>
        /// Vùng R là Inner nếu khung bao của nó nằm gọn trong khung bao của một
        /// vùng Q lớn hơn. (Xấp xỉ bằng khung bao: đủ cho bài toán nguyên liệu,
        /// sẽ sai với hình chữ C ôm lấy một vùng khác — hiếm gặp, ghi vào mục hạn chế.)
        /// </summary>
        private static void AssignRoles(RegionSet set)
        {
            foreach (var r in set.Regions)
            {
                r.Role = PartRole.Outer;
                foreach (var q in set.Regions)
                {
                    if (q.Id == r.Id || q.Area <= r.Area) continue;
                    if (r.MinX >= q.MinX && r.MaxX <= q.MaxX && r.MinY >= q.MinY && r.MaxY <= q.MaxY)
                    {
                        r.Role = PartRole.Inner;
                        break;
                    }
                }
            }
        }
    }
}
