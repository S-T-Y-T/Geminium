using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Geminium.Core.Data;
using Geminium.Core.Lab;
using Geminium.Core.Modes;
using Geminium.Core.Time;

namespace Geminium.Core.Save
{
    public sealed class SaveException : Exception
    {
        public SaveException(string message) : base(message) { }
    }

    // Сохранение партии (GDD 4.2): с чего началась партия + журнал принятых команд игрока + минута сохранения.
    // Загрузка — та же партия заново с повтором журнала: симуляция детерминирована, поэтому мир получается
    // тем же до последнего числа. Файл маленький и не зависит от внутреннего устройства подсистем.
    // Отпечаток данных показывает, что баланс изменился после сохранения: тогда повтор может разойтись.
    public sealed class SaveGame
    {
        public const string Format = "geminium-save";
        public const int FormatVersion = 1;

        public int Version = FormatVersion;
        public GameSetup Setup;
        public long Minute;
        public string DataHash;
        public List<LoggedCommand> Commands = new List<LoggedCommand>();

        // Для списка слотов: подпись, реальное время сохранения (ставит интерфейс), месяц и капитализация игрока.
        public string Label = "";
        public string SavedAt = "";
        public int MonthIndex;
        public long PlayerCapitalization;

        public static SaveGame Capture(GameSession session, string label = "", string savedAt = "")
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var world = session.World;
            return new SaveGame
            {
                Setup = session.Setup,
                Minute = world.Simulation.Now.Minute,
                DataHash = DataFingerprint.Of(world.Catalog),
                Commands = world.CommandLog.ToList(),
                Label = label ?? "",
                SavedAt = savedAt ?? "",
                MonthIndex = world.Simulation.MonthIndex,
                PlayerCapitalization = session.Player.LastReport?.Capitalization ?? session.Player.Cash,
            };
        }

        public bool DataMatches(DataCatalog catalog) => DataHash == DataFingerprint.Of(catalog);

        // Собрать партию заново и повторить журнал. Команда, которую мир не принял, — признак расхождения.
        // Повтор идёт со скоростью прогона без графики (36 месяцев — десятки секунд), progress получает долю 0–1 раз в игровой день.
        public GameSession Restore(DataCatalog catalog, IBotPolicy neuralPolicy = null, Action<double> progress = null)
        {
            if (Version != FormatVersion) throw new SaveException($"Сохранение версии {Version}, а игра читает версию {FormatVersion}.");
            var session = GameSession.Start(catalog, Setup, neuralPolicy);
            var world = session.World;
            foreach (var entry in Commands)
            {
                if (entry.Minute < world.Simulation.Now.Minute) throw new SaveException("Журнал повреждён: время команд идёт назад.");
                RunTo(world.Simulation, entry.Minute, progress);
                var company = world.FindCompany(entry.CompanyId) ?? throw new SaveException($"В партии нет компании «{entry.CompanyId}».");
                string error = world.Submit(company, entry.Command);
                if (error != null) throw new SaveException($"Повтор журнала разошёлся с партией (минута {entry.Minute}): {error}");
            }
            if (Minute < world.Simulation.Now.Minute) throw new SaveException("Журнал повреждён: команда позже минуты сохранения.");
            RunTo(world.Simulation, Minute, progress);
            progress?.Invoke(1);
            return session;
        }

        private void RunTo(Simulation simulation, long minute, Action<double> progress)
        {
            while (simulation.Now.Minute < minute)
            {
                simulation.Step();
                if (progress != null && simulation.Now.IsStartOfDay) progress(Minute > 0 ? (double)simulation.Now.Minute / Minute : 1);
            }
        }

        // ---------- JSON ----------

        public string ToJson()
        {
            var setup = new Dictionary<string, object>
            {
                ["mode"] = Setup.Mode.ToString(),
                ["seed"] = Setup.Seed.ToString(CultureInfo.InvariantCulture), // строкой: ulong не влезает в long
                ["mission"] = Setup.MissionId ?? "",
                ["rivals"] = Setup.Rivals.ToString(),
            };
            var commands = Commands.Select(c => (object)new Dictionary<string, object>
            {
                ["m"] = c.Minute, ["c"] = c.CompanyId, ["cmd"] = CommandCodec.Encode(c.Command),
            }).ToList();
            return MiniJson.Write(new Dictionary<string, object>
            {
                ["format"] = Format, ["version"] = Version, ["setup"] = setup, ["minute"] = Minute, ["dataHash"] = DataHash ?? "",
                ["label"] = Label ?? "", ["savedAt"] = SavedAt ?? "", ["month"] = MonthIndex, ["capitalization"] = PlayerCapitalization,
                ["commands"] = commands,
            });
        }

        public static SaveGame FromJson(string json)
        {
            try
            {
                var map = (Dictionary<string, object>)MiniJson.Read(json);
                if (!(map.TryGetValue("format", out var format) && Format.Equals(format))) throw new SaveException("Это не файл сохранения Geminium.");
                var setup = (Dictionary<string, object>)map["setup"];
                var save = new SaveGame
                {
                    Version = Convert.ToInt32(map["version"]),
                    Setup = new GameSetup
                    {
                        Mode = (GameMode)Enum.Parse(typeof(GameMode), (string)setup["mode"]),
                        Seed = ulong.Parse((string)setup["seed"], CultureInfo.InvariantCulture),
                        MissionId = (string)setup["mission"],
                        Rivals = (RivalBrain)Enum.Parse(typeof(RivalBrain), (string)setup["rivals"]),
                    },
                    Minute = Convert.ToInt64(map["minute"]),
                    DataHash = (string)map["dataHash"],
                    Label = (string)map["label"],
                    SavedAt = (string)map["savedAt"],
                    MonthIndex = Convert.ToInt32(map["month"]),
                    PlayerCapitalization = Convert.ToInt64(map["capitalization"]),
                };
                if (save.Version != FormatVersion) return save; // команды другой версии не читаем — Restore объяснит
                foreach (Dictionary<string, object> c in (List<object>)map["commands"])
                    save.Commands.Add(new LoggedCommand { Minute = Convert.ToInt64(c["m"]), CompanyId = (string)c["c"], Command = CommandCodec.Decode(c["cmd"]) });
                return save;
            }
            catch (Exception e) when (!(e is SaveException))
            {
                throw new SaveException("Файл сохранения повреждён: " + e.Message);
            }
        }
    }

    // Отпечаток данных игры: FNV-1a по всем открытым полям GameData (в порядке имён).
    public static class DataFingerprint
    {
        public static string Of(DataCatalog catalog) => catalog.Fingerprint;

        public static string Of(GameData data)
        {
            var text = new StringBuilder();
            Append(text, data);
            ulong hash = 14695981039346656037UL;
            foreach (char ch in text.ToString())
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private static void Append(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append('~'); return;
                case string s: sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"'); return;
                case bool b: sb.Append(b ? 'T' : 'F'); return;
                case Enum e: sb.Append(Convert.ToInt64(e, CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case IFormattable n when value.GetType().IsPrimitive: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); return;
                case IEnumerable items:
                    sb.Append('[');
                    foreach (var item in items)
                    {
                        Append(sb, item);
                        sb.Append(',');
                    }
                    sb.Append(']');
                    return;
            }
            sb.Append('{');
            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                sb.Append(field.Name).Append('=');
                Append(sb, field.GetValue(value));
                sb.Append(';');
            }
            sb.Append('}');
        }
    }
}
