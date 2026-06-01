using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>Всплывающее окно добавления / редактирования статического поля класса.</summary>
    public class FieldEditPopup : EditorWindow
    {
        private static readonly string[] Types = { "int", "float", "bool", "string" };

        private string _name         = "myField";
        private int    _typeIdx;
        private string _defaultValue = "";

        private Action<FieldDefinition> _onConfirm;
        private FieldDefinition         _editing;

        // ─── Фабрика ──────────────────────────────────────────────────────────

        public static void ShowCreate(string classId, Action<FieldDefinition> onConfirm)
        {
            var w = CreateInstance<FieldEditPopup>();
            w.titleContent = new GUIContent("Новое поле");
            w._onConfirm   = onConfirm;
            w._editing     = null;
            w.ShowUtility();
            w.minSize = new Vector2(300, 180);
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
            w.ShowUtility();
            w.minSize = new Vector2(300, 180);
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Тип поля", EditorStyles.boldLabel);
            _typeIdx = EditorGUILayout.Popup(_typeIdx, Types);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Имя поля", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Начальное значение (опционально)", EditorStyles.boldLabel);
            _defaultValue = EditorGUILayout.TextField(_defaultValue);

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();

            string confirmLabel = _editing == null ? "Добавить" : "Сохранить";
            if (GUILayout.Button(confirmLabel)) TryConfirm();
            if (GUILayout.Button("Отмена"))     Close();

            EditorGUILayout.EndHorizontal();
        }

        private void TryConfirm()
        {
            if (string.IsNullOrWhiteSpace(_name))
            {
                EditorUtility.DisplayDialog("Ошибка", "Введите имя поля.", "OK");
                return;
            }

            if (_editing != null)
            {
                _editing.Name         = _name.Trim();
                _editing.Type         = Types[_typeIdx];
                _editing.DefaultValue = _defaultValue.Trim();
                _onConfirm?.Invoke(_editing);
            }
            else
            {
                var f = new FieldDefinition
                {
                    Name         = _name.Trim(),
                    Type         = Types[_typeIdx],
                    DefaultValue = _defaultValue.Trim()
                };
                _onConfirm?.Invoke(f);
            }
            Close();
        }
    }
}
