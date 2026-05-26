using VisualScripting.Core.Generators;
using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

/// <summary>
/// Тесты для новых фич системы классов:
/// — новые NodeType (ClassNode, MethodOwner, ReturnValue)
/// — graceful handling ClassNode / MethodOwner / MethodCall в генераторе
/// — exec-цепочка сквозь «глухие» ноды (ClassNode, MethodCall без VariableName)
/// — TryGetIncrementDecrementClause (i++ / i-- паттерн в IncrementSubGraph)
/// — for-цикл с i++ через SubGraph
/// </summary>
public class ClassSystemTests
{
    private readonly SimpleCodeGenerator _gen = new();
    private readonly RoslynCodeParser    _parser = new();

    // ──────────────────────────────────────────────────────────────────────────
    // 1. NodeType enum — новые значения присутствуют
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NodeType_ClassNode_IsDefined()
    {
        // Enum.IsDefined выбрасывает, если значения нет
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.ClassNode));
    }

    [Fact]
    public void NodeType_MethodOwner_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.MethodOwner));
    }

    [Fact]
    public void NodeType_ReturnValue_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.ReturnValue));
    }

    [Fact]
    public void NodeType_NewValues_HaveUniqueNumericCodes()
    {
        // Убеждаемся, что новые значения не перекрываются с существующими
        var all = Enum.GetValues<NodeType>().ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void NodeData_CanBeCreatedWithClassNodeType()
    {
        var nd = new NodeData { Id = "c1", Type = NodeType.ClassNode, Value = "class-id-1", VariableName = "MyClass" };
        Assert.Equal(NodeType.ClassNode, nd.Type);
        Assert.Equal("class-id-1", nd.Value);
        Assert.Equal("MyClass", nd.VariableName);
    }

    [Fact]
    public void NodeData_CanBeCreatedWithMethodOwnerType()
    {
        var nd = new NodeData { Id = "mo1", Type = NodeType.MethodOwner, Value = "method-id-1", VariableName = "DoSomething" };
        Assert.Equal(NodeType.MethodOwner, nd.Type);
    }

    [Fact]
    public void NodeData_CanBeCreatedWithReturnValueType()
    {
        var nd = new NodeData { Id = "rv1", Type = NodeType.ReturnValue };
        Assert.Equal(NodeType.ReturnValue, nd.Type);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Генератор — graceful handling новых типов (нет краша, нет вывода)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Generator_ClassNodeAlone_DoesNotCrash_AndProducesNoCode()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData { Id = "c1", Type = NodeType.ClassNode, VariableName = "MyClass" });

        // Не должно выбросить исключение
        var output = _gen.Generate(graph);

        // ClassNode — не statement-entry, значит граф пустой → fallback-комментарий
        Assert.NotNull(output);
        Assert.DoesNotContain("MyClass", output);
    }

    [Fact]
    public void Generator_MethodOwnerAlone_DoesNotCrash_AndProducesNoCode()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData { Id = "mo1", Type = NodeType.MethodOwner, VariableName = "DoWork" });

        var output = _gen.Generate(graph);

        Assert.NotNull(output);
        Assert.DoesNotContain("DoWork", output);
    }

    [Fact]
    public void Generator_ReturnValueAlone_DoesNotCrash()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData { Id = "rv1", Type = NodeType.ReturnValue });

        var output = _gen.Generate(graph);
        Assert.NotNull(output);
    }

    [Fact]
    public void Generator_MethodCallNodeAlone_DoesNotCrash_AndProducesNoCode()
    {
        // MethodCallNode: VariableName пустой → EmitValueStatement ничего не пишет
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData { Id = "mc1", Type = NodeType.MethodCall, Value = "some-method-id" });

        var output = _gen.Generate(graph);
        Assert.NotNull(output);
    }

    [Fact]
    public void Generator_MixedGraph_ClassNodeDoesNotCorruptOtherStatements()
    {
        // ClassNode рядом с обычными переменными — не должна мешать
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData
        {
            Id = "x", Type = NodeType.LiteralInt,
            Value = "42", ValueType = "int", VariableName = "x"
        });
        graph.Nodes.Add(new NodeData
        {
            Id = "c1", Type = NodeType.ClassNode,
            Value = "class-id", VariableName = "MyClass"
        });

        var output = _gen.Generate(graph);
        Assert.Contains("int x = 42;", output);
        Assert.DoesNotContain("MyClass", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Exec-цепочка сквозь «глухие» ноды
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ClassNode (нет VariableName → ничего не эмитируется) стоит перед int-нодой
    /// через execOut → execIn. Генератор должен найти ClassNode как execIn-вход int-ноды
    /// и начать цепочку с неё. Сам ClassNode silent-skip, int-нода эмитируется.
    ///
    /// Порядок в Nodes: [classNode, intNode].
    /// Цепочка: classNode.execOut → intNode.execIn.
    /// Поскольку classNode не является statement-entry, intNode тоже имеет входящий execIn
    /// и не будет корнем. Поэтому оба не попадут в roots — генератор использует fallback,
    /// который всё равно эмитирует LiteralInt.
    /// </summary>
    [Fact]
    public void Generator_ClassNode_FollowedByLiteral_ViaExec_LiteralIsEmitted()
    {
        var classNode = new NodeData
        {
            Id = "c1", Type = NodeType.ClassNode, Value = "cid"
            // нет VariableName → EmitValueStatement ничего не пишет
        };
        var intNode = new NodeData
        {
            Id = "x", Type = NodeType.LiteralInt,
            Value = "7", ValueType = "int", VariableName = "x"
        };

        var graph = new GraphData();
        graph.Nodes.Add(classNode);
        graph.Nodes.Add(intNode);
        // classNode → intNode через exec
        graph.Edges.Add(new EdgeData
        {
            FromNodeId = "c1", FromPort = "execOut",
            ToNodeId   = "x",  ToPort   = "execIn"
        });

        var output = _gen.Generate(graph);

        // Fallback или нормальная эмиссия — int x должен присутствовать
        Assert.Contains("int x = 7;", output);
        Assert.DoesNotContain("ClassNode", output);
    }

    /// <summary>
    /// MethodCall (VariableName = "") стоит между двумя LiteralInt в exec-цепочке.
    /// Генератор должен эмитировать оба int-узла в правильном порядке.
    /// </summary>
    [Fact]
    public void Generator_MethodCallInExecChain_Skipped_SurroundingNodesEmitted()
    {
        var nodeA = new NodeData { Id = "a", Type = NodeType.LiteralInt, Value = "1", ValueType = "int", VariableName = "a" };
        var nodeM = new NodeData { Id = "m", Type = NodeType.MethodCall, Value = "mid" }; // нет VariableName
        var nodeB = new NodeData { Id = "b", Type = NodeType.LiteralInt, Value = "2", ValueType = "int", VariableName = "b" };

        var graph = new GraphData();
        graph.Nodes.Add(nodeA);
        graph.Nodes.Add(nodeM);
        graph.Nodes.Add(nodeB);

        // a → m → b через exec
        graph.Edges.Add(new EdgeData { FromNodeId = "a", FromPort = "execOut", ToNodeId = "m", ToPort = "execIn" });
        graph.Edges.Add(new EdgeData { FromNodeId = "m", FromPort = "execOut", ToNodeId = "b", ToPort = "execIn" });

        var output = _gen.Generate(graph);

        // Оба int-узла должны присутствовать
        Assert.Contains("int a = 1;", output);
        Assert.Contains("int b = 2;", output);

        // a должна стоять раньше b
        var posA = output.IndexOf("int a =", StringComparison.Ordinal);
        var posB = output.IndexOf("int b =", StringComparison.Ordinal);
        Assert.True(posA < posB, $"a должна быть до b через exec-цепочку. Вывод:\n{output}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. TryGetIncrementDecrementClause — прямые юнит-тесты паттерна i++/i--
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Строит IncrementSubGraph, моделирующий i = i + 1, и убеждается,
    /// что генератор для-цикла выводит "i++" в заголовке.
    /// </summary>
    [Fact]
    public void ForLoop_IncrementSubGraph_iPlus1_EmitsIPlusPlus()
    {
        var forNode = BuildForWithIncrementSubGraph("i", NodeType.MathAdd, "1");
        var graph   = new GraphData();
        graph.Nodes.Add(forNode);

        var output = _gen.Generate(graph);
        Assert.Contains("i++", output);
        Assert.DoesNotContain("i = i + 1", output);
    }

    [Fact]
    public void ForLoop_IncrementSubGraph_iMinus1_EmitsIMinus()
    {
        var forNode = BuildForWithIncrementSubGraph("i", NodeType.MathSubtract, "1");
        var graph   = new GraphData();
        graph.Nodes.Add(forNode);

        var output = _gen.Generate(graph);
        Assert.Contains("i--", output);
        Assert.DoesNotContain("i = i - 1", output);
    }

    [Fact]
    public void ForLoop_IncrementSubGraph_iPlus2_DoesNotEmitIPlusPlus()
    {
        // Шаг не 1 → паттерн не совпадает, должен быть обычный i = i + 2
        var forNode = BuildForWithIncrementSubGraph("i", NodeType.MathAdd, "2");
        var graph   = new GraphData();
        graph.Nodes.Add(forNode);

        var output = _gen.Generate(graph);
        Assert.DoesNotContain("i++", output);
        Assert.Contains("i + 2", output);
    }

    [Fact]
    public void ForLoop_IncrementSubGraph_DifferentVar_DoesNotEmitIPlusPlus()
    {
        // inputA — переменная j, а assign-нода — i: паттерн не совпадает
        var incGraph = new GraphData();

        var assignNode = new NodeData { Id = "asgn", Type = NodeType.LiteralInt, ValueType = "int", VariableName = "i" };
        var mathNode   = new NodeData { Id = "math", Type = NodeType.MathAdd };
        var refNode    = new NodeData { Id = "ref",  Type = NodeType.LiteralInt, ValueType = "int", VariableName = "j" }; // другая переменная
        var litNode    = new NodeData { Id = "lit",  Type = NodeType.LiteralInt, Value = "1", ValueType = "int" };

        incGraph.Nodes.AddRange(new[] { assignNode, mathNode, refNode, litNode });
        incGraph.Edges.Add(new EdgeData { FromNodeId = "math", FromPort = "output", ToNodeId = "asgn", ToPort = "inputValue" });
        incGraph.Edges.Add(new EdgeData { FromNodeId = "ref",  FromPort = "output", ToNodeId = "math", ToPort = "inputA" });
        incGraph.Edges.Add(new EdgeData { FromNodeId = "lit",  FromPort = "output", ToNodeId = "math", ToPort = "inputB" });

        var forNode = new NodeData
        {
            Id = "for1", Type = NodeType.FlowFor,
            IncrementSubGraph = incGraph,
            ConditionSubGraph = MakeSimpleCondGraph(),
            InitSubGraph      = MakeSimpleInitGraph("i")
        };

        var graph = new GraphData();
        graph.Nodes.Add(forNode);

        var output = _gen.Generate(graph);
        Assert.DoesNotContain("i++", output);
        Assert.DoesNotContain("j++", output);
    }

    [Fact]
    public void ForLoop_IncrementSubGraph_Empty_FallsBackToSubGraphExpression()
    {
        // Пустой IncrementSubGraph → fallback на GenerateForClauseFromSubGraph
        var forNode = new NodeData
        {
            Id = "for1", Type = NodeType.FlowFor,
            IncrementSubGraph = new GraphData(),         // пустой
            ConditionSubGraph = MakeSimpleCondGraph(),
            InitSubGraph      = MakeSimpleInitGraph("i")
        };

        var graph = new GraphData();
        graph.Nodes.Add(forNode);

        // Не должно крашиться, i++ не должен появляться
        var output = _gen.Generate(graph);
        Assert.NotNull(output);
        Assert.DoesNotContain("i++", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. For-цикл roundtrip через парсер → генератор (i++ сохраняется)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForLoop_Roundtrip_iPlusPlus_PreservesIplusPlus()
    {
        var code   = "int s = 0;\nfor (int i = 0; i < 5; i++)\n{\n    s += i;\n}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var output = _gen.Generate(result.Graph);
        Assert.Contains("i++", output);
    }

    [Fact]
    public void ForLoop_Roundtrip_iMinusMinus_PreservesIMinus()
    {
        var code   = "int s = 0;\nfor (int i = 10; i > 0; i--)\n{\n    s += i;\n}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var output = _gen.Generate(result.Graph);
        Assert.Contains("i--", output);
    }

    [Fact]
    public void ForLoop_Roundtrip_StepByTwo_NoIPlusPlus()
    {
        var code   = "for (int i = 0; i < 10; i = i + 2)\n{\n}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var output = _gen.Generate(result.Graph);
        Assert.DoesNotContain("i++", output);
        Assert.Contains("i + 2", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. MethodCall: exec-порты работают как у других execution-нод
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MethodCallNode теперь наследует BaseExecutionNode, значит у неё есть execIn/execOut.
    /// Тест проверяет, что если в графе есть:
    ///   intA.execOut → methodCall.execIn → intB.execIn
    /// то порядок: intA, (methodCall пропущен), intB.
    /// </summary>
    [Fact]
    public void Generator_MethodCallWithExecPorts_ChainOrderRespected()
    {
        var nodeA = new NodeData { Id = "a", Type = NodeType.LiteralInt, Value = "10", ValueType = "int", VariableName = "a" };
        var nodeM = new NodeData { Id = "m", Type = NodeType.MethodCall, Value = "mid" };   // нет VariableName
        var nodeB = new NodeData { Id = "b", Type = NodeType.LiteralInt, Value = "20", ValueType = "int", VariableName = "b" };

        var graph = new GraphData();
        graph.Nodes.Add(nodeA);
        graph.Nodes.Add(nodeM);
        graph.Nodes.Add(nodeB);

        // Порядок: a → methodCall → b
        graph.Edges.Add(new EdgeData { FromNodeId = "a", FromPort = "execOut", ToNodeId = "m", ToPort = "execIn" });
        graph.Edges.Add(new EdgeData { FromNodeId = "m", FromPort = "execOut", ToNodeId = "b", ToPort = "execIn" });

        var output = _gen.Generate(graph);

        Assert.Contains("int a = 10;", output);
        Assert.Contains("int b = 20;", output);

        var posA = output.IndexOf("int a =", StringComparison.Ordinal);
        var posB = output.IndexOf("int b =", StringComparison.Ordinal);
        Assert.True(posA < posB, $"a должна быть до b через exec-цепочку с MethodCall посередине.\nВывод:\n{output}");
    }

    /// <summary>
    /// MethodCallNode с ненулевым VariableName (результат метода сохраняется в переменную).
    /// Генератор должен не крашиться, но не эмитировать вызов (нет специальной логики).
    /// </summary>
    [Fact]
    public void Generator_MethodCallWithVariableName_DoesNotCrash()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData
        {
            Id = "m", Type = NodeType.MethodCall,
            Value = "mid", VariableName = "result"  // есть VariableName, но тип не Literal/BinaryOp/Builtin
        });

        var output = _gen.Generate(graph);
        Assert.NotNull(output);
        // VariableName "result" не должна появляться (MethodCall не входит ни в IsLiteral / IsBinaryOp / IsBuiltin)
        Assert.DoesNotContain("result =", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Совместная работа: класс-система не ломает существующие тесты генератора
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Generator_ClassAndMethodNodes_CoexistWithOrdinaryNodes_NoCorruption()
    {
        // Три обычных int-ноды + ClassNode + MethodOwner рядом с ними
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData { Id = "x", Type = NodeType.LiteralInt, Value = "1",  ValueType = "int", VariableName = "x" });
        graph.Nodes.Add(new NodeData { Id = "y", Type = NodeType.LiteralInt, Value = "2",  ValueType = "int", VariableName = "y" });
        graph.Nodes.Add(new NodeData { Id = "z", Type = NodeType.LiteralInt, Value = "3",  ValueType = "int", VariableName = "z" });
        graph.Nodes.Add(new NodeData { Id = "c1", Type = NodeType.ClassNode,   Value = "cid" });
        graph.Nodes.Add(new NodeData { Id = "m1", Type = NodeType.MethodOwner, Value = "mid" });

        var output = _gen.Generate(graph);

        Assert.Contains("int x = 1;", output);
        Assert.Contains("int y = 2;", output);
        Assert.Contains("int z = 3;", output);
    }

    [Fact]
    public void Generator_MultipleClassAndMethodOwnerNodes_NoneAppearInOutput()
    {
        var graph = new GraphData();
        for (int i = 0; i < 5; i++)
            graph.Nodes.Add(new NodeData { Id = $"c{i}", Type = NodeType.ClassNode,   Value = $"cid{i}", VariableName = $"Class{i}" });
        for (int i = 0; i < 3; i++)
            graph.Nodes.Add(new NodeData { Id = $"m{i}", Type = NodeType.MethodOwner, Value = $"mid{i}", VariableName = $"Method{i}" });

        var output = _gen.Generate(graph);

        // Ни один "class name" не должен попасть в вывод
        for (int i = 0; i < 5; i++) Assert.DoesNotContain($"Class{i}", output);
        for (int i = 0; i < 3; i++) Assert.DoesNotContain($"Method{i}", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Вспомогательные фабрики
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Строит NodeData типа FlowFor с IncrementSubGraph вида «varName = varName op litValue».
    /// </summary>
    private static NodeData BuildForWithIncrementSubGraph(string varName, NodeType mathOp, string litValue)
    {
        var incGraph = new GraphData();

        // Нода-цель: LiteralInt с VariableName (принимает результат из math через inputValue)
        var assignNode = new NodeData
        {
            Id = "asgn", Type = NodeType.LiteralInt,
            ValueType = "int", VariableName = varName
        };

        // Math-нода (Add или Subtract)
        var mathNode = new NodeData { Id = "math", Type = mathOp };

        // Ссылка на ту же переменную (inputA)
        var refNode = new NodeData
        {
            Id = "ref", Type = NodeType.LiteralInt,
            ValueType = "int", VariableName = varName
        };

        // Литерал-константа (inputB)
        var litNode = new NodeData
        {
            Id = "lit", Type = NodeType.LiteralInt,
            Value = litValue, ValueType = "int"
        };

        incGraph.Nodes.AddRange(new[] { assignNode, mathNode, refNode, litNode });

        // math.output → assignNode.inputValue
        incGraph.Edges.Add(new EdgeData { FromNodeId = "math", FromPort = "output", ToNodeId = "asgn", ToPort = "inputValue" });
        // ref.output → math.inputA
        incGraph.Edges.Add(new EdgeData { FromNodeId = "ref",  FromPort = "output", ToNodeId = "math", ToPort = "inputA" });
        // lit.output → math.inputB
        incGraph.Edges.Add(new EdgeData { FromNodeId = "lit",  FromPort = "output", ToNodeId = "math", ToPort = "inputB" });

        return new NodeData
        {
            Id = "for1",
            Type = NodeType.FlowFor,
            IncrementSubGraph = incGraph,
            ConditionSubGraph = MakeSimpleCondGraph(),
            InitSubGraph      = MakeSimpleInitGraph(varName)
        };
    }

    /// <summary>Минимальный ConditionSubGraph: LiteralBool true.</summary>
    private static GraphData MakeSimpleCondGraph()
    {
        var g = new GraphData();
        g.Nodes.Add(new NodeData { Id = "cond", Type = NodeType.LiteralBool, Value = "true", ValueType = "bool" });
        return g;
    }

    /// <summary>Минимальный InitSubGraph: int varName = 0.</summary>
    private static GraphData MakeSimpleInitGraph(string varName)
    {
        var g = new GraphData();
        g.Nodes.Add(new NodeData
        {
            Id = "init", Type = NodeType.LiteralInt,
            Value = "0", ValueType = "int", VariableName = varName
        });
        return g;
    }
}
