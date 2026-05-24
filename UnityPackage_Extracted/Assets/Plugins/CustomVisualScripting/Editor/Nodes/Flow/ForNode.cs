using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Flow
{
    [Serializable, NodeMenuItem("Flow/For")]
    public class ForNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.FlowFor;

        [HideInInspector]
        public GraphData conditionSubGraph = new GraphData();

        [HideInInspector]
        public GraphData initSubGraph = new GraphData();

        [HideInInspector]
        public GraphData incrementSubGraph = new GraphData();

        [HideInInspector]
        public GraphData bodySubGraph = new GraphData();

        public override string name => "For Loop";

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.ConditionSubGraph = conditionSubGraph;
            data.InitSubGraph = initSubGraph;
            data.IncrementSubGraph = incrementSubGraph;
            data.BodySubGraph = bodySubGraph;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            conditionSubGraph = data.ConditionSubGraph ?? new GraphData();
            initSubGraph = data.InitSubGraph ?? new GraphData();
            incrementSubGraph = data.IncrementSubGraph ?? new GraphData();
            bodySubGraph = data.BodySubGraph ?? new GraphData();
        }
    }
}