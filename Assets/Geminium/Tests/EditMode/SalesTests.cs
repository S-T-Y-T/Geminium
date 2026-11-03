using System;
using System.Linq;
using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Sales;

namespace Geminium.Tests.EditMode
{
    // Тендеры (GDD 7.2), маркетинг и репутация (GDD 7.3) — этап 3.
    public class SalesTests
    {
        // Мир без случайного шума, с одним тендером в месяц заданного уровня.
        private static GameWorld World(TenderLevel level, Action<GameData> tweak = null) =>
            TestData.World(d =>
            {
                TestData.Calm(d);
                d.Warehouse.StartingCells = 10000;
                d.Tenders.MinTendersPerMonth = 1;
                d.Tenders.MaxTendersPerMonth = 1;
                foreach (var l in d.Tenders.Levels) l.Weight = l.Level == level ? 1 : 0;
                tweak?.Invoke(d);
            });

        [Test]
        public void Tender_IsPublishedWithinLevelRules()
        {
            var world = World(TenderLevel.Club);
            world.AddCompany("a", new PlayerController());
            world.Simulation.Step();

            var tender = world.Tenders.OpenTenders.Single();
            Assert.AreEqual(TenderLevel.Club, tender.Level);
            Assert.AreEqual("dobok", tender.ProductId);
            Assert.That(tender.Units, Is.InRange(100, 500)); // спрос добока = эталонный, партия как в таблице
            Assert.That(tender.DeliveryMonths, Is.InRange(1, 2));
            Assert.AreEqual((long)Math.Round(450000 * 0.65), tender.MaxUnitPrice);
        }

        [Test]
        public void Tender_HighestScoreWinsAndContractIsDelivered()
        {
            var world = World(TenderLevel.School);
            var a = new PlayerController();
            var b = new PlayerController();
            var companyA = world.AddCompany("a", a);
            var companyB = world.AddCompany("b", b);
            companyA.Inventory.AddProducts("dobok", 5000, 64, 100000);
            companyB.Inventory.AddProducts("dobok", 5000, 64, 100000);
            world.Simulation.Step();

            var tender = world.Tenders.OpenTenders.Single();
            long cheaper = (long)(tender.MaxUnitPrice * 0.8);
            Assert.IsNull(a.Submit(new BidTenderCommand(tender.Id, tender.MaxUnitPrice)));
            Assert.IsNull(b.Submit(new BidTenderCommand(tender.Id, cheaper)));
            world.Simulation.RunMonths(1);

            // Равные репутация и история качества: решает цена (0,5 × 20% = +0,1 балла).
            Assert.AreEqual("b", tender.WinnerId);
            var contract = companyB.Sales.Contracts.Single();
            Assert.AreEqual(tender.Units, contract.Units);
            Assert.AreEqual(1, contract.DeadlineMonth);
            Assert.AreEqual(0.4, tender.Bids.Single(x => x.CompanyId == "b").Score, 1e-9);

            world.Simulation.RunMonths(1);
            Assert.AreEqual(ContractStatus.Completed, contract.Status);
            Assert.AreEqual(tender.Units * cheaper, companyB.Ledger.MonthTotal(1, LedgerCategory.SalesContracts));
            Assert.AreEqual(tender.Units * cheaper, companyB.Reports[1].ContractRevenue);
            Assert.AreEqual(1, companyB.Reports[1].ContractsCompleted);
            Assert.IsEmpty(companyA.Sales.Contracts);
        }

        [Test]
        public void Tender_RequiresReputationCertificateAndPriceLimit()
        {
            var club = World(TenderLevel.Club, d => d.Market.DefaultReputation = 20);
            var a = new PlayerController();
            club.AddCompany("a", a);
            club.Simulation.Step();
            var tender = club.Tenders.OpenTenders.Single();
            Assert.AreEqual("Нужна репутация не ниже 30, у компании 20.", a.Submit(new BidTenderCommand(tender.Id, 1000)));

            var national = World(TenderLevel.National, d => d.Market.DefaultReputation = 60);
            var b = new PlayerController();
            national.AddCompany("b", b);
            national.Simulation.Step();
            tender = national.Tenders.OpenTenders.Single();
            Assert.AreEqual("Нужен сертификат на «dobok».", b.Submit(new BidTenderCommand(tender.Id, 1000)));

            var school = World(TenderLevel.School);
            var c = new PlayerController();
            school.AddCompany("c", c);
            school.Simulation.Step();
            tender = school.Tenders.OpenTenders.Single();
            StringAssert.StartsWith("Цена выше предельной", c.Submit(new BidTenderCommand(tender.Id, tender.MaxUnitPrice + 1)));
            Assert.AreEqual($"Тендер №999 не принимает заявки.", c.Submit(new BidTenderCommand(999, 1000)));
        }

