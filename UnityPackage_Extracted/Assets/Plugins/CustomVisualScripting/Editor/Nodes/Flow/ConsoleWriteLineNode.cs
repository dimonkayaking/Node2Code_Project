using System;
using System.Globalization;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Flow
{
    [Serializable, NodeMenuItem("Flow/Console.WriteLine")]
    public class ConsoleWriteLineNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.ConsoleWriteLine;

        [Input("message")]
        public object message;

        [SerializeField, HideInInspector]
        public string messageText = "";

        [SerializeField, HideInInspector]
        public string messageValueType = "string";

        public override string name => "Console.WriteLine";

        protected override void Process()
        {
            var text = message?.ToString() ?? GetFormattedLiteralForDisplay();
            if (!string.IsNullOrEmpty(text))
            {
                UnityEngine.Debug.Log(text);
            }
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            messageText = data.Value ?? "";
            messageValueType = NormalizeType(data.ValueType);
        }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Value = messageText ?? "";
            data.ValueType = NormalizeType(messageValueType);
            return data;
        }

        private string GetFormattedLiteralForDisplay()
        {
            var type = NormalizeType(messageValueType);
            var raw = messageText ?? "";
            return type switch
            {
                "bool" => bool.TryParse(raw, out var b) ? b.ToString() : "false",
                "int" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? i.ToString(CultureInfo.InvariantCulture)
                    : "0",
                "float" => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                    ? f.ToString(CultureInfo.InvariantCulture)
                    : "0",
                _ => raw
            };
        }

        public static string NormalizeType(string type)
        {
            var t = (type ?? "").Trim().ToLowerInvariant();
            return t switch
            {
                "int" => "int",
                "float" => "float",
                "bool" => "bool",
                _ => "string"
            };
        }
    }
}