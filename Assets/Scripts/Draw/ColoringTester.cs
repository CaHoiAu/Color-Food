using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using ColorFood.Data;
using ColorFood.Recognition;

namespace ColorFood.Drawing
{
    /// <summary>
    /// Bàn thử chấm điểm đầy đủ: vẽ hình → tô màu → chấm (hình + bộ phận + màu).
    /// Thay cho DrawingTester khi muốn thử tô màu (đừng bật cả hai cùng lúc).
    ///
    /// CÁCH DÙNG:
    ///   1. Gắn vào một GameObject rỗng; kéo DrawingCanvas và các IngredientTemplate
    ///      vào ô tương ứng. Palette gán cho DrawingCanvas (tester tự lấy từ đó).
    ///   2. Play. Chọn nguyên liệu bằng phím 1–9.
    ///   3. Chọn màu bằng ô màu bên trái hoặc phím [ ] — màu dùng cho CẢ nét vẽ lẫn tô.
    ///      F = sáp màu (mặc định): dùng cho cả viền lẫn tô, tự đoán khi nhấc tay —
    ///          vẽ viền trước, rồi tô bên trong CÙNG MÀU viền.
    ///      D = bút vẽ viền mảnh, G = thùng sơn (bấm vào vùng kín).
    ///   4. ENTER = chấm điểm + ghi CSV. C = xóa canvas, không ghi.
    ///
    /// Màu và nút bấm vẽ bằng IMGUI (OnGUI) để khỏi phải dựng UI trong scene;
    /// nằm ở mép trái Game view nên cần Game view đủ rộng (ví dụ Full HD 1920×1080).
    ///
    /// CSV scoring_log.csv nằm cạnh calibration.csv (đường dẫn in ra Console):
    /// time,ingredient,strokes,rawPoints,d,shape,parts,total,capped,outerRegions,innerRegions,circOuter,circInner,spikeOuter,spikeInner,
    /// sketchD,sketchShape,sketchColor,sketchCoverage,sketchTotal   (5 cột cuối: cách chấm ảnh màu — điểm chính)
    /// (circOuter/circInner = độ tròn thô, spikeOuter/spikeInner = độ gai của lòng trắng/lòng đỏ,
    ///  dùng để hiệu chỉnh minRoundness / maxSpikiness.
    ///  Header đổi nên file cũ tự được cất thành scoring_log.csv.old-... và tạo file mới.)
    /// Đây là dữ liệu cho thí nghiệm "trước/sau luật bộ phận".
    /// </summary>
    public class ColoringTester : MonoBehaviour
    {
        [SerializeField] private DrawingCanvas canvas;
        [SerializeField] private ColorPaletteAsset palette;
        [SerializeField] private List<IngredientTemplate> ingredients = new List<IngredientTemplate>();

        [SerializeField, Range(0f, 1f)] private float passThreshold = 0.7f;
        [SerializeField] private string csvFileName = "scoring_log.csv";

        private const string CsvHeader =
            "time,ingredient,strokes,rawPoints,d,shape,parts,total,capped,outerRegions,innerRegions,circOuter,circInner,spikeOuter,spikeInner," +
            "sketchD,sketchShape,sketchColor,sketchCoverage,sketchTotal";

        private readonly PDollarRecognizer _recognizer = new PDollarRecognizer();
        private int _index;
        private string _csvPath;
        private string _lastReport = "";

        private IngredientTemplate Current =>
            (ingredients.Count > 0 && _index < ingredients.Count) ? ingredients[_index] : null;

