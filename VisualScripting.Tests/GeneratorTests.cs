using VisualScripting.Core.Generators;
using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

public class GeneratorTests
{
    private readonly RoslynCodeParser _parser = new();
    private readonly SimpleCodeGenerator _generator = new();

    private string Roundtrip(string code)
    {
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        return _generator.Generate(result.Graph);
    }

    [Fact]
    public void SimpleArithmetic()
    {
        var code = "int x = 10;\nint y = 20;\nint z = x + y;";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("int y = 20;", output);
        Assert.Contains("int z = x + y;", output);
    }

    [Fact]
    public void ArithmeticWithIntermediateNode()
    {
        var code = "int x = 10;\nint y = 20;\nint z = x + y * 2;";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("int y = 20;", output);
        Assert.Contains("int z = x + (y * 2);", output);
    }

    [Fact]
    public void ModuloOperator()
    {
        var code = "int x = 10;\nint y = 3;\nint z = x % y;";
        var output = Roundtrip(code);
        Assert.Contains("int z = x % y;", output);
    }

    [Fact]
    public void ComparisonOperators()
    {
        var code = "int a = 5;\nint b = 10;\nbool r1 = a >= b;\nbool r2 = a <= b;\nbool r3 = a != b;";
        var output = Roundtrip(code);
        Assert.Contains("bool r1 = a >= b;", output);
        Assert.Contains("bool r2 = a <= b;", output);
        Assert.Contains("bool r3 = a != b;", output);
    }

    [Fact]
    public void LogicalOperators()
    {
        var code = "bool a = true;\nbool b = false;\nbool r1 = a && b;\nbool r2 = a || b;\nbool r3 = !a;";
        var output = Roundtrip(code);
        Assert.Contains("bool r1 = a && b;", output);
        Assert.Contains("bool r2 = a || b;", output);
        Assert.Contains("bool r3 = !a;", output);
    }

    [Fact]
    public void SimpleIfElse()
    {
        var code = @"
int x = 10;
int y = 20;
int z = 0;
if (x > y)
{
    z = x + y;
}
else
{
    z = x - y;
}";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("int y = 20;", output);
        Assert.Contains("int z = 0;", output);
        Assert.Contains("if (x > y)", output);
        Assert.Contains("z = x + y;", output);
        Assert.Contains("else", output);
        Assert.Contains("z = x - y;", output);
    }

    [Fact]
    public void IfElseIfElse()
    {
        var code = @"
int x = 10;
int y = 20;
int z;
if (x > y)
{
    z = x;
}
else if (x == y)
{
    z = 0;
}
else
{
    z = y;
}";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("int y = 20;", output);
        Assert.Contains("int z = 0;", output);
        Assert.Contains("if (x > y)", output);
        Assert.Contains("z = x;", output);
        Assert.Contains("else if (x == y)", output);
        Assert.Contains("z = 0;", output);
        Assert.Contains("else", output);
        Assert.Contains("z = y;", output);
    }

    [Fact]
    public void ConditionWithLogic()
    {
        var code = @"
int x = 10;
int y = 20;
int z = 0;
bool flag = true;
if (x >= y && z != 0 || !flag)
{
    z = x + y;
}
else
{
    z = x - y;
}";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("bool flag = true;", output);
        Assert.Contains("if (", output);
        Assert.Contains(">=", output);
        Assert.Contains("&&", output);
        Assert.Contains("||", output);
        Assert.Contains("!flag", output);
        Assert.Contains("z = x + y;", output);
        Assert.Contains("z = x - y;", output);
    }

    [Fact]
    public void DeclarationWithoutInitializer()
    {
        var code = "int x;\nint y = 10;";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var declNode = result.Graph.Nodes.FirstOrDefault(n => n.VariableName == "x");
        Assert.NotNull(declNode);
        Assert.Equal(NodeType.LiteralInt, declNode.Type);
        Assert.Equal("int", declNode.ValueType);

        var output = _generator.Generate(result.Graph);
        Assert.Contains("int x = 0;", output);
        Assert.Contains("int y = 10;", output);
    }

    [Fact]
    public void AssignmentCreatesLiteralNode()
    {
        var code = "int x = 10;\nx = 20;";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var setNodes = result.Graph.Nodes.Where(n => n.VariableName == "x").ToList();
        Assert.Equal(2, setNodes.Count);
        Assert.Equal(NodeType.LiteralInt, setNodes[1].Type);
    }

