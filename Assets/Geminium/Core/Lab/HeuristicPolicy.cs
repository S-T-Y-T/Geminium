namespace Geminium.Core.Lab
{
    // Политика-правила в том же пространстве действий, что и нейросеть: точка сравнения для обучения
    // и «эмулятор игрока» на суррогате. Много упущенных продаж — цена +5% и план ×1,2, залежался товар —
    // цена −5%; сырьё — по архетипу; реклама 2% выручки; тендеры с наценкой 0,9; станок — при больших потерях.
    public sealed class HeuristicPolicy : IBotPolicy
    {
        public int[] Decide(float[] observation, bool[] mask)
        {
            var actions = new int[BotActions.BranchSizes.Length];
            double lostTotal = 0;
            for (int p = 0; p < BotSituation.MaxProducts; p++)
            {
                int o = p * BotObservation.PerProduct;
                double stock = observation[o + 3] * 2, lost = observation[o + 4];
                lostTotal += lost;
                actions[p] = lost > 0.2 ? 3 : stock > 1 ? 1 : 2;
            }
            bool premium = observation[BotObservation.Size - 2] > 0.5;
            actions[BotActions.PlanBranch] = lostTotal > 0.2 ? 2 : 1;
            actions[BotActions.MaterialsBranch] = premium ? 1 : 0;
            actions[BotActions.AdsBranch] = 1;
            actions[BotActions.TenderBranch] = 2;
            actions[BotActions.ExpandBranch] = lostTotal > 0.6 ? 1 : 0;
            return BotActions.Sanitize(actions, mask);
        }
    }
}
