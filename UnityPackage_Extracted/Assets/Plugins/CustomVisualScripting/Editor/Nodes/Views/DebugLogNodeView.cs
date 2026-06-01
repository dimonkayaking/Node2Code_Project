using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Debug;
using CustomVisualScripting.Editor.Nodes.Literals;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Вьюшка <see cref="DebugLogNode"/>: редактирование типа и значения сообщения во встроенном теле,
    /// либо подключение входа <c>message</c> — полностью аналогично Console.WriteLine.
    /// </summary>
    [NodeCustomEditor(typeof(DebugLogNode))]
    public class DebugLogNodeView : CollapsibleBodyGraphNodeView
    {
        private static readonly List<string> LiteralTypes = new() { "string", "int", "float", "bool" };

        private DebugLogNode _node;
        private PopupField<string> _typeField;
        private TextField _messageField;
        private Label _inputInfoLabel;
        private Label _validationLabel;

        public override (float minW, float minH) GetLiteralBoundsMins() =>
            IsBodyExpanded ? (260f, 108f) : (220f, 72f);

        public override void Enable()
        {
            base.Enable();

            _node = nodeTarget as DebugLogNode;
            if (_node == null) return;

            if (controlsContainer == null)
            {
                controlsContainer = new VisualElement();
                controlsContainer.name = "controls";
                mainContainer.Add(controlsContainer);
            }

            controlsContainer.style.flexDirection = FlexDirection.Column;
            controlsContainer.style.alignSelf = Align.Stretch;

            var initialType = DebugLogNode.NormalizeType(_node.messageValueType ?? "string");
            var typeIdx = LiteralTypes.IndexOf(initialType);
            if (typeIdx < 0)
                typeIdx = 0;
            _typeField = new PopupField<string>(LiteralTypes, typeIdx);
            _typeField.RegisterValueChangedCallback(evt =>
            {
                _node.messageValueType = DebugLogNode.NormalizeType(evt.newValue);
                ValidateAndPersist(_messageField.value ?? "");
            });
            controlsContainer.Add(LiteralRowLayout.Row("type", _typeField));

            _messageField = new TextField();
            _messageField.value = _node.messageText ?? "";
            _messageField.RegisterValueChangedCallback(evt => { ValidateAndPersist(evt.newValue ?? ""); });
            controlsContainer.Add(LiteralRowLayout.Row("value", _messageField));

            _validationLabel = new Label();
            _validationLabel.style.display = DisplayStyle.None;
            _validationLabel.style.color = new UnityEngine.Color(1f, 0.55f, 0.55f);
            controlsContainer.Add(_validationLabel);

            _inputInfoLabel = new Label();
            _inputInfoLabel.style.display = DisplayStyle.None;
            controlsContainer.Add(_inputInfoLabel);

            schedule.Execute(RefreshUiMode).Every(200);
            RefreshUiMode();

            FinishLiteralBodySetup();
        }

        private void RefreshUiMode()
        {
            var hasInput = TryGetConnectedMessageExpression(out var inputExpression);

            _typeField.SetEnabled(!hasInput);
            _messageField.SetEnabled(!hasInput);
            if (!hasInput)
            {
                var expectedValue = _node.messageText ?? "";
                if (_messageField.value != expectedValue)
                {
                    _messageField.SetValueWithoutNotify(expectedValue);
                }
                _inputInfoLabel.style.display = DisplayStyle.None;
                _validationLabel.style.display = DisplayStyle.None;
                return;
            }

            _inputInfoLabel.text = $"Input: {inputExpression}";
            _inputInfoLabel.style.display = DisplayStyle.Flex;
            _validationLabel.style.display = DisplayStyle.None;
        }

        private void ValidateAndPersist(string rawValue)
        {
            var normalizedType = DebugLogNode.NormalizeType(_typeField.value);
            _node.messageValueType = normalizedType;

            if (TryNormalizeValue(normalizedType, rawValue, out var normalized, out var error))
            {
                _node.messageText = normalized;
                _messageField.SetValueWithoutNotify(normalized);
                _validationLabel.style.display = DisplayStyle.None;
                return;
            }

            _validationLabel.text = error;
            _validationLabel.style.display = DisplayStyle.Flex;
        }

        private static bool TryNormalizeValue(string type, string rawValue, out string normalized, out string error)
        {
            normalized = rawValue;
            error = "";
            var input = (rawValue ?? "").Trim();
            switch (type)
            {
                case "int":
                    if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    {
                        error = "Expected int value.";
                        return false;
                    }
                    normalized = i.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "float":
                    if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    {
                        error = "Expected float value.";
                        return false;
                    }
                    normalized = f.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "bool":
                    if (!bool.TryParse(input, out var b))
                    {
                        error = "Expected bool value: true/false.";
                        return false;
                    }
                    normalized = b ? "true" : "false";
                    return true;
                default:
                    normalized = rawValue ?? "";
                    return true;
            }
        }

        private bool TryGetConnectedMessageExpression(out string expression)
        {
            expression = "";
            var messagePort = inputPortViews?.FirstOrDefault(p =>
                string.Equals(p.fieldName, "message", System.StringComparison.OrdinalIgnoreCase));
            if (messagePort == null)
                return false;

            var edge = messagePort.connections?.FirstOrDefault();
            var sourcePort = edge?.output as PortView;
            var sourceNode = sourcePort?.owner?.nodeTarget;
            if (sourceNode == null)
                return false;

            expression = ResolveSourceExpression(sourceNode);
            return true;
        }

        private static string ResolveSourceExpression(BaseNode sourceNode)
        {
            if (sourceNode is CustomBaseNode customNode)
            {
                if (!string.IsNullOrWhiteSpace(customNode.variableName))
                    return customNode.variableName;

                return customNode.NodeType switch
                {
                    NodeType.LiteralString when customNode is StringNode s => $"\"{s.stringValue}\"",
                    NodeType.LiteralInt when customNode is IntNode i => i.intValue.ToString(),
                    NodeType.LiteralFloat when customNode is FloatNode f => f.floatValue.ToString(),
                    NodeType.LiteralBool when customNode is BoolNode b => b.boolValue.ToString().ToLowerInvariant(),
                    _ => sourceNode.name
                };
            }

            return sourceNode.name;
        }
    }
}
