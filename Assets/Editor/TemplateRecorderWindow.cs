using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using ColorFood.Data;
using ColorFood.Recognition;

namespace ColorFood.EditorTools
{
    /// <summary>
    /// Cửa sổ Editor để VẼ TEMPLATE bằng chuột/bút và lưu thành asset
    /// IngredientTemplate. Mở: menu  Color Food → Template Recorder.
    ///
    /// Hai tab:
    ///   1. Vẽ template — vẽ nét mẫu, đặt ID/hình dạng, bấm Lưu. Một nguyên
    ///      liệu có thể có NHIỀU MẪU: lưu vào asset đã có thì chọn "Thêm làm
    ///      mẫu mới" (hoặc ghi đè mẫu đang sửa).
    ///   2. Thử chấm điểm — vẽ thử nhiều lần (bằng cách chấm ảnh màu, như trong
    ///      game) để xem điểm và để tool ĐỀ XUẤT sketchMaxDistance cho nguyên liệu.
    ///
    /// VẼ CÓ MÀU: chọn màu trong bảng rồi vẽ VÀ TÔ luôn bức mẫu (dâu đỏ, lá xanh...):
    /// vẽ viền bằng Bút, rồi dùng Thùng sơn bấm vào bên trong để tô kín (hoặc tăng cỡ bút
    /// để tô tay).
    /// Ảnh màu được lưu vào asset — đó chính là đáp án khi chấm màu theo vị trí
    /// (SketchScorer). Chọn "Đen (chỉ hình)" để vẽ mẫu không chấm màu.
    ///
    /// ĐẶT FILE NÀY TRONG thư mục tên "Editor" (Assets/Editor/).
    ///
    /// LƯU Ý: hệ tọa độ lưu trữ có trục Y hướng LÊN (giống DrawingCanvas),
    /// còn IMGUI có Y hướng xuống — code đã lật lại. Nếu không lật thì tam
    /// giác sẽ bị lưu ngược đầu.
    /// </summary>
    public class TemplateRecorderWindow : EditorWindow
    {
        private const float CanvasSize = 420f;
        private const float MinPointDistance = 3f;
        private const int MinPointsToSave = 10;
        private const string SaveFolder = "Assets/Data/Templates";
        private const string DefaultPalettePath = "Assets/Data/palette_default.asset";
        /// <summary>Bán kính nét mặc định (pixel canvas 420) — tương đương sáp màu bán kính 8 trên canvas game 512.</summary>
        private const int DefaultPaintRadius = 7;

        private enum Mode { Record, Test }
        private enum PaintTool { Brush, Bucket }

        /// <summary>Một lần đổ thùng sơn: tại điểm (X,Y), sau Order nét đầu tiên.</summary>
        private struct FillAction
        {
            public int Order;
            public float X, Y;
            public int ColorId;
        }

        /// <summary>Một bản phác: các nét (điểm) + các lần đổ thùng sơn + lớp màu nền đã lưu.</summary>
        private class Sketch
        {
            public readonly List<Point> Points = new List<Point>();
            public readonly Dictionary<int, int> Radius = new Dictionary<int, int>();   // bán kính từng nét
            public readonly List<FillAction> Fills = new List<FillAction>();
            /// <summary>Ảnh màu nạp từ asset (khi sửa mẫu có màu) — lớp dưới cùng; null = không có.</summary>
            public int[] BasePaint;
            public int StrokeId;
            public bool Drawing;

            public Texture2D Preview;
            public bool PreviewDirty = true;

            public int StrokeCount => Points.Count == 0 ? 0 : Points[Points.Count - 1].StrokeID + 1;

            public int RadiusOf(int strokeId) => Radius.TryGetValue(strokeId, out int r) ? r : DefaultPaintRadius;

            public void Clear()
            {
                Points.Clear();
                Radius.Clear();
                Fills.Clear();
                BasePaint = null;
                StrokeId = 0;
                Drawing = false;
                PreviewDirty = true;
            }

