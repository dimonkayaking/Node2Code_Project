#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Parsers
{
    public partial class RoslynCodeParser
    {
        private static readonly string WrapPrefix =
            "static class Mathf\n{\n" +
            "    public static float Abs(float x) => x;\n" +
            "    public static float Max(float a, float b) => a > b ? a : b;\n" +
            "    public static float Min(float a, float b) => a < b ? a : b;\n" +
            "}\n" +
            "static class __VsParseWrapper\n{\n    static void __VsParseMethod()\n    {\n";

        private static readonly string WrapSuffix = "\n    }\n}";
        private static readonly int WrapperNewlinesBeforeUser = WrapPrefix.Count(c => c == '\n');

        private int _nodeCounter;
        private GraphData _graph;
        private GraphData _rootGraph;
        private List<string> _errors;
        private readonly Dictionary<string, string> _symbolToNodeId = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _variableTypes = new Dictionary<string, string>();
        private const string SubGraphVariableRefMarker = "__varref:";

        private bool _inSubGraph;
        private readonly Stack<GraphData> _graphStack = new Stack<GraphData>();
        private readonly Stack<Dictionary<string, string>> _varRefStack = new Stack<Dictionary<string, string>>();
        private Dictionary<string, string> _subGraphVarRefs;

        public ParseResult Parse(string code)
        {
            _nodeCounter = 0;
            _graph = new GraphData();
            _rootGraph = _graph;
            _errors = new List<string>();
            _symbolToNodeId.Clear();
            _variableTypes.Clear();
            _inSubGraph = false;
            _graphStack.Clear();
            _varRefStack.Clear();
            _subGraphVarRefs = null;

            if (string.IsNullOrWhiteSpace(code))
            {
                _errors.Add("Код пуст");
                return Result();
            }

            var wrapped = WrapPrefix + code + WrapSuffix;

            var tree = CSharpSyntaxTree.ParseText(
                wrapped,
                new CSharpParseOptions(LanguageVersion.Latest));

            foreach (var d in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error))
            {
                _errors.Add(
                    $"{d.GetMessage()} ({FormatUserLocation(tree, d.Location.SourceSpan)})");
            }

            if (_errors.Count > 0)
                return Result();

            var root = tree.GetCompilationUnitRoot();
            var method = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "__VsParseMethod");

            if (method?.Body == null)
            {
                _errors.Add("Не удалось найти тело метода после разбора.");
                return Result();
            }

            VisitMethodBody(method.Body);
            return Result();
        }

        private ParseResult Result() =>
            new ParseResult { Graph = _graph, Errors = _errors };

        private static string FormatUserLocation(SyntaxTree tree, TextSpan span)
        {
            var pos = tree.GetLineSpan(span);
            var line1 = pos.StartLinePosition.Line + 1;
            var col1 = pos.StartLinePosition.Character + 1;
            var userLine = line1 - WrapperNewlinesBeforeUser;
            if (userLine < 1)
                return $"{line1}:{col1} (служебная обёртка)";
            return $"{userLine}:{col1}";
        }

        private void VisitMethodBody(BlockSyntax body)
        {
            string prevFlowNode = null;
            var prevFlowPort = PortIds.ExecOut;

            foreach (var stmt in body.Statements)
            {
                if (stmt is IfStatementSyntax ifStmt)
                {
                    var ifHost = VisitIfChain(ifStmt, prevFlowNode, prevFlowPort);
                    if (ifHost != null)
                    {
                        prevFlowNode = ifHost.NodeId;
                        prevFlowPort = ifHost.ExecOutPort;
                    }
                    else
                    {
                        prevFlowNode = null;
                        prevFlowPort = PortIds.ExecOut;
                    }
                }
                else if (stmt is ForStatementSyntax forStmt)
                {
                    var host = VisitForStatement(forStmt, prevFlowNode, prevFlowPort);
                    if (host != null)
                    {
                        prevFlowNode = host.NodeId;
                        prevFlowPort = host.ExecOutPort;
                    }
                }
                else if (stmt is WhileStatementSyntax whileStmt)
                {
                    var host = VisitWhileStatement(whileStmt, prevFlowNode, prevFlowPort);
                    if (host != null)
                    {
                        prevFlowNode = host.NodeId;
                        prevFlowPort = host.ExecOutPort;
                    }
                }
                else
                {
                    var host = VisitStatementForFlow(stmt, prevFlowNode, prevFlowPort);
                    if (host != null)
                    {
                        prevFlowNode = host.NodeId;
                        prevFlowPort = host.ExecOutPort;
                    }
                }
            }
        }

        private bool ShouldBreakFlowAfter(string nodeId)
        {
            var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
                return false;

            return node.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor
                or NodeType.FlowWhile or NodeType.ConsoleWriteLine or NodeType.DebugLog;
        }

        private sealed class FlowHost
        {
            public string NodeId { get; set; } = "";
            public string ExecOutPort { get; set; } = PortIds.ExecOut;
        }

        private FlowHost VisitStatementForFlow(StatementSyntax stmt, string prevNode, string prevPort)
        {
            switch (stmt)
            {
                case LocalDeclarationStatementSyntax local:
                    return VisitLocalDeclaration(local, prevNode, prevPort);
                case ExpressionStatementSyntax exprStmt:
                    return VisitExpressionStatement(exprStmt, prevNode, prevPort);
                case ForStatementSyntax forStmt:
                    return VisitForStatement(forStmt, prevNode, prevPort);
                case WhileStatementSyntax whileStmt:
                    return VisitWhileStatement(whileStmt, prevNode, prevPort);
                default:
                    ReportUnsupported(stmt);
                    return null;
            }
        }

        private void ReportUnsupported(SyntaxNode node)
        {
            _errors.Add(
                $"Неподдерживаемая конструкция ({FormatUserLocation(node.SyntaxTree, node.Span)}): {node.Kind()}. Поддерживаются: объявления, присваивания, +=/-=, ++/--, if/else, for/while, вызовы Parse/ToString/Mathf, Console.WriteLine.");
        }

        private string CreateDefaultLiteralNode(string typeStr, string variableName)
        {
            NodeType type;
            string value;
            switch (typeStr)
            {
                case "float": type = NodeType.LiteralFloat; value = "0"; break;
                case "bool": type = NodeType.LiteralBool; value = "false"; break;
                case "string": type = NodeType.LiteralString; value = ""; break;
                default: type = NodeType.LiteralInt; value = "0"; break;
            }
            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = type,
                Value = value,
                ValueType = typeStr,
                VariableName = variableName
            });
            return id;
        }

        private FlowHost VisitLocalDeclaration(LocalDeclarationStatementSyntax local, string prevNode, string prevPort)
        {
            FlowHost last = null;
            foreach (var v in local.Declaration.Variables)
            {
                var name = v.Identifier.Text;

                if (_symbolToNodeId.ContainsKey(name))
                {
                    _errors.Add(
                        $"Повторное объявление переменной «{name}» ({FormatUserLocation(local.SyntaxTree, v.Identifier.Span)}).");
                    continue;
                }

                var typeStr = local.Declaration.Type.ToString().Trim();
                var vType = typeStr switch
                {
                    "float" => "float",
                    "bool" => "bool",
                    "string" => "string",
                    _ => "int"
                };
                _variableTypes[name] = vType;

                if (v.Initializer == null)
                {
                    var declId = CreateDefaultLiteralNode(vType, name);
                    _symbolToNodeId[name] = declId;

                    var declHost = new FlowHost { NodeId = declId };
                    if (last != null)
                        AddEdge(last.NodeId, last.ExecOutPort, declHost.NodeId, PortIds.ExecIn);
                    else if (prevNode != null)
                        AddEdge(prevNode, prevPort, declHost.NodeId, PortIds.ExecIn);
                    last = declHost;
                    continue;
                }

                var rootId = VisitExpression(v.Initializer.Value, false, null, out var unsupported);
                if (unsupported) continue;
                if (rootId == null) continue;

                var rootNode = _graph.Nodes.FirstOrDefault(n => n.Id == rootId);
                string litId;
                if (rootNode != null && IsLiteralNodeType(rootNode.Type))
                {
                    rootNode.VariableName = name;
                    rootNode.ValueType = vType;
                    litId = rootId;
                }
                else
                {
                    litId = CreateDefaultLiteralNode(vType, name);
                    AddEdge(rootId, GetDataOutPortForNodeId(rootId), litId, "inputValue");
                    var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
                    if (litNode != null)
                    {
                        litNode.ExpressionOverride = v.Initializer.Value.ToString().Trim();
                        var computed = TryEvaluateExpression(rootId);
                        if (computed != null)
                            litNode.Value = computed;
                    }
                }

                _symbolToNodeId[name] = litId;

                var host = new FlowHost { NodeId = litId };
                if (last != null)
                    AddEdge(last.NodeId, last.ExecOutPort, host.NodeId, PortIds.ExecIn);
                else if (prevNode != null)
                    AddEdge(prevNode, prevPort, host.NodeId, PortIds.ExecIn);

                last = host;
            }

            return last;
        }

        private FlowHost VisitExpressionStatement(ExpressionStatementSyntax stmt, string prevNode, string prevPort)
        {
            var expr = stmt.Expression;

            if (expr is InvocationExpressionSyntax inv && IsConsoleWriteLine(inv))
                return VisitConsoleWriteLine(inv, prevNode, prevPort);

            if (expr is InvocationExpressionSyntax invDebug && IsDebugLog(invDebug))
                return VisitDebugLog(invDebug, prevNode, prevPort);

            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax)
            {
                if (assign.Kind() == SyntaxKind.SimpleAssignmentExpression)
                {
                    var idLeft = (IdentifierNameSyntax)assign.Left;
                    var name = idLeft.Identifier.Text;
                    var rootId = VisitExpression(assign.Right, false, null, out var unsupported);
                    if (unsupported || rootId == null)
                        return null;

                    var rootNode = _graph.Nodes.FirstOrDefault(n => n.Id == rootId);
                    string litId;
                    if (rootNode != null && IsLiteralNodeType(rootNode.Type) && string.IsNullOrEmpty(rootNode.VariableName))
                    {
                        rootNode.VariableName = name;
                        if (_variableTypes.TryGetValue(name, out var existingType))
                            rootNode.ValueType = existingType;
                        litId = rootId;
                    }
                    else
                    {
                        var vType = _variableTypes.TryGetValue(name, out var t) ? t : "int";
                        litId = CreateDefaultLiteralNode(vType, name);
                        AddEdge(rootId, GetDataOutPortForNodeId(rootId), litId, "inputValue");
                        var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
                        if (litNode != null)
                            litNode.ExpressionOverride = assign.Right.ToString().Trim();
                    }

                    _symbolToNodeId[name] = litId;

                    var host = new FlowHost { NodeId = litId };
                    if (prevNode != null)
                        AddEdge(prevNode, prevPort, host.NodeId, PortIds.ExecIn);
                    return host;
                }

                if (assign.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression
                    or SyntaxKind.MultiplyAssignmentExpression or SyntaxKind.DivideAssignmentExpression
                    or SyntaxKind.ModuloAssignmentExpression)
                {
                    return VisitCompoundAssignment(assign, prevNode, prevPort);
                }
            }

            if (expr is PostfixUnaryExpressionSyntax post &&
                (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression)) &&
                post.Operand is IdentifierNameSyntax idPost)
            {
                return VisitIncrementDecrementStatement(
                    idPost,
                    increment: post.IsKind(SyntaxKind.PostIncrementExpression),
                    prevNode,
                    prevPort);
            }

            if (expr is PrefixUnaryExpressionSyntax pre &&
                (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression)) &&
                pre.Operand is IdentifierNameSyntax idPre)
            {
                return VisitIncrementDecrementStatement(
                    idPre,
                    increment: pre.IsKind(SyntaxKind.PreIncrementExpression),
                    prevNode,
                    prevPort);
            }

            ReportUnsupported(stmt);
            return null;
        }

        private static bool IsConsoleWriteLine(InvocationExpressionSyntax inv)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                return false;
            if (ma.Name.Identifier.Text != "WriteLine")
                return false;
            return ma.Expression is IdentifierNameSyntax id && id.Identifier.Text == "Console";
        }

        private static bool IsDebugLog(InvocationExpressionSyntax inv)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                return false;
            if (ma.Name.Identifier.Text != "Log")
                return false;

            // Debug.Log(...) — и UnityEngine.Debug.Log(...) через квалифицированное имя.
            return ma.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text == "Debug",
                MemberAccessExpressionSyntax nested => nested.Name.Identifier.Text == "Debug",
                _ => false
            };
        }

        private FlowHost VisitDebugLog(InvocationExpressionSyntax inv, string prevNode, string prevPort)
        {
            return VisitMessagePrintInvocation(inv, prevNode, prevPort, NodeType.DebugLog);
        }

        private FlowHost VisitConsoleWriteLine(InvocationExpressionSyntax inv, string prevNode, string prevPort)
        {
            return VisitMessagePrintInvocation(inv, prevNode, prevPort, NodeType.ConsoleWriteLine);
        }

        private FlowHost VisitMessagePrintInvocation(
            InvocationExpressionSyntax inv, string prevNode, string prevPort, NodeType nodeType)
        {
            var nodeId = NewId();
            var nodeData = new NodeData
            {
                Id = nodeId,
                Type = nodeType,
                Value = "",
                ValueType = "string",
                VariableName = ""
            };

            if (inv.ArgumentList.Arguments.Count > 0)
            {
                var arg = inv.ArgumentList.Arguments[0].Expression;
                if (TryParseConsoleLiteral(arg, out var literalValue, out var literalType))
                {
                    nodeData.Value = literalValue;
                    nodeData.ValueType = literalType;
                }
                else
                {
                    var msgId = VisitExpression(arg, false, null, out var u);
                    if (u || msgId == null)
                        return null;
                    _graph.Nodes.Add(nodeData);
                    AddEdge(msgId, GetDataOutPortForNodeId(msgId), nodeId, "message");
                    if (prevNode != null)
                        AddEdge(prevNode, prevPort, nodeId, PortIds.ExecIn);
                    return new FlowHost { NodeId = nodeId };
                }
            }

            _graph.Nodes.Add(nodeData);

            if (prevNode != null)
                AddEdge(prevNode, prevPort, nodeId, PortIds.ExecIn);

            return new FlowHost { NodeId = nodeId };
        }

        private static bool TryParseConsoleLiteral(ExpressionSyntax expression, out string value, out string valueType)
        {
            value = "";
            valueType = "string";
            expression = expression is ParenthesizedExpressionSyntax p ? p.Expression : expression;

            if (expression is LiteralExpressionSyntax lit)
            {
                switch (lit.Kind())
                {
                    case SyntaxKind.StringLiteralExpression:
                        value = lit.Token.ValueText ?? "";
                        valueType = "string";
                        return true;
                    case SyntaxKind.NumericLiteralExpression:
                        var text = lit.Token.Text;
                        if (text.Contains('.') || text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                        {
                            value = text.TrimEnd('f', 'F');
                            valueType = "float";
                        }
                        else
                        {
                            value = text;
                            valueType = "int";
                        }
                        return true;
                    case SyntaxKind.TrueLiteralExpression:
                    case SyntaxKind.FalseLiteralExpression:
                        value = (lit.Token.ValueText ?? lit.Token.Text).ToLowerInvariant();
                        valueType = "bool";
                        return true;
                }
            }

            return false;
        }

        private string CreateLiteralStringNode(string text)
        {
            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralString,
                Value = text,
                ValueType = "string",
                VariableName = ""
            });
            return id;
        }

        private string CreateLiteralIntOne()
        {
            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralInt,
                Value = "1",
                ValueType = "int",
                VariableName = ""
            });
            return id;
        }

        private FlowHost VisitCompoundAssignment(AssignmentExpressionSyntax assign, string prevNode, string prevPort)
        {
            var name = ((IdentifierNameSyntax)assign.Left).Identifier.Text;
            var opType = assign.Kind() switch
            {
                SyntaxKind.AddAssignmentExpression => NodeType.MathAdd,
                SyntaxKind.SubtractAssignmentExpression => NodeType.MathSubtract,
                SyntaxKind.MultiplyAssignmentExpression => NodeType.MathMultiply,
                SyntaxKind.DivideAssignmentExpression => NodeType.MathDivide,
                SyntaxKind.ModuloAssignmentExpression => NodeType.MathModulo,
                _ => (NodeType?)null
            };

            if (opType == null)
            {
                ReportUnsupported(assign);
                return null;
            }

            string leftId;
            if (_inSubGraph && _symbolToNodeId.ContainsKey(name))
            {
                leftId = CreateVariableRefInSubGraph(name);
            }
            else if (!_symbolToNodeId.TryGetValue(name, out var tempLeft))
            {
                _errors.Add(
                    $"Неизвестная переменная «{name}» ({FormatUserLocation(assign.SyntaxTree, assign.Span)}).");
                return null;
            }
            else
            {
                leftId = tempLeft;
            }

            var rightId = VisitExpression(assign.Right, false, null, out var unsupported);
            if (unsupported || rightId == null)
                return null;

            var opId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = opId,
                Type = opType.Value,
                Value = "",
                ValueType = "",
                VariableName = ""
            });
            AddEdge(leftId, GetDataOutPortForNodeId(leftId), opId, "inputA");
            AddEdge(rightId, GetDataOutPortForNodeId(rightId), opId, "inputB");

            var vType = _variableTypes.TryGetValue(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            
            AddEdge(opId, "output", litId, "inputValue");
            _symbolToNodeId[name] = litId;

            var host = new FlowHost { NodeId = litId };
            if (prevNode != null)
                AddEdge(prevNode, prevPort, litId, PortIds.ExecIn);
            return host;
        }

        private FlowHost VisitIncrementDecrementStatement(
            IdentifierNameSyntax idExpr,
            bool increment,
            string prevNode,
            string prevPort)
        {
            var name = idExpr.Identifier.Text;
            string varNodeId;
            if (_inSubGraph && _symbolToNodeId.ContainsKey(name))
            {
                varNodeId = CreateVariableRefInSubGraph(name);
            }
            else if (!_symbolToNodeId.TryGetValue(name, out var tempVar))
            {
                _errors.Add(
                    $"Неизвестная переменная «{name}» ({FormatUserLocation(idExpr.SyntaxTree, idExpr.Span)}).");
                return null;
            }
            else
            {
                varNodeId = tempVar;
            }

            var oneId = CreateLiteralIntOne();
            var opType = increment ? NodeType.MathAdd : NodeType.MathSubtract;
            var opId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = opId,
                Type = opType,
                Value = "",
                ValueType = "",
                VariableName = ""
            });
            AddEdge(varNodeId, GetDataOutPortForNodeId(varNodeId), opId, "inputA");
            AddEdge(oneId, "output", opId, "inputB");

            var vType = _variableTypes.TryGetValue(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            
            AddEdge(opId, "output", litId, "inputValue");
            _symbolToNodeId[name] = litId;

            var host = new FlowHost { NodeId = litId };
            if (prevNode != null)
                AddEdge(prevNode, prevPort, litId, PortIds.ExecIn);
            return host;
        }

        private FlowHost VisitForStatement(ForStatementSyntax forStmt, string prevNode, string prevPort)
        {
            var forId = NewId();
            var forNodeData = new NodeData
            {
                Id = forId,
                Type = NodeType.FlowFor,
                Value = "",
                ValueType = "",
                VariableName = ""
            };

            if (prevNode != null)
                AddEdge(prevNode, prevPort, forId, PortIds.ExecIn);

            var initGraph = new GraphData();
            PushSubGraph(initGraph);
            VisitForInitialization(forStmt);
            PopSubGraph();
            forNodeData.InitSubGraph = initGraph;

            var condGraph = new GraphData();
            if (forStmt.Condition != null)
            {
                PushSubGraph(condGraph);
                VisitExpression(forStmt.Condition, false, null, out _);
                PopSubGraph();
            }
            forNodeData.ConditionSubGraph = condGraph;

            var incGraph = new GraphData();
            PushSubGraph(incGraph);
            foreach (var inc in forStmt.Incrementors)
            {
                VisitIncrementExpression(inc, out _);
            }
            PopSubGraph();
            forNodeData.IncrementSubGraph = incGraph;

            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            var bodyStmts = ExpandStatement(forStmt.Statement);
            BuildStatementsInSubGraph(bodyStmts);
            PopSubGraph();
            forNodeData.BodySubGraph = bodyGraph;

            _graph.Nodes.Add(forNodeData);

            return new FlowHost { NodeId = forId, ExecOutPort = PortIds.ExecOut };
        }

        private void VisitForInitialization(ForStatementSyntax forStmt)
        {
            if (forStmt.Declaration != null)
            {
                foreach (var v in forStmt.Declaration.Variables)
                {
                    var name = v.Identifier.Text;

                    if (v.Initializer == null)
                        continue;

                    var rootId = VisitExpression(v.Initializer.Value, true, name, out var unsupported);
                    if (unsupported || rootId == null)
                        continue;

                    _symbolToNodeId[name] = rootId;
                }
            }

            foreach (var initExpr in forStmt.Initializers)
            {
                if (initExpr is AssignmentExpressionSyntax ae &&
                    ae.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                    ae.Left is IdentifierNameSyntax idLeft)
                {
                    var n = idLeft.Identifier.Text;
                    var rootId = VisitExpression(ae.Right, true, n, out var unsupported);
                    if (unsupported || rootId == null)
                        continue;

                    var rootNode = _graph.Nodes.FirstOrDefault(node => node.Id == rootId);
                    string litId;
                    if (rootNode != null && rootNode.VariableName == n)
                    {
                        litId = rootId;
                    }
                    else
                    {
                        var vType = _variableTypes.TryGetValue(n, out var t) ? t : "int";
                        litId = CreateDefaultLiteralNode(vType, n);
                        AddEdge(rootId, GetDataOutPortForNodeId(rootId), litId, "inputValue");
                    }
                    
                    _symbolToNodeId[n] = litId;
                    continue;
                }

                VisitExpression(initExpr, false, null, out _);
            }
        }

        private string VisitIncrementExpression(ExpressionSyntax expr, out bool unsupported)
        {
            unsupported = false;
            while (expr is ParenthesizedExpressionSyntax paren)
                expr = paren.Expression;

            if (expr is PostfixUnaryExpressionSyntax post &&
                (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression)) &&
                post.Operand is IdentifierNameSyntax idPost)
            {
                return BuildIncrementSubgraph(idPost, post.IsKind(SyntaxKind.PostIncrementExpression), out unsupported);
            }

            if (expr is PrefixUnaryExpressionSyntax pre &&
                (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression)) &&
                pre.Operand is IdentifierNameSyntax idPre)
            {
                return BuildIncrementSubgraph(idPre, pre.IsKind(SyntaxKind.PreIncrementExpression), out unsupported);
            }

            if (expr is AssignmentExpressionSyntax assign &&
                assign.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assign.Left is IdentifierNameSyntax)
            {
                return BuildIncrementAssignmentSubgraph(assign, out unsupported);
            }

            return VisitExpression(expr, false, null, out unsupported);
        }

        private string BuildIncrementSubgraph(IdentifierNameSyntax id, bool increment, out bool unsupported)
        {
            unsupported = false;
            var name = id.Identifier.Text;
            
            string varNodeId;
            if (_inSubGraph && _symbolToNodeId.ContainsKey(name))
            {
                varNodeId = CreateVariableRefInSubGraph(name);
            }
            else if (!_symbolToNodeId.TryGetValue(name, out var temp))
            {
                unsupported = true;
                _errors.Add(
                    $"Неизвестная переменная «{name}» ({FormatUserLocation(id.SyntaxTree, id.Span)}).");
                return null;
            }
            else
            {
                varNodeId = temp;
            }

            var oneId = CreateLiteralIntOne();
            var opType = increment ? NodeType.MathAdd : NodeType.MathSubtract;
            var opId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = opId,
                Type = opType,
                Value = "",
                ValueType = "",
                VariableName = ""
            });
            AddEdge(varNodeId, GetDataOutPortForNodeId(varNodeId), opId, "inputA");
            AddEdge(oneId, "output", opId, "inputB");

            var vType = _variableTypes.TryGetValue(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
            if (litNode != null)
                litNode.Value = "?";
            
            AddEdge(opId, "output", litId, "inputValue");
            _symbolToNodeId[name] = litId;

            return opId;
        }

        private string BuildIncrementAssignmentSubgraph(AssignmentExpressionSyntax assign, out bool unsupported)
        {
            unsupported = false;
            if (assign.Left is not IdentifierNameSyntax idLeft)
            {
                unsupported = true;
                return null;
            }

            var name = idLeft.Identifier.Text;
            if (!_symbolToNodeId.ContainsKey(name))
            {
                unsupported = true;
                _errors.Add(
                    $"Неизвестная переменная «{name}» ({FormatUserLocation(assign.SyntaxTree, assign.Span)}).");
                return null;
            }

            var rhsId = VisitExpression(assign.Right, false, null, out unsupported);
            if (unsupported || rhsId == null)
                return null;

            var vType = _variableTypes.TryGetValue(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
            if (litNode != null)
                litNode.Value = "?";

            AddEdge(rhsId, GetDataOutPortForNodeId(rhsId), litId, "inputValue");
            _symbolToNodeId[name] = litId;
            return litId;
        }

        private FlowHost VisitWhileStatement(WhileStatementSyntax whileStmt, string prevNode, string prevPort)
        {
            var whileId = NewId();
            var whileNodeData = new NodeData
            {
                Id = whileId,
                Type = NodeType.FlowWhile,
                Value = "",
                ValueType = "",
                VariableName = ""
            };

            if (prevNode != null)
                AddEdge(prevNode, prevPort, whileId, PortIds.ExecIn);

            var condGraph = new GraphData();
            PushSubGraph(condGraph);
            VisitExpression(whileStmt.Condition, false, null, out _);
            PopSubGraph();
            whileNodeData.ConditionSubGraph = condGraph;

            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            var bodyStmts = ExpandStatement(whileStmt.Statement);
            BuildStatementsInSubGraph(bodyStmts);
            PopSubGraph();
            whileNodeData.BodySubGraph = bodyGraph;

            _graph.Nodes.Add(whileNodeData);

            return new FlowHost { NodeId = whileId, ExecOutPort = PortIds.ExecOut };
        }

        private FlowHost VisitIfChain(IfStatementSyntax stmt, string incomingNodeId, string incomingPort)
        {
            var ifNodeId = NewId();
            var ifNodeData = new NodeData
            {
                Id = ifNodeId,
                Type = NodeType.FlowIf,
                Value = "",
                ValueType = "",
                VariableName = ""
            };

            var condGraph = new GraphData();
            PushSubGraph(condGraph);
            VisitExpression(stmt.Condition, false, null, out _);
            PopSubGraph();
            ifNodeData.ConditionSubGraph = condGraph;

            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            var thenStmts = ExpandStatement(stmt.Statement);
            BuildStatementsInSubGraph(thenStmts);
            PopSubGraph();
            ifNodeData.BodySubGraph = bodyGraph;

            _graph.Nodes.Add(ifNodeData);

            if (incomingNodeId != null && incomingPort != null)
                AddEdge(incomingNodeId, incomingPort, ifNodeId, PortIds.ExecIn);

            if (stmt.Else != null)
            {
                if (stmt.Else.Statement is IfStatementSyntax elseIf)
                {
                    VisitIfChain(elseIf, ifNodeId, PortIds.FalseBranch);
                }
                else
                {
                    var elseNodeId = NewId();
                    var elseNodeData = new NodeData
                    {
                        Id = elseNodeId,
                        Type = NodeType.FlowElse,
                        Value = "",
                        ValueType = "",
                        VariableName = ""
                    };

                    var elseBodyGraph = new GraphData();
                    PushSubGraph(elseBodyGraph);
                    var elseStmts = ExpandStatement(stmt.Else.Statement);
                    BuildStatementsInSubGraph(elseStmts);
                    PopSubGraph();
                    elseNodeData.BodySubGraph = elseBodyGraph;

                    _graph.Nodes.Add(elseNodeData);
                    AddEdge(ifNodeId, PortIds.FalseBranch, elseNodeId, PortIds.ExecIn);
                }
            }

            return new FlowHost { NodeId = ifNodeId, ExecOutPort = PortIds.ExecOut };
        }

        private void PushSubGraph(GraphData target)
        {
            _graphStack.Push(_graph);
            _varRefStack.Push(_subGraphVarRefs);
            _graph = target;
            _inSubGraph = true;
            _subGraphVarRefs = new Dictionary<string, string>();
        }

        private void PopSubGraph()
        {
            _graph = _graphStack.Pop();
            _subGraphVarRefs = _varRefStack.Pop();
            _inSubGraph = _graphStack.Count > 0;
        }

        private void BuildStatementsInSubGraph(IReadOnlyList<StatementSyntax> statements)
        {
            string prevId = null;
            var prevPort = PortIds.ExecOut;

            foreach (var st in statements)
            {
                if (st is IfStatementSyntax nestedIf)
                {
                    var ifHost = VisitIfChain(nestedIf, prevId, prevPort);
                    if (ifHost != null)
                    {
                        prevId = ifHost.NodeId;
                        prevPort = ifHost.ExecOutPort;
                    }
                    else
                    {
                        prevId = null;
                        prevPort = PortIds.ExecOut;
                    }
                    continue;
                }

                if (st is ForStatementSyntax nestedFor)
                {
                    var fh = VisitForStatement(nestedFor, prevId, prevPort);
                    if (fh != null) { prevId = fh.NodeId; prevPort = fh.ExecOutPort; }
                    else { prevId = null; prevPort = PortIds.ExecOut; }
                    continue;
                }

                if (st is WhileStatementSyntax nestedWhile)
                {
                    var wh = VisitWhileStatement(nestedWhile, prevId, prevPort);
                    if (wh != null) { prevId = wh.NodeId; prevPort = wh.ExecOutPort; }
                    else { prevId = null; prevPort = PortIds.ExecOut; }
                    continue;
                }

                var host = VisitStatementForFlow(st, prevId, prevPort);
                if (host != null)
                {
                    prevId = host.NodeId;
                    prevPort = host.ExecOutPort;
                }
            }
        }

        private string CreateVariableRefInSubGraph(string varName)
        {
            if (_subGraphVarRefs != null && _subGraphVarRefs.TryGetValue(varName, out var existing))
                return existing;

            var vType = _variableTypes.TryGetValue(varName, out var t) ? t : "int";
            NodeType litType = vType switch
            {
                "float" => NodeType.LiteralFloat,
                "bool" => NodeType.LiteralBool,
                "string" => NodeType.LiteralString,
                _ => NodeType.LiteralInt
            };

            var value = "";
            if (_symbolToNodeId.TryGetValue(varName, out var sourceId))
            {
                var source = FindNodeByIdInTree(_rootGraph, sourceId);
                if (source != null)
                {
                    if (IsLiteralNodeType(source.Type))
                        value = source.Value ?? "";
                    else if (!string.IsNullOrEmpty(source.ExpressionOverride))
                        value = source.Value ?? "";
                }
            }

            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = litType,
                Value = value,
                ValueType = vType,
                VariableName = varName,
                // Marker: this node is a variable reference helper, not a statement assignment target.
                ExpressionOverride = SubGraphVariableRefMarker + varName
            });

            if (_subGraphVarRefs != null)
                _subGraphVarRefs[varName] = id;
            return id;
        }

        private static IReadOnlyList<StatementSyntax> ExpandStatement(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements.ToList();
            return new List<StatementSyntax> { statement };
        }

        private void ProcessBlockStatements(
            IReadOnlyList<StatementSyntax> statements,
            string entryFromNodeId,
            string entryFromPort)
        {
            string prevId = null;
            var prevPort = PortIds.ExecOut;
            var first = true;

            foreach (var st in statements)
            {
                if (st is IfStatementSyntax nestedIf)
                {
                    var ifHost = first
                        ? VisitIfChain(nestedIf, entryFromNodeId, entryFromPort)
                        : VisitIfChain(nestedIf, prevId, prevPort);

                    first = false;
                    if (ifHost != null)
                    {
                        prevId = ifHost.NodeId;
                        prevPort = ifHost.ExecOutPort;
                    }
                    else
                    {
                        prevId = null;
                        prevPort = PortIds.ExecOut;
                    }
                    continue;
                }

                if (st is ForStatementSyntax nestedFor)
                {
                    var fh = first
                        ? VisitForStatement(nestedFor, entryFromNodeId, entryFromPort)
                        : VisitForStatement(nestedFor, prevId, prevPort);
                    first = false;
                    if (fh != null)
                    {
                        prevId = fh.NodeId;
                        prevPort = fh.ExecOutPort;
                    }
                    else
                    {
                        prevId = null;
                        prevPort = PortIds.ExecOut;
                    }

                    continue;
                }

                if (st is WhileStatementSyntax nestedWhile)
                {
                    var wh = first
                        ? VisitWhileStatement(nestedWhile, entryFromNodeId, entryFromPort)
                        : VisitWhileStatement(nestedWhile, prevId, prevPort);
                    first = false;
                    if (wh != null)
                    {
                        prevId = wh.NodeId;
                        prevPort = wh.ExecOutPort;
                    }
                    else
                    {
                        prevId = null;
                        prevPort = PortIds.ExecOut;
                    }

                    continue;
                }

                var host = VisitStatementForFlow(st, first ? entryFromNodeId : prevId, first ? entryFromPort : prevPort);
                first = false;
                if (host != null)
                {
                    prevId = host.NodeId;
                    prevPort = host.ExecOutPort;
                }
            }
        }

        private string VisitExpression(ExpressionSyntax expr, bool isRoot, string assignVariableToRoot, out bool unsupported)
        {
            unsupported = false;
            while (expr is ParenthesizedExpressionSyntax paren)
                expr = paren.Expression;

            switch (expr)
            {
                case LiteralExpressionSyntax lit:
                    return CreateLiteralFromLiteralExpression(lit, isRoot ? assignVariableToRoot : null);

                case IdentifierNameSyntax id:
                    return ResolveIdentifier(id, out unsupported);

                case BinaryExpressionSyntax bin:
                    return VisitBinary(bin, isRoot, assignVariableToRoot, out unsupported);

                case PrefixUnaryExpressionSyntax pre when pre.IsKind(SyntaxKind.LogicalNotExpression):
                {
                    var inner = VisitExpression(pre.Operand, false, null, out unsupported);
                    if (unsupported || inner == null)
                        return null;
                    var notId = NewId();
                    var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
                    _graph.Nodes.Add(new NodeData
                    {
                        Id = notId,
                        Type = NodeType.LogicalNot,
                        Value = "",
                        ValueType = "",
                        VariableName = vn
                    });
                    AddEdge(inner, GetDataOutPortForNodeId(inner), notId, "input");
                    return notId;
                }

                case PrefixUnaryExpressionSyntax pre when pre.IsKind(SyntaxKind.UnaryPlusExpression):
                    return VisitExpression(pre.Operand, isRoot, assignVariableToRoot, out unsupported);

                case PrefixUnaryExpressionSyntax pre when pre.IsKind(SyntaxKind.UnaryMinusExpression):
                    return VisitUnaryMinus(pre, isRoot, assignVariableToRoot, out unsupported);

                case InvocationExpressionSyntax inv:
                    return VisitInvocationExpression(inv, isRoot, assignVariableToRoot, out unsupported);

                case MemberAccessExpressionSyntax mathMem when
                    IsMathfStaticReceiver(mathMem.Expression) || IsSystemMathStaticReceiver(mathMem.Expression):
                    return CreatePassthroughMathLiteral(mathMem.ToString(), isRoot, assignVariableToRoot);

                case ConditionalExpressionSyntax cond:
                    return CreateStringExpressionLiteralNode(cond.ToString().Trim(), isRoot ? assignVariableToRoot : null);

                case InterpolatedStringExpressionSyntax interpolated:
                    return CreateStringExpressionLiteralNode(interpolated.ToString().Trim(), isRoot ? assignVariableToRoot : null);

                default:
                    unsupported = true;
                    _errors.Add(
                        $"Неподдерживаемое выражение ({FormatUserLocation(expr.SyntaxTree, expr.Span)}): {expr.Kind()}.");
                    return null;
            }
        }

        private string VisitBinary(BinaryExpressionSyntax bin, bool isRoot, string assignVariableToRoot, out bool unsupported)
        {
            unsupported = false;
            var kind = bin.Kind();
            NodeType? opType = kind switch
            {
                SyntaxKind.AddExpression => NodeType.MathAdd,
                SyntaxKind.SubtractExpression => NodeType.MathSubtract,
                SyntaxKind.MultiplyExpression => NodeType.MathMultiply,
                SyntaxKind.DivideExpression => NodeType.MathDivide,
                SyntaxKind.ModuloExpression => NodeType.MathModulo,
                SyntaxKind.EqualsExpression => NodeType.CompareEqual,
                SyntaxKind.NotEqualsExpression => NodeType.CompareNotEqual,
                SyntaxKind.GreaterThanExpression => NodeType.CompareGreater,
                SyntaxKind.LessThanExpression => NodeType.CompareLess,
                SyntaxKind.GreaterThanOrEqualExpression => NodeType.CompareGreaterOrEqual,
                SyntaxKind.LessThanOrEqualExpression => NodeType.CompareLessOrEqual,
                SyntaxKind.LogicalAndExpression => NodeType.LogicalAnd,
                SyntaxKind.LogicalOrExpression => NodeType.LogicalOr,
                _ => null
            };

            if (opType == null)
            {
                unsupported = true;
                _errors.Add(
                    $"Неподдерживаемый оператор ({FormatUserLocation(bin.SyntaxTree, bin.Span)}): {kind}.");
                return null;
            }

            var leftPort = IsMath(opType.Value) ? "inputA" : "left";
            var rightPort = IsMath(opType.Value) ? "inputB" : "right";

            var leftId = VisitExpression(bin.Left, false, null, out unsupported);
            if (unsupported)
                return null;
            var rightId = VisitExpression(bin.Right, false, null, out unsupported);
            if (unsupported)
                return null;

            if (leftId == null || rightId == null)
                return null;

            var opId = NewId();
            var varName = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = opId,
                Type = opType.Value,
                Value = "",
                ValueType = "",
                VariableName = varName
            });

            AddEdge(leftId, GetDataOutPortForNodeId(leftId), opId, leftPort);
            AddEdge(rightId, GetDataOutPortForNodeId(rightId), opId, rightPort);
            return opId;
        }

        private string TryEvaluateExpression(string nodeId)
        {
            var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return null;

            if (IsLiteralNodeType(node.Type) && !string.IsNullOrEmpty(node.Value))
                return node.Value;

            if (!IsMath(node.Type)) return null;

            var leftEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == "inputA");
            var rightEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == "inputB");
            if (leftEdge == null || rightEdge == null) return null;

            var leftVal = TryEvaluateExpression(leftEdge.FromNodeId);
            var rightVal = TryEvaluateExpression(rightEdge.FromNodeId);
            if (leftVal == null || rightVal == null) return null;

            if (int.TryParse(leftVal, out int li) && int.TryParse(rightVal, out int ri))
            {
                int result = node.Type switch
                {
                    NodeType.MathAdd => li + ri,
                    NodeType.MathSubtract => li - ri,
                    NodeType.MathMultiply => li * ri,
                    NodeType.MathDivide when ri != 0 => li / ri,
                    NodeType.MathModulo when ri != 0 => li % ri,
                    _ => 0
                };
                return result.ToString();
            }

            return null;
        }

        private static bool IsMath(NodeType t) =>
            t is NodeType.MathAdd or NodeType.MathSubtract or NodeType.MathMultiply
                or NodeType.MathDivide or NodeType.MathModulo;

        private string CreateStringExpressionLiteralNode(string expressionText, string variableName)
        {
            var id = NewId();
            var valueType = "string";
            if (!string.IsNullOrEmpty(variableName) && _variableTypes.TryGetValue(variableName, out var declaredType))
                valueType = declaredType;

            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralString,
                Value = "",
                ValueType = valueType,
                VariableName = variableName ?? "",
                ExpressionOverride = expressionText
            });
            return id;
        }

        private static bool IsLiteralNodeType(NodeType t) =>
            t is NodeType.LiteralBool or NodeType.LiteralInt or NodeType.LiteralFloat or NodeType.LiteralString;

        /// <summary>
        /// <c>Mathf.*</c> или <c>UnityEngine.Mathf.*</c> — во втором случае выражение до точки — MemberAccess с именем Mathf.
        /// </summary>
        private static bool IsMathfStaticReceiver(ExpressionSyntax expr)
        {
            switch (expr)
            {
                case IdentifierNameSyntax id:
                    return string.Equals(id.Identifier.Text, "Mathf", StringComparison.Ordinal);
                case MemberAccessExpressionSyntax m:
                    return string.Equals(m.Name.Identifier.Text, "Mathf", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        /// <summary>
        /// <c>Math.*</c> или <c>System.Math.*</c> — те же ноды, что для Mathf (Abs / Max / Min).
        /// </summary>
        private static bool IsSystemMathStaticReceiver(ExpressionSyntax expr)
        {
            switch (expr)
            {
                case IdentifierNameSyntax id:
                    return string.Equals(id.Identifier.Text, "Math", StringComparison.Ordinal);
                case MemberAccessExpressionSyntax m when string.Equals(m.Name.Identifier.Text, "Math", StringComparison.Ordinal):
                    return m.Expression is IdentifierNameSyntax sys &&
                           string.Equals(sys.Identifier.Text, "System", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static bool IsAbsMaxMinStaticReceiver(ExpressionSyntax expr) =>
            IsMathfStaticReceiver(expr) || IsSystemMathStaticReceiver(expr);

        private string VisitInvocationExpression(
            InvocationExpressionSyntax inv,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
            {
                unsupported = true;
                _errors.Add(
                    $"Неподдерживаемый вызов ({FormatUserLocation(inv.SyntaxTree, inv.Span)}): ожидается member access.");
                return null;
            }

            var methodName = ma.Name.Identifier.Text;

            if (methodName == "Parse" && ma.Expression is PredefinedTypeSyntax pt)
            {
                if (pt.Keyword.IsKind(SyntaxKind.IntKeyword))
                    return CreateParseNode(NodeType.IntParse, inv, isRoot, assignVariableToRoot, out unsupported);
                if (pt.Keyword.IsKind(SyntaxKind.FloatKeyword))
                    return CreateParseNode(NodeType.FloatParse, inv, isRoot, assignVariableToRoot, out unsupported);
            }

            NodeType? absMaxMinType = methodName switch
            {
                "Abs" => NodeType.MathfAbs,
                "Max" => NodeType.MathfMax,
                "Min" => NodeType.MathfMin,
                _ => null
            };

            if (absMaxMinType != null && IsAbsMaxMinStaticReceiver(ma.Expression))
                return CreateMathfNode(absMaxMinType.Value, inv, isRoot, assignVariableToRoot, out unsupported);

            if (methodName == "ToString")
            {
                return CreateToStringNode(ma.Expression, inv, isRoot, assignVariableToRoot, out unsupported);
            }

            // Любые остальные Mathf.* / Math.* (Sqrt, Pow, Clamp, PI и т.д.)
            if (IsMathfStaticReceiver(ma.Expression) || IsSystemMathStaticReceiver(ma.Expression))
                return CreatePassthroughMathLiteral(inv.ToString(), isRoot, assignVariableToRoot);

            unsupported = true;
            _errors.Add(
                $"Неподдерживаемый вызов метода ({FormatUserLocation(inv.SyntaxTree, inv.Span)}): {methodName}.");
            return null;
        }

        /// <summary>
        /// Выражение Mathf/Math целиком в одну ноду (генератор подставляет ExpressionOverride).
        /// </summary>
        private string CreatePassthroughMathLiteral(string expressionText, bool isRoot, string assignVariableToRoot)
        {
            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralFloat,
                Value = "0",
                ValueType = "float",
                VariableName = vn,
                ExpressionOverride = expressionText.Trim()
            });
            return id;
        }

        private string VisitUnaryMinus(
            PrefixUnaryExpressionSyntax pre,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            if (pre.Operand is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                var folded = CreateNegatedNumericLiteral(lit, isRoot, assignVariableToRoot);
                if (folded != null)
                    return folded;
            }

            var operandId = VisitExpression(pre.Operand, false, null, out unsupported);
            if (unsupported || operandId == null)
                return null;

            var zeroId = CreateZeroLiteralMatchingOperand(operandId);
            var subId = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            var valueType = InferSubtractResultValueType(zeroId, operandId);
            _graph.Nodes.Add(new NodeData
            {
                Id = subId,
                Type = NodeType.MathSubtract,
                Value = "",
                ValueType = valueType,
                VariableName = vn
            });
            AddEdge(zeroId, GetDataOutPortForNodeId(zeroId), subId, "inputA");
            AddEdge(operandId, GetDataOutPortForNodeId(operandId), subId, "inputB");
            return subId;
        }

        private string CreateNegatedNumericLiteral(
            LiteralExpressionSyntax lit,
            bool isRoot,
            string assignVariableToRoot)
        {
            var text = lit.Token.Text;
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";

            if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                var core = text[..^1].Trim();
                if (float.TryParse(core, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    var id = NewId();
                    var neg = (-f).ToString(CultureInfo.InvariantCulture);
                    _graph.Nodes.Add(new NodeData
                    {
                        Id = id,
                        Type = NodeType.LiteralFloat,
                        Value = neg,
                        ValueType = "float",
                        VariableName = vn
                    });
                    return id;
                }
            }
            else if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var li))
            {
                var id = NewId();
                var negInt = unchecked(-li);
                _graph.Nodes.Add(new NodeData
                {
                    Id = id,
                    Type = NodeType.LiteralInt,
                    Value = negInt.ToString(CultureInfo.InvariantCulture),
                    ValueType = "int",
                    VariableName = vn
                });
                return id;
            }

            return null;
        }

        private string CreateZeroLiteralMatchingOperand(string operandNodeId)
        {
            var n = _graph.Nodes.First(x => x.Id == operandNodeId);
            var useFloat = n.Type == NodeType.LiteralFloat ||
                           string.Equals(n.ValueType, "float", StringComparison.OrdinalIgnoreCase);
            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = useFloat ? NodeType.LiteralFloat : NodeType.LiteralInt,
                Value = "0",
                ValueType = useFloat ? "float" : "int",
                VariableName = ""
            });
            return id;
        }

        private string InferSubtractResultValueType(string leftId, string rightId)
        {
            bool IsFloat(string id)
            {
                var n = _graph.Nodes.FirstOrDefault(x => x.Id == id);
                if (n == null)
                    return false;
                return n.Type == NodeType.LiteralFloat ||
                       string.Equals(n.ValueType, "float", StringComparison.OrdinalIgnoreCase);
            }

            return IsFloat(leftId) || IsFloat(rightId) ? "float" : "int";
        }

        private string CreateParseNode(
            NodeType parseType,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            if (inv.ArgumentList.Arguments.Count < 1)
            {
                unsupported = true;
                _errors.Add(
                    $"Parse требует аргумент ({FormatUserLocation(inv.SyntaxTree, inv.Span)}).");
                return null;
            }

            var arg = inv.ArgumentList.Arguments[0].Expression;
            var argId = VisitExpression(arg, false, null, out unsupported);
            if (unsupported || argId == null)
                return null;

            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = parseType,
                Value = "",
                ValueType = parseType == NodeType.FloatParse ? "float" : "int",
                VariableName = vn
            });
            AddEdge(argId, GetDataOutPortForNodeId(argId), id, "input");
            return id;
        }

        private string CreateMathfNode(
            NodeType mathfType,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            var args = inv.ArgumentList.Arguments;
            if (mathfType == NodeType.MathfAbs)
            {
                if (args.Count < 1)
                {
                    unsupported = true;
                    _errors.Add(
                        $"Abs требует аргумент ({FormatUserLocation(inv.SyntaxTree, inv.Span)}).");
                    return null;
                }

                var a = VisitExpression(args[0].Expression, false, null, out unsupported);
                if (unsupported || a == null)
                    return null;

                var id = NewId();
                var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
                _graph.Nodes.Add(new NodeData
                {
                    Id = id,
                    Type = mathfType,
                    Value = "",
                    ValueType = "float",
                    VariableName = vn
                });
                AddEdge(a, GetDataOutPortForNodeId(a), id, "input");
                return id;
            }

            if (args.Count < 2)
            {
                unsupported = true;
                _errors.Add(
                    $"{mathfType} требует два аргумента ({FormatUserLocation(inv.SyntaxTree, inv.Span)}).");
                return null;
            }

            var left = VisitExpression(args[0].Expression, false, null, out unsupported);
            if (unsupported || left == null)
                return null;
            var right = VisitExpression(args[1].Expression, false, null, out unsupported);
            if (unsupported || right == null)
                return null;

            var nodeId = NewId();
            var varName = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = nodeId,
                Type = mathfType,
                Value = "",
                ValueType = "float",
                VariableName = varName
            });
            AddEdge(left, GetDataOutPortForNodeId(left), nodeId, "inputA");
            AddEdge(right, GetDataOutPortForNodeId(right), nodeId, "inputB");
            return nodeId;
        }

        private string CreateToStringNode(
            ExpressionSyntax receiver,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            if (receiver == null)
            {
                unsupported = true;
                return null;
            }

            var recvId = VisitExpression(receiver, false, null, out unsupported);
            if (unsupported || recvId == null)
                return null;

            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.ToStringConvert,
                Value = "",
                ValueType = "string",
                VariableName = vn
            });
            AddEdge(recvId, GetDataOutPortForNodeId(recvId), id, "input");
            return id;
        }

        private string ResolveIdentifier(IdentifierNameSyntax id, out bool unsupported)
        {
            unsupported = false;
            var name = id.Identifier.Text;

            if (_inSubGraph && _symbolToNodeId.ContainsKey(name))
                return CreateVariableRefInSubGraph(name);

            if (_symbolToNodeId.TryGetValue(name, out var nodeId))
                return nodeId;

            unsupported = true;
            _errors.Add(
                $"Неизвестный идентификатор «{name}» ({FormatUserLocation(id.SyntaxTree, id.Span)}).");
            return null;
        }

        private string CreateLiteralFromLiteralExpression(LiteralExpressionSyntax lit, string variableName)
        {
            NodeType type;
            string value;
            string valueType;

            switch (lit.Kind())
            {
                case SyntaxKind.NumericLiteralExpression:
                    var text = lit.Token.Text;
                    if (text.Contains('.') || text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                    {
                        type = NodeType.LiteralFloat;
                        valueType = "float";
                        value = text.TrimEnd('f', 'F');
                    }
                    else
                    {
                        type = NodeType.LiteralInt;
                        valueType = "int";
                        value = text;
                    }
                    break;

                case SyntaxKind.StringLiteralExpression:
                    type = NodeType.LiteralString;
                    valueType = "string";
                    value = lit.Token.ValueText ?? "";
                    break;

                case SyntaxKind.TrueLiteralExpression:
                case SyntaxKind.FalseLiteralExpression:
                    type = NodeType.LiteralBool;
                    valueType = "bool";
                    value = lit.Token.ValueText ?? lit.Token.Text;
                    break;

                default:
                    _errors.Add(
                        $"Неподдерживаемый литерал ({FormatUserLocation(lit.SyntaxTree, lit.Span)}): {lit.Kind()}.");
                    return null;
            }

            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = type,
                Value = value,
                ValueType = valueType,
                VariableName = variableName ?? ""
            });
            return id;
        }

    }
}