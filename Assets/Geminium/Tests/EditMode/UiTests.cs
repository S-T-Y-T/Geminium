using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Geminium.Core.Data;
using Geminium.Core.Modes;
using Geminium.Core.News;
using Geminium.Core.Save;
using Geminium.EditorTools;
using Geminium.Game;
using Geminium.UI;

namespace Geminium.Tests.EditMode
{
    // Экраны (GDD 11), «голос» ИИ-ядра (GDD 9) и профиль кампании — этап 4.
    public class UiTests
    {
        private GameObject host;

        private static DataCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();

        [SetUp]
        public void SetUp()
        {
            Loc.Register(LocalizationTools.Tables());
            Loc.SetLanguage("ru");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                host.GetComponent<GameApp>().EndSession();
                UnityEngine.Object.DestroyImmediate(host);
            }
            Loc.Reset();
        }

        private GameApp App(GameSession session)
        {
            host = new GameObject("GameAppTest");
            var app = host.AddComponent<GameApp>();
            app.Attach(session);
            return app;
        }

        private static Panel[] Panels(GameApp app) => new Panel[]
        {
            new MissionPanel(app), new FloorPanel(app), new StaffPanel(app), new SupplyPanel(app), new WarehousePanel(app), new SalesPanel(app),
            new MarketingPanel(app), new FinancePanel(app), new DigitalPanel(app), new AnalyticsPanel(app),
        };

        private static string[] Texts(VisualElement root) =>
            root.Query<TextElement>().ToList().Select(t => t.text).Where(t => !string.IsNullOrEmpty(t)).ToArray();

        // Каждый экран строится без ошибок и без непереведённых ключей — в свободном режиме и в задании.
        [Test]
        public void Panels_BuildInFreeModeAndMission([Values("ru", "uz", "en")] string language)
        {
            Loc.SetLanguage(language);
            var free = GameSession.Start(Catalog(), GameSetup.Free(2));
            free.World.Simulation.RunMonths(1);
            var app = App(free);
            foreach (var panel in Panels(app).Where(p => p.Available))
            {
                panel.Refresh(true);
                var texts = Texts(panel.Root);
                Assert.Greater(texts.Length, 3, panel.GetType().Name);
                var raw = texts.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[a-z_]+\.[a-z_.A-Z]+")).ToList();
                CollectionAssert.IsEmpty(raw, $"{panel.GetType().Name}: непереведённые ключи");
            }
            Assert.IsFalse(new MissionPanel(app).Available, "в свободном режиме экрана задания нет");
            Assert.IsTrue(new MarketingPanel(app).Available);
        }

