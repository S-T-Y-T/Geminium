using System;
using System.Linq;
using Unity.InferenceEngine;
using Geminium.Core.Lab;

namespace Geminium.ML
{
    // Обученная политика ботов из ONNX (экспорт ML-Agents) через Inference Engine — на процессоре, синхронно,
    // поэтому работает и в игре, и в прогоне Лаборатории без режима игры.
    // Входы модели: obs_0 (наблюдения) и action_masks (1 — действие разрешено); выход — детерминированные
    // дискретные действия (по одному на ветку).
    public sealed class OnnxBotPolicy : IBotPolicy, IDisposable
    {
        private readonly Worker worker;
        private readonly string observationInput;
        private readonly string maskInput;
        private readonly string actionOutput;

        public OnnxBotPolicy(ModelAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            var model = ModelLoader.Load(asset);
            observationInput = model.inputs.Select(i => i.name).FirstOrDefault(n => n.StartsWith("obs")) ?? model.inputs[0].name;
            maskInput = model.inputs.Select(i => i.name).FirstOrDefault(n => n == "action_masks");
            var outputs = model.outputs.Select(o => o.name).ToList();
            actionOutput = outputs.Contains("deterministic_discrete_actions") ? "deterministic_discrete_actions" : "discrete_actions";
            if (!outputs.Contains(actionOutput)) throw new ArgumentException("В модели нет выхода с дискретными действиями.", nameof(asset));
            worker = new Worker(model, BackendType.CPU);
        }

        public int[] Decide(float[] observation, bool[] mask)
        {
            using var obs = new Tensor<float>(new TensorShape(1, observation.Length), observation);
            worker.SetInput(observationInput, obs);
            Tensor<float> masks = null;
            if (maskInput != null)
            {
                masks = new Tensor<float>(new TensorShape(1, mask.Length), mask.Select(m => m ? 1f : 0f).ToArray());
                worker.SetInput(maskInput, masks);
            }
            worker.Schedule();
            var output = worker.PeekOutput(actionOutput);
            int branches = BotActions.BranchSizes.Length;
            var actions = new int[branches];
            if (output is Tensor<int> ints)
            {
                using var result = ints.ReadbackAndClone();
                for (int i = 0; i < branches; i++) actions[i] = result[0, i];
            }
            else if (output is Tensor<float> floats)
            {
                using var result = floats.ReadbackAndClone();
                for (int i = 0; i < branches; i++) actions[i] = (int)Math.Round(result[0, i]);
            }
            masks?.Dispose();
            return actions;
        }

        public void Dispose() => worker.Dispose();
    }
}
