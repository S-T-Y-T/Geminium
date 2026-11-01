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

    // Откуда поставляется сырьё (GDD 7.4).
    public enum MaterialOrigin
    {
        Local = 0,
        Import = 1,
        LocalAndImport = 2,
    }
}
