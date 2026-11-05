using System;
using System.Collections.Generic;
using System.Linq;
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
        public abstract string Validate(Company company, GameWorld world);

        internal abstract void Apply(Company company, GameWorld world);

        // Команда принята в очередь (например, кандидат резервируется, чтобы его не наняли другие).
        internal virtual void OnAccepted(Company company, GameWorld world) { }

        protected static string CheckProduct(DataCatalog catalog, string productId) =>
            catalog.Products.Any(p => p.Id == productId) ? null : CoreText.T("err.no_product", "Нет продукта «{0}».", productId);

        protected static string CheckCash(Company company, long cost) =>
            company.Cash >= cost ? null : CoreText.T("err.not_enough_cash", "Не хватает денег: нужно {0:N0} сум, на счёте {1:N0}.", cost, company.Cash);
    }

    // Пакет команд, зависящих друг от друга (например, «покрасить зоны → поставить станки»).
    // Каждая подкоманда проверяется в момент применения, по очереди, и пропускается, если недопустима
    // (в том числе если на неё не хватает денег).
    public sealed class BatchCommand : CompanyCommand
    {
        public IReadOnlyList<CompanyCommand> Commands { get; }

        public BatchCommand(IEnumerable<CompanyCommand> commands) { Commands = commands.ToList(); }

        public override string Validate(Company company, GameWorld world) => Commands.Count == 0 ? CoreText.T("err.empty_batch", "Пакет пуст.") : null;

        internal override void Apply(Company company, GameWorld world)
        {
            foreach (var command in Commands)
                if ((world.Rules.Check(command, world) ?? command.Validate(company, world)) == null) command.Apply(company, world);
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

        public override string Validate(Company company, GameWorld world) =>
            CheckProduct(world.Catalog, ProductId) ?? (Price <= 0 ? CoreText.T("err.price_positive", "Цена должна быть больше нуля.") : null);

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

        public override string Validate(Company company, GameWorld world)
        {
            string error = CheckProduct(world.Catalog, ProductId);
            if (error != null) return error;
            if (Units < 0) return CoreText.T("err.plan_negative", "План не может быть отрицательным.");
            if (Units > 0 && !world.Rules.IsProductOpen(world.Catalog.Product(ProductId))) return CoreText.T("err.product_locked", "Продукт «{0}» ещё не открыт.", ProductId);
            return null;
        }

        internal override void Apply(Company company, GameWorld world) => company.SetMonthlyPlan(ProductId, Units);
    }

    // График работы: дни в неделю и число смен (GDD 7.7).
    public sealed class SetWorkScheduleCommand : CompanyCommand
    {
        private readonly WorkScheduleSettings settings;

        public SetWorkScheduleCommand(WorkScheduleSettings settings)
        {
            // Копия, чтобы изменение исходного объекта после подачи не меняло команду.
            this.settings = Copy(settings);
        }

        // Копия настроек (для сохранений и интерфейса).
        public WorkScheduleSettings Settings => Copy(settings);

        private static WorkScheduleSettings Copy(WorkScheduleSettings settings) => settings == null ? null : new WorkScheduleSettings
        {
            WorkingDaysPerWeek = settings.WorkingDaysPerWeek,
            ShiftsPerDay = settings.ShiftsPerDay,
            ShiftLengthMinutes = settings.ShiftLengthMinutes,
            FirstShiftStartMinute = settings.FirstShiftStartMinute,
        };

        public override string Validate(Company company, GameWorld world)
        {
            if (settings == null) return CoreText.T("err.no_schedule", "График не задан.");
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

    // Разовый заказ сырья у поставщика (GDD 7.4). Оплата — при поставке.
    public sealed class PlaceOrderCommand : CompanyCommand
    {
        public string SupplierId { get; }
        public double Quantity { get; }

        public PlaceOrderCommand(string supplierId, double quantity)
        {
            SupplierId = supplierId;
            Quantity = quantity;
        }

        public override string Validate(Company company, GameWorld world)
        {
            if (!world.Catalog.Suppliers.Any(s => s.Id == SupplierId)) return CoreText.T("err.no_supplier", "Нет поставщика «{0}».", SupplierId);
            var supplier = world.Catalog.Supplier(SupplierId);
            return Quantity < supplier.MinBatch ? CoreText.T("err.min_batch", "Минимальная партия у «{0}» — {1}.", SupplierId, supplier.MinBatch) : null;
        }

        internal override void Apply(Company company, GameWorld world) =>
            world.Supply.PlaceOrder(company, world.Catalog.Supplier(SupplierId), Quantity, DeliveryFrequency.None);
    }

    // Регулярная поставка: количество за раз и частота. Количество 0 или частота None — отменить.
    public sealed class SetSupplyScheduleCommand : CompanyCommand
    {
        public string SupplierId { get; }
        public double Quantity { get; }
        public DeliveryFrequency Frequency { get; }

        public SetSupplyScheduleCommand(string supplierId, double quantity, DeliveryFrequency frequency)
        {
            SupplierId = supplierId;
            Quantity = quantity;
            Frequency = frequency;
        }

        public override string Validate(Company company, GameWorld world)
        {
            if (!world.Catalog.Suppliers.Any(s => s.Id == SupplierId)) return CoreText.T("err.no_supplier", "Нет поставщика «{0}».", SupplierId);
            var supplier = world.Catalog.Supplier(SupplierId);
            bool cancel = Quantity <= 0 || Frequency == DeliveryFrequency.None;
            return !cancel && Quantity < supplier.MinBatch ? CoreText.T("err.min_batch", "Минимальная партия у «{0}» — {1}.", SupplierId, supplier.MinBatch) : null;
        }

        internal override void Apply(Company company, GameWorld world) =>
            world.Supply.SetSchedule(company, world.Catalog.Supplier(SupplierId), Quantity, Frequency);
    }

    // Взять кредит (GDD 7.8): ставка зависит от ключевой ставки и кредитного рейтинга.
    public sealed class TakeLoanCommand : CompanyCommand
    {
        public long Amount { get; }
        public int TermMonths { get; }

        public TakeLoanCommand(long amount, int termMonths)
        {
            Amount = amount;
            TermMonths = termMonths;
        }

        public override string Validate(Company company, GameWorld world)
        {
            var finance = world.Catalog.Finance;
            if (Amount <= 0) return CoreText.T("err.loan_positive", "Сумма кредита должна быть больше нуля.");
            if (TermMonths < 1 || TermMonths > finance.MaxLoanTermMonths) return CoreText.T("err.loan_term", "Срок кредита — от 1 до {0} месяцев.", finance.MaxLoanTermMonths);
            if (company.Debt + Amount > finance.MaxDebt) return CoreText.T("err.max_debt", "Банк не даст больше: предел долга {0:N0} сум.", finance.MaxDebt);
            return null;
        }

        internal override void Apply(Company company, GameWorld world) => world.Finance.IssueLoan(company, Amount, TermMonths);
    }
}
