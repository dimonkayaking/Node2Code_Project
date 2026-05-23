using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomVisualScripting.Editor.Methods
{
    /// <summary>
    /// Хранилище пользовательских методов для текущей сессии.
    /// Методы привязаны к файлу: сохраняются рядом с .cs как .methods.json.
    /// </summary>
    public static class MethodRegistry
    {
        private static readonly List<MethodDefinition> _methods = new();

        /// <summary>Вызывается при любом изменении списка методов (добавление / удаление / обновление).</summary>
        public static event Action OnChanged;

        public static IReadOnlyList<MethodDefinition> Methods => _methods;

        // ─── CRUD ─────────────────────────────────────────────────────────────

        public static void Add(MethodDefinition def)
        {
            if (def == null) return;
            _methods.Add(def);
            OnChanged?.Invoke();
        }

        public static void Remove(string id)
        {
            int n = _methods.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal));
            if (n > 0) OnChanged?.Invoke();
        }

        public static void Update(MethodDefinition def)
        {
            if (def == null) return;
            int idx = _methods.FindIndex(m => string.Equals(m.Id, def.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                _methods[idx] = def;
                OnChanged?.Invoke();
            }
        }

        /// <summary>Добавляет или обновляет метод по Id.</summary>
        public static void AddOrUpdate(MethodDefinition def)
        {
            if (def == null) return;
            int idx = _methods.FindIndex(m => string.Equals(m.Id, def.Id, StringComparison.Ordinal));
            if (idx >= 0)
                _methods[idx] = def;
            else
                _methods.Add(def);
            OnChanged?.Invoke();
        }

        public static MethodDefinition GetById(string id)
            => _methods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

        public static List<MethodDefinition> GetAll() => new List<MethodDefinition>(_methods);

        // ─── Bulk ─────────────────────────────────────────────────────────────

        /// <summary>Заменяет весь список (при загрузке файла).</summary>
        public static void ReplaceAll(IEnumerable<MethodDefinition> methods)
        {
            _methods.Clear();
            if (methods != null) _methods.AddRange(methods);
            OnChanged?.Invoke();
        }

        public static void Clear()
        {
            _methods.Clear();
            OnChanged?.Invoke();
        }
    }
}
