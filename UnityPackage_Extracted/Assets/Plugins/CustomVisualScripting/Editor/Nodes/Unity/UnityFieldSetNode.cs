using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Unity
{
    /// <summary>
    /// Generic-нода записи поля/свойства встроенного Unity-класса (transform.position = ...,
    /// gameObject.SetActive не сюда — это метод). Соответствует <see cref="NodeType.UnityFieldSet"/>.
    /// Stmt-нода с exec-портами: генерирует <c>{owner}.{MemberName} = {value};</c>.
    ///
    /// Класс-владелец хранится в <see cref="ClassName"/> (NodeData.Value), имя члена — в
    /// <see cref="MemberName"/> (NodeData.MemberName), тип значения — в <see cref="FieldType"/>
    /// (NodeData.ValueType), выражение получателя (для членов экземпляра) — в
    /// <see cref="OwnerExpr"/> (NodeData.OwnerExpression).
    /// </summary>
    [Serializable, NodeMenuItem("Unity/Field Set")]
    public class UnityFieldSetNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.UnityFieldSet;

        [HideInInspector] public string ClassName  = "";
        [HideInInspector] public string MemberName = "";
        [HideInInspector] public string FieldType  = "";
        [HideInInspector] public string OwnerExpr  = "";

        [Input("value")] public object value;

        public override string name =>
            string.IsNullOrEmpty(MemberName) ? "Field Set" :
            $"Set {(string.IsNullOrEmpty(OwnerExpr) ? ClassName : OwnerExpr)}.{MemberName}";

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type = NodeType.UnityFieldSet;
            data.Value = ClassName;
            data.MemberName = MemberName;
            data.ValueType = FieldType;
            data.OwnerExpression = OwnerExpr;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            ClassName  = data.Value ?? "";
            MemberName = data.MemberName ?? "";
            FieldType  = data.ValueType ?? "";
            OwnerExpr  = data.OwnerExpression ?? "";
        }
    }
}
