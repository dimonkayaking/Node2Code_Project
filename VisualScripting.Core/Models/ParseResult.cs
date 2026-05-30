using System.Collections.Generic;
using VisualScripting.Core.Models;

namespace VisualScripting.Core.Parsers
{
    public class ParseResult
    {
        public GraphData Graph { get; set; } = new GraphData();
        public List<string> Errors { get; set; } = new List<string>();
        public bool HasErrors => Errors.Count > 0;

        /// <summary>
        /// Методы, обнаруженные при парсинге как inline-локальные функции / class-методы.
        /// Editor-слой должен добавить их в MethodRegistry.
        /// </summary>
        public List<MethodInfo> DiscoveredMethods { get; set; } = new List<MethodInfo>();
    }
}