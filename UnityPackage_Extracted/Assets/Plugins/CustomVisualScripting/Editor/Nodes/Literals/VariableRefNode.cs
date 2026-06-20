using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Literals
{
    /// <summary>
    /// Ссылка на переменную/поле из внешнего scope (например, поле класса enemyPrefab,
    /// параметр метода и т.п.). Чистая data-нода без exec-портов.
    /// Имя переменной хранится в NodeData.ExpressionOverride; генератор подставляет его as-is.
    /// </summary>
    [Serializable, NodeMenuItem("Literals/Variable Ref")]
    public class VariableRefNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.VariableRef;

        [HideInInspector] public string RefName = "";

        [Output("output")] public object output;

        public override string name => string.IsNullOrEmpty(RefName) ? "Variable" : RefName;

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type               = NodeType.VariableRef;
            data.ExpressionOverride = RefName;
            data.ValueType          = "object";
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            RefName = data.ExpressionOverride ?? "";
        }
    }
}
