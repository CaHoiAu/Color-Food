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
    /// Hai chế độ: Draw (kéo để vẽ nét) và Fill (bấm vào vùng kín để tô màu
    /// đang chọn). Chỉ tô được VÙNG KÍN; bấm vào vùng hở thì màu rò ra ngoài
    /// nên canvas từ chối (nét hở nhỏ hơn closeGapPixels được tự vá).
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
        public enum Tool { Draw, Fill }

        [Header("Kích thước canvas (pixel)")]
        [SerializeField] private int textureSize = 512;

        [Header("Bút vẽ")]
        [SerializeField] private Color brushColor = Color.black;
        [SerializeField, Range(1, 20)] private int brushRadius = 4;

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
        private int[] _fill;                // ColorID từng pixel, -1 = chưa tô

        private readonly List<Point> _points = new List<Point>();
        private int _strokeId;
        private Vector2 _lastCanvasPos;
        private bool _hasLastPos;

        /// <summary>Point cloud thô của nét vẽ hiện tại. Rỗng nếu chưa vẽ gì.</summary>
        public IReadOnlyList<Point> RawPoints => _points;

        public int StrokeCount => _strokeId + (_hasLastPos ? 1 : 0);

        public Tool CurrentTool { get; private set; } = Tool.Draw;
        public int CurrentColorId { get; private set; }
        public ColorPaletteAsset Palette => palette;

        /// <summary>ColorID từng pixel (-1 = chưa tô). Chỉ đọc — dùng để chấm điểm.</summary>
        public int[] FillIds => _fill;
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
            AddPoint(pos);
        }

        public void OnDrag(PointerEventData e)
        {
            if (CurrentTool == Tool.Fill) return;
            if (!TryGetCanvasPosition(e, out Vector2 pos)) return;

            // Bỏ qua điểm quá gần điểm trước — giảm nhiễu và số điểm thừa
            if (_hasLastPos && Vector2.Distance(pos, _lastCanvasPos) < minPointDistance)
                return;

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

            // Nhấc bút = kết thúc một stroke
            _strokeId++;
            _hasLastPos = false;
        }

        private void AddPoint(Vector2 canvasPos)
        {
            _points.Add(new Point(canvasPos.x, canvasPos.y, _strokeId));
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
            var c = (Color32)brushColor;

            for (int y = -brushRadius; y <= brushRadius; y++)
            {
                for (int x = -brushRadius; x <= brushRadius; x++)
                {
                    if (x * x + y * y > r2) continue;

                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= textureSize || py < 0 || py >= textureSize) continue;

                    _pixels[py * textureSize + px] = c;
                    _ink[py * textureSize + px] = true;
                }
            }
            _dirty = true;
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
                _fill[i] = -1;
            }

            _points.Clear();
            _strokeId = 0;
            _hasLastPos = false;
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
                    }
                }
            }
            _dirty = true;
        }

        /// <summary>Danh sách vùng kín hiện có (đã tính cả đoạn vá khe hở).</summary>
        public RegionSet AnalyzeRegions()
        {
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

            float shape = recognizer.MatchBestScore(cloud, template.GetClouds(), template.maxDistance, out int best);
            distance = recognizer.Match(cloud, template.GetClouds()[best]);

            return IngredientScorer.Score(shape, AnalyzeRegions(), _fill, palette.Table,
                                          template.shapeWeight, template.parts,
                                          template.missingPartCap,
                                          template.minShape, template.shapeCap,
                                          template.roundnessCap);
        }

        /// <summary>
        /// Tạo PointCloud đã chuẩn hóa từ nét vẽ hiện tại.
        /// Trả về null nếu chưa đủ điểm.
        /// </summary>
        public PointCloud BuildPointCloud(string name = "drawn")
        {
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
