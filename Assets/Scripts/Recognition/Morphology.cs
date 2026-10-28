using System;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Phép "mở" hình thái học (morphological opening) bằng đĩa tròn bán kính r:
    /// co (erode) rồi nở (dilate) lại. Mọi phần của hình HẸP hơn ~2r (gai, mũi nhọn,
    /// sợi mảnh) bị cắt mất, còn phần rộng (kể cả lượn sóng, lồi lõm thoải) giữ nguyên.
    ///
    /// Dùng bản đồ khoảng cách Euclid (EDT) nên chi phí O(số pixel), không phụ thuộc r.
    /// Thuần C#, không dùng UnityEngine.
    /// </summary>
    public static class Morphology
    {
        private const double Inf = 1e12;   // "chưa có đặc trưng": đủ lớn hơn mọi d² trong ảnh

        /// <summary>
        /// Bình phương khoảng cách từ mỗi pixel tới pixel "đặc trưng" gần nhất
        /// (isFeature = true). Thuật toán Felzenszwalb–Huttenlocher, chính xác.
        /// Nếu không có pixel đặc trưng nào, mọi giá trị là Inf.
        /// </summary>
        public static double[] SquaredDistance(bool[] isFeature, int w, int h)
        {
            var d = new double[w * h];
            var col = new double[Math.Max(w, h)];
            var res = new double[Math.Max(w, h)];
            var v = new int[Math.Max(w, h)];
            var z = new double[Math.Max(w, h) + 1];

            // Lượt 1: theo cột (khoảng cách 1 chiều tới pixel đặc trưng gần nhất cùng cột)
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) col[y] = isFeature[y * w + x] ? 0 : Inf;
                Transform1D(col, h, res, v, z);
                for (int y = 0; y < h; y++) d[y * w + x] = res[y];
            }
            // Lượt 2: theo hàng
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) col[x] = d[y * w + x];
                Transform1D(col, w, res, v, z);
                for (int x = 0; x < w; x++) d[y * w + x] = res[x];
            }
            return d;
        }

        private static void Transform1D(double[] f, int n, double[] outD, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0; z[0] = -1e30; z[1] = 1e30;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q; z[k] = s; z[k + 1] = 1e30;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                outD[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        /// <summary>
        /// Mở hình bằng đĩa bán kính r (đĩa gồm mọi ô lệch (dx,dy) có dx²+dy²≤r²).
        /// Ngoài vùng ảnh coi là nền — nên truyền mask đã có viền trống ≥ r+1 pixel.
        /// </summary>
        public static bool[] Open(bool[] mask, int w, int h, int r)
        {
            int n = w * h;
            long r2 = (long)r * r;

            // Co: pixel sống sót nếu KHÔNG có nền nào trong bán kính r
            var notMask = new bool[n];
            for (int i = 0; i < n; i++) notMask[i] = !mask[i];
            double[] toBackground = SquaredDistance(notMask, w, h);

            var eroded = new bool[n];
            for (int i = 0; i < n; i++) eroded[i] = mask[i] && toBackground[i] > r2;

            // Nở: pixel thuộc kết quả nếu có pixel đã co trong bán kính r
            double[] toEroded = SquaredDistance(eroded, w, h);
            var opened = new bool[n];
            for (int i = 0; i < n; i++) opened[i] = toEroded[i] <= r2;
            return opened;
        }
    }
}
