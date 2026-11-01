using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core.Companies
{
    // Статьи движения денег. Из них строятся отчёт о движении денежных средств и P&L (GDD 7.8).
    // Числовые значения закреплены: они попадут в сохранения.
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
        Equipment = 10,        // покупка станков и модулей
        Taxes = 11,
        Interest = 12,         // проценты по кредиту
        LoanReceived = 13,     // получен кредит
        LoanRepayment = 14,    // погашение тела кредита
        Penalties = 15,        // штрафы и неустойки
    }

    public readonly struct LedgerEntry
    {
        public readonly SimTime Time;
        public readonly LedgerCategory Category;
        public readonly long Amount; // поступление — плюс, платёж — минус

        public LedgerEntry(SimTime time, LedgerCategory category, long amount)
        {
            Time = time;
            Category = category;
            Amount = amount;
        }
    }

    // Журнал движения денег компании. Баланс может уйти в минус — это овердрафт (GDD 4.2, банкротство).
    public sealed class Ledger
    {
        private readonly List<LedgerEntry> entries = new List<LedgerEntry>();

        public IReadOnlyList<LedgerEntry> Entries => entries;
        public long Balance { get; private set; }

        public void Record(SimTime time, LedgerCategory category, long amount)
        {
            if (entries.Count > 0 && time < entries[entries.Count - 1].Time)
                throw new ArgumentException("Записи журнала идут по времени.", nameof(time));
            entries.Add(new LedgerEntry(time, category, amount));
            Balance += amount;
        }

        // Сумма по статье за период [from, to).
        public long Total(LedgerCategory category, SimTime from, SimTime to)
        {
            long sum = 0;
            foreach (var entry in entries)
                if (entry.Category == category && entry.Time >= from && entry.Time < to) sum += entry.Amount;
            return sum;
        }
    }
}
