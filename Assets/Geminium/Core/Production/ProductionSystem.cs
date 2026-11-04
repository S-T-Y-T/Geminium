using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;
using Geminium.Core.Factory;
using Geminium.Core.Staff;
using Geminium.Core.Time;

namespace Geminium.Core.Production
{
    // Производство на пространственном цехе (GDD 7.6, этап 2).
    // Каждую рабочую минуту компании:
    //   1) усталость и опыт сотрудников на смене, отдых остальных;
    //   2) станки: простой (ремонт, ТО, нет оператора, нечего делать) или работа над операцией;
    //      время операции T = T_base / (Power · Eff) + t_feed, брак — на каждой операции, износ — после неё;
    //   3) раз в RequestIntervalMinutes — заявки на перевозку сырья, полуфабрикатов и готовой продукции;
    //   4) разнорабочие берут заявки по очереди и везут груз по проходам.
    // Готовая продукция на складе проходит выходной ОТК (если есть стол ОТК и контролёр на смене).
    // Цифровизация (GDD 7.9): ИИ-раскрой уменьшает отходы листовых материалов, ИИ-контроль проверяет каждую единицу,
    // предиктивное ТО обслуживает станок по состоянию, а не по счётчику операций.
    public sealed class ProductionSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly ProductionSettings settings;
        private readonly FloorSettings floorSettings;
        private readonly MaintenanceSettings maintenance;
        private readonly QualitySettings quality;
        private readonly StaffSettings staffSettings;
        private SimRandom random;
        private SimRandom qcRandom;

