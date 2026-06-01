using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Classes;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода класса на главном графе.
    /// Чисто декларативная — без exec-портов.
    /// ClassNodeView отображает поля и методы класса прямо внутри ноды.
    /// </summary>
    [Serializable, NodeMenuItem("Class/Class")]
    public class ClassNode : CustomBaseNode
    {
        public override NodeType NodeType => NodeType.ClassNode;

        [HideInInspector] public string ClassId   = "";
        [HideInInspector] public string ClassName = "";

        public override string name => string.IsNullOrEmpty(ClassName) ? "Class" : ClassName;

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data          = base.ToNodeData();
            data.Type         = NodeType.ClassNode;
            data.Value        = ClassId;
            data.VariableName = ClassName;
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            ClassId   = data.Value        ?? "";
            ClassName = data.VariableName ?? "";
            RefreshFromRegistry();
        }

        public void RefreshFromRegistry()
        {
            var def = ClassRegistry.GetById(ClassId);
            if (def == null) return;
            ClassName = def.Name;
        }
    }
}
