using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Статьи движения денег. Из них строятся отчёт о движении денежных средств и P&L (GDD 7.8).
    // Числовые значения закреплены: они попадут в сохранения. Новые статьи — только в конец.
    public enum LedgerCategory
    {
        Other = 0,
        StartingCapital = 1,   // стартовый капитал
        SalesRetail = 2,       // продажи B2C
        SalesContracts = 3,    // поставки по B2B-контрактам
        MaterialPurchases = 4, // закупки сырья
        Wages = 5,             // зарплаты
        Rent = 6,              // аренда здания и склада
        Energy = 7,            // электроэнергия
        Maintenance = 8,       // ТО и ремонт
        Marketing = 9,         // маркетинг и сертификация
        Equipment = 10,        // покупка станков, стеллажей и модулей
        Taxes = 11,            // налог на прибыль
        Interest = 12,         // проценты по кредитам и овердрафту
        LoanReceived = 13,     // получен кредит
        LoanRepayment = 14,    // погашение тела кредита
        Penalties = 15,        // штрафы и неустойки
        SocialTax = 16,        // социальный налог с фонда оплаты труда
        Returns = 17,          // возвраты брака покупателями и экспертиза
        Training = 18,         // обучение сотрудников
        DigitalModules = 19,   // внедрение модулей цифровизации (вложение, амортизируется)
        DigitalLicense = 20,   // лицензии модулей цифровизации
    }

    public readonly struct LedgerEntry
    {
        public readonly SimTime Time;
        public readonly int MonthIndex;  // учётный месяц (платежи при закрытии месяца относятся к нему)
        public readonly LedgerCategory Category;
        public readonly long Amount;     // поступление — плюс, платёж — минус

        public LedgerEntry(SimTime time, int monthIndex, LedgerCategory category, long amount)
        {
            Time = time;
            MonthIndex = monthIndex;
            Category = category;
            Amount = amount;
        }
    }

    // Журнал движения денег компании. Баланс может уйти в минус — это овердрафт (GDD 4.2, банкротство).
    public sealed class Ledger
    {
        private const int CategorySlots = 64;
        private readonly List<LedgerEntry> entries = new List<LedgerEntry>();
        private readonly Dictionary<long, long> monthTotals = new Dictionary<long, long>();

        public IReadOnlyList<LedgerEntry> Entries => entries;
        public long Balance { get; private set; }

        public void Record(SimTime time, int monthIndex, LedgerCategory category, long amount)
        {
            if (entries.Count > 0 && time < entries[entries.Count - 1].Time)
                throw new ArgumentException("Записи журнала идут по времени.", nameof(time));
            if (monthIndex < 0) throw new ArgumentOutOfRangeException(nameof(monthIndex));

            entries.Add(new LedgerEntry(time, monthIndex, category, amount));
            Balance += amount;
            long key = Key(monthIndex, category);
            monthTotals.TryGetValue(key, out long total);
            monthTotals[key] = total + amount;
        }

        // Сумма по статье за учётный месяц.
        public long MonthTotal(int monthIndex, LedgerCategory category) =>
            monthTotals.TryGetValue(Key(monthIndex, category), out long total) ? total : 0;

        private static long Key(int monthIndex, LedgerCategory category) => (long)monthIndex * CategorySlots + (int)category;
    }
}
