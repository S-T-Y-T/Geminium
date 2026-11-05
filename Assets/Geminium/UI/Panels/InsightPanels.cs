using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Lab;

namespace Geminium.UI
{
    // Цифровизация (GDD 7.9): модули, покупка, лицензии, подсказки двойника.
    public sealed class DigitalPanel : Panel
    {
        public DigitalPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.digital";
        public override string Icon => "◆";
        public override Feature? RequiredFeature => Feature.Digital;

        protected override void Build(VisualElement root)
        {
            double subsidy = World.Digital.SubsidyShare(Player);
            if (subsidy > 0) root.Add(Ui.Text(Loc.Get("digital.subsidy", Fmt.Percent(subsidy)), "positive", "wrap"));
            foreach (var def in Catalog.DigitalModules)
            {
                var module = def.Module;
                if (!World.Rules.IsModuleOpen(module)) continue;
                var owned = Player.Digital.Find(module);
                var card = new VisualElement().With("card").AddTo(root);
                card.Add(Ui.Text(Loc.Enum(module), "strong"));
                card.Add(Ui.Text(Loc.Get("digital." + module + ".effect"), "hint", "wrap"));
                card.Add(Ui.Text(Loc.Get("digital.costs", Fmt.Money(World.Digital.NetCapex(Player, module)), Fmt.Money(def.MonthlyLicense * World.Economy.PriceLevel)), "hint"));
                if (def.Requires != DigitalModule.None) card.Add(Ui.Text(Loc.Get("digital.requires", Loc.Enum(def.Requires)), "hint"));
                var row = Ui.Row().AddTo(card);
                if (owned == null || !owned.Active)
                    Ui.Button(Loc.Get(owned == null ? "digital.buy" : "digital.enable"), () => Submit(new BuyDigitalModuleCommand(module)), "small", "primary").AddTo(row);
                else
                {
                    row.Add(Ui.Text(Loc.Get("digital.active"), "positive"));
                    Ui.Button(Loc.Get("digital.disable"), () => Submit(new DisableDigitalModuleCommand(module)), "small", "plain").AddTo(row);
                }
                if (module == DigitalModule.AiPricing && owned != null && owned.Active)
                    card.Add(Ui.Toggle(Loc.Get("digital.auto_pricing"), Player.Digital.AutoPricing, v => Submit(new SetAutoPricingCommand(v))));
                if (module == DigitalModule.DigitalTwin && Player.Digital.Has(DigitalModule.DigitalTwin)) TwinSuggestions(card);
            }
        }

        private void TwinSuggestions(VisualElement card)
        {
            var moves = DigitalTwin.Suggest(World, Player, 3);
            if (moves.Count == 0)
            {
                card.Add(Ui.Hint(Loc.Get("digital.twin_none")));
                return;
            }
            foreach (var move in moves)
            {
                var o = Player.Floor.FindObject(move.ObjectId);
                Ui.Row(Ui.Text(Loc.Get("digital.twin_move", Loc.Name(o.DefId), o.Id, Fmt.Number(move.SavingCellsPerUnit, 1), Fmt.Money(move.Cost)), "hint", "grow"),
                    Ui.Button(Loc.Get("ui.apply"), () => Submit(new MoveObjectCommand(move.ObjectId, move.X, move.Y, move.Facing)), "tiny", "primary")).AddTo(card);
            }
        }
    }

    // Аналитика (GDD 11.2): графики месяцев, таблица KPI, выгрузка CSV для Excel.
    public sealed class AnalyticsPanel : Panel
    {
        public AnalyticsPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.analytics";
        public override string Icon => "▲";

