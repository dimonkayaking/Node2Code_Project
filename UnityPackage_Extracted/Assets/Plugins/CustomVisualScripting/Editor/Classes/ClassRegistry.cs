using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>
    /// Хранилище пользовательских классов для текущей сессии.
    /// Классы сохраняются рядом с .methods.json как .classes.json.
    /// </summary>
    public static class ClassRegistry
    {
        private static readonly List<ClassDefinition> _classes = new();

        /// <summary>Вызывается при любом изменении списка классов.</summary>
        public static event Action OnChanged;

        public static IReadOnlyList<ClassDefinition> Classes => _classes;

        // ─── CRUD ─────────────────────────────────────────────────────────────

        public static void Add(ClassDefinition def)
        {
            if (def == null) return;
            _classes.Add(def);
            OnChanged?.Invoke();
        }

        public static void Remove(string id)
        {
            int n = _classes.RemoveAll(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (n > 0) OnChanged?.Invoke();
        }

        public static void Update(ClassDefinition def)
        {
            if (def == null) return;
            int idx = _classes.FindIndex(c => string.Equals(c.Id, def.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                _classes[idx] = def;
                OnChanged?.Invoke();
            }
        }

        /// <summary>Добавляет или обновляет класс по Id.</summary>
        public static void AddOrUpdate(ClassDefinition def)
        {
            if (def == null) return;
            int idx = _classes.FindIndex(c => string.Equals(c.Id, def.Id, StringComparison.Ordinal));
            if (idx >= 0)
                _classes[idx] = def;
            else
                _classes.Add(def);
            OnChanged?.Invoke();
        }

        public static ClassDefinition GetById(string id)
            => _classes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

        public static List<ClassDefinition> GetAll() => new List<ClassDefinition>(_classes);

        // ─── Bulk ─────────────────────────────────────────────────────────────

        /// <summary>Заменяет весь список (при загрузке файла).</summary>
        public static void ReplaceAll(IEnumerable<ClassDefinition> classes)
        {
            _classes.Clear();
            if (classes != null) _classes.AddRange(classes);
            OnChanged?.Invoke();
        }

        public static void Clear()
        {
            _classes.Clear();
            OnChanged?.Invoke();
        }
    }
}
