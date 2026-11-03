using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Data;

namespace Geminium.Core.Lab.Surrogate
{
    // Компания в суррогатном мире: только то, что нужно месячной модели.
    public sealed class SurrogateCompany
    {
        public string Id;
        public BotArchetypeDef Archetype;
        public int ArchetypeIndex;
        public double Cash, Reputation = 50, Experience = 35, AdStock, Capitalization;
        public bool Bankrupt;
        public int NegativeMonths;
        public readonly List<MachineDef> Machines = new List<MachineDef>();
        public double[] PriceFactor, Stock, StockQuality, StockCost, HiddenDefects, Sold, Lost, Share;
        public double LastRevenue, LastRetailRevenue, LastNetIncome, LastOutput, LastDefective, LastPlan;
        public readonly List<double> NetIncomes = new List<double>();
        public readonly List<SurrogateContract> Contracts = new List<SurrogateContract>();
        public int B2bBatches, B2bComplaints;
        public int CertifiedFrom = int.MaxValue, CertifiedUntil = -1;
        public int SponsorBonusUntil = -1;
        public string Ambassador;
        public double Bids, PlanFactorNow = 1;   // решения текущего месяца: наценка в тендерах, множитель плана
        public bool QualityMaterialsNow;

        public double QualityHistory => 100.0 * (B2bBatches - B2bComplaints + 1) / (B2bBatches + 1);
        public bool Certified(int month) => month >= CertifiedFrom && month <= CertifiedUntil;
    }

    public sealed class SurrogateContract
    {
        public int Product;
        public int Units, Delivered;
        public double UnitPrice;
        public int Deadline;
        public TenderLevel Level;
        public bool Closed;
        public int Remaining => Units - Delivered;
    }

    public sealed class SurrogateTender
    {
        public TenderLevel Level;
        public int Product;
        public int Units;
        public double MaxUnitPrice;
        public int DeliveryMonths;
    }

    // Суррогатный мир (GDD 8.2): три компании-архетипа, месяц за шаг. Формулы рынка, тендеров, репутации
    // и финансов — те же, что в полном мире (Formulas); цех — откалиброванные формулы SurrogateFactory.
    // Упрощения: без событий ИИ-ядра, без кредитов (только овердрафт), персонал по нормативу бота
    // (оператор на станок, разнорабочий на 3 станка, наладчик, контролёр и менеджеры по архетипу).
    // Награда за месяц — прирост капитализации / стартовый капитал; банкротство — штраф −1.
    public sealed class SurrogateWorld
    {
        public const int EpisodeMonths = 36;
        private const double DaysPerMonth = 30.4;
        private readonly DataCatalog catalog;
        private readonly SurrogateSettings model;
        private readonly SimRandom random;
        private readonly List<ProductDef> products;
        private readonly double rawQualityCheap, rawQualityGood;
        private readonly List<SurrogateTender> openTenders = new List<SurrogateTender>();
        private readonly List<TournamentLevel> nextTournaments = new List<TournamentLevel>();

        public IReadOnlyList<SurrogateCompany> Companies { get; }
        public int Month { get; private set; }
        public double Index { get; private set; }
        public double Inflation { get; private set; }
        public double KeyRate { get; private set; }
        public double PriceLevel { get; private set; } = 1;
        public bool Done => Month >= EpisodeMonths || Companies.All(c => c.Bankrupt);
        public IReadOnlyList<SurrogateTender> OpenTenders => openTenders;

        public SurrogateWorld(DataCatalog catalog, ulong seed, string[] archetypeIds = null)
        {
            this.catalog = catalog;
            model = catalog.Surrogate;
            random = new SimRandom(seed).Fork("surrogate");
            products = catalog.Products.ToList();
            rawQualityCheap = SurrogateFactory.RawQuality(catalog, false);
            rawQualityGood = SurrogateFactory.RawQuality(catalog, true);
            var economy = catalog.Economy;
            Index = economy.InitialIndex;
            Inflation = economy.InitialInflation;
            KeyRate = Formulas.KeyRate(economy.KeyRateNeutral, economy.KeyRateInflationSensitivity, Inflation, economy.InflationTarget,
                economy.KeyRateMin, economy.KeyRateMax);

            archetypeIds ??= ScenarioRunner.ArchetypeIds;
            var companies = new List<SurrogateCompany>();
            for (int i = 0; i < archetypeIds.Length; i++) companies.Add(CreateCompany(ScenarioRunner.CompanyIds[i % 3] + (i >= 3 ? i.ToString() : ""), archetypeIds[i]));
            Companies = companies;
            PublishTenders();
            AnnounceTournaments();
        }

