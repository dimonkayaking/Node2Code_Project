using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Comparison;
using CustomVisualScripting.Editor.Nodes.Conversion;
using CustomVisualScripting.Editor.Nodes.Debug;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Nodes.Literals;
using CustomVisualScripting.Editor.Nodes.Logic;
using CustomVisualScripting.Editor.Nodes.Math;
using CustomVisualScripting.Editor.Nodes.Unity;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor
{
    public enum EditorNodeFactoryContext
    {
        Default,
        /// <summary>Condition sub-panel: no nested FlowIf/Else/For/While nodes.</summary>
        SubGraphConditionPanel
    }

    /// <summary>
    /// Creates editor node instances from <see cref="NodeData.Type"/> (same mapping as the main graph window).
    /// </summary>
    public static class EditorNodeFactory
    {
        public static CustomBaseNode Create(NodeData data, EditorNodeFactoryContext context = EditorNodeFactoryContext.Default)
        {
            if (data == null)
                return null;

            if (context == EditorNodeFactoryContext.SubGraphConditionPanel &&
                data.Type is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile)
                return null;

            return CreateUnchecked(data);
        }

        private static CustomBaseNode CreateUnchecked(NodeData data)
        {
            switch (data.Type)
            {
                case NodeType.LiteralInt: return new IntNode();
                case NodeType.LiteralFloat: return new FloatNode();
                case NodeType.LiteralBool: return new BoolNode();
                case NodeType.LiteralString: return new StringNode();
                case NodeType.MathAdd: return new AddNode();
                case NodeType.MathSubtract: return new SubtractNode();
                case NodeType.MathMultiply: return new MultiplyNode();
                case NodeType.MathDivide: return new DivideNode();
                case NodeType.MathModulo: return new ModuloNode();
                case NodeType.CompareEqual: return new EqualNode();
                case NodeType.CompareNotEqual: return new NotEqualNode();
                case NodeType.CompareGreater: return new GreaterNode();
                case NodeType.CompareGreaterOrEqual: return new GreaterOrEqualNode();
                case NodeType.CompareLess: return new LessNode();
                case NodeType.CompareLessOrEqual: return new LessOrEqualNode();
                case NodeType.LogicalAnd: return new AndNode();
                case NodeType.LogicalOr: return new OrNode();
                case NodeType.LogicalNot: return new NotNode();
                case NodeType.FlowIf: return new IfNode();
                case NodeType.FlowElse: return new ElseNode();
                case NodeType.FlowFor: return new ForNode();
                case NodeType.FlowWhile: return new WhileNode();
                case NodeType.ConsoleWriteLine: return new ConsoleWriteLineNode();
                case NodeType.DebugLog: return new DebugLogNode();
                case NodeType.IntParse: return new IntParseNode();
                case NodeType.FloatParse: return new FloatParseNode();
                case NodeType.ToStringConvert: return new ToStringNode();
                case NodeType.MathfAbs: return new MathfAbsNode();
                case NodeType.MathfMax: return new MathfMaxNode();
                case NodeType.MathfMin: return new MathfMinNode();
                case NodeType.UnityVector3: return new Vector3CreateNode();
                case NodeType.UnityGetPosition: return new GetPositionNode();
                case NodeType.UnitySetPosition: return new SetPositionNode();
                default: return null;
            }
        }
    }
}
