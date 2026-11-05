using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Production;
using Geminium.Core.Time;
using Geminium.Game;

namespace Geminium.UI
{
    // Цех (GDD 7.6): инструменты планировки, выбранный объект, ТО, график смен, политика брака, такт и узкое место.
    public sealed class FloorPanel : Panel
    {
        public FloorPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.floor";
        public override string Icon => "▦";

        protected override string Signature() =>
            $"{App.Floor?.Selected?.Id}|{App.Floor?.Tool}|{App.Floor?.ZoneBrush}|{App.Floor?.MachineBrush}|{App.Floor?.Facing}|{App.Floor?.ShowSpaghetti}|{Player.Floor.Version}";

        protected override void Build(VisualElement root)
        {
            var floor = App.Floor;
            Tools(root, floor);
            Selection(root, floor);
            Shop(root);
        }

        private void Tools(VisualElement root, FloorView floor)
        {
            var box = Ui.Section(root, Loc.Get("floor.tools"));
            var grid = new VisualElement().With("button-grid").AddTo(box);
            foreach (FloorTool tool in Enum.GetValues(typeof(FloorTool)))
            {
                if (tool == FloorTool.Rack && !World.Rules.Allows(Feature.Logistics)) continue;
                if (tool == FloorTool.QcTable && !World.Rules.Allows(Feature.Quality)) continue;
                var t = tool;
                Ui.Button(Loc.Enum(tool), () => { floor.Tool = t; Refresh(true); }, "small", floor.Tool == t ? "primary" : "plain").AddTo(grid);
            }
            if (floor.Tool == FloorTool.Zone)
            {
                var zones = Enum.GetValues(typeof(ZoneType)).Cast<ZoneType>().Where(z => z != ZoneType.None).ToList();
                Ui.Row(Ui.Text(Loc.Get("floor.zone"), "pair-label"),
                    Ui.Dropdown(zones.Select(z => Loc.Enum(z)).ToList(), zones.IndexOf(floor.ZoneBrush), i => floor.ZoneBrush = zones[i], 240)).AddTo(box);
            }
            if (floor.Tool == FloorTool.Machine)
            {
                var machines = Catalog.Machines.Where(m => World.Rules.Allows(Feature.Molding) || m.Zone != ProductionZone.Molding).ToList();
                if (floor.MachineBrush == null || machines.All(m => m.Id != floor.MachineBrush)) floor.MachineBrush = machines[0].Id;
                var names = machines.Select(m => Loc.Get("floor.machine_choice", Loc.Name(m.Id), m.Tier, Fmt.Millions(m.Price * World.Economy.PriceLevel))).ToList();
                Ui.Row(Ui.Text(Loc.Get("floor.machine"), "pair-label"),
                    Ui.Dropdown(names, machines.FindIndex(m => m.Id == floor.MachineBrush), i => floor.MachineBrush = machines[i].Id, 300)).AddTo(box);
            }
            var row = Ui.Row(Ui.Text(Loc.Get("floor.facing", Loc.Enum(floor.Facing)), "pair-label")).AddTo(box);
            Ui.Button(Loc.Get("floor.rotate"), () => { floor.Facing = (Facing)(((int)floor.Facing + 1) % 4); Refresh(true); }, "small", "plain").AddTo(row);
            Ui.Button(Loc.Get("floor.fit"), floor.FitToView, "small", "plain").AddTo(row);
            box.Add(Ui.Toggle(Loc.Get("floor.spaghetti"), floor.ShowSpaghetti, v => floor.ShowSpaghetti = v));
            box.Add(Ui.Hint(Loc.Get("floor.tools_hint")));
        }

