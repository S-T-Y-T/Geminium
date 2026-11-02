using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Staff;

namespace Geminium.Core.Lab
{
    // Профиль скриптового бота (GDD 8.1): цена, качество сырья, станки, персонал и контроль.
    public sealed class BotProfile
    {
        public double PriceFactor = 1.0;       // цена относительно эталонной
        public bool PreferQuality;             // поставщики с лучшим качеством вместо дешёвых
        public int MachineTier = 1;            // уровень покупаемых станков
        public double PlanGrowth = 1.05;       // план = прошлый спрос × рост
        public bool QcStation = true;          // стол ОТК и контролёр
        public bool InputQc;                   // входной контроль сырья
        public bool Mechanic = true;           // наладчик для ТО
        public CandidateChoice Hiring = CandidateChoice.Balanced;
        public DefectPolicy DefectPolicy = DefectPolicy.Scrap;

        public static BotProfile Balanced => new BotProfile();
        public static BotProfile Premium => new BotProfile
        {
            PriceFactor = 1.2, PreferQuality = true, MachineTier = 2, InputQc = true,
            Hiring = CandidateChoice.Experienced, DefectPolicy = DefectPolicy.Repair,
        };
        public static BotProfile Dumper => new BotProfile
        {
            PriceFactor = 0.85, PlanGrowth = 1.1, QcStation = false, Mechanic = false, Hiring = CandidateChoice.Cheap,
        };
    }

    public enum CandidateChoice
    {
        Balanced = 0,     // опыт на сум зарплаты
        Experienced = 1,  // самые опытные
        Cheap = 2,        // самые дешёвые
    }

    // Скриптовый бот: базовая линия Лаборатории и запасной вариант до ML-Agents (GDD 8.2).
    // Месяц 0: цех по шаблону (FloorPlanner), по станку на зону, найм, цены, план под мощность, поставки.
    // Каждый день: свободных операторов — к станкам без людей.
    // Каждый месяц: план = прошлый спрос × рост (под мощность), поставки под план, цена по остаткам и упущенным
    // продажам, новый станок у прохода при упущенных продажах, найм на свободные места, прибавка при низкой морали.
    public sealed class BaselineBotController : CompanyController
    {
        private const double WeeksPerMonth = 4.33;
        private const double WorkingDaysPerMonth = 21.7;
        private const double Utilization = 0.75;   // потери на логистику и простои при оценке мощности
        private readonly BotProfile profile;
        private readonly Dictionary<string, double> priceFactors = new Dictionary<string, double>();

        public BaselineBotController(BotProfile profile) { this.profile = profile ?? BotProfile.Balanced; }

        public BotProfile Profile => profile;
        private DataCatalog Catalog => World.Catalog;
        private List<ProductDef> Products => Catalog.Products.Where(p => p.UnlockedByDefault).ToList();

        protected internal override void OnMonthStarted(int monthIndex)
        {
            if (monthIndex == 0) Setup();
            else Adjust();
        }

        protected internal override void OnDayStarted(long dayIndex) => AssignOperators();

        private void Setup()
        {
            // Половина капитала — на станки, поровну на зону; в зоне — лучший станок профиля, который укладывается в долю.
            var zones = Products.SelectMany(p => p.Operations).Select(o => o.Zone).Distinct().OrderBy(z => z).ToList();
            double perZone = Company.Cash * 0.5 / Math.Max(1, zones.Count);
            var machines = zones.Select(z => PickMachine(z, perZone)).Where(m => m != null).ToList();
            var template = new LayoutTemplate { QcStation = profile.QcStation };
            Submit(new BatchCommand(FloorPlanner.InitialLayout(Catalog, template, machines.Select(m => m.Id))));

            Submit(new SetDefectPolicyCommand(profile.DefectPolicy));
            Submit(new SetInputQcCommand(profile.InputQc));
            Hire(machines);

            int rivals = Math.Max(1, World.Companies.Count);
            var plans = FitToCapacity(Products.ToDictionary(p => p.Id, p => p.BaseDemandPerMonth / (double)rivals), machines);
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
                double factor = priceFactors.TryGetValue(product.Id, out double f) ? f : profile.PriceFactor;
                if (productLost > 0.2 * Math.Max(1, productSold)) factor *= 1.03;
                else if (stock > productSold) factor *= 0.97;
                factor = Formulas.Clamp(factor, profile.PriceFactor * 0.8, profile.PriceFactor * 1.25);
                priceFactors[product.Id] = factor;
                Submit(new SetRetailPriceCommand(product.Id, (long)Math.Round(product.BasePrice * World.Economy.PriceLevel * factor)));
            }

            var owned = Company.Machines.Select(m => Catalog.Machine(m.DefId)).ToList();
            var newMachines = new List<MachineDef>();
            if (lost > 0.2 * Math.Max(1, sold))
            {
                var machine = PickMachine(Bottleneck(desired, owned));
                var spot = machine == null ? null : FloorPlanner.FindMachineSpot(Company.Floor, Catalog, machine.Id);
                if (spot.HasValue && Company.Cash > machine.Price * 3)
                {
                    Submit(PlaceObjectCommand.Machine(machine.Id, spot.Value.x, spot.Value.y, spot.Value.facing));
                    newMachines.Add(machine);
                }
            }

