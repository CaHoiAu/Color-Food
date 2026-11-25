using System.Collections.Generic;
using UnityEngine;
using ColorFood.Data;
using ColorFood.Drawing;
using ColorFood.Recognition;

namespace ColorFood.Game
{
    /// <summary>
    /// Vòng chơi: NHẬN ĐƠN MÓN → VẼ TỪNG NGUYÊN LIỆU → KÉO TỚI ĐẦU BẾP → NẤU → KẾT QUẢ + XU.
    ///
    ///   0. Level (LevelDefinition): các nguyên liệu + các món của level. Bức vẽ CHỈ được so
    ///      với nguyên liệu của level hiện tại. Làm đạt đủ dishesToComplete món thì sang
    ///      level sau. Level hiện tại và tiến độ lưu bằng PlayerPrefs.
    ///   1. Order: chọn ngẫu nhiên một món của level — không lặp món vừa xong —
    ///      hiện tên món và các nguyên liệu cần vẽ (kèm hình mẫu).
    ///   2. Drawing: vẽ lần lượt từng nguyên liệu trên DrawingCanvas. Bấm Done → chấm
    ///      (ScoreSketch, so cả với các nguyên liệu khác CỦA LEVEL).
    ///   3. Judged: ĐẠT → bức vẽ biến thành DẠNG HOÀN THIỆN (SketchCleaner: tô phẳng,
    ///      không còn nét sáp, có viền). CHƯA ĐẠT → giữ nguyên nét sáp; người chơi vẽ
    ///      lại hoặc giữ nguyên để đi tiếp.
    ///   4. Kitchen: vẽ xong mọi nguyên liệu → kéo từng nguyên liệu (khay dưới) thả vào
    ///      đầu bếp. Đủ hết thì bắt đầu nấu.
    ///   5. Cooking → DishResult: món đạt khi MỌI nguyên liệu đạt (OrderRules.Dish); sao
    ///      theo điểm trung bình; xu = sao × coinsPerStar × số nguyên liệu, lưu PlayerPrefs.
    ///
    /// Mọi chữ người chơi thấy đều bằng tiếng Anh (tên món / nguyên liệu lấy từ asset —
    /// hãy đặt dishName và displayName bằng tiếng Anh). Log trong Console giữ tiếng Việt.
    ///
    /// Giao diện IMGUI (OnGUI) theo khung 1920×1080, co theo màn hình; không cần dựng UI
    /// trong scene. CÁCH DÙNG: gắn vào một GameObject rỗng trong scene đã có DrawingCanvas
    /// (gán Palette) và EventSystem; kéo DrawingCanvas vào ô Canvas, các Level vào ô Levels
    /// (theo thứ tự). Không có Level nào thì dùng ô Recipes như một level duy nhất.
    /// PHÍM: Enter = nút chính của màn hiện tại.
    /// </summary>
    public class GameLoop : MonoBehaviour
    {
        private enum State { Order, Drawing, Judged, Kitchen, Cooking, DishResult }

        /// <summary>Một nguyên liệu trong món đang làm.</summary>
        private class Slot
        {
            public IngredientTemplate Template;
            public SketchResult Result;
            public bool Passed;
            public Texture2D Crayon;      // bức vẽ như người chơi vẽ (nét sáp)
            public Texture2D Finished;    // dạng hoàn thiện (chỉ khi đạt)
            public Texture2D Hint;        // hình mẫu
            public Rect HintUv = new Rect(0, 0, 1, 1);
            public Rect Uv = new Rect(0, 0, 1, 1);   // khung chứa hình trong texture (cho hình thu nhỏ)
            public bool Delivered;
            public List<string> Feedback = new List<string>();

            public Texture2D Shown => Passed && Finished != null ? Finished : Crayon;
            public string Name => Template != null ? Template.DisplayNameOrId : "-";
        }

