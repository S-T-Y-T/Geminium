using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // Общие тестовые данные.
    internal static class TestData
    {
        // Минимальный корректный набор: ткань, два раскройных станка, добок из одной операции, раскройщик.
        public static GameData Minimal() => new GameData
        {
            Materials = { new MaterialDef { Id = "cotton", BasePrice = 25000, Volatility = 0.1, UnitsPerCell = 200 } },
            Machines =
            {
                new MachineDef { Id = "cutting_band", Zone = ProductionZone.Cutting, Tier = 2, Power = 1.6, DurabilityOperations = 4000, Price = 25000000 },
                new MachineDef { Id = "cutting_knife_table", Zone = ProductionZone.Cutting, Tier = 1, Power = 1, DurabilityOperations = 5000, Price = 3000000 },
            },
            Products =
            {
                new ProductDef
                {
                    Id = "dobok", BasePrice = 450000, BaseDemandPerMonth = 2000, BaseDefectChance = 0.04, UnitsPerCell = 20,
                    Operations = { new OperationDef { Zone = ProductionZone.Cutting, BaseTimeMinutes = 25, Inputs = { new MaterialAmount { MaterialId = "cotton", Amount = 2.5 } } } },
                },
            },
            Professions = { new ProfessionDef { Id = "cutter", BaseMonthlyWage = 4000000, OperatedZone = ProductionZone.Cutting } },
            StartingCash = 80000000,
        };

        public static DataCatalog MinimalCatalog() => new DataCatalog(Minimal());
    }
}
