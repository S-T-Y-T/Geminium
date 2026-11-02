using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Geminium.Core.Time;

namespace Geminium.Core.Data
{
    // Проверка данных игры. Возвращает список понятных сообщений об ошибках;
    // пустой список — данные в порядке.
    public static class GameDataValidator
    {
        private static readonly Regex IdFormat = new Regex("^[a-z][a-z0-9_]*$");

        public static List<string> Validate(GameData data)
        {
            var errors = new List<string>();
            if (data == null)
            {
                errors.Add("Данные не заданы.");
                return errors;
            }

            var materialIds = CheckIds(data.Materials, m => m.Id, "Сырьё", errors);
            CheckIds(data.Machines, m => m.Id, "Станок", errors);
            CheckIds(data.Products, p => p.Id, "Продукт", errors);
            var professionIds = CheckIds(data.Professions, p => p.Id, "Профессия", errors);
            var productIds = new HashSet<string>((data.Products ?? new List<ProductDef>()).Where(p => p != null && p.Id != null).Select(p => p.Id));
            CheckIds(data.Suppliers, s => s.Id, "Поставщик", errors);
            CheckIds(data.Events, e => e.Id, "Событие", errors);

            var materials = (data.Materials ?? new List<MaterialDef>()).Where(m => m != null).ToList();
            var machines = (data.Machines ?? new List<MachineDef>()).Where(m => m != null).ToList();
            var products = (data.Products ?? new List<ProductDef>()).Where(p => p != null).ToList();
            var professions = (data.Professions ?? new List<ProfessionDef>()).Where(p => p != null).ToList();
            var suppliers = (data.Suppliers ?? new List<SupplierDef>()).Where(s => s != null).ToList();
            var events = (data.Events ?? new List<EventDef>()).Where(e => e != null).ToList();

            foreach (var m in materials)
            {
                if (m.BasePrice <= 0) errors.Add($"Сырьё «{m.Id}»: цена должна быть больше нуля.");
                if (m.Volatility < 0 || m.Volatility > 1) errors.Add($"Сырьё «{m.Id}»: волатильность — от 0 до 1.");
                if (m.UnitsPerCell <= 0) errors.Add($"Сырьё «{m.Id}»: единиц в ячейке склада должно быть больше нуля.");
                if (m.MarketSupplyPerMonth <= 0) errors.Add($"Сырьё «{m.Id}»: предложение рынка должно быть больше нуля.");
            }

            foreach (var s in suppliers)
            {
                if (!materialIds.Contains(s.MaterialId ?? "")) errors.Add($"Поставщик «{s.Id}»: нет сырья «{s.MaterialId}».");
                if (s.Origin == MaterialOrigin.LocalAndImport) errors.Add($"Поставщик «{s.Id}»: укажите Local или Import.");
                if (s.PriceDeviation <= -1) errors.Add($"Поставщик «{s.Id}»: отклонение цены должно быть больше −1.");
                if (s.Reliability < 0 || s.Reliability > 1) errors.Add($"Поставщик «{s.Id}»: надёжность — от 0 до 1.");
                if (s.QualityMin < 0 || s.QualityMax > 100 || s.QualityMin > s.QualityMax) errors.Add($"Поставщик «{s.Id}»: качество — 0 ≤ мин ≤ макс ≤ 100.");
                if (s.MinBatch <= 0) errors.Add($"Поставщик «{s.Id}»: минимальная партия должна быть больше нуля.");
                if (s.LeadTimeDays < 0) errors.Add($"Поставщик «{s.Id}»: срок поставки не может быть отрицательным.");
            }

            foreach (var e in events)
            {
                if (e.MonthlyProbability < 0 || e.MonthlyProbability > 1) errors.Add($"Событие «{e.Id}»: вероятность — от 0 до 1.");
                if (e.MinDurationMonths < 1 || e.MaxDurationMonths < e.MinDurationMonths) errors.Add($"Событие «{e.Id}»: длительность — 1 ≤ мин ≤ макс.");
                if (e.SeasonStartMonth < 0 || e.SeasonStartMonth > 12 || e.SeasonEndMonth < 0 || e.SeasonEndMonth > 12 || (e.SeasonStartMonth == 0) != (e.SeasonEndMonth == 0))
                    errors.Add($"Событие «{e.Id}»: сезон — месяцы 1–12 или оба 0 (круглый год).");
                if (e.Effect == EventEffect.None) errors.Add($"Событие «{e.Id}»: не указан эффект.");
                bool materialTarget = e.Effect == EventEffect.MaterialPrice;
                bool productTarget = e.Effect == EventEffect.Demand;
                if (!string.IsNullOrEmpty(e.Target) && !(materialTarget && materialIds.Contains(e.Target)) && !(productTarget && productIds.Contains(e.Target)))
                    errors.Add($"Событие «{e.Id}»: цель «{e.Target}» не подходит к эффекту {e.Effect}.");
            }

            foreach (var m in machines)
            {
                if (m.Zone == ProductionZone.None) errors.Add($"Станок «{m.Id}»: не указана зона.");
                if (m.Tier < 1 || m.Tier > 3) errors.Add($"Станок «{m.Id}»: уровень — от 1 до 3.");
                if (m.Power <= 0) errors.Add($"Станок «{m.Id}»: производительность должна быть больше нуля.");
                if (m.EnergyKw < 0) errors.Add($"Станок «{m.Id}»: энергопотребление не может быть отрицательным.");
                if (m.DurabilityOperations <= 0) errors.Add($"Станок «{m.Id}»: ресурс прочности должен быть больше нуля.");
                if (m.Price <= 0) errors.Add($"Станок «{m.Id}»: цена должна быть больше нуля.");
                if (m.SizeX < 1 || m.SizeY < 1) errors.Add($"Станок «{m.Id}»: размер — не меньше 1×1 клетки.");
            }

            var zonesWithMachines = new HashSet<ProductionZone>(machines.Select(m => m.Zone));
            var zonesWithOperators = new HashSet<ProductionZone>(professions.Select(p => p.OperatedZone));

            foreach (var p in products)
            {
                if (p.BasePrice <= 0) errors.Add($"Продукт «{p.Id}»: цена должна быть больше нуля.");
                if (p.BaseDemandPerMonth < 0) errors.Add($"Продукт «{p.Id}»: спрос не может быть отрицательным.");
                if (p.BaseDefectChance < 0 || p.BaseDefectChance > 1) errors.Add($"Продукт «{p.Id}»: шанс брака — от 0 до 1.");
                if (p.UnitsPerCell <= 0) errors.Add($"Продукт «{p.Id}»: единиц в ячейке склада должно быть больше нуля.");
                if (p.Operations == null || p.Operations.Count == 0)
                {
                    errors.Add($"Продукт «{p.Id}»: в рецепте нет операций.");
                    continue;
                }

                for (int i = 0; i < p.Operations.Count; i++)
                {
                    var op = p.Operations[i];
                    string where = $"Продукт «{p.Id}», операция {i + 1}";
                    if (op == null) { errors.Add($"{where}: пустая операция."); continue; }
                    if (op.Zone == ProductionZone.None) errors.Add($"{where}: не указана зона.");
                    else if (!zonesWithMachines.Contains(op.Zone)) errors.Add($"{where}: нет ни одного станка для зоны {op.Zone}.");
                    else if (p.UnlockedByDefault && !zonesWithOperators.Contains(op.Zone)) errors.Add($"{where}: нет профессии, которая работает в зоне {op.Zone}.");
                    if (op.BaseTimeMinutes <= 0) errors.Add($"{where}: время должно быть больше нуля.");

                    foreach (var input in op.Inputs ?? new List<MaterialAmount>())
                    {
                        if (input == null || !materialIds.Contains(input.MaterialId ?? ""))
                            errors.Add($"{where}: нет сырья «{input?.MaterialId}».");
                        else if (input.Amount <= 0)
                            errors.Add($"{where}: расход сырья «{input.MaterialId}» должен быть больше нуля.");
                    }
                }
            }

            foreach (var p in professions)
                if (p.BaseMonthlyWage <= 0) errors.Add($"Профессия «{p.Id}»: зарплата должна быть больше нуля.");

            // У каждого сырья из открытых рецептов должен быть хотя бы один поставщик.
            var suppliedMaterials = new HashSet<string>(suppliers.Select(s => s.MaterialId));
            foreach (var p in products.Where(p => p.UnlockedByDefault && p.Operations != null))
                foreach (var input in p.Operations.Where(o => o?.Inputs != null).SelectMany(o => o.Inputs))
                    if (input != null && materialIds.Contains(input.MaterialId ?? "") && !suppliedMaterials.Contains(input.MaterialId))
                        errors.Add($"Продукт «{p.Id}»: у сырья «{input.MaterialId}» нет поставщика.");

            if (data.Staff != null)
                foreach (var id in new[] { data.Staff.LaborerProfessionId, data.Staff.MechanicProfessionId, data.Staff.QcInspectorProfessionId })
                    if (!professionIds.Contains(id ?? "")) errors.Add($"Персонал: нет профессии «{id}».");

            CheckSettings(data, errors);
            return errors;
        }

