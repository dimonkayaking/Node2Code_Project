using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Views;

namespace CustomVisualScripting.Editor
{
    /// <summary>
    /// Общая DAG-раскладка и снятие перекрытий для основного графа, вкладок подпространств и вложенных <see cref="Nodes.Views.SubGraphPanel"/>.
    /// </summary>
    public static class GraphViewAutoLayout
    {
        public const float DefaultMinNodeWidth = 220f;
        public const float DefaultMinNodeHeight = 120f;
        public const float AutoLayoutSpacingX = 280f;
        public const float AutoLayoutSpacingY = 180f;
        public const float AutoLayoutColumnGap = 60f;
        public const float AutoLayoutRowGap = 40f;
        public const float OverlapResolveMargin = 24f;

        private const float NestedRetrySpacingX = 420f;
        private const float NestedRetrySpacingY = 280f;

        public static bool HasMeaningfulVisualLayout(GraphData graphData, int nodeViewCount)
        {
            if (graphData?.VisualLayout == null || graphData.VisualLayout.Count == 0)
                return false;
            if (graphData.VisualLayout.Count < nodeViewCount)
                return false;

            var unique = new HashSet<string>();
            foreach (var e in graphData.VisualLayout)
            {
                if (e == null || string.IsNullOrEmpty(e.NodeId))
                    continue;
                var x = Mathf.RoundToInt(e.X);
                var y = Mathf.RoundToInt(e.Y);
                unique.Add($"{x}:{y}");
            }

            return unique.Count > Math.Max(1, nodeViewCount / 3);
        }

        public static bool HasHeavyOverlap(IReadOnlyList<BaseNodeView> nodeViews)
        {
            if (nodeViews == null || nodeViews.Count <= 1)
                return false;

            int overlaps = 0;
            for (int i = 0; i < nodeViews.Count; i++)
            {
                for (int j = i + 1; j < nodeViews.Count; j++)
                {
                    if (nodeViews[i].GetPosition().Overlaps(nodeViews[j].GetPosition()))
                        overlaps++;
                }
            }

            return overlaps >= Math.Max(1, nodeViews.Count / 3);
        }

        public static void ResolveOverlaps(IReadOnlyList<BaseNodeView> nodeViews)
        {
            var customViews = nodeViews.Where(v => v?.nodeTarget is CustomBaseNode).ToList();
            if (customViews.Count <= 1)
                return;

            const int maxPasses = 4;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool movedAny = false;
                for (int i = 0; i < customViews.Count; i++)
                {
                    var aView = customViews[i];
                    var a = aView.GetPosition();
                    for (int j = i + 1; j < customViews.Count; j++)
                    {
                        var bView = customViews[j];
                        var b = bView.GetPosition();
                        if (!a.Overlaps(b))
                            continue;

                        float moveX = Mathf.Max(0f, a.xMax - b.xMin) + OverlapResolveMargin;
                        float moveY = Mathf.Max(0f, a.yMax - b.yMin) + OverlapResolveMargin;
                        if (moveX <= 0f && moveY <= 0f)
                            continue;

                        if (moveX <= moveY)
                            b.x += moveX;
                        else
                            b.y += moveY;

                        bView.SetPosition(b);
                        movedAny = true;
                    }
                }

                if (!movedAny)
                    return;
            }
        }

        public static (float w, float h) MeasureMainGraphCell(BaseNodeView view)
        {
            var rect = view.GetPosition();
            return (
                Mathf.Max(rect.width, DefaultMinNodeWidth),
                Mathf.Max(rect.height, DefaultMinNodeHeight));
        }

        /// <summary>
        /// Политика как у основного графа: нет осмысленных сохранённых позиций (VisualNodes) или сильное перекрытие.
        /// </summary>
        public static void ApplyIfNeededForMainGraph(
            GraphData logicGraph,
            IReadOnlyList<BaseNodeView> nodeViews,
            bool hasMeaningfulSavedPositions)
        {
            if (nodeViews == null || nodeViews.Count == 0)
                return;

            bool needLayout = !hasMeaningfulSavedPositions || HasHeavyOverlap(nodeViews);
            if (!needLayout)
                return;

            ApplyDagAutoLayout(
                logicGraph,
                nodeViews,
                MeasureMainGraphCell,
                AutoLayoutSpacingX,
                AutoLayoutSpacingY,
                40f,
                40f,
                AutoLayoutColumnGap,
                AutoLayoutRowGap,
                DefaultMinNodeWidth);

            ResolveOverlaps(nodeViews);
        }