        private int CalendarMonth(int month) => (catalog.Calendar.StartMonth - 1 + month) % 12 + 1;

        private SurrogateCompany CreateCompany(string id, string archetypeId)
        {
            var a = catalog.BotArchetype(archetypeId);
            int n = products.Count;
            var c = new SurrogateCompany
            {
                Id = id, Archetype = a, ArchetypeIndex = archetypeId == "premium" ? 1 : archetypeId == "dumper" ? 2 : 0,
                Cash = catalog.StartingCash, PriceFactor = new double[n], Stock = new double[n], StockQuality = new double[n],
                StockCost = new double[n], HiddenDefects = new double[n], Sold = new double[n], Lost = new double[n], Share = new double[n],
            };
            // Как BotController.Setup: половина капитала на станки поровну на зону, планировка и столы.
            var zones = products.Where(p => p.UnlockedByDefault).SelectMany(p => p.Operations).Select(o => o.Zone).Distinct().ToList();
            double perZone = c.Cash * 0.5 / Math.Max(1, zones.Count);
            foreach (var zone in zones)
            {
                var machine = PickMachine(a, zone, perZone);
                if (machine == null) continue;
                c.Machines.Add(machine);
                c.Cash -= machine.Price;
            }
            c.Cash -= (a.RawRacks + a.FinishedRacks) * catalog.Warehouse.RackPrice + (a.QcStation ? catalog.Floor.QcTablePrice : 0)
                      + a.SalesManagers * catalog.Floor.DeskPrice;
            for (int p = 0; p < n; p++) c.PriceFactor[p] = a.PriceFactor;
            c.QualityMaterialsNow = a.PreferQuality;
            c.LastPlan = 1;
            c.LastOutput = 1;
            return c;
        }

        private MachineDef PickMachine(BotArchetypeDef a, ProductionZone zone, double budget)
        {
            var machines = catalog.MachinesForZone(zone);
            return machines.LastOrDefault(m => m.Tier <= a.MachineTier && m.Price <= budget) ?? machines.FirstOrDefault();
        }

        // ---------- Наблюдения ----------

        public BotSituation Situation(int index)
        {
            var c = Companies[index];
            int rivals = Math.Max(1, Companies.Count(x => !x.Bankrupt));
            var s = new BotSituation
            {
                Archetype = c.ArchetypeIndex,
                CashRatio = c.Cash / catalog.StartingCash,
                Reputation = c.Reputation,
                NetIncomeRatio = c.LastNetIncome / catalog.StartingCash,
                Fulfilment = c.LastOutput / Math.Max(1, c.LastPlan),
                DefectRate = c.LastOutput > 0 ? c.LastDefective / c.LastOutput : 0,
                Machines = c.Machines.Count,
                EconomyIndex = Index,
                NextSeasonality = Formulas.Seasonality(CalendarMonth(Month + 1), catalog.Economy.SeasonalityAmplitude, catalog.Economy.SeasonalityPeakMonth),
                KeyRate = KeyRate,
                Inflation = Inflation,
                CalendarMonth = CalendarMonth(Month),
                CompetitorReputation = Companies.Where(x => x != c).Select(x => x.Reputation).DefaultIfEmpty(50).Average(),
            };
            var eligible = openTenders.Where(t => Eligible(c, t)).ToList();
            s.TenderAvailable = eligible.Count > 0;
            if (eligible.Count > 0)
            {
                var biggest = eligible.OrderByDescending(t => t.Units).First();
                s.TenderUnitsRatio = biggest.Units / (products[biggest.Product].BaseDemandPerMonth / (double)rivals);
            }
            for (int p = 0; p < products.Count && p < BotSituation.MaxProducts; p++)
            {
                var ps = s.Products[p];
                ps.Active = products[p].UnlockedByDefault;
                if (!ps.Active) continue;
                double perCompany = Math.Max(1, products[p].BaseDemandPerMonth / (double)rivals);
                ps.PriceFactor = c.PriceFactor[p];
                ps.MarketPriceFactor = Companies.Where(x => !x.Bankrupt).Select(x => x.PriceFactor[p]).DefaultIfEmpty(1).Average();
                ps.Share = c.Share[p];
                ps.StockRatio = c.Stock[p] / perCompany;
                ps.LostRatio = c.Lost[p] / perCompany;
                ps.CommittedRatio = c.Contracts.Where(k => !k.Closed && k.Product == p).Sum(k => k.Remaining) / perCompany;
            }
            return s;
        }

