using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    public class GraphData
    {
        public List<NodeData> Nodes { get; set; } = new List<NodeData>();
        public List<EdgeData> Edges { get; set; } = new List<EdgeData>();
        public List<VisualLayoutEntry> VisualLayout { get; set; } = new List<VisualLayoutEntry>();
    }

    public class EdgeData
    {
        public string FromNodeId { get; set; } = "";
        public string FromPort { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public string ToPort { get; set; } = "";
    }
}