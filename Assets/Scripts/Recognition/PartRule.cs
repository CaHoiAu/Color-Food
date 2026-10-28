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

        /// <summary>
        /// Độ tròn tối thiểu của đường bao (0 = không kiểm tra).
        /// circularity = 4π·Diện tích / Chu vi², hình tròn hình học lý tưởng = 1.
        ///
        /// Đây là chỗ bắt các đường bao LỒI LÕM NHIỀU CHỖ mà $P bỏ sót — $P đo
        /// khoảng cách trung bình trên cả đám điểm nên các gợn nhỏ bị pha loãng,
        /// còn circularity phạt trực tiếp: outline càng gồ ghề, chu vi càng tăng
        /// nhanh hơn diện tích, độ tròn càng tụt.
        ///
        /// QUAN TRỌNG: vì chu vi được đo bằng cách đếm cạnh pixel (nhanh, không
        /// cần dò biên), và vì nét vẽ có độ dày (brush) nên mép trong của vùng tô
        /// vốn đã lượn sóng nhẹ ở mức pixel — một hình tròn vẽ tay "hoàn hảo" bằng
        /// cách này cũng chỉ đo được circularity khoảng 0.3–0.6, KHÔNG gần 1.0.
        /// Vì vậy minRoundness không phải "% tròn tuyệt đối" mà là ngưỡng hiệu
        /// chỉnh theo đúng cách đo này. Riêng bộ phận Outer CÓ chứa một Inner bên
        /// trong (vd lòng trắng chứa lòng đỏ): viền của LỖ (do vùng Inner tạo ra)
        /// được loại khỏi chu vi của Outer trước khi tính (xem ComputePerimeters
        /// trong RegionAnalyzer) — nếu không, lòng trắng sẽ bị tính như một "cái
        /// nhẫn" (vành khuyên), vốn có circularity thấp một cách tự nhiên dù vẽ
        /// trơn hoàn hảo, không đủ chỗ để đặt ngưỡng phân biệt với outline méo.
        /// Số liệu đo trên nét vẽ tay thật của lòng đỏ (15 lần vẽ trứng đúng):
        /// 0.396–0.451 (trung vị 0.42); hình tổng hợp méo ≈ 0.23–0.33. Ngưỡng 0.30
        /// cách mức nhỏ nhất đo được ~25%.
        /// KHÔNG nên dùng minRoundness cho lòng trắng: viền lòng trắng thật vốn lượn sóng,
        /// không tròn, nên độ tròn phạt nhầm — dùng maxSpikiness (chỉ bắt gai nhọn).
        ///
        /// Dùng cho bộ phận có silhouette nên tròn/blob (lòng trắng, lòng đỏ,
        /// quả tròn...); để 0 cho bộ phận dài (dưa leo, cà rốt) vì hình dài vốn
        /// đã có circularity thấp một cách tự nhiên, không liên quan đến méo.
        /// </summary>
        public float minRoundness = 0f;

        /// <summary>
        /// Độ "gai" tối đa cho phép của hình bao (0 = không kiểm tra). Khác minRoundness:
        /// KHÔNG phạt hình dẹt hay viền lượn sóng tự nhiên (lòng trắng trứng thật vốn
        /// không tròn), chỉ phạt gai nhọn/mũi nhọn/sợi hẹp nhô ra.
        /// Đo bằng tỉ lệ diện tích bị cắt khi "mở" hình bằng đĩa nhỏ (xem Region.Spikiness).
        /// Số đo TRÊN NÉT VẼ TAY THẬT (nét cọ 4px, 19/10): lòng trắng vẽ đúng 0.0019–0.0025;
        /// lòng trắng vẽ gai 0.0084–0.0163. Hình mô phỏng: lượn sóng ≈ 0.001, gai nhọn rõ
        /// ≈ 0.02–0.045. Ngưỡng lòng trắng 0.0045 nằm giữa hai cụm thật (xấp xỉ trung bình nhân).
        /// Vượt ngưỡng thì điểm tổng bị chặn ở roundnessCap; điểm bộ phận giảm tuyến tính
        /// về 0 khi gai đạt 3 lần ngưỡng.
        /// LƯU Ý: số 0 nghĩa là TẮT luật — asset cũ không có trường này sẽ đọc ra 0, nên sau khi
        /// cập nhật code phải áp lại preset (Color Food → Presets) hoặc nhập tay trong Inspector.
        /// </summary>
        public float maxSpikiness = 0f;

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
                               targetColors = new[] { "white", "cream" }, maxSpikiness = 0.0045f, weight = 0.2f },
                new PartRule { name = "yolk", role = PartRole.Inner, minCount = 1, maxCount = 1,
                               areaRatioMin = 0.05f, areaRatioMax = 0.25f,
                               targetColors = new[] { "yellow", "orange" }, minRoundness = 0.30f, weight = 0.4f }
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
