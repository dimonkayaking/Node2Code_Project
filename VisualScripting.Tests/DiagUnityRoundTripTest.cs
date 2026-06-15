using System.Text.Json;
using VisualScripting.Core.Generators;
using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

public class DiagUnityRoundTripTest
{
    private readonly RoslynCodeParser _parser = new();
    private readonly SimpleCodeGenerator _gen = new();

    [Fact]
    public void Diag_ParseTestUnityShim_PrintsBodyGraph()
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

        Console.WriteLine("ERRORS: " + string.Join(" | ", result.Errors));
        Console.WriteLine("DiscoveredClasses: " + result.DiscoveredClasses.Count);

        foreach (var cls in result.DiscoveredClasses)
        {
            Console.WriteLine($"Class: {cls.Name}, Methods: {cls.DiscoveredMethods.Count}");
            foreach (var m in cls.DiscoveredMethods)
            {
                Console.WriteLine($"  Method: {m.Name}, BodyGraph nodes: {m.BodyGraph?.Nodes?.Count ?? -1}, edges: {m.BodyGraph?.Edges?.Count ?? -1}");
                if (m.BodyGraph != null)
                {
                    foreach (var n in m.BodyGraph.Nodes)
                    {
                        Console.WriteLine($"    Node {n.Id}: Type={n.Type}, Value={n.Value}, Member={n.MemberName}, ValueType={n.ValueType}, Owner={n.OwnerExpression}, VarName={n.VariableName}");
                    }
                    foreach (var e in m.BodyGraph.Edges)
                    {
                        Console.WriteLine($"    Edge: {e.FromNodeId}.{e.FromPortName} -> {e.ToNodeId}.{e.ToPortName}");
                    }

                    // Try generating code from this body graph
                    var genCode = _gen.Generate(m.BodyGraph);
                    Console.WriteLine("GENERATED CODE:\n" + genCode);
                }
            }
        }

        Assert.True(true);
    }
}
