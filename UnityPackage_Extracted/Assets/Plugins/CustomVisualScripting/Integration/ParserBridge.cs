using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;

namespace CustomVisualScripting.Integration
{
    public static class ParserBridge
    {
        private static readonly RoslynCodeParser _parser = new RoslynCodeParser();

        public static void Initialize()
        {
            Debug.Log("[VS] ParserBridge инициализирован");
        }

        // ─── Основной API ─────────────────────────────────────────────────────

        /// <summary>Парсинг без учёта пользовательских методов.</summary>
        public static ParseResult Parse(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                Debug.LogError("[VS] Код пуст");
                return new ParseResult
                {
                    Errors = new List<string> { "Код пуст" }
                };
            }

            _parser.SetKnownMethods(System.Array.Empty<MethodInfo>());
            var result = _parser.Parse(code);
            NormalizePorts(result.Graph);
            LogResult(result);
            return result;
        }

        /// <summary>
        /// Парсинг с учётом пользовательских методов: вызовы зарегистрированных методов
        /// распознаются и создают <c>NodeType.MethodCall</c> ноды в графе.
        /// Конвертация MethodDefinition → MethodInfo выполняется на стороне вызывающего (Editor-слой).
        /// </summary>
        public static ParseResult ParseWithMethods(string code, IEnumerable<MethodInfo> methods)
        {
            if (string.IsNullOrEmpty(code))
            {
                Debug.LogError("[VS] Код пуст");
                return new ParseResult { Errors = new List<string> { "Код пуст" } };
            }

            var methodInfos = (methods ?? Enumerable.Empty<MethodInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .ToArray();

            _parser.SetKnownMethods(methodInfos);
            var result = _parser.Parse(code);
            _parser.SetKnownMethods(System.Array.Empty<MethodInfo>()); // сбрасываем
            NormalizePorts(result.Graph);
            LogResult(result);
            return result;
        }

        // ─── Вспомогательные ─────────────────────────────────────────────────

        private static void NormalizePorts(GraphData graph)
        {
            if (graph?.Edges == null) return;
            foreach (var edge in graph.Edges)
            {
                edge.FromPort = PortIds.Normalize(edge.FromPort);
                edge.ToPort   = PortIds.Normalize(edge.ToPort);
            }
        }

        private static void LogResult(ParseResult result)
        {
            if (result.HasErrors)
                Debug.LogWarning(
                    $"[VS] Parse: ошибок={result.Errors.Count}\n" +
                    string.Join("\n", result.Errors.Select((e, i) => $"  [{i + 1}] {e}")));
            else
                Debug.Log(
                    $"[VS] Parse OK: Nodes={result.Graph.Nodes.Count}, Edges={result.Graph.Edges.Count}");
        }
    }
}
