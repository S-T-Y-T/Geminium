using System;

namespace Geminium.Core
{
    // Генератор случайных чисел с seed (алгоритм xoshiro256**).
    // Одинаковый seed даёт одинаковую последовательность на любом запуске — это делает
    // прогоны Лаборатории сценариев воспроизводимыми.
    // Подсистемы берут свои потоки через Fork("market"): новая случайность в одной
    // подсистеме не сдвигает числа в другой.
    public sealed class SimRandom
    {
        private ulong s0, s1, s2, s3;

        public ulong Seed { get; }

        public SimRandom(ulong seed)
        {
            Seed = seed;
            ulong x = seed;
            s0 = SplitMix64(ref x);
            s1 = SplitMix64(ref x);
            s2 = SplitMix64(ref x);
            s3 = SplitMix64(ref x);
        }

        // Независимый поток с именем. Зависит только от seed и имени, а не от того,
        // сколько чисел уже взято из родителя.
        public SimRandom Fork(string streamName)
        {
            if (string.IsNullOrEmpty(streamName)) throw new ArgumentException("Нужно имя потока.", nameof(streamName));
            ulong x = Seed ^ Fnv1a64(streamName);
            return new SimRandom(SplitMix64(ref x));
        }

        public ulong NextULong()
        {
            ulong result = RotateLeft(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = RotateLeft(s3, 45);
            return result;
        }

        // Число от 0 (включительно) до 1 (не включительно).
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        // Целое от minInclusive до maxExclusive без перекоса в сторону малых значений.
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Верхняя граница должна быть больше нижней.");
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue / range * range;
            ulong value;
            do value = NextULong(); while (value >= limit);
            return (int)(minInclusive + (long)(value % range));
        }

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        public bool Chance(double probability) => NextDouble() < probability;

        // Нормальное распределение (ξ ~ N(0, 1) в формулах GDD), преобразование Бокса — Мюллера.
        public double NextGaussian(double mean = 0, double stdDev = 1)
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static ulong SplitMix64(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        // Стабильный хеш имени: string.GetHashCode меняется между запусками, он не подходит.
        private static ulong Fnv1a64(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
