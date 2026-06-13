using System;
using System.Collections.Generic;
using UnityEngine;
using GraphProcessor;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Nodes.Base
{
    [Serializable]
    public abstract class CustomBaseNode : BaseNode
    {
        [HideInInspector]
        public string NodeId;

        [HideInInspector]
        public string variableName = "";

        public abstract NodeType NodeType { get; }

        protected override void Enable()
        {
            base.Enable();
            
            if (string.IsNullOrEmpty(GUID))
            {
                OnNodeCreated();
            }
            
            if (string.IsNullOrEmpty(NodeId))
            {
                NodeId = GUID;
            }
        }

        public void SetGUID(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            GUID = guid;
        }

        public virtual void InitializeFromData(NodeData data)
        {
            NodeId = data.Id;
            variableName = data.VariableName ?? "";
            SetGUID(NodeId);
        }

        public virtual NodeData ToNodeData()
        {
            return new NodeData
            {
                Id = NodeId,
                Type = NodeType,
                Value = "",
                ValueType = "",
                VariableName = variableName ?? "",
                InputConnections = new Dictionary<string, string>(),
                ExecutionFlow = new Dictionary<string, string>()
            };
        }

    }
}