using System.Linq;
using Geminium.Core.Data;
using Geminium.Core.Factory;
using Geminium.Core.Staff;

namespace Geminium.Core.Companies
{
    // Команды персонала и политик цеха (GDD 7.6.6, 7.7).

    public sealed class HireCommand : CompanyCommand
    {
        public int CandidateId { get; }

        public HireCommand(int candidateId) { CandidateId = candidateId; }

        public override string Validate(Company company, GameWorld world)
        {
            if (!world.Staff.Candidates.Any(c => c.Id == CandidateId)) return $"Кандидата №{CandidateId} уже нет на рынке.";
            return world.Staff.IsReservedByOther(CandidateId, this) ? $"Кандидата №{CandidateId} уже нанимает другая компания." : null;
        }

        internal override void OnAccepted(Company company, GameWorld world) => world.Staff.Reserve(CandidateId, this);

        internal override void Apply(Company company, GameWorld world) =>
            world.Staff.Hire(company, world.Staff.Candidates.First(c => c.Id == CandidateId));
    }

    public sealed class FireCommand : CompanyCommand
    {
        public int WorkerId { get; }

        public FireCommand(int workerId) { WorkerId = workerId; }

        public override string Validate(Company company, GameWorld world) => CheckWorker(company, WorkerId);

        internal override void Apply(Company company, GameWorld world) => world.Staff.Fire(company, company.FindWorker(WorkerId));

        internal static string CheckWorker(Company company, int workerId) =>
            company.FindWorker(workerId) == null ? $"Нет сотрудника №{workerId}." : null;
    }

    // Закрепить сотрудника за станком (или снять: ObjectId = −1) и поставить в смену.
    public sealed class AssignWorkerCommand : CompanyCommand
    {
        public int WorkerId { get; }
        public int ObjectId { get; }
        public int Shift { get; }

        public AssignWorkerCommand(int workerId, int objectId, int shift)
        {
            WorkerId = workerId;
            ObjectId = objectId;
            Shift = shift;
        }

        public override string Validate(Company company, GameWorld world)
        {
            string error = FireCommand.CheckWorker(company, WorkerId);
            if (error != null) return error;
            if (Shift < 0 || Shift > 2) return "Смена — 0, 1 или 2.";
            if (ObjectId < 0) return null;
            if (company.Floor.FindObject(ObjectId)?.Kind != FloorObjectKind.Machine) return $"Нет станка №{ObjectId}.";
            var profession = world.Catalog.Profession(company.FindWorker(WorkerId).ProfessionId);
            return profession.OperatedZone == ProductionZone.None ? "Эта профессия не работает на станках." : null;
        }

        internal override void Apply(Company company, GameWorld world)
        {
            var worker = company.FindWorker(WorkerId);
            worker.AssignedObjectId = ObjectId;
            worker.ShiftIndex = Shift;
        }
    }

    public sealed class SetWageCommand : CompanyCommand
    {
        public int WorkerId { get; }
        public long Wage { get; }

        public SetWageCommand(int workerId, long wage)
        {
            WorkerId = workerId;
            Wage = wage;
        }

        public override string Validate(Company company, GameWorld world) =>
            FireCommand.CheckWorker(company, WorkerId) ?? (Wage <= 0 ? "Зарплата должна быть больше нуля." : null);

        internal override void Apply(Company company, GameWorld world) => world.Staff.SetWage(company.FindWorker(WorkerId), Wage);
    }

    public sealed class TrainWorkerCommand : CompanyCommand
    {
        public int WorkerId { get; }

        public TrainWorkerCommand(int workerId) { WorkerId = workerId; }

        public override string Validate(Company company, GameWorld world) =>
            FireCommand.CheckWorker(company, WorkerId) ?? CheckCash(company, world.Catalog.Staff.TrainingCost);

        internal override void Apply(Company company, GameWorld world) => world.Staff.Train(company, company.FindWorker(WorkerId));
    }

    // Переработки: сколько часов после последней смены (только при 1–2 сменах).
    public sealed class SetOvertimeCommand : CompanyCommand
    {
        public int Hours { get; }

        public SetOvertimeCommand(int hours) { Hours = hours; }

        public override string Validate(Company company, GameWorld world) =>
            Hours < 0 || Hours > world.Catalog.Staff.MaxOvertimeHours ? $"Переработки — от 0 до {world.Catalog.Staff.MaxOvertimeHours} часов." : null;

        internal override void Apply(Company company, GameWorld world) => company.OvertimeHours = Hours;
    }

    public sealed class SetDefectPolicyCommand : CompanyCommand
    {
        public DefectPolicy Policy { get; }

        public SetDefectPolicyCommand(DefectPolicy policy) { Policy = policy; }

        public override string Validate(Company company, GameWorld world) => null;

        internal override void Apply(Company company, GameWorld world) => company.DefectPolicy = Policy;
    }

    // Входной контроль сырья: нужен стол ОТК и контролёр.
    public sealed class SetInputQcCommand : CompanyCommand
    {
        public bool Enabled { get; }

        public SetInputQcCommand(bool enabled) { Enabled = enabled; }

        public override string Validate(Company company, GameWorld world) => null;

        internal override void Apply(Company company, GameWorld world) => company.InputQcEnabled = Enabled;
    }

    public sealed class SetTransportBatchCommand : CompanyCommand
    {
        public int Units { get; }

        public SetTransportBatchCommand(int units) { Units = units; }

        public override string Validate(Company company, GameWorld world) =>
            Units < 1 || Units > world.Catalog.Production.MaxTransportBatch
                ? $"Транспортная партия — от 1 до {world.Catalog.Production.MaxTransportBatch}."
                : null;

        internal override void Apply(Company company, GameWorld world) => company.TransportBatch = Units;
    }

    // Регламент ТО станка: через сколько операций обслуживать.
    public sealed class SetMaintenanceIntervalCommand : CompanyCommand
    {
        public int ObjectId { get; }
        public int Operations { get; }

        public SetMaintenanceIntervalCommand(int objectId, int operations)
        {
            ObjectId = objectId;
            Operations = operations;
        }

        public override string Validate(Company company, GameWorld world)
        {
            if (company.Floor.FindObject(ObjectId)?.Kind != FloorObjectKind.Machine) return $"Нет станка №{ObjectId}.";
            return Operations < 1 ? "Интервал ТО — не меньше одной операции." : null;
        }

        internal override void Apply(Company company, GameWorld world) =>
            company.Floor.FindObject(ObjectId).Machine.MaintenanceInterval = Operations;
    }
}
