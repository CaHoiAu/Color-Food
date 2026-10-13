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
    /// Template của một nguyên liệu: nét vẽ mẫu + dung sai chấm điểm.
    ///
    /// Đây là DỮ LIỆU, không phải code — thêm nguyên liệu mới chỉ cần tạo
    /// thêm một asset (bằng Template Recorder), không viết dòng code nào.
    ///
    /// Lưu điểm THÔ (chưa chuẩn hóa) thay vì điểm đã chuẩn hóa, để nếu sau
    /// này đổi SamplingCount hoặc cách chuẩn hóa thì không phải vẽ lại mẫu.
    ///
    /// Tọa độ: gốc dưới-trái, trục Y hướng LÊN — cùng hệ với DrawingCanvas.
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
        public Point[] rawPoints = new Point[0];
        public int strokeCount;

        [TextArea]
        public string notes;

        [System.NonSerialized] private PointCloud _cloud;

        public bool IsValid => rawPoints != null && rawPoints.Length >= 2;

        /// <summary>
        /// PointCloud đã chuẩn hóa, tính MỘT LẦN rồi cache — không chuẩn hóa
        /// lại mỗi lần chấm điểm.
        /// </summary>
        public PointCloud GetCloud()
        {
            if (!IsValid) return null;
            if (_cloud == null) _cloud = new PointCloud(ingredientId, rawPoints);
            return _cloud;
        }

        /// <summary>Gọi khi rawPoints bị thay từ code (Inspector tự gọi OnValidate).</summary>
        public void InvalidateCache() => _cloud = null;

        private void OnEnable()   => _cloud = null;
        private void OnValidate() => _cloud = null;
    }
}
