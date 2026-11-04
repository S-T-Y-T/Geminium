using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;

namespace Geminium.Core.Lab
{
    // Прогон без графики (GDD 10): стандартная партия — игрок-бот и два конкурента, N месяцев с seed.
    // Конкуренты — архетипы из данных: Daebo — «Премиум», Abıdas — «Демпер», игрок — сбалансированный.
    // policy != null — ботами управляет политика (обученная нейросеть), иначе поиск восхождением.
    // Сценарии с модулями цифровизации появятся на этапе 4; сейчас это базовый сценарий A.
    public static class ScenarioRunner
    {
        public static readonly string[] CompanyIds = { "player", "daebo", "abidas" };
        public static readonly string[] ArchetypeIds = { "balanced", "premium", "dumper" };

        public static GameWorld CreateStandardWorld(DataCatalog catalog, ulong seed, IBotPolicy policy = null) =>
            CreateWorld(catalog, seed, new[] { policy, policy, policy });

        // Политика для каждой компании отдельно (null — поиск восхождением), например нейросеть только у игрока.
        public static GameWorld CreateWorld(DataCatalog catalog, ulong seed, IBotPolicy[] policies)
        {
            if (policies == null || policies.Length != CompanyIds.Length) throw new ArgumentException("Нужна политика на каждую компанию.", nameof(policies));
            var world = new GameWorld(catalog, seed);
            for (int i = 0; i < CompanyIds.Length; i++) world.AddCompany(CompanyIds[i], CreateBot(catalog, ArchetypeIds[i], policies[i]));
            return world;
        }

        // Сценарий Лаборатории (GDD 10): стандартная партия, фабрика игрока покупает модули сценария,
        // конкуренты — без цифровизации. Со «Субсидией» госпрограмма компенсирует внедрение весь прогон.
        // Ручки поиска восхождением у всех ботов заморожены: модули сравниваются при одной политике управления.
        public static GameWorld CreateScenarioWorld(DataCatalog catalog, ulong seed, ScenarioDef scenario, IBotPolicy policy = null)
        {
            var world = CreateStandardWorld(catalog, seed, policy);
            var player = world.Companies[0];
            foreach (var company in world.Companies)
                if (company.Controller is BaselineBotController baseline) baseline.FreezeKnobs = true;
            var bot = (BotController)player.Controller;
            bot.NaiveLayout = scenario.NaiveLayout;
            foreach (var module in OrderedByRequirements(catalog, scenario.Modules)) bot.PlannedModules.Add(module);
            if (scenario.Subsidy)
                player.Digital.ForcedSubsidy = catalog.Events.Where(e => e.Effect == EventEffect.StateSubsidy).Select(e => e.Magnitude).DefaultIfEmpty(0).Max();
            return world;
        }

        private static IEnumerable<DigitalModule> OrderedByRequirements(DataCatalog catalog, IEnumerable<DigitalModule> modules)
        {
            var list = modules.ToList();
            var requires = catalog.DigitalModules.ToDictionary(d => d.Module, d => d.Requires);
            return list.OrderBy(m => list.Contains(requires[m]) ? 1 : 0).ThenBy(m => (int)m);
        }

        public static CompanyController CreateBot(DataCatalog catalog, string archetypeId, IBotPolicy policy = null)
        {
            var archetype = catalog.BotArchetype(archetypeId);
            return policy == null ? new BaselineBotController(archetype) : (CompanyController)new PolicyBotController(archetype, policy);
        }

        public static GameWorld Run(DataCatalog catalog, ulong seed, int months, IBotPolicy policy = null)
        {
            if (months < 0) throw new ArgumentOutOfRangeException(nameof(months));
            var world = CreateStandardWorld(catalog, seed, policy);
            world.Simulation.RunMonths(months);
            return world;
        }
    }
}
