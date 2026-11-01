using System;

namespace Geminium.Core.Time
{
    // Момент игрового времени: число игровых минут от начала симуляции.
    // Одна игровая минута — один тик. Сутки начинаются в полночь.
    public readonly struct SimTime : IEquatable<SimTime>, IComparable<SimTime>
    {
        public const int MinutesPerHour = 60;
        public const int MinutesPerDay = 24 * MinutesPerHour;

        public readonly long Minute;

        public SimTime(long minute)
        {
            if (minute < 0) throw new ArgumentOutOfRangeException(nameof(minute), "Время не может быть отрицательным.");
            Minute = minute;
        }

        public static SimTime Zero => new SimTime(0);

        public long DayIndex => Minute / MinutesPerDay;
        public int MinuteOfDay => (int)(Minute % MinutesPerDay);
        public bool IsStartOfDay => MinuteOfDay == 0;

        public SimTime AddMinutes(long minutes) => new SimTime(Minute + minutes);

        public bool Equals(SimTime other) => Minute == other.Minute;
        public override bool Equals(object obj) => obj is SimTime other && Equals(other);
        public override int GetHashCode() => Minute.GetHashCode();
        public int CompareTo(SimTime other) => Minute.CompareTo(other.Minute);

        public static bool operator ==(SimTime a, SimTime b) => a.Minute == b.Minute;
        public static bool operator !=(SimTime a, SimTime b) => a.Minute != b.Minute;
        public static bool operator <(SimTime a, SimTime b) => a.Minute < b.Minute;
        public static bool operator >(SimTime a, SimTime b) => a.Minute > b.Minute;
        public static bool operator <=(SimTime a, SimTime b) => a.Minute <= b.Minute;
        public static bool operator >=(SimTime a, SimTime b) => a.Minute >= b.Minute;

        // Только для отладки; тексты интерфейса идут через локализацию.
        public override string ToString() => $"d{DayIndex} {MinuteOfDay / MinutesPerHour:00}:{MinuteOfDay % MinutesPerHour:00}";
    }
}
