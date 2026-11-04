using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core.Data
{
    // Все данные игры одним набором. В Unity хранится в файле GameDatabase.asset,
    // в тестах и прогонах без графики может собираться прямо в коде.
    [Serializable]
    public sealed class GameData
    {
        public List<MaterialDef> Materials = new List<MaterialDef>();
        public List<SupplierDef> Suppliers = new List<SupplierDef>();
        public List<MachineDef> Machines = new List<MachineDef>();
        public List<ProductDef> Products = new List<ProductDef>();
        public List<ProfessionDef> Professions = new List<ProfessionDef>();
        public List<EventDef> Events = new List<EventDef>();

        public CalendarSettings Calendar = new CalendarSettings();
        public WorkScheduleSettings DefaultWorkSchedule = new WorkScheduleSettings();
        public TimePacingSettings Pacing = new TimePacingSettings();

        // Стартовый капитал каждой компании, сумы (GDD 4.2: минимальная линия на один продукт + 3 месяца зарплат).
        public long StartingCash = 80000000;

        public EconomySettings Economy = new EconomySettings();
        public MarketSettings Market = new MarketSettings();
        public FinanceSettings Finance = new FinanceSettings();
        public SupplySettings Supply = new SupplySettings();
        public WarehouseSettings Warehouse = new WarehouseSettings();
        public ProductionSettings Production = new ProductionSettings();
        public FloorSettings Floor = new FloorSettings();
        public MaintenanceSettings Maintenance = new MaintenanceSettings();
        public StaffSettings Staff = new StaffSettings();
        public QualitySettings Quality = new QualitySettings();

        // Этап 3: тендеры, маркетинг, спортсмены-амбассадоры, архетипы ботов, суррогатная модель.
        public TenderSettings Tenders = new TenderSettings();
        public MarketingSettings Marketing = new MarketingSettings();
        public List<AthleteDef> Athletes = new List<AthleteDef>();
        public List<BotArchetypeDef> BotArchetypes = new List<BotArchetypeDef>
        {
            new BotArchetypeDef { Id = "balanced", SalesManagers = 1 },
            new BotArchetypeDef
            {
                Id = "premium", PriceFactor = 1.2, MinPriceFactor = 1.0, MaxPriceFactor = 1.5, PreferQuality = true, MachineTier = 2, InputQc = true,
                Hiring = CandidateChoice.Experienced, DefectPolicy = Factory.DefectPolicy.Repair, MaintenanceInterval = 300, SalesManagers = 2,
                AdBudgetShare = 0.06, MaxAdBudgetShare = 0.15, TenderMarkup = 0.95, TenderAppetite = 0.4, Certify = true,
                SponsorUpTo = TournamentLevel.National, Ambassadors = 1,
            },
            new BotArchetypeDef
            {
                Id = "dumper", PriceFactor = 0.85, MinPriceFactor = 0.7, MaxPriceFactor = 1.0, PlanGrowth = 1.1, QcStation = false, Mechanic = false,
                Hiring = CandidateChoice.Cheap, MaintenanceInterval = 800, RawRacks = 3, FinishedRacks = 2, AdBudgetShare = 0.01,
                MaxAdBudgetShare = 0.05, TenderMarkup = 0.8, TenderAppetite = 0.5,
            },
        };
        public SurrogateSettings Surrogate = new SurrogateSettings();

        // Этап 4: ветка «Цифровизация» и сценарии Лаборатории.
        public DigitalSettings Digital = new DigitalSettings();
        public List<DigitalModuleDef> DigitalModules = new List<DigitalModuleDef>
        {
            new DigitalModuleDef { Module = DigitalModule.IotSensors, Capex = 30000000, MonthlyLicense = 1500000 },
            new DigitalModuleDef { Module = DigitalModule.PredictiveMaintenance, Capex = 20000000, MonthlyLicense = 2000000, Requires = DigitalModule.IotSensors },
            new DigitalModuleDef { Module = DigitalModule.DigitalTwin, Capex = 40000000, MonthlyLicense = 3000000 },
            new DigitalModuleDef { Module = DigitalModule.AiCutting, Capex = 25000000, MonthlyLicense = 2000000 },
            new DigitalModuleDef { Module = DigitalModule.AiQuality, Capex = 35000000, MonthlyLicense = 2500000 },
            new DigitalModuleDef { Module = DigitalModule.DemandForecast, Capex = 15000000, MonthlyLicense = 1500000 },
            new DigitalModuleDef { Module = DigitalModule.AiPricing, Capex = 15000000, MonthlyLicense = 2000000 },
        };
        public List<ScenarioDef> Scenarios = new List<ScenarioDef>();
    }
}
