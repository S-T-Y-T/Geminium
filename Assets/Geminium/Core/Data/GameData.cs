using System;
using System.Collections.Generic;
using Geminium.Core.Time;

namespace Geminium.Core.Data
{
    // Все данные игры одним набором. В Unity хранится в файле GameDatabase.asset,
    // в тестах и прогонах без графики может собираться прямо в коде.
    [Serializable]
    public sealed class GameData
    {
        public List<MaterialDef> Materials = new List<MaterialDef>();
        public List<MachineDef> Machines = new List<MachineDef>();
        public List<ProductDef> Products = new List<ProductDef>();
        public List<ProfessionDef> Professions = new List<ProfessionDef>();

        public CalendarSettings Calendar = new CalendarSettings();
        public WorkScheduleSettings DefaultWorkSchedule = new WorkScheduleSettings();
        public TimePacingSettings Pacing = new TimePacingSettings();
    }
}
