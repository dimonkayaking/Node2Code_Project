using System;
using GraphProcessor;
using UnityEngine;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Snippets
{
    /// <summary>
    /// Нода-заглушка: хранит произвольный фрагмент C#-кода и вставляет его as-is при генерации.
    /// Используется как fallback для конструкций, которые парсер не распознал автоматически,
    /// а также для ручного ввода любого кода в граф.
    /// </summary>
    [Serializable, NodeMenuItem("Snippets/Code Snippet")]
    public class CodeSnippetNode : BaseExecutionNode
    {
        public override NodeType NodeType => NodeType.CodeSnippet;

        [HideInInspector]
        public string SnippetCode = "";

        public override string name => "Code Snippet";

        protected override void Process() { }

        public override NodeData ToNodeData()
        {
            var data   = base.ToNodeData();
            data.Type  = NodeType.CodeSnippet;
            data.Value = SnippetCode ?? "";
            return data;
        }

        public override void InitializeFromData(NodeData data)
        {
            base.InitializeFromData(data);
            SnippetCode = data.Value ?? "";
        }
    }
}
