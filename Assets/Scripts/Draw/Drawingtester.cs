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
    /// Script thu thập dữ liệu hiệu chỉnh: vẽ → chuẩn hóa → chấm điểm → ghi CSV.
    ///
    /// HAI NGUỒN TEMPLATE:
    ///   • Để trống danh sách "Template Assets": dùng 3 hình toán học sinh bằng
    ///     code (tròn / chữ nhật 2:1 / tam giác) — như các lần đo trước.
    ///   • Kéo các IngredientTemplate (tạo bằng Template Recorder) vào danh
    ///     sách: dùng template VẼ TAY, mỗi cái với maxDistance riêng.
    ///
    /// CÁCH DÙNG:
    ///   1. Gắn script vào một GameObject rỗng, kéo DrawingCanvas vào ô Canvas
    ///   2. Play. Phím 1–9 chọn HÌNH BẠN ĐỊNH VẼ (theo thứ tự trong danh sách)
    ///   3. Vẽ hình đó, nhấn SPACE → ghi 1 dòng vào CSV, canvas tự xóa
    ///
    /// PHÍM:
    ///   1–9   — chọn nhãn (thứ tự hiện trong Console khi bắt đầu)
    ///   SPACE — chấm điểm + ghi CSV + xóa canvas
    ///   C     — xóa canvas, KHÔNG ghi (vẽ hỏng thì bấm cái này)
    ///
    /// CSV nằm ở Application.persistentDataPath (đường dẫn in ra Console).
    /// Nếu tiêu đề cột đổi (đổi bộ template), file cũ được đổi tên thành
    /// *.old-yyyyMMdd-HHmmss và tạo file mới — không bao giờ ghi lệch cột.
    ///
    /// Số thập phân ghi theo InvariantCulture (dấu chấm) để Excel không đọc
    /// nhầm khi máy đặt locale Việt Nam (dấu phẩy).
    /// </summary>
    public class DrawingTester : MonoBehaviour
    {
        [SerializeField] private DrawingCanvas canvas;

        [Header("Template vẽ tay (để trống = dùng hình sinh bằng code)")]
        [SerializeField] private List<IngredientTemplate> templateAssets = new List<IngredientTemplate>();

        [Header("Hiệu chỉnh")]
        [Tooltip("Chỉ dùng cho template sinh bằng code. Template asset dùng maxDistance riêng.")]
        [SerializeField] private float maxDistance = 0.12f;

        [Tooltip("Ngưỡng để coi là vẽ đạt.")]
        [SerializeField, Range(0f, 1f)] private float passThreshold = 0.7f;

        [Header("Ghi dữ liệu")]
        [SerializeField] private string csvFileName = "calibration.csv";
        [SerializeField] private bool clearAfterRecord = true;

        private readonly PDollarRecognizer _recognizer = new PDollarRecognizer();
        private readonly Dictionary<string, int> _sampleCount = new Dictionary<string, int>();

        private Dictionary<string, PointCloud> _templates;
        private Dictionary<string, float> _maxDistances;   // maxDistance riêng từng template
        private string[] _templateOrder;                   // thứ tự phím 1..9 và cột CSV
        private string _label;                             // hình bạn định vẽ
        private string _csvPath;

        private void Start()
        {
            if (canvas == null)
            {
                Debug.LogError("[DrawingTester] Chưa gán DrawingCanvas trong Inspector.");
                enabled = false;
                return;
            }

            BuildTemplates();
            if (_templates.Count == 0)
            {
                Debug.LogError("[DrawingTester] Không có template hợp lệ nào.");
                enabled = false;
                return;
            }

            _label = _templateOrder[0];
            PrepareCsv();

            var keys = new StringBuilder();
            for (int i = 0; i < _templateOrder.Length && i < 9; i++)
                keys.Append($"  [{i + 1}] {_templateOrder[i]}");

            Debug.Log("[DrawingTester] Sẵn sàng. Phím nhãn:" + keys +
                      $"\nNhãn hiện tại: {_label}. SPACE = ghi mẫu, C = xóa không ghi.\n" +
                      $"File CSV: {_csvPath}");
        }

        private void Update()
        {
            for (int i = 0; i < _templateOrder.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    SetLabel(_templateOrder[i]);
            }

            if (Input.GetKeyDown(KeyCode.Space)) RecordSample();
            if (Input.GetKeyDown(KeyCode.C)) canvas.Clear();
        }

        private void SetLabel(string label)
        {
            _label = label;
            int n = _sampleCount.TryGetValue(label, out int c) ? c : 0;
            Debug.Log($"[DrawingTester] Nhãn: <b>{label}</b> (đã có {n} mẫu phiên này)");
        }

        // ------------------------------------------------------------------
        // Chấm điểm + ghi CSV
        // ------------------------------------------------------------------

        private void RecordSample()
        {
            var drawn = canvas.BuildPointCloud();
            if (drawn == null) return;

            var row = new StringBuilder();
            row.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            row.Append(',').Append(_label);
            row.Append(',').Append(canvas.StrokeCount);
            row.Append(',').Append(canvas.RawPoints.Count);

            var log = new StringBuilder($"--- Mẫu (nhãn: {_label}) ---\n");

            // Khoảng cách tới TẤT CẢ template, để thấy hệ thống có phân biệt được không.
            // Trong game thật chỉ so với MỘT template (Guided Shape Matching).
            foreach (string name in _templateOrder)
            {
                float d = _recognizer.Match(drawn, _templates[name]);
                float s = _recognizer.MatchScore(drawn, _templates[name], _maxDistances[name]);

                row.Append(',').Append(d.ToString("F5", CultureInfo.InvariantCulture));

                string mark = name == _label ? "  ← đúng nhãn" : "";
                log.AppendLine($"   {name,-12} d={d:F5}  score={s:P0}{mark}");
            }

            File.AppendAllText(_csvPath, row + "\n", new UTF8Encoding(false));

            _sampleCount[_label] = (_sampleCount.TryGetValue(_label, out int c) ? c : 0) + 1;

            float own = _recognizer.MatchScore(drawn, _templates[_label], _maxDistances[_label]);
            bool pass = own >= passThreshold;
            log.Append(pass
                ? $"<color=green>ĐẠT</color> {own:P0}"
                : $"<color=orange>CHƯA ĐẠT</color> {own:P0}");
            log.Append($"  |  mẫu #{_sampleCount[_label]} của '{_label}'");

            Debug.Log(log.ToString());

            if (clearAfterRecord) canvas.Clear();
        }

        private string BuildHeader()
        {
            var header = new StringBuilder("time,label,strokes,rawPoints");
            foreach (string name in _templateOrder)
                header.Append(",d_").Append(name);
            return header.ToString();
        }

        private void PrepareCsv()
        {
            _csvPath = Path.Combine(Application.persistentDataPath, csvFileName);
            string header = BuildHeader();

            if (File.Exists(_csvPath))
            {
                // Bỏ ký tự BOM (U+FEFF) nếu file được tạo bởi bản trước
                string first = (File.ReadLines(_csvPath).FirstOrDefault() ?? "").TrimStart('\uFEFF').Trim();

                if (first == header) return;   // cùng bộ cột: ghi tiếp

                // Khác bộ cột: cất file cũ đi, tránh ghi lệch cột
                string backup = _csvPath + ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(_csvPath, backup);
                Debug.LogWarning($"[DrawingTester] Bộ template đổi nên file CSV cũ được cất tại:\n{backup}");
            }

            File.WriteAllText(_csvPath, header + "\n", new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------
        // Nạp template
        // ------------------------------------------------------------------

        private void BuildTemplates()
        {
            _templates = new Dictionary<string, PointCloud>();
            _maxDistances = new Dictionary<string, float>();
            var order = new List<string>();

            var assets = templateAssets.Where(t => t != null && t.IsValid).ToList();

            if (assets.Count > 0)
            {
                foreach (var t in assets)
                {
                    if (_templates.ContainsKey(t.ingredientId))
                    {
                        Debug.LogWarning($"[DrawingTester] Trùng ID '{t.ingredientId}', bỏ qua bản sau.");
                        continue;
                    }
                    _templates[t.ingredientId] = t.GetCloud();
                    _maxDistances[t.ingredientId] = t.maxDistance;
                    order.Add(t.ingredientId);
                }
                Debug.Log($"[DrawingTester] Dùng {order.Count} template VẼ TAY từ asset.");
            }
            else
            {
                AddGenerated(order, "circle", Circle(256, 256, 150));
                // Chữ nhật ngang tỉ lệ 2:1. $P giữ nguyên tỉ lệ khi Scale,
                // nên hình này khác hình vuông rõ rệt.
                AddGenerated(order, "rectangle", Rectangle(256, 256, 320, 160));
                AddGenerated(order, "triangle", Triangle(256, 256, 260));
                Debug.Log("[DrawingTester] Dùng 3 template sinh bằng code (tròn / chữ nhật / tam giác).");
            }

            _templateOrder = order.ToArray();
        }

        private void AddGenerated(List<string> order, string name, List<Point> points)
        {
            _templates[name] = new PointCloud(name, points);
            _maxDistances[name] = maxDistance;
            order.Add(name);
        }

        // ------------------------------------------------------------------
        // Hình sinh bằng code (chỉ dùng khi chưa có template vẽ tay)
        // ------------------------------------------------------------------

        private static List<Point> Circle(float cx, float cy, float r, int n = 48)
        {
            var pts = new List<Point>();
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new Point(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a), 0));
            }
            return pts;
        }

        private static List<Point> Rectangle(float cx, float cy, float width, float height)
        {
            float hw = width / 2f, hh = height / 2f;
            var corners = new[]
            {
                new Vector2(cx - hw, cy - hh), new Vector2(cx + hw, cy - hh),
                new Vector2(cx + hw, cy + hh), new Vector2(cx - hw, cy + hh)
            };
            // Số điểm mỗi cạnh tỉ lệ với độ dài cạnh, để điểm phân bố đều
            int perSide = Mathf.Max(8, Mathf.RoundToInt(Mathf.Max(width, height) / 20f));
            return Polygon(corners, perSide);
        }

        private static List<Point> Triangle(float cx, float cy, float size, int perSide = 16)
        {
            float h = size / 2f;
            var corners = new[]
            {
                new Vector2(cx, cy + h),
                new Vector2(cx + h, cy - h),
                new Vector2(cx - h, cy - h)
            };
            return Polygon(corners, perSide);
        }

        private static List<Point> Polygon(Vector2[] corners, int perSide)
        {
            var pts = new List<Point>();
            for (int k = 0; k < corners.Length; k++)
            {
                Vector2 a = corners[k];
                Vector2 b = corners[(k + 1) % corners.Length];
                for (int i = 0; i < perSide; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)perSide);
                    pts.Add(new Point(p.x, p.y, 0));
                }
            }
            return pts;
        }
    }
}