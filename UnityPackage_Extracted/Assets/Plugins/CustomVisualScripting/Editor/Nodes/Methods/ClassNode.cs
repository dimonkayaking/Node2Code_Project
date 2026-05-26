using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Classes;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода класса. Отображается на главном графе как точка входа в класс.
    ///
    /// Порты:
    ///   execIn  (вход)  — постоянный, от BaseExecutionNode
    ///   execOut (выход) — постоянный, от BaseExecutionNode
    ///   methods (выход) — соединяется с первым MethodOwnerNode в цепочке методов
    ///
    /// Двойной клик / кнопка «✎» открывает вкладку тела класса (ClassBodyGraphView).
    /// </summary>
    [Serializable, NodeMenuItem("Class/Class")]
    public class ClassNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.ClassNode;

        [HideInInspector] public string ClassId   = "";
        [HideInInspector] public string ClassName = "";

        /// <summary>Выход для подключения к цепочке MethodOwnerNode внутри класса.</summary>
        [Output("methods")]
        public object methods;

        public override string name => string.IsNullOrEmpty(ClassName) ? "Class" : ClassName;

        protected override void Process() { }

        // ─── Serialization ────────────────────────────────────────────────────

        public override NodeData ToNodeData()
        {
            var data        = base.ToNodeData();
            data.Type       = NodeType.ClassNode;
            data.Value      = ClassId;
            data.VariableName = ClassName;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            ClassId   = data.Value ?? "";
            ClassName = data.VariableName ?? "";
            RefreshFromRegistry();
        }

        /// <summary>Обновляет имя из реестра классов.</summary>
        public void RefreshFromRegistry()
        {
            var def = ClassRegistry.GetById(ClassId);
            if (def == null) return;
            ClassName = def.Name;
        }
    }
}
