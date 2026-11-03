using System;
using System.Collections.Generic;
using Geminium.Core.Factory;

namespace Geminium.Core.Data
{
    // Данные этапа 3: тендеры (GDD 7.2), маркетинг и репутация (GDD 7.3), архетипы ботов (GDD 8),
    // суррогатная модель фабрики (GDD 8.2). Числа — заглушки до калибровки.

    // Уровень тендера. Номера закреплены — по ним хранятся данные.
    public enum TenderLevel
    {
        School = 0,
        Club = 1,
        Regional = 2,    // региональная федерация
        National = 3,    // национальная федерация
    }

    // Уровень турнира: спонсорство и выступления амбассадоров.
    public enum TournamentLevel
    {
        None = 0,
        Regional = 1,
        National = 2,
        World = 3,
    }

    // Кто выдаёт сертификат на продукт (GDD 7.3).
    public enum CertificationBody
    {
        WT = 0,     // World Taekwondo
        ITF = 1,    // International Taekwon-Do Federation
        WAKO = 2,
    }

    // Как бот выбирает кандидатов при найме.
    public enum CandidateChoice
    {
        Balanced = 0,     // опыт на сум зарплаты
        Experienced = 1,  // самые опытные
        Cheap = 2,        // самые дешёвые
    }

    // Параметры одного уровня тендеров.
    [Serializable]
    public sealed class TenderLevelDef
    {
        public TenderLevel Level;
        public int MinUnits;               // партия для продукта с эталонным спросом; для других — пропорционально спросу
        public int MaxUnits;
        public double MinReputation;       // заявки компаний с репутацией ниже не принимаются
        public bool CertificateRequired;   // нужен действующий сертификат на продукт
        public double CertificateBonus;    // прибавка к баллу заявки за сертификат
        public int MinDeliveryMonths = 1;
        public int MaxDeliveryMonths = 1;
        public double Weight = 1;          // вес при выборе уровня нового тендера
        public double MaxPriceShare = 0.7; // P_max = эталонная цена × доля (оптовая скидка 30–50%)
    }

    // Тендеры B2B (GDD 7.2).
    [Serializable]
    public sealed class TenderSettings
    {
        public int MinTendersPerMonth = 1;
        public int MaxTendersPerMonth = 3;
        public double ReferenceDemand = 2000;          // спрос, при котором партия — как в таблице уровня
        public double WeightPrice = 0.5;               // w_1
        public double WeightReputation = 0.2;          // w_2
        public double WeightQualityHistory = 0.2;      // w_3
        public double WeightManagers = 0.1;            // w_4
        public double ManagerBonusPerManager = 0.5;    // B_mgr = min(1, менеджеры × k)
        public double SponsorshipScoreBonus = 0.1;     // прибавка к баллу после спонсорства турнира этого уровня
        public int SponsorshipBonusMonths = 6;
        public double LatePenaltyPerWeek = 0.02;       // p_week — доля стоимости контракта за неделю просрочки
        public int MaxWeeksLate = 4;                   // N_max — дольше контракт расторгается
        public double TerminationReputationLoss = 10;
        public double ComplaintReputationLoss = 2;     // за каждую партию с рекламацией
        public double FederationMinReputation = 30;    // ниже федерации расторгают контракты
        public List<TenderLevelDef> Levels = new List<TenderLevelDef>
        {
            new TenderLevelDef { Level = TenderLevel.School, MinUnits = 50, MaxUnits = 200, Weight = 0.4, MaxPriceShare = 0.7 },
            new TenderLevelDef { Level = TenderLevel.Club, MinUnits = 100, MaxUnits = 500, MinReputation = 30, MaxDeliveryMonths = 2, Weight = 0.35, MaxPriceShare = 0.65 },
            new TenderLevelDef
            {
                Level = TenderLevel.Regional, MinUnits = 500, MaxUnits = 1500, MinReputation = 40, CertificateBonus = 0.1,
                MinDeliveryMonths = 2, MaxDeliveryMonths = 2, Weight = 0.18, MaxPriceShare = 0.6,
            },
            new TenderLevelDef
            {
                Level = TenderLevel.National, MinUnits = 1000, MaxUnits = 3000, MinReputation = 50, CertificateRequired = true,
                MinDeliveryMonths = 2, MaxDeliveryMonths = 3, Weight = 0.07, MaxPriceShare = 0.55,
            },
        };
    }

