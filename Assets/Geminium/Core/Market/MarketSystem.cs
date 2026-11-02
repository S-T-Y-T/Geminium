using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;
using Geminium.Core.Time;

namespace Geminium.Core.Market
{
    // Итоги рынка одного продукта за месяц.
    public sealed class ProductMarketMonth
    {
        public string ProductId;
        public long Demand;   // спрос, пришедший к компаниям (до перетекания)
        public long Sold;
        public readonly SortedDictionary<string, long> SoldByCompany = new SortedDictionary<string, long>(StringComparer.Ordinal);

        public double ShareOf(string companyId) =>
            Sold > 0 && SoldByCompany.TryGetValue(companyId, out long sold) ? (double)sold / Sold : 0;
    }

    // Рынок B2C (GDD 7.2). Продажи — в рабочие дни рынка (график 5/2): месячный спрос делится
    // на число рабочих дней месяца. Доли — по привлекательности, невыполненный спрос один раз
    // перетекает к компаниям с остатком товара. Дробный спрос копится, чтобы округление не теряло продажи.
    public sealed class MarketSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly MarketSettings settings;
        private readonly WorkSchedule marketDays;
        private readonly Dictionary<string, double> carry = new Dictionary<string, double>();
        private readonly SortedDictionary<string, ProductMarketMonth> current = new SortedDictionary<string, ProductMarketMonth>(StringComparer.Ordinal);
        private readonly List<SortedDictionary<string, ProductMarketMonth>> history = new List<SortedDictionary<string, ProductMarketMonth>>();
        private int workingDaysThisMonth;

        public IReadOnlyList<SortedDictionary<string, ProductMarketMonth>> History => history;

        public MarketSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Market;
            marketDays = new WorkSchedule(new WorkScheduleSettings { WorkingDaysPerWeek = 5, ShiftsPerDay = 1 });
        }

        private EconomySystem Economy => world.Economy;

        // Доля компании в продажах продукта за прошедший месяц (0–1).
        public double ShareOf(int monthIndex, string companyId, string productId) =>
            monthIndex >= 0 && monthIndex < history.Count && history[monthIndex].TryGetValue(productId, out var month) ? month.ShareOf(companyId) : 0;

        protected internal override void OnMonthStarted(int monthIndex)
        {
            workingDaysThisMonth = Math.Max(1, marketDays.CountWorkingDays(Simulation.Calendar, monthIndex));
            current.Clear();
            foreach (var product in catalog.Products) current[product.Id] = new ProductMarketMonth { ProductId = product.Id };
        }

        protected internal override void OnMonthEnded(int monthIndex) =>
            history.Add(new SortedDictionary<string, ProductMarketMonth>(current, StringComparer.Ordinal));

        protected internal override void OnDayEnded(long dayIndex)
        {
            DateTime date = Simulation.Calendar.StartDate.AddDays(dayIndex);
            if (!marketDays.IsWorkingDay(date.DayOfWeek)) return;

            var companies = world.Companies.Where(c => !c.IsBankrupt).ToList();
            if (companies.Count == 0) return;
            foreach (var product in catalog.Products) SellProduct(product, companies);
        }

        private void SellProduct(ProductDef product, List<Company> companies)
        {
            double referencePrice = product.BasePrice * Economy.PriceLevel;
            double monthly = Formulas.MonthlyDemand(product.BaseDemandPerMonth, Economy.Seasonality,
                Economy.Multiplier(EventEffect.Demand, product.Id), Economy.Index);
            double averagePrice = companies.Average(c => (double)c.RetailPrice(product.Id));
            double dayDemand = Formulas.MarketSize(monthly / workingDaysThisMonth, referencePrice, averagePrice, settings.MarketSizeElasticity);
            double elasticity = Economy.PriceElasticity(settings.PriceElasticity);

            int n = companies.Count;
            var attractiveness = new double[n];
            for (int i = 0; i < n; i++)
            {
                var c = companies[i];
                double quality = c.Inventory.ProductUnits(product.Id) > 0 ? c.Inventory.ProductQuality(product.Id) : 0;
                attractiveness[i] = Formulas.Attractiveness(quality, c.Reputation, c.RetailPrice(product.Id), referencePrice,
                    settings.QualityWeight, settings.ReputationWeight, elasticity);
            }
            double total = attractiveness.Sum();
            if (total <= 0) return;

            // Своя доля спроса каждой компании.
            var sales = new long[n];
            var stock = new long[n];
            long unmet = 0;
            var month = current[product.Id];
            for (int i = 0; i < n; i++)
            {
                var c = companies[i];
                string key = c.Id + "|" + product.Id;
                carry.TryGetValue(key, out double carried);
                double desired = dayDemand * attractiveness[i] / total + carried;
                long whole = (long)Math.Floor(desired);
                carry[key] = desired - whole;
                month.Demand += whole;

                stock[i] = c.Inventory.ProductUnits(product.Id);
                sales[i] = Math.Min(whole, stock[i]);
                long lost = whole - sales[i];
                unmet += lost;
                if (lost > 0) MonthStats.Add(c.CurrentMonth.LostSales, product.Id, lost);
            }

            // Перетекание невыполненного спроса к компаниям с остатком, по привлекательности.
            if (unmet > 0)
            {
                double remainingAttractiveness = 0;
                for (int i = 0; i < n; i++) if (stock[i] > sales[i]) remainingAttractiveness += attractiveness[i];
                if (remainingAttractiveness > 0)
                    for (int i = 0; i < n; i++)
                        if (stock[i] > sales[i])
                            sales[i] += Math.Min(stock[i] - sales[i], (long)Math.Floor(unmet * attractiveness[i] / remainingAttractiveness));
            }

            for (int i = 0; i < n; i++)
            {
                if (sales[i] <= 0) continue;
                var c = companies[i];
                c.Inventory.TryTakeProducts(product.Id, sales[i], out _, out double unitCost, out long defects);
                c.Receive(LedgerCategory.SalesRetail, sales[i] * c.RetailPrice(product.Id));
                if (defects > 0)
                {
                    // Брак, не пойманный ОТК, покупатели возвращают: деньги назад плюс экспертиза (GDD 7.2).
                    c.Pay(LedgerCategory.Returns, defects * (c.RetailPrice(product.Id) + catalog.Quality.ReturnFee));
                    c.CurrentMonth.ReturnedUnits += defects;
                }
                c.CurrentMonth.CostOfGoodsSold += (long)Math.Round(sales[i] * unitCost);
                MonthStats.Add(c.CurrentMonth.Sold, product.Id, sales[i]);
                MonthStats.Add(month.SoldByCompany, c.Id, sales[i]);
                month.Sold += sales[i];
            }
        }
    }
}
