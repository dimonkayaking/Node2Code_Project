using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace VisualScripting.Tests;

/// <summary>
/// Тесты наследования классов.
/// Запуск: dotnet test из папки VisualScripting.Tests
/// </summary>
public class InheritanceTests
{
    private readonly RoslynCodeParser _parser = new();

    // ── 1. Парсер: базовый класс обнаруживается ────────────────────────────

    [Fact]
    public void Parser_SimpleInheritance_DetectsBaseClassName()
    {
        var code = """
            class Animal
            {
                public static string Name;
                public static void Speak() { int x = 1; }
            }
            class Dog : Animal
            {
                public static void Bark() { }
                public static void Main() { int y = 2; }
            }
            """;

        var result = _parser.Parse(code);

        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.Equal(2, result.DiscoveredClasses.Count);

        var animal = result.DiscoveredClasses.First(c => c.Name == "Animal");
        var dog    = result.DiscoveredClasses.First(c => c.Name == "Dog");

        Assert.Equal("", animal.BaseClassName);      // Animal — корневой
        Assert.Equal("Animal", dog.BaseClassName);   // Dog наследует Animal
    }

    [Fact]
    public void Parser_ThreeLevelChain_DetectsAllBaseClassNames()
    {
        var code = """
            class A
            {
                public static void M1() { int a = 1; }
            }
            class B : A
            {
                public static void M2() { int b = 2; }
            }
            class C : B
            {
                public static void M3() { int c = 3; }
                public static void Main() { int x = 0; }
            }
            """;

        var result = _parser.Parse(code);

        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var a = result.DiscoveredClasses.First(c => c.Name == "A");
        var b = result.DiscoveredClasses.First(c => c.Name == "B");
        var c = result.DiscoveredClasses.First(c => c.Name == "C");

        Assert.Equal("",  a.BaseClassName);
        Assert.Equal("A", b.BaseClassName);
        Assert.Equal("B", c.BaseClassName);
    }

    [Fact]
    public void Parser_NoInheritance_BaseClassNameIsEmpty()
    {
        var code = """
            class Program
            {
                public static void Main() { int x = 1; }
            }
            """;

        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var prog = result.DiscoveredClasses.First(c => c.Name == "Program");
        Assert.Equal("", prog.BaseClassName);
    }

    // ── 2. Модели данных: новые поля присутствуют ─────────────────────────

    [Fact]
    public void ParsedClassInfo_HasBaseClassNameField()
    {
        var info = new ParsedClassInfo { Name = "Dog", BaseClassName = "Animal" };
        Assert.Equal("Animal", info.BaseClassName);
    }

    // ── 3. Парсер: поля дочернего класса не смешиваются с полями родителя ─

    [Fact]
    public void Parser_InheritedClass_FieldsBelongToCorrectClass()
    {
        var code = """
            class Animal
            {
                public static string Name;
                public static void Main() { int x = 1; }
            }
            class Dog : Animal
            {
                public static int Age;
            }
            """;

        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var animal = result.DiscoveredClasses.First(c => c.Name == "Animal");
        var dog    = result.DiscoveredClasses.First(c => c.Name == "Dog");

        Assert.Single(animal.Fields);
        Assert.Equal("Name", animal.Fields[0].Name);

        Assert.Single(dog.Fields);
        Assert.Equal("Age", dog.Fields[0].Name);
    }

    // ── 4. Парсер: методы дочернего класса ────────────────────────────────

