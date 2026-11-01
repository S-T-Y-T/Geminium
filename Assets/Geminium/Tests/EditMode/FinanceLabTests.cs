using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Lab;
using Geminium.Game;

namespace Geminium.Tests.EditMode
{
    // Финансы (GDD 7.8) и Лаборатория (GDD 10).
    public class FinanceLabTests
    {
        [Test]
        public void Loan_IsIssuedAtRatingBasedRateAndRepaidMonthly()
        {
            var world = TestData.World(TestData.Calm);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);

            Assert.IsNull(player.Submit(new TakeLoanCommand(12000000, 12)));
            world.Simulation.Step();
            Assert.AreEqual(0.124, company.Loans[0].AnnualRate, 1e-9); // 13,5% + 4% − 0,85 × 6%
            Assert.AreEqual(92000000, company.Cash);

            world.Simulation.RunMonths(1);
            Assert.AreEqual(11000000, company.Debt);
            Assert.AreEqual(-124000, company.Ledger.MonthTotal(0, LedgerCategory.Interest));
            Assert.AreEqual(-1000000, company.Ledger.MonthTotal(0, LedgerCategory.LoanRepayment));
            Assert.AreEqual("Срок кредита — от 1 до 36 месяцев.", player.Submit(new TakeLoanCommand(1000000, 48)));
        }

        [Test]
        public void NegativeCashForTwoMonths_MeansBankruptcy()
        {
            var world = TestData.World(d => { TestData.Calm(d); d.StartingCash = 1000000; });
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            world.Simulation.RunMonths(2);

            Assert.IsTrue(company.IsBankrupt);
            Assert.AreEqual(1, company.BankruptMonth);
            Assert.AreEqual("Компания обанкротилась.", player.Submit(new SetRetailPriceCommand("dobok", 400000)));
        }

        [Test]
        public void MonthlyReport_IsConsistent()
        {
            var world = ScenarioRunner.Run(TestData.MinimalCatalog(), 3, 3);
            foreach (var company in world.Companies)
            foreach (var r in company.Reports.Where(r => !r.Bankrupt))
            {
                Assert.AreEqual(r.Revenue - r.CostOfGoodsSold - r.Wages - r.SocialTax - r.Rent - r.Energy - r.Penalties
                                - r.OtherExpenses - r.Interest - r.Depreciation, r.PreTaxProfit);
                Assert.AreEqual(r.PreTaxProfit > 0 ? System.Math.Round(r.PreTaxProfit * 0.15) : 0, r.ProfitTax, 1);
                Assert.AreEqual(r.PreTaxProfit - r.ProfitTax, r.NetIncome);
                Assert.That(r.CreditRating, Is.InRange(0.0, 1.0));
            }
        }

        private static string RunLab(ulong seed, int months)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset");
            return KpiExport.ToCsv(ScenarioRunner.Run(database.CreateCatalog(), seed, months));
        }

        [Test]
        public void Lab_StandardRunProducesSalesAndDeterministicCsv()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset");
            var world = ScenarioRunner.Run(database.CreateCatalog(), 1, 4);
            Assert.That(world.Companies.All(c => c.Reports.Count == 4));
            Assert.That(world.Companies.All(c => c.Reports.Sum(r => r.UnitsSold) > 0), "Каждая компания должна что-то продать за 4 месяца.");

            string csv = KpiExport.ToCsv(world);
            var lines = csv.Trim().Split('\n');
            Assert.AreEqual(1 + 4 * 3, lines.Length);
            StringAssert.StartsWith("month;date;company;", lines[0]);
            StringAssert.Contains("price_cotton", lines[0]);

            Assert.AreEqual(csv, RunLab(1, 4));
            Assert.AreNotEqual(csv, RunLab(2, 4));
        }
    }
}
