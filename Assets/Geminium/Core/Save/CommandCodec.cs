using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Geminium.Core.Companies;
using Geminium.Core.Time;

namespace Geminium.Core.Save
{
    // Команды компании ↔ JSON для журнала сохранений. Правило: у команды есть открытый конструктор, а каждому
    // его параметру соответствует открытое свойство с тем же именем (с большой буквы). Тест CommandCodec_RoundTrips
    // проверяет это для всех команд ядра, так что новая команда без такого свойства сразу будет замечена.
    public static class CommandCodec
    {
        private static readonly Dictionary<string, Type> Types = typeof(CompanyCommand).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(CompanyCommand).IsAssignableFrom(t))
            .ToDictionary(t => t.Name);

        public static IReadOnlyCollection<Type> CommandTypes => Types.Values;

        public static Dictionary<string, object> Encode(CompanyCommand command)
        {
            var type = command.GetType();
            var args = new List<object>();
            foreach (var parameter in Constructor(type).GetParameters())
            {
                var property = type.GetProperty(parameter.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                               ?? throw new InvalidOperationException($"{type.Name}: нет свойства для параметра «{parameter.Name}».");
                args.Add(EncodeValue(property.GetValue(command)));
            }
            return new Dictionary<string, object> { ["t"] = type.Name, ["a"] = args };
        }

        public static CompanyCommand Decode(object json)
        {
            var map = (Dictionary<string, object>)json;
            string name = (string)map["t"];
            if (!Types.TryGetValue(name, out var type)) throw new FormatException($"Неизвестная команда «{name}».");
            var ctor = Constructor(type);
            var parameters = ctor.GetParameters();
            var args = (List<object>)map["a"];
            if (args.Count != parameters.Length) throw new FormatException($"{name}: ожидалось {parameters.Length} аргументов.");
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) values[i] = DecodeValue(args[i], parameters[i].ParameterType);
            return (CompanyCommand)ctor.Invoke(values);
        }

        private static ConstructorInfo Constructor(Type type) =>
            type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

        private static object EncodeValue(object value)
        {
            switch (value)
            {
                case null: return null;
                case Enum e: return e.ToString();
                case string or bool or int or long or double: return value;
                case WorkScheduleSettings s:
                    return new Dictionary<string, object>
                    {
                        ["days"] = s.WorkingDaysPerWeek, ["shifts"] = s.ShiftsPerDay,
                        ["length"] = s.ShiftLengthMinutes, ["start"] = s.FirstShiftStartMinute,
                    };
                case IEnumerable<CompanyCommand> commands: return commands.Select(c => (object)Encode(c)).ToList();
                default: throw new InvalidOperationException($"Значение типа {value.GetType().Name} нельзя сохранить.");
            }
        }

        private static object DecodeValue(object value, Type type)
        {
            if (value == null) return null;
            if (type.IsEnum) return Enum.Parse(type, (string)value);
            if (type == typeof(string) || type == typeof(bool)) return value;
            if (type == typeof(int)) return Convert.ToInt32(value);
            if (type == typeof(long)) return Convert.ToInt64(value);
            if (type == typeof(double)) return Convert.ToDouble(value);
            if (type == typeof(WorkScheduleSettings))
            {
                var map = (Dictionary<string, object>)value;
                return new WorkScheduleSettings
                {
                    WorkingDaysPerWeek = Convert.ToInt32(map["days"]), ShiftsPerDay = Convert.ToInt32(map["shifts"]),
                    ShiftLengthMinutes = Convert.ToInt32(map["length"]), FirstShiftStartMinute = Convert.ToInt32(map["start"]),
                };
            }
            if (typeof(IEnumerable<CompanyCommand>).IsAssignableFrom(type))
                return ((List<object>)value).Select(Decode).ToList();
            throw new FormatException($"Тип параметра {type.Name} не поддерживается.");
        }
    }
}