        private void Start()
        {
            // Palette lấy từ canvas nếu ô này để trống, để chỉ phải gán một chỗ
            if (palette == null && canvas != null) palette = canvas.Palette;

            if (canvas == null || palette == null)
            {
                Debug.LogError("[ColoringTester] Cần gán DrawingCanvas, và gán ColorPaletteAsset cho canvas (hoặc cho tester).");
                enabled = false;
                return;
            }

            ingredients = ingredients.Where(t => t != null && t.IsValid).ToList();
            if (ingredients.Count == 0)
            {
                Debug.LogError("[ColoringTester] Chưa có IngredientTemplate hợp lệ nào.");
                enabled = false;
                return;
            }

            // Bắt đầu bằng màu tối nhất (màu 0 thường là trắng — vẽ nét trắng sẽ không thấy)
            canvas.SetColor(DarkestColor());
            canvas.SetTool(DrawingCanvas.Tool.Crayon);
            PrepareCsv();

            var keys = new StringBuilder();
            for (int i = 0; i < ingredients.Count && i < 9; i++)
                keys.Append($"  [{i + 1}] {ingredients[i].ingredientId}");

            // In luật bộ phận THỰC SỰ đang nằm trong asset, để thấy ngay khi asset cũ còn
            // giá trị 0 (luật tắt) hoặc chưa áp preset.
            foreach (var ing in ingredients)
            {
                var sb = new StringBuilder();
                if (ing.parts == null || ing.parts.Length == 0) sb.Append(" (không có luật bộ phận)");
                else
                    foreach (var p in ing.parts)
                        sb.Append($"\n    {p.name} [{p.role}] maxGai={p.maxSpikiness:0.0000} minTròn={p.minRoundness:0.00}");
                Debug.Log($"[ColoringTester] Luật của '{ing.ingredientId}':{sb}  | minShape={ing.minShape:0.00} roundnessCap={ing.roundnessCap:0.00}");
            }

            Debug.Log("[ColoringTester] Sẵn sàng. Phím nguyên liệu:" + keys +
                      "\nF = sáp màu (viền + tô, tự đoán), D = bút viền, G = thùng sơn, [ ] = đổi màu, ENTER = chấm điểm, C = xóa.\n" +
                      $"File CSV: {_csvPath}");
        }

        private void Update()
        {
            for (int i = 0; i < ingredients.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(i);

            if (Input.GetKeyDown(KeyCode.D)) canvas.SetTool(DrawingCanvas.Tool.Draw);
            if (Input.GetKeyDown(KeyCode.F)) canvas.SetTool(DrawingCanvas.Tool.Crayon);
            if (Input.GetKeyDown(KeyCode.G)) canvas.SetTool(DrawingCanvas.Tool.Fill);

            if (Input.GetKeyDown(KeyCode.LeftBracket))
                canvas.SetColor((canvas.CurrentColorId + palette.Count - 1) % palette.Count);
            if (Input.GetKeyDown(KeyCode.RightBracket))
                canvas.SetColor((canvas.CurrentColorId + 1) % palette.Count);

            if (Input.GetKeyDown(KeyCode.Return)) ScoreNow();
            if (Input.GetKeyDown(KeyCode.C)) { canvas.Clear(); _lastReport = ""; }
        }

        private int DarkestColor()
        {
            int best = 0;
            float bestLum = float.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                Color32 c = palette.ColorOf(i);
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                if (lum < bestLum) { bestLum = lum; best = i; }
            }
            return best;
        }

        private void Select(int i)
        {
            _index = i;
            Debug.Log($"[ColoringTester] Nguyên liệu: <b>{Current.ingredientId}</b>");
        }

        // ------------------------------------------------------------------
        // Chấm điểm + ghi CSV
        // ------------------------------------------------------------------

