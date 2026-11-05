using System.Linq;
using UnityEngine.UIElements;
using Geminium.Core.Data;
using Geminium.Core.Modes;
using Geminium.Core.Save;
using Geminium.Game;

namespace Geminium.UI
{
    // Модальные окна партии.
    public static class Dialogs
    {
        // Перед заданием: что нужно сделать и по чему оценят.
        public static void MissionBriefing(GameApp app, VisualElement d)
        {
            var mission = app.Session.Mission;
            d.Add(Ui.Title(Loc.Get("mission." + mission.Id + ".title")));
            d.Add(Ui.Text(Loc.Get("mission." + mission.Id + ".brief"), "wrap"));
            d.Add(Ui.Text(Loc.Get("mission.goals", mission.Months, Fmt.Number(mission.PassScore)), "section-title"));
            foreach (var goal in mission.Goals) d.Add(Ui.Text("• " + GoalText(goal), "wrap"));
            d.Add(Ui.Button(Loc.Get("mission.start"), app.CloseDialog, "primary"));
        }

        public static string GoalText(GoalDef goal)
        {
            string product = string.IsNullOrEmpty(goal.ProductId) ? Loc.Get("mission.all_products") : Loc.Name(goal.ProductId);
            return Loc.Get("goal." + goal.Metric, Fmt.Number(goal.Target, goal.Target % 1 == 0 ? 0 : 1), product);
        }

        // Конец месяца: сводка «голоса» и главные числа отчёта.
        public static void MonthReport(GameApp app, VisualElement d, int month)
        {
            var player = app.Session.Player;
            var r = player.Reports.FirstOrDefault(x => x.Month == month);
            var date = app.Session.World.Simulation.Calendar.MonthStart(month);
            d.Add(Ui.Title(Loc.Get("report.title", Fmt.Month(app.Session.World.Simulation.Calendar.ToDateTime(date)))));
            d.Add(Ui.Text(Voice.MonthSummary(player, month), "wrap", "summary"));
            if (r != null)
            {
                var box = Ui.Section(d, Loc.Get("report.numbers"));
                Ui.Pair(box, Loc.Get("finance.revenue"), Fmt.Money(r.Revenue));
                Ui.Pair(box, Loc.Get("finance.net_income"), Fmt.Money(r.NetIncome));
                Ui.Pair(box, Loc.Get("finance.cash"), Fmt.Money(r.Cash));
                Ui.Pair(box, Loc.Get("finance.capitalization"), Fmt.Money(r.Capitalization));
                Ui.Pair(box, Loc.Get("kpi.produced"), Fmt.Number(r.UnitsProduced));
                Ui.Pair(box, Loc.Get("kpi.sold"), Fmt.Number(r.UnitsSold));
            }
            if (app.Session.MissionTracker != null && app.Session.MissionTracker.Result == null)
            {
                var progress = app.Session.MissionTracker.Progress();
                d.Add(Ui.Text(Loc.Get("mission.progress", Fmt.Number(progress.Score)), "hint"));
            }
            d.Add(Ui.Button(Loc.Get("report.continue"), app.CloseDialog, "primary"));
        }

        public static void MissionResult(GameApp app, VisualElement d, MissionResult result)
        {
            d.Add(Ui.Title(Loc.Get(result.Passed ? "mission.passed" : "mission.failed")));
            d.Add(Ui.Text(Loc.Get("mission.score", Fmt.Number(result.Score), Fmt.Number(app.Session.Mission.PassScore)), "big"));
            foreach (var g in result.Goals)
            {
                var row = Ui.Row(Ui.Text(GoalText(g.Goal), "goal-text"), Ui.Text(Fmt.Number(g.Score), "goal-score")).AddTo(d);
                row.Add(Ui.Bar(g.Score / 100));
            }
            var buttons = Ui.Row().AddTo(d);
            Ui.Button(Loc.Get("mission.continue_play"), app.CloseDialog, "plain").AddTo(buttons);
            Ui.Button(Loc.Get("menu.to_menu"), () => { app.CloseDialog(); app.ExitToMenu(); }, "primary").AddTo(buttons);
        }

        // Итог свободного режима или банкротство: рейтинг по капитализации.
        public static void FinalRanking(GameApp app, VisualElement d)
        {
            bool bankrupt = app.Session.Player.IsBankrupt;
            d.Add(Ui.Title(Loc.Get(bankrupt ? "final.bankrupt" : "final.title")));
            if (!bankrupt) d.Add(Ui.Text(Loc.Get("final.hint"), "wrap"));
            foreach (var entry in app.Session.Ranking())
            {
                string name = Loc.Name(entry.CompanyId) + (entry.Bankrupt ? " — " + Loc.Get("final.bankrupt_short") : "");
                Ui.Row(Ui.Text(entry.Place + ".", "rank"), Ui.Text(name, "goal-text"), Ui.Text(Fmt.Money(entry.Capitalization), "pair-value"))
                    .With(entry.CompanyId == app.Session.Player.Id ? "rank-player" : "rank-row").AddTo(d);
            }
            var buttons = Ui.Row().AddTo(d);
            if (!bankrupt) Ui.Button(Loc.Get("final.continue"), app.CloseDialog, "plain").AddTo(buttons);
            Ui.Button(Loc.Get("menu.to_menu"), () => { app.CloseDialog(); app.ExitToMenu(); }, "primary").AddTo(buttons);
        }

        // Меню партии: сохранить в слот, загрузить, выйти.
        public static void GameMenu(GameApp app, VisualElement d)
        {
            app.Clock.Pause();
            d.Add(Ui.Title(Loc.Get("hud.menu")));
            var saves = Ui.Section(d, Loc.Get("save.title"));
            for (int i = 1; i <= SaveStorage.ManualSlots; i++)
            {
                string slot = SaveStorage.ManualSlot(i);
                SaveGame existing = null;
                try { existing = app.Saves.Read(slot); }
                catch (SaveException) { }
                var row = Ui.Row(Ui.Text(MainMenu.SlotName(slot), "pair-label"),
                    Ui.Text(existing == null ? Loc.Get("save.empty") : MainMenu.Describe(app, existing), "hint", "slot-info")).AddTo(saves);
                Ui.Button(Loc.Get("save.save"), () => { app.SaveTo(slot); app.CloseDialog(); }, "small", "primary").AddTo(row);
                if (existing != null) Ui.Button(Loc.Get("save.load"), () => { app.CloseDialog(); MainMenu.Load(app, existing); }, "small", "plain").AddTo(row);
            }
            d.Add(Ui.Hint(Loc.Get("save.autosave_hint")));
            var buttons = Ui.Row().AddTo(d);
            Ui.Button(Loc.Get("ui.close"), app.CloseDialog, "plain").AddTo(buttons);
            Ui.Button(Loc.Get("menu.to_menu"), () => { app.CloseDialog(); app.ExitToMenu(); }, "primary").AddTo(buttons);
        }
    }
}
