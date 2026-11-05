using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using Geminium.Core;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Modes;
using Geminium.EditorTools;
using Geminium.Game;
using Geminium.UI;

namespace Geminium.Tests.EditMode
{
    // Языки (GDD 11.3) — этап 4: таблица строк, новый узбекский алфавит, ключи из кода, перевод ошибок ядра.
    public class LocalizationTests
    {
        private static List<LocalizationTools.Row> Rows() =>
            LocalizationTools.ReadCsv(File.ReadAllText(LocalizationTools.CsvPath, Encoding.UTF8));

        private static DataCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Geminium/Data/GameDatabase.asset").CreateCatalog();

        [TearDown]
        public void ResetLanguage() => Loc.Reset();

        // Каждая строка переведена на три языка, подстановки {0}… одинаковые во всех языках.
        [Test]
        public void Csv_EveryRowHasThreeLanguagesAndSamePlaceholders()
        {
            var rows = Rows();
            Assert.Greater(rows.Count, 400);
            var placeholder = new Regex(@"\{(\d+)(:[^}]*)?\}");
            foreach (var row in rows)
            {
                foreach (string code in LocalizationTools.Codes)
                    Assert.IsFalse(string.IsNullOrWhiteSpace(row.Values[code]), $"{row.Key}: нет текста на «{code}»");
                var expected = placeholder.Matches(row.Values["ru"]).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                foreach (string code in new[] { "uz", "en" })
                {
                    var actual = placeholder.Matches(row.Values[code]).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                    CollectionAssert.AreEqual(expected, actual, $"{row.Key}: подстановки в «{code}» не совпадают с русскими");
                }
            }
        }

        // Узбекский — на алфавите 2026 года: Ş, Ç, Ö, Ğ вместо sh, ch, oʻ, gʻ.
        [Test]
        public void Csv_UzbekUsesNewAlphabet()
        {
            var old = new Regex("sh|ch|[oOgG][ʻ'‘`]", RegexOptions.IgnoreCase);
            foreach (var row in Rows().Where(r => !r.Key.StartsWith("language.")))
            {
                string text = Regex.Replace(row.Values["uz"], @"\{[^}]*\}", "");
                Assert.IsFalse(old.IsMatch(text), $"{row.Key}: старое написание в «{row.Values["uz"]}»");
            }
        }

        // Все ключи, которые запрашивает код и данные, есть в таблице.
        [Test]
        public void Csv_HasEveryKeyTheGameAsksFor()
        {
            var keys = new HashSet<string>(Rows().Select(r => r.Key));
            var needed = new HashSet<string>();
            var literal = new Regex("\"([a-z_]+\\.[a-zA-Z0-9_.]*[a-zA-Z0-9_])\"");
            foreach (string file in Directory.GetFiles("Assets/Geminium/UI", "*.cs", SearchOption.AllDirectories))
                foreach (Match m in literal.Matches(File.ReadAllText(file)))
                    if (!m.Groups[1].Value.EndsWith(".csv") && !m.Groups[1].Value.EndsWith(".json") && m.Groups[1].Value != "news.bot_price")
                        needed.Add(m.Groups[1].Value);
            foreach (Match m in new Regex("\"(reason\\.[a-z_]+)\"").Matches(File.ReadAllText("Assets/Geminium/Core/News/NewsSystem.cs")))
                needed.Add(m.Groups[1].Value);
            foreach (var (key, _) in CoreTemplates()) needed.Add(key);

            var catalog = Catalog();
            foreach (var m in catalog.Missions)
                foreach (string part in new[] { "title", "short", "brief", "tip" }) needed.Add($"mission.{m.Id}.{part}");
            foreach (GoalMetric metric in Enum.GetValues(typeof(GoalMetric))) needed.Add("goal." + metric);
            foreach (var s in catalog.Scenarios) needed.Add("scenario." + s.Id);
            foreach (var d in catalog.DigitalModules) needed.Add($"digital.{d.Module}.effect");
            foreach (var e in catalog.Events) needed.Add("news.event." + e.Id);
            foreach (string id in catalog.Products.Select(p => p.Id).Concat(catalog.Materials.Select(m => m.Id)).Concat(catalog.Machines.Select(m => m.Id))
                         .Concat(catalog.Professions.Select(p => p.Id)).Concat(catalog.Suppliers.Select(s => s.Id)).Concat(catalog.Athletes.Select(a => a.Id))
                         .Concat(catalog.Events.Select(e => e.Id)).Concat(new[] { "player", "daebo", "abidas" }))
                needed.Add("name." + id);
            foreach (var type in new[] { typeof(Geminium.Game.FloorTool), typeof(Geminium.Core.Factory.ZoneType), typeof(Geminium.Core.Factory.Facing),
                         typeof(Geminium.Core.Factory.FloorObjectKind), typeof(Geminium.Core.Factory.FeedSide), typeof(Geminium.Core.Factory.DefectPolicy),
                         typeof(DeliveryFrequency), typeof(MaterialOrigin), typeof(TenderLevel), typeof(Geminium.Core.Sales.ContractStatus),
                         typeof(TournamentLevel), typeof(CertificationBody), typeof(DigitalModule) })
                foreach (var value in Enum.GetValues(type)) needed.Add($"enum.{type.Name}.{value}");

            var missing = needed.Where(k => !k.EndsWith(".") && !keys.Contains(k)).OrderBy(k => k).ToList();
            CollectionAssert.IsEmpty(missing, "нет в strings.csv: " + string.Join(", ", missing));
        }

