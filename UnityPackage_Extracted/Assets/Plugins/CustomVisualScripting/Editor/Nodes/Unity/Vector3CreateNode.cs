using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Unity
{
    [Serializable, NodeMenuItem("Unity/Create Vector3")]
    public class Vector3CreateNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.UnityVector3;

        [Input("X")]
        public float X;

        [Input("Y")]
        public float Y;

        [Input("Z")]
        public float Z;

        [Output("Vector3")]
        public Vector3 vector;

        public override string name => "Create Vector3";

        protected override void Process()
        {
            vector = new Vector3(X, Y, Z);
        }

        public override NodeData ToNodeData()
        {
            var nodeData = base.ToNodeData();
            nodeData.ValueType = "Vector3";
            return nodeData;
        }
    }
}