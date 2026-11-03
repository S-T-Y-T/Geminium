using System;

namespace Geminium.Core.Data
{
    // Параметры подсистем. Рабочие значения — в GameDatabase.asset, здесь значения по умолчанию.
    // Все числа — стартовые заглушки до калибровки (Приложение Д GDD).

    // ИИ-ядро: макроэкономика (GDD 7.1).
    [Serializable]
    public sealed class EconomySettings
    {
        public double InitialIndex = 1.0;            // индекс экономики I на старте
        public double IndexReversion = 0.1;          // θ — скорость возврата к 1
        public double IndexVolatility = 0.06;        // σ — шум индекса за месяц
        public double IndexMin = 0.5;
        public double IndexMax = 1.5;
        public double InitialInflation = 0.085;      // годовая инфляция на старте
        public double InflationTarget = 0.05;        // цель ЦБ
        public double InflationReversion = 0.08;     // скорость возврата инфляции к цели за месяц
        public double InflationIndexSensitivity = 0.01; // бум разгоняет инфляцию, кризис замедляет (за месяц)
        public double InflationVolatility = 0.004;
        public double KeyRateNeutral = 0.10;         // r_0
        public double KeyRateInflationSensitivity = 1.0; // k_π
        public double KeyRateMin = 0.05;
        public double KeyRateMax = 0.30;
        public double SeasonalityAmplitude = 0.3;    // S(t) от 0,7 до 1,3
        public int SeasonalityPeakMonth = 9;         // пик — сентябрь (начало учебного года)
        public double ElasticityCrisisSensitivity = 1.0; // в кризис ценовая эластичность растёт
        public double CapitalizationMultiplierMonths = 60; // M при I = 1: оценка в 60 месячных прибылей
    }

    // Рынок B2C (GDD 7.2).
    [Serializable]
    public sealed class MarketSettings
    {
        public double QualityWeight = 1.0;       // w_q
        public double ReputationWeight = 0.5;    // w_r
        public double PriceElasticity = 1.5;     // w_p при нормальной экономике
        public double MarketSizeElasticity = 0.5; // ε_m
        public double DefaultReputation = 50;
    }

    // Финансы (GDD 7.8).
    [Serializable]
    public sealed class FinanceSettings
    {
        public double ProfitTaxRate = 0.15;      // налог на прибыль РУз (сверить по lex.uz)
        public double SocialTaxRate = 0.12;      // социальный налог с ФОТ (сверить по lex.uz)
        public double BankMargin = 0.04;         // m_bank
        public double TrustDiscount = 0.06;      // k_trust
        public double OverdraftMargin = 0.10;    // надбавка к ключевой ставке за минусовой баланс
        public int DepreciationMonths = 60;      // линейная амортизация станков
        public int MaxLoanTermMonths = 36;
        public long MaxDebt = 300000000;         // предел суммарного долга компании, сумы
        public int NegativeCashMonthsToBankrupt = 2;
        public int MaxOverdueMonths = 1;         // просрочка дольше — банкротство
    }

    // Снабжение (GDD 7.4).
    [Serializable]
    public sealed class SupplySettings
    {
        public double LoyaltyDiscountPerMillion = 0.001; // скидка за каждый 1 млн сум закупок у поставщика
        public double MaxLoyaltyDiscount = 0.10;
        public double WeeklyDiscount = 0.05;     // поставка раз в неделю — больше партия, больше скидка
        public double TwiceWeeklyDiscount = 0.02;
        public double DailyDiscount = 0.0;
        public double PriceRatioMin = 0.5;       // пределы множителя спрос/предложение
        public double PriceRatioMax = 2.0;
        public int MaxDelayDays = 5;             // ненадёжный поставщик задерживает до N дней
        public double FailureShare = 0.2;        // доля срывов среди неудачных поставок (остальное — задержки)
    }

    // Склад (GDD 7.5).
    [Serializable]
    public sealed class WarehouseSettings
    {
        public int StartingCells = 60;
        public long DailyRent = 150000;              // фиксированная аренда здания в день
        public long DailyCostPerOccupiedCell = 2000; // обслуживание занятой ячейки в день
        public long OverflowPenaltyPerUnit = 500;    // штраф в день за единицу, не поместившуюся на склад
        public int CellsPerRack = 10;
        public long RackPrice = 2500000;
    }

