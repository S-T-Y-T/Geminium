using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;

namespace Geminium.Core.Digital
{
    // Предложение двойника: куда перенести станок и сколько клеток пути это сэкономит на единицу плана.
    public sealed class LayoutMove
    {
        public int ObjectId;
        public int X, Y;
        public Facing Facing;
        public double SavingCellsPerUnit;
        public long Cost;               // перенос с учётом скидки двойника
    }

    // Цифровой двойник цеха (GDD 7.9): «что если» для планировки — оценка пути грузов до оплаты перестановки.
    // Путь станка за месяц = Σ по продуктам плана: сырьё со склада (если операция его расходует), полуфабрикаты
    // от ближайшего станка предыдущей операции, к ближайшему станку следующей или готовое — на склад.
    // Расстояния — в клетках по проходам (Pathfinder). Двойник ищет место в зоне станка с самым коротким путём.
    public static class DigitalTwin
    {
        public static List<LayoutMove> Suggest(GameWorld world, Company company, int maxMoves)
        {
            var catalog = world.Catalog;
            var floor = company.Floor;
            var plans = catalog.Products.Where(p => p.UnlockedByDefault && company.MonthlyPlan(p.Id) > 0)
                .ToDictionary(p => p, p => (double)company.MonthlyPlan(p.Id));
            double totalPlan = plans.Values.Sum();
            var moves = new List<LayoutMove>();
            if (totalPlan <= 0) return moves;

            foreach (var machine in company.Machines.ToList())
            {
                var def = catalog.Machine(machine.DefId);
                var zone = (ZoneType)(int)def.Zone;
                double current = Cost(world, company, machine, machine.AccessCell, plans);
                if (current <= 0) continue;
                LayoutMove best = null;
                double bestCost = current;
                for (int y = 0; y < floor.Height; y++)
                for (int x = 0; x < floor.Width; x++)
                {
                    if (x == machine.X && y == machine.Y) continue;
                    if (floor.CanPlace(def.SizeX, def.SizeY, x, y, machine.Facing, z => z == zone, machine.Id) != null) continue;
                    bool turned = machine.Facing == Facing.West || machine.Facing == Facing.East;
                    var access = FloorObject.AccessCellFor(x, y, turned ? def.SizeY : def.SizeX, turned ? def.SizeX : def.SizeY, machine.Facing);
                    if (!floor.IsAisle(access.x, access.y)) continue;
                    double cost = Cost(world, company, machine, access, plans);
                    if (cost < 0 || cost >= bestCost) continue;
                    bestCost = cost;
                    best = new LayoutMove { ObjectId = machine.Id, X = x, Y = y, Facing = machine.Facing };
                }
                if (best == null) continue;
                best.SavingCellsPerUnit = (current - bestCost) / totalPlan;
                if (best.SavingCellsPerUnit < catalog.Digital.TwinMinSavingCells) continue;
                best.Cost = MoveObjectCommand.RelocationCost(company, machine, world);
                moves.Add(best);
            }
            return moves.OrderByDescending(m => m.SavingCellsPerUnit).Take(maxMoves).ToList();
        }

        // Взвешенный планом путь грузов станка при клетке подхода access; −1 — с этого места что-то недоступно.
        public static double Cost(GameWorld world, Company company, FloorObject machine, (int x, int y) access,
            Dictionary<ProductDef, double> plans)
        {
            var catalog = world.Catalog;
            var floor = company.Floor;
            var zone = catalog.Machine(machine.DefId).Zone;
            int at = floor.Index(access.x, access.y);
            double cost = 0;
            foreach (var pair in plans)
            {
                var ops = pair.Key.Operations;
                for (int k = 0; k < ops.Count; k++)
                {
                    if (ops[k].Zone != zone) continue;
                    double units = pair.Value;
                    if (ops[k].Inputs.Count > 0 && !Add(ref cost, units, Nearest(company, StorageAccess(company, ZoneType.RawStorage), at))) return -1;
                    if (k > 0 && !Add(ref cost, units, Nearest(company, MachineAccess(world, company, ops[k - 1].Zone, machine.Id), at))) return -1;
                    var next = k + 1 < ops.Count ? MachineAccess(world, company, ops[k + 1].Zone, machine.Id) : StorageAccess(company, ZoneType.FinishedStorage);
                    if (!Add(ref cost, units, Nearest(company, next, at))) return -1;
                }
            }
            return cost;
        }

        private static bool Add(ref double cost, double units, int distance)
        {
            if (distance < 0) return false;
            cost += units * distance;
            return true;
        }

        private static int Nearest(Company company, List<int> targets, int from)
        {
            int best = -1;
            foreach (int target in targets)
            {
                int d = company.Paths.Distance(from, target);
                if (d >= 0 && (best < 0 || d < best)) best = d;
            }
            return best;
        }

        // Клетки подхода к стеллажам складской зоны; если стеллажей нет — ворота.
        private static List<int> StorageAccess(Company company, ZoneType zone)
        {
            var floor = company.Floor;
            var result = new List<int>();
            foreach (var rack in floor.ObjectsOfKind(FloorObjectKind.Rack))
            {
                if (floor.ZoneAt(rack.X, rack.Y) != zone) continue;
                var (x, y) = rack.AccessCell;
                if (floor.IsAisle(x, y)) result.Add(floor.Index(x, y));
            }
            if (result.Count == 0) result.Add(floor.Index(floor.Gate.x, floor.Gate.y));
            return result;
        }

        private static List<int> MachineAccess(GameWorld world, Company company, ProductionZone zone, int exceptId)
        {
            var floor = company.Floor;
            var result = new List<int>();
            foreach (var m in company.Machines)
            {
                if (m.Id == exceptId || world.Catalog.Machine(m.DefId).Zone != zone) continue;
                var (x, y) = m.AccessCell;
                if (floor.IsAisle(x, y)) result.Add(floor.Index(x, y));
            }
            if (result.Count == 0) result.Add(floor.Index(floor.Gate.x, floor.Gate.y));
            return result;
        }
    }
}
