using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Factory;
using Geminium.Core.Lab;
using Geminium.Core.Modes;
using Geminium.Core.Save;
using Geminium.Game;

namespace Geminium.Tests.EditMode
{
    // Кампания и свободный режим (GDD 4.1–4.2) — этап 4.
    public class CampaignTests
    {
        private static DataCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();

        [Test]
        public void Data_HasSixMissionsInOrder()
        {
            var catalog = Catalog();
            CollectionAssert.AreEqual(new[] { "m1_first_shop", "m2_logistics", "m3_quality", "m4_equipment", "m5_market", "m6_contracts" },
                catalog.Missions.Select(m => m.Id).ToArray());
            Assert.AreEqual(36, catalog.FreeModeMonths);
            Assert.IsTrue(catalog.Missions.Where(m => m.Id != "m5_market" && m.Id != "m6_contracts").All(m => !m.Rivals), "боты — с задания 5");
        }

        [Test]
        public void GoalScore_IsLinearBetweenZeroAndTarget()
        {
            var more = new GoalDef { Target = 200, Zero = 0 };
            var less = new GoalDef { Target = 2, Zero = 6 };
            Assert.AreEqual(50, MissionSystem.GoalScore(more, 100), 1e-9);
            Assert.AreEqual(100, MissionSystem.GoalScore(more, 500), 1e-9);
            Assert.AreEqual(50, MissionSystem.GoalScore(less, 4), 1e-9);
            Assert.AreEqual(0, MissionSystem.GoalScore(less, 9), 1e-9);
            Assert.AreEqual(100, MissionSystem.GoalScore(less, 1), 1e-9);
        }

