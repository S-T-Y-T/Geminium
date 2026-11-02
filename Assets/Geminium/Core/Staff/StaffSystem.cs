using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;

namespace Geminium.Core.Staff
{
    // Персонал и рынок труда (GDD 7.7).
    // Начало месяца: новый пул кандидатов. Чем больше наняли все компании в прошлом месяце,
    // тем меньше кандидатов и тем выше их ожидания (дефицит кадров).
    // Начало дня: кто отсутствует (эпидемия, отключения — события с эффектом ProductionCapacity).
    // Конец месяца: зарплаты с переработками, мораль, увольнения по собственному желанию.
    // Усталость и опыт меняет производство — оно знает, кто сейчас на смене.
    public sealed class StaffSystem : SimulationSystem
    {
        private const double HoursPerMonth = 176;
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly StaffSettings settings;
        private readonly List<Candidate> candidates = new List<Candidate>();
        private readonly Dictionary<string, int> hiresThisMonth = new Dictionary<string, int>();
        private readonly Dictionary<string, int> hiresLastMonth = new Dictionary<string, int>();
        private readonly Dictionary<int, object> reservations = new Dictionary<int, object>();
        private int nextWorkerId = 1;
        private int nextCandidateId = 1;
        private SimRandom random;
        private SimRandom absenceRandom;

        public IReadOnlyList<Candidate> Candidates => candidates;

        public StaffSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Staff;
        }

        private EconomySystem Economy => world.Economy;

        protected override void OnAttached()
        {
            random = Simulation.Random.Fork("staff");
            absenceRandom = Simulation.Random.Fork("staff.absence");
        }

        // Рыночная зарплата профессии при таком опыте: растёт с опытом, инфляцией и в бум.
        public long MarketWage(string professionId, double experience) =>
            (long)Math.Round(catalog.Profession(professionId).BaseMonthlyWage * (0.8 + experience * settings.WageExperienceFactor)
                             * Economy.PriceLevel * (0.9 + 0.1 * Economy.Index));

        protected internal override void OnMonthStarted(int monthIndex)
        {
            hiresLastMonth.Clear();
            foreach (var pair in hiresThisMonth) hiresLastMonth[pair.Key] = pair.Value;
            hiresThisMonth.Clear();

            candidates.Clear();
            reservations.Clear();
            foreach (var profession in catalog.Professions)
            {
                hiresLastMonth.TryGetValue(profession.Id, out int hired);
                int count = Math.Max(1, (int)Math.Round(settings.CandidatesPerProfession * (2 - Economy.Index)) - hired / 2);
                double shortage = 1 + Math.Min(0.5, 0.05 * hired);
                for (int i = 0; i < count; i++)
                {
                    double experience = Formulas.Clamp(random.NextGaussian(settings.CandidateExperienceMean, settings.CandidateExperienceSd), 1, 100);
                    double spread = random.Range(0.9, 1.1);
                    double stamina = random.Range(0.8, 1.2);
                    double loyalty = random.Range(0.8, 1.2);
                    long wage = (long)Math.Round(MarketWage(profession.Id, experience) * shortage * spread);
                    candidates.Add(new Candidate(nextCandidateId++, profession.Id, experience, wage, stamina, loyalty));
                }
            }
        }

        protected internal override void OnDayStarted(long dayIndex)
        {
            double capacity = Economy.Multiplier(EventEffect.ProductionCapacity);
            foreach (var company in world.Companies)
            foreach (var worker in company.workers)
            {
                double roll = absenceRandom.NextDouble();
                worker.AbsentToday = capacity < 1 && roll < 1 - capacity;
                worker.WorkedOvertimeToday = false;
            }
        }

