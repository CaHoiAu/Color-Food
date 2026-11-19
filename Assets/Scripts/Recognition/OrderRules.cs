using System;
using System.Collections.Generic;

namespace ColorFood.Recognition
{
    /// <summary>Kết quả cả món: đạt khi MỌI nguyên liệu đạt.</summary>
    public struct DishOutcome
    {
        public bool Passed;
        public float Average;
        public int Stars;
        public int Coins;
    }

    /// <summary>
    /// Luật của đơn hàng: điểm → sao → xu (từng nguyên liệu và cả món), và lời nhận
    /// xét cho người chơi từ kết quả chấm (SketchResult). Mọi chữ người chơi thấy đều
    /// bằng TIẾNG ANH. Thuần C# để test được; GameLoop chỉ hiển thị.
    /// </summary>
    public static class OrderRules
    {
        public const float DefaultPass = 0.7f;
        public const int DefaultCoinsPerStar = 10;

        /// <summary>0 sao: chưa đạt (&lt; pass). 1 sao: đạt. 2 sao: ≥ 80%. 3 sao: ≥ 90%.</summary>
        public static int Stars(float total, float pass = DefaultPass)
        {
            if (total < pass) return 0;
            if (total >= 0.9f) return 3;
            if (total >= 0.8f) return 2;
            return 1;
        }

        public static int Coins(int stars, int coinsPerStar = DefaultCoinsPerStar)
            => Math.Max(0, stars) * Math.Max(0, coinsPerStar);

        /// <summary>
        /// Cả món: đạt khi mọi nguyên liệu ≥ pass. Sao theo điểm TRUNG BÌNH (món chưa đạt:
        /// 0 sao). Xu = sao × coinsPerStar × số nguyên liệu (món nhiều nguyên liệu thưởng nhiều hơn).
        /// </summary>
        public static DishOutcome Dish(IReadOnlyList<float> totals, float pass = DefaultPass,
                                       int coinsPerStar = DefaultCoinsPerStar)
        {
            var o = new DishOutcome();
            if (totals == null || totals.Count == 0) return o;

            float sum = 0f;
            o.Passed = true;
            foreach (float t in totals)
            {
                sum += t;
                if (t < pass) o.Passed = false;
            }
            o.Average = sum / totals.Count;
            o.Stars = o.Passed ? Stars(o.Average, pass) : 0;
            o.Coins = Coins(o.Stars, coinsPerStar) * totals.Count;
            return o;
        }

        /// <summary>
        /// Tối đa max lời nhận xét (tiếng Anh), lỗi nặng nhất trước: giống nguyên liệu khác →
        /// hình sai → dài / tròn sai → thiếu / sai chi tiết → thiếu màu / sai màu. Bức vẽ
        /// tốt thì một lời khen.
        /// </summary>
        public static List<string> Feedback(SketchResult r, ColorTable colors, string ingredient,
                                            float pass = DefaultPass, int max = 3)
        {
            var list = new List<string>();
            if (r == null)
            {
                list.Add("You haven't drawn anything yet.");
                return list;
            }

            if (r.RivalCapped || (r.Rival.Length > 0 && r.RivalLead > r.RivalMargin))
                list.Add("This looks more like " + r.Rival + " than " + ingredient + ".");

            if (r.ShapeCapped || r.Shape < 0.5f)
                list.Add("The shape doesn't look like " + ingredient + " yet. Check the reference.");

            if (r.ElongationFactor < 1f)
                list.Add(r.Elongation > r.TemplateElongation
                    ? "Too long and thin compared to the reference."
                    : "Too round and wide compared to the reference.");

            foreach (DetailResult d in r.DetailParts)
            {
                if (d.Missing) list.Add("The " + ColorName(colors, d.ColorId) + " part is missing.");
                else if (d.Score < 0.5f) list.Add("The " + ColorName(colors, d.ColorId) + " part has the wrong shape.");
            }

            if (r.HasColor)
                foreach (ColorPartResult c in r.Parts)
                {
                    if (c.ColorId < 0) continue;
                    if (c.Coverage < 0.5f) list.Add("Color in more " + ColorName(colors, c.ColorId) + ".");
                    else if (c.Accuracy < 0.6f) list.Add("The " + ColorName(colors, c.ColorId) + " part has the wrong color.");
                }

            if (list.Count == 0)
                list.Add(r.Total >= 0.9f ? "Perfect! Just like the reference."
                       : r.Total >= pass ? "Nice work!"
                       : "Almost there. Try a little more carefully.");

            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>Tên màu cho người chơi (tên trong palette, tiếng Anh).</summary>
        public static string ColorName(ColorTable colors, int id)
            => colors != null ? colors.NameOf(id) : "";
    }
}