            /// <summary>Hoàn tác thao tác cuối: lần đổ thùng sơn cuối, hoặc nét cuối.</summary>
            public void UndoLastStroke()
            {
                Drawing = false;
                PreviewDirty = true;

                if (Fills.Count > 0 && Fills[Fills.Count - 1].Order >= StrokeCount)
                {
                    Fills.RemoveAt(Fills.Count - 1);
                    return;
                }
                if (Points.Count == 0) return;

                int last = Points[Points.Count - 1].StrokeID;
                Points.RemoveAll(p => p.StrokeID == last);
                Radius.Remove(last);

                StrokeId = Points.Count == 0 ? 0 : Points[Points.Count - 1].StrokeID + 1;
            }
        }

        // --- trạng thái ---
        private Mode _mode = Mode.Record;
        private Vector2 _scroll;
        private readonly Sketch _record = new Sketch();
        private readonly Sketch _test = new Sketch();

        private string _id = "circle";
        private string _displayName = "Circle";
        private ShapeType _shape = ShapeType.Circle;
        private float _maxDistance = 0.12f;
        private string _notes = "";

        private IngredientTemplate _loaded;     // template đang sửa / đang thử
        private int _variant;                   // mẫu được chọn trong _loaded
        private IngredientTemplate _editing;    // asset mà _record đang nạp từ đó (null = vẽ mới)
        private int _editingVariant;            // mẫu mà _record đang nạp

        private readonly PDollarRecognizer _recognizer = new PDollarRecognizer();
        private readonly List<float> _testDistances = new List<float>();
        private float _lastDistance = -1f;
        private float _lastScore = -1f;
        private string _lastMatched = "";
        private string _lastReport = "";
        private float _lastTotal = -1f;

        private ColorPaletteAsset _palette;
        private int _colorId = -1;              // -1 = đen, chỉ hình (không màu)
        private PaintTool _tool = PaintTool.Brush;
        private int _brushRadius = DefaultPaintRadius;

        // ------------------------------------------------------------------

        [MenuItem("Color Food/Template Recorder")]
        public static void Open()
        {
            var window = GetWindow<TemplateRecorderWindow>("Template Recorder");
            window.minSize = new Vector2(470f, 720f);
        }

        private void OnEnable()
        {
            if (_palette == null) _palette = AssetDatabase.LoadAssetAtPath<ColorPaletteAsset>(DefaultPalettePath);
        }

        private void OnDisable()
        {
            if (_record.Preview != null) DestroyImmediate(_record.Preview);
            if (_test.Preview != null) DestroyImmediate(_test.Preview);
        }

        // ------------------------------------------------------------------
        // GUI
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            _mode = (Mode)GUILayout.Toolbar((int)_mode,
                new[] { "1. Vẽ template", "2. Thử chấm điểm" });

            EditorGUILayout.Space(6);

            // Cửa sổ thấp hơn nội dung thì cuộn được, không che mất các nút dưới canvas
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_mode == Mode.Record) DrawRecordTab();
            else DrawTestTab();
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------- TAB 1 -------------------------------

