using System;
using System.Collections.Generic;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;
using Geminium.Core.Time;

namespace Geminium.Core.Supply
{
    // Заказ, который ещё в пути.
    public sealed class PendingDelivery
    {
        public Company Company { get; }
        public SupplierDef Supplier { get; }
        public double Quantity { get; }
        public double UnitPrice { get; }
        public long DueDay { get; }

        internal PendingDelivery(Company company, SupplierDef supplier, double quantity, double unitPrice, long dueDay)
        {
            Company = company;
            Supplier = supplier;
            Quantity = quantity;
            UnitPrice = unitPrice;
            DueDay = dueDay;
        }
    }

    // Регулярная поставка: столько-то единиц с такой-то частотой.
    public sealed class SupplySchedule
    {
        public Company Company { get; }
        public SupplierDef Supplier { get; }
        public double Quantity { get; }
        public DeliveryFrequency Frequency { get; }

        internal SupplySchedule(Company company, SupplierDef supplier, double quantity, DeliveryFrequency frequency)
        {
            Company = company;
            Supplier = supplier;
            Quantity = quantity;
            Frequency = frequency;
        }
    }

    // Снабжение (GDD 7.4): биржевые цены, поставщики, лояльность, разовые и регулярные поставки.
    // Исход поставки (в срок, задержка, срыв) и качество партии определяются генератором «supply».
    public sealed class SupplySystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly SupplySettings settings;
        private readonly SortedDictionary<string, double> exchangePrices = new SortedDictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> orderedThisMonth = new Dictionary<string, double>();
        private readonly Dictionary<string, long> purchasesBySupplier = new Dictionary<string, long>();
        private readonly SortedDictionary<string, SupplySchedule> schedules = new SortedDictionary<string, SupplySchedule>(StringComparer.Ordinal);
        private readonly List<PendingDelivery> pending = new List<PendingDelivery>();
        private readonly List<Dictionary<string, double>> priceHistory = new List<Dictionary<string, double>>();
        private SimRandom random;
        private SimRandom priceRandom;

        public IReadOnlyList<PendingDelivery> PendingDeliveries => pending;
        public IEnumerable<SupplySchedule> Schedules => schedules.Values;
        public IReadOnlyList<Dictionary<string, double>> PriceHistory => priceHistory; // по месяцам
        public int FailedDeliveries { get; private set; }
        public int DelayedDeliveries { get; private set; }