    // Производство на пространственном цехе (GDD 7.6).
    [Serializable]
    public sealed class ProductionSettings
    {
        public double WearExponent = 2;              // k в M(Cond) — рост брака при износе
        public long EnergyPricePerKwh = 1000;        // тариф для юрлиц, сумы/кВт·ч
        public double MismatchEfficiency = 0.6;      // k_fit, если профессия не совпадает с зоной станка
        public int DefaultTransportBatch = 5;        // транспортная партия по умолчанию, штук
        public int MaxTransportBatch = 20;
        public int FlushMinutes = 120;               // неполную партию везут, если она ждёт дольше
        public int RequestIntervalMinutes = 10;      // как часто цех формирует заявки на перевозку
    }

    // Здание и логистика (GDD 7.6.1, 7.6.6).
    [Serializable]
    public sealed class FloorSettings
    {
        public int BuildingWidth = 32;               // клеток
        public int BuildingHeight = 20;
        public double CellSizeMeters = 2;
        public int ExpansionColumns = 8;             // сколько столбцов клеток даёт одно расширение
        public int MaxExpansions = 2;
        public long ExpansionMonthlyRent = 5000000;  // аренда одного расширения в месяц
        public double WalkMetersPerMinute = 40;      // скорость разнорабочего с опытом 50
        public double LoadMinutes = 1;               // погрузка или разгрузка партии
        public double RearFeedExtraMinutes = 0.5;    // t_feed: подача сзади удлиняет каждую операцию
        public double RelocationCostShare = 0.05;    // перенос станка — доля его цены
        public long QcTablePrice = 3000000;
        public long DeskPrice = 1000000;
    }

    // Износ и обслуживание станков (GDD 7.6.5).
    [Serializable]
    public sealed class MaintenanceSettings
    {
        public int DefaultIntervalOperations = 400;  // регламент ТО по умолчанию
        public double PlannedCostShare = 0.01;       // плановое ТО — доля цены станка
        public int PlannedMinutes = 120;
        public double UnplannedCostShare = 0.05;     // внеплановый ремонт после поломки
        public int UnplannedMinutes = 480;
        public double NoMechanicFactor = 2;          // без наладчика ремонт дольше и дороже (сторонний сервис)
        public double ConditionAfterRepair = 80;
    }

    // Персонал и рынок труда (GDD 7.7).
    [Serializable]
    public sealed class StaffSettings
    {
        public string LaborerProfessionId = "laborer";
        public string MechanicProfessionId = "mechanic";
        public string QcInspectorProfessionId = "qc_inspector";
        public string SalesManagerProfessionId = "sales_manager";
        public int CandidatesPerProfession = 4;      // кандидатов каждой профессии в месяц при нормальной экономике
        public double CandidateExperienceMean = 35;
        public double CandidateExperienceSd = 20;
        public double WageExperienceFactor = 0.004;  // ожидаемая зарплата = база × (0,8 + опыт × k) × уровень цен
        public double FatiguePerHour = 8;
        public double FatigueRecoveryPerHour = 6;
        public double ExperiencePerHour = 0.02;
        public int MaxOvertimeHours = 4;
        public double OvertimePayFactor = 1.5;
        public double OvertimeFatigueFactor = 1.5;
        public int OvertimeDaysTolerance = 8;        // переработки сверх этого числа дней в месяц снижают мораль
        public double MoralePerExcessOvertimeDay = 1.5;
        public double MoralePerBreakdown = 2;
        public double MoraleWageGapFactor = 0.5;     // пунктов морали за каждый % отставания зарплаты от рынка
        public double MoraleRecoveryPerMonth = 2;
        public double StartingMorale = 70;
        public double QuitMorale = 25;               // ниже — увольняется
        public double SeveranceMonths = 1;
        public long TrainingCost = 1500000;
        public double TrainingExperienceGain = 10;
    }

    // Контроль качества и брак (GDD 7.6.6, 7.4).
    [Serializable]
    public sealed class QualitySettings
    {
        public double OutputQcDetection = 0.9;       // доля брака, которую находит выходной ОТК
        public int QcUnitsPerHour = 60;              // сколько единиц контролёр проверяет за час
        public double InputQcThreshold = 40;         // партии сырья хуже этого качества возвращаются поставщику
        public int RepairMinutes = 20;               // ремонт бракованной единицы на швейном станке
        public double RepairQualityPenalty = 10;
        public double DiscountPriceShare = 0.4;      // уценённый брак продаётся за эту долю цены
        public long ReturnFee = 15000;               // экспертиза по каждому возврату
    }
}
