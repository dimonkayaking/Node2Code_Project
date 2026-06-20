using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>Всплывающее окно добавления / редактирования поля класса.</summary>
    public class FieldEditPopup : EditorWindow
    {
        private static readonly string[] Types =
        {
            "int", "float", "bool", "string",
            "Vector3", "Vector2", "Quaternion",
            "Transform", "GameObject", "Collider2D"
        };

        /// <summary>
        /// Ссылочные Unity-типы, для которых поле не может иметь текстовый литерал
        /// в качестве начального значения (Transform/GameObject/Collider2D — это ссылки на
        /// компоненты сцены, а не конструируемые значения).
        /// </summary>
        private static readonly HashSet<string> ReferenceTypes = new() { "Transform", "GameObject", "Collider2D" };

        private string _name         = "myField";
        private int    _typeIdx;
        private string _defaultValue = "";
        private bool   _isPublic     = true;
        private bool   _isStatic     = false;

        private Action<FieldDefinition> _onConfirm;
        private FieldDefinition         _editing;

        // ─── Фабрика ──────────────────────────────────────────────────────────

        public static void ShowCreate(string classId, Action<FieldDefinition> onConfirm)
        {
            var w = CreateInstance<FieldEditPopup>();
            w.titleContent = new GUIContent("Новое поле");
            w._onConfirm   = onConfirm;
            w._editing     = null;
            w._isPublic    = true;
            w._isStatic    = false;
            w.ShowUtility();
            w.minSize = new Vector2(320, 230);
        }

        public static void ShowEdit(FieldDefinition existing, Action<FieldDefinition> onConfirm)
        {
            var w = CreateInstance<FieldEditPopup>();
            w.titleContent  = new GUIContent("Редактировать поле");
            w._onConfirm    = onConfirm;
            w._editing      = existing;
            w._name         = existing.Name;
            w._typeIdx      = Mathf.Max(0, Array.IndexOf(Types, existing.Type));
            w._defaultValue = existing.DefaultValue ?? "";
            w._isPublic     = existing.IsPublic;
            w._isStatic     = existing.IsStatic;
            w.ShowUtility();
            w.minSize = new Vector2(320, 230);
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            // ── Модификаторы: public/private + static/instance ─────────────────
            EditorGUILayout.LabelField("Модификаторы", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _isPublic = DrawToggle(_isPublic, "public", "private", new Color(0.2f, 0.55f, 0.85f));
            GUILayout.Space(6);
            _isStatic = DrawToggle(_isStatic, "static", "—",       new Color(0.55f, 0.4f, 0.75f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Тип поля", EditorStyles.boldLabel);
            _typeIdx = EditorGUILayout.Popup(_typeIdx, Types);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Имя поля", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            var isRefType = ReferenceTypes.Contains(Types[_typeIdx]);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(isRefType))
            {
                var label = isRefType
                    ? "Начальное значение (недоступно для ссылочных типов)"
                    : "Начальное значение (опционально)";
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                _defaultValue = EditorGUILayout.TextField(isRefType ? "" : _defaultValue);
            }

            // Превью объявления
            EditorGUILayout.Space(6);
            var preview = BuildPreview();
            var prev = GUI.color;
            GUI.color = new Color(0.6f, 0.8f, 0.6f);
            EditorGUILayout.LabelField(preview, EditorStyles.miniLabel);
            GUI.color = prev;

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();

            string confirmLabel = _editing == null ? "Добавить" : "Сохранить";
            if (GUILayout.Button(confirmLabel)) TryConfirm();
            if (GUILayout.Button("Отмена"))     Close();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Кнопка-переключатель: <paramref name="onLabel"/> при true, <paramref name="offLabel"/> при false.</summary>
        private static bool DrawToggle(bool value, string onLabel, string offLabel, Color onColor)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = value ? onColor : new Color(0.4f, 0.4f, 0.4f);
            if (GUILayout.Button(value ? onLabel : offLabel, GUILayout.Height(22)))
                value = !value;
            GUI.backgroundColor = prev;
            return value;
        }

        private string BuildPreview()
        {
            var modifier = _isPublic ? "public" : "private";
            var staticStr = _isStatic ? " static" : "";
            var isRefType = ReferenceTypes.Contains(Types[_typeIdx]);
            var init = (isRefType || string.IsNullOrWhiteSpace(_defaultValue)) ? "" : $" = {_defaultValue.Trim()}";
            return $"{modifier}{staticStr} {Types[_typeIdx]} {_name}{init};";
        }

        private void TryConfirm()
        {
            if (string.IsNullOrWhiteSpace(_name))
            {
                EditorUtility.DisplayDialog("Ошибка", "Введите имя поля.", "OK");
                return;
            }

            var type = Types[_typeIdx];
            // Для ссылочных Unity-типов (Transform/GameObject) начальное значение
            // как текстовый литерал не имеет смысла — это ссылки на объекты сцены.
            var defaultValue = ReferenceTypes.Contains(type) ? "" : _defaultValue.Trim();

            if (_editing != null)
            {
                _editing.Name         = _name.Trim();
                _editing.Type         = type;
                _editing.DefaultValue = defaultValue;
                _editing.IsPublic     = _isPublic;
                _editing.IsStatic     = _isStatic;
                _onConfirm?.Invoke(_editing);
            }
            else
            {
                var f = new FieldDefinition
                {
                    Name         = _name.Trim(),
                    Type         = type,
                    DefaultValue = defaultValue,
                    IsPublic     = _isPublic,
                    IsStatic     = _isStatic
                };
                _onConfirm?.Invoke(f);
            }
            Close();
        }
    }
}
