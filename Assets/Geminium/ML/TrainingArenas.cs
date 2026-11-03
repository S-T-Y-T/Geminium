using Unity.MLAgents;
using UnityEngine;
using Geminium.Core;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Lab.Surrogate;

namespace Geminium.ML
{
    // Площадка на суррогатной модели (GDD 8.2): три компании-архетипа с общей политикой — self-play,
    // где каждый играет против копий себя. Шаг Academy = игровой месяц всех трёх.
    // Награда — прирост капитализации / (10 стартовых капиталов), банкротство — −1. Эпизод — 36 месяцев.
    public sealed class SurrogateArena : MonoBehaviour, ITrainingArena
    {
        public const double RewardScale = 10;
        private DataCatalog catalog;
        private ulong seed;
        private int episode;
        private SurrogateWorld world;
        private GeminiumBotAgent[] agents;
        private int[][] pending;
        private bool[] received;
        private bool[] ended;
        private bool requested;

        public void Init(DataCatalog catalog, ulong seed)
        {
            this.catalog = catalog;
            this.seed = seed;
            world = new SurrogateWorld(catalog, seed);
            int n = world.Companies.Count;
            agents = new GeminiumBotAgent[n];
            pending = new int[n][];
            received = new bool[n];
            ended = new bool[n];
            for (int i = 0; i < n; i++) agents[i] = GeminiumBotAgent.Create(transform, this, i);
            Academy.Instance.AgentPreStep += PreStep;
        }

        private void OnDestroy()
        {
            if (Academy.IsInitialized) Academy.Instance.AgentPreStep -= PreStep;
        }

        public BotSituation Situation(int index) => world.Situation(index);

        public void OnActions(int index, int[] actions)
        {
            pending[index] = actions;
            received[index] = true;
        }

        private void PreStep(int academyStep)
        {
            if (requested)
            {
                for (int i = 0; i < agents.Length; i++)
                    if (!ended[i] && !received[i]) return; // ждём решений всех живых компаний

                var rewards = world.Step(pending);
                for (int i = 0; i < agents.Length; i++)
                {
                    if (ended[i]) continue;
                    bool bankrupt = world.Companies[i].Bankrupt;
                    agents[i].AddReward(bankrupt ? -1f : (float)(rewards[i] / RewardScale));
                    if (bankrupt)
                    {
                        agents[i].EndEpisode();
                        ended[i] = true;
                    }
                }
                if (world.Done)
                {
                    for (int i = 0; i < agents.Length; i++)
                        if (!ended[i]) agents[i].EndEpisode();
                    episode++;
                    world = new SurrogateWorld(catalog, seed + (ulong)episode * 1000003UL);
                    for (int i = 0; i < agents.Length; i++) ended[i] = false;
                }
                requested = false;
            }

            for (int i = 0; i < agents.Length; i++)
            {
                if (ended[i]) continue;
                received[i] = false;
                agents[i].RequestDecision();
            }
            requested = true;
        }
    }

    // Площадка на полном цехе — для короткого дообучения (GDD 8.2). Боты — PolicyBotController без политики:
    // в начале месяца каждый готовит наблюдения и ждёт решения агента; симуляция стоит, пока решения не придут.
    // Эпизод — 12 месяцев (месяц 0 — стройка цеха без решений).
    public sealed class FullSimArena : MonoBehaviour, ITrainingArena
    {
        public int EpisodeMonths = 12;
        public int StepsPerFrame = 50000;
        private DataCatalog catalog;
        private ulong seed;
        private int episode;
        private GameWorld world;
        private PolicyBotController[] bots;
        private GeminiumBotAgent[] agents;
        private double[] lastCapitalization;
        private bool[] ended;
        private bool requested;

        public void Init(DataCatalog catalog, ulong seed)
        {
            this.catalog = catalog;
            this.seed = seed;
            int n = ScenarioRunner.CompanyIds.Length;
            agents = new GeminiumBotAgent[n];
            for (int i = 0; i < n; i++) agents[i] = GeminiumBotAgent.Create(transform, this, i);
            NewWorld();
            Academy.Instance.AgentPreStep += PreStep;
        }

        private void OnDestroy()
        {
            if (Academy.IsInitialized) Academy.Instance.AgentPreStep -= PreStep;
        }

        private void NewWorld()
        {
            world = new GameWorld(catalog, seed + (ulong)episode * 1000003UL);
            int n = ScenarioRunner.CompanyIds.Length;
            bots = new PolicyBotController[n];
            lastCapitalization = new double[n];
            ended = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bots[i] = new PolicyBotController(catalog.BotArchetype(ScenarioRunner.ArchetypeIds[i]));
                world.AddCompany(ScenarioRunner.CompanyIds[i], bots[i]);
                lastCapitalization[i] = catalog.StartingCash;
            }
            requested = false;
        }

        public BotSituation Situation(int index) => bots[index].PendingSituation ?? bots[index].Situation();

        public void OnActions(int index, int[] actions)
        {
            if (bots[index].AwaitingActions) bots[index].Act(actions);
        }

        private bool AnyAwaiting()
        {
            foreach (var bot in bots)
                if (bot.AwaitingActions) return true;
            return false;
        }

        private void Update()
        {
            if (world == null || AnyAwaiting()) return;
            requested = false;
            for (int budget = StepsPerFrame; budget > 0 && !AnyAwaiting(); budget--)
            {
                int month = world.Simulation.MonthIndex;
                world.Simulation.Step();
                if (world.Simulation.MonthIndex != month && CloseMonth(month)) return;
            }
        }

        // Месяц закрыт финансами: награды; true — эпизод окончен и мир пересоздан.
        private bool CloseMonth(int month)
        {
            for (int i = 0; i < bots.Length; i++)
            {
                if (ended[i] || month == 0) continue;
                var company = world.Companies[i];
                var report = company.LastReport;
                double reward = (report.Capitalization - lastCapitalization[i]) / (catalog.StartingCash * SurrogateArena.RewardScale);
                lastCapitalization[i] = report.Capitalization;
                agents[i].AddReward(company.IsBankrupt ? -1f : (float)reward);
                if (company.IsBankrupt)
                {
                    agents[i].EndEpisode();
                    ended[i] = true;
                }
            }
            if (month == 0)
                for (int i = 0; i < bots.Length; i++) lastCapitalization[i] = world.Companies[i].LastReport.Capitalization;
            if (month + 1 < EpisodeMonths) return false;
            for (int i = 0; i < agents.Length; i++)
                if (!ended[i]) agents[i].EndEpisode();
            episode++;
            NewWorld();
            return true;
        }

        private void PreStep(int academyStep)
        {
            if (requested || !AnyAwaiting()) return;
            for (int i = 0; i < bots.Length; i++)
                if (bots[i].AwaitingActions) agents[i].RequestDecision();
            requested = true;
        }
    }
}
