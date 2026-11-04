using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Geminium.Core.Lab;
using Geminium.Core.Modes;
using Geminium.Game;

namespace Geminium.EditorTools
{
    // Пункты меню «Geminium» в редакторе и точки входа для запуска без окна (-executeMethod).
    public static class GeminiumTools
    {
        public const string DatabasePath = "Assets/Geminium/Data/GameDatabase.asset";
        public const string DebugScenePath = "Assets/Geminium/Scenes/Debug.unity";

        // Прогон Лаборатории без графики: 36 месяцев, seed 1, CSV в папку Reports рядом с проектом.
        [MenuItem("Geminium/Прогон без графики (36 месяцев)")]
        public static void RunLabFromMenu()
        {
            string path = RunLab(1, 36, null);
            EditorUtility.RevealInFinder(path);
        }

        // Запуск из командной строки:
        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.GeminiumTools.RunLabFromCommandLine
        //           [-geminiumSeed 1] [-geminiumMonths 36] [-geminiumOut путь.csv] -quit
        public static void RunLabFromCommandLine()
        {
            ulong seed = ulong.Parse(Argument("-geminiumSeed") ?? "1");
            int months = int.Parse(Argument("-geminiumMonths") ?? "36");
            RunLab(seed, months, Argument("-geminiumOut"));
        }

