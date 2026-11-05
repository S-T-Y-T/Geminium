using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Geminium.EditorTools
{
    // Таблицы строк (GDD 11.3). Источник — Assets/Geminium/Localization/strings.csv: «ключ;ru;uz;en;комментарий»,
    // UTF-8, разделитель «;» (так его открывает Excel с русскими настройками). Этот инструмент создаёт настройки
    // Unity Localization, языки uz / ru / en и коллекцию «Geminium» и переносит в неё все строки CSV;
    // ключи, которых в CSV больше нет, удаляются. Правьте тексты в CSV, а не в окне таблиц: CSV перезапишет таблицу.
    public static class LocalizationTools
    {
        public const string Folder = "Assets/Geminium/Localization";
        public const string CsvPath = Folder + "/strings.csv";
        public const string TableName = "Geminium";
        public static readonly string[] Codes = { "uz", "ru", "en" };

        [MenuItem("Geminium/Языки: обновить таблицы из strings.csv")]
        public static void ImportFromMenu()
        {
            var collection = Import();
            Debug.Log($"Geminium: таблица «{TableName}» обновлена: {collection.SharedData.Entries.Count} строк × {Codes.Length} языка.");
        }

        // Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod Geminium.EditorTools.LocalizationTools.ImportFromCommandLine -quit
        public static void ImportFromCommandLine() => ImportFromMenu();

        public static StringTableCollection Import()
        {
            var rows = ReadCsv(File.ReadAllText(CsvPath, Encoding.UTF8));
            EnsureSettings();
            var locales = Codes.Select(EnsureLocale).ToList();

            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName)
                             ?? LocalizationEditorSettings.CreateStringTableCollection(TableName, Folder + "/Tables", locales);
            foreach (var locale in locales)
                if (!collection.ContainsTable(locale.Identifier)) collection.AddNewTable(locale.Identifier);

            var keys = new HashSet<string>();
            foreach (var row in rows)
            {
                keys.Add(row.Key);
                if (collection.SharedData.GetEntry(row.Key) == null) collection.SharedData.AddKey(row.Key);
                foreach (string code in Codes)
                {
                    var table = (StringTable)collection.GetTable(code);
                    string value = row.Values[code];
                    var entry = table.GetEntry(row.Key) ?? table.AddEntry(row.Key, value);
                    entry.Value = value;
                    entry.IsSmart = false;
                    EditorUtility.SetDirty(table);
                }
            }
            foreach (var stale in collection.SharedData.Entries.Where(e => !keys.Contains(e.Key)).Select(e => e.Key).ToList())
                collection.RemoveEntry(stale);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            return collection;
        }

        // Таблицы языков по порядку Codes — для GameApp.Tables.
        public static List<StringTable> Tables()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName) ?? Import();
            return Codes.Select(code => (StringTable)collection.GetTable(code)).ToList();
        }

        private static void EnsureSettings()
        {
            if (LocalizationEditorSettings.ActiveLocalizationSettings != null) return;
            string path = Folder + "/LocalizationSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(path);
            if (settings == null)
            {
                Directory.CreateDirectory(Folder);
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, path);
            }
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        }

        private static Locale EnsureLocale(string code)
        {
            var locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code));
            if (locale != null) return locale;
            Directory.CreateDirectory(Folder + "/Locales");
            string path = $"{Folder}/Locales/{code}.asset";
            locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
            if (locale == null)
            {
                locale = Locale.CreateLocale(new LocaleIdentifier(code));
                AssetDatabase.CreateAsset(locale, path);
            }
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        public sealed class Row
        {
            public string Key;
            public Dictionary<string, string> Values;
            public string Comment;
        }

        // CSV с «;»: поле в кавычках может содержать «;» и удвоенные кавычки. Первая строка — заголовок.
        public static List<Row> ReadCsv(string text)
        {
            var lines = SplitRecords(text.TrimStart('﻿'));
            if (lines.Count == 0) throw new FormatException("strings.csv пуст.");
            var header = lines[0];
            int Column(string name) => header.FindIndex(h => h.Trim() == name);
            int keyColumn = Column("key"), commentColumn = Column("comment");
            var columns = Codes.ToDictionary(c => c, Column);
            if (keyColumn < 0 || columns.Values.Any(i => i < 0)) throw new FormatException("strings.csv: нужны колонки key;ru;uz;en.");
            var rows = new List<Row>();
            var seen = new HashSet<string>();
            for (int n = 1; n < lines.Count; n++)
            {
                var cells = lines[n];
                if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0])) continue;
                string key = cells.ElementAtOrDefault(keyColumn)?.Trim();
                if (string.IsNullOrEmpty(key)) throw new FormatException($"strings.csv, строка {n + 1}: пустой ключ.");
                if (!seen.Add(key)) throw new FormatException($"strings.csv: ключ «{key}» повторяется.");
                rows.Add(new Row
                {
                    Key = key,
                    Values = columns.ToDictionary(c => c.Key, c => cells.ElementAtOrDefault(c.Value) ?? ""),
                    Comment = commentColumn >= 0 ? cells.ElementAtOrDefault(commentColumn) ?? "" : "",
                });
            }
            return rows;
        }

        private static List<List<string>> SplitRecords(string text)
        {
            var records = new List<List<string>>();
            var record = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                if (c == '"' && cell.Length == 0) quoted = true;
                else if (c == ';') { record.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    record.Add(cell.ToString());
                    cell.Clear();
                    records.Add(record);
                    record = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || record.Count > 0)
            {
                record.Add(cell.ToString());
                records.Add(record);
            }
            return records;
        }
    }
}
