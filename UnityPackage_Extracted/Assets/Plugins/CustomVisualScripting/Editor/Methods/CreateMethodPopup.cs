using System;
using System.Collections.Generic;
using System.Linq;
using CustomVisualScripting.Editor.Classes;
using UnityEditor;
using UnityEngine;

namespace CustomVisualScripting.Editor.Methods
{
    /// <summary>
    /// Всплывающее окно создания / редактирования пользовательского метода.
    /// При создании требует обязательную привязку к классу из <see cref="ClassRegistry"/>.
    /// </summary>
    public class CreateMethodPopup : EditorWindow
    {
        private static readonly string[] ReturnTypes = { "void", "int", "float", "bool", "string" };
        private static readonly string[] ParamTypes  = { "int", "float", "bool", "string" };

        private string _name = "MyMethod";
        private int    _returnTypeIdx;
        private List<ParameterDefinition> _params = new();

        private Action<MethodDefinition> _onConfirm;
        private MethodDefinition         _editing; // null → режим создания
        private Vector2                  _scroll;

        // ─── Класс-привязка ───────────────────────────────────────────────────
        private List<ClassDefinition> _availableClasses = new();
        private string[]              _classNames       = Array.Empty<string>();
        private int                   _classIdx;

        // ─── Фабрика ──────────────────────────────────────────────────────────

        public static void ShowCreate(Action<MethodDefinition> onConfirm, string defaultName = "MyMethod")
        {
            var w = CreateInstance<CreateMethodPopup>();
            w.titleContent   = new GUIContent("Новый метод");
            w._onConfirm     = onConfirm;
            w._editing       = null;
            w._name          = defaultName;
            w._returnTypeIdx = 0;
            w.RefreshClassList(selectedClassId: null);
            w.ShowUtility();
            w.minSize = new Vector2(380, 320);
        }

        public static void ShowEdit(MethodDefinition existing, Action<MethodDefinition> onConfirm)
        {
            var w = CreateInstance<CreateMethodPopup>();
            w.titleContent   = new GUIContent("Редактировать метод");
            w._onConfirm     = onConfirm;
            w._editing       = existing;
            w._name          = existing.Name;
            w._returnTypeIdx = Mathf.Max(0, Array.IndexOf(ReturnTypes, existing.ReturnType));
            w._params        = new List<ParameterDefinition>(existing.Parameters
                                   .ConvertAll(p => new ParameterDefinition { Name = p.Name, Type = p.Type }));
            w.RefreshClassList(selectedClassId: existing.ClassId);
            w.ShowUtility();
            w.minSize = new Vector2(380, 320);
        }

        private void RefreshClassList(string selectedClassId)
        {
            _availableClasses = ClassRegistry.GetAll();
            _classNames       = _availableClasses.Select(c => c.Name).ToArray();

            if (string.IsNullOrEmpty(selectedClassId))
            {
                _classIdx = 0;
                return;
            }
            int found = _availableClasses.FindIndex(c => string.Equals(c.Id, selectedClassId, StringComparison.Ordinal));
            _classIdx = Mathf.Max(0, found);
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            // ── Класс-владелец ─────────────────────────────────────────────────
            EditorGUILayout.LabelField("Класс", EditorStyles.boldLabel);
            if (_availableClasses.Count == 0)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.3f);
                EditorGUILayout.LabelField("Нет доступных классов. Сначала создайте класс.");
                GUI.color = oldColor;
            }
            else
            {
                _classIdx = EditorGUILayout.Popup(_classIdx, _classNames);
            }

            EditorGUILayout.Space(6);

            // ── Название ──────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Название метода", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(_name);

            EditorGUILayout.Space(6);

            // ── Тип возврата ──────────────────────────────────────────────────
            EditorGUILayout.LabelField("Возвращаемый тип", EditorStyles.boldLabel);
            _returnTypeIdx = EditorGUILayout.Popup(_returnTypeIdx, ReturnTypes);

            EditorGUILayout.Space(8);

            // ── Параметры ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Параметры", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(160));
            for (int i = 0; i < _params.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                _params[i].Name = EditorGUILayout.TextField(_params[i].Name, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));

                int ptIdx = Mathf.Max(0, Array.IndexOf(ParamTypes, _params[i].Type));
                ptIdx = EditorGUILayout.Popup(ptIdx, ParamTypes, GUILayout.Width(80));
                _params[i].Type = ParamTypes[ptIdx];

                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("✕", GUILayout.Width(26)))
                {
                    _params.RemoveAt(i);
                    GUI.color = oldColor;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.color = oldColor;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ Параметр"))
            {
                // Уникальное имя параметра
                var existingNames = new HashSet<string>(
                    _params.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
                string pname = "param";
                if (existingNames.Contains(pname))
                {
                    int n = 1;
                    while (existingNames.Contains($"param{n}")) n++;
                    pname = $"param{n}";
                }
                _params.Add(new ParameterDefinition { Name = pname, Type = "int" });
            }

            // ── Кнопки ────────────────────────────────────────────────────────
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
            if (_availableClasses.Count == 0)
            {
                EditorUtility.DisplayDialog("Ошибка", "Сначала создайте хотя бы один класс.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(_name))
            {
                EditorUtility.DisplayDialog("Ошибка", "Введите название метода.", "OK");
                return;
            }

            string selectedClassId = (_classIdx >= 0 && _classIdx < _availableClasses.Count)
                ? _availableClasses[_classIdx].Id
                : "";

            MethodDefinition def;
            if (_editing != null)
            {
                // Мутируем существующий объект — сохраняем ParamGraph, BodyGraph и живую ссылку рантайма
                _editing.Name       = _name.Trim();
                _editing.ReturnType = ReturnTypes[_returnTypeIdx];
                _editing.Parameters = new List<ParameterDefinition>(_params);
                _editing.ClassId    = selectedClassId;
                def = _editing;
            }
            else
            {
                def = new MethodDefinition
                {
                    Id         = Guid.NewGuid().ToString(),
                    Name       = _name.Trim(),
                    ReturnType = ReturnTypes[_returnTypeIdx],
                    Parameters = new List<ParameterDefinition>(_params),
                    ClassId    = selectedClassId,
                    BodyGraph  = new VisualScripting.Core.Models.GraphData()
                };
            }

            _onConfirm?.Invoke(def);
            Close();
        }
    }
}