        private void Selection(VisualElement root, FloorView floor)
        {
            var box = Ui.Section(root, Loc.Get("floor.selected"));
            var o = floor.Selected;
            if (o == null || Player.Floor.FindObject(o.Id) == null)
            {
                box.Add(Ui.Hint(Loc.Get("floor.select_hint")));
                return;
            }
            string name = o.Kind == FloorObjectKind.Machine ? Loc.Name(o.DefId) : Loc.Enum(o.Kind);
            box.Add(Ui.Text($"{name} №{o.Id}", "strong"));
            if (o.Kind == FloorObjectKind.Machine)
            {
                var m = o.Machine;
                double condition = World.Production.ConditionEstimate(Player, o);
                Ui.Pair(box, Loc.Get("floor.condition"), Fmt.Number(condition) + "%" + (m.Broken ? " — " + Loc.Get("floor.broken") : ""));
                Ui.Pair(box, Loc.Get("floor.load"), Fmt.Percent(ProductionSystem.MachineLoad(o)));
                Ui.Pair(box, Loc.Get("floor.maintenance_counter"), Loc.Get("floor.of", m.OperationsSinceMaintenance, m.MaintenanceInterval));
                var op = Player.Workers.FirstOrDefault(w => w.AssignedObjectId == o.Id);
                Ui.Pair(box, Loc.Get("floor.operator"), op == null ? Loc.Get("floor.no_operator") : Loc.Get("floor.operator_info", Loc.Name(op.ProfessionId), op.Id, Fmt.Number(op.Experience)));
                Ui.Pair(box, Loc.Get("floor.downtime"), Loc.Get("floor.downtime_info", Fmt.Number(m.Downtime[0] / 60.0), Fmt.Number(m.Downtime[1] / 60.0), Fmt.Number(m.Downtime[2] / 60.0), Fmt.Number(m.Downtime[3] / 60.0)));

                if (World.Rules.Allows(Feature.Maintenance))
                {
                    var interval = Ui.Field(m.MaintenanceInterval.ToString(), 90);
                    Ui.Row(Ui.Text(Loc.Get("floor.maintenance_interval"), "pair-label"), interval,
                        Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(interval.value); if (v != null) Submit(new SetMaintenanceIntervalCommand(o.Id, (int)v.Value)); }, "small")).AddTo(box);
                }
                // Поставить оператора: свободные сотрудники с профессией этой зоны.
                var zone = Catalog.Machine(o.DefId).Zone;
                var free = Player.Workers.Where(w => Catalog.Profession(w.ProfessionId).OperatedZone == zone && w.AssignedObjectId != o.Id).ToList();
                if (free.Count > 0)
                {
                    int pick = 0;
                    var dropdown = Ui.Dropdown(free.Select(w => Loc.Get("staff.worker_short", w.Id, Fmt.Number(w.Experience), w.AssignedObjectId >= 0 ? "●" : "○")).ToList(), 0, i => pick = i, 220);
                    Ui.Row(dropdown, Ui.Button(Loc.Get("floor.assign"), () => Submit(new AssignWorkerCommand(free[pick].Id, o.Id, free[pick].ShiftIndex)), "small")).AddTo(box);
                }
            }
            var actions = Ui.Row().AddTo(box);
            if (o.Kind == FloorObjectKind.Machine && World.Rules.Allows(Feature.Logistics))
                Ui.Button(Loc.Get("floor.feed", Loc.Enum(o.Feed)), () => floor.Submit(new SetFeedSideCommand(o.Id, o.Feed == FeedSide.Front ? FeedSide.Rear : FeedSide.Front)), "small", "plain").AddTo(actions);
            Ui.Button(Loc.Get("floor.move"), () => { floor.Tool = FloorTool.Move; Refresh(true); }, "small", "plain").AddTo(actions);
            Ui.Button(Loc.Get("floor.remove"), () => { if (floor.Submit(new RemoveObjectCommand(o.Id)) == null) floor.Selected = null; }, "small", "danger").AddTo(actions);
        }

        private void Shop(VisualElement root)
        {
            var box = Ui.Section(root, Loc.Get("floor.shop"));
            Ui.Pair(box, Loc.Get("floor.takt"), Loc.Get("floor.takt_value", Fmt.Number(World.Production.TaktMinutes(Player), 1)));
            var bottleneck = World.Production.Bottleneck(Player);
            Ui.Pair(box, Loc.Get("floor.bottleneck"), bottleneck == null ? "—" : $"{Loc.Name(bottleneck.DefId)} №{bottleneck.Id} ({Fmt.Percent(ProductionSystem.MachineLoad(bottleneck))})");
            Ui.Pair(box, Loc.Get("floor.transport_queue"), Fmt.Number(Player.Production.Queue.Count));
            Ui.Pair(box, Loc.Get("floor.expansions"), Loc.Get("floor.of", Player.Floor.Expansions, Catalog.Floor.MaxExpansions));
            Ui.Button(Loc.Get("floor.expand", Fmt.Money(Catalog.Floor.ExpansionMonthlyRent * World.Economy.PriceLevel)), () => Submit(new ExpandBuildingCommand()), "small", "plain").AddTo(box);

            // График работы (GDD 7.7).
            var schedule = Player.Schedule;
            int days = schedule.WorkingDaysPerWeek, shifts = schedule.ShiftsPerDay;
            var dayChoices = new[] { 5, 6 };
            var shiftChoices = new[] { 1, 2, 3 };
            var row = Ui.Row(Ui.Text(Loc.Get("floor.schedule"), "pair-label"),
                Ui.Dropdown(dayChoices.Select(d => Loc.Get("floor.days", d)).ToList(), Array.IndexOf(dayChoices, days), i => days = dayChoices[i], 110),
                Ui.Dropdown(shiftChoices.Select(s => Loc.Get("floor.shifts", s)).ToList(), Array.IndexOf(shiftChoices, shifts), i => shifts = shiftChoices[i], 110)).AddTo(box);
            Ui.Button(Loc.Get("ui.apply"), () => Submit(new SetWorkScheduleCommand(new WorkScheduleSettings
            {
                WorkingDaysPerWeek = days, ShiftsPerDay = shifts, ShiftLengthMinutes = schedule.ShiftLengthMinutes, FirstShiftStartMinute = schedule.FirstShiftStartMinute,
            })), "small").AddTo(row);

            if (World.Rules.Allows(Feature.Quality))
            {
                var policies = Enum.GetValues(typeof(DefectPolicy)).Cast<DefectPolicy>().ToList();
                Ui.Row(Ui.Text(Loc.Get("floor.defect_policy"), "pair-label"),
                    Ui.Dropdown(policies.Select(p => Loc.Enum(p)).ToList(), policies.IndexOf(Player.DefectPolicy), i => Submit(new SetDefectPolicyCommand(policies[i])), 220)).AddTo(box);
            }
            if (World.Rules.Allows(Feature.Logistics))
            {
                var batch = Ui.Field(Player.TransportBatch.ToString(), 80);
                Ui.Row(Ui.Text(Loc.Get("floor.transport_batch"), "pair-label"), batch,
                    Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(batch.value); if (v != null) Submit(new SetTransportBatchCommand((int)v.Value)); }, "small")).AddTo(box);
            }
        }
    }
}
