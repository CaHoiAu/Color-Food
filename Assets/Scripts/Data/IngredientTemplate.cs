using System.Collections.Generic;
using UnityEngine;
using ColorFood.Recognition;

namespace ColorFood.Data
{
    /// <summary>
    /// Từ vựng hình dạng cố định — dùng chung với cột ShapeType trong sheet
    /// Ingredients để lọc và kiểm tra va chạm silhouette.
    /// </summary>
    public enum ShapeType { Circle, Ellipse, Blob, Rectangle, Triangle, Strip, Other }

    /// <summary>
    /// Một nét vẽ mẫu PHỤ của nguyên liệu (cách vẽ khác: nhìn nghiêng, cắt
    /// lát, số nét khác…). Điểm thô, cùng hệ tọa độ với rawPoints.
    /// </summary>
    [System.Serializable]
    public class TemplateVariant
    {
        public string label = "";
        public Point[] rawPoints = new Point[0];
        public int strokeCount;

        public bool IsValid => rawPoints != null && rawPoints.Length >= 2;
    }

    /// <summary>
    /// Template của một nguyên liệu: nét vẽ mẫu + dung sai chấm điểm.
    ///
    /// Đây là DỮ LIỆU, không phải code — thêm nguyên liệu mới chỉ cần tạo
    /// thêm một asset (bằng Template Recorder), không viết dòng code nào.
    ///
    /// Lưu điểm THÔ (chưa chuẩn hóa) thay vì điểm đã chuẩn hóa, để nếu sau
    /// này đổi SamplingCount hoặc cách chuẩn hóa thì không phải vẽ lại mẫu.
    ///
    /// Tọa độ: gốc dưới-trái, trục Y hướng LÊN — cùng hệ với DrawingCanvas.
    ///
    /// NHIỀU MẪU: một nguyên liệu có thể có nhiều cách vẽ. Mẫu 1 là
    /// rawPoints (giữ nguyên tên trường để asset cũ không phải sửa), các mẫu
    /// tiếp theo nằm trong extraVariants. Khi chấm điểm, nét vẽ được so với
    /// TẤT CẢ mẫu và lấy mẫu giống nhất — thêm mẫu không bao giờ làm điểm tụt.
    /// Mọi mẫu dùng chung maxDistance và luật bộ phận.
    ///
    /// GÓC ĐỘ: từ mỗi mẫu còn tự sinh bản xoay nghiêng / lật trái-phải
    /// (autoRotateMax, autoRotateStep, autoMirror), nên không phải vẽ lại
    /// cùng một hình ở nhiều góc.
    /// </summary>
    [CreateAssetMenu(menuName = "Color Food/Ingredient Template", fileName = "template_new")]
    public class IngredientTemplate : ScriptableObject
    {
        [Header("Định danh")]
        public string ingredientId = "new_ingredient";
        public string displayName = "";
        public ShapeType shapeType = ShapeType.Blob;

        [Header("Chấm điểm")]
        [Tooltip("Khoảng cách ứng với score = 0, RIÊNG cho nguyên liệu này. " +
                 "Hình khó vẽ (tam giác) cần giá trị lớn hơn hình dễ (tròn). " +
                 "Dùng tab 'Thử chấm điểm' trong Template Recorder để tìm giá trị phù hợp.")]
        public float maxDistance = 0.12f;

        [Header("Nét vẽ mẫu (do Template Recorder ghi)")]
        [Tooltip("Mẫu 1 (mẫu chính).")]
        public Point[] rawPoints = new Point[0];
        public int strokeCount;

        [Tooltip("Mẫu 2, 3… — các cách vẽ khác của cùng nguyên liệu. " +
                 "Thêm bằng Template Recorder (Lưu → Thêm mẫu mới).")]
        public TemplateVariant[] extraVariants = new TemplateVariant[0];

        [Header("Tự sinh góc độ (không cần vẽ lại)")]
        [Tooltip("Từ mỗi mẫu tự sinh thêm bản xoay nghiêng tới ±góc này (độ). " +
                 "0 = tắt. Đừng để quá lớn với hình có hướng rõ (tam giác đỉnh lên " +
                 "xoay 180° thành đỉnh xuống).")]
        [Range(0f, 90f)] public float autoRotateMax = 30f;

        [Tooltip("Bước xoay (độ). 30 với bước 15 → thêm các góc ±15°, ±30°.")]
        [Range(5f, 45f)] public float autoRotateStep = 15f;

        [Tooltip("Tự sinh thêm bản lật trái ↔ phải (quả quay hướng ngược lại).")]
        public bool autoMirror = true;

        [Header("Bộ phận và màu (bước 4)")]
        [Tooltip("Trọng số của điểm hình ($P) so với tổng trọng số các bộ phận.")]
        [Range(0f, 1f)] public float shapeWeight = 0.4f;

        [Tooltip("Thiếu bộ phận bắt buộc thì điểm tổng không vượt quá giá trị này " +
                 "(nên dưới ngưỡng đạt 0.7).")]
        [Range(0f, 1f)] public float missingPartCap = 0.5f;

        [Tooltip("Hình ($P) dưới ngưỡng này thì điểm tổng cũng bị chặn, dù tô màu đúng hết. " +
                 "Không có luật này, tô màu đúng có thể bù cho một nét vẽ sai hình rõ rệt " +
                 "(ví dụ hình 0.42 + bộ phận hoàn hảo vẫn ra tổng ~0.77 với trọng số mặc định). " +
                 "0 = tắt (không chặn theo hình).")]
        [Range(0f, 1f)] public float minShape = 0.5f;

