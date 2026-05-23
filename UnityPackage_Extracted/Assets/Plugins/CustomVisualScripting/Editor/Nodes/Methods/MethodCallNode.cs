using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Methods;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода вызова пользовательского метода.
    /// Поддерживает до <see cref="MaxParams"/> входных портов (param0…param7)
    /// и один выходной порт (output, скрыт если ReturnType == "void").
    ///
    /// Имена портов задаются через [CustomPortBehavior] — GraphProcessor использует их
    /// как источник истины при создании и обновлении PortView. Это гарантирует, что
    /// "int x" никогда не будет сброшен обратно в "param0" при внутренних обновлениях.
    /// </summary>
    [Serializable]
    public class MethodCallNode : CustomBaseNode
    {
        public const int MaxParams = 8;

        // ─── Мета ─────────────────────────────────────────────────────────────
        [HideInInspector] public string MethodId;
        [HideInInspector] public string MethodName;
        [HideInInspector] public string ReturnType = "void";
        [HideInInspector] public int    ActiveParamCount;
        [HideInInspector] public string[] ParamNames = new string[MaxParams];
        [HideInInspector] public string[] ParamTypes  = new string[MaxParams];

        // ─── Порты (фиксированные поля; отображение управляется CustomPortBehavior) ──
        [Input("param0")] public object param0;
        [Input("param1")] public object param1;
        [Input("param2")] public object param2;
        [Input("param3")] public object param3;
        [Input("param4")] public object param4;
        [Input("param5")] public object param5;
        [Input("param6")] public object param6;
        [Input("param7")] public object param7;

        [Output("output")] public object output;

        // ─── NodeType ─────────────────────────────────────────────────────────
        public override NodeType NodeType => NodeType.MethodCall;

        protected override void Process() { }

        // ─── CustomPortBehavior ───────────────────────────────────────────────
        // GraphProcessor вызывает эти методы при создании и обновлении PortView.
        // Возвращаем пустой IEnumerable — порт скрыт.
        // Возвращаем один PortData с displayName — порт виден с нашим именем.

        private string GetParamLabel(int i)
        {
            string name = (ParamNames != null && i < ParamNames.Length) ? ParamNames[i] : null;
            string type = (ParamTypes  != null && i < ParamTypes.Length)  ? ParamTypes[i]  : null;
            if (string.IsNullOrEmpty(name)) name = $"param{i}";
            return string.IsNullOrEmpty(type) ? name : $"{type} {name}";
        }

        private IEnumerable<PortData> ParamPortData(int i)
        {
            if (i >= ActiveParamCount) yield break;
            yield return new PortData
            {
                displayName         = GetParamLabel(i),
                identifier          = $"param{i}",
                acceptMultipleEdges = false
            };
        }

        [CustomPortBehavior(nameof(param0))]
        IEnumerable<PortData> Param0Behavior(List<SerializableEdge> _) => ParamPortData(0);

        [CustomPortBehavior(nameof(param1))]
        IEnumerable<PortData> Param1Behavior(List<SerializableEdge> _) => ParamPortData(1);

        [CustomPortBehavior(nameof(param2))]
        IEnumerable<PortData> Param2Behavior(List<SerializableEdge> _) => ParamPortData(2);

        [CustomPortBehavior(nameof(param3))]
        IEnumerable<PortData> Param3Behavior(List<SerializableEdge> _) => ParamPortData(3);

        [CustomPortBehavior(nameof(param4))]
        IEnumerable<PortData> Param4Behavior(List<SerializableEdge> _) => ParamPortData(4);

        [CustomPortBehavior(nameof(param5))]
        IEnumerable<PortData> Param5Behavior(List<SerializableEdge> _) => ParamPortData(5);

        [CustomPortBehavior(nameof(param6))]
        IEnumerable<PortData> Param6Behavior(List<SerializableEdge> _) => ParamPortData(6);

        [CustomPortBehavior(nameof(param7))]
        IEnumerable<PortData> Param7Behavior(List<SerializableEdge> _) => ParamPortData(7);

        [CustomPortBehavior(nameof(output))]
        IEnumerable<PortData> OutputBehavior(List<SerializableEdge> _)
        {
            if (string.Equals(ReturnType, "void", StringComparison.Ordinal)) yield break;
            yield return new PortData
            {
                displayName         = "output",
                identifier          = "output",
                acceptMultipleEdges = false
            };
        }

        // ─── Serialization ────────────────────────────────────────────────────

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type         = NodeType.MethodCall;
            data.Value        = MethodId;
            data.VariableName = MethodName;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            MethodId   = data.Value ?? "";
            MethodName = data.VariableName ?? "";
            RefreshFromRegistry();
        }

        /// <summary>Обновляет мета-данные из реестра методов.</summary>
        public void RefreshFromRegistry()
        {
            var def = MethodRegistry.GetById(MethodId);
            if (def == null) return;

            MethodName       = def.Name;
            ReturnType       = def.ReturnType;
            ActiveParamCount = Mathf.Min(def.Parameters.Count, MaxParams);

            if (ParamNames == null || ParamNames.Length < MaxParams) ParamNames = new string[MaxParams];
            if (ParamTypes  == null || ParamTypes.Length  < MaxParams) ParamTypes  = new string[MaxParams];

            for (int i = 0; i < MaxParams; i++)
            {
                if (i < def.Parameters.Count)
                {
                    ParamNames[i] = def.Parameters[i].Name;
                    ParamTypes[i] = def.Parameters[i].Type;
                }
                else
                {
                    ParamNames[i] = "";
                    ParamTypes[i] = "";
                }
            }
        }
    }
}
