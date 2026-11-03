using System;

namespace Geminium.Core.Lab
{
    // Стохастический поиск восхождением (алгоритм из исходного ТЗ, GDD 8.2): раз в месяц одна ручка
    // решения сдвигается на небольшой шаг; если цель (прибыль месяца) выросла — сдвиг остаётся, иначе откатывается.
    public sealed class HillClimber
    {
        private readonly double[] values;
        private readonly double[] min;
        private readonly double[] max;
        private readonly double step;
        private readonly SimRandom random;
        private int pendingIndex = -1;
        private double pendingDelta;
        private double? reference;

        public int Accepted { get; private set; }
        public int Reverted { get; private set; }

        public HillClimber(double[] start, double[] min, double[] max, double step, SimRandom random)
        {
            values = (double[])start.Clone();
            this.min = min;
            this.max = max;
            this.step = step;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public double this[int index] => values[index];

        // Итог месяца, прожитого с текущими значениями: сохранить или откатить прошлый сдвиг и сделать новый.
        public void Step(double objective)
        {
            if (pendingIndex >= 0 && reference.HasValue && objective < reference.Value)
            {
                values[pendingIndex] -= pendingDelta;
                Reverted++;
            }
            else
            {
                if (pendingIndex >= 0) Accepted++;
                reference = objective;
            }

            int index = random.NextInt(0, values.Length);
            double sign = random.Chance(0.5) ? 1 : -1;
            double next = Formulas.Clamp(values[index] + sign * step * (max[index] - min[index]), min[index], max[index]);
            pendingDelta = next - values[index];
            pendingIndex = pendingDelta == 0 ? -1 : index;
            values[index] = next;
        }
    }
}
