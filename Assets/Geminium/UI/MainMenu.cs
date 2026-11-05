using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Geminium.Core.Modes;
using Geminium.Core.Save;
using Geminium.Game;

namespace Geminium.UI
{
    // Главное меню (GDD 11.2): кампания, свободный режим, Лаборатория, сохранения, язык.
    public static class MainMenu
    {
        public static void Build(GameApp app, VisualElement layer)
        {
            var screen = new VisualElement().With("menu-screen").AddTo(layer);
            var box = new VisualElement().With("menu").AddTo(screen);
            ShowMain(app, box);
        }

        private static void Header(VisualElement box, string subtitleKey = null)
        {
            box.Clear();
            box.Add(Ui.Text("Geminium", "logo"));
            box.Add(Ui.Text(subtitleKey == null ? Loc.Get("menu.tagline") : Loc.Get(subtitleKey), "menu-hint"));
        }

        private static void ShowMain(GameApp app, VisualElement box)
        {
            Header(box);
            var autosave = TryRead(app, SaveStorage.AutosaveSlot);
            if (autosave != null)
                Ui.Button(Loc.Get("menu.continue", Loc.Get("hud.month_short", autosave.MonthIndex + 1)), () => app.LoadGame(autosave), "menu-button", "primary").AddTo(box);
            Ui.Button(Loc.Get("menu.campaign"), () => ShowCampaign(app, box), "menu-button").AddTo(box);
            var free = Ui.Button(Loc.Get("menu.free"), () => ShowFree(app, box), "menu-button").AddTo(box);
            if (!app.Profile.IsFreeModeOpen(app.Catalog))
            {
                free.SetEnabled(false);
                free.tooltip = Loc.Get("menu.free_locked");
            }
            Ui.Button(Loc.Get("menu.load"), () => ShowLoad(app, box), "menu-button").AddTo(box);
            Ui.Button(Loc.Get("menu.lab"), () => LabScreen.Show(app, box, () => ShowMain(app, box)), "menu-button").AddTo(box);
            var languages = Ui.Row(Ui.Text(Loc.Get("menu.language"), "pair-label")).With("menu-row").AddTo(box);
            foreach (string code in Loc.Languages.Where(Loc.HasLanguage))
            {
                string c = code;
                Ui.Button(Loc.Get("language." + c), () => app.SetLanguage(c), "small", Loc.Language == c ? "primary" : "plain").AddTo(languages);
            }
            Ui.Button(Loc.Get("menu.quit"), Application.Quit, "menu-button", "plain").AddTo(box);
        }

        // Кампания: 6 заданий по порядку; следующее открывается после прохождения предыдущего.
        private static void ShowCampaign(GameApp app, VisualElement box)
        {
            Header(box, "menu.campaign_hint");
            int n = 1;
            foreach (var mission in app.Catalog.Missions)
            {
                var m = mission;
                bool open = app.Profile.IsMissionOpen(app.Catalog, m.Id);
                var card = new VisualElement().With("mission-card").AddTo(box);
                card.Add(Ui.Text($"{n++}. {Loc.Get("mission." + m.Id + ".title")}", "mission-title"));
                card.Add(Ui.Text(Loc.Get("mission." + m.Id + ".short"), "wrap", "hint"));
                string status = app.Profile.Passed.Contains(m.Id) ? Loc.Get("menu.mission_passed", Fmt.Number(app.Profile.BestScores[m.Id]))
                    : app.Profile.BestScores.TryGetValue(m.Id, out double best) ? Loc.Get("menu.mission_best", Fmt.Number(best))
                    : open ? Loc.Get("menu.mission_new") : Loc.Get("menu.mission_locked");
                var row = Ui.Row(Ui.Text(status, "hint")).AddTo(card);
                new VisualElement().With("spacer").AddTo(row);
                var play = Ui.Button(Loc.Get("menu.play"), () => app.StartGame(GameSetup.Campaign(m.Id, NewSeed())), "small", "primary").AddTo(row);
                play.SetEnabled(open);
            }
            Ui.Button(Loc.Get("ui.back"), () => ShowMain(app, box), "menu-button", "plain").AddTo(box);
        }

