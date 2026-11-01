namespace Geminium.Core.Data
{
    // Числовые значения закреплены: они хранятся в файлах данных, менять их нельзя.

    // Производственные зоны цеха (GDD 7.6.1).
    public enum ProductionZone
    {
        None = 0,
        Cutting = 1,             // раскрой
        Sewing = 2,              // пошив
        Molding = 3,             // формовка
        ElectronicsAssembly = 4, // сборка электроники (после НИОКР)
    }

    public enum MaterialGroup
    {
        Fabric = 0,      // ткани
        Composite = 1,   // композиты
        Accessory = 2,   // фурнитура
        Electronics = 3, // электроника
    }

    public enum MaterialUnit
    {
        SquareMeter = 0, // м²
        Meter = 1,       // м
        Piece = 2,       // шт.
        Kilogram = 3,    // кг
    }

    // Чем событие ИИ-ядра действует на мир (GDD 7.1).
    public enum EventEffect
    {
        None = 0,
        MaterialPrice = 1,      // множитель биржевой цены сырья (Target — Id сырья или все)
        ImportPrice = 2,        // множитель цены импортных поставщиков (курсовой шок)
        Demand = 3,             // множитель спроса (Target — Id продукта или все)
        PriceElasticity = 4,    // множитель ценовой эластичности
        ProductionCapacity = 5, // множитель рабочего времени станков (отключения, эпидемия)
        SupplierFailure = 6,    // добавка к доле неудачных поставок
        BatchQuality = 7,       // добавка к качеству партий сырья, пункты (отрицательная — брак)
        StateSubsidy = 8,       // доля компенсации модулей цифровизации (этап 4)
    }

    // Как часто приходит регулярная поставка (GDD 7.4).
    public enum DeliveryFrequency
    {
        None = 0,
        Weekly = 1,       // по понедельникам
        TwiceWeekly = 2,  // по понедельникам и средам
        Daily = 3,        // каждый рабочий день (пн–пт)
    }

    // Откуда поставляется сырьё (GDD 7.4).
    public enum MaterialOrigin
    {
        Local = 0,
        Import = 1,
        LocalAndImport = 2,
    }
}
