using System;
using System.Collections.Generic;

namespace Geminium.Core.Data
{
    // Данные этапа 4: кампания из шести заданий (GDD 4.1). Числа — заглушки до балансировки.

    // Возможности, которые кампания открывает по одной. Номера закреплены — по ним хранятся данные.
    public enum Feature
    {
        Molding = 0,        // зона формовки
        Logistics = 1,      // стеллажи, транспортная партия, сторона подачи
        Quality = 2,        // входной контроль, политика брака, стол ОТК
        Maintenance = 3,    // регламент ТО
        Marketing = 4,      // реклама, турниры, амбассадоры
        Tenders = 5,
        Loans = 6,
        Certification = 7,
        Digital = 8,        // ветка цифровизации (какие модули — MissionDef.Modules)
    }

    // С каким цехом начинается задание.
    public enum StartLayout
    {
        Empty = 0,      // пустой цех: игрок строит сам
        Standard = 1,   // готовый цех по шаблону, люди наняты
        Naive = 2,      // готовый цех с неудачной планировкой (задание про логистику)
    }

    // По чему оценивается задание.
    public enum GoalMetric
    {
        ProducedUnits = 0,               // выпустить не меньше Target единиц продукта ProductId за задание
        MaxDefectSharePct = 1,           // доля брака за задание не выше Target %
        LogisticsReductionPct = 2,       // путь грузов по планировке (индекс двойника) короче стартового на Target %
        NoWarehouseOverflow = 3,         // ни дня переполнения склада
        MaxReturnedUnits = 4,            // возвратов и рекламаций не больше Target
        MaxDowntimeHours = 5,            // простоев оборудования (поломки + ТО) за задание не больше Target часов
        MinMarketSharePct = 6,           // доля рынка продукта ProductId в последнем месяце не ниже Target %
        FederationContractOnTime = 7,    // контракт федерации (региональной или национальной): 1 — закрыт в срок,
                                         // 0,5 + 0,4 × доля поставленного — выигран, но не закрыт в срок, 0 — не выигран
    }

    // Балл цели = 100 × (факт − Zero) / (Target − Zero) в пределах 0–100: Target — значение на 100 баллов,
    // Zero — на 0. Если Target меньше Zero, лучше меньше (брак, простои). ProductId пуст — все продукты задания.
    // Балл задания — среднее баллов целей с весами Weight.
    [Serializable]
    public sealed class GoalDef
    {
        public GoalMetric Metric;
        public double Target;
        public double Zero;
        public string ProductId;
        public double Weight = 1;
    }

    // Тендер, который задание публикует обязательно (в дополнение к случайным).
    [Serializable]
    public sealed class MissionTenderDef
    {
        public int Month;
        public TenderLevel Level;
        public string ProductId;
        public int Units;
        public int DeliveryMonths = 2;
    }

    // Задание кампании: что открыто, с чего начинается, сколько длится и по чему оценивается.
    [Serializable]
    public sealed class MissionDef
    {
        public string Id;
        public int Months = 2;
        public bool Rivals;                                         // боты Daebo и Abıdas
        public StartLayout Layout;
        public string LayoutArchetype = "balanced";                 // по какому архетипу бот-помощник строит готовый цех
        public long StartingCash;                                   // 0 — как в данных
        public double StartCondition = 100;                         // состояние станков готового цеха на старте, %
        public List<string> Products = new List<string>();
        public List<Feature> Features = new List<Feature>();
        public List<DigitalModule> Modules = new List<DigitalModule>();
        public List<MissionTenderDef> Tenders = new List<MissionTenderDef>();
        public List<GoalDef> Goals = new List<GoalDef>();
        public double PassScore = 60;                               // задание пройдено, если балл не ниже
    }
}