        [Tooltip("Hình dưới minShape thì điểm tổng không vượt quá giá trị này.")]
        [Range(0f, 1f)] public float shapeCap = 0.5f;

        [Tooltip("Một bộ phận có minRoundness (độ tròn) mà đo được thấp hơn ngưỡng đó " +
                 "thì điểm tổng không vượt quá giá trị này — giống cơ chế shapeCap, " +
                 "nhưng xét độ tròn TỪNG vùng thay vì hình toàn cục ($P). Bắt đúng case " +
                 "outline gồ ghề (méo như amip) mà $P vẫn cho hình khá cao.")]
        [Range(0f, 1f)] public float roundnessCap = 0.5f;

        [Tooltip("Luật từng bộ phận: số lượng, diện tích, màu. Để trống = chỉ chấm hình. " +
                 "Có thể điền nhanh bằng menu Color Food → Presets.")]
        public PartRule[] parts = new PartRule[0];

        [TextArea]
        public string notes;

        [System.NonSerialized] private PointCloud[] _clouds;

        /// <summary>Tổng số mẫu, kể cả mẫu 1 (rawPoints) — tính cả mẫu rỗng.</summary>
        public int VariantCount => 1 + (extraVariants != null ? extraVariants.Length : 0);

        /// <summary>Có ít nhất một mẫu đủ điểm để chấm.</summary>
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < VariantCount; i++)
                    if (IsVariantValid(i)) return true;
                return false;
            }
        }

        public Point[] GetVariantPoints(int index)
            => index == 0 ? rawPoints : extraVariants[index - 1].rawPoints;

        public int GetVariantStrokeCount(int index)
            => index == 0 ? strokeCount : extraVariants[index - 1].strokeCount;

        public string GetVariantLabel(int index)
        {
            string label = index == 0 ? "" : extraVariants[index - 1]?.label;
            return string.IsNullOrEmpty(label)
                ? "Mẫu " + (index + 1) + (index == 0 ? " (chính)" : "")
                : "Mẫu " + (index + 1) + ": " + label;
        }

        private bool IsVariantValid(int index)
        {
            Point[] pts = index == 0 ? rawPoints : extraVariants[index - 1]?.rawPoints;
            return pts != null && pts.Length >= 2;
        }

        /// <summary>
        /// PointCloud đã chuẩn hóa của MỌI mẫu hợp lệ cùng các biến thể góc độ
        /// tự sinh (xoay / lật), tính MỘT LẦN rồi cache — không chuẩn hóa lại mỗi
        /// lần chấm điểm. Tên mỗi cloud cho biết nó từ mẫu nào, biến đổi gì
        /// (ví dụ "tomato#2 lật xoay -15°"). Chỉ số trong mảng này KHÁC chỉ số mẫu.
        /// </summary>
        public IReadOnlyList<PointCloud> GetClouds()
        {
            if (_clouds != null) return _clouds;

            var list = new List<PointCloud>();
            for (int i = 0; i < VariantCount; i++)
            {
                if (!IsVariantValid(i)) continue;
                foreach (var (suffix, points) in PointTransform.Variants(
                             GetVariantPoints(i), autoRotateMax, autoRotateStep, autoMirror))
                    list.Add(new PointCloud(ingredientId + "#" + (i + 1) + suffix, points));
            }
            _clouds = list.ToArray();
            return _clouds;
        }

        /// <summary>PointCloud của mẫu hợp lệ đầu tiên (null nếu không có).</summary>
        public PointCloud GetCloud()
        {
            var clouds = GetClouds();
            return clouds.Count > 0 ? clouds[0] : null;
        }

        /// <summary>Thay nét vẽ của một mẫu có sẵn (index 0 = mẫu chính).</summary>
        public void SetVariant(int index, Point[] points, int strokes)
        {
            if (index == 0) { rawPoints = points; strokeCount = strokes; }
            else
            {
                extraVariants[index - 1].rawPoints = points;
                extraVariants[index - 1].strokeCount = strokes;
            }
            InvalidateCache();
        }

        /// <summary>Thêm một mẫu mới, trả về chỉ số của nó. Mẫu chính còn trống thì điền vào đó.</summary>
        public int AddVariant(Point[] points, int strokes, string label = "")
        {
            if (!IsVariantValid(0))
            {
                SetVariant(0, points, strokes);
                return 0;
            }

            var list = new List<TemplateVariant>(extraVariants ?? new TemplateVariant[0]);
            list.Add(new TemplateVariant { label = label, rawPoints = points, strokeCount = strokes });
            extraVariants = list.ToArray();
            InvalidateCache();
            return list.Count;
        }

        /// <summary>
        /// Xóa một mẫu. Xóa mẫu chính thì mẫu 2 được đôn lên thay; nếu chỉ còn
        /// một mẫu thì không xóa (trả về false).
        /// </summary>
        public bool RemoveVariant(int index)
        {
            if (VariantCount <= 1 || index < 0 || index >= VariantCount) return false;

            var list = new List<TemplateVariant>(extraVariants);
            if (index == 0)
            {
                rawPoints = list[0].rawPoints;
                strokeCount = list[0].strokeCount;
                list.RemoveAt(0);
            }
            else list.RemoveAt(index - 1);

            extraVariants = list.ToArray();
            InvalidateCache();
            return true;
        }

        /// <summary>Gọi khi điểm mẫu bị thay từ code (Inspector tự gọi OnValidate).</summary>
        public void InvalidateCache() => _clouds = null;

        private void OnEnable() => _clouds = null;
        private void OnValidate() => _clouds = null;
    }
}
