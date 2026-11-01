namespace Geminium.Core.Companies
{
    // Тот, кто принимает решения за компанию: игрок, бот или (позже) удалённый игрок.
    // Решения подаются только командами через Submit — у всех одни правила.
    public abstract class CompanyController
    {
        public Company Company { get; private set; }
        protected GameWorld World { get; private set; }

        internal void Attach(GameWorld world, Company company)
        {
            World = world;
            Company = company;
        }

        // Отправить команду. Возвращает текст ошибки или null, если команда принята.
        // Принятая команда применяется до расчёта ближайшей игровой минуты, в порядке подачи.
        public string Submit(CompanyCommand command) => World.Submit(Company, command);

        // Боты принимают решения раз в месяц (GDD 8), сценарные контроллеры могут и чаще.
        protected internal virtual void OnMonthStarted(int monthIndex) { }
        protected internal virtual void OnDayStarted(long dayIndex) { }
    }

    // Игрок: сам ничего не решает, команды приходят из интерфейса через Submit.
    public sealed class PlayerController : CompanyController
    {
    }
}
