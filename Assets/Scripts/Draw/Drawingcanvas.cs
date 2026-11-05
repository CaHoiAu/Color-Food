using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ColorFood.Data;
using ColorFood.Recognition;

namespace ColorFood.Drawing
{
    /// <summary>
    /// Canvas vẽ + tô màu. Ghi ĐỒNG THỜI ba thứ:
    ///   1. Texture2D   — để người chơi nhìn thấy nét vẽ và màu tô
    ///   2. Point cloud — để $P chấm điểm hình
    ///   3. Mặt nạ mực (_ink) và bản đồ màu (_fill) — để đóng nét, đếm vùng kín,
    ///      chấm màu từng bộ phận
    ///
    /// Ba chế độ, cả ba đều dùng MÀU ĐANG CHỌN trong palette:
    ///   • Draw   — kéo để vẽ nét viền (nét đỏ cho quả dâu, nét xanh cho lá).
    ///              Chỉ nét Draw mới vào point cloud ($P) và tạo vùng kín.
    ///   • Crayon — sáp màu dùng cho CẢ viền lẫn tô (như tô màu thật). Nét to, có
    ///              vân giấy. Nhấc tay thì tự đoán (StrokeClassifier): nét nằm trong
    ///              một vùng kín và cùng màu viền vùng đó = NÉT TÔ (không vào $P,
    ///              không chặn vùng); còn lại = NÉT VIỀN (vào $P, tạo vùng kín).
    ///   • Fill   — bấm vào vùng kín để đổ đầy màu (thùng sơn). Chỉ tô được VÙNG
    ///              KÍN; nét hở nhỏ hơn closeGapPixels được tự vá.
    ///
    /// Nên đặt một Image nền xám nhạt phía sau RawImage, nếu không màu trắng
    /// và kem tô lên nền trắng sẽ không nhìn thấy.
    ///
    /// Hai thứ này song song, không thay thế nhau. Texture không dùng được
    /// cho $P (nó cần danh sách điểm có thứ tự stroke), và point cloud
    /// không hiển thị được.
    ///
    /// CÁCH ĐẶT TRONG SCENE:
    ///   Canvas (Screen Space - Overlay)
    ///     └── RawImage  ← gắn script này vào đây
    ///           RectTransform: đặt kích thước vuông, ví dụ 512x512
    ///
    /// Gắn script vào chính GameObject có RawImage.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class DrawingCanvas : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public enum Tool { Draw, Crayon, Fill }

        [Header("Kích thước canvas (pixel)")]
        [SerializeField] private int textureSize = 512;

        [Header("Bút vẽ")]
        [Tooltip("Màu nét khi chưa gán palette. Có palette thì nét vẽ dùng màu đang chọn.")]
        [SerializeField] private Color brushColor = Color.black;
        [SerializeField, Range(1, 20)] private int brushRadius = 4;

        [Header("Sáp màu")]
        [SerializeField, Range(2, 40)] private int crayonRadius = 8;
        [Tooltip("Tỉ lệ pixel để trống trong mỗi nét sáp (vân giấy). Chỉ để nhìn — " +
                 "khi chấm điểm, cả vệt nét sáp được tính là đã tô / là viền.")]
        [SerializeField, Range(0f, 0.8f)] private float crayonGrain = 0.3f;

        [Header("Lọc điểm")]
        [Tooltip("Hai điểm gần hơn khoảng này thì bỏ qua — giảm nhiễu khi vẽ chậm.")]
        [SerializeField] private float minPointDistance = 3f;

        [Header("Tô màu (bước 4)")]
        [SerializeField] private ColorPaletteAsset palette;
        [Tooltip("Khe hở giữa hai đầu nét nhỏ hơn giá trị này (pixel) sẽ được tự vá khi tô/đếm vùng.")]
        [SerializeField] private float closeGapPixels = 25f;
        [Tooltip("Vùng kín nhỏ hơn số pixel này bị bỏ qua (lỗ do nét chồng nhau).")]
        [SerializeField] private int minRegionArea = 40;

        [Header("Debug")]
        [SerializeField] private bool logOnEvaluate = true;

        // --- trạng thái ---
        private RawImage _image;
        private Texture2D _texture;
        private Color32[] _pixels;          // buffer, nhanh hơn SetPixel từng cái
        private bool _dirty;

