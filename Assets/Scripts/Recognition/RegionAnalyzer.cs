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
        public int MinX, MaxX, MinY, MaxY;
        public float CentroidX, CentroidY;
        public PartRole Role;
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

            AssignRoles(set);
            return set;
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
