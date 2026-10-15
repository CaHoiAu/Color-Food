using System;
using System.Collections.Generic;
using NUnit.Framework;
using ColorFood.Recognition;

/// <summary>
/// Đặt trong Assets/Tests/EditMode/ (cùng chỗ với PDollarTests).
/// Kiểm tra tầng thuần C# của bước 4: tô màu, đóng nét, đếm vùng kín, chấm bộ phận.
/// </summary>
public class ColoringTests
{
    private const int W = 512, H = 512, Brush = 4;
    private ColorTable _colors;

    [SetUp]
    public void Setup()
    {
        _colors = new ColorTable(DefaultPalette.Names, DefaultPalette.Colors);
    }

    // ------------------------------------------------------------------
    // Dựng dữ liệu
    // ------------------------------------------------------------------

    /// <summary>Elip nét liền, bỏ đi gapDeg độ ở cuối để tạo khe hở.</summary>
    private static List<Point> Ellipse(float cx, float cy, float rx, float ry,
                                       int stroke = 0, float gapDeg = 0f, int n = 120)
    {
        var pts = new List<Point>();
        float sweep = 360f - gapDeg;
        for (int i = 0; i <= n; i++)
        {
            double a = (sweep * i / n) * Math.PI / 180.0;
            pts.Add(new Point(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a), stroke));
        }
        return pts;
    }

    private static List<Point> Concat(params List<Point>[] parts)
    {
        var all = new List<Point>();
        foreach (var p in parts) all.AddRange(p);
        return all;
    }

    private class Drawing
    {
        public bool[] Ink = new bool[W * H];
        public bool[] Barrier;
        public int[] Fill = new int[W * H];
        public List<Point> Points;
    }

    private static Drawing Draw(List<Point> pts, float closeGap = 0f)
    {
        var d = new Drawing { Points = pts };
        for (int i = 0; i < d.Fill.Length; i++) d.Fill[i] = -1;
        Raster.DrawPoints(d.Ink, W, H, pts, Brush);

        d.Barrier = (bool[])d.Ink.Clone();
        if (closeGap > 0f)
            foreach (var s in OutlineCloser.FindClosures(pts, closeGap))
                Raster.DrawSegment(d.Barrier, W, H, s, 2);
        return d;
    }

    /// <summary>Tô như canvas thật: chỉ tô vùng kín.</summary>
    private static bool FillAt(Drawing d, int x, int y, int colorId)
    {
        bool touches;
        var region = FloodFill.FindRegion(d.Barrier, W, H, x, y, out touches);
        if (region.Count == 0 || touches) return false;
        foreach (int p in region) d.Fill[p] = colorId;
        return true;
    }

    private static Drawing Egg(bool withYolk, float closeGap = 25f)
    {
        var outer = Ellipse(256, 256, 190, 115, 0);
        var all = withYolk ? Concat(outer, Ellipse(270, 250, 50, 50, 1)) : outer;
        return Draw(all, closeGap);
    }

    private ScoreResult ScoreEgg(Drawing d, float shape = 1f, float cap = 0.5f)
    {
        var regions = RegionAnalyzer.Analyze(d.Barrier, W, H);
        return IngredientScorer.Score(shape, regions, d.Fill, _colors, 0.4f,
                                      PartPresets.FriedEgg(), cap);
    }

    // ------------------------------------------------------------------
    // Flood fill
    // ------------------------------------------------------------------

    [Test]
    public void FloodFill_FillsClosedCircle_AreaIsRight()
    {
        var d = Draw(Ellipse(256, 256, 100, 100));
        bool touches;
        var region = FloodFill.FindRegion(d.Barrier, W, H, 256, 256, out touches);

        float expected = (float)(Math.PI * 96 * 96);   // bán kính trừ nửa bề dày nét
        Assert.IsFalse(touches);
        Assert.Greater(region.Count, expected * 0.9f);
        Assert.Less(region.Count, expected * 1.1f);
    }

    [Test]
    public void FloodFill_OpenRing_ReportsLeak()
    {
        var d = Draw(Ellipse(256, 256, 100, 100, 0, gapDeg: 60f));
        bool touches;
        FloodFill.FindRegion(d.Barrier, W, H, 256, 256, out touches);
        Assert.IsTrue(touches, "vùng hở phải chạm mép canvas");
    }

    [Test]
    public void FloodFill_ClickOnInk_ReturnsEmpty()
    {
        var d = Draw(Ellipse(256, 256, 100, 100));
        bool touches;
        var region = FloodFill.FindRegion(d.Barrier, W, H, 356, 256, out touches);   // đúng trên nét
        Assert.AreEqual(0, region.Count);
    }

    [Test]
    public void FloodFill_DoesNotCrossInk_BetweenTwoBoxes()
    {
        // Hai ô vuông dùng chung một cạnh giữa
        var box = new List<Point>
        {
            new Point(100,100,0), new Point(400,100,0), new Point(400,300,0),
            new Point(100,300,0), new Point(100,100,0)
        };
        var wall = new List<Point> { new Point(250,100,1), new Point(250,300,1) };
        var d = Draw(Concat(box, wall));

        Assert.IsTrue(FillAt(d, 170, 200, 5));
        Assert.AreEqual(-1, d.Fill[200 * W + 330], "ô bên phải không được dính màu");
        Assert.AreEqual(5, d.Fill[200 * W + 170]);
    }

    // ------------------------------------------------------------------
    // Tự đóng nét
    // ------------------------------------------------------------------

    [Test]
    public void Closer_SmallGap_IsSealed()
    {
        var pts = Ellipse(256, 256, 100, 100, 0, gapDeg: 10f);   // khe ~17px

        Assert.IsFalse(FillAt(Draw(pts, 0f), 256, 256, 3), "không đóng nét: màu phải rò ra ngoài");
        Assert.IsTrue(FillAt(Draw(pts, 25f), 256, 256, 3), "có đóng nét: tô được trong vòng");
    }

    [Test]
    public void Closer_BigGap_IsNotSealed()
    {
        var pts = Ellipse(256, 256, 100, 100, 0, gapDeg: 90f);   // hở một phần tư vòng
        Assert.IsFalse(FillAt(Draw(pts, 25f), 256, 256, 3));
    }

    [Test]
    public void Closer_ClosedShape_AddsNothingHarmful()
    {
        var d = Draw(Ellipse(256, 256, 100, 100), 25f);
        var set = RegionAnalyzer.Analyze(d.Barrier, W, H);
        Assert.AreEqual(1, set.Regions.Count);
    }

    // ------------------------------------------------------------------
    // Vùng kín
    // ------------------------------------------------------------------

    [Test]
    public void Regions_Egg_HasOuterAndInner()
    {
        var set = RegionAnalyzer.Analyze(Egg(true).Barrier, W, H);

        Assert.AreEqual(2, set.Regions.Count);
        int outer = 0, inner = 0;
        foreach (var r in set.Regions) { if (r.Role == PartRole.Outer) outer++; else inner++; }
        Assert.AreEqual(1, outer);
        Assert.AreEqual(1, inner);

        float yolkRatio = 0f;
        foreach (var r in set.Regions)
            if (r.Role == PartRole.Inner) yolkRatio = r.Area / (float)set.TotalArea;
        TestContext.WriteLine("tỉ lệ lòng đỏ/tổng = " + yolkRatio);
        Assert.Greater(yolkRatio, 0.05f);
        Assert.Less(yolkRatio, 0.25f);
    }

    [Test]
    public void Regions_OutlineOnly_HasNoInner()
    {
        var set = RegionAnalyzer.Analyze(Egg(false).Barrier, W, H);
        Assert.AreEqual(1, set.Regions.Count);
        Assert.AreEqual((int)PartRole.Outer, (int)set.Regions[0].Role);
    }

    [Test]
    public void Regions_TinyOverlapHoles_AreIgnored()
    {
        // Hai nét cắt nhau tạo lỗ nhỏ xíu, không được tính là vùng
        var a = new List<Point> { new Point(100,100,0), new Point(300,100,0) };
        var b = new List<Point> { new Point(100,106,1), new Point(300,106,1) };
        var set = RegionAnalyzer.Analyze(Draw(Concat(a, b)).Barrier, W, H);
        Assert.AreEqual(0, set.Regions.Count);
    }

    // ------------------------------------------------------------------
    // Chấm bộ phận: các kịch bản đã đo được trên dữ liệu thật
    // ------------------------------------------------------------------

    [Test]
    public void Score_Egg_FullAndCorrect_IsHigh()
    {
        var d = Egg(true);
        Assert.IsTrue(FillAt(d, 90, 256, _colors.IndexOf("white")));     // lòng trắng
        Assert.IsTrue(FillAt(d, 270, 250, _colors.IndexOf("yellow")));   // lòng đỏ

        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report);
        Assert.GreaterOrEqual(r.Total, 0.9f);
        Assert.IsFalse(r.Capped);
    }

    [Test]
    public void Score_Egg_OnlyWhite_IsCapped_EvenWithPerfectShape()
    {
        // Đây chính là lỗi $P không thấy: chỉ vẽ lòng trắng vẫn đạt > 70%.
        var d = Egg(false);
        Assert.IsTrue(FillAt(d, 256, 256, _colors.IndexOf("white")));

        var r = ScoreEgg(d, shape: 0.97f);
        TestContext.WriteLine(r.Report);
        Assert.IsTrue(r.Capped);
        Assert.LessOrEqual(r.Total, 0.5f);
        Assert.Less(r.Total, 0.7f, "phải dưới ngưỡng đạt 0.7");
    }

    [Test]
    public void Score_Egg_WrongYolkColor_IsLow()
    {
        var d = Egg(true);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("blue"));

        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report);
        Assert.Less(r.Total, 0.7f);
    }

    [Test]
    public void Score_Egg_Unfilled_IsLow()
    {
        var r = ScoreEgg(Egg(true));
        TestContext.WriteLine(r.Report);
        Assert.Less(r.Total, 0.7f);
    }

    [Test]
    public void Score_Egg_SimilarYolkColor_StillGetsPartialCredit()
    {
        var d = Egg(true);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("pale yellow"));

        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report);
        Assert.Greater(r.Total, 0.6f);
        Assert.Less(r.Total, 1.0f);
    }

    [Test]
    public void Score_Egg_YolkTooBig_LosesAreaPoints()
    {
        var outer = Ellipse(256, 256, 190, 115, 0);
        var yolk = Ellipse(256, 256, 105, 80, 1);    // lòng đỏ chiếm quá nửa
        var d = Draw(Concat(outer, yolk), 25f);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 256, 256, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report);
        Assert.Less(r.PartResults[1].AreaFactor, 1f);
    }

    [Test]
    public void Score_Egg_TwoYolks_LosesCountPoints()
    {
        var all = Concat(Ellipse(256, 256, 190, 115, 0),
                         Ellipse(200, 256, 35, 35, 1), Ellipse(320, 256, 35, 35, 2));
        var d = Draw(all, 25f);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 200, 256, _colors.IndexOf("yellow"));
        FillAt(d, 320, 256, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report);
        Assert.AreEqual(2, r.PartResults[1].Count);
        Assert.AreEqual(1, (int)Math.Round(r.PartResults[1].CountFactor * 2));   // 0.5
    }

    [Test]
    public void Score_NoRules_ReturnsShapeScore()
    {
        var set = RegionAnalyzer.Analyze(Egg(false).Barrier, W, H);
        var r = IngredientScorer.Score(0.83f, set, new int[W * H], _colors, 0.4f, null, 0.5f);
        Assert.AreEqual(0.83f, r.Total, 0.0001f);
    }

    [Test]
    public void Score_SingleBody_Tomato_RedGood_BlueBad()
    {
        var pts = Ellipse(256, 256, 120, 110);
        var good = Draw(pts, 25f);
        FillAt(good, 256, 256, _colors.IndexOf("red"));
        var bad = Draw(pts, 25f);
        FillAt(bad, 256, 256, _colors.IndexOf("blue"));

        var rules = PartPresets.SingleBody("red", "dark red");
        float g = IngredientScorer.Score(1f, RegionAnalyzer.Analyze(good.Barrier, W, H), good.Fill,
                                         _colors, 0.4f, rules, 0.5f).Total;
        float b = IngredientScorer.Score(1f, RegionAnalyzer.Analyze(bad.Barrier, W, H), bad.Fill,
                                         _colors, 0.4f, rules, 0.5f).Total;
        TestContext.WriteLine("cà chua đỏ = " + g + ", xanh dương = " + b);
        Assert.GreaterOrEqual(g, 0.95f);
        Assert.Less(b, 0.5f);
    }

    // ------------------------------------------------------------------
    // Bảng màu
    // ------------------------------------------------------------------

    [Test]
    public void Color_HueWrapsAroundCircle()
    {
        Assert.Less(ColorTable.HueDifference(350f, 10f), 0.12f);   // 20° trên vòng tròn, không phải 340°
        Assert.Greater(ColorTable.HueDifference(0f, 180f), 0.99f);
    }

    [Test]
    public void Color_Similarity_RankingMakesSense()
    {
        int red = _colors.IndexOf("red"), dark = _colors.IndexOf("dark red");
        int orange = _colors.IndexOf("orange"), yellow = _colors.IndexOf("yellow");
        int blue = _colors.IndexOf("blue");
        int white = _colors.IndexOf("white"), cream = _colors.IndexOf("cream");

        Assert.AreEqual(1f, _colors.Similarity(red, red), 0.0001f);
        Assert.Greater(_colors.Similarity(red, dark), 0.6f);      // đỏ ≈ đỏ đậm
        Assert.Greater(_colors.Similarity(red, dark), _colors.Similarity(red, orange));
        Assert.Greater(_colors.Similarity(red, orange), _colors.Similarity(red, yellow));
        Assert.Less(_colors.Similarity(red, blue), 0.1f);
        Assert.Greater(_colors.Similarity(white, cream), 0.7f);
    }

    [Test]
    public void Color_PrintSimilarityTable()
    {
        var sb = new System.Text.StringBuilder("\n            ");
        for (int b = 0; b < _colors.Count; b++) sb.Append(Short(_colors.NameOf(b)) + " ");
        sb.Append("\n");
        for (int a = 0; a < _colors.Count; a++)
        {
            sb.Append(Short(_colors.NameOf(a)).PadRight(12));
            for (int b = 0; b < _colors.Count; b++)
                sb.Append(((int)Math.Round(_colors.Similarity(a, b) * 100)).ToString().PadLeft(4) + " ");
            sb.Append("\n");
        }
        TestContext.WriteLine(sb.ToString());
        Assert.Pass();
    }

    private static string Short(string s) => (s.Length > 4 ? s.Substring(0, 4) : s).PadRight(4);
}