        // Закрытое заданием нельзя сделать ни игроку, ни боту; в свободном режиме открыто всё.
        [Test]
        public void Rules_LockFeaturesProductsAndModules()
        {
            var catalog = Catalog();
            var m1 = GameSession.Start(catalog, GameSetup.Campaign("m1_first_shop", 1));
            var world = m1.World;
            Assert.AreEqual(GameRules.LockedMessage, world.Submit(m1.Player, new TakeLoanCommand(1_000_000, 6)));
            Assert.AreEqual(GameRules.LockedMessage, world.Submit(m1.Player, new SetAdBudgetCommand(1_000_000)));
            Assert.AreEqual(GameRules.LockedMessage, world.Submit(m1.Player, new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            Assert.AreEqual(GameRules.LockedMessage, world.Submit(m1.Player, new SetTransportBatchCommand(5)));
            Assert.AreEqual("Продукт «helmet» ещё не открыт.", world.Submit(m1.Player, new SetMonthlyPlanCommand("helmet", 10)));
            Assert.IsNull(world.Submit(m1.Player, new SetMonthlyPlanCommand("dobok", 10)));
            var press = catalog.MachinesForZone(ProductionZone.Molding).First();
            Assert.AreEqual(GameRules.LockedMessage, world.Submit(m1.Player, new PlaceObjectCommand(FloorObjectKind.Machine, press.Id, 1, 1, Facing.North)));
            Assert.AreEqual(1, world.Companies.Count, "в первом задании ботов нет");
            Assert.IsTrue(world.Tenders.Tenders.All(t => t.ProductId == "dobok"), "тендеры — только по открытым продуктам");

            var m4 = GameSession.Start(catalog, GameSetup.Campaign("m4_equipment", 1));
            Assert.IsNull(m4.World.Submit(m4.Player, new BuyDigitalModuleCommand(DigitalModule.IotSensors)));
            Assert.AreEqual(GameRules.LockedMessage, m4.World.Submit(m4.Player, new BuyDigitalModuleCommand(DigitalModule.AiPricing)), "модуль не из списка задания");

            var free = GameSession.Start(catalog, GameSetup.Free(1));
            Assert.IsNull(free.World.Submit(free.Player, new TakeLoanCommand(1_000_000, 6)));
            Assert.IsNull(free.World.Submit(free.Player, new SetMonthlyPlanCommand("helmet", 10)));
        }

        // Готовый цех собирает помощник без записи в журнал; загрузка собирает его заново и повторяет ходы игрока.
        [Test]
        public void PrebuiltShop_IsNotLoggedAndSurvivesSaveLoad()
        {
            var catalog = Catalog();
            var session = GameSession.Start(catalog, GameSetup.Campaign("m2_logistics", 5));
            var world = session.World;
            Assert.Greater(session.Player.Machines.Count(), 1);
            Assert.AreEqual(0, world.CommandLog.Count);
            Assert.Greater(session.MissionTracker.StartLayoutCost, 0);

            // Ход игрока: перенос станка, который предлагает двойник.
            var move = DigitalTwin.Suggest(world, session.Player, 1).First();
            Assert.IsNull(world.Submit(session.Player, new MoveObjectCommand(move.ObjectId, move.X, move.Y, move.Facing)));
            world.Simulation.RunMinutes(2 * 60);
            double progress = session.MissionTracker.Progress().Goals.First(g => g.Goal.Metric == GoalMetric.LogisticsReductionPct).Actual;
            Assert.Greater(progress, 0);

            var restored = SaveGame.FromJson(SaveGame.Capture(session).ToJson()).Restore(catalog);
            Assert.AreEqual(1, restored.World.CommandLog.Count);
            Assert.AreEqual(session.MissionTracker.StartLayoutCost, restored.MissionTracker.StartLayoutCost, 1e-9);
            Assert.AreEqual(progress, restored.MissionTracker.Progress().Goals.First(g => g.Goal.Metric == GoalMetric.LogisticsReductionPct).Actual, 1e-9);
            CollectionAssert.AreEqual(session.Player.Machines.Select(m => (m.Id, m.X, m.Y)).ToList(), restored.Player.Machines.Select(m => (m.Id, m.X, m.Y)).ToList());
        }

        // Первое задание: без действий — провал, бот за игрока строит цех и проходит.
        [Test]
        public void FirstMission_IdleFailsAndBotPasses()
        {
            var catalog = Catalog();
            var idle = GameSession.Start(catalog, GameSetup.Campaign("m1_first_shop", 2));
            idle.World.Simulation.RunMonths(idle.Months);
            Assert.IsTrue(idle.IsFinished);
            Assert.IsNotNull(idle.MissionTracker.Result);
            Assert.IsFalse(idle.MissionTracker.Result.Passed);

            var bot = GameSession.Start(catalog, GameSetup.Campaign("m1_first_shop", 2), null, ScenarioRunner.CreateBot(catalog, "balanced"));
            MissionResult completed = null;
            bot.MissionTracker.Completed += r => completed = r;
            bot.World.Simulation.RunMonths(bot.Months);
            Assert.IsNotNull(completed);
            Assert.IsTrue(completed.Passed, $"балл {completed.Score:F1}");
            Assert.AreEqual(2, completed.Goals.Count);
        }

        // Задание 4: изношенные станки без наладчика ломаются; наладчик и ТО в первый месяц снимают простои.
        [Test]
        public void EquipmentMission_StartsWithWornMachines()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Campaign("m4_equipment", 1));
            Assert.IsTrue(session.Player.Machines.All(m => m.Machine.Condition <= session.Mission.StartCondition));
            Assert.AreEqual(0, session.Player.CountWorkers(session.World.Catalog.Staff.MechanicProfessionId), "у «Демпера» нет наладчика");
        }

        // Задание 6 публикует тендер федерации в первом месяце.
        [Test]
        public void ContractsMission_PublishesFederationTender()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Campaign("m6_contracts", 1));
            var tender = session.World.Tenders.OpenTenders.Single(t => t.Level == TenderLevel.Regional && t.ProductId == "dobok" && t.Units == 200);
            Assert.AreEqual(2, tender.DeliveryMonths);
            Assert.IsNull(session.World.Submit(session.Player, new BidTenderCommand(tender.Id, tender.MaxUnitPrice)));
            Assert.AreEqual(3, session.World.Companies.Count);
        }

        // Свободный режим: 3 года, рейтинг по капитализации.
        [Test]
        public void FreeMode_RanksByCapitalization()
        {
            var session = GameSession.Start(Catalog(), GameSetup.Free(4));
            Assert.AreEqual(36, session.Months);
            Assert.IsNull(session.Mission);
            session.World.Simulation.RunMonths(2);
            var ranking = session.Ranking();
            CollectionAssert.AreEquivalent(ScenarioRunner.CompanyIds, ranking.Select(r => r.CompanyId).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ranking.Select(r => r.Place).ToArray());
            for (int i = 1; i < ranking.Count; i++)
                Assert.IsTrue(ranking[i - 1].Bankrupt == ranking[i].Bankrupt ? ranking[i - 1].Capitalization >= ranking[i].Capitalization : !ranking[i - 1].Bankrupt);
            Assert.IsFalse(session.IsFinished);
        }
    }
}
