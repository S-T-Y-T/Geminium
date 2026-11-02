using System;
using System.Collections.Generic;
using Geminium.Core.Data;

namespace Geminium.Core.Factory
{
    // Цех компании на сетке (GDD 7.6.1): зоны-«комнаты», проходы и объекты.
    // Ворота — клетка-проход на левой стене посередине. Разнорабочие ходят только по проходам,
    // объекты стоят только на клетках своей зоны и не на проходах.
    // Version растёт при любом изменении планировки — по нему сбрасывается кеш маршрутов.
    public sealed class FactoryFloor
    {
        private readonly List<FloorObject> objects = new List<FloorObject>();
        private ZoneType[] zones;
        private bool[] aisles;
        private int[] occupants;
        private int nextObjectId = 1;

        public int Width { get; private set; }
        public int Height { get; }
        public int Expansions { get; private set; }
        public int Version { get; private set; }
        public IReadOnlyList<FloorObject> Objects => objects;
        public (int x, int y) Gate => (0, Height / 2);

        public FactoryFloor(FloorSettings settings)
        {
            Width = settings.BuildingWidth;
            Height = settings.BuildingHeight;
            zones = new ZoneType[Width * Height];
            aisles = new bool[Width * Height];
            occupants = new int[Width * Height];
            for (int i = 0; i < occupants.Length; i++) occupants[i] = -1;
            var (gx, gy) = Gate;
            zones[Index(gx, gy)] = ZoneType.Receiving;
            aisles[Index(gx, gy)] = true;
        }

        public int Index(int x, int y) => y * Width + x;
        public (int x, int y) Cell(int index) => (index % Width, index / Width);
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public ZoneType ZoneAt(int x, int y) => InBounds(x, y) ? zones[Index(x, y)] : ZoneType.None;
        public bool IsAisle(int x, int y) => InBounds(x, y) && aisles[Index(x, y)];
        public bool IsAisle(int index) => aisles[index];

        public FloorObject ObjectAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            int id = occupants[Index(x, y)];
            return id < 0 ? null : FindObject(id);
        }

        public FloorObject FindObject(int id)
        {
            foreach (var o in objects)
                if (o.Id == id) return o;
            return null;
        }

        public IEnumerable<FloorObject> ObjectsOfKind(FloorObjectKind kind)
        {
            foreach (var o in objects)
                if (o.Kind == kind) yield return o;
        }

        // Покрасить клетки прямоугольника в зону. Клетки под объектами не меняются, ворота — тоже.
        internal void Paint(int x, int y, int w, int h, ZoneType zone)
        {
            ForEachCell(x, y, w, h, i => { if (occupants[i] < 0 && !IsGate(i)) zones[i] = zone; });
            Version++;
        }

        // Отметить клетки как проход (или снять отметку). Клетки под объектами пропускаются.
        internal void SetAisle(int x, int y, int w, int h, bool aisle)
        {
            ForEachCell(x, y, w, h, i => { if (occupants[i] < 0 && !IsGate(i)) aisles[i] = aisle; });
            Version++;
        }

        // Можно ли поставить объект: в границах, на свободных клетках нужной зоны, не на проходе.
        public string CanPlace(int sizeX, int sizeY, int x, int y, Facing facing, Func<ZoneType, bool> zoneAllowed, int ignoreObjectId = -1)
        {
            bool turned = facing == Facing.West || facing == Facing.East;
            int w = turned ? sizeY : sizeX, h = turned ? sizeX : sizeY;
            for (int cx = x; cx < x + w; cx++)
            for (int cy = y; cy < y + h; cy++)
            {
                if (!InBounds(cx, cy)) return "Объект выходит за стены цеха.";
                int i = Index(cx, cy);
                if (aisles[i]) return "Объект нельзя ставить на проход.";
                if (occupants[i] >= 0 && occupants[i] != ignoreObjectId) return "Место занято другим объектом.";
                if (!zoneAllowed(zones[i])) return "Объект стоит не в своей зоне.";
            }
            return null;
        }

        internal FloorObject Place(FloorObjectKind kind, string defId, int sizeX, int sizeY, int x, int y, Facing facing, FeedSide feed,
            long price, int month)
        {
            var o = new FloorObject(nextObjectId++, kind, defId, sizeX, sizeY, price, month) { X = x, Y = y, Facing = facing, Feed = feed };
            objects.Add(o);
            Occupy(o, o.Id);
            Version++;
            return o;
        }

        internal void Move(FloorObject o, int x, int y, Facing facing)
        {
            Occupy(o, -1);
            o.X = x;
            o.Y = y;
            o.Facing = facing;
            Occupy(o, o.Id);
            Version++;
        }

        internal void Remove(FloorObject o)
        {
            Occupy(o, -1);
            objects.Remove(o);
            Version++;
        }

        internal void SetFeed(FloorObject o, FeedSide feed)
        {
            o.Feed = feed;
            Version++;
        }

        // Пристроить столбцы справа (GDD 7.6.1: соседние блоки можно арендовать).
        internal void Expand(int columns)
        {
            int newWidth = Width + columns;
            var newZones = new ZoneType[newWidth * Height];
            var newAisles = new bool[newWidth * Height];
            var newOccupants = new int[newWidth * Height];
            for (int i = 0; i < newOccupants.Length; i++) newOccupants[i] = -1;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                newZones[y * newWidth + x] = zones[Index(x, y)];
                newAisles[y * newWidth + x] = aisles[Index(x, y)];
                newOccupants[y * newWidth + x] = occupants[Index(x, y)];
            }
            Width = newWidth;
            zones = newZones;
            aisles = newAisles;
            occupants = newOccupants;
            Expansions++;
            Version++;
        }

        private bool IsGate(int index) => index == Index(Gate.x, Gate.y);

        private void Occupy(FloorObject o, int value)
        {
            for (int cx = o.X; cx < o.X + o.Width; cx++)
            for (int cy = o.Y; cy < o.Y + o.Height; cy++)
                occupants[Index(cx, cy)] = value;
        }

        private void ForEachCell(int x, int y, int w, int h, Action<int> action)
        {
            for (int cx = Math.Max(0, x); cx < Math.Min(Width, x + w); cx++)
            for (int cy = Math.Max(0, y); cy < Math.Min(Height, y + h); cy++)
                action(Index(cx, cy));
        }
    }
}
