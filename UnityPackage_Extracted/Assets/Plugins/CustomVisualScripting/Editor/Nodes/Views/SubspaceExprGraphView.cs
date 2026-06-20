using UnityEditor;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// GraphView для expression-подпространств (условие if/while, init/increment цикла for).
    /// Разрешены только чистые expression-ноды: Literals, Math, Comparison, Logic, Conversion, Unity.
    /// Flow, Return, Debug, Method, Class — запрещены.
    /// </summary>
    public class SubspaceExprGraphView : FilteredCreateMenuBaseGraphView
    {
        public SubspaceExprGraphView(EditorWindow window) : base(window) { }

        public override GraphContext GraphContext => GraphContext.SubspaceExpr;

        protected override bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            return !(path.StartsWith("Literals/")   ||
                     path.StartsWith("Math/")        ||
                     path.StartsWith("Comparison/")  ||
                     path.StartsWith("Logic/")       ||
                     path.StartsWith("Conversion/")  ||
                     path.StartsWith("Unity/")        ||
                     path.StartsWith("Snippets/"));
        }
    }
}
