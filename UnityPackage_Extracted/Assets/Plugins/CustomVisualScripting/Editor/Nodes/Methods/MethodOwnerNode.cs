using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Methods;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Основная нода метода — отображается в теле класса (ClassBodyGraphView).
    /// Содержит кнопку редактирования, открывающую вкладку метода.
    ///
    /// В отличие от <see cref="MethodCallNode"/> (ссылочная нода, размещаемая там, где метод вызывается),
    /// MethodOwnerNode находится внутри класса-владельца и является точкой объявления метода.
    ///
    /// Порты:
    ///   execIn  (вход)  — постоянный
    ///   execOut (выход) — постоянный
    /// </summary>
    [Serializable]
    public class MethodOwnerNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.MethodOwner;

        [HideInInspector] public string MethodId   = "";
        [HideInInspector] public string MethodName = "";

        public override string name => string.IsNullOrEmpty(MethodName) ? "Method" : MethodName;

        protected override void Process() { }

        // ─── Serialization ────────────────────────────────────────────────────

        public override NodeData ToNodeData()
        {
            var data          = base.ToNodeData();
            data.Type         = NodeType.MethodOwner;
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

        /// <summary>Обновляет имя из реестра методов.</summary>
        public void RefreshFromRegistry()
        {
            var def = MethodRegistry.GetById(MethodId);
            if (def == null) return;
            MethodName = def.Name;
        }
    }
}
