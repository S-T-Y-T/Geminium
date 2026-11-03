using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Lab.Surrogate;
using Geminium.Game;
using Geminium.ML;

namespace Geminium.Tests.EditMode
{
    // Боты (GDD 8): поиск восхождением, политика, суррогатная модель, обученная нейросеть.
    public class BotTests
    {
        private static DataCatalog RealCatalog() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();

        // Всегда одно и то же действие.
        private sealed class FixedPolicy : IBotPolicy
        {
            private readonly int[] actions;
            public FixedPolicy(int[] actions) { this.actions = actions; }
            public int[] Decide(float[] observation, bool[] mask) => (int[])actions.Clone();
        }

        [Test]
        public void HillClimber_KeepsGainsAndRevertsLosses()
        {
            var climber = new HillClimber(new[] { 0.2 }, new[] { 0.0 }, new[] { 1.0 }, 0.05, new SimRandom(3));
            for (int i = 0; i < 300; i++) climber.Step(-(climber[0] - 0.7) * (climber[0] - 0.7));
            Assert.AreEqual(0.7, climber[0], 0.051);
            Assert.Greater(climber.Accepted, 0);
            Assert.Greater(climber.Reverted, 0);
        }

        [Test]
        public void Mask_RespectsArchetypeAndInactiveProducts()
        {
            var premium = new BotSituation { Archetype = 1 };
            premium.Products[0].Active = true;
            var mask = BotActions.Mask(premium);
            int materials = BotActions.BranchSizes.Take(BotActions.MaterialsBranch).Sum();
            Assert.IsFalse(mask[materials]);       // дешёвое сырьё «Премиуму» нельзя
            Assert.IsTrue(mask[materials + 1]);
            Assert.IsTrue(mask[0] && mask[4]);       // активный продукт: любое изменение цены
            Assert.IsFalse(mask[5] || mask[9]);      // неактивный продукт: только «без изменения»
            Assert.IsTrue(mask[7]);

            var actions = BotActions.Sanitize(new[] { 4, 0, 2, 2, 2, 1, 0, 0, 0, 0 }, mask);
            Assert.AreEqual(4, actions[0]);
            Assert.AreEqual(2, actions[1]);
            Assert.AreEqual(1, actions[BotActions.MaterialsBranch]);
            Assert.AreEqual(BotObservation.Size, BotObservation.Encode(premium).Length);
        }

        [Test]
        public void PolicyBot_AppliesPolicyActionsAndCanWaitForThem()
        {
            var catalog = TestData.MinimalCatalog();
            var raise = new[] { 4, 2, 2, 2, 2, 1, 0, 0, 0, 0 }; // цена +10%
            var world = new GameWorld(catalog, 1);
            var bot = new PolicyBotController(catalog.BotArchetype("balanced"), new FixedPolicy(raise));
            var company = world.AddCompany("bot", bot);
            world.Simulation.RunMonths(1);
            world.Simulation.Step();
            Assert.AreEqual((long)Math.Round(450000 * world.Economy.PriceLevel * 1.1), company.RetailPrice("dobok"));
            CollectionAssert.AreEqual(raise, bot.LastActions);

            var external = new GameWorld(catalog, 1);
            var waiting = new PolicyBotController(catalog.BotArchetype("balanced"));
            var waitingCompany = external.AddCompany("bot", waiting);
            external.Simulation.RunMonths(1);
            external.Simulation.Step();
            Assert.IsTrue(waiting.AwaitingActions);
            Assert.IsNotNull(waiting.PendingSituation);
            Assert.AreEqual(450000, waitingCompany.RetailPrice("dobok"));
            waiting.Act(raise);
            Assert.IsFalse(waiting.AwaitingActions);
            external.Simulation.Step();
            Assert.AreEqual((long)Math.Round(450000 * external.Economy.PriceLevel * 1.1), waitingCompany.RetailPrice("dobok"));
            Assert.Throws<InvalidOperationException>(() => waiting.Act(raise));
        }

