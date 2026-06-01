using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEngine;
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

            // ── Значение по умолчанию (опционально) ──────────────────────────
            var defaultField = new TextField("По умолч.") { value = _node.DefaultValue };
            defaultField.style.minWidth = 120;
            defaultField.tooltip = "Необязательное значение по умолчанию (оставьте пустым если не нужно)";
            defaultField.RegisterValueChangedCallback(evt => _node.DefaultValue = evt.newValue);

            extensionContainer.Add(nameField);
            extensionContainer.Add(typeField);
            extensionContainer.Add(defaultField);
            RefreshExpandedState();
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // Только одна опция — добавить ещё одну ссылку на этот параметр в тело метода
            evt.menu.AppendAction(
                "Добавить параметр в тело",
                _ => AddExtraParamToBody(),
                _ => CanAddToBody()
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);
        }

        private MethodParamGraphView GetParamGraphView() =>
            GetFirstAncestorOfType<MethodParamGraphView>();

        private bool CanAddToBody() =>
            _node != null &&
            GetParamGraphView()?.BodyGraphView != null;

        private void AddExtraParamToBody()
        {
            if (_node == null) return;
            var bodyView = GetParamGraphView()?.BodyGraphView;
            if (bodyView?.graph == null) return;

            // Считаем сколько ref-нод этого параметра уже есть в body
            int existingCount = bodyView.graph.nodes
                .OfType<MethodParamNode>()
                .Count(n => string.Equals(n.ParamName, _node.ParamName, StringComparison.Ordinal));

            // Новый экземпляр — уникальный GUID, без стабильного "_paramref_" префикса
            var copy = new MethodParamNode
            {
                ParamName    = _node.ParamName,
                ParamType    = _node.ParamType,
                DefaultValue = _node.DefaultValue
            };
            copy.NodeId = Guid.NewGuid().ToString();
            copy.SetGUID(copy.NodeId);
            copy.position = new Rect(40f + existingCount * 230f, 40f, 200f, 80f);

            try { bodyView.AddNode(copy); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VS] AddExtraParamToBody: {ex.Message}");
            }
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