        [Test]
        public void Contract_LatePenaltiesThenTermination()
        {
            var world = World(TenderLevel.School);
            var a = new PlayerController();
            var company = world.AddCompany("a", a);
            world.Simulation.Step();
            var tender = world.Tenders.OpenTenders.Single();
            Assert.IsNull(a.Submit(new BidTenderCommand(tender.Id, tender.MaxUnitPrice)));
            world.Simulation.RunMonths(3); // склад пуст: поставить нечего

            // Срок — февраль. Март 2027: понедельники 1, 8, 15, 22 — неустойка; 29-го просрочка 5 недель — расторжение.
            var contract = company.Sales.Contracts.Single();
            Assert.AreEqual(ContractStatus.Terminated, contract.Status);
            Assert.AreEqual(5, contract.WeeksLate);
            long weekly = (long)Math.Round(contract.Value * 0.02);
            Assert.AreEqual(-4 * weekly, company.Ledger.MonthTotal(2, LedgerCategory.Penalties));
            Assert.AreEqual(1, company.Reports[2].ContractsTerminated);
            Assert.AreEqual(10, company.Reports[2].ReputationLoss, 1e-9);
        }

        [Test]
        public void Advertising_RaisesShareFromNextMonth()
        {
            var world = World(TenderLevel.School);
            var a = new PlayerController();
            var companyA = world.AddCompany("a", a);
            var companyB = world.AddCompany("b", new PlayerController());
            companyA.Inventory.AddProducts("dobok", 5000, 64, 100000);
            companyB.Inventory.AddProducts("dobok", 5000, 64, 100000);
            Assert.IsNull(a.Submit(new SetAdBudgetCommand(50_000_000)));
            world.Simulation.RunMonths(2);

            // Бюджет задан после начала месяца 0 — списывается и работает с месяца 1.
            Assert.AreEqual(0, companyA.Ledger.MonthTotal(0, LedgerCategory.Marketing));
            Assert.AreEqual(-50_000_000, companyA.Ledger.MonthTotal(1, LedgerCategory.Marketing));
            Assert.That(companyA.Reports[0].UnitsSold - companyB.Reports[0].UnitsSold, Is.InRange(-2, 2));
            Assert.Greater(companyA.Reports[1].UnitsSold, companyB.Reports[1].UnitsSold * 1.2);
        }

        [Test]
        public void Reputation_DriftsToFiftyAndFallsWithReturns()
        {
            var drift = World(TenderLevel.School, d => d.Market.DefaultReputation = 80);
            var calm = drift.AddCompany("a", new PlayerController());
            drift.Simulation.RunMonths(1);
            Assert.AreEqual(80 + 0.05 * (50 - 80), calm.Reputation, 1e-9);

            var returns = World(TenderLevel.School);
            var faulty = returns.AddCompany("b", new PlayerController());
            faulty.Inventory.AddProducts("dobok", 1000, 64, 100000, hiddenDefects: 100);
            returns.Simulation.RunMonths(1);
            // Возвращено ~9% проданного → −0,3 за каждый процент.
            Assert.That(faulty.Reputation, Is.InRange(46.0, 48.5));
            Assert.Greater(faulty.Reports[0].ReputationLoss, 0);
        }

