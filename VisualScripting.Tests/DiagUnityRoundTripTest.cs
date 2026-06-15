using VisualScripting.Core.Generators;
using VisualScripting.Core.Parsers;
using Xunit;
using Xunit.Abstractions;

namespace VisualScripting.Tests;

/// <summary>
/// Диагностический round-trip тест для Unity API (Этап 8).
/// Парсит класс с методом, использующим Vector3/Mathf/Debug.Log,
/// и генерирует код обратно из графа тела метода.
/// </summary>
public class DiagUnityRoundTripTest
{
    private readonly ITestOutputHelper _output;
    private readonly RoslynCodeParser _parser = new();
    private readonly SimpleCodeGenerator _gen = new();

    public DiagUnityRoundTripTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Diag_ParseTestUnityShim_PrintsBodyGraphAndGeneratesCode()
    {
        var code = """
            class TestUnityShim
            {
                public void TestMethod()
                {
                    Vector3 a = new Vector3(0f, 0f, 0f);
                    Vector3 b = new Vector3(3f, 4f, 0f);
                    float dist = Vector3.Distance(a, b);
                    Debug.Log(dist);
                    float clamped = Mathf.Clamp(10f, 0f, 5f);
                    Debug.Log(clamped);
                    Vector3 moved = Vector3.MoveTowards(a, b, 1f);
                    Debug.Log(moved);
                }
            }
            """;

        var result = _parser.Parse(code);

        _output.WriteLine("ERRORS: " + string.Join(" | ", result.Errors));
        _output.WriteLine("DiscoveredClasses: " + result.DiscoveredClasses.Count);
        _output.WriteLine("DiscoveredMethods: " + result.DiscoveredMethods.Count);

        foreach (var m in result.DiscoveredMethods)
        {
            _output.WriteLine($"Method: {m.Name}, BodyGraph nodes: {m.BodyGraph?.Nodes?.Count ?? -1}, edges: {m.BodyGraph?.Edges?.Count ?? -1}");
            if (m.BodyGraph == null)
                continue;

            foreach (var n in m.BodyGraph.Nodes)
            {
                _output.WriteLine($"    Node {n.Id}: Type={n.Type}, Value={n.Value}, Member={n.MemberName}, ValueType={n.ValueType}, Owner={n.OwnerExpression}, VarName={n.VariableName}, ExprOverride={n.ExpressionOverride}");
            }
            foreach (var e in m.BodyGraph.Edges)
            {
                _output.WriteLine($"    Edge: {e.FromNodeId}.{e.FromPort} -> {e.ToNodeId}.{e.ToPort}");
            }

            var genCode = _gen.Generate(m.BodyGraph);
            _output.WriteLine("GENERATED CODE:\n" + genCode);

            // Главная проверка round-trip: "moved" должен генерироваться как
            // Vector3.MoveTowards(...), а не как new Vector3(0, 0, 0).
            Assert.Contains("Vector3.MoveTowards", genCode);
            Assert.DoesNotContain("new Vector3(0f, 0f, 0f)", genCode.Split('\n').Last(l => l.Contains("moved")));
        }
    }
}
