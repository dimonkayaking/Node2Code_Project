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
    public class RoslynCodeParser
    {
        /// <summary>Обёртка: заглушка Mathf + метод; число '\n' до пользовательского кода = смещение строк в диагностиках.</summary>
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
        private GraphData _graph = null!;
        private List<string> _errors = null!;

        // Смещение по строкам для кода, извлечённого из class-обёртки (StripClassWrapper).
        // Нужно, чтобы FormatUserLocation показывал позиции относительно исходного файла (п.4).
        private int _userCodeLineOffset;

        // Лимит глубины рекурсии VisitExpression — защита от StackOverflow на
        // патологически глубоко вложенных выражениях (п.7).
        private int _expressionDepth;
        private const int MaxExpressionDepth = 200;

        // Таблицы символов и типов переменных на стеке областей видимости.
        // Top стека — самая внутренняя область. Позволяет корректно объявлять
        // одно и то же имя в разных if/for/while-блоках без ложных ошибок
        // "повторного объявления".
        private readonly Stack<Dictionary<string, string>> _symbolScopes = new Stack<Dictionary<string, string>>();
        private readonly Stack<Dictionary<string, string>> _typeScopes = new Stack<Dictionary<string, string>>();

        private void PushVarScope()
        {
            _symbolScopes.Push(new Dictionary<string, string>());
            _typeScopes.Push(new Dictionary<string, string>());
        }

        private void PopVarScope()
        {
            if (_symbolScopes.Count > 1) _symbolScopes.Pop();
            if (_typeScopes.Count > 1) _typeScopes.Pop();
        }

        private bool SymbolVisible(string name)
        {
            foreach (var scope in _symbolScopes)
                if (scope.ContainsKey(name)) return true;
            return false;
        }

        private bool TryGetSymbol(string name, out string nodeId)
        {
            foreach (var scope in _symbolScopes)
                if (scope.TryGetValue(name, out nodeId!)) return true;
            nodeId = null!;
            return false;
        }

        private void SetSymbol(string name, string nodeId)
        {
            foreach (var scope in _symbolScopes)
            {
                if (scope.ContainsKey(name))
                {
                    scope[name] = nodeId;
                    return;
                }
            }
            if (_symbolScopes.Count == 0) PushVarScope();
            _symbolScopes.Peek()[name] = nodeId;
        }

        private bool TryGetVariableType(string name, out string type)
        {
            foreach (var scope in _typeScopes)
                if (scope.TryGetValue(name, out type!)) return true;
            type = null!;
            return false;
        }

        private void SetVariableType(string name, string type)
        {
            foreach (var scope in _typeScopes)
            {
                if (scope.ContainsKey(name))
                {
                    scope[name] = type;
                    return;
                }
            }
            if (_typeScopes.Count == 0) PushVarScope();
            _typeScopes.Peek()[name] = type;
        }

        private bool _inSubGraph;
        private GraphData _rootGraph = null!;
        private readonly Stack<GraphData> _graphStack = new Stack<GraphData>();
        private readonly Stack<Dictionary<string, string>?> _varRefStack = new Stack<Dictionary<string, string>?>();
        private Dictionary<string, string>? _subGraphVarRefs;

        // ── Семантика (п.1) ───────────────────────────────────────────────────────
        // Семантическая модель строится по обёрнутому дереву и используется ТОЛЬКО как
        // помощник вывода типов (var/double/long/… → корректный поддерживаемый тип).
        // НЕ источник ошибок: passthrough незнакомых API (Mathf.Sqrt, Mathf.PI и т.п.)
        // сознательно сохраняется. Любой сбой построения модели → молчаливый фолбэк
        // на прежнее строковое сопоставление типов.
        private SemanticModel? _semanticModel;

        /// <summary>
        /// Жёсткая семантическая валидация. По умолчанию ВЫКЛ — поведение и набор
        /// диагностик не меняются. При включении к ошибкам добавляются семантические
        /// (несовместимость типов и т.п.), но НЕ те, на которых держится passthrough
        /// (отсутствующие имена/члены/типы — CS0103/CS0117/CS0234/CS0246/CS1061).
        /// </summary>
        public bool StrictSemantics { get; set; }

        // Ссылки на сборки кэшируются: построение MetadataReference дороже самой модели.
        private static MetadataReference[]? _cachedReferences;

        public ParseResult Parse(string code)
        {
            _nodeCounter = 0;
            _graph = new GraphData();
            _rootGraph = _graph;
            _errors = new List<string>();
            _symbolScopes.Clear();
            _typeScopes.Clear();
            PushVarScope();
            _inSubGraph = false;
            _graphStack.Clear();
            _varRefStack.Clear();
            _subGraphVarRefs = null;
            _semanticModel = null;
            _userCodeLineOffset = 0;
            _expressionDepth = 0;

            if (string.IsNullOrWhiteSpace(code))
            {
                _errors.Add("Код пуст");
                return Result();
            }

            // Если код содержит top-level class/namespace — извлекаем тело Main-метода,
            // чтобы не получить «class внутри метода» при оборачивании.
            code = StripClassWrapper(code);

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

            // ── Семантическая модель (п.1) ────────────────────────────────────────
            // Строим только для синтаксически корректного кода. Используется как
            // помощник вывода типов; при сбое — молчаливый фолбэк (null).
            BuildSemanticModel(tree);

            // Жёсткая валидация (опционально, по умолчанию ВЫКЛ).
            if (StrictSemantics)
            {
                CollectSemanticErrors(tree);
                if (_errors.Count > 0)
                    return Result();
            }

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

        /// <summary>
        /// Если <paramref name="code"/> содержит top-level объявление класса/namespace,
        /// пытается извлечь тело первого метода с именем Main (или первого метода вообще).
        /// Иначе возвращает исходный код без изменений.
        /// Это позволяет парсить полные файлы вида <c>class Program { static void Main() { ... } }</c>.
        /// </summary>
        private string StripClassWrapper(string code)
        {
            // Парсим без обёртки и проверяем наличие top-level объявления типа/namespace
            // по дереву (надёжнее, чем сопоставление префикса строки): ловит
            // sealed/abstract/partial class, record, struct, enum, атрибуты, ведущие
            // комментарии и т.п. (п.5).
            var rawTree = CSharpSyntaxTree.ParseText(
                code, new CSharpParseOptions(LanguageVersion.Latest));
            var rawRoot = rawTree.GetCompilationUnitRoot();

            if (!HasTopLevelTypeDeclaration(rawRoot))
                return code;

            // Сначала пробуем метод с именем Main, затем любой первый метод
            var methodBody = rawRoot.DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>()
                                 .FirstOrDefault(m => m.Identifier.Text == "Main")
                                 ?.Body
                             ?? rawRoot.DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>()
                                 .FirstOrDefault()
                                 ?.Body;

            if (methodBody == null || methodBody.Statements.Count == 0)
                return code;

            // Возвращаем текст всех statements внутри метода и запоминаем, на сколько
            // строк исходного файла смещён извлечённый фрагмент — чтобы диагностики
            // показывали позиции относительно оригинала, а не обрезка (п.4).
            var start = methodBody.Statements.First().SpanStart;
            var end   = methodBody.Statements.Last().Span.End;
            _userCodeLineOffset = rawTree.GetLineSpan(new TextSpan(start, 0)).StartLinePosition.Line;
            return code.Substring(start, end - start);
        }

        /// <summary>Есть ли в компиляционной единице top-level объявление типа или namespace.</summary>
        private static bool HasTopLevelTypeDeclaration(CompilationUnitSyntax root)
        {
            foreach (var member in root.Members)
            {
                if (member is BaseTypeDeclarationSyntax or NamespaceDeclarationSyntax)
                    return true;
            }
            return false;
        }

        /// <summary>Строка:колонка относительно исходного кода пользователя (без служебной обёртки).</summary>
        private string FormatUserLocation(SyntaxTree tree, TextSpan span)
        {
            var pos = tree.GetLineSpan(span);
            var line1 = pos.StartLinePosition.Line + 1;
            var col1 = pos.StartLinePosition.Character + 1;
            // Вычитаем строки служебной обёртки и прибавляем смещение фрагмента,
            // извлечённого из class-обёртки (0, если код не оборачивался).
            var userLine = line1 - WrapperNewlinesBeforeUser + _userCodeLineOffset;
            if (userLine < 1)
                return $"{line1}:{col1} (служебная обёртка)";
            return $"{userLine}:{col1}";
        }

        // ── Семантика: построение модели и вывод типов (п.1) ──────────────────────

        /// <summary>Строит <see cref="SemanticModel"/> по обёрнутому дереву. Любой сбой → null.</summary>
        private void BuildSemanticModel(SyntaxTree tree)
        {
            try
            {
                var compilation = CSharpCompilation.Create(
                    "VsParseSemantic",
                    new[] { tree },
                    GetMetadataReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                _semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            }
            catch
            {
                _semanticModel = null;
            }
        }

        /// <summary>
        /// Минимальный набор ссылок для разрешения базовых типов (corelib достаточно
        /// для SpecialType и числовых литералов). Кэшируется. Под Unity/IL2CPP, где
        /// <c>Assembly.Location</c> может быть пустым, список окажется пустым —
        /// тогда вывод типов просто не активируется (фолбэк на строковое сопоставление).
        /// </summary>
        private static MetadataReference[] GetMetadataReferences()
        {
            if (_cachedReferences != null)
                return _cachedReferences;

            var list = new List<MetadataReference>();
            void TryAdd(System.Reflection.Assembly asm)
            {
                try
                {
                    var loc = asm.Location;
                    if (!string.IsNullOrEmpty(loc))
                        list.Add(MetadataReference.CreateFromFile(loc));
                }
                catch { /* ignore */ }
            }

            TryAdd(typeof(object).Assembly);
            try
            {
                if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa && !string.IsNullOrEmpty(tpa))
                {
                    foreach (var path in tpa.Split(System.IO.Path.PathSeparator))
                    {
                        if (path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            try { list.Add(MetadataReference.CreateFromFile(path)); } catch { /* ignore */ }
                        }
                    }
                }
            }
            catch { /* ignore */ }

            _cachedReferences = list.ToArray();
            return _cachedReferences;
        }

        /// <summary>
        /// Сопоставляет объявленный тип переменной с поддерживаемым (int/float/bool/string).
        /// Для явных int/float/bool/string — быстрый путь без семантики (поведение не меняется).
        /// Для <c>var</c> и неподдерживаемых алиасов (double/long/uint/byte/…) пытается вывести
        /// тип через <see cref="SemanticModel"/>; при неудаче — прежнее правило (всё прочее → int).
        /// </summary>
        private string MapDeclaredType(TypeSyntax typeSyntax, ExpressionSyntax? initializer, string typeStr)
        {
            switch (typeStr)
            {
                case "int":
                case "float":
                case "bool":
                case "string":
                    return typeStr;
            }

            var inferred = TryInferSupportedType(typeStr == "var" ? (SyntaxNode?)initializer : typeSyntax);
            if (inferred != null)
                return inferred;

            return typeStr switch
            {
                "float" => "float",
                "bool" => "bool",
                "string" => "string",
                _ => "int"
            };
        }

        /// <summary>Выводит поддерживаемый тип из семантической модели; null, если невозможно.</summary>
        private string? TryInferSupportedType(SyntaxNode? node)
        {
            if (node == null || _semanticModel == null)
                return null;

            try
            {
                // TypeSyntax наследует ExpressionSyntax, поэтому явный тип резолвим
                // отдельно (GetTypeInfo, при пустом — GetSymbolInfo), а инициализатор —
                // через GetTypeInfo как обычное выражение.
                ITypeSymbol? t;
                if (node is TypeSyntax ts)
                {
                    t = _semanticModel.GetTypeInfo(ts).Type
                        ?? _semanticModel.GetSymbolInfo(ts).Symbol as ITypeSymbol;
                }
                else if (node is ExpressionSyntax e)
                {
                    t = _semanticModel.GetTypeInfo(e).Type;
                }
                else
                {
                    return null;
                }

                if (t == null || t.TypeKind == TypeKind.Error)
                    return null;

                return t.SpecialType switch
                {
                    SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => "float",
                    SpecialType.System_Boolean => "bool",
                    SpecialType.System_String or SpecialType.System_Char => "string",
                    SpecialType.System_SByte or SpecialType.System_Byte
                        or SpecialType.System_Int16 or SpecialType.System_UInt16
                        or SpecialType.System_Int32 or SpecialType.System_UInt32
                        or SpecialType.System_Int64 or SpecialType.System_UInt64 => "int",
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// При <see cref="StrictSemantics"/> добавляет семантические ошибки, КРОМЕ тех,
        /// на которых держится passthrough (отсутствующие имена/члены/типы).
        /// </summary>
        private void CollectSemanticErrors(SyntaxTree tree)
        {
            if (_semanticModel == null)
                return;

            try
            {
                foreach (var d in _semanticModel.Compilation.GetDiagnostics()
                             .Where(x => x.Severity == DiagnosticSeverity.Error && !IsPassthroughDiagnostic(x.Id)))
                {
                    _errors.Add($"{d.GetMessage()} ({FormatUserLocation(tree, d.Location.SourceSpan)})");
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Коды диагностик, которые намеренно игнорируются ради passthrough незнакомых API.</summary>
        private static bool IsPassthroughDiagnostic(string id) =>
            id is "CS0103"   // имя не существует в текущем контексте
                or "CS0117"  // тип не содержит определения члена
                or "CS0234"  // нет члена в пространстве имён
                or "CS0246"  // тип/namespace не найден
                or "CS1061"; // нет определения/метода расширения

        private void VisitMethodBody(BlockSyntax body)
        {
            string? prevFlowNode = null;
            var prevFlowPort = "execOut";

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
                        prevFlowPort = "execOut";
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

        private sealed class FlowHost
        {
            public required string NodeId { get; init; }
            public string ExecOutPort { get; init; } = "execOut";
        }

        private FlowHost? VisitStatementForFlow(StatementSyntax stmt, string? prevNode, string prevPort)
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

        private FlowHost? VisitLocalDeclaration(LocalDeclarationStatementSyntax local, string? prevNode, string prevPort)
        {
            FlowHost? last = null;
            foreach (var v in local.Declaration.Variables)
            {
                var name = v.Identifier.Text;

                if (SymbolVisible(name))
                {
                    _errors.Add(
                        $"Повторное объявление переменной «{name}» ({FormatUserLocation(local.SyntaxTree, v.Identifier.Span)}).");
                    continue;
                }

                var typeStr = local.Declaration.Type.ToString().Trim();
                var vType = MapDeclaredType(local.Declaration.Type, v.Initializer?.Value, typeStr);
                _typeScopes.Peek()[name] = vType;

                if (v.Initializer == null)
                {
                    var declId = CreateDefaultLiteralNode(vType, name);
                    _symbolScopes.Peek()[name] = declId;

                    var declHost = new FlowHost { NodeId = declId };
                    if (last != null)
                        AddEdge(last.NodeId, last.ExecOutPort, declHost.NodeId, "execIn");
                    else if (prevNode != null)
                        AddEdge(prevNode, prevPort, declHost.NodeId, "execIn");
                    last = declHost;
                    continue;
                }

                var rootId = VisitExpression(v.Initializer.Value, false, null, out var unsupported);
                if (unsupported)
                    continue;

                if (rootId == null)
                    continue;

                var rootNode = _graph.Nodes.FirstOrDefault(n => n.Id == rootId);
                string litId;
                if (rootNode != null && IsLiteralNodeType(rootNode.Type))
                {
                    rootNode.VariableName = name;
                    // Исправляем тип: тернарник и другие opaque-выражения создают LiteralString,
                    // но объявленный тип переменной (vType) всегда точнее.
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
                        // Store the original expression text so generators can emit it directly
                        // (e.g. "x + y" instead of the computed literal "30").
                        litNode.ExpressionOverride = v.Initializer.Value.ToString().Trim();
                        var computed = TryEvaluateExpression(rootId);
                        if (computed != null)
                            litNode.Value = computed;
                    }
                }

                _symbolScopes.Peek()[name] = litId;

                var host = new FlowHost { NodeId = litId };
                if (last != null)
                    AddEdge(last.NodeId, last.ExecOutPort, host.NodeId, "execIn");
                else if (prevNode != null)
                    AddEdge(prevNode, prevPort, host.NodeId, "execIn");

                last = host;
            }

            return last;
        }

        private FlowHost? VisitExpressionStatement(ExpressionStatementSyntax stmt, string? prevNode, string prevPort)
        {
            var expr = stmt.Expression;

            if (expr is InvocationExpressionSyntax inv && IsConsoleWriteLine(inv))
                return VisitConsoleWriteLine(inv, prevNode, prevPort);

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
                    // Only rename the RHS node when it is a fresh unnamed literal (e.g. the node
                    // created for the literal 10 in "z = 10"). When the node already has a variable
                    // name it is a variable-reference copy and must NOT be renamed.
                    if (rootNode != null && IsLiteralNodeType(rootNode.Type)
                        && string.IsNullOrEmpty(rootNode.VariableName))
                    {
                        rootNode.VariableName = name;
                        litId = rootId;
                    }
                    else
                    {
                        var vType = TryGetVariableType(name, out var t) ? t : "int";
                        litId = CreateDefaultLiteralNode(vType, name);
                        AddEdge(rootId, GetDataOutPortForNodeId(rootId), litId, "inputValue");
                        var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
                        if (litNode != null)
                            litNode.ExpressionOverride = assign.Right.ToString().Trim();
                    }

                    SetSymbol(name, litId);

                    var host = new FlowHost { NodeId = litId };
                    if (prevNode != null)
                        AddEdge(prevNode, prevPort, host.NodeId, "execIn");
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

        private FlowHost? VisitConsoleWriteLine(InvocationExpressionSyntax inv, string? prevNode, string prevPort)
        {
            var nodeId = NewId();
            var nodeData = new NodeData
            {
                Id = nodeId,
                Type = NodeType.ConsoleWriteLine,
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
                        AddEdge(prevNode, prevPort, nodeId, "execIn");
                    return new FlowHost { NodeId = nodeId };
                }
            }

            _graph.Nodes.Add(nodeData);

            if (prevNode != null)
                AddEdge(prevNode, prevPort, nodeId, "execIn");

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

        private FlowHost? VisitCompoundAssignment(AssignmentExpressionSyntax assign, string? prevNode, string prevPort)
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
            if (_inSubGraph && SymbolVisible(name))
            {
                leftId = CreateVariableRefInSubGraph(name);
            }
            else if (!TryGetSymbol(name, out var tempLeft))
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

            var vType = TryGetVariableType(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);

            AddEdge(opId, "output", litId, "inputValue");
            SetSymbol(name, litId);

            var host = new FlowHost { NodeId = litId };
            if (prevNode != null)
                AddEdge(prevNode, prevPort, litId, "execIn");
            return host;
        }

        private FlowHost? VisitIncrementDecrementStatement(
            IdentifierNameSyntax idExpr,
            bool increment,
            string? prevNode,
            string prevPort)
        {
            var name = idExpr.Identifier.Text;
            string varNodeId;
            if (_inSubGraph && SymbolVisible(name))
            {
                varNodeId = CreateVariableRefInSubGraph(name);
            }
            else if (!TryGetSymbol(name, out var tempVar))
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

            var vType = TryGetVariableType(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);

            AddEdge(opId, "output", litId, "inputValue");
            SetSymbol(name, litId);

            var host = new FlowHost { NodeId = litId };
            if (prevNode != null)
                AddEdge(prevNode, prevPort, litId, "execIn");
            return host;
        }

        private FlowHost? VisitForStatement(ForStatementSyntax forStmt, string? prevNode, string prevPort)
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
                AddEdge(prevNode, prevPort, forId, "execIn");

            // for создаёт новую область видимости, покрывающую init/cond/inc/body:
            // переменная цикла из init должна быть видна в condition, increment и теле,
            // но не после выхода из for.
            PushVarScope();

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

            PopVarScope();

            _graph.Nodes.Add(forNodeData);

            return new FlowHost { NodeId = forId, ExecOutPort = "execOut" };
        }

        private void VisitForInitialization(ForStatementSyntax forStmt)
        {
            if (forStmt.Declaration != null)
            {
                foreach (var v in forStmt.Declaration.Variables)
                {
                    var name = v.Identifier.Text;
                    if (SymbolVisible(name))
                    {
                        _errors.Add(
                            $"Повторное объявление переменной «{name}» ({FormatUserLocation(forStmt.SyntaxTree, v.Identifier.Span)}).");
                        continue;
                    }

                    var typeStr = forStmt.Declaration.Type.ToString().Trim();
                    var vType0 = MapDeclaredType(forStmt.Declaration.Type, v.Initializer?.Value, typeStr);
                    _typeScopes.Peek()[name] = vType0;

                    if (v.Initializer == null)
                        continue;

                    var rootId = VisitExpression(v.Initializer.Value, true, name, out var unsupported);
                    if (unsupported || rootId == null)
                        continue;

                    _symbolScopes.Peek()[name] = rootId;
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
                        var vType = TryGetVariableType(n, out var t) ? t : "int";
                        litId = CreateDefaultLiteralNode(vType, n);
                        AddEdge(rootId, GetDataOutPortForNodeId(rootId), litId, "inputValue");
                    }

                    SetSymbol(n, litId);
                    continue;
                }

                VisitExpression(initExpr, false, null, out _);
            }
        }

        private string? VisitIncrementExpression(ExpressionSyntax expr, out bool unsupported)
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

        private string? BuildIncrementSubgraph(IdentifierNameSyntax id, bool increment, out bool unsupported)
        {
            unsupported = false;
            var name = id.Identifier.Text;
            
            string varNodeId;
            if (_inSubGraph && SymbolVisible(name))
            {
                varNodeId = CreateVariableRefInSubGraph(name);
            }
            else if (!TryGetSymbol(name, out var temp))
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

            var vType = TryGetVariableType(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
            if (litNode != null)
                litNode.Value = "?";

            AddEdge(opId, "output", litId, "inputValue");
            SetSymbol(name, litId);

            return opId;
        }

        private string? BuildIncrementAssignmentSubgraph(AssignmentExpressionSyntax assign, out bool unsupported)
        {
            unsupported = false;
            if (assign.Left is not IdentifierNameSyntax idLeft)
            {
                unsupported = true;
                return null;
            }

            var name = idLeft.Identifier.Text;
            if (!SymbolVisible(name))
            {
                unsupported = true;
                _errors.Add(
                    $"Неизвестная переменная «{name}» ({FormatUserLocation(assign.SyntaxTree, assign.Span)}).");
                return null;
            }

            var rhsId = VisitExpression(assign.Right, false, null, out unsupported);
            if (unsupported || rhsId == null)
                return null;

            var vType = TryGetVariableType(name, out var t) ? t : "int";
            var litId = CreateDefaultLiteralNode(vType, name);
            var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
            if (litNode != null)
                litNode.Value = "?";

            AddEdge(rhsId, GetDataOutPortForNodeId(rhsId), litId, "inputValue");
            SetSymbol(name, litId);
            return litId;
        }

        private FlowHost? VisitWhileStatement(WhileStatementSyntax whileStmt, string? prevNode, string prevPort)
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
                AddEdge(prevNode, prevPort, whileId, "execIn");

            var condGraph = new GraphData();
            PushSubGraph(condGraph);
            VisitExpression(whileStmt.Condition, false, null, out _);
            PopSubGraph();
            whileNodeData.ConditionSubGraph = condGraph;

            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            PushVarScope();
            var bodyStmts = ExpandStatement(whileStmt.Statement);
            BuildStatementsInSubGraph(bodyStmts);
            PopVarScope();
            PopSubGraph();
            whileNodeData.BodySubGraph = bodyGraph;

            _graph.Nodes.Add(whileNodeData);

            return new FlowHost { NodeId = whileId, ExecOutPort = "execOut" };
        }

        private FlowHost? VisitIfChain(IfStatementSyntax stmt, string? incomingNodeId, string? incomingPort)
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
            PushVarScope();
            var thenStmts = ExpandStatement(stmt.Statement);
            BuildStatementsInSubGraph(thenStmts);
            PopVarScope();
            PopSubGraph();
            ifNodeData.BodySubGraph = bodyGraph;

            _graph.Nodes.Add(ifNodeData);

            if (incomingNodeId != null && incomingPort != null)
                AddEdge(incomingNodeId, incomingPort, ifNodeId, "execIn");

            if (stmt.Else != null)
            {
                if (stmt.Else.Statement is IfStatementSyntax elseIf)
                {
                    VisitIfChain(elseIf, ifNodeId, "falseBranch");
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
                    PushVarScope();
                    var elseStmts = ExpandStatement(stmt.Else.Statement);
                    BuildStatementsInSubGraph(elseStmts);
                    PopVarScope();
                    PopSubGraph();
                    elseNodeData.BodySubGraph = elseBodyGraph;

                    _graph.Nodes.Add(elseNodeData);
                    AddEdge(ifNodeId, "falseBranch", elseNodeId, "execIn");
                }
            }

            return new FlowHost { NodeId = ifNodeId, ExecOutPort = "execOut" };
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
            string? prevId = null;
            var prevPort = "execOut";

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
                        prevPort = "execOut";
                    }
                    continue;
                }

                if (st is ForStatementSyntax nestedFor)
                {
                    var fh = VisitForStatement(nestedFor, prevId, prevPort);
                    if (fh != null) { prevId = fh.NodeId; prevPort = fh.ExecOutPort; }
                    else { prevId = null; prevPort = "execOut"; }
                    continue;
                }

                if (st is WhileStatementSyntax nestedWhile)
                {
                    var wh = VisitWhileStatement(nestedWhile, prevId, prevPort);
                    if (wh != null) { prevId = wh.NodeId; prevPort = wh.ExecOutPort; }
                    else { prevId = null; prevPort = "execOut"; }
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

            var vType = TryGetVariableType(varName, out var t) ? t : "int";
            NodeType litType = vType switch
            {
                "float" => NodeType.LiteralFloat,
                "bool" => NodeType.LiteralBool,
                "string" => NodeType.LiteralString,
                _ => NodeType.LiteralInt
            };

            var value = "";
            var expressionOverride = "";
            if (TryGetSymbol(varName, out var sourceId))
            {
                var source = FindNodeByIdInTree(_rootGraph, sourceId);
                if (source != null)
                {
                    if (IsLiteralNodeType(source.Type))
                    {
                        value = source.Value ?? "";
                        expressionOverride = source.ExpressionOverride ?? "";
                    }
                    else if (!string.IsNullOrEmpty(source.ExpressionOverride))
                    {
                        expressionOverride = source.ExpressionOverride;
                        value = source.Value ?? "";
                    }
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
                ExpressionOverride = expressionOverride
            });

            if (_subGraphVarRefs != null)
                _subGraphVarRefs[varName] = id;
            return id;
        }

        /// <summary>Ищет ноду по id в корневом графе и во всех вложенных подграфах (условие, тело, for-init и т.д.).</summary>
        private static NodeData? FindNodeByIdInTree(GraphData? graph, string nodeId) =>
            FindNodeByIdInTree(graph, nodeId, new HashSet<GraphData>());

        private static NodeData? FindNodeByIdInTree(GraphData? graph, string nodeId, HashSet<GraphData> visited)
        {
            // visited.Add по ссылочному равенству: если граф пришёл извне с циклом
            // (подграф ссылается на уже посещённый), рекурсия не уйдёт в бесконечность (п.7).
            if (graph == null || string.IsNullOrEmpty(nodeId) || !visited.Add(graph))
                return null;

            foreach (var n in graph.Nodes)
            {
                if (n.Id == nodeId)
                    return n;
            }

            foreach (var n in graph.Nodes)
            {
                var found = FindNodeByIdInTree(n.ConditionSubGraph, nodeId, visited)
                            ?? FindNodeByIdInTree(n.BodySubGraph, nodeId, visited)
                            ?? FindNodeByIdInTree(n.InitSubGraph, nodeId, visited)
                            ?? FindNodeByIdInTree(n.IncrementSubGraph, nodeId, visited);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static IReadOnlyList<StatementSyntax> ExpandStatement(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements.ToList();
            return new List<StatementSyntax> { statement };
        }

        // Мёртвый ProcessBlockStatements удалён: всю работу выполняет BuildStatementsInSubGraph.

        private string? VisitExpression(ExpressionSyntax expr, bool isRoot, string? assignVariableToRoot, out bool unsupported)
        {
            unsupported = false;

            if (++_expressionDepth > MaxExpressionDepth)
            {
                _expressionDepth--;
                unsupported = true;
                _errors.Add(
                    $"Слишком глубоко вложенное выражение ({FormatUserLocation(expr.SyntaxTree, expr.Span)}).");
                return null;
            }

            try
            {
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
                    var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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
            finally
            {
                _expressionDepth--;
            }
        }

        private string? VisitBinary(BinaryExpressionSyntax bin, bool isRoot, string? assignVariableToRoot, out bool unsupported)
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
            var varName = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        /// <summary>Constant-fold math trees at parse time (e.g. int z = x + y with x=10,y=20 → "30").</summary>
        private string? TryEvaluateExpression(string nodeId)
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
                // Деление/остаток на ноль НЕ сворачиваем: возвращаем null, чтобы сохранить
                // исходное выражение, а не подменять его тихим «0» (п.7).
                if ((node.Type == NodeType.MathDivide || node.Type == NodeType.MathModulo) && ri == 0)
                    return null;

                int result = node.Type switch
                {
                    NodeType.MathAdd => li + ri,
                    NodeType.MathSubtract => li - ri,
                    NodeType.MathMultiply => li * ri,
                    NodeType.MathDivide => li / ri,
                    NodeType.MathModulo => li % ri,
                    _ => 0
                };
                return result.ToString();
            }

            return null;
        }

        private static bool IsMath(NodeType t) =>
            t is NodeType.MathAdd or NodeType.MathSubtract or NodeType.MathMultiply
                or NodeType.MathDivide or NodeType.MathModulo;

        private string CreateStringExpressionLiteralNode(string expressionText, string? variableName)
        {
            var id = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralString,
                Value = "",
                ValueType = "string",
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

        private string? VisitInvocationExpression(
            InvocationExpressionSyntax inv,
            bool isRoot,
            string? assignVariableToRoot,
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

            // Любые остальные Mathf.* / Math.* (Sqrt, Pow, Clamp, PI и т.д.) — сохраняем текст выражения для генерации кода.
            if (IsMathfStaticReceiver(ma.Expression) || IsSystemMathStaticReceiver(ma.Expression))
                return CreatePassthroughMathLiteral(inv.ToString(), isRoot, assignVariableToRoot);

            unsupported = true;
            _errors.Add(
                $"Неподдерживаемый вызов метода ({FormatUserLocation(inv.SyntaxTree, inv.Span)}): {methodName}.");
            return null;
        }

        /// <summary>
        /// Выражение Mathf/Math целиком в одну ноду (генератор подставляет <see cref="NodeData.ExpressionOverride"/>).
        /// </summary>
        private string CreatePassthroughMathLiteral(string expressionText, bool isRoot, string? assignVariableToRoot)
        {
            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        private string? VisitUnaryMinus(
            PrefixUnaryExpressionSyntax pre,
            bool isRoot,
            string? assignVariableToRoot,
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
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        private string? CreateNegatedNumericLiteral(
            LiteralExpressionSyntax lit,
            bool isRoot,
            string? assignVariableToRoot)
        {
            var text = lit.Token.Text;
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";

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
            // FirstOrDefault + guard: операнд может лежать в другом графе (подграф vs
            // текущий _graph), тогда First бросил бы InvalidOperationException (п.7).
            var n = _graph.Nodes.FirstOrDefault(x => x.Id == operandNodeId);
            var useFloat = n != null &&
                           (n.Type == NodeType.LiteralFloat ||
                            string.Equals(n.ValueType, "float", StringComparison.OrdinalIgnoreCase));
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

        private string? CreateParseNode(
            NodeType parseType,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string? assignVariableToRoot,
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
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        private string? CreateMathfNode(
            NodeType mathfType,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string? assignVariableToRoot,
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
                var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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
            var varName = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        private string? CreateToStringNode(
            ExpressionSyntax? receiver,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string? assignVariableToRoot,
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
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot! : "";
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

        private string? ResolveIdentifier(IdentifierNameSyntax id, out bool unsupported)
        {
            unsupported = false;
            var name = id.Identifier.Text;

            if (_inSubGraph && SymbolVisible(name))
                return CreateVariableRefInSubGraph(name);

            if (TryGetSymbol(name, out var nodeId))
                return nodeId;

            unsupported = true;
            _errors.Add(
                $"Неизвестный идентификатор «{name}» ({FormatUserLocation(id.SyntaxTree, id.Span)}).");
            return null;
        }

        private string? CreateLiteralFromLiteralExpression(LiteralExpressionSyntax lit, string? variableName)
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
            _graph.Edges.Add(new EdgeData
            {
                FromNodeId = fromId,
                FromPort = fromPort,
                ToNodeId = toId,
                ToPort = toPort
            });
        }

        private string NewId() => $"node_{_nodeCounter++}";
    }
}
