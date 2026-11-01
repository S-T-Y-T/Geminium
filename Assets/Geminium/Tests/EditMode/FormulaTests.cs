using System;
using NUnit.Framework;
using Geminium.Core;

namespace Geminium.Tests.EditMode
{
    // Формулы GDD (Приложение Б) на конкретных числах.
    public class FormulaTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void Seasonality_PeaksInSeptemberAndBottomsInMarch()
        {
            Assert.AreEqual(1.3, Formulas.Seasonality(9, 0.3, 9), Eps);
            Assert.AreEqual(0.7, Formulas.Seasonality(3, 0.3, 9), Eps);
            Assert.AreEqual(1.0, Formulas.Seasonality(6, 0.3, 9), Eps);
        }

        [Test]
        public void EconomyIndex_RevertsToOneAndStaysInBounds()
        {
            Assert.AreEqual(1.36, Formulas.NextEconomyIndex(1.4, 0.1, 0.06, 0, 0.5, 1.5), Eps);
            Assert.AreEqual(1.5, Formulas.NextEconomyIndex(1.4, 0.1, 0.06, 10, 0.5, 1.5), Eps);
            Assert.AreEqual(0.5, Formulas.NextEconomyIndex(0.6, 0.1, 0.06, -10, 0.5, 1.5), Eps);
        }

        [Test]
        public void KeyRate_FollowsInflation()
        {
            Assert.AreEqual(0.135, Formulas.KeyRate(0.10, 1.0, 0.085, 0.05, 0.05, 0.30), Eps);
            Assert.AreEqual(0.05, Formulas.KeyRate(0.10, 1.0, -0.2, 0.05, 0.05, 0.30), Eps);
        }

        [Test]
        public void PriceElasticity_GrowsInCrisis()
        {
            Assert.AreEqual(1.5, Formulas.PriceElasticity(1.5, 1.0, 1.0), Eps);
            Assert.AreEqual(2.25, Formulas.PriceElasticity(1.5, 1.0, 0.5), Eps);
        }

        [Test]
        public void Demand_MarketSizeAndAttractiveness()
        {
            Assert.AreEqual(2000 * 0.85 * 1.2, Formulas.MonthlyDemand(2000, 0.85, 1.2, 1.0), Eps);
            Assert.AreEqual(1000, Formulas.MarketSize(1000, 100, 100, 0.5), Eps);
            Assert.AreEqual(1000 / Math.Sqrt(2), Formulas.MarketSize(1000, 100, 200, 0.5), Eps);
            Assert.AreEqual(105, Formulas.Attractiveness(80, 50, 100, 100, 1, 0.5, 1.5), Eps);
            Assert.AreEqual(105 / Math.Pow(2, 1.5), Formulas.Attractiveness(80, 50, 200, 100, 1, 0.5, 1.5), Eps);
        }

        [Test]
        public void QualityDefectsAndOperationTime()
        {
            Assert.AreEqual(64, Formulas.UnitQuality(60, 50, 100), Eps);
            Assert.AreEqual(0.028, Formulas.DefectChance(0.04, 50, 60, 100, 0, 2), Eps);
            Assert.AreEqual(0.30, Formulas.DefectChance(0.04, 50, 60, 100, 80, 2), Eps);
            Assert.AreEqual(0.028 * Math.Exp(2), Formulas.DefectChance(0.04, 50, 60, 0, 0, 2), Eps);
            Assert.AreEqual(0.95, Formulas.DefectChance(1, 0, 0, 0, 0, 2), Eps);
            Assert.AreEqual(1.0, Formulas.WearMultiplier(50, 2), Eps);
            Assert.AreEqual(1.0, Formulas.WorkerEfficiency(50, 0, 1), Eps);
            Assert.AreEqual(0.5, Formulas.WorkerEfficiency(50, 80, 1), Eps);
            Assert.AreEqual(25.0 / 1.6 + 2, Formulas.OperationTime(25, 1.6, 1, 2), Eps);
        }

        [Test]
        public void SupplyPrices()
        {
            Assert.AreEqual(25000, Formulas.ExchangePrice(25000, 0.1, 0, 0, 20000, 0.5, 2), Eps);
            Assert.AreEqual(37500, Formulas.ExchangePrice(25000, 0.1, 0, 10000, 20000, 0.5, 2), Eps);
            Assert.AreEqual(50000, Formulas.ExchangePrice(25000, 0.1, 0, 1e9, 20000, 0.5, 2), Eps);
            Assert.AreEqual(99, Formulas.SupplierPrice(100, 0.1, 0.05, 0.05), Eps);
        }

        [Test]
        public void FinanceFormulas()
        {
            Assert.AreEqual(0.85, Formulas.CreditRating(1, 0, 0, 100), Eps);
            Assert.AreEqual(0.0, Formulas.CreditRating(0, -0.5, 100, 50), Eps);
            Assert.AreEqual(0.124, Formulas.LoanRate(0.135, 0.04, 0.85, 0.06), Eps);
            Assert.AreEqual(760, Formulas.Capitalization(100, 50, 0, 30, 20, 10, 60, 50));
            Assert.AreEqual(80, Formulas.Capitalization(100, 0, 0, 0, 20, -10, 60, 50)); // убыток не уменьшает оценку ниже чистых активов
            Assert.AreEqual(300, Formulas.ResidualValue(600, 30, 60));
            Assert.AreEqual(0, Formulas.ResidualValue(600, 60, 60));
            Assert.AreEqual(170000, Formulas.WarehouseDailyCost(150000, 10, 2000));
        }
    }
}
