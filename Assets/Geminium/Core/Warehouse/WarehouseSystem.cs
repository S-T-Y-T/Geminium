using System;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Core.Warehouse
{
    // Склад (GDD 7.5). Каждый день в конце: аренда + обслуживание занятых ячеек, штраф за единицы,
    // которые не поместились. Ячейки заполняются по порядку: сначала сырьё, потом продукция, внутри — по Id.
    // Вместимость — площадка у ворот плюс стеллажи, расставленные в цехе. Аренда пристроек — тоже здесь.
    public sealed class WarehouseSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly WarehouseSettings settings;

        public WarehouseSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Warehouse;
        }

        // Сколько ячеек занято и сколько единиц не поместилось.
        public (int occupiedCells, double overflowUnits) Occupancy(Company company)
        {
            int capacity = company.WarehouseCells;
            int used = 0;
            double overflow = 0;

            void Place(double quantity, int unitsPerCell)
            {
                int needed = (int)Math.Ceiling(quantity / unitsPerCell - 1e-9);
                if (used + needed <= capacity)
                {
                    used += needed;
                    return;
                }
                double fits = (capacity - used) * (double)unitsPerCell;
                overflow += Math.Max(0, quantity - fits);
                used = capacity;
            }

            foreach (var pair in company.Inventory.Materials) Place(pair.Value, catalog.Material(pair.Key).UnitsPerCell);
            foreach (var pair in company.Inventory.Products) Place(pair.Value, catalog.Product(pair.Key).UnitsPerCell);
            foreach (var pair in company.Inventory.DefectiveStockItems) Place(pair.Value, catalog.Product(pair.Key).UnitsPerCell);
            return (used, overflow);
        }

        protected internal override void OnDayEnded(long dayIndex)
        {
            double priceLevel = world.Economy.PriceLevel;
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                var (occupied, overflow) = Occupancy(company);
                long cost = Formulas.WarehouseDailyCost(settings.DailyRent, occupied, settings.DailyCostPerOccupiedCell);
                double expansions = company.Floor.Expansions * catalog.Floor.ExpansionMonthlyRent / 30.4;
                company.Pay(LedgerCategory.Rent, (long)Math.Round((cost + expansions) * priceLevel));
                if (overflow > 0)
                {
                    company.Pay(LedgerCategory.Penalties, (long)Math.Round(overflow * settings.OverflowPenaltyPerUnit));
                    company.CurrentMonth.OverflowDays++;
                }
            }
        }
    }
}
