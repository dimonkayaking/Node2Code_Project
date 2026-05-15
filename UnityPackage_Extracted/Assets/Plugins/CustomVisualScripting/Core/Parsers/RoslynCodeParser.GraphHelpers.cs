using System.Linq;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Parsers
{
    public partial class RoslynCodeParser
    {
        private string GetDataOutPortForNodeId(string nodeId)
        {
            var n = _graph.Nodes.FirstOrDefault(x => x.Id == nodeId);
            if (n == null)
                return "output";
            return GetDataOutPort(n.Type);
        }

        private static string GetDataOutPort(NodeType type)
        {
            if (IsMath(type))
                return "output";
            return type switch
            {
                NodeType.LiteralBool or NodeType.LiteralInt or NodeType.LiteralFloat or NodeType.LiteralString => "output",
                NodeType.CompareEqual or NodeType.CompareGreater or NodeType.CompareLess
                    or NodeType.CompareNotEqual or NodeType.CompareGreaterOrEqual
                    or NodeType.CompareLessOrEqual => "result",
                NodeType.LogicalAnd or NodeType.LogicalOr or NodeType.LogicalNot => "result",
                NodeType.IntParse or NodeType.FloatParse or NodeType.ToStringConvert
                    or NodeType.MathfAbs or NodeType.MathfMax or NodeType.MathfMin => "output",
                _ => "output"
            };
        }

        private void AddEdge(string fromId, string fromPort, string toId, string toPort)
        {
            var normalizedFrom = PortIds.Normalize(fromPort);
            var normalizedTo = PortIds.Normalize(toPort);

            if (PortIds.IsExecOut(normalizedFrom) || PortIds.IsFalseBranch(normalizedFrom))
            {
                if (!SupportsExecOut(fromId))
                    return;
            }

            if (PortIds.IsExecIn(normalizedTo))
            {
                if (!SupportsExecIn(toId))
                    return;
            }

            _graph.Edges.Add(new EdgeData
            {
                FromNodeId = fromId,
                FromPort = normalizedFrom,
                ToNodeId = toId,
                ToPort = normalizedTo
            });
        }

        private bool SupportsExecOut(string nodeId)
        {
            var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
                return false;

            return node.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile
                or NodeType.ConsoleWriteLine or NodeType.DebugLog;
        }

        private bool SupportsExecIn(string nodeId)
        {
            var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
                return false;

            return node.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile
                or NodeType.ConsoleWriteLine or NodeType.DebugLog;
        }

        private string NewId() => $"node_{_nodeCounter++}";

        /// <summary>Ищет ноду по id в корневом графе и во всех вложенных подграфах (условие, тело, for-init и т.д.).</summary>
        private static NodeData FindNodeByIdInTree(GraphData graph, string nodeId)
        {
            if (graph == null || string.IsNullOrEmpty(nodeId))
                return null;

            foreach (var n in graph.Nodes)
            {
                if (n.Id == nodeId)
                    return n;
            }

            foreach (var n in graph.Nodes)
            {
                var found = FindNodeByIdInTree(n.ConditionSubGraph, nodeId)
                            ?? FindNodeByIdInTree(n.BodySubGraph, nodeId)
                            ?? FindNodeByIdInTree(n.InitSubGraph, nodeId)
                            ?? FindNodeByIdInTree(n.IncrementSubGraph, nodeId);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