        [SerializeField] private DrawingCanvas canvas;
        [Tooltip("Các level theo thứ tự chơi.")]
        [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();
        [Tooltip("Chỉ dùng khi chưa có Level nào: coi các món này là một level duy nhất.")]
        [SerializeField] private List<RecipeDefinition> recipes = new List<RecipeDefinition>();

        [Header("Rules")]
        [SerializeField, Range(0f, 1f)] private float passThreshold = OrderRules.DefaultPass;
        [SerializeField] private int coinsPerStar = OrderRules.DefaultCoinsPerStar;
        [SerializeField] private float cookSeconds = 2f;

        [Header("Look")]
        [Tooltip("Hình nhân vật đầu bếp (tùy chọn). Trống = vẽ khung thay thế.")]
        [SerializeField] private Texture2D chefTexture;
        [Tooltip("Hiện hình mẫu nhỏ ở góc khi vẽ.")]
        [SerializeField] private bool showHint = true;
        [Tooltip("Font cho chữ (tùy chọn). Trống = font mặc định của Unity.")]
        [SerializeField] private Font uiFont;
        [Tooltip("Tắt ColoringTester / DrawingTester trong scene để không tranh phím và giao diện.")]
        [SerializeField] private bool disableTesters = true;

        private const string CoinsKey = "ColorFood.Coins";
        private const string LevelKey = "ColorFood.Level";
        private const string ProgressKey = "ColorFood.LevelProgress";
        private const float RefW = 1920f, RefH = 1080f;

        private State _state;
        private RecipeDefinition _recipe;
        private int _lastRecipe = -1;
        private readonly List<Slot> _slots = new List<Slot>();
        private int _current;                 // nguyên liệu đang vẽ
        // Level hiện tại
        private int _level;
        private int _levelDone;                // số món đã làm đạt trong level
        private bool _levelCleared;            // vừa đủ số món để qua level
        private List<RecipeDefinition> _recipes = new List<RecipeDefinition>();
        private List<IngredientTemplate> _pool = new List<IngredientTemplate>();   // chỉ so với các nguyên liệu này
        private LevelDefinition CurrentLevel => _level >= 0 && _level < levels.Count ? levels[_level] : null;

        private int _coins;
        private DishOutcome _dish;
        private bool _paid;
        private float _cookStart;
        private string _message = "";

        // kéo thả trong bếp
        private int _dragging = -1;
        private Vector2 _dragPos;

        private float _w, _h;                 // kích thước màn hình theo hệ tọa độ GUI (sau khi co)
        private GUIStyle _title, _text, _big, _box, _button, _swatch, _toggle;

        private ColorPaletteAsset Palette => canvas != null ? canvas.Palette : null;
        private Slot Current => _current >= 0 && _current < _slots.Count ? _slots[_current] : null;

        // ------------------------------------------------------------------
        // Vòng chơi
        // ------------------------------------------------------------------

        private void Start()
        {
            if (canvas == null || Palette == null || Palette.Count == 0)
            {
                Debug.LogError("[GameLoop] Cần gán DrawingCanvas (đã gán Palette) vào ô Canvas.");
                enabled = false;
                return;
            }
            levels = levels.FindAll(l => l != null && l.ValidRecipes.Count > 0);
            recipes = recipes.FindAll(r => r != null && r.IsValid);
            if (levels.Count == 0 && recipes.Count == 0)
            {
                Debug.LogError("[GameLoop] Chưa có Level (hoặc Recipe) hợp lệ nào — mỗi món cần ít nhất một nguyên liệu đã vẽ có màu.");
                enabled = false;
                return;
            }

            if (disableTesters)
            {
                foreach (var t in FindObjectsByType<ColoringTester>(FindObjectsSortMode.None)) t.enabled = false;
                foreach (var t in FindObjectsByType<DrawingTester>(FindObjectsSortMode.None)) t.enabled = false;
            }

            _coins = PlayerPrefs.GetInt(CoinsKey, 0);
            LoadLevel(Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0), 0, Mathf.Max(0, levels.Count - 1)),
                      PlayerPrefs.GetInt(ProgressKey, 0));
            NextOrder();
        }

