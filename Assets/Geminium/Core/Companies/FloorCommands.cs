using System;
using System.Linq;
using Geminium.Core.Data;
using Geminium.Core.Factory;

namespace Geminium.Core.Companies
{
    // Команды планировки цеха (GDD 7.6.1, 7.6.6).

    // Поставить объект: станок (DefId — Id станка), стеллаж, стол ОТК или стол офиса. Объект покупается сразу.
    public sealed class PlaceObjectCommand : CompanyCommand
    {
        public FloorObjectKind Kind { get; }
        public string MachineId { get; }
        public int X { get; }
        public int Y { get; }
        public Facing Facing { get; }
        public FeedSide Feed { get; }

        public PlaceObjectCommand(FloorObjectKind kind, string machineId, int x, int y, Facing facing, FeedSide feed = FeedSide.Front)
        {
            Kind = kind;
            MachineId = machineId;
            X = x;
            Y = y;
            Facing = facing;
            Feed = feed;
        }

        public static PlaceObjectCommand Machine(string machineId, int x, int y, Facing facing, FeedSide feed = FeedSide.Front) =>
            new PlaceObjectCommand(FloorObjectKind.Machine, machineId, x, y, facing, feed);

        public override string Validate(Company company, GameWorld world)
        {
            if (Kind == FloorObjectKind.Machine && !world.Catalog.Machines.Any(m => m.Id == MachineId)) return $"Нет станка «{MachineId}».";
            var (sizeX, sizeY) = FloorRules.Size(world.Catalog, Kind, MachineId);
            return company.Floor.CanPlace(sizeX, sizeY, X, Y, Facing, FloorRules.ZoneAllowed(world.Catalog, Kind, MachineId))
                   ?? CheckCash(company, FloorRules.Price(world.Catalog, Kind, MachineId));
        }

        internal override void Apply(Company company, GameWorld world)
        {
            var (sizeX, sizeY) = FloorRules.Size(world.Catalog, Kind, MachineId);
            long price = FloorRules.Price(world.Catalog, Kind, MachineId);
            string defId = Kind == FloorObjectKind.Machine ? MachineId : FloorRules.DefIdOf(Kind);
            var o = company.Floor.Place(Kind, defId, sizeX, sizeY, X, Y, Facing, Feed, price, world.Simulation.MonthIndex);
            if (Kind == FloorObjectKind.Machine) world.Production.InitializeMachine(o);
            company.Pay(LedgerCategory.Equipment, price);
        }
    }

    // Переставить объект. Перенос станка стоит долю его цены (демонтаж и монтаж).
    public sealed class MoveObjectCommand : CompanyCommand
    {
        public int ObjectId { get; }
        public int X { get; }
        public int Y { get; }
        public Facing Facing { get; }

        public MoveObjectCommand(int objectId, int x, int y, Facing facing)
        {
            ObjectId = objectId;
            X = x;
            Y = y;
            Facing = facing;
        }

        public override string Validate(Company company, GameWorld world)
        {
            var o = company.Floor.FindObject(ObjectId);
            if (o == null) return $"Нет объекта №{ObjectId}.";
            string machineId = o.Kind == FloorObjectKind.Machine ? o.DefId : null;
            return company.Floor.CanPlace(o.SizeX, o.SizeY, X, Y, Facing, FloorRules.ZoneAllowed(world.Catalog, o.Kind, machineId), o.Id)
                   ?? CheckCash(company, RelocationCost(o, world));
        }

        internal override void Apply(Company company, GameWorld world)
        {
            var o = company.Floor.FindObject(ObjectId);
            company.Pay(LedgerCategory.Other, RelocationCost(o, world));
            company.Floor.Move(o, X, Y, Facing);
        }

        private static long RelocationCost(FloorObject o, GameWorld world) =>
            o.Kind == FloorObjectKind.Machine ? (long)Math.Round(o.PurchasePrice * world.Catalog.Floor.RelocationCostShare) : 0;
    }

    // Убрать объект. Сырьё со станка возвращается на склад, незавершённые единицы списываются.
    public sealed class RemoveObjectCommand : CompanyCommand
    {
        public int ObjectId { get; }

        public RemoveObjectCommand(int objectId) { ObjectId = objectId; }

        public override string Validate(Company company, GameWorld world) =>
            company.Floor.FindObject(ObjectId) == null ? $"Нет объекта №{ObjectId}." : null;

