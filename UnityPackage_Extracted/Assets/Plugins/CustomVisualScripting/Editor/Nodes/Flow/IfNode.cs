using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Flow
{
    [Serializable, NodeMenuItem("Flow/If")]
    public class IfNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.FlowIf;

        [Output("false")]
        public object falseBranch;

        [HideInInspector]
        public GraphData conditionSubGraph = new GraphData();

        [HideInInspector]
        public GraphData bodySubGraph = new GraphData();

        public override string name => "If Statement";

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.ConditionSubGraph = conditionSubGraph;
            data.BodySubGraph = bodySubGraph;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            conditionSubGraph = data.ConditionSubGraph ?? new GraphData();
            bodySubGraph = data.BodySubGraph ?? new GraphData();
        }
    }
}
