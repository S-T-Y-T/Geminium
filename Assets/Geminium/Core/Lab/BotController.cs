using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Factory;
using Geminium.Core.Sales;
using Geminium.Core.Staff;

namespace Geminium.Core.Lab
{
    // Решения бота на месяц — «ручки», которые выставляет слой решений (поиск восхождением или нейросеть).
    public sealed class BotKnobs
    {
        public readonly Dictionary<string, double> PriceFactors = new Dictionary<string, double>(); // к эталонной цене × уровень цен
        public double PlanFactor = 1;      // план = оценка спроса × множитель
        public bool QualityMaterials;      // поставщики с лучшим качеством
        public double AdShare;             // реклама — доля выручки прошлого месяца
        public double TenderMarkup;        // заявка = P_max × наценка; 0 — не участвовать
        public bool Expand;                // докупить станок в узкое место
    }

    // Общая часть ботов (GDD 8.1). Планировка и расширение — скриптовый планировщик по архетипу,
    // месячные решения — наследник (Decide). Исполнение решений одинаковое: всё через команды.
    // Месяц 0: цех по шаблону архетипа, найм, цены, план под мощность, поставки.
    // Каждый день: свободных операторов — к станкам, регламент ТО на новых станках.
    // Каждый месяц: Decide → цены, план (спрос + контракты), поставки, расширение, найм, маркетинг, тендеры.
    public abstract class BotController : CompanyController
    {
        private const double WeeksPerMonth = 4.33;
        private const double WorkingDaysPerMonth = 21.7;
        private const double Utilization = 0.75;   // потери на логистику и простои при оценке мощности
        private readonly HashSet<int> maintenanceSet = new HashSet<int>();

        public BotKnobs Knobs { get; } = new BotKnobs();
        public BotArchetypeDef Archetype { get; }

        // Модули цифровизации, которые бот купит, как только хватит денег (сценарии Лаборатории, GDD 10).
        public List<DigitalModule> PlannedModules { get; } = new List<DigitalModule>();
        public bool NaiveLayout { get; set; }     // неудачная планировка (сценарий Лаборатории для двойника)

        protected BotController(BotArchetypeDef archetype)
        {
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
        }

        protected DataCatalog Catalog => World.Catalog;
        protected List<ProductDef> Products => World.Rules.OpenProducts(Catalog).ToList();

        // 0 — сбалансированный, 1 — «Премиум», 2 — «Демпер» (для наблюдений нейросети).
        public int ArchetypeIndex => Archetype.Id == "premium" ? 1 : Archetype.Id == "dumper" ? 2 : 0;

        protected internal override void OnMonthStarted(int monthIndex)
        {
            if (monthIndex == 0)
            {
                Setup();
                return;
            }
            if (Company.LastReport == null) return;
            BuyPlannedModules();
            if (Decide(monthIndex)) Execute();
        }

        protected internal override void OnDayStarted(long dayIndex)
        {
            AssignOperators();
            foreach (var machine in Company.Machines)
                if (maintenanceSet.Add(machine.Id) && machine.Machine.MaintenanceInterval != Archetype.MaintenanceInterval)
                    Submit(new SetMaintenanceIntervalCommand(machine.Id, Archetype.MaintenanceInterval));
        }

        // Слой решений: выставить Knobs на этот месяц. false — решение придёт позже (см. ExecuteDecision).
        protected abstract bool Decide(int monthIndex);

        // Исполнить решение, принятое после начала месяца (команды применятся в ближайшую минуту).
        protected void ExecuteDecision() => Execute();

        // ---------- Старт ----------

