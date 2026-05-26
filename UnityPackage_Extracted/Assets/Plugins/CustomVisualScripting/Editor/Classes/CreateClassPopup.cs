using System;
using UnityEditor;
using UnityEngine;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>
    /// Всплывающее окно создания / переименования пользовательского класса.
    /// Минималистичное: только название (без параметров и типа возврата — это базовая версия).
    /// </summary>
    public class CreateClassPopup : EditorWindow
    {
        private string _name = "MyClass";
        private Action<ClassDefinition> _onConfirm;
        private ClassDefinition _editing; // null → режим создания

        // ─── Фабрика ──────────────────────────────────────────────────────────

        public static void ShowCreate(Action<ClassDefinition> onConfirm, string defaultName = "MyClass")
        {
            var w = CreateInstance<CreateClassPopup>();
            w.titleContent = new GUIContent("Новый класс");
            w._onConfirm   = onConfirm;
            w._editing     = null;
            w._name        = defaultName;
            w.ShowUtility();
            w.minSize = new Vector2(320, 120);
            w.maxSize = new Vector2(480, 120);
        }

        public static void ShowEdit(ClassDefinition existing, Action<ClassDefinition> onConfirm)
        {
            var w = CreateInstance<CreateClassPopup>();
            w.titleContent = new GUIContent("Переименовать класс");
            w._onConfirm   = onConfirm;
            w._editing     = existing;
            w._name        = existing.Name;
            w.ShowUtility();
            w.minSize = new Vector2(320, 120);
            w.maxSize = new Vector2(480, 120);
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Название класса", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();

            string confirmLabel = _editing == null ? "Создать" : "Сохранить";
            if (GUILayout.Button(confirmLabel))
                TryConfirm();

            if (GUILayout.Button("Отмена"))
                Close();

            EditorGUILayout.EndHorizontal();
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
                _editing.Name = _name.Trim();
                def = _editing;
            }
            else
            {
                def = new ClassDefinition
                {
                    Id   = Guid.NewGuid().ToString(),
                    Name = _name.Trim(),
                    ClassBodyGraph = new VisualScripting.Core.Models.GraphData()
                };
            }

            _onConfirm?.Invoke(def);
            Close();
        }
    }
}
