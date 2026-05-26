using System;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Classes
{
    /// <summary>
    /// Модель пользовательского класса.
    /// Содержит список методов (через ClassBodyGraph) — тело класса отображается
    /// в отдельной вкладке редактора в виде MethodOwnerNode-нод, соединённых через execIn/execOut.
    /// </summary>
    [Serializable]
    public class ClassDefinition
    {
        public string Id;
        public string Name;

        /// <summary>Граф тела класса: ноды типа <see cref="NodeType.MethodOwner"/>, соединённые цепочкой.</summary>
        public GraphData ClassBodyGraph = new();

        public ClassDefinition()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
