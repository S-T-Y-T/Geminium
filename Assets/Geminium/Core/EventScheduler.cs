using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core
{
    // Отложенные события: «в такую-то минуту сделать то-то» — поставка, платёж по кредиту.
    // События одной минуты выполняются в порядке постановки, это нужно для детерминизма.
    // Событие на уже прошедшую минуту выполнится в ближайшую обработку.
    public sealed class EventScheduler
    {
        private readonly List<ScheduledEvent> heap = new List<ScheduledEvent>();
        private long nextOrder;

        public int PendingCount => heap.Count;

        public ScheduledEvent Schedule(SimTime at, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var scheduled = new ScheduledEvent(at, nextOrder++, action);
            heap.Add(scheduled);
            SiftUp(heap.Count - 1);
            return scheduled;
        }

        // Выполнить все события, чьё время наступило.
        public void RunDue(SimTime now)
        {
            while (heap.Count > 0 && heap[0].At <= now)
            {
                ScheduledEvent next = PopMin();
                if (!next.IsCancelled) next.Action();
            }
        }

        private ScheduledEvent PopMin()
        {
            ScheduledEvent min = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            if (heap.Count > 0) SiftDown(0);
            return min;
        }

        private static bool Earlier(ScheduledEvent a, ScheduledEvent b) =>
            a.At < b.At || (a.At == b.At && a.Order < b.Order);

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Earlier(heap[i], heap[parent])) break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int left = 2 * i + 1, right = left + 1, smallest = i;
                if (left < heap.Count && Earlier(heap[left], heap[smallest])) smallest = left;
                if (right < heap.Count && Earlier(heap[right], heap[smallest])) smallest = right;
                if (smallest == i) return;
                (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
                i = smallest;
            }
        }
    }

    // Запланированное событие; его можно отменить до срабатывания.
    public sealed class ScheduledEvent
    {
        public SimTime At { get; }
        public bool IsCancelled { get; private set; }
        internal long Order { get; }
        internal Action Action { get; }

        internal ScheduledEvent(SimTime at, long order, Action action)
        {
            At = at;
            Order = order;
            Action = action;
        }

        public void Cancel() => IsCancelled = true;
    }
}
