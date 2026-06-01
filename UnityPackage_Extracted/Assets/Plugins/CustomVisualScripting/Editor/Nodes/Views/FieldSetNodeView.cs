using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Nodes.Methods;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Отображение <see cref="FieldSetNode"/>: выпадающий список полей из всех классов.
    /// Выбранное поле записывается в <see cref="FieldSetNode.FieldName"/> / <see cref="FieldSetNode.FieldType"/>.
    /// </summary>
    [NodeCustomEditor(typeof(FieldSetNode))]
    public class FieldSetNodeView : BaseNodeView
    {
        private static readonly Color HeaderColor = new Color(0.45f, 0.20f, 0.05f, 0.75f);

        private FieldSetNode  _node;
        private DropdownField _dropdown;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as FieldSetNode;
            if (_node == null) return;

            titleContainer.style.backgroundColor = HeaderColor;
            RefreshTitle();
            BuildDropdown();

            ClassRegistry.OnChanged += OnRegistryChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => ClassRegistry.OnChanged -= OnRegistryChanged);
        }

        private void OnRegistryChanged()
        {
            extensionContainer.Clear();
            BuildDropdown();
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            title = string.IsNullOrEmpty(_node.FieldName) ? "FieldSet" : $"Set {_node.FieldName}";
        }

        private void BuildDropdown()
        {
            // Строим список: "ClassName.FieldName (Type)"
            var options = BuildOptions();

            if (options.Count == 0)
            {
                var hint = new Label("Нет полей");
                hint.style.fontSize    = 9;
                hint.style.color       = new Color(0.7f, 0.7f, 0.7f);
                hint.style.paddingLeft = 6;
                hint.style.paddingTop  = 2;
                extensionContainer.Add(hint);
                return;
            }

            // Подбираем текущий выбор
            string currentLabel = options.FirstOrDefault(o => o.Contains("." + _node.FieldName + " (")) ?? options[0];

            _dropdown = new DropdownField(options, currentLabel);
            _dropdown.style.marginLeft   = 4;
            _dropdown.style.marginRight  = 4;
            _dropdown.style.marginTop    = 4;
            _dropdown.style.marginBottom = 4;
            _dropdown.RegisterValueChangedCallback(evt => ApplySelection(evt.newValue));
            extensionContainer.Add(_dropdown);

            ApplySelection(currentLabel);
        }

        private static List<string> BuildOptions()
        {
            var result = new List<string>();
            foreach (var cls in ClassRegistry.Classes)
            {
                if (cls.Fields == null) continue;
                foreach (var f in cls.Fields)
                    result.Add($"{cls.Name}.{f.Name} ({f.Type})");
            }
            return result;
        }

        private void ApplySelection(string label)
        {
            foreach (var cls in ClassRegistry.Classes)
            {
                if (cls.Fields == null) continue;
                foreach (var f in cls.Fields)
                {
                    if (label == $"{cls.Name}.{f.Name} ({f.Type})")
                    {
                        _node.FieldName = f.Name;
                        _node.FieldType = f.Type;
                        RefreshTitle();
                        return;
                    }
                }
            }
        }
    }
}
