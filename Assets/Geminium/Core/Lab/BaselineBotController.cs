using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Core.Lab
{
    // Профиль скриптового бота (GDD 8.1): отношение к цене, качеству сырья и уровню станков.
    public sealed class BotProfile
    {
        public double PriceFactor = 1.0;       // цена относительно эталонной
        public bool PreferQuality;             // выбирать поставщиков с лучшим качеством, а не с низкой ценой
        public int MachineTier = 1;            // какой уровень станков покупать
        public double PlanGrowth = 1.05;       // план = прошлый спрос × рост

        public static BotProfile Balanced => new BotProfile();
        public static BotProfile Premium => new BotProfile { PriceFactor = 1.2, PreferQuality = true, MachineTier = 2 };
        public static BotProfile Dumper => new BotProfile { PriceFactor = 0.85, MachineTier = 1, PlanGrowth = 1.1 };
    }

    // Скриптовый бот этапа 1: базовая линия для Лаборатории и запасной вариант до ML-Agents (GDD 8.2).
    // Месяц 0: по станку на каждую нужную зону, цены, план по доле рынка, урезанный под мощность, недельные поставки.
    // Дальше раз в месяц: план = прошлый спрос × рост (под мощность), поставки под план, цена — по остаткам
    // и упущенным продажам, станок в самую загруженную зону при упущенных продажах, кредит при нехватке денег.
    public sealed class BaselineBotController : CompanyController
    {
        private const double WeeksPerMonth = 4.33;
        private const double WorkingDaysPerMonth = 21.7;
        private readonly BotProfile profile;
        private readonly Dictionary<string, double> priceFactors = new Dictionary<string, double>();

        public BaselineBotController(BotProfile profile) { this.profile = profile ?? BotProfile.Balanced; }

        private DataCatalog Catalog => World.Catalog;

        private List<ProductDef> Products => Catalog.Products.Where(p => p.UnlockedByDefault).ToList();

        protected internal override void OnMonthStarted(int monthIndex)
        {
            if (monthIndex == 0) Setup();
            else Adjust();
        }

        private void Setup()
        {
            var machines = Products.SelectMany(p => p.Operations).Select(o => o.Zone).Distinct().OrderBy(z => z)
                .Select(PickMachine).Where(m => m != null).ToList();
            foreach (var machine in machines) Submit(new BuyMachineCommand(machine.Id));

            int rivals = Math.Max(1, World.Companies.Count);
            var desired = Products.ToDictionary(p => p.Id, p => p.BaseDemandPerMonth / (double)rivals);
            var plans = FitToCapacity(desired, machines);
            foreach (var product in Products)
            {
                priceFactors[product.Id] = profile.PriceFactor;
                Submit(new SetRetailPriceCommand(product.Id, (long)Math.Round(product.BasePrice * profile.PriceFactor)));
                Submit(new SetMonthlyPlanCommand(product.Id, (int)Math.Round(plans[product.Id])));
            }
            UpdateSupply(plans);
        }

        private void Adjust()
        {
            var last = Company.LastReport;
            if (last == null) return;

            var desired = new Dictionary<string, double>();
            long sold = 0, lost = 0;
            foreach (var product in Products)
            {
                last.SoldByProduct.TryGetValue(product.Id, out long productSold);
                last.LostSalesByProduct.TryGetValue(product.Id, out long productLost);
                sold += productSold;
                lost += productLost;
                long stock = Company.Inventory.ProductUnits(product.Id);
                desired[product.Id] = Math.Max(10, (productSold + productLost) * profile.PlanGrowth - stock);

                // Цена: много упущенных продаж — дороже, залежался товар — дешевле.
                double factor = priceFactors[product.Id];
                if (productLost > 0.2 * Math.Max(1, productSold)) factor *= 1.03;
                else if (stock > productSold) factor *= 0.97;
                factor = Formulas.Clamp(factor, profile.PriceFactor * 0.8, profile.PriceFactor * 1.25);
                priceFactors[product.Id] = factor;
                Submit(new SetRetailPriceCommand(product.Id, (long)Math.Round(product.BasePrice * World.Economy.PriceLevel * factor)));
            }

            var owned = Company.Machines.Select(m => Catalog.Machine(m.EquipmentId)).ToList();
            var plans = FitToCapacity(desired, owned);
            foreach (var pair in plans) Submit(new SetMonthlyPlanCommand(pair.Key, (int)Math.Round(pair.Value)));
            UpdateSupply(plans);

            // Упускаем продажи — станок в самую загруженную зону, если денег хватает с запасом.
            if (lost > 0.2 * Math.Max(1, sold))
            {
                var machine = PickMachine(Bottleneck(desired, owned));
                if (machine != null && Company.Cash > machine.Price * 3) Submit(new BuyMachineCommand(machine.Id));
            }

            if (Company.Cash < 10_000_000 && Company.Debt == 0) Submit(new TakeLoanCommand(30_000_000, 12));
        }

        // Минуты станков зоны за месяц при текущем графике.
        private double ZoneCapacity(ProductionZone zone, List<MachineDef> machines) =>
            machines.Where(m => m.Zone == zone).Sum(m => m.Power)
            * Company.Schedule.ShiftsPerDay * Company.Schedule.ShiftLengthMinutes * WorkingDaysPerMonth;

        private Dictionary<ProductionZone, double> ZoneLoads(Dictionary<string, double> plans)
        {
            var loads = new Dictionary<ProductionZone, double>();
            foreach (var product in Products)
            foreach (var op in product.Operations)
            {
                loads.TryGetValue(op.Zone, out double load);
                loads[op.Zone] = load + plans[product.Id] * op.BaseTimeMinutes;
            }
            return loads;
        }

        // Урезать план пропорционально, чтобы самая загруженная зона успевала.
        private Dictionary<string, double> FitToCapacity(Dictionary<string, double> desired, List<MachineDef> machines)
        {
            double factor = 1;
            foreach (var pair in ZoneLoads(desired))
                if (pair.Value > 0) factor = Math.Min(factor, ZoneCapacity(pair.Key, machines) / pair.Value);
            return desired.ToDictionary(p => p.Key, p => Math.Floor(p.Value * factor));
        }

        private ProductionZone Bottleneck(Dictionary<string, double> desired, List<MachineDef> machines) =>
            ZoneLoads(desired).OrderByDescending(p => p.Value / Math.Max(1, ZoneCapacity(p.Key, machines))).First().Key;

        private MachineDef PickMachine(ProductionZone zone)
        {
            var machines = Catalog.MachinesForZone(zone);
            return machines.LastOrDefault(m => m.Tier <= profile.MachineTier) ?? machines.FirstOrDefault();
        }

        // Недельные поставки сырья под месячный план: (нужно − запас) / 4,33 недели, не меньше минимальной партии.
        private void UpdateSupply(Dictionary<string, double> plans)
        {
            var need = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var product in Products)
            foreach (var input in product.Operations.SelectMany(o => o.Inputs))
            {
                need.TryGetValue(input.MaterialId, out double amount);
                need[input.MaterialId] = amount + input.Amount * plans[product.Id];
            }

            foreach (var pair in need)
            {
                var suppliers = Catalog.SuppliersOf(pair.Key);
                if (suppliers.Count == 0) continue;
                var chosen = profile.PreferQuality
                    ? suppliers.OrderByDescending(s => s.QualityMax).ThenBy(s => s.PriceDeviation).First()
                    : suppliers.OrderBy(s => s.PriceDeviation).ThenByDescending(s => s.Reliability).First();
                double missing = Math.Max(0, pair.Value - Company.Inventory.MaterialQuantity(pair.Key));
                foreach (var other in suppliers.Where(s => s != chosen))
                    Submit(new SetSupplyScheduleCommand(other.Id, 0, DeliveryFrequency.None));
                double weekly = missing <= 0 ? 0 : Math.Max(chosen.MinBatch, Math.Ceiling(missing / WeeksPerMonth));
                Submit(new SetSupplyScheduleCommand(chosen.Id, weekly, weekly > 0 ? DeliveryFrequency.Weekly : DeliveryFrequency.None));
            }
        }
    }
}
