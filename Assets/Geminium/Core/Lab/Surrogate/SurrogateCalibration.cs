using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Time;

namespace Geminium.Core.Lab.Surrogate
{
    // Записывает месяцы полного цеха для калибровки суррогата. Стоит последней подсистемой — после финансов.
    public sealed class SurrogateRecorder : SimulationSystem
    {
        private readonly GameWorld world;
        public List<FactorySample> Samples { get; } = new List<FactorySample>();

        public SurrogateRecorder(GameWorld world) { this.world = world; }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            if (monthIndex == 0) return; // первый месяц — стройка цеха и наём, не типичный месяц
            var catalog = world.Catalog;
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt || company.LastReport == null || company.LastReport.Month != monthIndex) continue;
                var report = company.LastReport;
                var schedule = company.Schedule;
                var s = new FactorySample
                {
                    Month = monthIndex,
                    CompanyId = company.Id,
                    Archetype = company.Controller is BotController bot ? bot.ArchetypeIndex : 0,
                    WorkMinutes = schedule.ShiftsPerDay * schedule.ShiftLengthMinutes * (double)schedule.CountWorkingDays(Simulation.Calendar, monthIndex),
                    QualityMaterials = company.Controller is BotController b && b.Knobs.QualityMaterials,
                    QcStation = company.HasQcStation && company.CountWorkers(catalog.Staff.QcInspectorProfessionId) > 0,
                    Output = report.UnitsProduced + report.UnitsDefective,
                    Defective = report.UnitsDefective,
                    Energy = report.Energy,
                    Maintenance = -company.Ledger.MonthTotal(monthIndex, LedgerCategory.Maintenance),
                };
                foreach (var machine in company.Machines)
                {
                    var def = catalog.Machine(machine.DefId);
                    s.ZonePower[(int)def.Zone] += def.Power;
                    s.ZoneMachines[(int)def.Zone]++;
                }
                var operators = company.Workers.Where(w => catalog.Profession(w.ProfessionId).OperatedZone != ProductionZone.None).ToList();
                foreach (var w in operators) s.ZoneOperators[(int)catalog.Profession(w.ProfessionId).OperatedZone]++;
                s.AverageExperience = operators.Count > 0 ? operators.Average(w => w.Experience) : 30;
                foreach (var product in world.Rules.OpenProducts(catalog))
                {
                    int plan = company.MonthlyPlan(product.Id);
                    if (plan > 0) s.Plan[product.Id] = plan;
                }
                if (s.Plan.Count > 0) Samples.Add(s);
            }
        }
    }

    // Калибровка суррогата по прогонам полного цеха (GDD 8.2, пункт 3.4).
    // Выпуск: U и F — перебором по сетке, минимум суммы квадратов ошибок; брак: множитель m — МНК через ноль;
    // энергия и ТО — отношение сумм. Качество подгонки — R² по выпуску и браку.
    public static class SurrogateCalibration
    {
        // Данные: прогоны стандартной партии с разными seed — скриптовыми ботами и ботами со случайной политикой
        // (случайные решения разнообразят план, цены и расширение цеха).
        public static List<FactorySample> Collect(DataCatalog catalog, int runs, int months, ulong firstSeed = 100)
        {
            var samples = new List<FactorySample>();
            for (int i = 0; i < runs; i++)
            {
                ulong seed = firstSeed + (ulong)i;
                IBotPolicy policy = i % 2 == 0 ? null : new RandomPolicy(new SimRandom(seed * 7919));
                var world = ScenarioRunner.CreateStandardWorld(catalog, seed, policy);
                var recorder = new SurrogateRecorder(world);
                world.Simulation.AddSystem(recorder);
                world.Simulation.RunMonths(months);
                samples.AddRange(recorder.Samples);
            }
            return samples;
        }

        public static SurrogateSettings Fit(IReadOnlyList<FactorySample> samples, DataCatalog catalog)
        {
            if (samples.Count == 0) throw new ArgumentException("Нет данных для калибровки.", nameof(samples));
            var result = new SurrogateSettings { CalibrationSamples = samples.Count };

            // U до 1,5: опытный рабочий быстрее нормы (Eff = 0,5 + опыт/100 > 1).
            const int uMin = 20, uMax = 150, fMin = 50, fMax = 100;
            var errors = new double[uMax + 1, fMax + 1];
            double bestError = double.MaxValue;
            for (int u = uMin; u <= uMax; u++)
            for (int f = fMin; f <= fMax; f++)
            {
                double error = 0;
                foreach (var s in samples)
                {
                    double e = s.Plan.Values.Sum() * SurrogateFactory.PlanShare(s, catalog, u / 100.0, f / 100.0) - s.Output;
                    error += e * e;
                }
                errors[u, f] = error;
                if (error < bestError)
                {
                    bestError = error;
                    result.PlanFulfilment = f / 100.0;
                }
            }
            // Если мощность в данных почти не ограничивает выпуск, ошибка перестаёт зависеть от U.
            // Тогда берём наименьшее U с той же ошибкой (в пределах 0,1%) — осторожную оценку мощности.
            int bestF = (int)Math.Round(result.PlanFulfilment * 100);
            for (int u = uMin; u <= uMax; u++)
                if (errors[u, bestF] <= bestError * 1.001)
                {
                    result.Utilization = u / 100.0;
                    break;
                }
            result.OutputR2 = RSquared(samples.Select(s => s.Output).ToList(), samples.Select(s => SurrogateFactory.PredictOutput(s, catalog, result)).ToList());

            // Брак при фактическом выпуске: множитель через ноль.
            var basePrediction = samples.Select(s => SurrogateFactory.PredictDefective(s, s.Output, catalog, 1)).ToList();
            double num = 0, den = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                num += basePrediction[i] * samples[i].Defective;
                den += basePrediction[i] * basePrediction[i];
            }
            result.DefectMultiplier = den > 0 ? num / den : 1;
            result.DefectR2 = RSquared(samples.Select(s => s.Defective).ToList(), basePrediction.Select(x => x * result.DefectMultiplier).ToList());

            double minutes = samples.Sum(s => SurrogateFactory.OutputMinutes(s, s.Output, catalog));
            result.EnergyPerMinute = minutes > 0 ? samples.Sum(s => s.Energy) / minutes : 0;
            double output = samples.Sum(s => s.Output);
            result.MaintenancePerUnit = output > 0 ? samples.Sum(s => s.Maintenance) / output : 0;
            return result;
        }

        public static double RSquared(IReadOnlyList<double> actual, IReadOnlyList<double> predicted)
        {
            double mean = actual.Average();
            double total = 0, residual = 0;
            for (int i = 0; i < actual.Count; i++)
            {
                total += (actual[i] - mean) * (actual[i] - mean);
                residual += (actual[i] - predicted[i]) * (actual[i] - predicted[i]);
            }
            return total > 0 ? 1 - residual / total : 0;
        }
    }

    // Случайная политика: равномерно по разрешённым действиям каждой ветки. Для разнообразия данных и тестов.
    public sealed class RandomPolicy : IBotPolicy
    {
        private readonly SimRandom random;

        public RandomPolicy(SimRandom random) { this.random = random ?? throw new ArgumentNullException(nameof(random)); }

        public int[] Decide(float[] observation, bool[] mask)
        {
            var actions = new int[BotActions.BranchSizes.Length];
            int offset = 0;
            for (int branch = 0; branch < actions.Length; branch++)
            {
                int size = BotActions.BranchSizes[branch];
                var allowed = Enumerable.Range(0, size).Where(a => mask[offset + a]).ToList();
                actions[branch] = allowed.Count == 0 ? 0 : allowed[random.NextInt(0, allowed.Count)];
                offset += size;
            }
            return actions;
        }
    }
}
