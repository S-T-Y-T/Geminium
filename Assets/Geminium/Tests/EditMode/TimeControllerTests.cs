using System;
using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Time;

namespace Geminium.Tests.EditMode
{
    // Темп игры: 1 смена по графику 5/2; январь 2027 начинается в пятницу.
    public class TimeControllerTests
    {
        private Simulation simulation;
        private TimeController controller;
        private int autoPausedMonth;

        [SetUp]
        public void SetUp()
        {
            simulation = new Simulation(1, new GameCalendar(new CalendarSettings { StartYear = 2027, StartMonth = 1 }));
            var schedule = new WorkSchedule(new WorkScheduleSettings { WorkingDaysPerWeek = 5, ShiftsPerDay = 1 });
            controller = new TimeController(simulation, schedule, new TimePacingSettings());
            autoPausedMonth = -1;
            controller.AutoPaused += month => autoPausedMonth = month;
        }

        private void MoveTo(int day, int hour) =>
            simulation.RunUntil(new SimTime((day - 1) * (long)SimTime.MinutesPerDay + hour * 60));

        [Test]
        public void X1_ShiftTakesAbout40Seconds()
        {
            MoveTo(1, 8); // пятница 08:00, начало смены
            int ticks = controller.Advance(40.0);
            Assert.That(ticks, Is.InRange(479, 480));
        }

        [Test]
        public void X8_IsEightTimesFaster()
        {
            MoveTo(1, 8);
            controller.SetSpeed(8);
            Assert.That(controller.Advance(5.0), Is.InRange(479, 480));
        }

        [Test]
        public void NonWorkingTime_Runs60TimesFaster()
        {
            MoveTo(2, 8); // суббота — выходной
            Assert.That(controller.Advance(1.0), Is.InRange(719, 720));
        }

        [Test]
        public void Paused_DoesNotAdvance()
        {
            controller.Pause();
            Assert.AreEqual(0, controller.Advance(100.0));
            Assert.AreEqual(SimTime.Zero, simulation.Now);
        }

        [Test]
        public void MonthEnd_AutoPausesExactlyAtTheBoundary()
        {
            MoveTo(31, 20); // воскресенье 31 января, вечер
            controller.SetSpeed(8);
            controller.Advance(1000.0);

            Assert.IsTrue(controller.IsPaused);
            Assert.AreEqual(0, autoPausedMonth);
            Assert.AreEqual(simulation.Calendar.MonthStart(1), simulation.Now);
        }

        [Test]
        public void LongFrame_IsCappedByMaxTicks()
        {
            var settings = new TimePacingSettings { AutoPauseAtMonthEnd = false, MaxTicksPerAdvance = 5000 };
            var schedule = new WorkSchedule(new WorkScheduleSettings());
            var capped = new TimeController(simulation, schedule, settings);

            Assert.AreEqual(5000, capped.Advance(1e9));
            Assert.AreEqual(0, capped.Advance(0.000001)); // накопленный хвост отброшен
        }

        [Test]
        public void SetSpeed_RejectsUnsupportedValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(3));
        }
    }
}