        [Test]
        public void SurrogateCalibration_RecoversKnownParameters()
        {
            var catalog = TestData.MinimalCatalog();
            var truth = new SurrogateSettings { Utilization = 0.8, PlanFulfilment = 0.9 };
            var samples = new List<FactorySample>();
            for (int k = 0; k < 60; k++)
            {
                var s = new FactorySample { WorkMinutes = 10000, AverageExperience = 30 + k % 40, QualityMaterials = k % 2 == 0 };
                int machines = 1 + k % 3;
                s.ZonePower[(int)ProductionZone.Cutting] = machines;
                s.ZoneMachines[(int)ProductionZone.Cutting] = machines;
                s.ZoneOperators[(int)ProductionZone.Cutting] = machines;
                s.Plan["dobok"] = 100 + 37 * k;
                s.Output = SurrogateFactory.PredictOutput(s, catalog, truth);
                s.Defective = SurrogateFactory.PredictDefective(s, s.Output, catalog, 1.3);
                s.Energy = 8 * SurrogateFactory.OutputMinutes(s, s.Output, catalog);
                s.Maintenance = 500 * s.Output;
                samples.Add(s);
            }
            var fit = SurrogateCalibration.Fit(samples, catalog);
            Assert.AreEqual(0.8, fit.Utilization, 0.011);
            Assert.AreEqual(0.9, fit.PlanFulfilment, 0.011);
            Assert.AreEqual(1.3, fit.DefectMultiplier, 1e-6);
            Assert.AreEqual(8, fit.EnergyPerMinute, 1e-6);
            Assert.AreEqual(500, fit.MaintenancePerUnit, 1e-6);
            Assert.Greater(fit.OutputR2, 0.999);
            Assert.AreEqual(60, fit.CalibrationSamples);
        }

        [Test]
        public void SurrogateWorld_IsDeterministicAndLasts36Months()
        {
            var catalog = RealCatalog();
            double[] Play(ulong seed, out SurrogateWorld world)
            {
                world = new SurrogateWorld(catalog, seed);
                var policy = new HeuristicPolicy();
                var total = new double[world.Companies.Count];
                int months = 0;
                while (!world.Done)
                {
                    var actions = new int[world.Companies.Count][];
                    for (int i = 0; i < actions.Length; i++)
                    {
                        var s = world.Situation(i);
                        var obs = BotObservation.Encode(s);
                        Assert.That(obs.All(v => v >= -1.0001f && v <= 1.0001f), "наблюдения вне −1…+1");
                        actions[i] = policy.Decide(obs, BotActions.Mask(s));
                    }
                    var rewards = world.Step(actions);
                    for (int i = 0; i < total.Length; i++) total[i] += rewards[i];
                    months++;
                }
                Assert.AreEqual(SurrogateWorld.EpisodeMonths, months);
                return total;
            }

            var first = Play(5, out var w1);
            var second = Play(5, out var w2);
            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(w1.Companies.Select(c => c.Cash), w2.Companies.Select(c => c.Cash));
            Assert.That(first.All(r => !double.IsNaN(r) && !double.IsInfinity(r)));
            // «Премиум» с сертификатом и спонсорством к концу эпизода репутацией выше «Демпера» без ОТК.
            Assert.Greater(w1.Companies[1].Reputation, w1.Companies[2].Reputation);
        }

        [Test]
        public void StandardRun_HasTendersMarketingAndArchetypeDifferences()
        {
            var world = ScenarioRunner.Run(RealCatalog(), 1, 18);
            var daebo = world.FindCompany("daebo");
            var abidas = world.FindCompany("abidas");
            Assert.Greater(world.Companies.Sum(c => c.Reports.Sum(r => r.TendersWon)), 0);
            Assert.Greater(daebo.Reports.Sum(r => r.MarketingSpend), 0);
            Assert.IsNotEmpty(daebo.Sales.Certificates);
            Assert.IsNotEmpty(daebo.Sales.Ambassadors);
            Assert.Greater(daebo.Reputation, abidas.Reputation);
            Assert.That(world.Companies.All(c => !c.IsBankrupt));
        }

        [Test]
        public void TrainedModel_ReturnsAllowedActions()
        {
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Geminium/ML/Models/GeminiumBot.onnx");
            if (model == null) Assert.Ignore("Модель ещё не обучена.");
            using var policy = new OnnxBotPolicy(model);
            var world = new SurrogateWorld(RealCatalog(), 2);
            for (int i = 0; i < world.Companies.Count; i++)
            {
                var s = world.Situation(i);
                var mask = BotActions.Mask(s);
                var actions = policy.Decide(BotObservation.Encode(s), mask);
                Assert.AreEqual(BotActions.BranchSizes.Length, actions.Length);
                int offset = 0;
                for (int b = 0; b < actions.Length; b++)
                {
                    Assert.That(actions[b], Is.InRange(0, BotActions.BranchSizes[b] - 1));
                    Assert.IsTrue(mask[offset + actions[b]], $"ветка {b}: запрещённое действие {actions[b]}");
                    offset += BotActions.BranchSizes[b];
                }
            }
        }
    }
}
