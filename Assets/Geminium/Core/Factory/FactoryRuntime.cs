using System;
using System.Collections.Generic;
using Geminium.Core.Companies;
using Geminium.Core.Staff;

namespace Geminium.Core.Factory
{
    // Причины простоя станка (GDD 7.10: простои по причинам). Значения закреплены.
    public enum DowntimeCause
    {
        NoOperator = 0,   // нет оператора на смене
        NoMaterials = 1,  // нечего делать: нет сырья или полуфабриката
        Broken = 2,       // поломка, внеплановый ремонт
        Maintenance = 3,  // плановое ТО
    }

    // Единица продукции в работе: какие операции пройдены и из чего складывается её качество.
    public sealed class WorkUnit
    {
        public string ProductId;
        public int NextStep;              // индекс следующей операции рецепта
        public double RawQualitySum;
        public int RawQualityCount;
        public double SkillSum;
        public double ConditionSum;
        public int OperationsDone;
        public double MaterialCost;
        public bool Defective;
        public bool IsRework;
        public long ReadySince;           // минута, с которой ждёт перевозки

        public double Quality
        {
            get
            {
                double raw = RawQualityCount > 0 ? RawQualitySum / RawQualityCount : 50;
                double skill = OperationsDone > 0 ? SkillSum / OperationsDone : 50;
                double condition = OperationsDone > 0 ? ConditionSum / OperationsDone : 100;
                return Formulas.Clamp(Formulas.UnitQuality(raw, skill, condition), 0, 100);
            }
        }
    }

    // Состояние станка во время игры: износ, ТО, текущая операция, буферы и учёт простоев.
    public sealed class MachineRuntime
    {
        public double Condition = 100;
        public int OperationsSinceMaintenance;
        public int MaintenanceInterval;
        public bool Broken;
        public long DownUntil = -1;                 // до какой минуты станок стоит (ремонт или ТО)
        public DowntimeCause DownCause;
        public WorkUnit Current;
        public int CurrentStep;                     // какую операцию рецепта делает
        public double Progress;
        public double RequiredMinutes;
        public readonly Inventory Materials = new Inventory();     // сырьё у станка
        public readonly List<WorkUnit> Input = new List<WorkUnit>();  // полуфабрикаты, ждущие обработки здесь
        public readonly List<WorkUnit> Output = new List<WorkUnit>(); // ждут перевозки

        // Учёт за текущий и прошлый месяц / неделю.
        public long AvailableMinutes, BusyMinutes;
        public long WeekAvailableMinutes, WeekBusyMinutes;
        public long LastWeekAvailableMinutes, LastWeekBusyMinutes;
        public readonly long[] Downtime = new long[4];
        public int Breakdowns;
        public int Maintenances;

        // Загрузка за прошлую неделю (0–1) — для графика такта и поиска узкого места.
        public double LastWeekLoad => LastWeekAvailableMinutes > 0 ? (double)LastWeekBusyMinutes / LastWeekAvailableMinutes : 0;
    }

    public enum TransportKind
    {
        Material = 0,   // сырьё со склада к станку
        Wip = 1,        // полуфабрикат от станка к станку
        Finished = 2,   // готовая продукция на склад
    }

    // Заявка на перевозку. Разнорабочие берут их по очереди (первым пришёл — первым обслужен).
    public sealed class TransportRequest
    {
        public TransportKind Kind;
        public int FromObjectId = -1;   // −1 — склад
        public int ToObjectId = -1;     // −1 — склад
        public string MaterialId;
        public double Quantity;         // сырьё
        public string ProductId;        // полуфабрикат
        public int Step;
        public int Units;
        public string Key;              // чтобы не дублировать заявки
    }

    // Перевозка, которую сейчас выполняет разнорабочий.
    public sealed class TransportTask
    {
        public TransportRequest Request;
        public List<int> Path = new List<int>();   // клетки маршрута (к месту погрузки и дальше к месту выгрузки)
        public int LoadIndex;                      // где в Path заканчивается дорога к месту погрузки
        public long StartMinute;
        public long EndMinute;
        public string MaterialId;                  // груз: сырьё
        public double Quantity;
        public double Quality;
        public double UnitCost;
        public readonly List<WorkUnit> Units = new List<WorkUnit>(); // груз: единицы продукции
    }

    // Производственное состояние компании: заявки, учёт за месяц, диаграмма спагетти.
    public sealed class ProductionState
    {
        public readonly List<TransportRequest> Queue = new List<TransportRequest>();
        public readonly HashSet<string> PendingKeys = new HashSet<string>();
        public readonly Dictionary<string, int> StartedThisMonth = new Dictionary<string, int>();
        public readonly List<WorkUnit> Rework = new List<WorkUnit>();
        public int[] SpaghettiWeek = new int[0];
        public int[] SpaghettiLastWeek = new int[0];
        public double EnergyKwhToday;
        public double LogisticsCellsThisMonth;
        public long LastWorkingMinute = -1;
        public int QcCapacityUsedToday;
        public int InspectorsOnShift;
        internal readonly Dictionary<int, Worker> OperatorsNow = new Dictionary<int, Worker>();
        internal readonly List<Worker> LaborersNow = new List<Worker>();

        internal void EnsureSpaghetti(int cells)
        {
            if (SpaghettiWeek.Length == cells) return;
            Array.Resize(ref SpaghettiWeek, cells);
            Array.Resize(ref SpaghettiLastWeek, cells);
        }
    }
}