        /// <summary>Nạp level: món của level + tập nguyên liệu để so (chỉ nguyên liệu của level).</summary>
        private void LoadLevel(int index, int done)
        {
            _level = index;
            _levelDone = done;
            _levelCleared = false;
            _lastRecipe = -1;
            if (CurrentLevel != null)
            {
                _recipes = CurrentLevel.ValidRecipes;
                _pool = CurrentLevel.Pool();
            }
            else
            {
                _recipes = recipes;
                _pool = new List<IngredientTemplate>();
                foreach (var r in recipes)
                    foreach (var t in r.ingredients)
                        if (t != null && t.GetSketchShapes().Count > 0 && !_pool.Contains(t)) _pool.Add(t);
            }
            PlayerPrefs.SetInt(LevelKey, _level);
            PlayerPrefs.SetInt(ProgressKey, _levelDone);
            PlayerPrefs.Save();

            var names = new List<string>();
            foreach (var t in _pool) names.Add(t.DisplayNameOrId);
            Debug.Log($"[GameLoop] {LevelName}: chấm chỉ so với {_pool.Count} nguyên liệu — {string.Join(", ", names)}");
        }

        private string LevelName => CurrentLevel != null ? CurrentLevel.levelName : "Level";
        private int DishesToComplete => CurrentLevel != null ? CurrentLevel.dishesToComplete : 0;
        private bool HasNextLevel => _level + 1 < levels.Count;

        /// <summary>Sau màn kết quả: sang level mới nếu vừa qua level, rồi nhận đơn mới.</summary>
        private void AfterDish()
        {
            if (_levelCleared && HasNextLevel) LoadLevel(_level + 1, 0);
            else if (_levelCleared) _levelCleared = false;   // level cuối: chơi tiếp level này
            NextOrder();
        }

        private void NextOrder()
        {
            ReleaseSlots();

            int i = Random.Range(0, _recipes.Count);
            if (_recipes.Count > 1 && i == _lastRecipe) i = (i + 1 + Random.Range(0, _recipes.Count - 1)) % _recipes.Count;
            _lastRecipe = i;
            _recipe = _recipes[i];

            foreach (var t in _recipe.ingredients)
            {
                if (t == null || t.GetSketchShapes().Count == 0) continue;
                var slot = new Slot { Template = t };
                slot.Hint = BuildHint(t, out slot.HintUv);
                _slots.Add(slot);
            }
            _current = 0;
            _paid = false;
            _message = "";

            canvas.Clear();
            canvas.InputEnabled = false;
            canvas.SetVisible(false);
            _state = State.Order;
        }

        private void StartDrawing()
        {
            Slot s = Current;
            if (s == null) return;
            if (s.Crayon != null) Destroy(s.Crayon);
            if (s.Finished != null) Destroy(s.Finished);
            s.Crayon = s.Finished = null;
            s.Result = null;
            s.Passed = false;

            canvas.SetVisible(true);
            canvas.Clear();
            canvas.InputEnabled = true;
            canvas.SetTool(DrawingCanvas.Tool.Crayon);
            canvas.SetColor(DarkestColor());
            _message = "";
            _state = State.Drawing;
        }

        private void Submit()
        {
            Slot s = Current;
            SketchResult r = canvas.ScoreSketch(s.Template, _pool);   // chỉ so với nguyên liệu của level
            if (r == null)
            {
                _message = "Draw something first!";
                return;
            }

            s.Result = r;
            s.Passed = r.Total >= passThreshold;
            s.Feedback = OrderRules.Feedback(r, Palette.Table, s.Name, passThreshold);

            ColorSketch sketch = canvas.BuildSketch();
            s.Uv = ContentUv(sketch);
            s.Crayon = canvas.CaptureTexture();
            if (s.Passed)
            {
                // Đạt: biến bức vẽ thành dạng hoàn thiện ngay trên canvas
                s.Finished = RenderFinished(SketchCleaner.Clean(sketch));
                canvas.ShowOverride(s.Finished);
            }

            Debug.Log($"[GameLoop] {_recipe.dishName} — {s.Name}: {r.Total:P0} " +
                      $"{(s.Passed ? "ĐẠT" : "CHƯA ĐẠT")}\n{r.Report}");

            canvas.InputEnabled = false;
            _state = State.Judged;
        }

        /// <summary>Sang nguyên liệu tiếp theo, hoặc vào bếp nếu đã vẽ hết.</summary>
        private void Continue()
        {
            _current++;
            if (_current < _slots.Count)
            {
                StartDrawing();
                return;
            }
            canvas.SetVisible(false);
            canvas.InputEnabled = false;
            foreach (var s in _slots) s.Delivered = false;
            _dragging = -1;
            _state = State.Kitchen;
        }