        private bool[] _ink;                // true = pixel có mực
        private int[] _inkColor;            // ColorID của nét mực từng pixel, -1 = không có / không có palette
        private int[] _fill;                // ColorID từng pixel, -1 = chưa tô

        private readonly List<Point> _points = new List<Point>();
        private int _strokeId;
        private Vector2 _lastCanvasPos;
        private bool _hasLastPos;

        private readonly List<Point> _crayonStroke = new List<Point>();   // nét sáp đang kéo
        private RegionSet _regions;          // vùng kín của các nét viền hiện có (cache)
        private bool[] _regionsBarrier;
        private int[][] _regionOutlineColors;
        private bool _regionsDirty = true;

        /// <summary>Một thao tác, theo đúng thứ tự — để dựng lại dữ liệu chấm điểm sau khi xét lại.</summary>
        private class CanvasEvent
        {
            public DrawnStroke Stroke;     // nét bút / nét sáp
            public int[] BucketPixels;     // hoặc một lần đổ thùng sơn
            public int ColorId;
        }
        private readonly List<CanvasEvent> _events = new List<CanvasEvent>();
        private DrawnStroke _penStroke;    // nét bút đang kéo
        private bool _needsReconcile;      // có nét sáp đang là viền → nên xét lại trước khi chấm

        /// <summary>Point cloud thô của nét vẽ hiện tại. Rỗng nếu chưa vẽ gì.</summary>
        public IReadOnlyList<Point> RawPoints => _points;

        public int StrokeCount => _strokeId + (_hasLastPos ? 1 : 0);

        public Tool CurrentTool { get; private set; } = Tool.Draw;
        public int CurrentColorId { get; private set; }
        public ColorPaletteAsset Palette => palette;

        /// <summary>ColorID từng pixel (-1 = chưa tô). Chỉ đọc — dùng để chấm điểm.</summary>
        public int[] FillIds => _fill;

        /// <summary>ColorID nét viền từng pixel (-1 = không phải mực). Chỉ đọc — dùng để chấm điểm.</summary>
        public int[] InkColorIds => _inkColor;
        public int TextureSize => textureSize;

        // ------------------------------------------------------------------
        // Khởi tạo
        // ------------------------------------------------------------------

        private void Awake()
        {
            _image = GetComponent<RawImage>();

            _texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;

            _pixels = new Color32[textureSize * textureSize];
            _ink = new bool[textureSize * textureSize];
            _inkColor = new int[textureSize * textureSize];
            _fill = new int[textureSize * textureSize];
            _image.texture = _texture;

            Clear();
        }

        private void LateUpdate()
        {
            // Chỉ Apply một lần mỗi frame — Apply() rất tốn, đừng gọi mỗi điểm
            if (_dirty)
            {
                _texture.SetPixels32(_pixels);
                _texture.Apply();
                _dirty = false;
            }
        }

        // ------------------------------------------------------------------
        // Thu thập nét vẽ
        // ------------------------------------------------------------------

        public void OnPointerDown(PointerEventData e)
        {
            if (!TryGetCanvasPosition(e, out Vector2 pos)) return;

            if (CurrentTool == Tool.Fill)
            {
                TryFill(pos);
                return;
            }

            _hasLastPos = false;
            if (CurrentTool == Tool.Crayon)
            {
                _crayonStroke.Clear();
                AddCrayonPoint(pos);
                return;
            }
            _penStroke = new DrawnStroke
            {
                Radius = brushRadius,
                Auto = false,
                ColorId = HasPalette ? CurrentColorId : -1
            };
            AddPoint(pos);
        }

        public void OnDrag(PointerEventData e)
        {
            if (CurrentTool == Tool.Fill) return;
            if (!TryGetCanvasPosition(e, out Vector2 pos)) return;

            // Bỏ qua điểm quá gần điểm trước — giảm nhiễu và số điểm thừa
            if (_hasLastPos && Vector2.Distance(pos, _lastCanvasPos) < minPointDistance)
                return;

            if (CurrentTool == Tool.Crayon)
            {
                AddCrayonPoint(pos);
                return;
            }

            // Nội suy giữa hai frame: nếu máy lag hoặc kéo nhanh, khoảng cách
            // giữa hai frame có thể lớn, để nguyên thì nét vẽ bị đứt đoạn.
            if (_hasLastPos)
                DrawLine(_lastCanvasPos, pos);

            AddPoint(pos);
        }

