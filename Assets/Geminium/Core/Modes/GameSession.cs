using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.News;

namespace Geminium.Core.Modes
{
    public enum GameMode
    {
        Free = 0,       // 3 года против двух ботов (GDD 4.2)
        Campaign = 1,   // одно задание обучающей кампании (GDD 4.1)
    }

    // Кто управляет ботами-конкурентами.
    public enum RivalBrain
    {
        Scripted = 0,   // поиск восхождением (без модели)
        Neural = 1,     // обученная нейросеть (модель подключает слой Unity)
    }

    // С чего начинается партия. Вместе с журналом команд игрока это всё, что нужно для сохранения.
    public sealed class GameSetup
    {
        public GameMode Mode;
        public ulong Seed;
        public string MissionId;
        public RivalBrain Rivals;

        public static GameSetup Free(ulong seed, RivalBrain rivals = RivalBrain.Scripted) =>
            new GameSetup { Mode = GameMode.Free, Seed = seed, Rivals = rivals };

        public static GameSetup Campaign(string missionId, ulong seed) =>
            new GameSetup { Mode = GameMode.Campaign, Seed = seed, MissionId = missionId };
    }

    public sealed class RankingEntry
    {
        public int Place;
        public string CompanyId;
        public long Capitalization;
        public bool Bankrupt;
    }

    // Партия: мир, компания игрока и режим. Конкуренты — те же Daebo («Премиум») и Abıdas («Демпер»), что в Лаборатории.
    // Партия отдаётся после первой минуты: кандидаты рынка труда, стартовые решения ботов и готовый цех задания
    // появляются в начале месяца, и игрок начинает уже с ними.
    public sealed class GameSession
    {
        public const string PlayerId = "player";

        public GameSetup Setup { get; }
        public GameWorld World { get; }
        public Company Player { get; }
        public MissionDef Mission { get; }          // null — свободный режим
        public MissionSystem MissionTracker { get; }
        public NewsSystem News { get; }             // лента новостей ИИ-ядра (факты для «голоса»)
        public int Months { get; }                  // срок партии; после него свободный режим можно продолжать

        public bool IsFinished => World.Simulation.MonthIndex >= Months;

        private GameSession(GameSetup setup, GameWorld world, Company player, MissionDef mission, MissionSystem tracker, NewsSystem news, int months)
        {
            News = news;
            Setup = setup;
            World = world;
            Player = player;
            Mission = mission;
            MissionTracker = tracker;
            Months = months;
        }

        // neuralPolicy — модель для ботов при RivalBrain.Neural. playerController — кто играет за игрока
        // (по умолчанию человек; бот — для проверки заданий и исследований).
        public static GameSession Start(DataCatalog catalog, GameSetup setup, IBotPolicy neuralPolicy = null,
            CompanyController playerController = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (setup.Rivals == RivalBrain.Neural && neuralPolicy == null)
                throw new ArgumentException("Для ботов-нейросетей нужна модель.", nameof(neuralPolicy));

            var mission = setup.Mode == GameMode.Campaign ? catalog.Mission(setup.MissionId) : null;
            var world = new GameWorld(catalog, setup.Seed);
            long? cash = mission != null && mission.StartingCash > 0 ? mission.StartingCash : (long?)null;
            if (mission != null) world.Rules = new GameRules(mission.Products, mission.Features, mission.Modules);

            var player = world.AddCompany(PlayerId, playerController ?? new PlayerController(), cash);
            if (mission == null || mission.Rivals)
            {
                var policy = setup.Rivals == RivalBrain.Neural ? neuralPolicy : null;
                for (int i = 1; i < ScenarioRunner.CompanyIds.Length; i++)
                    world.AddCompany(ScenarioRunner.CompanyIds[i], ScenarioRunner.CreateBot(catalog, ScenarioRunner.ArchetypeIds[i], policy), cash);
            }

            MissionSystem tracker = null;
            if (mission != null)
            {
                foreach (var tender in mission.Tenders) world.Tenders.AddScripted(tender);
                world.Simulation.AddSystem(tracker = new MissionSystem(world, player, mission));
            }
            var news = new NewsSystem(world);
            world.Simulation.AddSystem(news);
            world.Simulation.Step();
            return new GameSession(setup, world, player, mission, tracker, news, mission?.Months ?? catalog.FreeModeMonths);
        }

        // Рейтинг «игрок против ботов» (GDD 4.2): по капитализации последнего отчёта, банкроты — в конце.
        public List<RankingEntry> Ranking()
        {
            var list = World.Companies
                .Select(c => new RankingEntry { CompanyId = c.Id, Capitalization = c.LastReport?.Capitalization ?? c.Cash, Bankrupt = c.IsBankrupt })
                .OrderBy(e => e.Bankrupt).ThenByDescending(e => e.Capitalization).ThenBy(e => e.CompanyId, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < list.Count; i++) list[i].Place = i + 1;
            return list;
        }
    }
}
