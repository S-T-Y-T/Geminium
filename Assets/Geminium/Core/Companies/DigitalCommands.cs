using Geminium.Core.Data;

namespace Geminium.Core.Companies
{
    // Решения по цифровизации (GDD 7.9).

    // Купить модуль: внедрение оплачивается сразу (с компенсацией госпрограммы, если она идёт),
    // лицензия — с начала следующего месяца. Отключённый модуль включается снова без повторного внедрения.
    public sealed class BuyDigitalModuleCommand : CompanyCommand
    {
        public DigitalModule Module { get; }

        public BuyDigitalModuleCommand(DigitalModule module) { Module = module; }

        public override string Validate(Company company, GameWorld world)
        {
            string error = world.Digital.CheckPurchase(company, Module);
            if (error != null) return error;
            var owned = company.Digital.Find(Module);
            return owned != null ? null : CheckCash(company, world.Digital.NetCapex(company, Module));
        }

        internal override void Apply(Company company, GameWorld world) => world.Digital.Buy(company, Module);
    }

    // Отключить модуль: лицензия больше не списывается, эффект пропадает. Без датчиков отключается и предиктивное ТО.
    public sealed class DisableDigitalModuleCommand : CompanyCommand
    {
        public DigitalModule Module { get; }

        public DisableDigitalModuleCommand(DigitalModule module) { Module = module; }

        public override string Validate(Company company, GameWorld world) =>
            company.Digital.Has(Module) ? null : "Этот модуль не работает.";

        internal override void Apply(Company company, GameWorld world) => world.Digital.Disable(company, Module);
    }

    // ИИ-цены: выставлять ли цены автоматически (модуль остаётся, можно задавать цены вручную).
    public sealed class SetAutoPricingCommand : CompanyCommand
    {
        public bool Enabled { get; }

        public SetAutoPricingCommand(bool enabled) { Enabled = enabled; }

        public override string Validate(Company company, GameWorld world) =>
            company.Digital.Find(DigitalModule.AiPricing) == null ? "Нет модуля ИИ-ценообразования." : null;

        internal override void Apply(Company company, GameWorld world) => company.Digital.AutoPricing = Enabled;
    }
}
