using System.Collections.Generic;

using VisualScripting.Core.Models;

namespace VisualScripting.Core.Parsers
{
    /// <summary>Поле класса, обнаруженное при парсинге.</summary>
    public class ParsedFieldInfo
    {
        public string Name         = "";
        public string Type         = "int";
        public string DefaultValue = "";
        /// <summary>public (true) или private (false). По умолчанию true.</summary>
        public bool   IsPublic     = true;
        /// <summary>static (true) или instance (false).</summary>
        public bool   IsStatic     = false;
    }

    /// <summary>Информация об одном классе, обнаруженном при парсинге.</summary>
    public class ParsedClassInfo
    {
        public string             Name          = "";
        /// <summary>Имя родительского класса из синтаксиса <c>: BaseClass</c>. Пусто — нет родителя.</summary>
        public string             BaseClassName = "";
        public List<string>       MethodNames   = new List<string>();
        public List<ParsedFieldInfo> Fields      = new List<ParsedFieldInfo>();
    }

    public class ParseResult
    {
        public GraphData    Graph  { get; set; } = new GraphData();
        public List<string> Errors { get; set; } = new List<string>();
        public bool HasErrors => Errors.Count > 0;

        /// <summary>
        /// Методы, обнаруженные при парсинге (inline-локальные или методы класса).
        /// Editor-слой должен добавить их в <c>MethodRegistry</c>.
        /// </summary>
        public List<MethodInfo> DiscoveredMethods { get; set; } = new List<MethodInfo>();

        /// <summary>true — код содержал class/namespace обёртку.</summary>
        public bool HasClassWrapper { get; set; }

        /// <summary>Имя класса, в котором найден метод Main.</summary>
        public string MainClassName { get; set; } = "";

        /// <summary>Список всех классов с именами их методов.</summary>
        public List<ParsedClassInfo> DiscoveredClasses { get; set; } = new List<ParsedClassInfo>();
    }
}
