using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using Geminium.Core.Lab;

namespace Geminium.ML
{
    // Площадка обучения: мир, в котором живут агенты (суррогатный или полный).
    public interface ITrainingArena
    {
        BotSituation Situation(int index);
        void OnActions(int index, int[] actions);
    }

    // Агент ML-Agents (GDD 8.1): один на компанию. Наблюдения, маска и действия — из ядра (BotObservation, BotActions),
    // поэтому обученная политика без изменений работает и в суррогате, и в полном цехе.
    // Решения запрашивает площадка — раз в игровой месяц.
    public sealed class GeminiumBotAgent : Agent
    {
        public const string BehaviorName = "GeminiumBot";

        private ITrainingArena arena;
        private int index;

        public int[] LastActions { get; private set; }

        // Агента собираем выключенным: параметры поведения должны быть заданы до его инициализации.
        public static GeminiumBotAgent Create(Transform parent, ITrainingArena arena, int index)
        {
            var go = new GameObject("Agent " + index);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var parameters = go.AddComponent<BehaviorParameters>();
            parameters.BehaviorName = BehaviorName;
            parameters.BrainParameters.VectorObservationSize = BotObservation.Size;
            parameters.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(BotActions.BranchSizes);
            parameters.BehaviorType = BehaviorType.Default;
            var agent = go.AddComponent<GeminiumBotAgent>();
            agent.arena = arena;
            agent.index = index;
            agent.MaxStep = 0;
            go.SetActive(true);
            return agent;
        }

        public override void CollectObservations(VectorSensor sensor) =>
            sensor.AddObservation(BotObservation.Encode(arena.Situation(index)));

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            var mask = BotActions.Mask(arena.Situation(index));
            int offset = 0;
            for (int branch = 0; branch < BotActions.BranchSizes.Length; branch++)
            {
                for (int a = 0; a < BotActions.BranchSizes[branch]; a++)
                    if (!mask[offset + a]) actionMask.SetActionEnabled(branch, a, false);
                offset += BotActions.BranchSizes[branch];
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var discrete = actions.DiscreteActions;
            var result = new int[discrete.Length];
            for (int i = 0; i < result.Length; i++) result[i] = discrete[i];
            LastActions = result;
            arena.OnActions(index, result);
        }

        // Без нейросети (Behavior Type = Heuristic Only) — правила HeuristicPolicy.
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var situation = arena.Situation(index);
            var actions = new HeuristicPolicy().Decide(BotObservation.Encode(situation), BotActions.Mask(situation));
            var discrete = actionsOut.DiscreteActions;
            for (int i = 0; i < actions.Length; i++) discrete[i] = actions[i];
        }
    }
}
