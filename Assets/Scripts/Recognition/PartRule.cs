using System;

namespace ColorFood.Recognition
{
    /// <summary>
    /// Luật cho MỘT bộ phận của nguyên liệu. Là dữ liệu (nằm trong asset
    /// IngredientTemplate), không phải code — thêm nguyên liệu mới = điền thêm luật.
    ///
    /// Ví dụ trứng ốp la có 2 luật:
    ///   white: Outer, 1 vùng, màu white/cream
    ///   yolk : Inner, 1 vùng, chiếm 5–25% tổng diện tích, màu yellow/orange
    ///
    /// Vì bảng màu cố định nên targetColors là TÊN màu trong palette; nhiều tên =
    /// chấp nhận nhiều màu (độ giống lấy theo màu gần nhất).
    /// </summary>
    [Serializable]
    public class PartRule
    {
        public string name = "part";
        public PartRole role = PartRole.Outer;

        public int minCount = 1;
        /// <summary>0 = không giới hạn.</summary>
        public int maxCount = 1;

        /// <summary>Tỉ lệ diện tích bộ phận / tổng diện tích các vùng kín. max = 0 → bỏ qua kiểm tra.</summary>
        public float areaRatioMin = 0f;
        public float areaRatioMax = 0f;

        public string[] targetColors = new string[0];
        public float weight = 0.3f;

        /// <summary>Thiếu bộ phận bắt buộc thì điểm tổng bị chặn trần.</summary>
        public bool Required => minCount > 0;
    }

    /// <summary>Bộ luật dựng sẵn để không phải nhập tay trong Inspector.</summary>
    public static class PartPresets
    {
        public static PartRule[] FriedEgg()
        {
            return new[]
            {
                new PartRule { name = "white", role = PartRole.Outer, minCount = 1, maxCount = 1,
                               targetColors = new[] { "white", "cream" }, weight = 0.2f },
                new PartRule { name = "yolk", role = PartRole.Inner, minCount = 1, maxCount = 1,
                               areaRatioMin = 0.05f, areaRatioMax = 0.25f,
                               targetColors = new[] { "yellow", "orange" }, weight = 0.4f }
            };
        }

        /// <summary>Nguyên liệu một khối một màu (cà chua, cà rốt, táo...).</summary>
        public static PartRule[] SingleBody(params string[] colors)
        {
            return new[]
            {
                new PartRule { name = "body", role = PartRole.Outer, minCount = 1, maxCount = 1,
                               targetColors = colors, weight = 0.6f }
            };
        }
    }
}
