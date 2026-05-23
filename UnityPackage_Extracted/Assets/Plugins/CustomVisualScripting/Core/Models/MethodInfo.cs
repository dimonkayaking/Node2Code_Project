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
        public List<string> ParamNames { get; set; } = new List<string>();
        public List<string> ParamTypes { get; set; } = new List<string>();
        /// <summary>Граф тела метода (нужен только генератору).</summary>
        public GraphData BodyGraph { get; set; }
    }
}
