using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Data;

namespace Geminium.Core.Lab.Surrogate
{
    // Месяц цеха одной компании глазами суррогатной модели: ресурсы и решения на входе, итоги — на выходе.
    // Из полного цеха его записывает SurrogateRecorder, в быстром мире обучения — заполняет SurrogateWorld.
    public sealed class FactorySample
    {
        public int Month;
        public string CompanyId;
        public int Archetype;
        public double WorkMinutes;                 // рабочих минут одного станка за месяц (смены × длина × рабочие дни)
        public readonly double[] ZonePower = new double[5];     // Σ мощности станков зоны (индекс — ProductionZone)
        public readonly int[] ZoneMachines = new int[5];
        public readonly int[] ZoneOperators = new int[5];
        public double AverageExperience;           // опыт операторов, 0–100
        public bool QualityMaterials;
        public bool QcStation;
        public readonly Dictionary<string, double> Plan = new Dictionary<string, double>();
        // Итоги месяца (из полного цеха).
        public double Output;                      // выпуск: годные + брак
        public double Defective;
        public double Energy;                      // сумы
        public double Maintenance;                 // сумы
    }

    // Формулы суррогата (GDD 8.2). Параметры — SurrogateSettings, их подбирает калибровка.
    //   C_z   = Σ мощность_z × минуты × min(1, операторы_z / станки_z)  — мощность зоны в «базовых минутах»
    //   L_z   = Σ_p план_p × T_op(p, z)                                — нагрузка плана на зону
    //   доля  = min(F, U × min_z C_z / L_z)                            — сколько плана выпускается
    //   брак  = m × Σ_p выпуск_p · p_base_p · (1,5 − опыт/100) · (1,3 − Q_raw/100)
    //   энергия = e × выпуск в базовых минутах; ТО = k × выпуск
    public static class SurrogateFactory
    {
        public static double PlanShare(FactorySample s, DataCatalog catalog, double utilization, double fulfilment)
        {
            double ratio = double.MaxValue;
            for (int z = 1; z < 5; z++)
            {
                double load = ZoneLoad(s, catalog, z);
                if (load <= 0) continue;
                double staffing = s.ZoneMachines[z] == 0 ? 0 : Math.Min(1, s.ZoneOperators[z] / (double)s.ZoneMachines[z]);
                ratio = Math.Min(ratio, utilization * s.ZonePower[z] * s.WorkMinutes * staffing / load);
            }
            return ratio == double.MaxValue ? 0 : Math.Max(0, Math.Min(fulfilment, ratio));
        }

        public static double ZoneLoad(FactorySample s, DataCatalog catalog, int zone)
        {
            double load = 0;
            foreach (var pair in s.Plan)
                foreach (var op in catalog.Product(pair.Key).Operations)
                    if ((int)op.Zone == zone) load += pair.Value * op.BaseTimeMinutes;
            return load;
        }

        public static double PredictOutput(FactorySample s, DataCatalog catalog, SurrogateSettings p) =>
            s.Plan.Values.Sum() * PlanShare(s, catalog, p.Utilization, p.PlanFulfilment);

        // Брак при данном выпуске (выпуск распределён по продуктам как план).
        public static double PredictDefective(FactorySample s, double output, DataCatalog catalog, double multiplier)
        {
            double plan = s.Plan.Values.Sum();
            if (plan <= 0 || output <= 0) return 0;
            double rawQuality = RawQuality(catalog, s.QualityMaterials);
            double weighted = 0;
            foreach (var pair in s.Plan)
                weighted += pair.Value / plan * catalog.Product(pair.Key).BaseDefectChance;
            double factor = (1.5 - s.AverageExperience / 100) * (1.3 - rawQuality / 100);
            return Math.Min(output, multiplier * output * weighted * Math.Max(0, factor));
        }

        // Выпуск в базовых минутах (для энергии): сумма времени операций.
        public static double OutputMinutes(FactorySample s, double output, DataCatalog catalog)
        {
            double plan = s.Plan.Values.Sum();
            if (plan <= 0) return 0;
            double minutes = 0;
            foreach (var pair in s.Plan)
                minutes += pair.Value / plan * catalog.Product(pair.Key).Operations.Sum(o => o.BaseTimeMinutes);
            return output * minutes;
        }

        // Среднее скрытое качество сырья: у самых качественных или у самых дешёвых поставщиков.
        public static double RawQuality(DataCatalog catalog, bool quality)
        {
            var values = new List<double>();
            foreach (var material in catalog.Materials)
            {
                var suppliers = catalog.SuppliersOf(material.Id);
                if (suppliers.Count == 0) continue;
                var chosen = quality
                    ? suppliers.OrderByDescending(x => x.QualityMax).ThenBy(x => x.PriceDeviation).First()
                    : suppliers.OrderBy(x => x.PriceDeviation).ThenByDescending(x => x.Reliability).First();
                values.Add((chosen.QualityMin + chosen.QualityMax) / 2);
            }
            return values.Count == 0 ? 50 : values.Average();
        }
    }
}
