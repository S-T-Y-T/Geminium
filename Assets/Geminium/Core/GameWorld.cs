using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core
{
    // Игровой мир: данные, симуляция и компании. С него начинается любая партия —
    // и в игре, и в прогоне Лаборатории без графики.
    public sealed class GameWorld
    {
        private static readonly Regex IdFormat = new Regex("^[a-z][a-z0-9_]*$");
        private readonly List<Company> companies = new List<Company>();

        public DataCatalog Catalog { get; }
        public Simulation Simulation { get; }
        public IReadOnlyList<Company> Companies => companies;

        public GameWorld(DataCatalog catalog, ulong seed)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Simulation = new Simulation(seed, new GameCalendar(catalog.Calendar));
            // Первая подсистема: контроллеры решают раньше, чем считаются рынок и производство.
            Simulation.AddSystem(new ControllerSystem(this));
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

            string error = command.Validate(company, Catalog);
            if (error != null) return error;

            Simulation.Scheduler.Schedule(Simulation.Now, () =>
            {
                if (command.Validate(company, Catalog) == null) command.Apply(company, this);
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
                foreach (var company in world.companies) company.Controller.OnMonthStarted(monthIndex);
            }

            protected internal override void OnDayStarted(long dayIndex)
            {
                foreach (var company in world.companies) company.Controller.OnDayStarted(dayIndex);
            }
        }
    }
}
