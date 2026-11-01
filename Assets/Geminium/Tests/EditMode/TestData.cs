using System;
using Geminium.Core;
using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // Общие тестовые данные.
    internal static class TestData
    {
        // Минимальный корректный набор: хлопок с одним поставщиком, два раскройных станка,
        // добок из одной операции раскроя, раскройщик и разнорабочий.
        public static GameData Minimal() => new GameData
        {
            Materials = { new MaterialDef { Id = "cotton", BasePrice = 25000, Volatility = 0.1, UnitsPerCell = 200, MarketSupplyPerMonth = 20000 } },
            Suppliers =
            {
                new SupplierDef
                {
                    Id = "cotton_test", MaterialId = "cotton", Origin = MaterialOrigin.Local, Reliability = 1,
                    QualityMin = 60, QualityMax = 60, MinBatch = 10, LeadTimeDays = 2,
                },
            },
            Machines =
            {
                new MachineDef { Id = "cutting_band", Zone = ProductionZone.Cutting, Tier = 2, Power = 1.6, EnergyKw = 1.5, DurabilityOperations = 4000, Price = 25000000 },
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
            Professions =
            {
                new ProfessionDef { Id = "cutter", BaseMonthlyWage = 4000000, OperatedZone = ProductionZone.Cutting },
                new ProfessionDef { Id = "laborer", BaseMonthlyWage = 3000000 },
            },
            StartingCash = 80000000,
        };

        public static DataCatalog MinimalCatalog() => new DataCatalog(Minimal());

        // Мир на минимальных данных; tweak меняет данные перед стартом.
        public static GameWorld World(Action<GameData> tweak = null, ulong seed = 1)
        {
            var data = Minimal();
            tweak?.Invoke(data);
            return new GameWorld(new DataCatalog(data), seed);
        }

        // Без случайного шума цен и событий — для точных проверок сумм.
        public static void Calm(GameData data)
        {
            data.Materials[0].Volatility = 0;
            data.Economy.IndexVolatility = 0;
            data.Economy.InflationVolatility = 0;
            data.Supply.LoyaltyDiscountPerMillion = 0;
            data.Events.Clear();
        }
    }
}