        // ---------- Шаг: месяц ----------

        public double[] Step(int[][] actions)
        {
            if (Done) throw new InvalidOperationException("Эпизод закончен.");
            int n = Companies.Count;
            var rewards = new double[n];
            var revenue = new double[n];
            var retail = new double[n];
            var costs = new double[n];
            var cogs = new double[n];
            var repGain = new double[n];
            var repLoss = new double[n];
            var plans = new double[n][];
            var output = new double[n];
            var defective = new double[n];

            HoldTournaments(repGain, costs);
            for (int i = 0; i < n; i++)
            {
                var c = Companies[i];
                if (c.Bankrupt) continue;
                var mask = BotActions.Mask(Situation(i));
                var act = BotActions.Sanitize(actions[i], mask);
                ApplyActions(c, act, costs, i);
                plans[i] = Produce(c, costs, out output[i], out defective[i], i);
            }
            for (int i = 0; i < n; i++)
                if (!Companies[i].Bankrupt) ShipContracts(Companies[i], revenue, cogs, costs, repLoss, i);
            SellRetail(revenue, retail, cogs, costs, repLoss);
            AwardTenders();

            for (int i = 0; i < n; i++)
            {
                var c = Companies[i];
                if (c.Bankrupt) continue;
                double fixedCosts = MonthlyFixedCosts(c);
                double depreciation = c.Machines.Sum(m => m.Price) / catalog.Finance.DepreciationMonths;
                double interest = c.Cash < 0 ? -c.Cash * (KeyRate + catalog.Finance.OverdraftMargin) / 12 : 0;
                c.Cash -= fixedCosts + interest;
                double preTax = revenue[i] - cogs[i] - costs[i] - fixedCosts - interest - depreciation;
                double tax = preTax > 0 ? preTax * catalog.Finance.ProfitTaxRate : 0;
                c.Cash -= tax;
                c.LastNetIncome = preTax - tax;
                c.NetIncomes.Add(c.LastNetIncome);
                c.LastRevenue = revenue[i];
                c.LastRetailRevenue = retail[i];
                c.LastOutput = output[i];
                c.LastDefective = defective[i];
                c.LastPlan = Math.Max(1, plans[i].Sum());
                c.Experience = Math.Min(80, c.Experience + 2);

                c.Reputation = Formulas.NextReputation(c.Reputation, repGain[i], repLoss[i], catalog.Marketing.ReputationReversion);

                c.NegativeMonths = c.Cash < 0 ? c.NegativeMonths + 1 : 0;
                double previous = c.Capitalization == 0 ? catalog.StartingCash : c.Capitalization;
                c.Capitalization = CapitalizationOf(c);
                rewards[i] = (c.Capitalization - previous) / catalog.StartingCash;
                if (c.NegativeMonths >= catalog.Finance.NegativeCashMonthsToBankrupt)
                {
                    c.Bankrupt = true;
                    rewards[i] = -1;
                }
            }

            AdvanceEconomy();
            Month++;
            PublishTenders();
            AnnounceTournaments();
            return rewards;
        }

        // ---------- Решения ----------

