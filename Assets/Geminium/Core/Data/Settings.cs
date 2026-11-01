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

    // Производство. На этапе 1 — заглушка: выпуск = мощность станков × рабочее время (TODO 1.11).
    [Serializable]
    public sealed class ProductionSettings
    {
        public double DefaultSkill = 50;             // навык рабочих до появления персонала (этап 2)
        public double DefaultMachineCondition = 100; // станки пока не изнашиваются
        public double WearExponent = 2;              // k в M(Cond) — рост брака при износе
        public long EnergyPricePerKwh = 1000;        // тариф для юрлиц, сумы/кВт·ч
        public string LaborProfessionId = "laborer"; // разнорабочие на складе
        public int BaseLaborers = 1;
    }
}
