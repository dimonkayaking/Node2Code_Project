using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода записи статического поля класса.
    /// Stmt-нода с exec-портами: генерирует <c>fieldName = value;</c>.
    /// Пользователь добавляет её в body-граф метода и выбирает поле через <see cref="Views.FieldSetNodeView"/>.
    /// </summary>
    [Serializable]
    public class FieldSetNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.FieldSet;

        [HideInInspector] public string FieldName = "";
        [HideInInspector] public string FieldType = "int";

        [Input("value")] public object value;

        public override string name =>
            string.IsNullOrEmpty(FieldName) ? "FieldSet" : $"Set {FieldName}";

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data          = base.ToNodeData();
            data.Type         = NodeType.FieldSet;
            data.VariableName = FieldName;
            data.ValueType    = FieldType;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            FieldName = data.VariableName ?? "";
            FieldType = data.ValueType    ?? "int";
        }
    }
}
