using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Generators
{
    public class SimpleCodeGenerator
    {
        private Dictionary<string, NodeData> _map = new();
        private GraphData _graph = new();
        private Stack<HashSet<string>> _scopeStack = new();
        private HashSet<string> _emitted = new();

        private void PushScope() => _scopeStack.Push(new HashSet<string>());

        private void PopScope()
        {
            if (_scopeStack.Count > 1)
                _scopeStack.Pop();
        }

        private void DeclareInCurrentScope(string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
                return;
            if (_scopeStack.Count == 0)
                PushScope();
            _scopeStack.Peek().Add(variableName);
        }

        private bool IsVisibleInAnyScope(string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
                return false;
            foreach (var scope in _scopeStack)
            {
                if (scope.Contains(variableName))
                    return true;
            }
            return false;
        }

        public string GenerateCode(GraphData graph) => Generate(graph);

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

            // Первая инструкция в методе не имеет входящего execIn; одиночный Console.WriteLine тоже.
            var roots = graph.Nodes
                .Where(n => !hasIncomingExec.Contains(n.Id) && IsStatementEntryNode(n))
                .Where(n => !IsChainedElseBranchTarget(n.Id))
                .ToList();

            if (roots.Count == 0)
                return GenerateFallback();

            var sb = new StringBuilder();
            foreach (var root in roots)
                EmitChain(root.Id, sb, 0);

            return sb.ToString().TrimEnd();
        }

        private string GenerateFallback()
        {
            var sb = new StringBuilder();
            foreach (var node in _graph.Nodes)
            {
                if (IsLiteral(node.Type) && !string.IsNullOrEmpty(node.VariableName))
                {
                    var valueEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "inputValue");
                    string rhs = valueEdge != null ? EmitCondExpr(valueEdge.FromNodeId) : LiteralRhs(node);
                    sb.AppendLine($"{KeywordFor(node.ValueType)} {node.VariableName} = {rhs};");
                    DeclareInCurrentScope(node.VariableName);
                }
                else if (IsBinaryOp(node.Type) && !string.IsNullOrEmpty(node.VariableName))
                {
                    var type = InferResultType(node);
                    sb.AppendLine($"{KeywordFor(type)} {node.VariableName} = {EmitStmtExpr(node.Id)};");
                    DeclareInCurrentScope(node.VariableName);
                }
            }
            return sb.ToString().TrimEnd();
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

            if (IsLiteral(node.Type))
            {
                var valueEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == node.Id && e.ToPort == "inputValue");
                string rhs;
                // Граф-обход имеет приоритет: даёт правильные скобки и Math.* имена.
                // ExpressionOverride используется только как fallback (тернарник, Sqrt и т.п. без edge).
                if (valueEdge != null)
                    rhs = EmitCondExpr(valueEdge.FromNodeId);
                else if (!string.IsNullOrEmpty(node.ExpressionOverride))
                    rhs = node.ExpressionOverride;
                else
                    rhs = LiteralRhs(node);

                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {rhs};");
                else
                {
                    DeclareInCurrentScope(vn);
                    sb.AppendLine($"{pad}{KeywordFor(node.ValueType)} {vn} = {rhs};");
                }
                return;
            }

            if (IsBinaryOp(node.Type) || node.Type == NodeType.LogicalNot)
            {
                var expr = EmitStmtExpr(node.Id);
                if (IsVisibleInAnyScope(vn))
                    sb.AppendLine($"{pad}{vn} = {expr};");
                else
                {
                    DeclareInCurrentScope(vn);
                    sb.AppendLine($"{pad}{KeywordFor(InferResultType(node))} {vn} = {expr};");
                }
                return;
            }

            if (IsBuiltinExpressionNode(node.Type))
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

            if (!TryResolveIfFalseSuccessor(ifNode, out var target) || target == null)
                return;

            if (target.Type == NodeType.FlowIf)
            {
                _emitted.Add(target.Id);
                sb.Append($"{pad}else ");
                EmitIf(target, sb, indent, inline: true);
            }
            else if (target.Type == NodeType.FlowElse)
            {
                _emitted.Add(target.Id);
                sb.AppendLine($"{pad}else");
                sb.AppendLine($"{pad}{{");
                PushScope();

                if (target.BodySubGraph != null && target.BodySubGraph.Nodes.Count > 0)
                {
                    GenerateStatementsFromSubGraph(target.BodySubGraph, sb, indent + 1);
                }
                else
                {
                    var bodyEdge = _graph.Edges.FirstOrDefault(
                        e => e.FromNodeId == target.Id && IsExecOutPort(e.FromPort));
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
                return v != null ? $"System.Math.Abs({EmitSubExpr(v, map, graph, false)})" : "???";
            }

            if (node.Type is NodeType.MathfMax or NodeType.MathfMin)
            {
                var a = SubIn("inputA");
                var b = SubIn("inputB");
                if (a == null || b == null) return "???";
                var fn = node.Type == NodeType.MathfMax ? "Max" : "Min";
                return $"System.Math.{fn}({EmitSubExpr(a, map, graph, false)}, {EmitSubExpr(b, map, graph, false)})";
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
            PushScope();
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
            PopScope();
            sb.AppendLine($"{pad}}}");
        }

        /// <summary>
        /// Генерирует одно предложение for-клаузы (init или increment) из sub-графа.
        /// Результат: "int i = 0" или "i++" — без точки с запятой.
        /// </summary>
        private string GenerateForClauseFromSubGraph(GraphData subGraph)
        {
            if (subGraph == null || subGraph.Nodes.Count == 0) return "";
            var sb = new StringBuilder();
            GenerateStatementsFromSubGraph(subGraph, sb, 0);
            // GenerateStatementsFromSubGraph добавляет ";\n" — убираем хвост и лишние пробелы
            return sb.ToString()
                .Replace(";\r\n", "").Replace(";\n", "")
                .Trim();
        }

        private string EmitForInitClause(NodeData forNode)
        {
            // Новый путь: init хранится в SubGraph
            if (forNode.InitSubGraph != null && forNode.InitSubGraph.Nodes.Count > 0)
                return GenerateForClauseFromSubGraph(forNode.InitSubGraph);

            // Устаревший путь: init-ребро в главном графе
            var initEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == forNode.Id && e.ToPort == "init");
            if (initEdge == null)
                return "";

            var fromId = initEdge.FromNodeId;
            if (!_map.TryGetValue(fromId, out var n))
                return "";

            if (IsLiteral(n.Type) && !string.IsNullOrEmpty(n.VariableName))
            {
                var valueEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == n.Id && e.ToPort == "inputValue");
                string rhs = valueEdge != null ? EmitCondExpr(valueEdge.FromNodeId) : LiteralRhs(n);
                return $"{KeywordFor(n.ValueType)} {n.VariableName} = {rhs}";
            }

            return EmitStmtExpr(fromId);
        }

        private string EmitForIncrementClause(NodeData forNode)
        {
            // Новый путь: increment хранится в SubGraph
            if (forNode.IncrementSubGraph != null && forNode.IncrementSubGraph.Nodes.Count > 0)
            {
                // Пробуем распознать паттерн varName++ / varName-- перед генерацией
                var incDec = TryGetIncrementDecrementClause(forNode.IncrementSubGraph);
                if (incDec != null) return incDec;

                return GenerateForClauseFromSubGraph(forNode.IncrementSubGraph);
            }

            // Устаревший путь: increment-ребро в главном графе
            var incEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == forNode.Id && e.ToPort == "increment");
            if (incEdge == null)
                return "";

            var fromId = incEdge.FromNodeId;
            var setEdge = _graph.Edges.FirstOrDefault(
                e => e.FromNodeId == fromId && e.FromPort == "output" && e.ToPort == "inputValue");
            if (setEdge != null && _map.TryGetValue(setEdge.ToNodeId, out var setN) &&
                IsLiteral(setN.Type) && !string.IsNullOrEmpty(setN.VariableName))
            {
                var name = setN.VariableName;
                var a = Input(fromId, "inputA");
                var b = Input(fromId, "inputB");
                if (a != null && b != null &&
                    _map.TryGetValue(b, out var lit) && lit.Type == NodeType.LiteralInt && lit.Value == "1")
                {
                    var leftName = EmitExpr(a).Trim();
                    if (leftName == name)
                    {
                        if (_map[fromId].Type == NodeType.MathAdd)
                            return $"{name}++";
                        if (_map[fromId].Type == NodeType.MathSubtract)
                            return $"{name}--";
                    }
                }

                return $"{name} = {EmitStmtExpr(fromId)}";
            }

            return EmitStmtExpr(fromId);
        }

        /// <summary>
        /// Проверяет, содержит ли subGraph паттерн «varName = varName ± 1»
        /// и если да — возвращает «varName++» или «varName--».
        /// Иначе возвращает null, и вызывающий код падает на общую генерацию.
        /// </summary>
        private static string? TryGetIncrementDecrementClause(GraphData subGraph)
        {
            if (subGraph == null || subGraph.Nodes.Count == 0) return null;

            var subMap = subGraph.Nodes.ToDictionary(n => n.Id);

            // Ищем целевую ноду: литерал с variableName, в inputValue которого приходит MathAdd/Sub
            foreach (var assignNode in subGraph.Nodes
                         .Where(n => IsLiteral(n.Type) && !string.IsNullOrEmpty(n.VariableName)))
            {
                var varName = assignNode.VariableName;

                var inputEdge = subGraph.Edges
                    .FirstOrDefault(e => e.ToNodeId == assignNode.Id && e.ToPort == "inputValue");
                if (inputEdge == null) continue;

                if (!subMap.TryGetValue(inputEdge.FromNodeId, out var mathNode)) continue;
                if (mathNode.Type != NodeType.MathAdd && mathNode.Type != NodeType.MathSubtract) continue;

                // inputA должен ссылаться на ту же переменную
                var inputAEdge = subGraph.Edges
                    .FirstOrDefault(e => e.ToNodeId == mathNode.Id && e.ToPort == "inputA");
                if (inputAEdge == null) continue;
                if (!subMap.TryGetValue(inputAEdge.FromNodeId, out var refNode)) continue;
                if (refNode.VariableName != varName) continue;

                // inputB должен быть LiteralInt == 1
                var inputBEdge = subGraph.Edges
                    .FirstOrDefault(e => e.ToNodeId == mathNode.Id && e.ToPort == "inputB");
                if (inputBEdge == null) continue;
                if (!subMap.TryGetValue(inputBEdge.FromNodeId, out var litNode)) continue;
                if (litNode.Type != NodeType.LiteralInt || litNode.Value != "1") continue;

                return mathNode.Type == NodeType.MathAdd
                    ? $"{varName}++"
                    : $"{varName}--";
            }

            return null;
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

        private void EmitConsoleWriteLine(NodeData node, StringBuilder sb, string pad)
        {
            var msgEdge = _graph.Edges.FirstOrDefault(
                e => e.ToNodeId == node.Id && e.ToPort == "message");
            var msg = msgEdge != null
                ? EmitCondExpr(msgEdge.FromNodeId)
                : FormatConsoleLiteral(node);
            sb.AppendLine($"{pad}Console.WriteLine({msg});");
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

            if (!string.IsNullOrEmpty(node.VariableName) && nodeId != selfId)
                return node.VariableName;

            if (IsLiteral(node.Type))
                return LiteralRhs(node);

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
                return $"System.Math.Abs({EmitExpr(v)})";
            }

            if (node.Type is NodeType.MathfMax or NodeType.MathfMin)
            {
                var a = Input(nodeId, "inputA");
                var b = Input(nodeId, "inputB");
                if (a == null || b == null) return "???";
                var fn = node.Type == NodeType.MathfMax ? "Max" : "Min";
                return $"System.Math.{fn}({EmitExpr(a)}, {EmitExpr(b)})";
            }

            return "???";
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
            _ => n.Value
        };

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
            _ => "int"
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

        /// <summary>Узел, с которого начинается цепочка исполнения (первая инструкция или нет входящего execIn).</summary>
        private static bool IsStatementEntryNode(NodeData n)
        {
            if (n.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile
                or NodeType.ConsoleWriteLine)
                return true;

            if (IsLiteral(n.Type) && !string.IsNullOrEmpty(n.VariableName))
                return true;

            if ((IsBinaryOp(n.Type) || n.Type == NodeType.LogicalNot || IsBuiltinExpressionNode(n.Type)) &&
                !string.IsNullOrEmpty(n.VariableName))
                return true;

            return false;
        }

        /// <summary>
        /// else if / else не должны попадать в корни: на них уже есть вход с «ложного» выхода родительского if.
        /// Иначе при произвольном порядке нод в списке средний if генерируется отдельным корнем и ломает цепочку.
        /// </summary>
        private bool IsChainedElseBranchTarget(string nodeId)
        {
            return _graph.Edges.Any(e =>
                e.ToNodeId == nodeId &&
                _map.TryGetValue(e.FromNodeId, out var from) &&
                from.Type == NodeType.FlowIf &&
                IsFalseBranchOutputPort(e.FromPort));
        }

        private static bool IsFalseBranchOutputPort(string? fromPort)
        {
            if (string.IsNullOrEmpty(fromPort))
                return false;
            if (fromPort == "false" || fromPort == "falseBranch")
                return true;
            return string.Equals(fromPort, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fromPort, "falseBranch", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveIfFalseSuccessor(NodeData ifNode, out NodeData? successor)
        {
            successor = null;
            var ifId = ifNode.Id;
            var explicitFalse = _graph.Edges.FirstOrDefault(e =>
                e.FromNodeId == ifId &&
                IsFalseBranchOutputPort(e.FromPort) &&
                _map.TryGetValue(e.ToNodeId, out var targetNode) &&
                (targetNode.Type == NodeType.FlowIf || targetNode.Type == NodeType.FlowElse));

            if (explicitFalse != null)
            {
                successor = _map[explicitFalse.ToNodeId];
                return true;
            }

            // Совместимость с устаревшим форматом: некоторые старые графы хранили
            // if→if/else через execOut вместо falseBranch.
            var legacyExec = _graph.Edges.FirstOrDefault(e =>
                e.FromNodeId == ifId &&
                IsExecOutPort(e.FromPort) &&
                _map.TryGetValue(e.ToNodeId, out var leg) &&
                (leg.Type == NodeType.FlowIf || leg.Type == NodeType.FlowElse));

            if (legacyExec == null)
                return false;

            successor = _map[legacyExec.ToNodeId];
            return true;
        }

        private static bool IsExecInPort(string? toPort)
        {
            if (string.IsNullOrEmpty(toPort)) return false;
            var t = toPort.Trim();
            return t == "execIn"
                || t == "exec"
                || string.Equals(t, "execIn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "exec", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExecOutPort(string? fromPort)
        {
            if (string.IsNullOrEmpty(fromPort)) return false;
            var t = fromPort.Trim();
            return t == "execOut" || string.Equals(t, "execOut", StringComparison.OrdinalIgnoreCase);
        }
    }
}