        private void ApplyActions(SurrogateCompany c, int[] act, double[] costs, int i)
        {
            var a = c.Archetype;
            for (int p = 0; p < products.Count && p < BotSituation.MaxProducts; p++)
                if (products[p].UnlockedByDefault)
                    c.PriceFactor[p] = Formulas.Clamp(c.PriceFactor[p] * (1 + BotActions.PriceStep(act, p)), a.MinPriceFactor, a.MaxPriceFactor);

            double discount = Math.Min(catalog.Marketing.MaxManagerMarketingDiscount, a.SalesManagers * catalog.Marketing.ManagerMarketingDiscount);
            double adBudget = c.LastRetailRevenue * BotActions.AdShare(act);
            Spend(c, costs, i, adBudget * (1 - discount));
            c.AdStock = c.AdStock * catalog.Marketing.AdCarryover + adBudget;

            c.Bids = BotActions.TenderMarkup(act);
            c.QualityMaterialsNow = BotActions.QualityMaterials(act);
            c.PlanFactorNow = BotActions.PlanFactor(act);

            if (BotActions.Expand(act))
            {
                var bottleneck = BottleneckZone(c);
                var machine = PickMachine(a, bottleneck, double.MaxValue);
                if (machine != null && c.Cash > machine.Price * 3 && c.Machines.Count < 40)
                {
                    c.Machines.Add(machine);
                    c.Cash -= machine.Price;
                }
            }

            // Маркетинг по архетипу — как у BotController: сертификация, амбассадор.
            if (a.Certify && c.CertifiedUntil < Month + 3 && c.CertifiedFrom > Month && Month >= 2)
            {
                double cost = catalog.Marketing.CertificationCost * PriceLevel * 2;
                if (c.Cash > cost * 3)
                {
                    Spend(c, costs, i, cost);
                    c.CertifiedFrom = Month + random.NextInt(catalog.Marketing.CertificationMinMonths, catalog.Marketing.CertificationMaxMonths + 1);
                    c.CertifiedUntil = c.CertifiedFrom + catalog.Marketing.CertificateValidityMonths - 1;
                }
            }
            if (a.Ambassadors > 0 && c.Ambassador == null)
            {
                var athlete = catalog.Athletes.Where(x => Companies.All(o => o.Ambassador != x.Id) && x.MonthlyFee * PriceLevel <= Math.Max(1, c.LastRevenue) * 0.03)
                    .OrderByDescending(x => x.Rating).FirstOrDefault();
                if (athlete != null && c.Cash > athlete.MonthlyFee * 6) c.Ambassador = athlete.Id;
            }
            if (c.Ambassador != null) Spend(c, costs, i, catalog.Athlete(c.Ambassador).MonthlyFee * PriceLevel * (1 - discount));
        }

        private static void Spend(SurrogateCompany c, double[] costs, int i, double amount)
        {
            c.Cash -= amount;
            costs[i] += amount;
        }

        // ---------- Цех ----------

        private FactorySample Sample(SurrogateCompany c, double[] plan)
        {
            var s = new FactorySample
            {
                WorkMinutes = catalog.DefaultWorkSchedule.ShiftsPerDay * catalog.DefaultWorkSchedule.ShiftLengthMinutes * 21.7,
                AverageExperience = c.Experience,
                QualityMaterials = c.QualityMaterialsNow,
                QcStation = c.Archetype.QcStation,
            };
            foreach (var m in c.Machines)
            {
                s.ZonePower[(int)m.Zone] += m.Power;
                s.ZoneMachines[(int)m.Zone]++;
                s.ZoneOperators[(int)m.Zone]++;
            }
            for (int p = 0; p < products.Count; p++)
                if (plan[p] > 0) s.Plan[products[p].Id] = plan[p];
            return s;
        }

        private double[] Produce(SurrogateCompany c, double[] costs, out double totalOutput, out double totalDefective, int i)
        {
            int n = products.Count;
            var plan = new double[n];
            int rivals = Math.Max(1, Companies.Count(x => !x.Bankrupt));
            for (int p = 0; p < n; p++)
            {
                if (!products[p].UnlockedByDefault) continue;
                double demand = c.Sold[p] + c.Lost[p];
                if (Month == 0) demand = products[p].BaseDemandPerMonth / (double)rivals;
                double contractNeed = c.Contracts.Where(k => !k.Closed && k.Product == p).Sum(k => k.Remaining / (double)Math.Max(1, k.Deadline - Month + 1));
                plan[p] = Math.Max(10, demand * c.PlanFactorNow - c.Stock[p] + contractNeed);
            }
            // Как BotController.FitToCapacity: план урезается под оценку мощности с запасом 0,75.
            var sample = Sample(c, plan);
            double factor = 1;
            for (int z = 1; z < 5; z++)
            {
                double load = SurrogateFactory.ZoneLoad(sample, catalog, z);
                if (load > 0) factor = Math.Min(factor, sample.ZonePower[z] * sample.WorkMinutes * 0.75 / load);
            }
            for (int p = 0; p < n; p++) plan[p] = Math.Floor(plan[p] * factor);
            sample = Sample(c, plan);

            double share = SurrogateFactory.PlanShare(sample, catalog, model.Utilization, model.PlanFulfilment);
            double rawQuality = c.QualityMaterialsNow ? rawQualityGood : rawQualityCheap;
            double unitQuality = Formulas.UnitQuality(rawQuality, c.Experience, 85);
            totalOutput = 0;
            totalDefective = 0;
            for (int p = 0; p < n; p++)
            {
                double units = plan[p] * share;
                if (units <= 0) continue;
                var one = new FactorySample { AverageExperience = c.Experience, QualityMaterials = c.QualityMaterialsNow };
                one.Plan[products[p].Id] = units;
                double bad = SurrogateFactory.PredictDefective(one, units, catalog, model.DefectMultiplier);
                double materials = units * MaterialCost(products[p], c.QualityMaterialsNow);
                c.Cash -= materials; // в расходы попадёт при продаже — через себестоимость

                // ОТК находит 90% брака; ремонт возвращает его в годные, иначе — списание. Остальное — скрытый брак.
                double detected = c.Archetype.QcStation ? bad * catalog.Quality.OutputQcDetection : 0;
                double hidden = bad - detected;
                bool repair = c.Archetype.DefectPolicy == Factory.DefectPolicy.Repair;
                double good = units - bad + (repair ? detected : 0);
                double toStock = good + hidden;
                double unitCost = materials / units;
                if (toStock > 0)
                {
                    double total = c.Stock[p] + toStock;
                    c.StockQuality[p] = (c.StockQuality[p] * c.Stock[p] + unitQuality * toStock) / total;
                    c.StockCost[p] = (c.StockCost[p] * c.Stock[p] + unitCost * toStock) / total;
                    c.Stock[p] = total;
                    c.HiddenDefects[p] += hidden;
                }
                totalOutput += units;
                totalDefective += bad;
            }
            sample = Sample(c, plan);
            double energy = model.EnergyPerMinute * SurrogateFactory.OutputMinutes(sample, totalOutput, catalog) * PriceLevel;
            double maintenance = model.MaintenancePerUnit * totalOutput * PriceLevel;
            Spend(c, costs, i, energy + maintenance);
            return plan;
        }

