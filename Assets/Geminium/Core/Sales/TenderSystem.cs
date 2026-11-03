using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core.Sales
{
    // B2B-тендеры (GDD 7.2).
    // Начало месяца: ИИ-ядро публикует 1–3 тендера, компании подают цену до конца месяца.
    // Конец месяца: побеждает наибольший балл среди заявок, прошедших требования; победитель получает контракт.
    // Рабочие дни: поставки по контрактам — равномерно до срока, после срока — всё, что есть на складе.
    // Контракты отгружаются раньше розницы (система стоит до рынка). По понедельникам — неустойки за просрочку.
    public sealed class TenderSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly TenderSettings settings;
        private readonly WorkSchedule shippingDays;
        private readonly List<Tender> tenders = new List<Tender>();
        private readonly List<Tender> open = new List<Tender>();
        private int nextTenderId = 1;
        private int nextContractId = 1;
        private SimRandom random;

        public IReadOnlyList<Tender> Tenders => tenders;   // все опубликованные
        public IReadOnlyList<Tender> OpenTenders => open;  // приём заявок в этом месяце

        public TenderSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Tenders;
            shippingDays = new WorkSchedule(new WorkScheduleSettings { WorkingDaysPerWeek = 5, ShiftsPerDay = 1 });
        }

        protected override void OnAttached() => random = Simulation.Random.Fork("tenders");

        public Tender FindTender(int tenderId) => tenders.FirstOrDefault(t => t.Id == tenderId);

        public TenderLevelDef LevelDef(TenderLevel level) => settings.Levels.First(l => l.Level == level);

        // ---------- Публикация ----------

        protected internal override void OnMonthStarted(int monthIndex)
        {
            open.Clear();
            var products = catalog.Products.Where(p => p.UnlockedByDefault).ToList();
            int count = random.NextInt(settings.MinTendersPerMonth, settings.MaxTendersPerMonth + 1);
            double totalWeight = settings.Levels.Sum(l => l.Weight);
            for (int i = 0; i < count; i++)
            {
                // Чисел из генератора всегда одинаково, чтобы история тендеров не зависела от решений компаний.
                double levelRoll = random.NextDouble() * totalWeight;
                int productIndex = random.NextInt(0, Math.Max(1, products.Count));
                double unitsRoll = random.NextDouble();
                double deliveryRoll = random.NextDouble();
                if (products.Count == 0) continue;

                var level = settings.Levels[settings.Levels.Count - 1];
                foreach (var l in settings.Levels)
                {
                    if (levelRoll < l.Weight) { level = l; break; }
                    levelRoll -= l.Weight;
                }
                var product = products[productIndex];
                double scale = Formulas.Clamp(product.BaseDemandPerMonth / settings.ReferenceDemand, 0.25, 1.5);
                int units = Math.Max(10, (int)Math.Round((level.MinUnits + unitsRoll * (level.MaxUnits - level.MinUnits)) * scale));
                int delivery = level.MinDeliveryMonths + (int)Math.Floor(deliveryRoll * (level.MaxDeliveryMonths - level.MinDeliveryMonths + 1));
                var tender = new Tender
                {
                    Id = nextTenderId++, Level = level.Level, ProductId = product.Id, Units = units, PublishedMonth = monthIndex,
                    DeliveryMonths = Math.Min(delivery, level.MaxDeliveryMonths),
                    MaxUnitPrice = (long)Math.Round(product.BasePrice * world.Economy.PriceLevel * level.MaxPriceShare),
                };
                tenders.Add(tender);
                open.Add(tender);
            }
        }

        // ---------- Заявки ----------

        // null — заявку можно подать; иначе — почему нельзя.
        public string CheckBid(Company company, int tenderId, long unitPrice)
        {
            var tender = open.FirstOrDefault(t => t.Id == tenderId);
            if (tender == null) return $"Тендер №{tenderId} не принимает заявки.";
            if (unitPrice <= 0) return "Цена заявки должна быть больше нуля.";
            if (unitPrice > tender.MaxUnitPrice) return $"Цена выше предельной: не больше {tender.MaxUnitPrice:N0} сум за единицу.";
            return CheckEligibility(company, tender);
        }

        // Требования уровня: репутация и сертификат.
        public string CheckEligibility(Company company, Tender tender)
        {
            var level = LevelDef(tender.Level);
            if (company.Reputation < level.MinReputation)
                return $"Нужна репутация не ниже {level.MinReputation:0}, у компании {company.Reputation:0}.";
            if (level.CertificateRequired && !company.Sales.HasValidCertificate(tender.ProductId, Simulation.MonthIndex))
                return $"Нужен сертификат на «{tender.ProductId}».";
            return null;
        }

        internal void PlaceBid(Company company, int tenderId, long unitPrice)
        {
            var tender = open.First(t => t.Id == tenderId);
            var bid = tender.Bids.FirstOrDefault(b => b.CompanyId == company.Id);
            if (bid == null) tender.Bids.Add(new TenderBid { CompanyId = company.Id, UnitPrice = unitPrice });
            else bid.UnitPrice = unitPrice;
        }

        // Балл заявки компании по формуле GDD 7.2.
        public double Score(Company company, Tender tender, long unitPrice)
        {
            int month = Simulation.MonthIndex;
            var level = LevelDef(tender.Level);
            double bonus = 0;
            if (company.Sales.HasValidCertificate(tender.ProductId, month)) bonus += level.CertificateBonus;
            if (company.Sales.HasSponsorshipBonus(tender.Level, month)) bonus += settings.SponsorshipScoreBonus;
            double managers = world.Marketing.EffectiveManagers(company) * settings.ManagerBonusPerManager;
            return Formulas.TenderScore(tender.MaxUnitPrice, unitPrice, company.Reputation, company.Sales.QualityHistory, managers,
                settings.WeightPrice, settings.WeightReputation, settings.WeightQualityHistory, settings.WeightManagers, bonus);
        }

        // ---------- Итоги месяца ----------

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var tender in open) Award(tender, monthIndex);
            open.Clear();

            // Репутация ниже порога: федерации расторгают свои контракты.
            foreach (var company in world.Companies)
            {
                if (company.Reputation >= settings.FederationMinReputation) continue;
                foreach (var contract in company.Sales.contracts)
                    if (contract.Status == ContractStatus.Active && contract.Level >= TenderLevel.Regional)
                        Terminate(company, contract, 0);
            }
        }

        private void Award(Tender tender, int monthIndex)
        {
            tender.Closed = true;
            TenderBid best = null;
            foreach (var bid in tender.Bids)
            {
                var company = world.FindCompany(bid.CompanyId);
                bid.Eligible = company != null && !company.IsBankrupt && CheckEligibility(company, tender) == null;
                bid.Score = bid.Eligible ? Score(company, tender, bid.UnitPrice) : 0;
                if (!bid.Eligible) continue;
                if (best == null || bid.Score > best.Score + 1e-12 || (Math.Abs(bid.Score - best.Score) <= 1e-12 && bid.UnitPrice < best.UnitPrice))
                    best = bid;
            }
            if (best == null) return;

            tender.WinnerId = best.CompanyId;
            var winner = world.FindCompany(best.CompanyId);
            winner.Sales.contracts.Add(new Contract
            {
                Id = nextContractId++, TenderId = tender.Id, Level = tender.Level, ProductId = tender.ProductId, Units = tender.Units,
                UnitPrice = best.UnitPrice, AwardedMonth = monthIndex, DeadlineMonth = monthIndex + tender.DeliveryMonths,
            });
            winner.CurrentMonth.TendersWon++;
        }

        private void Terminate(Company company, Contract contract, double reputationLoss)
        {
            contract.Status = ContractStatus.Terminated;
            company.CurrentMonth.ContractsTerminated++;
            company.CurrentMonth.ReputationLoss += reputationLoss;
        }

        // ---------- Поставки и неустойки ----------

        protected internal override void OnDayStarted(long dayIndex)
        {
            DateTime date = Simulation.Calendar.StartDate.AddDays(dayIndex);
            if (date.DayOfWeek != DayOfWeek.Monday) return;
            int month = Simulation.MonthIndex;
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                foreach (var contract in company.Sales.contracts)
                {
                    if (contract.Status != ContractStatus.Active || month <= contract.DeadlineMonth) continue;
                    contract.WeeksLate++;
                    if (contract.WeeksLate > settings.MaxWeeksLate)
                    {
                        Terminate(company, contract, settings.TerminationReputationLoss);
                        continue;
                    }
                    company.Pay(LedgerCategory.Penalties, (long)Math.Round(contract.Value * settings.LatePenaltyPerWeek));
                }
            }
        }

        protected internal override void OnDayEnded(long dayIndex)
        {
            DateTime date = Simulation.Calendar.StartDate.AddDays(dayIndex);
            if (!shippingDays.IsWorkingDay(date.DayOfWeek)) return;
            int month = Simulation.MonthIndex;
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                foreach (var contract in company.Sales.contracts)
                    if (contract.Status == ContractStatus.Active && contract.AwardedMonth < month) Ship(company, contract, date, month);
            }
        }

        // Отгрузка за день: до срока — ровными долями по оставшимся рабочим дням, после срока — всё, что есть.
        private void Ship(Company company, Contract contract, DateTime today, int month)
        {
            long stock = company.Inventory.ProductUnits(contract.ProductId);
            if (stock <= 0) return;
            long quota = contract.Remaining;
            if (month <= contract.DeadlineMonth)
            {
                int daysLeft = WorkingDaysUntilDeadline(today, contract.DeadlineMonth);
                quota = (long)Math.Ceiling(contract.Remaining / (double)Math.Max(1, daysLeft));
            }
            long units = Math.Min(quota, stock);
            if (units <= 0) return;

            company.Inventory.TryTakeProducts(contract.ProductId, units, out _, out double unitCost, out long defects);
            company.Receive(LedgerCategory.SalesContracts, units * contract.UnitPrice);
            company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(units * unitCost);
            company.CurrentMonth.ContractUnits += units;
            company.Sales.B2bBatches++;
            if (defects > 0)
            {
                // Рекламация: брак возвращается, деньги и экспертиза — заказчику, годные единицы ещё нужно довезти.
                company.Pay(LedgerCategory.Returns, defects * (contract.UnitPrice + catalog.Quality.ReturnFee));
                company.CurrentMonth.ReturnedUnits += defects;
                company.CurrentMonth.ContractReturnedUnits += defects;
                company.CurrentMonth.B2bComplaints++;
                company.CurrentMonth.ReputationLoss += settings.ComplaintReputationLoss;
                company.Sales.B2bComplaints++;
            }
            contract.Delivered += (int)(units - defects);
            if (contract.Remaining <= 0)
            {
                contract.Status = ContractStatus.Completed;
                company.CurrentMonth.ContractsCompleted++;
            }
        }

        private int WorkingDaysUntilDeadline(DateTime today, int deadlineMonth)
        {
            DateTime end = Simulation.Calendar.StartDate.AddMonths(deadlineMonth + 1);
            int days = 0;
            for (DateTime d = today; d < end; d = d.AddDays(1))
                if (shippingDays.IsWorkingDay(d.DayOfWeek)) days++;
            return days;
        }
    }
}
