using System;
using System.Collections.Generic;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Literals
{
    [Serializable, NodeMenuItem("Literals/Bool")]
    public class BoolNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.LiteralBool;

        [Input("inputValue")]
        public object inputValue;

        [Output("output")]
        public object output;

        // Exec-порты отображаются только когда нода является корневой (имеет variableName)
        [Input("execIn")]
        public object execIn;

        [Output("execOut")]
        public object execOut;

        [HideInInspector]
        public bool boolValue = true;

        [HideInInspector]
        public string expressionOverride = "";

        public override string name => string.IsNullOrEmpty(variableName) ? $"Bool: {boolValue}" : $"{variableName} = {boolValue}";

        [CustomPortBehavior(nameof(execIn))]
        IEnumerable<PortData> GetExecInBehavior(List<SerializableEdge> edges)
        {
            yield return new PortData { identifier = "execIn", displayName = "Exec In", acceptMultipleEdges = false };
        }

        [CustomPortBehavior(nameof(execOut))]
        IEnumerable<PortData> GetExecOutBehavior(List<SerializableEdge> edges)
        {
            yield return new PortData { identifier = "execOut", displayName = "Exec Out", acceptMultipleEdges = false };
        }

        protected override void Process()
        {
            if (inputValue != null)
            {
                boolValue = inputValue switch
                {
                    bool b => b,
                    int i => i != 0,
                    float f => f != 0,
                    string s => bool.TryParse(s, out bool result) ? result : false,
                    _ => false
                };
            }
            output = boolValue;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            if (bool.TryParse(data.Value, out bool parsed))
            {
                boolValue = parsed;
            }
            expressionOverride = data.ExpressionOverride ?? "";
        }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Value = boolValue.ToString();
            data.ValueType = "bool";
            data.ExpressionOverride = expressionOverride ?? "";
            return data;
        }
    }
}
