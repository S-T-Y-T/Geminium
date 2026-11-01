using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;

namespace Geminium.Core.Production
{
    // Производство этапа 1 — заглушка до пространственного цеха (TODO 1.11, этап 2).
    // Каждый рабочий день: мощность станков зоны × рабочие минуты графика = минуты станков.
    // Продукты делаются по порядку данных, пока хватает минут всех зон рецепта, сырья и плана.
    // Качество и брак — по формулам GDD 7.6.4 с навыком и состоянием станка из настроек.
    // Брак списывается, его сырьё ложится в себестоимость годных единиц.
    // Раз в месяц — зарплаты: оператор на каждый станок в каждой смене + разнорабочие.
    public sealed class ProductionSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly ProductionSettings settings;
        private SimRandom random;

        public ProductionSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Production;
        }

        protected override void OnAttached() => random = Simulation.Random.Fork("production");

        private EconomySystem Economy => world.Economy;

        protected internal override void OnDayEnded(long dayIndex)
        {
            DateTime date = Simulation.Calendar.StartDate.AddDays(dayIndex);
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                var schedule = company.Schedule;
                if (!schedule.IsWorkingDay(date.DayOfWeek) || company.Machines.Count == 0) continue;

                double minutes = schedule.ShiftsPerDay * schedule.ShiftLengthMinutes;
                PayEnergy(company, minutes);
                ProduceDay(company, minutes * Economy.Multiplier(EventEffect.ProductionCapacity));
            }
        }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var company in world.Companies)
                if (!company.IsBankrupt) company.Pay(LedgerCategory.Wages, MonthlyPayroll(company));
        }

        // Фонд оплаты труда за месяц с учётом инфляции.
        public long MonthlyPayroll(Company company)
        {
            double payroll = 0;
            if (settings.BaseLaborers > 0)
                payroll += settings.BaseLaborers * catalog.Profession(settings.LaborProfessionId).BaseMonthlyWage;
            foreach (var owned in company.Machines)
            {
                var operatorProfession = catalog.OperatorOf(catalog.Machine(owned.EquipmentId).Zone);
                if (operatorProfession != null) payroll += company.Schedule.ShiftsPerDay * operatorProfession.BaseMonthlyWage;
            }
            return (long)Math.Round(payroll * Economy.PriceLevel);
        }

        private void PayEnergy(Company company, double minutes)
        {
            double kwh = company.Machines.Sum(m => catalog.Machine(m.EquipmentId).EnergyKw) * minutes / 60;
            company.Pay(LedgerCategory.Energy, (long)Math.Round(kwh * settings.EnergyPricePerKwh * Economy.PriceLevel));
        }

        private void ProduceDay(Company company, double minutes)
        {
            var zoneMinutes = new Dictionary<ProductionZone, double>();
            foreach (var owned in company.Machines)
            {
                var machine = catalog.Machine(owned.EquipmentId);
                zoneMinutes.TryGetValue(machine.Zone, out double current);
                zoneMinutes[machine.Zone] = current + machine.Power * minutes;
            }

            double efficiency = Formulas.WorkerEfficiency(settings.DefaultSkill, 0, 1);
            foreach (var product in catalog.Products)
            {
                if (!product.UnlockedByDefault) continue;
                company.CurrentMonth.Produced.TryGetValue(product.Id, out long produced);
                company.CurrentMonth.Defective.TryGetValue(product.Id, out long defective);
                long remaining = company.MonthlyPlan(product.Id) - produced - defective;
                if (remaining <= 0) continue;

                // Минуты станков на единицу по зонам и расход сырья на единицу.
                var perUnit = new Dictionary<ProductionZone, double>();
                var inputs = new SortedDictionary<string, double>(StringComparer.Ordinal);
                foreach (var op in product.Operations)
                {
                    perUnit.TryGetValue(op.Zone, out double time);
                    perUnit[op.Zone] = time + op.BaseTimeMinutes / efficiency;
                    foreach (var input in op.Inputs)
                    {
                        inputs.TryGetValue(input.MaterialId, out double amount);
                        inputs[input.MaterialId] = amount + input.Amount;
                    }
                }

                long units = remaining;
                foreach (var pair in perUnit)
                {
                    zoneMinutes.TryGetValue(pair.Key, out double available);
                    units = Math.Min(units, (long)Math.Floor(available / pair.Value + 1e-9));
                }
                foreach (var pair in inputs)
                    units = Math.Min(units, (long)Math.Floor(company.Inventory.MaterialQuantity(pair.Key) / pair.Value + 1e-9));
                if (units <= 0) continue;

                double materialCost = 0, qualitySum = 0;
                foreach (var pair in inputs)
                {
                    company.Inventory.TryTakeMaterial(pair.Key, pair.Value * units, out double quality, out double unitCost);
                    materialCost += pair.Value * units * unitCost;
                    qualitySum += quality;
                }
                foreach (var pair in perUnit) zoneMinutes[pair.Key] -= pair.Value * units;

                double rawQuality = inputs.Count > 0 ? qualitySum / inputs.Count : 50;
                double defectChance = Formulas.DefectChance(product.BaseDefectChance, settings.DefaultSkill, rawQuality,
                    settings.DefaultMachineCondition, 0, settings.WearExponent);
                long defects = 0;
                for (long i = 0; i < units; i++)
                    if (random.Chance(defectChance)) defects++;
                long good = units - defects;

                MonthStats.Add(company.CurrentMonth.Produced, product.Id, good);
                MonthStats.Add(company.CurrentMonth.Defective, product.Id, defects);
                if (good > 0)
                {
                    double quality = Formulas.UnitQuality(rawQuality, settings.DefaultSkill, settings.DefaultMachineCondition);
                    company.Inventory.AddProducts(product.Id, good, Formulas.Clamp(quality, 0, 100), materialCost / good);
                }
                else
                {
                    company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(materialCost); // весь выпуск в брак — списание
                }
            }
        }
    }
}
