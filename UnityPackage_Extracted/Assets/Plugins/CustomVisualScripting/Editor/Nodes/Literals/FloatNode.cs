using System;
using System.Collections.Generic;
using System.Globalization;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Literals
{
    [Serializable, NodeMenuItem("Literals/Float")]
    public class FloatNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.LiteralFloat;

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
        public float floatValue = 0f;

        [HideInInspector]
        public string expressionOverride = "";

        public override string name => string.IsNullOrEmpty(variableName)
            ? $"Float: {floatValue.ToString(CultureInfo.InvariantCulture)}"
            : $"{variableName} = {floatValue.ToString(CultureInfo.InvariantCulture)}";

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
                floatValue = inputValue switch
                {
                    float f => f,
                    int i => i,
                    string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0f,
                    bool b => b ? 1f : 0f,
                    _ => 0f
                };
            }
            output = floatValue;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            if (float.TryParse(data.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                floatValue = parsed;
            }
            expressionOverride = data.ExpressionOverride ?? "";
        }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Value = floatValue.ToString(CultureInfo.InvariantCulture);
            data.ValueType = "float";
            data.ExpressionOverride = expressionOverride ?? "";
            return data;
        }
    }
}
