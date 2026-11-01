using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;
using Geminium.Core.Finance;
using Geminium.Core.Market;
using Geminium.Core.Production;
using Geminium.Core.Supply;
using Geminium.Core.Time;
using Geminium.Core.Warehouse;

namespace Geminium.Core
{
    // Игровой мир: данные, симуляция, подсистемы и компании. С него начинается любая партия —
    // и в игре, и в прогоне Лаборатории без графики.
    // Порядок подсистем важен: контроллеры → экономика → снабжение → производство → рынок → склад → финансы.
    // Так решения применяются раньше расчётов, а финансы подводят месяц последними.
    public sealed class GameWorld
    {
        private static readonly Regex IdFormat = new Regex("^[a-z][a-z0-9_]*$");
        private readonly List<Company> companies = new List<Company>();

        public DataCatalog Catalog { get; }
        public Simulation Simulation { get; }
        public IReadOnlyList<Company> Companies => companies;

        public EconomySystem Economy { get; }
        public SupplySystem Supply { get; }
        public ProductionSystem Production { get; }
        public MarketSystem Market { get; }
        public WarehouseSystem Warehouse { get; }
        public FinanceSystem Finance { get; }

        public GameWorld(DataCatalog catalog, ulong seed)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Simulation = new Simulation(seed, new GameCalendar(catalog.Calendar));

            Simulation.AddSystem(new ControllerSystem(this));
            Simulation.AddSystem(Economy = new EconomySystem(catalog));
            Simulation.AddSystem(Supply = new SupplySystem(this));
            Simulation.AddSystem(Production = new ProductionSystem(this));
            Simulation.AddSystem(Market = new MarketSystem(this));
            Simulation.AddSystem(Warehouse = new WarehouseSystem(this));
            Simulation.AddSystem(Finance = new FinanceSystem(this));
        }

        // Добавить компанию. Все стартуют одинаково: стартовый капитал из данных, цены — эталонные.
        public Company AddCompany(string id, CompanyController controller)
        {
            if (string.IsNullOrEmpty(id) || !IdFormat.IsMatch(id))
                throw new ArgumentException($"Id компании «{id}» — латинские строчные буквы, цифры и «_».", nameof(id));
            if (FindCompany(id) != null) throw new ArgumentException($"Компания «{id}» уже есть.", nameof(id));
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (controller.Company != null) throw new ArgumentException("Контроллер уже управляет другой компанией.", nameof(controller));

            var company = new Company(id, controller, Simulation, Catalog);
            company.Receive(LedgerCategory.StartingCapital, Catalog.StartingCash);
            controller.Attach(this, company);
            companies.Add(company);
            return company;
        }

        public Company FindCompany(string id)
        {
            foreach (var company in companies)
                if (company.Id == id) return company;
            return null;
        }

        // Принять команду: проверить сейчас, применить до расчёта ближайшей минуты в порядке подачи.
        // Команды из начала месяца или дня применяются в ту же минуту, поданные позже — в следующую.
        public string Submit(Company company, CompanyCommand command)
        {
            if (company == null) throw new ArgumentNullException(nameof(company));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (company.IsBankrupt) return "Компания обанкротилась.";

            string error = command.Validate(company, this);
            if (error != null) return error;

            Simulation.Scheduler.Schedule(Simulation.Now, () =>
            {
                if (!company.IsBankrupt && command.Validate(company, this) == null) command.Apply(company, this);
            });
            return null;
        }

        // Передаёт контроллерам начало месяца и дня в порядке добавления компаний.
        private sealed class ControllerSystem : SimulationSystem
        {
            private readonly GameWorld world;

            public ControllerSystem(GameWorld world) { this.world = world; }

            protected internal override void OnMonthStarted(int monthIndex)
            {
                foreach (var company in world.companies)
                    if (!company.IsBankrupt) company.Controller.OnMonthStarted(monthIndex);
            }

            protected internal override void OnDayStarted(long dayIndex)
            {
                foreach (var company in world.companies)
                    if (!company.IsBankrupt) company.Controller.OnDayStarted(dayIndex);
            }
        }
    }
}