        private void Setup()
        {
            // Половина капитала — на станки, поровну на зону; в зоне — лучший станок архетипа, который укладывается в долю.
            var zones = Products.SelectMany(p => p.Operations).Select(o => o.Zone).Distinct().OrderBy(z => z).ToList();
            double perZone = Company.Cash * 0.5 / Math.Max(1, zones.Count);
            var machines = zones.Select(z => PickMachine(z, perZone)).Where(m => m != null).ToList();
            var template = new LayoutTemplate
            {
                QcStation = Archetype.QcStation, RawRacks = Archetype.RawRacks, FinishedRacks = Archetype.FinishedRacks,
                Desks = Archetype.SalesManagers, Naive = NaiveLayout,
            };
            Submit(new BatchCommand(FloorPlanner.InitialLayout(Catalog, template, machines.Select(m => m.Id))));
            Submit(new SetDefectPolicyCommand(Archetype.DefectPolicy));
            Submit(new SetInputQcCommand(Archetype.InputQc));
            Hire(machines);

            int rivals = Math.Max(1, World.Companies.Count);
            var plans = FitToCapacity(Products.ToDictionary(p => p.Id, p => p.BaseDemandPerMonth / (double)rivals), machines);
            Knobs.QualityMaterials = Archetype.PreferQuality;
            foreach (var product in Products)
            {
                Knobs.PriceFactors[product.Id] = Archetype.PriceFactor;
                Submit(new SetRetailPriceCommand(product.Id, (long)Math.Round(product.BasePrice * Archetype.PriceFactor)));
                Submit(new SetMonthlyPlanCommand(product.Id, (int)Math.Round(plans[product.Id])));
            }
            UpdateSupply(plans);
        }

        // ---------- Исполнение решений месяца ----------

        private void Execute()
        {
            var last = Company.LastReport;
            bool aiPricing = Company.Digital.Has(DigitalModule.AiPricing) && Company.Digital.AutoPricing;
            foreach (var product in Products)
            {
                double factor = Formulas.Clamp(PriceFactor(product.Id), Archetype.MinPriceFactor, Archetype.MaxPriceFactor);
                Knobs.PriceFactors[product.Id] = factor;
                if (!aiPricing) Submit(new SetRetailPriceCommand(product.Id, (long)Math.Round(product.BasePrice * World.Economy.PriceLevel * factor)));
            }

            var desired = new Dictionary<string, double>();
            foreach (var product in Products)
            {
                last.SoldByProduct.TryGetValue(product.Id, out long sold);
                last.LostSalesByProduct.TryGetValue(product.Id, out long lost);
                long stock = Company.Inventory.ProductUnits(product.Id);
                double demand = Company.Digital.Has(DigitalModule.DemandForecast) ? World.Market.Forecast(Company, product.Id) : sold + lost;
                desired[product.Id] = Math.Max(10, demand * Knobs.PlanFactor - stock + ContractNeedThisMonth(product.Id));
            }

            var owned = Company.Machines.Select(m => Catalog.Machine(m.DefId)).ToList();
            var newMachines = new List<MachineDef>();
            if (Knobs.Expand)
            {
                var machine = PickMachine(Bottleneck(desired, owned));
                var spot = machine == null ? null : FloorPlanner.FindMachineSpot(Company.Floor, Catalog, machine.Id, NaiveLayout);
                if (spot.HasValue && Company.Cash > machine.Price * 3)
                {
                    Submit(PlaceObjectCommand.Machine(machine.Id, spot.Value.x, spot.Value.y, spot.Value.facing));
                    newMachines.Add(machine);
                }
            }
            var all = owned.Concat(newMachines).ToList();
            var plans = FitToCapacity(desired, all);
            foreach (var pair in plans) Submit(new SetMonthlyPlanCommand(pair.Key, (int)Math.Round(pair.Value)));
            UpdateSupply(plans);
            Hire(all);

            foreach (var worker in Company.Workers.Where(w => w.Morale < 40))
                Submit(new SetWageCommand(worker.Id, (long)Math.Round(worker.MonthlyWage * 1.05)));
            if (Company.Cash < 10_000_000 && Company.Debt == 0) Submit(new TakeLoanCommand(30_000_000, 12));

            Submit(new SetAdBudgetCommand((long)Math.Round(Math.Max(0, last.Revenue - last.ContractRevenue) * Knobs.AdShare)));
            Marketing(last);
            BidTenders(all);
            if (Company.Digital.Has(DigitalModule.DigitalTwin) && World.Simulation.MonthIndex % Catalog.Digital.TwinReviewMonths == 0)
                foreach (var move in DigitalTwin.Suggest(World, Company, 2))
                    if (Company.Cash > move.Cost * 10) Submit(new MoveObjectCommand(move.ObjectId, move.X, move.Y, move.Facing));
        }

