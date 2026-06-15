using System;
using System.Collections.Generic;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Unity
{
    /// <summary>
    /// Generic-нода вызова метода встроенного Unity-класса (Vector3.MoveTowards, Mathf.Clamp,
    /// transform.Translate, ...). Соответствует <see cref="NodeType.UnityMethodCall"/>.
    ///
    /// Поддерживает до <see cref="MaxParams"/> входных портов (param0..param3) и один выходной
    /// порт (output, скрыт если ReturnType == "void") — по аналогии с <see cref="Methods.MethodCallNode"/>,
    /// но метаданные (имя/тип параметров, тип результата) берутся из
    /// <see cref="UnityLibraryRegistry"/> по <see cref="ClassName"/>/<see cref="MemberName"/>.
    ///
    /// Класс-владелец хранится в NodeData.Value, имя члена — в NodeData.MemberName, тип
    /// результата — в NodeData.ValueType, выражение получателя (для членов экземпляра) — в
    /// NodeData.OwnerExpression, присваиваемая переменная — в NodeData.VariableName.
    /// </summary>
    [Serializable, NodeMenuItem("Unity/Method Call")]
    public class UnityMethodCallNode : BaseExecutionNode
    {
        public const int MaxParams = 4;

        public override NodeType NodeType => NodeType.UnityMethodCall;

        [HideInInspector] public string ClassName  = "";
        [HideInInspector] public string MemberName = "";
        [HideInInspector] public string ReturnType = "void";
        [HideInInspector] public string OwnerExpr  = "";
        [HideInInspector] public int    ActiveParamCount;
        [HideInInspector] public string[] ParamNames = new string[MaxParams];
        [HideInInspector] public string[] ParamTypes  = new string[MaxParams];

        [Input("param0")] public object param0;
        [Input("param1")] public object param1;
        [Input("param2")] public object param2;
        [Input("param3")] public object param3;

        [Output("output")] public object output;

        public override string name =>
            string.IsNullOrEmpty(MemberName) ? "Unity Method Call" :
            $"{(string.IsNullOrEmpty(OwnerExpr) ? ClassName : OwnerExpr)}.{MemberName}";

        protected override void Process() { }

        private string GetParamLabel(int i)
        {
            string n = (ParamNames != null && i < ParamNames.Length) ? ParamNames[i] : null;
            string t = (ParamTypes  != null && i < ParamTypes.Length)  ? ParamTypes[i]  : null;
            if (string.IsNullOrEmpty(n)) n = $"param{i}";
            return string.IsNullOrEmpty(t) ? n : $"{t} {n}";
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

        public override NodeData ToNodeData()
        {
            var data = base.ToNodeData();
            data.Type = NodeType.UnityMethodCall;
            data.Value = ClassName;
            data.MemberName = MemberName;
            data.ValueType = ReturnType;
            data.OwnerExpression = OwnerExpr;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            ClassName  = data.Value ?? "";
            MemberName = data.MemberName ?? "";
            ReturnType = data.ValueType ?? "void";
            OwnerExpr  = data.OwnerExpression ?? "";
            RefreshFromRegistry();
        }

        /// <summary>Обновляет ActiveParamCount/ParamNames/ParamTypes/ReturnType из реестра Unity API.</summary>
        public void RefreshFromRegistry()
        {
            var member = UnityLibraryRegistry.FindMethod(ClassName, MemberName);
            if (member == null) return;

            ReturnType       = member.ReturnType;
            ActiveParamCount = Mathf.Min(member.Parameters.Count, MaxParams);

            if (ParamNames == null || ParamNames.Length < MaxParams) ParamNames = new string[MaxParams];
            if (ParamTypes  == null || ParamTypes.Length  < MaxParams) ParamTypes  = new string[MaxParams];

            for (int i = 0; i < MaxParams; i++)
            {
                if (i < member.Parameters.Count)
                {
                    ParamNames[i] = member.Parameters[i].Name;
                    ParamTypes[i] = member.Parameters[i].Type;
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
