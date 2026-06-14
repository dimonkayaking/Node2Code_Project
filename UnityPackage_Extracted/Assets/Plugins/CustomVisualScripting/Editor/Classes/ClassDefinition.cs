using System;
using System.Collections.Generic;

namespace CustomVisualScripting.Editor.Classes
{
    [Serializable]
    public class FieldDefinition
    {
        public string Id           = "";
        public string Name         = "myField";
        public string Type         = "int";   // "int" | "float" | "bool" | "string"
        public string DefaultValue = "";      // пустая строка → без инициализатора

        /// <summary>public (true) или private (false).</summary>
        public bool IsPublic = true;
        /// <summary>static (true) или instance/нестатическое (false).</summary>
        public bool IsStatic = false;

        public FieldDefinition()
        {
            Id = Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Модель пользовательского класса.
    /// Методы класса хранятся в <see cref="MethodRegistry"/> — каждый
    /// <see cref="Methods.MethodDefinition"/> ссылается на класс через <c>ClassId</c>.
    /// </summary>
    [Serializable]
    public class ClassDefinition
    {
        public string Id;
        public string Name;

        /// <summary>Id родительского класса (ClassDefinition.Id). Пусто — нет родителя.</summary>
        public string BaseClassId = "";

        /// <summary>Наследуется ли класс от MonoBehaviour. true по умолчанию; игнорируется при наличии BaseClassId.</summary>
        public bool InheritsMonoBehaviour = true;

        /// <summary>Статические поля класса.</summary>
        public List<FieldDefinition> Fields = new();

        public ClassDefinition()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
