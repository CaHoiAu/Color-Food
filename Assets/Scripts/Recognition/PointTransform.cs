using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ColorFood.Recognition
{
    public static class PointTransform
    {
        public static Point[] Rotate(IReadOnlyList<Point> points, float degrees)
        {
            Centroid(points, out float cx, out float cy);
            double rad = degrees * Mathf.PI / 180.0;
            float cos = (float)System.Math.Cos(rad);
            float sin = (float)System.Math.Sin(rad);

            var result = new Point[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Point p = points[i];
                float dx = p.X - cx;
                float dy = p.Y - cy;
                result[i] = new Point(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos, p.StrokeID, p.ColorID);
            }
            return result;
        }
        public static Point[] MirrorX(IReadOnlyList<Point> points)
        {
            Centroid(points, out float cx, out float cy);
            var result = new Point[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Point p = points[i];
                result[i] = new Point(cx - (p.X - cx), p.Y, p.StrokeID, p.ColorID);
            }
            return result;
        }
        public static List<(string suffix, Point[] points)> Variants(
            IReadOnlyList<Point> points, float maxAngle, float step, bool mirror)
        {
            var angles = new List<float>();
            if (maxAngle > 0f && step > 0f)
            {
                for (float a = -step; a <= maxAngle+1e-3f; a += step)
                {
                    angles.Add(a);
                    angles.Add(-a);
                }
            }
            var original = new Point[points.Count];
            for (int i = 0; i < points.Count; i++) original[i] = points[i];
            var result = new List<(string suffix, Point[] points)>();
            AddWithRotations(result, "", original, angles);
            if (mirror)
            {
                AddWithRotations(result, "Lật", MirrorX(original), angles);
            }
            return result;
        }
        private static void AddWithRotations(List<(string suffix, Point[] points)> result,
            string prefix, Point[] original, List<float> angles)
        {
            result.Add((prefix, original));
            foreach (float a in angles)
            {
                var rotated = Rotate(original, a);
                result.Add((prefix + " xoay " + (a > 0 ? "+" : "") + a.ToString("0.#") + "°", rotated));
            }
        }
        private static void Centroid(IReadOnlyList<Point> points, out float cx, out float cy)
        {
            cx = 0f; cy = 0f;
            foreach (Point p in points)
            {
                cx += p.X; cy += p.Y;
            }
            cx /= points.Count; cy /= points.Count;
        }
    }
}
