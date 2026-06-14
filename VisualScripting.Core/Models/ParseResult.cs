using System.Collections.Generic;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Parsers
{
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

    public class ParsedClassInfo
    {
        public string             Name          = "";
        /// <summary>Имя родительского класса из синтаксиса <c>: BaseClass</c>. Пусто — нет родителя.</summary>
        public string             BaseClassName = "";
        public List<string>       MethodNames   = new List<string>();
        public List<ParsedFieldInfo> Fields     = new List<ParsedFieldInfo>();
    }

    public class ParseResult
    {
        public GraphData    Graph  { get; set; } = new GraphData();
        public List<string> Errors { get; set; } = new List<string>();
        public bool HasErrors => Errors.Count > 0;

        public List<MethodInfo> DiscoveredMethods { get; set; } = new List<MethodInfo>();

        public bool HasClassWrapper { get; set; }
        public string MainClassName { get; set; } = "";
        public List<ParsedClassInfo> DiscoveredClasses { get; set; } = new List<ParsedClassInfo>();
    }
}
