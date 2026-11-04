using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geminium.Core.Data;

namespace Geminium.Core.Lab
{
    // Итоги одного прогона сценария для фабрики игрока.
    public sealed class ScenarioRun
    {
        public string Scenario;
        public ulong Seed;
        public int Months;
        public int PurchaseMonth;             // первая покупка модуля; до неё прогон совпадает с базой
        public double[] NetIncome;            // по месяцам
        public double[] DigitalDepreciation;  // по месяцам (входит в чистую прибыль)
        public double[] KeyRate;              // годовая ставка по месяцам — для дисконтирования
        public double DigitalCapex, DigitalLicense, DigitalSubsidy;
        public double Revenue, UnitCost, DefectShare, ReturnedUnits, DowntimeHours, LogisticsKm, FabricWaste,
            LostSales, InventoryValue, Breakdowns, Maintenances, Capitalization;
    }

    // Эффект сценария относительно базы (GDD 10).
    public sealed class ScenarioSummary
    {
        public string Scenario;
        public string Modules;
        public string Baseline;
        public int Seeds;
        public double Capex;                  // внедрение в среднем, сумы
        public double DeltaPerMonth;          // ΔNI в среднем за месяц после покупки (без амортизации модулей, с лицензиями)
        public double DeltaSd;                // разброс ΔNI между seed
        public double Ci95;                   // половина 95%-го доверительного интервала
        public double Roi;                    // доля
        public double PaybackMonths;          // ∞ — не окупается
        public double Npv;
        public readonly Dictionary<string, double> KpiDelta = new Dictionary<string, double>();
    }

    // Лаборатория сценариев (GDD 10, пункт 4.6): каждый сценарий — N прогонов с разными seed без графики,
    // эффект модуля = парная разница KPI сценария и базы на одном seed (экономика и события совпадают).
    //   ΔNI_t = (NI_t + амортизация модулей_t)_сценарий − NI_t,база   — амортизацию не вычитаем дважды;
    //   средний ΔNI — за месяцы после первой покупки (до неё прогоны совпадают)
    //   ROI = (Σ ΔNI − Capex) / Capex;   срок окупаемости = Capex / ΔNI_ср;   NPV = −Capex + Σ ΔNI_t / (1 + r_t/12)^t
    public static class ScenarioLab
    {
        public static readonly string[] KpiNames =
        {
            "revenue_month", "unit_cost", "defect_share_pct", "returned_units", "downtime_h_month", "logistics_km_month",
            "fabric_waste_m2", "lost_sales", "inventory_value", "breakdowns", "maintenances", "capitalization",
        };

        public static List<ScenarioRun> Run(DataCatalog catalog, IReadOnlyList<ScenarioDef> scenarios, int seeds, int months,
            bool parallel = true, ulong firstSeed = 1)
        {
            var jobs = scenarios.SelectMany(s => Enumerable.Range(0, seeds).Select(i => (s, seed: firstSeed + (ulong)i))).ToList();
            var results = new ScenarioRun[jobs.Count];
            if (parallel) Parallel.For(0, jobs.Count, i => results[i] = RunOne(catalog, jobs[i].s, jobs[i].seed, months));
            else for (int i = 0; i < jobs.Count; i++) results[i] = RunOne(catalog, jobs[i].s, jobs[i].seed, months);
            return results.ToList();
        }

