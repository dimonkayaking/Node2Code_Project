#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Generators
{
    public partial class SimpleCodeGenerator
    {
        private const string SubGraphVariableRefMarker = "__varref:";
        private Dictionary<string, NodeData> _map = new();
        private GraphData _graph = new();
        private Stack<HashSet<string>> _scopeStack = new();
        private HashSet<string> _emitted = new();

        // Метаданные пользовательских методов; задаётся через перегрузку Generate/GenerateMethodBody.
        private IReadOnlyDictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();

        public string GenerateCode(GraphData graph) => Generate(graph);

        /// <summary>Генерация с учётом метаданных пользовательских методов.</summary>
        public string Generate(GraphData graph, IReadOnlyDictionary<string, MethodInfo> methods)
        {
            _methods = methods ?? new Dictionary<string, MethodInfo>();
            var result = Generate(graph);
            _methods = new Dictionary<string, MethodInfo>(); // сбрасываем после использования
            return result;
        }

        /// <summary>
        /// Генерирует тело метода (без сигнатуры и фигурных скобок).
        /// Параметры метода трактуются как уже объявленные переменные.
        /// </summary>
        public string GenerateMethodBody(GraphData bodyGraph, MethodInfo def,
            IReadOnlyDictionary<string, MethodInfo>? methods = default)
        {
            if (bodyGraph == null || bodyGraph.Nodes == null || bodyGraph.Nodes.Count == 0)
            {
                return def.ReturnType == "void"
                    ? "    // Тело пусто"
                    : $"    return {GetDefaultValue(def.ReturnType)};";
            }

            _methods    = methods ?? new Dictionary<string, MethodInfo>();
            _graph      = bodyGraph;
            _map        = bodyGraph.Nodes.ToDictionary(n => n.Id);
            _scopeStack = new Stack<HashSet<string>>();
            PushScope();
            _emitted    = new HashSet<string>();

            // Параметры уже объявлены в сигнатуре — вносим в скоуп
            if (def.ParamNames != null)
                foreach (var pn in def.ParamNames)
                    if (!string.IsNullOrEmpty(pn)) DeclareInCurrentScope(pn);

            var hasIncomingExec = new HashSet<string>(
                bodyGraph.Edges.Where(e => IsExecInPort(e.ToPort)).Select(e => e.ToNodeId));

            var roots = bodyGraph.Nodes
                .Where(n => n.Type != NodeType.MethodParam)          // param-ноды не операторы
                .Where(n => !hasIncomingExec.Contains(n.Id))
                .Where(n => IsStatementEntryNode(n))
                .Where(n => !IsChainedElseBranchTarget(n.Id))
                .ToList();

            var sb = new StringBuilder();
            var orderedRoots = TopologicallyOrderRoots(roots);
            foreach (var root in orderedRoots)
                EmitChain(root.Id, sb, 1);

            // Для non-void методов без явного Return-узла добавляем fallback return.
            // Если в графе есть хотя бы одна ReturnValue-нода — return уже эмитирован
            // внутри EmitChain → EmitReturn.
            if (def.ReturnType != "void" &&
                !bodyGraph.Nodes.Any(n => n.Type == NodeType.ReturnValue))
            {
                sb.AppendLine($"    return {GetDefaultValue(def.ReturnType)};");
            }

            var result = sb.ToString().TrimEnd();
            _methods = new Dictionary<string, MethodInfo>(); // сбрасываем
            return result;
        }

        public string Generate(GraphData graph)
        {
            if (graph == null || graph.Nodes.Count == 0)
                return "// Нет узлов для генерации";

            _graph = graph;
            _map = graph.Nodes.ToDictionary(n => n.Id);
            _scopeStack = new Stack<HashSet<string>>();
            PushScope();
            _emitted = new HashSet<string>();

            var hasIncomingExec = new HashSet<string>(
                graph.Edges.Where(e => IsExecInPort(e.ToPort)).Select(e => e.ToNodeId));

            var roots = graph.Nodes
                .Where(n => !hasIncomingExec.Contains(n.Id) && IsStatementEntryNode(n))
                .Where(n => !IsChainedElseBranchTarget(n.Id))
                .ToList();

            if (roots.Count == 0)
                return GenerateFallback();

            // Порядок создания нод в графе ни к чему не обязывает: надо эмитить источники
            // данных раньше их потребителей, иначе в коде появятся обращения к переменным
            // до их объявления.
            var orderedRoots = TopologicallyOrderRoots(roots);

            var sb = new StringBuilder();
            foreach (var root in orderedRoots)
                EmitChain(root.Id, sb, 0);

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Упорядочивает корни так, чтобы любой root, который данными питает другой root,
        /// стоял раньше своего потребителя (стабильная DFS-сортировка, циклы пропускаются).
        /// </summary>
        private List<NodeData> TopologicallyOrderRoots(List<NodeData> roots)
        {
            var rootIds = new HashSet<string>(roots.Select(r => r.Id));
            var visited = new HashSet<string>();
            var onStack = new HashSet<string>();
            var ordered = new List<NodeData>();

            void Visit(string nodeId)
            {
                if (visited.Contains(nodeId) || onStack.Contains(nodeId))
                    return;
                onStack.Add(nodeId);

                foreach (var depRootId in CollectDataRootDependencies(nodeId, rootIds))
                {
                    if (depRootId == nodeId)
                        continue;
                    Visit(depRootId);
                }

                onStack.Remove(nodeId);

                if (visited.Add(nodeId) && _map.TryGetValue(nodeId, out var node))
                    ordered.Add(node);
            }

            foreach (var root in roots)
                Visit(root.Id);

            return ordered;
        }

        /// <summary>
        /// Возвращает id всех root-нод, от которых через data-связи (любые не-exec рёбра)
        /// зависит <paramref name="nodeId"/> — прямо или транзитивно через промежуточные non-root узлы.
        /// </summary>
        private IEnumerable<string> CollectDataRootDependencies(string nodeId, HashSet<string> rootIds)
        {
            var seen = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var edge in _graph.Edges)
                {
                    if (edge.ToNodeId != current)
                        continue;
                    if (IsExecInPort(edge.ToPort) || IsExecOutPort(edge.FromPort))
                        continue;

                    var src = edge.FromNodeId;
                    if (!seen.Add(src))
                        continue;

                    if (rootIds.Contains(src))
                        yield return src;
                    else
                        queue.Enqueue(src);
                }
            }
        }

        private string GenerateFallback()
        {
            var sb = new StringBuilder();
            
            var variables = _graph.Nodes
                .Where(n => !string.IsNullOrEmpty(n.VariableName) && IsVariableDeclarationCandidate(n))
                .OrderBy(n => n.Id).ToList();

            foreach (var node in variables)
            {
                if (IsPlaceholderVariableRefLiteral(node))
                    continue;

                string valueExpr;
                
                // Если есть ExpressionOverride — используем его
                if (!string.IsNullOrEmpty(node.ExpressionOverride))
                {
                    valueExpr = node.ExpressionOverride;
                }
                else if (node.Type == NodeType.UnityVector3)
                {
                    valueExpr = EmitExpr(node.Id);
                }
                else
                {
                    var incomingEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "inputValue");

                    if (incomingEdge != null && _map.TryGetValue(incomingEdge.FromNodeId, out var sourceNode))
                    {
                        valueExpr = GetExpressionForNode(sourceNode);
                    }
                    else
                    {
                        valueExpr = GetDefaultValue(node.ValueType);
                    }
                }
                
                string type = GetKeywordForType(node.ValueType);
                sb.AppendLine($"{type} {node.VariableName} = {valueExpr};");
                DeclareInCurrentScope(node.VariableName);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Можно ли эмитить ноду как объявление переменной «тип имя = значение».
        /// Структурные (ClassNode/MethodOwner), параметры, вызовы, return и flow-ноды
        /// имеют непустой VariableName (имя класса/метода/параметра), но переменными
        /// НЕ являются — их нельзя выводить как «int MyClass = 0;».
        /// </summary>
        private static bool IsVariableDeclarationCandidate(NodeData n)
        {
            switch (n.Type)
            {
                case NodeType.ClassNode:
                case NodeType.MethodOwner:
                case NodeType.MethodParam:
                case NodeType.MethodCall:
                case NodeType.ReturnValue:
                case NodeType.FlowIf:
                case NodeType.FlowElse:
                case NodeType.FlowFor:
                case NodeType.FlowWhile:
                case NodeType.ConsoleWriteLine:
                case NodeType.DebugLog:
                case NodeType.FieldRef:
                case NodeType.FieldSet:
                case NodeType.UnityFieldSet:
                    return false;
                default:
                    return true;
            }
        }

        private void EmitChain(string nodeId, StringBuilder sb, int indent)
        {
            if (_emitted.Contains(nodeId))
                return;
            _emitted.Add(nodeId);

            var node = _map[nodeId];
            var pad = Pad(indent);

            switch (node.Type)
            {
                case NodeType.FlowIf:
                    EmitIf(node, sb, indent);
                    break;

                case NodeType.FlowElse:
                    break;

                case NodeType.FlowFor:
                    EmitFor(node, sb, indent);
                    break;

                case NodeType.FlowWhile:
                    EmitWhile(node, sb, indent);
                    break;

                case NodeType.ConsoleWriteLine:
                    EmitConsoleWriteLine(node, sb, pad);
                    break;

                case NodeType.DebugLog:
                    EmitDebugLog(node, sb, pad);
                    break;

                case NodeType.MethodCall:
                    EmitMethodCallStatement(node, sb, pad);
                    break;

                case NodeType.MethodParam:
                    // Объявления параметров — не операторы; пропускаем.
                    break;

                case NodeType.FieldRef:
                    // В режиме записи (value-вход подключён) — присваиваем поле.
                    EmitFieldRefWrite(node, sb, pad);
                    break;

                case NodeType.FieldSet:
                    EmitFieldSet(node, sb, pad);
                    break;

                case NodeType.ClassNode:
                case NodeType.MethodOwner:
                    // Структурные ноды — пропускаем.
                    break;

                case NodeType.ReturnValue:
                    EmitReturn(node, sb, pad);
                    // Нет exec-out → цепочка обрывается естественным образом (next == null).
                    return;

                case NodeType.UnityFieldSet:
                    EmitUnityFieldSet(node, sb, pad);
                    break;

                case NodeType.UnityMethodCall:
                    {
                        var member = UnityLibraryRegistry.FindMethod(node.Value, node.MemberName);
                        if (member != null && member.ReturnType == "void" && string.IsNullOrEmpty(node.VariableName))
                            sb.AppendLine($"{pad}{BuildUnityMethodCallExpr(node.Id)};");
                        else
                            EmitValueStatement(node, sb, pad);
                    }
                    break;

                default:
                    EmitValueStatement(node, sb, pad);
                    break;
            }

            var next = _graph.Edges.FirstOrDefault(
                e => e.FromNodeId == nodeId && IsExecOutPort(e.FromPort));
            if (next != null)
                EmitChain(next.ToNodeId, sb, indent);
        }

        private void EmitValueStatement(NodeData node, StringBuilder sb, string pad)
        {
            var vn = node.VariableName;
            if (string.IsNullOrEmpty(vn))
                return;
            if (IsPlaceholderVariableRefLiteral(node))
                return;
            if (IsSubGraphVariableRefLiteral(node))
                return;

            // Если есть ExpressionOverride — используем его
            if (!string.IsNullOrEmpty(node.ExpressionOverride))
            {
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {node.ExpressionOverride};");
                else
                {
                    string type = GetKeywordForType(node.ValueType);
                    sb.AppendLine($"{pad}{type} {vn} = {node.ExpressionOverride};");
                    DeclareInCurrentScope(vn);
                }
                return;
            }

            // Если есть литерал и нет входящей связи
            if (IsLiteral(node.Type) && !_graph.Edges.Any(e => e.ToNodeId == node.Id && e.ToPort == "inputValue"))
            {
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {LiteralRhs(node)};");
                else
                {
                    DeclareInCurrentScope(vn);
                    sb.AppendLine($"{pad}{KeywordFor(node.ValueType)} {vn} = {LiteralRhs(node)};");
                }
                return;
            }

            var incomingEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "inputValue");
            
            if (incomingEdge != null && _map.TryGetValue(incomingEdge.FromNodeId, out var sourceNode))
            {
                string valueExpr = GetExpressionForNode(sourceNode);
                
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {valueExpr};");
                else
                {
                    string type = GetKeywordForType(node.ValueType);
                    sb.AppendLine($"{pad}{type} {vn} = {valueExpr};");
                    DeclareInCurrentScope(vn);
                }
            }
            else if (IsLiteral(node.Type))
            {
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {LiteralRhs(node)};");
                else
                {
                    DeclareInCurrentScope(vn);
                    sb.AppendLine($"{pad}{KeywordFor(node.ValueType)} {vn} = {LiteralRhs(node)};");
                }
            }
            else if ((IsBinaryOp(node.Type) || node.Type == NodeType.UnityVector3
                        || node.Type == NodeType.UnityFieldAccess || node.Type == NodeType.UnityMethodCall)
                     && !string.IsNullOrEmpty(vn))
            {
                var expr = EmitStmtExpr(node.Id);
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {expr};");
                else
                {
                    DeclareInCurrentScope(vn);
                    sb.AppendLine($"{pad}{KeywordFor(InferResultType(node))} {vn} = {expr};");
                }
            }
        }

        private string GetExpressionForNode(NodeData node)
        {
            if (node == null)
                return "???";

            // MethodParam / FieldRef — предобъявленные переменные; используем имя как выражение
            if (node.Type is NodeType.MethodParam or NodeType.FieldRef)
                return !string.IsNullOrEmpty(node.VariableName) ? node.VariableName : "???";

            // MethodCall — генерируем вызов (VariableName == MethodName, не переменная-результат)
            if (node.Type == NodeType.MethodCall)
                return BuildMethodCallExpr(node.Id);

            if (!string.IsNullOrEmpty(node.VariableName))
                return node.VariableName;

            if (IsLiteral(node.Type))
            {
                if (!string.IsNullOrEmpty(node.ExpressionOverride) &&
                    !node.ExpressionOverride.StartsWith(SubGraphVariableRefMarker, StringComparison.Ordinal))
                    return node.ExpressionOverride;
                return LiteralRhs(node);
            }

            // Централизованный путь генерации выражений:
            // поддерживает math/compare/logical/parse/ToString/Mathf и т.д.
            return EmitExpr(node.Id);
        }

        private void EmitIf(NodeData ifNode, StringBuilder sb, int indent, bool inline = false)
        {
            var pad = Pad(indent);

            string condExpr;
            if (ifNode.ConditionSubGraph != null && ifNode.ConditionSubGraph.Nodes.Count > 0)
            {
                condExpr = GenerateExpressionFromSubGraph(ifNode.ConditionSubGraph);
            }
            else
            {
                var condEdge = _graph.Edges.FirstOrDefault(
                    e => e.ToNodeId == ifNode.Id && e.ToPort == "condition");
                condExpr = condEdge != null ? EmitCondExpr(condEdge.FromNodeId) : "true";
            }

            if (inline)
                sb.AppendLine($"if ({condExpr})");
            else
                sb.AppendLine($"{pad}if ({condExpr})");

            sb.AppendLine($"{pad}{{");
            PushScope();

            if (ifNode.BodySubGraph != null && ifNode.BodySubGraph.Nodes.Count > 0)
            {
                GenerateStatementsFromSubGraph(ifNode.BodySubGraph, sb, indent + 1);
            }
            else
            {
                var trueEdge = _graph.Edges.FirstOrDefault(
                    e => e.FromNodeId == ifNode.Id && e.FromPort == "true");
                if (trueEdge != null)
                    EmitChain(trueEdge.ToNodeId, sb, indent + 1);
            }
            PopScope();

            sb.AppendLine($"{pad}}}");

            if (!TryResolveIfFalseSuccessor(ifNode, out var elseNode) || elseNode == null)
                return;

            if (elseNode.Type == NodeType.FlowIf)
            {
                _emitted.Add(elseNode.Id);
                sb.Append($"{pad}else ");
                EmitIf(elseNode, sb, indent, inline: true);
            }
            else if (elseNode.Type == NodeType.FlowElse)
            {
                _emitted.Add(elseNode.Id);
                sb.AppendLine($"{pad}else");
                sb.AppendLine($"{pad}{{");
                PushScope();

                if (elseNode.BodySubGraph != null && elseNode.BodySubGraph.Nodes.Count > 0)
                {
                    GenerateStatementsFromSubGraph(elseNode.BodySubGraph, sb, indent + 1);
                }
                else
                {
                    var bodyEdge = _graph.Edges.FirstOrDefault(
                        e => e.FromNodeId == elseNode.Id && IsExecOutPort(e.FromPort));
                    if (bodyEdge != null)
                        EmitChain(bodyEdge.ToNodeId, sb, indent + 1);
                }
                PopScope();

                sb.AppendLine($"{pad}}}");
            }
        }

        private string GenerateExpressionFromSubGraph(GraphData subGraph)
        {
            var subMap = subGraph.Nodes.ToDictionary(n => n.Id);
            var outgoing = new HashSet<string>(subGraph.Edges.Select(e => e.FromNodeId));
            var sinkNodes = subGraph.Nodes.Where(n => !outgoing.Contains(n.Id)).ToList();

            if (sinkNodes.Count == 0)
                return "true";

            return EmitSubExpr(sinkNodes.Last().Id, subMap, subGraph, false);
        }

        private string EmitSubExpr(string nodeId, Dictionary<string, NodeData> map, GraphData graph, bool wrap)
        {
            if (!map.TryGetValue(nodeId, out var node)) return "???";
            if (!string.IsNullOrEmpty(node.VariableName)) return node.VariableName;
            if (IsLiteral(node.Type)) return LiteralRhs(node);

            string? SubIn(string port) =>
                graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == port)?.FromNodeId;

            if (IsMath(node.Type))
            {
                var l = SubIn("inputA");
                var r = SubIn("inputB");
                if (l == null || r == null) return "???";
                var expr = $"{EmitSubExpr(l, map, graph, true)} {MathOp(node.Type)} {EmitSubExpr(r, map, graph, true)}";
                return wrap ? $"({expr})" : expr;
            }

            if (IsCompare(node.Type))
            {
                var l = SubIn("left");
                var r = SubIn("right");
                if (l == null || r == null) return "???";
                var expr = $"{EmitSubExpr(l, map, graph, true)} {CmpOp(node.Type)} {EmitSubExpr(r, map, graph, true)}";
                return wrap ? $"({expr})" : expr;
            }

            if (node.Type is NodeType.LogicalAnd or NodeType.LogicalOr)
            {
                var l = SubIn("left");
                var r = SubIn("right");
                if (l == null || r == null) return "???";
                var op = node.Type == NodeType.LogicalAnd ? "&&" : "||";
                var expr = $"{EmitSubExpr(l, map, graph, true)} {op} {EmitSubExpr(r, map, graph, true)}";
                return wrap ? $"({expr})" : expr;
            }

            if (node.Type == NodeType.LogicalNot)
            {
                var i = SubIn("input");
                if (i == null) return "???";
                return $"!{EmitSubExpr(i, map, graph, true)}";
            }

            if (node.Type == NodeType.IntParse)
            {
                var s = SubIn("input");
                return s != null ? $"int.Parse({EmitSubExpr(s, map, graph, false)})" : "???";
            }

            if (node.Type == NodeType.FloatParse)
            {
                var s = SubIn("input");
                return s != null ? $"float.Parse({EmitSubExpr(s, map, graph, false)})" : "???";
            }

            if (node.Type == NodeType.ToStringConvert)
            {
                var v = SubIn("input");
                return v != null ? $"{EmitSubExpr(v, map, graph, true)}.ToString()" : "???";
            }

            if (node.Type == NodeType.MathfAbs)
            {
                var v = SubIn("input");
                return v != null ? $"Math.Abs({EmitSubExpr(v, map, graph, false)})" : "???";
            }

            if (node.Type is NodeType.MathfMax or NodeType.MathfMin)
            {
                var a = SubIn("inputA");
                var b = SubIn("inputB");
                if (a == null || b == null) return "???";
                var fn = node.Type == NodeType.MathfMax ? "Max" : "Min";
                return $"Math.{fn}({EmitSubExpr(a, map, graph, false)}, {EmitSubExpr(b, map, graph, false)})";
            }

            return "???";
        }

        private void GenerateStatementsFromSubGraph(GraphData subGraph, StringBuilder sb, int indent)
        {
            var savedMap = _map;
            var savedGraph = _graph;
            var savedEmitted = _emitted;

            _map = subGraph.Nodes.ToDictionary(n => n.Id);
            _graph = subGraph;
            _emitted = new HashSet<string>();

            var hasIncomingExec = new HashSet<string>(
                subGraph.Edges.Where(e => IsExecInPort(e.ToPort)).Select(e => e.ToNodeId));

            var roots = subGraph.Nodes
                .Where(n => !hasIncomingExec.Contains(n.Id) && IsStatementEntryNode(n))
                .Where(n => !IsChainedElseBranchTarget(n.Id))
                .ToList();

            foreach (var root in roots)
                EmitChain(root.Id, sb, indent);

            _map = savedMap;
            _graph = savedGraph;
            _emitted = savedEmitted;
        }

        private void EmitFor(NodeData forNode, StringBuilder sb, int indent)
        {
            var pad = Pad(indent);
            PushScope();
            var initStr = EmitForInitClause(forNode);
            var condStr = "";
            if (forNode.ConditionSubGraph != null && forNode.ConditionSubGraph.Nodes.Count > 0)
            {
                condStr = GenerateExpressionFromSubGraph(forNode.ConditionSubGraph);
            }
            else
            {
                var condEdge = _graph.Edges.FirstOrDefault(
                    e => e.ToNodeId == forNode.Id && e.ToPort == "condition");
                condStr = condEdge != null ? EmitCondExpr(condEdge.FromNodeId) : "";
            }
            var incStr = EmitForIncrementClause(forNode);
            sb.AppendLine($"{pad}for ({initStr}; {condStr}; {incStr})");
            sb.AppendLine($"{pad}{{");
            if (forNode.BodySubGraph != null && forNode.BodySubGraph.Nodes.Count > 0)
            {
                GenerateStatementsFromSubGraph(forNode.BodySubGraph, sb, indent + 1);
            }
            else
            {
                var bodyEdge = _graph.Edges.FirstOrDefault(
                    e => e.FromNodeId == forNode.Id && e.FromPort == "body");
                if (bodyEdge != null)
                    EmitChain(bodyEdge.ToNodeId, sb, indent + 1);
            }
            sb.AppendLine($"{pad}}}");
            PopScope();
        }

        private string GenerateForClauseFromSubGraph(GraphData subGraph)
        {
            if (subGraph == null || subGraph.Nodes.Count == 0) return "";
            var sb = new StringBuilder();
            GenerateStatementsFromSubGraph(subGraph, sb, 0);
            return sb.ToString().Replace(";\r\n", ", ").Replace(";\n", ", ").TrimEnd(',', ' ', '\r', '\n', ';');
        }

        private string EmitForInitClause(NodeData forNode)
        {
            if (forNode.InitSubGraph != null && forNode.InitSubGraph.Nodes.Count > 0)
                return GenerateForClauseFromSubGraph(forNode.InitSubGraph);

            var initEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == forNode.Id && e.ToPort == "init");
            if (initEdge == null)
                return "";

            var fromId = initEdge.FromNodeId;
            if (!_map.TryGetValue(fromId, out var n))
                return "";

            if (IsLiteral(n.Type) && !string.IsNullOrEmpty(n.VariableName))
                return $"{KeywordFor(n.ValueType)} {n.VariableName} = {LiteralRhs(n)}";

            return EmitStmtExpr(fromId);
        }

        private string EmitForIncrementClause(NodeData forNode)
        {
            if (forNode.IncrementSubGraph != null && forNode.IncrementSubGraph.Nodes.Count > 0)
                return GenerateForClauseFromSubGraph(forNode.IncrementSubGraph);

            var incEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == forNode.Id && e.ToPort == "increment");
            if (incEdge == null)
                return "";

            var fromId = incEdge.FromNodeId;
            return EmitStmtExpr(fromId);
        }

        private void EmitWhile(NodeData whileNode, StringBuilder sb, int indent)
        {
            var pad = Pad(indent);
            var condStr = "true";
            if (whileNode.ConditionSubGraph != null && whileNode.ConditionSubGraph.Nodes.Count > 0)
            {
                condStr = GenerateExpressionFromSubGraph(whileNode.ConditionSubGraph);
            }
            else
            {
                var condEdge = _graph.Edges.FirstOrDefault(
                    e => e.ToNodeId == whileNode.Id && e.ToPort == "condition");
                condStr = condEdge != null ? EmitCondExpr(condEdge.FromNodeId) : "true";
            }
            sb.AppendLine($"{pad}while ({condStr})");
            sb.AppendLine($"{pad}{{");
            PushScope();
            if (whileNode.BodySubGraph != null && whileNode.BodySubGraph.Nodes.Count > 0)
            {
                GenerateStatementsFromSubGraph(whileNode.BodySubGraph, sb, indent + 1);
            }
            else
            {
                var bodyEdge = _graph.Edges.FirstOrDefault(
                    e => e.FromNodeId == whileNode.Id && e.FromPort == "body");
                if (bodyEdge != null)
                    EmitChain(bodyEdge.ToNodeId, sb, indent + 1);
            }
            PopScope();
            sb.AppendLine($"{pad}}}");
        }

        private void EmitMethodCallStatement(NodeData node, StringBuilder sb, string pad)
        {
            sb.AppendLine($"{pad}{BuildMethodCallExpr(node.Id)};");
        }

        private void EmitReturn(NodeData node, StringBuilder sb, string pad)
        {
            var valueEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == node.Id && e.ToPort == "value");
            if (valueEdge != null)
                sb.AppendLine($"{pad}return {EmitExpr(valueEdge.FromNodeId)};");
            else
                sb.AppendLine($"{pad}return;");
        }

        /// <summary>
        /// Строит выражение вызова пользовательского метода: <c>Name(arg0, arg1, ...)</c>.
        /// Читает входные порты param0…paramN из текущего графа.
        /// </summary>
        private string BuildMethodCallExpr(string nodeId)
        {
            if (!_map.TryGetValue(nodeId, out var node)) return "???";

            var methodId   = node.Value;       // MethodId  (GUID)
            var methodName = node.VariableName; // MethodName (отображаемое имя)

            _methods.TryGetValue(methodId, out var def);

            // Если метод не найден в реестре (например, локальная inline-функция),
            // определяем количество аргументов по фактическим param*-рёбрам в графе.
            int paramCount;
            if (def?.ParamNames != null)
            {
                paramCount = def.ParamNames.Count;
            }
            else
            {
                paramCount = _graph.Edges.Count(e =>
                    e.ToNodeId == nodeId &&
                    e.ToPort.StartsWith("param", StringComparison.Ordinal) &&
                    int.TryParse(e.ToPort.AsSpan("param".Length), out _));
            }

            var args = new List<string>();
            for (int i = 0; i < paramCount; i++)
            {
                var port   = $"param{i}";
                var inEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == port);
                if (inEdge != null)
                {
                    args.Add(EmitExpr(inEdge.FromNodeId));
                }
                else
                {
                    var paramType = (def?.ParamTypes != null && i < def.ParamTypes.Count)
                        ? def.ParamTypes[i] : "int";
                    args.Add(GetDefaultValue(paramType));
                }
            }

            // Для статических методов с классом-владельцем добавляем префикс ClassName.
            var prefix = (!string.IsNullOrEmpty(def?.ClassName)) ? def.ClassName + "." : "";
            return $"{prefix}{methodName}({string.Join(", ", args)})";
        }

        /// <summary>
        /// Если к FieldRef подключён value-вход — это режим записи: emit <c>field = expr;</c>.
        /// Если value-вход не подключён — нода используется только для чтения; пропускаем.
        /// </summary>
        private void EmitFieldRefWrite(NodeData node, StringBuilder sb, string pad)
        {
            var valEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "value");
            if (valEdge == null) return; // режим чтения — не оператор
            var expr = EmitExpr(valEdge.FromNodeId);
            sb.AppendLine($"{pad}{node.VariableName} = {expr};");
        }

        private void EmitFieldSet(NodeData node, StringBuilder sb, string pad)
        {
            var fieldName = node.VariableName;
            if (string.IsNullOrEmpty(fieldName)) return;

            var valEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "value");
            var expr    = valEdge != null ? EmitExpr(valEdge.FromNodeId) : GetDefaultValue(node.ValueType);
            sb.AppendLine($"{pad}{fieldName} = {expr};");
        }

        private void EmitConsoleWriteLine(NodeData node, StringBuilder sb, string pad)
        {
            var msgEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == node.Id && e.ToPort == "message");
            var msg = msgEdge != null
                ? EmitCondExpr(msgEdge.FromNodeId)
                : FormatConsoleLiteral(node);
            sb.AppendLine($"{pad}Console.WriteLine({msg});");
        }

        private void EmitDebugLog(NodeData node, StringBuilder sb, string pad)
        {
            var msgEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == node.Id && e.ToPort == "message");
            var msg = msgEdge != null
                ? EmitCondExpr(msgEdge.FromNodeId)
                : FormatConsoleLiteral(node);
            sb.AppendLine($"{pad}Debug.Log({msg});");
        }

        private static string FormatConsoleLiteral(NodeData node)
        {
            var valueType = (node.ValueType ?? "").Trim().ToLowerInvariant();
            var raw = node.Value ?? "";
            return valueType switch
            {
                "int" => int.TryParse(raw, out var i) ? i.ToString() : "0",
                "float" => float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
                    ? $"{f.ToString(System.Globalization.CultureInfo.InvariantCulture)}f"
                    : "0f",
                "bool" => bool.TryParse(raw, out var b) ? b.ToString().ToLowerInvariant() : "false",
                "vector3" => $"{FormatVector3Literal(raw)}.ToString()",
                _ => $"\"{EscapeString(raw)}\""
            };
        }

        private string EmitExpr(string nodeId) => EmitCore(nodeId, true, null);
        private string EmitCondExpr(string nodeId) => EmitCore(nodeId, false, null);
        private string EmitStmtExpr(string nodeId) => EmitCore(nodeId, false, nodeId);

        private string EmitCore(string nodeId, bool wrap, string? selfId)
        {
            if (!_map.ContainsKey(nodeId))
                return "???";

            var node = _map[nodeId];

            // MethodCall: VariableName == MethodName (не переменная-результат) — строим вызов
            if (node.Type == NodeType.MethodCall)
                return BuildMethodCallExpr(nodeId);

            // MethodParam / FieldRef: предобъявленные переменные — просто имя
            if (node.Type is NodeType.MethodParam or NodeType.FieldRef)
                return !string.IsNullOrEmpty(node.VariableName) ? node.VariableName : "???";

            if (!string.IsNullOrEmpty(node.VariableName) && nodeId != selfId)
                return node.VariableName;

            if (IsLiteral(node.Type))
            {
                if (!string.IsNullOrEmpty(node.ExpressionOverride) &&
                    !node.ExpressionOverride.StartsWith(SubGraphVariableRefMarker, StringComparison.Ordinal))
                    return node.ExpressionOverride;
                return LiteralRhs(node);
            }

            if (IsMath(node.Type))
            {
                var l = Input(nodeId, "inputA");
                var r = Input(nodeId, "inputB");
                if (l == null || r == null) return "???";
                var expr = $"{EmitExpr(l)} {MathOp(node.Type)} {EmitExpr(r)}";
                return wrap ? $"({expr})" : expr;
            }

            if (IsCompare(node.Type))
            {
                var l = Input(nodeId, "left");
                var r = Input(nodeId, "right");
                if (l == null || r == null) return "???";
                var expr = $"{EmitExpr(l)} {CmpOp(node.Type)} {EmitExpr(r)}";
                return wrap ? $"({expr})" : expr;
            }

            if (node.Type is NodeType.LogicalAnd or NodeType.LogicalOr)
            {
                var l = Input(nodeId, "left");
                var r = Input(nodeId, "right");
                if (l == null || r == null) return "???";
                var op = node.Type == NodeType.LogicalAnd ? "&&" : "||";
                var expr = $"{EmitExpr(l)} {op} {EmitExpr(r)}";
                return wrap ? $"({expr})" : expr;
            }

            if (node.Type == NodeType.LogicalNot)
            {
                var i = Input(nodeId, "input");
                if (i == null) return "???";
                return $"!{EmitExpr(i)}";
            }

            if (node.Type == NodeType.IntParse)
            {
                var s = Input(nodeId, "input");
                if (s == null) return "???";
                return $"int.Parse({EmitExpr(s)})";
            }

            if (node.Type == NodeType.FloatParse)
            {
                var s = Input(nodeId, "input");
                if (s == null) return "???";
                return $"float.Parse({EmitExpr(s)})";
            }

            if (node.Type == NodeType.ToStringConvert)
            {
                var v = Input(nodeId, "input");
                if (v == null) return "???";
                return $"{EmitExpr(v)}.ToString()";
            }

            if (node.Type == NodeType.MathfAbs)
            {
                var v = Input(nodeId, "input");
                if (v == null) return "???";
                return $"Math.Abs({EmitExpr(v)})";
            }

            if (node.Type is NodeType.MathfMax or NodeType.MathfMin)
            {
                var a = Input(nodeId, "inputA");
                var b = Input(nodeId, "inputB");
                if (a == null || b == null) return "???";
                var fn = node.Type == NodeType.MathfMax ? "Max" : "Min";
                return $"Math.{fn}({EmitExpr(a)}, {EmitExpr(b)})";
            }

            if (node.Type == NodeType.UnityVector3)
            {
                var x = Input(nodeId, "X");
                var y = Input(nodeId, "Y");
                var z = Input(nodeId, "Z");
                var xs = x != null ? EmitExpr(x) : "0f";
                var ys = y != null ? EmitExpr(y) : "0f";
                var zs = z != null ? EmitExpr(z) : "0f";
                return $"new Vector3({xs}, {ys}, {zs})";
            }

            if (node.Type == NodeType.UnityFieldAccess)
                return BuildUnityFieldAccessExpr(nodeId);

            if (node.Type == NodeType.UnityMethodCall)
                return BuildUnityMethodCallExpr(nodeId);

            return "???";
        }

        /// <summary>Префикс получателя для члена Unity API: OwnerExpression (экземпляр) либо Value (класс — статический член).</summary>
        private static string ResolveOwnerPrefix(NodeData node) =>
            !string.IsNullOrEmpty(node.OwnerExpression) ? node.OwnerExpression : node.Value;

        /// <summary>Строит список аргументов вызова метода Unity API из портов param0..param3.</summary>
        private string BuildUnityCallArgs(string nodeId, UnityMemberInfo member)
        {
            var args = new List<string>();
            for (int i = 0; i < member.Parameters.Count && i < 4; i++)
            {
                var port = Input(nodeId, $"param{i}");
                if (port != null)
                    args.Add(EmitExpr(port));
                else if (!string.IsNullOrEmpty(member.Parameters[i].DefaultValue))
                    args.Add(member.Parameters[i].DefaultValue);
                else
                    args.Add("default");
            }
            return string.Join(", ", args);
        }

        /// <summary>Строит выражение вызова метода Unity API (UnityMethodCall) на основе UnityLibraryRegistry.</summary>
        private string BuildUnityMethodCallExpr(string nodeId)
        {
            var node = _map[nodeId];
            var member = UnityLibraryRegistry.FindMethod(node.Value, node.MemberName);
            if (member == null)
                return "???";
            var prefix = ResolveOwnerPrefix(node);
            var args = BuildUnityCallArgs(nodeId, member);
            return $"{prefix}.{node.MemberName}({args})";
        }

        /// <summary>Строит выражение доступа к полю/свойству Unity API (UnityFieldAccess).</summary>
        private string BuildUnityFieldAccessExpr(string nodeId)
        {
            var node = _map[nodeId];
            var prefix = ResolveOwnerPrefix(node);
            return $"{prefix}.{node.MemberName}";
        }

        /// <summary>Генерирует инструкцию записи в поле/свойство Unity API (UnityFieldSet).</summary>
        private void EmitUnityFieldSet(NodeData node, StringBuilder sb, string pad)
        {
            var prefix = ResolveOwnerPrefix(node);
            var valEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "value");
            var rhs = valEdge != null ? EmitExpr(valEdge.FromNodeId) : "default";
            sb.AppendLine($"{pad}{prefix}.{node.MemberName} = {rhs};");
        }

        private string? Input(string nodeId, string port)
        {
            var edge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == nodeId && e.ToPort == port);
            return edge?.FromNodeId;
        }

        private string InferResultType(NodeData node)
        {
            if (IsCompare(node.Type) || IsLogical(node.Type))
                return "bool";

            if (IsMath(node.Type))
            {
                var l = Input(node.Id, "inputA");
                var r = Input(node.Id, "inputB");
                if (l != null && OperandType(l) == "float") return "float";
                if (r != null && OperandType(r) == "float") return "float";
                return "int";
            }

            if (node.Type == NodeType.IntParse)
                return "int";
            if (node.Type == NodeType.FloatParse || node.Type is NodeType.MathfAbs or NodeType.MathfMax or NodeType.MathfMin)
                return "float";
            if (node.Type == NodeType.ToStringConvert)
                return "string";

            return !string.IsNullOrEmpty(node.ValueType) ? node.ValueType : "int";
        }

        private string OperandType(string nodeId)
        {
            if (!_map.ContainsKey(nodeId)) return "int";
            var n = _map[nodeId];
            if (!string.IsNullOrEmpty(n.ValueType)) return n.ValueType;
            if (n.Type == NodeType.LiteralFloat) return "float";
            if (IsMath(n.Type)) return InferResultType(n);
            return "int";
        }

        private static string Pad(int indent) => new string(' ', indent * 4);

        private static string LiteralRhs(NodeData n) => n.ValueType switch
        {
            "string" => $"\"{EscapeString(n.Value)}\"",
            "float" => $"{n.Value}f",
            "bool" => n.Value.ToLowerInvariant(),
            "Vector3" => FormatVector3Literal(n.Value),
            _ => n.Value
        };

        /// <summary>
        /// Преобразует значение Vector3-литерала ("x,y,z") в выражение C# <c>new Vector3(x, y, z)</c>.
        /// При некорректном/пустом значении подставляет 0 для каждой компоненты.
        /// </summary>
        private static string FormatVector3Literal(string value)
        {
            var parts = (value ?? "").Split(',');
            string Component(int i)
            {
                var raw = i < parts.Length ? parts[i].Trim() : "0";
                return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
                    ? f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "0";
            }
            return $"new Vector3({Component(0)}f, {Component(1)}f, {Component(2)}f)";
        }

        /// <summary>Экранирует спецсимволы строки для вставки в строковый литерал C#.</summary>
        private static string EscapeString(string s)
        {
            if (s == null) return "";
            return s
                .Replace("\\", "\\\\")   // сначала одиночный обратный слэш
                .Replace("\r\n", "\\r\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\"", "\\\"");
        }

        private static string KeywordFor(string? vt) => vt switch
        {
            "float" => "float",
            "bool" => "bool",
            "string" => "string",
            "Vector3" => "Vector3",
            _ => "int"
        };
        
        private static string GetKeywordForType(string? vt) => vt switch
        {
            "float" => "float",
            "bool" => "bool",
            "string" => "string",
            "Vector3" => "Vector3",
            _ => "int"
        };

        private static string GetDefaultValue(string? vt) => vt switch
        {
            "float" => "0f",
            "bool" => "false",
            "string" => "\"\"",
            "Vector3" => "new Vector3(0f, 0f, 0f)",
            _ => "0"
        };

        private static string MathOp(NodeType t) => t switch
        {
            NodeType.MathAdd => "+",
            NodeType.MathSubtract => "-",
            NodeType.MathMultiply => "*",
            NodeType.MathDivide => "/",
            NodeType.MathModulo => "%",
            _ => "+"
        };

        private static string CmpOp(NodeType t) => t switch
        {
            NodeType.CompareEqual => "==",
            NodeType.CompareNotEqual => "!=",
            NodeType.CompareGreater => ">",
            NodeType.CompareLess => "<",
            NodeType.CompareGreaterOrEqual => ">=",
            NodeType.CompareLessOrEqual => "<=",
            _ => "=="
        };

        private static bool IsLiteral(NodeType t) =>
            t is NodeType.LiteralBool or NodeType.LiteralInt
                or NodeType.LiteralFloat or NodeType.LiteralString;

        private static bool IsMath(NodeType t) =>
            t is NodeType.MathAdd or NodeType.MathSubtract or NodeType.MathMultiply
                or NodeType.MathDivide or NodeType.MathModulo;

        private static bool IsCompare(NodeType t) =>
            t is NodeType.CompareEqual or NodeType.CompareNotEqual
                or NodeType.CompareGreater or NodeType.CompareLess
                or NodeType.CompareGreaterOrEqual or NodeType.CompareLessOrEqual;

        private static bool IsLogical(NodeType t) =>
            t is NodeType.LogicalAnd or NodeType.LogicalOr or NodeType.LogicalNot;

        private static bool IsBinaryOp(NodeType t) =>
            IsMath(t) || IsCompare(t) || t is NodeType.LogicalAnd or NodeType.LogicalOr;

        private static bool IsBuiltinExpressionNode(NodeType t) =>
            t is NodeType.IntParse or NodeType.FloatParse or NodeType.ToStringConvert
                or NodeType.MathfAbs or NodeType.MathfMax or NodeType.MathfMin;

        private bool IsStatementEntryNode(NodeData n)
        {
            if (n.Type == NodeType.MethodParam) return false;

            // FieldRef — оператор только в режиме записи (value-вход подключён)
            if (n.Type == NodeType.FieldRef)
                return _graph.Edges.Any(e => e.ToNodeId == n.Id && e.ToPort == "value");

            if (n.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile
                or NodeType.ConsoleWriteLine or NodeType.DebugLog or NodeType.ReturnValue
                or NodeType.FieldSet)
                return true;

            // MethodCall — оператор, если:
            //   • метод void (нет полезного возврата), ИЛИ
            //   • выходной порт ни к чему не подключён (результат не используется)
            if (n.Type == NodeType.MethodCall)
            {
                if (_methods.TryGetValue(n.Value, out var def) && def.ReturnType != "void")
                    return !_graph.Edges.Any(e => e.FromNodeId == n.Id && e.FromPort == "output");
                return true; // void method or unknown → always a statement
            }

            if (IsLiteral(n.Type) && !string.IsNullOrEmpty(n.VariableName) &&
                !IsPlaceholderVariableRefLiteral(n) && !IsSubGraphVariableRefLiteral(n))
                return true;

            if ((IsBinaryOp(n.Type) || n.Type == NodeType.LogicalNot || IsBuiltinExpressionNode(n.Type)
                    || n.Type == NodeType.UnityVector3) &&
                !string.IsNullOrEmpty(n.VariableName))
                return true;

            // UnityFieldSet — всегда оператор (запись поля/свойства).
            if (n.Type == NodeType.UnityFieldSet)
                return true;

            // UnityFieldAccess — оператор-точка входа только если результат присваивается переменной.
            if (n.Type == NodeType.UnityFieldAccess && !string.IsNullOrEmpty(n.VariableName))
                return true;

            // UnityMethodCall — оператор, если результат присваивается переменной,
            // либо метод void (вызывается как самостоятельная инструкция).
            if (n.Type == NodeType.UnityMethodCall)
            {
                if (!string.IsNullOrEmpty(n.VariableName))
                    return true;
                var member = UnityLibraryRegistry.FindMethod(n.Value, n.MemberName);
                if (member != null && member.ReturnType == "void")
                    return true;
            }

            return false;
        }

        private bool IsPlaceholderVariableRefLiteral(NodeData node)
        {
            if (!IsLiteral(node.Type) || string.IsNullOrEmpty(node.VariableName))
                return false;
            if (!string.IsNullOrEmpty(node.ExpressionOverride))
                return false;
            if (!string.IsNullOrEmpty(node.Value))
                return false;

            // Subgraph variable refs are expression helpers and must not be emitted as statements.
            var hasInputValue = _graph.Edges.Any(e =>
                e.ToNodeId == node.Id &&
                string.Equals(e.ToPort, "inputValue", StringComparison.OrdinalIgnoreCase));
            return !hasInputValue;
        }

        private bool IsSubGraphVariableRefLiteral(NodeData node)
        {
            if (!IsLiteral(node.Type) || string.IsNullOrEmpty(node.VariableName))
                return false;
            if (string.IsNullOrEmpty(node.ExpressionOverride) ||
                !node.ExpressionOverride.StartsWith(SubGraphVariableRefMarker, StringComparison.Ordinal))
                return false;

            var hasInputValue = _graph.Edges.Any(e =>
                e.ToNodeId == node.Id &&
                string.Equals(e.ToPort, "inputValue", StringComparison.OrdinalIgnoreCase));
            return !hasInputValue;
        }

        /// <summary>
        /// else if / else не должны быть корнями: на них уже есть вход с false-выхода родительского if.
        /// Иначе при порядке нод в списке средний if обрабатывается раньше внешнего и ломает цепочку.
        /// </summary>
        private bool IsChainedElseBranchTarget(string nodeId)
        {
            return _graph.Edges.Any(e =>
                e.ToNodeId == nodeId &&
                _map.TryGetValue(e.FromNodeId, out var from) &&
                from.Type == NodeType.FlowIf &&
                IsFalseBranchOutputPort(e.FromPort));
        }

        /// <summary>
        /// Имя выхода «ложь» в редакторе может совпадать с полем C# (falseBranch) или с именем из [Output].
        /// </summary>
        private static bool IsFalseBranchOutputPort(string? fromPort)
        {
            if (string.IsNullOrEmpty(fromPort))
                return false;
            return PortIds.IsFalseBranch(fromPort);
        }

        /// <summary>
        /// Ветка false на основном графе разрешается только по явному false-порту.
        /// Это предотвращает склейку обычных exec-связей в else-if цепочки.
        /// </summary>
        private bool TryResolveIfFalseSuccessor(NodeData ifNode, out NodeData? successor)
        {
            successor = null;
            var ifId = ifNode.Id;
            var explicitFalse = _graph.Edges.FirstOrDefault(e =>
                e.FromNodeId == ifId &&
                IsFalseBranchOutputPort(e.FromPort) &&
                _map.TryGetValue(e.ToNodeId, out var targetNode) &&
                (targetNode.Type == NodeType.FlowIf || targetNode.Type == NodeType.FlowElse));

            if (explicitFalse == null)
            {
                // Compatibility fallback: some persisted editor graphs can lose falseBranch
                // and keep only execOut for If -> (If|Else). Treat it as a false branch only
                // when target is a flow branch node.
                var legacyExecOut = _graph.Edges.FirstOrDefault(e =>
                    e.FromNodeId == ifId &&
                    IsExecOutPort(e.FromPort) &&
                    _map.TryGetValue(e.ToNodeId, out var targetNode) &&
                    (targetNode.Type == NodeType.FlowIf || targetNode.Type == NodeType.FlowElse));

                if (legacyExecOut == null)
                    return false;

                successor = _map[legacyExecOut.ToNodeId];
                return true;
            }

            successor = _map[explicitFalse.ToNodeId];
            return true;
        }

        private static bool IsExecInPort(string? toPort)
        {
            if (string.IsNullOrEmpty(toPort)) return false;
            return PortIds.IsExecIn(toPort);
        }

        private static bool IsExecOutPort(string? fromPort)
        {
            if (string.IsNullOrEmpty(fromPort)) return false;
            return PortIds.IsExecOut(fromPort);
        }
    }
}