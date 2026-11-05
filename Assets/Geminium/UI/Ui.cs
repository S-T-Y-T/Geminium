using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

namespace Geminium.UI
{
    // Кирпичики интерфейса на UI Toolkit. Внешний вид — классы в Styles/Geminium.uss.
    public static class Ui
    {
        public static T With<T>(this T element, params string[] classes) where T : VisualElement
        {
            foreach (string c in classes) element.AddToClassList(c);
            return element;
        }

        public static T AddTo<T>(this T element, VisualElement parent) where T : VisualElement
        {
            parent.Add(element);
            return element;
        }

        public static Label Text(string text, params string[] classes) => new Label(text).With(classes);

        public static Label Title(string text) => Text(text, "title");

        public static Label Hint(string text) => Text(text, "hint");

        public static Button Button(string text, Action onClick, params string[] classes) => new Button(onClick) { text = text }.With(classes);

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement().With("row");
            foreach (var c in children) row.Add(c);
            return row;
        }

        // Блок с заголовком.
        public static VisualElement Section(VisualElement parent, string title)
        {
            var box = new VisualElement().With("section").AddTo(parent);
            box.Add(Text(title, "section-title"));
            return box;
        }

        // Строка «подпись — значение».
        public static VisualElement Pair(VisualElement parent, string label, string value)
        {
            var row = Row(Text(label, "pair-label"), Text(value, "pair-value")).With("pair");
            parent.Add(row);
            return row;
        }

        public static TextField Field(string value, float width = 110)
        {
            var field = new TextField { value = value }.With("field");
            field.style.width = width;
            return field;
        }

        // Целое из поля (пробелы и разделители групп игнорируются); null — не число.
        public static long? ParseLong(string text)
        {
            string s = new string((text ?? "").Where(c => char.IsDigit(c) || c == '-').ToArray());
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : (long?)null;
        }

        public static double? ParseDouble(string text)
        {
            string s = (text ?? "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        public static DropdownField Dropdown(IList<string> choices, int index, Action<int> changed, float width = 160)
        {
            var dropdown = new DropdownField(choices.ToList(), Math.Max(0, Math.Min(index, choices.Count - 1))).With("dropdown");
            dropdown.style.width = width;
            dropdown.RegisterValueChangedCallback(_ => changed(dropdown.index));
            return dropdown;
        }

        public static Toggle Toggle(string label, bool value, Action<bool> changed)
        {
            var toggle = new Toggle(label) { value = value }.With("toggle");
            toggle.RegisterValueChangedCallback(e => changed(e.newValue));
            return toggle;
        }

        // Таблица: заголовки и строки из готовых элементов; ширины колонок — в пикселях.
        public static VisualElement Table(VisualElement parent, float[] widths, string[] headers, IEnumerable<VisualElement[]> rows)
        {
            var table = new VisualElement().With("table").AddTo(parent);
            var head = new VisualElement().With("tr", "th").AddTo(table);
            for (int i = 0; i < headers.Length; i++) Cell(head, Text(headers[i]), widths[i]);
            int n = 0;
            foreach (var cells in rows)
            {
                var tr = new VisualElement().With("tr").AddTo(table);
                if (n++ % 2 == 1) tr.AddToClassList("zebra");
                for (int i = 0; i < cells.Length; i++) Cell(tr, cells[i] ?? Text(""), widths[i]);
            }
            if (n == 0) table.Add(Hint(Loc.Get("ui.empty")));
            return table;
        }

        private static void Cell(VisualElement tr, VisualElement content, float width)
        {
            var td = new VisualElement().With("td").AddTo(tr);
            td.style.width = width;
            td.Add(content);
        }

        // Полоска прогресса 0–1.
        public static VisualElement Bar(double share, string cssClass = "bar-fill")
        {
            var bar = new VisualElement().With("bar");
            var fill = new VisualElement().With(cssClass).AddTo(bar);
            fill.style.width = Length.Percent((float)Math.Max(0, Math.Min(1, share)) * 100);
            return bar;
        }

        // Столбиковая диаграмма значений (может быть отрицательной): подписи снизу.
        public static VisualElement Chart(string title, IList<double> values, IList<string> labels, Func<double, string> format)
        {
            var box = new VisualElement().With("chart");
            box.Add(Text(title, "chart-title"));
            var area = new VisualElement().With("chart-area").AddTo(box);
            double max = values.Count == 0 ? 1 : Math.Max(1e-9, values.Max(v => Math.Abs(v)));
            for (int i = 0; i < values.Count; i++)
            {
                var col = new VisualElement().With("chart-col").AddTo(area);
                col.tooltip = labels[i] + ": " + format(values[i]);
                var bar = new VisualElement().With(values[i] >= 0 ? "chart-bar" : "chart-bar-negative").AddTo(col);
                bar.style.height = Length.Percent((float)(Math.Abs(values[i]) / max * 100));
                col.Add(Text(labels[i], "chart-label"));
            }
            return box;
        }
    }
}
