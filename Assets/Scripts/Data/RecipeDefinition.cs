using System.Collections.Generic;
using UnityEngine;

namespace ColorFood.Data
{
    /// <summary>
    /// Một món ăn: tên (tiếng Anh, hiện cho người chơi) + các nguyên liệu cần vẽ.
    /// Tạo: chuột phải trong Project → Create → Color Food → Recipe.
    /// </summary>
    [CreateAssetMenu(menuName = "Color Food/Recipe", fileName = "recipe_new")]
    public class RecipeDefinition : ScriptableObject
    {
        [Tooltip("Tên món hiện cho người chơi, vd \"Tomato Salad\".")]
        public string dishName = "New Dish";

        public Texture2D dishImage;

        [Tooltip("Các nguyên liệu người chơi phải vẽ, theo thứ tự. Mỗi nguyên liệu là một IngredientTemplate đã vẽ có màu.")]
        public List<IngredientTemplate> ingredients = new List<IngredientTemplate>();

        /// <summary>Có ít nhất một nguyên liệu chấm được.</summary>
        public bool IsValid => ingredients != null &&
                               ingredients.Exists(t => t != null && t.GetSketchShapes().Count > 0);
    }
}