        // Купить запланированные модули по порядку (зависимые — после тех, от которых зависят), если денег хватает с запасом.
        private void BuyPlannedModules()
        {
            if (PlannedModules.Count == 0 || World.Simulation.MonthIndex < Catalog.Digital.LabPurchaseFromMonth) return;
            foreach (var module in PlannedModules)
            {
                if (Company.Digital.Has(module)) continue;
                var requires = World.Digital.Def(module).Requires;
                if (requires != DigitalModule.None && !Company.Digital.Has(requires)) continue;
                if (Company.Cash < World.Digital.NetCapex(Company, module) * Catalog.Digital.LabCashReserve) return;
                Submit(new BuyDigitalModuleCommand(module));
                return; // по одному модулю в месяц: зависимый купится в следующем
            }
        }

        protected virtual double PriceFactor(string productId) =>
            Knobs.PriceFactors.TryGetValue(productId, out double f) ? f : Archetype.PriceFactor;

        // Сколько единиц продукта нужно выпустить в этом месяце под контракты (остаток / месяцев до срока).
        private double ContractNeedThisMonth(string productId)
        {
            double need = 0;
            int month = World.Simulation.MonthIndex;
            foreach (var c in Company.Sales.Contracts.Where(c => c.Status == ContractStatus.Active && c.ProductId == productId))
                need += c.Remaining / (double)Math.Max(1, c.DeadlineMonth - month + 1);
            return need;
        }

        // ---------- Маркетинг по архетипу (GDD 8: «Премиум» сертифицируется, спонсирует, нанимает амбассадора) ----------

        private void Marketing(MonthlyReport last)
        {
            var marketing = World.Marketing;
            int month = World.Simulation.MonthIndex;
            long revenue = Math.Max(1, last.Revenue);

            if (Archetype.Certify)
            {
                // Два самых массовых продукта — с сертификатом WT; продлить заранее, за 3 месяца до конца срока.
                foreach (var product in Products.OrderByDescending(p => p.BaseDemandPerMonth).Take(2))
                {
                    bool covered = Company.Sales.Certificates.Any(c => c.ProductId == product.Id && c.Body == CertificationBody.WT
                                                                      && (!c.Checked || (c.Passed && c.ValidUntilMonth > month + 3)));
                    long cost = (long)Math.Round(Catalog.Marketing.CertificationCost * World.Economy.PriceLevel);
                    if (!covered && Company.Cash > cost * 3 && Company.Inventory.ProductUnits(product.Id) > 0)
                        Submit(new CertifyProductCommand(product.Id, CertificationBody.WT));
                }
            }

            if (Archetype.SponsorUpTo != TournamentLevel.None)
            {
                var tournament = marketing.UpcomingTournaments
                    .Where(t => t.Level <= Archetype.SponsorUpTo && !t.Sponsors.Contains(Company.Id))
                    .OrderByDescending(t => t.Level).FirstOrDefault();
                if (tournament != null && Company.Cash > marketing.MarketingCost(Company, marketing.SponsorshipCost(tournament.Level)) * 5)
                    Submit(new SponsorTournamentCommand(tournament.Id));
            }

            if (Company.Sales.Ambassadors.Count < Archetype.Ambassadors)
            {
                // Лучший свободный спортсмен, чей гонорар не больше 3% выручки.
                var athlete = Catalog.Athletes.Where(a => marketing.AmbassadorOwner(a.Id) == null && a.MonthlyFee * World.Economy.PriceLevel <= revenue * 0.03)
                    .OrderByDescending(a => a.Rating).FirstOrDefault();
                if (athlete != null && Company.Cash > athlete.MonthlyFee * 6) Submit(new HireAmbassadorCommand(athlete.Id));
            }
        }

        // ---------- Тендеры ----------

        private void BidTenders(List<MachineDef> machines)
        {
            if (Knobs.TenderMarkup <= 0) return;
            var produced = new HashSet<string>(Products.Select(p => p.Id));
            foreach (var tender in World.Tenders.OpenTenders.OrderByDescending(t => t.Units))
            {
                if (!produced.Contains(tender.ProductId) || World.Tenders.CheckEligibility(Company, tender) != null) continue;
                if (!HasRoomFor(tender, machines)) continue;
                long price = (long)Math.Round(tender.MaxUnitPrice * Knobs.TenderMarkup);
                Submit(new BidTenderCommand(tender.Id, Math.Max(1, price)));
            }
        }

