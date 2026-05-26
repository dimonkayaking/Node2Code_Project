using System;
using System.Collections.Generic;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Methods
{
    [Serializable]
    public class ParameterDefinition
    {
        public string Name = "param";
        public string Type = "int"; // "int" | "float" | "bool" | "string"
    }

    [Serializable]
    public class MethodDefinition
    {
        public string Id;
        public string Name;
        public string ReturnType = "void"; // "void" | "int" | "float" | "bool" | "string"

        /// <summary>Id класса-владельца. Пустая строка — метод не привязан к классу (legacy).</summary>
        public string ClassId = "";

        public List<ParameterDefinition> Parameters = new();
        public GraphData ParamGraph = new();
        public GraphData BodyGraph  = new();

        public MethodDefinition()
        {
            Id = Guid.NewGuid().ToString();
        }

        /// <summary>Возвращает сигнатуру метода для отображения.</summary>
        public string Signature()
        {
            var pars = string.Join(", ", Parameters.ConvertAll(p => $"{p.Type} {p.Name}"));
            return $"{ReturnType} {Name}({pars})";
        }
    }
}
