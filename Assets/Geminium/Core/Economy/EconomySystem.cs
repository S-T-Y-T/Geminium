using System;
using System.Collections.Generic;
using Geminium.Core.Data;

namespace Geminium.Core.Economy
{
    // Событие, которое сейчас действует.
    public sealed class ActiveEvent
    {
        public EventDef Definition { get; }
        public int StartMonth { get; }
        public int RemainingMonths { get; internal set; }

        public ActiveEvent(EventDef definition, int startMonth, int durationMonths)
        {
            Definition = definition;
            StartMonth = startMonth;
            RemainingMonths = durationMonths;
        }
    }

    // Состояние экономики на начало месяца — для отчётов, CSV и будущих новостей LLM.
    public sealed class EconomySnapshot
    {
        public int Month;
        public double Index;
        public double Inflation;      // годовая
        public double KeyRate;        // годовая
        public double PriceLevel;     // накопленная инфляция, 1 — уровень старта
        public double Seasonality;
        public double PriceElasticity;
        public readonly List<string> ActiveEventIds = new List<string>();
    }

    // ИИ-ядро, макроэкономика (GDD 7.1): индекс экономики, инфляция, ключевая ставка, уровень цен,
    // сезонность и события. Это стохастическая модель на правилах, не нейросеть (GDD 15).
    // Пересчитывается в начале каждого месяца; события начинаются со второго месяца.
    public sealed class EconomySystem : SimulationSystem
    {
        private readonly DataCatalog catalog;
        private readonly EconomySettings settings;
        private readonly List<ActiveEvent> active = new List<ActiveEvent>();
        private readonly List<EconomySnapshot> history = new List<EconomySnapshot>();
        private readonly List<ActiveEvent> started = new List<ActiveEvent>();
        private SimRandom random;
        private SimRandom eventRandom;

        public double Index { get; private set; }
        public double Inflation { get; private set; }
        public double KeyRate { get; private set; }
        public double PriceLevel { get; private set; } = 1;
        public double Seasonality { get; private set; } = 1;

        public IReadOnlyList<ActiveEvent> ActiveEvents => active;
        public IReadOnlyList<ActiveEvent> StartedEvents => started;   // журнал всех начавшихся событий
        public IReadOnlyList<EconomySnapshot> History => history;

        public EconomySystem(DataCatalog catalog)
        {
            this.catalog = catalog;
            settings = catalog.Economy;
        }

        protected override void OnAttached()
        {
            random = Simulation.Random.Fork("economy");
            eventRandom = Simulation.Random.Fork("economy.events");
            Index = settings.InitialIndex;
            Inflation = settings.InitialInflation;
            KeyRate = CalculateKeyRate();
        }

        protected internal override void OnMonthStarted(int monthIndex)
        {
            if (monthIndex > 0)
            {
                Index = Formulas.NextEconomyIndex(Index, settings.IndexReversion, settings.IndexVolatility,
                    random.NextGaussian(), settings.IndexMin, settings.IndexMax);
                Inflation = Formulas.Clamp(Inflation
                    + settings.InflationReversion * (settings.InflationTarget - Inflation)
                    + settings.InflationIndexSensitivity * (Index - 1)
                    + settings.InflationVolatility * random.NextGaussian(), -0.05, 0.5);
                KeyRate = CalculateKeyRate();
                PriceLevel *= 1 + Inflation / 12;
                UpdateEvents(monthIndex);
            }

            int calendarMonth = Simulation.Calendar.ToDateTime(Simulation.Now).Month;
            Seasonality = Formulas.Seasonality(calendarMonth, settings.SeasonalityAmplitude, settings.SeasonalityPeakMonth);

            var snapshot = new EconomySnapshot
            {
                Month = monthIndex,
                Index = Index,
                Inflation = Inflation,
                KeyRate = KeyRate,
                PriceLevel = PriceLevel,
                Seasonality = Seasonality,
                PriceElasticity = PriceElasticity(catalog.Market.PriceElasticity),
            };
            foreach (var e in active) snapshot.ActiveEventIds.Add(e.Definition.Id);
            history.Add(snapshot);
        }

        // Произведение (1 + Magnitude) действующих событий с этим эффектом и подходящей целью.
        public double Multiplier(EventEffect effect, string target = null)
        {
            double multiplier = 1;
            foreach (var e in active)
                if (e.Definition.Effect == effect && Matches(e.Definition, target)) multiplier *= 1 + e.Definition.Magnitude;
            return Math.Max(0, multiplier);
        }

        // Сумма Magnitude действующих событий с этим эффектом.
        public double Additive(EventEffect effect, string target = null)
        {
            double sum = 0;
            foreach (var e in active)
                if (e.Definition.Effect == effect && Matches(e.Definition, target)) sum += e.Definition.Magnitude;
            return sum;
        }

        // Текущая ценовая эластичность w_p: растёт в кризис, меняется событиями.
        public double PriceElasticity(double baseElasticity) =>
            Formulas.PriceElasticity(baseElasticity, settings.ElasticityCrisisSensitivity, Index) * Multiplier(EventEffect.PriceElasticity);

        // Мультипликатор капитализации M: выше в бум, ниже в кризис.
        public double CapitalizationMultiplier => settings.CapitalizationMultiplierMonths * Index;

        private double CalculateKeyRate() =>
            Formulas.KeyRate(settings.KeyRateNeutral, settings.KeyRateInflationSensitivity, Inflation, settings.InflationTarget,
                settings.KeyRateMin, settings.KeyRateMax);

        private void UpdateEvents(int monthIndex)
        {
            foreach (var e in active) e.RemainingMonths--;
            active.RemoveAll(e => e.RemainingMonths <= 0);

            int calendarMonth = Simulation.Calendar.ToDateTime(Simulation.Now).Month;
            foreach (var definition in catalog.Events)
            {
                // Вероятность бросаем всегда — так число вызовов генератора не зависит от состояния.
                bool happens = eventRandom.Chance(definition.MonthlyProbability);
                int duration = eventRandom.NextInt(definition.MinDurationMonths, definition.MaxDurationMonths + 1);
                if (!happens || !InSeason(definition, calendarMonth) || active.Exists(e => e.Definition == definition)) continue;

                var activeEvent = new ActiveEvent(definition, monthIndex, duration);
                active.Add(activeEvent);
                started.Add(activeEvent);
            }
        }

        private static bool Matches(EventDef definition, string target) =>
            string.IsNullOrEmpty(definition.Target) || definition.Target == target;

        private static bool InSeason(EventDef definition, int calendarMonth)
        {
            if (definition.SeasonStartMonth == 0) return true;
            return definition.SeasonStartMonth <= definition.SeasonEndMonth
                ? calendarMonth >= definition.SeasonStartMonth && calendarMonth <= definition.SeasonEndMonth
                : calendarMonth >= definition.SeasonStartMonth || calendarMonth <= definition.SeasonEndMonth;
        }
    }
}
