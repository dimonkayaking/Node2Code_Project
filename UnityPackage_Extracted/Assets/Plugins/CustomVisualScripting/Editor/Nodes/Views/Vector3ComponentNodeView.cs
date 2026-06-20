using GraphProcessor;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Unity;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    [NodeCustomEditor(typeof(Vector3ComponentNode))]
    public class Vector3ComponentNodeView : CollapsibleBodyGraphNodeView
    {
        private Vector3ComponentNode _node;

        public override void Enable()
        {
            base.Enable();

            _node = nodeTarget as Vector3ComponentNode;
            if (_node == null) return;

            if (controlsContainer == null)
            {
                controlsContainer = new VisualElement();
                controlsContainer.name = "controls";
                mainContainer.Add(controlsContainer);
            }

            var popup = new UnityEditor.UIElements.PopupField<string>(
                new System.Collections.Generic.List<string> { "x", "y", "z" },
                _node.Component);

            popup.RegisterValueChangedCallback(evt =>
            {
                _node.Component = evt.newValue;
                title = _node.name;
                // Обновляем порты (изменился label у output-порта)
                RefreshPorts();
            });

            controlsContainer.Add(LiteralRowLayout.Row("axis", popup));

            title = _node.name;
        }
    }
}
