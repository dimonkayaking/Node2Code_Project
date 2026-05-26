using UnityEditor;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// GraphView для тела класса.
    /// Показывает только ноды категории «Class/MethodOwner» — цепочку методов класса.
    /// Все остальные ноды скрыты.
    /// </summary>
    public class ClassBodyGraphView : FilteredCreateMenuBaseGraphView
    {
        public ClassBodyGraphView(EditorWindow window) : base(window) { }

        protected override bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Показываем только "Class/MethodOwner" из категории Class
            if (path == "Class/MethodOwner") return false;

            // Всё остальное скрыто
            return true;
        }
    }
}
