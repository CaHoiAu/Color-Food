using System;
using System.Collections.Generic;
using NUnit.Framework;
using ColorFood.Recognition;

/// <summary>
/// Đặt file này trong Assets/Tests/EditMode/
/// Chạy: Window → General → Test Runner → EditMode → Run All
///
/// Ba test giữa (Scaled / Translated / Rotated) chính là kiểm chứng
/// ba tính bất biến của $P. Nếu chúng fail thì bước Scale hoặc
/// TranslateToOrigin đang sai.
/// </summary>
public class PDollarTests
{
    private PDollarRecognizer _recognizer;

    [SetUp]
    public void Setup()
    {
        _recognizer = new PDollarRecognizer();
    }

    // ------------------------------------------------------------------
    // Hàm tạo dữ liệu mẫu
    // ------------------------------------------------------------------

    private static List<Point> MakeCircle(
        float cx, float cy, float radius, int count = 40, int strokeId = 0)
    {
        var pts = new List<Point>();
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(2 * Math.PI * i / count);
            pts.Add(new Point(
                cx + radius * (float)Math.Cos(angle),
                cy + radius * (float)Math.Sin(angle),
                strokeId));
        }
        return pts;
    }

    private static List<Point> MakeSquare(
        float cx, float cy, float size, int perSide = 10, int strokeId = 0)
    {
        var pts = new List<Point>();
        float h = size / 2f;

        for (int i = 0; i < perSide; i++)
        {
            float t = i / (float)perSide;
            pts.Add(new Point(cx - h + size * t, cy - h, strokeId));
        }
        for (int i = 0; i < perSide; i++)
        {
            float t = i / (float)perSide;
            pts.Add(new Point(cx + h, cy - h + size * t, strokeId));
        }
        for (int i = 0; i < perSide; i++)
        {
            float t = i / (float)perSide;
            pts.Add(new Point(cx + h - size * t, cy + h, strokeId));
        }
        for (int i = 0; i < perSide; i++)
        {
            float t = i / (float)perSide;
            pts.Add(new Point(cx - h, cy + h - size * t, strokeId));
        }
        return pts;
    }

    private static List<Point> Rotate(List<Point> pts, float degrees, float cx, float cy)
    {
        float rad = degrees * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad);
        float sin = (float)Math.Sin(rad);

        var result = new List<Point>();
        foreach (var p in pts)
        {
            float dx = p.X - cx;
            float dy = p.Y - cy;
            result.Add(new Point(
                cx + dx * cos - dy * sin,
                cy + dx * sin + dy * cos,
                p.StrokeID, p.ColorID));
        }
        return result;
    }

    // ------------------------------------------------------------------
    // TEST 1 — Hai đám mây giống hệt nhau
    // ------------------------------------------------------------------

    [Test]
    public void IdenticalClouds_DistanceNearZero()
    {
        var a = new PointCloud("circle", MakeCircle(100, 100, 50));
        var b = new PointCloud("circle", MakeCircle(100, 100, 50));

        float d = _recognizer.Match(a, b);

        Assert.Less(d, 0.01f, $"Hai hình giống hệt phải có khoảng cách ~0, đang là {d}");
    }

    // ------------------------------------------------------------------
    // TEST 2 — Bất biến với KÍCH THƯỚC (kiểm chứng bước Scale)
    // ------------------------------------------------------------------

    [Test]
    public void ScaledCloud_StillMatches()
    {
        var small = new PointCloud("circle", MakeCircle(100, 100, 30));
        var large = new PointCloud("circle", MakeCircle(100, 100, 120));

        float d = _recognizer.Match(small, large);

        Assert.Less(d, 0.05f, $"Cùng hình khác kích thước phải khớp. Khoảng cách: {d}");
    }

    // ------------------------------------------------------------------
    // TEST 3 — Bất biến với VỊ TRÍ (kiểm chứng bước TranslateToOrigin)
    // ------------------------------------------------------------------

    [Test]
    public void TranslatedCloud_StillMatches()
    {
        var left = new PointCloud("circle", MakeCircle(50, 50, 40));
        var right = new PointCloud("circle", MakeCircle(500, 300, 40));

        float d = _recognizer.Match(left, right);

        Assert.Less(d, 0.05f, $"Cùng hình khác vị trí phải khớp. Khoảng cách: {d}");
    }

    // ------------------------------------------------------------------
    // TEST 4 — Hai hình KHÁC NHAU phải có khoảng cách rõ rệt
    // ------------------------------------------------------------------

    [Test]
    public void DifferentShapes_DistanceIsHigh()
    {
        var circle = new PointCloud("circle", MakeCircle(100, 100, 50));
        var square = new PointCloud("square", MakeSquare(100, 100, 100));

        float dSame = _recognizer.Match(circle, new PointCloud("c2", MakeCircle(200, 200, 80)));
        float dDiff = _recognizer.Match(circle, square);

        Assert.Greater(dDiff, dSame * 2f,
            $"Hình khác nhau ({dDiff}) phải xa hơn hẳn hình giống nhau ({dSame})");
    }

    // ------------------------------------------------------------------
    // TEST 5 — Không phụ thuộc THỨ TỰ vẽ (đặc trưng riêng của $P)
    // ------------------------------------------------------------------

    [Test]
    public void StrokeOrder_DoesNotMatter()
    {
        // Trứng rán vẽ lòng trắng trước rồi lòng đỏ
        var whiteFirst = new List<Point>();
        whiteFirst.AddRange(MakeCircle(100, 100, 60, 30, strokeId: 0));
        whiteFirst.AddRange(MakeCircle(100, 100, 20, 15, strokeId: 1));

        // Cùng hình nhưng vẽ lòng đỏ trước
        var yolkFirst = new List<Point>();
        yolkFirst.AddRange(MakeCircle(100, 100, 20, 15, strokeId: 0));
        yolkFirst.AddRange(MakeCircle(100, 100, 60, 30, strokeId: 1));

        var a = new PointCloud("egg", whiteFirst);
        var b = new PointCloud("egg", yolkFirst);

        float d = _recognizer.Match(a, b);

        Assert.Less(d, 0.1f, $"$P phải bỏ qua thứ tự vẽ. Khoảng cách: {d}");
    }

    // ------------------------------------------------------------------
    // TEST 5b — Hình NHIỀU nét (lát cà chua: 1 vòng + 6 múi = 7 nét)
    // Bản Resample trước làm nét cuối mất 2/3 số điểm; test này bắt lỗi đó.
    // ------------------------------------------------------------------

    private static List<Point> MakeTomatoSlice(int[] segmentOrder)
    {
        var pts = MakeCircle(100, 100, 80, 70, strokeId: 0);
        int stroke = 1;
        foreach (int k in segmentOrder)
        {
            double a = 2 * Math.PI * k / 6;
            pts.AddRange(MakeCircle(
                100 + 35 * (float)Math.Cos(a), 100 + 35 * (float)Math.Sin(a),
                14, 14, strokeId: stroke++));
        }
        return pts;
    }

    [Test]
    public void MultiStroke_EveryStrokeKeepsEnoughPoints()
    {
        var raw = MakeTomatoSlice(new[] { 0, 1, 2, 3, 4, 5 });
        var resampled = PointCloud.Resample(raw, PointCloud.SamplingCount);

        Assert.AreEqual(PointCloud.SamplingCount, resampled.Length);

        // Mỗi nét phải còn ít nhất 2 điểm. Bản Resample cũ cắt đuôi nên
        // nét cuối chỉ còn 1 điểm (mất 2/3), làm lệch cả đám mây.
        var perStroke = new Dictionary<int, int>();
        foreach (var p in resampled)
        {
            int c;
            perStroke.TryGetValue(p.StrokeID, out c);
            perStroke[p.StrokeID] = c + 1;
        }

        Assert.AreEqual(7, perStroke.Count, "Phải còn đủ 7 nét sau khi resample");
        foreach (var kv in perStroke)
            Assert.GreaterOrEqual(kv.Value, 2, $"Nét {kv.Key} chỉ còn {kv.Value} điểm");
    }

    [Test]
    public void MultiStroke_OrderDoesNotMatter()
    {
        var a = new PointCloud("tomato", MakeTomatoSlice(new[] { 0, 1, 2, 3, 4, 5 }));
        var b = new PointCloud("tomato", MakeTomatoSlice(new[] { 3, 0, 5, 2, 1, 4 }));

        float d = _recognizer.Match(a, b);

        Assert.Less(d, 0.01f, $"Vẽ múi theo thứ tự khác phải cho cùng kết quả. Khoảng cách: {d}");
    }

    // ------------------------------------------------------------------
    // TEST 6 — MatchScore trả về giá trị trong [0, 1]
    // ------------------------------------------------------------------

    [Test]
    public void MatchScore_IsInValidRange()
    {
        var circle = new PointCloud("circle", MakeCircle(100, 100, 50));
        var square = new PointCloud("square", MakeSquare(100, 100, 100));

        float sameScore = _recognizer.MatchScore(circle, circle);
        float diffScore = _recognizer.MatchScore(circle, square);

        Assert.GreaterOrEqual(sameScore, 0f);
        Assert.LessOrEqual(sameScore, 1f);
        Assert.Greater(sameScore, diffScore, "Giống nhau phải điểm cao hơn khác nhau");
    }

    // ------------------------------------------------------------------
    // TEST 6b — Hình khác nhau phải cho điểm THẤP RÕ RỆT
    // Nếu test này fail, maxDistance đang đặt quá lớn → mọi thứ đều ~1.0
    // ------------------------------------------------------------------

    [Test]
    public void DifferentShapes_ScoreIsLow()
    {
        var circle = new PointCloud("circle", MakeCircle(100, 100, 50));
        var triangle = new PointCloud("tri", MakeTriangle(100, 100, 100));

        float score = _recognizer.MatchScore(circle, triangle);

        Assert.Less(score, 0.4f,
            $"Tròn vs tam giác phải điểm thấp, đang là {score:F3}. " +
            "Kiểm tra lại maxDistance trong PDollarRecognizer.");
    }

    /// <summary>
    /// Không phải test — in ra bảng khoảng cách để hiệu chỉnh maxDistance.
    /// Chạy lại sau khi có dữ liệu nét vẽ tay thật.
    /// </summary>
    [Test]
    public void PrintCalibrationTable()
    {
        var circle = new PointCloud("c", MakeCircle(100, 100, 50));

        void Row(string label, PointCloud other)
            => TestContext.WriteLine($"{label,-28} d = {_recognizer.Match(circle, other):F5}");

        TestContext.WriteLine("--- Bảng hiệu chỉnh maxDistance ---");
        Row("tròn vs tròn (khác cỡ)", new PointCloud("c", MakeCircle(300, 200, 90)));
        Row("tròn vs ellipse 2:1", new PointCloud("e", MakeEllipse(100, 100, 100, 50)));
        Row("tròn vs vuông", new PointCloud("s", MakeSquare(100, 100, 100)));
        Row("tròn vs tam giác", new PointCloud("t", MakeTriangle(100, 100, 100)));

        Assert.Pass();
    }

    private static List<Point> MakeTriangle(float cx, float cy, float size, int perSide = 13)
    {
        float h = size / 2f;
        var corners = new[] { (cx, cy - h), (cx + h, cy + h), (cx - h, cy + h) };
        var pts = new List<Point>();

        for (int k = 0; k < 3; k++)
        {
            var (ax, ay) = corners[k];
            var (bx, by) = corners[(k + 1) % 3];
            for (int i = 0; i < perSide; i++)
            {
                float t = i / (float)perSide;
                pts.Add(new Point(ax + (bx - ax) * t, ay + (by - ay) * t, 0));
            }
        }
        return pts;
    }

    private static List<Point> MakeEllipse(float cx, float cy, float rx, float ry, int count = 40)
    {
        var pts = new List<Point>();
        for (int i = 0; i < count; i++)
        {
            float a = (float)(2 * Math.PI * i / count);
            pts.Add(new Point(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a), 0));
        }
        return pts;
    }

    // ------------------------------------------------------------------
    // TEST 7 — Resample cho đúng số điểm
    // ------------------------------------------------------------------

    [Test]
    public void Resample_ProducesExactCount()
    {
        var raw = MakeCircle(100, 100, 50, count: 137);
        var resampled = PointCloud.Resample(raw, PointCloud.SamplingCount);

        Assert.AreEqual(PointCloud.SamplingCount, resampled.Length);
    }

    // ------------------------------------------------------------------
    // TEST 8 — Xoay nhẹ vẫn khớp (người chơi hiếm khi vẽ thẳng hàng)
    // ------------------------------------------------------------------

    [Test]
    public void SlightRotation_StillMatches()
    {
        var upright = MakeSquare(100, 100, 100);
        var tilted = Rotate(upright, 15f, 100, 100);

        var a = new PointCloud("square", upright);
        var b = new PointCloud("square", tilted);

        float d = _recognizer.Match(a, b);

        // $P KHÔNG bất biến với xoay (khác $1 có bước rotate-to-zero).
        // Xoay nhẹ 15° vẫn nên khớp tương đối — nếu test này fail nhiều,
        // cân nhắc nới maxDistance hoặc thêm bước chuẩn hóa góc.
        Assert.Less(d, 0.15f, $"Xoay 15 độ vẫn nên khớp. Khoảng cách: {d}");
    }

    // ------------------------------------------------------------------
    // TEST 9 — Nhiều mẫu cho một nguyên liệu: mẫu giống nhất thắng
    // ------------------------------------------------------------------

    [Test]
    public void MatchBest_PicksClosestVariant()
    {
        var circle = new PointCloud("circle", MakeCircle(0, 0, 50));
        var square = new PointCloud("square", MakeSquare(0, 0, 100));
        var drawn = new PointCloud("drawn", MakeSquare(300, 300, 60));

        var variants = new List<PointCloud> { circle, square };
        float best = _recognizer.MatchBest(drawn, variants, out int index);

        Assert.AreEqual(1, index, "Hình vuông phải khớp mẫu vuông (index 1)");
        Assert.AreEqual(_recognizer.Match(drawn, square), best, 1e-6f);
        Assert.Less(best, _recognizer.Match(drawn, circle));
    }

    [Test]
    public void MatchBest_ScoreNotLowerThanSingleTemplate()
    {
        var circle = new PointCloud("circle", MakeCircle(0, 0, 50));
        var square = new PointCloud("square", MakeSquare(0, 0, 100));
        var drawn = new PointCloud("drawn", MakeSquare(0, 0, 80));

        float single = _recognizer.MatchScore(drawn, circle, 0.12f);
        float multi = PDollarRecognizer.ScoreFromDistance(
            _recognizer.MatchBest(drawn, new List<PointCloud> { circle, square }, out _), 0.12f);

        Assert.GreaterOrEqual(multi, single, "Thêm mẫu không được làm điểm tụt");
    }

    [Test]
    public void MatchBest_EmptyList_ReturnsNoMatch()
    {
        var drawn = new PointCloud("drawn", MakeCircle(0, 0, 50));

        float d = _recognizer.MatchBest(drawn, new List<PointCloud>(), out int index);

        Assert.AreEqual(-1, index);
        Assert.AreEqual(float.MaxValue, d);
        Assert.AreEqual(0f, PDollarRecognizer.ScoreFromDistance(d, 0.12f));
    }

    // ------------------------------------------------------------------
    // TEST 10 — Tự sinh góc độ (xoay / lật) từ một mẫu
    // ------------------------------------------------------------------

    [Test]
    public void PointTransform_Rotate90_AroundCentroid()
    {
        // Đoạn ngang quanh (0,0) xoay +90° (ngược kim đồng hồ) thành đoạn dọc
        var pts = new List<Point> { new Point(-10, 0, 0), new Point(10, 0, 0) };
        var r = PointTransform.Rotate(pts, 90f);

        Assert.AreEqual(0f, r[0].X, 1e-4f);
        Assert.AreEqual(-10f, r[0].Y, 1e-4f);
        Assert.AreEqual(0f, r[1].X, 1e-4f);
        Assert.AreEqual(10f, r[1].Y, 1e-4f);
    }

    [Test]
    public void PointTransform_MirrorX_KeepsStrokeAndCentroid()
    {
        var pts = new List<Point> { new Point(0, 0, 0), new Point(10, 5, 1) };
        var m = PointTransform.MirrorX(pts);

        Assert.AreEqual(10f, m[0].X, 1e-4f);
        Assert.AreEqual(0f, m[1].X, 1e-4f);
        Assert.AreEqual(5f, m[1].Y, 1e-4f);
        Assert.AreEqual(1, m[1].StrokeID);
    }

    [Test]
    public void PointTransform_Variants_Count()
    {
        var pts = MakeSquare(0, 0, 100);

        // gốc, ±15, ±30 = 5; có lật = 10
        Assert.AreEqual(10, PointTransform.Variants(pts, 30f, 15f, true).Count);
        Assert.AreEqual(5, PointTransform.Variants(pts, 30f, 15f, false).Count);
        Assert.AreEqual(1, PointTransform.Variants(pts, 0f, 15f, false).Count);
        Assert.AreEqual("", PointTransform.Variants(pts, 30f, 15f, true)[0].suffix);
    }

    [Test]
    public void AutoRotatedVariants_MatchTiltedDrawingBetter()
    {
        // Mẫu là một hình KHÔNG đối xứng xoay (chữ nhật 2:1), người chơi vẽ nghiêng 30°
        var template = new List<Point>();
        template.AddRange(MakeSquare(0, 0, 100));
        for (int i = 0; i < template.Count; i++)
            template[i] = new Point(template[i].X * 2f, template[i].Y, 0);
        var tilted = new PointCloud("drawn", Rotate(template, 30f, 0, 0));

        float single = _recognizer.Match(tilted, new PointCloud("t", template));

        var clouds = new List<PointCloud>();
        foreach (var (_, p) in PointTransform.Variants(template, 30f, 15f, false))
            clouds.Add(new PointCloud("t", p));
        float multi = _recognizer.MatchBest(tilted, clouds, out _);

        Assert.Less(multi, single, "Có bản xoay 30° thì nét vẽ nghiêng phải khớp tốt hơn");
        Assert.Less(multi, 0.01f, "Bản xoay đúng góc phải gần như trùng khớp");
    }
}