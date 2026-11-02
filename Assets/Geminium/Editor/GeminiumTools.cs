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