        public static string RunLab(ulong seed, int months, string outputPath)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            var world = ScenarioRunner.Run(database.CreateCatalog(), seed, months);
            outputPath ??= Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports", $"lab_seed{seed}_{months}m.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, KpiExport.ToCsv(world), new UTF8Encoding(true));
            Debug.Log($"Geminium: прогон {months} мес. (seed {seed}) сохранён в {outputPath}");
            return outputPath;
        }

        // Снимок цеха в PNG для документов (TODO 2.8): стандартная партия, N месяцев, цех компании №company.
        // Unity.exe -batchmode -projectPath <проект> -executeMethod Geminium.EditorTools.GeminiumTools.CaptureFloorFromCommandLine
        //           [-geminiumMonths 3] [-geminiumCompany 0] [-geminiumSpaghetti] [-geminiumOut путь.png] -quit
        // Без -nographics: снимку нужна видеокарта.
        public static void CaptureFloorFromCommandLine()
        {
            int months = int.Parse(Argument("-geminiumMonths") ?? "3");
            int company = int.Parse(Argument("-geminiumCompany") ?? "0");
            bool spaghetti = Array.IndexOf(Environment.GetCommandLineArgs(), "-geminiumSpaghetti") >= 0;
            CaptureFloor(1, months, company, spaghetti, Argument("-geminiumOut"));
        }

        public static string CaptureFloor(ulong seed, int months, int companyIndex, bool spaghetti, string outputPath)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            var world = ScenarioRunner.CreateStandardWorld(database.CreateCatalog(), seed);
            world.Simulation.RunMonths(months);
            world.Simulation.RunMinutes(10 * 60); // 10:00 — середина первой смены, люди на местах

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = world.Companies[companyIndex].Floor;
            const int cellPixels = 40;
            int width = (floor.Width + 2) * cellPixels, height = (floor.Height + 2) * cellPixels;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = (floor.Height + 2) / 2f;
            camera.transform.position = new Vector3(floor.Width / 2f, floor.Height / 2f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.36f, 0.38f, 0.42f);
            var view = new GameObject("Floor").AddComponent<FloorView>();
            view.World = world;
            view.CompanyIndex = companyIndex;
            view.ShowSpaghetti = spaghetti;
            view.Render();

            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            // Первый кадр — пробный: в нём только что созданные спрайты рисуются с чужой текстурой.
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            string company = world.Companies[companyIndex].Id;
            outputPath ??= Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports",
                $"floor_{company}_{months}m{(spaghetti ? "_spaghetti" : "")}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log($"Geminium: снимок цеха {company} сохранён в {outputPath}");
            return outputPath;
        }

        // Лаборатория сценариев (GDD 10, TODO 4.6): все сценарии из данных × N seed без графики, параллельно.
        // Два CSV в Reports: прогоны (scenarios_runs.csv) и эффекты против баз с ROI, сроком окупаемости и NPV
        // (scenarios_summary.csv).
        [MenuItem("Geminium/Лаборатория сценариев (5 seed × 36 месяцев)")]
        public static void RunScenariosFromMenu() => EditorUtility.RevealInFinder(RunScenarios(5, 36, null));

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.GeminiumTools.RunScenariosFromCommandLine
        //           [-geminiumSeeds 5] [-geminiumMonths 36] [-geminiumOut папка] -quit
        public static void RunScenariosFromCommandLine() =>
            RunScenarios(int.Parse(Argument("-geminiumSeeds") ?? "5"), int.Parse(Argument("-geminiumMonths") ?? "36"), Argument("-geminiumOut"));

        public static string RunScenarios(int seeds, int months, string folder)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath).CreateCatalog();
            if (catalog.Scenarios.Count == 0) throw new InvalidOperationException("В GameDatabase нет сценариев (Data → Scenarios).");
            var runs = ScenarioLab.Run(catalog, catalog.Scenarios, seeds, months);
            var summary = ScenarioLab.Summarize(runs, catalog.Scenarios);
            folder ??= Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "scenarios_runs.csv"), ScenarioLab.RunsCsv(runs), new UTF8Encoding(true));
            string summaryPath = Path.Combine(folder, "scenarios_summary.csv");
            File.WriteAllText(summaryPath, ScenarioLab.SummaryCsv(summary), new UTF8Encoding(true));
            foreach (var s in summary)
                Debug.Log($"Geminium: {s.Scenario}: ΔNI {s.DeltaPerMonth / 1e6:0.0} ± {s.Ci95 / 1e6:0.0} млн/мес, ROI {s.Roi:P0}, NPV {s.Npv / 1e6:0} млн");
            Debug.Log("Geminium: Лаборатория сценариев сохранена в " + folder);
            return summaryPath;
        }

        // Проверка заданий кампании (GDD 4.1): каждое задание проходят бездействующий игрок и бот «сбалансированный»
        // за игрока, по seed 1–3. Бездействие должно проваливаться. Бот не делает того, чему учат задания 2, 4 и 6
        // (перестановка станков, наладчик и ТО, заявка на тендер в первый месяц), поэтому там он тоже не проходит.
        // Итог — в Console и в Reports/missions.csv.
        [MenuItem("Geminium/Кампания: проверить задания")]
        public static void RunMissionsFromMenu() => EditorUtility.RevealInFinder(RunMissions(3, null));

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.GeminiumTools.RunMissionsFromCommandLine
        //           [-geminiumSeeds 3] [-geminiumOut папка] -quit
        public static void RunMissionsFromCommandLine() => RunMissions(int.Parse(Argument("-geminiumSeeds") ?? "3"), Argument("-geminiumOut"));

        public static string RunMissions(int seeds, string folder)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath).CreateCatalog();
            var csv = new StringBuilder("mission;player;seed;score;passed;goals\n");
            foreach (var mission in catalog.Missions)
            foreach (string player in new[] { "idle", "bot" })
            {
                double total = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var controller = player == "bot" ? ScenarioRunner.CreateBot(catalog, "balanced") : null;
                    var session = GameSession.Start(catalog, GameSetup.Campaign(mission.Id, (ulong)seed), null, controller);
                    session.World.Simulation.RunMonths(mission.Months);
                    var result = session.MissionTracker.Result;
                    total += result.Score;
                    csv.Append($"{mission.Id};{player};{seed};{result.Score:0.0};{result.Passed};")
                       .Append(string.Join(" ", result.Goals.Select(g => $"{g.Goal.Metric}={g.Actual:0.##}"))).Append('\n');
                }
                Debug.Log($"Geminium: {mission.Id}, {(player == "bot" ? "бот" : "без действий")}: средний балл {total / seeds:0.0} (проходной {mission.PassScore:0})");
            }
            folder ??= Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "missions.csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            Debug.Log("Geminium: проверка заданий сохранена в " + path);
            return path;
        }

        // Отладочная сцена: камера + объект Simulation с DebugSimulationRunner и ссылкой на GameDatabase.
        [MenuItem("Geminium/Создать отладочную сцену")]
        public static void BuildDebugScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var runner = new GameObject("Simulation").AddComponent<DebugSimulationRunner>();
            runner.Database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(DebugScenePath));
            EditorSceneManager.SaveScene(scene, DebugScenePath);
            Debug.Log("Geminium: отладочная сцена сохранена в " + DebugScenePath);
        }

        // Записать в файл данных все поля, включая новые со значениями по умолчанию.
        [MenuItem("Geminium/Пересохранить GameDatabase")]
        public static void ReserializeDatabase()
        {
            AssetDatabase.ForceReserializeAssets(new[] { DatabasePath });
            Debug.Log("Geminium: GameDatabase пересохранён.");
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
