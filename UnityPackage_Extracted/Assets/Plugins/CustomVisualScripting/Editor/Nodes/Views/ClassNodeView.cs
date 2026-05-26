using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Отображение <see cref="ClassNode"/>: зелёный заголовок + кнопка открытия вкладки класса.
    /// </summary>
    [NodeCustomEditor(typeof(ClassNode))]
    public class ClassNodeView : BaseNodeView
    {
        // Зелёный цвет — категория «Классы»
        private static readonly Color ClassHeaderColor = new Color(0.10f, 0.45f, 0.20f, 0.75f);

        private ClassNode _node;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as ClassNode;
            if (_node == null) return;

            _node.RefreshFromRegistry();

            title = string.IsNullOrWhiteSpace(_node.ClassName) ? "Class" : _node.ClassName;
            titleContainer.style.backgroundColor = ClassHeaderColor;

            // Кнопка открытия тела класса
            var editBtn = new Button(OnOpenClassClicked) { text = "✎" };
            editBtn.AddToClassList("node-subspace-link");
            editBtn.style.marginLeft  = 4;
            editBtn.style.marginRight = 2;
            titleContainer.Add(editBtn);
        }

        private void OnOpenClassClicked()
        {
            if (_node == null) return;
            var window = VisualScriptingWindow.ActiveWindow;
            window?.OpenClassTab(_node.ClassId);
        }
    }
}
