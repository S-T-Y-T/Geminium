using System;

namespace Geminium.Core.Lab
{
    // Наблюдения и действия бота (GDD 8.1) — одинаковые в полном цехе и в суррогатной модели.
    // На этом совпадении держится перенос обученной политики с суррогата на детальный двойник (GDD 8.2).

    // Что бот знает о продукте.
    public sealed class ProductSituation
    {
        public bool Active;              // продукт открыт и выпускается
        public double PriceFactor;       // своя цена / (эталонная × уровень цен)
        public double MarketPriceFactor; // средняя цена рынка / (эталонная × уровень цен)
        public double Share;             // доля рынка за прошлый месяц, 0–1
        public double StockRatio;        // запас / месячный спрос на одну компанию
        public double LostRatio;         // упущенные продажи / месячный спрос на одну компанию
        public double CommittedRatio;    // недопоставлено по контрактам / месячный спрос на одну компанию
    }

    // Что бот знает о себе, рынке и конкурентах на начало месяца. Конкуренты — только публичные данные.
    public sealed class BotSituation
    {
        public const int MaxProducts = 5;

        public int Archetype;            // 0 — сбалансированный, 1 — «Премиум», 2 — «Демпер»
        public double CashRatio;         // деньги / стартовый капитал
        public double DebtRatio;
        public double Reputation;        // 0–100
        public double NetIncomeRatio;    // чистая прибыль прошлого месяца / стартовый капитал
        public double Fulfilment;        // выпуск / план прошлого месяца
        public double DefectRate;        // брак / выпуск
        public double Machines;          // штук
        public double EconomyIndex;
        public double NextSeasonality;
        public double KeyRate;           // годовая, доля
        public double Inflation;
        public int CalendarMonth;        // 1–12
        public double CompetitorReputation;
        public bool TenderAvailable;     // есть открытый тендер, в котором можно участвовать
        public double TenderUnitsRatio;  // партия крупнейшего такого тендера / месячный спрос на компанию
        public readonly ProductSituation[] Products = new ProductSituation[MaxProducts];

        public BotSituation()
        {
            for (int i = 0; i < MaxProducts; i++) Products[i] = new ProductSituation();
        }
    }

    // Вектор наблюдений для нейросети: значения приведены примерно к −1…+1.
    public static class BotObservation
    {
        public const int PerProduct = 6;
        public const int Size = BotSituation.MaxProducts * PerProduct + 18;

        public static float[] Encode(BotSituation s)
        {
            var v = new float[Size];
            int i = 0;
            foreach (var p in s.Products)
            {
                v[i++] = p.Active ? (float)Clip(p.PriceFactor - 1, 1) : 0;
                v[i++] = p.Active ? (float)Clip(p.MarketPriceFactor - 1, 1) : 0;
                v[i++] = p.Active ? (float)Clip(p.Share, 1) : 0;
                v[i++] = p.Active ? (float)Clip(p.StockRatio / 2, 1) : 0;
                v[i++] = p.Active ? (float)Clip(p.LostRatio, 1) : 0;
                v[i++] = p.Active ? (float)Clip(p.CommittedRatio / 2, 1) : 0;
            }
            v[i++] = (float)Clip(s.CashRatio / 5, 1);
            v[i++] = (float)Clip(s.DebtRatio / 3, 1);
            v[i++] = (float)(s.Reputation / 100);
            v[i++] = (float)Clip(s.NetIncomeRatio * 5, 1);
            v[i++] = (float)Clip(s.Fulfilment, 1);
            v[i++] = (float)Clip(s.DefectRate * 5, 1);
            v[i++] = (float)Clip(s.Machines / 20, 1);
            v[i++] = (float)Clip(s.EconomyIndex - 1, 1);
            v[i++] = (float)Clip(s.NextSeasonality - 1, 1);
            v[i++] = (float)Clip(s.KeyRate * 5, 1);
            v[i++] = (float)Clip(s.Inflation * 5, 1);
            v[i++] = (float)Math.Sin(2 * Math.PI * s.CalendarMonth / 12);
            v[i++] = (float)Math.Cos(2 * Math.PI * s.CalendarMonth / 12);
            v[i++] = (float)(s.CompetitorReputation / 100);
            v[i++] = s.TenderAvailable ? 1 : 0;
            v[i++] = (float)Clip(s.TenderUnitsRatio / 3, 1);
            v[i++] = s.Archetype == 1 ? 1 : 0;
            v[i++] = s.Archetype == 2 ? 1 : 0;
            return v;
        }