        public void OnPointerUp(PointerEventData e)
        {
            // Chế độ tô không tạo nét, nên không được tăng StrokeID
            if (CurrentTool == Tool.Fill) return;
            if (CurrentTool == Tool.Crayon)
            {
                CommitCrayonStroke();
                _hasLastPos = false;
                return;
            }

            // Nhấc bút = kết thúc một stroke
            _strokeId++;
            _hasLastPos = false;
            _regionsDirty = true;
            if (_penStroke != null && _penStroke.Points.Count > 0)
                _events.Add(new CanvasEvent { Stroke = _penStroke, ColorId = _penStroke.ColorId });
            _penStroke = null;
        }

        private bool HasPalette => palette != null && palette.Count > 0;

        private void AddCrayonPoint(Vector2 pos)
        {
            if (palette == null || palette.Count == 0)
            {
                Debug.LogWarning("[DrawingCanvas] Chưa gán Palette nên không tô được.");
                return;
            }

            if (_hasLastPos) CrayonLine(_lastCanvasPos, pos);
            else CrayonDot(pos);

            _crayonStroke.Add(new Point(pos.x, pos.y, _strokeId, CurrentColorId));
            _lastCanvasPos = pos;
            _hasLastPos = true;
        }

        /// <summary>
        /// Nhấc tay khỏi nét sáp: đoán viền hay tô rồi ghi vào đúng chỗ.
        ///   Tô  → _fill (để chấm màu), không đụng point cloud / vùng kín.
        ///   Viền → point cloud ($P) + mặt nạ mực (tạo vùng kín, màu viền).
        /// </summary>
        private void CommitCrayonStroke()
        {
            if (_crayonStroke.Count == 0) return;

            bool isFill = false;
            if (_points.Count > 0)
            {
                if (_regionsDirty)
                {
                    _regionsBarrier = BuildBarrier(out _);
                    _regions = RegionAnalyzer.Analyze(_regionsBarrier, textureSize, textureSize, minRegionArea);
                    _regionOutlineColors = StrokeClassifier.RegionOutlineColors(_regions, _inkColor, palette.Count);
                    _regionsDirty = false;
                }
                isFill = StrokeClassifier.IsFill(_crayonStroke, CurrentColorId, _regions,
                                                 _regionOutlineColors, palette.Table,
                                                 barrier: _regionsBarrier);
            }

            StampStroke(_crayonStroke, !isFill, crayonRadius, CurrentColorId);
            if (!isFill)
            {
                _points.AddRange(_crayonStroke);
                _strokeId++;
                _regionsDirty = true;
                _needsReconcile = true;
            }

            _events.Add(new CanvasEvent
            {
                ColorId = CurrentColorId,
                Stroke = new DrawnStroke
                {
                    Points = new List<Point>(_crayonStroke),
                    ColorId = CurrentColorId,
                    Radius = crayonRadius,
                    IsFill = isFill,
                    Auto = true
                }
            });

            if (logOnEvaluate)
                Debug.Log($"[DrawingCanvas] Nét sáp {palette.NameOf(CurrentColorId)} → {(isFill ? "TÔ" : "VIỀN")}");
            _crayonStroke.Clear();
        }