        // Свободный режим: seed (для повторяемости) и кто управляет ботами.
        private static void ShowFree(GameApp app, VisualElement box)
        {
            Header(box, "menu.free_hint");
            var seed = Ui.Field(NewSeed().ToString(), 160);
            Ui.Row(Ui.Text(Loc.Get("menu.seed"), "pair-label"), seed).With("menu-row").AddTo(box);
            var brains = new[] { Loc.Get("menu.rivals_scripted"), Loc.Get("menu.rivals_neural") };
            int brain = 0;
            var dropdown = Ui.Dropdown(brains, 0, i => brain = i, 240);
            Ui.Row(Ui.Text(Loc.Get("menu.rivals"), "pair-label"), dropdown).With("menu-row").AddTo(box);
            if (app.BotModel == null)
            {
                dropdown.SetEnabled(false);
                box.Add(Ui.Hint(Loc.Get("menu.no_model")));
            }
            Ui.Button(Loc.Get("menu.start"), () =>
            {
                ulong value = ulong.TryParse(seed.value, out ulong s) ? s : NewSeed();
                app.StartGame(GameSetup.Free(value, brain == 1 ? RivalBrain.Neural : RivalBrain.Scripted));
            }, "menu-button", "primary").AddTo(box);
            Ui.Button(Loc.Get("ui.back"), () => ShowMain(app, box), "menu-button", "plain").AddTo(box);
        }

        private static void ShowLoad(GameApp app, VisualElement box)
        {
            Header(box, "menu.load_hint");
            foreach (string slot in SaveStorage.Slots())
            {
                string s = slot;
                SaveGame save = null;
                string problem = null;
                try { save = app.Saves.Read(s); }
                catch (SaveException e) { problem = e.Message; }
                var card = new VisualElement().With("mission-card").AddTo(box);
                card.Add(Ui.Text(SlotName(s), "mission-title"));
                card.Add(Ui.Text(problem ?? (save == null ? Loc.Get("save.empty") : Describe(app, save)), "hint", "wrap"));
                if (save == null) continue;
                var row = Ui.Row().AddTo(card);
                new VisualElement().With("spacer").AddTo(row);
                Ui.Button(Loc.Get("save.delete"), () => { app.Saves.Delete(s); ShowLoad(app, box); }, "small", "plain").AddTo(row);
                Ui.Button(Loc.Get("save.load"), () => Load(app, save), "small", "primary").AddTo(row);
            }
            Ui.Button(Loc.Get("ui.back"), () => ShowMain(app, box), "menu-button", "plain").AddTo(box);
        }

        public static void Load(GameApp app, SaveGame save)
        {
            if (save.DataMatches(app.Catalog))
            {
                app.LoadGame(save);
                return;
            }
            app.ShowDialog(d =>
            {
                d.Add(Ui.Title(Loc.Get("load.data_changed_title")));
                d.Add(Ui.Text(Loc.Get("load.data_changed"), "wrap"));
                Ui.Row(Ui.Button(Loc.Get("ui.cancel"), app.CloseDialog, "plain"), Ui.Button(Loc.Get("save.load"), () => { app.CloseDialog(); app.LoadGame(save); }, "primary")).AddTo(d);
            });
        }

        public static string SlotName(string slot) =>
            slot == SaveStorage.AutosaveSlot ? Loc.Get("save.autosave") : Loc.Get("save.slot", slot.Substring(4));

        public static string Describe(GameApp app, SaveGame save)
        {
            string mode = save.Setup.Mode == GameMode.Campaign ? Loc.Get("mission." + save.Setup.MissionId + ".title") : Loc.Get("menu.free");
            return Loc.Get("save.describe", mode, save.MonthIndex + 1, Fmt.Money(save.PlayerCapitalization), save.SavedAt);
        }

        private static SaveGame TryRead(GameApp app, string slot)
        {
            try { return app.Saves.Read(slot); }
            catch (SaveException) { return null; }
        }

        private static ulong NewSeed() => (ulong)(DateTime.Now.Ticks % 1_000_000);
    }
}
