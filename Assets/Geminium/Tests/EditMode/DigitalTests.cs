using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Factory;
using Geminium.Core.Lab;

namespace Geminium.Tests.EditMode
{
    // Цифровизация (GDD 7.9) и Лаборатория сценариев (GDD 10) — этап 4.
    public class DigitalTests
    {
        [Test]
        public void Modules_PurchaseLicenseDependencyAndDepreciation()
        {
            var world = TestData.World(TestData.Calm);
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            world.Simulation.Step();

            Assert.AreEqual("Сначала нужен модуль IotSensors.", player.Submit(new BuyDigitalModuleCommand(DigitalModule.PredictiveMaintenance)));
            Assert.IsNull(player.Submit(new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            world.Simulation.Step();
            Assert.AreEqual(-30_000_000, company.Ledger.MonthTotal(0, LedgerCategory.DigitalModules));
            Assert.AreEqual("Модуль уже работает.", player.Submit(new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            Assert.IsNull(player.Submit(new BuyDigitalModuleCommand(DigitalModule.PredictiveMaintenance)));
            world.Simulation.RunMonths(2);

            var report = company.Reports[1];
            Assert.AreEqual(50_000_000, company.Reports[0].DigitalCapex);
            Assert.AreEqual(2, report.DigitalModules);
            Assert.AreEqual((long)Math.Round(1_500_000 * world.Economy.History[1].PriceLevel) + (long)Math.Round(2_000_000 * world.Economy.History[1].PriceLevel),
                report.DigitalLicense);
            long monthly = (long)Math.Round(30_000_000 / 36.0) + (long)Math.Round(20_000_000 / 36.0);
            Assert.AreEqual(monthly, report.DigitalDepreciation);
            Assert.AreEqual(Formulas.ResidualValue(30_000_000, 2, 36) + Formulas.ResidualValue(20_000_000, 2, 36), report.DigitalValue);

            // Без датчиков предиктивное ТО не работает — отключается вместе с ними.
            Assert.IsNull(player.Submit(new DisableDigitalModuleCommand(DigitalModule.IotSensors)));
            world.Simulation.Step();
            Assert.IsFalse(company.Digital.Has(DigitalModule.IotSensors));
            Assert.IsFalse(company.Digital.Has(DigitalModule.PredictiveMaintenance));
            Assert.AreEqual("Этот модуль не работает.", player.Submit(new DisableDigitalModuleCommand(DigitalModule.IotSensors)));
        }

        [Test]
        public void StateProgram_CompensatesPartOfCapex()
        {
            var world = TestData.World(d =>
            {
                TestData.Calm(d);
                d.Events.Add(new EventDef { Id = "digital_program", MonthlyProbability = 1, MinDurationMonths = 3, MaxDurationMonths = 3,
                    Effect = EventEffect.StateSubsidy, Magnitude = 0.5 });
            });
            var player = new PlayerController();
            var company = world.AddCompany("player", player);
            world.Simulation.RunMonths(1);
            world.Simulation.Step(); // месяц 1: госпрограмма началась
            Assert.AreEqual(0.5, world.Digital.SubsidyShare(company), 1e-9);
            Assert.IsNull(player.Submit(new BuyDigitalModuleCommand(DigitalModule.AiCutting)));
            world.Simulation.RunMonths(1);

            long gross = (long)Math.Round(25_000_000 * world.Economy.History[1].PriceLevel);
            long net = (long)Math.Round(25_000_000 * world.Economy.History[1].PriceLevel * 0.5);
            Assert.AreEqual(net, company.Reports[1].DigitalCapex);
            Assert.AreEqual(gross - net, company.Reports[1].DigitalSubsidy);
        }

        [Test]
        public void PredictiveMaintenance_ServicesByCondition()
        {
            var f = TestFactory.Build(mechanic: true);
            var m = f.Machine.Machine;
            m.Condition = 60;
            m.OperationsSinceMaintenance = 10_000;
            Assert.IsTrue(f.World.Production.MaintenanceDue(f.Company, m)); // по регламенту — давно пора

            Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            f.World.Simulation.Step();
            Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.PredictiveMaintenance)));
            f.World.Simulation.Step();
            Assert.IsFalse(f.World.Production.MaintenanceDue(f.Company, m)); // по состоянию — ещё рано
            m.Condition = 50;
            Assert.IsTrue(f.World.Production.MaintenanceDue(f.Company, m));
        }

        [Test]
        public void IotSensors_ShowExactCondition()
        {
            var f = TestFactory.Build();
            f.Machine.Machine.Condition = 73.4;
            Assert.AreEqual(60, f.World.Production.ConditionEstimate(f.Company, f.Machine)); // без датчиков — с шагом 30
            Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            f.World.Simulation.Step();
            Assert.AreEqual(73.4, f.World.Production.ConditionEstimate(f.Company, f.Machine), 1e-9);
        }

        [Test]
        public void AiCutting_ReducesFabricWaste()
        {
            var f = TestFactory.Build();
            var op = f.World.Catalog.Product("dobok").Operations[0];
            var cotton = op.Inputs[0];
            Assert.AreEqual(2.5 * 1.12, f.World.Production.InputAmount(f.Company, op, cotton), 1e-9);
            f.Supply();
            f.Plan(30);
            f.RunDays(5);
            Assert.Greater(f.Company.CurrentMonth.FabricWaste, 0);

            Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.AiCutting)));
            f.World.Simulation.Step();
            Assert.AreEqual(2.5 * 1.04, f.World.Production.InputAmount(f.Company, op, cotton), 1e-9);
        }

        [Test]
        public void AiQuality_FindsDefectsWithoutInspector()
        {
            long Detected(bool vision)
            {
                var f = TestFactory.Build(d => d.Products[0].BaseDefectChance = 0.9);
                if (vision)
                {
                    Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.AiQuality)));
                    f.World.Simulation.Step();
                }
                f.Supply();
                f.Plan(40);
                f.RunDays(9);
                return f.Company.CurrentMonth.DetectedDefects;
            }
            Assert.AreEqual(0, Detected(false)); // нет ни стола ОТК, ни контролёра
            Assert.Greater(Detected(true), 0);
        }

        [Test]
        public void AiPricing_SetsPriceWithinBoundsAtMonthStart()
        {
            var world = TestData.World(d => { TestData.Calm(d); d.Warehouse.StartingCells = 1000; });
            var a = new PlayerController();
            var companyA = world.AddCompany("a", a);
            var companyB = world.AddCompany("b", new PlayerController());
            companyA.Inventory.AddProducts("dobok", 300, 64, 100000);
            companyB.Inventory.AddProducts("dobok", 5000, 64, 100000);
            Assert.IsNull(a.Submit(new BuyDigitalModuleCommand(DigitalModule.AiPricing)));
            world.Simulation.RunMonths(1);
            world.Simulation.Step();

            double reference = 450000 * world.Economy.PriceLevel;
            double factor = companyA.RetailPrice("dobok") / reference;
            Assert.That(factor, Is.InRange(0.7 - 1e-6, 1.6 + 1e-6));
            Assert.AreEqual(world.Market.OptimalPrice(companyA, "dobok"), companyA.RetailPrice("dobok"));
            Assert.AreEqual(450000, companyB.RetailPrice("dobok")); // у конкурента модуля нет
            Assert.IsNull(a.Submit(new SetAutoPricingCommand(false)));
        }

        [Test]
        public void DemandForecast_IsSeasonalNaive()
        {
            var world = TestData.World(d => { TestData.Calm(d); d.Warehouse.StartingCells = 1000; });
            var company = world.AddCompany("a", new PlayerController());
            company.Inventory.AddProducts("dobok", 300, 64, 100000);
            world.Simulation.RunMonths(1);
            world.Simulation.Step();

            var last = company.LastReport;
            long demand = last.SoldByProduct["dobok"] + last.LostSalesByProduct["dobok"];
            var history = world.Economy.History;
            double expected = demand * history[1].Seasonality / history[0].Seasonality * history[1].Index / history[0].Index;
            Assert.AreEqual(expected, world.Market.Forecast(company, "dobok"), 1e-6);
        }

        [Test]
        public void DigitalTwin_SuggestsShorterPathAndCheaperMove()
        {
            var f = TestFactory.Build();
            Assert.IsNull(f.Player.Submit(new MoveObjectCommand(f.Machine.Id, 10, 9, Facing.North)));
            f.Plan(30);
            f.World.Simulation.Step();
            Assert.AreEqual(-150_000, f.Company.Ledger.MonthTotal(0, LedgerCategory.Other)); // перенос без двойника — 5% цены

            // Станок у дальнего края зоны: склад сырья и готовой продукции — у ворот.
            var move = DigitalTwin.Suggest(f.World, f.Company, 3).Single();
            Assert.AreEqual(f.Machine.Id, move.ObjectId);
            Assert.AreEqual(5, move.X);
            Assert.AreEqual(10, move.SavingCellsPerUnit, 1e-9); // 9 + 9 клеток против 4 + 4

            Assert.IsNull(f.Player.Submit(new BuyDigitalModuleCommand(DigitalModule.DigitalTwin)));
            f.World.Simulation.Step();
            Assert.AreEqual(30_000, MoveObjectCommand.RelocationCost(f.Company, f.Machine, f.World)); // с двойником — 1%
        }

        [Test]
        public void ScenarioLab_ComputesRoiPaybackAndNpv()
        {
            var scenarios = new List<ScenarioDef>
            {
                new ScenarioDef { Id = "a" },
                new ScenarioDef { Id = "b", Modules = { DigitalModule.AiCutting } },
            };
            ScenarioRun Run(string id, double[] income, double[] depreciation, double capex, int purchase) => new ScenarioRun
            {
                Scenario = id, Seed = 1, Months = 4, PurchaseMonth = purchase, NetIncome = income, DigitalDepreciation = depreciation,
                KeyRate = new[] { 0.12, 0.12, 0.12, 0.12 }, DigitalCapex = capex,
            };
            var runs = new List<ScenarioRun>
            {
                Run("a", new double[] { 10, 10, 10, 10 }, new double[4], 0, 4),
                Run("b", new double[] { 10, 12, 14, 14 }, new double[] { 0, 1, 1, 1 }, 6, 1),
            };
            var summary = ScenarioLab.Summarize(runs, scenarios).Single(s => s.Scenario == "b");

            // ΔNI по месяцам (с возвратом амортизации модулей): 0, 3, 5, 5 → 13 за три месяца после покупки.
            Assert.AreEqual(13 / 3.0, summary.DeltaPerMonth, 1e-9);
            Assert.AreEqual((13 - 6) / 6.0, summary.Roi, 1e-9);
            Assert.AreEqual(6 / (13 / 3.0), summary.PaybackMonths, 1e-9);
            Assert.AreEqual(-6 + 3 / Math.Pow(1.01, 2) + 5 / Math.Pow(1.01, 3) + 5 / Math.Pow(1.01, 4), summary.Npv, 1e-9);
            Assert.AreEqual("a", summary.Baseline);
            StringAssert.Contains("b;AiCutting;a;1;", ScenarioLab.SummaryCsv(new[] { summary }));
        }

        [Test]
        public void ScenarioWorld_BuysModulesAfterFactoryStarts()
        {
            var catalog = TestData.MinimalCatalog();
            var scenario = new ScenarioDef { Id = "forecast", Modules = { DigitalModule.DemandForecast } };
            var run = ScenarioLab.RunOne(catalog, scenario, 1, 8);
            Assert.AreEqual(8, run.Months);
            Assert.That(run.PurchaseMonth, Is.InRange(3, 7));
            Assert.Greater(run.DigitalCapex, 0);

            var world = ScenarioRunner.CreateScenarioWorld(catalog, 1, scenario);
            Assert.That(world.Companies.All(c => ((BaselineBotController)c.Controller).FreezeKnobs));
            CollectionAssert.AreEqual(new[] { DigitalModule.DemandForecast }, ((BotController)world.Companies[0].Controller).PlannedModules);
        }
    }
}
