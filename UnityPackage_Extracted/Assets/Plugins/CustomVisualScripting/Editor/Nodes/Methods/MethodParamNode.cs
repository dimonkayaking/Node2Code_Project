using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода параметра метода. Живёт в верхнем (param) графе вкладки метода.
    /// Пользователь задаёт имя и тип прямо в поле графа — без создания через попап.
    /// </summary>
    [Serializable]
    [NodeMenuItem("Method/Param")]
    public class MethodParamNode : CustomBaseNode
    {
        [HideInInspector] public string ParamName     = "param";
        [HideInInspector] public string ParamType     = "int"; // int | float | bool | string
        [HideInInspector] public string DefaultValue  = "";    // необязательное значение по умолчанию

        /// <summary>Выходное значение параметра — подключается к нодам тела метода.</summary>
        [Output("value")] public object value;

        public override NodeType NodeType => NodeType.MethodParam;

        protected override void Process() { }

        // ─── Serialization ────────────────────────────────────────────────────

        public override NodeData ToNodeData()
        {
            var data                = base.ToNodeData();
            data.Type               = NodeType.MethodParam;
            data.VariableName       = ParamName;
            data.Value              = ParamType;
            data.ExpressionOverride = DefaultValue;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            ParamName    = string.IsNullOrEmpty(data.VariableName) ? "param" : data.VariableName;
            ParamType    = string.IsNullOrEmpty(data.Value)        ? "int"   : data.Value;
            DefaultValue = data.ExpressionOverride                 ?? "";
        }
    }
}
