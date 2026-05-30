using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

/// <summary>
/// Тесты харднинга парсера: п.2 (scoping), п.4 (смещение строк class-обёртки),
/// п.5 (надёжный детект top-level), п.7 (робастность: рекурсия, деление на ноль).
/// </summary>
public class ParserHardeningTests
{
    private readonly RoslynCodeParser _parser = new();

    // ── п.5: детект top-level по дереву, а не по префиксу строки ───────────────

    [Fact]
    public void SealedClass_IsStripped_AndMainBodyParses()
    {
        // "sealed class" не ловился старым строковым префиксом → раньше падало.
        var code = "sealed class Program\n{\n    static void Main()\n    {\n        int x = 5;\n    }\n}";
        var r = _parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        Assert.Contains(r.Graph.Nodes, n => n.VariableName == "x");
    }

    [Fact]
    public void ClassWithLeadingComment_IsStripped_AndMainBodyParses()
    {
        // Ведущий комментарий ломал строковую эвристику (trimmed начинался с "//").
        var code = "// заголовок файла\nclass Program\n{\n    static void Main()\n    {\n        int y = 7;\n    }\n}";
        var r = _parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        Assert.Contains(r.Graph.Nodes, n => n.VariableName == "y");
    }

    [Fact]
    public void PlainTopLevelStatements_AreNotStripped_AndStillParse()
    {
        // Обычный код без типа не должен трактоваться как class-обёртка.
        var r = _parser.Parse("int a = 1;\nint b = 2;");
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        Assert.Contains(r.Graph.Nodes, n => n.VariableName == "a");
        Assert.Contains(r.Graph.Nodes, n => n.VariableName == "b");
    }

    // ── п.4: позиции диагностик относительно исходного файла, а не обрезки ─────

    [Fact]
    public void ClassWrapped_DiagnosticLine_IsRelativeToOriginalFile()
    {
        // unknownVar на 6-й строке исходного файла. Без смещения парсер показал бы 2-ю.
        var code =
            "class P\n" +        // 1
            "{\n" +              // 2
            "    static void Main()\n" + // 3
            "    {\n" +          // 4
            "        int x = 1;\n" +     // 5
            "        int z = unknownVar;\n" + // 6
            "    }\n" +          // 7
            "}";                 // 8
        var r = _parser.Parse(code);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("(6:"));
    }

    // ── п.7: лимит глубины рекурсии (нет StackOverflow) ───────────────────────

    [Fact]
    public void DeeplyNestedExpression_DoesNotCrash_ReportsError()
    {
        // 300 вложенных логических НЕ → рекурсия VisitExpression. Должен сработать
        // лимит глубины и вернуться ошибка, а не уронить процесс StackOverflow'ом.
        var code = "bool b = " + new string('!', 300) + "true;";
        var r = _parser.Parse(code);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("глубоко"));
    }

    // ── п.7: деление на ноль не сворачивается в тихий 0 ───────────────────────

    [Fact]
    public void ConstantFolding_NormalDivision_StillFolds()
    {
        // Регрессионная проверка рефактора switch: 6/2 по-прежнему сворачивается в 3.
        var r = _parser.Parse("int x = 6 / 2;");
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
        var node = r.Graph.Nodes.First(n => n.VariableName == "x");
        Assert.Equal("3", node.Value);
    }

    [Fact]
    public void ConstantFolding_DivisionByZero_NotFolded_ExpressionPreserved()
    {
        var r = _parser.Parse("int x = 5 / 0;");
        Assert.False(r.HasErrors, string.Join("\n", r.Errors)); // не компайл-тайм ошибка
        var node = r.Graph.Nodes.First(n => n.VariableName == "x");
        // Выражение сохранено (а не подменено тихим литералом).
        Assert.Contains("/", node.ExpressionOverride);
    }

    // ── п.2: области видимости блоков (нет ложных «повторных объявлений») ──────

    [Fact]
    public void RedeclareAfterForLoop_NoFalseRedeclarationError()
    {
        var code = "for (int i = 0; i < 3; i = i + 1)\n{\n}\nint i = 99;";
        var r = _parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
    }

    [Fact]
    public void RedeclareAfterWhileBody_NoFalseRedeclarationError()
    {
        var code = "bool go = false;\nwhile (go)\n{\n    int w = 1;\n}\nint w = 2;";
        var r = _parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
    }

    [Fact]
    public void RedeclareAfterIfElseBodies_NoFalseRedeclarationError()
    {
        var code =
            "bool c = true;\n" +
            "if (c)\n{\n    int t = 1;\n}\nelse\n{\n    int t = 2;\n}\n" +
            "int t = 3;";
        var r = _parser.Parse(code);
        Assert.False(r.HasErrors, string.Join("\n", r.Errors));
    }
}
