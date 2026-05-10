using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GraphProcessor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Debug;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Nodes.Literals;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor
{
    /// <summary>
    /// Syncs <see cref="GraphData"/> from a GraphProcessor view (nodes, edges, nested visual layout).
    /// </summary>
    public static class GraphDataViewSync
    {
        public static NodeData BuildSyncedNodeData(CustomBaseNode customNode)
        {
            var nodeData = customNode.ToNodeData();
            nodeData.Id = customNode.NodeId;
            nodeData.VariableName = customNode.variableName;

            if (customNode is IntNode intNode)
            {
                nodeData.Value = intNode.intValue.ToString();
                nodeData.ExpressionOverride = intNode.expressionOverride;
            }
            else if (customNode is FloatNode floatNode)
            {
                nodeData.Value = floatNode.floatValue.ToString(CultureInfo.InvariantCulture);
                nodeData.ExpressionOverride = floatNode.expressionOverride;
            }
            else if (customNode is BoolNode boolNode)
            {
                nodeData.Value = boolNode.boolValue.ToString();
                nodeData.ExpressionOverride = boolNode.expressionOverride;
            }
            else if (customNode is StringNode stringNode)
            {
                nodeData.Value = stringNode.stringValue;
                nodeData.ExpressionOverride = stringNode.expressionOverride;
            }
            else if (customNode is ConsoleWriteLineNode cwlNode)
            {
                nodeData.Value = cwlNode.messageText;
                nodeData.ValueType = cwlNode.messageValueType;
            }

            if (customNode is IfNode ifNode)
            {
                nodeData.ConditionSubGraph = ifNode.conditionSubGraph;
                nodeData.BodySubGraph = ifNode.bodySubGraph;
            }
            else if (customNode is ElseNode elseNode)
            {
                nodeData.BodySubGraph = elseNode.bodySubGraph;
            }
            else if (customNode is ForNode forNode)
            {
                nodeData.InitSubGraph = forNode.initSubGraph;
                nodeData.ConditionSubGraph = forNode.conditionSubGraph;
                nodeData.IncrementSubGraph = forNode.incrementSubGraph;
                nodeData.BodySubGraph = forNode.bodySubGraph;
            }
            else if (customNode is WhileNode whileNode)
            {
                nodeData.ConditionSubGraph = whileNode.conditionSubGraph;
                nodeData.BodySubGraph = whileNode.bodySubGraph;
            }

            return nodeData;
        }

        public static void SyncGraphDataNodesAndEdgesFromView(
            GraphData target,
            IEnumerable<CustomBaseNode> graphNodes,
            BaseGraphView graphView)
        {
            target.Nodes.Clear();
            target.Edges.Clear();

            var validNodeIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var customNode in graphNodes)
            {
                target.Nodes.Add(BuildSyncedNodeData(customNode));
                validNodeIds.Add(customNode.NodeId);
            }

            foreach (var edgeView in graphView.edgeViews)
            {
                if (edgeView == null) continue;

                var fromPort = edgeView.output as PortView;
                var toPort = edgeView.input as PortView;

                if (fromPort == null || toPort == null) continue;
                if (fromPort.direction != Direction.Output || toPort.direction != Direction.Input) continue;

                var fromNode = fromPort.owner.nodeTarget as CustomBaseNode;
                var toNode = toPort.owner.nodeTarget as CustomBaseNode;

                if (fromNode == null || toNode == null) continue;
                if (!validNodeIds.Contains(fromNode.NodeId) || !validNodeIds.Contains(toNode.NodeId)) continue;

                var canonicalFrom = GraphViewPortStorage.CanonicalPortIdForStorage(fromPort);
                var canonicalTo = GraphViewPortStorage.CanonicalPortIdForStorage(toPort);
                if (string.IsNullOrEmpty(canonicalFrom) || string.IsNullOrEmpty(canonicalTo))
                    continue;

                target.Edges.Add(new EdgeData
                {
                    FromNodeId = fromNode.NodeId,
                    FromPort = canonicalFrom,
                    ToNodeId = toNode.NodeId,
                    ToPort = canonicalTo
                });
            }
        }

        public static void SaveVisualLayoutToGraphData(GraphData target, BaseGraph internalGraph, BaseGraphView graphView)
        {
            if (target == null || internalGraph == null || graphView == null)
                return;

            target.VisualLayout ??= new List<VisualLayoutEntry>();
            target.VisualLayout.Clear();

            foreach (var customNode in internalGraph.nodes.OfType<CustomBaseNode>())
            {
                if (!graphView.nodeViewsPerNode.TryGetValue(customNode, out var nodeView))
                    continue;

                var pos = nodeView.GetPosition().position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsInfinity(pos.x) || float.IsInfinity(pos.y))
                    continue;

                target.VisualLayout.Add(new VisualLayoutEntry
                {
                    NodeId = customNode.NodeId,
                    X = pos.x,
                    Y = pos.y
                });
            }
        }

        public static void ApplySavedVisualLayout(GraphData graphData, BaseGraphView graphView)
        {
            if (graphData?.VisualLayout == null || graphData.VisualLayout.Count == 0 || graphView == null)
                return;

            var byId = graphData.VisualLayout.ToDictionary(e => e.NodeId, System.StringComparer.Ordinal);
            foreach (var nodeView in graphView.nodeViews)
            {
                if (nodeView?.nodeTarget is not CustomBaseNode cn)
                    continue;
                if (!byId.TryGetValue(cn.NodeId, out var entry))
                    continue;
                
                // Don't overwrite width/height with 0f, preserve existing rect size
                var currentRect = nodeView.GetPosition();
                nodeView.SetPosition(new Rect(entry.X, entry.Y, currentRect.width, currentRect.height));
            }
        }
    }
}
