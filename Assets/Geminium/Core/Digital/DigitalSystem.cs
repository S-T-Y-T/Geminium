using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;

namespace Geminium.Core.Digital
{
    // Купленный модуль: внедрение капитализируется и амортизируется, лицензия — расход каждого месяца.
    public sealed class OwnedModule
    {
        public DigitalModule Module;
        public int PurchaseMonth;
        public long Capex;          // заплачено за внедрение (после субсидии)
        public long Subsidy;        // компенсировано госпрограммой
        public bool Active = true;  // лицензия оплачивается; false — модуль отключён
    }

    // Цифровизация компании (GDD 7.9).
    public sealed class DigitalState
    {
        internal readonly List<OwnedModule> modules = new List<OwnedModule>();

        public IReadOnlyList<OwnedModule> Modules => modules;
        public bool AutoPricing { get; internal set; } = true;  // ИИ-цены выставляются сами (можно выключить)
        public double ForcedSubsidy { get; internal set; }        // компенсация на весь прогон (сценарий Лаборатории с госпрограммой)

        public bool Has(DigitalModule module)
        {
            foreach (var m in modules)
                if (m.Module == module && m.Active) return true;
            return false;
        }

        public OwnedModule Find(DigitalModule module) => modules.FirstOrDefault(m => m.Module == module);
    }

    // Ветка «Цифровизация» (GDD 7.9): покупка модулей, лицензии, компенсация госпрограммы, ИИ-цены.
    // Эффекты остальных модулей — там, где они действуют: предиктивное ТО, ИИ-раскрой и ИИ-контроль —
    // в ProductionSystem, прогноз спроса — в MarketSystem, перенос с двойником — в MoveObjectCommand,
    // планировка двойника — DigitalTwin.Suggest (её применяет бот или игрок).
    // Месяц: лицензии списываются в начале; ИИ-цены выставляются в начале, до решений контроллеров.
    public sealed class DigitalSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;

        public DigitalSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
        }

        private EconomySystem Economy => world.Economy;

        public DigitalModuleDef Def(DigitalModule module) => catalog.DigitalModules.First(d => d.Module == module);

        // Доля компенсации, которую сейчас даёт госпрограмма (событие StateSubsidy или сценарий Лаборатории).
        public double SubsidyShare(Company company) =>
            Math.Min(0.95, Math.Max(Economy.Additive(EventEffect.StateSubsidy), company.Digital.ForcedSubsidy));

        // Цена внедрения для компании сейчас: с инфляцией и за вычетом компенсации.
        public long NetCapex(Company company, DigitalModule module) =>
            (long)Math.Round(Def(module).Capex * Economy.PriceLevel * (1 - SubsidyShare(company)));

        public string CheckPurchase(Company company, DigitalModule module)
        {
            if (!catalog.DigitalModules.Any(d => d.Module == module)) return $"Нет модуля {module}.";
            var owned = company.Digital.Find(module);
            if (owned != null && owned.Active) return "Модуль уже работает.";
            var requires = Def(module).Requires;
            if (requires != DigitalModule.None && !company.Digital.Has(requires)) return $"Сначала нужен модуль {requires}.";
            return null;
        }

        internal void Buy(Company company, DigitalModule module)
        {
            var owned = company.Digital.Find(module);
            if (owned != null)
            {
                owned.Active = true; // повторное включение: внедрение уже оплачено
                return;
            }
            long gross = (long)Math.Round(Def(module).Capex * Economy.PriceLevel);
            long net = NetCapex(company, module);
            company.Pay(LedgerCategory.DigitalModules, net);
            company.Digital.modules.Add(new OwnedModule
            {
                Module = module, PurchaseMonth = Simulation.MonthIndex, Capex = net, Subsidy = gross - net,
            });
            company.CurrentMonth.DigitalSubsidy += gross - net;
        }

        internal void Disable(Company company, DigitalModule module)
        {
            var owned = company.Digital.Find(module);
            if (owned == null) return;
            owned.Active = false;
            // Без датчиков предиктивное ТО работать не может.
            if (module == DigitalModule.IotSensors) Disable(company, DigitalModule.PredictiveMaintenance);
        }

        // Остаточная стоимость и амортизация модулей за месяц (для финансов).
        public (long value, long depreciation) Assets(Company company, int month)
        {
            int months = catalog.Digital.DepreciationMonths;
            long value = 0, depreciation = 0;
            foreach (var m in company.Digital.Modules)
            {
                int age = month - m.PurchaseMonth;
                if (age < months) depreciation += (long)Math.Round((double)m.Capex / months);
                value += Formulas.ResidualValue(m.Capex, age + 1, months);
            }
            return (value, depreciation);
        }

        protected internal override void OnMonthStarted(int monthIndex)
        {
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                foreach (var m in company.Digital.Modules.Where(m => m.Active))
                    company.Pay(LedgerCategory.DigitalLicense, (long)Math.Round(Def(m.Module).MonthlyLicense * Economy.PriceLevel));
                if (company.Digital.Has(DigitalModule.AiPricing) && company.Digital.AutoPricing)
                    foreach (var product in catalog.Products.Where(p => p.UnlockedByDefault))
                        company.SetRetailPrice(product.Id, world.Market.OptimalPrice(company, product.Id));
            }
        }
    }
}
