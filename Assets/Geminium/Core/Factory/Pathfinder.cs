using System.Collections.Generic;

namespace Geminium.Core.Factory
{
    // Поиск пути по проходам цеха (поиск в ширину, 4 соседа). Карты расстояний от каждой
    // стартовой клетки кешируются до изменения планировки (FactoryFloor.Version).
    public sealed class Pathfinder
    {
        private readonly FactoryFloor floor;
        private readonly Dictionary<int, int[]> cache = new Dictionary<int, int[]>();
        private int cachedVersion = -1;
        private int cachedWidth = -1;

        public Pathfinder(FactoryFloor floor) { this.floor = floor; }

        // Число шагов по проходам или −1, если пути нет.
        public int Distance(int from, int to)
        {
            if (!floor.IsAisle(from) || !floor.IsAisle(to)) return -1;
            return DistancesFrom(from)[to];
        }

        // Клетки маршрута от from до to включительно; пустой список, если пути нет.
        public List<int> Path(int from, int to)
        {
            var path = new List<int>();
            int distance = Distance(from, to);
            if (distance < 0) return path;

            var map = DistancesFrom(from);
            int current = to;
            path.Add(current);
            while (current != from)
            {
                int next = -1;
                foreach (int n in Neighbors(current))
                    if (map[n] == map[current] - 1) { next = n; break; }
                current = next;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }

        private int[] DistancesFrom(int from)
        {
            if (floor.Version != cachedVersion || floor.Width != cachedWidth)
            {
                cache.Clear();
                cachedVersion = floor.Version;
                cachedWidth = floor.Width;
            }
            if (cache.TryGetValue(from, out var known)) return known;

            var map = new int[floor.Width * floor.Height];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var queue = new Queue<int>();
            map[from] = 0;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int n in Neighbors(current))
                {
                    if (map[n] >= 0 || !floor.IsAisle(n)) continue;
                    map[n] = map[current] + 1;
                    queue.Enqueue(n);
                }
            }
            cache[from] = map;
            return map;
        }

        // Соседи в фиксированном порядке: юг, запад, север, восток — маршрут детерминирован.
        private IEnumerable<int> Neighbors(int index)
        {
            var (x, y) = floor.Cell(index);
            if (y > 0) yield return floor.Index(x, y - 1);
            if (x > 0) yield return floor.Index(x - 1, y);
            if (y < floor.Height - 1) yield return floor.Index(x, y + 1);
            if (x < floor.Width - 1) yield return floor.Index(x + 1, y);
        }
    }
}
