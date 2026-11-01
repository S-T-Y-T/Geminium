using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Geminium.Core.Lab;
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
