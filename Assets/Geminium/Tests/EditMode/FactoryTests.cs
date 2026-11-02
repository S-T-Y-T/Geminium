using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Lab;
using Geminium.Game;

namespace Geminium.Tests.EditMode
{
    // Маленький цех для тестов: склад сырья, раскрой, склад готовой продукции, ОТК, проход у ворот,
    // по стеллажу на склад и раскройный стол. Люди нанимаются с рынка труда.
    internal sealed class TestFactory
    {
        public GameWorld World;
        public Company Company;
        public PlayerController Player;
        public FloorObject Machine;

        public static TestFactory Build(Action<GameData> tweak = null, bool laborer = true, bool cutter = true,
            bool mechanic = false, bool inspector = false, bool qcTable = false)
        {
            var f = new TestFactory { World = TestData.World(d => { TestData.Calm(d); tweak?.Invoke(d); }), Player = new PlayerController() };
            f.Company = f.World.AddCompany("player", f.Player);
            var commands = new List<CompanyCommand>
            {
                new PaintZoneCommand(1, 6, 4, 4, ZoneType.RawStorage),
                new PaintZoneCommand(5, 6, 6, 4, ZoneType.Cutting),
                new PaintZoneCommand(1, 11, 4, 4, ZoneType.FinishedStorage),
                new PaintZoneCommand(5, 11, 3, 4, ZoneType.QualityControl),
                new SetAisleCommand(0, 10, 12, 1, true),
                new PlaceObjectCommand(FloorObjectKind.Rack, null, 1, 9, Facing.North),
                new PlaceObjectCommand(FloorObjectKind.Rack, null, 1, 11, Facing.South),
                PlaceObjectCommand.Machine("cutting_knife_table", 5, 9, Facing.North),
            };
            if (qcTable) commands.Add(new PlaceObjectCommand(FloorObjectKind.QcTable, null, 5, 11, Facing.South));
            Assert.IsNull(f.Player.Submit(new BatchCommand(commands)));
            f.World.Simulation.Step(); // минута 0: планировка применена, рынок труда открыт
            f.Machine = f.Company.Machines.Single();

            if (laborer) f.Hire("laborer");
            if (cutter) f.Hire("cutter");
            if (mechanic) f.Hire("mechanic");
            if (inspector) f.Hire("qc_inspector");
            f.World.Simulation.Step();
            var op = f.Company.Workers.FirstOrDefault(w => w.ProfessionId == "cutter");
            if (op != null) Assert.IsNull(f.Player.Submit(new AssignWorkerCommand(op.Id, f.Machine.Id, 0)));
            f.World.Simulation.Step();
            return f;
        }

        public void Hire(string profession)
        {
            var candidate = World.Staff.Candidates.First(c => c.ProfessionId == profession);
            Assert.IsNull(Player.Submit(new HireCommand(candidate.Id)));
        }

        public void Supply(double cotton = 500) => Assert.IsNull(Player.Submit(new PlaceOrderCommand("cotton_test", cotton)));
        public void Plan(int units) => Assert.IsNull(Player.Submit(new SetMonthlyPlanCommand("dobok", units)));
        public void RunDays(int days) => World.Simulation.RunMinutes(days * 1440L);
        public long Produced => Get(Company.CurrentMonth.Produced) + Get(Company.CurrentMonth.Defective);
        private static long Get(SortedDictionary<string, long> d) => d.TryGetValue("dobok", out long v) ? v : 0;
    }

    // Пространственный цех (этап 2): планировка, логистика, производство, ТО, персонал, ОТК, аналитика.
    public class FactoryTests
    {
        // Пустой цех, в котором планировку меняют только командами — как это делает игрок.
        private static (GameWorld, Company, PlayerController) EmptyFloor(Action<GameData> tweak = null)
        {
            var world = TestData.World(tweak);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            return (world, company, player);
        }

        [Test]
        public void Floor_PlacementRulesAndAccessCell()
        {
            var (world, company, player) = EmptyFloor(d => d.Machines[0].SizeX = 2); // ленточный раскрой 2×1
            Assert.IsNull(player.Submit(new BatchCommand(new CompanyCommand[]
            {
                new PaintZoneCommand(5, 6, 6, 4, ZoneType.Cutting),
                new SetAisleCommand(0, 10, 12, 1, true),
            })));
            world.Simulation.Step();

            Assert.AreEqual("Объект нельзя ставить на проход.", player.Submit(PlaceObjectCommand.Machine("cutting_band", 5, 10, Facing.North)));
            Assert.AreEqual("Объект стоит не в своей зоне.", player.Submit(PlaceObjectCommand.Machine("cutting_band", 12, 9, Facing.North)));
            Assert.AreEqual("Объект выходит за стены цеха.", player.Submit(PlaceObjectCommand.Machine("cutting_band", -1, 9, Facing.North)));
            Assert.IsNull(player.Submit(PlaceObjectCommand.Machine("cutting_band", 5, 9, Facing.North)));
            world.Simulation.Step();

            var o = company.Machines.Single();
            Assert.AreEqual(2, o.Width);
            Assert.AreEqual((6, 10), o.AccessCell);
            Assert.AreSame(o, company.Floor.ObjectAt(6, 9));
            Assert.AreEqual("Место занято другим объектом.", player.Submit(PlaceObjectCommand.Machine("cutting_knife_table", 6, 9, Facing.North)));
            Assert.AreEqual(80000000 - 25000000, company.Cash);
        }