        private double MaterialCost(ProductDef product, bool quality)
        {
            double cost = 0;
            foreach (var input in product.Operations.SelectMany(o => o.Inputs))
                cost += input.Amount * catalog.Material(input.MaterialId).BasePrice;
            return cost * PriceLevel * (quality ? 1.1 : 0.95);
        }

        private ProductionZone BottleneckZone(SurrogateCompany c)
        {
            var plan = new double[products.Count];
            for (int p = 0; p < plan.Length; p++) plan[p] = products[p].UnlockedByDefault ? Math.Max(1, c.Sold[p] + c.Lost[p]) : 0;
            var sample = Sample(c, plan);
            var best = ProductionZone.Cutting;
            double worst = -1;
            for (int z = 1; z < 5; z++)
            {
                double load = SurrogateFactory.ZoneLoad(sample, catalog, z);
                if (load <= 0) continue;
                double pressure = load / Math.Max(1, sample.ZonePower[z] * sample.WorkMinutes);
                if (pressure > worst) { worst = pressure; best = (ProductionZone)z; }
            }
            return best;
        }

        // Зарплаты по нормативу бота, соцналог, аренда.
        private double MonthlyFixedCosts(SurrogateCompany c)
        {
            var a = c.Archetype;
            var staff = catalog.Staff;
            double wageFactor = (0.8 + c.Experience * staff.WageExperienceFactor) * PriceLevel;
            double wages = 0;
            foreach (var m in c.Machines) wages += (catalog.OperatorOf(m.Zone)?.BaseMonthlyWage ?? 0) * wageFactor;
            wages += Math.Max(1, Math.Ceiling(c.Machines.Count / 3.0)) * catalog.Profession(staff.LaborerProfessionId).BaseMonthlyWage * wageFactor;
            if (a.Mechanic) wages += catalog.Profession(staff.MechanicProfessionId).BaseMonthlyWage * wageFactor;
            if (a.QcStation) wages += catalog.Profession(staff.QcInspectorProfessionId).BaseMonthlyWage * wageFactor;
            wages += a.SalesManagers * catalog.Profession(staff.SalesManagerProfessionId).BaseMonthlyWage * wageFactor;
            double rent = catalog.Warehouse.DailyRent * DaysPerMonth;
            return wages * (1 + catalog.Finance.SocialTaxRate) + rent;
        }

        // ---------- Сбыт ----------

