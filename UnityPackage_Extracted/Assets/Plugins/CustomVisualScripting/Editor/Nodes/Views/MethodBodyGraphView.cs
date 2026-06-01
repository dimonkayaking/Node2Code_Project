using UnityEditor;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// GraphView для тела пользовательского метода.
    /// В отличие от базового <see cref="FilteredCreateMenuBaseGraphView"/>, показывает
    /// ноды категории «Method/Return», но скрывает «Method/Param» и служебные Utils/Unity.
    /// </summary>
    public class MethodBodyGraphView : FilteredCreateMenuBaseGraphView
    {
        public MethodBodyGraphView(EditorWindow window) : base(window) { }

        public override GraphContext GraphContext => GraphContext.MethodBody;

        protected override bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Показываем только "Method/Return" из категории Method
            if (path == "Method/Return") return false;

            // Всё остальное под Method/ (в т.ч. Method/Param) — скрываем
            if (path.StartsWith("Method/") || path == "Method") return true;

            // Служебные и Unity-ноды всегда скрыты
            return path.StartsWith("Utils/") || path.StartsWith("Unity/");
        }
    }
}
