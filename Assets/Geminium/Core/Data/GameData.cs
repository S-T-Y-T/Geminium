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
    }
}
