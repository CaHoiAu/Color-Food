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

    private ScoreResult ScoreEgg(Drawing d, float shape = 1f, float cap = 0.5f,
                                 float minShape = 0.5f, float shapeCap = 0.5f)
    {
        var regions = RegionAnalyzer.Analyze(d.Barrier, W, H);
        return IngredientScorer.Score(shape, regions, d.Fill, _colors, 0.4f,
                                      PartPresets.FriedEgg(), cap, minShape, shapeCap);
    }

    /// <summary>
    /// Vòng ngoài có mấy chỗ lồi hẳn ra như amip (giống hình trứng méo nhiều gợn
    /// người dùng vẽ thật) — bán kính dao động theo góc thay vì hằng số.
    /// $P vẫn có thể cho điểm hình khá cao vì nó đo khoảng cách TRUNG BÌNH trên
    /// cả đám điểm, còn circularity thì phạt trực tiếp vì chu vi tăng vọt.
    /// </summary>
    private static List<Point> JaggedBlob(float cx, float cy, float baseR,
                                          float bumpAmount, int bumps, int stroke = 0, int n = 160)
    {
        var pts = new List<Point>();
        for (int i = 0; i <= n; i++)
        {
            double a = 2 * Math.PI * i / n;
            float r = baseR * (1f + bumpAmount * (float)Math.Sin(bumps * a));
            pts.Add(new Point(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a), stroke));
        }
        return pts;
    }

    private static Func<double, double> EllipseR(float rx, float ry)
        => a => (rx * ry) / Math.Sqrt(Math.Pow(ry * Math.Cos(a), 2) + Math.Pow(rx * Math.Sin(a), 2));

    private static List<Point> Polar(float cx, float cy, Func<double, double> r, int stroke = 0, int n = 360)
    {
        var pts = new List<Point>();
        for (int i = 0; i <= n; i++)
        {
            double a = i == n ? 0.0 : 2 * Math.PI * i / n;
            pts.Add(new Point(cx + (float)(r(a) * Math.Cos(a)), cy + (float)(r(a) * Math.Sin(a)), stroke));
        }
        return pts;
    }

    /// <summary>Lòng trắng trứng thật: elip bị méo bởi vài sóng thoải (không gai).</summary>
    private static List<Point> OrganicWhite(int seed, float amp, int stroke = 0)
    {
        var rng = new Random(seed);
        var ph = new double[5]; var am = new double[5];
        for (int k = 0; k < 5; k++)
        {
            ph[k] = rng.NextDouble() * 2 * Math.PI;
            am[k] = amp * (0.4 + 0.6 * rng.NextDouble()) / Math.Pow(k + 2, 0.7);
        }
        var e = EllipseR(170, 115);
        return Polar(256, 256, a =>
        {
            double s = 0;
            for (int k = 0; k < 5; k++) s += am[k] * Math.Sin((k + 2) * a + ph[k]);
            return e(a) * (1 + s);
        }, stroke);
    }

    /// <summary>Lòng trắng có GAI NHỌN: các tam giác hẹp nhô ra trên nền elip.</summary>
    private static List<Point> SpikyWhite(int spikes, float height, float widthRad, int stroke = 0)
    {
        var e = EllipseR(170, 115);
        var centers = new double[spikes];
        for (int i = 0; i < spikes; i++) centers[i] = 2 * Math.PI * i / spikes;
        return Polar(256, 256, a =>
        {
            double s = 0;
            foreach (double c in centers)
            {
                double d = Math.Abs(((a - c + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI);
                if (d < widthRad) s = Math.Max(s, height * (1 - d / widthRad));
            }
            return e(a) * (1 + s);
        }, stroke);
    }

    /// <summary>Tô lòng trắng bằng cách thử vài điểm bên trái (nằm ngoài lòng đỏ); true nếu tô được.</summary>
    private bool FillWhite(Drawing d)
    {
        foreach (int x in new[] { 150, 140, 130, 120, 110 })
            if (FillAt(d, x, 256, _colors.IndexOf("white"))) return true;
        return false;
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
    public void Score_Egg_VeryBadShape_PerfectParts_IsStillCapped()
    {
        // Dữ liệu thật (scoring_log.csv dòng 9): hình 0.42, nếu tô đúng hết thì
        // total cũ = 0.4*0.42 + 0.6*1.0 = 0.77 — VẪN ĐẠT dù hình rớt rõ rệt.
        // minShape phải chặn trường hợp này lại dưới ngưỡng đạt.
        var d = Egg(true);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d, shape: 0.42f);
        TestContext.WriteLine(r.Report);
        Assert.IsTrue(r.Capped);
        Assert.LessOrEqual(r.Total, 0.5f);
        Assert.Less(r.Total, 0.7f, "phải dưới ngưỡng đạt 0.7");
    }

    [Test]
    public void Score_Egg_ShapeAboveMinShape_IsNotCappedByShape()
    {
        // Ngay trên ngưỡng (mặc định 0.5): không bị chặn vì hình, chỉ chặn nếu thiếu bộ phận.
        var d = Egg(true);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d, shape: 0.55f);
        TestContext.WriteLine(r.Report);
        Assert.IsFalse(r.Capped);
    }

    [Test]
    public void Score_Egg_MinShapeZero_DisablesShapeCap()
    {
        // minShape = 0 nghĩa là tắt hẳn cơ chế này (giữ hành vi cũ) — dùng được
        // cho nguyên liệu mà hình quá khó đo (ví dụ chỉ có 1 nét ngắn).
        var regions = RegionAnalyzer.Analyze(Egg(true).Barrier, W, H);
        var fillIds = Egg(true).Fill;
        var r = IngredientScorer.Score(0.42f, regions, fillIds, _colors, 0.4f,
                                       PartPresets.FriedEgg(), 0.5f, minShape: 0f);
        TestContext.WriteLine(r.Report);
        Assert.IsFalse(r.Capped, "minShape=0 không được chặn gì theo hình");
    }

    [Test]
    public void Score_NoRules_ShapeTooLow_IsStillCapped()
    {
        // Nguyên liệu không có luật bộ phận: total = shape luôn, nên cap chỉ có
        // tác dụng khi shapeCap nằm GIỮA shape và minShape (shapeCap < shape < minShape) —
        // nếu không, total vốn đã thấp hơn cap, không có gì để chặn thêm.
        var set = RegionAnalyzer.Analyze(Egg(false).Barrier, W, H);
        var r = IngredientScorer.Score(0.55f, set, new int[W * H], _colors, 0.4f, null, 0.5f,
                                       minShape: 0.6f, shapeCap: 0.5f);
        Assert.IsTrue(r.Capped);
        Assert.AreEqual(0.5f, r.Total, 0.0001f);
    }

    [Test]
    public void Score_NoRules_ShapeBelowShapeCap_IsNotArtificiallyRaised()
    {
        // Khi total vốn đã thấp hơn shapeCap, không có gì "để chặn" — total
        // giữ nguyên giá trị thấp đó, cap không kéo điểm LÊN.
        var set = RegionAnalyzer.Analyze(Egg(false).Barrier, W, H);
        var r = IngredientScorer.Score(0.3f, set, new int[W * H], _colors, 0.4f, null, 0.5f,
                                       minShape: 0.5f, shapeCap: 0.5f);
        Assert.IsFalse(r.Capped);
        Assert.AreEqual(0.3f, r.Total, 0.0001f);
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

    // ------------------------------------------------------------------
    // Độ tròn (circularity) — bắt đường bao lồi lõm mà $P bỏ sót
    // ------------------------------------------------------------------

    [Test]
    public void Circularity_SmoothCircle_IsInExpectedRange()
    {
        // LUU Y: circularity do bang cach dem canh pixel (ComputePerimeters), cong
        // them mep brush-stamp (DrawPoints) von da gon song nhe o muc pixel, nen
        // MOT HINH TRON VE TRON TUYET DOI cung chi do duoc khoang 0.4-0.6, KHONG
        // gan 1.0 nhu cong thuc hinh hoc ly tuong. Da kiem chung bang mo phong
        // Python doc lap (dia tron mau toan hoc, khong brush): ty le chu vi do
        // duoc / chu vi thuc hoi tu ve 4/pi (~1.273) khi ban kinh lon, nen
        // circularity ly thuyet toi da chi ~ pi^2/16 = 0.617 — con brush-stamp
        // lam no thap hon nua. Vi vay test nay chi kiem tra hinh tron NAM TRONG
        // mot khoang hop ly do thuc nghiem, khong ky vong gan 1.
        var d = Draw(Ellipse(256, 256, 120, 120));
        var set = RegionAnalyzer.Analyze(d.Barrier, W, H);
        float c = set.Regions[0].Circularity();
        TestContext.WriteLine("circularity hinh tron = " + c);
        Assert.Greater(c, 0.4f);
        Assert.Less(c, 0.65f);
    }

    [Test]
    public void Circularity_JaggedBlob_IsMuchLower()
    {
        var smooth = Draw(Ellipse(256, 256, 120, 120));
        var jagged = Draw(JaggedBlob(256, 256, 120, 0.35f, 5));

        float cSmooth = RegionAnalyzer.Analyze(smooth.Barrier, W, H).Regions[0].Circularity();
        float cJagged = RegionAnalyzer.Analyze(jagged.Barrier, W, H).Regions[0].Circularity();
        TestContext.WriteLine("tron = " + cSmooth + "  |  gon ghe = " + cJagged);

        Assert.Less(cJagged, 0.7f);
        Assert.Greater(cSmooth - cJagged, 0.15f, "hinh gon ghe phai thap hon ro ret");
    }

    [Test]
    public void Score_Egg_SpikyOutline_CorrectPartsButIsCapped()
    {
        // Đúng tình huống cần bắt: lòng trắng có gai nhọn, nhưng đủ lòng đỏ, tô đúng màu,
        // diện tích hợp lệ — $P vẫn cho hình khá cao. maxSpikiness phải chặn lại.
        var d = Draw(Concat(SpikyWhite(12, 0.30f, 0.08f), Ellipse(270, 250, 45, 45, 1)), 25f);
        Assert.IsTrue(FillWhite(d));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d, shape: 0.68f);
        TestContext.WriteLine(r.Report + "spike=" + r.PartResults[0].Spikiness);

        Assert.Greater(r.PartResults[0].Spikiness, 0.008f);
        Assert.Less(r.PartResults[0].SpikeFactor, 1f);
        Assert.IsTrue(r.Capped);
        Assert.Less(r.Total, 0.7f, "phải KHÔNG đạt dù hình và màu đều ổn");
    }

    [Test]
    public void Score_Egg_MildSpikes_InRealMeasuredRange_AreCapped()
    {
        // Nét vẽ gai thật của người dùng (19/10) chỉ đo 0.0084–0.0163, nhẹ hơn nhiều so với
        // hình mô phỏng gai rõ (0.02–0.04). Các hình gai nhẹ này cũng phải bị chặn.
        foreach (var p in new[] { (12, 0.12f, 0.06f), (16, 0.12f, 0.10f), (12, 0.18f, 0.06f) })
        {
            var d = Draw(Concat(SpikyWhite(p.Item1, p.Item2, p.Item3), Ellipse(270, 250, 45, 45, 1)), 25f);
            Assert.IsTrue(FillWhite(d));
            FillAt(d, 270, 250, _colors.IndexOf("yellow"));

            var r = ScoreEgg(d, shape: 0.7f);
            string tag = "n=" + p.Item1 + " h=" + p.Item2 + " w=" + p.Item3;
            TestContext.WriteLine(tag + ": gai=" + r.PartResults[0].Spikiness + " tổng=" + r.Total);
            Assert.Greater(r.PartResults[0].Spikiness, 0.0080f, tag);
            Assert.Less(r.PartResults[0].Spikiness, 0.0200f, tag);
            Assert.IsTrue(r.Capped, tag);
            Assert.Less(r.Total, 0.7f, tag);
        }
    }

    [Test]
    public void Score_Egg_SpikeRuleOff_WhenMaxSpikinessIsZero_ButStillMeasured()
    {
        // Tái hiện lỗi thực tế: asset cũ có maxSpikiness = 0 → luật TẮT, trứng gai vẫn "đạt".
        // Độ gai vẫn phải được đo và hiện trong báo cáo để phát hiện ra tình trạng này.
        var d = Draw(Concat(SpikyWhite(12, 0.30f, 0.08f), Ellipse(270, 250, 45, 45, 1)), 25f);
        Assert.IsTrue(FillWhite(d));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var rules = PartPresets.FriedEgg();
        rules[0].maxSpikiness = 0f;
        var r = IngredientScorer.Score(0.7f, RegionAnalyzer.Analyze(d.Barrier, W, H), d.Fill, _colors,
                                       0.4f, rules, 0.5f);
        Assert.Greater(r.PartResults[0].Spikiness, 0.015f, "vẫn phải đo được gai");
        Assert.IsFalse(r.Capped, "luật tắt thì không chặn");
        Assert.IsTrue(r.Report.Contains("luật gai TẮT"), "báo cáo phải nói rõ luật gai đang tắt");
    }

    [Test]
    public void Score_Egg_OrganicWavyOutline_IsNotPenalized()
    {
        // Lòng trắng trứng thật: viền lượn sóng, không tròn, không gai → phải ĐẠT.
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var d = Draw(Concat(OrganicWhite(seed, 0.26f), Ellipse(270, 250, 45, 45, 1)), 25f);
            Assert.IsTrue(FillWhite(d), "seed " + seed);
            FillAt(d, 270, 250, _colors.IndexOf("yellow"));

            var r = ScoreEgg(d, shape: 0.8f);
            TestContext.WriteLine("seed " + seed + ": gai=" + r.PartResults[0].Spikiness + " tổng=" + r.Total);
            Assert.LessOrEqual(r.PartResults[0].Spikiness, 0.0045f, "seed " + seed);
            Assert.IsFalse(r.Capped, "seed " + seed);
            Assert.GreaterOrEqual(r.Total, 0.85f, "seed " + seed);
        }
    }

    [Test]
    public void Spikiness_SmoothFarBelowSpiky()
    {
        var smooth = RegionAnalyzer.Analyze(Draw(Ellipse(256, 256, 190, 115)).Barrier, W, H).Regions[0].Spikiness;
        var spiky = RegionAnalyzer.Analyze(Draw(SpikyWhite(12, 0.30f, 0.08f)).Barrier, W, H).Regions[0].Spikiness;
        TestContext.WriteLine("trơn=" + smooth + "  gai=" + spiky);
        Assert.Less(smooth, 0.004f);
        Assert.Greater(spiky, 0.015f);
    }

    [Test]
    public void Morphology_Open_MatchesBruteForce()
    {
        // So EDT-based Open với định nghĩa trực tiếp (co rồi nở bằng đĩa) trên hình ngẫu nhiên nhỏ.
        var rng = new Random(7);
        int w = 40, h = 33;
        for (int trial = 0; trial < 6; trial++)
        {
            var m = new bool[w * h];
            for (int y = 4; y < h - 4; y++)
                for (int x = 4; x < w - 4; x++)
                    m[y * w + x] = rng.NextDouble() < 0.82;
            int r = 2 + trial % 3;

            var fast = Morphology.Open(m, w, h, r);
            var slow = BruteOpen(m, w, h, r);
            for (int i = 0; i < m.Length; i++)
                Assert.IsTrue(fast[i] == slow[i], "trial " + trial + " r=" + r + " pixel " + i);
        }
    }

    private static bool[] BruteOpen(bool[] m, int w, int h, int r)
    {
        var er = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool ok = m[y * w + x];
                for (int dy = -r; dy <= r && ok; dy++)
                    for (int dx = -r; dx <= r && ok; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || xx >= w || yy < 0 || yy >= h || !m[yy * w + xx]) ok = false;
                    }
                er[y * w + x] = ok;
            }
        var op = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (er[y * w + x])
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > r * r) continue;
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && xx < w && yy >= 0 && yy < h) op[yy * w + xx] = true;
                        }
        return op;
    }

    [Test]
    public void Score_Egg_SmoothOutline_RoundnessDoesNotPenalize()
    {
        var d = Egg(true);   // outer la ellipse tron, nhu truoc gio
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var r = ScoreEgg(d, shape: 0.97f);
        TestContext.WriteLine(r.Report);

        Assert.GreaterOrEqual(r.PartResults[0].RoundnessFactor, 0.95f);
        Assert.GreaterOrEqual(r.Total, 0.9f);
    }

    [Test]
    public void Score_ReportsRawCircularity_ForLogging()
    {
        var d = Egg(true);
        FillAt(d, 90, 256, _colors.IndexOf("white"));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));
        var r = ScoreEgg(d);
        TestContext.WriteLine(r.Report + "outer=" + r.OuterCircularity + " inner=" + r.InnerCircularity);

        // Số đã đo độc lập: lòng trắng (đã trừ lỗ) ~0.32, lòng đỏ ~0.57
        Assert.Greater(r.OuterCircularity, 0.28f);
        Assert.Less(r.OuterCircularity, 0.36f);
        Assert.Greater(r.InnerCircularity, 0.52f);
        Assert.Less(r.InnerCircularity, 0.62f);
        // lòng đỏ có minRoundness nên PartResult ghi lại cùng số đo; lòng trắng dùng maxSpikiness
        Assert.AreEqual(r.InnerCircularity, r.PartResults[1].Circularity, 0.0001f);
    }

    [Test]
    public void Score_NoMinRoundness_SkipsCheck()
    {
        var rules = new[] { new PartRule { name = "body", role = PartRole.Outer, minCount = 1,
                                           maxCount = 1, weight = 0.6f } };   // minRoundness = 0 (mac dinh)
        var d = Draw(JaggedBlob(256, 256, 150, 0.4f, 6), 25f);
        var regions = RegionAnalyzer.Analyze(d.Barrier, W, H);
        var r = IngredientScorer.Score(1f, regions, d.Fill, _colors, 0.4f, rules, 0.5f);
        Assert.AreEqual(1f, r.PartResults[0].RoundnessFactor, 0.0001f);
    }

    /// <summary>
    /// Ly do can roundnessCap rieng (khong chi nhan RoundnessFactor vao diem bo
    /// phan): weight cua long trang chi 0.2 nen du RoundnessFactor tut het, phan
    /// dong gop vao tong cung chi giam toi da 0.2 — khong du keo tong xuong duoi
    /// nguong dat neu hinh va long do van tot. Test nay khoa chat GIA TRI cap
    /// chinh xac (giong cach Score_NoRules_ShapeTooLow_IsStillCapped khoa shapeCap).
    /// </summary>
    [Test]
    public void Score_Egg_SpikyOutline_UsesCapValueExactly()
    {
        var d = Draw(Concat(SpikyWhite(12, 0.30f, 0.08f), Ellipse(270, 250, 45, 45, 1)), 25f);
        Assert.IsTrue(FillWhite(d));
        FillAt(d, 270, 250, _colors.IndexOf("yellow"));

        var regions = RegionAnalyzer.Analyze(d.Barrier, W, H);
        // hình ($P) cao, không thiếu bộ phận — chỉ có gai vi phạm, nên cap này là thứ chặn tổng
        var r = IngredientScorer.Score(0.9f, regions, d.Fill, _colors, 0.4f,
                                       PartPresets.FriedEgg(), missingCap: 0.5f,
                                       minShape: 0.5f, shapeCap: 0.5f, roundnessCap: 0.42f);
        TestContext.WriteLine(r.Report);

        Assert.IsTrue(r.Capped);
        Assert.AreEqual(0.42f, r.Total, 0.0001f);
    }
}
