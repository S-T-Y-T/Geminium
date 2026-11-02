using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Factory;
using Geminium.Core.Staff;

namespace Geminium.Game
{
    // Отрисовка цеха выбранной компании (TODO 2.8): клетки — текстурой (зоны, проходы, тепловая карта),
    // объекты — прямоугольниками, разнорабочие — кружками на маршрутах. Камера, выбор объекта, правая панель
    // и инструменты планировки. Все правки идут командами — по тем же правилам, что у ботов.
    // Отладочный инструмент: тексты не идут через локализацию.
    public sealed class FloorView : MonoBehaviour
    {
        private enum Tool { Select, Zone, Aisle, EraseAisle, Machine, Rack, QcTable, Move }

        // Экранные области панелей (пиксели): верхняя строка и сводка слева — DebugSimulationRunner, справа — эта панель.
        private const float TopBarHeight = 60, LeftPanelWidth = 380, RightPanelWidth = 300;

        private static readonly Color[] ZoneColors =
        {
            new Color(0.93f, 0.93f, 0.93f), new Color(0.80f, 0.92f, 0.82f), new Color(0.98f, 0.82f, 0.85f),
            new Color(0.75f, 0.88f, 0.76f), new Color(0.98f, 0.95f, 0.80f), new Color(0.99f, 0.90f, 0.80f),
            new Color(0.99f, 0.86f, 0.72f), new Color(0.96f, 0.80f, 0.65f), new Color(0.84f, 0.87f, 0.92f),
            new Color(0.82f, 0.93f, 0.95f),
        };

        [System.NonSerialized] public GameWorld World; // задаёт DebugSimulationRunner при запуске
        public int CompanyIndex;
        public bool ShowSpaghetti;
        public bool EditMode;

        private Camera cam;
        private Texture2D texture;
        private SpriteRenderer floorRenderer;
        private Sprite square, circle;
        private readonly List<SpriteRenderer> objectPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> workerPool = new List<SpriteRenderer>();
        private int drawnVersion = -1, drawnWidth = -1;
        private bool drawnSpaghetti;
        private long drawnWeek = -1;
        private FloorObject selected;
        private Tool tool;
        private ZoneType zoneBrush = ZoneType.Cutting;
        private int machineBrush;
        private Facing facing = Facing.North;
        private Vector2Int? dragStart;
        private string message;

        private Company Company => World != null && CompanyIndex < World.Companies.Count ? World.Companies[CompanyIndex] : null;

        private void Start() => EnsureCreated();

        private void EnsureCreated()
        {
            if (floorRenderer != null) return;
            cam = Camera.main;
            square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero, 4);
            circle = MakeCircle();
            floorRenderer = new GameObject("FloorCells").AddComponent<SpriteRenderer>();
            floorRenderer.transform.SetParent(transform, false);
            floorRenderer.sortingOrder = 0;
        }

        private void Update()
        {
            var company = Company;
            if (company == null || cam == null) return;
            UpdateCamera(company.Floor);
            Render();
            HandleMouse(company);
        }

        // Нарисовать цех в текущем состоянии. Каждый кадр это делает Update; снимок цеха для документов
        // (GeminiumTools.CaptureFloor) вызывает метод сам — без режима игры.
        public void Render()
        {
            EnsureCreated();
            var company = Company;
            if (company == null) return;
            if (selected != null && company.Floor.FindObject(selected.Id) == null) selected = null;
            RedrawCellsIfNeeded(company);
            DrawObjects(company);
            DrawWorkers(company);
        }

        // ---------- Камера ----------

