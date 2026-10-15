using UnityEditor;
using UnityEngine;
using ColorFood.Data;
using ColorFood.Recognition;

namespace ColorFood.EditorTools
{
    /// <summary>
    /// Menu tiện ích cho bước 4. Đặt trong thư mục Assets/Editor/.
    ///   Color Food → Create Default Palette
    ///   Color Food → Presets → Fried Egg Parts (selected template)
    ///   Color Food → Presets → Single Body Part (selected template)
    /// </summary>
    public static class ColorFoodMenus
    {
        private const string DataFolder = "Assets/Data";
        private const string PalettePath = "Assets/Data/palette_default.asset";

        [MenuItem("Color Food/Create Default Palette")]
        public static void CreateDefaultPalette()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ColorPaletteAsset>(PalettePath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                Debug.Log("[Color Food] Palette đã có sẵn: " + PalettePath);
                return;
            }

            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder("Assets", "Data");

            var asset = ScriptableObject.CreateInstance<ColorPaletteAsset>();
            AssetDatabase.CreateAsset(asset, PalettePath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Debug.Log("[Color Food] Đã tạo palette 16 màu: " + PalettePath);
        }

        [MenuItem("Color Food/Presets/Fried Egg Parts (selected template)")]
        public static void ApplyFriedEgg()
        {
            Apply(PartPresets.FriedEgg(), "Fried Egg");
        }

        [MenuItem("Color Food/Presets/Single Body Part (selected template)")]
        public static void ApplySingleBody()
        {
            // Màu để trống: nhập tên màu trong Inspector (ví dụ red, dark red)
            Apply(PartPresets.SingleBody(), "Single Body");
        }

        private static void Apply(PartRule[] rules, string label)
        {
            var template = Selection.activeObject as IngredientTemplate;
            if (template == null)
            {
                Debug.LogWarning("[Color Food] Hãy chọn một IngredientTemplate trong cửa sổ Project trước.");
                return;
            }

            Undo.RecordObject(template, "Apply " + label);
            template.parts = rules;
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Color Food] Đã áp bộ luật '{label}' cho {template.ingredientId}. " +
                      "Xem và chỉnh trong Inspector (mục Parts).");
        }
    }
}
