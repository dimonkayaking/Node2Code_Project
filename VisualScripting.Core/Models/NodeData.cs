using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    public class NodeData
    {
        public string Id { get; set; } = "";
        public NodeType Type { get; set; }
        public string Value { get; set; } = "";
        public string ValueType { get; set; } = "";
        public string VariableName { get; set; } = "";
        public Dictionary<string, string> InputConnections { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> ExecutionFlow { get; set; } = new Dictionary<string, string>();

        public GraphData? ConditionSubGraph { get; set; }
        public GraphData? BodySubGraph { get; set; }
        public GraphData? InitSubGraph { get; set; }
        public GraphData? IncrementSubGraph { get; set; }

        /// <summary>
        /// Stores the original source expression text (e.g. "x + y") for robust code generation
        /// when the node graph edges may not be fully preserved (e.g. after editor round-trip).
        /// </summary>
        public string ExpressionOverride { get; set; } = "";

        /// <summary>
        /// Имя члена встроенного Unity-класса (метод или поле/свойство) для нод
        /// UnityMethodCall / UnityFieldAccess / UnityFieldSet, например "Clamp" или "position".
        /// Класс-владелец хранится в <see cref="Value"/> (например "Mathf", "Vector3", "Transform").
        /// </summary>
        public string MemberName { get; set; } = "";

        /// <summary>
        /// Выражение объекта-получателя для нод экземплярных членов Unity API
        /// (например "transform", "gameObject"). Пусто для статических членов —
        /// в этом случае в качестве префикса используется <see cref="Value"/> (имя класса).
        /// </summary>
        public string OwnerExpression { get; set; } = "";
    }
}