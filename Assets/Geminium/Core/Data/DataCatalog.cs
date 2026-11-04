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
        private readonly Dictionary<string, SupplierDef> suppliers;
        private readonly Dictionary<string, AthleteDef> athletes;
        private readonly Dictionary<string, BotArchetypeDef> botArchetypes;

        public IReadOnlyList<MaterialDef> Materials { get; }
        public IReadOnlyList<MachineDef> Machines { get; }
        public IReadOnlyList<ProductDef> Products { get; }
        public IReadOnlyList<ProfessionDef> Professions { get; }
        public IReadOnlyList<SupplierDef> Suppliers { get; }
        public IReadOnlyList<EventDef> Events { get; }
        public IReadOnlyList<AthleteDef> Athletes { get; }
        public IReadOnlyList<BotArchetypeDef> BotArchetypes { get; }
        public IReadOnlyList<DigitalModuleDef> DigitalModules { get; }
        public IReadOnlyList<ScenarioDef> Scenarios { get; }

        public CalendarSettings Calendar { get; }
        public WorkScheduleSettings DefaultWorkSchedule { get; }
        public TimePacingSettings Pacing { get; }
        public long StartingCash { get; }
        public EconomySettings Economy { get; }
        public MarketSettings Market { get; }
        public FinanceSettings Finance { get; }
        public SupplySettings Supply { get; }
        public WarehouseSettings Warehouse { get; }
        public ProductionSettings Production { get; }
        public FloorSettings Floor { get; }
        public MaintenanceSettings Maintenance { get; }
        public StaffSettings Staff { get; }
        public QualitySettings Quality { get; }
        public TenderSettings Tenders { get; }
        public MarketingSettings Marketing { get; }
        public SurrogateSettings Surrogate { get; }
        public DigitalSettings Digital { get; }

        public DataCatalog(GameData data)
        {
            var errors = GameDataValidator.Validate(data);
            if (errors.Count > 0) throw new GameDataException(errors);

            Materials = data.Materials.ToList();
            Machines = data.Machines.ToList();
            Products = data.Products.ToList();
            Professions = data.Professions.ToList();
            Suppliers = data.Suppliers.ToList();
            Events = data.Events.ToList();
            Athletes = data.Athletes.ToList();
            BotArchetypes = data.BotArchetypes.ToList();
            DigitalModules = data.DigitalModules.ToList();
            Scenarios = data.Scenarios.ToList();
            athletes = Athletes.ToDictionary(a => a.Id);
            botArchetypes = BotArchetypes.ToDictionary(b => b.Id);
            suppliers = Suppliers.ToDictionary(s => s.Id);
            materials = Materials.ToDictionary(m => m.Id);
            machines = Machines.ToDictionary(m => m.Id);
            products = Products.ToDictionary(p => p.Id);
            professions = Professions.ToDictionary(p => p.Id);

            Calendar = data.Calendar;
            DefaultWorkSchedule = data.DefaultWorkSchedule;
            Pacing = data.Pacing;
            StartingCash = data.StartingCash;
            Economy = data.Economy;
            Market = data.Market;
            Finance = data.Finance;
            Supply = data.Supply;
            Warehouse = data.Warehouse;
            Production = data.Production;
            Floor = data.Floor;
            Maintenance = data.Maintenance;
            Staff = data.Staff;
            Quality = data.Quality;
            Tenders = data.Tenders;
            Marketing = data.Marketing;
            Surrogate = data.Surrogate;
            Digital = data.Digital;
        }

        public MaterialDef Material(string id) => Find(materials, id, "сырья");
        public MachineDef Machine(string id) => Find(machines, id, "станка");
        public ProductDef Product(string id) => Find(products, id, "продукта");
        public ProfessionDef Profession(string id) => Find(professions, id, "профессии");
        public SupplierDef Supplier(string id) => Find(suppliers, id, "поставщика");
        public AthleteDef Athlete(string id) => Find(athletes, id, "спортсмена");
        public BotArchetypeDef BotArchetype(string id) => Find(botArchetypes, id, "архетипа бота");

        public IReadOnlyList<SupplierDef> SuppliersOf(string materialId) =>
            Suppliers.Where(s => s.MaterialId == materialId).ToList();

        // Профессия, которая работает на станках зоны (или null).
        public ProfessionDef OperatorOf(ProductionZone zone) =>
            Professions.FirstOrDefault(p => p.OperatedZone == zone);

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