        private void StartCooking()
        {
            _cookStart = Time.time;
            _state = State.Cooking;
        }

        private void FinishCooking()
        {
            var totals = new List<float>();
            foreach (var s in _slots) totals.Add(s.Result != null ? s.Result.Total : 0f);
            _dish = OrderRules.Dish(totals, passThreshold, coinsPerStar);
            if (!_paid && _dish.Coins > 0)
            {
                _paid = true;
                _coins += _dish.Coins;
                PlayerPrefs.SetInt(CoinsKey, _coins);
                PlayerPrefs.Save();
            }
            if (_dish.Passed && CurrentLevel != null && !_levelCleared)
            {
                _levelDone++;
                if (_levelDone >= DishesToComplete) _levelCleared = true;
                PlayerPrefs.SetInt(ProgressKey, _levelDone);
                PlayerPrefs.Save();
            }
            Debug.Log($"[GameLoop] Món {_recipe.dishName}: {(_dish.Passed ? "ĐẠT" : "CHƯA ĐẠT")}, " +
                      $"trung bình {_dish.Average:P0}, {_dish.Stars} sao, +{_dish.Coins} xu" +
                      (CurrentLevel != null ? $" | {LevelName}: {_levelDone}/{DishesToComplete}" : ""));
            _state = State.DishResult;
        }

        private void Update()
        {
            if (_state == State.Cooking && Time.time - _cookStart >= cookSeconds) FinishCooking();

            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            switch (_state)
            {
                case State.Order: StartDrawing(); break;
                case State.Drawing: Submit(); break;
                case State.Judged:
                    if (Current.Passed) Continue(); else StartDrawing();
                    break;
                case State.DishResult: AfterDish(); break;
            }
        }

        private void OnDestroy() => ReleaseSlots();

        private void ReleaseSlots()
        {
            foreach (var s in _slots)
            {
                if (s.Crayon != null) Destroy(s.Crayon);
                if (s.Finished != null) Destroy(s.Finished);
                if (s.Hint != null) Destroy(s.Hint);
            }
            _slots.Clear();
        }

        // ------------------------------------------------------------------
        // Hình: mẫu, dạng hoàn thiện, khung chứa hình
        // ------------------------------------------------------------------

        private Texture2D BuildHint(IngredientTemplate t, out Rect uv)
        {
            uv = new Rect(0, 0, 1, 1);
            ColorSketch s = t.GetVariantSketch(0);
            if (s == null && t.IsValid) s = ColorSketch.FromStrokes(t.GetVariantPoints(0));
            if (s == null) return null;
            uv = ContentUv(s);
            return RenderFinished(s);
        }

