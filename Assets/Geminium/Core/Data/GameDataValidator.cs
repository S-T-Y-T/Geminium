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
            CheckIds(data.Professions, p => p.Id, "Профессия", errors);

            var materials = (data.Materials ?? new List<MaterialDef>()).Where(m => m != null).ToList();
            var machines = (data.Machines ?? new List<MachineDef>()).Where(m => m != null).ToList();
            var products = (data.Products ?? new List<ProductDef>()).Where(p => p != null).ToList();
            var professions = (data.Professions ?? new List<ProfessionDef>()).Where(p => p != null).ToList();

            foreach (var m in materials)
            {
                if (m.BasePrice <= 0) errors.Add($"Сырьё «{m.Id}»: цена должна быть больше нуля.");
                if (m.Volatility < 0 || m.Volatility > 1) errors.Add($"Сырьё «{m.Id}»: волатильность — от 0 до 1.");
                if (m.UnitsPerCell <= 0) errors.Add($"Сырьё «{m.Id}»: единиц в ячейке склада должно быть больше нуля.");
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
