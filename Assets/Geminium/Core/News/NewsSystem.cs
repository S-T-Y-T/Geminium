using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core.News
{
    // Факт для ленты новостей (GDD 9): ключ шаблона и аргументы — Id, числа, ключи причин. Текста здесь нет:
    // текст собирает «голос» в слое Unity — по шаблонам таблицы локализации, позже языковой моделью.
    public sealed class NewsItem
    {
        public long Minute;
        public int Month;
        public string Key;
        public string CompanyId;     // о какой компании новость; пусто — о рынке
        public object[] Args;
    }

    // Лента новостей ИИ-ядра. Только читает мир, поэтому не меняет ход партии и сама повторяется при загрузке.
    // Стоит в конце порядка подсистем: в первую минуту месяца видит уже применённые решения ботов.
    //   news.event         — началось событие ИИ-ядра: Id события, сила;
    //   news.tender        — опубликован тендер: уровень, продукт, объём, предельная цена;
    //   news.tender_won    — тендер выигран: компания, уровень, продукт, цена;
    //   news.bot_price     — бот изменил цену: компания, продукт, изменение в %, ключ причины;
    //   news.bot_expand    — бот купил станок: компания, Id станка;
    //   news.bot_ads       — бот изменил рекламу: компания, бюджет;
    //   news.ambassador    — компания подписала амбассадора: компания, Id спортсмена.
    public sealed class NewsSystem : SimulationSystem
    {
        public const double PriceChangeThreshold = 0.05;   // меньше 5% — не новость

        private readonly GameWorld world;
        private readonly List<NewsItem> items = new List<NewsItem>();
        private readonly Dictionary<(string company, string product), long> lastPrices = new Dictionary<(string, string), long>();
        private readonly Dictionary<string, int> lastMachines = new Dictionary<string, int>();
        private readonly Dictionary<string, long> lastAds = new Dictionary<string, long>();
        private readonly HashSet<(string company, string athlete)> knownAmbassadors = new HashSet<(string, string)>();
        private int seenEvents;
        private long monthStartMinute = -1;

        public IReadOnlyList<NewsItem> Items => items;
        public event Action<NewsItem> Published;

        public NewsSystem(GameWorld world) { this.world = world ?? throw new ArgumentNullException(nameof(world)); }

        protected internal override void OnMonthStarted(int monthIndex)
        {
            monthStartMinute = Simulation.Now.Minute;
            var economy = world.Economy;
            for (; seenEvents < economy.StartedEvents.Count; seenEvents++)
            {
                var e = economy.StartedEvents[seenEvents];
                Publish("news.event", null, e.Definition.Id, e.Definition.Magnitude, e.RemainingMonths);
            }
            foreach (var tender in world.Tenders.OpenTenders)
                Publish("news.tender", null, tender.Level.ToString(), tender.ProductId, tender.Units, tender.MaxUnitPrice);
        }

        // Решения ботов первой минуты месяца уже применены — сравнить с прошлым месяцем.
        protected internal override void OnMinute(SimTime now)
        {
            if (now.Minute != monthStartMinute) return;
            bool firstMonth = Simulation.MonthIndex == 0;
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                bool bot = !(company.Controller is PlayerController);
                foreach (var product in world.Rules.OpenProducts(world.Catalog))
                {
                    long price = company.RetailPrice(product.Id);
                    var key = (company.Id, product.Id);
                    if (bot && !firstMonth && lastPrices.TryGetValue(key, out long old) && old > 0 && price > 0)
                    {
                        double change = (price - old) / (double)old;
                        if (Math.Abs(change) >= PriceChangeThreshold)
                            Publish("news.bot_price", company.Id, product.Id, Math.Round(change * 100), PriceReason(company, product.Id, old, change));
                    }
                    lastPrices[key] = price;
                }

                int machines = company.MachineCount;
                if (bot && !firstMonth && lastMachines.TryGetValue(company.Id, out int oldMachines) && machines > oldMachines)
                    Publish("news.bot_expand", company.Id, company.Machines.OrderByDescending(m => m.Id).First().DefId);
                lastMachines[company.Id] = machines;

                long ads = company.Sales.AdBudget;
                if (bot && !firstMonth && lastAds.TryGetValue(company.Id, out long oldAds) && Math.Abs(ads - oldAds) > Math.Max(oldAds, 1) * 0.2)
                    Publish("news.bot_ads", company.Id, ads);
                lastAds[company.Id] = ads;

                foreach (string athlete in company.Sales.Ambassadors)
                    if (knownAmbassadors.Add((company.Id, athlete)) && bot)
                        Publish("news.ambassador", company.Id, athlete);
            }
        }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var tender in world.Tenders.Tenders)
                if (tender.PublishedMonth == monthIndex && tender.WinnerId != null)
                {
                    var bid = tender.Bids.First(b => b.CompanyId == tender.WinnerId);
                    Publish("news.tender_won", tender.WinnerId, tender.Level.ToString(), tender.ProductId, bid.UnitPrice);
                }
        }

        // Почему бот сменил цену — по тем же данным, что видит он сам (GDD 9, «объяснение ходов ботов»).
        private string PriceReason(Company company, string productId, long oldPrice, double change)
        {
            var last = company.LastReport;
            long sold = last != null && last.SoldByProduct.TryGetValue(productId, out long s) ? s : 0;
            long lost = last != null && last.LostSalesByProduct.TryGetValue(productId, out long l) ? l : 0;
            long stock = company.Inventory.ProductUnits(productId);
            var rivals = world.Companies.Where(c => c != company && !c.IsBankrupt && c.RetailPrice(productId) > 0).ToList();
            double rivalPrice = rivals.Count > 0 ? rivals.Average(c => (double)c.RetailPrice(productId)) : 0;
            if (change < 0)
            {
                if (stock > Math.Max(sold, 1)) return "reason.overstock";
                if (rivalPrice > 0 && rivalPrice < oldPrice) return "reason.rivals_cheaper";
                return "reason.weak_demand";
            }
            if (lost > 0.2 * Math.Max(sold, 1)) return "reason.shortage";
            if (rivalPrice > oldPrice) return "reason.rivals_pricier";
            return "reason.costs";
        }

        private void Publish(string key, string companyId, params object[] args)
        {
            var item = new NewsItem { Minute = Simulation.Now.Minute, Month = Simulation.MonthIndex, Key = key, CompanyId = companyId, Args = args };
            items.Add(item);
            Published?.Invoke(item);
        }
    }
}
