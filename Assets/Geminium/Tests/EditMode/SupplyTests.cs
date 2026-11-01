using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // Снабжение (GDD 7.4): заказы, поставки, расписания, биржевая цена.
    public class SupplyTests
    {
        private static (GameWorld world, Company company, PlayerController player) NewWorld(System.Action<GameData> tweak = null)
        {
            var world = TestData.World(d => { TestData.Calm(d); tweak?.Invoke(d); });
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            return (world, company, player);
        }

        [Test]
        public void Order_IsDeliveredAfterLeadTimeAndPaidOnDelivery()
        {
            var (world, company, player) = NewWorld();
            Assert.IsNull(player.Submit(new PlaceOrderCommand("cotton_test", 100)));

            world.Simulation.RunMinutes(2 * 1440 - 1); // ещё не прошло 2 дня
            Assert.AreEqual(0, company.Inventory.MaterialQuantity("cotton"));

            world.Simulation.RunMinutes(2);
            Assert.AreEqual(100, company.Inventory.MaterialQuantity("cotton"), 1e-9);
            Assert.AreEqual(60, company.Inventory.MaterialQuality("cotton"), 1e-9);
            Assert.AreEqual(-100 * 25000, company.Ledger.MonthTotal(0, LedgerCategory.MaterialPurchases));
        }

        [Test]
        public void Order_BelowMinimumBatch_IsRejected()
        {
            var (_, _, player) = NewWorld();
            Assert.AreEqual("Минимальная партия у «cotton_test» — 10.", player.Submit(new PlaceOrderCommand("cotton_test", 5)));
            Assert.AreEqual("Нет поставщика «nobody».", player.Submit(new PlaceOrderCommand("nobody", 50)));
        }

        [Test]
        public void WeeklySchedule_OrdersEveryMondayWithDiscount()
        {
            var (world, company, player) = NewWorld();
            player.Submit(new SetSupplyScheduleCommand("cotton_test", 20, DeliveryFrequency.Weekly));
            world.Simulation.RunMonths(1);

            // Понедельники января 2027: 4, 11, 18, 25 — четыре поставки по 20 м² со скидкой 5%.
            Assert.AreEqual(80, company.Inventory.MaterialQuantity("cotton"), 1e-9);
            Assert.AreEqual(-(long)(80 * 25000 * 0.95), company.Ledger.MonthTotal(0, LedgerCategory.MaterialPurchases));
        }

        [Test]
        public void BigDemand_RaisesExchangePriceNextMonth()
        {
            var (world, _, player) = NewWorld();
            player.Submit(new PlaceOrderCommand("cotton_test", 20000));
            world.Simulation.RunMonths(1);
            world.Simulation.Step(); // начало февраля: биржа пересчитала цену

            double ratio = world.Supply.ExchangePrice("cotton") / 25000.0 / world.Economy.PriceLevel;
            Assert.AreEqual(2.0, ratio, 1e-6);
        }

        [Test]
        public void UnreliableSupplier_CanFailDelivery()
        {
            var (world, company, player) = NewWorld(d =>
            {
                d.Suppliers[0].Reliability = 0;
                d.Supply.FailureShare = 1;
            });
            player.Submit(new PlaceOrderCommand("cotton_test", 100));
            world.Simulation.RunMinutes(10 * 1440);

            Assert.AreEqual(1, world.Supply.FailedDeliveries);
            Assert.AreEqual(0, company.Inventory.MaterialQuantity("cotton"));
            Assert.AreEqual(0, company.Ledger.MonthTotal(0, LedgerCategory.MaterialPurchases));
        }
    }
}
