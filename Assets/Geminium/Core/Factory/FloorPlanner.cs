using System.Collections.Generic;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Core.Factory
{
    // Что включить в шаблон цеха бота (GDD 8.1: «Премиум» — просторный, с ОТК; «Демпер» — плотный, без контроля).
    public sealed class LayoutTemplate
    {
        public bool QcStation = true;
        public int RawRacks = 4;
        public int FinishedRacks = 3;
        public int Desks = 1;          // столы менеджеров по продажам в офисе
    }

    // Планировщик цеха для ботов (GDD 8.1, решение №22): шаблон раскладки и поиск места под новый станок.
    // Шаблон: главный проход от ворот вдоль всего цеха; ниже прохода — разгрузка, склад сырья, раскрой,
    // формовка, сборка электроники; выше — склад готовой продукции, ОТК, офис, пошив.
    // Станки и стеллажи стоят вплотную к проходу и обращены к нему.
    // Раскладка сначала проигрывается на черновой копии цеха, потом превращается в обычные команды.
    public static class FloorPlanner
    {
        private struct Band
        {
            public ZoneType Zone;
            public int X0, X1;
            public bool Top;
        }

        public static int AisleRow(FactoryFloor floor) => floor.Gate.y;

        private static List<Band> Bands(int width) => new List<Band>
        {
            new Band { Zone = ZoneType.Receiving, X0 = 0, X1 = 3 },
            new Band { Zone = ZoneType.RawStorage, X0 = 4, X1 = 9 },
            new Band { Zone = ZoneType.Cutting, X0 = 10, X1 = 17 },
            new Band { Zone = ZoneType.Molding, X0 = 18, X1 = 25 },
            new Band { Zone = ZoneType.ElectronicsAssembly, X0 = 26, X1 = width - 1 },
            new Band { Zone = ZoneType.FinishedStorage, X0 = 0, X1 = 5, Top = true },
            new Band { Zone = ZoneType.QualityControl, X0 = 6, X1 = 8, Top = true },
            new Band { Zone = ZoneType.Office, X0 = 9, X1 = 11, Top = true },
            new Band { Zone = ZoneType.Sewing, X0 = 12, X1 = width - 1, Top = true },
        };

        // Команды начальной раскладки: зоны, проход, стеллажи, ОТК, офис и станки из списка.
        public static List<CompanyCommand> InitialLayout(DataCatalog catalog, LayoutTemplate template, IEnumerable<string> machineIds)
        {
            var scratch = new FactoryFloor(catalog.Floor);
            var commands = new List<CompanyCommand>();
            int row = AisleRow(scratch);
            const int depth = 4;

            foreach (var band in Bands(scratch.Width))
            {
                int y = band.Top ? row + 1 : row - depth;
                Add(commands, scratch, catalog, new PaintZoneCommand(band.X0, y, band.X1 - band.X0 + 1, depth, band.Zone));
            }
            Add(commands, scratch, catalog, new SetAisleCommand(0, row, scratch.Width, 1, true));

            var raw = BandOf(ZoneType.RawStorage, scratch.Width);
            for (int i = 0; i < template.RawRacks && raw.X0 + i <= raw.X1; i++)
                Add(commands, scratch, catalog, new PlaceObjectCommand(FloorObjectKind.Rack, null, raw.X0 + i, row - 1, Facing.North));
            var finished = BandOf(ZoneType.FinishedStorage, scratch.Width);
            for (int i = 0; i < template.FinishedRacks && finished.X0 + i <= finished.X1; i++)
                Add(commands, scratch, catalog, new PlaceObjectCommand(FloorObjectKind.Rack, null, finished.X0 + i, row + 1, Facing.South));
            if (template.QcStation)
                Add(commands, scratch, catalog, new PlaceObjectCommand(FloorObjectKind.QcTable, null, BandOf(ZoneType.QualityControl, scratch.Width).X0, row + 1, Facing.South));
            var office = BandOf(ZoneType.Office, scratch.Width);
            for (int i = 0; i < template.Desks && office.X0 + i <= office.X1; i++)
                Add(commands, scratch, catalog, new PlaceObjectCommand(FloorObjectKind.Desk, null, office.X0 + i, row + 1, Facing.South));

            foreach (string machineId in machineIds)
            {
                var spot = FindMachineSpot(scratch, catalog, machineId);
                if (spot.HasValue) Add(commands, scratch, catalog, PlaceObjectCommand.Machine(machineId, spot.Value.x, spot.Value.y, spot.Value.facing));
            }
            return commands;
        }

        // Свободное место под станок у главного прохода в его зоне; null — места нет.
        public static (int x, int y, Facing facing)? FindMachineSpot(FactoryFloor floor, DataCatalog catalog, string machineId)
        {
            var def = catalog.Machine(machineId);
            var zone = (ZoneType)(int)def.Zone;
            int row = AisleRow(floor);
            foreach (var band in Bands(floor.Width))
            {
                if (band.Zone != zone) continue;
                var facing = band.Top ? Facing.South : Facing.North;
                int y = band.Top ? row + 1 : row - def.SizeY;
                for (int x = band.X0; x + def.SizeX - 1 <= band.X1; x++)
                    if (floor.CanPlace(def.SizeX, def.SizeY, x, y, facing, z => z == zone) == null) return (x, y, facing);
            }
            return null;
        }

        private static Band BandOf(ZoneType zone, int width) => Bands(width).Find(b => b.Zone == zone);

        // Применить команду к черновой копии (чтобы следующие места считались с её учётом) и запомнить её.
        private static void Add(List<CompanyCommand> commands, FactoryFloor scratch, DataCatalog catalog, CompanyCommand command)
        {
            switch (command)
            {
                case PaintZoneCommand paint: scratch.Paint(paint.X, paint.Y, paint.Width, paint.Height, paint.Zone); break;
                case SetAisleCommand aisle: scratch.SetAisle(aisle.X, aisle.Y, aisle.Width, aisle.Height, aisle.Aisle); break;
                case PlaceObjectCommand place:
                    var (sizeX, sizeY) = FloorRules.Size(catalog, place.Kind, place.MachineId);
                    scratch.Place(place.Kind, place.MachineId ?? "", sizeX, sizeY, place.X, place.Y, place.Facing, place.Feed, 0, 0);
                    break;
            }
            commands.Add(command);
        }
    }
}
