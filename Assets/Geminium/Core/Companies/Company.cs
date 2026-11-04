using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Factory;
using Geminium.Core.Sales;
using Geminium.Core.Staff;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Компания: деньги, запасы, цех, сотрудники и текущие решения.
    // Решения меняются только командами (см. CompanyCommand), остальное — подсистемами ядра.
    public sealed class Company
    {
        private readonly Simulation simulation;
        private readonly DataCatalog catalog;
        private readonly Dictionary<string, long> retailPrices = new Dictionary<string, long>();
        private readonly Dictionary<string, int> monthlyPlan = new Dictionary<string, int>();
        internal readonly List<Loan> loans = new List<Loan>();
        internal readonly List<MonthlyReport> reports = new List<MonthlyReport>();
        internal readonly List<Worker> workers = new List<Worker>();
        internal long totalPaymentsOnTime;
        internal long totalPaymentsMissed;

        public string Id { get; }
        public CompanyController Controller { get; }
        public Ledger Ledger { get; } = new Ledger();
        public Inventory Inventory { get; } = new Inventory();
        public WorkSchedule Schedule { get; internal set; }

        public double Reputation { get; internal set; }       // 0–100 (GDD 7.3)
        public bool IsBankrupt { get; internal set; }
        public int BankruptMonth { get; internal set; } = -1;
        public int NegativeCashMonths { get; internal set; }  // сколько месяцев подряд баланс в минусе

        // Цех и производство (этап 2).
        public FactoryFloor Floor { get; }
        public Pathfinder Paths { get; }
        public ProductionState Production { get; } = new ProductionState();
        public IReadOnlyList<Worker> Workers => workers;
        public int OvertimeHours { get; internal set; }       // переработки в день, часы (0 — нет)
        public DefectPolicy DefectPolicy { get; internal set; } = DefectPolicy.Scrap;
        public bool InputQcEnabled { get; internal set; }
        public int TransportBatch { get; internal set; }

        // Сбыт и маркетинг (этап 3): контракты, сертификаты, реклама, амбассадоры.
        public SalesState Sales { get; } = new SalesState();

        // Цифровизация (этап 4): купленные модули.
        public DigitalState Digital { get; } = new DigitalState();

        public IReadOnlyList<Loan> Loans => loans;
        public IReadOnlyList<MonthlyReport> Reports => reports;
        public MonthStats CurrentMonth { get; } = new MonthStats();

        public long Cash => Ledger.Balance;
        public long Debt => loans.Sum(l => l.Remaining);
        public MonthlyReport LastReport => reports.Count > 0 ? reports[reports.Count - 1] : null;

        public IEnumerable<FloorObject> Machines => Floor.ObjectsOfKind(FloorObjectKind.Machine);
        public int MachineCount => Machines.Count();

        // Вместимость склада: площадка у ворот + ячейки стеллажей в цехе (GDD 7.5).
        public int WarehouseCells => catalog.Warehouse.StartingCells
                                     + Floor.ObjectsOfKind(FloorObjectKind.Rack).Count() * catalog.Warehouse.CellsPerRack;

        public bool HasQcStation => Floor.ObjectsOfKind(FloorObjectKind.QcTable).Any();

        internal Company(string id, CompanyController controller, Simulation simulation, DataCatalog catalog)
        {
            Id = id;
            Controller = controller;
            this.simulation = simulation;
            this.catalog = catalog;
            Schedule = new WorkSchedule(catalog.DefaultWorkSchedule);
            Reputation = catalog.Market.DefaultReputation;
            TransportBatch = catalog.Production.DefaultTransportBatch;
            Floor = new FactoryFloor(catalog.Floor);
            Paths = new Pathfinder(Floor);
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

        public Worker FindWorker(int workerId) => workers.FirstOrDefault(w => w.Id == workerId);

        public int CountWorkers(string professionId) => workers.Count(w => w.ProfessionId == professionId);

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
