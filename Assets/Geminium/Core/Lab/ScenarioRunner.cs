using Geminium.Core.Data;

namespace Geminium.Core.Lab
{
    // Прогон без графики (GDD 10): стандартная партия — игрок-бот и два конкурента, N месяцев с seed.
    // Сценарии с модулями цифровизации появятся на этапе 4; сейчас это базовый сценарий A.
    public static class ScenarioRunner
    {
        public static GameWorld CreateStandardWorld(DataCatalog catalog, ulong seed)
        {
            var world = new GameWorld(catalog, seed);
            world.AddCompany("player", new BaselineBotController(BotProfile.Balanced));
            world.AddCompany("daebo", new BaselineBotController(BotProfile.Premium));
            world.AddCompany("abidas", new BaselineBotController(BotProfile.Dumper));
            return world;
        }

        public static GameWorld Run(DataCatalog catalog, ulong seed, int months)
        {
            var world = CreateStandardWorld(catalog, seed);
            world.Simulation.RunMonths(months);
            return world;
        }
    }
}
