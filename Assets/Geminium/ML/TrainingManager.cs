using System;
using UnityEngine;
using Geminium.Core.Data;
using Geminium.Game;

namespace Geminium.ML
{
    // Сцена обучения (Training.unity, сборка GeminiumTraining.exe): создаёт площадки при запуске.
    // Аргументы командной строки (mlagents-learn передаёт их через --env-args):
    //   -geminiumMode surrogate|full   — суррогат (обучение) или полный цех (дообучение)
    //   -geminiumArenas N              — сколько площадок (миров) работает одновременно
    //   -geminiumSeed S
    public sealed class TrainingManager : MonoBehaviour
    {
        public GameDatabase Database;
        public bool FullSimulation;
        public int Arenas = 32;
        public int Seed = 1;

        private void Start()
        {
            ReadArguments();
            Application.targetFrameRate = -1;
            UnityEngine.QualitySettings.vSyncCount = 0;
            DataCatalog catalog = Database.CreateCatalog();
            for (int k = 0; k < Arenas; k++)
            {
                var go = new GameObject((FullSimulation ? "Full arena " : "Surrogate arena ") + k);
                go.transform.SetParent(transform, false);
                ulong seed = (ulong)(Seed + k * 7919);
                if (FullSimulation) go.AddComponent<FullSimArena>().Init(catalog, seed);
                else go.AddComponent<SurrogateArena>().Init(catalog, seed);
            }
            Debug.Log($"Geminium: обучение, {(FullSimulation ? "полный цех" : "суррогат")}, площадок {Arenas}");
        }

        private void ReadArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-geminiumMode") FullSimulation = args[i + 1] == "full";
                if (args[i] == "-geminiumArenas" && int.TryParse(args[i + 1], out int arenas)) Arenas = Math.Max(1, arenas);
                if (args[i] == "-geminiumSeed" && int.TryParse(args[i + 1], out int seed)) Seed = seed;
            }
        }
    }
}
