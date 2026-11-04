using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Geminium.Core.Modes;
using Geminium.Core.Save;

namespace Geminium.Game
{
    // Файлы сохранений (GDD 4.2): автосохранение и 3 ручных слота в папке saves внутри Application.persistentDataPath
    // (в Windows — %USERPROFILE%\AppData\LocalLow\<компания>\<продукт>\saves). Один слот — один файл JSON.
    // Запись атомарная: сначала временный файл, потом замена — сбой посреди записи не портит прежнее сохранение.
    public sealed class SaveStorage
    {
        public const string AutosaveSlot = "autosave";
        public const int ManualSlots = 3;

        public string Directory { get; }

        public SaveStorage(string directory) { Directory = directory ?? throw new ArgumentNullException(nameof(directory)); }

        public static SaveStorage Default => new SaveStorage(Path.Combine(Application.persistentDataPath, "saves"));

        public static string ManualSlot(int number) =>
            number >= 1 && number <= ManualSlots ? "slot" + number : throw new ArgumentOutOfRangeException(nameof(number));

        // Все слоты по порядку: автосохранение, slot1–slot3.
        public static IEnumerable<string> Slots()
        {
            yield return AutosaveSlot;
            for (int i = 1; i <= ManualSlots; i++) yield return ManualSlot(i);
        }

        public string PathOf(string slot) => Path.Combine(Directory, slot + ".json");

        public void Write(string slot, SaveGame save)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathOf(slot), temp = path + ".tmp";
            File.WriteAllText(temp, save.ToJson(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        // null — слот пуст. Повреждённый файл — SaveException.
        public SaveGame Read(string slot)
        {
            string path = PathOf(slot);
            return File.Exists(path) ? SaveGame.FromJson(File.ReadAllText(path, Encoding.UTF8)) : null;
        }

        public void Delete(string slot)
        {
            string path = PathOf(slot);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // Автосохранение в конце каждого месяца партии. Подписывается на конец месяца симуляции; Dispose — отписка.
    public sealed class Autosave : IDisposable
    {
        private readonly GameSession session;
        private readonly SaveStorage storage;

        public Autosave(GameSession session, SaveStorage storage)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            session.World.Simulation.MonthEnded += OnMonthEnded;
        }

        private void OnMonthEnded(int monthIndex) =>
            storage.Write(SaveStorage.AutosaveSlot, SaveGame.Capture(session, SaveStorage.AutosaveSlot, DateTime.Now.ToString("yyyy-MM-dd HH:mm")));

        public void Dispose() => session.World.Simulation.MonthEnded -= OnMonthEnded;
    }
}
