using System;
using System.Collections.Generic;

namespace Geminium.Core.Companies
{
    // Запасы компании: сырьё (дробное количество — м², м) и готовая продукция (штуки).
    // У каждой позиции хранятся среднее качество 0–100 и средняя себестоимость единицы:
    // качество нужно для формул качества и привлекательности (GDD 7.2, 7.6.4), себестоимость —
    // для P&L и стоимости склада в капитализации (GDD 7.8). Поступления усредняются по количеству.
    // Порядок перебора стабильный (по Id) — это нужно для детерминизма.
    public sealed class Inventory
    {
        private const double Epsilon = 1e-9;

        private sealed class Lot
        {
            public double Quantity;
            public double Quality;
            public double UnitCost;
        }

        private readonly SortedDictionary<string, Lot> materials = new SortedDictionary<string, Lot>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Lot> products = new SortedDictionary<string, Lot>(StringComparer.Ordinal);

        public double MaterialQuantity(string materialId) => Get(materials, materialId)?.Quantity ?? 0;
        public double MaterialQuality(string materialId) => Get(materials, materialId)?.Quality ?? 0;
        public long ProductUnits(string productId) => (long)Math.Round(Get(products, productId)?.Quantity ?? 0);
        public double ProductQuality(string productId) => Get(products, productId)?.Quality ?? 0;

        public IEnumerable<KeyValuePair<string, double>> Materials
        {
            get { foreach (var pair in materials) yield return new KeyValuePair<string, double>(pair.Key, pair.Value.Quantity); }
        }

        public IEnumerable<KeyValuePair<string, long>> Products
        {
            get { foreach (var pair in products) yield return new KeyValuePair<string, long>(pair.Key, (long)Math.Round(pair.Value.Quantity)); }
        }

        // Стоимость запасов по себестоимости, сумы.
        public long TotalValue()
        {
            double value = 0;
            foreach (var lot in materials.Values) value += lot.Quantity * lot.UnitCost;
            foreach (var lot in products.Values) value += lot.Quantity * lot.UnitCost;
            return (long)Math.Round(value);
        }

        public void AddMaterial(string materialId, double quantity, double quality, double unitCost) =>
            Add(materials, materialId, quantity, quality, unitCost);

        public bool TryTakeMaterial(string materialId, double quantity, out double quality, out double unitCost) =>
            TryTake(materials, materialId, quantity, out quality, out unitCost);

        public void AddProducts(string productId, long units, double quality, double unitCost) =>
            Add(products, productId, units, quality, unitCost);

        public bool TryTakeProducts(string productId, long units, out double quality, out double unitCost) =>
            TryTake(products, productId, units, out quality, out unitCost);

        private static Lot Get(SortedDictionary<string, Lot> lots, string id) =>
            id != null && lots.TryGetValue(id, out var lot) ? lot : null;

        private static void Add(SortedDictionary<string, Lot> lots, string id, double quantity, double quality, double unitCost)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Нужен Id.", nameof(id));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Количество должно быть больше нуля.");
            if (quality < 0 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality), "Качество — от 0 до 100.");
            if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost), "Себестоимость не может быть отрицательной.");

            if (!lots.TryGetValue(id, out var lot)) lots[id] = lot = new Lot();
            double total = lot.Quantity + quantity;
            lot.Quality = (lot.Quality * lot.Quantity + quality * quantity) / total;
            lot.UnitCost = (lot.UnitCost * lot.Quantity + unitCost * quantity) / total;
            lot.Quantity = total;
        }

        private static bool TryTake(SortedDictionary<string, Lot> lots, string id, double quantity, out double quality, out double unitCost)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Количество должно быть больше нуля.");
            quality = 0;
            unitCost = 0;
            var lot = Get(lots, id);
            if (lot == null || lot.Quantity + Epsilon < quantity) return false;

            quality = lot.Quality;
            unitCost = lot.UnitCost;
            lot.Quantity -= quantity;
            if (lot.Quantity < Epsilon) lots.Remove(id);
            return true;
        }
    }
}