    [Fact]
    public void FloatLiteral()
    {
        var code = "float x = 1.5f;";
        var output = Roundtrip(code);
        Assert.Contains("float x = 1.5f;", output);
    }

    [Fact]
    public void BoolLiteral()
    {
        var code = "bool flag = true;";
        var output = Roundtrip(code);
        Assert.Contains("bool flag = true;", output);
    }

    [Fact]
    public void StringLiteral()
    {
        var code = "string name = \"hello\";";
        var output = Roundtrip(code);
        Assert.Contains("string name = \"hello\";", output);
    }

    [Fact]
    public void EmptyGraphReturnsComment()
    {
        var output = _generator.Generate(new GraphData());
        Assert.Contains("//", output);
    }

    [Fact]
    public void NullGraphReturnsComment()
    {
        var output = _generator.Generate(null!);
        Assert.Contains("//", output);
    }

    [Fact]
    public void DivisionOperator()
    {
        var code = "int x = 10;\nint y = 2;\nint z = x / y;";
        var output = Roundtrip(code);
        Assert.Contains("int z = x / y;", output);
    }

    [Fact]
    public void SubtractionOperator()
    {
        var code = "int x = 10;\nint y = 2;\nint z = x - y;";
        var output = Roundtrip(code);
        Assert.Contains("int z = x - y;", output);
    }

    [Fact]
    public void NestedInlineExpressions()
    {
        var code = "int a = 1;\nint b = 2;\nint c = 3;\nint d = a + b * c;";
        var output = Roundtrip(code);
        Assert.Contains("int d = a + (b * c);", output);
    }

    [Fact]
    public void IfWithOnlyTrueBranch()
    {
        var code = @"
int x = 10;
if (x > 5)
{
    x = 0;
}";
        var output = Roundtrip(code);
        Assert.Contains("if (x > 5)", output);
        Assert.Contains("x = 0;", output);
        Assert.DoesNotContain("else", output);
    }

    [Fact]
    public void ForLoopWithCompoundAssignmentInBody()
    {
        var code = @"
int sum = 0;
for (int i = 0; i < 10; i++)
{
    sum += i;
}";
        var output = Roundtrip(code);
        Assert.Contains("for (int i = 0; i < 10; i++)", output);
        Assert.Contains("sum = sum + i;", output);
    }

    [Fact]
    public void WhileLoop()
    {
        var code = @"
int n = 3;
while (n > 0)
{
    n--;
}";
        var output = Roundtrip(code);
        Assert.Contains("while (n > 0)", output);
        Assert.Contains("n = n - 1;", output);
    }

    [Fact]
    public void ConsoleWriteLineStatement()
    {
        var code = @"Console.WriteLine(""Hello"");";
        var output = Roundtrip(code);
        Assert.Contains("Console.WriteLine(\"Hello\");", output);
    }

    [Fact]
    public void IntParseRoundtrip()
    {
        var code = @"int x = int.Parse(""42"");";
        var output = Roundtrip(code);
        Assert.Contains("int x = int.Parse(\"42\");", output);
    }

    [Fact]
    public void FloatParseRoundtrip()
    {
        var code = @"float f = float.Parse(""3.14"");";
        var output = Roundtrip(code);
        Assert.Contains("float f = float.Parse(\"3.14\");", output);
    }

    [Fact]
    public void ToStringRoundtrip()
    {
        var code = @"
int n = 5;
string s = n.ToString();";
        var output = Roundtrip(code);
        Assert.Contains("string s = n.ToString();", output);
    }

    [Fact]
    public void CompoundAssignmentPlus()
    {
        var code = @"
int a = 1;
a += 2;";
        var output = Roundtrip(code);
        Assert.Contains("a = a + 2;", output);
    }

    [Fact]
    public void MathfAbsMaxMinRoundtrip()
    {
        var code = @"
float x = 1f;
float y = Mathf.Abs(x);
float z = Mathf.Max(x, y);
float w = Mathf.Min(x, y);";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        var output = _generator.Generate(result.Graph);
        Assert.Contains("Math.Abs", output);
        Assert.Contains("Math.Max", output);
        Assert.Contains("Math.Min", output);
    }

    [Fact]
    public void MathfAbsQualifiedUnityEngine_ParsesWithoutErrors()
    {
        var code = @"
float x = 2f;
float y = UnityEngine.Mathf.Abs(x);";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.Contains(result.Graph.Nodes, n => n.Type == NodeType.MathfAbs);
    }

