using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core
{
    // Симуляция: часы, календарь, планировщик событий, случайность и подсистемы.
    // Один вызов Step() — одна игровая минута. Порядок внутри минуты:
    //   начало месяца → начало дня → отложенные события → OnMinute подсистем →
    //   конец дня → конец месяца.
    public sealed class Simulation
    {
        private readonly List<SimulationSystem> systems = new List<SimulationSystem>();
        private long monthStartMinute;
        private long nextMonthStartMinute;

        public GameCalendar Calendar { get; }
        public EventScheduler Scheduler { get; }
        public SimRandom Random { get; }
        public SimTime Now { get; private set; }

        // Номер идущего месяца (0 — первый).
        public int MonthIndex { get; private set; }

        // Месяц закончился (передаётся его номер). Сюда подписывается автопауза и отчёт.
        public event Action<int> MonthEnded;

        public Simulation(ulong seed, GameCalendar calendar)
        {
            Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            Scheduler = new EventScheduler();
            Random = new SimRandom(seed);
            Now = SimTime.Zero;
            MonthIndex = 0;
            monthStartMinute = 0;
            nextMonthStartMinute = calendar.MonthStart(1).Minute;
        }

        public void AddSystem(SimulationSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            system.Attach(this);
            systems.Add(system);
        }

        public void Step()
        {
            if (Now.IsStartOfDay)
            {
                if (Now.Minute == monthStartMinute)
                    foreach (var system in systems) system.OnMonthStarted(MonthIndex);
                foreach (var system in systems) system.OnDayStarted(Now.DayIndex);
            }

            Scheduler.RunDue(Now);
            foreach (var system in systems) system.OnMinute(Now);

            Now = Now.AddMinutes(1);

            if (Now.IsStartOfDay)
            {
                long endedDay = Now.DayIndex - 1;
                foreach (var system in systems) system.OnDayEnded(endedDay);

                if (Now.Minute == nextMonthStartMinute)
                {
                    int endedMonth = MonthIndex;
                    foreach (var system in systems) system.OnMonthEnded(endedMonth);
                    MonthIndex++;
                    monthStartMinute = nextMonthStartMinute;
                    nextMonthStartMinute = Calendar.MonthStart(MonthIndex + 1).Minute;
                    MonthEnded?.Invoke(endedMonth);
                }
            }
        }

        public void RunMinutes(long minutes)
        {
            for (long i = 0; i < minutes; i++) Step();
        }

        public void RunUntil(SimTime time)
        {
            while (Now < time) Step();
        }

        // Прогнать целые месяцы (для прогонов без графики).
        public void RunMonths(int months)
        {
            int target = MonthIndex + months;
            while (MonthIndex < target) Step();
        }
    }
}