        /// <summary>Ghi cả vệt nét vào mặt nạ mực (asInk) hoặc bản đồ màu tô. Không vẽ lên màn hình.</summary>
        private void StampStroke(List<Point> stroke, bool asInk, int radius, int colorId)
        {
            StampDisc(stroke[0].X, stroke[0].Y, asInk, radius, colorId);
            for (int k = 1; k < stroke.Count; k++)
            {
                var a = new Vector2(stroke[k - 1].X, stroke[k - 1].Y);
                var b = new Vector2(stroke[k].X, stroke[k].Y);
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / Mathf.Max(1f, radius * 0.5f));
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                    StampDisc(p.x, p.y, asInk, radius, colorId);
                }
            }
        }

        private void StampDisc(float fx, float fy, bool asInk, int radius, int colorId)
        {
            int cx = Mathf.RoundToInt(fx), cy = Mathf.RoundToInt(fy);
            int r2 = radius * radius;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > r2) continue;

                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= textureSize || py < 0 || py >= textureSize) continue;

                    int i = py * textureSize + px;
                    if (asInk)
                    {
                        _ink[i] = true;
                        _inkColor[i] = colorId;
                        _fill[i] = -1;
                    }
                    else if (!_ink[i]) _fill[i] = colorId;
                }
            }
        }

        private void AddPoint(Vector2 canvasPos)
        {
            var point = new Point(canvasPos.x, canvasPos.y, _strokeId, CurrentColorId);
            _points.Add(point);
            _penStroke?.Points.Add(point);
            DrawDot(canvasPos);

            _lastCanvasPos = canvasPos;
            _hasLastPos = true;
        }

        /// <summary>
        /// Chuyển tọa độ màn hình sang tọa độ pixel trên texture (gốc dưới-trái).
        /// </summary>
        private bool TryGetCanvasPosition(PointerEventData e, out Vector2 result)
        {
            result = default;

            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, e.position, e.pressEventCamera, out Vector2 local))
                return false;

            // local có gốc ở pivot; chuyển về [0..1] rồi nhân lên kích thước texture
            Rect r = rt.rect;
            float nx = (local.x - r.x) / r.width;
            float ny = (local.y - r.y) / r.height;

            if (nx < 0f || nx > 1f || ny < 0f || ny > 1f) return false;

            result = new Vector2(nx * textureSize, ny * textureSize);
            return true;
        }

        // ------------------------------------------------------------------
        // Vẽ lên texture
        // ------------------------------------------------------------------

        private void DrawDot(Vector2 p)
        {
            int cx = Mathf.RoundToInt(p.x);
            int cy = Mathf.RoundToInt(p.y);
            int r2 = brushRadius * brushRadius;
            bool hasPalette = palette != null && palette.Count > 0;
            var c = hasPalette ? palette.ColorOf(CurrentColorId) : (Color32)brushColor;
            int colorId = hasPalette ? CurrentColorId : -1;

            for (int y = -brushRadius; y <= brushRadius; y++)
            {
                for (int x = -brushRadius; x <= brushRadius; x++)
                {
                    if (x * x + y * y > r2) continue;

                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= textureSize || py < 0 || py >= textureSize) continue;

                    int i = py * textureSize + px;
                    _pixels[i] = c;
                    _ink[i] = true;
                    _inkColor[i] = colorId;
                    _fill[i] = -1;     // nét viền đè lên chỗ đã tô: pixel này giờ là mực
                }
            }
            _dirty = true;
        }

        /// <summary>
        /// Một chấm sáp CHỈ ĐỂ NHÌN: vẽ lên màn hình các pixel không rơi vào "vân
        /// giấy". Việc ghi vào mặt nạ mực / bản đồ màu làm lúc nhấc tay (CommitCrayonStroke).
        /// </summary>
        private void CrayonDot(Vector2 p)
        {
            int cx = Mathf.RoundToInt(p.x);
            int cy = Mathf.RoundToInt(p.y);
            int r2 = crayonRadius * crayonRadius;
            Color32 c = palette.ColorOf(CurrentColorId);

            for (int y = -crayonRadius; y <= crayonRadius; y++)
            {
                for (int x = -crayonRadius; x <= crayonRadius; x++)
                {
                    if (x * x + y * y > r2) continue;

                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= textureSize || py < 0 || py >= textureSize) continue;

                    if (Grain(px, py) >= crayonGrain) _pixels[py * textureSize + px] = c;
                }
            }
            _dirty = true;
        }

        private void CrayonLine(Vector2 a, Vector2 b)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(dist / (crayonRadius * 0.5f));

            for (int i = 1; i <= steps; i++)
                CrayonDot(Vector2.Lerp(a, b, i / (float)steps));
        }

        /// <summary>Vân giấy: số giả ngẫu nhiên 0..1 cố định theo vị trí pixel.</summary>
        private static float Grain(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        /// <summary>Nối hai điểm bằng các chấm liên tiếp (Bresenham đơn giản hóa).</summary>
        private void DrawLine(Vector2 a, Vector2 b)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(dist / (brushRadius * 0.5f));

            for (int i = 1; i <= steps; i++)
                DrawDot(Vector2.Lerp(a, b, i / (float)steps));
        }

        // ------------------------------------------------------------------
        // API công khai
        // ------------------------------------------------------------------

        public void Clear()
        {
            var transparent = new Color32(0, 0, 0, 0);
            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = transparent;
                _ink[i] = false;
                _inkColor[i] = -1;
                _fill[i] = -1;
            }

            _points.Clear();
            _crayonStroke.Clear();
            _events.Clear();
            _penStroke = null;
            _needsReconcile = false;
            _strokeId = 0;
            _hasLastPos = false;
            _regionsDirty = true;
            _dirty = true;
        }

        public void SetBrushColor(Color c) => brushColor = c;

        public void SetTool(Tool tool) => CurrentTool = tool;

        /// <summary>Chọn màu tô theo ColorID trong palette.</summary>
        public void SetColor(int colorId)
        {
            if (palette == null || palette.Count == 0) { CurrentColorId = 0; return; }
            CurrentColorId = Mathf.Clamp(colorId, 0, palette.Count - 1);
        }

        // ------------------------------------------------------------------
        // Tô màu, đóng nét, phân tích vùng
        // ------------------------------------------------------------------

        /// <summary>
        /// Vật cản = mực + các đoạn vá khe hở. Đoạn vá chỉ tồn tại trong mặt nạ
        /// này, không vẽ lên màn hình và không đổi point cloud của $P.
        /// closureOnly (nếu cần) nhận các pixel chỉ thuộc đoạn vá.
        /// </summary>
        private bool[] BuildBarrier(out bool[] closure)
        {
            int n = textureSize * textureSize;
            closure = new bool[n];
            foreach (var seg in OutlineCloser.FindClosures(_points, closeGapPixels))
                Raster.DrawSegment(closure, textureSize, textureSize, seg, 2);

            var barrier = new bool[n];
            for (int i = 0; i < n; i++) barrier[i] = _ink[i] || closure[i];
            return barrier;
        }

        private void TryFill(Vector2 pos)
        {
            if (palette == null || palette.Count == 0)
            {
                Debug.LogWarning("[DrawingCanvas] Chưa gán Palette nên không tô được.");
                return;
            }

            bool[] closure;
            bool[] barrier = BuildBarrier(out closure);

            List<int> region = FloodFill.FindRegion(barrier, textureSize, textureSize,
                                                    (int)pos.x, (int)pos.y, out bool touchesBorder);

            if (region.Count == 0) return;   // bấm trúng nét mực

            if (touchesBorder)
            {
                Debug.LogWarning("[DrawingCanvas] Vùng này hở (chạm mép canvas) nên không tô. " +
                                 "Hãy vẽ khép kín nét.");
                return;
            }

            Color32 c = palette.ColorOf(CurrentColorId);
            var inRegion = new bool[_fill.Length];
            var filled = new List<int>(region);
            foreach (int idx in region)
            {
                inRegion[idx] = true;
                _fill[idx] = CurrentColorId;
                if (!_ink[idx]) _pixels[idx] = c;
            }

            // Tô luôn các pixel của đoạn vá kề vùng, để không còn đường trắng ở chỗ vá
            for (int y = 1; y < textureSize - 1; y++)
            {
                for (int x = 1; x < textureSize - 1; x++)
                {
                    int i = y * textureSize + x;
                    if (!closure[i] || _ink[i]) continue;
                    if (inRegion[i - 1] || inRegion[i + 1] ||
                        inRegion[i - textureSize] || inRegion[i + textureSize])
                    {
                        _fill[i] = CurrentColorId;
                        _pixels[i] = c;
                        filled.Add(i);
                    }
                }
            }
            _dirty = true;
            _events.Add(new CanvasEvent { BucketPixels = filled.ToArray(), ColorId = CurrentColorId });
        }

        /// <summary>
        /// Xét lại mọi nét sáp đang là viền (StrokeClassifier.Reconcile) — sửa các nét
        /// tô được vẽ TRƯỚC khi viền khép kín. Có nét đổi thì dựng lại mặt nạ mực, bản
        /// đồ màu tô và point cloud từ danh sách thao tác. Màn hình giữ nguyên (nét
        /// viền và nét tô trông giống nhau). Gọi tự động trước khi chấm điểm.
        /// </summary>
        public void EnsureReconciled()
        {
            if (!_needsReconcile || !HasPalette) return;
            _needsReconcile = false;

            var strokes = new List<DrawnStroke>();
            foreach (var ev in _events) if (ev.Stroke != null) strokes.Add(ev.Stroke);

            int before = 0;
            foreach (var st in strokes) if (st.IsFill) before++;
            if (!StrokeClassifier.Reconcile(strokes, textureSize, textureSize, closeGapPixels,
                                            minRegionArea, palette.Table))
                return;

            int after = 0;
            foreach (var st in strokes) if (st.IsFill) after++;
            RebuildFromEvents();
            if (logOnEvaluate)
                Debug.Log($"[DrawingCanvas] Xét lại: {after - before} nét sáp đổi từ VIỀN sang TÔ.");
        }

        /// <summary>Dựng lại _ink, _inkColor, _fill, _points theo đúng thứ tự thao tác.</summary>
        private void RebuildFromEvents()
        {
            for (int i = 0; i < _ink.Length; i++) { _ink[i] = false; _inkColor[i] = -1; _fill[i] = -1; }
            _points.Clear();
            _strokeId = 0;

            foreach (var ev in _events)
            {
                if (ev.BucketPixels != null)
                {
                    foreach (int i in ev.BucketPixels) if (!_ink[i]) _fill[i] = ev.ColorId;
                    continue;
                }

                DrawnStroke st = ev.Stroke;
                StampStroke(st.Points, !st.IsFill, st.Radius, st.ColorId);
                if (st.IsFill) continue;

                foreach (Point p in st.Points) _points.Add(new Point(p.X, p.Y, _strokeId, st.ColorId));
                _strokeId++;
            }
            _regionsDirty = true;
        }

        /// <summary>Danh sách vùng kín hiện có (đã tính cả đoạn vá khe hở).</summary>
        public RegionSet AnalyzeRegions()
        {
            EnsureReconciled();
            bool[] closure;
            bool[] barrier = BuildBarrier(out closure);
            return RegionAnalyzer.Analyze(barrier, textureSize, textureSize, minRegionArea);
        }

        /// <summary>
        /// Chấm điểm đầy đủ một nguyên liệu: hình ($P) + bộ phận (số lượng, diện tích, màu).
        /// Trả về null nếu chưa vẽ gì / chưa gán template hoặc palette.
        /// </summary>
        public ScoreResult ScoreIngredient(IngredientTemplate template, PDollarRecognizer recognizer,
                                           out float distance)
        {
            distance = -1f;
            if (template == null || !template.IsValid || palette == null || palette.Count == 0)
                return null;

            PointCloud cloud = BuildPointCloud();
            if (cloud == null) return null;

            // Nguyên liệu có nhiều mẫu: so với tất cả, lấy mẫu giống nhất
            distance = recognizer.MatchBest(cloud, template.GetClouds(), out _);
            float shape = PDollarRecognizer.ScoreFromDistance(distance, template.maxDistance);

            return IngredientScorer.Score(shape, AnalyzeRegions(), _fill, palette.Table,
                                          template.shapeWeight, template.parts,
                                          template.missingPartCap,
                                          template.minShape, template.shapeCap,
                                          template.roundnessCap, _inkColor);
        }

        /// <summary>
        /// Tạo PointCloud đã chuẩn hóa từ nét vẽ hiện tại.
        /// Trả về null nếu chưa đủ điểm.
        /// </summary>
        public PointCloud BuildPointCloud(string name = "drawn")
        {
            EnsureReconciled();
            if (_points.Count < 2)
            {
                if (logOnEvaluate) Debug.LogWarning("[DrawingCanvas] Chưa đủ điểm để tạo PointCloud.");
                return null;
            }
            return new PointCloud(name, _points);
        }

        /// <summary>
        /// Chấm điểm nét vẽ hiện tại so với một template.
        /// Trả về -1 nếu chưa vẽ gì.
        /// </summary>
        public float Evaluate(PointCloud template, PDollarRecognizer recognizer, float maxDistance = 0.09f)
        {
            var cloud = BuildPointCloud();
            if (cloud == null || template == null) return -1f;

            float distance = recognizer.Match(cloud, template);
            float score = recognizer.MatchScore(cloud, template, maxDistance);

            if (logOnEvaluate)
            {
                Debug.Log($"[DrawingCanvas] điểm thô: {_points.Count} | stroke: {_strokeId} | " +
                          $"khoảng cách: {distance:F5} | score: {score:P1}");
            }
            return score;
        }
    }
}
