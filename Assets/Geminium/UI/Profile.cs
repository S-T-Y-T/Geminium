using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Geminium.Core.Data;
using Geminium.Core.Save;

namespace Geminium.UI
{
    // Профиль игрока на этом компьютере (persistentDataPath/profile.json): язык и лучшие баллы заданий кампании.
    // Задание открывается, когда пройдено предыдущее; свободный режим — после всей кампании (GDD 4.1).
    public sealed class Profile
    {
        public string Language = "";
        public readonly Dictionary<string, double> BestScores = new Dictionary<string, double>();
        public readonly HashSet<string> Passed = new HashSet<string>();
        public bool UnlockAll;   // в редакторе Unity открыто всё — для разработки и проверки

        private string path;

        public static Profile Load(string path)
        {
            var profile = new Profile { path = path };
            try
            {
                if (!File.Exists(path)) return profile;
                var map = (Dictionary<string, object>)MiniJson.Read(File.ReadAllText(path, Encoding.UTF8));
                profile.Language = map.TryGetValue("language", out var l) ? (string)l : "";
                if (map.TryGetValue("best", out var best))
                    foreach (var pair in (Dictionary<string, object>)best) profile.BestScores[pair.Key] = Convert.ToDouble(pair.Value);
                if (map.TryGetValue("passed", out var passed))
                    foreach (var id in (List<object>)passed) profile.Passed.Add((string)id);
            }
            catch (Exception)
            {
                // Испорченный профиль не мешает игре: начинаем с чистого.
            }
            return profile;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var map = new Dictionary<string, object>
            {
                ["language"] = Language,
                ["best"] = BestScores.ToDictionary(p => p.Key, p => (object)p.Value),
                ["passed"] = Passed.OrderBy(p => p, StringComparer.Ordinal).Select(p => (object)p).ToList(),
            };
            File.WriteAllText(path, MiniJson.Write(map), new UTF8Encoding(false));
        }

        public bool IsMissionOpen(DataCatalog catalog, string missionId)
        {
            int index = catalog.Missions.ToList().FindIndex(m => m.Id == missionId);
            return UnlockAll || index == 0 || (index > 0 && Passed.Contains(catalog.Missions[index - 1].Id));
        }

        public bool IsFreeModeOpen(DataCatalog catalog) => UnlockAll || catalog.Missions.All(m => Passed.Contains(m.Id));

        public void RecordMission(string missionId, double score, bool passed)
        {
            if (!BestScores.TryGetValue(missionId, out double best) || score > best) BestScores[missionId] = score;
            if (passed) Passed.Add(missionId);
            Save();
        }
    }
}
