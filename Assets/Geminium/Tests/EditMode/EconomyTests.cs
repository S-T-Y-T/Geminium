using System.Linq;
using NUnit.Framework;
using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // ИИ-ядро: макроэкономика и события (GDD 7.1).
    public class EconomyTests
    {
        [Test]
        public void History_IsDeterministicAndBounded()
        {
            var a = TestData.World(seed: 5);
            var b = TestData.World(seed: 5);
            var c = TestData.World(seed: 6);
            a.Simulation.RunMonths(12);
            b.Simulation.RunMonths(12);
            c.Simulation.RunMonths(12);

            Assert.AreEqual(12, a.Economy.History.Count);
            CollectionAssert.AreEqual(a.Economy.History.Select(h => h.Index), b.Economy.History.Select(h => h.Index));
            CollectionAssert.AreNotEqual(a.Economy.History.Select(h => h.Index), c.Economy.History.Select(h => h.Index));
            Assert.That(a.Economy.History.All(h => h.Index >= 0.5 && h.Index <= 1.5));
            Assert.Greater(a.Economy.History[11].PriceLevel, 1.0);
        }

        [Test]
        public void Seasonality_FollowsCalendar()
        {
            var world = TestData.World();
            world.Simulation.RunMonths(9);
            Assert.AreEqual(0.85, world.Economy.History[0].Seasonality, 1e-9); // январь
            Assert.AreEqual(1.3, world.Economy.History[8].Seasonality, 1e-9);  // сентябрь
        }

        [Test]
        public void Event_StartsFromSecondMonthAndLastsItsDuration()
        {
            var world = TestData.World(d =>
            {
                TestData.Calm(d);
                d.Events.Add(new EventDef { Id = "boom", MonthlyProbability = 1, MinDurationMonths = 2, MaxDurationMonths = 2, Effect = EventEffect.Demand, Magnitude = 0.5 });
            });
            world.Simulation.RunMonths(2);
            Assert.AreEqual(1.5, world.Economy.Multiplier(EventEffect.Demand, "dobok"), 1e-9);

            world.Simulation.RunMonths(3);
            CollectionAssert.IsEmpty(world.Economy.History[0].ActiveEventIds);
            CollectionAssert.AreEqual(new[] { 1, 3 }, world.Economy.StartedEvents.Select(e => e.StartMonth));
        }

        [Test]
        public void SeasonalEvent_StartsOnlyInItsSeason()
        {
            var world = TestData.World(d =>
            {
                TestData.Calm(d);
                d.Events.Add(new EventDef { Id = "summer", MonthlyProbability = 1, SeasonStartMonth = 6, SeasonEndMonth = 8, Effect = EventEffect.ProductionCapacity, Magnitude = -0.2 });
            });
            world.Simulation.RunMonths(13);
            CollectionAssert.AreEqual(new[] { 5, 6, 7 }, world.Economy.StartedEvents.Select(e => e.StartMonth));
        }
    }
}
