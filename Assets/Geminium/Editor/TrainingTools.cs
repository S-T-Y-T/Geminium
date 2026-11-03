using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Geminium.Core;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Lab.Surrogate;
using Geminium.Game;
using Geminium.ML;

namespace Geminium.EditorTools
{
    // Обучение ботов (TODO 3.4–3.5): калибровка суррогата, сборка среды для mlagents-learn,
    // сравнение скриптовых ботов и нейросети. Команды есть и в меню, и для запуска без окна.
    public static class TrainingTools
    {
        public const string TrainingScenePath = "Assets/Geminium/Scenes/Training.unity";
        public const string ModelPath = "Assets/Geminium/ML/Models/GeminiumBot.onnx";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        // ---------- Калибровка суррогата ----------

        [MenuItem("Geminium/Обучение/Калибровать суррогат")]
        public static void CalibrateFromMenu() => Calibrate(8, 24);

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.TrainingTools.CalibrateFromCommandLine
        //           [-geminiumRuns 8] [-geminiumMonths 24] -quit
        public static void CalibrateFromCommandLine() =>
            Calibrate(int.Parse(Argument("-geminiumRuns") ?? "8"), int.Parse(Argument("-geminiumMonths") ?? "24"));

        public static SurrogateSettings Calibrate(int runs, int months)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(GeminiumTools.DatabasePath);
            var catalog = database.CreateCatalog();
            var samples = SurrogateCalibration.Collect(catalog, runs, months);
            var fit = SurrogateCalibration.Fit(samples, catalog);
            database.Data.Surrogate = fit;
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"Geminium: суррогат откалиброван по {fit.CalibrationSamples} компаниям-месяцам: U = {fit.Utilization:0.00}, F = {fit.PlanFulfilment:0.00}, " +
                      $"R² выпуска {fit.OutputR2:0.000}, множитель брака {fit.DefectMultiplier:0.000}, R² брака {fit.DefectR2:0.000}");
            return fit;
        }

        // ---------- Среда обучения ----------

        [MenuItem("Geminium/Обучение/Собрать среду обучения")]
        public static void BuildFromMenu() => EditorUtility.RevealInFinder(BuildTrainingPlayer(null));

        // Unity.exe -batchmode -projectPath <проект> -executeMethod Geminium.EditorTools.TrainingTools.BuildFromCommandLine
        //           [-geminiumOut путь\GeminiumTraining.exe] -quit   (по умолчанию %USERPROFILE%\.geminium\env)
        public static void BuildFromCommandLine() => BuildTrainingPlayer(Argument("-geminiumOut"));

        // Сцена Training.unity с TrainingManager и сборка Windows-приложения для mlagents-learn.
        public static string BuildTrainingPlayer(string output)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var manager = new GameObject("Training").AddComponent<TrainingManager>();
            manager.Database = AssetDatabase.LoadAssetAtPath<GameDatabase>(GeminiumTools.DatabasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(TrainingScenePath));
            EditorSceneManager.SaveScene(scene, TrainingScenePath);

            // По умолчанию — в папку пользователя: сетевая библиотека ML-Agents (gRPC) не загружается из пути
            // с кириллицей, а проект лежит в «БМИ\симулятор».
            output ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".geminium", "env", "GeminiumTraining.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Окно, а не весь экран, и работа в фоне — на время сборки, настройки проекта не меняются.
            bool runInBackground = PlayerSettings.runInBackground;
            var fullScreen = PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.runInBackground = true;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { TrainingScenePath }, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Сборка среды обучения не удалась: " + report.summary.result);
            }
            finally
            {
                PlayerSettings.runInBackground = runInBackground;
                PlayerSettings.fullScreenMode = fullScreen;
            }
            Debug.Log("Geminium: среда обучения собрана: " + output);
            return output;
        }

        // ---------- Сравнение ботов ----------

        [MenuItem("Geminium/Обучение/Сравнить ботов (скрипт и нейросеть)")]
        public static void CompareFromMenu() => EditorUtility.RevealInFinder(CompareBots(5, 36, null));

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.TrainingTools.CompareFromCommandLine
        //           [-geminiumSeeds 5] [-geminiumMonths 36] [-geminiumOut Reports/bots_compare.csv] -quit
        public static void CompareFromCommandLine() =>
            CompareBots(int.Parse(Argument("-geminiumSeeds") ?? "5"), int.Parse(Argument("-geminiumMonths") ?? "36"), Argument("-geminiumOut"));

        // Три режима на одних и тех же seed: все боты скриптовые; все под нейросетью; нейросеть только у игрока.
        public static string CompareBots(int seeds, int months, string output)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameDatabase>(GeminiumTools.DatabasePath).CreateCatalog();
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            if (model == null) throw new FileNotFoundException("Нет обученной модели: " + ModelPath);
            using var policy = new OnnxBotPolicy(model);
            var modes = new (string name, IBotPolicy[] policies)[]
            {
                ("scripted", new IBotPolicy[] { null, null, null }),
                ("neural", new IBotPolicy[] { policy, policy, policy }),
                ("neural_player", new IBotPolicy[] { policy, null, null }),
            };
            var csv = new StringBuilder("mode;seed;company;archetype;capitalization;net_income_total;revenue_total;tenders_won;reputation;bankrupt\n");
            var summary = new Dictionary<string, List<double>>();
            foreach (var (name, policies) in modes)
            for (int s = 1; s <= seeds; s++)
            {
                var world = ScenarioRunner.CreateWorld(catalog, (ulong)s, policies);
                world.Simulation.RunMonths(months);
                for (int i = 0; i < world.Companies.Count; i++)
                {
                    var c = world.Companies[i];
                    var last = c.LastReport;
                    csv.AppendLine(string.Join(";", name, s, c.Id, ScenarioRunner.ArchetypeIds[i], last.Capitalization, c.Reports.Sum(r => r.NetIncome),
                        c.Reports.Sum(r => r.Revenue), c.Reports.Sum(r => r.TendersWon), Math.Round(c.Reputation, 1), c.IsBankrupt ? 1 : 0));
                    string key = name + "|" + c.Id;
                    if (!summary.ContainsKey(key)) summary[key] = new List<double>();
                    summary[key].Add(last.Capitalization);
                }
            }
            output ??= Path.Combine(ProjectRoot, "Reports", "bots_compare.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, csv.ToString(), new UTF8Encoding(true));
            foreach (var pair in summary)
                Debug.Log($"Geminium: {pair.Key}: капитализация в среднем {pair.Value.Average() / 1e6:N0} млн сум (seed: {pair.Value.Count})");
            Debug.Log("Geminium: сравнение ботов сохранено в " + output);
            return output;
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
