using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace CustomVisualScripting.Windows.Views
{
    public class ConsoleView : VisualElement
    {
        // ── Данные ────────────────────────────────────────────────────────────
        private readonly StringBuilder _plainSb = new StringBuilder();  // для «Copy All»
        private readonly StringBuilder _richSb  = new StringBuilder();  // для отображения

        // ── UI ────────────────────────────────────────────────────────────────
        private TextField     _textArea;
        private ScrollView    _scrollView;
        private Button        _toggleButton;
        private bool          _isVisible = true;

        // ── Resize-state ──────────────────────────────────────────────────────
        private bool  _isDragging;
        private float _dragStartY;
        private float _dragStartHeight;
        private const float MinH     = 60f;
        private const float MaxH     = 800f;
        private const float DefaultH = 140f;

        public ConsoleView()
        {
            style.backgroundColor = new Color(0.10f, 0.10f, 0.10f);
            style.borderTopWidth  = 1;
            style.borderTopColor  = new Color(0.15f, 0.15f, 0.15f);

            BuildDragHandle();
            BuildHeader();
            BuildTextArea();
        }

        // ────────────────────────────────────────────────────────────────────
        // Публичный API
        // ────────────────────────────────────────────────────────────────────

        public void AddMessage(string message, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error or LogType.Exception => "[ERR]",
                LogType.Warning                    => "[WRN]",
                _                                  => "[LOG]"
            };

            var line = $"{prefix} {message}";

            // Плоский текст для «Copy All»
            if (_plainSb.Length > 0) _plainSb.Append('\n');
            _plainSb.Append(line);

            // Rich-text версия с цветом
            if (_richSb.Length > 0) _richSb.Append('\n');
            string colorHex = type switch
            {
                LogType.Error or LogType.Exception => "#ff6b6b",
                LogType.Warning                    => "#ffcc44",
                _                                  => "#b3b3b3"
            };
            _richSb.Append($"<color={colorHex}>{EscapeRichText(line)}</color>");

            _textArea.value = _richSb.ToString();

            // Прокрутка вниз
            schedule.Execute(() =>
            {
                if (_scrollView != null)
                    _scrollView.verticalScroller.value = _scrollView.verticalScroller.highValue;
            }).ExecuteLater(20);
        }

        public new void Clear()
        {
            _plainSb.Clear();
            _richSb.Clear();
            _textArea.value = "";
        }

        private static string EscapeRichText(string s)
        {
            // Экранируем угловые скобки чтобы случайные <...> в тексте не трактовались как теги
            return s.Replace("<", "<").Replace(">", ">");
        }

        // ────────────────────────────────────────────────────────────────────
        // Построение UI
        // ────────────────────────────────────────────────────────────────────

        private void BuildDragHandle()
        {
            var handle = new VisualElement();
            handle.style.height          = 8;
            handle.style.flexShrink      = 0;
            handle.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f);
            handle.style.alignItems      = Align.Center;
            handle.style.justifyContent  = Justify.Center;
            handle.tooltip               = "Перетащите чтобы изменить высоту";


            // Визуальные точки-ручки
            var grip = new Label("· · · · ·");
            grip.style.color    = new Color(0.5f, 0.5f, 0.5f);
            grip.style.fontSize = 8;
            handle.Add(grip);

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                _isDragging      = true;
                _dragStartY      = evt.position.y;
                _dragStartHeight = _scrollView?.resolvedStyle.height ?? DefaultH;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isDragging) return;
                float delta  = _dragStartY - evt.position.y;          // вверх = +
                float newH   = Mathf.Clamp(_dragStartHeight + delta, MinH, MaxH);
                if (_scrollView != null)
                    _scrollView.style.height = newH;
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                handle.ReleasePointer(evt.pointerId);
            });

            Add(handle);
        }

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection  = FlexDirection.Row;
            header.style.alignItems     = Align.Center;
            header.style.paddingLeft    = 8;
            header.style.paddingRight   = 6;
            header.style.paddingTop     = 3;
            header.style.paddingBottom  = 3;
            header.style.flexShrink     = 0;

            var title = new Label("Консоль");
            title.style.fontSize                = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color                   = Color.white;
            title.style.flexGrow                = 1;
            header.Add(title);

            // Кнопка «Копировать всё»
            var copyAll = new Button(OnCopyAll)
            {
                text    = "⧉ Всё",
                tooltip = "Скопировать всё содержимое консоли"
            };
            copyAll.style.marginRight = 4;
            header.Add(copyAll);

            // Кнопка очистки
            var clearBtn = new Button(Clear)
            {
                text    = "✕",
                tooltip = "Очистить консоль"
            };
            clearBtn.style.width       = 26;
            clearBtn.style.marginRight = 4;
            header.Add(clearBtn);

            // Кнопка сворачивания
            _toggleButton = new Button(ToggleConsole) { text = "▼" };
            _toggleButton.style.width = 26;
            header.Add(_toggleButton);

            Add(header);
        }

        private void BuildTextArea()
        {
            _scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scrollView.style.height    = DefaultH;
            _scrollView.style.flexGrow  = 0;
            _scrollView.style.flexShrink = 0;
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _scrollView.verticalScrollerVisibility   = ScrollerVisibility.Auto;

            // TextField — multiline + readonly: поддерживает выделение мышкой и Ctrl+C
            _textArea = new TextField
            {
                multiline  = true,
                isReadOnly = true,
                value      = ""
            };

            // Убираем перенос строк чтобы текст не сжимался при уменьшении консоли
            _textArea.style.whiteSpace  = WhiteSpace.NoWrap;
            _textArea.style.flexGrow    = 1;
            _textArea.style.minWidth    = 0;
            _textArea.style.fontSize    = 11;
            _textArea.style.color       = new Color(0.80f, 0.80f, 0.80f);

            // Убираем стандартный фон и рамку TextField
            _textArea.style.backgroundColor         = new Color(0.05f, 0.05f, 0.05f);
            _textArea.style.borderTopWidth          = 0;
            _textArea.style.borderBottomWidth       = 0;
            _textArea.style.borderLeftWidth         = 0;
            _textArea.style.borderRightWidth        = 0;
            _textArea.style.paddingLeft             = 6;
            _textArea.style.paddingTop              = 4;
            _textArea.style.paddingBottom           = 4;

            // Включаем rich text после добавления в панель
            _textArea.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var textEl = _textArea.Q<TextElement>();
                if (textEl != null) textEl.enableRichText = true;
            });

            _scrollView.Add(_textArea);
            Add(_scrollView);
        }

        // ────────────────────────────────────────────────────────────────────
        // Приватные методы
        // ────────────────────────────────────────────────────────────────────

        private void ToggleConsole()
        {
            _isVisible = !_isVisible;
            if (_scrollView != null)
                _scrollView.style.display = _isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleButton.text = _isVisible ? "▼" : "▲";
        }

        private void OnCopyAll()
        {
            if (_plainSb.Length == 0) return;
            GUIUtility.systemCopyBuffer = _plainSb.ToString();
        }
    }
}
