using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Geminium.Core.Modes;
using Geminium.Core.Time;
using Geminium.Game;
using Geminium.UI;

namespace Geminium.Tests.PlayMode
{
    // Сквозная проверка игровой сцены (TODO 4.1): меню → задание → все экраны → три языка → конец месяца с отчётом →
    // сохранение и загрузка → свободный режим → меню. Любая ошибка или исключение в логе роняет тест.
    public class GameSmokeTests
    {
        private const string ScenePath = "Assets/Geminium/Scenes/Game.unity";
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "geminium_smoke_" + Guid.NewGuid().ToString("N"));
            GameApp.DataFolderOverride = folder;
        }

        [TearDown]
        public void TearDown()
        {
            GameApp.DataFolderOverride = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        [UnityTest]
        public IEnumerator Game_PlaysThroughMenusScreensSavesAndLanguages()
        {
            SceneManager.LoadScene(ScenePath);
            yield return null;
            yield return null;
            var app = UnityEngine.Object.FindFirstObjectByType<GameApp>();
            Assert.IsNotNull(app, "в сцене нет GameApp");
            Assert.IsNull(app.DataError);
            var root = app.GetComponent<UIDocument>().rootVisualElement;
            Assert.Greater(root.Query<Button>().ToList().Count, 4, "главное меню");

            // Задание 1: брифинг, все экраны, смена языка.
            app.StartGame(GameSetup.Campaign("m1_first_shop", 7));
            yield return null;
            Assert.IsNotNull(app.Session);
            Assert.IsTrue(app.DialogOpen, "брифинг задания");
            app.CloseDialog();
            foreach (string language in new[] { "uz", "en", "ru" })
            {
                app.SetLanguage(language);
                yield return null;
                foreach (var button in root.Query<Button>(className: "nav-button").ToList())
                {
                    app.Show((Panel)button.userData);
                    yield return null;
                }
            }

            // Конец месяца: автопауза и отчёт со сводкой.
            var sim = app.Session.World.Simulation;
            sim.RunUntil(new SimTime(sim.Calendar.MonthStart(1).Minute - 30));
            app.Clock.SetSpeed(8);
            app.Clock.Resume();
            float timeout = Time.realtimeSinceStartup + 30;
            while (!app.DialogOpen && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsTrue(app.DialogOpen, "отчёт месяца");
            Assert.AreEqual(1, sim.MonthIndex);
            app.CloseDialog();

            // Сохранение в слот и загрузка в фоне.
            app.SaveTo("slot1");
            var save = app.Saves.Read("slot1");
            Assert.IsNotNull(save);
            app.LoadGame(save);
            timeout = Time.realtimeSinceStartup + 60;
            while (app.Session == null && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsNotNull(app.Session, "загрузка");
            Assert.AreEqual(save.Minute, app.Session.World.Simulation.Now.Minute);
            if (app.DialogOpen) app.CloseDialog();

            // Свободный режим и выход в меню.
            app.StartGame(GameSetup.Free(3));
            yield return null;
            Assert.AreEqual(36, app.Session.Months);
            Assert.IsTrue(root.Query<Button>(className: "nav-button").ToList().Count >= 9);
            app.Clock.SetSpeed(8);
            app.Clock.Resume();
            for (int i = 0; i < 20; i++) yield return null;
            app.ExitToMenu();
            yield return null;
            Assert.IsNull(app.Session);
        }
    }
}
