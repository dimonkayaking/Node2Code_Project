using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Возвращает true, если данная нода должна отображать exec-порты (является корневым инструкционным узлом).
        ///
        /// Нода с variableName является корневым оператором, КРОМЕ случая когда её data-выход
        /// ведёт ИСКЛЮЧИТЕЛЬНО в порт inputB бинарной операции — тогда она является "вторичным"
        /// операндом, а "первичный" (inputA) берёт на себя роль exec-точки входа для группы.
        ///
        /// Примеры:
        ///   x (→ Add.inputA) → показывает exec-порты  (primary operand)
        ///   y (→ Add.inputB) → НЕ показывает exec-порты (secondary operand)
        ///   z (← Add.output, variableName="z") → показывает exec-порты (result statement)
        /// </summary>
        protected bool IsStatementRootNode()
        {
            if (string.IsNullOrEmpty(variableName))
                return false;

            if (graph == null)
                return true;

            // Собираем рёбра, идущие из нашего data-порта "output"
            var dataOutputEdges = graph.edges
                .Where(e => e.outputNode == this && e.outputPortIdentifier == "output")
                .ToList();

            // Нет исходящих data-рёбер → нода стоит сама по себе, показываем exec-порты
            if (dataOutputEdges.Count == 0)
                return true;

            // Если хотя бы одно ребро ведёт НЕ в inputB → нода является primary-операндом
            // или ведёт в "условие", "значение" и т.п. → показываем exec-порты
            return dataOutputEdges.Any(e => e.inputPortIdentifier != "inputB");
        }
    }
}