        // Хватит ли доли мощности под контракты (TenderAppetite) на уже взятые контракты и этот тендер.
        private bool HasRoomFor(Tender tender, List<MachineDef> machines)
        {
            int month = World.Simulation.MonthIndex;
            var load = new Dictionary<ProductionZone, double>();
            void AddLoad(string productId, double unitsPerMonth)
            {
                foreach (var op in Catalog.Product(productId).Operations)
                {
                    load.TryGetValue(op.Zone, out double l);
                    load[op.Zone] = l + unitsPerMonth * op.BaseTimeMinutes;
                }
            }
            foreach (var c in Company.Sales.Contracts.Where(c => c.Status == ContractStatus.Active))
                AddLoad(c.ProductId, c.Remaining / (double)Math.Max(1, c.DeadlineMonth - month + 1));
            AddLoad(tender.ProductId, tender.Units / (double)Math.Max(1, tender.DeliveryMonths));
            return load.All(pair => pair.Value <= Archetype.TenderAppetite * ZoneCapacity(pair.Key, machines));
        }

        // ---------- Персонал ----------

        // Нанять недостающих: оператор на каждый станок, разнорабочие (1 на 3 станка), наладчик, контролёр, менеджеры.
        private void Hire(List<MachineDef> machines)
        {
            var staff = Catalog.Staff;
            var need = new List<string>();
            var byProfession = Company.Workers.GroupBy(w => w.ProfessionId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in machines.GroupBy(m => Catalog.OperatorOf(m.Zone)?.Id).Where(g => g.Key != null))
            {
                byProfession.TryGetValue(group.Key, out int have);
                for (int i = have; i < group.Count(); i++) need.Add(group.Key);
            }
            int laborers = Math.Max(1, (int)Math.Ceiling(machines.Count / 3.0));
            for (int i = Company.CountWorkers(staff.LaborerProfessionId); i < laborers; i++) need.Add(staff.LaborerProfessionId);
            if (Archetype.Mechanic && Company.CountWorkers(staff.MechanicProfessionId) == 0) need.Add(staff.MechanicProfessionId);
            if (Archetype.QcStation && Company.CountWorkers(staff.QcInspectorProfessionId) == 0) need.Add(staff.QcInspectorProfessionId);
            for (int i = Company.CountWorkers(staff.SalesManagerProfessionId); i < Archetype.SalesManagers; i++) need.Add(staff.SalesManagerProfessionId);

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
            switch (Archetype.Hiring)
            {
                case CandidateChoice.Experienced: return c.Experience;
                case CandidateChoice.Cheap: return -c.ExpectedWage;
                default: return c.Experience / Math.Max(1, c.ExpectedWage) * 1_000_000;
            }
        }

        // Свободных операторов — к станкам их зоны, у которых нет человека в смене 0.
        internal void AssignOperators()
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

        protected double ZoneCapacity(ProductionZone zone, List<MachineDef> machines) =>
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
            return machines.LastOrDefault(m => m.Tier <= Archetype.MachineTier && m.Price <= budget) ?? machines.FirstOrDefault();
        }

        // Недельные поставки сырья под месячный план: (нужно − запас) / 4,33 недели, не меньше минимальной партии.
        private void UpdateSupply(Dictionary<string, double> plans)
        {
            var need = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var product in Products)
            foreach (var op in product.Operations)
            foreach (var input in op.Inputs)
            {
                need.TryGetValue(input.MaterialId, out double amount);
                need[input.MaterialId] = amount + World.Production.InputAmount(Company, op, input) * plans[product.Id];
            }

