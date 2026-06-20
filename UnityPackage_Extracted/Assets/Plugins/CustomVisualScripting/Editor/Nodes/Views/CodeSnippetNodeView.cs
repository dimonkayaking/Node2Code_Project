using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Snippets;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Вьюшка <see cref="CodeSnippetNode"/>: многострочное поле для редактирования кода-заглушки.
    /// </summary>
    [NodeCustomEditor(typeof(CodeSnippetNode))]
    public class CodeSnippetNodeView : CollapsibleBodyGraphNodeView
    {
        private CodeSnippetNode _node;
        private TextField _codeField;

        public override (float minW, float minH) GetLiteralBoundsMins() =>
            IsBodyExpanded ? (320f, 130f) : (220f, 52f);

        public override void Enable()
        {
            base.Enable();

            _node = nodeTarget as CodeSnippetNode;
            if (_node == null) return;

            if (controlsContainer == null)
            {
                controlsContainer = new VisualElement();
                controlsContainer.name = "controls";
                mainContainer.Add(controlsContainer);
            }

            controlsContainer.style.flexDirection = FlexDirection.Column;
            controlsContainer.style.alignSelf     = Align.Stretch;
            controlsContainer.style.paddingLeft   = 6;
            controlsContainer.style.paddingRight  = 6;
            controlsContainer.style.paddingTop    = 4;

            // Метка
            var label = new Label("code");
            label.style.fontSize  = 11;
            label.style.color     = new Color(0.82f, 0.82f, 0.82f);
            label.style.marginBottom = 2;
            controlsContainer.Add(label);

            // Многострочный TextField
            _codeField = new TextField
            {
                multiline = true,
                value     = _node.SnippetCode ?? ""
            };
            _codeField.style.flexGrow   = 1;
            _codeField.style.minHeight  = 64;
            _codeField.style.whiteSpace = WhiteSpace.Normal;

            // Шрифт кода (моноширинный через Unity-дефолт — лучший вариант без кастомного шрифта)
            _codeField.style.fontSize = 11;

            _codeField.RegisterValueChangedCallback(evt =>
            {
                _node.SnippetCode = evt.newValue ?? "";
            });

            controlsContainer.Add(_codeField);

            FinishLiteralBodySetup();
        }
    }
}
