using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    /// <summary>
    /// Bridge-DTO: метаданные пользовательского метода для передачи
    /// из Editor-слоя в Core-генератор и Core-парсер без обратной зависимости.
    /// </summary>
    public class MethodInfo
    {
        public string Id         { get; set; } = "";
        public string Name       { get; set; } = "";
        public string ReturnType { get; set; } = "void";
        /// <summary>Id класса-владельца (Editor ClassDefinition.Id). Пустая строка — метод не привязан к классу.</summary>
        public string ClassId    { get; set; } = "";
        public string ClassName  { get; set; } = "";
        public List<string> ParamNames    { get; set; } = new List<string>();
        public List<string> ParamTypes    { get; set; } = new List<string>();
        public List<string> ParamDefaults { get; set; } = new List<string>();
        /// <summary>Граф тела метода (нужен только генератору).</summary>
        public GraphData BodyGraph { get; set; }
    }
}
