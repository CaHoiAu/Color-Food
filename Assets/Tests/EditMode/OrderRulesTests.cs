using System.Collections.Generic;
using NUnit.Framework;
using ColorFood.Recognition;

/// <summary>Luật đơn hàng: điểm → sao → xu (nguyên liệu và cả món), lời nhận xét tiếng Anh.</summary>
public class OrderRulesTests
{
    private const int Red = 5, Green = 11;
    private ColorTable _colors;

    [SetUp]
    public void Setup() => _colors = new ColorTable(DefaultPalette.Names, DefaultPalette.Colors);

    private static SketchResult Good() => new SketchResult
    {
        Total = 0.92f,
        Shape = 0.85f,
        Color = 1f,
        Detail = 0.9f,
        HasColor = true,
        Parts = new List<ColorPartResult>
        {
            new ColorPartResult { ColorId = Red, Coverage = 0.9f, Accuracy = 1f, Score = 1f },
            new ColorPartResult { ColorId = Green, Coverage = 0.9f, Accuracy = 1f, Score = 1f }
        },
        DetailParts = new List<DetailResult> { new DetailResult { ColorId = Green, Score = 0.9f, Distance = 0.06f } }
    };

    [Test]
    public void Stars_FollowThresholds()
    {
        Assert.AreEqual(0, OrderRules.Stars(0.69f));
        Assert.AreEqual(1, OrderRules.Stars(0.70f));
        Assert.AreEqual(1, OrderRules.Stars(0.79f));
        Assert.AreEqual(2, OrderRules.Stars(0.80f));
        Assert.AreEqual(3, OrderRules.Stars(0.90f));
        Assert.AreEqual(0, OrderRules.Stars(0.75f, pass: 0.8f), "ngưỡng đạt chỉnh được");
    }

    [Test]
    public void Coins_StarsTimesRate()
    {
        Assert.AreEqual(0, OrderRules.Coins(0));
        Assert.AreEqual(30, OrderRules.Coins(3));
        Assert.AreEqual(10, OrderRules.Coins(2, 5));
    }

    [Test]
    public void Feedback_GoodDrawing_Praises()
    {
        var f = OrderRules.Feedback(Good(), _colors, "Tomato");
        Assert.AreEqual(1, f.Count);
        StringAssert.Contains("Perfect", f[0]);
    }

    [Test]
    public void Feedback_Nothing_Drawn()
    {
        var f = OrderRules.Feedback(null, _colors, "Tomato");
        StringAssert.Contains("haven't drawn", f[0]);
    }

    [Test]
    public void Feedback_Rival_ComesFirst()
    {
        var r = Good();
        r.Total = 0.5f; r.Shape = 0.3f; r.Rival = "Bell Pepper"; r.RivalLead = 0.2f; r.RivalMargin = 0.05f; r.RivalCapped = true;
        var f = OrderRules.Feedback(r, _colors, "Tomato");
        StringAssert.Contains("Bell Pepper", f[0]);
        StringAssert.Contains("shape", f[1]);
    }

    [Test]
    public void Feedback_MissingLeaf_And_ColorProblems()
    {
        var r = Good();
        r.Total = 0.55f;
        r.DetailParts[0].Missing = true; r.DetailParts[0].Score = 0f;
        r.Parts[0].Coverage = 0.3f;                 // thân tô ít
        var f = OrderRules.Feedback(r, _colors, "Tomato");
        CollectionAssert.Contains(f, "The green part is missing.");
        CollectionAssert.Contains(f, "Color in more red.");
    }

    [Test]
    public void Feedback_Elongation_SaysWhichWay()
    {
        var r = Good();
        r.ElongationFactor = 0.5f; r.Elongation = 1.6f; r.TemplateElongation = 1.1f;
        StringAssert.Contains("long", OrderRules.Feedback(r, _colors, "Tomato")[0]);
        r.Elongation = 1.0f; r.TemplateElongation = 1.6f;
        StringAssert.Contains("round", OrderRules.Feedback(r, _colors, "Bell Pepper")[0]);
    }

    [Test]
    public void Feedback_IsCappedAtMax()
    {
        var r = Good();
        r.Total = 0.3f; r.Shape = 0.2f; r.ShapeCapped = true;
        r.ElongationFactor = 0.5f; r.Elongation = 1.6f; r.TemplateElongation = 1.0f;
        r.DetailParts[0].Missing = true;
        r.Parts[0].Coverage = 0.2f; r.Parts[1].Accuracy = 0.1f;
        Assert.AreEqual(3, OrderRules.Feedback(r, _colors, "Tomato", max: 3).Count);
    }

    [Test]
    public void Dish_PassesOnlyWhenEveryIngredientPasses()
    {
        var ok = OrderRules.Dish(new List<float> { 0.95f, 0.85f });
        Assert.IsTrue(ok.Passed);
        Assert.AreEqual(0.9f, ok.Average, 1e-5f);
        Assert.AreEqual(3, ok.Stars);
        Assert.AreEqual(3 * 10 * 2, ok.Coins, "sao × xu/sao × số nguyên liệu");

        var bad = OrderRules.Dish(new List<float> { 0.95f, 0.6f });
        Assert.IsFalse(bad.Passed);
        Assert.AreEqual(0, bad.Stars);
        Assert.AreEqual(0, bad.Coins);

        Assert.IsFalse(OrderRules.Dish(new List<float>()).Passed);
    }

    // ------------------------------------------------------------------
    // Làm sạch bức vẽ thành dạng hoàn thiện
    // ------------------------------------------------------------------

    [Test]
    public void Cleaner_FillsCrayonGaps_KeepsColors()
    {
        const int W = 512;
        var p = new int[W * W];
        for (int i = 0; i < p.Length; i++) p[i] = -1;
        Crayon(p, W, 220, 250, 98, 98, Red);
        Crayon(p, W, 251, 358, 35, 20, Green);
        var s = ColorSketch.FromPaint(p, W, W);

        var c = SketchCleaner.Clean(s);
        var sil = SketchScorer.Silhouette(SketchScorer.KeptPaint(s), s.Size);

        int holes = 0, before = 0, after = 0;
        for (int i = 0; i < sil.Length; i++)
        {
            if (sil[i] && c.Cells[i] == ColorSketch.Empty) holes++;
            if (s.Cells[i] != ColorSketch.Empty) before++;
            if (c.Cells[i] != ColorSketch.Empty) after++;
        }
        Assert.AreEqual(0, holes, "không còn khe / lỗ trong hình");
        Assert.Greater(after, before, "khe giữa các nét sáp đã được lấp");
        Assert.AreEqual(Red, c.Cells[125 * 256 + 110], "giữa thân vẫn đỏ");
        Assert.AreEqual(Green, c.Cells[179 * 256 + 125], "giữa lá vẫn xanh");
        Assert.AreEqual(ColorSketch.Empty, c.Cells[5 * 256 + 5], "ngoài hình vẫn trống");
    }

    /// <summary>Viền dày ~8px + tô sọc kiểu sáp (7/10 cột), như SketchTests.</summary>
    private static void Crayon(int[] p, int w, float cx, float cy, float rx, float ry, int color)
    {
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                double e = System.Math.Pow((x - cx) / rx, 2) + System.Math.Pow((y - cy) / ry, 2);
                bool ring = e <= 1 && e >= System.Math.Pow(1 - 8.0 / System.Math.Min(rx, ry), 2);
                if (ring || (e < 1 && x % 10 < 7)) p[y * w + x] = color;
            }
    }
}
