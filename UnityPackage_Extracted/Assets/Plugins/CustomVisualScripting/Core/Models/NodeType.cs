namespace VisualScripting.Core.Models
{
    public enum NodeType
    {
        // Литералы
        LiteralBool,
        LiteralInt,
        LiteralFloat,
        LiteralString,

        // Математические операции
        MathAdd,
        MathSubtract,
        MathMultiply,
        MathDivide,
        MathModulo,

        // Сравнения
        CompareEqual,
        CompareGreater,
        CompareLess,
        CompareNotEqual,
        CompareGreaterOrEqual,
        CompareLessOrEqual,

        // Логические операции
        LogicalAnd,
        LogicalOr,
        LogicalNot,

        // Flow
        FlowIf,
        FlowElse,
        FlowFor,
        FlowWhile,
        ConsoleWriteLine,

        // Debug
        DebugLog,

        // Unity
        UnityGetPosition,
        UnitySetPosition,
        UnityVector3,

        // Конвертация
        IntParse,
        FloatParse,
        ToStringConvert,

        // Mathf
        MathfAbs,
        MathfMax,
        MathfMin,

        // Пользовательские методы
        MethodCall,
        MethodParam,
        ReturnValue
    }
}