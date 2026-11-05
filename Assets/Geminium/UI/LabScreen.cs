using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Geminium.Core.Lab;

namespace Geminium.UI
{
    // Лаборатория сценариев (GDD 10) из главного меню: выбрать сценарии, число seed и месяцев, прогнать без графики
    // в фоновом потоке и получить эффекты, ROI, срок окупаемости и NPV; CSV для Excel — в папку reports.
    public static class LabScreen
    {
        public static void Show(GameApp app, VisualElement box, Action back)
        {
            box.Clear();
            box.Add(Ui.Text(Loc.Get("menu.lab"), "logo-small"));
            box.Add(Ui.Text(Loc.Get("lab.hint"), "menu-hint", "wrap"));
            var catalog = app.Catalog;
            var chosen = new HashSet<string>(catalog.Scenarios.Select(s => s.Id));
            var list = new VisualElement().With("lab-list").AddTo(box);
            foreach (var scenario in catalog.Scenarios)
            {
                string id = scenario.Id;
                list.Add(Ui.Toggle(Loc.Get("scenario." + id), true, v => { if (v) chosen.Add(id); else chosen.Remove(id); }));
            }
            var seeds = Ui.Field("5", 60);
            var months = Ui.Field("36", 60);
            Ui.Row(Ui.Text(Loc.Get("lab.seeds"), "pair-label-short"), seeds, Ui.Text(Loc.Get("lab.months"), "pair-label-short"), months).With("menu-row").AddTo(box);
            var status = Ui.Text("", "menu-hint", "wrap").AddTo(box);
            var results = new VisualElement().AddTo(box);
            Button run = null;
            run = Ui.Button(Loc.Get("lab.run"), () =>
            {
                // Базовые сценарии нужны для сравнения, даже если не отмечены.
                var selected = catalog.Scenarios.Where(s => chosen.Contains(s.Id)).ToList();
                foreach (var s in selected.ToList())
                {
                    string baseId = string.IsNullOrEmpty(s.Baseline) ? catalog.Scenarios.FirstOrDefault(x => x.Modules.Count == 0 && !x.NaiveLayout)?.Id : s.Baseline;
                    var b = catalog.Scenarios.FirstOrDefault(x => x.Id == baseId);
                    if (b != null && !selected.Contains(b)) selected.Insert(0, b);
                }
                int n = (int)Math.Max(1, Math.Min(30, Ui.ParseLong(seeds.value) ?? 5));
                int m = (int)Math.Max(3, Math.Min(120, Ui.ParseLong(months.value) ?? 36));
                run.SetEnabled(false);
                status.text = Loc.Get("lab.running", selected.Count * n);
                var task = Task.Run(() =>
                {
                    var runs = ScenarioLab.Run(catalog, selected, n, m);
                    return (runs, summary: ScenarioLab.Summarize(runs, selected));
                });
                box.schedule.Execute(() => { }).Until(() =>
                {
                    if (!task.IsCompleted) return false;
                    run.SetEnabled(true);
                    if (task.IsFaulted)
                    {
                        status.text = task.Exception?.InnerException?.Message ?? task.Exception?.Message;
                        return true;
                    }
                    string folder = Path.Combine(Application.persistentDataPath, "reports");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "scenarios_runs.csv"), ScenarioLab.RunsCsv(task.Result.runs), new UTF8Encoding(true));
                    string summaryPath = Path.Combine(folder, "scenarios_summary.csv");
                    File.WriteAllText(summaryPath, ScenarioLab.SummaryCsv(task.Result.summary), new UTF8Encoding(true));
                    status.text = Loc.Get("lab.done", summaryPath);
                    ShowResults(results, task.Result.summary);
                    return true;
                }).Every(250);
            }, "menu-button", "primary").AddTo(box);
            Ui.Button(Loc.Get("ui.back"), back, "menu-button", "plain").AddTo(box);
        }

        private static void ShowResults(VisualElement host, List<ScenarioSummary> summary)
        {
            host.Clear();
            Ui.Table(host, new float[] { 190, 140, 80, 80, 100 },
                new[] { Loc.Get("lab.scenario"), Loc.Get("lab.delta"), Loc.Get("lab.roi"), Loc.Get("lab.payback"), Loc.Get("lab.npv") },
                summary.Select(s => new VisualElement[]
                {
                    Ui.Text(Loc.Get("scenario." + s.Scenario)),
                    Ui.Text(s.Scenario == s.Baseline || string.IsNullOrEmpty(s.Baseline) ? "—" : Loc.Get("lab.delta_value", Fmt.Number(s.DeltaPerMonth / 1e6, 1), Fmt.Number(s.Ci95 / 1e6, 1))),
                    Ui.Text(double.IsNaN(s.Roi) || s.Capex <= 0 ? "—" : Fmt.Percent(s.Roi)),
                    Ui.Text(double.IsNaN(s.PaybackMonths) || double.IsInfinity(s.PaybackMonths) || s.PaybackMonths <= 0 ? "—" : Fmt.Number(s.PaybackMonths, 1)),
                    Ui.Text(s.Capex <= 0 ? "—" : Fmt.Millions(s.Npv)),
                }));
        }
    }
}
