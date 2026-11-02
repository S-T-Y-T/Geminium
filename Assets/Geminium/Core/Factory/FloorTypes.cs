using System;

namespace Geminium.Core.Factory
{
    // Числовые значения закреплены: они попадут в сохранения.

    // Тип клетки цеха — «комната» (GDD 7.6.1). Значения 1–4 совпадают с ProductionZone.
    public enum ZoneType
    {
        None = 0,
        Cutting = 1,
        Sewing = 2,
        Molding = 3,
        ElectronicsAssembly = 4,
        RawStorage = 5,        // склад сырья
        FinishedStorage = 6,   // склад готовой продукции
        Receiving = 7,         // разгрузка и отгрузка у ворот
        QualityControl = 8,    // ОТК
        Office = 9,
    }

    public enum FloorObjectKind
    {
        Machine = 0,
        Rack = 1,
        QcTable = 2,
        Desk = 3,
    }

    // С какой стороны к объекту подходит разнорабочий (GDD 7.6.6, «ориентация рабочего места»).
    public enum Facing
    {
        South = 0,
        West = 1,
        North = 2,
        East = 3,
    }

    // Подача материала: фронтальная или сзади (+t_feed к каждой операции).
    public enum FeedSide
    {
        Front = 0,
        Rear = 1,
    }

    // Что делать с бракованной единицей, найденной ОТК (GDD 7.6.6).
    public enum DefectPolicy
    {
        Scrap = 0,     // утилизировать
        Store = 1,     // хранить на складе
        Repair = 2,    // вернуть на пошив
        Discount = 3,  // продать с уценкой
    }

    // Объект на сетке цеха: станок, стеллаж, стол ОТК или стол офиса.
    public sealed class FloorObject
    {
        public int Id { get; }
        public FloorObjectKind Kind { get; }
        public string DefId { get; }          // Id станка из данных; для остальных — «rack», «qc_table», «desk»
        public int X { get; internal set; }   // левый нижний угол
        public int Y { get; internal set; }
        public int SizeX { get; }             // размер при обращении на юг или север
        public int SizeY { get; }
        public Facing Facing { get; internal set; }
        public FeedSide Feed { get; internal set; }
        public long PurchasePrice { get; }
        public int PurchaseMonth { get; }
        public MachineRuntime Machine { get; internal set; } // только у станков

        public int Width => Facing == Facing.South || Facing == Facing.North ? SizeX : SizeY;
        public int Height => Facing == Facing.South || Facing == Facing.North ? SizeY : SizeX;

        internal FloorObject(int id, FloorObjectKind kind, string defId, int sizeX, int sizeY, long purchasePrice, int purchaseMonth)
        {
            Id = id;
            Kind = kind;
            DefId = defId;
            SizeX = sizeX;
            SizeY = sizeY;
            PurchasePrice = purchasePrice;
            PurchaseMonth = purchaseMonth;
        }

        public bool Covers(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

        // Клетка, на которой стоит разнорабочий, когда подвозит или забирает груз.
        public (int x, int y) AccessCell => AccessCellFor(X, Y, Width, Height, Facing);

        public static (int x, int y) AccessCellFor(int x, int y, int width, int height, Facing facing)
        {
            switch (facing)
            {
                case Facing.South: return (x + width / 2, y - 1);
                case Facing.North: return (x + width / 2, y + height);
                case Facing.West: return (x - 1, y + height / 2);
                case Facing.East: return (x + width, y + height / 2);
                default: throw new ArgumentOutOfRangeException(nameof(facing));
            }
        }
    }
}