        [Test]
        public void Panels_HideLockedScreensInMission()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Campaign("m1_first_shop", 3));
            var app = App(session);
            Assert.IsTrue(new MissionPanel(app).Available);
            Assert.IsFalse(new MarketingPanel(app).Available);
            Assert.IsFalse(new DigitalPanel(app).Available);
            var mission = new MissionPanel(app);
            mission.Refresh(true);
            CollectionAssert.Contains(Texts(mission.Root), "Первый цех");
            var staff = new StaffPanel(app);
            staff.Refresh(true);
            Assert.IsTrue(staff.Root.Query<Button>().ToList().Any(b => b.text == "Нанять"), "на рынке труда есть кандидаты");
        }

        // Команда из экрана идёт тем же путём, что у ботов, и попадает в журнал сохранения.
        [Test]
        public void App_SubmitLogsPlayerCommandAndShowsErrors()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Free(5));
            var app = App(session);
            int version = app.CommandsVersion;
            Assert.IsTrue(app.Submit(new Geminium.Core.Companies.SetMonthlyPlanCommand("dobok", 40)));
            Assert.AreEqual(version + 1, app.CommandsVersion);
            Assert.IsFalse(app.Submit(new Geminium.Core.Companies.SetMonthlyPlanCommand("dobok", -1)));
            Assert.AreEqual(1, session.World.CommandLog.Count);
        }

        // Новости и сводка месяца складываются в текст на всех языках, без ключей и пустых подстановок.
        [Test]
        public void Voice_NewsAndSummaryInEveryLanguage()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Free(3));
            session.World.Simulation.RunMonths(6);
            var bot = session.World.Companies.First(c => c.Id == "daebo");
            Assert.Greater(session.News.Items.Count, 10);
            foreach (string language in Loc.Languages)
            {
                Loc.SetLanguage(language);
                foreach (var item in session.News.Items)
                {
                    string text = Voice.News(item);
                    StringAssert.DoesNotStartWith("news.", text);
                    StringAssert.DoesNotContain("{", text);
                    StringAssert.DoesNotContain("name.", text);
                }
                string summary = Voice.MonthSummary(bot, 4);
                StringAssert.Contains(Fmt.Money(bot.Reports[4].Revenue), summary);
                StringAssert.DoesNotContain("summary.", summary);
                Assert.IsTrue(NumberGuard.Check(summary, Voice.SummaryNumbers(bot, 4)), $"{language}: числа сводки — из отчёта: {summary}");
            }
        }

        // Проверка чисел для текста языковой модели (GDD 9).
        [Test]
        public void NumberGuard_AcceptsOnlySimulationNumbers()
        {
            CollectionAssert.AreEqual(new[] { 1234567, 12.5, -8 }, NumberGuard.Extract("Выручка 1 234 567 сум, брак 12,5%, цена −8%"));
            CollectionAssert.AreEqual(new[] { 1234567.0 }, NumberGuard.Extract("Revenue UZS 1,234,567"));
            Assert.IsTrue(NumberGuard.Check("Daebo снизил цену на 8%, выручка 1 234 567 сум.", new double[] { 8, 1234567 }));
            Assert.IsFalse(NumberGuard.Check("Daebo снизил цену на 9%.", new double[] { 8 }), "выдуманное число");
        }

        // Лента новостей: одинаковый seed — одинаковые новости; загрузка сохранения повторяет ленту.
        [Test]
        public void News_AreDeterministicAndSurviveLoad()
        {
            var catalog = Catalog();
            var a = GameSession.Start(catalog, GameSetup.Free(9));
            var b = GameSession.Start(catalog, GameSetup.Free(9));
            a.World.Simulation.RunMonths(3);
            b.World.Simulation.RunMonths(3);
            CollectionAssert.AreEqual(a.News.Items.Select(n => n.Key + string.Join(",", n.Args)), b.News.Items.Select(n => n.Key + string.Join(",", n.Args)));
            Assert.IsTrue(a.News.Items.Any(n => n.Key == "news.tender"));
            var restored = SaveGame.FromJson(SaveGame.Capture(a).ToJson()).Restore(catalog);
            Assert.AreEqual(a.News.Items.Count, restored.News.Items.Count);
        }

        // Профиль: задания открываются по порядку, свободный режим — после кампании; файл переживает перезапуск.
        [Test]
        public void Profile_UnlocksMissionsInOrder()
        {
            var catalog = Catalog();
            string path = Path.Combine(Path.GetTempPath(), "geminium_profile_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var profile = Profile.Load(path);
                Assert.IsTrue(profile.IsMissionOpen(catalog, "m1_first_shop"));
                Assert.IsFalse(profile.IsMissionOpen(catalog, "m2_logistics"));
                profile.RecordMission("m1_first_shop", 45, false);
                Assert.IsFalse(profile.IsMissionOpen(catalog, "m2_logistics"));
                profile.RecordMission("m1_first_shop", 80, true);
                profile.RecordMission("m1_first_shop", 70, true);
                Assert.IsTrue(profile.IsMissionOpen(catalog, "m2_logistics"));
                Assert.IsFalse(profile.IsFreeModeOpen(catalog));
                profile.Language = "uz";
                profile.Save();

                var again = Profile.Load(path);
                Assert.AreEqual("uz", again.Language);
                Assert.AreEqual(80, again.BestScores["m1_first_shop"], 1e-9);
                Assert.IsTrue(again.Passed.Contains("m1_first_shop"));
                foreach (var m in catalog.Missions) again.Passed.Add(m.Id);
                Assert.IsTrue(again.IsFreeModeOpen(catalog));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
