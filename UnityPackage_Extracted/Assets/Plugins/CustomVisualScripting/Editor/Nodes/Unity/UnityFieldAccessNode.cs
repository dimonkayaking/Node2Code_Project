using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Unity
{
    /// <summary>
    /// Generic-нода чтения поля/свойства встроенного Unity-класса (Vector3.zero, Time.deltaTime,
    /// transform.position, ...). Соответствует <see cref="NodeType.UnityFieldAccess"/>.
    ///
    /// Класс-владелец хранится в <see cref="ClassName"/> (NodeData.Value), имя члена — в
    /// <see cref="MemberName"/> (NodeData.MemberName), тип результата — в <see cref="FieldType"/>
    /// (NodeData.ValueType). Для членов экземпляра (transform.position) выражение получателя
    /// хранится в <see cref="OwnerExpr"/> (NodeData.OwnerExpression); для статических членов
    /// (Vector3.zero) оно пусто и в генераторе используется ClassName.
    /// </summary>
    [Serializable, NodeMenuItem("Unity/Field Access")]
    public class UnityFieldAccessNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.UnityFieldAccess;

        [HideInInspector] public string ClassName  = "";
        [HideInInspector] public string MemberName = "";
        [HideInInspector] public string FieldType  = "";
        [HideInInspector] public string OwnerExpr  = "";

        [Output("output")] public object output;

        public override string name =>
            string.IsNullOrEmpty(MemberName) ? "Field Access" :
            $"{(string.IsNullOrEmpty(OwnerExpr) ? ClassName : OwnerExpr)}.{MemberName}";

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type = NodeType.UnityFieldAccess;
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
