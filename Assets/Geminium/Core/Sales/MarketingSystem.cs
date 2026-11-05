using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;

namespace Geminium.Core.Sales
{
    // Маркетинг, турниры, сертификация и репутация (GDD 7.3).
    // Начало месяца: проходят объявленные турниры (спонсоры и победы амбассадоров поднимают репутацию),
    // объявляются турниры следующего месяца, списываются реклама и гонорары амбассадоров, бывают скандалы,
    // заканчиваются проверки сертификации.
    // Конец месяца: репутация R(t+1) = R + прибавки − потери + λ·(50 − R); потери — рекламации, возвраты,
    // расторжения, скандалы.
    public sealed class MarketingSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly MarketingSettings settings;
        private readonly List<Tournament> tournaments = new List<Tournament>();
        private readonly Dictionary<string, string> ambassadorOwners = new Dictionary<string, string>(); // спортсмен → компания
        private int nextTournamentId = 1;
        private SimRandom random;
        private SimRandom certificationRandom;

        public IReadOnlyList<Tournament> Tournaments => tournaments;

        public MarketingSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Marketing;
        }

        private EconomySystem Economy => world.Economy;

        protected override void OnAttached()
        {
            random = Simulation.Random.Fork("marketing");
            certificationRandom = Simulation.Random.Fork("marketing.certification");
        }

        // Турниры, объявленные на будущие месяцы (их можно спонсировать).
        public IEnumerable<Tournament> UpcomingTournaments => tournaments.Where(t => t.Month > Simulation.MonthIndex);

        public string AmbassadorOwner(string athleteId) => ambassadorOwners.TryGetValue(athleteId, out string owner) ? owner : null;

        // Менеджеры по продажам на работе, у которых есть стол (GDD 7.2, 7.7).
        public int EffectiveManagers(Company company)
        {
            int managers = company.Workers.Count(w => w.ProfessionId == catalog.Staff.SalesManagerProfessionId && !w.AbsentToday);
            int desks = company.Floor.ObjectsOfKind(Factory.FloorObjectKind.Desk).Count();
            return Math.Min(managers, desks);
        }

        // Скидка на маркетинг от менеджеров по продажам.
        public double MarketingDiscount(Company company) =>
            Math.Min(settings.MaxManagerMarketingDiscount, EffectiveManagers(company) * settings.ManagerMarketingDiscount);

        // Цена маркетинговой услуги для компании с учётом инфляции и скидки менеджеров.
        public long MarketingCost(Company company, long baseCost) =>
            (long)Math.Round(baseCost * Economy.PriceLevel * (1 - MarketingDiscount(company)));

        public long SponsorshipCost(TournamentLevel level) =>
            level == TournamentLevel.World ? settings.WorldSponsorshipCost
            : level == TournamentLevel.National ? settings.NationalSponsorshipCost : settings.RegionalSponsorshipCost;

        // Множитель спроса B2C компании на продукт: реклама и бонус сертификата в месяцы крупных турниров.
        public double DemandMultiplier(Company company, string productId)
        {
            double k = Formulas.AdMultiplier(company.Sales.AdStock, settings.AdBudgetScale * Economy.PriceLevel, settings.AdMaxBoost);
            int month = Simulation.MonthIndex;
            bool bigTournament = tournaments.Any(t => t.Level >= TournamentLevel.National && (t.Month == month || t.Month == month + 1));
            if (bigTournament && company.Sales.HasValidCertificate(productId, month)) k *= 1 + settings.CertifiedTournamentBonus;
            return k;
        }

        // ---------- Начало месяца ----------

        protected internal override void OnMonthStarted(int monthIndex)
        {
            foreach (var tournament in tournaments.Where(t => t.Month == monthIndex).ToList()) Hold(tournament, monthIndex);
            Announce(monthIndex + 1);
            ChargeAmbassadors();
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                var sales = company.Sales;
                company.Pay(LedgerCategory.Marketing, (long)Math.Round(sales.AdBudget * (1 - MarketingDiscount(company))));
                sales.AdStock = sales.AdStock * settings.AdCarryover + sales.AdBudget;
                FinishCertifications(company, monthIndex);
            }
        }

        private void Announce(int month)
        {
            // Броски — всегда все три, чтобы история турниров не зависела от решений компаний.
            bool regional = random.Chance(settings.RegionalTournamentChance);
            bool national = random.Chance(settings.NationalTournamentChance);
            bool worldCup = random.Chance(settings.WorldTournamentChance);
            if (regional) tournaments.Add(new Tournament { Id = nextTournamentId++, Level = TournamentLevel.Regional, Month = month });
            if (national) tournaments.Add(new Tournament { Id = nextTournamentId++, Level = TournamentLevel.National, Month = month });
            if (worldCup) tournaments.Add(new Tournament { Id = nextTournamentId++, Level = TournamentLevel.World, Month = month });
        }

        private void Hold(Tournament tournament, int month)
        {
            foreach (string companyId in tournament.Sponsors)
            {
                var company = world.FindCompany(companyId);
                if (company == null || company.IsBankrupt) continue;
                company.CurrentMonth.ReputationGain += SponsorshipReputation(tournament.Level);
                foreach (var level in BonusLevels(tournament.Level))
                    company.Sales.sponsorshipBonusUntil[level] = month + world.Catalog.Tenders.SponsorshipBonusMonths - 1;
            }

            // Выступают все спортсмены; чемпион — самый рейтинговый из победивших в своём забеге.
            double exponent = tournament.Level == TournamentLevel.World ? settings.WorldWinExponent
                : tournament.Level == TournamentLevel.National ? settings.NationalWinExponent : settings.RegionalWinExponent;
            AthleteDef champion = null;
            foreach (var athlete in catalog.Athletes)
            {
                bool won = random.Chance(Formulas.WinChance(athlete.Rating, settings.WinChanceScale, exponent));
                if (won && (champion == null || athlete.Rating > champion.Rating)) champion = athlete;
            }
            double gainShare = random.Range(settings.AmbassadorWinGainMin, settings.AmbassadorWinGainMax);
            if (champion == null) return;
            tournament.WinnerAthleteId = champion.Id;
            var owner = world.FindCompany(AmbassadorOwner(champion.Id) ?? "");
            if (owner != null && !owner.IsBankrupt) owner.CurrentMonth.ReputationGain += owner.Reputation * gainShare;
        }

        private double SponsorshipReputation(TournamentLevel level) =>
            level == TournamentLevel.World ? settings.WorldSponsorshipReputation
            : level == TournamentLevel.National ? settings.NationalSponsorshipReputation : settings.RegionalSponsorshipReputation;

        // Спонсор турнира получает бонус в тендерах федераций этого уровня (национальный и мировой — и в региональных).
        private static IEnumerable<TenderLevel> BonusLevels(TournamentLevel level)
        {
            yield return TenderLevel.Regional;
            if (level >= TournamentLevel.National) yield return TenderLevel.National;
        }

        private void ChargeAmbassadors()
        {
            foreach (var athlete in catalog.Athletes)
            {
                bool scandal = random.Chance(settings.ScandalChance);
                var owner = world.FindCompany(AmbassadorOwner(athlete.Id) ?? "");
                if (owner == null) continue;
                if (owner.IsBankrupt)
                {
                    Release(owner, athlete.Id);
                    continue;
                }
                owner.Pay(LedgerCategory.Marketing, MarketingCost(owner, athlete.MonthlyFee));
                if (!scandal) continue;
                owner.CurrentMonth.ReputationLoss += settings.ScandalReputationLoss;
                owner.CurrentMonth.Scandals++;
                Release(owner, athlete.Id);
            }
        }

        private void FinishCertifications(Company company, int month)
        {
            foreach (var certificate in company.Sales.certificates)
            {
                if (certificate.Checked || certificate.ReadyMonth > month) continue;
                certificate.Checked = true;
                // Тестовая партия — продукция со склада: нужна хотя бы одна единица с достаточным средним качеством.
                certificate.Passed = company.Inventory.ProductUnits(certificate.ProductId) > 0
                                     && company.Inventory.ProductQuality(certificate.ProductId) >= settings.CertificationQuality;
                certificate.ValidUntilMonth = certificate.Passed ? month + settings.CertificateValidityMonths - 1 : -1;
            }
        }

        // ---------- Решения компаний (через команды) ----------

        public string CheckSponsorship(Company company, int tournamentId)
        {
            var tournament = UpcomingTournaments.FirstOrDefault(t => t.Id == tournamentId);
            if (tournament == null) return CoreText.T("err.no_tournament", "Турнир №{0} не объявлен или уже прошёл.", tournamentId);
            return tournament.Sponsors.Contains(company.Id) ? CoreText.T("err.already_sponsor", "Компания уже спонсирует этот турнир.") : null;
        }

        internal void Sponsor(Company company, int tournamentId)
        {
            var tournament = tournaments.First(t => t.Id == tournamentId);
            company.Pay(LedgerCategory.Marketing, MarketingCost(company, SponsorshipCost(tournament.Level)));
            tournament.Sponsors.Add(company.Id);
        }

        public string CheckAmbassador(string athleteId)
        {
            if (!catalog.Athletes.Any(a => a.Id == athleteId)) return CoreText.T("err.no_athlete", "Нет спортсмена «{0}».", athleteId);
            return AmbassadorOwner(athleteId) != null ? CoreText.T("err.athlete_taken", "У спортсмена уже есть контракт.") : null;
        }

        internal void HireAmbassador(Company company, string athleteId)
        {
            ambassadorOwners[athleteId] = company.Id;
            company.Sales.ambassadors.Add(athleteId);
        }

        internal void Release(Company company, string athleteId)
        {
            ambassadorOwners.Remove(athleteId);
            company.Sales.ambassadors.Remove(athleteId);
        }

        public string CheckCertification(Company company, string productId, CertificationBody body)
        {
            if (!catalog.Products.Any(p => p.Id == productId)) return CoreText.T("err.no_product", "Нет продукта «{0}».", productId);
            int month = Simulation.MonthIndex;
            bool busy = company.Sales.Certificates.Any(c => c.ProductId == productId && c.Body == body
                                                             && (!c.Checked || c.IsValid(month)));
            return busy ? CoreText.T("err.certificate_busy", "Сертификат на этот продукт уже есть или проверка ещё идёт.") : null;
        }

        internal void StartCertification(Company company, string productId, CertificationBody body)
        {
            company.Pay(LedgerCategory.Marketing, (long)Math.Round(settings.CertificationCost * Economy.PriceLevel));
            int months = certificationRandom.NextInt(settings.CertificationMinMonths, settings.CertificationMaxMonths + 1);
            company.Sales.certificates.Add(new Certificate
            {
                ProductId = productId, Body = body, ReadyMonth = Simulation.MonthIndex + months, ValidUntilMonth = -1,
            });
        }

        // ---------- Конец месяца: репутация ----------

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                var stats = company.CurrentMonth;
                long soldRetail = stats.Sold.Values.Sum();
                long returnedRetail = stats.ReturnedUnits - stats.ContractReturnedUnits;
                if (returnedRetail > 0)
                    stats.ReputationLoss += returnedRetail * 100.0 / Math.Max(1, soldRetail + returnedRetail) * settings.ReturnReputationPerPercent;
                company.Reputation = Formulas.NextReputation(company.Reputation, stats.ReputationGain, stats.ReputationLoss,
                    settings.ReputationReversion);
            }
        }
    }
}