        protected internal override void OnDayEnded(long dayIndex)
        {
            foreach (var company in world.Companies)
            foreach (var worker in company.workers)
                if (worker.WorkedOvertimeToday) worker.OvertimeDaysThisMonth++;
        }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                PayWages(company);
                UpdateMorale(company);
            }
        }

        private void PayWages(Company company)
        {
            double total = 0;
            foreach (var worker in company.workers)
            {
                total += worker.MonthlyWage;
                total += worker.OvertimeHoursThisMonth * worker.MonthlyWage / HoursPerMonth * settings.OvertimePayFactor;
            }
            company.Pay(LedgerCategory.Wages, (long)Math.Round(total));
        }

        private void UpdateMorale(Company company)
        {
            foreach (var worker in company.workers.ToList())
            {
                double market = MarketWage(worker.ProfessionId, worker.Experience);
                double gapPercent = Math.Max(0, (market - worker.MonthlyWage) / market * 100);
                double loss = gapPercent * settings.MoraleWageGapFactor
                              + Math.Max(0, worker.OvertimeDaysThisMonth - settings.OvertimeDaysTolerance) * settings.MoralePerExcessOvertimeDay
                              + worker.BreakdownsThisMonth * settings.MoralePerBreakdown;
                worker.Morale = Formulas.Clamp(worker.Morale - loss * (2 - worker.Loyalty) + settings.MoraleRecoveryPerMonth, 0, 100);
                worker.OvertimeDaysThisMonth = 0;
                worker.OvertimeHoursThisMonth = 0;
                worker.BreakdownsThisMonth = 0;

                if (worker.Morale < settings.QuitMorale)
                {
                    company.workers.Remove(worker);
                    company.CurrentMonth.Quits++;
                }
            }
        }

        // Кандидат зарезервирован командой найма, пока она ждёт применения.
        internal bool IsReservedByOther(int candidateId, object command) =>
            reservations.TryGetValue(candidateId, out var owner) && owner != command;

        internal void Reserve(int candidateId, object command) => reservations[candidateId] = command;

        internal Worker Hire(Company company, Candidate candidate)
        {
            candidates.Remove(candidate);
            reservations.Remove(candidate.Id);
            var worker = new Worker(nextWorkerId++, candidate.ProfessionId, candidate.Experience, candidate.ExpectedWage,
                candidate.Stamina, candidate.Loyalty, settings.StartingMorale, Simulation.MonthIndex);
            var (gx, gy) = company.Floor.Gate;
            worker.PositionCell = company.Floor.Index(gx, gy);
            company.workers.Add(worker);
            hiresThisMonth.TryGetValue(candidate.ProfessionId, out int hired);
            hiresThisMonth[candidate.ProfessionId] = hired + 1;
            company.CurrentMonth.Hires++;
            return worker;
        }

        // Увольнение: выходное пособие, груз разнорабочего возвращается в цех.
        internal void Fire(Company company, Worker worker)
        {
            company.Pay(LedgerCategory.Wages, (long)Math.Round(worker.MonthlyWage * settings.SeveranceMonths));
            world.Production.AbandonTask(company, worker);
            company.workers.Remove(worker);
        }

        internal void SetWage(Worker worker, long wage)
        {
            double raisePercent = (wage - worker.MonthlyWage) * 100.0 / Math.Max(1, worker.MonthlyWage);
            if (raisePercent > 0) worker.Morale = Formulas.Clamp(worker.Morale + raisePercent * settings.MoraleWageGapFactor, 0, 100);
            worker.MonthlyWage = wage;
        }

        internal void Train(Company company, Worker worker)
        {
            company.Pay(LedgerCategory.Training, (long)Math.Round(settings.TrainingCost * Economy.PriceLevel));
            worker.Experience = Math.Min(100, worker.Experience + settings.TrainingExperienceGain);
        }

        // Минута работы: опыт растёт, усталость копится (сильнее в переработку).
        internal void Work(Worker worker, bool overtime)
        {
            worker.Experience = Math.Min(100, worker.Experience + settings.ExperiencePerHour / 60);
            double rate = settings.FatiguePerHour / 60 * worker.Stamina * (overtime ? settings.OvertimeFatigueFactor : 1);
            worker.Fatigue = Math.Min(100, worker.Fatigue + rate);
            if (overtime)
            {
                worker.OvertimeHoursThisMonth += 1.0 / 60;
                worker.WorkedOvertimeToday = true;
            }
        }

        // Отдых: усталость спадает.
        internal void Rest(Worker worker, double minutes) =>
            worker.Fatigue = Math.Max(0, worker.Fatigue - settings.FatigueRecoveryPerHour / 60 * minutes);
    }
}
