using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VisualScripting.Core.Models;
using VisualScripting.Core.Generators;

namespace CustomVisualScripting.Integration
{
    public static class GeneratorBridge
    {
        private static readonly SimpleCodeGenerator _generator = new SimpleCodeGenerator();

        public static void Initialize()
        {
            Debug.Log("[VS] GeneratorBridge инициализирован");
        }

        // ─── Основной API ─────────────────────────────────────────────────────

        /// <summary>
        /// Генерирует полный C#-код из классов и их методов.
        /// Каждый класс превращается в <c>class ClassName { static fields; static methods; }</c>.
        /// Методы без ClassId попадают в последний класс «Program» (или игнорируются).
        /// </summary>
        public static string GenerateWithClasses(
            IEnumerable<ClassInfo> classes,
            IEnumerable<MethodInfo> methods)
        {
            var classList  = (classes  ?? Enumerable.Empty<ClassInfo>()).ToList();
            var methodList = (methods  ?? Enumerable.Empty<MethodInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .ToList();

            if (classList.Count == 0)
                return "// Нет классов";

            // Нормализуем порты
            foreach (var m in methodList)
                if (m.BodyGraph != null) NormalizePorts(m.BodyGraph);

            var methodInfosById = methodList.ToDictionary(m => m.Id);

            var sb = new StringBuilder();
            bool first = true;

            foreach (var cls in classList)
            {
                if (!first) sb.AppendLine();
                first = false;

                sb.AppendLine($"class {cls.Name}");
                sb.AppendLine("{");

                // Статические поля
                foreach (var field in cls.Fields ?? Enumerable.Empty<ClassFieldData>())
                {
                    if (!string.IsNullOrWhiteSpace(field.DefaultValue))
                        sb.AppendLine($"    public static {field.Type} {field.Name} = {field.DefaultValue};");
                    else
                        sb.AppendLine($"    public static {field.Type} {field.Name};");
                }
                if (cls.Fields?.Count > 0) sb.AppendLine();

                // Статические методы класса
                var classMethods = methodList
                    .Where(m => string.Equals(m.ClassId, cls.Id, StringComparison.Ordinal))
                    .ToList();

                bool firstMethod = true;
                foreach (var def in classMethods)
                {
                    if (!firstMethod) sb.AppendLine();
                    firstMethod = false;

                    var code = BuildStaticMethod(def, methodInfosById);
                    sb.AppendLine(IndentLines(code, 1));
                }

                sb.AppendLine("}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetDefaultForType(string type) => type switch
        {
            "float"  => "0f",
            "bool"   => "false",
            "string" => "\"\"",
            _        => "0"
        };

        /// <summary>Генерирует код только из основного графа (без пользовательских методов).</summary>
        public static string Generate(GraphData graph)
        {
            if (graph == null)
            {
                Debug.LogError("[VS] GraphData пуст");
                return "";
            }
            NormalizePorts(graph);
            return _generator.Generate(graph);
        }

        /// <summary>
        /// Генерирует полный код в виде валидного C#:
        /// <c>class Program { static void Main() { ... } static void Method1(...) { ... } }</c>.
        /// Если методов нет — возвращает плоский код основного графа (для обратной совместимости
        /// с <see cref="CSharpProcessRunner"/>, который оборачивает плоский код в Main сам).
        /// Конвертация MethodDefinition → MethodInfo выполняется на стороне вызывающего (Editor-слой).
        /// </summary>
        public static string GenerateWithMethods(GraphData mainGraph, IEnumerable<MethodInfo> methods)
        {
            var methodInfos = (methods ?? Enumerable.Empty<MethodInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
                .ToDictionary(m => m.Id);

            if (methodInfos.Count == 0)
                return Generate(mainGraph);

            // Нормализуем порты
            if (mainGraph != null) NormalizePorts(mainGraph);
            foreach (var info in methodInfos.Values)
                if (info.BodyGraph != null) NormalizePorts(info.BodyGraph);

            // ── Код основного графа (0-indent) ───────────────────────────────
            var mainCode = mainGraph != null
                ? _generator.Generate(mainGraph, methodInfos)
                : null;

            // ── Собираем class Program { ... } ───────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine("class Program");
            sb.AppendLine("{");
            sb.AppendLine("    static void Main()");
            sb.AppendLine("    {");

            if (!string.IsNullOrWhiteSpace(mainCode))
            {
                // Основной код начинается с 0-indent → добавляем 2 уровня (8 пробелов)
                sb.AppendLine(IndentLines(mainCode, 2));
            }

            sb.AppendLine("    }");

            // Статические методы уровня класса (1 уровень = 4 пробела)
            foreach (var def in methodInfos.Values.Where(d => !string.IsNullOrWhiteSpace(d.Name)))
            {
                var methodCode = BuildStaticMethod(def, methodInfos);
                if (!string.IsNullOrWhiteSpace(methodCode))
                {
                    sb.AppendLine();
                    sb.AppendLine(IndentLines(methodCode, 1));
                }
            }

            sb.AppendLine("}");
            return sb.ToString().TrimEnd();
        }

        // ─── Вспомогательные ─────────────────────────────────────────────────

        /// <summary>
        /// Строит объявление статического метода уровня класса.
        /// Сигнатура без отступа; тело — с 1 уровнем отступа (как возвращает GenerateMethodBody).
        /// </summary>
        private static string BuildStaticMethod(MethodInfo def,
            IReadOnlyDictionary<string, MethodInfo> allMethods)
        {
            var paramList = string.Join(", ",
                Enumerable.Range(0, def.ParamNames.Count)
                    .Select(i =>
                    {
                        var type = (def.ParamTypes != null && i < def.ParamTypes.Count)
                            ? def.ParamTypes[i] : "object";
                        var defaultVal = (def.ParamDefaults != null && i < def.ParamDefaults.Count)
                            ? def.ParamDefaults[i] : "";
                        var param = $"{type} {def.ParamNames[i]}";
                        return string.IsNullOrWhiteSpace(defaultVal) ? param : $"{param} = {defaultVal}";
                    }));

            var returnType = string.IsNullOrEmpty(def.ReturnType) ? "void" : def.ReturnType;
            var signature  = $"public static {returnType} {def.Name}({paramList})";

            var bodyCode = _generator.GenerateMethodBody(def.BodyGraph, def, allMethods);

            return $"{signature}\n{{\n{bodyCode}\n}}";
        }

        // Псевдоним для обратной совместимости — не используется снаружи.
        private static string BuildLocalFunction(MethodInfo def,
            IReadOnlyDictionary<string, MethodInfo> allMethods)
            => BuildStaticMethod(def, allMethods);

        /// <summary>Добавляет <paramref name="levels"/>×4 пробела к каждой непустой строке кода.</summary>
        private static string IndentLines(string code, int levels)
        {
            if (string.IsNullOrEmpty(code)) return code;
            var prefix = new string(' ', levels * 4);
            var lines = code.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (i < lines.Length - 1)
                    sb.AppendLine(l.Length == 0 ? "" : prefix + l);
                else
                    sb.Append(l.Length == 0 ? "" : prefix + l);
            }
            return sb.ToString();
        }

        private static void NormalizePorts(GraphData graph)
        {
            if (graph?.Edges == null) return;
            foreach (var edge in graph.Edges)
            {
                edge.FromPort = PortIds.Normalize(edge.FromPort);
                edge.ToPort   = PortIds.Normalize(edge.ToPort);
            }
        }
    }
}
