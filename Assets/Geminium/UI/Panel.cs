using UnityEngine.UIElements;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Modes;

namespace Geminium.UI
{
    // Экран модуля в правой части (GDD 11.2). Панель целиком пересобирается, когда меняется её «подпись»:
    // день, месяц, язык, число принятых команд и то, что панель добавит сама. Пока игрок вводит текст в поле
    // этой панели, пересборка ждёт — ввод не сбивается.
    public abstract class Panel
    {
        protected readonly GameApp App;
        private string drawnSignature;

        public ScrollView Root { get; } = new ScrollView(ScrollViewMode.Vertical);

        protected Panel(GameApp app)
        {
            App = app;
            Root.AddToClassList("panel-scroll");
        }

        public abstract string TitleKey { get; }
        public virtual string Icon => "•";
        // Возможность, без которой экран скрыт в задании кампании; null — экран есть всегда.
        public virtual Feature? RequiredFeature => null;
        public virtual bool Available => RequiredFeature == null || World.Rules.Allows(RequiredFeature.Value);

        protected GameSession Session => App.Session;
        protected GameWorld World => App.Session.World;
        protected Company Player => App.Session.Player;
        protected DataCatalog Catalog => App.Session.World.Catalog;

        protected virtual string Signature() => "";

        public void Refresh(bool force = false)
        {
            if (App.Session == null) return;
            var sim = World.Simulation;
            string signature = $"{sim.Now.DayIndex}|{sim.MonthIndex}|{Loc.Language}|{App.CommandsVersion}|{Signature()}";
            if (!force && signature == drawnSignature) return;
            if (!force && IsEditing()) return;
            drawnSignature = signature;
            float scroll = Root.verticalScroller.value;
            Root.Clear();
            Root.Add(Ui.Title(Loc.Get(TitleKey)));
            Build(Root.contentContainer);
            Root.schedule.Execute(() => Root.verticalScroller.value = scroll);
        }

        protected abstract void Build(VisualElement root);

        // Подать команду игрока; ошибка — всплывающим сообщением. true — команда принята.
        protected bool Submit(CompanyCommand command) => App.Submit(command);

        private bool IsEditing()
        {
            var focused = Root.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && Root.Contains(focused) && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
        }
    }
}
