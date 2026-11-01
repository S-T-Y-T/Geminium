using System;
using System.Collections.Generic;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Компания: деньги, запасы и текущие решения (цены, план выпуска, график работы).
    // Решения меняются только командами (см. CompanyCommand), деньги — подсистемами ядра.
    public sealed class Company
    {
        private readonly Simulation simulation;
        private readonly Dictionary<string, long> retailPrices = new Dictionary<string, long>();
        private readonly Dictionary<string, int> monthlyPlan = new Dictionary<string, int>();

        public string Id { get; }
        public CompanyController Controller { get; }
        public Ledger Ledger { get; } = new Ledger();
        public Inventory Inventory { get; } = new Inventory();
        public WorkSchedule Schedule { get; internal set; }

        public long Cash => Ledger.Balance;

        internal Company(string id, CompanyController controller, Simulation simulation, DataCatalog catalog)
        {
            Id = id;
            Controller = controller;
            this.simulation = simulation;
            Schedule = new WorkSchedule(catalog.DefaultWorkSchedule);
            foreach (var product in catalog.Products)
            {
                retailPrices[product.Id] = product.BasePrice; // на старте цена равна эталонной
                monthlyPlan[product.Id] = 0;
            }
        }

        // Розничная цена (канал B2C), сумы.
        public long RetailPrice(string productId) => retailPrices.TryGetValue(productId, out long price) ? price : 0;

        // План выпуска на месяц, штуки.
        public int MonthlyPlan(string productId) => monthlyPlan.TryGetValue(productId, out int units) ? units : 0;

        internal void SetRetailPrice(string productId, long price) => retailPrices[productId] = price;

        internal void SetMonthlyPlan(string productId, int units) => monthlyPlan[productId] = units;

        internal void Receive(LedgerCategory category, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Сумма поступления не может быть отрицательной.");
            Ledger.Record(simulation.Now, category, amount);
        }

        internal void Pay(LedgerCategory category, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Сумма платежа не может быть отрицательной.");
            Ledger.Record(simulation.Now, category, -amount);
        }
    }
}
