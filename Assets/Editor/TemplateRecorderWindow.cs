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
    ///   2. Thử chấm điểm — vẽ thử nhiều lần để xem khoảng cách d và để tool
    ///      ĐỀ XUẤT maxDistance riêng cho nguyên liệu đó.
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

        private enum Mode { Record, Test }

        /// <summary>Một bản phác: danh sách điểm + trạng thái đang vẽ.</summary>
        private class Sketch
        {
            public readonly List<Point> Points = new List<Point>();
            public int StrokeId;
            public bool Drawing;

            public int StrokeCount => Points.Count == 0 ? 0 : Points[Points.Count - 1].StrokeID + 1;

            public void Clear()
            {
                Points.Clear();
                StrokeId = 0;
                Drawing = false;
            }

            public void UndoLastStroke()
            {
                if (Points.Count == 0) return;

                int last = Points[Points.Count - 1].StrokeID;
                Points.RemoveAll(p => p.StrokeID == last);

                StrokeId = Points.Count == 0 ? 0 : Points[Points.Count - 1].StrokeID + 1;
                Drawing = false;
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

        // ------------------------------------------------------------------

        [MenuItem("Color Food/Template Recorder")]
        public static void Open()
        {
            var window = GetWindow<TemplateRecorderWindow>("Template Recorder");
            window.minSize = new Vector2(470f, 720f);
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
            else                      DrawTestTab();
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
                "cách thành một MẪU của cùng ID — khi chấm sẽ lấy mẫu giống nhất.",
                MessageType.Info);

            DrawCanvas(_record);

            EditorGUILayout.LabelField(
                $"{_record.StrokeCount} nét  |  {_record.Points.Count} điểm", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hoàn tác nét cuối")) { _record.UndoLastStroke(); Repaint(); }
            if (GUILayout.Button("Xóa hết"))           { _record.Clear();           Repaint(); }
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
                $"{_loaded.ingredientId}  |  {_loaded.GetClouds().Count} mẫu  |  " +
                $"maxDistance hiện tại: {_loaded.maxDistance:F3}",
                EditorStyles.miniBoldLabel);

            EditorGUILayout.HelpBox(
                "Vẽ thử như người chơi thật. SPACE = chấm điểm và xóa canvas, " +
                "DELETE = xóa không ghi. Vẽ 10–20 lần rồi xem đề xuất bên dưới.",
                MessageType.Info);

            DrawCanvas(_test);
            HandleTestKeys();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Chấm điểm (Space)")) EvaluateTest();
            if (GUILayout.Button("Hoàn tác nét"))      { _test.UndoLastStroke(); Repaint(); }
            if (GUILayout.Button("Xóa (Delete)"))      { _test.Clear();          Repaint(); }
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
            if (_test.Points.Count < 2) return;

            var drawn = new PointCloud("test", _test.Points);
            var clouds = _loaded.GetClouds();

            // So với mọi mẫu, lấy mẫu giống nhất — giống hệt cách chấm trong game
            _lastDistance = _recognizer.MatchBest(drawn, clouds, out int best);
            _lastScore = PDollarRecognizer.ScoreFromDistance(_lastDistance, _loaded.maxDistance);
            _lastMatched = clouds[best].Name;
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

            bool pass = _lastScore >= 0.7f;
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = pass ? new Color(0.1f, 0.6f, 0.2f) : new Color(0.85f, 0.45f, 0.05f);

            EditorGUILayout.LabelField(
                $"Lần vừa rồi:  d = {_lastDistance:F5}   score = {_lastScore:P0}   " +
                (pass ? "ĐẠT" : "CHƯA ĐẠT"), style);

            if (_loaded.GetClouds().Count > 1)
                EditorGUILayout.LabelField($"Giống nhất với mẫu: {_lastMatched}", EditorStyles.miniLabel);

            if (_testDistances.Count < 5)
            {
                EditorGUILayout.LabelField(
                    $"Đã chấm {_testDistances.Count} lần. Cần ít nhất 5 lần để đề xuất maxDistance.",
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
                    $"maxDistance đề xuất: {suggested:F3}\n" +
                    "(với giá trị này, 80% lần vẽ của bạn đạt từ 70% trở lên)",
                    MessageType.None);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Áp dụng vào template"))
                {
                    Undo.RecordObject(_loaded, "Set maxDistance");
                    _loaded.maxDistance = Mathf.Round(suggested * 1000f) / 1000f;
                    EditorUtility.SetDirty(_loaded);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Xóa lịch sử"))
                {
                    _testDistances.Clear();
                    _lastDistance = _lastScore = -1f;
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

            DrawStrokes(canvas, sketch);
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

            if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                s.Drawing = true;
                AddPoint(canvas, s, e.mousePosition, force: true);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && s.Drawing)
            {
                AddPoint(canvas, s, e.mousePosition, force: false);
                e.Use();
                Repaint();
            }
            else if ((e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) && s.Drawing)
            {
                // Nhấc bút = hết một nét
                s.Drawing = false;
                s.StrokeId++;
                e.Use();
                Repaint();
            }
        }

        private static void AddPoint(Rect canvas, Sketch s, Vector2 mouse, bool force)
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

            s.Points.Add(new Point(x, y, s.StrokeId));
        }

        private static Vector3 ToScreen(Rect canvas, Point p)
            => new Vector3(canvas.x + p.X, canvas.y + (CanvasSize - p.Y), 0f);

        private static void DrawStrokes(Rect canvas, Sketch s)
        {
            Handles.color = new Color(0.08f, 0.08f, 0.08f);

            var line = new List<Vector3>();
            int currentId = int.MinValue;

            foreach (Point p in s.Points)
            {
                if (p.StrokeID != currentId)
                {
                    FlushLine(line);
                    currentId = p.StrokeID;
                }
                line.Add(ToScreen(canvas, p));
            }
            FlushLine(line);
        }

        private static void FlushLine(List<Vector3> line)
        {
            if (line.Count >= 2)
                Handles.DrawAAPolyLine(3.5f, line.ToArray());
            else if (line.Count == 1)
                Handles.DrawSolidDisc(line[0], Vector3.forward, 2.5f);

            line.Clear();
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
                            $"{(pts == null ? 0 : pts.Length)} điểm)";
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
                    saved = asset.AddVariant(points, _record.StrokeCount);
                    // Chưa nạp asset này thì các ô trong cửa sổ không phải của nó —
                    // giữ nguyên maxDistance đã hiệu chỉnh, không đè bằng giá trị mặc định.
                    writeInfo = _editing == asset;
                }
                else
                {
                    asset.SetVariant(target, points, _record.StrokeCount);
                    saved = target;
                }
            }
            else
            {
                asset = CreateInstance<IngredientTemplate>();
                AssetDatabase.CreateAsset(asset, path);
                asset.SetVariant(0, points, _record.StrokeCount);
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
