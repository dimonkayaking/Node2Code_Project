using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    [NodeCustomEditor(typeof(MethodCallNode))]
    public class MethodCallNodeView : BaseNodeView
    {
        private MethodCallNode _node;
        private Button _editBtn;

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as MethodCallNode;
            if (_node == null) return;

            // RefreshFromRegistry обновляет ActiveParamCount / ParamNames / ParamTypes.
            // [CustomPortBehavior]-методы на MethodCallNode уже прочитали эти данные при
            // создании PortView — здесь мы вызываем его только для заголовка и кнопки.
            _node.RefreshFromRegistry();

            title = _node.name;  // "ClassName.MethodName" или просто "MethodName"

            // ── Цвет заголовка (циановый — цвет категории Методы) ────────────
            titleContainer.style.backgroundColor = new Color(0f, 0.47f, 0.53f, 0.55f);

            // ── Кнопка редактирования в заголовке ─────────────────────────────
            _editBtn = new Button(OnEditClicked) { text = "✎" };
            _editBtn.AddToClassList("node-subspace-link");
            _editBtn.style.marginLeft  = 4;
            _editBtn.style.marginRight = 2;
            titleContainer.Add(_editBtn);
        }

        private void OnEditClicked()
        {
            if (_node == null) return;
            var window = VisualScriptingWindow.ActiveWindow;
            if (window != null)
                window.OpenMethodTab(_node.MethodId);
        }
    }
}
