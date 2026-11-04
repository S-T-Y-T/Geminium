using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Lab;
using Geminium.Core.Modes;
using Geminium.Core.Save;
using Geminium.Core.Time;
using Geminium.Game;

namespace Geminium.Tests.EditMode
{
    // Сохранения (GDD 4.2) — этап 4: журнал команд, JSON, загрузка повтором журнала.
    public class SaveTests
    {
        private static DataCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();

        [Test]
        public void MiniJson_RoundTripsValues()
        {
            var value = new Dictionary<string, object>
            {
                ["text"] = "Ş Ç Ö Ğ «кавычки» \"q\" \\ \n\t", ["long"] = 9_000_000_000L, ["negative"] = -42L, ["double"] = 0.1,
                ["small"] = 1e-7, ["yes"] = true, ["no"] = false, ["null"] = null,
                ["list"] = new List<object> { 1L, "two", new Dictionary<string, object> { ["x"] = 2.5 }, new List<object>() },
            };
            string json = MiniJson.Write(value);
            var back = (Dictionary<string, object>)MiniJson.Read(json);
            Assert.AreEqual(json, MiniJson.Write(back));
            Assert.AreEqual("Ş Ç Ö Ğ «кавычки» \"q\" \\ \n\t", back["text"]);
            Assert.AreEqual(9_000_000_000L, back["long"]);
            Assert.AreEqual(0.1, back["double"]);
            Assert.IsNull(back["null"]);
            Assert.Throws<FormatException>(() => MiniJson.Read("{\"a\": }"));
        }

        // Любая команда ядра переживает JSON: у каждого параметра конструктора есть свойство с тем же именем.
        [Test]
        public void CommandCodec_RoundTripsEveryCommandType()
        {
            Assert.Greater(CommandCodec.CommandTypes.Count, 30);
            foreach (var type in CommandCodec.CommandTypes)
            {
                var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                var command = (CompanyCommand)ctor.Invoke(ctor.GetParameters().Select(p => Sample(p.ParameterType)).ToArray());
                string json = MiniJson.Write(CommandCodec.Encode(command));
                var decoded = CommandCodec.Decode(MiniJson.Read(json));
                Assert.AreEqual(type, decoded.GetType(), type.Name);
                Assert.AreEqual(json, MiniJson.Write(CommandCodec.Encode(decoded)), type.Name);
            }
        }

        private static object Sample(Type type)
        {
            if (type == typeof(string)) return "dobok";
            if (type == typeof(int)) return 3;
            if (type == typeof(long)) return 1_234_567L;
            if (type == typeof(double)) return 2.5;
            if (type == typeof(bool)) return true;
            if (type.IsEnum) return Enum.GetValues(type).Cast<object>().Last();
            if (type == typeof(WorkScheduleSettings))
                return new WorkScheduleSettings { WorkingDaysPerWeek = 6, ShiftsPerDay = 2, ShiftLengthMinutes = 480, FirstShiftStartMinute = 420 };
            if (typeof(IEnumerable<CompanyCommand>).IsAssignableFrom(type))
                return new List<CompanyCommand> { new SetRetailPriceCommand("dobok", 1), new SetMonthlyPlanCommand("dobok", 2) };
            throw new NotSupportedException(type.Name);
        }