        public static ScenarioRun RunOne(DataCatalog catalog, ScenarioDef scenario, ulong seed, int months)
        {
            var world = ScenarioRunner.CreateScenarioWorld(catalog, seed, scenario);
            world.Simulation.RunMonths(months);
            var player = world.Companies[0];
            var reports = player.Reports;
            int n = reports.Count;
            long produced = reports.Sum(r => r.UnitsProduced + r.UnitsDefective);
            long shipped = reports.Sum(r => r.UnitsSold + r.ContractUnits);
            return new ScenarioRun
            {
                Scenario = scenario.Id,
                Seed = seed,
                Months = n,
                PurchaseMonth = player.Digital.Modules.Count > 0 ? player.Digital.Modules.Min(m => m.PurchaseMonth) : n,
                NetIncome = reports.Select(r => (double)r.NetIncome).ToArray(),
                DigitalDepreciation = reports.Select(r => (double)r.DigitalDepreciation).ToArray(),
                KeyRate = Enumerable.Range(0, n).Select(m => m < world.Economy.History.Count ? world.Economy.History[m].KeyRate : world.Economy.KeyRate).ToArray(),
                DigitalCapex = reports.Sum(r => r.DigitalCapex),
                DigitalLicense = reports.Sum(r => r.DigitalLicense),
                DigitalSubsidy = reports.Sum(r => r.DigitalSubsidy),
                Revenue = reports.Average(r => (double)r.Revenue),
                UnitCost = shipped > 0 ? reports.Sum(r => (double)r.CostOfGoodsSold) / shipped : 0,
                DefectShare = produced > 0 ? 100.0 * reports.Sum(r => r.UnitsDefective) / produced : 0,
                ReturnedUnits = reports.Sum(r => r.ReturnedUnits),
                DowntimeHours = reports.Average(r => r.DowntimeNoOperatorHours + r.DowntimeNoMaterialsHours + r.DowntimeBrokenHours + r.DowntimeMaintenanceHours),
                LogisticsKm = reports.Average(r => r.LogisticsKm),
                FabricWaste = reports.Sum(r => r.FabricWaste),
                LostSales = reports.Sum(r => r.LostSales),
                InventoryValue = reports.Average(r => (double)r.InventoryValue),
                Breakdowns = reports.Sum(r => r.Breakdowns),
                Maintenances = reports.Sum(r => r.Maintenances),
                Capitalization = reports[n - 1].Capitalization,
            };
        }

        // Эффекты сценариев против их баз (ScenarioDef.Baseline, по умолчанию — первый сценарий), по общим seed.
        public static List<ScenarioSummary> Summarize(IReadOnlyList<ScenarioRun> runs, IReadOnlyList<ScenarioDef> scenarios)
        {
            var result = new List<ScenarioSummary>();
            foreach (var scenario in scenarios)
            {
                string baselineId = string.IsNullOrEmpty(scenario.Baseline) ? scenarios[0].Id : scenario.Baseline;
                var baseline = runs.Where(r => r.Scenario == baselineId).ToDictionary(r => r.Seed);
                var pairs = runs.Where(r => r.Scenario == scenario.Id && baseline.ContainsKey(r.Seed)).Select(r => (s: r, b: baseline[r.Seed])).ToList();
                if (pairs.Count == 0) continue;
                var summary = new ScenarioSummary
                {
                    Scenario = scenario.Id,
                    Modules = string.Join("+", scenario.Modules) + (scenario.Subsidy ? " (субсидия)" : "") + (scenario.NaiveLayout ? " (наивная планировка)" : ""),
                    Baseline = baselineId,
                    Seeds = pairs.Count,
                };
                var monthly = new List<double>();
                var roi = new List<double>();
                var npv = new List<double>();
                foreach (var (s, b) in pairs)
                {
                    int months = Math.Min(s.Months, b.Months);
                    double sum = 0, discounted = 0;
                    for (int t = 0; t < months; t++)
                    {
                        double delta = s.NetIncome[t] + s.DigitalDepreciation[t] - b.NetIncome[t] - b.DigitalDepreciation[t];
                        sum += delta;
                        discounted += delta / Math.Pow(1 + s.KeyRate[t] / 12, t + 1);
                    }
                    monthly.Add(sum / Math.Max(1, months - Math.Min(s.PurchaseMonth, months)));
                    roi.Add(s.DigitalCapex > 0 ? (sum - s.DigitalCapex) / s.DigitalCapex : 0);
                    npv.Add(discounted - s.DigitalCapex);
                }
                summary.Capex = pairs.Average(p => p.s.DigitalCapex);
                summary.DeltaPerMonth = monthly.Average();
                summary.DeltaSd = StdDev(monthly);
                summary.Ci95 = 1.96 * summary.DeltaSd / Math.Sqrt(monthly.Count);
                summary.Roi = roi.Average();
                summary.Npv = npv.Average();
                summary.PaybackMonths = summary.Capex <= 0 ? 0 : summary.DeltaPerMonth > 0 ? summary.Capex / summary.DeltaPerMonth : double.PositiveInfinity;
                double Avg(Func<ScenarioRun, double> kpi) => pairs.Average(p => kpi(p.s) - kpi(p.b));
                summary.KpiDelta["revenue_month"] = Avg(r => r.Revenue);
                summary.KpiDelta["unit_cost"] = Avg(r => r.UnitCost);
                summary.KpiDelta["defect_share_pct"] = Avg(r => r.DefectShare);
                summary.KpiDelta["returned_units"] = Avg(r => r.ReturnedUnits);
                summary.KpiDelta["downtime_h_month"] = Avg(r => r.DowntimeHours);
                summary.KpiDelta["logistics_km_month"] = Avg(r => r.LogisticsKm);
                summary.KpiDelta["fabric_waste_m2"] = Avg(r => r.FabricWaste);
                summary.KpiDelta["lost_sales"] = Avg(r => r.LostSales);
                summary.KpiDelta["inventory_value"] = Avg(r => r.InventoryValue);
                summary.KpiDelta["breakdowns"] = Avg(r => r.Breakdowns);
                summary.KpiDelta["maintenances"] = Avg(r => r.Maintenances);
                summary.KpiDelta["capitalization"] = Avg(r => r.Capitalization);
                result.Add(summary);
            }
            return result;
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        }

