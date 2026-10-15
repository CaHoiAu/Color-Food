using System;
using UnityEngine;
using ColorFood.Recognition;

namespace ColorFood.Data
{
    /// <summary>
    /// Bảng màu của game (12–16 màu cố định) dưới dạng asset.
    /// Tạo bằng menu Color Food → Create Default Palette, rồi sửa tên/màu tùy ý.
    ///
    /// Thứ tự trong danh sách = ColorID. Luật bộ phận của nguyên liệu tham chiếu
    /// màu theo TÊN (ví dụ "yellow"), nên đổi thứ tự không làm hỏng luật —
    /// nhưng ĐỔI TÊN màu thì phải sửa luật tương ứng.
    /// </summary>
    [CreateAssetMenu(menuName = "Color Food/Color Palette", fileName = "palette_default")]
    public class ColorPaletteAsset : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public Color32 color;
        }

        public Entry[] entries = DefaultEntries();

        [Tooltip("Khoảng cách màu ứng với độ giống = 0. Nhỏ = khắt khe hơn. " +
                 "Mặc định 0.25: đỏ ≈ đỏ đậm (~75%), đỏ vs cam (~50%), đỏ vs vàng (~25%).")]
        [Range(0.05f, 1f)]
        public float tolerance = 0.25f;

        [NonSerialized] private ColorTable _table;

        public int Count => entries.Length;

        /// <summary>Bảng tương đồng tính sẵn; chỉ tạo lại khi asset bị sửa.</summary>
        public ColorTable Table
        {
            get
            {
                if (_table == null) _table = BuildTable();
                return _table;
            }
        }

        public Color32 ColorOf(int id)
        {
            if (id < 0 || id >= entries.Length) return new Color32(0, 0, 0, 0);
            return entries[id].color;
        }

        public string NameOf(int id) => (id >= 0 && id < entries.Length) ? entries[id].name : "";

        private ColorTable BuildTable()
        {
            var names = new string[entries.Length];
            var colors = new Rgb[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                names[i] = entries[i].name;
                Color32 c = entries[i].color;
                colors[i] = new Rgb(c.r, c.g, c.b);
            }
            return new ColorTable(names, colors, tolerance);
        }

        private static Entry[] DefaultEntries()
        {
            var list = new Entry[DefaultPalette.Names.Length];
            for (int i = 0; i < list.Length; i++)
            {
                Rgb c = DefaultPalette.Colors[i];
                list[i] = new Entry { name = DefaultPalette.Names[i], color = new Color32(c.R, c.G, c.B, 255) };
            }
            return list;
        }

        [ContextMenu("Reset to default 16 colors")]
        public void ResetToDefault()
        {
            entries = DefaultEntries();
            _table = null;
        }

        private void OnEnable()   => _table = null;
        private void OnValidate() => _table = null;
    }
}
