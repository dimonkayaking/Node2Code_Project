using UnityEditor;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// GraphView для тела подпространства (тело if/while/for).
    /// Разрешены execution-ноды и Method/Return; запрещены Method/Param, Class, Utils, Unity.
    /// </summary>
    public class SubspaceBodyGraphView : FilteredCreateMenuBaseGraphView
    {
        public SubspaceBodyGraphView(EditorWindow window) : base(window) { }

        public override GraphContext GraphContext => GraphContext.SubspaceBody;

        protected override bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (path == "Method/Return") return false;
            if (path.StartsWith("Method/") || path == "Method") return true;

            return path.StartsWith("Utils/") || path.StartsWith("Unity/") ||
                   path.StartsWith("Class/") || path == "Class";
        }
    }
}
