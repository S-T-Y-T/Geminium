using System.IO;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Geminium.Game;
using Geminium.UI;

namespace Geminium.EditorTools
{
    // Игровая сцена (TODO 4.1): камера + объект Game с UIDocument и GameApp. Все ссылки (данные, модель ботов,
    // таблицы строк, стили, PanelSettings) проставляются здесь, руками в инспекторе ничего делать не нужно.
    // Сцена становится первой в списке сборки (File → Build Profiles).
    public static class GameSceneTools
    {
        public const string ScenePath = "Assets/Geminium/Scenes/Game.unity";
        public const string PanelSettingsPath = "Assets/Geminium/UI/GeminiumPanelSettings.asset";
        public const string ThemePath = "Assets/Geminium/UI/Styles/GeminiumTheme.tss";
        public const string StylePath = "Assets/Geminium/UI/Styles/Geminium.uss";
        public const string ModelPath = "Assets/Geminium/ML/Models/GeminiumBot.onnx";

        [MenuItem("Geminium/Создать игровую сцену")]
        public static void BuildFromMenu()
        {
            BuildGameScene();
            Debug.Log("Geminium: игровая сцена сохранена в " + ScenePath + " и стоит первой в списке сборки.");
        }

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.GameSceneTools.BuildFromCommandLine -quit
        public static void BuildFromCommandLine()
        {
            LocalizationTools.Import();
            BuildFromMenu();
        }

        public static void BuildGameScene()
        {
            var tables = LocalizationTools.Tables();
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }
            panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1;
            EditorUtility.SetDirty(panelSettings);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.933f, 0.945f, 0.961f);

            var game = new GameObject("Game");
            var document = game.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var app = game.AddComponent<GameApp>();
            app.Database = AssetDatabase.LoadAssetAtPath<GameDatabase>(GeminiumTools.DatabasePath);
            app.BotModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            app.Tables = tables;
            app.Style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
