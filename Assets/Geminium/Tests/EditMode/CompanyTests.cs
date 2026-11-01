using System;
using System.Collections.Generic;
using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Time;

namespace Geminium.Tests.EditMode
{
    public class CompanyTests
    {
        // Контроллер для тестов: считает вызовы и в начале месяца ставит план выпуска.
        private sealed class ScriptedController : CompanyController
        {
            public int MonthsStarted, DaysStarted;
            public readonly List<string> Log;
            private readonly string name;

            public ScriptedController(List<string> log = null, string name = "")
            {
                Log = log ?? new List<string>();
                this.name = name;
            }

            protected override void OnMonthStarted(int monthIndex)
            {
                MonthsStarted++;
                Log.Add(name + "M" + monthIndex);
                Submit(new SetMonthlyPlanCommand("dobok", 100 * (monthIndex + 1)));
            }

            protected override void OnDayStarted(long dayIndex)
            {
                DaysStarted++;
                if (dayIndex == 0) Log.Add(name + "D0");
            }
        }

        private static GameWorld NewWorld() => new GameWorld(TestData.MinimalCatalog(), 1);

        [Test]
        public void Ledger_TracksBalanceAndTotalsByCategoryAndPeriod()
        {
            var ledger = new Ledger();
            ledger.Record(new SimTime(0), LedgerCategory.StartingCapital, 1000);
            ledger.Record(new SimTime(10), LedgerCategory.Wages, -300);
            ledger.Record(new SimTime(20), LedgerCategory.Wages, -200);

            Assert.AreEqual(500, ledger.Balance);
            Assert.AreEqual(-500, ledger.Total(LedgerCategory.Wages, new SimTime(0), new SimTime(100)));
            Assert.AreEqual(-300, ledger.Total(LedgerCategory.Wages, new SimTime(0), new SimTime(20)));
            Assert.Throws<ArgumentException>(() => ledger.Record(new SimTime(5), LedgerCategory.Other, 1));
        }

        [Test]
        public void Inventory_AveragesQualityAndRefusesToGiveMoreThanItHas()
        {
            var inventory = new Inventory();
            inventory.AddMaterial("cotton", 10, 80);
            inventory.AddMaterial("cotton", 30, 40);
            Assert.AreEqual(40, inventory.MaterialQuantity("cotton"), 1e-9);
            Assert.AreEqual(50, inventory.MaterialQuality("cotton"), 1e-9);

            Assert.IsTrue(inventory.TryTakeMaterial("cotton", 15.5, out double quality));
            Assert.AreEqual(50, quality, 1e-9);
            Assert.AreEqual(24.5, inventory.MaterialQuantity("cotton"), 1e-9);
            Assert.IsFalse(inventory.TryTakeMaterial("cotton", 100, out _));
            Assert.AreEqual(24.5, inventory.MaterialQuantity("cotton"), 1e-9);

            inventory.AddProducts("dobok", 5, 70);
            Assert.IsTrue(inventory.TryTakeProducts("dobok", 5, out _));
            Assert.AreEqual(0, inventory.ProductUnits("dobok"));
            CollectionAssert.IsEmpty(inventory.Products);
        }

        [Test]
        public void NewCompany_StartsWithStartingCashAndBasePrices()
        {
            var world = NewWorld();
            var company = world.AddCompany("player", new PlayerController());

            Assert.AreEqual(80000000, company.Cash);
            Assert.AreEqual(450000, company.RetailPrice("dobok"));
            Assert.AreEqual(0, company.MonthlyPlan("dobok"));
            Assert.AreEqual(5, company.Schedule.WorkingDaysPerWeek);
            Assert.AreSame(company, world.FindCompany("player"));
        }

        [Test]
        public void AddCompany_RejectsBadOrDuplicateIdAndReusedController()
        {
            var world = NewWorld();
            var controller = new PlayerController();
            world.AddCompany("player", controller);

            Assert.Throws<ArgumentException>(() => world.AddCompany("player", new PlayerController()));
            Assert.Throws<ArgumentException>(() => world.AddCompany("Daebo!", new PlayerController()));
            Assert.Throws<ArgumentException>(() => world.AddCompany("daebo", controller));
        }

        [Test]
        public void Command_IsAppliedWhenNextMinuteIsComputed_InSubmissionOrder()
        {
            var world = NewWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);

            Assert.IsNull(player.Submit(new SetRetailPriceCommand("dobok", 500000)));
            Assert.IsNull(player.Submit(new SetRetailPriceCommand("dobok", 520000)));
            Assert.AreEqual(450000, company.RetailPrice("dobok"));

            world.Simulation.Step();

            Assert.AreEqual(520000, company.RetailPrice("dobok"));
        }

        [Test]
        public void InvalidCommand_IsRejectedWithMessageAndNotApplied()
        {
            var world = NewWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);

            Assert.AreEqual("Цена должна быть больше нуля.", player.Submit(new SetRetailPriceCommand("dobok", 0)));
            Assert.AreEqual("Нет продукта «helmet».", player.Submit(new SetMonthlyPlanCommand("helmet", 10)));
            Assert.IsNotNull(player.Submit(new SetWorkScheduleCommand(new WorkScheduleSettings { ShiftsPerDay = 4 })));
            world.Simulation.Step();

            Assert.AreEqual(450000, company.RetailPrice("dobok"));
            Assert.AreEqual(1, company.Schedule.ShiftsPerDay);
        }

        [Test]
        public void WorkScheduleCommand_ChangesSchedule()
        {
            var world = NewWorld();
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            var settings = new WorkScheduleSettings { WorkingDaysPerWeek = 6, ShiftsPerDay = 2 };

            Assert.IsNull(player.Submit(new SetWorkScheduleCommand(settings)));
            settings.ShiftsPerDay = 3; // команда хранит копию — это изменение не должно попасть в компанию
            world.Simulation.Step();

            Assert.AreEqual(6, company.Schedule.WorkingDaysPerWeek);
            Assert.AreEqual(2, company.Schedule.ShiftsPerDay);
        }

        [Test]
        public void Controller_GetsMonthAndDayCallbacks_AndItsCommandsApply()
        {
            var world = NewWorld();
            var bot = new ScriptedController();
            var company = world.AddCompany("daebo", bot);

            world.Simulation.RunMonths(1);
            Assert.AreEqual(1, bot.MonthsStarted);
            Assert.AreEqual(31, bot.DaysStarted);
            Assert.AreEqual(100, company.MonthlyPlan("dobok"));

            world.Simulation.Step(); // начало февраля: команда бота применяется до расчёта этой же минуты
            Assert.AreEqual(200, company.MonthlyPlan("dobok"));
        }

        [Test]
        public void Controllers_AreCalledInCompanyOrder()
        {
            var world = NewWorld();
            var log = new List<string>();
            world.AddCompany("player", new ScriptedController(log, "P"));
            world.AddCompany("daebo", new ScriptedController(log, "D"));

            world.Simulation.Step();

            CollectionAssert.AreEqual(new[] { "PM0", "DM0", "PD0", "DD0" }, log);
        }
    }
}
