using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Time;

namespace Geminium.Core.Data
{
    // Проверенные данные игры с быстрым поиском по Id. Создаётся один раз при старте.
    // Если в данных есть ошибки, бросает GameDataException со всеми сообщениями сразу.
    public sealed class DataCatalog
    {
        private readonly Dictionary<string, MaterialDef> materials;
        private readonly Dictionary<string, MachineDef> machines;
        private readonly Dictionary<string, ProductDef> products;
        private readonly Dictionary<string, ProfessionDef> professions;

        public IReadOnlyList<MaterialDef> Materials { get; }
        public IReadOnlyList<MachineDef> Machines { get; }
        public IReadOnlyList<ProductDef> Products { get; }
        public IReadOnlyList<ProfessionDef> Professions { get; }

        public CalendarSettings Calendar { get; }
        public WorkScheduleSettings DefaultWorkSchedule { get; }
        public TimePacingSettings Pacing { get; }
        public long StartingCash { get; }

        public DataCatalog(GameData data)
        {
            var errors = GameDataValidator.Validate(data);
            if (errors.Count > 0) throw new GameDataException(errors);

            Materials = data.Materials.ToList();
            Machines = data.Machines.ToList();
            Products = data.Products.ToList();
            Professions = data.Professions.ToList();
            materials = Materials.ToDictionary(m => m.Id);
            machines = Machines.ToDictionary(m => m.Id);
            products = Products.ToDictionary(p => p.Id);
            professions = Professions.ToDictionary(p => p.Id);

            Calendar = data.Calendar;
            DefaultWorkSchedule = data.DefaultWorkSchedule;
            Pacing = data.Pacing;
            StartingCash = data.StartingCash;
        }

        public MaterialDef Material(string id) => Find(materials, id, "сырья");
        public MachineDef Machine(string id) => Find(machines, id, "станка");
        public ProductDef Product(string id) => Find(products, id, "продукта");
        public ProfessionDef Profession(string id) => Find(professions, id, "профессии");

        // Станки зоны от младшего уровня к старшему.
        public IReadOnlyList<MachineDef> MachinesForZone(ProductionZone zone) =>
            Machines.Where(m => m.Zone == zone).OrderBy(m => m.Tier).ToList();

        private static T Find<T>(Dictionary<string, T> items, string id, string kind)
        {
            if (id != null && items.TryGetValue(id, out var item)) return item;
            throw new KeyNotFoundException($"Нет {kind} с Id «{id}».");
        }
    }

    // Ошибки в данных игры: все сообщения сразу, чтобы исправить их за один заход.
    public sealed class GameDataException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public GameDataException(IReadOnlyList<string> errors)
            : base("Ошибки в данных игры:\n" + string.Join("\n", errors))
        {
            Errors = errors;
        }
    }
}