        [Test]
        public void Pathfinder_WalksOnlyAislesAndRebuildsAfterChanges()
        {
            var (world, company, player) = EmptyFloor();
            Assert.IsNull(player.Submit(new SetAisleCommand(0, 10, 10, 1, true)));
            world.Simulation.Step();
            var floor = company.Floor;
            int gate = floor.Index(0, 10), end = floor.Index(9, 10), off = floor.Index(9, 12);

            Assert.AreEqual(9, company.Paths.Distance(gate, end));
            Assert.AreEqual(10, company.Paths.Path(gate, end).Count);
            Assert.AreEqual(-1, company.Paths.Distance(gate, off));

            Assert.IsNull(player.Submit(new SetAisleCommand(9, 11, 1, 2, true)));
            world.Simulation.Step();
            Assert.AreEqual(11, company.Paths.Distance(gate, off));
        }

        [Test]
        public void SmallFactory_ProducesThroughLogistics()
        {
            var f = TestFactory.Build();
            f.Supply();
            f.Plan(30);
            f.RunDays(7);

            Assert.Greater(f.Produced, 0);
            Assert.Greater(f.Company.CurrentMonth.LogisticsMeters, 0);
            Assert.Greater(f.Company.Production.SpaghettiWeek.Sum(), 0);
            Assert.Greater(f.Machine.Machine.BusyMinutes, 0);
            Assert.Greater(f.Company.Inventory.ProductUnits("dobok") + f.Company.CurrentMonth.Sold.Values.Sum(), 0);
        }

        [Test]
        public void WithoutLaborer_MachineStarves()
        {
            var f = TestFactory.Build(laborer: false);
            f.Supply();
            f.Plan(30);
            f.RunDays(7);
            Assert.AreEqual(0, f.Produced);
            Assert.Greater(f.Company.CurrentMonth.DowntimeMinutes[(int)DowntimeCause.NoMaterials], 0);
        }

        [Test]
        public void WithoutOperator_MachineWaits()
        {
            var f = TestFactory.Build(cutter: false);
            f.Supply();
            f.Plan(30);
            f.RunDays(7);
            Assert.AreEqual(0, f.Produced);
            Assert.Greater(f.Company.CurrentMonth.DowntimeMinutes[(int)DowntimeCause.NoOperator], 0);
        }

        [Test]
        public void Mechanic_DoesPlannedMaintenanceInTheShiftWindow()
        {
            var f = TestFactory.Build(d => d.Maintenance.DefaultIntervalOperations = 3, mechanic: true);
            f.Supply();
            f.Plan(30);
            f.RunDays(7);
            Assert.Greater(f.Company.CurrentMonth.Maintenances, 0);
            Assert.AreEqual(0, f.Company.CurrentMonth.Breakdowns);
            Assert.Less(f.Company.Ledger.MonthTotal(0, LedgerCategory.Maintenance), 0);
        }

        [Test]
        public void WithoutMechanic_WornMachineBreaksDown()
        {
            var f = TestFactory.Build(d => d.Machines.First(m => m.Id == "cutting_knife_table").DurabilityOperations = 5);
            f.Supply();
            f.Plan(30);
            f.RunDays(7);
            Assert.Greater(f.Company.CurrentMonth.Breakdowns, 0);
            Assert.Greater(f.Company.CurrentMonth.DowntimeMinutes[(int)DowntimeCause.Broken], 0);
        }

        [Test]
        public void FartherMachine_MeansLongerLogistics()
        {
            double Meters(bool far)
            {
                var f = TestFactory.Build();
                if (far)
                {
                    Assert.IsNull(f.Player.Submit(new MoveObjectCommand(f.Machine.Id, 9, 9, Facing.North)));
                    f.World.Simulation.Step();
                }
                f.Supply();
                f.Plan(30);
                f.RunDays(7);
                return f.Company.CurrentMonth.LogisticsMeters / Math.Max(1, f.Produced);
            }
            Assert.Greater(Meters(far: true), Meters(far: false));
        }

        [Test]
        public void Overtime_AddsHoursAndFatigue()
        {
            var f = TestFactory.Build();
            Assert.IsNull(f.Player.Submit(new SetOvertimeCommand(3)));
            f.Supply();
            f.Plan(200);
            f.RunDays(5);
            Assert.Greater(f.Company.CurrentMonth.OvertimeHours, 0);
            Assert.AreEqual("Переработки — от 0 до 4 часов.", f.Player.Submit(new SetOvertimeCommand(9)));
        }