        // ---------- CSV для Excel (разделитель «;», десятичная запятая) ----------

        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        private static string Num(double value) =>
            double.IsInfinity(value) ? "∞" : Math.Round(value, 4).ToString("0.####", Russian);

        public static string RunsCsv(IReadOnlyList<ScenarioRun> runs)
        {
            var csv = new StringBuilder("scenario;seed;months;purchase_month;net_income_total;digital_capex;digital_license;digital_subsidy;" +
                                        "revenue_month;unit_cost;defect_share_pct;returned_units;downtime_h_month;logistics_km_month;" +
                                        "fabric_waste_m2;lost_sales;inventory_value;breakdowns;maintenances;capitalization\n");
            foreach (var r in runs)
                csv.AppendLine(string.Join(";", r.Scenario, r.Seed.ToString(CultureInfo.InvariantCulture), r.Months.ToString(CultureInfo.InvariantCulture),
                    r.PurchaseMonth.ToString(CultureInfo.InvariantCulture), Num(r.NetIncome.Sum()), Num(r.DigitalCapex), Num(r.DigitalLicense), Num(r.DigitalSubsidy), Num(r.Revenue), Num(r.UnitCost),
                    Num(r.DefectShare), Num(r.ReturnedUnits), Num(r.DowntimeHours), Num(r.LogisticsKm), Num(r.FabricWaste), Num(r.LostSales),
                    Num(r.InventoryValue), Num(r.Breakdowns), Num(r.Maintenances), Num(r.Capitalization)));
            return csv.ToString();
        }

        public static string SummaryCsv(IReadOnlyList<ScenarioSummary> summaries)
        {
            var csv = new StringBuilder("scenario;modules;baseline;seeds;capex;delta_ni_month;delta_sd;ci95;roi_pct;payback_months;npv;" +
                                        string.Join(";", KpiNames.Select(k => "d_" + k)) + "\n");
            foreach (var s in summaries)
                csv.AppendLine(string.Join(";", s.Scenario, s.Modules, s.Baseline, s.Seeds.ToString(CultureInfo.InvariantCulture), Num(s.Capex), Num(s.DeltaPerMonth),
                    Num(s.DeltaSd), Num(s.Ci95), Num(s.Roi * 100), Num(s.PaybackMonths), Num(s.Npv)) + ";" +
                    string.Join(";", KpiNames.Select(k => Num(s.KpiDelta[k]))));
            return csv.ToString();
        }
    }
}
