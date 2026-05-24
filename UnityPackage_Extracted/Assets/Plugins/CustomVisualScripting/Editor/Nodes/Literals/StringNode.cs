using System;
using System.Collections.Generic;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Literals
{
    [Serializable, NodeMenuItem("Literals/String")]
    public class StringNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.LiteralString;

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
        public string stringValue = "";

        [HideInInspector]
        public string expressionOverride = "";

        public override string name => string.IsNullOrEmpty(variableName) ? $"String: \"{stringValue}\"" : $"{variableName} = \"{stringValue}\"";

        [CustomPortBehavior(nameof(execIn))]
        IEnumerable<PortData> GetExecInBehavior(List<SerializableEdge> edges)
        {
            if (IsStatementRootNode())
                yield return new PortData { identifier = "execIn", displayName = "Exec In", acceptMultipleEdges = false };
        }

        [CustomPortBehavior(nameof(execOut))]
        IEnumerable<PortData> GetExecOutBehavior(List<SerializableEdge> edges)
        {
            if (IsStatementRootNode())
                yield return new PortData { identifier = "execOut", displayName = "Exec Out", acceptMultipleEdges = false };
        }

        protected override void Process()
        {
            if (inputValue != null)
            {
                stringValue = inputValue.ToString() ?? "";
            }
            output = stringValue;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            stringValue = data.Value ?? "";
            expressionOverride = data.ExpressionOverride ?? "";
        }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Value = stringValue;
            data.ValueType = "string";
            data.ExpressionOverride = expressionOverride ?? "";
            return data;
        }
    }
}