        private void ShipContracts(SurrogateCompany c, double[] revenue, double[] cogs, double[] costs, double[] repLoss, int i)
        {
            var settings = catalog.Tenders;
            foreach (var k in c.Contracts.Where(k => !k.Closed))
            {
                int monthsLeft = k.Deadline - Month + 1;
                double quota = monthsLeft > 0 ? Math.Ceiling(k.Remaining / (double)monthsLeft) : k.Remaining;
                double units = Math.Floor(Math.Min(quota, c.Stock[k.Product]));
                if (units > 0)
                {
                    double defects = Take(c, k.Product, units, out double cost);
                    revenue[i] += units * k.UnitPrice;
                    cogs[i] += cost;
                    c.Cash += units * k.UnitPrice;
                    c.B2bBatches++;
                    if (defects > 0.5)
                    {
                        Spend(c, costs, i, defects * (k.UnitPrice + catalog.Quality.ReturnFee));
                        c.B2bComplaints++;
                        repLoss[i] += settings.ComplaintReputationLoss;
                    }
                    k.Delivered += (int)Math.Round(units - defects);
                }
                if (k.Remaining <= 0) { k.Closed = true; continue; }
                if (Month > k.Deadline)
                {
                    Spend(c, costs, i, k.Units * k.UnitPrice * settings.LatePenaltyPerWeek * 4.33);
                    if (Month > k.Deadline + 1)
                    {
                        k.Closed = true;
                        repLoss[i] += settings.TerminationReputationLoss;
                    }
                }
            }
        }

        // Забрать единицы со склада; вернуть, сколько среди них скрытого брака.
        private static double Take(SurrogateCompany c, int p, double units, out double cost)
        {
            double hiddenShare = c.Stock[p] > 0 ? c.HiddenDefects[p] / c.Stock[p] : 0;
            double defects = units * hiddenShare;
            c.Stock[p] -= units;
            c.HiddenDefects[p] = Math.Max(0, c.HiddenDefects[p] - defects);
            cost = units * c.StockCost[p];
            return defects;
        }

        private void SellRetail(double[] revenue, double[] retail, double[] cogs, double[] costs, double[] repLoss)
        {
            var market = catalog.Market;
            var economy = catalog.Economy;
            int n = Companies.Count;
            double seasonality = Formulas.Seasonality(CalendarMonth(Month), economy.SeasonalityAmplitude, economy.SeasonalityPeakMonth);
            double elasticity = Formulas.PriceElasticity(market.PriceElasticity, economy.ElasticityCrisisSensitivity, Index);
            bool bigTournament = nextTournaments.Any(t => t >= TournamentLevel.National);
            var returnedUnits = new double[n];
            var soldUnits = new double[n];
            foreach (var c in Companies) { Array.Clear(c.Sold, 0, c.Sold.Length); Array.Clear(c.Lost, 0, c.Lost.Length); Array.Clear(c.Share, 0, c.Share.Length); }

            for (int p = 0; p < products.Count; p++)
            {
                if (!products[p].UnlockedByDefault) continue;
                var active = Enumerable.Range(0, n).Where(i => !Companies[i].Bankrupt).ToList();
                if (active.Count == 0) return;
                double reference = products[p].BasePrice * PriceLevel;
                double demand = Formulas.MonthlyDemand(products[p].BaseDemandPerMonth, seasonality, 1, Index);
                double averagePrice = active.Average(i => Companies[i].PriceFactor[p] * reference);
                double size = Formulas.MarketSize(demand, reference, averagePrice, market.MarketSizeElasticity);

                var attractiveness = new double[n];
                foreach (int i in active)
                {
                    var c = Companies[i];
                    double quality = c.Stock[p] >= 1 ? c.StockQuality[p] : 0;
                    double k = Formulas.AdMultiplier(c.AdStock, catalog.Marketing.AdBudgetScale * PriceLevel, catalog.Marketing.AdMaxBoost);
                    if (bigTournament && c.Certified(Month)) k *= 1 + catalog.Marketing.CertifiedTournamentBonus;
                    attractiveness[i] = Formulas.Attractiveness(quality, c.Reputation, c.PriceFactor[p] * reference, reference,
                        market.QualityWeight, market.ReputationWeight, elasticity) * k;
                }
                double total = attractiveness.Sum();
                if (total <= 0) continue;

                var sales = new double[n];
                double unmet = 0;
                foreach (int i in active)
                {
                    double wanted = size * attractiveness[i] / total;
                    sales[i] = Math.Min(wanted, Math.Floor(Companies[i].Stock[p]));
                    Companies[i].Lost[p] = wanted - sales[i];
                    unmet += wanted - sales[i];
                }
                double remaining = active.Where(i => Companies[i].Stock[p] - sales[i] >= 1).Sum(i => attractiveness[i]);
                if (unmet > 0 && remaining > 0)
                    foreach (int i in active.Where(i => Companies[i].Stock[p] - sales[i] >= 1))
                        sales[i] += Math.Min(Math.Floor(Companies[i].Stock[p] - sales[i]), unmet * attractiveness[i] / remaining);

                double soldTotal = sales.Sum();
                foreach (int i in active)
                {
                    var c = Companies[i];
                    double units = Math.Floor(sales[i]);
                    if (units <= 0) continue;
                    double price = c.PriceFactor[p] * reference;
                    double defects = Take(c, p, units, out double cost);
                    revenue[i] += units * price;
                    retail[i] += units * price;
                    cogs[i] += cost;
                    c.Cash += units * price;
                    if (defects > 0) Spend(c, costs, i, defects * (price + catalog.Quality.ReturnFee));
                    c.Sold[p] = units;
                    c.Share[p] = soldTotal > 0 ? units / soldTotal : 0;
                    returnedUnits[i] += defects;
                    soldUnits[i] += units;
                }
            }
            for (int i = 0; i < n; i++)
                if (returnedUnits[i] > 0)
                    repLoss[i] += returnedUnits[i] * 100 / Math.Max(1, soldUnits[i]) * catalog.Marketing.ReturnReputationPerPercent;
        }