        /// <summary>
        /// Ảnh màu → texture "hoàn thiện": màu phẳng theo palette, viền ngoài đậm (2 ô),
        /// đường ranh giữa hai màu hơi tối. Phủ đúng khung canvas để thay thế bức vẽ.
        /// </summary>
        private Texture2D RenderFinished(ColorSketch s)
        {
            int n = s.Size;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    int c = s.Cells[i];
                    if (c == ColorSketch.Empty) { pixels[i] = new Color32(0, 0, 0, 0); continue; }

                    Color32 baseColor = c >= 0 && c < Palette.Count ? Palette.ColorOf(c) : new Color32(40, 40, 40, 255);
                    bool outer = false, seam = false;
                    for (int dy = -2; dy <= 2 && !outer; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            int nc = nx < 0 || ny < 0 || nx >= n || ny >= n ? ColorSketch.Empty : s.Cells[ny * n + nx];
                            if (nc == ColorSketch.Empty) { outer = true; break; }
                            if (nc != c && dx * dx + dy * dy <= 1) seam = true;
                        }
                    float k = outer ? 0.45f : seam ? 0.75f : 1f;
                    pixels[i] = new Color32((byte)(baseColor.r * k), (byte)(baseColor.g * k), (byte)(baseColor.b * k), 255);
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);   // hàng 0 = dưới cùng, khớp hệ trục Y-lên của ảnh màu và canvas
            tex.Apply();
            return tex;
        }

        /// <summary>Khung (UV 0..1) bao phần có màu, chừa lề — để hình thu nhỏ chỉ hiện nguyên liệu.</summary>
        private static Rect ContentUv(ColorSketch s)
        {
            int n = s.Size, minX = n, minY = n, maxX = -1, maxY = -1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (s.Cells[y * n + x] != ColorSketch.Empty)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return new Rect(0, 0, 1, 1);
            float side = Mathf.Max(maxX - minX, maxY - minY) * 1.15f + 4;
            float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
            return new Rect((cx - side / 2) / n, (cy - side / 2) / n, side / n, side / n);
        }

        private int DarkestColor()
        {
            int best = 0;
            float bestLum = float.MaxValue;
            for (int i = 0; i < Palette.Count; i++)
            {
                Color32 c = Palette.ColorOf(i);
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                if (lum < bestLum) { bestLum = lum; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Giao diện (IMGUI)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (canvas == null || Palette == null || _recipe == null) return;
            EnsureStyles();

            float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _w = Screen.width / scale;
            _h = Screen.height / scale;

            GUI.Label(new Rect(_w - 340, 16, 320, 50), "Coins: " + _coins, _big);
            if (CurrentLevel != null)
                GUI.Label(new Rect(_w - 340, 60, 320, 40),
                          LevelName + "  " + Mathf.Min(_levelDone, DishesToComplete) + "/" + DishesToComplete + " dishes", _text);

            switch (_state)
            {
                case State.Order: DrawOrder(); break;
                case State.Drawing: DrawDrawing(); break;
                case State.Judged: DrawJudged(); break;
                case State.Kitchen: DrawKitchen(); break;
                case State.Cooking: DrawCooking(); break;
                case State.DishResult: DrawDishResult(); break;
            }
            GUI.matrix = Matrix4x4.identity;
        }

        private void DrawOrder()
        {
            Texture2D dish = _recipe.dishImage;
            var box = new Rect(_w / 2 - 520, 60, 1040, dish != null ? 960 : 760);
            GUI.Box(box, GUIContent.none, _box);
            GUI.Label(new Rect(box.x, box.y + 24, box.width, 56),
                      CurrentLevel != null ? LevelName + " — NEW ORDER!" : "NEW ORDER!", _title);
            GUI.Label(new Rect(box.x, box.y + 90, box.width, 50), _recipe.dishName, _big);

            float y = box.y + 150;
            if (dish != null)
            {
                GUI.DrawTexture(new Rect(box.center.x - 150, y, 300, 300), dish, ScaleMode.ScaleToFit);
                y += 320;
            }
            GUI.Label(new Rect(box.x, y, box.width, 40), "Draw these ingredients:", _text);
            y += 56;

            float max = dish != null ? 190f : 260f;
            float size = Mathf.Min(max, (box.width - 80) / Mathf.Max(1, _slots.Count) - 20);
            float x = box.center.x - (_slots.Count * (size + 20) - 20) / 2f;
            foreach (var s in _slots)
            {
                var r = new Rect(x, y, size, size);
                GUI.Box(r, GUIContent.none, _box);
                if (s.Hint != null) GUI.DrawTextureWithTexCoords(Inset(r, 10), s.Hint, s.HintUv);
                GUI.Label(new Rect(r.x - 20, r.yMax + 8, r.width + 40, 40), s.Name, _big);
                x += size + 20;
            }
            if (GUI.Button(new Rect(box.center.x - 170, box.yMax - 100, 340, 70), "Start drawing (Enter)", _button))
                StartDrawing();
        }

        private void DrawDrawing()
        {
            Slot s = Current;
            GUI.Label(new Rect(_w / 2 - 500, 16, 1000, 56),
                      $"Draw: {s.Name}  ({_current + 1}/{_slots.Count})", _title);

            // Bảng màu + công cụ (bên trái)
            const int cols = 2, bw = 120, bh = 46, pad = 8;
            var oldBg = GUI.backgroundColor;
            var oldContent = GUI.contentColor;
            for (int i = 0; i < Palette.Count; i++)
            {
                var r = new Rect(16 + (i % cols) * (bw + pad), 90 + (i / cols) * (bh + pad), bw, bh);
                Color32 c32 = Palette.ColorOf(i);
                var c = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 1f);
                GUI.backgroundColor = c;
                GUI.contentColor = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b > 0.55f ? Color.black : Color.white;
                string label = (i == canvas.CurrentColorId ? "> " : "") + OrderRules.ColorName(Palette.Table, i);
                if (GUI.Button(r, label, _swatch)) canvas.SetColor(i);
            }
            GUI.backgroundColor = oldBg;
            GUI.contentColor = oldContent;

            float y = 90 + (Palette.Count + cols - 1) / cols * (bh + pad) + 12;
            Tool(new Rect(16, y, 2 * bw + pad, 52), DrawingCanvas.Tool.Crayon, "Crayon");
            Tool(new Rect(16, y + 60, 2 * bw + pad, 52), DrawingCanvas.Tool.Fill, "Paint bucket");

            // Hình mẫu (bên phải)
            showHint = GUI.Toggle(new Rect(_w - 340, 80, 320, 36), showHint, " Show reference", _toggle);
            if (showHint && s.Hint != null)
            {
                var r = new Rect(_w - 340, 122, 320, 320);
                GUI.Box(r, GUIContent.none, _box);
                GUI.DrawTextureWithTexCoords(Inset(r, 10), s.Hint, s.HintUv);
            }

            if (GUI.Button(new Rect(_w / 2 - 340, _h - 110, 300, 72), "Clear", _button)) canvas.Clear();
            if (GUI.Button(new Rect(_w / 2 + 40, _h - 110, 300, 72), "Done (Enter)", _button)) Submit();
            if (_message.Length > 0) GUI.Label(new Rect(_w / 2 - 400, _h - 160, 800, 44), _message, _big);
        }

        private void DrawJudged()
        {
            Slot s = Current;
            SketchResult r = s.Result;
            var box = new Rect(_w - 640, 90, 620, 880);
            GUI.Box(box, GUIContent.none, _box);

            GUI.Label(new Rect(box.x, box.y + 20, box.width, 50), s.Name, _big);
            GUI.Label(new Rect(box.x, box.y + 76, box.width, 56), s.Passed ? "GREAT!" : "NOT QUITE", _title);
            GUI.Label(new Rect(box.x, box.y + 136, box.width, 44), "Score: " + Pct(r.Total), _big);

            float y = box.y + 200;
            Bar(new Rect(box.x + 40, y, box.width - 80, 36), "Shape", r.Shape); y += 50;
            if (r.DetailParts.Count > 0) { Bar(new Rect(box.x + 40, y, box.width - 80, 36), "Details", r.Detail); y += 50; }
            if (r.HasColor) { Bar(new Rect(box.x + 40, y, box.width - 80, 36), "Color", r.Color); y += 50; }

            y += 10;
            foreach (string f in s.Feedback)
            {
                GUI.Label(new Rect(box.x + 40, y, box.width - 80, 64), "• " + f, _text);
                y += 64;
            }

            bool last = _current == _slots.Count - 1;
            string next = last ? "Go to the kitchen" : "Next ingredient";
            if (s.Passed)
            {
                if (GUI.Button(new Rect(box.center.x - 200, box.yMax - 96, 400, 70), next + " (Enter)", _button)) Continue();
            }
            else
            {
                if (GUI.Button(new Rect(box.x + 30, box.yMax - 96, 260, 70), "Redraw (Enter)", _button)) StartDrawing();
                if (GUI.Button(new Rect(box.xMax - 310, box.yMax - 96, 280, 70), "Keep it", _button)) Continue();
            }
        }

        private Rect ChefRect => new Rect(_w - 620, 160, 460, 560);

        private void DrawChef(string caption)
        {
            Rect chef = ChefRect;
            if (chefTexture != null) GUI.DrawTexture(chef, chefTexture, ScaleMode.ScaleToFit);
            else
            {
                GUI.Box(chef, GUIContent.none, _box);
                GUI.DrawTexture(new Rect(chef.center.x - 110, chef.y + 30, 220, 110), Texture2D.whiteTexture,
                                ScaleMode.StretchToFill, false, 0, Color.white, 0, 30);          // mũ đầu bếp
                GUI.DrawTexture(new Rect(chef.center.x - 120, chef.y + 140, 240, 240), Texture2D.whiteTexture,
                                ScaleMode.StretchToFill, false, 0, new Color(1f, 0.85f, 0.7f), 0, 120);   // mặt
                GUI.Label(new Rect(chef.x, chef.y + 400, chef.width, 60), "CHEF", _title);
            }
            GUI.Label(new Rect(chef.x - 60, chef.yMax + 10, chef.width + 120, 50), caption, _big);
        }

        private void DrawKitchen()
        {
            GUI.Label(new Rect(_w / 2 - 600, 30, 1200, 60), "Bring the ingredients to the chef!", _title);
            GUI.Label(new Rect(_w / 2 - 600, 90, 1200, 44), _recipe.dishName, _big);
            DrawChef("Drop ingredients here");
            if (_recipe.dishImage != null)
            {
                // Mục tiêu: món sẽ nấu
                var goal = new Rect(40, 40, 220, 220);
                GUI.Box(goal, GUIContent.none, _box);
                GUI.DrawTexture(Inset(goal, 12), _recipe.dishImage, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(goal.x, goal.yMax + 6, goal.width, 36), "Goal", _text);
            }

            // Khay nguyên liệu
            float size = 220f;
            Event e = Event.current;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                Rect r = TrayRect(i, size);
                GUI.Box(r, GUIContent.none, _box);
                GUI.Label(new Rect(r.x - 30, r.yMax + 6, r.width + 60, 36), s.Name + (s.Passed ? "" : " (crayon)"), _text);
                if (s.Delivered || i == _dragging) continue;
                if (s.Shown != null) GUI.DrawTextureWithTexCoords(Inset(r, 12), s.Shown, s.Uv);
                if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
                {
                    _dragging = i;
                    _dragPos = e.mousePosition;
                    e.Use();
                }
            }

            // Nguyên liệu đã giao: xếp nhỏ cạnh đầu bếp
            int k = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].Delivered || _slots[i].Shown == null) continue;
                var r = new Rect(ChefRect.x - 150, ChefRect.y + k * 140, 130, 130);
                GUI.DrawTextureWithTexCoords(r, _slots[i].Shown, _slots[i].Uv);
                k++;
            }

            // Đang kéo
            if (_dragging >= 0)
            {
                if (e.type == EventType.MouseDrag) { _dragPos = e.mousePosition; e.Use(); }
                Slot s = _slots[_dragging];
                if (s.Shown != null)
                    GUI.DrawTextureWithTexCoords(new Rect(_dragPos.x - size / 2, _dragPos.y - size / 2, size, size), s.Shown, s.Uv);
                if (e.rawType == EventType.MouseUp)
                {
                    if (ChefRect.Contains(e.mousePosition)) s.Delivered = true;
                    _dragging = -1;
                    if (_slots.TrueForAll(x => x.Delivered)) StartCooking();
                }
            }
        }

        private Rect TrayRect(int i, float size)
        {
            float total = _slots.Count * (size + 40) - 40;
            float x0 = Mathf.Max(40, (_w - 700) / 2 - total / 2);
            return new Rect(x0 + i * (size + 40), _h - size - 120, size, size);
        }

        private void DrawCooking()
        {
            GUI.Label(new Rect(_w / 2 - 600, 30, 1200, 60), "Cooking " + _recipe.dishName + "...", _title);
            DrawChef("");
            float t = Mathf.Clamp01((Time.time - _cookStart) / Mathf.Max(0.01f, cookSeconds));
            if (_recipe.dishImage != null)
            {
                // Món hiện dần theo tiến độ nấu
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, t);
                GUI.DrawTexture(new Rect(_w / 2 - 500 + 200, _h / 2 - 340, 300, 300), _recipe.dishImage, ScaleMode.ScaleToFit);
                GUI.color = old;
            }
            var track = new Rect(_w / 2 - 500, _h / 2, 700, 40);
            GUI.DrawTexture(track, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.85f, 0.85f, 0.85f), 0, 8);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture,
                            ScaleMode.StretchToFill, false, 0, new Color(1f, 0.6f, 0.2f), 0, 8);
        }

        private void DrawDishResult()
        {
            Texture2D dish = _recipe.dishImage;
            var box = new Rect(_w / 2 - 560, dish != null ? 40 : 90, 1120, dish != null ? 1000 : 880);
            GUI.Box(box, GUIContent.none, _box);
            GUI.Label(new Rect(box.x, box.y + 20, box.width, 56), _recipe.dishName, _title);
            GUI.Label(new Rect(box.x, box.y + 84, box.width, 56), _dish.Passed ? "DISH COMPLETE!" : "THE DISH DIDN'T WORK OUT", _title);
            GUI.Label(new Rect(box.x, box.y + 146, box.width, 60),
                      new string('★', _dish.Stars) + new string('☆', 3 - _dish.Stars), _title);
            GUI.Label(new Rect(box.x, box.y + 210, box.width, 44),
                      "Average score: " + Pct(_dish.Average) + "    " +
                      (_dish.Coins > 0 ? "+" + _dish.Coins + " coins" : "No coins"), _big);

            float y = box.y + 280;
            if (dish != null)
            {
                // Đạt: món hoàn chỉnh đủ màu. Không đạt: món xám mờ.
                var old = GUI.color;
                GUI.color = _dish.Passed ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.55f);
                GUI.DrawTexture(new Rect(box.center.x - 130, y, 260, 260), dish, ScaleMode.ScaleToFit);
                GUI.color = old;
                y += 280;
            }

            float max = dish != null ? 150f : 240f;
            float size = Mathf.Min(max, (box.width - 80) / Mathf.Max(1, _slots.Count) - 30);
            float x = box.center.x - (_slots.Count * (size + 30) - 30) / 2f;
            foreach (var s in _slots)
            {
                var r = new Rect(x, y, size, size);
                GUI.Box(r, GUIContent.none, _box);
                if (s.Shown != null) GUI.DrawTextureWithTexCoords(Inset(r, 10), s.Shown, s.Uv);
                GUI.Label(new Rect(r.x - 30, r.yMax + 6, r.width + 60, 40), s.Name, _big);
                GUI.Label(new Rect(r.x - 30, r.yMax + 48, r.width + 60, 40),
                          (s.Passed ? "OK " : "Not quite ") + (s.Result != null ? Pct(s.Result.Total) : "-"), _text);
                x += size + 30;
            }
            if (!_dish.Passed)
                GUI.Label(new Rect(box.x + 40, box.yMax - 170, box.width - 80, 60),
                          "Every ingredient must pass for the dish to succeed.", _big);
            else if (_levelCleared)
                GUI.Label(new Rect(box.x + 40, box.yMax - 170, box.width - 80, 60),
                          HasNextLevel ? LevelName + " COMPLETE!" : "ALL LEVELS COMPLETE!", _title);
            string next = _levelCleared && HasNextLevel ? "Next level (Enter)" : "Next order (Enter)";
            if (GUI.Button(new Rect(box.center.x - 180, box.yMax - 96, 360, 70), next, _button))
                AfterDish();
        }

        private void Tool(Rect r, DrawingCanvas.Tool tool, string label)
        {
            bool on = canvas.CurrentTool == tool;
            if (GUI.Toggle(r, on, label, _button) && !on) canvas.SetTool(tool);
        }

        private void Bar(Rect r, string label, float v)
        {
            GUI.Label(new Rect(r.x, r.y, 130, r.height), label, _text);
            var track = new Rect(r.x + 140, r.y + 6, r.width - 230, r.height - 12);
            GUI.DrawTexture(track, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                            new Color(0.85f, 0.85f, 0.85f), 0, 6);
            var fill = new Rect(track.x, track.y, track.width * Mathf.Clamp01(v), track.height);
            var c = v >= 0.7f ? new Color(0.3f, 0.7f, 0.35f) : v >= 0.5f ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.9f, 0.35f, 0.3f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, c, 0, 6);
            GUI.Label(new Rect(r.xMax - 80, r.y, 80, r.height), Pct(v), _text);
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _box = new GUIStyle(GUI.skin.box);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 28 };
            _swatch = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 24 };
            if (uiFont != null)
                foreach (var s in new[] { _text, _big, _title, _button, _swatch, _toggle }) s.font = uiFont;
            _big.normal.textColor = _title.normal.textColor = Color.white;
        }

        private static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);
        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