        // Русские шаблоны ошибок в ядре совпадают с русской колонкой таблицы.
        [Test]
        public void CoreText_RussianTemplatesMatchTable()
        {
            var rows = Rows().ToDictionary(r => r.Key);
            var templates = CoreTemplates().ToList();
            Assert.Greater(templates.Count, 40);
            foreach (var (key, russian) in templates)
            {
                Assert.IsTrue(rows.ContainsKey(key), $"нет ключа {key}");
                Assert.AreEqual(russian, rows[key].Values["ru"], key);
            }
        }

        private static IEnumerable<(string key, string russian)> CoreTemplates()
        {
            var call = new Regex("CoreText\\.T\\(\"([a-z_.]+)\", \"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (string file in Directory.GetFiles("Assets/Geminium/Core", "*.cs", SearchOption.AllDirectories))
                foreach (Match m in call.Matches(File.ReadAllText(file)))
                    yield return (m.Groups[1].Value, m.Groups[2].Value);
        }

        // Таблицы Unity Localization совпадают с CSV: после правки CSV нужно меню «Языки: обновить таблицы».
        [Test]
        public void Tables_AreImportedFromCsv()
        {
            var tables = LocalizationTools.Tables();
            Assert.AreEqual(3, tables.Count);
            foreach (var row in Rows())
                for (int i = 0; i < LocalizationTools.Codes.Length; i++)
                    Assert.AreEqual(row.Values[LocalizationTools.Codes[i]], tables[i].GetEntry(row.Key)?.Value, $"{row.Key} ({LocalizationTools.Codes[i]}): таблица не обновлена из CSV");
        }

        // Ошибки ядра говорят на языке игрока; названия из данных подставляются переведёнными.
        [Test]
        public void Loc_TranslatesCoreErrorsAndNames()
        {
            Loc.Register(LocalizationTools.Tables());
            var session = GameSession.Start(Catalog(), GameSetup.Campaign("m1_first_shop", 1));

            Loc.SetLanguage("en");
            Assert.AreEqual("This unlocks in later campaign missions.", session.World.Submit(session.Player, new TakeLoanCommand(1000, 3)));
            Assert.AreEqual("The product “helmet” isn't unlocked yet.", session.World.Submit(session.Player, new SetMonthlyPlanCommand("helmet", 5)));
            Loc.SetLanguage("uz");
            Assert.AreEqual("«dubulğa» mahsuloti hali oçilmagan.", session.World.Submit(session.Player, new SetMonthlyPlanCommand("helmet", 5)));
            Loc.SetLanguage("ru");
            Assert.AreEqual("Продукт «шлем» ещё не открыт.", session.World.Submit(session.Player, new SetMonthlyPlanCommand("helmet", 5)));
            Assert.AreEqual("no.such.key", Loc.Get("no.such.key"), "нет перевода — виден ключ");

            Loc.Reset();
            Assert.AreEqual("Продукт «helmet» ещё не открыт.", session.World.Submit(session.Player, new SetMonthlyPlanCommand("helmet", 5)), "без перевода — русский шаблон с Id");
        }
    }
}