    // Маркетинг, турниры, сертификация и репутация (GDD 7.3).
    [Serializable]
    public sealed class MarketingSettings
    {
        public double AdMaxBoost = 0.3;                // a в k_ads = 1 + a·(1 − e^(−B/B_0))
        public long AdBudgetScale = 5000000;           // B_0, сумы в месяц (растёт с уровнем цен)
        public double AdCarryover = 0.5;               // какая доля рекламы прошлых месяцев ещё работает
        public double ReputationReversion = 0.05;      // λ — сползание к 50
        public double ReturnReputationPerPercent = 0.3; // минус репутации за каждый 1% возвращённых покупателями B2C
        public double RegionalTournamentChance = 0.5;  // вероятность турнира в месяц
        public double NationalTournamentChance = 0.15;
        public double WorldTournamentChance = 0.04;
        public long RegionalSponsorshipCost = 8000000;
        public long NationalSponsorshipCost = 25000000;
        public long WorldSponsorshipCost = 70000000;
        public double RegionalSponsorshipReputation = 2;
        public double NationalSponsorshipReputation = 5;
        public double WorldSponsorshipReputation = 10;
        public double WinChanceScale = 0.6;            // шанс победы = scale · (рейтинг/100)^степень уровня
        public double RegionalWinExponent = 1;
        public double NationalWinExponent = 3;
        public double WorldWinExponent = 6;
        public double AmbassadorWinGainMin = 0.15;     // победа амбассадора: репутация +15–20%
        public double AmbassadorWinGainMax = 0.2;
        public double ScandalChance = 0.02;            // в месяц на амбассадора
        public double ScandalReputationLoss = 8;
        public long CertificationCost = 12000000;      // за продукт и федерацию
        public int CertificationMinMonths = 2;
        public int CertificationMaxMonths = 3;
        public double CertificationQuality = 60;       // среднее качество тестовой партии (склада) не ниже
        public int CertificateValidityMonths = 24;
        public double CertifiedTournamentBonus = 0.1;  // привлекательность в месяцы национальных и мировых турниров
        public double ManagerMarketingDiscount = 0.05; // скидка на маркетинг за каждого менеджера по продажам
        public double MaxManagerMarketingDiscount = 0.2;
    }

    // Вымышленный спортсмен — возможный амбассадор (GDD 7.3, 12). Имя — в таблицах локализации по Id.
    [Serializable]
    public sealed class AthleteDef
    {
        public string Id;
        public double Rating;      // 0–100
        public long MonthlyFee;    // сумы в месяц
    }

    // Архетип скриптового бота (GDD 8): стартовые решения и пределы поиска восхождением.
    [Serializable]
    public sealed class BotArchetypeDef
    {
        public string Id;
        public double PriceFactor = 1.0;        // цена относительно эталонной на старте
        public double MinPriceFactor = 0.85;
        public double MaxPriceFactor = 1.2;
        public bool PreferQuality;              // поставщики с лучшим качеством вместо дешёвых
        public int MachineTier = 1;
        public double PlanGrowth = 1.05;        // план = прошлый спрос × рост
        public bool QcStation = true;
        public bool InputQc;
        public bool Mechanic = true;
        public CandidateChoice Hiring = CandidateChoice.Balanced;
        public DefectPolicy DefectPolicy = DefectPolicy.Scrap;
        public int MaintenanceInterval = 400;   // регламент ТО, операций
        public int RawRacks = 4;
        public int FinishedRacks = 3;
        public int SalesManagers;               // менеджеры по продажам (и столы для них)
        public double AdBudgetShare = 0.03;     // реклама — доля выручки прошлого месяца
        public double MaxAdBudgetShare = 0.1;
        public double TenderMarkup = 0.9;       // заявка = P_max × наценка; 0 — в тендерах не участвует
        public double TenderAppetite = 0.3;     // какую долю месячной мощности бот отдаёт под контракты
        public bool Certify;                    // сертифицировать основные продукты
        public TournamentLevel SponsorUpTo = TournamentLevel.None;
        public int Ambassadors;
        public double HillClimbStep = 0.05;     // шаг поиска восхождением
    }

    // Суррогатная модель фабрики (GDD 8.2): быстрые формулы вместо поминутного цеха.
    // Числа заполняет калибровка по прогонам полного цеха (меню Geminium → «Калибровать суррогат»).
    [Serializable]
    public sealed class SurrogateSettings
    {
        public double Utilization = 0.7;        // доля доступных минут станков, превращаемая в выпуск (до 2: опытные быстрее нормы)
        public double PlanFulfilment = 0.9;     // доля плана, которая выпускается, если мощности хватает
        public double DefectMultiplier = 1.0;   // поправка к формуле брака
        public double EnergyPerMinute = 30;     // сумы электроэнергии на минуту работы станков
        public double MaintenancePerUnit = 1000; // ТО и ремонт, сумы на единицу выпуска
        public int CalibrationSamples;          // сколько компаний-месяцев использовано
        public double OutputR2;                 // качество подгонки выпуска (R²)
        public double DefectR2;
    }
}
