using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    /// <summary>DTO для передачи описания класса в GeneratorBridge.</summary>
    public class ClassInfo
    {
        public string Id;
        public string Name;
        /// <summary>Имя родительского класса. Пусто — нет родителя.</summary>
        public string BaseName = "";
        public List<ClassFieldData> Fields = new();
    }

    /// <summary>Описание поля класса.</summary>
    public class ClassFieldData
    {
        public string Name;
        public string Type;
        public string DefaultValue;
        /// <summary>public (true) или private (false).</summary>
        public bool IsPublic = true;
        /// <summary>static (true) или instance (false).</summary>
        public bool IsStatic = false;
    }
}
