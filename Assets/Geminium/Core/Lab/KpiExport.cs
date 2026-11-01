using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Geminium.Core.Lab
{
    // Выгрузка KPI в CSV для Excel (GDD 10): строка на компанию и месяц.
    // Разделитель «;» и десятичная запятая — так файл без настройки открывает русский Excel.
    // Колонки: месяц, макроэкономика, P&L, баланс, капитализация, выпуск и продажи, доли и цены сырья.
    public static class KpiExport
    {
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        public static string ToCsv(GameWorld world)
        {
            var catalog = world.Catalog;
            var products = catalog.Products.Select(p => p.Id).ToList();
            var materials = catalog.Materials.Select(m => m.Id).ToList();

            var header = new List<string>
            {
                "month", "date", "company",
                "economy_index", "inflation_pct", "key_rate_pct", "seasonality", "price_level", "events",
                "revenue", "cogs", "wages", "social_tax", "rent", "energy", "penalties", "other_expenses", "interest", "depreciation",
                "pretax_profit", "profit_tax", "net_income", "cash", "debt", "inventory_value", "equipment_value", "capitalization",
                "credit_rating", "reputation", "bankrupt",
                "units_produced", "units_defective", "units_sold", "lost_sales", "overflow_days",
            };
            header.AddRange(products.Select(p => "sold_" + p));
            header.AddRange(products.Select(p => "share_pct_" + p));
            header.AddRange(materials.Select(m => "price_" + m));

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(";", header));

            int months = world.Companies.Count == 0 ? 0 : world.Companies.Max(c => c.Reports.Count);
            for (int month = 0; month < months; month++)
            {
                var economy = month < world.Economy.History.Count ? world.Economy.History[month] : null;
                var prices = month < world.Supply.PriceHistory.Count ? world.Supply.PriceHistory[month] : new Dictionary<string, double>();
                DateTime date = world.Simulation.Calendar.StartDate.AddMonths(month);

                foreach (var company in world.Companies)
                {
                    if (month >= company.Reports.Count) continue;
                    var r = company.Reports[month];
                    var row = new List<string>
                    {
                        (month + 1).ToString(Russian), date.ToString("yyyy-MM", CultureInfo.InvariantCulture), company.Id,
                        Num(economy?.Index), Num(economy?.Inflation * 100), Num(economy?.KeyRate * 100), Num(economy?.Seasonality),
                        Num(economy?.PriceLevel), economy == null ? "" : string.Join(" ", economy.ActiveEventIds),
                        Num(r.Revenue), Num(r.CostOfGoodsSold), Num(r.Wages), Num(r.SocialTax), Num(r.Rent), Num(r.Energy),
                        Num(r.Penalties), Num(r.OtherExpenses), Num(r.Interest), Num(r.Depreciation),
                        Num(r.PreTaxProfit), Num(r.ProfitTax), Num(r.NetIncome), Num(r.Cash), Num(r.Debt), Num(r.InventoryValue),
                        Num(r.MachinesValue), Num(r.Capitalization), Num(r.CreditRating), Num(r.Reputation), r.Bankrupt ? "1" : "0",
                        Num(r.UnitsProduced), Num(r.UnitsDefective), Num(r.UnitsSold), Num(r.LostSales), Num(r.OverflowDays),
                    };
                    row.AddRange(products.Select(p => Num(r.SoldByProduct.TryGetValue(p, out long sold) ? sold : 0)));
                    row.AddRange(products.Select(p => Num((r.MarketShareByProduct.TryGetValue(p, out double share) ? share : 0) * 100)));
                    row.AddRange(materials.Select(m => Num(prices.TryGetValue(m, out double price) ? price : (double?)null)));
                    csv.AppendLine(string.Join(";", row));
                }
            }
            return csv.ToString();
        }

        private static string Num(long value) => value.ToString(Russian.NumberFormat);

        private static string Num(double? value) => value.HasValue ? Math.Round(value.Value, 4).ToString("0.####", Russian) : "";
    }
}
