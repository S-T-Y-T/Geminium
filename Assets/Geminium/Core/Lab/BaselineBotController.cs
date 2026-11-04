using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Data;

namespace Geminium.Core.Lab
{
    // Скриптовый бот (GDD 8.2): базовая линия Лаборатории и запасной вариант до ML-Agents.
    // Месячные решения — поиск восхождением по четырём ручкам: общий уровень цены, доля рекламы,
    // наценка в тендерах, рост плана. Цель — чистая прибыль прошлого месяца.
    // Поверх общего уровня цена каждого продукта подстраивается правилом: много упущенных продаж — дороже,
    // залежался товар — дешевле. Станок докупается, когда упущенных продаж больше 20% от проданного.
    public sealed class BaselineBotController : BotController
    {
        private const int PriceKnob = 0, AdKnob = 1, TenderKnob = 2, PlanKnob = 3;
        private readonly Dictionary<string, double> adjustments = new Dictionary<string, double>();
        private HillClimber climber;

        public BaselineBotController(BotArchetypeDef archetype) : base(archetype) { }

        public HillClimber Climber => climber;

        // Без поиска: ручки остаются на стартовых значениях архетипа (Лаборатория сценариев сравнивает модули
        // при одной и той же политике управления — иначе крошечная разница в прибыли переворачивает решения поиска).
        public bool FreezeKnobs { get; set; }

        protected override bool Decide(int monthIndex)
        {
            var a = Archetype;
            if (climber == null)
            {
                bool tenders = a.TenderMarkup > 0;
                climber = new HillClimber(
                    new[] { a.PriceFactor, a.AdBudgetShare, a.TenderMarkup, a.PlanGrowth },
                    new[] { a.MinPriceFactor, 0, tenders ? 0.7 : 0, 0.9 },
                    new[] { a.MaxPriceFactor, a.MaxAdBudgetShare, tenders ? 1.0 : 0, 1.4 },
                    a.HillClimbStep, World.Simulation.Random.Fork("bot." + Company.Id));
            }
            var last = Company.LastReport;
            // Первый полный месяц — точка отсчёта; искать начинаем со второго.
            if (monthIndex >= 2 && !FreezeKnobs) climber.Step(last.NetIncome);

            long sold = 0, lost = 0;
            foreach (var product in Products)
            {
                last.SoldByProduct.TryGetValue(product.Id, out long productSold);
                last.LostSalesByProduct.TryGetValue(product.Id, out long productLost);
                sold += productSold;
                lost += productLost;
                double adjustment = adjustments.TryGetValue(product.Id, out double x) ? x : 1;
                if (productLost > 0.2 * Math.Max(1, productSold)) adjustment *= 1.03;
                else if (Company.Inventory.ProductUnits(product.Id) > productSold) adjustment *= 0.97;
                adjustments[product.Id] = Formulas.Clamp(adjustment, 0.85, 1.15);
                Knobs.PriceFactors[product.Id] = climber[PriceKnob] * adjustments[product.Id];
            }
            Knobs.AdShare = climber[AdKnob];
            Knobs.TenderMarkup = climber[TenderKnob];
            Knobs.PlanFactor = climber[PlanKnob];
            Knobs.QualityMaterials = a.PreferQuality;
            Knobs.Expand = lost > 0.2 * Math.Max(1, sold);
            return true;
        }
    }

    // Бот под управлением политики (обученной нейросети или любой другой реализации IBotPolicy).
    // Планировка, найм, поставки и маркетинг по архетипу — как у скриптового бота; месячные ручки — от политики.
    // policy == null — действия приходят извне (дообучение в ML-Agents): в начале месяца бот готовит наблюдения
    // и ждёт вызова Act; команды месяца применятся на минуту позже — в полночь цех всё равно не работает.
    public sealed class PolicyBotController : BotController
    {
        private readonly IBotPolicy policy;

        public PolicyBotController(BotArchetypeDef archetype, IBotPolicy policy = null) : base(archetype)
        {
            this.policy = policy;
        }

        public int[] LastActions { get; private set; }
        public bool AwaitingActions { get; private set; }
        public BotSituation PendingSituation { get; private set; }
        public bool[] PendingMask { get; private set; }

        protected override bool Decide(int monthIndex)
        {
            var situation = Situation();
            var mask = BotActions.Mask(situation);
            if (policy == null)
            {
                PendingSituation = situation;
                PendingMask = mask;
                AwaitingActions = true;
                return false;
            }
            Apply(BotActions.Sanitize(policy.Decide(BotObservation.Encode(situation), mask), mask));
            return true;
        }

        // Действия извне для месяца, которого бот ждёт.
        public void Act(int[] actions)
        {
            if (!AwaitingActions) throw new InvalidOperationException("Бот не ждёт решения.");
            AwaitingActions = false;
            Apply(BotActions.Sanitize(actions, PendingMask));
            ExecuteDecision();
        }

        private void Apply(int[] actions)
        {
            LastActions = actions;

            for (int i = 0; i < Catalog.Products.Count && i < BotSituation.MaxProducts; i++)
            {
                var product = Catalog.Products[i];
                if (!product.UnlockedByDefault) continue;
                double current = Knobs.PriceFactors.TryGetValue(product.Id, out double f) ? f : Archetype.PriceFactor;
                Knobs.PriceFactors[product.Id] = current * (1 + BotActions.PriceStep(actions, i));
            }
            Knobs.PlanFactor = BotActions.PlanFactor(actions);
            Knobs.QualityMaterials = BotActions.QualityMaterials(actions);
            Knobs.AdShare = BotActions.AdShare(actions);
            Knobs.TenderMarkup = BotActions.TenderMarkup(actions);
            Knobs.Expand = BotActions.Expand(actions);
        }
    }
}
