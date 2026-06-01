using UnityEditor;
using UnityEngine.UIElements;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// GraphView главного графа.
    /// Разрешено создавать только ClassNode (Class/Class).
    /// ПКМ-меню полностью отключено — ClassNode добавляются через правую панель.
    /// </summary>
    public class MainClassGraphView : FilteredCreateMenuBaseGraphView
    {
        public MainClassGraphView(EditorWindow window) : base(window) { }

        public override GraphContext GraphContext => GraphContext.Main;

        protected override bool ShouldHideMenuPath(string path) =>
            path != "Class/Class";

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // В главном графе ноды добавляются только через правую панель — RMB-меню пустое
        }
    }
}
