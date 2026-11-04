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
                foreach (var id in new[] { data.Staff.LaborerProfessionId, data.Staff.MechanicProfessionId, data.Staff.QcInspectorProfessionId,
                             data.Staff.SalesManagerProfessionId })
                    if (!professionIds.Contains(id ?? "")) errors.Add($"Персонал: нет профессии «{id}».");

            CheckSettings(data, errors);
            CheckSales(data, errors);
            CheckDigital(data, errors);
            return errors;
        }

        // Этап 4: модули цифровизации и сценарии Лаборатории.
        private static void CheckDigital(GameData data, List<string> errors)
        {
            var modules = (data.DigitalModules ?? new List<DigitalModuleDef>()).Where(m => m != null).ToList();
            foreach (DigitalModule module in Enum.GetValues(typeof(DigitalModule)))
                if (module != DigitalModule.None && modules.Count(m => m.Module == module) != 1)
                    errors.Add($"Цифровизация: модуль {module} должен быть задан ровно один раз.");
            foreach (var m in modules)
            {
                if (m.Module == DigitalModule.None || m.Capex < 0 || m.MonthlyLicense < 0)
                    errors.Add($"Цифровизация, {m.Module}: стоимость и лицензия не меньше нуля.");
                if (m.Requires == m.Module) errors.Add($"Цифровизация, {m.Module}: модуль не может зависеть от себя.");
            }
            var d = data.Digital;
            if (d == null) errors.Add("Не заданы параметры цифровизации.");
            else if (d.ConditionErrorWithoutIot < 0 || d.PredictiveConditionThreshold <= 0 || d.PredictiveConditionThreshold >= 100
                     || !IsShare(d.TwinRelocationCostShare) || d.TwinReviewMonths < 1 || d.TwinMinSavingCells < 0
                     || !IsShare(d.AiCuttingWasteShare) || !IsShare(d.AiQualityDetection) || d.PricingMinFactor <= 0
                     || d.PricingMaxFactor < d.PricingMinFactor || d.PricingStep <= 0 || d.DepreciationMonths < 1
                     || d.LabPurchaseFromMonth < 0 || d.LabCashReserve < 0)
                errors.Add("Цифровизация: неверные параметры.");
            if (data.Production != null && !IsShare(data.Production.CuttingWasteShare)) errors.Add("Производство: доля отходов раскроя — от 0 до 1.");

            var scenarioIds = CheckIds(data.Scenarios, s => s.Id, "Сценарий", errors);
            foreach (var s in (data.Scenarios ?? new List<ScenarioDef>()).Where(s => s != null && !string.IsNullOrEmpty(s.Baseline)))
                if (!scenarioIds.Contains(s.Baseline)) errors.Add($"Сценарий «{s.Id}»: нет базового сценария «{s.Baseline}».");
            foreach (var s in (data.Scenarios ?? new List<ScenarioDef>()).Where(s => s != null))
                if (s.Modules == null || s.Modules.Any(m => m == DigitalModule.None) || s.Modules.Distinct().Count() != s.Modules.Count)
                    errors.Add($"Сценарий «{s.Id}»: модули без повторов и без None.");
        }

        // Этап 3: тендеры, маркетинг, спортсмены, архетипы ботов, суррогат.
        private static void CheckSales(GameData data, List<string> errors)
        {
            CheckIds(data.Athletes, a => a.Id, "Спортсмен", errors);
            foreach (var a in (data.Athletes ?? new List<AthleteDef>()).Where(a => a != null))
                if (a.Rating < 0 || a.Rating > 100 || a.MonthlyFee < 0) errors.Add($"Спортсмен «{a.Id}»: рейтинг 0–100, гонорар не меньше нуля.");

            CheckIds(data.BotArchetypes, b => b.Id, "Архетип бота", errors);
            foreach (var b in (data.BotArchetypes ?? new List<BotArchetypeDef>()).Where(b => b != null))
            {
                if (b.MinPriceFactor <= 0 || b.PriceFactor < b.MinPriceFactor || b.PriceFactor > b.MaxPriceFactor)
                    errors.Add($"Архетип «{b.Id}»: нужно 0 < MinPriceFactor ≤ PriceFactor ≤ MaxPriceFactor.");
                if (b.AdBudgetShare < 0 || b.AdBudgetShare > b.MaxAdBudgetShare || b.MaxAdBudgetShare > 1 || b.TenderMarkup < 0 || b.TenderMarkup > 1
                    || b.TenderAppetite < 0 || b.TenderAppetite > 1 || b.HillClimbStep <= 0 || b.MachineTier < 1 || b.MachineTier > 3
                    || b.MaintenanceInterval < 1 || b.PlanGrowth <= 0 || b.RawRacks < 0 || b.FinishedRacks < 0 || b.SalesManagers < 0 || b.Ambassadors < 0)
                    errors.Add($"Архетип «{b.Id}»: неверные доли, уровень станков или количества.");
            }

            var tenders = data.Tenders;
            if (tenders == null) errors.Add("Не заданы параметры тендеров.");
            else
            {
                if (tenders.MinTendersPerMonth < 0 || tenders.MaxTendersPerMonth < tenders.MinTendersPerMonth || tenders.ReferenceDemand <= 0
                    || tenders.WeightPrice < 0 || tenders.WeightReputation < 0 || tenders.WeightQualityHistory < 0 || tenders.WeightManagers < 0
                    || tenders.LatePenaltyPerWeek < 0 || tenders.MaxWeeksLate < 0 || tenders.SponsorshipBonusMonths < 0)
                    errors.Add("Тендеры: неверные параметры.");
                var levels = tenders.Levels ?? new List<TenderLevelDef>();
                foreach (TenderLevel level in Enum.GetValues(typeof(TenderLevel)))
                    if (levels.Count(l => l != null && l.Level == level) != 1) errors.Add($"Тендеры: уровень {level} должен быть задан ровно один раз.");
                foreach (var l in levels.Where(l => l != null))
                    if (l.MinUnits < 1 || l.MaxUnits < l.MinUnits || l.MinDeliveryMonths < 1 || l.MaxDeliveryMonths < l.MinDeliveryMonths
                        || l.Weight < 0 || l.MaxPriceShare <= 0 || l.MaxPriceShare > 1 || l.MinReputation < 0 || l.MinReputation > 100)
                        errors.Add($"Тендеры, уровень {l.Level}: неверные партия, срок, вес или предельная цена.");
                if (levels.Where(l => l != null).Sum(l => l.Weight) <= 0) errors.Add("Тендеры: сумма весов уровней должна быть больше нуля.");
            }

            var m = data.Marketing;
            if (m == null) errors.Add("Не заданы параметры маркетинга.");
            else if (m.AdMaxBoost < 0 || m.AdBudgetScale <= 0 || m.AdCarryover < 0 || m.AdCarryover >= 1 || m.ReputationReversion < 0 || m.ReputationReversion > 1
                     || !IsShare(m.RegionalTournamentChance) || !IsShare(m.NationalTournamentChance) || !IsShare(m.WorldTournamentChance)
                     || !IsShare(m.ScandalChance) || !IsShare(m.WinChanceScale) || m.AmbassadorWinGainMax < m.AmbassadorWinGainMin
                     || m.CertificationMinMonths < 1 || m.CertificationMaxMonths < m.CertificationMinMonths || m.CertificateValidityMonths < 1
                     || m.CertificationCost < 0 || m.RegionalSponsorshipCost < 0 || m.NationalSponsorshipCost < 0 || m.WorldSponsorshipCost < 0
                     || m.ManagerMarketingDiscount < 0 || m.MaxManagerMarketingDiscount < 0 || m.MaxManagerMarketingDiscount >= 1)
                errors.Add("Маркетинг: неверные параметры.");

            var s = data.Surrogate;
            if (s == null) errors.Add("Не заданы параметры суррогатной модели.");
            else if (s.Utilization <= 0 || s.Utilization > 2 || s.PlanFulfilment <= 0 || s.PlanFulfilment > 1 || s.DefectMultiplier < 0
                     || s.EnergyPerMinute < 0 || s.MaintenancePerUnit < 0)
                errors.Add("Суррогатная модель: неверные параметры.");
        }

        private static bool IsShare(double value) => value >= 0 && value <= 1;

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
