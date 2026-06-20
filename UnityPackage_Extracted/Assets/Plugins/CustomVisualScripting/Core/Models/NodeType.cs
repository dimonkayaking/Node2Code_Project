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

        // Unity API (универсальные ноды): вызов метода / чтение поля-свойства / запись поля-свойства
        UnityMethodCall,
        UnityFieldAccess,
        UnityFieldSet,

        // Разбивка Vector3 на компоненты (.x / .y / .z)
        Vector3Component,

        // Заглушка — произвольный код вставляется в генерацию as-is (неизвестный код при парсинге)
        CodeSnippet,

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
        ReturnValue,

        // Классы
        ClassNode,
        MethodOwner,

        // Поля класса
        VariableRef, // Ссылка на переменную/поле из outer scope (пасстру)
        FieldRef,   // чтение статического поля — output-порт
        FieldSet    // запись статического поля — exec-нода + value-вход
    }
}