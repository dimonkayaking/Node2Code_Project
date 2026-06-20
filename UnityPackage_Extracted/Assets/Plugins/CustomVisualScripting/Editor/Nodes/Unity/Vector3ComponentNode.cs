using System;
using System.Collections.Generic;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Unity
{
    /// <summary>
    /// Нода-декомпозер Vector3: принимает Vector3 на вход и выдаёт одну из компонент (x / y / z) как float.
    /// Компонент хранится в NodeData.Value ("x", "y" или "z").
    /// Соответствует NodeType.Vector3Component.
    /// </summary>
    [Serializable, NodeMenuItem("Unity/Vector3 Component")]
    public class Vector3ComponentNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.Vector3Component;

        [Input("vector")]
        public object vector;

        [Output("value")]
        public float value;

        [HideInInspector]
        public string Component = "x";  // "x" | "y" | "z"

        public override string name => $"Vector3.{Component}";

        protected override void Process() { }

        [CustomPortBehavior(nameof(vector))]
        IEnumerable<PortData> VectorBehavior(List<SerializableEdge> _)
        {
            yield return new PortData
            {
                identifier          = "vector",
                displayName         = "Vector3",
                acceptMultipleEdges = false
            };
        }

        [CustomPortBehavior(nameof(value))]
        IEnumerable<PortData> ValueBehavior(List<SerializableEdge> _)
        {
            yield return new PortData
            {
                identifier          = "value",
                displayName         = $".{Component} (float)",
                acceptMultipleEdges = false
            };
        }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type      = NodeType.Vector3Component;
            data.Value     = Component;
            data.ValueType = "float";
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            Component = string.IsNullOrEmpty(data.Value) ? "x" : data.Value;
        }
    }
}
