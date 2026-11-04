using System;
using System.Collections.Generic;

namespace Geminium.Core.Data
{
    // Данные этапа 4: ветка «Цифровизация» (GDD 7.9) и сценарии Лаборатории (GDD 10). Числа — заглушки.

    // Модуль цифровизации. Номера закреплены — по ним хранятся данные; −1 — «нет модуля».
    public enum DigitalModule
    {
        None = -1,
        IotSensors = 0,             // IoT-датчики: точное состояние станков
        PredictiveMaintenance = 1,  // предиктивное ТО (нужны датчики)
        DigitalTwin = 2,            // цифровой двойник цеха: «что если» для планировки
        AiCutting = 3,              // ИИ-раскрой: меньше отходов ткани
        AiQuality = 4,              // ИИ-контроль качества: компьютерное зрение до отгрузки
        DemandForecast = 5,         // прогноз спроса
        AiPricing = 6,              // ИИ-ценообразование
    }

    // Стоимость модуля и от чего он зависит.
    [Serializable]
    public sealed class DigitalModuleDef
    {
        public DigitalModule Module;
        public long Capex;                         // внедрение, сумы (растёт с уровнем цен)
        public long MonthlyLicense;                // лицензия и поддержка в месяц
        public DigitalModule Requires = DigitalModule.None;
    }

    // Эффекты модулей (GDD 7.9).
    [Serializable]
    public sealed class DigitalSettings
    {
        public double ConditionErrorWithoutIot = 15;     // без датчиков состояние станка известно с ошибкой ± пунктов
        public double PredictiveConditionThreshold = 55; // предиктивное ТО — когда состояние опустится до порога
        public double TwinRelocationCostShare = 0.01;    // с двойником перенос станка дешевле: проверен до оплаты
        public int TwinReviewMonths = 3;                 // как часто двойник пересматривает планировку
        public int TwinMinSavingCells = 2;               // переносить, если путь короче хотя бы на столько клеток
        public double AiCuttingWasteShare = 0.04;        // отходы раскроя с ИИ (без него — ProductionSettings.CuttingWasteShare)
        public double AiQualityDetection = 0.97;         // доля брака, которую находит компьютерное зрение (каждая единица)
        public double PricingMinFactor = 0.7;            // ИИ-цены: пределы и шаг перебора относительно эталонной цены
        public double PricingMaxFactor = 1.6;
        public double PricingStep = 0.05;
        public int DepreciationMonths = 36;              // амортизация внедрения модулей
        public int LabPurchaseFromMonth = 3;             // Лаборатория: фабрика покупает модули не раньше этого месяца
        public double LabCashReserve = 3;                // … и когда денег больше стоимости модуля во столько раз
    }

    // Сценарий Лаборатории: какие модули покупает фабрика игрока (GDD 10).
    [Serializable]
    public sealed class ScenarioDef
    {
        public string Id;
        public List<DigitalModule> Modules = new List<DigitalModule>();
        public bool Subsidy;        // госпрограмма «Цифровой Узбекистан»: компенсация части стоимости на весь прогон
        public bool NaiveLayout;    // станки в дальних от ворот концах своих зон (неудачная планировка — для двойника)
        public string Baseline;     // с каким сценарием сравнивать; пусто — с первым
    }
}
