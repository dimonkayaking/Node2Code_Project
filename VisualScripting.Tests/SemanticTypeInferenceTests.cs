using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

/// <summary>
/// Тесты п.1: SemanticModel как помощник вывода типов.
/// До фикса `var` и алиасы (double/…) схлопывались в int. Эти кейсы
/// в основном наборе не покрыты, поэтому проверяются здесь отдельно.
/// Также проверяется опциональный StrictSemantics и сохранность passthrough.
/// </summary>
public class SemanticTypeInferenceTests
{
    private readonly RoslynCodeParser _parser = new();

    private NodeData DeclNode(ParseResult r, string varName)
    {
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        var node = r.Graph.Nodes.FirstOrDefault(n => n.VariableName == varName);
        Assert.NotNull(node);
        return node!;
    }

    // ── var: тип берётся из инициализатора, а не схлопывается в int ────────────

    [Fact]
    public void Var_FloatInitializer_InfersFloat()
    {
        var node = DeclNode(_parser.Parse("var x = 5.0f;"), "x");
        Assert.Equal("float", node.ValueType);
        Assert.Equal(NodeType.LiteralFloat, node.Type);
    }

    [Fact]
    public void Var_StringInitializer_InfersString()
    {
        var node = DeclNode(_parser.Parse("var s = \"hi\";"), "s");
        Assert.Equal("string", node.ValueType);
        Assert.Equal(NodeType.LiteralString, node.Type);
    }

    [Fact]
    public void Var_BoolInitializer_InfersBool()
    {
        var node = DeclNode(_parser.Parse("var b = true;"), "b");
        Assert.Equal("bool", node.ValueType);
        Assert.Equal(NodeType.LiteralBool, node.Type);
    }

    [Fact]
    public void Var_IntInitializer_InfersInt()
    {
        var node = DeclNode(_parser.Parse("var n = 42;"), "n");
        Assert.Equal("int", node.ValueType);
        Assert.Equal(NodeType.LiteralInt, node.Type);
    }

    // ── Алиасы, которых нет в быстром пути: double → float (раньше → int) ──────

    [Fact]
    public void DoubleDeclaration_MapsToFloat()
    {
        var node = DeclNode(_parser.Parse("double d = 3.14;"), "d");
        Assert.Equal("float", node.ValueType);
    }

    // ── Явные поддерживаемые типы: быстрый путь, поведение неизменно ───────────

    [Theory]
    [InlineData("int a = 1;", "a", "int")]
    [InlineData("float a = 1f;", "a", "float")]
    [InlineData("bool a = true;", "a", "bool")]
    [InlineData("string a = \"q\";", "a", "string")]
    public void ExplicitType_FastPath_Unchanged(string code, string name, string expected)
    {
        var node = DeclNode(_parser.Parse(code), name);
        Assert.Equal(expected, node.ValueType);
    }

    // ── StrictSemantics ───────────────────────────────────────────────────────

    [Fact]
    public void StrictSemantics_Off_TypeMismatch_NoError_DefaultBehavior()
    {
        // По умолчанию семантика не валидирует — несоответствие типов не ошибка.
        var r = _parser.Parse("int x = \"hello\";");
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
    }

    [Fact]
    public void StrictSemantics_On_TypeMismatch_ReportsError()
    {
        var parser = new RoslynCodeParser { StrictSemantics = true };
        var r = parser.Parse("int x = \"hello\";");
        Assert.True(r.HasErrors, "Ожидалась семантическая ошибка несоответствия типов при StrictSemantics.");
    }

    [Fact]
    public void StrictSemantics_On_UnknownMathfMember_StillPassesThrough()
    {
        // Даже при жёсткой валидации незнакомый член (CS0117) исключён — passthrough жив.
        var parser = new RoslynCodeParser { StrictSemantics = true };
        var code = "float x = 4f;\nfloat r = Mathf.Sqrt(x);";
        var r = parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        Assert.Contains(r.Graph.Nodes, n =>
            !string.IsNullOrEmpty(n.ExpressionOverride) && n.ExpressionOverride.Contains("Sqrt"));
    }
}
