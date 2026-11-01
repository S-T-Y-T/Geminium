using System;
using System.Collections.Generic;

namespace Geminium.Core.Companies
{
    // Купленное оборудование: станок или стеллаж. На этапе 1 станки не изнашиваются
    // (заглушка производства), остаточная стоимость убывает линейно (GDD 7.8, Приложение Д).
    public sealed class OwnedEquipment
    {
        public string EquipmentId { get; }  // Id станка из данных или "rack" для стеллажа
        public long PurchasePrice { get; }
        public int PurchaseMonth { get; }

        public OwnedEquipment(string equipmentId, long purchasePrice, int purchaseMonth)
        {
            EquipmentId = equipmentId;
            PurchasePrice = purchasePrice;
            PurchaseMonth = purchaseMonth;
        }
    }

    // Кредит: равные доли тела в конце каждого месяца, проценты на остаток (GDD 7.8).
    public sealed class Loan
    {
        public long Principal { get; }
        public double AnnualRate { get; }
        public int TermMonths { get; }
        public int TakenMonth { get; }
        public long Remaining { get; internal set; }
        public int PaymentsMade { get; internal set; }
        public int OverdueMonths { get; internal set; }

        public Loan(long principal, double annualRate, int termMonths, int takenMonth)
        {
            Principal = principal;
            AnnualRate = annualRate;
            TermMonths = termMonths;
            TakenMonth = takenMonth;
            Remaining = principal;
        }
    }

    // Накопители текущего месяца: их заполняют производство и рынок, финансы закрывают месяц.
    public sealed class MonthStats
    {
        public long CostOfGoodsSold;
        public long PaymentsOnTime;
        public long PaymentsMissed;
        public readonly SortedDictionary<string, long> Produced = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> Defective = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> Sold = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> LostSales = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public int OverflowDays;

        internal static void Add(SortedDictionary<string, long> counter, string id, long amount)
        {
            counter.TryGetValue(id, out long current);
            counter[id] = current + amount;
        }

        internal void Reset()
        {
            CostOfGoodsSold = 0;
            PaymentsOnTime = 0;
            PaymentsMissed = 0;
            Produced.Clear();
            Defective.Clear();
            Sold.Clear();
            LostSales.Clear();
            OverflowDays = 0;
        }
    }

    // Итоги месяца компании: P&L, баланс, капитализация (GDD 7.8, 7.10). Основа KPI для Лаборатории.
    public sealed class MonthlyReport
    {
        public int Month;
        public long Revenue;
        public long CostOfGoodsSold;
        public long Wages;
        public long SocialTax;
        public long Rent;
        public long Energy;
        public long Penalties;
        public long OtherExpenses;      // маркетинг, ТО, прочее
        public long Interest;
        public long Depreciation;
        public long PreTaxProfit;
        public long ProfitTax;
        public long NetIncome;
        public long Cash;
        public long Debt;
        public long InventoryValue;
        public long MachinesValue;
        public long Capitalization;
        public double CreditRating;
        public double Reputation;
        public bool Bankrupt;
        public long UnitsProduced;
        public long UnitsDefective;
        public long UnitsSold;
        public long LostSales;
        public int OverflowDays;
        public readonly SortedDictionary<string, long> SoldByProduct = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> LostSalesByProduct = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, double> MarketShareByProduct = new SortedDictionary<string, double>(StringComparer.Ordinal);
    }
}
