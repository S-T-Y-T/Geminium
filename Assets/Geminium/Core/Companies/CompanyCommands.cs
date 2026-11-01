using System;
using Geminium.Core.Data;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Решение компании. Проверяется при подаче и ещё раз при применении:
    // за минуту между ними состояние могло измениться.
    // Все команды описаны в ядре — правила для игрока и ботов одинаковые.
    public abstract class CompanyCommand
    {
        // null — команда допустима; иначе понятный текст ошибки.
        public abstract string Validate(Company company, DataCatalog catalog);

        internal abstract void Apply(Company company, GameWorld world);

        protected static string CheckProduct(DataCatalog catalog, string productId)
        {
            foreach (var product in catalog.Products)
                if (product.Id == productId) return null;
            return $"Нет продукта «{productId}».";
        }
    }

    // Розничная цена продукта (канал B2C).
    public sealed class SetRetailPriceCommand : CompanyCommand
    {
        public string ProductId { get; }
        public long Price { get; }

        public SetRetailPriceCommand(string productId, long price)
        {
            ProductId = productId;
            Price = price;
        }

        public override string Validate(Company company, DataCatalog catalog) =>
            CheckProduct(catalog, ProductId) ?? (Price <= 0 ? "Цена должна быть больше нуля." : null);

        internal override void Apply(Company company, GameWorld world) => company.SetRetailPrice(ProductId, Price);
    }

    // План выпуска продукта на месяц.
    public sealed class SetMonthlyPlanCommand : CompanyCommand
    {
        public string ProductId { get; }
        public int Units { get; }

        public SetMonthlyPlanCommand(string productId, int units)
        {
            ProductId = productId;
            Units = units;
        }

        public override string Validate(Company company, DataCatalog catalog) =>
            CheckProduct(catalog, ProductId) ?? (Units < 0 ? "План не может быть отрицательным." : null);

        internal override void Apply(Company company, GameWorld world) => company.SetMonthlyPlan(ProductId, Units);
    }

    // График работы: дни в неделю и число смен (GDD 7.7).
    public sealed class SetWorkScheduleCommand : CompanyCommand
    {
        private readonly WorkScheduleSettings settings;

        public SetWorkScheduleCommand(WorkScheduleSettings settings)
        {
            // Копия, чтобы изменение исходного объекта после подачи не меняло команду.
            this.settings = settings == null ? null : new WorkScheduleSettings
            {
                WorkingDaysPerWeek = settings.WorkingDaysPerWeek,
                ShiftsPerDay = settings.ShiftsPerDay,
                ShiftLengthMinutes = settings.ShiftLengthMinutes,
                FirstShiftStartMinute = settings.FirstShiftStartMinute,
            };
        }

        public override string Validate(Company company, DataCatalog catalog)
        {
            if (settings == null) return "График не задан.";
            try
            {
                new WorkSchedule(settings);
                return null;
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }
        }

        internal override void Apply(Company company, GameWorld world) => company.Schedule = new WorkSchedule(settings);
    }
}
