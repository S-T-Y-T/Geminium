using System;
using System.Linq;
using UnityEngine.UIElements;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;

namespace Geminium.UI
{
    // Персонал (GDD 7.7): рынок труда, сотрудники, зарплаты, обучение, переработки.
    public sealed class StaffPanel : Panel
    {
        public StaffPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.staff";
        public override string Icon => "☺";

        protected override void Build(VisualElement root)
        {
            var staff = Ui.Section(root, Loc.Get("staff.workers", Player.Workers.Count));
            var autoRow = Ui.Row().AddTo(staff);
            Ui.Button(Loc.Get("staff.auto_assign"), AutoAssign, "small").AddTo(autoRow);
            var overtime = Ui.Field(Player.OvertimeHours.ToString(), 60);
            autoRow.Add(Ui.Text(Loc.Get("staff.overtime"), "pair-label"));
            autoRow.Add(overtime);
            Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(overtime.value); if (v != null) Submit(new SetOvertimeCommand((int)v.Value)); }, "small").AddTo(autoRow);

            Ui.Table(staff, new float[] { 150, 50, 90, 50, 50, 140 },
                new[] { Loc.Get("staff.profession"), Loc.Get("staff.experience"), Loc.Get("staff.wage"), Loc.Get("staff.morale"), Loc.Get("staff.fatigue"), "" },
                Player.Workers.OrderBy(w => w.ProfessionId).ThenBy(w => w.Id).Select(w =>
                {
                    var actions = Ui.Row();
                    Ui.Button("+5%", () => Submit(new SetWageCommand(w.Id, (long)Math.Round(w.MonthlyWage * 1.05))), "tiny").AddTo(actions);
                    Ui.Button(Loc.Get("staff.train"), () => Submit(new TrainWorkerCommand(w.Id)), "tiny").AddTo(actions);
                    Ui.Button("✕", () => Submit(new FireCommand(w.Id)), "tiny", "danger").AddTo(actions);
                    string place = w.AssignedObjectId >= 0 ? " ●" : "";
                    return new VisualElement[]
                    {
                        Ui.Text($"{Loc.Name(w.ProfessionId)} №{w.Id}{place}" + (w.AbsentToday ? " — " + Loc.Get("staff.absent") : "")),
                        Ui.Text(Fmt.Number(w.Experience)), Ui.Text(Fmt.Number(w.MonthlyWage)), Ui.Text(Fmt.Number(w.Morale)), Ui.Text(Fmt.Number(w.Fatigue)), actions,
                    };
                }));
            staff.Add(Ui.Hint(Loc.Get("staff.hint", Fmt.Money(Catalog.Staff.TrainingCost * World.Economy.PriceLevel))));