    [Fact]
    public void Parser_InheritedClass_MethodsBelongToCorrectClass()
    {
        var code = """
            class Animal
            {
                public static void Speak() { int a = 1; }
            }
            class Dog : Animal
            {
                public static void Bark() { int b = 2; }
                public static void Main() { int x = 0; }
            }
            """;

        var result = _parser.Parse(code);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));

        var animal = result.DiscoveredClasses.First(c => c.Name == "Animal");
        var dog    = result.DiscoveredClasses.First(c => c.Name == "Dog");

        Assert.Equal(new[] { "Speak" }, animal.MethodNames);
        Assert.Contains("Bark", dog.MethodNames);
    }

    // ── 5. Топологическая сортировка (реализация прямо в тесте) ───────────

    private record ClassStub(string Name, string BaseName);

    [Fact]
    public void TopologicalSort_ParentBeforeChild()
    {
        var classes = new List<ClassStub>
        {
            new("Dog",    "Animal"),
            new("Animal", "")
        };

        var sorted = TopoSort(classes);

        var idxAnimal = sorted.FindIndex(c => c.Name == "Animal");
        var idxDog    = sorted.FindIndex(c => c.Name == "Dog");
        Assert.True(idxAnimal < idxDog, $"Animal ({idxAnimal}) должен быть до Dog ({idxDog})");
    }

    [Fact]
    public void TopologicalSort_ThreeLevels_CorrectOrder()
    {
        var classes = new List<ClassStub>
        {
            new("C", "B"),
            new("A", ""),
            new("B", "A")
        };

        var sorted    = TopoSort(classes);
        var names     = sorted.Select(c => c.Name).ToList();

        Assert.Equal(new[] { "A", "B", "C" }, names);
    }

    [Fact]
    public void TopologicalSort_NoInheritance_OrderPreserved()
    {
        var classes = new List<ClassStub>
        {
            new("X", ""),
            new("Y", ""),
            new("Z", "")
        };

        var sorted = TopoSort(classes);
        Assert.Equal(new[] { "X", "Y", "Z" }, sorted.Select(c => c.Name));
    }

    // ── 6. Логика фильтрации кандидатов при выборе родителя ───────────────

    [Fact]
    public void ParentFilter_ParentOfEditedClass_IsAllowedAsCandidate()
    {
        // Редактируем Dog (у которого уже родитель Animal).
        // Animal должен оставаться доступным кандидатом — это не создаёт цикл.
        var editing  = new ClassStub("Dog",    "Animal");
        var animal   = new ClassStub("Animal", "");
        var cat      = new ClassStub("Cat",    "");

        var all = new List<ClassStub> { editing, animal, cat };

        var candidates = all.Where(c => !IsSelfOrDescendant(c, editing, all)).ToList();

        Assert.Contains(candidates, c => c.Name == "Animal"); // ✓ предок разрешён
        Assert.DoesNotContain(candidates, c => c.Name == "Dog"); // ✗ сам себя нельзя
        Assert.Contains(candidates, c => c.Name == "Cat");    // ✓ несвязанный разрешён
    }

    [Fact]
    public void ParentFilter_DescendantOfEditedClass_IsExcluded()
    {
        // Редактируем Animal. Puppy : Dog : Animal — потомки Animal нельзя выбирать.
        var animal = new ClassStub("Animal", "");
        var dog    = new ClassStub("Dog",    "Animal");
        var puppy  = new ClassStub("Puppy",  "Dog");

        var all = new List<ClassStub> { animal, dog, puppy };

        var candidates = all.Where(c => !IsSelfOrDescendant(c, animal, all)).ToList();

        Assert.DoesNotContain(candidates, c => c.Name == "Animal"); // сам себя нельзя
        Assert.DoesNotContain(candidates, c => c.Name == "Dog");    // потомок нельзя
        Assert.DoesNotContain(candidates, c => c.Name == "Puppy");  // потомок нельзя
    }

    // Вспомогательный предикат (копия логики из CreateClassPopup.IsSelfOrDescendant)
    private static bool IsSelfOrDescendant(ClassStub candidate, ClassStub editing, List<ClassStub> all)
    {
        if (candidate.Name == editing.Name) return true;
        var byName = all.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var current = candidate;
        while (current != null && !string.IsNullOrEmpty(current.BaseName))
        {
            if (!visited.Add(current.BaseName)) break;
            if (current.BaseName == editing.Name) return true;
            byName.TryGetValue(current.BaseName, out current!);
        }
        return false;
    }

    [Fact]
    public void TopologicalSort_Cycle_DoesNotHang()
    {
        // Цикл A→B→A — должен завершиться, не зависнуть
        var classes = new List<ClassStub>
        {
            new("A", "B"),
            new("B", "A")
        };

        var sorted = TopoSort(classes);
        Assert.Equal(2, sorted.Count); // оба класса в результате, порядок любой
    }

    // ── Вспомогательная топосортировка (копия логики из GeneratorBridge) ──

    private static List<ClassStub> TopoSort(List<ClassStub> classes)
    {
        var byName  = classes.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);
        var result  = new List<ClassStub>(classes.Count);

        void Visit(ClassStub cls)
        {
            if (visited.Contains(cls.Name)) return;
            if (inStack.Contains(cls.Name)) return; // цикл
            inStack.Add(cls.Name);
            if (!string.IsNullOrEmpty(cls.BaseName) &&
                byName.TryGetValue(cls.BaseName, out var parent))
                Visit(parent);
            inStack.Remove(cls.Name);
            visited.Add(cls.Name);
            result.Add(cls);
        }

        foreach (var cls in classes) Visit(cls);
        return result;
    }
}
