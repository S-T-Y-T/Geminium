using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Time;

namespace Geminium.Tests.EditMode
{
    public class SimulationTests
    {
        // Подсистема, которая записывает свои вызовы в общий журнал.
        private sealed class Recorder : SimulationSystem
        {
            public long Minutes;
            private readonly List<string> log;
            private readonly string name;

            public Recorder(List<string> log, string name = "")
            {
                this.log = log;
                this.name = name;
            }

            protected override void OnMonthStarted(int monthIndex) => log.Add(name + "MS" + monthIndex);
            protected override void OnDayStarted(long dayIndex) => log.Add(name + "DS" + dayIndex);
            protected override void OnMinute(SimTime now)
            {
                Minutes++;
                if (now.Minute < 3 || now.Minute == 5) log.Add(name + "M" + now.Minute);
            }
            protected override void OnDayEnded(long dayIndex) => log.Add(name + "DE" + dayIndex);
            protected override void OnMonthEnded(int monthIndex) => log.Add(name + "ME" + monthIndex);
        }

        // Подсистема, которая каждый день берёт случайное число из своего потока.
        private sealed class DailyDice : SimulationSystem
        {
            public double Sum;
            private SimRandom random;
            protected override void OnAttached() => random = Simulation.Random.Fork("dice");
            protected override void OnDayStarted(long dayIndex) => Sum += random.NextDouble();
        }

        private static Simulation NewSimulation(ulong seed = 1) =>
            new Simulation(seed, new GameCalendar(new CalendarSettings { StartYear = 2027, StartMonth = 1 }));

        [Test]
        public void OneMonth_FiresEventsInOrder()
        {
            var simulation = NewSimulation();
            var log = new List<string>();
            var recorder = new Recorder(log);
            simulation.AddSystem(recorder);

            simulation.RunMonths(1);

            Assert.AreEqual(31L * SimTime.MinutesPerDay, recorder.Minutes);
            Assert.AreEqual(1, simulation.MonthIndex);
            Assert.AreEqual(simulation.Calendar.MonthStart(1), simulation.Now);
            CollectionAssert.AreEqual(new[] { "MS0", "DS0", "M0", "M1", "M2" }, log.Take(5));
            CollectionAssert.AreEqual(new[] { "DE30", "ME0" }, log.Skip(log.Count - 2));
            Assert.AreEqual(31, log.Count(e => e.StartsWith("DS")));
            Assert.AreEqual(31, log.Count(e => e.StartsWith("DE")));
            Assert.AreEqual(1, log.Count(e => e.StartsWith("MS")));
        }

        [Test]
        public void NextMonth_StartsOnTheFollowingStep()
        {
            var simulation = NewSimulation();
            var log = new List<string>();
            simulation.AddSystem(new Recorder(log));
            simulation.RunMonths(1);
            log.Clear();

            simulation.Step();

            CollectionAssert.AreEqual(new[] { "MS1", "DS31" }, log);
        }

        [Test]
        public void Systems_AreCalledInRegistrationOrder()
        {
            var simulation = NewSimulation();
            var log = new List<string>();
            simulation.AddSystem(new Recorder(log, "A"));
            simulation.AddSystem(new Recorder(log, "B"));

            simulation.Step();

            CollectionAssert.AreEqual(new[] { "AMS0", "BMS0", "ADS0", "BDS0", "AM0", "BM0" }, log);
        }

        [Test]
        public void Scheduler_RunsEventsAtTheirMinuteInInsertionOrder_BeforeSystems()
        {
            var simulation = NewSimulation();
            var log = new List<string>();
            simulation.AddSystem(new Recorder(log));
            simulation.Scheduler.Schedule(new SimTime(10), () => log.Add("a@" + simulation.Now.Minute));
            simulation.Scheduler.Schedule(new SimTime(5), () => log.Add("b@" + simulation.Now.Minute));
            simulation.Scheduler.Schedule(new SimTime(10), () => log.Add("c@" + simulation.Now.Minute));
            var cancelled = simulation.Scheduler.Schedule(new SimTime(7), () => log.Add("x"));
            cancelled.Cancel();

            simulation.RunMinutes(20);

            var events = log.Where(e => e.Contains("@") || e == "M5" || e == "x").ToList();
            CollectionAssert.AreEqual(new[] { "b@5", "M5", "a@10", "c@10" }, events);
            Assert.AreEqual(0, simulation.Scheduler.PendingCount);
        }

        [Test]
        public void SameSeed_GivesSameResult_DifferentSeed_Differs()
        {
            double Run(ulong seed)
            {
                var simulation = NewSimulation(seed);
                var dice = new DailyDice();
                simulation.AddSystem(dice);
                simulation.RunMonths(2);
                return dice.Sum;
            }

            Assert.AreEqual(Run(99), Run(99));
            Assert.AreNotEqual(Run(99), Run(100));
        }
    }
}
