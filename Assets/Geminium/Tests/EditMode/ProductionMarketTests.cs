using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;

namespace Geminium.Tests.EditMode
{
    // Рынок B2C (GDD 7.2) и склад (GDD 7.5).
    public class ProductionMarketTests
    {
        private static GameWorld CalmWorld(System.Action<GameData> tweak = null) =>
            TestData.World(d => { TestData.Calm(d); tweak?.Invoke(d); });

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
        public void Market_HiddenDefectsComeBackAsReturns()
        {
            var world = CalmWorld(d => d.Warehouse.StartingCells = 1000);
            var a = world.AddCompany("a", new PlayerController());
            a.Inventory.AddProducts("dobok", 1000, 64, 100000, hiddenDefects: 100);
            world.Simulation.RunMonths(1);
            var report = a.Reports[0];
            Assert.That(report.ReturnedUnits, Is.EqualTo(report.UnitsSold / 10).Within(2));
            Assert.AreEqual(-report.ReturnedUnits * (450000 + 15000), a.Ledger.MonthTotal(0, LedgerCategory.Returns));
        }

        [Test]
        public void Warehouse_ChargesCellsPenalizesOverflowAndGrowsWithRacks()
        {
            var world = CalmWorld(d => d.Warehouse.StartingCells = 1);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            company.Inventory.AddMaterial("cotton", 450, 60, 25000); // нужно 3 ячейки по 200, есть 1
            world.Simulation.RunMinutes(1440);

            Assert.AreEqual(-(150000 + 2000), company.Ledger.MonthTotal(0, LedgerCategory.Rent));
            Assert.AreEqual(-250 * 500, company.Ledger.MonthTotal(0, LedgerCategory.Penalties));

            Assert.IsNull(player.Submit(new BatchCommand(new CompanyCommand[]
            {
                new PaintZoneCommand(1, 6, 4, 4, ZoneType.RawStorage),
                new PlaceObjectCommand(FloorObjectKind.Rack, null, 1, 9, Facing.North),
            })));
            world.Simulation.Step();
            Assert.AreEqual(11, company.WarehouseCells);
            Assert.AreEqual(-2500000, company.Ledger.MonthTotal(0, LedgerCategory.Equipment));
        }
    }
}
