using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Geminium.Core.Save
{
    // Маленький JSON для сохранений: объекты (Dictionary<string, object>), массивы (List<object>), строки, числа,
    // true / false / null. Числа читаются как long, если целые, иначе как double. Ядро не зависит от внешних библиотек.
    public static class MiniJson
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case ulong u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (var pair in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default: throw new ArgumentException($"JSON: тип {value.GetType().Name} не поддерживается.");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static object Read(string json)
        {
            int i = 0;
            object value = ReadValue(json, ref i);
            SkipSpace(json, ref i);
            if (i != json.Length) throw new FormatException("JSON: лишние символы после значения.");
            return value;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: неожиданный конец.");
            char c = s[i];
            if (c == '{')
            {
                var map = new Dictionary<string, object>();
                i++;
                SkipSpace(s, ref i);
                if (s[i] == '}') { i++; return map; }
                while (true)
                {
                    SkipSpace(s, ref i);
                    string key = ReadString(s, ref i);
                    SkipSpace(s, ref i);
                    if (s[i++] != ':') throw new FormatException("JSON: нужен «:».");
                    map[key] = ReadValue(s, ref i);
                    SkipSpace(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i++] == '}') return map;
                    throw new FormatException("JSON: нужна «,» или «}».");
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                i++;
                SkipSpace(s, ref i);
                if (s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ReadValue(s, ref i));
                    SkipSpace(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i++] == ']') return list;
                    throw new FormatException("JSON: нужна «,» или «]».");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string number = s.Substring(start, i - start);
            if (number.Length == 0) throw new FormatException($"JSON: непонятный символ «{c}».");
            if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: нужна строка.");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }
}
