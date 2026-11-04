using System;
using System.Collections.Generic;

namespace Geminium.Core.Companies
{
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

    // Накопители текущего месяца: их заполняют подсистемы, финансы закрывают месяц.
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
        public double LogisticsMeters;
        public readonly long[] DowntimeMinutes = new long[4];   // по DowntimeCause
        public int Breakdowns;
        public int Maintenances;
        public long DetectedDefects;
        public long ScrappedUnits;
        public long ReturnedUnits;
        public int RejectedBatches;
        public int Hires;
        public int Quits;
        public double OvertimeHours;
        // Этап 3: тендеры и репутация.
        public long ContractUnits;          // отгружено по контрактам
        public long ContractReturnedUnits;  // из возвратов — рекламации по контрактам
        public int B2bComplaints;           // партий с рекламациями
        public int TendersWon;
        public int ContractsCompleted;
        public int ContractsTerminated;
        public int Scandals;
        public double ReputationGain;
        public double ReputationLoss;
        // Этап 4: цифровизация.
        public double FabricWaste;          // отходы раскроя, м²
        public long DigitalSubsidy;         // компенсация госпрограммы за внедрение модулей

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
            LogisticsMeters = 0;
            Array.Clear(DowntimeMinutes, 0, DowntimeMinutes.Length);
            Breakdowns = 0;
            Maintenances = 0;
            DetectedDefects = 0;
            ScrappedUnits = 0;
            ReturnedUnits = 0;
            RejectedBatches = 0;
            Hires = 0;
            Quits = 0;
            OvertimeHours = 0;
            ContractUnits = 0;
            ContractReturnedUnits = 0;
            B2bComplaints = 0;
            TendersWon = 0;
            ContractsCompleted = 0;
            ContractsTerminated = 0;
            Scandals = 0;
            ReputationGain = 0;
            ReputationLoss = 0;
            FabricWaste = 0;
            DigitalSubsidy = 0;
        }
    }

    // Итоги месяца компании: P&L, баланс, капитализация и операционные KPI (GDD 7.8, 7.10, 10).
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
        public long OtherExpenses;      // ТО и ремонт, возвраты, обучение, маркетинг, прочее
        public long Interest;
        public long Depreciation;
        public long PreTaxProfit;
        public long ProfitTax;
        public long NetIncome;
        public long Cash;
        public long Debt;
        public long InventoryValue;     // склад + незавершённое производство
        public long MachinesValue;      // оборудование по остаточной стоимости
        public long Capitalization;
        public double CreditRating;
        public double Reputation;
        public bool Bankrupt;
        public long UnitsProduced;
        public long UnitsDefective;
        public long UnitsSold;
        public long LostSales;
        public int OverflowDays;
        public double LogisticsKm;
        public double DowntimeNoOperatorHours;
        public double DowntimeNoMaterialsHours;
        public double DowntimeBrokenHours;
        public double DowntimeMaintenanceHours;
        public int Breakdowns;
        public int Maintenances;
        public long DetectedDefects;
        public long ScrappedUnits;
        public long ReturnedUnits;
        public int RejectedBatches;
        public int Workers;
        public double AverageMorale;
        public int Hires;
        public int Quits;
        public double OvertimeHours;
        public long ContractRevenue;        // выручка по контрактам B2B (входит в Revenue)
        public long ContractUnits;
        public int TendersWon;
        public int ContractsActive;
        public int ContractsCompleted;
        public int ContractsTerminated;
        public int B2bComplaints;
        public long MarketingSpend;         // реклама, спонсорство, амбассадоры, сертификация (входит в OtherExpenses)
        public long AdBudget;
        public int Certificates;            // действующих
        public int Ambassadors;
        public int Scandals;
        public double ReputationGain;
        public double ReputationLoss;
        public double FabricWaste;          // отходы раскроя, м²
        public long DigitalCapex;           // внедрение модулей в этом месяце (вложение, не расход)
        public long DigitalLicense;         // лицензии (входят в OtherExpenses)
        public long DigitalDepreciation;    // амортизация модулей (входит в Depreciation)
        public long DigitalValue;           // остаточная стоимость модулей (входит в капитализацию)
        public long DigitalSubsidy;
        public int DigitalModules;          // работающих модулей
        public readonly SortedDictionary<string, long> SoldByProduct = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> LostSalesByProduct = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, double> MarketShareByProduct = new SortedDictionary<string, double>(StringComparer.Ordinal);
    }
}