        public SupplySystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Supply;
        }

        protected override void OnAttached()
        {
            random = Simulation.Random.Fork("supply");
            priceRandom = Simulation.Random.Fork("supply.prices");
        }

        private EconomySystem Economy => world.Economy;

        // Текущая биржевая цена единицы сырья, сумы.
        public double ExchangePrice(string materialId) =>
            exchangePrices.TryGetValue(materialId, out double price) ? price : catalog.Material(materialId).BasePrice;

        // Цена у поставщика для компании: биржа, курс (импорт), отклонение, лояльность, частота.
        public double QuotePrice(Company company, SupplierDef supplier, DeliveryFrequency frequency)
        {
            double exchange = ExchangePrice(supplier.MaterialId);
            if (supplier.Origin == MaterialOrigin.Import) exchange *= Economy.Multiplier(EventEffect.ImportPrice);
            return Formulas.SupplierPrice(exchange, supplier.PriceDeviation, LoyaltyDiscount(company, supplier), FrequencyDiscount(frequency));
        }

        public double LoyaltyDiscount(Company company, SupplierDef supplier)
        {
            purchasesBySupplier.TryGetValue(Key(company, supplier), out long purchases);
            return Math.Min(settings.MaxLoyaltyDiscount, settings.LoyaltyDiscountPerMillion * purchases / 1_000_000.0);
        }

        public double FrequencyDiscount(DeliveryFrequency frequency)
        {
            switch (frequency)
            {
                case DeliveryFrequency.Weekly: return settings.WeeklyDiscount;
                case DeliveryFrequency.TwiceWeekly: return settings.TwiceWeeklyDiscount;
                case DeliveryFrequency.Daily: return settings.DailyDiscount;
                default: return 0;
            }
        }

        public SupplySchedule ScheduleOf(Company company, SupplierDef supplier) =>
            schedules.TryGetValue(Key(company, supplier), out var schedule) ? schedule : null;

        internal void SetSchedule(Company company, SupplierDef supplier, double quantity, DeliveryFrequency frequency)
        {
            string key = Key(company, supplier);
            if (quantity <= 0 || frequency == DeliveryFrequency.None) schedules.Remove(key);
            else schedules[key] = new SupplySchedule(company, supplier, quantity, frequency);
        }

        // Заказ: цена фиксируется сейчас, оплата — при поставке, исход разыгрывается сразу.
        internal void PlaceOrder(Company company, SupplierDef supplier, double quantity, DeliveryFrequency frequency)
        {
            double unitPrice = QuotePrice(company, supplier, frequency);
            orderedThisMonth.TryGetValue(supplier.MaterialId, out double ordered);
            orderedThisMonth[supplier.MaterialId] = ordered + quantity;

            double unreliability = Formulas.Clamp(1 - supplier.Reliability + Economy.Additive(EventEffect.SupplierFailure), 0, 1);
            bool problem = random.Chance(unreliability);
            bool failure = random.Chance(settings.FailureShare);
            int delay = settings.MaxDelayDays > 0 ? random.NextInt(1, settings.MaxDelayDays + 1) : 0;
            if (problem && failure)
            {
                FailedDeliveries++;
                return;
            }
            if (problem) DelayedDeliveries++;
            else delay = 0;

            long dueDay = Simulation.Now.DayIndex + supplier.LeadTimeDays + delay;
            var delivery = new PendingDelivery(company, supplier, quantity, unitPrice, dueDay);
            pending.Add(delivery);
            var at = new SimTime(Math.Max(Simulation.Now.Minute, dueDay * SimTime.MinutesPerDay));
            Simulation.Scheduler.Schedule(at, () => Deliver(delivery));
        }

        private void Deliver(PendingDelivery delivery)
        {
            pending.Remove(delivery);
            var company = delivery.Company;
            if (company.IsBankrupt) return;

            var supplier = delivery.Supplier;
            double quality = Formulas.Clamp(random.Range(supplier.QualityMin, supplier.QualityMax) + Economy.Additive(EventEffect.BatchQuality), 0, 100);
            if (company.InputQcEnabled && company.HasQcStation && company.CountWorkers(catalog.Staff.QcInspectorProfessionId) > 0
                && quality < catalog.Quality.InputQcThreshold)
            {
                // Входной контроль вернул партию: поставщик заменяет её без оплаты сейчас (GDD 7.4).
                company.CurrentMonth.RejectedBatches++;
                var replacement = new PendingDelivery(company, supplier, delivery.Quantity, delivery.UnitPrice,
                    Simulation.Now.DayIndex + Math.Max(1, supplier.LeadTimeDays));
                pending.Add(replacement);
                Simulation.Scheduler.Schedule(new SimTime(replacement.DueDay * SimTime.MinutesPerDay), () => Deliver(replacement));
                return;
            }
            long cost = (long)Math.Round(delivery.Quantity * delivery.UnitPrice);
            company.Pay(LedgerCategory.MaterialPurchases, cost);
            company.Inventory.AddMaterial(supplier.MaterialId, delivery.Quantity, quality, delivery.UnitPrice);

            string key = Key(company, supplier);
            purchasesBySupplier.TryGetValue(key, out long purchases);
            purchasesBySupplier[key] = purchases + cost;
        }

        protected internal override void OnMonthStarted(int monthIndex)
        {
            foreach (var material in catalog.Materials)
            {
                orderedThisMonth.TryGetValue(material.Id, out double demand);
                double basePrice = material.BasePrice * Economy.PriceLevel * Economy.Multiplier(EventEffect.MaterialPrice, material.Id);
                exchangePrices[material.Id] = Formulas.ExchangePrice(basePrice, material.Volatility, priceRandom.NextGaussian(),
                    demand, material.MarketSupplyPerMonth, settings.PriceRatioMin, settings.PriceRatioMax);
            }
            orderedThisMonth.Clear();
            priceHistory.Add(new Dictionary<string, double>(exchangePrices));
        }

        protected internal override void OnDayStarted(long dayIndex)
        {
            DayOfWeek day = Simulation.Calendar.ToDateTime(Simulation.Now).DayOfWeek;
            foreach (var schedule in new List<SupplySchedule>(schedules.Values))
            {
                if (schedule.Company.IsBankrupt || !IsDeliveryDay(schedule.Frequency, day)) continue;
                PlaceOrder(schedule.Company, schedule.Supplier, schedule.Quantity, schedule.Frequency);
            }
        }

        private static bool IsDeliveryDay(DeliveryFrequency frequency, DayOfWeek day)
        {
            switch (frequency)
            {
                case DeliveryFrequency.Weekly: return day == DayOfWeek.Monday;
                case DeliveryFrequency.TwiceWeekly: return day == DayOfWeek.Monday || day == DayOfWeek.Wednesday;
                case DeliveryFrequency.Daily: return day != DayOfWeek.Saturday && day != DayOfWeek.Sunday;
                default: return false;
            }
        }

        private static string Key(Company company, SupplierDef supplier) => company.Id + "|" + supplier.Id;
    }
}
