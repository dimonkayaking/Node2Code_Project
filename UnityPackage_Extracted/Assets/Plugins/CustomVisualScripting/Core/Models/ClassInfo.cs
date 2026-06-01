using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    /// <summary>DTO для передачи описания класса в GeneratorBridge.</summary>
    public class ClassInfo
    {
        public string Id;
        public string Name;
        public List<ClassFieldData> Fields = new();
    }

    /// <summary>Описание статического поля класса.</summary>
    public class ClassFieldData
    {
        public string Name;
        public string Type;
        public string DefaultValue;
    }
}
