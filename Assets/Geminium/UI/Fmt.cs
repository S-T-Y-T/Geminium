using System;

namespace Geminium.UI
{
    // Числа для интерфейса — по культуре языка игрока.
    public static class Fmt
    {
        public static string Money(long sum) => string.Format(Loc.Culture, "{0:N0} {1}", sum, Loc.Get("unit.sum"));

        public static string Money(double sum) => Money((long)Math.Round(sum));

        // Крупные суммы: «12,3 млн сум».
        public static string Millions(double sum) => Loc.Get("unit.millions", sum / 1e6);

        public static string Number(double value, int decimals = 0) => value.ToString("N" + decimals, Loc.Culture);

        public static string Percent(double share, int decimals = 0) => (share * 100).ToString("N" + decimals, Loc.Culture) + "%";

        public static string Date(DateTime date) => date.ToString("d MMMM yyyy", Loc.Culture);

        public static string Month(DateTime date) => date.ToString("MMMM yyyy", Loc.Culture);

        // Знак изменения: «+5%», «−3%».
        public static string Change(double share) => (share >= 0 ? "+" : "−") + Percent(Math.Abs(share));
    }
}
