using System;
using System.Collections.Generic;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Компания: деньги, запасы, имущество и текущие решения.
    // Решения меняются только командами (см. CompanyCommand), остальное — подсистемами ядра.
    public sealed class Company
    {
        private readonly Simulation simulation;
        private readonly Dictionary<string, long> retailPrices = new Dictionary<string, long>();
        private readonly Dictionary<string, int> monthlyPlan = new Dictionary<string, int>();
        internal readonly List<OwnedEquipment> machines = new List<OwnedEquipment>();
        internal readonly List<OwnedEquipment> racks = new List<OwnedEquipment>();
        internal long totalPaymentsOnTime;
        internal long totalPaymentsMissed;
        internal readonly List<Loan> loans = new List<Loan>();
        internal readonly List<MonthlyReport> reports = new List<MonthlyReport>();

        public string Id { get; }
        public CompanyController Controller { get; }
        public Ledger Ledger { get; } = new Ledger();
        public Inventory Inventory { get; } = new Inventory();
        public WorkSchedule Schedule { get; internal set; }

        public double Reputation { get; internal set; }       // 0–100 (GDD 7.3); на этапе 1 постоянная
        public int WarehouseCells { get; internal set; }      // вместимость склада, ячеек
        public bool IsBankrupt { get; internal set; }
        public int BankruptMonth { get; internal set; } = -1;
        public int NegativeCashMonths { get; internal set; }  // сколько месяцев подряд баланс в минусе

        public IReadOnlyList<OwnedEquipment> Machines => machines;
        public IReadOnlyList<OwnedEquipment> Racks => racks;
        public IReadOnlyList<Loan> Loans => loans;
        public IReadOnlyList<MonthlyReport> Reports => reports;
        public MonthStats CurrentMonth { get; } = new MonthStats();

        public long Cash => Ledger.Balance;

        public long Debt
        {
            get
            {
                long debt = 0;
                foreach (var loan in loans) debt += loan.Remaining;
                return debt;
            }
        }

        public MonthlyReport LastReport => reports.Count > 0 ? reports[reports.Count - 1] : null;

        internal Company(string id, CompanyController controller, Simulation simulation, DataCatalog catalog)
        {
            Id = id;
            Controller = controller;
            this.simulation = simulation;
            Schedule = new WorkSchedule(catalog.DefaultWorkSchedule);
            Reputation = catalog.Market.DefaultReputation;
            WarehouseCells = catalog.Warehouse.StartingCells;
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
            if (amount > 0) Ledger.Record(simulation.Now, simulation.MonthIndex, category, amount);
        }

        internal void Pay(LedgerCategory category, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Сумма платежа не может быть отрицательной.");
            if (amount > 0) Ledger.Record(simulation.Now, simulation.MonthIndex, category, -amount);
        }
    }
}
