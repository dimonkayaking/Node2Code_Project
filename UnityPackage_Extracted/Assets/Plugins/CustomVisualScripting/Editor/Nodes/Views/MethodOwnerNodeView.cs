using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Отображение <see cref="MethodOwnerNode"/>: циановый заголовок + кнопка открытия вкладки метода.
    /// Используется внутри ClassBodyGraphView (тело класса).
    /// </summary>
    [NodeCustomEditor(typeof(MethodOwnerNode))]
    public class MethodOwnerNodeView : BaseNodeView
    {
        // Тот же циановый цвет, что и у MethodCallNodeView — принадлежность к категории «Методы»
        private static readonly Color MethodHeaderColor = new Color(0f, 0.47f, 0.53f, 0.55f);

        private MethodOwnerNode _node;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as MethodOwnerNode;
            if (_node == null) return;

            _node.RefreshFromRegistry();

            title = string.IsNullOrWhiteSpace(_node.MethodName) ? "Method" : _node.MethodName;
            titleContainer.style.backgroundColor = MethodHeaderColor;

            // Кнопка открытия вкладки метода (тело + параметры)
            var editBtn = new Button(OnEditClicked) { text = "✎" };
            editBtn.AddToClassList("node-subspace-link");
            editBtn.style.marginLeft  = 4;
            editBtn.style.marginRight = 2;
            titleContainer.Add(editBtn);
        }

        private void OnEditClicked()
        {
            if (_node == null) return;
            var window = VisualScriptingWindow.ActiveWindow;
            window?.OpenMethodTab(_node.MethodId);
        }
    }
}
