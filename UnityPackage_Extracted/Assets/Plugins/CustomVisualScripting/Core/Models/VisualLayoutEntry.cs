namespace VisualScripting.Core.Models
{
    /// <summary>
    /// Persisted top-left position for a node within a <see cref="GraphData"/> (nested subgraph layout).
    /// </summary>
    public class VisualLayoutEntry
    {
        public string NodeId { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }
}