        [Test]
        public void Certification_TakesMonthsAndNeedsQualityBatch()
        {
            var world = World(TenderLevel.School);
            var good = new PlayerController();
            var bad = new PlayerController();
            var goodCompany = world.AddCompany("good", good);
            var badCompany = world.AddCompany("bad", bad);
            goodCompany.Inventory.AddProducts("dobok", 100000, 70, 100000);
            badCompany.Inventory.AddProducts("dobok", 100000, 40, 100000);
            Assert.IsNull(good.Submit(new CertifyProductCommand("dobok", CertificationBody.WT)));
            Assert.IsNull(bad.Submit(new CertifyProductCommand("dobok", CertificationBody.WT)));
            world.Simulation.Step();
            Assert.AreEqual(-12_000_000, goodCompany.Ledger.MonthTotal(0, LedgerCategory.Marketing));
            Assert.AreEqual("Сертификат на этот продукт уже есть или проверка ещё идёт.",
                good.Submit(new CertifyProductCommand("dobok", CertificationBody.WT)));

            world.Simulation.RunMonths(4);
            var certificate = goodCompany.Sales.Certificates.Single();
            Assert.IsTrue(certificate.Checked && certificate.Passed);
            Assert.That(certificate.ReadyMonth, Is.InRange(2, 3));
            Assert.IsTrue(goodCompany.Sales.HasValidCertificate("dobok", 4));
            Assert.AreEqual(certificate.ReadyMonth + 23, certificate.ValidUntilMonth);
            Assert.IsFalse(badCompany.Sales.HasValidCertificate("dobok", 4));
            Assert.IsTrue(badCompany.Sales.Certificates.Single().Checked);
        }

        [Test]
        public void Sponsorship_AndAmbassadorWin_RaiseReputation()
        {
            var world = World(TenderLevel.School, d =>
            {
                d.Athletes.Add(new AthleteDef { Id = "athlete_x", Rating = 100, MonthlyFee = 1_000_000 });
                d.Marketing.RegionalTournamentChance = 1;
                d.Marketing.NationalTournamentChance = 0;
                d.Marketing.WorldTournamentChance = 0;
                d.Marketing.WinChanceScale = 1;
                d.Marketing.ScandalChance = 0;
                d.Marketing.AmbassadorWinGainMin = 0.2;
                d.Marketing.AmbassadorWinGainMax = 0.2;
            });
            var a = new PlayerController();
            var company = world.AddCompany("a", a);
            world.Simulation.Step();
            var tournament = world.Marketing.UpcomingTournaments.Single();
            Assert.AreEqual(1, tournament.Month);
            Assert.IsNull(a.Submit(new SponsorTournamentCommand(tournament.Id)));
            Assert.IsNull(a.Submit(new HireAmbassadorCommand("athlete_x")));
            world.Simulation.RunMonths(2);

            Assert.AreEqual(-8_000_000, company.Ledger.MonthTotal(0, LedgerCategory.Marketing));
            Assert.AreEqual("athlete_x", tournament.WinnerAthleteId);
            // Месяц 1: спонсорство +2 и победа амбассадора +20% от 50 = +12.
            Assert.AreEqual(12, company.Reports[1].ReputationGain, 1e-9);
            Assert.AreEqual(62, company.Reports[1].Reputation, 1e-9);
            Assert.IsTrue(company.Sales.HasSponsorshipBonus(TenderLevel.Regional, 6));
            Assert.IsFalse(company.Sales.HasSponsorshipBonus(TenderLevel.National, 1));
            Assert.Less(company.Ledger.MonthTotal(1, LedgerCategory.Marketing), 0); // гонорар амбассадора
        }

        [Test]
        public void AmbassadorCannotWorkForTwoCompanies()
        {
            var world = World(TenderLevel.School, d => d.Athletes.Add(new AthleteDef { Id = "athlete_x", Rating = 70, MonthlyFee = 1_000_000 }));
            var a = new PlayerController();
            var b = new PlayerController();
            world.AddCompany("a", a);
            world.AddCompany("b", b);
            world.Simulation.Step();
            Assert.IsNull(a.Submit(new HireAmbassadorCommand("athlete_x")));
            world.Simulation.Step();
            Assert.AreEqual("У спортсмена уже есть контракт.", b.Submit(new HireAmbassadorCommand("athlete_x")));
            Assert.AreEqual("«athlete_x» не амбассадор компании.", b.Submit(new ReleaseAmbassadorCommand("athlete_x")));
            Assert.IsNull(a.Submit(new ReleaseAmbassadorCommand("athlete_x")));
            world.Simulation.Step();
            Assert.IsNull(b.Submit(new HireAmbassadorCommand("athlete_x")));
        }
    }
}
