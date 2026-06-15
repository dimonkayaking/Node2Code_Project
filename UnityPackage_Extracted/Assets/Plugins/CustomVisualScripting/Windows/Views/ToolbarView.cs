using UnityEngine;
using UnityEngine.UIElements;

namespace CustomVisualScripting.Windows.Views
{
    public class ToolbarView : VisualElement
    {
        public Button ParseButton { get; private set; }
        public Button GenerateButton { get; private set; }
        public Button RunButton { get; private set; }
        public Button StopButton { get; private set; }
        public Button SaveButton { get; private set; }
        public Button SaveAsButton { get; private set; }
        public Button LoadButton { get; private set; }
        public Button ClearButton { get; private set; }
        
        private Label _statusLabel;
        
        public ToolbarView()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            style.paddingTop = 6;
            style.paddingBottom = 6;
            style.paddingLeft = 12;
            style.paddingRight = 12;
            style.borderBottomWidth = 1;
            style.borderBottomColor = Color.black;
            style.borderTopWidth = 1;
            style.borderTopColor = Color.black;

            // ── Группа: Код ───────────────────────────────────────────────────
            ParseButton = new Button { text = "Парсить код", tooltip = "Разобрать код в граф" };
            ConfigureToolbarButton(ParseButton);
            ParseButton.style.marginRight = 5;
            Add(ParseButton);

            GenerateButton = new Button { text = "Сгенерировать", tooltip = "Сгенерировать код из графа" };
            ConfigureToolbarButton(GenerateButton);
            GenerateButton.style.marginRight = 5;
            Add(GenerateButton);

            Add(MakeSeparator());

            // ── Группа: Запуск ────────────────────────────────────────────────
            RunButton = new Button { text = "▶ Run", tooltip = "Запустить (F5 / Ctrl+Enter)" };
            ConfigureToolbarButton(RunButton);
            RunButton.style.marginRight = 5;
            RunButton.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f);
            Add(RunButton);

            StopButton = new Button { text = "⏹ Stop", tooltip = "Остановить выполнение" };
            ConfigureToolbarButton(StopButton);
            StopButton.style.marginRight = 5;
            StopButton.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            StopButton.SetEnabled(false);
            Add(StopButton);

            Add(MakeSeparator());

            // ── Группа: Файл ──────────────────────────────────────────────────
            SaveButton = new Button { text = "Сохранить", tooltip = "Сохранить граф (Ctrl+S)" };
            ConfigureToolbarButton(SaveButton);
            SaveButton.style.marginRight = 5;
            Add(SaveButton);

            SaveAsButton = new Button { text = "Сохранить как…", tooltip = "Сохранить в новый файл (Ctrl+Shift+S)" };
            ConfigureToolbarButton(SaveAsButton);
            SaveAsButton.style.marginRight = 5;
            Add(SaveAsButton);

            LoadButton = new Button { text = "Загрузить", tooltip = "Загрузить граф из файла" };
            ConfigureToolbarButton(LoadButton);
            LoadButton.style.marginRight = 5;
            Add(LoadButton);

            ClearButton = new Button { text = "Очистить", tooltip = "Сбросить граф и код" };
            ConfigureToolbarButton(ClearButton);
            ClearButton.style.marginRight = 10;
            Add(ClearButton);

            // ── Статус ────────────────────────────────────────────────────────
            var statusContainer = new VisualElement();
            statusContainer.style.flexGrow = 1;
            statusContainer.style.alignItems = Align.Center;
            statusContainer.style.justifyContent = Justify.FlexEnd;
            statusContainer.style.flexDirection = FlexDirection.Row;
            statusContainer.style.paddingRight = 4;

            _statusLabel = new Label("Готов");
            _statusLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _statusLabel.style.fontSize = 12;
            _statusLabel.style.paddingLeft = 8;
            _statusLabel.style.paddingRight = 4;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            _statusLabel.style.borderLeftWidth = 1;
            _statusLabel.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
            statusContainer.Add(_statusLabel);
            Add(statusContainer);

            // Эффект наведения — читаем цвет лениво в момент MouseEnter (resolvedStyle уже валиден)
            AddHoverEffect(RunButton);
            AddHoverEffect(StopButton);
        }

        /// <summary>Вертикальный разделитель между группами кнопок.</summary>
        private static VisualElement MakeSeparator()
        {
            var sep = new VisualElement();
            sep.style.width            = 1;
            sep.style.alignSelf        = Align.Stretch;
            sep.style.marginTop        = 3;
            sep.style.marginBottom     = 3;
            sep.style.marginLeft       = 6;
            sep.style.marginRight      = 6;
            sep.style.backgroundColor  = new Color(0.38f, 0.38f, 0.38f);
            return sep;
        }

        /// <summary>
        /// Hover-эффект: цвет читается лениво при MouseEnter (resolvedStyle валиден в обработчике),
        /// чтобы избежать Color.clear при чтении в конструкторе до attach к панели.
        /// </summary>
        private static void AddHoverEffect(Button button)
        {
            Color? savedColor = null;

            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!button.enabledSelf) return;
                savedColor = button.resolvedStyle.backgroundColor;
                button.style.backgroundColor = LightenColor(savedColor.Value, 0.07f);
            });

            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!savedColor.HasValue) return;
                button.style.backgroundColor = savedColor.Value;
                savedColor = null;
            });
        }

        private static Color LightenColor(Color color, float amount)
        {
            return new Color(
                Mathf.Min(color.r + amount, 1f),
                Mathf.Min(color.g + amount, 1f),
                Mathf.Min(color.b + amount, 1f),
                color.a
            );
        }

        private static void ConfigureToolbarButton(Button button)
        {
            button.style.fontSize = 14;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.minHeight = 24;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
        }
        
        public void SetRunMode(bool isRunning)
        {
            RunButton.SetEnabled(!isRunning);
            StopButton.SetEnabled(isRunning);
            
            if (isRunning)
            {
                RunButton.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
                StopButton.style.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
            }
            else
            {
                RunButton.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f);
                StopButton.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            }
        }
        
        public void SetStatusNormal(string message)
        {
            _statusLabel.text = message;
            _statusLabel.style.color = Color.white;
        }
        
        public void SetStatusWarning(string message)
        {
            _statusLabel.text = message;
            _statusLabel.style.color = Color.yellow;
        }
        
        public void SetStatusError(string message)
        {
            _statusLabel.text = message;
            _statusLabel.style.color = Color.red;
        }
        
        public void SetStatusSuccess(string message)
        {
            _statusLabel.text = message;
            _statusLabel.style.color = Color.green;
        }
    }
}