        // ---------- Тендеры ----------

        private bool Eligible(SurrogateCompany c, SurrogateTender t)
        {
            var level = catalog.Tenders.Levels.First(l => l.Level == t.Level);
            return !c.Bankrupt && c.Reputation >= level.MinReputation && (!level.CertificateRequired || c.Certified(Month));
        }

        private void PublishTenders()
        {
            openTenders.Clear();
            var settings = catalog.Tenders;
            var unlocked = Enumerable.Range(0, products.Count).Where(p => products[p].UnlockedByDefault).ToList();
            int count = random.NextInt(settings.MinTendersPerMonth, settings.MaxTendersPerMonth + 1);
            double totalWeight = settings.Levels.Sum(l => l.Weight);
            for (int i = 0; i < count; i++)
            {
                double roll = random.NextDouble() * totalWeight;
                int product = unlocked[random.NextInt(0, unlocked.Count)];
                double unitsRoll = random.NextDouble(), deliveryRoll = random.NextDouble();
                var level = settings.Levels[settings.Levels.Count - 1];
                foreach (var l in settings.Levels)
                {
                    if (roll < l.Weight) { level = l; break; }
                    roll -= l.Weight;
                }
                double scale = Formulas.Clamp(products[product].BaseDemandPerMonth / settings.ReferenceDemand, 0.25, 1.5);
                openTenders.Add(new SurrogateTender
                {
                    Level = level.Level, Product = product,
                    Units = Math.Max(10, (int)Math.Round((level.MinUnits + unitsRoll * (level.MaxUnits - level.MinUnits)) * scale)),
                    DeliveryMonths = Math.Min(level.MaxDeliveryMonths, level.MinDeliveryMonths + (int)Math.Floor(deliveryRoll * (level.MaxDeliveryMonths - level.MinDeliveryMonths + 1))),
                    MaxUnitPrice = products[product].BasePrice * PriceLevel * level.MaxPriceShare,
                });
            }
        }

        private void AwardTenders()
        {
            var settings = catalog.Tenders;
            foreach (var t in openTenders)
            {
                var level = catalog.Tenders.Levels.First(l => l.Level == t.Level);
                SurrogateCompany best = null;
                double bestScore = double.MinValue, bestPrice = 0;
                foreach (var c in Companies)
                {
                    if (c.Bids <= 0 || !Eligible(c, t) || !HasRoom(c, t)) continue;
                    double price = Math.Max(1, Math.Round(t.MaxUnitPrice * c.Bids));
                    double bonus = (c.Certified(Month) ? level.CertificateBonus : 0)
                                   + (c.SponsorBonusUntil >= Month && t.Level >= TenderLevel.Regional ? settings.SponsorshipScoreBonus : 0);
                    double score = Formulas.TenderScore((long)Math.Round(t.MaxUnitPrice), (long)price, c.Reputation, c.QualityHistory,
                        c.Archetype.SalesManagers * settings.ManagerBonusPerManager, settings.WeightPrice, settings.WeightReputation,
                        settings.WeightQualityHistory, settings.WeightManagers, bonus);
                    if (score > bestScore) { bestScore = score; best = c; bestPrice = price; }
                }
                if (best == null) continue;
                best.Contracts.Add(new SurrogateContract
                {
                    Product = t.Product, Units = t.Units, UnitPrice = bestPrice, Deadline = Month + t.DeliveryMonths, Level = t.Level,
                });
            }
        }