        private void UpdateCamera(FactoryFloor floor)
        {
            if (!cam.orthographic) cam.orthographic = true;
            var keyboard = Keyboard.current;
            if (drawnWidth < 0 || (keyboard != null && keyboard.fKey.wasPressedThisFrame)) FitCamera(floor);
            var mouse = Mouse.current;
            float speed = cam.orthographicSize * Time.unscaledDeltaTime;
            var move = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (keyboard.rKey.wasPressedThisFrame) facing = (Facing)(((int)facing + 1) % 4);
            }
            cam.transform.position += move * speed * 2;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - Mathf.Sign(scroll) * 1.5f, 4, 40);
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                cam.transform.position -= new Vector3(delta.x, delta.y) * (cam.orthographicSize * 2 / Screen.height);
            }
        }

        // Весь цех в свободной части экрана: между панелями слева и справа, под верхней строкой.
        private void FitCamera(FactoryFloor floor)
        {
            float freeWidth = Mathf.Max(100, Screen.width - LeftPanelWidth - RightPanelWidth);
            float freeHeight = Mathf.Max(100, Screen.height - TopBarHeight);
            float pixelsPerCell = Mathf.Min(freeWidth / (floor.Width + 2), freeHeight / (floor.Height + 2));
            cam.orthographicSize = Screen.height / (2 * pixelsPerCell);
            // Центр свободной части экрана (пиксели от левого нижнего угла) должен совпасть с центром цеха.
            float shiftX = (LeftPanelWidth + freeWidth / 2 - Screen.width / 2f) / pixelsPerCell;
            float shiftY = (freeHeight / 2 - Screen.height / 2f) / pixelsPerCell;
            cam.transform.position = new Vector3(floor.Width / 2f - shiftX, floor.Height / 2f - shiftY, -10);
        }

        // ---------- Клетки ----------

        private void RedrawCellsIfNeeded(Company company)
        {
            var floor = company.Floor;
            long week = World.Simulation.Now.DayIndex / 7;
            bool spaghettiStale = ShowSpaghetti && week != drawnWeek;
            if (floor.Version == drawnVersion && floor.Width == drawnWidth && ShowSpaghetti == drawnSpaghetti && !spaghettiStale) return;

            if (texture == null || texture.width != floor.Width || texture.height != floor.Height)
            {
                texture = new Texture2D(floor.Width, floor.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                floorRenderer.sprite = Sprite.Create(texture, new Rect(0, 0, floor.Width, floor.Height), Vector2.zero, 1);
            }
            var spaghetti = company.Production.SpaghettiLastWeek;
            int max = spaghetti.Length > 0 ? Mathf.Max(1, spaghetti.Max()) : 1;
            for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
            {
                int i = floor.Index(x, y);
                Color c = ZoneColors[(int)floor.ZoneAt(x, y)];
                if (floor.IsAisle(i)) c = Color.white;
                if (ShowSpaghetti && i < spaghetti.Length && spaghetti[i] > 0)
                    c = Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(0.85f, 0.1f, 0.1f), spaghetti[i] / (float)max);
                if ((x + y) % 2 == 0) c *= 0.97f;
                c.a = 1;
                texture.SetPixel(x, y, c);
            }
            var (gx, gy) = floor.Gate;
            texture.SetPixel(gx, gy, new Color(0.3f, 0.3f, 0.3f));
            texture.Apply();
            drawnVersion = floor.Version;
            drawnWidth = floor.Width;
            drawnSpaghetti = ShowSpaghetti;
            drawnWeek = week;
        }

        // ---------- Объекты и люди ----------

        private void DrawObjects(Company company)
        {
            var bottleneck = World.Production.Bottleneck(company);
            int n = 0;
            foreach (var o in company.Floor.Objects)
            {
                var r = Pooled(objectPool, n++, square, 2);
                r.transform.position = new Vector3(o.X + 0.08f, o.Y + 0.08f, 0);
                r.transform.localScale = new Vector3(o.Width - 0.16f, o.Height - 0.16f, 1);
                r.color = ObjectColor(o, o == bottleneck);
                // Сторона подхода — маленький квадрат у края.
                var marker = Pooled(objectPool, n++, square, 3);
                var (ax, ay) = o.AccessCell;
                marker.transform.position = new Vector3((ax + o.X + o.Width / 2f) / 2f - 0.1f + (ax < o.X ? 0.5f : ax >= o.X + o.Width ? -0.5f : 0),
                    (ay + o.Y + o.Height / 2f) / 2f - 0.1f + (ay < o.Y ? 0.5f : ay >= o.Y + o.Height ? -0.5f : 0), 0);
                marker.transform.localScale = new Vector3(0.2f, 0.2f, 1);
                marker.color = o.Feed == FeedSide.Rear ? new Color(0.8f, 0.1f, 0.1f) : new Color(0.95f, 0.55f, 0.1f);
            }
            for (int i = n; i < objectPool.Count; i++) objectPool[i].enabled = false;
        }

        private Color ObjectColor(FloorObject o, bool bottleneck)
        {
            Color c;
            switch (o.Kind)
            {
                case FloorObjectKind.Rack: c = new Color(0.85f, 0.55f, 0.2f); break;
                case FloorObjectKind.QcTable: c = new Color(0.35f, 0.42f, 0.55f); break;
                case FloorObjectKind.Desk: c = new Color(0.1f, 0.6f, 0.65f); break;
                default:
                    c = new Color(0.15f, 0.6f, 0.3f) * (0.75f + 0.12f * World.Catalog.Machine(o.DefId).Tier);
                    if (o.Machine.Broken) c = new Color(0.85f, 0.15f, 0.15f);
                    else if (bottleneck) c = new Color(0.95f, 0.45f, 0.1f);
                    break;
            }
            if (o == selected) c = Color.Lerp(c, Color.white, 0.4f);
            c.a = 1;
            return c;
        }

        private void DrawWorkers(Company company)
        {
            var floor = company.Floor;
            long now = World.Simulation.Now.Minute;
            var laborerId = World.Catalog.Staff.LaborerProfessionId;
            int n = 0;
            foreach (var w in company.Workers)
            {
                Vector2 position;
                if (w.ProfessionId == laborerId) position = LaborerPosition(floor, w, now);
                else if (w.AssignedObjectId >= 0 && floor.FindObject(w.AssignedObjectId) is FloorObject o)
                {
                    var (ax, ay) = o.AccessCell;
                    position = Vector2.Lerp(new Vector2(o.X + o.Width / 2f, o.Y + o.Height / 2f), new Vector2(ax + 0.5f, ay + 0.5f), 0.45f);
                }
                else continue;
                var r = Pooled(workerPool, n++, circle, 5);
                r.transform.position = new Vector3(position.x - 0.2f, position.y - 0.2f, 0);
                r.transform.localScale = new Vector3(0.4f, 0.4f, 1);
                r.color = w.AbsentToday ? Color.gray : WorkerColor(w);
            }
            for (int i = n; i < workerPool.Count; i++) workerPool[i].enabled = false;
        }

        // Разнорабочий — плавно по маршруту перевозки (с учётом доли прошедшего времени).
        private static Vector2 LaborerPosition(FactoryFloor floor, Worker w, long now)
        {
            var task = w.Task;
            if (task == null || task.Path.Count == 0)
            {
                var (x, y) = floor.Cell(w.PositionCell);
                return new Vector2(x + 0.5f, y + 0.5f);
            }
            float t = Mathf.Clamp01((now - task.StartMinute) / (float)Mathf.Max(1, task.EndMinute - task.StartMinute));
            float at = t * (task.Path.Count - 1);
            int i = Mathf.Min(task.Path.Count - 2, (int)at);
            if (i < 0) i = 0;
            var (x0, y0) = floor.Cell(task.Path[i]);
            var (x1, y1) = floor.Cell(task.Path[Mathf.Min(task.Path.Count - 1, i + 1)]);
            return Vector2.Lerp(new Vector2(x0 + 0.5f, y0 + 0.5f), new Vector2(x1 + 0.5f, y1 + 0.5f), at - i);
        }

        private Color WorkerColor(Worker w)
        {
            switch (World.Catalog.Profession(w.ProfessionId).OperatedZone)
            {
                case Geminium.Core.Data.ProductionZone.Cutting: return new Color(0.2f, 0.7f, 0.35f);
                case Geminium.Core.Data.ProductionZone.Sewing: return new Color(0.88f, 0.33f, 0.42f);
                case Geminium.Core.Data.ProductionZone.Molding: return new Color(0.18f, 0.49f, 0.2f);
                default:
                    if (w.ProfessionId == World.Catalog.Staff.MechanicProfessionId) return new Color(0.43f, 0.36f, 0.82f);
                    if (w.ProfessionId == World.Catalog.Staff.QcInspectorProfessionId) return new Color(0.05f, 0.6f, 0.65f);
                    return new Color(0.9f, 0.54f, 0.18f);
            }
        }

        private SpriteRenderer Pooled(List<SpriteRenderer> pool, int index, Sprite sprite, int order)
        {
            while (pool.Count <= index)
            {
                var r = new GameObject("Item").AddComponent<SpriteRenderer>();
                r.transform.SetParent(transform, false);
                pool.Add(r);
            }
            var renderer = pool[index];
            renderer.enabled = true;
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Sprite MakeCircle()
        {
            const int size = 32;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                t.SetPixel(x, y, d <= size / 2f - 1 ? Color.white : Color.clear);
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), Vector2.zero, size);
        }

        // ---------- Мышь: выбор и инструменты ----------

        private void HandleMouse(Company company)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screen = mouse.position.ReadValue();
            if (OverPanel(screen)) return;
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            var cell = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

            if (mouse.leftButton.wasPressedThisFrame) dragStart = cell;
            if (!mouse.leftButton.wasReleasedThisFrame || dragStart == null) return;
            var start = dragStart.Value;
            dragStart = null;
            int x0 = Mathf.Min(start.x, cell.x), y0 = Mathf.Min(start.y, cell.y);
            int w = Mathf.Abs(start.x - cell.x) + 1, h = Mathf.Abs(start.y - cell.y) + 1;

            if (!EditMode || tool == Tool.Select)
            {
                selected = company.Floor.ObjectAt(cell.x, cell.y);
                return;
            }
            var machines = World.Catalog.Machines;
            CompanyCommand command = null;
            switch (tool)
            {
                case Tool.Zone: command = new PaintZoneCommand(x0, y0, w, h, zoneBrush); break;
                case Tool.Aisle: command = new SetAisleCommand(x0, y0, w, h, true); break;
                case Tool.EraseAisle: command = new SetAisleCommand(x0, y0, w, h, false); break;
                case Tool.Machine: command = PlaceObjectCommand.Machine(machines[machineBrush % machines.Count].Id, cell.x, cell.y, facing); break;
                case Tool.Rack: command = new PlaceObjectCommand(FloorObjectKind.Rack, null, cell.x, cell.y, facing); break;
                case Tool.QcTable: command = new PlaceObjectCommand(FloorObjectKind.QcTable, null, cell.x, cell.y, facing); break;
                case Tool.Move:
                    if (selected != null) command = new MoveObjectCommand(selected.Id, cell.x, cell.y, facing);
                    break;
            }
            if (command != null) message = company.Controller.Submit(command) ?? "Принято.";
        }

        private static bool OverPanel(Vector2 screen)
        {
            float top = Screen.height - screen.y; // IMGUI считает от верха
            return top < TopBarHeight || screen.x < LeftPanelWidth || screen.x > Screen.width - RightPanelWidth;
        }

        // ---------- Правая панель ----------

        private void OnGUI()
        {
            var company = Company;
            if (company == null) return;
            GUILayout.BeginArea(new Rect(Screen.width - 290, 60, 280, Screen.height - 70), GUI.skin.box);
            if (EditMode) DrawToolbar(company);
            GUILayout.Space(6);
            DrawSelection(company);
            GUILayout.EndArea();
        }

        private void DrawToolbar(Company company)
        {
            GUILayout.Label("Инструменты (R — поворот: " + facing + ")");
            string[] names = { "Выбор", "Зона", "Проход", "Убрать проход", "Станок", "Стеллаж", "Стол ОТК", "Переместить" };
            tool = (Tool)GUILayout.SelectionGrid((int)tool, names, 2);
            if (tool == Tool.Zone && GUILayout.Button("Зона: " + zoneBrush)) zoneBrush = (ZoneType)(((int)zoneBrush + 1) % 10);
            if (tool == Tool.Machine && GUILayout.Button("Станок: " + World.Catalog.Machines[machineBrush % World.Catalog.Machines.Count].Id))
                machineBrush++;
            if (message != null) GUILayout.Label(message);
        }

        private void DrawSelection(Company company)
        {
            GUILayout.Label($"Такт: {World.Production.TaktMinutes(company):0.0} мин/шт · очередь перевозок: {company.Production.Queue.Count}");
            var bottleneck = World.Production.Bottleneck(company);
            if (bottleneck != null) GUILayout.Label($"Узкое место: {bottleneck.DefId} №{bottleneck.Id}, загрузка {ProductionLoad(bottleneck):P0}");
            if (selected == null)
            {
                GUILayout.Label("Кликните по объекту в цехе.");
                return;
            }
            GUILayout.Label($"{selected.Kind} №{selected.Id}: {selected.DefId}, подача {selected.Feed}, подход {selected.Facing}");
            if (selected.Kind == FloorObjectKind.Machine)
            {
                var m = selected.Machine;
                GUILayout.Label($"Состояние: {m.Condition:0}%{(m.Broken ? " — СЛОМАН" : "")}");
                GUILayout.Label($"ТО: {m.OperationsSinceMaintenance} из {m.MaintenanceInterval} операций");
                GUILayout.Label($"Загрузка: {ProductionLoad(selected):P0}; в работе: {(m.Current != null ? m.Current.ProductId : "—")}");
                GUILayout.Label($"Буфер: вход {m.Input.Count}, выход {m.Output.Count}");
                GUILayout.Label($"Простои за месяц, ч: нет оператора {m.Downtime[0] / 60f:0}, нет работы {m.Downtime[1] / 60f:0}, " +
                                $"ремонт {m.Downtime[2] / 60f:0}, ТО {m.Downtime[3] / 60f:0}");
                var op = company.Workers.FirstOrDefault(w => w.AssignedObjectId == selected.Id);
                if (op != null) GUILayout.Label($"Оператор №{op.Id}: опыт {op.Experience:0}, усталость {op.Fatigue:0}, мораль {op.Morale:0}");
                if (EditMode && GUILayout.Button("Подача: " + selected.Feed))
                    message = company.Controller.Submit(new SetFeedSideCommand(selected.Id, selected.Feed == FeedSide.Front ? FeedSide.Rear : FeedSide.Front));
            }
            if (EditMode && GUILayout.Button("Убрать объект"))
            {
                message = company.Controller.Submit(new RemoveObjectCommand(selected.Id)) ?? "Принято.";
                selected = null;
            }
        }

        private static float ProductionLoad(FloorObject o) => (float)Geminium.Core.Production.ProductionSystem.MachineLoad(o);
    }
}
