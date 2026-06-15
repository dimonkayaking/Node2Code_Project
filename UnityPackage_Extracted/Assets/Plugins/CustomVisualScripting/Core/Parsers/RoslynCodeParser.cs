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

        // Пользовательские методы, переданные из GeneratorBridge/ParserBridge.
        // _baseKnownMethods — неизменяемый набор, заданный через SetKnownMethods.
        // _knownMethods — рабочая копия на время одного Parse() (дополняется class-методами
        // и локальными функциями текущего разбора и сбрасывается в начале каждого Parse()).
        private IReadOnlyList<MethodInfo> _baseKnownMethods = System.Array.Empty<MethodInfo>();
        private IReadOnlyList<MethodInfo> _knownMethods = System.Array.Empty<MethodInfo>();

        // Глубина рекурсии VisitExpression — защита от StackOverflow на патологически
        // глубоко вложенных выражениях.
        private int _expressionDepth;

        // Смещение по строкам для кода, извлечённого из class-обёртки (StripClassWrapper).
        // Нужно, чтобы FormatUserLocation показывал позиции относительно исходного файла.
        private int _userCodeLineOffset;

        // ── Семантика (п.1) ───────────────────────────────────────────────────────
        // Модель строится по обёрнутому дереву и используется ТОЛЬКО как помощник
        // вывода типов (var и выражения, для которых литеральной эвристики мало).
        // НЕ источник ошибок: passthrough незнакомых API (Mathf.Sqrt и т.п.) сохраняется.
        // Любой сбой построения → молчаливый фолбэк (null).
        private SemanticModel _semanticModel;

        /// <summary>
        /// Жёсткая семантическая валидация. По умолчанию ВЫКЛ — поведение не меняется.
        /// При включении добавляются семантические ошибки, КРОМЕ кодов, на которых
        /// держится passthrough (CS0103/CS0117/CS0234/CS0246/CS1061).
        /// </summary>
        public bool StrictSemantics { get; set; }

        // Ссылки на сборки кэшируются между вызовами.
        private static MetadataReference[] _cachedReferences;

        // Методы, обнаруженные при парсинге как inline-локальные функции
        private List<MethodInfo> _discoveredMethods = new List<MethodInfo>();
        // Флаг: предотвращает рекурсивный парсинг тел вложенных локальных функций
        private bool _isParsingFunctionBody;
        // Методы класса, извлечённые из top-level class wrapper при StripClassWrapper
        private List<MethodDeclarationSyntax> _pendingClassMethods = new List<MethodDeclarationSyntax>();

        // Структура классов, обнаруженная при StripClassWrapper
        private bool _hasClassWrapper;
        private string _mainClassName = "";
        private List<ParsedClassInfo> _discoveredClasses = new List<ParsedClassInfo>();

        /// <summary>Передаёт метаданные зарегистрированных методов до вызова Parse().</summary>
        public void SetKnownMethods(IReadOnlyList<MethodInfo> methods)
        {
            _baseKnownMethods = methods ?? System.Array.Empty<MethodInfo>();
        }

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
            _discoveredMethods    = new List<MethodInfo>();
            _isParsingFunctionBody = false;
            _pendingClassMethods  = new List<MethodDeclarationSyntax>();
            _hasClassWrapper      = false;
            _mainClassName        = "";
            _discoveredClasses    = new List<ParsedClassInfo>();
            _expressionDepth = 0;
            _userCodeLineOffset = 0;
            _semanticModel = null;

            // Рабочая копия известных методов — свежая на каждый вызов, чтобы class-методы
            // и локальные функции прошлого разбора не «протекали» в текущий.
            _knownMethods = _baseKnownMethods.Count > 0
                ? new List<MethodInfo>(_baseKnownMethods)
                : System.Array.Empty<MethodInfo>();

            if (string.IsNullOrWhiteSpace(code))
            {
                _errors.Add("Код пуст");
                return Result();
            }

            // Если код содержит top-level class/namespace — извлекаем тело Main-метода
            // и собираем прочие методы класса в _pendingClassMethods.
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
            // Строим только для синтаксически корректного кода. Помощник вывода типов;
            // при сбое — молчаливый фолбэк (null).
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

            // Парсим тела методов, извлечённых из class-обёртки, и добавляем в DiscoveredMethods.
            // Это позволяет парсить файлы с несколькими методами в классе.
            if (_pendingClassMethods.Count > 0 && !_isParsingFunctionBody)
            {
                _isParsingFunctionBody = true;
                try
                {
                    foreach (var mdecl in _pendingClassMethods)
                        ParseAndDiscoverClassMethod(mdecl);
                }
                finally
                {
                    _isParsingFunctionBody = false;
                }
            }

            return Result();
        }

        private ParseResult Result() =>
            new ParseResult
            {
                Graph             = _graph,
                Errors            = _errors,
                DiscoveredMethods = _discoveredMethods,
                HasClassWrapper   = _hasClassWrapper,
                MainClassName     = _mainClassName,
                DiscoveredClasses = _discoveredClasses
            };

        /// <summary>
        /// Если <paramref name="code"/> содержит top-level объявление класса/namespace,
        /// извлекает тело метода Main в качестве основного кода.
        /// Все прочие (не-Main) static-методы собираются в <see cref="_pendingClassMethods"/>,
        /// а их сигнатуры сразу добавляются в <see cref="_knownMethods"/> — так вызовы этих методов
        /// внутри Main распознаются как <c>NodeType.MethodCall</c>.
        /// </summary>
        private string StripClassWrapper(string code)
        {
            var rawTree = CSharpSyntaxTree.ParseText(
                code, new CSharpParseOptions(LanguageVersion.Latest));
            var rawRoot = rawTree.GetCompilationUnitRoot();

            // Разворачиваем обёртку только если в коде есть top-level объявление типа
            // (class/struct/record/interface/enum) или namespace. Определяем по дереву,
            // а не по префиксу строки — устойчиво к атрибутам, комментариям и модификаторам.
            if (!HasTopLevelTypeDeclaration(rawRoot))
                return code;

            // Собираем структуру классов (методы + поля) для передачи в Editor-слой
            _hasClassWrapper = true;
            foreach (var classDecl in rawRoot.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var info = new ParsedClassInfo { Name = classDecl.Identifier.Text };

                // Извлекаем имя родительского класса из синтаксиса `: BaseClass`
                var baseType = classDecl.BaseList?.Types.FirstOrDefault();
                if (baseType != null)
                    info.BaseClassName = baseType.Type.ToString().Trim();

                // Методы
                foreach (var m in classDecl.Members.OfType<MethodDeclarationSyntax>())
                    info.MethodNames.Add(m.Identifier.Text);

                // Поля (public/private + static/instance)
                foreach (var fieldDecl in classDecl.Members.OfType<FieldDeclarationSyntax>())
                {
                    var rawType    = fieldDecl.Declaration.Type.ToString().Trim();
                    var mappedType = MapValueType(rawType) ?? "int";
                    var isPublic   = fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
                    var isStatic   = fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        var defaultVal = variable.Initializer?.Value?.ToString().Trim() ?? "";
                        info.Fields.Add(new ParsedFieldInfo
                        {
                            Name         = variable.Identifier.Text,
                            Type         = mappedType,
                            DefaultValue = defaultVal,
                            IsPublic     = isPublic,
                            IsStatic     = isStatic
                        });
                    }
                }

                _discoveredClasses.Add(info);
                if (info.MethodNames.Contains("Main"))
                    _mainClassName = info.Name;
            }

            var allMethods = rawRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .ToList();

            // Только метод с именем Main становится «основным графом» (корневой граф).
            // Нет fallback на первый метод: в классовой модели остальные методы
            // (Start, Update, пользовательские) — обычные методы класса.
            var mainMethod = allMethods.FirstOrDefault(m => m.Identifier.Text == "Main");

            // Все методы, КРОМЕ Main, — методы класса: регистрируем их сигнатуры как известные
            // (чтобы вызовы распознавались как MethodCall) и складываем тела в _pendingClassMethods.
            var extraMethods = new List<MethodInfo>(_knownMethods);
            foreach (var m in allMethods)
            {
                if (m == mainMethod || m.Body == null) continue;
                _pendingClassMethods.Add(m);
                var sig = BuildMethodInfoSignature(m);
                if (sig != null) extraMethods.Add(sig);
            }
            if (extraMethods.Count > _knownMethods.Count)
                _knownMethods = extraMethods;

            // Тело Main (если есть) становится корневым графом. Нет Main или пустое тело →
            // корневой граф пуст. Возвращать исходный код НЕЛЬЗЯ: top-level class не может
            // находиться внутри __VsParseMethod (Roslyn выдаст «} expected»). Классы и их
            // методы уже собраны выше.
            var methodBody = mainMethod?.Body;
            if (methodBody == null || methodBody.Statements.Count == 0)
                return "";

            var start = methodBody.Statements.First().SpanStart;
            var end   = methodBody.Statements.Last().Span.End;

            // Сколько строк исходного файла предшествует извлечённому коду — чтобы позиции
            // ошибок (FormatUserLocation) указывали на исходный файл, а не на фрагмент.
            _userCodeLineOffset = CountNewlines(code, start);

            return code.Substring(start, end - start);
        }

        private static bool HasTopLevelTypeDeclaration(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members)
            {
                if (m is BaseTypeDeclarationSyntax
                    || m is NamespaceDeclarationSyntax
                    || m.IsKind((SyntaxKind)8845)) // FileScopedNamespaceDeclaration (Roslyn 4.0+)
                    return true;
            }
            return false;
        }

        private static int CountNewlines(string text, int upToExclusive)
        {
            int count = 0;
            int limit = System.Math.Min(upToExclusive, text.Length);
            for (int i = 0; i < limit; i++)
                if (text[i] == '\n') count++;
            return count;
        }

        /// <summary>
        /// Строит <see cref="MethodInfo"/> только с сигнатурой (без тела) для class-level метода.
        /// ID: <c>"__classfn__" + methodName</c> — стабильный между сессиями.
        /// </summary>
        private static MethodInfo BuildMethodInfoSignature(MethodDeclarationSyntax mdecl)
        {
            if (mdecl == null) return null;
            var name = mdecl.Identifier.Text;
            var returnTypeStr = mdecl.ReturnType.ToString().Trim();
            var returnType = returnTypeStr == "void" ? "void" : (MapValueType(returnTypeStr) ?? "int");
            var paramNames = new List<string>();
            var paramTypes = new List<string>();
            foreach (var p in mdecl.ParameterList.Parameters)
            {
                paramNames.Add(p.Identifier.Text);
                var typeStr = p.Type?.ToString().Trim() ?? "int";
                paramTypes.Add(MapValueType(typeStr) ?? "int");
            }
            return new MethodInfo
            {
                Id         = "__classfn__" + name,
                Name       = name,
                ReturnType = returnType,
                IsPublic   = mdecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)),
                IsStatic   = mdecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
                ParamNames = paramNames,
                ParamTypes = paramTypes,
                BodyGraph  = null
            };
        }

        /// <summary>
        /// Парсит тело class-level метода и добавляет результат в <see cref="_discoveredMethods"/>.
        /// Берёт сигнатуру из ранее зарегистрированного <see cref="_knownMethods"/> по ID.
        /// </summary>
        private void ParseAndDiscoverClassMethod(MethodDeclarationSyntax mdecl)
        {
            if (mdecl?.Body == null) return;
            var id = "__classfn__" + mdecl.Identifier.Text;
            var mi = _knownMethods.FirstOrDefault(m => m.Id == id)
                     ?? BuildMethodInfoSignature(mdecl);
            if (mi == null) return;

            // Извлекаем поля родительского класса — они должны быть видны в теле метода.
            var classFields = ExtractClassFieldsFromParent(mdecl);

            mi.BodyGraph = ParseMethodBodyGraph(mdecl.Body, mi, classFields);
            _discoveredMethods.Add(mi);
        }

        /// <summary>
        /// Возвращает (name, mappedType) для каждого поля объявленного в классе-владельце метода.
        /// </summary>
        private static List<(string name, string type)> ExtractClassFieldsFromParent(
            MethodDeclarationSyntax mdecl)
        {
            var result = new List<(string, string)>();
            if (mdecl.Parent is not ClassDeclarationSyntax classDecl) return result;

            foreach (var member in classDecl.Members.OfType<FieldDeclarationSyntax>())
            {
                var rawType  = member.Declaration.Type.ToString().Trim();
                var typeStr  = MapValueType(rawType) ?? "int";
                foreach (var variable in member.Declaration.Variables)
                    result.Add((variable.Identifier.Text, typeStr));
            }
            return result;
        }

        /// <summary>
        /// Сопоставляет C#-тип одному из поддерживаемых графом значений (int/float/bool/string/Vector3
        /// либо одному из ссылочных Unity-типов из <see cref="UnityLibraryRegistry"/> — Transform,
        /// GameObject и т.п.). Числовые типы шире int сводятся к int, double — к float (с потерей точности).
        /// Возвращает <c>null</c> для совсем неподдерживаемых типов (var, decimal, char, пользовательские).
        /// </summary>
        private static string MapValueType(string typeStr) => typeStr switch
        {
            "float" or "Single"                                  => "float",
            "double" or "Double"                                 => "float",
            "bool" or "Boolean"                                  => "bool",
            "string" or "String"                                 => "string",
            "int" or "Int32"
                or "long" or "Int64"
                or "short" or "Int16"
                or "byte" or "Byte"
                or "sbyte" or "SByte"
                or "uint" or "UInt32"
                or "ushort" or "UInt16"
                or "ulong" or "UInt64"                           => "int",
            "Vector3"                                            => "Vector3",
            // Ссылочные Unity-типы (Transform, GameObject, ...) — узнаём по UnityLibraryRegistry,
            // чтобы поле класса не превращалось в "int" и сохраняло свой реальный тип
            // (public Transform transform; вместо public int transform;).
            _ when UnityLibraryRegistry.GetClass(typeStr) != null => typeStr,
            _                                                    => null
        };

        /// <summary>
        /// Выводит тип переменной <c>var</c> из выражения-инициализатора, когда это возможно
        /// без семантической модели (по виду литерала). Иначе — <c>int</c> как нейтральный тип.
        /// </summary>
        private static string InferVarType(ExpressionSyntax initializer)
        {
            var e = initializer;
            while (e is ParenthesizedExpressionSyntax p)
                e = p.Expression;
            if (e is PrefixUnaryExpressionSyntax u &&
                (u.IsKind(SyntaxKind.UnaryMinusExpression) || u.IsKind(SyntaxKind.UnaryPlusExpression)))
                e = u.Operand;

            if (e is LiteralExpressionSyntax lit)
            {
                switch (lit.Kind())
                {
                    case SyntaxKind.StringLiteralExpression:
                        return "string";
                    case SyntaxKind.TrueLiteralExpression:
                    case SyntaxKind.FalseLiteralExpression:
                        return "bool";
                    case SyntaxKind.NumericLiteralExpression:
                        var t = lit.Token.Text;
                        return (t.Contains('.') || t.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                            ? "float" : "int";
                }
            }
            return "int";
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
                _semanticModel = compilation.GetSemanticModel(tree, true);
            }
            catch
            {
                _semanticModel = null;
            }
        }

        /// <summary>
        /// Минимальный набор ссылок для разрешения базовых типов. Кэшируется.
        /// Под Unity/IL2CPP, где Assembly.Location может быть пустым, список окажется
        /// пустым — тогда вывод типов через модель не активируется (фолбэк на эвристику).
        /// </summary>
        private static MetadataReference[] GetMetadataReferences()
        {
            if (_cachedReferences != null)
                return _cachedReferences;

            var list = new List<MetadataReference>();
            void TryAddAsm(System.Reflection.Assembly asm)
            {
                try
                {
                    var loc = asm.Location;
                    if (!string.IsNullOrEmpty(loc))
                        list.Add(MetadataReference.CreateFromFile(loc));
                }
                catch { /* ignore */ }
            }

            TryAddAsm(typeof(object).Assembly);
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
        /// Выводит поддерживаемый тип (int/float/bool/string) из семантической модели.
        /// Возвращает null, если вывести невозможно (нет модели, ошибка типа, неподдерживаемый).
        /// </summary>
        private string TryInferSupportedType(SyntaxNode node)
        {
            if (node == null || _semanticModel == null)
                return null;

            try
            {
                // TypeSyntax наследует ExpressionSyntax — явный тип резолвим отдельно.
                ITypeSymbol t;
                if (node is TypeSyntax ts)
                    t = _semanticModel.GetTypeInfo(ts).Type ?? _semanticModel.GetSymbolInfo(ts).Symbol as ITypeSymbol;
                else if (node is ExpressionSyntax e)
                    t = _semanticModel.GetTypeInfo(e).Type;
                else
                    return null;

                if (t == null || t.TypeKind == TypeKind.Error)
                    return null;

                switch (t.SpecialType)
                {
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_Decimal:
                        return "float";
                    case SpecialType.System_Boolean:
                        return "bool";
                    case SpecialType.System_String:
                    case SpecialType.System_Char:
                        return "string";
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                        return "int";
                    default:
                        return null;
                }
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

        /// <summary>Коды диагностик, намеренно игнорируемые ради passthrough незнакомых API.</summary>
        private static bool IsPassthroughDiagnostic(string id) =>
            id == "CS0103"   // имя не существует в текущем контексте
            || id == "CS0117"  // тип не содержит определения члена
            || id == "CS0234"  // нет члена в пространстве имён
            || id == "CS0246"  // тип/namespace не найден
            || id == "CS1061"; // нет определения/метода расширения

        private string FormatUserLocation(SyntaxTree tree, TextSpan span)
        {
            var pos = tree.GetLineSpan(span);
            var line1 = pos.StartLinePosition.Line + 1;
            var col1 = pos.StartLinePosition.Character + 1;
            var userLine = line1 - WrapperNewlinesBeforeUser + _userCodeLineOffset;
            if (userLine < 1)
                return $"{line1}:{col1} (служебная обёртка)";
            return $"{userLine}:{col1}";
        }

        private void VisitMethodBody(BlockSyntax body)
        {
            // ── Pre-scan: собираем локальные функции и регистрируем их сигнатуры ──
            var localFuncSyntaxList = body.Statements
                .OfType<LocalFunctionStatementSyntax>()
                .ToList();

            var localFuncInfoList = localFuncSyntaxList
                .Select(ExtractLocalFunctionInfo)
                .Where(m => m != null)
                .ToList();

            if (localFuncInfoList.Count > 0)
            {
                var merged = new List<MethodInfo>(_knownMethods);
                merged.AddRange(localFuncInfoList);
                _knownMethods = merged;
            }

            // ── Main parse pass ──────────────────────────────────────────────────
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

            // ── Post-scan: парсим тела локальных функций и добавляем в DiscoveredMethods ──
            // Флаг предотвращает рекурсию (вложенные локальные функции пропускаются).
            if (!_isParsingFunctionBody && localFuncInfoList.Count > 0)
            {
                _isParsingFunctionBody = true;
                try
                {
                    for (int i = 0; i < localFuncSyntaxList.Count; i++)
                    {
                        var lf = localFuncSyntaxList[i];
                        var mi = localFuncInfoList[i];
                        if (mi == null) continue;

                        if (lf.Body != null)
                            mi.BodyGraph = ParseMethodBodyGraph(lf.Body, mi);

                        _discoveredMethods.Add(mi);
                    }
                }
                finally
                {
                    _isParsingFunctionBody = false;
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
                case ReturnStatementSyntax returnStmt:
                    return VisitReturnStatement(returnStmt, prevNode, prevPort);
                case ForStatementSyntax forStmt:
                    return VisitForStatement(forStmt, prevNode, prevPort);
                case WhileStatementSyntax whileStmt:
                    return VisitWhileStatement(whileStmt, prevNode, prevPort);
                case LocalFunctionStatementSyntax:
                    // Объявления локальных функций внутри кода игнорируются —
                    // методы определяются через панель Methods.
                    return null;

                default:
                    ReportUnsupported(stmt);
                    return null;
            }
        }

        private FlowHost VisitReturnStatement(ReturnStatementSyntax stmt, string prevNode, string prevPort)
        {
            var retId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id           = retId,
                Type         = NodeType.ReturnValue,
                Value        = "",
                ValueType    = "",
                VariableName = ""
            });

            if (prevNode != null)
                AddEdge(prevNode, prevPort, retId, PortIds.ExecIn);

            if (stmt.Expression != null)
            {
                var exprId = VisitExpression(stmt.Expression, false, null, out var unsupported);
                if (!unsupported && exprId != null)
                    AddEdge(exprId, GetDataOutPortForNodeId(exprId), retId, "value");
            }

            // ReturnNode не имеет exec-out: последующие AddEdge для execOut будут
            // отброшены в SupportsExecOut (ReturnValue не входит в список).
            return new FlowHost { NodeId = retId };
        }

        /// <summary>
        /// Парсит тело локальной функции в отдельный <see cref="GraphData"/>.
        /// Полностью сохраняет и восстанавливает состояние парсера.
        /// Параметры функции инжектируются как <see cref="NodeType.MethodParam"/> ноды
        /// со стабильными ID <c>"_paramref_{name}"</c>.
        /// </summary>
        private GraphData ParseMethodBodyGraph(BlockSyntax body, MethodInfo methodInfo,
            List<(string name, string type)> classFields = null)
        {
            if (body == null) return new GraphData();

            // ── Сохраняем состояние парсера ───────────────────────────────────────
            var savedGraph           = _graph;
            var savedRootGraph       = _rootGraph;
            var savedSymbolToNodeId  = new Dictionary<string, string>(_symbolToNodeId);
            var savedVariableTypes   = new Dictionary<string, string>(_variableTypes);
            var savedInSubGraph      = _inSubGraph;
            var savedSubGraphVarRefs = _subGraphVarRefs;
            var savedKnownMethods    = _knownMethods;
            var savedGraphStackArr   = _graphStack.ToArray();   // top-first
            var savedVarRefStackArr  = _varRefStack.ToArray();  // top-first
            // _nodeCounter НЕ сбрасываем — глобальная уникальность ID

            try
            {
                // ── Переключаемся в контекст тела метода ──────────────────────────
                var bodyGraph = new GraphData();
                _graph       = bodyGraph;
                _rootGraph   = bodyGraph;
                _symbolToNodeId.Clear();
                _variableTypes.Clear();
                _inSubGraph      = false;
                _subGraphVarRefs = null;
                _graphStack.Clear();
                _varRefStack.Clear();

                // ── Инжектируем ноды-параметры ────────────────────────────────────
                for (int i = 0; i < (methodInfo.ParamNames?.Count ?? 0); i++)
                {
                    var pname = methodInfo.ParamNames[i];
                    var ptype = methodInfo.ParamTypes != null && i < methodInfo.ParamTypes.Count
                        ? methodInfo.ParamTypes[i] : "int";

                    var paramId = "_paramref_" + pname;
                    bodyGraph.Nodes.Add(new NodeData
                    {
                        Id           = paramId,
                        Type         = NodeType.MethodParam,
                        Value        = ptype,
                        ValueType    = ptype,
                        VariableName = pname
                    });

                    _symbolToNodeId[pname] = paramId;
                    _variableTypes[pname]  = ptype;
                }

                // ── Инжектируем поля класса как FieldRef-ноды ─────────────────────
                // Это позволяет парсеру распознавать обращения к полям (value = ..., return value)
                // без ошибки «Неизвестный идентификатор».
                if (classFields != null)
                {
                    foreach (var (fieldName, fieldType) in classFields)
                    {
                        var fieldRefId = "_fieldref_" + fieldName;
                        bodyGraph.Nodes.Add(new NodeData
                        {
                            Id           = fieldRefId,
                            Type         = NodeType.FieldRef,
                            VariableName = fieldName,
                            ValueType    = fieldType
                        });
                        _symbolToNodeId[fieldName] = fieldRefId;
                        _variableTypes[fieldName]  = fieldType;
                    }
                }

                // ── Парсим тело ───────────────────────────────────────────────────
                VisitMethodBody(body);

                return bodyGraph;
            }
            finally
            {
                // ── Восстанавливаем состояние ─────────────────────────────────────
                _graph      = savedGraph;
                _rootGraph  = savedRootGraph;
                _inSubGraph = savedInSubGraph;
                _subGraphVarRefs = savedSubGraphVarRefs;
                _knownMethods    = savedKnownMethods;

                _symbolToNodeId.Clear();
                foreach (var kvp in savedSymbolToNodeId) _symbolToNodeId[kvp.Key] = kvp.Value;

                _variableTypes.Clear();
                foreach (var kvp in savedVariableTypes) _variableTypes[kvp.Key] = kvp.Value;

                // Восстанавливаем стеки (ToArray() возвращает top-first, поэтому Reverse перед Push)
                _graphStack.Clear();
                foreach (var g in savedGraphStackArr.Reverse()) _graphStack.Push(g);
                _varRefStack.Clear();
                foreach (var v in savedVarRefStackArr.Reverse()) _varRefStack.Push(v);
            }
        }

        private void ReportUnsupported(SyntaxNode node)
        {
            _errors.Add(
                $"Неподдерживаемая конструкция ({FormatUserLocation(node.SyntaxTree, node.Span)}): {node.Kind()}. Поддерживаются: объявления, присваивания, +=/-=, ++/--, if/else, for/while, вызовы Parse/ToString/Mathf, Console.WriteLine.");
        }

        /// <summary>
        /// Извлекает сигнатуру локальной функции (имя, тип возврата, параметры)
        /// и возвращает временный <see cref="MethodInfo"/> для use в текущем разборе.
        /// ID имеет префикс "__localfn__" и не совпадёт ни с каким зарегистрированным методом.
        /// </summary>
        private static MethodInfo ExtractLocalFunctionInfo(LocalFunctionStatementSyntax lf)
        {
            if (lf == null) return null;

            var name = lf.Identifier.Text;
            var returnTypeStr = lf.ReturnType.ToString().Trim();
            var returnType = returnTypeStr == "void" ? "void" : (MapValueType(returnTypeStr) ?? "int");

            var paramNames = new System.Collections.Generic.List<string>();
            var paramTypes = new System.Collections.Generic.List<string>();
            foreach (var p in lf.ParameterList.Parameters)
            {
                paramNames.Add(p.Identifier.Text);
                var typeStr = p.Type?.ToString().Trim() ?? "int";
                paramTypes.Add(MapValueType(typeStr) ?? "int");
            }

            return new MethodInfo
            {
                Id         = "__localfn__" + name,
                Name       = name,
                ReturnType = returnType,
                ParamNames = paramNames,
                ParamTypes = paramTypes,
                BodyGraph  = null
            };
        }

        private string CreateDefaultLiteralNode(string typeStr, string variableName)
        {
            if (typeStr == "Vector3")
                return CreateVector3Node(new[] { "0", "0", "0" }, variableName);

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

        /// <summary>
        /// Создаёt ноду <see cref="NodeType.UnityVector3"/> (конструктор Vector3) с тремя
        /// дочерними LiteralFloat-нодами, подключёнными к входным портам X, Y, Z —
        /// аналогично представлению Vector3CreateNode в редакторе.
        /// </summary>
        private string CreateVector3Node(string[] components, string variableName)
        {
            var vecId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id = vecId,
                Type = NodeType.UnityVector3,
                ValueType = "Vector3",
                VariableName = variableName ?? ""
            });

            var ports = new[] { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                var compId = NewId();
                _graph.Nodes.Add(new NodeData
                {
                    Id = compId,
                    Type = NodeType.LiteralFloat,
                    Value = i < components.Length ? components[i] : "0",
                    ValueType = "float",
                    VariableName = ""
                });
                AddEdge(compId, GetDataOutPortForNodeId(compId), vecId, ports[i]);
            }

            return vecId;
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
                string vType;
                if (typeStr == "var")
                {
                    // Сначала пробуем семантику (точно для любых выражений), затем —
                    // литеральную эвристику как фолбэк (когда модель недоступна).
                    vType = TryInferSupportedType(v.Initializer?.Value)
                            ?? InferVarType(v.Initializer?.Value);
                }
                else
                {
                    // Строковое сопоставление, затем семантика (decimal/char и т.п.) до ошибки.
                    vType = MapValueType(typeStr) ?? TryInferSupportedType(local.Declaration.Type);
                    if (vType == null)
                    {
                        _errors.Add(
                            $"Тип «{typeStr}» не поддерживается ({FormatUserLocation(local.SyntaxTree, v.Identifier.Span)}). Поддерживаются: int, float, bool, string.");
                        continue;
                    }
                }
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
                if (rootNode != null && (IsLiteralNodeType(rootNode.Type) || rootNode.Type == NodeType.UnityVector3
                    || rootNode.Type == NodeType.UnityMethodCall || rootNode.Type == NodeType.UnityFieldAccess))
                {
                    // Узел уже представляет вычисленное значение (вызов Unity-метода/доступ
                    // к полю) — не оборачиваем его в литерал-пустышку, а просто помечаем
                    // как переменную напрямую, иначе генератор кода и редакторские ноды
                    // получат «обёртку» с дефолтным значением (например, Vector3(0,0,0))
                    // вместо реального выражения.
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

            // Самостоятельный вызов пользовательского метода: MyMethod(x, y); или obj.MyMethod(x, y);
            if (expr is InvocationExpressionSyntax invUser && _knownMethods.Count > 0)
            {
                string candidateName = invUser.Expression switch
                {
                    IdentifierNameSyntax idExpr      => idExpr.Identifier.Text,
                    MemberAccessExpressionSyntax mex => mex.Name.Identifier.Text,
                    _                                => null
                };
                if (candidateName != null)
                {
                    var userMethod = _knownMethods.FirstOrDefault(m =>
                        string.Equals(m.Name, candidateName, System.StringComparison.Ordinal));
                    if (userMethod != null)
                    {
                        var callId = CreateMethodCallNode(userMethod, invUser, false, null, out _);
                        if (callId != null)
                        {
                            // Подключаем к exec-цепочке (MethodCallNode наследует BaseExecutionNode)
                            if (prevNode != null)
                                AddEdge(prevNode, prevPort, callId, PortIds.ExecIn);
                            return new FlowHost { NodeId = callId, ExecOutPort = PortIds.ExecOut };
                        }
                        return null;
                    }
                }
            }

            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax)
            {
                if (assign.Kind() == SyntaxKind.SimpleAssignmentExpression)
                {
                    var idLeft = (IdentifierNameSyntax)assign.Left;
                    var name   = idLeft.Identifier.Text;
                    var rootId = VisitExpression(assign.Right, false, null, out var unsupported);
                    if (unsupported || rootId == null)
                        return null;

                    // ── Присваивание полю класса (FieldSet-нода) ──────────────────
                    // Каждое присваивание создаёт НОВЫЙ FieldSet-узел, а не переиспользует
                    // FieldRef. _symbolToNodeId[name] не меняем: читает поле по-прежнему FieldRef.
                    //
                    // Поле распознаём по стабильному префиксу id ("_fieldref_"), а НЕ по поиску
                    // ноды в текущем графе: внутри подпространства (тело if/for/while) FieldRef-нода
                    // лежит в родительском графе и в _graph не находится. Раньше из-за этого
                    // присваивание полю внутри if/for/while превращалось в объявление локальной
                    // переменной («int score = 0;»), и повторный парсинг падал с «повторным объявлением».
                    if (_symbolToNodeId.TryGetValue(name, out var existingId))
                    {
                        var existingNode = _graph.Nodes.FirstOrDefault(n => n.Id == existingId);
                        bool isField = existingId.StartsWith("_fieldref_", StringComparison.Ordinal)
                                       || existingNode?.Type == NodeType.FieldRef;
                        if (isField)
                        {
                            var fieldType = existingNode?.ValueType
                                ?? (_variableTypes.TryGetValue(name, out var ft) ? ft : "int");
                            var fieldSetId = NewId();
                            _graph.Nodes.Add(new NodeData
                            {
                                Id           = fieldSetId,
                                Type         = NodeType.FieldSet,
                                VariableName = existingNode?.VariableName ?? name,
                                ValueType    = fieldType,
                                Value        = existingNode?.Value ?? ""
                            });
                            AddEdge(rootId, GetDataOutPortForNodeId(rootId), fieldSetId, "value");
                            if (prevNode != null)
                                AddEdge(prevNode, prevPort, fieldSetId, PortIds.ExecIn);
                            return new FlowHost { NodeId = fieldSetId, ExecOutPort = PortIds.ExecOut };
                        }
                    }

                    // ── Стандартное присваивание локальной переменной ─────────────
                    var rootNode = _graph.Nodes.FirstOrDefault(n => n.Id == rootId);
                    string litId;
                    if (rootNode != null && (IsLiteralNodeType(rootNode.Type) || rootNode.Type == NodeType.UnityVector3
                        || rootNode.Type == NodeType.UnityMethodCall || rootNode.Type == NodeType.UnityFieldAccess) && string.IsNullOrEmpty(rootNode.VariableName))
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

            // Unity API: вызов void-метода как самостоятельная инструкция, например transform.Translate(...)
            if (expr is InvocationExpressionSyntax invUnityStmt &&
                TryResolveUnityMethodCall(invUnityStmt, out var umClassS, out var umOwnerS, out var umMemberS) &&
                umMemberS.ReturnType == "void")
            {
                var callId = CreateUnityMethodCallNode(umClassS, umOwnerS, umMemberS, invUnityStmt, false, null, out var unsupportedCall);
                if (unsupportedCall || callId == null)
                    return null;

                var hostCall = new FlowHost { NodeId = callId };
                if (prevNode != null)
                    AddEdge(prevNode, prevPort, hostCall.NodeId, PortIds.ExecIn);
                return hostCall;
            }

            // Unity API: запись поля/свойства, например transform.position = new Vector3(...)
            if (expr is AssignmentExpressionSyntax assignField &&
                assignField.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                assignField.Left is MemberAccessExpressionSyntax leftMa &&
                TryResolveUnityFieldAccess(leftMa, out var fsClass, out var fsOwner, out var fsMember))
            {
                var rhsId = VisitExpression(assignField.Right, false, null, out var unsupportedRhs);
                if (unsupportedRhs || rhsId == null)
                    return null;

                var setId = NewId();
                _graph.Nodes.Add(new NodeData
                {
                    Id = setId,
                    Type = NodeType.UnityFieldSet,
                    Value = fsClass,
                    MemberName = fsMember.Name,
                    ValueType = fsMember.ReturnType,
                    OwnerExpression = fsOwner,
                    VariableName = ""
                });
                AddEdge(rhsId, GetDataOutPortForNodeId(rhsId), setId, "value");

                var hostSet = new FlowHost { NodeId = setId };
                if (prevNode != null)
                    AddEdge(prevNode, prevPort, hostSet.NodeId, PortIds.ExecIn);
                return hostSet;
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

        /// <summary>
        /// Является ли <paramref name="name"/> полем класса. Определяем по стабильному
        /// префиксу id ("_fieldref_") — надёжно и внутри подпространств, где сама FieldRef-нода
        /// лежит в родительском графе.
        /// </summary>
        private bool IsFieldSymbol(string name) =>
            _symbolToNodeId.TryGetValue(name, out var id)
            && id.StartsWith("_fieldref_", StringComparison.Ordinal);

        /// <summary>Создаёт FieldSet-узел, пишущий в поле значение с выхода <paramref name="valueNodeId"/>.</summary>
        private FlowHost EmitFieldSet(string fieldName, string valueNodeId, string prevNode, string prevPort)
        {
            var vType = _variableTypes.TryGetValue(fieldName, out var t) ? t : "int";
            var fieldSetId = NewId();
            _graph.Nodes.Add(new NodeData
            {
                Id           = fieldSetId,
                Type         = NodeType.FieldSet,
                VariableName = fieldName,
                ValueType    = vType,
                Value        = ""
            });
            AddEdge(valueNodeId, GetDataOutPortForNodeId(valueNodeId), fieldSetId, "value");
            if (prevNode != null)
                AddEdge(prevNode, prevPort, fieldSetId, PortIds.ExecIn);
            // _symbolToNodeId[fieldName] не меняем — чтение поля остаётся через FieldRef.
            return new FlowHost { NodeId = fieldSetId, ExecOutPort = PortIds.ExecOut };
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

            // Поле класса (score += x) → FieldSet, а не объявление локальной переменной.
            if (IsFieldSymbol(name))
                return EmitFieldSet(name, opId, prevNode, prevPort);

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

            // Поле класса (score++ / score--) → FieldSet, а не объявление локальной переменной.
            if (IsFieldSymbol(name))
                return EmitFieldSet(name, opId, prevNode, prevPort);

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

            // Переменные, объявленные в init и теле цикла, видны внутри цикла, но не снаружи.
            var forScope = SnapshotScope();

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

            RestoreScope(forScope);

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

            // Составное присваивание в инкременте for (i += step, f -= 0.1f и т.п.) —
            // переиспользуем VisitCompoundAssignment без exec-обвязки (prevNode == null).
            if (expr is AssignmentExpressionSyntax compound &&
                compound.Left is IdentifierNameSyntax &&
                compound.Kind() is SyntaxKind.AddAssignmentExpression
                    or SyntaxKind.SubtractAssignmentExpression
                    or SyntaxKind.MultiplyAssignmentExpression
                    or SyntaxKind.DivideAssignmentExpression
                    or SyntaxKind.ModuloAssignmentExpression)
            {
                var host = VisitCompoundAssignment(compound, null, null);
                if (host == null)
                {
                    unsupported = true;
                    return null;
                }
                return host.NodeId;
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

            // Переменные тела цикла не видны снаружи.
            var whileScope = SnapshotScope();
            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            var bodyStmts = ExpandStatement(whileStmt.Statement);
            BuildStatementsInSubGraph(bodyStmts);
            PopSubGraph();
            whileNodeData.BodySubGraph = bodyGraph;
            RestoreScope(whileScope);

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

            // Переменные ветки then не видны вне неё (ни снаружи, ни в ветке else).
            var thenScope = SnapshotScope();
            var bodyGraph = new GraphData();
            PushSubGraph(bodyGraph);
            var thenStmts = ExpandStatement(stmt.Statement);
            BuildStatementsInSubGraph(thenStmts);
            PopSubGraph();
            ifNodeData.BodySubGraph = bodyGraph;
            RestoreScope(thenScope);

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

                    var elseScope = SnapshotScope();
                    var elseBodyGraph = new GraphData();
                    PushSubGraph(elseBodyGraph);
                    var elseStmts = ExpandStatement(stmt.Else.Statement);
                    BuildStatementsInSubGraph(elseStmts);
                    PopSubGraph();
                    elseNodeData.BodySubGraph = elseBodyGraph;
                    RestoreScope(elseScope);

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

        /// <summary>
        /// Снимок имён переменных, объявленных к данному моменту. Используется для имитации
        /// блочной области видимости: см. <see cref="RestoreScope"/>.
        /// </summary>
        private (HashSet<string> Symbols, HashSet<string> Types) SnapshotScope() =>
            (new HashSet<string>(_symbolToNodeId.Keys), new HashSet<string>(_variableTypes.Keys));

        /// <summary>
        /// Удаляет переменные, объявленные ПОСЛЕ снимка (т.е. внутри блока), возвращая область
        /// видимости к состоянию до входа в блок. Переприсваивания внешних переменных не трогаются
        /// (их имена уже были в снимке), поэтому изменения внешних переменных сохраняются.
        /// </summary>
        private void RestoreScope((HashSet<string> Symbols, HashSet<string> Types) snapshot)
        {
            var symToRemove = _symbolToNodeId.Keys.Where(k => !snapshot.Symbols.Contains(k)).ToList();
            foreach (var k in symToRemove)
                _symbolToNodeId.Remove(k);

            var typeToRemove = _variableTypes.Keys.Where(k => !snapshot.Types.Contains(k)).ToList();
            foreach (var k in typeToRemove)
                _variableTypes.Remove(k);
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

        private const int MaxExpressionDepth = 256;

        private string VisitExpression(ExpressionSyntax expr, bool isRoot, string assignVariableToRoot, out bool unsupported)
        {
            unsupported = false;

            // Защита от StackOverflow на патологически глубоко вложенных выражениях.
            if (++_expressionDepth > MaxExpressionDepth)
            {
                _expressionDepth--;
                unsupported = true;
                _errors.Add(
                    $"Слишком глубоко вложенное выражение ({FormatUserLocation(expr.SyntaxTree, expr.Span)}). Упростите его.");
                return null;
            }

            try
            {
                return VisitExpressionInner(expr, isRoot, assignVariableToRoot, out unsupported);
            }
            finally
            {
                _expressionDepth--;
            }
        }

        private string VisitExpressionInner(ExpressionSyntax expr, bool isRoot, string assignVariableToRoot, out bool unsupported)
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

                case InvocationExpressionSyntax invUnity when TryResolveUnityMethodCall(invUnity, out var umClass, out var umOwner, out var umMember):
                    return CreateUnityMethodCallNode(umClass, umOwner, umMember, invUnity, isRoot, assignVariableToRoot, out unsupported);

                case InvocationExpressionSyntax inv:
                    return VisitInvocationExpression(inv, isRoot, assignVariableToRoot, out unsupported);

                case MemberAccessExpressionSyntax mathMem when
                    IsMathfStaticReceiver(mathMem.Expression) || IsSystemMathStaticReceiver(mathMem.Expression):
                    return CreatePassthroughMathLiteral(mathMem.ToString(), isRoot, assignVariableToRoot);

                case MemberAccessExpressionSyntax faMem when TryResolveUnityFieldAccess(faMem, out var faClass, out var faOwner, out var faMember):
                    return CreateUnityFieldAccessNode(faClass, faOwner, faMember, isRoot, assignVariableToRoot);

                case MemberAccessExpressionSyntax memberAccess:
                {
                    // Static field / property read from another class (e.g. Calculator.accumulator).
                    // Emit as passthrough — generator uses ExpressionOverride as-is.
                    var inferredType = TryInferSupportedType(memberAccess) ?? "int";
                    var nodeId = NewId();
                    var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
                    NodeType litType = inferredType switch
                    {
                        "float"  => NodeType.LiteralFloat,
                        "bool"   => NodeType.LiteralBool,
                        "string" => NodeType.LiteralString,
                        _        => NodeType.LiteralInt
                    };
                    _graph.Nodes.Add(new NodeData
                    {
                        Id                 = nodeId,
                        Type               = litType,
                        Value              = "",
                        ValueType          = inferredType,
                        VariableName       = vn,
                        ExpressionOverride = memberAccess.ToString().Trim()
                    });
                    return nodeId;
                }

                case ConditionalExpressionSyntax cond:
                    return CreateStringExpressionLiteralNode(cond.ToString().Trim(), isRoot ? assignVariableToRoot : null);

                case InterpolatedStringExpressionSyntax interpolated:
                    return CreateStringExpressionLiteralNode(interpolated.ToString().Trim(), isRoot ? assignVariableToRoot : null);

                case ObjectCreationExpressionSyntax objCreate when IsVector3Type(objCreate.Type):
                    return CreateVector3LiteralFromObjectCreation(objCreate, isRoot ? assignVariableToRoot : null, out unsupported);

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

        private string TryEvaluateExpression(string nodeId, int depth = 0)
        {
            // Защита от зацикливания, если граф (например, пришедший извне) содержит цикл в рёбрах.
            if (depth > MaxExpressionDepth)
                return null;

            var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return null;

            if (IsLiteralNodeType(node.Type) && !string.IsNullOrEmpty(node.Value))
                return node.Value;

            if (!IsMath(node.Type)) return null;

            var leftEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == "inputA");
            var rightEdge = _graph.Edges.FirstOrDefault(e => e.ToNodeId == nodeId && e.ToPort == "inputB");
            if (leftEdge == null || rightEdge == null) return null;

            var leftVal = TryEvaluateExpression(leftEdge.FromNodeId, depth + 1);
            var rightVal = TryEvaluateExpression(rightEdge.FromNodeId, depth + 1);
            if (leftVal == null || rightVal == null) return null;

            if (int.TryParse(leftVal, out int li) && int.TryParse(rightVal, out int ri))
            {
                // Деление/остаток на ноль НЕ сворачиваем: возвращаем null, чтобы сохранить
                // исходное выражение (ExpressionOverride) и не маскировать рантайм-ошибку нулём.
                switch (node.Type)
                {
                    case NodeType.MathAdd:      return (li + ri).ToString();
                    case NodeType.MathSubtract: return (li - ri).ToString();
                    case NodeType.MathMultiply: return (li * ri).ToString();
                    case NodeType.MathDivide:   return ri != 0 ? (li / ri).ToString() : null;
                    case NodeType.MathModulo:   return ri != 0 ? (li % ri).ToString() : null;
                    default:                    return null;
                }
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
        /// Известные имена переменных-получателей экземплярных членов Unity API
        /// (например, <c>transform</c>, <c>gameObject</c>) → имя класса в реестре.
        /// </summary>
        private static readonly Dictionary<string, string> KnownUnityReceiverTypes = new()
        {
            ["transform"] = "Transform",
            ["gameObject"] = "GameObject",
        };

        /// <summary>
        /// Пытается определить класс Unity API (<see cref="UnityLibraryRegistry"/>) и выражение
        /// получателя для члена (метода/поля), к которому обращается <paramref name="expr"/>.
        /// Статические члены (например, <c>Mathf.Clamp</c>, <c>Vector3.zero</c>) → ownerExpr = "".
        /// Члены экземпляра (например, <c>transform.position</c>) → className = "Transform", ownerExpr = "transform".
        /// </summary>
        private static bool TryResolveUnityReceiver(ExpressionSyntax expr, out string className, out string ownerExpr)
        {
            className = "";
            ownerExpr = "";

            switch (expr)
            {
                case IdentifierNameSyntax id:
                    var name = id.Identifier.Text;
                    if (UnityLibraryRegistry.GetClass(name) != null)
                    {
                        className = name;
                        ownerExpr = "";
                        return true;
                    }
                    if (KnownUnityReceiverTypes.TryGetValue(name, out var cls))
                    {
                        className = cls;
                        ownerExpr = name;
                        return true;
                    }
                    return false;

                case MemberAccessExpressionSyntax ma:
                    var memberName = ma.Name.Identifier.Text;
                    if (KnownUnityReceiverTypes.TryGetValue(memberName, out var cls2))
                    {
                        className = cls2;
                        ownerExpr = ma.ToString();
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>Распознаёт чтение поля/свойства встроенного Unity-класса (UnityFieldAccess).</summary>
        private static bool TryResolveUnityFieldAccess(MemberAccessExpressionSyntax ma, out string className, out string ownerExpr, out UnityMemberInfo member)
        {
            className = "";
            ownerExpr = "";
            member = null;

            if (!TryResolveUnityReceiver(ma.Expression, out var cls, out var owner))
                return false;

            var found = UnityLibraryRegistry.FindField(cls, ma.Name.Identifier.Text);
            if (found == null)
                return false;

            className = cls;
            ownerExpr = owner;
            member = found;
            return true;
        }

        /// <summary>Распознаёт вызов метода встроенного Unity-класса (UnityMethodCall).</summary>
        private static bool TryResolveUnityMethodCall(InvocationExpressionSyntax inv, out string className, out string ownerExpr, out UnityMemberInfo member)
        {
            className = "";
            ownerExpr = "";
            member = null;

            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                return false;

            if (!TryResolveUnityReceiver(ma.Expression, out var cls, out var owner))
                return false;

            var found = UnityLibraryRegistry.FindMethod(cls, ma.Name.Identifier.Text);
            if (found == null)
                return false;

            className = cls;
            ownerExpr = owner;
            member = found;
            return true;
        }

        /// <summary>Создаёт ноду UnityFieldAccess (чтение поля/свойства Unity API).</summary>
        private string CreateUnityFieldAccessNode(string className, string ownerExpr, UnityMemberInfo member, bool isRoot, string assignVariableToRoot)
        {
            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.UnityFieldAccess,
                Value = className,
                MemberName = member.Name,
                ValueType = member.ReturnType,
                OwnerExpression = ownerExpr,
                VariableName = vn
            });
            return id;
        }

        /// <summary>Создаёт ноду UnityMethodCall (вызов метода Unity API) и подключает аргументы к param0..param3.</summary>
        private string CreateUnityMethodCallNode(string className, string ownerExpr, UnityMemberInfo member, InvocationExpressionSyntax inv, bool isRoot, string assignVariableToRoot, out bool unsupported)
        {
            unsupported = false;
            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.UnityMethodCall,
                Value = className,
                MemberName = member.Name,
                ValueType = member.ReturnType,
                OwnerExpression = ownerExpr,
                VariableName = vn
            });

            var args = inv.ArgumentList.Arguments;
            for (int i = 0; i < args.Count && i < 4; i++)
            {
                var argId = VisitExpression(args[i].Expression, false, null, out unsupported);
                if (unsupported || argId == null)
                    return null;
                AddEdge(argId, GetDataOutPortForNodeId(argId), id, $"param{i}");
            }

            return id;
        }

        /// <summary>
        /// Создаёт ноду пользовательского вызова метода (<see cref="NodeType.MethodCall"/>)
        /// и подключает аргументы к портам param0…paramN.
        /// </summary>
        private string CreateMethodCallNode(
            MethodInfo def,
            InvocationExpressionSyntax inv,
            bool isRoot,
            string assignVariableToRoot,
            out bool unsupported)
        {
            unsupported = false;
            var argIds = new List<string>();

            foreach (var arg in inv.ArgumentList.Arguments)
            {
                var argId = VisitExpression(arg.Expression, false, null, out unsupported);
                if (unsupported || argId == null) return null;
                argIds.Add(argId);
            }

            var id = NewId();
            // VariableName хранит имя метода (для генератора), Value — MethodId (GUID)
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            _graph.Nodes.Add(new NodeData
            {
                Id           = id,
                Type         = NodeType.MethodCall,
                Value        = def.Id,         // MethodId
                VariableName = def.Name,       // MethodName — нужно генератору
                ValueType    = def.ReturnType
            });

            for (int i = 0; i < argIds.Count; i++)
                AddEdge(argIds[i], GetDataOutPortForNodeId(argIds[i]), id, $"param{i}");

            // Если вызов присваивается переменной, создаём literal-враппер
            if (isRoot && !string.IsNullOrEmpty(assignVariableToRoot))
            {
                var vType = _variableTypes.TryGetValue(assignVariableToRoot, out var t)
                    ? t : (def.ReturnType ?? "int");
                var litId = CreateDefaultLiteralNode(vType, assignVariableToRoot);
                AddEdge(id, "output", litId, "inputValue");
                var litNode = _graph.Nodes.FirstOrDefault(n => n.Id == litId);
                if (litNode != null)
                    litNode.ExpressionOverride = inv.ToString().Trim();
                _symbolToNodeId[assignVariableToRoot] = litId;
                return litId;
            }

            return id;
        }

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

            // Прямой вызов без квалификатора: MyMethod(x, y)
            if (inv.Expression is IdentifierNameSyntax directId)
            {
                // Сначала ищем в известных методах
                if (_knownMethods.Count > 0)
                {
                    var userMethod = _knownMethods.FirstOrDefault(m =>
                        string.Equals(m.Name, directId.Identifier.Text, System.StringComparison.Ordinal));
                    if (userMethod != null)
                        return CreateMethodCallNode(userMethod, inv, isRoot, assignVariableToRoot, out unsupported);
                }

                // Метод не найден в реестре — emit как passthrough-выражение
                // (например, вызов локальной функции из кода; генератор подставит ExpressionOverride)
                return CreatePassthroughExpressionNode(inv.ToString(), isRoot, assignVariableToRoot);
            }

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

            // Проверяем пользовательские методы
            if (_knownMethods.Count > 0)
            {
                // Вызовы без квалификатора (ma.Expression — просто имя) или через any.Method
                // Нас интересует только простое имя: SomeName.MethodName или просто MethodName
                var candidateName = methodName;
                var userMethod = _knownMethods.FirstOrDefault(m =>
                    string.Equals(m.Name, candidateName, System.StringComparison.Ordinal));
                if (userMethod != null)
                    return CreateMethodCallNode(userMethod, inv, isRoot, assignVariableToRoot, out unsupported);
            }

            unsupported = true;
            _errors.Add(
                $"Неподдерживаемый вызов метода ({FormatUserLocation(inv.SyntaxTree, inv.Span)}): {methodName}.");
            return null;
        }

        /// <summary>
        /// Создаёт passthrough-ноду для произвольного вызова/выражения, которое парсер не умеет
        /// разобрать на граф (например, вызов локальной функции или неизвестного метода).
        /// Генератор подставит <see cref="NodeData.ExpressionOverride"/> как есть.
        /// </summary>
        private string CreatePassthroughExpressionNode(string expressionText, bool isRoot, string assignVariableToRoot)
        {
            var id = NewId();
            var vn = isRoot && !string.IsNullOrEmpty(assignVariableToRoot) ? assignVariableToRoot : "";
            // Тип неизвестен → int как наиболее нейтральный; генератор использует ExpressionOverride
            _graph.Nodes.Add(new NodeData
            {
                Id = id,
                Type = NodeType.LiteralInt,
                Value = "",
                ValueType = "int",
                VariableName = vn,
                ExpressionOverride = expressionText.Trim()
            });
            return id;
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
            // FirstOrDefault, а не First: операнд может находиться в другом графе (внешняя
            // область относительно текущего подграфа) — тогда тип по умолчанию считаем int.
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

        /// <summary>Проверяет, что тип в <c>new ...(...)</c> — это Vector3 (с/без namespace UnityEngine).</summary>
        private static bool IsVector3Type(TypeSyntax type)
        {
            var name = type.ToString().Trim();
            return name == "Vector3" || name == "UnityEngine.Vector3";
        }

        /// <summary>
        /// Пытается прочитать аргумент конструктора Vector3 как числовой литерал (с возможным
        /// унарным минусом), например <c>1f</c>, <c>-2.5f</c>, <c>0</c>.
        /// </summary>
        private static bool TryGetNumericLiteralText(ExpressionSyntax expr, out string text)
        {
            while (expr is ParenthesizedExpressionSyntax paren)
                expr = paren.Expression;

            var sign = "";
            if (expr is PrefixUnaryExpressionSyntax pre && pre.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                sign = "-";
                expr = pre.Operand;
            }

            if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                text = sign + lit.Token.Text.TrimEnd('f', 'F', 'd', 'D', 'm', 'M');
                return true;
            }

            text = "";
            return false;
        }

        /// <summary>
        /// Создаёт литерал-ноду UnityVector3 из выражения <c>new Vector3(...)</c>.
        /// Поддерживаются конструкторы с 0 (zero), 2 (x, y; z=0) и 3 (x, y, z) аргументами,
        /// каждый аргумент — числовой литерал (опционально со знаком минус).
        /// </summary>
        private string CreateVector3LiteralFromObjectCreation(ObjectCreationExpressionSyntax objCreate, string variableName, out bool unsupported)
        {
            unsupported = false;
            var args = objCreate.ArgumentList?.Arguments ?? default;

            if (args.Count != 0 && args.Count != 2 && args.Count != 3)
            {
                unsupported = true;
                _errors.Add(
                    $"Неподдерживаемый конструктор Vector3 ({FormatUserLocation(objCreate.SyntaxTree, objCreate.Span)}): ожидается 0, 2 или 3 аргумента.");
                return null;
            }

            var components = new[] { "0", "0", "0" };
            for (int i = 0; i < args.Count; i++)
            {
                var argExpr = args[i].Expression;
                if (!TryGetNumericLiteralText(argExpr, out var text))
                {
                    unsupported = true;
                    _errors.Add(
                        $"Неподдерживаемый аргумент конструктора Vector3 ({FormatUserLocation(argExpr.SyntaxTree, argExpr.Span)}): ожидается числовой литерал.");
                    return null;
                }
                components[i] = text;
            }

            return CreateVector3Node(components, variableName ?? "");
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