        // Как BotController.HasRoomFor: контракты не больше доли мощности TenderAppetite.
        private bool HasRoom(SurrogateCompany c, SurrogateTender t)
        {
            var plan = new double[products.Count];
            foreach (var k in c.Contracts.Where(k => !k.Closed)) plan[k.Product] += k.Remaining / (double)Math.Max(1, k.Deadline - Month + 1);
            plan[t.Product] += t.Units / (double)Math.Max(1, t.DeliveryMonths);
            var sample = Sample(c, plan);
            for (int z = 1; z < 5; z++)
            {
                double load = SurrogateFactory.ZoneLoad(sample, catalog, z);
                if (load > c.Archetype.TenderAppetite * sample.ZonePower[z] * sample.WorkMinutes * 0.75) return false;
            }
            return true;
        }

        // ---------- Турниры: спонсорство «Премиума» и победы амбассадоров ----------

        private void AnnounceTournaments()
        {
            nextTournaments.Clear();
            var m = catalog.Marketing;
            if (random.Chance(m.RegionalTournamentChance)) nextTournaments.Add(TournamentLevel.Regional);
            if (random.Chance(m.NationalTournamentChance)) nextTournaments.Add(TournamentLevel.National);
            if (random.Chance(m.WorldTournamentChance)) nextTournaments.Add(TournamentLevel.World);
        }

        private void HoldTournaments(double[] repGain, double[] costs)
        {
            var m = catalog.Marketing;
            foreach (var level in nextTournaments)
            {
                for (int i = 0; i < Companies.Count; i++)
                {
                    var c = Companies[i];
                    if (c.Bankrupt) continue;
                    if (c.Archetype.SponsorUpTo >= level)
                    {
                        double cost = (level == TournamentLevel.World ? m.WorldSponsorshipCost : level == TournamentLevel.National ? m.NationalSponsorshipCost : m.RegionalSponsorshipCost) * PriceLevel;
                        if (c.Cash > cost * 5)
                        {
                            Spend(c, costs, i, cost);
                            repGain[i] += level == TournamentLevel.World ? m.WorldSponsorshipReputation
                                : level == TournamentLevel.National ? m.NationalSponsorshipReputation : m.RegionalSponsorshipReputation;
                            c.SponsorBonusUntil = Month + catalog.Tenders.SponsorshipBonusMonths - 1;
                        }
                    }
                    if (c.Ambassador == null) continue;
                    double exponent = level == TournamentLevel.World ? m.WorldWinExponent : level == TournamentLevel.National ? m.NationalWinExponent : m.RegionalWinExponent;
                    if (random.Chance(Formulas.WinChance(catalog.Athlete(c.Ambassador).Rating, m.WinChanceScale, exponent)))
                        repGain[i] += c.Reputation * random.Range(m.AmbassadorWinGainMin, m.AmbassadorWinGainMax);
                    if (random.Chance(m.ScandalChance / Math.Max(1, nextTournaments.Count)))
                    {
                        repGain[i] -= m.ScandalReputationLoss;
                        c.Ambassador = null;
                    }
                }
            }
        }

        // ---------- Экономика и капитализация ----------

        private void AdvanceEconomy()
        {
            var e = catalog.Economy;
            Index = Formulas.NextEconomyIndex(Index, e.IndexReversion, e.IndexVolatility, random.NextGaussian(), e.IndexMin, e.IndexMax);
            Inflation = Formulas.Clamp(Inflation + e.InflationReversion * (e.InflationTarget - Inflation) + e.InflationIndexSensitivity * (Index - 1)
                                       + e.InflationVolatility * random.NextGaussian(), -0.05, 0.5);
            KeyRate = Formulas.KeyRate(e.KeyRateNeutral, e.KeyRateInflationSensitivity, Inflation, e.InflationTarget, e.KeyRateMin, e.KeyRateMax);
            PriceLevel *= 1 + Inflation / 12;
        }

        private double CapitalizationOf(SurrogateCompany c)
        {
            int age = Month + 1;
            long machines = c.Machines.Sum(m => Formulas.ResidualValue(m.Price, age, catalog.Finance.DepreciationMonths));
            double inventory = 0;
            for (int p = 0; p < products.Count; p++) inventory += c.Stock[p] * c.StockCost[p];
            double average = c.NetIncomes.Skip(Math.Max(0, c.NetIncomes.Count - 6)).DefaultIfEmpty(0).Average();
            return Formulas.Capitalization((long)c.Cash, machines, 0, (long)inventory, 0, average,
                catalog.Economy.CapitalizationMultiplierMonths * Index, c.Reputation);
        }
    }
}
