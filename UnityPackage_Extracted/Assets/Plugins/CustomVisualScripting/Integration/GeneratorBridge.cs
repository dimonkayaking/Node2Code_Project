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
        /// Генерирует полный код: сначала объявления методов (как локальные static-функции),
        /// затем код основного графа.
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

            var sb = new StringBuilder();

            // Объявления методов как локальных static-функций (допустимо в C# 8+)
            foreach (var def in methodInfos.Values.Where(d => !string.IsNullOrWhiteSpace(d.Name)))
            {
                var methodCode = BuildLocalFunction(def, methodInfos);
                if (!string.IsNullOrWhiteSpace(methodCode))
                {
                    sb.AppendLine(methodCode);
                    sb.AppendLine();
                }
            }

            // Код основного графа
            if (mainGraph != null)
            {
                var mainCode = _generator.Generate(mainGraph, methodInfos);
                if (!string.IsNullOrWhiteSpace(mainCode))
                    sb.Append(mainCode);
            }

            return sb.ToString().TrimEnd();
        }

        // ─── Вспомогательные ─────────────────────────────────────────────────

        /// <summary>Строит объявление локальной статической функции.</summary>
        private static string BuildLocalFunction(MethodInfo def,
            IReadOnlyDictionary<string, MethodInfo> allMethods)
        {
            var paramList = string.Join(", ",
                Enumerable.Range(0, def.ParamNames.Count)
                    .Select(i =>
                    {
                        var type = (def.ParamTypes != null && i < def.ParamTypes.Count)
                            ? def.ParamTypes[i] : "object";
                        return $"{type} {def.ParamNames[i]}";
                    }));

            var returnType = string.IsNullOrEmpty(def.ReturnType) ? "void" : def.ReturnType;
            var signature  = $"static {returnType} {def.Name}({paramList})";

            var bodyCode = _generator.GenerateMethodBody(def.BodyGraph, def, allMethods);

            return $"{signature}\n{{\n{bodyCode}\n}}";
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