        private static double Clip(double value, double limit) => value < -limit ? -limit : value > limit ? limit : value;
    }

    // Действия бота раз в месяц: дискретные ветки (как в ML-Agents).
    public static class BotActions
    {
        // 5 веток цены (по продукту), план, класс сырья, реклама, тендеры, расширение.
        public static readonly int[] BranchSizes = { 5, 5, 5, 5, 5, 4, 2, 4, 4, 2 };
        public const int PlanBranch = 5, MaterialsBranch = 6, AdsBranch = 7, TenderBranch = 8, ExpandBranch = 9;

        private static readonly double[] PriceSteps = { -0.10, -0.05, 0, 0.05, 0.10 };
        private static readonly double[] PlanFactors = { 0.8, 1.0, 1.2, 1.4 };
        private static readonly double[] AdShares = { 0, 0.02, 0.05, 0.10 };
        private static readonly double[] TenderMarkups = { 0, 0.8, 0.9, 1.0 };

        public static int MaskSize
        {
            get
            {
                int n = 0;
                foreach (int size in BranchSizes) n += size;
                return n;
            }
        }

        public static double PriceStep(int[] actions, int product) => PriceSteps[actions[product]];
        public static double PlanFactor(int[] actions) => PlanFactors[actions[PlanBranch]];
        public static bool QualityMaterials(int[] actions) => actions[MaterialsBranch] == 1;
        public static double AdShare(int[] actions) => AdShares[actions[AdsBranch]];
        public static double TenderMarkup(int[] actions) => TenderMarkups[actions[TenderBranch]];
        public static bool Expand(int[] actions) => actions[ExpandBranch] == 1;

        // Маска допустимых действий: true — можно. «Премиум» не берёт дешёвое сырьё, «Демпер» — дорогое;
        // неактивные продукты не меняют цену.
        public static bool[] Mask(BotSituation s)
        {
            var mask = new bool[MaskSize];
            for (int i = 0; i < mask.Length; i++) mask[i] = true;
            int offset = 0;
            for (int branch = 0; branch < BranchSizes.Length; branch++)
            {
                if (branch < BotSituation.MaxProducts && !s.Products[branch].Active)
                    for (int a = 0; a < BranchSizes[branch]; a++) mask[offset + a] = a == 2; // только «без изменения»
                if (branch == MaterialsBranch && s.Archetype == 1) mask[offset] = false;
                if (branch == MaterialsBranch && s.Archetype == 2) mask[offset + 1] = false;
                offset += BranchSizes[branch];
            }
            return mask;
        }

        // Первое разрешённое действие ветки (на случай, если политика выбрала запрещённое).
        public static int[] Sanitize(int[] actions, bool[] mask)
        {
            var result = new int[BranchSizes.Length];
            int offset = 0;
            for (int branch = 0; branch < BranchSizes.Length; branch++)
            {
                int a = actions != null && branch < actions.Length ? actions[branch] : 0;
                if (a < 0 || a >= BranchSizes[branch] || !mask[offset + a])
                {
                    a = 0;
                    while (a < BranchSizes[branch] - 1 && !mask[offset + a]) a++;
                }
                result[branch] = a;
                offset += BranchSizes[branch];
            }
            return result;
        }
    }

    // Политика бота: по наблюдениям и маске выбирает действие в каждой ветке.
    // Реализации: обученная нейросеть (ONNX через Inference Engine, сборка Game) или правила.
    public interface IBotPolicy
    {
        int[] Decide(float[] observation, bool[] mask);
    }
}