        private static void CheckSettings(GameData data, List<string> errors)
        {
            if (data.Calendar == null) errors.Add("Не заданы параметры календаря.");
            else if (data.Calendar.StartMonth < 1 || data.Calendar.StartMonth > 12 || data.Calendar.StartYear < 2000 || data.Calendar.StartYear > 2100)
                errors.Add("Календарь: неверная дата старта.");

            if (data.DefaultWorkSchedule == null) errors.Add("Не задан график работы по умолчанию.");
            else
            {
                try { new WorkSchedule(data.DefaultWorkSchedule); }
                catch (ArgumentException e) { errors.Add("График работы: " + e.Message); }
            }

            if (data.StartingCash <= 0) errors.Add("Стартовый капитал должен быть больше нуля.");

            var economy = data.Economy;
            if (economy == null) errors.Add("Не заданы параметры экономики.");
            else if (economy.IndexMin <= 0 || economy.IndexMin > economy.IndexMax || economy.InitialIndex < economy.IndexMin || economy.InitialIndex > economy.IndexMax
                     || economy.KeyRateMin < 0 || economy.KeyRateMin > economy.KeyRateMax || economy.SeasonalityAmplitude < 0 || economy.SeasonalityAmplitude >= 1
                     || economy.SeasonalityPeakMonth < 1 || economy.SeasonalityPeakMonth > 12 || economy.CapitalizationMultiplierMonths < 0)
                errors.Add("Экономика: неверные параметры (индекс, ставка, сезонность или мультипликатор).");

            var market = data.Market;
            if (market == null) errors.Add("Не заданы параметры рынка.");
            else if (market.QualityWeight < 0 || market.ReputationWeight < 0 || market.QualityWeight + market.ReputationWeight <= 0
                     || market.PriceElasticity < 0 || market.MarketSizeElasticity < 0 || market.DefaultReputation < 0 || market.DefaultReputation > 100)
                errors.Add("Рынок: неверные параметры.");

            var finance = data.Finance;
            if (finance == null) errors.Add("Не заданы параметры финансов.");
            else if (finance.ProfitTaxRate < 0 || finance.ProfitTaxRate >= 1 || finance.SocialTaxRate < 0 || finance.SocialTaxRate >= 1
                     || finance.DepreciationMonths < 1 || finance.MaxLoanTermMonths < 1 || finance.MaxDebt < 0
                     || finance.NegativeCashMonthsToBankrupt < 1 || finance.MaxOverdueMonths < 0)
                errors.Add("Финансы: неверные параметры.");

            var supply = data.Supply;
            if (supply == null) errors.Add("Не заданы параметры снабжения.");
            else if (supply.MaxLoyaltyDiscount < 0 || supply.MaxLoyaltyDiscount >= 1 || supply.PriceRatioMin <= 0 || supply.PriceRatioMin > supply.PriceRatioMax
                     || supply.MaxDelayDays < 0 || supply.FailureShare < 0 || supply.FailureShare > 1)
                errors.Add("Снабжение: неверные параметры.");

            var warehouse = data.Warehouse;
            if (warehouse == null) errors.Add("Не заданы параметры склада.");
            else if (warehouse.StartingCells < 0 || warehouse.CellsPerRack < 1 || warehouse.RackPrice <= 0 || warehouse.DailyRent < 0)
                errors.Add("Склад: неверные параметры.");

            var production = data.Production;
            if (production == null) errors.Add("Не заданы параметры производства.");
            else if (production.EnergyPricePerKwh < 0 || production.MismatchEfficiency <= 0 || production.MismatchEfficiency > 1
                     || production.DefaultTransportBatch < 1 || production.MaxTransportBatch < production.DefaultTransportBatch
                     || production.FlushMinutes < 1 || production.RequestIntervalMinutes < 1)
                errors.Add("Производство: неверные параметры.");

            var floor = data.Floor;
            if (floor == null) errors.Add("Не заданы параметры цеха.");
            else if (floor.BuildingWidth < 8 || floor.BuildingHeight < 8 || floor.CellSizeMeters <= 0 || floor.ExpansionColumns < 1 || floor.MaxExpansions < 0
                     || floor.WalkMetersPerMinute <= 0 || floor.LoadMinutes < 0 || floor.RearFeedExtraMinutes < 0 || floor.RelocationCostShare < 0)
                errors.Add("Цех: неверные параметры.");

            var maintenance = data.Maintenance;
            if (maintenance == null) errors.Add("Не заданы параметры обслуживания.");
            else if (maintenance.DefaultIntervalOperations < 1 || maintenance.PlannedMinutes < 0 || maintenance.UnplannedMinutes < 1
                     || maintenance.NoMechanicFactor < 1 || maintenance.ConditionAfterRepair <= 0 || maintenance.ConditionAfterRepair > 100)
                errors.Add("Обслуживание: неверные параметры.");

            var staff = data.Staff;
            if (staff == null) errors.Add("Не заданы параметры персонала.");
            else if (staff.CandidatesPerProfession < 0 || staff.FatiguePerHour < 0 || staff.FatigueRecoveryPerHour < 0 || staff.MaxOvertimeHours < 0
                     || staff.OvertimePayFactor < 1 || staff.StartingMorale < 0 || staff.StartingMorale > 100 || staff.QuitMorale < 0 || staff.QuitMorale >= staff.StartingMorale)
                errors.Add("Персонал: неверные параметры.");

            var quality = data.Quality;
            if (quality == null) errors.Add("Не заданы параметры качества.");
            else if (quality.OutputQcDetection < 0 || quality.OutputQcDetection > 1 || quality.QcUnitsPerHour < 1 || quality.InputQcThreshold < 0
                     || quality.InputQcThreshold > 100 || quality.RepairMinutes < 1 || quality.DiscountPriceShare < 0 || quality.DiscountPriceShare > 1)
                errors.Add("Качество: неверные параметры.");

            if (data.Pacing == null) errors.Add("Не заданы параметры темпа игры.");
            else if (data.Pacing.SecondsPerShiftAtX1 <= 0 || data.Pacing.NonWorkingSpeedup < 1 || data.Pacing.MaxTicksPerAdvance < 1)
                errors.Add("Темп игры: неверные параметры.");
        }

        // Проверяет, что у каждого элемента есть уникальный Id в нужном формате; возвращает набор Id.
        private static HashSet<string> CheckIds<T>(List<T> items, Func<T, string> getId, string kind, List<string> errors) where T : class
        {
            var ids = new HashSet<string>();
            if (items == null)
            {
                errors.Add($"{kind}: список не задан.");
                return ids;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) { errors.Add($"{kind} №{i + 1}: пустая запись."); continue; }
                string id = getId(items[i]);
                if (string.IsNullOrEmpty(id) || !IdFormat.IsMatch(id))
                    errors.Add($"{kind} №{i + 1}: Id «{id}» — нужны латинские строчные буквы, цифры и «_», начиная с буквы.");
                else if (!ids.Add(id))
                    errors.Add($"{kind}: Id «{id}» встречается дважды.");
            }
            return ids;
        }
    }
}
