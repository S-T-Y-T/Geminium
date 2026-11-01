using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // Производство-заглушка (TODO 1.11), рынок B2C (GDD 7.2) и склад (GDD 7.5).
    public class ProductionMarketTests
    {
        private static GameWorld CalmWorld(System.Action<GameData> tweak = null) =>
            TestData.World(d => { TestData.Calm(d); tweak?.Invoke(d); });

        [Test]
        public void BuyMachine_PaysAndRejectsWhenCashIsShort()
        {
            var world = CalmWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);

            Assert.IsNull(player.Submit(new BuyMachineCommand("cutting_band")));
            world.Simulation.Step();
            Assert.AreEqual(1, company.Machines.Count);
            Assert.AreEqual(-25000000, company.Ledger.MonthTotal(0, LedgerCategory.Equipment));
            StringAssert.StartsWith("Не хватает денег", player.Submit(new BuyRacksCommand(1000)));
        }

        [Test]
        public void Production_FollowsPlanMaterialsAndPaysWages()
        {
            var world = CalmWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            player.Submit(new BuyMachineCommand("cutting_band"));
            player.Submit(new PlaceOrderCommand("cotton_test", 1000));
            player.Submit(new SetMonthlyPlanCommand("dobok", 100));
            world.Simulation.RunMonths(1);

            var report = company.Reports[0];
            Assert.AreEqual(100, report.UnitsProduced + report.UnitsDefective);
            Assert.AreEqual(750, company.Inventory.MaterialQuantity("cotton"), 1e-6);
            Assert.AreEqual(7000000, report.Wages);       // разнорабочий 3 млн + раскройщик 4 млн
            Assert.AreEqual(840000, report.SocialTax);     // 12% от ФОТ
            Assert.Greater(report.Energy, 0);
        }

        [Test]
        public void Production_WithoutMaterials_ProducesNothing()
        {
            var world = CalmWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            player.Submit(new BuyMachineCommand("cutting_band"));
            player.Submit(new SetMonthlyPlanCommand("dobok", 100));
            world.Simulation.RunMonths(1);
            Assert.AreEqual(0, company.Reports[0].UnitsProduced);
        }

        private static (GameWorld, Company, Company) TwoSellers(long priceB, long stockA, long stockB)
        {
            var world = CalmWorld(d => d.Warehouse.StartingCells = 1000);
            var a = world.AddCompany("a", new PlayerController());
            var bController = new PlayerController();
            var b = world.AddCompany("b", bController);
            if (stockA > 0) a.Inventory.AddProducts("dobok", stockA, 64, 100000);
            if (stockB > 0) b.Inventory.AddProducts("dobok", stockB, 64, 100000);
            if (priceB != 450000) bController.Submit(new SetRetailPriceCommand("dobok", priceB));
            world.Simulation.RunMonths(1);
            return (world, a, b);
        }

        [Test]
        public void Market_EqualCompaniesSplitDemand()
        {
            var (world, a, b) = TwoSellers(450000, 5000, 5000);
            long soldA = a.Reports[0].UnitsSold, soldB = b.Reports[0].UnitsSold;
            Assert.That(System.Math.Abs(soldA - soldB), Is.LessThanOrEqualTo(2));
            // Январь: спрос 2000 × сезонность 0,85 = 1700.
            Assert.That(soldA + soldB, Is.InRange(1690, 1700));
            Assert.AreEqual(soldA * 450000, a.Ledger.MonthTotal(0, LedgerCategory.SalesRetail));
            Assert.AreEqual(soldA * 100000, a.Reports[0].CostOfGoodsSold);
            Assert.AreEqual(0.5, world.Market.ShareOf(0, "a", "dobok"), 0.01);
        }

        [Test]
        public void Market_CheaperCompanySellsMore()
        {
            var (_, a, b) = TwoSellers(360000, 5000, 5000);
            Assert.Greater(b.Reports[0].UnitsSold, a.Reports[0].UnitsSold * 1.2);
        }

        [Test]
        public void Market_UnmetDemandSpillsToCompanyWithStock()
        {
            var (_, a, b) = TwoSellers(450000, 0, 5000);
            Assert.AreEqual(0, a.Reports[0].UnitsSold);
            Assert.Greater(a.Reports[0].LostSales, 0);
            Assert.That(b.Reports[0].UnitsSold, Is.GreaterThan(1600));
        }

        [Test]
        public void Warehouse_ChargesCellsAndPenalizesOverflow()
        {
            var world = CalmWorld(d => d.Warehouse.StartingCells = 1);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            company.Inventory.AddMaterial("cotton", 450, 60, 25000); // нужно 3 ячейки по 200, есть 1
            world.Simulation.RunMinutes(1440);

            Assert.AreEqual(-(150000 + 2000), company.Ledger.MonthTotal(0, LedgerCategory.Rent));
            Assert.AreEqual(-250 * 500, company.Ledger.MonthTotal(0, LedgerCategory.Penalties));

            player.Submit(new BuyRacksCommand(2));
            world.Simulation.Step();
            Assert.AreEqual(21, company.WarehouseCells);
        }
    }
}