        [Test]
        public void Wages_ArePaidAndUnderpaidWorkerQuits()
        {
            var f = TestFactory.Build(d => d.Staff.MoraleWageGapFactor = 2);
            var cutter = f.Company.Workers.Single(w => w.ProfessionId == "cutter");
            long expectedWages = f.Company.Workers.Sum(w => w.MonthlyWage) - cutter.MonthlyWage + 1000;
            Assert.IsNull(f.Player.Submit(new SetWageCommand(cutter.Id, 1000)));
            f.World.Simulation.RunMonths(1);

            var report = f.Company.Reports[0];
            Assert.AreEqual(expectedWages, -f.Company.Ledger.MonthTotal(0, LedgerCategory.Wages));
            Assert.AreEqual(1, report.Quits);
            Assert.IsNull(f.Company.FindWorker(cutter.Id));
        }

        [Test]
        public void Candidate_CannotBeHiredByTwoCompanies()
        {
            var world = TestData.World();
            var a = new PlayerController();
            var b = new PlayerController();
            world.AddCompany("a", a);
            world.AddCompany("b", b);
            world.Simulation.Step();
            int id = world.Staff.Candidates.First().Id;
            Assert.IsNull(a.Submit(new HireCommand(id)));
            Assert.AreEqual($"Кандидата №{id} уже нанимает другая компания.", b.Submit(new HireCommand(id)));
            world.Simulation.Step();
            Assert.AreEqual(1, world.Companies[0].Workers.Count);
        }

        [Test]
        public void OutputQc_FindsDefectsAndScrapsThem()
        {
            var f = TestFactory.Build(d => d.Products[0].BaseDefectChance = 0.9, inspector: true, qcTable: true);
            f.Supply();
            f.Plan(40);
            f.RunDays(9);
            Assert.Greater(f.Company.CurrentMonth.DetectedDefects, 0);
            Assert.AreEqual(f.Company.CurrentMonth.DetectedDefects, f.Company.CurrentMonth.ScrappedUnits);
        }

        [Test]
        public void InputQc_RejectsBadBatches()
        {
            var f = TestFactory.Build(d => { d.Suppliers[0].QualityMin = 10; d.Suppliers[0].QualityMax = 20; }, inspector: true, qcTable: true);
            Assert.IsNull(f.Player.Submit(new SetInputQcCommand(true)));
            f.Supply();
            f.RunDays(3);
            Assert.Greater(f.Company.CurrentMonth.RejectedBatches, 0);
            Assert.AreEqual(0, f.Company.Inventory.MaterialQuantity("cotton"));
            Assert.AreEqual(0, f.Company.Ledger.MonthTotal(0, LedgerCategory.MaterialPurchases));
        }

        [Test]
        public void Analytics_FindBottleneckTaktAndLastWeekSpaghetti()
        {
            var f = TestFactory.Build();
            f.Supply();
            f.Plan(60);
            f.RunDays(10); // понедельник 11 января — неделя закрыта
            Assert.AreSame(f.Machine, f.World.Production.Bottleneck(f.Company));
            Assert.Greater(f.World.Production.TaktMinutes(f.Company), 0);
            Assert.Greater(f.Company.Production.SpaghettiLastWeek.Sum(), 0);
        }

        [Test]
        public void Planner_BuildsValidReachableLayoutForRealData()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();
            var world = new GameWorld(catalog, 1);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            var machines = new[] { "cutting_knife_table", "sewing_straight", "molding_hand_press", "sewing_overlock" };
            Assert.IsNull(player.Submit(new BatchCommand(FloorPlanner.InitialLayout(catalog, new LayoutTemplate(), machines))));
            world.Simulation.Step();

            Assert.AreEqual(4, company.MachineCount);
            var (gx, gy) = company.Floor.Gate;
            int gate = company.Floor.Index(gx, gy);
            foreach (var o in company.Floor.Objects)
            {
                var (ax, ay) = o.AccessCell;
                Assert.GreaterOrEqual(company.Paths.Distance(gate, company.Floor.Index(ax, ay)), 0, $"{o.DefId} №{o.Id} недоступен с ворот");
            }
            Assert.IsTrue(company.HasQcStation);
            Assert.AreEqual(catalog.Warehouse.StartingCells + 7 * catalog.Warehouse.CellsPerRack, company.WarehouseCells);
        }

        [Test]
        public void StandardRun_IsFastEnough()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();
            var timer = Stopwatch.StartNew();
            var world = ScenarioRunner.Run(catalog, 1, 3);
            timer.Stop();
            TestContext.WriteLine($"3 месяца, 3 цеха: {timer.Elapsed.TotalSeconds:0.0} с");
            Assert.Less(timer.Elapsed.TotalSeconds, 120);
            Assert.That(world.Companies.All(c => c.Reports.Sum(r => r.UnitsProduced) > 0));
        }
    }
}
