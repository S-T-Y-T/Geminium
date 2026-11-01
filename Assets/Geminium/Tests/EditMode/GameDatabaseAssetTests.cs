using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core.Data;
using Geminium.Game;

namespace Geminium.Tests.EditMode
{
    // Настоящий файл данных игры должен загружаться и проходить проверку.
    public class GameDatabaseAssetTests
    {
        private const string AssetPath = "Assets/Geminium/Data/GameDatabase.asset";

        private static DataCatalog LoadCatalog()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(AssetPath);
            Assert.IsNotNull(database, "Не найден файл данных " + AssetPath);
            return database.CreateCatalog();
        }

        [Test]
        public void GameDatabase_LoadsAndPassesValidation()
        {
            var catalog = LoadCatalog();
            Assert.AreEqual(10, catalog.Materials.Count);
            Assert.AreEqual(10, catalog.Machines.Count);
            Assert.AreEqual(5, catalog.Products.Count);
            Assert.AreEqual(7, catalog.Professions.Count);
        }

        [Test]
        public void GameDatabase_MatchesGddRoutesAndUnlocks()
        {
            var catalog = LoadCatalog();
            CollectionAssert.AreEqual(
                new[] { ProductionZone.Cutting, ProductionZone.Sewing },
                catalog.Product("dobok").Operations.Select(o => o.Zone));
            CollectionAssert.AreEqual(
                new[] { ProductionZone.Cutting, ProductionZone.Molding, ProductionZone.Sewing, ProductionZone.ElectronicsAssembly },
                catalog.Product("e_vest").Operations.Select(o => o.Zone));
            Assert.IsFalse(catalog.Product("e_vest").UnlockedByDefault);
            Assert.AreEqual(3, catalog.MachinesForZone(ProductionZone.Molding).Count);
            Assert.AreEqual(2027, catalog.Calendar.StartYear);
            Assert.AreEqual(40, catalog.Pacing.SecondsPerShiftAtX1);
        }
    }
}
