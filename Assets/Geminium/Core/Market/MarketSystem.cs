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

        // ---------- Прогноз спроса и ИИ-цены (цифровизация, GDD 7.9) ----------

        private const double ExpectedQuality = 60;  // качество, которое модель ждёт от компании без запаса на складе

        // Прогноз спроса компании на продукт в этом месяце (модуль «Прогноз спроса»): сезонно-наивный —
        // спрос прошлого месяца (продано + упущено) × изменение сезонности, индекса экономики и событий спроса.
        // Без истории — ожидаемые продажи по модели рынка.
        public double Forecast(Company company, string productId)
        {
            var last = company.LastReport;
            var history = Economy.History;
            if (last == null || history.Count < 2)
                return ExpectedSales(company, catalog.Product(productId), company.RetailPrice(productId));
            last.SoldByProduct.TryGetValue(productId, out long sold);
            last.LostSalesByProduct.TryGetValue(productId, out long lost);
            var previous = history[history.Count - 2];
            var current = history[history.Count - 1];
            double change = current.Seasonality / Math.Max(1e-6, previous.Seasonality) * current.Index / Math.Max(1e-6, previous.Index)
                            * DemandEvents(current, productId) / DemandEvents(previous, productId);
            return (sold + lost) * change;
        }

        // Множитель спроса от событий, действовавших в месяц снимка экономики.
        private double DemandEvents(EconomySnapshot snapshot, string productId)
        {
            double multiplier = 1;
            foreach (string id in snapshot.ActiveEventIds)
            {
                var e = catalog.Events.FirstOrDefault(x => x.Id == id);
                if (e != null && e.Effect == EventEffect.Demand && (string.IsNullOrEmpty(e.Target) || e.Target == productId))
                    multiplier *= 1 + e.Magnitude;
            }
            return Math.Max(1e-6, multiplier);
        }

        // ИИ-цена: перебор множителя к эталонной цене, максимум ожидаемой маржи (цена − сырьё по бирже),
        // продажи не больше доступного товара (запас + план месяца).
        public long OptimalPrice(Company company, string productId)
        {
            var product = catalog.Product(productId);
            var d = catalog.Digital;
            double reference = product.BasePrice * Economy.PriceLevel;
            double available = Math.Max(1, company.Inventory.ProductUnits(productId) + company.MonthlyPlan(productId));
            double unitCost = 0;
            foreach (var input in product.Operations.SelectMany(o => o.Inputs))
                unitCost += input.Amount * world.Supply.ExchangePrice(input.MaterialId);
            double best = double.MinValue, bestPrice = company.RetailPrice(productId);
            for (int k = 0; d.PricingMinFactor + k * d.PricingStep <= d.PricingMaxFactor + 1e-9; k++)
            {
                double price = reference * (d.PricingMinFactor + k * d.PricingStep);
                double margin = Math.Min(ExpectedSales(company, product, price), available) * (price - unitCost);
                if (margin > best + 1e-6)
                {
                    best = margin;
                    bestPrice = price;
                }
            }
            return (long)Math.Round(bestPrice);
        }

        // Продажи компании по модели рынка, если её цена — price, а у конкурентов — текущие.
        private double ExpectedSales(Company company, ProductDef product, double price)
        {
            var active = world.Companies.Where(c => !c.IsBankrupt).ToList();
            if (!active.Contains(company)) return 0;
            double reference = product.BasePrice * Economy.PriceLevel;
            double elasticity = Economy.PriceElasticity(settings.PriceElasticity);
            double total = 0, own = 0, sum = 0;
            foreach (var c in active)
            {
                double p = c == company ? price : c.RetailPrice(product.Id);
                sum += p;
                double quality = c.Inventory.ProductUnits(product.Id) > 0 ? c.Inventory.ProductQuality(product.Id) : ExpectedQuality;
                double a = Formulas.Attractiveness(quality, c.Reputation, p, reference, settings.QualityWeight, settings.ReputationWeight, elasticity)
                           * world.Marketing.DemandMultiplier(c, product.Id);
                total += a;
                if (c == company) own = a;
            }
            double monthly = Formulas.MonthlyDemand(product.BaseDemandPerMonth, Economy.Seasonality,
                Economy.Multiplier(EventEffect.Demand, product.Id), Economy.Index);
            double size = Formulas.MarketSize(monthly, reference, sum / active.Count, settings.MarketSizeElasticity);
            return total > 0 ? size * own / total : 0;
        }

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
                    settings.QualityWeight, settings.ReputationWeight, elasticity) * world.Marketing.DemandMultiplier(c, product.Id);
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
