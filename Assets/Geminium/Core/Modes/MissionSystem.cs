using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Lab;
using Geminium.Core.Sales;
using Geminium.Core.Time;

namespace Geminium.Core.Modes
{
    public sealed class GoalResult
    {
        public GoalDef Goal;
        public double Actual;   // фактическое значение показателя
        public double Score;    // 0–100
    }

    public sealed class MissionResult
    {
        public string MissionId;
        public double Score;            // среднее баллов целей с весами, 0–100
        public bool Finished;           // срок задания вышел
        public bool Passed;             // срок вышел, балл не ниже проходного, компания жива
        public List<GoalResult> Goals = new List<GoalResult>();
    }

    // Задание кампании (GDD 4.1). Стоит последней подсистемой — после финансов, когда отчёт месяца уже готов.
    // Готовый цех (Layout ≠ Empty) в начале задания собирает бот-помощник по архетипу задания: те же команды,
    // что у ботов, но без записи в журнал — при загрузке сохранения помощник соберёт цех заново.
    // В минуту 0 после сборки задание запоминает стартовую планировку (для цели «Логистика») и ставит износ станков.
    public sealed class MissionSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly Company player;
        private BaselineBotController helper;
        private Dictionary<ProductDef, double> startPlans;

        public MissionDef Mission { get; }
        public double StartLayoutCost { get; private set; }   // путь грузов стартовой планировки, клеток на изделие плана
        public MissionResult Result { get; private set; }     // итог — после последнего месяца задания
        public event Action<MissionResult> Completed;

        public MissionSystem(GameWorld world, Company player, MissionDef mission)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            Mission = mission ?? throw new ArgumentNullException(nameof(mission));
        }

        // Текущая оценка — для экрана задания до конца срока.
        public MissionResult Progress() => Result ?? Evaluate(false);

        protected internal override void OnMonthStarted(int monthIndex)
        {
            if (monthIndex != 0 || Mission.Layout == StartLayout.Empty || !(player.Controller is PlayerController)) return;
            helper = new BaselineBotController(world.Catalog.BotArchetype(Mission.LayoutArchetype)) { NaiveLayout = Mission.Layout == StartLayout.Naive };
            helper.Attach(world, player);
            Unlogged(() => helper.OnMonthStarted(0));
        }

        // Команды минуты 0 (готовый цех) уже применены: запомнить старт.
        protected internal override void OnMinute(SimTime now)
        {
            if (now.Minute != 0) return;
            foreach (var machine in player.Machines) machine.Machine.Condition = Math.Min(machine.Machine.Condition, Mission.StartCondition);
            startPlans = DigitalTwin.Plans(world, player);
            StartLayoutCost = DigitalTwin.LayoutCost(world, player, startPlans);
        }

        // Нанятые в минуту 0 люди выходят после начала дня 0 — помощник ставит их к станкам утром дня 1.
        protected internal override void OnDayStarted(long dayIndex)
        {
            if (helper == null || dayIndex == 0) return;
            Unlogged(helper.AssignOperators);
            helper = null;
        }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            if (monthIndex != Mission.Months - 1 || Result != null) return;
            Result = Evaluate(true);
            Completed?.Invoke(Result);
        }

        private void Unlogged(Action action)
        {
            world.LoggingSuppressed = true;
            try { action(); }
            finally { world.LoggingSuppressed = false; }
        }

        // ---------- Оценка ----------

        private MissionResult Evaluate(bool finished)
        {
            var reports = player.Reports.Where(r => r.Month < Mission.Months).ToList();
            var result = new MissionResult { MissionId = Mission.Id, Finished = finished };
            foreach (var goal in Mission.Goals)
            {
                double actual = Measure(goal, reports);
                result.Goals.Add(new GoalResult { Goal = goal, Actual = actual, Score = GoalScore(goal, actual) });
            }
            double weight = result.Goals.Sum(g => g.Goal.Weight);
            result.Score = player.IsBankrupt || weight <= 0 ? 0 : result.Goals.Sum(g => g.Score * g.Goal.Weight) / weight;
            result.Passed = finished && !player.IsBankrupt && result.Score >= Mission.PassScore;
            return result;
        }

        public static double GoalScore(GoalDef goal, double actual) =>
            100 * Formulas.Clamp((actual - goal.Zero) / (goal.Target - goal.Zero), 0, 1);

        private double Measure(GoalDef goal, List<MonthlyReport> reports)
        {
            var last = reports.Count > 0 ? reports[reports.Count - 1] : null;
            var products = string.IsNullOrEmpty(goal.ProductId) ? Mission.Products : new List<string> { goal.ProductId };
            switch (goal.Metric)
            {
                case GoalMetric.ProducedUnits:
                    return reports.Sum(r => products.Sum(p => r.ProducedByProduct.TryGetValue(p, out long n) ? n : 0));
                case GoalMetric.MaxDefectSharePct:
                {
                    double total = reports.Sum(r => r.UnitsProduced + r.UnitsDefective);
                    return total > 0 ? 100 * reports.Sum(r => r.UnitsDefective) / total : 100;  // ничего не выпущено — нечего оценивать
                }
                case GoalMetric.LogisticsReductionPct:
                {
                    // Веса — план на старте: обнулить план, чтобы «сократить путь», не выйдет.
                    if (startPlans == null || StartLayoutCost <= 0) return 0;
                    return 100 * (StartLayoutCost - DigitalTwin.LayoutCost(world, player, startPlans)) / StartLayoutCost;
                }
                case GoalMetric.NoWarehouseOverflow:
                    return reports.Sum(r => r.OverflowDays);
                case GoalMetric.MaxReturnedUnits:
                    return reports.Sum(r => r.ReturnedUnits);
                case GoalMetric.MaxDowntimeHours:
                    // Простои оборудования: поломки и ТО (простои без людей и сырья — не про оборудование).
                    return reports.Sum(r => r.DowntimeBrokenHours + r.DowntimeMaintenanceHours);
                case GoalMetric.MinMarketSharePct:
                    return last == null ? 0 : 100 * products.Average(p => last.MarketShareByProduct.TryGetValue(p, out double s) ? s : 0);
                case GoalMetric.FederationContractOnTime:
                {
                    // 1 — контракт федерации закрыт в срок; 0,5 + 0,4 × доля поставленного — выигран, но в срок не закрыт;
                    // 0 — не выигран.
                    var federation = player.Sales.Contracts.Where(c => c.Level >= TenderLevel.Regional).ToList();
                    if (federation.Any(c => c.CompletedOnTime)) return 1;
                    return federation.Count == 0 ? 0 : federation.Max(c => 0.5 + 0.4 * c.Delivered / (double)c.Units);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(goal), goal.Metric, "Неизвестный показатель цели.");
            }
        }
    }
}
