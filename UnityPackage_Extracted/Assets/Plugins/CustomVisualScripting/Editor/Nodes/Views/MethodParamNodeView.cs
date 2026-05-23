using System.Collections.Generic;
using GraphProcessor;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Methods;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    [NodeCustomEditor(typeof(MethodParamNode))]
    public class MethodParamNodeView : BaseNodeView
    {
        private MethodParamNode _node;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as MethodParamNode;
            if (_node == null) return;

            RefreshTitle();

            // ── Поле имени ────────────────────────────────────────────────────
            var nameField = new TextField("Имя") { value = _node.ParamName };
            nameField.style.minWidth = 120;
            nameField.RegisterValueChangedCallback(evt =>
            {
                _node.ParamName = evt.newValue;
                RefreshTitle();
            });

            // ── Поле типа ─────────────────────────────────────────────────────
            var typeField = new DropdownField(
                label:   "Тип",
                choices: new List<string> { "int", "float", "bool", "string" },
                defaultIndex: GetTypeIndex(_node.ParamType));
            typeField.RegisterValueChangedCallback(evt =>
            {
                _node.ParamType = evt.newValue;
            });

            extensionContainer.Add(nameField);
            extensionContainer.Add(typeField);
            RefreshExpandedState();
        }

        private void RefreshTitle() =>
            title = $"Параметр: {_node?.ParamName ?? "param"}";

        private static int GetTypeIndex(string type) => type switch
        {
            "float"  => 1,
            "bool"   => 2,
            "string" => 3,
            _        => 0
        };
    }
}
