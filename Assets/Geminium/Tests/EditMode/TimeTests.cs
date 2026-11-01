using System;
using NUnit.Framework;
using Geminium.Core.Time;

namespace Geminium.Tests.EditMode
{
    // Календарь и график смен. Январь 2027: 1-е число — пятница.
    public class TimeTests
    {
        private static GameCalendar Calendar() => new GameCalendar(new CalendarSettings { StartYear = 2027, StartMonth = 1 });

        private static WorkSchedule Schedule(int days, int shifts) =>
            new WorkSchedule(new WorkScheduleSettings { WorkingDaysPerWeek = days, ShiftsPerDay = shifts });

        private static DateTime At(int day, int hour, int minute = 0) => new DateTime(2027, 1, day, hour, minute, 0);

        [Test]
        public void Calendar_StartsAtFirstDayOfStartMonth()
        {
            Assert.AreEqual(new DateTime(2027, 1, 1), Calendar().ToDateTime(SimTime.Zero));
        }

        [Test]
        public void Calendar_MonthsHaveCalendarLength()
        {
            var calendar = Calendar();
            Assert.AreEqual(31L * SimTime.MinutesPerDay, calendar.MonthStart(1).Minute);
            Assert.AreEqual((31L + 28) * SimTime.MinutesPerDay, calendar.MonthStart(2).Minute);
            Assert.AreEqual(28, calendar.DaysInMonth(1));
            Assert.AreEqual(0, calendar.MonthIndexAt(calendar.MonthStart(1).AddMinutes(-1)));
            Assert.AreEqual(1, calendar.MonthIndexAt(calendar.MonthStart(1)));
        }

        [Test]
        public void OneShift_FiveDayWeek_WorksMondayToFridayFrom8To16()
        {
            var schedule = Schedule(5, 1);
            Assert.AreEqual(0, schedule.ShiftAt(At(4, 8)));       // понедельник 08:00
            Assert.AreEqual(0, schedule.ShiftAt(At(4, 15, 59)));
            Assert.AreEqual(-1, schedule.ShiftAt(At(4, 16)));
            Assert.AreEqual(-1, schedule.ShiftAt(At(4, 7, 59)));
            Assert.AreEqual(-1, schedule.ShiftAt(At(9, 10)));     // суббота
        }

        [Test]
        public void SixDayWeek_WorksSaturdayButNotSunday()
        {
            var schedule = Schedule(6, 1);
            Assert.AreEqual(0, schedule.ShiftAt(At(9, 10)));      // суббота
            Assert.AreEqual(-1, schedule.ShiftAt(At(10, 10)));    // воскресенье
        }

        [Test]
        public void ThreeShifts_NightShiftBelongsToPreviousWorkDay()
        {
            var schedule = Schedule(5, 3);
            Assert.AreEqual(1, schedule.ShiftAt(At(8, 20)));      // пятница 20:00 — вторая смена
            Assert.AreEqual(2, schedule.ShiftAt(At(9, 2)));       // суббота 02:00 — ночная смена пятницы
            Assert.AreEqual(-1, schedule.ShiftAt(At(9, 10)));     // суббота днём — выходной
            Assert.AreEqual(-1, schedule.ShiftAt(At(4, 2)));      // ночь на понедельник — смены нет
        }

        [Test]
        public void CountWorkingDays_January2027()
        {
            Assert.AreEqual(21, Schedule(5, 1).CountWorkingDays(Calendar(), 0));
            Assert.AreEqual(26, Schedule(6, 1).CountWorkingDays(Calendar(), 0));
        }

        [Test]
        public void Schedule_RejectsShiftsThatDoNotFitInADay()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedule(5, 4));
        }
    }
}