        /// <summary>
        /// Политика для вложенного <see cref="GraphData"/>: нет осмысленного <see cref="GraphData.VisualLayout"/> или перекрытие; при необходимости второй проход с большим шагом.
        /// </summary>
        public static void ApplyIfNeededForNestedGraph(
            GraphData subGraph,
            IReadOnlyList<BaseNodeView> nodeViews,
            Func<BaseNodeView, (float w, float h)> measureCell)
        {
            if (nodeViews == null || nodeViews.Count == 0 || measureCell == null)
                return;

            bool meaningful = HasMeaningfulVisualLayout(subGraph, nodeViews.Count);
            bool needLayout = !meaningful || HasHeavyOverlap(nodeViews);
            if (!needLayout)
                return;

            ApplyDagForNested(subGraph, nodeViews, measureCell, AutoLayoutSpacingX, AutoLayoutSpacingY);
            ResolveOverlaps(nodeViews);

            if (HasHeavyOverlap(nodeViews))
            {
                ApplyDagForNested(subGraph, nodeViews, measureCell, NestedRetrySpacingX, NestedRetrySpacingY);
                ResolveOverlaps(nodeViews);
            }
        }

        private static void ApplyDagForNested(
            GraphData subGraph,
            IReadOnlyList<BaseNodeView> nodeViews,
            Func<BaseNodeView, (float w, float h)> measureCell,
            float spacingX,
            float spacingY)
        {
            ApplyDagAutoLayout(
                subGraph,
                nodeViews,
                measureCell,
                spacingX,
                spacingY,
                30f,
                30f,
                40f,
                24f,
                NodeViewBoundsUtils.DefaultGraphNodeMinWidth);
        }

        public static void ApplyDagAutoLayout(
            GraphData logicGraph,
            IReadOnlyList<BaseNodeView> nodeViews,
            Func<BaseNodeView, (float w, float h)> measureCell,
            float spacingX,
            float spacingY,
            float startX,
            float startY,
            float columnGapFloor,
            float rowGapFloor,
            float layerMaxWidthSeed)
        {
            var customViews = nodeViews.Where(v => v?.nodeTarget is CustomBaseNode).ToList();
            if (customViews.Count == 0)
                return;

            var nodeById = new Dictionary<string, BaseNodeView>(StringComparer.Ordinal);
            foreach (var view in customViews)
            {
                var node = view.nodeTarget as CustomBaseNode;
                if (node != null && !string.IsNullOrEmpty(node.NodeId))
                    nodeById[node.NodeId] = view;
            }

            var outgoing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var incoming = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var incomingCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var nodeId in nodeById.Keys)
            {
                outgoing[nodeId] = new HashSet<string>(StringComparer.Ordinal);
                incoming[nodeId] = new HashSet<string>(StringComparer.Ordinal);
                incomingCount[nodeId] = 0;
            }

            if (logicGraph?.Edges != null)
            {
                foreach (var edge in logicGraph.Edges)
                {
                    if (edge == null || string.IsNullOrEmpty(edge.FromNodeId) || string.IsNullOrEmpty(edge.ToNodeId))
                        continue;
                    if (!nodeById.ContainsKey(edge.FromNodeId) || !nodeById.ContainsKey(edge.ToNodeId))
                        continue;
                    if (edge.FromNodeId == edge.ToNodeId)
                        continue;

                    if (outgoing[edge.FromNodeId].Add(edge.ToNodeId))
                    {
                        incoming[edge.ToNodeId].Add(edge.FromNodeId);
                        incomingCount[edge.ToNodeId]++;
                    }
                }
            }

            var nodeTypeById = new Dictionary<string, NodeType>(StringComparer.Ordinal);
            if (logicGraph?.Nodes != null)
            {
                foreach (var n in logicGraph.Nodes)
                {
                    if (n != null && !string.IsNullOrEmpty(n.Id))
                        nodeTypeById[n.Id] = n.Type;
                }
            }

            var inDegreeOriginal = incomingCount.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var depthById = new Dictionary<string, int>(StringComparer.Ordinal);
            var rootIds = incomingCount
                .Where(kv => kv.Value == 0)
                .Select(kv => kv.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            foreach (var rootId in rootIds)
                depthById[rootId] = 0;

            var queue = new Queue<string>(rootIds);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int currentDepth = depthById.TryGetValue(current, out var d) ? d : 0;
                foreach (var next in outgoing[current].OrderBy(id => id, StringComparer.Ordinal))
                {
                    int nextDepth = currentDepth + 1;
                    if (!depthById.TryGetValue(next, out var existingDepth) || nextDepth > existingDepth)
                        depthById[next] = nextDepth;

                    incomingCount[next]--;
                    if (incomingCount[next] == 0)
                        queue.Enqueue(next);
                }
            }