            var plans = FitToCapacity(desired, owned.Concat(newMachines).ToList());
            foreach (var pair in plans) Submit(new SetMonthlyPlanCommand(pair.Key, (int)Math.Round(pair.Value)));
            UpdateSupply(plans);
            Hire(owned.Concat(newMachines).ToList());

            foreach (var worker in Company.Workers.Where(w => w.Morale < 40))
                Submit(new SetWageCommand(worker.Id, (long)Math.Round(worker.MonthlyWage * 1.05)));

            if (Company.Cash < 10_000_000 && Company.Debt == 0) Submit(new TakeLoanCommand(30_000_000, 12));
        }

        // ---------- Персонал ----------

        // Нанять недостающих: оператор на каждый станок, разнорабочие (1 на 3 станка), наладчик, контролёр.
        private void Hire(List<MachineDef> machines)
        {
            var staff = Catalog.Staff;
            var need = new List<string>();
            var operatorsByProfession = Company.Workers.GroupBy(w => w.ProfessionId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in machines.GroupBy(m => Catalog.OperatorOf(m.Zone)?.Id).Where(g => g.Key != null))
            {
                operatorsByProfession.TryGetValue(group.Key, out int have);
                for (int i = have; i < group.Count(); i++) need.Add(group.Key);
            }
            int laborers = Math.Max(1, (int)Math.Ceiling(machines.Count / 3.0));
            for (int i = Company.CountWorkers(staff.LaborerProfessionId); i < laborers; i++) need.Add(staff.LaborerProfessionId);
            if (profile.Mechanic && Company.CountWorkers(staff.MechanicProfessionId) == 0) need.Add(staff.MechanicProfessionId);
            if (profile.QcStation && Company.CountWorkers(staff.QcInspectorProfessionId) == 0) need.Add(staff.QcInspectorProfessionId);

            var taken = new HashSet<int>();
            foreach (string professionId in need)
            {
                foreach (var candidate in World.Staff.Candidates.Where(c => c.ProfessionId == professionId && !taken.Contains(c.Id))
                             .OrderByDescending(Score).ThenBy(c => c.Id))
                {
                    taken.Add(candidate.Id);
                    if (Submit(new HireCommand(candidate.Id)) == null) break; // занят другой компанией — берём следующего
                }
            }
        }

        private double Score(Candidate c)
        {
            switch (profile.Hiring)
            {
                case CandidateChoice.Experienced: return c.Experience;
                case CandidateChoice.Cheap: return -c.ExpectedWage;
                default: return c.Experience / Math.Max(1, c.ExpectedWage) * 1_000_000;
            }
        }

        // Свободных операторов — к станкам их зоны, у которых нет человека в смене 0.
        private void AssignOperators()
        {
            var busy = new HashSet<int>(Company.Workers.Where(w => w.AssignedObjectId >= 0).Select(w => w.AssignedObjectId));
            foreach (var worker in Company.Workers)
            {
                if (worker.AssignedObjectId >= 0 && Company.Floor.FindObject(worker.AssignedObjectId) != null) continue;
                var zone = Catalog.Profession(worker.ProfessionId).OperatedZone;
                if (zone == ProductionZone.None) continue;
                var machine = Company.Machines.FirstOrDefault(m => !busy.Contains(m.Id) && Catalog.Machine(m.DefId).Zone == zone);
                if (machine == null) continue;
                busy.Add(machine.Id);
                Submit(new AssignWorkerCommand(worker.Id, machine.Id, 0));
            }
        }

        // ---------- План и мощность ----------

        private double ZoneCapacity(ProductionZone zone, List<MachineDef> machines) =>
            machines.Where(m => m.Zone == zone).Sum(m => m.Power)
            * Company.Schedule.ShiftsPerDay * Company.Schedule.ShiftLengthMinutes * WorkingDaysPerMonth * Utilization;

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

        private Dictionary<string, double> FitToCapacity(Dictionary<string, double> desired, List<MachineDef> machines)
        {
            double factor = 1;
            foreach (var pair in ZoneLoads(desired))
                if (pair.Value > 0) factor = Math.Min(factor, ZoneCapacity(pair.Key, machines) / pair.Value);
            return desired.ToDictionary(p => p.Key, p => Math.Floor(p.Value * factor));
        }

        private ProductionZone Bottleneck(Dictionary<string, double> desired, List<MachineDef> machines) =>
            ZoneLoads(desired).OrderByDescending(p => p.Value / Math.Max(1, ZoneCapacity(p.Key, machines))).First().Key;

        private MachineDef PickMachine(ProductionZone zone, double budget = double.MaxValue)
        {
            var machines = Catalog.MachinesForZone(zone);
            return machines.LastOrDefault(m => m.Tier <= profile.MachineTier && m.Price <= budget) ?? machines.FirstOrDefault();
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