        // Журнал пишет только принятые команды игрока; боты и отклонённые команды в него не попадают.
        [Test]
        public void CommandLog_KeepsOnlyAcceptedPlayerCommands()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Free(3));
            var world = session.World;
            Assert.AreEqual(1, world.Simulation.Now.Minute, "партия отдаётся после первой минуты");
            Assert.AreEqual(0, world.CommandLog.Count, "решения ботов не пишутся");
            Assert.IsNotNull(world.Submit(session.Player, new SetMonthlyPlanCommand("dobok", -1)));
            Assert.IsNull(world.Submit(session.Player, new SetMonthlyPlanCommand("dobok", 50)));
            Assert.AreEqual(1, world.CommandLog.Count);
            Assert.AreEqual(1, world.CommandLog[0].Minute);
            Assert.AreEqual(GameSession.PlayerId, world.CommandLog[0].CompanyId);
        }

        // Главное свойство сохранений: загрузка дальше идёт так же, как партия без сохранения.
        [Test]
        public void SaveGame_RestoreContinuesExactlyLikeUninterruptedGame()
        {
            var catalog = Catalog();
            var original = GameSession.Start(catalog, GameSetup.Free(11));
            TestHuman.Play(original, 2);
            original.World.Simulation.RunMinutes(3 * SimTime.MinutesPerDay + 17);
            string json = SaveGame.Capture(original, "слот 1", "2026-11-04 12:00").ToJson();
            original.World.Simulation.RunMonths(2);

            var save = SaveGame.FromJson(json);
            Assert.AreEqual("слот 1", save.Label);
            Assert.AreEqual(2, save.MonthIndex);
            Assert.Greater(save.Commands.Count, 5);
            Assert.IsTrue(save.DataMatches(catalog));
            var restored = save.Restore(catalog);
            Assert.AreEqual(save.Minute, restored.World.Simulation.Now.Minute);
            Assert.AreEqual(save.Commands.Count, restored.World.CommandLog.Count, "повтор журнала восстанавливает и сам журнал");
            Assert.AreEqual(json, SaveGame.Capture(restored, "слот 1", "2026-11-04 12:00").ToJson());

            restored.World.Simulation.RunMonths(2);
            Assert.AreEqual(KpiExport.ToCsv(original.World), KpiExport.ToCsv(restored.World));
        }

        [Test]
        public void SaveGame_RejectsForeignOrDamagedFiles()
        {
            var catalog = Catalog();
            Assert.Throws<SaveException>(() => SaveGame.FromJson("{\"format\":\"other\"}"));
            Assert.Throws<SaveException>(() => SaveGame.FromJson("не JSON"));

            var save = SaveGame.Capture(GameSession.Start(catalog, GameSetup.Free(1)));
            save.Version = SaveGame.FormatVersion + 1;
            Assert.Throws<SaveException>(() => save.Restore(catalog));

            // Команда, которую мир не примет, — расхождение журнала.
            var broken = SaveGame.Capture(GameSession.Start(catalog, GameSetup.Free(1)));
            broken.Commands.Add(new LoggedCommand { Minute = 5, CompanyId = GameSession.PlayerId, Command = new HireCommand(999_999) });
            broken.Minute = 10;
            Assert.Throws<SaveException>(() => broken.Restore(catalog));
        }

        // Слоты на диске: запись с заменой, чтение, пустой слот, автосохранение в конце месяца.
        [Test]
        public void SaveStorage_WritesSlotsAndAutosavesMonthly()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "geminium_saves_" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveStorage(folder);
                var session = GameSession.Start(Catalog(), GameSetup.Free(2));
                Assert.IsNull(storage.Read(SaveStorage.ManualSlot(1)));
                storage.Write(SaveStorage.ManualSlot(1), SaveGame.Capture(session, "первый"));
                session.World.Submit(session.Player, new SetMonthlyPlanCommand("dobok", 10));
                storage.Write(SaveStorage.ManualSlot(1), SaveGame.Capture(session, "второй"));
                var read = storage.Read(SaveStorage.ManualSlot(1));
                Assert.AreEqual("второй", read.Label);
                Assert.AreEqual(1, read.Commands.Count);
                CollectionAssert.AreEqual(new[] { "autosave", "slot1", "slot2", "slot3" }, SaveStorage.Slots().ToArray());

                using (new Autosave(session, storage)) session.World.Simulation.RunMonths(1);
                var autosave = storage.Read(SaveStorage.AutosaveSlot);
                Assert.AreEqual(1, autosave.MonthIndex);
                Assert.AreEqual(session.World.Simulation.Now.Minute, autosave.Minute);
                storage.Delete(SaveStorage.ManualSlot(1));
                Assert.IsNull(storage.Read(SaveStorage.ManualSlot(1)));
                System.IO.File.WriteAllText(storage.PathOf(SaveStorage.ManualSlot(2)), "{ сломано");
                Assert.Throws<SaveException>(() => storage.Read(SaveStorage.ManualSlot(2)));
            }
            finally
            {
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
        }

        [Test]
        public void DataFingerprint_ChangesWithData()
        {
            var a = new DataCatalog(TestData.Minimal());
            var b = new DataCatalog(TestData.Minimal());
            var changed = TestData.Minimal();
            changed.StartingCash += 1;
            Assert.AreEqual(a.Fingerprint, b.Fingerprint);
            Assert.AreNotEqual(a.Fingerprint, new DataCatalog(changed).Fingerprint);
        }
    }

    // «Человек» для тестов: решения подаются извне, между минутами — как из интерфейса, и попадают в журнал.
    internal static class TestHuman
    {
        // Старт (раскрой и пошив по шаблону, найм, план, цена, поставки), утром дня 1 — люди к станкам,
        // в начале каждого следующего месяца до untilMonth — цена, график 6/1 и пакет команд.
        public static void Play(GameSession session, int untilMonth)
        {
            var world = session.World;
            var catalog = world.Catalog;
            var company = session.Player;
            var machines = new[] { ProductionZone.Cutting, ProductionZone.Sewing }
                .Select(z => catalog.MachinesForZone(z).OrderBy(m => m.Price).First()).ToList();
            Submit(session, new BatchCommand(FloorPlanner.InitialLayout(catalog, new LayoutTemplate { QcStation = false, Desks = 0 }, machines.Select(m => m.Id))));
            foreach (string professionId in machines.Select(m => catalog.OperatorOf(m.Zone).Id).Append(catalog.Staff.LaborerProfessionId))
            {
                var candidate = world.Staff.Candidates.Where(c => c.ProfessionId == professionId).OrderBy(c => c.Id).First();
                Submit(session, new HireCommand(candidate.Id));
            }
            Submit(session, new SetMonthlyPlanCommand("dobok", 120));
            Submit(session, new SetRetailPriceCommand("dobok", 460_000));
            foreach (var input in catalog.Product("dobok").Operations.SelectMany(o => o.Inputs))
            {
                var supplier = catalog.SuppliersOf(input.MaterialId).First();
                Submit(session, new SetSupplyScheduleCommand(supplier.Id, Math.Max(supplier.MinBatch, Math.Ceiling(input.Amount * 40)), DeliveryFrequency.Weekly));
            }

            world.Simulation.RunUntil(new SimTime(SimTime.MinutesPerDay));
            var busy = new HashSet<int>();
            foreach (var worker in company.Workers)
            {
                var zone = catalog.Profession(worker.ProfessionId).OperatedZone;
                var machine = company.Machines.FirstOrDefault(m => !busy.Contains(m.Id) && catalog.Machine(m.DefId).Zone == zone);
                if (zone == ProductionZone.None || machine == null) continue;
                busy.Add(machine.Id);
                Submit(session, new AssignWorkerCommand(worker.Id, machine.Id, 0));
            }

            while (world.Simulation.MonthIndex < untilMonth)
            {
                world.Simulation.RunMonths(1);
                if (world.Simulation.MonthIndex >= untilMonth) break;
                Submit(session, new SetRetailPriceCommand("dobok", 440_000 + 10_000 * world.Simulation.MonthIndex));
                Submit(session, new SetWorkScheduleCommand(new WorkScheduleSettings { WorkingDaysPerWeek = 6 }));
                Submit(session, new BatchCommand(new CompanyCommand[] { new SetMonthlyPlanCommand("dobok", 150), new SetInputQcCommand(true) }));
            }
        }

        private static void Submit(GameSession session, CompanyCommand command) =>
            Assert.IsNull(session.World.Submit(session.Player, command), command.GetType().Name);
    }
}
