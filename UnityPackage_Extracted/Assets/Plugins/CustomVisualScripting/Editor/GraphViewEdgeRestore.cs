#nullable enable
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEditor.Experimental.GraphView;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor
{
    /// <summary>
    /// Restores serialized <see cref="EdgeData"/> connections on a <see cref="BaseGraphView"/>.
    /// </summary>
    public static class GraphViewEdgeRestore
    {
        /// <param name="validatePortDirections">When true, skips ports whose GraphProcessor direction does not match output→input (matches main graph restore).</param>
        public static void RestoreEdges<TNode>(
            BaseGraphView graphView,
            IEnumerable<EdgeData>? edges,
            IReadOnlyDictionary<string, TNode> nodeMap,
            bool validatePortDirections = true)
            where TNode : BaseNode
        {
            if (graphView == null || edges == null || nodeMap == null || nodeMap.Count == 0)
                return;

            foreach (var edgeData in edges)
            {
                if (edgeData == null) continue;
                if (!nodeMap.TryGetValue(edgeData.FromNodeId, out var fromNode)) continue;
                if (!nodeMap.TryGetValue(edgeData.ToNodeId, out var toNode)) continue;
                if (!graphView.nodeViewsPerNode.TryGetValue(fromNode, out var fromNodeView)) continue;
                if (!graphView.nodeViewsPerNode.TryGetValue(toNode, out var toNodeView)) continue;

                var fromPort = fromNodeView.outputPortViews.FirstOrDefault(p =>
                    GraphViewPortStorage.IsPortMatchForStorage(p, edgeData.FromPort));
                var toPort = toNodeView.inputPortViews.FirstOrDefault(p =>
                    GraphViewPortStorage.IsPortMatchForStorage(p, edgeData.ToPort));
                if (fromPort == null || toPort == null) continue;

                if (validatePortDirections &&
                    (fromPort.direction != Direction.Output || toPort.direction != Direction.Input))
                    continue;

                if (graphView.edgeViews.Any(e => e.output == fromPort && e.input == toPort))
                    continue;

                graphView.Connect(toPort, fromPort);
            }
        }
    }
}
