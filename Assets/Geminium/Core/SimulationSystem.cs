using System;
using Geminium.Core.Time;

namespace Geminium.Core
{
    // Подсистема симуляции: рынок, склад, финансы и т.д.
    // Симуляция вызывает подсистемы строго в порядке их добавления.
    public abstract class SimulationSystem
    {
        protected Simulation Simulation { get; private set; }

        internal void Attach(Simulation simulation)
        {
            if (Simulation != null) throw new InvalidOperationException("Подсистема уже добавлена в симуляцию.");
            Simulation = simulation;
            OnAttached();
        }

        protected virtual void OnAttached() { }

        protected internal virtual void OnMonthStarted(int monthIndex) { }
        protected internal virtual void OnDayStarted(long dayIndex) { }
        protected internal virtual void OnMinute(SimTime now) { }
        protected internal virtual void OnDayEnded(long dayIndex) { }
        protected internal virtual void OnMonthEnded(int monthIndex) { }
    }
}