            foreach (var pair in need)
            {
                var suppliers = Catalog.SuppliersOf(pair.Key);
                if (suppliers.Count == 0) continue;
                var chosen = Knobs.QualityMaterials
                    ? suppliers.OrderByDescending(s => s.QualityMax).ThenBy(s => s.PriceDeviation).First()
                    : suppliers.OrderBy(s => s.PriceDeviation).ThenByDescending(s => s.Reliability).First();
                double missing = Math.Max(0, pair.Value - Company.Inventory.MaterialQuantity(pair.Key));
                foreach (var other in suppliers.Where(s => s != chosen))
                    Submit(new SetSupplyScheduleCommand(other.Id, 0, DeliveryFrequency.None));
                double weekly = missing <= 0 ? 0 : Math.Max(chosen.MinBatch, Math.Ceiling(missing / WeeksPerMonth));
                Submit(new SetSupplyScheduleCommand(chosen.Id, weekly, weekly > 0 ? DeliveryFrequency.Weekly : DeliveryFrequency.None));
            }
        }

        // ---------- Наблюдения для нейросети ----------

        // Ситуация компании на начало месяца — тот же набор, что даёт суррогатная модель.
        public BotSituation Situation()
        {
            var world = World;
            var last = Company.LastReport;
            var economy = world.Economy;
            int month = world.Simulation.MonthIndex;
            var s = new BotSituation
            {
                Archetype = ArchetypeIndex,
                CashRatio = Company.Cash / (double)Catalog.StartingCash,
                DebtRatio = Company.Debt / (double)Catalog.StartingCash,
                Reputation = Company.Reputation,
                NetIncomeRatio = (last?.NetIncome ?? 0) / (double)Catalog.StartingCash,
                Fulfilment = last == null ? 1 : last.UnitsProduced / (double)Math.Max(1, Products.Sum(p => Company.MonthlyPlan(p.Id))),
                DefectRate = last == null || last.UnitsProduced + last.UnitsDefective == 0 ? 0
                    : last.UnitsDefective / (double)(last.UnitsProduced + last.UnitsDefective),
                Machines = Company.MachineCount,
                EconomyIndex = economy.Index,
                NextSeasonality = Formulas.Seasonality(world.Simulation.Calendar.ToDateTime(world.Simulation.Now).AddMonths(1).Month,
                    Catalog.Economy.SeasonalityAmplitude, Catalog.Economy.SeasonalityPeakMonth),
                KeyRate = economy.KeyRate,
                Inflation = economy.Inflation,
                CalendarMonth = world.Simulation.Calendar.ToDateTime(world.Simulation.Now).Month,
                CompetitorReputation = world.Companies.Where(c => c != Company).Select(c => c.Reputation).DefaultIfEmpty(50).Average(),
            };
            int rivals = Math.Max(1, world.Companies.Count(c => !c.IsBankrupt));
            var candidates = world.Tenders.OpenTenders.Where(t => world.Tenders.CheckEligibility(Company, t) == null).ToList();
            s.TenderAvailable = candidates.Count > 0;
            if (candidates.Count > 0)
            {
                var biggest = candidates.OrderByDescending(t => t.Units).First();
                s.TenderUnitsRatio = biggest.Units / (Catalog.Product(biggest.ProductId).BaseDemandPerMonth / (double)rivals);
            }

            for (int i = 0; i < Catalog.Products.Count && i < BotSituation.MaxProducts; i++)
            {
                var product = Catalog.Products[i];
                var p = s.Products[i];
                p.Active = world.Rules.IsProductOpen(product);
                if (!p.Active) continue;
                double reference = product.BasePrice * economy.PriceLevel;
                double perCompany = Math.Max(1, product.BaseDemandPerMonth / (double)rivals);
                p.PriceFactor = Company.RetailPrice(product.Id) / reference;
                p.MarketPriceFactor = world.Companies.Where(c => !c.IsBankrupt).Average(c => (double)c.RetailPrice(product.Id)) / reference;
                p.Share = last != null && last.MarketShareByProduct.TryGetValue(product.Id, out double share) ? share : 0;
                p.StockRatio = Company.Inventory.ProductUnits(product.Id) / perCompany;
                p.LostRatio = last != null && last.LostSalesByProduct.TryGetValue(product.Id, out long lost) ? lost / perCompany : 0;
                p.CommittedRatio = Company.Sales.CommittedUnits(product.Id) / perCompany;
            }
            return s;
        }
    }
}
