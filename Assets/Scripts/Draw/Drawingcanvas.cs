using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ColorFood.Recognition;

namespace ColorFood.Drawing
{
    /// <summary>
    /// Canvas vẽ. Ghi ĐỒNG THỜI hai thứ:
    ///   1. Texture2D  — để người chơi nhìn thấy nét vẽ
    ///   2. Point cloud — để $P chấm điểm
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
        [Header("Kích thước canvas (pixel)")]
        [SerializeField] private int textureSize = 512;

        [Header("Bút vẽ")]
        [SerializeField] private Color brushColor = Color.black;
        [SerializeField, Range(1, 20)] private int brushRadius = 4;

        [Header("Lọc điểm")]
        [Tooltip("Hai điểm gần hơn khoảng này thì bỏ qua — giảm nhiễu khi vẽ chậm.")]
        [SerializeField] private float minPointDistance = 3f;

        [Header("Debug")]
        [SerializeField] private bool logOnEvaluate = true;

        // --- trạng thái ---
        private RawImage _image;
        private Texture2D _texture;
        private Color32[] _pixels;          // buffer, nhanh hơn SetPixel từng cái
        private bool _dirty;

        private readonly List<Point> _points = new List<Point>();
        private int _strokeId;
        private Vector2 _lastCanvasPos;
        private bool _hasLastPos;

        /// <summary>Point cloud thô của nét vẽ hiện tại. Rỗng nếu chưa vẽ gì.</summary>
        public IReadOnlyList<Point> RawPoints => _points;

        public int StrokeCount => _strokeId + (_hasLastPos ? 1 : 0);

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

            _hasLastPos = false;
            AddPoint(pos);
        }

        public void OnDrag(PointerEventData e)
        {
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
                _pixels[i] = transparent;

            _points.Clear();
            _strokeId = 0;
            _hasLastPos = false;
            _dirty = true;
        }

        public void SetBrushColor(Color c) => brushColor = c;

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