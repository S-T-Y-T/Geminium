using System.Linq;
using System.Text;
using UnityEngine;
using Geminium.Core;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Time;

namespace Geminium.Game
{
    // Отладочная сцена (TODO 1.13, 2.8): стандартная партия (игрок-бот, Daebo, Abıdas), цех выбранной
    // компании (FloorView), сводка слева и управление временем сверху.
    // Инструмент разработчика, не игровой интерфейс: тексты не идут через локализацию.
    public sealed class DebugSimulationRunner : MonoBehaviour
    {
        public GameDatabase Database;
        public int Seed = 1;

        private GameWorld world;
        private TimeController time;
        private FloorView floorView;
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
                if (!TryGetComponent(out floorView)) floorView = gameObject.AddComponent<FloorView>();
                floorView.World = world;
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
            if (error != null)
            {
                GUILayout.Label(error);
                return;
            }
            if (world == null) return;

            var sim = world.Simulation;
            GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, 44), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{sim.Calendar.ToDateTime(sim.Now):dd.MM.yyyy HH:mm}  месяц {sim.MonthIndex + 1}", GUILayout.Width(220));
            if (GUILayout.Button(time.IsPaused ? "▶ Пуск" : "|| Пауза", GUILayout.Width(80)))
            {
                if (time.IsPaused) time.Resume(); else time.Pause();
            }
            foreach (int speed in new[] { 1, 2, 4, 8 })
                if (GUILayout.Toggle(time.Speed == speed, $"x{speed}", "Button", GUILayout.Width(36))) time.SetSpeed(speed);
            GUILayout.Space(16);
            for (int i = 0; i < world.Companies.Count; i++)
                if (GUILayout.Toggle(floorView.CompanyIndex == i, world.Companies[i].Id, "Button", GUILayout.Width(70))) floorView.CompanyIndex = i;
            GUILayout.Space(16);
            floorView.ShowSpaghetti = GUILayout.Toggle(floorView.ShowSpaghetti, "Спагетти", "Button", GUILayout.Width(80));
            floorView.EditMode = GUILayout.Toggle(floorView.EditMode, "Редактировать", "Button", GUILayout.Width(110));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(10, 60, 360, Screen.height - 70), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Describe());
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private string Describe()
        {
            var text = new StringBuilder();
            var economy = world.Economy;
            text.AppendLine($"Экономика: индекс {economy.Index:0.00}, инфляция {economy.Inflation:P1}");
            text.AppendLine($"ключевая ставка {economy.KeyRate:P1}, сезонность {economy.Seasonality:0.00}");
            text.AppendLine("События: " + (economy.ActiveEvents.Count == 0 ? "нет" : string.Join(", ", economy.ActiveEvents.Select(e => e.Definition.Id))));
            text.AppendLine();

            var company = world.Companies[floorView.CompanyIndex];
            var last = company.LastReport;
            text.AppendLine($"[{company.Id}] деньги {company.Cash:N0}{(company.IsBankrupt ? " — БАНКРОТ" : "")}");
            text.AppendLine($"долг {company.Debt:N0}; станков {company.MachineCount}; склад {company.WarehouseCells} ячеек");
            if (last != null)
            {
                text.AppendLine($"прошлый месяц: выручка {last.Revenue:N0}");
                text.AppendLine($"чистая прибыль {last.NetIncome:N0}, капитализация {last.Capitalization:N0}");
                text.AppendLine($"выпуск {last.UnitsProduced}, брак {last.UnitsDefective}, продано {last.UnitsSold}, упущено {last.LostSales}");
                text.AppendLine($"логистика {last.LogisticsKm:0.0} км; простои, ч: нет людей {last.DowntimeNoOperatorHours:0}, " +
                                $"нет работы {last.DowntimeNoMaterialsHours:0}, ремонт {last.DowntimeBrokenHours:0}");
                text.AppendLine($"поломок {last.Breakdowns}, ТО {last.Maintenances}, найден брак {last.DetectedDefects}, возвраты {last.ReturnedUnits}");
            }
            text.AppendLine();
            text.AppendLine($"Персонал ({company.Workers.Count}):");
            foreach (var group in company.Workers.GroupBy(w => w.ProfessionId))
                text.AppendLine($"   {group.Key}: {group.Count()}, мораль {group.Average(w => w.Morale):0}, усталость {group.Average(w => w.Fatigue):0}");
            text.AppendLine($"кандидатов на рынке: {world.Staff.Candidates.Count}");
            text.AppendLine();
            text.AppendLine("Продукция: " + string.Join(", ", company.Inventory.Products.Select(p => $"{p.Key} {p.Value}")));
            text.AppendLine("Сырьё: " + string.Join(", ", company.Inventory.Materials.Select(m => $"{m.Key} {m.Value:0.#}")));
            text.AppendLine();
            text.AppendLine("Биржевые цены сырья (сум за единицу):");
            foreach (var material in world.Catalog.Materials)
                text.AppendLine($"   {material.Id}: {world.Supply.ExchangePrice(material.Id):N0}");
            return text.ToString();
        }
    }
}