        protected override void Build(VisualElement root)
        {
            var reports = Player.Reports.Skip(Math.Max(0, Player.Reports.Count - 12)).ToList();
            if (reports.Count == 0)
            {
                root.Add(Ui.Hint(Loc.Get("analytics.empty")));
                return;
            }
            var labels = reports.Select(r => (r.Month + 1).ToString()).ToList();
            root.Add(Ui.Chart(Loc.Get("finance.revenue"), reports.Select(r => (double)r.Revenue).ToList(), labels, v => Fmt.Money(v)));
            root.Add(Ui.Chart(Loc.Get("finance.net_income"), reports.Select(r => (double)r.NetIncome).ToList(), labels, v => Fmt.Money(v)));
            root.Add(Ui.Chart(Loc.Get("kpi.produced"), reports.Select(r => (double)r.UnitsProduced).ToList(), labels, v => Fmt.Number(v)));

            var table = Ui.Section(root, Loc.Get("analytics.kpi"));
            Ui.Table(table, new float[] { 40, 70, 60, 60, 60, 70, 70, 90 },
                new[] { Loc.Get("analytics.month"), Loc.Get("kpi.produced"), Loc.Get("kpi.defect_pct"), Loc.Get("kpi.sold"), Loc.Get("kpi.lost"),
                    Loc.Get("kpi.logistics_km"), Loc.Get("kpi.downtime_h"), Loc.Get("kpi.share_pct") },
                reports.AsEnumerable().Reverse().Select(r =>
                {
                    long made = r.UnitsProduced + r.UnitsDefective;
                    double downtime = r.DowntimeBrokenHours + r.DowntimeMaintenanceHours + r.DowntimeNoMaterialsHours + r.DowntimeNoOperatorHours;
                    double share = r.MarketShareByProduct.Count > 0 ? r.MarketShareByProduct.Values.Where(v => v > 0).DefaultIfEmpty(0).Average() : 0;
                    return new VisualElement[]
                    {
                        Ui.Text((r.Month + 1).ToString()), Ui.Text(Fmt.Number(r.UnitsProduced)), Ui.Text(made > 0 ? Fmt.Percent(r.UnitsDefective / (double)made, 1) : "—"),
                        Ui.Text(Fmt.Number(r.UnitsSold)), Ui.Text(Fmt.Number(r.LostSales)), Ui.Text(Fmt.Number(r.LogisticsKm, 1)), Ui.Text(Fmt.Number(downtime)),
                        Ui.Text(Fmt.Percent(share)),
                    };
                }));
            Ui.Button(Loc.Get("analytics.export"), Export, "small", "primary").AddTo(root);
        }

        // Все показатели всех компаний по месяцам — тот же CSV, что у Лаборатории (90 колонок).
        private void Export()
        {
            string folder = Path.Combine(Application.persistentDataPath, "reports");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"geminium_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, KpiExport.ToCsv(World), new UTF8Encoding(true));
            App.Toast(Loc.Get("analytics.exported", path));
        }
    }

    // Задание кампании: цели и текущая оценка.
    public sealed class MissionPanel : Panel
    {
        public MissionPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.mission";
        public override string Icon => "⚑";
        public override bool Available => Session.Mission != null;

        protected override void Build(VisualElement root)
        {
            var mission = Session.Mission;
            root.Add(Ui.Text(Loc.Get("mission." + mission.Id + ".title"), "strong"));
            root.Add(Ui.Text(Loc.Get("mission." + mission.Id + ".brief"), "wrap"));
            int left = Math.Max(0, mission.Months - World.Simulation.MonthIndex);
            root.Add(Ui.Text(Loc.Get("mission.months_left", left), "hint"));
            var progress = Session.MissionTracker.Progress();
            var goals = Ui.Section(root, Loc.Get("mission.progress", Fmt.Number(progress.Score)));
            foreach (var g in progress.Goals)
            {
                goals.Add(Ui.Text(Dialogs.GoalText(g.Goal), "wrap"));
                goals.Add(Ui.Row(Ui.Bar(g.Score / 100), Ui.Text(Loc.Get("mission.goal_now", Fmt.Number(g.Actual, 1), Fmt.Number(g.Score)), "hint")));
            }
            goals.Add(Ui.Hint(Loc.Get("mission.pass_hint", Fmt.Number(mission.PassScore))));
            root.Add(Ui.Text(Loc.Get("mission." + mission.Id + ".tip"), "wrap", "tip"));
        }
    }
}
