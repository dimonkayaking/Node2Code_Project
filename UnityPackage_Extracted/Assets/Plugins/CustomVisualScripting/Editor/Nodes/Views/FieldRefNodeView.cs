using System;
using System.Linq;
using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Nodes.Methods;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Отображение <see cref="FieldRefNode"/>: тип, начальное значение (редактируемое).
    /// </summary>
    [NodeCustomEditor(typeof(FieldRefNode))]
    public class FieldRefNodeView : BaseNodeView
    {
        private static readonly Color HeaderColor = new Color(0.05f, 0.40f, 0.45f, 0.75f);

        private FieldRefNode _node;
        private TextField    _defaultField;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as FieldRefNode;
            if (_node == null) return;

            titleContainer.style.backgroundColor = HeaderColor;
            title = _node.name;

            BuildExtension();
        }

        private void BuildExtension()
        {
            extensionContainer.Clear();

            // Тип поля
            var typeLabel = new Label(_node.FieldType);
            typeLabel.style.color        = new Color(0.55f, 0.82f, 1f);
            typeLabel.style.fontSize     = 9;
            typeLabel.style.paddingLeft  = 6;
            typeLabel.style.paddingTop   = 2;
            extensionContainer.Add(typeLabel);

            // Начальное значение — берём из ClassRegistry
            var fieldDef = FindFieldDef();
            var currentDefault = fieldDef?.DefaultValue ?? "";

            _defaultField = new TextField("По умолч.")
            {
                value = currentDefault
            };
            _defaultField.style.marginLeft   = 4;
            _defaultField.style.marginRight  = 4;
            _defaultField.style.marginTop    = 2;
            _defaultField.style.marginBottom = 4;
            _defaultField.RegisterValueChangedCallback(evt => ApplyDefaultValue(evt.newValue));
            extensionContainer.Add(_defaultField);

            RefreshExpandedState();
        }

        private void ApplyDefaultValue(string newDefault)
        {
            var fieldDef = FindFieldDef();
            if (fieldDef == null) return;

            fieldDef.DefaultValue = newDefault;

            // Обновляем ClassDefinition через ClassRegistry
            var cls = ClassRegistry.Classes.FirstOrDefault(c =>
                c.Fields != null && c.Fields.Any(f =>
                    (!string.IsNullOrEmpty(_node.FieldId) && f.Id == _node.FieldId) ||
                    (string.IsNullOrEmpty(_node.FieldId) &&
                     string.Equals(f.Name, _node.FieldName, StringComparison.Ordinal))));
            if (cls != null)
                ClassRegistry.Update(cls);
        }

        private FieldDefinition FindFieldDef()
        {
            foreach (var cls in ClassRegistry.Classes)
            {
                if (cls.Fields == null) continue;
                foreach (var f in cls.Fields)
                {
                    if (!string.IsNullOrEmpty(_node.FieldId) && f.Id == _node.FieldId)
                        return f;
                    // Фолбэк для нод, инжектированных парсером (FieldId ещё не присвоен)
                    if (string.IsNullOrEmpty(_node.FieldId) &&
                        string.Equals(f.Name, _node.FieldName, StringComparison.Ordinal))
                        return f;
                }
            }
            return null;
        }
    }
}