            int maxDepth = depthById.Count == 0 ? 0 : depthById.Values.Max();
            var unresolvedIds = nodeById.Keys
                .Where(id => !depthById.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < unresolvedIds.Count; i++)
                depthById[unresolvedIds[i]] = maxDepth + 1 + i;

            var layers = depthById
                .GroupBy(kv => kv.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

            var laneCache = new Dictionary<string, int>(StringComparer.Ordinal);
            int GetBranchLane(string nodeId, HashSet<string> visiting = null)
            {
                if (laneCache.TryGetValue(nodeId, out var cached))
                    return cached;

                if (nodeTypeById.TryGetValue(nodeId, out var type))
                {
                    if (type == NodeType.FlowIf)
                        return laneCache[nodeId] = 1;
                    if (type == NodeType.FlowElse)
                        return laneCache[nodeId] = 2;
                }

                visiting ??= new HashSet<string>(StringComparer.Ordinal);
                if (!visiting.Add(nodeId))
                    return 0;

                int lane = 0;
                foreach (var parent in incoming[nodeId])
                    lane = Mathf.Max(lane, GetBranchLane(parent, visiting));

                visiting.Remove(nodeId);
                laneCache[nodeId] = lane;
                return lane;
            }

            int TypePriority(string nodeId)
            {
                if (!nodeTypeById.TryGetValue(nodeId, out var type))
                    return 50;

                switch (type)
                {
                    case NodeType.LiteralBool:
                    case NodeType.LiteralInt:
                    case NodeType.LiteralFloat:
                    case NodeType.LiteralString:
                        return 10;

                    case NodeType.MathAdd:
                    case NodeType.MathSubtract:
                    case NodeType.MathMultiply:
                    case NodeType.MathDivide:
                    case NodeType.MathModulo:
                    case NodeType.CompareEqual:
                    case NodeType.CompareGreater:
                    case NodeType.CompareLess:
                    case NodeType.CompareNotEqual:
                    case NodeType.CompareGreaterOrEqual:
                    case NodeType.CompareLessOrEqual:
                    case NodeType.LogicalAnd:
                    case NodeType.LogicalOr:
                    case NodeType.LogicalNot:
                    case NodeType.MathfAbs:
                    case NodeType.MathfMax:
                    case NodeType.MathfMin:
                    case NodeType.IntParse:
                    case NodeType.FloatParse:
                    case NodeType.ToStringConvert:
                    case NodeType.UnityVector3:
                    case NodeType.UnityGetPosition:
                        return 20;

                    case NodeType.FlowIf:
                    case NodeType.FlowElse:
                    case NodeType.FlowFor:
                    case NodeType.FlowWhile:
                        return 30;

                    case NodeType.ConsoleWriteLine:
                    case NodeType.DebugLog:
                    case NodeType.UnitySetPosition:
                        return 40;

                    default:
                        return 50;
                }
            }

            foreach (var layer in layers.Values)
            {
                layer.Sort((a, b) =>
                {
                    int laneCmp = GetBranchLane(a).CompareTo(GetBranchLane(b));
                    if (laneCmp != 0)
                        return laneCmp;

                    int typeCmp = TypePriority(a).CompareTo(TypePriority(b));
                    if (typeCmp != 0)
                        return typeCmp;

                    int inCmp = inDegreeOriginal[a].CompareTo(inDegreeOriginal[b]);
                    if (inCmp != 0)
                        return inCmp;

                    int outCmp = outgoing[b].Count.CompareTo(outgoing[a].Count);
                    if (outCmp != 0)
                        return outCmp;

                    return StringComparer.Ordinal.Compare(a, b);
                });
            }

            float columnGap = Mathf.Max(columnGapFloor, spacingX * 0.2f);
            float rowGap = Mathf.Max(rowGapFloor, spacingY * 0.2f);
            float columnX = startX;
            foreach (var layerEntry in layers)
            {
                var ids = layerEntry.Value;
                float layerMaxWidth = layerMaxWidthSeed;
                float rowY = startY;
                for (int row = 0; row < ids.Count; row++)
                {
                    var view = nodeById[ids[row]];
                    var rect = view.GetPosition();
                    var (mw, mh) = measureCell(view);
                    float width = Mathf.Max(rect.width, mw);
                    float height = Mathf.Max(rect.height, mh);
                    layerMaxWidth = Mathf.Max(layerMaxWidth, width);
                    view.SetPosition(new Rect(columnX, rowY, width, height));
                    rowY += height + rowGap;
                }

                columnX += layerMaxWidth + columnGap;
            }
        }
    }
}
