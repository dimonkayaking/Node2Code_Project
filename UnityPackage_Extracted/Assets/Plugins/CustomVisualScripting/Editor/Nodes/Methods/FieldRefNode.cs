using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода поля класса — одновременно читает и записывает статическое поле.
    ///
    /// Режим чтения:  подключить Output → получить значение поля.
    /// Режим записи:  подключить ExecIn/ExecOut + Input → присвоить полю новое значение.
    ///
    /// Авто-инжектируется в body-граф метода через SyncBodyFieldReferences.
    /// Stable ID: <c>"_fieldref_" + FieldId</c>.
    /// </summary>
    [Serializable, NodeMenuItem("Class/FieldRef")]
    public class FieldRefNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.FieldRef;

        [HideInInspector] public string FieldId      = "";
        [HideInInspector] public string FieldName    = "";
        [HideInInspector] public string FieldType    = "int";

        /// <summary>Входное значение для присвоения (режим записи).</summary>
        [Input("value")] public object value;

        /// <summary>Текущее значение поля (режим чтения).</summary>
        [Output("output")] public object output;

        public override string name =>
            string.IsNullOrEmpty(FieldName) ? "Поле" : $"Поле: {FieldName}";

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data          = base.ToNodeData();
            data.Type         = NodeType.FieldRef;
            data.Value        = FieldId;
            data.VariableName = FieldName;
            data.ValueType    = FieldType;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            FieldId   = data.Value        ?? "";
            FieldName = data.VariableName ?? "";
            FieldType = data.ValueType    ?? "int";
        }
    }
}
