using System;
using System.Globalization;

namespace Geminium.Core
{
    // Тексты ядра для игрока (ошибки команд). Ядро не знает языков: каждый текст — ключ таблицы локализации
    // и русский шаблон по умолчанию в формате string.Format ({0}, {0:N0}). Слой Unity ставит Translator,
    // который берёт перевод по ключу; без него (тесты, Лаборатория) — русский шаблон.
    // Тест LocalizationTests проверяет, что каждый ключ есть в таблице и русский текст в ней совпадает с шаблоном.
    public static class CoreText
    {
        // (ключ, аргументы) → текст или null, если перевода нет.
        public static Func<string, object[], string> Translator;

        public static string T(string key, string russian, params object[] args) =>
            Translator?.Invoke(key, args) ?? string.Format(CultureInfo.GetCultureInfo("ru-RU"), russian, args);
    }
}
