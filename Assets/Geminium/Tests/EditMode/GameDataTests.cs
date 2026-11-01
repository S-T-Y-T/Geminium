using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Geminium.Core.Data;

namespace Geminium.Tests.EditMode
{
    // Проверка данных: валидатор должен находить ошибки, а каталог — искать по Id.
    public class GameDataTests
    {
        private static GameData ValidData() => TestData.Minimal();

        private static void AssertHasError(GameData data, string fragment)
        {
            var errors = GameDataValidator.Validate(data);
            Assert.That(errors.Any(e => e.Contains(fragment)), "Ожидалась ошибка с текстом «" + fragment + "», получено:\n" + string.Join("\n", errors));
        }

        [Test]
        public void ValidData_HasNoErrors()
        {
            CollectionAssert.IsEmpty(GameDataValidator.Validate(ValidData()));
        }

        [Test]
        public void Catalog_FindsByIdAndSortsMachinesByTier()
        {
            var catalog = new DataCatalog(ValidData());
            Assert.AreEqual(25000, catalog.Material("cotton").BasePrice);
            Assert.AreEqual(ProductionZone.Cutting, catalog.Profession("cutter").OperatedZone);
            CollectionAssert.AreEqual(new[] { "cutting_knife_table", "cutting_band" }, catalog.MachinesForZone(ProductionZone.Cutting).Select(m => m.Id));
            Assert.Throws<KeyNotFoundException>(() => catalog.Product("helmet"));
        }

        [Test]
        public void DuplicateId_IsReported()
        {
            var data = ValidData();
            data.Materials.Add(new MaterialDef { Id = "cotton", BasePrice = 1, UnitsPerCell = 1 });
            AssertHasError(data, "«cotton» встречается дважды");
        }

        [Test]
        public void BadIdFormat_IsReported()
        {
            var data = ValidData();
            data.Professions[0].Id = "Cutter!";
            AssertHasError(data, "Id «Cutter!»");
        }

        [Test]
        public void UnknownMaterialInRecipe_IsReported()
        {
            var data = ValidData();
            data.Products[0].Operations[0].Inputs[0].MaterialId = "silk";
            AssertHasError(data, "нет сырья «silk»");
        }

        [Test]
        public void ZoneWithoutMachines_IsReported()
        {
            var data = ValidData();
            data.Products[0].Operations.Add(new OperationDef { Zone = ProductionZone.Sewing, BaseTimeMinutes = 60 });
            AssertHasError(data, "нет ни одного станка для зоны Sewing");
        }

        [Test]
        public void OperatorIsRequiredOnlyForUnlockedProducts()
        {
            var data = ValidData();
            data.Professions.Clear();
            AssertHasError(data, "нет профессии, которая работает в зоне Cutting");

            data.Products[0].UnlockedByDefault = false;
            CollectionAssert.IsEmpty(GameDataValidator.Validate(data));
        }

        [Test]
        public void NonPositiveStartingCash_IsReported()
        {
            var data = ValidData();
            data.StartingCash = 0;
            AssertHasError(data, "Стартовый капитал");
        }

        [Test]
        public void InvalidSchedule_IsReported()
        {
            var data = ValidData();
            data.DefaultWorkSchedule.ShiftsPerDay = 4;
            AssertHasError(data, "График работы");
        }

        [Test]
        public void Catalog_RejectsInvalidDataWithAllErrors()
        {
            var data = ValidData();
            data.Materials[0].BasePrice = 0;
            data.Machines[0].Tier = 5;
            var exception = Assert.Throws<GameDataException>(() => new DataCatalog(data));
            Assert.AreEqual(2, exception.Errors.Count);
        }
    }
}
