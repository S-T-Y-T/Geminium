using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Geminium.Core.News
{
    // Проверка чисел в тексте языковой модели (GDD 9: «числа не придумываются»). Каждое число в ответе должно
    // совпадать с одним из чисел симуляции (с точностью до округления); иначе ответ отбрасывается и берётся шаблон.
    public static class NumberGuard
    {
        // Числа вида 1 234 567, 1,234,567, 12,5, 12.5, −8, 8% (разделители групп — пробел, неразрывный пробел, запятая).
        // Порядок важен: сначала разряды через пробел, потом через запятую (1,234,567), потом простое число с дробью.
        private static readonly Regex Number = new Regex(
            @"[-−]?\d{1,3}(?:[   ]\d{3})+(?:[.,]\d+)?|[-−]?\d{1,3}(?:,\d{3})+(?:\.\d+)?(?!\d)|[-−]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        public static IReadOnlyList<double> Extract(string text)
        {
            var result = new List<double>();
            foreach (Match m in Number.Matches(text ?? ""))
            {
                string s = m.Value.Replace('−', '-').Replace(" ", "").Replace(" ", "").Replace(" ", "");
                // «1,234,567» — разделители групп; «12,5» — десятичная запятая.
                if (Regex.IsMatch(s, @"^-?\d{1,3}(,\d{3})+$")) s = s.Replace(",", "");
                else s = s.Replace(',', '.');
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) result.Add(value);
            }
            return result;
        }

        // Все числа текста есть среди разрешённых (по модулю, с относительной погрешностью округления).
        public static bool Check(string text, IEnumerable<double> allowed, double relativeTolerance = 0.005)
        {
            var values = allowed.Select(Math.Abs).ToList();
            return Extract(text).All(n => values.Any(a => Math.Abs(Math.Abs(n) - a) <= Math.Max(0.5, a * relativeTolerance)));
        }
    }
}
