using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Modes;
using Geminium.Core.News;
using Geminium.Core.Save;
using Geminium.Core.Time;
using Geminium.Game;
using Geminium.ML;

namespace Geminium.UI
{
    // Игра целиком (GDD 11): главное меню, партия, время, автосохранение, экраны модулей и диалоги.
    // Главный экран (11.1): сверху — дата, деньги, репутация, скорости, язык; слева — меню модулей;
    // в центре — цех (FloorView рисует его камерой за интерфейсом); справа — экран модуля; внизу — лента новостей.
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameApp : MonoBehaviour
    {
        public const float NavWidth = 176, PanelWidth = 560, TopBarHeight = 48, NewsHeight = 118;

        public GameDatabase Database;
        public ModelAsset BotModel;                              // нейросеть ботов; без неё доступны только скриптовые боты
        public List<StringTable> Tables = new List<StringTable>(); // таблицы строк uz / ru / en
        public StyleSheet Style;

        public DataCatalog Catalog { get; private set; }
        public Profile Profile { get; private set; }
        public SaveStorage Saves { get; private set; }
        public GameSession Session { get; private set; }
        public TimeController Clock { get; private set; }
        public FloorView Floor { get; private set; }
        public int CommandsVersion { get; private set; }         // растёт с каждой принятой командой — экраны перерисовываются
        public string DataError { get; private set; }

        // Папка профиля и сохранений; null — persistentDataPath. Тесты подставляют временную папку.
        public static string DataFolderOverride;

        private VisualElement root, layer, overlay, dialogHost, panelHost, navHost, newsHost;
        private Label dateLabel, monthLabel, cashLabel, reputationLabel, toast;
        private Button pauseButton;
        private readonly List<Button> speedButtons = new List<Button>();
        private readonly List<Panel> panels = new List<Panel>();
        private readonly Queue<Action> pendingDialogs = new Queue<Action>();
        private Panel current;
        private Type currentType;
        private Autosave autosave;
        private OnnxBotPolicy policy;
        private WorkSchedule clockSchedule;
        private float refreshTimer;
        private bool finalShown;
        private Task<GameSession> loading;
        private volatile float loadingProgress;
        private Label loadingLabel;
        private int drawnNews = -1;
        private string drawnNewsLanguage;

        // ---------- Запуск ----------

        private void Awake()
        {
            string folder = DataFolderOverride ?? Application.persistentDataPath;
            Saves = new SaveStorage(Path.Combine(folder, "saves"));
            Profile = Profile.Load(Path.Combine(folder, "profile.json"));
            Profile.UnlockAll = Application.isEditor;
            Loc.Register(Tables);
            string language = Loc.HasLanguage(Profile.Language) ? Profile.Language : DefaultLanguage();
            if (Loc.HasLanguage(language)) Loc.SetLanguage(language);
            try
            {
                if (Database == null) throw new InvalidOperationException("GameApp: не задан GameDatabase.");
                Catalog = Database.CreateCatalog();
            }
            catch (Exception e) when (e is GameDataException || e is InvalidOperationException)
            {
                DataError = e.Message;
            }
        }

        private static string DefaultLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.English: return "en";
                default: return "uz";
            }
        }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            if (Style != null && !root.styleSheets.Contains(Style)) root.styleSheets.Add(Style);
            Loc.Changed += Rebuild;
            Rebuild();
        }

        private void OnDisable() => Loc.Changed -= Rebuild;

        private void OnDestroy()
        {
            EndSession();
            Loc.Reset();
        }

        public void SetLanguage(string code)
        {
            Profile.Language = code;
            Profile.Save();
            Loc.SetLanguage(code);
        }

        // Пересобрать весь интерфейс (смена языка, переход меню ↔ партия).
        private void Rebuild()
        {
            if (root == null) return;
            root.Clear();
            layer = new VisualElement().With("layer");
            layer.pickingMode = PickingMode.Ignore;
            root.Add(layer);
            overlay = new VisualElement().With("overlay");
            dialogHost = new VisualElement().With("dialog").AddTo(overlay);
            overlay.style.display = DisplayStyle.None;
            root.Add(overlay);
            toast = Ui.Text("", "toast");
            toast.style.display = DisplayStyle.None;
            root.Add(toast);

            if (DataError != null) layer.Add(Ui.Text(DataError, "error"));
            else if (loading != null) BuildLoading();
            else if (Session == null) MainMenu.Build(this, layer);
            else BuildGame();
        }

        // ---------- Партия ----------

        public void StartGame(GameSetup setup)
        {
            try
            {
                EndSession();
                var policyForSetup = setup.Rivals == RivalBrain.Neural ? CreatePolicy() : null;
                Attach(GameSession.Start(Catalog, setup, policyForSetup));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast(e.Message);
            }
        }

        public void LoadGame(SaveGame save)
        {
            EndSession();
            if (save.Setup.Rivals == RivalBrain.Neural)
            {
                // Нейросеть работает только в главном потоке — загрузка без полосы прогресса.
                try { Attach(save.Restore(Catalog, CreatePolicy())); }
                catch (Exception e) { ShowLoadError(e); }
                return;
            }
            loadingProgress = 0;
            var catalog = Catalog;
            loading = Task.Run(() => save.Restore(catalog, null, p => loadingProgress = (float)p));
            Rebuild();
        }

        private void ShowLoadError(Exception e)
        {
            Debug.LogException(e);
            Rebuild();
            ShowDialog(d =>
            {
                d.Add(Ui.Title(Loc.Get("load.failed")));
                d.Add(Ui.Text(e is SaveException ? e.Message : e.ToString(), "wrap"));
                d.Add(Ui.Button(Loc.Get("ui.ok"), CloseDialog, "primary"));
            });
        }

        private OnnxBotPolicy CreatePolicy()
        {
            if (BotModel == null) throw new InvalidOperationException(Loc.Get("menu.no_model"));
            policy?.Dispose();
            policy = new OnnxBotPolicy(BotModel);
            return policy;
        }

        // Начать играть готовой партией (новой или загруженной).
        public void Attach(GameSession session)
        {
            Session = session;
            finalShown = false;
            if (Saves != null) autosave = new Autosave(session, Saves);
            if (session.MissionTracker != null) session.MissionTracker.Completed += OnMissionCompleted;
            CreateClock();
            Clock.Pause();
            CreateFloor();
            currentType = session.Mission != null ? typeof(MissionPanel) : typeof(FloorPanel);
            Rebuild();
            if (session.Mission != null) ShowDialog(d => Dialogs.MissionBriefing(this, d));
        }

        public void EndSession()
        {
            autosave?.Dispose();
            autosave = null;
            if (Floor != null)
            {
                if (Application.isPlaying) Destroy(Floor.gameObject);
                else DestroyImmediate(Floor.gameObject);
            }
            Floor = null;
            Session = null;
            Clock = null;
            current = null;
            pendingDialogs.Clear();
            policy?.Dispose();
            policy = null;
        }

        public void ExitToMenu()
        {
            EndSession();
            Rebuild();
        }

        public void SaveTo(string slot)
        {
            if (Saves == null) return;
            Saves.Write(slot, SaveGame.Capture(Session, slot, DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
            Toast(Loc.Get("save.done"));
        }

        private void CreateClock()
        {
            bool paused = Clock?.IsPaused ?? true;
            int speed = Clock?.Speed ?? 1;
            clockSchedule = Session.Player.Schedule;
            Clock = new TimeController(Session.World.Simulation, clockSchedule, Session.World.Catalog.Pacing);
            Clock.SetSpeed(speed);
            if (paused) Clock.Pause();
            Clock.AutoPaused += month => ShowDialog(d => Dialogs.MonthReport(this, d, month));
        }

        private void CreateFloor()
        {
            Floor = new GameObject("Floor").AddComponent<FloorView>();
            Floor.World = Session.World;
            Floor.CompanyIndex = Session.World.Companies.ToList().IndexOf(Session.Player);
            Floor.DrawDebugPanel = false;
            Floor.EditMode = true;
            Floor.InsetLeft = NavWidth;
            Floor.InsetRight = PanelWidth;
            Floor.InsetTop = TopBarHeight;
            Floor.InsetBottom = NewsHeight;
            Floor.PointerOverUi = PointerOverUi;
            Floor.KeyboardCaptured = KeyboardCaptured;
            Floor.Submitted += error =>
            {
                if (error != null) Toast(error);
                else CommandsVersion++;
            };
        }

        // Подать команду игрока. Ошибка — всплывающим сообщением.
        public bool Submit(CompanyCommand command)
        {
            string error = Session.World.Submit(Session.Player, command);
            if (error != null)
            {
                Toast(error);
                return false;
            }
            CommandsVersion++;
            return true;
        }

        private void OnMissionCompleted(MissionResult result)
        {
            Profile.RecordMission(result.MissionId, result.Score, result.Passed);
            ShowDialog(d => Dialogs.MissionResult(this, d, result));
        }

        // ---------- Кадр ----------

        private void Update()
        {
            if (loading != null)
            {
                if (loadingLabel != null) loadingLabel.text = Loc.Get("load.progress", Fmt.Percent(loadingProgress));
                if (!loading.IsCompleted) return;
                var task = loading;
                loading = null;
                if (task.IsFaulted) ShowLoadError(task.Exception?.InnerException ?? task.Exception);
                else Attach(task.Result);
                return;
            }
            if (Session == null) return;
            if (Session.Player.Schedule != clockSchedule) CreateClock();
            if (!DialogOpen) Clock.Advance(Time.deltaTime);
            UpdateTopBar();
            CheckFinish();
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0)
            {
                refreshTimer = 0.25f;
                current?.Refresh();
                UpdateNews();
            }
        }

        // Конец свободного режима или банкротство игрока — итоговый рейтинг (один раз).
        private void CheckFinish()
        {
            if (finalShown) return;
            bool finished = Session.Mission == null && Session.IsFinished;
            if (!finished && !Session.Player.IsBankrupt) return;
            finalShown = true;
            Clock.Pause();
            ShowDialog(d => Dialogs.FinalRanking(this, d));
        }

        // ---------- Экран партии ----------

        private void BuildGame()
        {
            var top = new VisualElement().With("topbar").AddTo(layer);
            dateLabel = Ui.Text("", "top-date").AddTo(top);
            monthLabel = Ui.Text("", "top-item").AddTo(top);
            cashLabel = Ui.Text("", "top-item", "top-cash").AddTo(top);
            reputationLabel = Ui.Text("", "top-item").AddTo(top);
            new VisualElement().With("spacer").AddTo(top);
            pauseButton = Ui.Button("", () => { if (Clock.IsPaused) Clock.Resume(); else Clock.Pause(); }, "speed").AddTo(top);
            speedButtons.Clear();
            foreach (int speed in new[] { 1, 2, 4, 8 })
            {
                int s = speed;
                speedButtons.Add(Ui.Button("x" + s, () => { Clock.SetSpeed(s); Clock.Resume(); }, "speed").AddTo(top));
            }
            LanguageDropdown(top);
            Ui.Button(Loc.Get("hud.menu"), () => ShowDialog(d => Dialogs.GameMenu(this, d)), "top-button").AddTo(top);

            var main = new VisualElement().With("main").AddTo(layer);
            main.pickingMode = PickingMode.Ignore;
            navHost = new VisualElement().With("nav").AddTo(main);
            var center = new VisualElement().With("center").AddTo(main);
            center.pickingMode = PickingMode.Ignore;
            new VisualElement { pickingMode = PickingMode.Ignore }.With("center-space").AddTo(center);
            newsHost = new VisualElement().With("news").AddTo(center);
            drawnNews = -1;
            panelHost = new VisualElement().With("panel").AddTo(main);

            panels.Clear();
            if (Session.Mission != null) panels.Add(new MissionPanel(this));
            panels.AddRange(new Panel[]
            {
                new FloorPanel(this), new StaffPanel(this), new SupplyPanel(this), new WarehousePanel(this), new SalesPanel(this),
                new MarketingPanel(this), new FinancePanel(this), new DigitalPanel(this), new AnalyticsPanel(this),
            });
            foreach (var panel in panels.Where(p => p.Available))
            {
                var p = panel;
                var button = Ui.Button(p.Icon + "  " + Loc.Get(p.TitleKey), () => Show(p), "nav-button").AddTo(navHost);
                button.userData = p;
            }
            Show(panels.FirstOrDefault(p => p.GetType() == currentType && p.Available) ?? panels.First(p => p.Available));
            UpdateTopBar();
            UpdateNews();
        }

        private void LanguageDropdown(VisualElement parent)
        {
            var codes = Loc.Languages.Where(Loc.HasLanguage).ToList();
            Ui.Dropdown(codes.Select(c => c.ToUpperInvariant()).ToList(), codes.IndexOf(Loc.Language), i => SetLanguage(codes[i]), 64).AddTo(parent);
        }

        public void Show(Panel panel)
        {
            current = panel;
            currentType = panel.GetType();
            panelHost.Clear();
            panelHost.Add(panel.Root);
            foreach (var button in navHost.Children().OfType<Button>())
                button.EnableInClassList("nav-active", button.userData == panel);
            panel.Refresh(true);
        }

        public T Find<T>() where T : Panel => panels.OfType<T>().FirstOrDefault();

        private void UpdateTopBar()
        {
            if (dateLabel == null || Session == null) return;
            var sim = Session.World.Simulation;
            var player = Session.Player;
            dateLabel.text = sim.Calendar.ToDateTime(sim.Now).ToString("d MMMM yyyy, HH:mm", Loc.Culture);
            monthLabel.text = Loc.Get("hud.month", Math.Min(sim.MonthIndex + 1, Session.Months), Session.Months);
            cashLabel.text = Fmt.Money(player.Cash);
            cashLabel.EnableInClassList("negative", player.Cash < 0);
            reputationLabel.text = Loc.Get("hud.reputation", Fmt.Number(player.Reputation));
            pauseButton.text = Clock.IsPaused ? "▶" : "❚❚";
            for (int i = 0; i < speedButtons.Count; i++)
                speedButtons[i].EnableInClassList("speed-active", !Clock.IsPaused && Clock.Speed == 1 << i);
        }

        private void UpdateNews()
        {
            if (newsHost == null || Session == null) return;
            var items = Session.News.Items;
            int count = items.Count;
            if (drawnNews == count && drawnNewsLanguage == Loc.Language) return;
            drawnNews = count;
            drawnNewsLanguage = Loc.Language;
            newsHost.Clear();
            newsHost.Add(Ui.Text(Loc.Get("hud.news"), "news-title"));
            var calendar = Session.World.Simulation.Calendar;
            foreach (var item in items.Skip(Math.Max(0, count - 4)).Reverse())
            {
                string when = Fmt.Month(calendar.ToDateTime(new SimTime(item.Minute)));
                newsHost.Add(Ui.Text(when + " · " + Voice.News(item), "news-item"));
            }
        }

        // ---------- Диалоги и сообщения ----------

        // Модальное окно поверх игры. Пока оно открыто, время стоит. Следующее окно ждёт закрытия текущего.
        public void ShowDialog(Action<VisualElement> build)
        {
            if (overlay == null) return; // интерфейс ещё не построен
            if (overlay.style.display == DisplayStyle.Flex)
            {
                pendingDialogs.Enqueue(() => ShowDialog(build));
                return;
            }
            dialogHost.Clear();
            build(dialogHost);
            overlay.style.display = DisplayStyle.Flex;
        }

        public bool DialogOpen => overlay != null && overlay.style.display == DisplayStyle.Flex;

        public void CloseDialog()
        {
            if (overlay == null) return;
            overlay.style.display = DisplayStyle.None;
            dialogHost.Clear();
            if (pendingDialogs.Count > 0) pendingDialogs.Dequeue()();
        }

        public void Toast(string text)
        {
            if (toast == null) return;
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toast.schedule.Execute(() => toast.style.display = DisplayStyle.None).StartingIn(3500);
        }

        private void BuildLoading()
        {
            var box = new VisualElement().With("menu").AddTo(layer);
            box.Add(Ui.Title(Loc.Get("load.title")));
            loadingLabel = Ui.Text(Loc.Get("load.progress", Fmt.Percent(0)), "menu-hint").AddTo(box);
        }

        // ---------- Связь с цехом ----------

        private bool PointerOverUi(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            if (overlay != null && overlay.style.display == DisplayStyle.Flex) return true;
            var position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(position) != null;
        }

        private bool KeyboardCaptured()
        {
            var focused = root?.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
        }
    }
}