    [Fact]
    public void SystemMathAbsMaxMin_ParseToSameNodesAsMathf()
    {
        var code = @"
float a = 1f;
float b = 2f;
float x = System.Math.Abs(a);
float y = Math.Max(a, b);
float z = Math.Min(a, b);";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.Contains(result.Graph.Nodes, n => n.Type == NodeType.MathfAbs);
        Assert.Contains(result.Graph.Nodes, n => n.Type == NodeType.MathfMax);
        Assert.Contains(result.Graph.Nodes, n => n.Type == NodeType.MathfMin);
    }

    [Fact]
    public void UnaryMinus_ParseWithoutErrors_FoldedAndSubtract()
    {
        var code = @"
float a = 2f;
float b = -a;
float c = -3.5f;";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.True(result.Graph.Nodes.Exists(n =>
            n.Type == NodeType.LiteralFloat && n.Value.StartsWith('-')));
        Assert.Contains(result.Graph.Nodes, n => n.Type == NodeType.MathSubtract);
    }

    [Fact]
    public void MathfSqrt_and_MathPi_Passthrough_ParseWithoutErrors()
    {
        var code = @"
float x = 4f;
float r = Mathf.Sqrt(x);
float pi = Mathf.PI;";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.True(result.Graph.Nodes.Exists(n =>
            !string.IsNullOrEmpty(n.ExpressionOverride) &&
            n.ExpressionOverride.Contains("Sqrt")));
        Assert.True(result.Graph.Nodes.Exists(n =>
            !string.IsNullOrEmpty(n.ExpressionOverride) &&
            n.ExpressionOverride.Contains("PI")));
    }

    [Fact]
    public void VariableReassignment()
    {
        var code = "int x = 10;\nx = 20;";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("x = 20;", output);
    }

    [Fact]
    public void VariableReassignmentWithExpression()
    {
        var code = "int x = 10;\nint y = 5;\nx = x + y;";
        var output = Roundtrip(code);
        Assert.Contains("int x = 10;", output);
        Assert.Contains("int y = 5;", output);
        Assert.Contains("x = x + y;", output);
    }

    [Fact]
    public void FloatVariableRoundtrip()
    {
        var code = "float speed = 5.5f;";
        var output = Roundtrip(code);
        Assert.Contains("float speed = 5.5f;", output);
    }

    [Fact]
    public void FloatArithmeticRoundtrip()
    {
        var code = "float a = 1.5f;\nfloat b = 2.5f;\nfloat c = a + b;";
        var output = Roundtrip(code);
        Assert.Contains("float a = 1.5f;", output);
        Assert.Contains("float b = 2.5f;", output);
        Assert.Contains("float c = a + b;", output);
    }

    [Fact]
    public void IfWithVariableAssignmentInBranch()
    {
        var code = @"
int score = 75;
string grade = """";
if (score >= 90)
{
    grade = ""A"";
}";
        var output = Roundtrip(code);
        Assert.Contains("int score = 75;", output);
        Assert.Contains("if (score >= 90)", output);
        Assert.Contains("grade = \"A\";", output);
    }

    [Fact]
    public void IfElseIfElseChain()
    {
        var code = @"
int score = 75;
string grade = """";
if (score >= 90)
{
    grade = ""A"";
}
else if (score >= 80)
{
    grade = ""B"";
}
else
{
    grade = ""F"";
}";
        var output = Roundtrip(code);
        Assert.Contains("if (score >= 90)", output);
        Assert.Contains("grade = \"A\";", output);
        Assert.Contains("else if (score >= 80)", output);
        Assert.Contains("grade = \"B\";", output);
        Assert.Contains("else", output);
        Assert.Contains("grade = \"F\";", output);
    }

    [Fact]
    public void AgeIfElseIfElseWithConsoleRoundtrip()
    {
        var code = @"
int age = 18;

if (age < 18)
{
    Console.WriteLine(""Вы несовершеннолетний"");
}
else if (age >= 18 && age < 65)
{
    Console.WriteLine(""Вы взрослый"");
}
else
{
    Console.WriteLine(""Вы пенсионер"");
}
";
        var output = Roundtrip(code);
        Assert.DoesNotContain("else if (age < 18)", output);
        Assert.Contains("else if ((age >= 18)", output);
        Assert.Contains("Вы пенсионер", output);
    }

    [Fact]
    public void IfElseIfElse_ShuffledNodeOrder_StillEmitsElseIfChain()
    {
        var code = @"
int x = 10;
int y = 20;
int z;
if (x > y)
{
    z = x;
}
else if (x == y)
{
    z = 0;
}
else
{
    z = y;
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        var nodes = result.Graph.Nodes;
        var ifNodes = nodes.Where(n => n.Type == NodeType.FlowIf).ToList();
        Assert.Equal(2, ifNodes.Count);
        nodes.Remove(ifNodes[1]);
        nodes.Insert(0, ifNodes[1]);

        var output = _generator.Generate(result.Graph);
        Assert.Contains("else if (x == y)", output);
        Assert.Contains("z = y;", output);
    }

    [Fact]
    public void IfElseIfElse_LegacyFalseAndExecPortNames_StillEmitsElseIfChain()
    {
        var code = @"
int number = 10;
if (number > 0)
{
    Console.WriteLine(""Число положительное"");
}
else if (number < 0)
{
    Console.WriteLine(""Число отрицательное"");
}
else
{
    Console.WriteLine(""Число равно нулю"");
}";

        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var ifNodes = result.Graph.Nodes.Where(n => n.Type == NodeType.FlowIf).ToList();
        Assert.Equal(2, ifNodes.Count);
        var firstIfId = ifNodes[0].Id;
        var secondIfId = ifNodes[1].Id;

        var falseEdge = result.Graph.Edges.FirstOrDefault(
            e => e.FromNodeId == firstIfId && e.ToNodeId == secondIfId && e.FromPort == "falseBranch");
        Assert.NotNull(falseEdge);
        falseEdge!.FromPort = "false";
        falseEdge.ToPort = "exec";

        var output = _generator.Generate(result.Graph).Replace("\r", "");
        Assert.Contains("else if (number < 0)", output);
        Assert.DoesNotContain("}\nif (number < 0)", output);
    }

    [Fact]
    public void IfElseIfElse_LegacyExecOutInsteadOfFalseBranch_StillEmitsElseIfChain()
    {
        var code = @"
int number = 10;
if (number > 0)
{
    Console.WriteLine(""Число положительное"");
}
else if (number < 0)
{
    Console.WriteLine(""Число отрицательное"");
}
else
{
    Console.WriteLine(""Число равно нулю"");
}";

        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var ifNodes = result.Graph.Nodes.Where(n => n.Type == NodeType.FlowIf).ToList();
        Assert.Equal(2, ifNodes.Count);
        var firstIfId = ifNodes[0].Id;
        var secondIfId = ifNodes[1].Id;

        var falseEdge = result.Graph.Edges.FirstOrDefault(
            e => e.FromNodeId == firstIfId && e.ToNodeId == secondIfId && e.FromPort == "falseBranch");
        Assert.NotNull(falseEdge);
        falseEdge!.FromPort = "execOut";
        falseEdge.ToPort = "execIn";

        var output = _generator.Generate(result.Graph).Replace("\r", "");
        Assert.Contains("else if (number < 0)", output);
        Assert.DoesNotContain("}\nif (number < 0)", output);
    }

    [Fact]
    public void Roundtrip_IfElseIfElse_AndTernaryDeclarations_PreservesStructureAndTypes()
    {
        var code = @"
int a = 15;
int b = 7;
int result = 0;

if (a > b)
{
    result = a - b;
}
else if (a < b)
{
    result = b - a;
}
else
{
    result = a + b;
}

int max = (a > b) ? a : b;
int absolute = (result < 0) ? -result : result;";

        var output = Roundtrip(code).Replace("\r", "");
        Assert.Contains("if (a > b)", output);
        Assert.Contains("else if (a < b)", output);
        Assert.Contains("else", output);
        Assert.DoesNotContain("}\nif (a < b)", output);

        Assert.Contains("int max = (a > b) ? a : b;", output);
        Assert.Contains("int absolute = (result < 0) ? -result : result;", output);

        Assert.DoesNotContain("a = ;", output);
        Assert.DoesNotContain("b = ;", output);
        Assert.DoesNotContain("string max =", output);
        Assert.DoesNotContain("string absolute =", output);
    }

    [Fact]
    public void ConsoleWriteLineWithVariable()
    {
        var code = @"
string message = ""Hello World"";
Console.WriteLine(message);";
        var output = Roundtrip(code);
        Assert.Contains("string message = \"Hello World\";", output);
        Assert.Contains("Console.WriteLine(message);", output);
    }

    [Fact]
    public void ConsoleWriteLineWithoutMessageEdgeUsesNodeValue()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData
        {
            Id = "print_1",
            Type = NodeType.ConsoleWriteLine,
            Value = "Direct literal from node",
            ValueType = "string"
        });

        var output = _generator.Generate(graph);
        Assert.Contains("Console.WriteLine(\"Direct literal from node\");", output);
    }

    [Fact]
    public void ConsoleWriteLineWithoutMessageEdgeUsesTypedLiteral()
    {
        var graph = new GraphData();
        graph.Nodes.Add(new NodeData
        {
            Id = "print_int",
            Type = NodeType.ConsoleWriteLine,
            Value = "40",
            ValueType = "int"
        });

        var output = _generator.Generate(graph);
        Assert.Contains("Console.WriteLine(40);", output);
    }

    [Fact]
    public void MultipleConsoleWriteLines()
    {
        var code = @"
Console.WriteLine(""First"");
Console.WriteLine(""Second"");";
        var output = Roundtrip(code);
        Assert.Contains("Console.WriteLine(\"First\");", output);
        Assert.Contains("Console.WriteLine(\"Second\");", output);
    }

    [Fact]
    public void ForLoopSimple()
    {
        var code = @"
int sum = 0;
for (int i = 0; i < 5; i++)
{
    sum += i;
}";
        var output = Roundtrip(code);
        Assert.Contains("for (int i = 0; i < 5; i++)", output);
        Assert.Contains("sum = sum + i;", output);
    }

    [Fact]
    public void WhileLoopWithDecrement()
    {
        var code = @"
int count = 10;
while (count > 0)
{
    count--;
}";
        var output = Roundtrip(code);
        Assert.Contains("while (count > 0)", output);
        Assert.Contains("count = count - 1;", output);
    }

    [Fact]
    public void ParserCreatesExecFlowEdges()
    {
        var code = @"
int x = 10;
if (x > 0)
{
    Console.WriteLine(x);
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var execEdges = result.Graph.Edges
            .Where(e => PortIds.IsExecIn(e.ToPort) || PortIds.IsExecOut(e.FromPort) || PortIds.IsFalseBranch(e.FromPort))
            .ToList();
        Assert.True(execEdges.Count > 0, "Parser should create execution flow edges for flow nodes");
    }

    [Fact]
    public void ParserUsesCanonicalExecPortIds()
    {
        var code = @"
int x = 10;
if (x > 3)
{
    x = x + 1;
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        Assert.All(result.Graph.Edges.Where(e => PortIds.IsExecIn(e.ToPort)),
            e => Assert.Equal(PortIds.ExecIn, PortIds.Normalize(e.ToPort)));
        Assert.All(result.Graph.Edges.Where(e => PortIds.IsExecOut(e.FromPort) || PortIds.IsFalseBranch(e.FromPort)),
            e => Assert.True(e.FromPort == PortIds.ExecOut || e.FromPort == PortIds.FalseBranch));
    }

    [Fact]
    public void GeneratorRestoresElseIfFromFalseBranchOnly()
    {
        var code = @"
int number = 1;
if (number > 1)
{
    number = 10;
}
else if (number < 0)
{
    number = 10;
}
else
{
    number = 0;
}";

        var parsed = _parser.Parse(code);
        Assert.False(parsed.HasErrors, string.Join("\n", parsed.Errors));

        var generated = _generator.Generate(parsed.Graph).Replace("\r", "");
        Assert.Contains("else if (number < 0)", generated);
    }

    [Fact]
    public void ParserIfCreatesSubGraphs()
    {
        var code = @"
int x = 10;
if (x > 5)
{
    x = 0;
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var ifNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.FlowIf);
        Assert.NotNull(ifNode);

        Assert.NotNull(ifNode.ConditionSubGraph);
        Assert.True(ifNode.ConditionSubGraph.Nodes.Count > 0, "Condition sub-graph should have nodes");

        Assert.NotNull(ifNode.BodySubGraph);
        Assert.True(ifNode.BodySubGraph.Nodes.Count > 0, "Body sub-graph should have nodes");
    }

    [Fact]
    public void ParserIfElseCreatesLadder()
    {
        var code = @"
int x = 10;
if (x > 20)
{
    x = 1;
}
else
{
    x = 2;
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var ifNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.FlowIf);
        Assert.NotNull(ifNode);
        var elseNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.FlowElse);
        Assert.NotNull(elseNode);

        var falseEdge = result.Graph.Edges.FirstOrDefault(
            e => e.FromNodeId == ifNode.Id && e.FromPort == "falseBranch" && e.ToNodeId == elseNode.Id);
        Assert.NotNull(falseEdge);

        Assert.NotNull(elseNode.BodySubGraph);
        Assert.True(elseNode.BodySubGraph.Nodes.Count > 0, "Else body sub-graph should have nodes");
    }

    [Fact]
    public void LiteralNodeActsAsAssignment()
    {
        var code = "int x = 10;\nx = 20;";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var nodesWithX = result.Graph.Nodes.Where(n => n.VariableName == "x").ToList();
        Assert.True(nodesWithX.Count >= 2);
    }

    [Fact]
    public void ConsoleWriteLineNodeCreated()
    {
        var code = @"Console.WriteLine(""Test"");";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var cwlNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.ConsoleWriteLine);
        Assert.NotNull(cwlNode);
        Assert.Equal("string", cwlNode.ValueType);
        Assert.Equal("Test", cwlNode.Value);

        var msgEdge = result.Graph.Edges.FirstOrDefault(e => e.ToNodeId == cwlNode.Id && e.ToPort == "message");
        Assert.Null(msgEdge);
    }

    [Fact]
    public void ConsoleWriteLineVariableKeepsMessageEdge()
    {
        var code = @"
string message = ""Hi"";
Console.WriteLine(message);";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var cwlNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.ConsoleWriteLine);
        Assert.NotNull(cwlNode);

        var msgEdge = result.Graph.Edges.FirstOrDefault(e => e.ToNodeId == cwlNode!.Id && e.ToPort == "message");
        Assert.NotNull(msgEdge);
    }

    [Fact]
    public void SiblingIfBlocksDoNotShareVariableScope()
    {
        // Два независимых if-блока объявляют локальную x каждый в своей области
        // видимости — не должно быть re-declaration и не должно быть
        // "просто присваивания" во втором if.
        var code = @"
bool a = true;
bool b = false;
if (a)
{
    int x = 1;
}
if (b)
{
    int x = 2;
}";
        var output = Roundtrip(code);

        // Оба if должны содержать полноценное объявление с типом.
        var withTypeCount = System.Text.RegularExpressions.Regex.Matches(output, @"\bint\s+x\s*=\s*\d+\s*;").Count;
        Assert.True(withTypeCount >= 2,
            $"Expected two independent 'int x = ...;' declarations, got:\n{output}");

        // Не должно быть строки вида "x = 2;" без типа — это указывало бы на
        // ошибочное переиспользование области видимости.
        Assert.DoesNotMatch(
            new System.Text.RegularExpressions.Regex(@"(?<!int\s)\bx\s*=\s*2\s*;"),
            output);
    }

    [Fact]
    public void SiblingForBlocksDoNotShareVariableScope()
    {
        var code = @"
for (int i = 0; i < 3; i = i + 1)
{
    int k = 10;
}
for (int j = 0; j < 2; j = j + 1)
{
    int k = 20;
}";
        var output = Roundtrip(code);

        var withTypeCount = System.Text.RegularExpressions.Regex.Matches(output, @"\bint\s+k\s*=\s*\d+\s*;").Count;
        Assert.True(withTypeCount >= 2,
            $"Expected two independent 'int k = ...;' declarations, got:\n{output}");
    }

    [Fact]
    public void InnerIfCanDeclareVariableAfterSiblingIfPopsScope()
    {
        var code = @"
bool a = true;
if (a)
{
    int x = 1;
}
else
{
    int x = 2;
}";
        var output = Roundtrip(code);

        Assert.Contains("int x = 1;", output);
        Assert.Contains("int x = 2;", output);
    }

    [Fact]
    public void ConditionSubGraph_VariableRefStub_CopiesInitializerValueFromOuterScope()
    {
        var code = @"
int score = 72;
string grade = """";

if (score >= 90)
{
    grade = ""A"";
}";
        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var ifNode = result.Graph.Nodes.FirstOrDefault(n => n.Type == NodeType.FlowIf);
        Assert.NotNull(ifNode);
        Assert.NotNull(ifNode!.ConditionSubGraph);

        var scoreStub = ifNode.ConditionSubGraph!.Nodes.FirstOrDefault(
            n => n.VariableName == "score" && n.Type == NodeType.LiteralInt);
        Assert.NotNull(scoreStub);
        Assert.Equal("72", scoreStub!.Value);
    }
}
