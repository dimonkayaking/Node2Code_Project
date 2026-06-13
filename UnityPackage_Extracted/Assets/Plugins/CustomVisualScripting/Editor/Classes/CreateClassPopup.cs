using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>
    /// Всплывающее окно создания / переименования пользовательского класса.
    /// Позволяет задать имя, выбрать родительский класс и настроить наследование MonoBehaviour.
    /// </summary>
    public class CreateClassPopup : EditorWindow
    {
        private string _name = "MyClass";
        private string _baseClassId = "";
        private bool   _inheritsMonoBehaviour = true;
        private Action<ClassDefinition> _onConfirm;
        private ClassDefinition _editing; // null → режим создания

        // ─── Фабрика ──────────────────────────────────────────────────────────

        public static void ShowCreate(Action<ClassDefinition> onConfirm, string defaultName = "MyClass")
        {
            var w = CreateInstance<CreateClassPopup>();
            w.titleContent         = new GUIContent("Новый класс");
            w._onConfirm           = onConfirm;
            w._editing             = null;
            w._name                = defaultName;
            w._baseClassId         = "";
            w._inheritsMonoBehaviour = true;
            w.ShowUtility();
            w.minSize = new Vector2(320, 200);
            w.maxSize = new Vector2(480, 200);
        }

        public static void ShowEdit(ClassDefinition existing, Action<ClassDefinition> onConfirm)
        {
            var w = CreateInstance<CreateClassPopup>();
            w.titleContent           = new GUIContent("Редактировать класс");
            w._onConfirm             = onConfirm;
            w._editing               = existing;
            w._name                  = existing.Name;
            w._baseClassId           = existing.BaseClassId ?? "";
            w._inheritsMonoBehaviour = existing.InheritsMonoBehaviour;
            w.ShowUtility();
            w.minSize = new Vector2(320, 200);
            w.maxSize = new Vector2(480, 200);
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Название класса", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            EditorGUILayout.Space(8);
            DrawBaseClassSelector();

            EditorGUILayout.Space(6);
            DrawMonoBehaviourToggle();

            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();

            string confirmLabel = _editing == null ? "Создать" : "Сохранить";
            if (GUILayout.Button(confirmLabel))
                TryConfirm();

            if (GUILayout.Button("Отмена"))
                Close();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBaseClassSelector()
        {
            EditorGUILayout.LabelField("Родительский класс", EditorStyles.boldLabel);

            // Исключаем сам редактируемый класс и его потомков (выбор предка не создаёт цикл)
            var candidates = ClassRegistry.Classes
                .Where(c => _editing == null || !IsSelfOrDescendant(c.Id, _editing.Id))
                .ToList();

            var names = new string[candidates.Count + 1];
            names[0] = "— нет (корневой класс) —";
            for (int i = 0; i < candidates.Count; i++)
                names[i + 1] = candidates[i].Name;

            int currentIdx = 0;
            if (!string.IsNullOrEmpty(_baseClassId))
            {
                int found = candidates.FindIndex(c => c.Id == _baseClassId);
                if (found >= 0) currentIdx = found + 1;
            }

            int newIdx = EditorGUILayout.Popup(currentIdx, names);
            _baseClassId = newIdx == 0 ? "" : candidates[newIdx - 1].Id;
        }

        private void DrawMonoBehaviourToggle()
        {
            // MonoBehaviour актуален только когда нет пользовательского родителя
            bool hasUserParent = !string.IsNullOrEmpty(_baseClassId);

            EditorGUI.BeginDisabledGroup(hasUserParent);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Наследовать MonoBehaviour",
                    hasUserParent ? "Недоступно при наличии родительского класса" : ""),
                EditorStyles.boldLabel);

            // Кнопка-переключатель
            var style = new GUIStyle(GUI.skin.button);
            if (_inheritsMonoBehaviour && !hasUserParent)
            {
                style.normal.textColor  = Color.white;
                style.focused.textColor = Color.white;
            }
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = (_inheritsMonoBehaviour && !hasUserParent)
                ? new Color(0.2f, 0.65f, 0.2f)
                : new Color(0.4f, 0.4f, 0.4f);

            string label = (_inheritsMonoBehaviour && !hasUserParent) ? "Вкл" : "Выкл";
            if (GUILayout.Button(label, style, GUILayout.Width(52)))
                _inheritsMonoBehaviour = !_inheritsMonoBehaviour;

            GUI.backgroundColor = prevColor;
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
        }

        // Возвращает true если candidateId — это сам редактируемый класс или его потомок.
        // Именно такие классы нельзя выбирать родителем: создастся цикл.
        // Предки редактируемого класса — разрешены (один из них уже может быть текущим родителем).
        private static bool IsSelfOrDescendant(string candidateId, string editingId)
        {
            if (candidateId == editingId) return true;
            // Идём вверх по цепочке предков кандидата — если где-то встречаем editingId,
            // значит candidate является потомком редактируемого класса.
            var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var current = ClassRegistry.GetById(candidateId);
            while (current != null && !string.IsNullOrEmpty(current.BaseClassId))
            {
                if (!visited.Add(current.BaseClassId)) break;
                if (current.BaseClassId == editingId) return true;
                current = ClassRegistry.GetById(current.BaseClassId);
            }
            return false;
        }

        private void TryConfirm()
        {
            if (string.IsNullOrWhiteSpace(_name))
            {
                EditorUtility.DisplayDialog("Ошибка", "Введите название класса.", "OK");
                return;
            }

            ClassDefinition def;
            if (_editing != null)
            {
                _editing.Name                  = _name.Trim();
                _editing.BaseClassId           = _baseClassId;
                _editing.InheritsMonoBehaviour = string.IsNullOrEmpty(_baseClassId) && _inheritsMonoBehaviour;
                def = _editing;
            }
            else
            {
                def = new ClassDefinition
                {
                    Name                  = _name.Trim(),
                    BaseClassId           = _baseClassId,
                    InheritsMonoBehaviour = string.IsNullOrEmpty(_baseClassId) && _inheritsMonoBehaviour
                };
            }

            _onConfirm?.Invoke(def);
            Close();
        }
    }
}
