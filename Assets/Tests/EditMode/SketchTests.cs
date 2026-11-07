using System;
using System.Collections.Generic;
using NUnit.Framework;
using ColorFood.Recognition;

/// <summary>
/// Cách chấm "ảnh màu" (SketchScorer): không phân biệt nét viền / nét tô.
/// Hình vẽ dựng trên bản đồ màu 512×512 như canvas thật rồi thu về ColorSketch.
/// </summary>
public class SketchTests
{
    private const int W = 512;
    private const int Red = 5, Green = 11, Blue = 15;
    private ColorTable _colors;
    private PDollarRecognizer _recognizer;

    [SetUp]
    public void Setup()
    {
        _colors = new ColorTable(DefaultPalette.Names, DefaultPalette.Colors);
        _recognizer = new PDollarRecognizer();
    }

    // ------------------------------------------------------------------
    // Dựng hình trên bản đồ màu
    // ------------------------------------------------------------------

    private static int[] Blank()
    {
        var paint = new int[W * W];
        for (int i = 0; i < paint.Length; i++) paint[i] = -1;
        return paint;
    }

    /// <summary>Điểm (x,y) so với elip: &lt;1 bên trong, 1 trên viền.</summary>
    private static double E(int x, int y, float cx, float cy, float rx, float ry)
        => Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2);

    /// <summary>Tô đặc elip.</summary>
    private static void Solid(int[] p, float cx, float cy, float rx, float ry, int color)
    {
        for (int y = 0; y < W; y++)
            for (int x = 0; x < W; x++)
                if (E(x, y, cx, cy, rx, ry) <= 1) p[y * W + x] = color;
    }

    /// <summary>Như người chơi: viền dày ~8px + tô sọc kiểu sáp (per10/10 số cột).</summary>
    private static void Crayon(int[] p, float cx, float cy, float rx, float ry, int color, int per10 = 7)
    {
        for (int y = 0; y < W; y++)
            for (int x = 0; x < W; x++)
            {
                double e = E(x, y, cx, cy, rx, ry);
                bool ring = e <= 1 && e >= Math.Pow(1 - 8.0 / Math.Min(rx, ry), 2);
                bool fill = e < 1 && x % 10 < per10;
                if (ring || fill) p[y * W + x] = color;
            }
    }

    /// <summary>Mẫu: thân đỏ, lá xanh lệch phải phía trên.</summary>
    private static ColorSketch Template()
    {
        var p = Blank();
        Solid(p, 256, 230, 140, 140, Red);
        Solid(p, 300, 385, 50, 28, Green);
        return ColorSketch.FromPaint(p, W, W);
    }

    private List<SketchShape> Prepare(ColorSketch s, bool mirror = true)
        => SketchScorer.PrepareTemplate(s, "tomato", 30f, 15f, mirror);

    private SketchResult Score(int[] paint, List<SketchShape> template)
        => SketchScorer.Score(ColorSketch.FromPaint(paint, W, W), template, _recognizer, _colors,
                              0.12f, 0.4f, 0.6f);

    /// <summary>Người chơi vẽ cùng hình nhưng nhỏ hơn, ở chỗ khác, bằng viền + tô sáp.</summary>
    private static int[] PlayerTomato(int body = Red, int leaf = Green, int fillPer10 = 7,
                                      float leafDx = 31f, float leafDy = 108f)
    {
        var p = Blank();
        Crayon(p, 220, 250, 98, 98, body, fillPer10);
        Crayon(p, 220 + leafDx, 250 + leafDy, 35, 20, leaf, fillPer10);
        return p;
    }

    // ------------------------------------------------------------------
    // ColorSketch
    // ------------------------------------------------------------------

    [Test]
    public void Sketch_Rle_RoundTrip()
    {
        var s = Template();
        var back = ColorSketch.FromRle(s.Size, s.ToRle());
        Assert.IsNotNull(back);
        CollectionAssert.AreEqual(s.Cells, back.Cells);
        Assert.IsNull(ColorSketch.FromRle(s.Size, "5:3"), "chuỗi thiếu ô phải bị từ chối");
    }

    [Test]
    public void Sketch_FromPaint_MajorityColor_AndIgnoresSparseCells()
    {
        var p = Blank();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) p[y * W + x] = x < 3 ? Red : Green;   // ô (0,0): 12 đỏ, 4 xanh
        p[10 * W + 10] = Blue;                                                // ô (2,2): 1/16 pixel → trống

        var s = ColorSketch.FromPaint(p, W, W);   // 512 → 128: mỗi ô 4×4 pixel
        Assert.AreEqual(Red, s.Cells[0]);
        Assert.AreEqual(ColorSketch.Empty, s.Cells[2 * 128 + 2]);
    }

    // ------------------------------------------------------------------
    // Hình
    // ------------------------------------------------------------------

    [Test]
    public void Silhouette_FillsHoles_AndCrayonGaps()
    {
        var p = Blank();
        Crayon(p, 256, 256, 150, 150, Red, per10: 3);   // viền + tô thưa
        var s = ColorSketch.FromPaint(p, W, W);
        var sil = SketchScorer.Silhouette(SketchScorer.KeptPaint(s), s.Size);

        int c = 64 * 128 + 64;
        Assert.IsTrue(sil[c], "giữa hình phải đặc");
        Assert.IsFalse(sil[2 * 128 + 2], "góc ngoài vẫn trống");

        int filled = 0;
        foreach (bool b in sil) if (b) filled++;
        double disk = Math.PI * 37.5 * 37.5;   // bán kính 150/4 ô
        Assert.AreEqual(disk, filled, disk * 0.1, "bóng ≈ hình tròn đặc");
    }

    [Test]
    public void KeptPaint_DropsStrayDot()
    {
        var p = Blank();
        Solid(p, 256, 256, 120, 120, Red);
        Solid(p, 40, 40, 6, 6, Blue);    // chấm lạc
        var s = ColorSketch.FromPaint(p, W, W);
        var kept = SketchScorer.KeptPaint(s);

        Assert.IsTrue(kept[64 * 128 + 64]);
        Assert.IsFalse(kept[10 * 128 + 10], "chấm lạc bị bỏ");
    }

    [Test]
    public void FarthestPointSample_IsSpreadAndDeterministic()
    {
        var pts = new List<Point>();
        for (int i = 0; i < 400; i++)
        {
            double a = 2 * Math.PI * i / 400;
            pts.Add(new Point((float)(100 * Math.Cos(a)), (float)(100 * Math.Sin(a)), 0));
        }
        var s1 = SketchScorer.FarthestPointSample(pts, 32);
        var s2 = SketchScorer.FarthestPointSample(pts, 32);

        Assert.AreEqual(32, s1.Length);
        CollectionAssert.AreEqual(s1, s2);
        float minD = float.MaxValue;
        for (int i = 0; i < s1.Length; i++)
            for (int j = i + 1; j < s1.Length; j++) minD = Math.Min(minD, Point.Distance(s1[i], s1[j]));
        // 32 điểm cách đều trên vòng bán kính 100: khoảng cách kề ≈ 19.6
        Assert.Greater(minD, 12f);
    }

    // ------------------------------------------------------------------
    // Chấm điểm
    // ------------------------------------------------------------------

    [Test]
    public void Score_SameDrawing_DifferentSizeAndPlace_IsHigh()
    {
        var r = Score(PlayerTomato(), Prepare(Template()));

        Assert.IsTrue(r.HasColor);
        Assert.Greater(r.Shape, 0.7f, r.Report);
        Assert.Greater(r.Color, 0.9f, r.Report);
        Assert.Greater(r.Total, 0.8f, r.Report);
    }

    [Test]
    public void Score_SwappedColors_IsLow()
    {
        var r = Score(PlayerTomato(body: Green, leaf: Red), Prepare(Template()));
        Assert.Less(r.Color, 0.3f, r.Report);
        Assert.Less(r.Total, 0.7f, r.Report);
    }

    [Test]
    public void Score_OutlineOnly_LowCoverage()
    {
        var r = Score(PlayerTomato(fillPer10: 0), Prepare(Template()));
        Assert.Less(r.Coverage, 0.5f, r.Report);
        Assert.Less(r.Color, 0.8f, r.Report);
    }

    [Test]
    public void Score_LeafInWrongPlace_GreenPartIsLow()
    {
        var good = Score(PlayerTomato(), Prepare(Template()));
        var bad = Score(PlayerTomato(leafDy: -108f), Prepare(Template(), mirror: false));

        ColorPartResult green = bad.Parts.Find(p => p.ColorId == Green);
        Assert.IsNotNull(green, bad.Report);
        Assert.Less(green.Score, 0.5f, bad.Report);
        Assert.Less(bad.Total, good.Total, bad.Report);
        Assert.Less(bad.Total, 0.7f, "lá sai chỗ không được đạt — mỗi phần màu có trọng số ngang nhau. " + bad.Report);
    }

    [Test]
    public void Score_MirroredLeaf_MatchesMirrorVariant()
    {
        var r = Score(PlayerTomato(leafDx: -31f), Prepare(Template()));
        // Tên biến thể chỉ để hiển thị — không phân biệt hoa/thường, dấu cách
        StringAssert.Contains("lật", r.Variant.ToLowerInvariant(), r.Report);
        Assert.Greater(r.Total, 0.8f, r.Report);
    }

    [Test]
    public void Score_TemplateWithoutColor_ScoresShapeOnly()
    {
        // Mẫu cũ: chỉ có nét viền đen
        var pts = new List<Point>();
        for (int i = 0; i <= 120; i++)
        {
            double a = 2 * Math.PI * i / 120;
            pts.Add(new Point((float)(200 + 150 * Math.Cos(a)), (float)(200 + 150 * Math.Sin(a)), 0));
        }
        var template = Prepare(ColorSketch.FromStrokes(pts));
        var r = Score(PlayerTomato(), template);

        Assert.IsFalse(r.HasColor);
        Assert.AreEqual(r.Shape, r.Total, 1e-6f);
    }

    [Test]
    public void Score_NothingDrawn_ReturnsNull()
    {
        Assert.IsNull(Score(Blank(), Prepare(Template())));
    }
}