            var market = Ui.Section(root, Loc.Get("staff.candidates"));
            Ui.Table(market, new float[] { 170, 60, 110, 60, 80 },
                new[] { Loc.Get("staff.profession"), Loc.Get("staff.experience"), Loc.Get("staff.expected_wage"), Loc.Get("staff.stamina"), "" },
                World.Staff.Candidates.OrderBy(c => c.ProfessionId).ThenByDescending(c => c.Experience).Select(c => new VisualElement[]
                {
                    Ui.Text(Loc.Name(c.ProfessionId)), Ui.Text(Fmt.Number(c.Experience)), Ui.Text(Fmt.Number(c.ExpectedWage)),
                    Ui.Text(Fmt.Number(c.Stamina, 1)), Ui.Button(Loc.Get("staff.hire"), () => Submit(new HireCommand(c.Id)), "tiny", "primary"),
                }));
        }

        // Свободных операторов — к свободным станкам своей зоны (как делают боты).
        private void AutoAssign()
        {
            var busy = Player.Workers.Where(w => w.AssignedObjectId >= 0).Select(w => w.AssignedObjectId).ToHashSet();
            int assigned = 0;
            foreach (var worker in Player.Workers.Where(w => w.AssignedObjectId < 0))
            {
                var zone = Catalog.Profession(worker.ProfessionId).OperatedZone;
                if (zone == ProductionZone.None) continue;
                var machine = Player.Machines.FirstOrDefault(m => !busy.Contains(m.Id) && Catalog.Machine(m.DefId).Zone == zone);
                if (machine == null) continue;
                busy.Add(machine.Id);
                if (Submit(new AssignWorkerCommand(worker.Id, machine.Id, 0))) assigned++;
            }
            App.Toast(Loc.Get("staff.assigned", assigned));
        }
    }

    // Снабжение (GDD 7.4): биржа сырья, поставщики, разовые заказы и регулярные поставки, входной контроль.
    public sealed class SupplyPanel : Panel
    {
        private string material;

        public SupplyPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.supply";
        public override string Icon => "⇩";
        protected override string Signature() => material;

        protected override void Build(VisualElement root)
        {
            var used = World.Rules.OpenProducts(Catalog).SelectMany(p => p.Operations).SelectMany(o => o.Inputs).Select(i => i.MaterialId).Distinct().ToList();
            var materials = Catalog.Materials.Where(m => used.Contains(m.Id)).ToList();
            if (material == null || materials.All(m => m.Id != material)) material = materials.FirstOrDefault()?.Id;

            var exchange = Ui.Section(root, Loc.Get("supply.exchange"));
            Ui.Table(exchange, new float[] { 170, 120, 110, 100 },
                new[] { Loc.Get("supply.material"), Loc.Get("supply.price"), Loc.Get("supply.stock"), "" },
                materials.Select(m => new VisualElement[]
                {
                    Ui.Text(Loc.Name(m.Id)), Ui.Text(Fmt.Number(World.Supply.ExchangePrice(m.Id))),
                    Ui.Text(Fmt.Number(Player.Inventory.MaterialQuantity(m.Id), 1)),
                    Ui.Button(Loc.Get("supply.suppliers"), () => { material = m.Id; Refresh(true); }, "tiny", m.Id == material ? "primary" : "plain"),
                }));

            if (material != null)
            {
                var box = Ui.Section(root, Loc.Get("supply.suppliers_of", material));
                foreach (var s in Catalog.SuppliersOf(material))
                {
                    var supplier = s;
                    var card = new VisualElement().With("card").AddTo(box);
                    card.Add(Ui.Text(Loc.Name(s.Id), "strong"));
                    card.Add(Ui.Text(Loc.Get("supply.supplier_info", Loc.Enum(s.Origin), Fmt.Percent(s.Reliability), s.QualityMin, s.QualityMax, s.MinBatch, s.LeadTimeDays), "hint", "wrap"));
                    card.Add(Ui.Text(Loc.Get("supply.quote", Fmt.Number(World.Supply.QuotePrice(Player, s, DeliveryFrequency.None))), "hint"));
                    var qty = Ui.Field(s.MinBatch.ToString(), 90);
                    var frequencies = Enum.GetValues(typeof(DeliveryFrequency)).Cast<DeliveryFrequency>().ToList();
                    var current = World.Supply.ScheduleOf(Player, s);
                    int frequency = frequencies.IndexOf(current?.Frequency ?? DeliveryFrequency.Weekly);
                    var row = Ui.Row(qty).AddTo(card);
                    Ui.Button(Loc.Get("supply.order"), () => { var v = Ui.ParseDouble(qty.value); if (v != null) Submit(new PlaceOrderCommand(supplier.Id, v.Value)); }, "small").AddTo(row);
                    row.Add(Ui.Dropdown(frequencies.Select(f => Loc.Enum(f)).ToList(), frequency, i => frequency = i, 150));
                    Ui.Button(Loc.Get("supply.schedule"), () => { var v = Ui.ParseDouble(qty.value); if (v != null) Submit(new SetSupplyScheduleCommand(supplier.Id, v.Value, frequencies[frequency])); }, "small").AddTo(row);
                    if (current != null)
                        Ui.Row(Ui.Text(Loc.Get("supply.current_schedule", Fmt.Number(current.Quantity, 1), Loc.Enum(current.Frequency)), "hint"),
                            Ui.Button(Loc.Get("supply.cancel"), () => Submit(new SetSupplyScheduleCommand(supplier.Id, 0, DeliveryFrequency.None)), "tiny", "danger")).AddTo(card);
                }
            }

            var pending = World.Supply.PendingDeliveries.Where(p => p.Company == Player).ToList();
            var deliveries = Ui.Section(root, Loc.Get("supply.pending", pending.Count));
            foreach (var p in pending.OrderBy(p => p.DueDay))
                deliveries.Add(Ui.Text(Loc.Get("supply.pending_item", Loc.Name(p.Supplier.MaterialId), Fmt.Number(p.Quantity, 1), Fmt.Date(World.Simulation.Calendar.StartDate.AddDays(p.DueDay))), "hint"));

            if (World.Rules.Allows(Feature.Quality))
                Ui.Section(root, Loc.Get("supply.quality")).Add(Ui.Toggle(Loc.Get("supply.input_qc"), Player.InputQcEnabled, v => Submit(new SetInputQcCommand(v))));
        }
    }

    // Склад (GDD 7.5): вместимость, остатки, переполнение.
    public sealed class WarehousePanel : Panel
    {
        public WarehousePanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.warehouse";
        public override string Icon => "▤";

        protected override void Build(VisualElement root)
        {
            var (occupied, overflow) = World.Warehouse.Occupancy(Player);
            var box = Ui.Section(root, Loc.Get("warehouse.capacity"));
            Ui.Pair(box, Loc.Get("warehouse.cells"), Loc.Get("floor.of", occupied, Player.WarehouseCells));
            box.Add(Ui.Bar(Player.WarehouseCells > 0 ? occupied / (double)Player.WarehouseCells : 1, overflow > 0 ? "bar-danger" : "bar-fill"));
            if (overflow > 0) box.Add(Ui.Text(Loc.Get("warehouse.overflow", Fmt.Number(overflow)), "negative"));
            Ui.Pair(box, Loc.Get("warehouse.racks"), Fmt.Number(Player.Floor.ObjectsOfKind(FloorObjectKind.Rack).Count()));
            box.Add(Ui.Hint(Loc.Get("warehouse.hint", Catalog.Warehouse.CellsPerRack)));

            var materials = Ui.Section(root, Loc.Get("warehouse.materials"));
            Ui.Table(materials, new float[] { 220, 120, 120 }, new[] { Loc.Get("supply.material"), Loc.Get("warehouse.quantity"), Loc.Get("warehouse.quality") },
                Player.Inventory.Materials.Where(m => m.Value > 0.01).Select(m => new VisualElement[]
                {
                    Ui.Text(Loc.Name(m.Key)), Ui.Text(Fmt.Number(m.Value, 1)), Ui.Text(Fmt.Number(Player.Inventory.MaterialQuality(m.Key))),
                }));

            var products = Ui.Section(root, Loc.Get("warehouse.products"));
            Ui.Table(products, new float[] { 200, 90, 90, 90 }, new[] { Loc.Get("sales.product"), Loc.Get("warehouse.good"), Loc.Get("warehouse.defective"), Loc.Get("warehouse.quality") },
                Catalog.Products.Where(p => Player.Inventory.ProductUnits(p.Id) > 0 || Player.Inventory.DefectiveStock(p.Id) > 0).Select(p => new VisualElement[]
                {
                    Ui.Text(Loc.Name(p.Id)), Ui.Text(Fmt.Number(Player.Inventory.ProductUnits(p.Id))), Ui.Text(Fmt.Number(Player.Inventory.DefectiveStock(p.Id))),
                    Ui.Text(Fmt.Number(Player.Inventory.ProductQuality(p.Id))),
                }));
        }
    }
}