        public ProductionSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Production;
            floorSettings = catalog.Floor;
            maintenance = catalog.Maintenance;
            quality = catalog.Quality;
            staffSettings = catalog.Staff;
        }

        protected override void OnAttached()
        {
            random = Simulation.Random.Fork("production");
            qcRandom = Simulation.Random.Fork("production.qc");
        }

        private EconomySystem Economy => world.Economy;
        private StaffSystem Staff => world.Staff;

        // Текущая смена компании и признак переработки; смена −1 — нерабочее время.
        // Переработка — часы сразу после последней смены рабочего дня (только при 1–2 сменах).
        public static (int shift, bool overtime) CurrentShift(Company company, DateTime moment)
        {
            var schedule = company.Schedule;
            int shift = schedule.ShiftAt(moment);
            if (shift >= 0) return (shift, false);
            if (company.OvertimeHours <= 0 || schedule.ShiftsPerDay >= 3) return (-1, false);

            DateTime workDay = moment.AddMinutes(-schedule.FirstShiftStartMinute);
            if (!schedule.IsWorkingDay(workDay.DayOfWeek)) return (-1, false);
            int minute = workDay.Hour * 60 + workDay.Minute;
            int end = schedule.ShiftsPerDay * schedule.ShiftLengthMinutes;
            return minute >= end && minute < end + company.OvertimeHours * 60 ? (schedule.ShiftsPerDay - 1, true) : (-1, false);
        }

        // Сколько сырья уходит на операцию у этой компании: в раскрое листовые материалы (м²) идут с отходами.
        public double InputAmount(Company company, OperationDef op, MaterialAmount input)
        {
            if (op.Zone != ProductionZone.Cutting || catalog.Material(input.MaterialId).Unit != MaterialUnit.SquareMeter) return input.Amount;
            double waste = company.Digital.Has(DigitalModule.AiCutting) ? catalog.Digital.AiCuttingWasteShare : settings.CuttingWasteShare;
            return input.Amount * (1 + waste);
        }

        // Пора ли обслуживать станок: с предиктивным ТО — по состоянию, иначе — по регламенту в операциях.
        public bool MaintenanceDue(Company company, MachineRuntime m) =>
            company.Digital.Has(DigitalModule.PredictiveMaintenance)
                ? m.Condition <= catalog.Digital.PredictiveConditionThreshold
                : m.OperationsSinceMaintenance >= m.MaintenanceInterval;

        // Состояние станка, каким его видит компания: с IoT-датчиками — точно, без них — грубо, с шагом ошибки.
        public double ConditionEstimate(Company company, FloorObject machine)
        {
            double condition = machine.Machine.Condition;
            if (company.Digital.Has(DigitalModule.IotSensors)) return condition;
            double step = Math.Max(1, catalog.Digital.ConditionErrorWithoutIot * 2);
            return Formulas.Clamp(Math.Round(condition / step) * step, 0, 100);
        }

        internal void InitializeMachine(FloorObject machine) =>
            machine.Machine = new MachineRuntime { MaintenanceInterval = maintenance.DefaultIntervalOperations };

        // ---------- Календарные события ----------

        protected internal override void OnMonthStarted(int monthIndex)
        {
            foreach (var company in world.Companies)
            {
                company.Production.StartedThisMonth.Clear();
                foreach (var machine in company.Machines)
                {
                    var m = machine.Machine;
                    m.AvailableMinutes = m.BusyMinutes = 0;
                    Array.Clear(m.Downtime, 0, m.Downtime.Length);
                    m.Breakdowns = m.Maintenances = 0;
                }
            }
        }

        protected internal override void OnDayStarted(long dayIndex)
        {
            bool monday = Simulation.Calendar.StartDate.AddDays(dayIndex).DayOfWeek == DayOfWeek.Monday;
            foreach (var company in world.Companies)
            {
                var state = company.Production;
                state.QcCapacityUsedToday = 0;
                if (!monday) continue;
                foreach (var machine in company.Machines)
                {
                    var m = machine.Machine;
                    m.LastWeekAvailableMinutes = m.WeekAvailableMinutes;
                    m.LastWeekBusyMinutes = m.WeekBusyMinutes;
                    m.WeekAvailableMinutes = m.WeekBusyMinutes = 0;
                }
                state.EnsureSpaghetti(company.Floor.Width * company.Floor.Height);
                Array.Copy(state.SpaghettiWeek, state.SpaghettiLastWeek, state.SpaghettiWeek.Length);
                Array.Clear(state.SpaghettiWeek, 0, state.SpaghettiWeek.Length);
            }
        }

        protected internal override void OnDayEnded(long dayIndex)
        {
            foreach (var company in world.Companies)
            {
                if (company.IsBankrupt) continue;
                var state = company.Production;
                company.Pay(LedgerCategory.Energy, (long)Math.Round(state.EnergyKwhToday * settings.EnergyPricePerKwh * Economy.PriceLevel));
                state.EnergyKwhToday = 0;

                // При 1–2 сменах плановое ТО делается в окне между сменами, без простоя.
                bool mechanicToday = company.workers.Any(w => w.ProfessionId == staffSettings.MechanicProfessionId && !w.AbsentToday);
                if (company.Schedule.ShiftsPerDay >= 3 || !mechanicToday) continue;
                foreach (var machine in company.Machines)
                {
                    var m = machine.Machine;
                    if (!m.Broken && MaintenanceDue(company, m)) DoMaintenance(company, machine);
                }
            }
        }

        protected internal override void OnMinute(SimTime now)
        {
            DateTime moment = Simulation.Calendar.ToDateTime(now);
            foreach (var company in world.Companies)
                if (!company.IsBankrupt) ProcessMinute(company, now.Minute, moment);
        }

        // ---------- Минута цеха ----------

        private void ProcessMinute(Company company, long minute, DateTime moment)
        {
            var (shift, overtime) = CurrentShift(company, moment);
            if (shift < 0) return;

            var state = company.Production;
            state.EnsureSpaghetti(company.Floor.Width * company.Floor.Height);
            if (state.LastWorkingMinute >= 0 && minute - state.LastWorkingMinute > 1)
                foreach (var w in company.workers) Staff.Rest(w, minute - state.LastWorkingMinute - 1);
            state.LastWorkingMinute = minute;

            var operators = state.OperatorsNow;
            var laborers = state.LaborersNow;
            operators.Clear();
            laborers.Clear();
            int mechanicsOnShift = 0, inspectorsOnShift = 0;
            bool anyMechanic = false;
            foreach (var w in company.workers)
            {
                bool isMechanic = w.ProfessionId == staffSettings.MechanicProfessionId;
                anyMechanic |= isMechanic;
                if (w.ShiftIndex != shift || w.AbsentToday)
                {
                    Staff.Rest(w, 1);
                    continue;
                }
                Staff.Work(w, overtime);
                if (overtime) company.CurrentMonth.OvertimeHours += 1.0 / 60;

                if (w.ProfessionId == staffSettings.LaborerProfessionId) laborers.Add(w);
                else if (isMechanic) mechanicsOnShift++;
                else if (w.ProfessionId == staffSettings.QcInspectorProfessionId) inspectorsOnShift++;
                else if (w.AssignedObjectId >= 0 && !operators.ContainsKey(w.AssignedObjectId)) operators[w.AssignedObjectId] = w;
            }
            state.InspectorsOnShift = inspectorsOnShift;

            foreach (var o in company.Floor.Objects)
                if (o.Kind == FloorObjectKind.Machine) ProcessMachine(company, o, operators, minute, mechanicsOnShift > 0, anyMechanic);

            if (minute % settings.RequestIntervalMinutes == 0) GenerateRequests(company, minute);
            foreach (var w in laborers) ProcessLaborer(company, w, minute);
        }

        private void ProcessMachine(Company company, FloorObject o, Dictionary<int, Worker> operators, long minute, bool mechanicOnShift, bool anyMechanic)
        {
            var m = o.Machine;
            var def = catalog.Machine(o.DefId);
            m.AvailableMinutes++;
            m.WeekAvailableMinutes++;

            if (m.DownUntil > minute)
            {
                Down(company, m, m.DownCause);
                return;
            }
            if (m.Broken)
            {
                m.Broken = false;
                m.Condition = maintenance.ConditionAfterRepair;
                m.OperationsSinceMaintenance = 0;
            }
            // При трёх сменах окна нет: плановое ТО останавливает станок.
            if (company.Schedule.ShiftsPerDay >= 3 && MaintenanceDue(company, m) && mechanicOnShift)
            {
                DoMaintenance(company, o);
                m.DownUntil = minute + maintenance.PlannedMinutes;
                m.DownCause = DowntimeCause.Maintenance;
                Down(company, m, DowntimeCause.Maintenance);
                return;
            }
            if (!operators.TryGetValue(o.Id, out var worker))
            {
                Down(company, m, DowntimeCause.NoOperator);
                return;
            }
            if (m.Current == null && !TryStartJob(company, o, def, worker))
            {
                Down(company, m, DowntimeCause.NoMaterials);
                return;
            }

            m.BusyMinutes++;
            m.WeekBusyMinutes++;
            m.Progress += 1;
            company.Production.EnergyKwhToday += def.EnergyKw / 60;
            if (m.Progress + 1e-9 >= m.RequiredMinutes) CompleteOperation(company, o, def, worker, minute, anyMechanic);
        }

        private static void Down(Company company, MachineRuntime m, DowntimeCause cause)
        {
            company.CurrentMonth.DowntimeMinutes[(int)cause]++;
            m.Downtime[(int)cause]++;
        }

        // Взять работу: ремонт брака (на пошиве), полуфабрикат из буфера или новую единицу по плану.
        private bool TryStartJob(Company company, FloorObject o, MachineDef def, Worker worker)
        {
            var m = o.Machine;
            var zone = def.Zone;
            double fit = catalog.Profession(worker.ProfessionId).OperatedZone == zone ? 1 : settings.MismatchEfficiency;
            double efficiency = Formulas.WorkerEfficiency(worker.Experience, worker.Fatigue, fit);
            double feed = o.Feed == FeedSide.Rear ? floorSettings.RearFeedExtraMinutes : 0;
            var state = company.Production;

            if (zone == ProductionZone.Sewing && state.Rework.Count > 0)
            {
                m.Current = state.Rework[0];
                state.Rework.RemoveAt(0);
                m.CurrentStep = -1;
                m.Progress = 0;
                m.RequiredMinutes = Formulas.OperationTime(quality.RepairMinutes, def.Power, efficiency, feed);
                return true;
            }

            for (int i = 0; i < m.Input.Count; i++)
            {
                var unit = m.Input[i];
                var ops = catalog.Product(unit.ProductId).Operations;
                if (unit.NextStep >= ops.Count || ops[unit.NextStep].Zone != zone || !HasInputs(company, m, ops[unit.NextStep])) continue;
                m.Input.RemoveAt(i);
                Start(company, m, unit, unit.NextStep, ops[unit.NextStep], def, efficiency, feed);
                return true;
            }

            foreach (var product in catalog.Products)
            {
                if (!product.UnlockedByDefault || product.Operations[0].Zone != zone) continue;
                state.StartedThisMonth.TryGetValue(product.Id, out int started);
                if (started >= company.MonthlyPlan(product.Id) || !HasInputs(company, m, product.Operations[0])) continue;
                state.StartedThisMonth[product.Id] = started + 1;
                Start(company, m, new WorkUnit { ProductId = product.Id }, 0, product.Operations[0], def, efficiency, feed);
                return true;
            }
            return false;
        }

        private bool HasInputs(Company company, MachineRuntime m, OperationDef op)
        {
            foreach (var input in op.Inputs)
                if (m.Materials.MaterialQuantity(input.MaterialId) + 1e-9 < InputAmount(company, op, input)) return false;
            return true;
        }

        private void Start(Company company, MachineRuntime m, WorkUnit unit, int step, OperationDef op, MachineDef def, double efficiency, double feed)
        {
            foreach (var input in op.Inputs)
            {
                double amount = InputAmount(company, op, input);
                m.Materials.TryTakeMaterial(input.MaterialId, amount, out double q, out double cost);
                unit.RawQualitySum += q;
                unit.RawQualityCount++;
                unit.MaterialCost += amount * cost;
                company.CurrentMonth.FabricWaste += amount - input.Amount;
            }
            m.Current = unit;
            m.CurrentStep = step;
            m.Progress = 0;
            m.RequiredMinutes = Formulas.OperationTime(op.BaseTimeMinutes, def.Power, efficiency, feed);
        }

        private void CompleteOperation(Company company, FloorObject o, MachineDef def, Worker worker, long minute, bool anyMechanic)
        {
            var m = o.Machine;
            var unit = m.Current;
            m.Current = null;
            unit.ReadySince = minute;

            if (m.CurrentStep < 0)
            {
                // Ремонт брака: единица снова годная, но чуть хуже.
                unit.Defective = false;
                unit.RawQualitySum -= quality.RepairQualityPenalty * Math.Max(1, unit.RawQualityCount);
            }
            else
            {
                var product = catalog.Product(unit.ProductId);
                double raw = unit.RawQualityCount > 0 ? unit.RawQualitySum / unit.RawQualityCount : 50;
                double chance = Formulas.DefectChance(product.BaseDefectChance / product.Operations.Count, worker.Experience, raw,
                    m.Condition, worker.Fatigue, settings.WearExponent);
                if (random.Chance(chance)) unit.Defective = true;
                unit.SkillSum += worker.Experience;
                unit.ConditionSum += m.Condition;
                unit.OperationsDone++;
                unit.NextStep = m.CurrentStep + 1;
                if (unit.NextStep >= product.Operations.Count)
                    MonthStats.Add(unit.Defective ? company.CurrentMonth.Defective : company.CurrentMonth.Produced, unit.ProductId, 1);
            }
            m.Output.Add(unit);

            m.Condition -= 100.0 / def.DurabilityOperations;
            m.OperationsSinceMaintenance++;
            if (m.Condition <= 0) Breakdown(company, o, def, worker, minute, anyMechanic);
        }

        private void Breakdown(Company company, FloorObject o, MachineDef def, Worker worker, long minute, bool anyMechanic)
        {
            var m = o.Machine;
            double factor = anyMechanic ? 1 : maintenance.NoMechanicFactor;
            m.Condition = 0;
            m.Broken = true;
            m.DownUntil = minute + (long)Math.Round(maintenance.UnplannedMinutes * factor);
            m.DownCause = DowntimeCause.Broken;
            company.Pay(LedgerCategory.Maintenance, (long)Math.Round(def.Price * maintenance.UnplannedCostShare * factor * Economy.PriceLevel));
            company.CurrentMonth.Breakdowns++;
            m.Breakdowns++;
            worker.BreakdownsThisMonth++;
        }

        private void DoMaintenance(Company company, FloorObject o)
        {
            var m = o.Machine;
            company.Pay(LedgerCategory.Maintenance, (long)Math.Round(catalog.Machine(o.DefId).Price * maintenance.PlannedCostShare * Economy.PriceLevel));
            m.Condition = 100;
            m.OperationsSinceMaintenance = 0;
            company.CurrentMonth.Maintenances++;
            m.Maintenances++;
        }

        // ---------- Заявки на перевозку ----------

        private void GenerateRequests(Company company, long minute)
        {
            var state = company.Production;
            int batch = company.TransportBatch;
            var active = catalog.Products.Where(p => p.UnlockedByDefault && company.MonthlyPlan(p.Id) > 0).ToList();

            foreach (var o in company.Floor.Objects)
            {
                if (o.Kind != FloorObjectKind.Machine) continue;
                var m = o.Machine;
                var zone = catalog.Machine(o.DefId).Zone;

                // Сырьё к станку: держим запас примерно на полпартии.
                foreach (var product in active)
                foreach (var op in product.Operations)
                {
                    if (op.Zone != zone) continue;
                    foreach (var input in op.Inputs)
                    {
                        double amount = InputAmount(company, op, input);
                        if (m.Materials.MaterialQuantity(input.MaterialId) >= amount * Math.Max(1, batch / 2.0)) continue;
                        string key = "M|" + o.Id + "|" + input.MaterialId;
                        if (state.PendingKeys.Contains(key) || company.Inventory.MaterialQuantity(input.MaterialId) + 1e-9 < amount) continue;
                        Enqueue(state, new TransportRequest
                        {
                            Kind = TransportKind.Material, ToObjectId = o.Id, MaterialId = input.MaterialId,
                            Quantity = amount * batch, Key = key,
                        });
                    }
                }

                // Готовые единицы со станка: полная партия или неполная, если ждёт слишком долго.
                var groups = new List<string>();
                foreach (var unit in m.Output)
                {
                    string group = unit.ProductId + "|" + unit.NextStep + "|" + unit.IsRework;
                    if (!groups.Contains(group)) groups.Add(group);
                }
                foreach (string group in groups)
                {
                    var units = m.Output.Where(u => u.ProductId + "|" + u.NextStep + "|" + u.IsRework == group).ToList();
                    if (units.Count < batch && minute - units.Min(u => u.ReadySince) < settings.FlushMinutes) continue;
                    var first = units[0];
                    var product = catalog.Product(first.ProductId);
                    string key = "T|" + o.Id + "|" + group;
                    if (state.PendingKeys.Contains(key)) continue;

                    if (first.IsRework || first.NextStep >= product.Operations.Count)
                    {
                        Enqueue(state, new TransportRequest
                        {
                            Kind = TransportKind.Finished, FromObjectId = o.Id, ProductId = first.ProductId, Step = first.NextStep,
                            Units = Math.Min(units.Count, batch), Key = key,
                        });
                        continue;
                    }
                    var target = PickTarget(company, product.Operations[first.NextStep].Zone);
                    if (target == null) continue;
                    Enqueue(state, new TransportRequest
                    {
                        Kind = TransportKind.Wip, FromObjectId = o.Id, ToObjectId = target.Id, ProductId = first.ProductId,
                        Step = first.NextStep, Units = Math.Min(units.Count, batch), Key = key,
                    });
                }
            }
        }

        private static void Enqueue(ProductionState state, TransportRequest request)
        {
            state.Queue.Add(request);
            state.PendingKeys.Add(request.Key);
        }

        // Самый свободный исправный станок нужной зоны.
        private FloorObject PickTarget(Company company, ProductionZone zone)
        {
            FloorObject best = null;
            int bestLoad = int.MaxValue;
            foreach (var o in company.Machines)
            {
                if (catalog.Machine(o.DefId).Zone != zone || o.Machine.Broken) continue;
                int load = o.Machine.Input.Count + (o.Machine.Current != null ? 1 : 0);
                if (load < bestLoad) { best = o; bestLoad = load; }
            }
            return best;
        }

        // ---------- Разнорабочие ----------

        private void ProcessLaborer(Company company, Worker worker, long minute)
        {
            if (worker.Task != null)
            {
                if (minute < worker.Task.EndMinute) return;
                CompleteTask(company, worker.Task);
                worker.PositionCell = worker.Task.Path[worker.Task.Path.Count - 1];
                worker.Task = null;
            }
            if (!company.Floor.IsAisle(worker.PositionCell))
            {
                var (gx, gy) = company.Floor.Gate;
                worker.PositionCell = company.Floor.Index(gx, gy);
            }
            TryStartTask(company, worker, minute);
        }

        private void TryStartTask(Company company, Worker worker, long minute)
        {
            var state = company.Production;
            for (int i = 0; i < state.Queue.Count; i++)
            {
                var request = state.Queue[i];
                int pickup = PickupCell(company, request);
                int drop = pickup < 0 ? -1 : DropCell(company, request, pickup);
                if (drop < 0) continue;
                int toPickup = company.Paths.Distance(worker.PositionCell, pickup);
                int toDrop = company.Paths.Distance(pickup, drop);
                if (toPickup < 0 || toDrop < 0) continue;

                var task = new TransportTask { Request = request };
                state.Queue.RemoveAt(i);
                if (!Reserve(company, request, task))
                {
                    state.PendingKeys.Remove(request.Key);
                    i--;
                    continue;
                }

                task.Path.AddRange(company.Paths.Path(worker.PositionCell, pickup));
                task.LoadIndex = task.Path.Count - 1;
                task.Path.AddRange(company.Paths.Path(pickup, drop).Skip(1));
                double cells = toPickup + toDrop;
                double speed = floorSettings.WalkMetersPerMinute * (0.75 + worker.Experience / 200) * (worker.Fatigue > 70 ? 0.5 : 1)
                               / floorSettings.CellSizeMeters;
                task.StartMinute = minute;
                task.EndMinute = minute + Math.Max(1, (long)Math.Ceiling(cells / speed + 2 * floorSettings.LoadMinutes));
                foreach (int cell in task.Path) state.SpaghettiWeek[cell]++;
                company.CurrentMonth.LogisticsMeters += cells * floorSettings.CellSizeMeters;
                worker.Task = task;
                return;
            }
        }

        private int AccessIndex(Company company, FloorObject o)
        {
            if (o == null) return -1;
            var (x, y) = o.AccessCell;
            return company.Floor.InBounds(x, y) ? company.Floor.Index(x, y) : -1;
        }

        private int PickupCell(Company company, TransportRequest request)
        {
            if (request.Kind != TransportKind.Material) return AccessIndex(company, company.Floor.FindObject(request.FromObjectId));
            int destination = AccessIndex(company, company.Floor.FindObject(request.ToObjectId));
            return destination < 0 ? -1 : StoragePoint(company, ZoneType.RawStorage, destination);
        }

        private int DropCell(Company company, TransportRequest request, int pickup) =>
            request.Kind == TransportKind.Finished
                ? StoragePoint(company, ZoneType.FinishedStorage, pickup)
                : AccessIndex(company, company.Floor.FindObject(request.ToObjectId));

        // Ближайший к клетке стеллаж нужной складской зоны; если стеллажей нет — ворота.
        private int StoragePoint(Company company, ZoneType zone, int near)
        {
            int best = -1, bestDistance = int.MaxValue;
            foreach (var rack in company.Floor.ObjectsOfKind(FloorObjectKind.Rack))
            {
                if (company.Floor.ZoneAt(rack.X, rack.Y) != zone) continue;
                int access = AccessIndex(company, rack);
                if (access < 0) continue;
                int distance = company.Paths.Distance(near, access);
                if (distance >= 0 && distance < bestDistance) { best = access; bestDistance = distance; }
            }
            var (gx, gy) = company.Floor.Gate;
            return best >= 0 ? best : company.Floor.Index(gx, gy);
        }

        // Забрать груз в начале перевозки. false — забирать нечего, заявка отменяется.
        private bool Reserve(Company company, TransportRequest request, TransportTask task)
        {
            if (request.Kind == TransportKind.Material)
            {
                double quantity = Math.Min(request.Quantity, company.Inventory.MaterialQuantity(request.MaterialId));
                if (quantity <= 1e-9 || company.Floor.FindObject(request.ToObjectId) == null) return false;
                company.Inventory.TryTakeMaterial(request.MaterialId, quantity, out double q, out double cost);
                task.MaterialId = request.MaterialId;
                task.Quantity = quantity;
                task.Quality = q;
                task.UnitCost = cost;
                return true;
            }

            var source = company.Floor.FindObject(request.FromObjectId);
            if (source?.Machine == null) return false;
            if (request.Kind == TransportKind.Wip && company.Floor.FindObject(request.ToObjectId)?.Machine == null) return false;
            var output = source.Machine.Output;
            for (int i = 0; i < output.Count && task.Units.Count < request.Units; i++)
            {
                var unit = output[i];
                if (unit.ProductId != request.ProductId || unit.NextStep != request.Step) continue;
                task.Units.Add(unit);
                output.RemoveAt(i);
                i--;
            }
            return task.Units.Count > 0;
        }

        private void CompleteTask(Company company, TransportTask task)
        {
            var request = task.Request;
            company.Production.PendingKeys.Remove(request.Key);
            switch (request.Kind)
            {
                case TransportKind.Material:
                    var target = company.Floor.FindObject(request.ToObjectId)?.Machine;
                    if (target != null) target.Materials.AddMaterial(task.MaterialId, task.Quantity, task.Quality, task.UnitCost);
                    else company.Inventory.AddMaterial(task.MaterialId, task.Quantity, task.Quality, task.UnitCost);
                    break;
                case TransportKind.Wip:
                    var next = company.Floor.FindObject(request.ToObjectId)?.Machine ?? company.Floor.FindObject(request.FromObjectId)?.Machine;
                    if (next != null) next.Input.AddRange(task.Units);
                    else foreach (var unit in task.Units) company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(unit.MaterialCost);
                    break;
                default:
                    StoreFinished(company, task.Units);
                    break;
            }
        }

        // ---------- ОТК и склад ----------

        private void StoreFinished(Company company, List<WorkUnit> units)
        {
            var state = company.Production;
            int capacity = state.InspectorsOnShift * quality.QcUnitsPerHour * 8;
            bool qcStation = company.HasQcStation;
            foreach (var unit in units)
            {
                if (unit.IsRework) unit.IsRework = false;
                bool vision = company.Digital.Has(DigitalModule.AiQuality);
                bool inspected = vision || (qcStation && state.QcCapacityUsedToday < capacity);
                if (inspected && !vision) state.QcCapacityUsedToday++;
                bool detected = qcRandom.Chance(vision ? catalog.Digital.AiQualityDetection : quality.OutputQcDetection) && inspected && unit.Defective;
                if (detected)
                {
                    company.CurrentMonth.DetectedDefects++;
                    ApplyDefectPolicy(company, unit);
                    continue;
                }
                company.Inventory.AddProducts(unit.ProductId, 1, unit.Quality, unit.MaterialCost, unit.Defective ? 1 : 0);
            }
        }

        private void ApplyDefectPolicy(Company company, WorkUnit unit)
        {
            switch (company.DefectPolicy)
            {
                case DefectPolicy.Store:
                    company.Inventory.AddDefectiveStock(unit.ProductId, 1, unit.MaterialCost);
                    break;
                case DefectPolicy.Repair:
                    unit.IsRework = true;
                    company.Production.Rework.Add(unit);
                    break;
                case DefectPolicy.Discount:
                    company.Receive(LedgerCategory.SalesRetail,
                        (long)Math.Round(company.RetailPrice(unit.ProductId) * quality.DiscountPriceShare));
                    company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(unit.MaterialCost);
                    break;
                default:
                    company.CurrentMonth.ScrappedUnits++;
                    company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(unit.MaterialCost);
                    break;
            }
        }

        // ---------- Для команд, финансов и интерфейса ----------

        // Уволенный разнорабочий бросает перевозку: груз возвращается туда, откуда взят.
        internal void AbandonTask(Company company, Worker worker)
        {
            var task = worker.Task;
            if (task == null) return;
            worker.Task = null;
            company.Production.PendingKeys.Remove(task.Request.Key);
            if (task.MaterialId != null) company.Inventory.AddMaterial(task.MaterialId, task.Quantity, task.Quality, task.UnitCost);
            var source = company.Floor.FindObject(task.Request.FromObjectId)?.Machine;
            if (source != null) source.Output.AddRange(task.Units);
            else foreach (var unit in task.Units) company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(unit.MaterialCost);
        }

        // Убираемый станок: сырьё возвращается на склад, незавершённые единицы списываются.
        internal void ReleaseMachine(Company company, FloorObject o)
        {
            var m = o.Machine;
            if (m == null) return;
            foreach (var pair in m.Materials.Materials.ToList())
            {
                double quality = m.Materials.MaterialQuality(pair.Key);
                m.Materials.TryTakeMaterial(pair.Key, pair.Value, out _, out double cost);
                company.Inventory.AddMaterial(pair.Key, pair.Value, quality, cost);
            }
            foreach (var unit in m.Input.Concat(m.Output).Concat(m.Current != null ? new[] { m.Current } : new WorkUnit[0]))
                company.CurrentMonth.CostOfGoodsSold += (long)Math.Round(unit.MaterialCost);
            m.Input.Clear();
            m.Output.Clear();
            m.Current = null;
            var state = company.Production;
            state.Queue.RemoveAll(r =>
            {
                bool related = r.FromObjectId == o.Id || r.ToObjectId == o.Id;
                if (related) state.PendingKeys.Remove(r.Key);
                return related;
            });
        }

        // Стоимость незавершённого производства: сырьё у станков, единицы в работе и в пути.
        public long WipValue(Company company)
        {
            double value = 0;
            foreach (var o in company.Machines)
            {
                var m = o.Machine;
                value += m.Materials.TotalValue();
                foreach (var unit in m.Input) value += unit.MaterialCost;
                foreach (var unit in m.Output) value += unit.MaterialCost;
                if (m.Current != null) value += m.Current.MaterialCost;
            }
            foreach (var unit in company.Production.Rework) value += unit.MaterialCost;
            foreach (var w in company.workers)
            {
                if (w.Task == null) continue;
                value += w.Task.Quantity * w.Task.UnitCost;
                foreach (var unit in w.Task.Units) value += unit.MaterialCost;
            }
            return (long)Math.Round(value);
        }

        // Узкое место: станок с самой высокой загрузкой за прошлую неделю (или текущую, если прошлой нет).
        public FloorObject Bottleneck(Company company)
        {
            FloorObject best = null;
            double bestLoad = -1;
            foreach (var o in company.Machines)
            {
                double load = MachineLoad(o);
                if (load > bestLoad) { best = o; bestLoad = load; }
            }
            return bestLoad > 0 ? best : null;
        }

        public static double MachineLoad(FloorObject o)
        {
            var m = o.Machine;
            if (m.LastWeekAvailableMinutes > 0) return m.LastWeekLoad;
            return m.WeekAvailableMinutes > 0 ? (double)m.WeekBusyMinutes / m.WeekAvailableMinutes : 0;
        }

        // Такт: сколько минут рабочего времени месяца приходится на одну единицу плана.
        public double TaktMinutes(Company company)
        {
            int plan = catalog.Products.Sum(p => company.MonthlyPlan(p.Id));
            if (plan <= 0) return 0;
            var schedule = company.Schedule;
            int days = schedule.CountWorkingDays(Simulation.Calendar, Simulation.MonthIndex);
            double minutes = days * (schedule.ShiftsPerDay * schedule.ShiftLengthMinutes + (schedule.ShiftsPerDay < 3 ? company.OvertimeHours * 60 : 0));
            return minutes / plan;
        }
    }
}