        private void DrawRecordTab()
        {
            EditorGUILayout.BeginHorizontal();
            _loaded = (IngredientTemplate)EditorGUILayout.ObjectField(
                "Template có sẵn", _loaded, typeof(IngredientTemplate), false);

            using (new EditorGUI.DisabledScope(_loaded == null))
            {
                if (GUILayout.Button("Nạp để sửa", GUILayout.Width(90)))
                    LoadFromAsset(_loaded, _variant);
            }
            EditorGUILayout.EndHorizontal();

            if (_loaded != null)
            {
                EditorGUILayout.BeginHorizontal();
                _variant = EditorGUILayout.Popup("Mẫu", Mathf.Clamp(_variant, 0, _loaded.VariantCount - 1),
                                                 VariantLabels(_loaded));
                using (new EditorGUI.DisabledScope(_loaded.VariantCount <= 1))
                {
                    if (GUILayout.Button("Xóa mẫu", GUILayout.Width(90)))
                        RemoveVariant(_loaded, _variant);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField(
                _editing == null
                    ? "Đang vẽ mới"
                    : $"Đang sửa: {_editing.ingredientId} — {_editing.GetVariantLabel(_editingVariant)}",
                EditorStyles.miniBoldLabel);

            EditorGUILayout.Space(2);

            _id = EditorGUILayout.TextField("ID", _id);
            _displayName = EditorGUILayout.TextField("Tên hiển thị", _displayName);
            _shape = (ShapeType)EditorGUILayout.EnumPopup("Hình dạng", _shape);
            _maxDistance = EditorGUILayout.FloatField("maxDistance", _maxDistance);

            EditorGUILayout.HelpBox(
                "Vẽ ĐÚNG HƯỚNG như sẽ hiện trong game (tam giác đỉnh hướng lên, " +
                "chữ nhật nằm ngang...). $P không bất biến với phép xoay.\n" +
                "Hình nhiều nét thì vẽ nhiều nét (trứng rán: lòng trắng + lòng đỏ).\n" +
                "Nguyên liệu có nhiều cách vẽ (nguyên quả / bổ đôi...) thì lưu mỗi " +
                "cách thành một MẪU của cùng ID — khi chấm sẽ lấy mẫu giống nhất.\n" +
                "VẼ VÀ TÔ MÀU LUÔN bức mẫu (thân đỏ, lá xanh...) — màu tô ở đâu thì khi chấm, " +
                "người chơi phải tô màu đó ở đúng chỗ đó.",
                MessageType.Info);

            DrawPalette();
            DrawTools();
            DrawCanvas(_record);

            EditorGUILayout.LabelField(
                $"{_record.StrokeCount} nét  |  {_record.Points.Count} điểm", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hoàn tác nét cuối")) { _record.UndoLastStroke(); Repaint(); }
            if (GUILayout.Button("Xóa hết")) { _record.Clear(); Repaint(); }
            if (GUILayout.Button("Vẽ mới"))
            {
                _record.Clear();
                _editing = null;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(_record.Points.Count < MinPointsToSave))
            {
                if (GUILayout.Button("LƯU THÀNH ASSET", GUILayout.Height(32)))
                    Save();
            }
        }

        // ---------------------------- TAB 2 -------------------------------

        private void DrawTestTab()
        {
            _loaded = (IngredientTemplate)EditorGUILayout.ObjectField(
                "Template cần thử", _loaded, typeof(IngredientTemplate), false);

            if (_loaded == null || !_loaded.IsValid)
            {
                EditorGUILayout.HelpBox(
                    "Chọn một template đã lưu (hoặc lưu template ở tab 1 trước).",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                $"{_loaded.ingredientId}  |  {_loaded.VariantCount} mẫu vẽ → " +
                $"{_loaded.GetSketchShapes().Count} biến thể (cả xoay/lật)  |  " +
                $"sketchMaxDistance hiện tại: {_loaded.sketchMaxDistance:F3}",
                EditorStyles.miniBoldLabel);

            EditorGUILayout.HelpBox(
                "Vẽ và tô thử như người chơi thật (chấm bằng cách ảnh màu, như trong game). " +
                "SPACE = chấm điểm và xóa canvas, DELETE = xóa không ghi. " +
                "Vẽ 10–20 lần rồi xem đề xuất bên dưới.",
                MessageType.Info);

            DrawPalette();
            DrawTools();
            DrawCanvas(_test);
            HandleTestKeys();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Chấm điểm (Space)")) EvaluateTest();
            if (GUILayout.Button("Hoàn tác nét")) { _test.UndoLastStroke(); Repaint(); }
            if (GUILayout.Button("Xóa (Delete)")) { _test.Clear(); Repaint(); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawTestResults();
        }

        private void HandleTestKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.Space)
            {
                EvaluateTest();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                _test.Clear();
                e.Use();
                Repaint();
            }
        }

        private void EvaluateTest()
        {
            if (_loaded == null || !_loaded.IsValid) return;
            if (_test.Points.Count < 2 || _palette == null) return;

            // Chấm giống hệt trong game: ảnh màu so với mọi biến thể của mẫu
            SketchResult r = SketchScorer.Score(BuildColorSketch(_test), _loaded.GetSketchShapes(), _recognizer,
                                                _palette.Table, _loaded.sketchMaxDistance, _loaded.shapeWeight,
                                                _loaded.minColorCoverage, _loaded.minShape, _loaded.shapeCap);
            if (r == null) return;

            _lastDistance = r.Distance;
            _lastScore = r.Shape;
            _lastTotal = r.Total;
            _lastMatched = r.Variant;
            _lastReport = r.Report;
            _testDistances.Add(_lastDistance);

            _test.Clear();
            Repaint();
        }

        private void DrawTestResults()
        {
            if (_lastDistance < 0f)
            {
                EditorGUILayout.LabelField("Chưa có lần chấm nào.", EditorStyles.miniLabel);
                return;
            }

            bool pass = _lastTotal >= 0.7f;
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = pass ? new Color(0.1f, 0.6f, 0.2f) : new Color(0.85f, 0.45f, 0.05f);

            EditorGUILayout.LabelField(
                $"Lần vừa rồi:  TỔNG {_lastTotal:P0} {(pass ? "ĐẠT" : "CHƯA ĐẠT")}   |   " +
                $"d = {_lastDistance:F5}   hình = {_lastScore:P0}", style);
            EditorGUILayout.HelpBox(_lastReport.TrimEnd(), MessageType.None);

            if (_testDistances.Count < 5)
            {
                EditorGUILayout.LabelField(
                    $"Đã chấm {_testDistances.Count} lần. Cần ít nhất 5 lần để đề xuất sketchMaxDistance.",
                    EditorStyles.miniLabel);
            }
            else
            {
                var sorted = new List<float>(_testDistances);
                sorted.Sort();

                float median = sorted[sorted.Count / 2];
                // p80: 80% số lần vẽ có d <= giá trị này
                float p80 = sorted[Mathf.Clamp(Mathf.CeilToInt(0.8f * sorted.Count) - 1, 0, sorted.Count - 1)];

                // score = 1 - d/M  >= 0.7  <=>  d <= 0.3 * M  <=>  M >= d / 0.3
                float suggested = p80 / 0.3f;

                EditorGUILayout.LabelField(
                    $"{sorted.Count} lần  |  d trung vị = {median:F4}  |  p80 = {p80:F4}",
                    EditorStyles.miniLabel);

                EditorGUILayout.HelpBox(
                    $"sketchMaxDistance đề xuất: {suggested:F3}\n" +
                    "(với giá trị này, 80% lần vẽ của bạn có điểm HÌNH từ 70% trở lên)",
                    MessageType.None);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Áp dụng vào template"))
                {
                    Undo.RecordObject(_loaded, "Set sketchMaxDistance");
                    _loaded.sketchMaxDistance = Mathf.Round(suggested * 1000f) / 1000f;
                    EditorUtility.SetDirty(_loaded);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Xóa lịch sử"))
                {
                    _testDistances.Clear();
                    _lastDistance = _lastScore = _lastTotal = -1f;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------
        // Canvas: vẽ + nhận chuột
        // ------------------------------------------------------------------

        private void DrawCanvas(Sketch sketch)
        {
            Rect canvas = GUILayoutUtility.GetRect(
                CanvasSize, CanvasSize, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            HandleMouse(canvas, sketch);

            if (Event.current.type != EventType.Repaint) return;

            // nền + khung
            EditorGUI.DrawRect(canvas, new Color(0.96f, 0.95f, 0.92f));
            DrawFrame(canvas, new Color(0.35f, 0.35f, 0.35f));

            // đường dẫn giữa canvas để căn hình
            var guide = new Color(0f, 0f, 0f, 0.08f);
            EditorGUI.DrawRect(new Rect(canvas.center.x - 0.5f, canvas.y, 1f, canvas.height), guide);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.center.y - 0.5f, canvas.width, 1f), guide);

            // Ảnh màu (nền đã lưu + vùng đổ thùng sơn + nét), rồi vẽ nét đè lên cho sắc nét
            GUI.DrawTexture(canvas, GetPreview(sketch));
            DrawStrokes(canvas, sketch);
        }

        private Texture2D GetPreview(Sketch s)
        {
            int size = (int)CanvasSize;
            if (s.Preview == null)
            {
                s.Preview = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                s.PreviewDirty = true;
            }
            if (!s.PreviewDirty) return s.Preview;

            int[] paint = BuildPaint(s);
            var pixels = new Color32[paint.Length];
            var dark = new Color32(20, 20, 20, 255);
            for (int i = 0; i < paint.Length; i++)
            {
                int c = paint[i];
                if (c == ColorSketch.Empty) pixels[i] = new Color32(0, 0, 0, 0);
                else if (c < 0 || _palette == null || c >= _palette.Count) pixels[i] = dark;
                else pixels[i] = _palette.ColorOf(c);
            }
            s.Preview.SetPixels32(pixels);   // hàng 0 = dưới cùng, trùng hệ trục Y-lên của bản đồ màu
            s.Preview.Apply();
            s.PreviewDirty = false;
            return s.Preview;
        }

        /// <summary>Công cụ (bút / thùng sơn) + cỡ bút.</summary>
        private void DrawTools()
        {
            EditorGUILayout.BeginHorizontal();
            _tool = (PaintTool)GUILayout.Toolbar((int)_tool, new[] { "Bút (vẽ / tô)", "Thùng sơn (bấm vào vùng kín)" });
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(_tool != PaintTool.Brush))
                _brushRadius = EditorGUILayout.IntSlider("Cỡ bút", _brushRadius, 2, 30);
        }

        private static void DrawFrame(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        private void HandleMouse(Rect canvas, Sketch s)
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition) &&
                _tool == PaintTool.Bucket)
            {
                TryBucket(canvas, s, e.mousePosition);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                s.Drawing = true;
                s.Radius[s.StrokeId] = _brushRadius;
                AddPoint(canvas, s, e.mousePosition, force: true, colorId: _colorId);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && s.Drawing)
            {
                AddPoint(canvas, s, e.mousePosition, force: false, colorId: _colorId);
                e.Use();
                Repaint();
            }
            else if ((e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) && s.Drawing)
            {
                // Nhấc bút = hết một nét
                s.Drawing = false;
                s.StrokeId++;
                s.PreviewDirty = true;
                e.Use();
                Repaint();
            }
        }

        private static void AddPoint(Rect canvas, Sketch s, Vector2 mouse, bool force, int colorId)
        {
            // Kẹp trong canvas để kéo ra ngoài không mất nét
            float x = Mathf.Clamp(mouse.x - canvas.x, 0f, CanvasSize);
            float yDown = Mathf.Clamp(mouse.y - canvas.y, 0f, CanvasSize);

            // LẬT TRỤC Y: IMGUI Y hướng xuống, game và asset Y hướng lên
            float y = CanvasSize - yDown;

            if (!force && s.Points.Count > 0)
            {
                Point last = s.Points[s.Points.Count - 1];
                if (last.StrokeID == s.StrokeId &&
                    Mathf.Abs(last.X - x) + Mathf.Abs(last.Y - y) < MinPointDistance)
                    return;
            }

            s.Points.Add(new Point(x, y, s.StrokeId, colorId));
        }

        private static Vector3 ToScreen(Rect canvas, Point p)
            => new Vector3(canvas.x + p.X, canvas.y + (CanvasSize - p.Y), 0f);

        private void DrawStrokes(Rect canvas, Sketch s)
        {
            var line = new List<Vector3>();
            int currentId = int.MinValue;
            int radius = DefaultPaintRadius;

            foreach (Point p in s.Points)
            {
                if (p.StrokeID != currentId)
                {
                    FlushLine(line, radius);
                    currentId = p.StrokeID;
                    radius = s.RadiusOf(currentId);
                    Handles.color = StrokeColor(p.ColorID);
                }
                line.Add(ToScreen(canvas, p));
            }
            FlushLine(line, radius);
        }

        private Color StrokeColor(int colorId)
        {
            if (_palette == null || colorId < 0 || colorId >= _palette.Count) return new Color(0.08f, 0.08f, 0.08f);
            Color32 c = _palette.ColorOf(colorId);
            return new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
        }

        /// <summary>Nét dày đúng bằng bề rộng được ghi vào ảnh màu, để thấy chính xác cái được lưu.</summary>
        private static void FlushLine(List<Vector3> line, int radius)
        {
            if (line.Count >= 2)
                Handles.DrawAAPolyLine(radius * 2f, line.ToArray());
            else if (line.Count == 1)
                Handles.DrawSolidDisc(line[0], Vector3.forward, radius);

            line.Clear();
        }

        /// <summary>
        /// Đổ thùng sơn tại chỗ bấm: lấp vùng TRỐNG liền nhau quanh điểm đó (bấm vào
        /// chỗ đã có màu thì đổi màu cả mảng cùng màu). Vùng hở ra mép canvas thì
        /// không đổ — như canvas trong game.
        /// </summary>
        private void TryBucket(Rect canvas, Sketch s, Vector2 mouse)
        {
            if (_colorId < 0)
            {
                ShowNotification(new GUIContent("Chọn một màu trước khi đổ thùng sơn"));
                return;
            }
            float x = Mathf.Clamp(mouse.x - canvas.x, 0f, CanvasSize - 1);
            float y = CanvasSize - 1 - Mathf.Clamp(mouse.y - canvas.y, 0f, CanvasSize - 1);

            var fill = new FillAction { Order = s.StrokeCount, X = x, Y = y, ColorId = _colorId };
            s.Fills.Add(fill);
            BuildPaint(s, out bool lastFillOk);
            if (!lastFillOk)
            {
                s.Fills.RemoveAt(s.Fills.Count - 1);
                ShowNotification(new GUIContent("Vùng này hở ra mép — hãy vẽ khép kín viền trước"));
                return;
            }
            s.PreviewDirty = true;
        }

        /// <summary>Bảng màu: bấm để chọn màu vẽ. "Đen" = nét không màu (mẫu chỉ chấm hình).</summary>
        private void DrawPalette()
        {
            _palette = (ColorPaletteAsset)EditorGUILayout.ObjectField(
                "Bảng màu", _palette, typeof(ColorPaletteAsset), false);
            if (_palette == null)
            {
                EditorGUILayout.HelpBox("Chưa có bảng màu: nét vẽ màu đen, mẫu chỉ chấm hình.", MessageType.Warning);
                _colorId = -1;
                return;
            }

            const int perRow = 6;
            var oldBg = GUI.backgroundColor;
            for (int i = -1; i < _palette.Count; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int k = i; k < i + perRow && k < _palette.Count; k++)
                {
                    string label = (k == _colorId ? "> " : "") + (k < 0 ? "Đen (chỉ hình)" : _palette.NameOf(k));
                    GUI.backgroundColor = k < 0 ? Color.gray : StrokeColor(k);
                    if (GUILayout.Button(label, GUILayout.MinWidth(60))) _colorId = k;
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = oldBg;
        }

        private static ColorSketch BuildColorSketch(Sketch s)
            => ColorSketch.FromPaint(BuildPaint(s), (int)CanvasSize, (int)CanvasSize);

        private static int[] BuildPaint(Sketch s) => BuildPaint(s, out _);

        /// <summary>
        /// Dựng bản đồ màu 420×420 theo đúng thứ tự thao tác — giống cách canvas game
        /// ghi PaintIds: lớp nền đã lưu (nếu có), rồi lần lượt từng nét (nét sau đè nét
        /// trước, bán kính riêng từng nét) và từng lần đổ thùng sơn. Nét không màu
        /// (ColorID &lt; 0) ghi là NoColor. lastFillOk = lần đổ thùng sơn cuối có thành công.
        /// </summary>
        private static int[] BuildPaint(Sketch s, out bool lastFillOk)
        {
            int size = (int)CanvasSize;
            var paint = new int[size * size];
            if (s.BasePaint != null) Array.Copy(s.BasePaint, paint, paint.Length);
            else for (int i = 0; i < paint.Length; i++) paint[i] = ColorSketch.Empty;

            // Gom điểm theo nét
            var strokes = new List<List<Point>>();
            for (int i0 = 0; i0 < s.Points.Count;)
            {
                int i1 = i0;
                while (i1 + 1 < s.Points.Count && s.Points[i1 + 1].StrokeID == s.Points[i0].StrokeID) i1++;
                strokes.Add(s.Points.GetRange(i0, i1 - i0 + 1));
                i0 = i1 + 1;
            }

            lastFillOk = true;
            var mask = new bool[size * size];
            int f = 0;
            for (int k = 0; k <= strokes.Count; k++)
            {
                // các lần đổ thùng sơn làm khi mới có k nét
                for (; f < s.Fills.Count && s.Fills[f].Order <= k; f++)
                    lastFillOk = Bucket(paint, size, s.Fills[f]);

                if (k == strokes.Count) break;
                List<Point> stroke = strokes[k];
                Array.Clear(mask, 0, mask.Length);
                Raster.DrawPoints(mask, size, size, stroke, s.RadiusOf(stroke[0].StrokeID));
                int color = stroke[0].ColorID >= 0 ? stroke[0].ColorID : ColorSketch.NoColor;
                for (int i = 0; i < mask.Length; i++) if (mask[i]) paint[i] = color;
            }
            for (; f < s.Fills.Count; f++) lastFillOk = Bucket(paint, size, s.Fills[f]);
            return paint;
        }

        /// <summary>Đổ màu vào mảng liền nhau (4 hướng) cùng màu với điểm bấm. false = hở ra mép, không đổ.</summary>
        private static bool Bucket(int[] paint, int size, FillAction a)
        {
            int sx = Mathf.Clamp((int)a.X, 0, size - 1), sy = Mathf.Clamp((int)a.Y, 0, size - 1);
            int target = paint[sy * size + sx];
            if (target == a.ColorId) return true;

            var region = new List<int>();
            var seen = new bool[paint.Length];
            var stack = new Stack<int>();
            stack.Push(sy * size + sx);
            seen[sy * size + sx] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                region.Add(p);
                int y = p / size, x = p - y * size;
                if (target == ColorSketch.Empty && (x == 0 || y == 0 || x == size - 1 || y == size - 1))
                    return false;   // vùng trống thông ra mép: chưa khép kín
                if (x > 0) Visit(p - 1);
                if (x < size - 1) Visit(p + 1);
                if (y > 0) Visit(p - size);
                if (y < size - 1) Visit(p + size);
            }
            foreach (int p in region) paint[p] = a.ColorId;
            return true;

            void Visit(int q)
            {
                if (seen[q] || paint[q] != target) return;
                seen[q] = true;
                stack.Push(q);
            }
        }

        private static bool HasColoredStroke(Sketch s)
        {
            foreach (Point p in s.Points) if (p.ColorID >= 0) return true;
            if (s.Fills.Count > 0) return true;
            if (s.BasePaint != null) foreach (int c in s.BasePaint) if (c >= 0) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Lưu / nạp asset
        // ------------------------------------------------------------------

        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = Regex.Replace(raw.Trim().ToLowerInvariant(), "[^a-z0-9_]+", "_");
            return s.Trim('_');
        }

        private static string[] VariantLabels(IngredientTemplate asset)
        {
            var labels = new string[asset.VariantCount];
            for (int i = 0; i < labels.Length; i++)
            {
                Point[] pts = asset.GetVariantPoints(i);
                labels[i] = $"{asset.GetVariantLabel(i)}  ({asset.GetVariantStrokeCount(i)} nét, " +
                            $"{(pts == null ? 0 : pts.Length)} điểm" +
                            (asset.GetVariantSketch(i) != null ? ", có màu)" : ", chỉ nét)");
            }
            return labels;
        }

        private void RemoveVariant(IngredientTemplate asset, int index)
        {
            if (!EditorUtility.DisplayDialog("Xóa mẫu?",
                    $"Xóa {asset.GetVariantLabel(index)} khỏi {asset.name}?", "Xóa", "Hủy"))
                return;

            Undo.RecordObject(asset, "Remove template variant");
            if (!asset.RemoveVariant(index)) return;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // Chỉ số các mẫu sau bị dời; nét đang sửa không còn gắn với mẫu nào
            if (_editing == asset) _editing = null;
            _variant = Mathf.Clamp(_variant, 0, asset.VariantCount - 1);
            Repaint();
        }

        /// <summary>Phóng ảnh màu (lưới nhỏ) về kích thước canvas, lấy ô gần nhất.</summary>
        private static int[] Upscale(ColorSketch s, int size)
        {
            var paint = new int[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    paint[y * size + x] = s.Cells[(y * s.Size / size) * s.Size + x * s.Size / size];
            return paint;
        }

        private void LoadFromAsset(IngredientTemplate asset, int variant)
        {
            if (asset == null) return;
            variant = Mathf.Clamp(variant, 0, asset.VariantCount - 1);

            _id = asset.ingredientId;
            _displayName = asset.displayName;
            _shape = asset.shapeType;
            _maxDistance = asset.maxDistance;
            _notes = asset.notes;

            _record.Clear();
            Point[] points = asset.GetVariantPoints(variant);
            if (points != null)
                _record.Points.AddRange(points);

            // Mẫu có màu: ảnh màu đã lưu (gồm cả vùng đổ thùng sơn) làm lớp nền, nét vẽ chồng lên
            ColorSketch saved = asset.GetVariantSketch(variant);
            if (saved != null) _record.BasePaint = Upscale(saved, (int)CanvasSize);

            // Mẫu cũ chỉ có nét (không ảnh màu): ColorID trong điểm không có nghĩa — vẽ đen
            if (saved == null)
                for (int i = 0; i < _record.Points.Count; i++)
                {
                    Point p = _record.Points[i];
                    _record.Points[i] = new Point(p.X, p.Y, p.StrokeID, -1);
                }

            _editing = asset;
            _editingVariant = variant;

            _record.StrokeId = _record.Points.Count == 0
                ? 0
                : _record.Points[_record.Points.Count - 1].StrokeID + 1;

            Repaint();
        }

        private void Save()
        {
            string id = SanitizeId(_id);
            if (string.IsNullOrEmpty(id))
            {
                EditorUtility.DisplayDialog("Thiếu ID",
                    "ID chỉ gồm chữ thường, số và dấu gạch dưới (ví dụ: fried_egg).", "OK");
                return;
            }

            if (_record.Points.Count < MinPointsToSave)
            {
                EditorUtility.DisplayDialog("Quá ít điểm",
                    $"Cần ít nhất {MinPointsToSave} điểm. Hãy vẽ lại.", "OK");
                return;
            }

            Directory.CreateDirectory(SaveFolder);
            string path = $"{SaveFolder}/template_{id}.asset";

            IngredientTemplate asset = AssetDatabase.LoadAssetAtPath<IngredientTemplate>(path);
            Point[] points = _record.Points.ToArray();
            ColorSketch sketch = HasColoredStroke(_record) ? BuildColorSketch(_record) : null;
            int saved;
            bool writeInfo = true;   // ghi ID/tên/maxDistance/ghi chú từ cửa sổ vào asset

            if (asset != null)
            {
                // Đang sửa đúng asset này thì mặc định ghi đè mẫu đang sửa, ngược lại mẫu chính
                int target = _editing == asset ? _editingVariant : 0;
                int choice = EditorUtility.DisplayDialogComplex("Template đã tồn tại",
                    $"{path} đã có {asset.VariantCount} mẫu.\n\n" +
                    "• Thêm làm mẫu mới: giữ các mẫu cũ, thêm một cách vẽ nữa.\n" +
                    $"• Ghi đè: thay {asset.GetVariantLabel(target)} bằng nét vẽ hiện tại.",
                    "Thêm làm mẫu mới", "Hủy", "Ghi đè " + asset.GetVariantLabel(target));
                if (choice == 1) return;

                Undo.RecordObject(asset, "Update ingredient template");
                if (choice == 0)
                {
                    saved = asset.AddVariant(points, _record.StrokeCount, "", sketch);
                    // Chưa nạp asset này thì các ô trong cửa sổ không phải của nó —
                    // giữ nguyên maxDistance đã hiệu chỉnh, không đè bằng giá trị mặc định.
                    writeInfo = _editing == asset;
                }
                else
                {
                    asset.SetVariant(target, points, _record.StrokeCount, sketch);
                    saved = target;
                }
            }
            else
            {
                asset = CreateInstance<IngredientTemplate>();
                AssetDatabase.CreateAsset(asset, path);
                asset.SetVariant(0, points, _record.StrokeCount, sketch);
                saved = 0;
            }

            if (writeInfo)
            {
                asset.ingredientId = id;
                asset.displayName = _displayName;
                asset.shapeType = _shape;
                asset.maxDistance = _maxDistance;
                asset.notes = _notes;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            _id = id;
            _loaded = asset;
            _variant = saved;
            _editing = asset;
            _editingVariant = saved;
            EditorGUIUtility.PingObject(asset);

            Debug.Log($"[TemplateRecorder] Đã lưu {path} — {asset.GetVariantLabel(saved)}  " +
                      $"({_record.StrokeCount} nét, {points.Length} điểm; tổng {asset.VariantCount} mẫu)");
        }
    }
}
