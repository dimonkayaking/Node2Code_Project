using System;
using GraphProcessor;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Methods
{
    /// <summary>
    /// Нода возврата значения из пользовательского метода.
    /// Имеет exec-in порт (встраивается в цепочку выполнения) и опциональный
    /// data-in порт «value» (подключается возвращаемое выражение).
    /// Не имеет exec-out: завершает ветку выполнения.
    /// </summary>
    [Serializable, NodeMenuItem("Method/Return")]
    public class ReturnNode : BaseFlowNode
    {
        public override NodeType NodeType => NodeType.ReturnValue;

        /// <summary>Возвращаемое значение (опционально; для void-методов не подключается).</summary>
        [Input("value")]
        public object value;

        public override string name => "Return";

        protected override void Process() { }
    }
}
