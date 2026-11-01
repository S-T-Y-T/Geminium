using System;

namespace Geminium.Core.Time
{
    // Параметры календаря. Рабочие значения — в GameDatabase.asset, здесь значения по умолчанию.
    [Serializable]
    public sealed class CalendarSettings
    {
        public int StartYear = 2027;
        public int StartMonth = 1; // игра всегда начинается с 1-го числа месяца
    }

    // Календарь игры: переводит минуты симуляции в обычные даты (григорианский календарь).
    // Месяц — календарный, 28–31 день; при графике 5/2 это 20–23 рабочих дня.
    public sealed class GameCalendar
    {
        public DateTime StartDate { get; }

        public GameCalendar(CalendarSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            StartDate = new DateTime(settings.StartYear, settings.StartMonth, 1);
        }

        public DateTime ToDateTime(SimTime time) => StartDate.AddMinutes(time.Minute);

        // Номер месяца от начала игры: 0 — первый месяц.
        public int MonthIndexAt(SimTime time)
        {
            DateTime date = ToDateTime(time);
            return (date.Year - StartDate.Year) * 12 + date.Month - StartDate.Month;
        }

        public SimTime MonthStart(int monthIndex)
        {
            if (monthIndex < 0) throw new ArgumentOutOfRangeException(nameof(monthIndex));
            return new SimTime((long)(StartDate.AddMonths(monthIndex) - StartDate).TotalMinutes);
        }

        public int DaysInMonth(int monthIndex)
        {
            DateTime month = StartDate.AddMonths(monthIndex);
            return DateTime.DaysInMonth(month.Year, month.Month);
        }
    }
}