        internal override void Apply(Company company, GameWorld world)
        {
            var o = company.Floor.FindObject(ObjectId);
            world.Production.ReleaseMachine(company, o);
            foreach (var worker in company.workers.Where(w => w.AssignedObjectId == o.Id)) worker.AssignedObjectId = -1;
            company.Floor.Remove(o);
        }
    }

    // Покрасить прямоугольник клеток в зону («нарисовать комнату»).
    public sealed class PaintZoneCommand : CompanyCommand
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public ZoneType Zone { get; }

        public PaintZoneCommand(int x, int y, int width, int height, ZoneType zone)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Zone = zone;
        }

        public override string Validate(Company company, GameWorld world) => FloorRules.CheckRect(company, X, Y, Width, Height);

        internal override void Apply(Company company, GameWorld world) => company.Floor.Paint(X, Y, Width, Height, Zone);
    }

    // Отметить клетки как проход для разнорабочих (или снять отметку).
    public sealed class SetAisleCommand : CompanyCommand
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Aisle { get; }

        public SetAisleCommand(int x, int y, int width, int height, bool aisle)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Aisle = aisle;
        }

        public override string Validate(Company company, GameWorld world) => FloorRules.CheckRect(company, X, Y, Width, Height);

        internal override void Apply(Company company, GameWorld world) => company.Floor.SetAisle(X, Y, Width, Height, Aisle);
    }

    // Сторона подачи материала у станка: фронтальная или сзади.
    public sealed class SetFeedSideCommand : CompanyCommand
    {
        public int ObjectId { get; }
        public FeedSide Feed { get; }

        public SetFeedSideCommand(int objectId, FeedSide feed)
        {
            ObjectId = objectId;
            Feed = feed;
        }

        public override string Validate(Company company, GameWorld world) =>
            company.Floor.FindObject(ObjectId)?.Kind == FloorObjectKind.Machine ? null : $"Нет станка №{ObjectId}.";

        internal override void Apply(Company company, GameWorld world) => company.Floor.SetFeed(company.Floor.FindObject(ObjectId), Feed);
    }

    // Арендовать пристройку: несколько столбцов клеток справа (GDD 7.6.1).
    public sealed class ExpandBuildingCommand : CompanyCommand
    {
        public override string Validate(Company company, GameWorld world) =>
            company.Floor.Expansions >= world.Catalog.Floor.MaxExpansions ? "Больше пристроек арендовать нельзя." : null;

        internal override void Apply(Company company, GameWorld world) => company.Floor.Expand(world.Catalog.Floor.ExpansionColumns);
    }

    // Правила размещения объектов: размер, цена и в какой зоне можно ставить.
    public static class FloorRules
    {
        public static (int sizeX, int sizeY) Size(DataCatalog catalog, FloorObjectKind kind, string machineId)
        {
            switch (kind)
            {
                case FloorObjectKind.Machine:
                    var machine = catalog.Machine(machineId);
                    return (machine.SizeX, machine.SizeY);
                case FloorObjectKind.QcTable: return (2, 1);
                default: return (1, 1);
            }
        }

        public static long Price(DataCatalog catalog, FloorObjectKind kind, string machineId)
        {
            switch (kind)
            {
                case FloorObjectKind.Machine: return catalog.Machine(machineId).Price;
                case FloorObjectKind.Rack: return catalog.Warehouse.RackPrice;
                case FloorObjectKind.QcTable: return catalog.Floor.QcTablePrice;
                default: return catalog.Floor.DeskPrice;
            }
        }

        public static Func<ZoneType, bool> ZoneAllowed(DataCatalog catalog, FloorObjectKind kind, string machineId)
        {
            switch (kind)
            {
                case FloorObjectKind.Machine:
                    var zone = (ZoneType)(int)catalog.Machine(machineId).Zone;
                    return z => z == zone;
                case FloorObjectKind.Rack: return z => z == ZoneType.RawStorage || z == ZoneType.FinishedStorage;
                case FloorObjectKind.QcTable: return z => z == ZoneType.QualityControl || z == ZoneType.Receiving;
                default: return z => z == ZoneType.Office;
            }
        }

        public static string DefIdOf(FloorObjectKind kind) =>
            kind == FloorObjectKind.Rack ? "rack" : kind == FloorObjectKind.QcTable ? "qc_table" : "desk";

        internal static string CheckRect(Company company, int x, int y, int width, int height) =>
            width < 1 || height < 1 || !company.Floor.InBounds(x, y) || !company.Floor.InBounds(x + width - 1, y + height - 1)
                ? "Прямоугольник выходит за стены цеха."
                : null;
    }
}
