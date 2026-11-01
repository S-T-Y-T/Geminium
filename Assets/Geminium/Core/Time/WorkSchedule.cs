using System;

namespace Geminium.Core.Time
{
    // Параметры графика работы компании (GDD 7.7). Рабочие значения — в GameDatabase.asset, здесь значения по умолчанию.
    [Serializable]
    public sealed class WorkScheduleSettings
    {
        public int WorkingDaysPerWeek = 5;     // 5 — график 5/2, 6 — график 6/1
        public int ShiftsPerDay = 1;           // 1, 2 или 3 смены
        public int ShiftLengthMinutes = 480;   // смена 8 часов
        public int FirstShiftStartMinute = 480; // первая смена начинается в 08:00
    }

    // График работы: какие минуты рабочие и какая смена идёт.
    // Рабочие сутки начинаются с первой смены: при трёх сменах ночная смена
    // (00:00–08:00 субботы) относится к пятнице.
    public sealed class WorkSchedule
    {
        public int WorkingDaysPerWeek { get; }
        public int ShiftsPerDay { get; }
        public int ShiftLengthMinutes { get; }
        public int FirstShiftStartMinute { get; }

        public WorkSchedule(WorkScheduleSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.WorkingDaysPerWeek < 1 || settings.WorkingDaysPerWeek > 7)
                throw new ArgumentOutOfRangeException(nameof(settings), "Рабочих дней в неделе — от 1 до 7.");
            if (settings.ShiftLengthMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "Длина смены должна быть больше нуля.");
            if (settings.ShiftsPerDay < 1 || settings.ShiftsPerDay * settings.ShiftLengthMinutes > SimTime.MinutesPerDay)
                throw new ArgumentOutOfRangeException(nameof(settings), "Смены не помещаются в сутки.");
            if (settings.FirstShiftStartMinute < 0 || settings.FirstShiftStartMinute >= SimTime.MinutesPerDay)
                throw new ArgumentOutOfRangeException(nameof(settings), "Начало первой смены — внутри суток.");

            WorkingDaysPerWeek = settings.WorkingDaysPerWeek;
            ShiftsPerDay = settings.ShiftsPerDay;
            ShiftLengthMinutes = settings.ShiftLengthMinutes;
            FirstShiftStartMinute = settings.FirstShiftStartMinute;
        }

        // Номер идущей смены (0, 1, 2) или -1, если время нерабочее.
        public int ShiftAt(DateTime moment)
        {
            DateTime workDayTime = moment.AddMinutes(-FirstShiftStartMinute);
            if (!IsWorkingDay(workDayTime.DayOfWeek)) return -1;
            int minuteIntoWorkDay = workDayTime.Hour * SimTime.MinutesPerHour + workDayTime.Minute;
            int shift = minuteIntoWorkDay / ShiftLengthMinutes;
            return shift < ShiftsPerDay ? shift : -1;
        }

        public bool IsWorkingTime(DateTime moment) => ShiftAt(moment) >= 0;

        // Неделя начинается с понедельника: при 5/2 рабочие пн–пт, при 6/1 — пн–сб.
        public bool IsWorkingDay(DayOfWeek day) => ((int)day + 6) % 7 < WorkingDaysPerWeek;

        // Сколько рабочих дней в месяце игры (нужно, чтобы делить месячный спрос по дням).
        public int CountWorkingDays(GameCalendar calendar, int monthIndex)
        {
            DateTime first = calendar.StartDate.AddMonths(monthIndex);
            int days = calendar.DaysInMonth(monthIndex);
            int count = 0;
            for (int d = 0; d < days; d++)
                if (IsWorkingDay(first.AddDays(d).DayOfWeek)) count++;
            return count;
        }
    }
}
