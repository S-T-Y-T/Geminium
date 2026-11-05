using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.Localization.Tables;
using Geminium.Core;

namespace Geminium.UI
{
    // Тексты интерфейса (GDD 11.3): таблица «Geminium» пакета Unity Localization, языки uz / ru / en.
    // Источник строк — Assets/Geminium/Localization/strings.csv; меню Geminium → «Языки: обновить таблицы»
    // переносит его в таблицы пакета. Таблицы каждого языка GameApp получает ссылками (они попадают в сборку
    // вместе со сценой), поэтому тексты доступны сразу, без асинхронной загрузки.
    // Шаблоны — string.Format: {0}, {0:N0}; числа и даты форматируются по культуре языка.
    // Аргумент-строка, для которой есть ключ name.<значение>, подставляется переведённым названием (dobok → «добок»).
    public static class Loc
    {
        public const string TableName = "Geminium";
        public static readonly string[] Languages = { "uz", "ru", "en" };

        private static readonly Dictionary<string, StringTable> tables = new Dictionary<string, StringTable>();
        private static StringTable current;

        public static string Language { get; private set; } = "ru";
        public static CultureInfo Culture { get; private set; } = CultureFor("ru");
        public static event Action Changed;

        public static void Register(IEnumerable<StringTable> list)
        {
            foreach (var table in list.Where(t => t != null)) tables[table.LocaleIdentifier.Code] = table;
        }

        public static bool HasLanguage(string code) => tables.ContainsKey(code);

        // Сменить язык. Ядро тоже начинает говорить на нём: CoreText берёт переводы ошибок команд отсюда.
        public static void SetLanguage(string code)
        {
            if (!tables.TryGetValue(code, out var table)) throw new ArgumentException($"Нет таблицы строк для языка «{code}».", nameof(code));
            current = table;
            Language = code;
            Culture = CultureFor(code);
            CoreText.Translator = Translate;
            Changed?.Invoke();
        }

        // Отключить переводы (тесты): ядро снова говорит русскими шаблонами.
        public static void Reset()
        {
            current = null;
            tables.Clear();
            Language = "ru";
            Culture = CultureFor("ru");
            CoreText.Translator = null;
        }

        public static bool Has(string key) => !string.IsNullOrEmpty(current?.GetEntry(key)?.Value);

        public static string Get(string key, params object[] args)
        {
            string template = current?.GetEntry(key)?.Value;
            if (string.IsNullOrEmpty(template)) return args.Length == 0 ? key : key + ": " + string.Join(", ", args);
            if (args.Length == 0) return template;
            return string.Format(Culture, template, args.Select(LocalizeArgument).ToArray());
        }

        // Название сущности из данных по Id: продукт, сырьё, станок, профессия, событие, спортсмен, компания.
        public static string Name(string id) => string.IsNullOrEmpty(id) ? "" : Has("name." + id) ? Get("name." + id) : id;

        // Значение перечисления: ключ enum.<Тип>.<Значение>.
        public static string Enum(System.Enum value) => Get($"enum.{value.GetType().Name}.{value}");

        private static object LocalizeArgument(object arg) => arg is string s && Has("name." + s) ? Get("name." + s) : arg;

        private static string Translate(string key, object[] args) => Has(key) ? Get(key, args) : null;

        private static CultureInfo CultureFor(string code)
        {
            string name = code == "uz" ? "uz-Latn-UZ" : code == "en" ? "en-US" : "ru-RU";
            try { return CultureInfo.GetCultureInfo(name); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }
}
