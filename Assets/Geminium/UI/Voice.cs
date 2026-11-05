using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.News;

namespace Geminium.UI
{
    // «Голос» ИИ-ядра (GDD 9), первая ступень — шаблоны из таблицы локализации: новости, объяснение ходов ботов,
    // сводка месяца. Все числа подставляются из симуляции, поэтому верны на любом языке. Языковая модель,
    // когда появится, будет переписывать эти тексты живее; её ответ проверит NumberGuard, а при ошибке
    // останется шаблон.
    public static class Voice
    {
        public static string News(NewsItem item)
        {
            var a = item.Args;
            switch (item.Key)
            {
                case "news.event":
                    string eventKey = "news.event." + a[0];
                    return Loc.Has(eventKey) ? Loc.Get(eventKey, Fmt.Percent(Math.Abs(Convert.ToDouble(a[1]))), a[2]) : Loc.Get("news.event", Loc.Name((string)a[0]));
                case "news.tender":
                    return Loc.Get("news.tender", Loc.Get("enum.TenderLevel." + a[0]), a[1], a[2], Fmt.Money(Convert.ToInt64(a[3])));
                case "news.tender_won":
                    return Loc.Get("news.tender_won", item.CompanyId, Loc.Get("enum.TenderLevel." + a[0]), a[1], Fmt.Money(Convert.ToInt64(a[2])));
                case "news.bot_price":
                    double pct = Convert.ToDouble(a[1]);
                    return Loc.Get(pct < 0 ? "news.bot_price_down" : "news.bot_price_up", item.CompanyId, a[0], Math.Abs(pct), Loc.Get((string)a[2]));
                case "news.bot_expand":
                    return Loc.Get("news.bot_expand", item.CompanyId, a[0]);
                case "news.bot_ads":
                    return Loc.Get("news.bot_ads", item.CompanyId, Fmt.Money(Convert.ToInt64(a[0])));
                case "news.ambassador":
                    return Loc.Get("news.ambassador", item.CompanyId, a[0]);
                default:
                    return Loc.Get(item.Key, a);
            }
        }

        // Сводка месяца: несколько фраз по отчёту (что выросло, что мешало). Только факты из отчёта.
        public static string MonthSummary(Company company, int month)
        {
            var reports = company.Reports;
            var r = reports.FirstOrDefault(x => x.Month == month);
            if (r == null) return Loc.Get("summary.none");
            var previous = reports.FirstOrDefault(x => x.Month == month - 1);
            var lines = new List<string>();

            if (previous != null && previous.Revenue > 0)
                lines.Add(Loc.Get("summary.revenue_change", Fmt.Money(r.Revenue), Fmt.Change((r.Revenue - previous.Revenue) / (double)previous.Revenue)));
            else
                lines.Add(Loc.Get("summary.revenue", Fmt.Money(r.Revenue)));
            lines.Add(Loc.Get(r.NetIncome >= 0 ? "summary.profit" : "summary.loss", Fmt.Money(Math.Abs(r.NetIncome))));

            long made = r.UnitsProduced + r.UnitsDefective;
            if (made > 0) lines.Add(Loc.Get("summary.output", r.UnitsProduced, Fmt.Percent(r.UnitsDefective / (double)made, 1)));
            var best = r.SoldByProduct.Where(p => p.Value > 0).OrderByDescending(p => p.Value).FirstOrDefault();
            if (best.Key != null) lines.Add(Loc.Get("summary.best", best.Key, best.Value));
            if (r.LostSales > 0.1 * Math.Max(1, r.UnitsSold)) lines.Add(Loc.Get("summary.lost_sales", r.LostSales));

            var downtime = new[]
            {
                ("downtime.no_operator", r.DowntimeNoOperatorHours), ("downtime.no_materials", r.DowntimeNoMaterialsHours),
                ("downtime.broken", r.DowntimeBrokenHours), ("downtime.maintenance", r.DowntimeMaintenanceHours),
            }.OrderByDescending(d => d.Item2).First();
            if (downtime.Item2 >= 1) lines.Add(Loc.Get("summary.downtime", Loc.Get(downtime.Item1), Fmt.Number(downtime.Item2)));
            if (r.TendersWon > 0) lines.Add(Loc.Get("summary.tenders", r.TendersWon));
            if (r.ReturnedUnits > 0) lines.Add(Loc.Get("summary.returns", r.ReturnedUnits));
            lines.Add(Loc.Get("summary.cash", Fmt.Money(r.Cash)));
            return string.Join(" ", lines);
        }

        // Числа сводки — то, что языковая модель может упомянуть (для NumberGuard).
        public static IEnumerable<double> SummaryNumbers(Company company, int month)
        {
            var r = company.Reports.FirstOrDefault(x => x.Month == month);
            if (r == null) yield break;
            foreach (double v in new double[] { r.Revenue, r.NetIncome, r.UnitsProduced, r.UnitsDefective, r.UnitsSold, r.LostSales, r.Cash, r.TendersWon, r.ReturnedUnits })
                yield return v;
            foreach (var p in r.SoldByProduct) yield return p.Value;
            long made = r.UnitsProduced + r.UnitsDefective;
            if (made > 0) yield return Math.Round(100.0 * r.UnitsDefective / made, 1);
            var previous = company.Reports.FirstOrDefault(x => x.Month == month - 1);
            if (previous != null && previous.Revenue > 0) yield return Math.Round(100.0 * Math.Abs(r.Revenue - previous.Revenue) / previous.Revenue);
            foreach (double h in new[] { r.DowntimeNoOperatorHours, r.DowntimeNoMaterialsHours, r.DowntimeBrokenHours, r.DowntimeMaintenanceHours })
                yield return Math.Round(h);
        }
    }
}
