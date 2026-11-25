using System.Collections.Generic;
using UnityEngine;

namespace ColorFood.Data
{
    /// <summary>
    /// Một level: các nguyên liệu dùng trong level + các món khách có thể đặt.
    ///
    /// Khi chấm trong level, bức vẽ CHỈ được so với nguyên liệu của level này (vd Level 1:
    /// Bell Pepper, Tomato, Chili → vẽ cà chua chỉ cần khác ớt chuông và ớt cay, không bị
    /// so với táo của level khác). Ít đối thủ hơn → ít nhầm hơn, chấm nhanh hơn.
    ///
    /// Tạo: chuột phải trong Project → Create → Color Food → Level.
    /// </summary>
    [CreateAssetMenu(menuName = "Color Food/Level", fileName = "level_new")]
    public class LevelDefinition : ScriptableObject
    {
        [Tooltip("Tên level hiện cho người chơi (tiếng Anh), vd \"Level 1\".")]
        public string levelName = "Level 1";

        [Tooltip("Nguyên liệu của level. Bức vẽ chỉ được so với các nguyên liệu này. " +
                 "Nguyên liệu nằm trong món mà quên thêm vào đây vẫn được tự tính vào.")]
        public List<IngredientTemplate> ingredients = new List<IngredientTemplate>();

        [Tooltip("Các món khách có thể đặt trong level.")]
        public List<RecipeDefinition> recipes = new List<RecipeDefinition>();

        [Tooltip("Số món phải làm ĐẠT để qua level.")]
        [Min(1)] public int dishesToComplete = 3;

        /// <summary>Món dùng được (có ít nhất một nguyên liệu chấm được).</summary>
        public List<RecipeDefinition> ValidRecipes => recipes.FindAll(r => r != null && r.IsValid);

        /// <summary>
        /// Mọi nguyên liệu chấm được của level: danh sách ingredients + nguyên liệu trong các
        /// món (không trùng). Đây là tập dùng để so "giống nguyên liệu khác hơn".
        /// </summary>
        public List<IngredientTemplate> Pool()
        {
            var pool = new List<IngredientTemplate>();
            void Add(IngredientTemplate t)
            {
                if (t != null && !pool.Contains(t) && t.GetSketchShapes().Count > 0) pool.Add(t);
            }
            foreach (var t in ingredients) Add(t);
            foreach (var r in recipes)
                if (r != null) foreach (var t in r.ingredients) Add(t);
            return pool;
        }

        private void OnValidate()
        {
            foreach (var r in recipes)
            {
                if (r == null) continue;
                foreach (var t in r.ingredients)
                    if (t != null && !ingredients.Contains(t))
                        Debug.LogWarning($"[Level] {levelName}: món '{r.dishName}' dùng '{t.DisplayNameOrId}' " +
                                         "nhưng nguyên liệu này chưa có trong danh sách Ingredients của level " +
                                         "(vẫn được tự tính vào khi chấm).", this);
            }
        }
    }
}
