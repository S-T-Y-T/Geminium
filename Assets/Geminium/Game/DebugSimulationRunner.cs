using System.Linq;
using System.Text;
using UnityEngine;
using Geminium.Core;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Time;

namespace Geminium.Game
{
    // Отладочная сцена (TODO 1.13): запускает стандартную партию (игрок-бот, Daebo, Abıdas)
    // и показывает состояние мира. Это инструмент разработчика, не игровой интерфейс:
    // тексты здесь не идут через локализацию.
    public sealed class DebugSimulationRunner : MonoBehaviour
    {
        public GameDatabase Database;
        public int Seed = 1;

        private GameWorld world;
        private TimeController time;
        private string error;
        private Vector2 scroll;

        private void Start()
        {
            if (Database == null)
            {
                error = "Не задан GameDatabase в компоненте DebugSimulationRunner.";
                return;
            }
            try
            {
                DataCatalog catalog = Database.CreateCatalog();
                world = ScenarioRunner.CreateStandardWorld(catalog, (ulong)Seed);
                time = new TimeController(world.Simulation, world.Companies[0].Schedule, catalog.Pacing);
            }
            catch (GameDataException e)
            {
                error = e.Message;
            }
        }

        private void Update()
        {
            if (time != null) time.Advance(Time.deltaTime);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));
            if (error != null)
            {
                GUILayout.Label(error);
                GUILayout.EndArea();
                return;
            }
            if (world == null)
            {
                GUILayout.EndArea();
                return;
            }

            var sim = world.Simulation;
            var date = sim.Calendar.ToDateTime(sim.Now);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{date:dd.MM.yyyy HH:mm}  месяц {sim.MonthIndex + 1}", GUILayout.Width(260));
            if (GUILayout.Button(time.IsPaused ? "▶ Пуск" : "|| Пауза", GUILayout.Width(90)))
            {
                if (time.IsPaused) time.Resume(); else time.Pause();
            }
            foreach (int speed in new[] { 1, 2, 4, 8 })
                if (GUILayout.Toggle(time.Speed == speed, $"x{speed}", "Button", GUILayout.Width(40))) time.SetSpeed(speed);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Describe());
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private string Describe()
        {
            var text = new StringBuilder();
            var economy = world.Economy;
            text.AppendLine($"Экономика: индекс {economy.Index:0.00}, инфляция {economy.Inflation:P1}, ключевая ставка {economy.KeyRate:P1}, " +
                            $"сезонность {economy.Seasonality:0.00}, уровень цен {economy.PriceLevel:0.000}");
            text.AppendLine("События: " + (economy.ActiveEvents.Count == 0 ? "нет" : string.Join(", ", economy.ActiveEvents.Select(e => e.Definition.Id))));
            text.AppendLine();

            foreach (var company in world.Companies)
            {
                var last = company.LastReport;
                text.AppendLine($"[{company.Id}] деньги {company.Cash:N0}, долг {company.Debt:N0}, станков {company.Machines.Count}, " +
                                $"ячеек склада {company.WarehouseCells}{(company.IsBankrupt ? ", БАНКРОТ" : "")}");
                if (last != null)
                    text.AppendLine($"   прошлый месяц: выручка {last.Revenue:N0}, чистая прибыль {last.NetIncome:N0}, " +
                                    $"продано {last.UnitsSold}, выпущено {last.UnitsProduced}, капитализация {last.Capitalization:N0}");
                text.AppendLine("   продукция: " + string.Join(", ", company.Inventory.Products.Select(p => $"{p.Key} {p.Value}")));
                text.AppendLine("   сырьё: " + string.Join(", ", company.Inventory.Materials.Select(m => $"{m.Key} {m.Value:0.#}")));
            }

            text.AppendLine();
            text.AppendLine("Биржевые цены сырья (сум за единицу):");
            foreach (var material in world.Catalog.Materials)
                text.AppendLine($"   {material.Id}: {world.Supply.ExchangePrice(material.Id):N0}");
            return text.ToString();
        }
    }
}
