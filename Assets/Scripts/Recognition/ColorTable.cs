using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>Màu RGB 0..255. Thuần C#, không dùng UnityEngine.Color.</summary>
    [Serializable]
    public struct Rgb
    {
        public byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
    }

    /// <summary>H: 0..360 độ, S và V: 0..1.</summary>
    public struct Hsv
    {
        public float H, S, V;
        public Hsv(float h, float s, float v) { H = h; S = s; V = v; }

        public static Hsv FromRgb(Rgb c)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            float h = 0f;
            if (delta > 1e-6f)
            {
                if (max == r)      h = 60f * (((g - b) / delta) % 6f);
                else if (max == g) h = 60f * (((b - r) / delta) + 2f);
                else               h = 60f * (((r - g) / delta) + 4f);
            }
            if (h < 0f) h += 360f;

            float s = max <= 1e-6f ? 0f : delta / max;
            return new Hsv(h, s, max);
        }
    }

    /// <summary>
    /// Bảng màu cố định + ma trận tương đồng tính SẴN.
    ///
    /// Vì bảng màu cố định (12–16 màu), việc "màu người chơi tô có giống màu
    /// mục tiêu không" chỉ là tra bảng [màu đã tô][màu mục tiêu] — không cần
    /// histogram hay phân tích ảnh lúc chơi. Mỗi điểm/vùng đã mang sẵn ColorID.
    ///
    /// Khoảng cách màu (0..1) = wH·Δhue + wS·ΔS + wV·ΔV, trong đó Δhue tính
    /// TRÊN VÒNG TRÒN (350° và 10° chỉ cách nhau 20°, không phải 340°).
    /// Màu gần như không có sắc (trắng, xám, đen) thì hue vô nghĩa, nên trọng số
    /// hue giảm dần về 0 theo độ bão hòa của màu nhạt hơn trong cặp.
    /// </summary>
    public class ColorTable
    {
        public const float WeightHue = 0.60f;
        public const float WeightSat = 0.25f;
        public const float WeightVal = 0.15f;
        /// <summary>Bão hòa từ mức này trở lên thì hue được tính đầy đủ.</summary>
        public const float ChromaRamp = 0.30f;

        private readonly string[] _names;
        private readonly Hsv[] _hsv;
        private readonly float[,] _distance;
        private readonly float _tolerance;

        public int Count => _hsv.Length;

        /// <param name="tolerance">
        /// Khoảng cách màu ứng với độ giống = 0. Nhỏ = khắt khe.
        /// Đỏ vs đỏ đậm ≈ 0.06; đỏ vs cam ≈ 0.11; đỏ vs vàng ≈ 0.19 (xem test in bảng).
        /// </param>
        public ColorTable(IReadOnlyList<string> names, IReadOnlyList<Rgb> colors, float tolerance = 0.25f)
        {
            if (names == null || colors == null || names.Count != colors.Count || colors.Count == 0)
                throw new ArgumentException("names và colors phải cùng độ dài và không rỗng.");

            _tolerance = tolerance;
            int n = colors.Count;
            _names = new string[n];
            _hsv = new Hsv[n];
            for (int i = 0; i < n; i++)
            {
                _names[i] = names[i];
                _hsv[i] = Hsv.FromRgb(colors[i]);
            }

            _distance = new float[n, n];
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    _distance[a, b] = ComputeDistance(_hsv[a], _hsv[b]);
        }

        public string NameOf(int id) => (id >= 0 && id < _names.Length) ? _names[id] : "(chưa tô)";
        public Hsv HsvOf(int id) => _hsv[id];

        /// <summary>Tìm id theo tên (không phân biệt hoa thường). -1 nếu không có.</summary>
        public int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < _names.Length; i++)
                if (string.Equals(_names[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public float Distance(int a, int b) => _distance[a, b];

        /// <summary>Độ giống 0..1 giữa hai màu trong bảng (1 = cùng màu).</summary>
        public float Similarity(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Count || b >= Count) return 0f;
            float s = 1f - _distance[a, b] / _tolerance;
            return s < 0f ? 0f : (s > 1f ? 1f : s);
        }

        public static float HueDifference(float h1, float h2)
        {
            float d = Math.Abs(h1 - h2) % 360f;
            if (d > 180f) d = 360f - d;
            return d / 180f;   // 0..1
        }

        public static float ComputeDistance(Hsv a, Hsv b)
        {
            // Hue chỉ có nghĩa khi CẢ HAI màu đủ sắc. Trọng số hue tăng dần theo độ
            // bão hòa của màu nhạt hơn (liên tục, không có ngưỡng cứng): trắng vs kem
            // (S≈0.16) gần như chỉ so độ sáng/bão hòa, còn đỏ vs vàng so hue đầy đủ.
            float chroma = Math.Min(a.S, b.S) / ChromaRamp;
            if (chroma > 1f) chroma = 1f;

            float hue = HueDifference(a.H, b.H) * chroma;
            float ds = Math.Abs(a.S - b.S);
            float dv = Math.Abs(a.V - b.V);
            return WeightHue * hue + WeightSat * ds + WeightVal * dv;
        }
    }

    /// <summary>Bảng màu mặc định 16 màu cho nguyên liệu nấu ăn. Có thể sửa trong asset.</summary>
    public static class DefaultPalette
    {
        public static readonly string[] Names =
        {
            "white", "cream", "grey", "brown", "tan", "red", "dark red", "pink",
            "orange", "yellow", "pale yellow", "green", "dark green", "light green", "purple", "blue"
        };

        public static readonly Rgb[] Colors =
        {
            new Rgb(255,255,255), new Rgb(245,235,205), new Rgb(150,150,150), new Rgb(110,70,40),
            new Rgb(210,170,120), new Rgb(220,40,40),   new Rgb(140,20,25),   new Rgb(245,160,170),
            new Rgb(240,140,30),  new Rgb(250,215,40),  new Rgb(250,240,160), new Rgb(70,170,60),
            new Rgb(30,100,40),   new Rgb(170,220,110), new Rgb(120,60,150),  new Rgb(60,110,200)
        };
    }
}
