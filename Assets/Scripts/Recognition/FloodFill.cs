using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Tô vùng bằng thuật toán scanline (duyệt từng đoạn ngang), 4 hướng.
    ///
    /// KHÔNG dùng đệ quy: canvas 512×512 có thể có ~260.000 pixel, đệ quy
    /// từng pixel sẽ tràn stack. Dùng Stack tự quản lý + duyệt cả đoạn ngang
    /// mỗi lần nên nhanh hơn nhiều.
    ///
    /// 4 hướng (không đi chéo) để màu không rò qua khe chéo giữa hai nét mực.
    /// </summary>
    public static class FloodFill
    {
        /// <summary>
        /// Tìm vùng chứa điểm (sx, sy), bị chặn bởi barrier (true = vật cản).
        /// Trả về danh sách chỉ số pixel (y*w + x); rỗng nếu điểm bấm trúng mực.
        /// touchesBorder = true nghĩa là vùng chạm mép canvas = vùng HỞ (không khép kín).
        /// </summary>
        public static List<int> FindRegion(bool[] barrier, int w, int h, int sx, int sy,
                                           out bool touchesBorder)
        {
            var visited = new bool[w * h];
            var result = new List<int>();
            Flood(barrier, visited, w, h, sx, sy, result, out touchesBorder);
            return result;
        }

        /// <summary>Lõi dùng chung; visited được truyền vào để RegionAnalyzer gán nhãn nhiều vùng.</summary>
        internal static void Flood(bool[] barrier, bool[] visited, int w, int h,
                                   int sx, int sy, List<int> output, out bool touchesBorder)
        {
            touchesBorder = false;
            if (sx < 0 || sy < 0 || sx >= w || sy >= h) return;
            int seed = sy * w + sx;
            if (barrier[seed] || visited[seed]) return;

            var stack = new Stack<int>();
            stack.Push(seed);

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                if (barrier[p] || visited[p]) continue;

                int y = p / w;
                int x = p - y * w;
                int row = y * w;

                int left = x;
                while (left > 0 && !barrier[row + left - 1] && !visited[row + left - 1]) left--;
                int right = x;
                while (right < w - 1 && !barrier[row + right + 1] && !visited[row + right + 1]) right++;

                if (left == 0 || right == w - 1 || y == 0 || y == h - 1) touchesBorder = true;

                for (int i = left; i <= right; i++)
                {
                    visited[row + i] = true;
                    output.Add(row + i);
                }

                PushRuns(stack, barrier, visited, w, h, y - 1, left, right);
                PushRuns(stack, barrier, visited, w, h, y + 1, left, right);
            }
        }

        // Với mỗi đoạn trống liên tục ở hàng kế bên, chỉ cần đẩy một điểm đại diện
        private static void PushRuns(Stack<int> stack, bool[] barrier, bool[] visited,
                                     int w, int h, int y, int left, int right)
        {
            if (y < 0 || y >= h) return;
            int row = y * w;
            bool inRun = false;
            for (int i = left; i <= right; i++)
            {
                bool free = !barrier[row + i] && !visited[row + i];
                if (free && !inRun) { stack.Push(row + i); inRun = true; }
                else if (!free) inRun = false;
            }
        }
    }
}
