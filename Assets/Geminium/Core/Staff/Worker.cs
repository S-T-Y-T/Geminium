using Geminium.Core.Factory;

namespace Geminium.Core.Staff
{
    // Сотрудник (GDD 7.7): опыт, усталость, мораль, зарплата и скрытые черты.
    public sealed class Worker
    {
        public int Id { get; }
        public string ProfessionId { get; }
        public double Experience { get; internal set; }    // 1–100
        public double Fatigue { get; internal set; }       // 0–100
        public double Morale { get; internal set; }        // 0–100
        public long MonthlyWage { get; internal set; }
        public double Stamina { get; }                     // скрытая черта: множитель роста усталости
        public double Loyalty { get; }                     // скрытая черта: меньше теряет мораль
        public int ShiftIndex { get; internal set; }       // в какой смене работает (0, 1, 2)
        public int AssignedObjectId { get; internal set; } = -1; // станок или стол; −1 — не закреплён
        public bool AbsentToday { get; internal set; }
        public int HiredMonth { get; }

        // Разнорабочий: где стоит и что везёт.
        public int PositionCell { get; internal set; }
        public TransportTask Task { get; internal set; }

        internal int OvertimeDaysThisMonth;
        internal int BreakdownsThisMonth;
        internal double OvertimeHoursThisMonth;
        internal bool WorkedOvertimeToday;

        internal Worker(int id, string professionId, double experience, long wage, double stamina, double loyalty, double morale, int hiredMonth)
        {
            Id = id;
            ProfessionId = professionId;
            Experience = experience;
            MonthlyWage = wage;
            Stamina = stamina;
            Loyalty = loyalty;
            Morale = morale;
            HiredMonth = hiredMonth;
        }
    }

    // Кандидат с рынка труда. Живёт до конца месяца, если его не наняли.
    public sealed class Candidate
    {
        public int Id { get; }
        public string ProfessionId { get; }
        public double Experience { get; }
        public long ExpectedWage { get; }
        public double Stamina { get; }
        public double Loyalty { get; }

        internal Candidate(int id, string professionId, double experience, long expectedWage, double stamina, double loyalty)
        {
            Id = id;
            ProfessionId = professionId;
            Experience = experience;
            ExpectedWage = expectedWage;
            Stamina = stamina;
            Loyalty = loyalty;
        }
    }
}