        private void ScoreNow()
        {
            var t = Current;
            // Cách chấm chính: ảnh màu (không phân biệt viền / tô)
            SketchResult sk = canvas.ScoreSketch(t, _recognizer);
            // Cách cũ (vùng kín + đoán viền/tô) — in kèm để so sánh
            ScoreResult r = canvas.ScoreIngredient(t, _recognizer, out float d);
            if (sk == null && r == null)
            {
                Debug.LogWarning("[ColoringTester] Chưa vẽ gì (hoặc thiếu template/palette).");
                return;
            }

            float total = sk != null ? sk.Total : r.Total;
            bool pass = total >= passThreshold;
            string verdict = pass ? "<color=green>ĐẠT</color>" : "<color=orange>CHƯA ĐẠT</color>";
            _lastReport = $"{t.ingredientId}: {total:P0} {(pass ? "ĐẠT" : "CHƯA ĐẠT")}";

            var log = new StringBuilder($"[{t.ingredientId}] TỔNG {total:P0} {verdict}\n");
            if (sk != null) log.Append($"ẢNH MÀU (d = {sk.Distance:F4}):\n{sk.Report}");
            if (r != null) log.Append($"--- cách cũ, để so sánh: {r.Total:P0} (d = {d:F4})\n{r.Report}");
            Debug.Log(log.ToString());

            var row = new StringBuilder();
            row.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            row.Append(',').Append(t.ingredientId);
            row.Append(',').Append(canvas.StrokeCount);
            row.Append(',').Append(canvas.RawPoints.Count);
            if (r != null)
            {
                row.Append(',').Append(d.ToString("F5", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.Shape.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.Parts.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.Total.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.Capped ? 1 : 0);
                row.Append(',').Append(r.OuterCount);
                row.Append(',').Append(r.InnerCount);
                row.Append(',').Append(r.OuterCircularity.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.InnerCircularity.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.OuterSpikiness.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(r.InnerSpikiness.ToString("F4", CultureInfo.InvariantCulture));
            }
            else row.Append(",,,,,,,,,,,");
            if (sk != null)
            {
                row.Append(',').Append(sk.Distance.ToString("F5", CultureInfo.InvariantCulture));
                row.Append(',').Append(sk.Shape.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(sk.Color.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(sk.Coverage.ToString("F4", CultureInfo.InvariantCulture));
                row.Append(',').Append(sk.Total.ToString("F4", CultureInfo.InvariantCulture));
            }
            else row.Append(",,,,,");
            File.AppendAllText(_csvPath, row + "\n", new UTF8Encoding(false));
        }

        private void PrepareCsv()
        {
            _csvPath = Path.Combine(Application.persistentDataPath, csvFileName);

            if (File.Exists(_csvPath))
            {
                string first = (File.ReadLines(_csvPath).FirstOrDefault() ?? "").TrimStart('\uFEFF').Trim();
                if (first == CsvHeader) return;

                string backup = _csvPath + ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(_csvPath, backup);
                Debug.LogWarning($"[ColoringTester] Tiêu đề CSV khác, file cũ cất tại:\n{backup}");
            }
            File.WriteAllText(_csvPath, CsvHeader + "\n", new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------
        // Giao diện IMGUI: ô màu + nút chế độ (chữ ASCII để font mặc định hiển thị đúng)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (canvas == null || palette == null) return;

            const int cols = 2, w = 74, h = 30, pad = 6;
            var oldBg = GUI.backgroundColor;
            var oldContent = GUI.contentColor;

            for (int i = 0; i < palette.Count; i++)
            {
                int col = i % cols, row = i / cols;
                var rect = new Rect(10 + col * (w + pad), 10 + row * (h + pad), w, h);

                Color32 c32 = palette.ColorOf(i);
                var c = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 1f);
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

                GUI.backgroundColor = c;
                GUI.contentColor = lum > 0.55f ? Color.black : Color.white;

                string label = (i == canvas.CurrentColorId ? "> " : "") + palette.NameOf(i);
                if (GUI.Button(rect, label)) canvas.SetColor(i);
            }

            GUI.backgroundColor = oldBg;
            GUI.contentColor = oldContent;

            int rows = (palette.Count + cols - 1) / cols;
            float y = 10 + rows * (h + pad) + 10;

            ToolButton(new Rect(10, y, 150, 26), DrawingCanvas.Tool.Draw, " Draw (D)");
            ToolButton(new Rect(10, y + 30, 150, 26), DrawingCanvas.Tool.Crayon, " Crayon (F)");
            ToolButton(new Rect(10, y + 60, 150, 26), DrawingCanvas.Tool.Fill, " Bucket (G)");

            var t = Current;
            GUI.Label(new Rect(10, y + 96, 260, 22), "Ingredient: " + (t != null ? t.ingredientId : "-"));
            GUI.Label(new Rect(10, y + 116, 300, 22), _lastReport);
        }

        private void ToolButton(Rect rect, DrawingCanvas.Tool tool, string label)
        {
            bool on = canvas.CurrentTool == tool;
            if (GUI.Toggle(rect, on, label, "Button") && !on) canvas.SetTool(tool);
        }
    }
}
