using System;
using System.Collections.Generic;
using UnityEngine;
using ColorFood.Recognition;

namespace ColorFood.Drawing
{
    /// <summary>
    /// Script tạm để kiểm chứng toàn bộ luồng: vẽ → chuẩn hóa → chấm điểm.
    /// Sẽ bị thay bằng hệ thống thật sau, nhưng giờ dùng để xác nhận
    /// canvas và $P ghép với nhau đúng.
    ///
    /// CÁCH DÙNG:
    ///   1. Gắn script này vào một GameObject bất kỳ trong scene
    ///   2. Kéo DrawingCanvas vào ô Canvas trong Inspector
    ///   3. Chạy Play, vẽ một hình, nhấn SPACE
    ///   4. Xem kết quả trong Console
    ///
    /// PHÍM:
    ///   SPACE — chấm điểm nét vẽ với template đang chọn
    ///   C     — xóa canvas
    ///   1/2/3 — đổi template (tròn / vuông / tam giác)
    /// </summary>
    public class DrawingTester : MonoBehaviour
    {
        [SerializeField] private DrawingCanvas canvas;

        [Header("Hiệu chỉnh")]
        [Tooltip("Khoảng cách ứng với score = 0. Đo lại khi có dữ liệu vẽ tay thật.")]
        [SerializeField] private float maxDistance = 0.09f;

        [Tooltip("Ngưỡng để coi là vẽ đạt.")]
        [SerializeField, Range(0f, 1f)] private float passThreshold = 0.7f;

        private readonly PDollarRecognizer _recognizer = new PDollarRecognizer();
        private Dictionary<string, PointCloud> _templates;
        private string _current = "circle";

        private void Start()
        {
            if (canvas == null)
            {
                Debug.LogError("[DrawingTester] Chưa gán DrawingCanvas trong Inspector.");
                enabled = false;
                return;
            }

            BuildTemplates();
            Debug.Log($"[DrawingTester] Sẵn sàng. Template: {_current}. " +
                      "SPACE = chấm điểm, C = xóa, 1/2/3 = đổi template.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Evaluate();
            if (Input.GetKeyDown(KeyCode.C)) canvas.Clear();

            if (Input.GetKeyDown(KeyCode.Alpha1)) SetTemplate("circle");
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetTemplate("square");
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetTemplate("triangle");
        }

        private void SetTemplate(string name)
        {
            _current = name;
            Debug.Log($"[DrawingTester] Template hiện tại: {name}");
        }

        private void Evaluate()
        {
            var drawn = canvas.BuildPointCloud();
            if (drawn == null) return;

            Debug.Log($"--- Chấm điểm (template: {_current}) ---");

            // So với TẤT CẢ template để thấy hệ thống phân biệt được không.
            // Trong game thật chỉ so với MỘT template (Guided Shape Matching).
            foreach (var kv in _templates)
            {
                float d = _recognizer.Match(drawn, kv.Value);
                float s = _recognizer.MatchScore(drawn, kv.Value, maxDistance);

                string mark = kv.Key == _current ? " ← mong đợi" : "";
                Debug.Log($"   {kv.Key,-10} d={d:F5}  score={s:P1}{mark}");
            }

            float score = _recognizer.MatchScore(drawn, _templates[_current], maxDistance);
            bool pass = score >= passThreshold;

            Debug.Log(pass
                ? $"<color=green>ĐẠT</color> — {score:P1} (ngưỡng {passThreshold:P0})"
                : $"<color=orange>CHƯA ĐẠT</color> — {score:P1} (ngưỡng {passThreshold:P0})");
        }

        // ------------------------------------------------------------------
        // Template tạm, sinh bằng code.
        // Sau này thay bằng template người vẽ, lưu trong ScriptableObject.
        // ------------------------------------------------------------------

        private void BuildTemplates()
        {
            _templates = new Dictionary<string, PointCloud>
            {
                ["circle"] = new PointCloud("circle", Circle(256, 256, 150)),
                ["square"] = new PointCloud("square", Square(256, 256, 260)),
                ["triangle"] = new PointCloud("triangle", Triangle(256, 256, 260))
            };
        }

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

        private static List<Point> Square(float cx, float cy, float size, int perSide = 12)
        {
            float h = size / 2f;
            var corners = new[]
            {
                new Vector2(cx - h, cy - h), new Vector2(cx + h, cy - h),
                new Vector2(cx + h, cy + h), new Vector2(cx - h, cy + h)
            };
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

        private static List<Point> Rectangle(float cx, float cy, float width, float height, int perSide = 12)
        {
            float hx = width / 2f;
            float hy = height / 2f;
            var corners = new[]
            {
                new Vector2(cx - hx, cy - hy), new Vector2(cx + hx, cy - hy),
                new Vector2(cx + hx, cy + hy), new Vector2(cx - hx, cy + hy)
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