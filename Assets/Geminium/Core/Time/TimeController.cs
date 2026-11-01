using System;

namespace Geminium.Core.Time
{
    // Параметры темпа игры (GDD, раздел 5). Рабочие значения — в GameDatabase.asset, здесь значения по умолчанию.
    [Serializable]
    public sealed class TimePacingSettings
    {
        public double SecondsPerShiftAtX1 = 40;  // на x1 смена длится около 40 секунд
        public double NonWorkingSpeedup = 60;    // нерабочее время проматывается в 60 раз быстрее
        public int MaxTicksPerAdvance = 20000;   // защита, если кадр подвис: лишнее время отбрасывается
        public bool AutoPauseAtMonthEnd = true;  // в конце месяца пауза и отчёт
    }

    // Связывает реальное время с игровым: пауза, скорости x1–x8, быстрая промотка
    // нерабочих часов и автопауза в конце месяца.
    // Рабочее время определяется графиком компании игрока.
    public sealed class TimeController
    {
        private static readonly int[] AllowedSpeeds = { 1, 2, 4, 8 };

        private readonly Simulation simulation;
        private readonly WorkSchedule playerSchedule;
        private readonly TimePacingSettings settings;
        private readonly double secondsPerWorkingMinuteAtX1;
        private double accumulatedSeconds;
        private bool monthEndedDuringStep;

        public int Speed { get; private set; } = 1;
        public bool IsPaused { get; private set; }

        // Сработала автопауза в конце месяца (передаётся номер закончившегося месяца).
        public event Action<int> AutoPaused;

        public TimeController(Simulation simulation, WorkSchedule playerSchedule, TimePacingSettings settings)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.playerSchedule = playerSchedule ?? throw new ArgumentNullException(nameof(playerSchedule));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (settings.SecondsPerShiftAtX1 <= 0 || settings.NonWorkingSpeedup < 1 || settings.MaxTicksPerAdvance < 1)
                throw new ArgumentOutOfRangeException(nameof(settings), "Неверные параметры темпа.");

            secondsPerWorkingMinuteAtX1 = settings.SecondsPerShiftAtX1 / playerSchedule.ShiftLengthMinutes;
            simulation.MonthEnded += _ => monthEndedDuringStep = true;
        }

        public void SetSpeed(int speed)
        {
            if (Array.IndexOf(AllowedSpeeds, speed) < 0)
                throw new ArgumentOutOfRangeException(nameof(speed), "Допустимые скорости: 1, 2, 4, 8.");
            Speed = speed;
        }

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;

        // Продвинуть симуляцию на столько тиков, сколько помещается в прошедшие реальные секунды.
        // Возвращает число выполненных тиков.
        public int Advance(double realSeconds)
        {
            if (IsPaused || realSeconds <= 0) return 0;

            accumulatedSeconds += realSeconds;
            int ticks = 0;
            while (ticks < settings.MaxTicksPerAdvance)
            {
                double cost = SecondsPerTick();
                if (accumulatedSeconds < cost) return ticks;

                accumulatedSeconds -= cost;
                monthEndedDuringStep = false;
                simulation.Step();
                ticks++;

                if (monthEndedDuringStep && settings.AutoPauseAtMonthEnd)
                {
                    IsPaused = true;
                    accumulatedSeconds = 0;
                    AutoPaused?.Invoke(simulation.MonthIndex - 1);
                    return ticks;
                }
            }

            accumulatedSeconds = 0;
            return ticks;
        }

        // Сколько реальных секунд стоит следующая игровая минута при текущей скорости.
        private double SecondsPerTick()
        {
            double seconds = secondsPerWorkingMinuteAtX1 / Speed;
            bool working = playerSchedule.IsWorkingTime(simulation.Calendar.ToDateTime(simulation.Now));
            return working ? seconds : seconds / settings.NonWorkingSpeedup;
        }
    }
}
