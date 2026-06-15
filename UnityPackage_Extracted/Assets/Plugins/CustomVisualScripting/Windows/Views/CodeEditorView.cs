using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Text;

namespace CustomVisualScripting.Windows.Views
{
    public class CodeEditorView : VisualElement
    {
        private const float GutterWidth = 48f;
        private const float OuterHorizontalPadding = 8f;
        private const float OuterVerticalPadding = 6f;
        private const float CodeGapFromGutter = 10f;
        private const string CodeControlName = "CodeEditorTextArea";
        private const float HeaderHeight = 38f;
        private const float LineHeightPx = 16f;
        private const float RightExtraSpace = 8f;

        private readonly IMGUIContainer _imguiEditor;
        private bool _pendingTabIndent;
        private bool _pendingShiftTabDedent;
        private string _pendingAutoIndent; // indent string to insert after Enter
        private bool _pendingUndo;
        private bool _pendingRedo;

        // ── Undo / Redo ───────────────────────────────────────────────────────
        // Каждый снимок: (текст, позиция курсора).  Максимум 200 состояний.
        private const int MaxUndoHistory = 200;
        private readonly List<(string text, int cursor)> _undoStack = new(MaxUndoHistory + 1);
        private readonly List<(string text, int cursor)> _redoStack = new(MaxUndoHistory + 1);

        // Bust-tracking: группируем быстрый ввод в один undo-снимок.
        // При начале печати запоминаем код ДО изменений; через ~0.8 с простоя фиксируем снимок.
        private bool   _burstActive;
        private string _burstStartText;
        private int    _burstStartCursor;
        private double _lastTypeTime;

        private string _code = string.Empty;
        private string _lineNumbers = "1";
        private int _lineCount = 1;
        private int _maxLineLength = 1;

        private Vector2 _scrollPosition;
        private GUIStyle _codeStyle;
        private GUIStyle _lineNumberStyle;

        // Syntax highlighting
        private Dictionary<string, Color> _nodeVariableColors;
        private string _highlightedCode;
        private bool _highlightCodeDirty = true;
        private GUIStyle _richTextStyle;
        private GUIStyle _transparentInputStyle;
        private int _lastCursorIndex = -1;
        private int _lastSelectIndex = -1;
        private bool _wasCaretFocused;
        private double _caretBlinkStartTime = -1d;

        /// <summary>Снимок индексов после GUI.TextArea — до ApplyPendingTabIndent, чтобы GUI.FocusControl не портила selectIndex.</summary>
        private int _capturedCursorIndex;
        private int _capturedSelectIndex;
        private bool _capturedHasSelection;

        private Rect _lastValidViewportRect;
        private GUIContent _codeContent = new GUIContent("");

        private float GetActualLineHeight()
        {
            if (_codeStyle == null) return LineHeightPx;
            float lh = _codeStyle.lineHeight;
            if (lh > 0f) return lh;
            float h1 = _codeStyle.CalcSize(new GUIContent("M")).y;
            float h2 = _codeStyle.CalcSize(new GUIContent("M\nM")).y;
            lh = h2 - h1;
            return lh > 0f ? lh : LineHeightPx;
        }

        public string Code
        {
            get => _code;
            set
            {
                _code = value?.Replace("\r", "") ?? string.Empty;
                _codeContent.text = _code;
                RebuildLineMetadata();
                _highlightCodeDirty = true;
                _scrollPosition = Vector2.zero;   // сбрасываем скролл при внешней установке кода
                _imguiEditor?.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Устанавливает карту variableName → Color для подсветки нод-переменных.
        /// Вызывается из VisualScriptingWindow при изменении графа.
        /// </summary>
        public void SetNodeVariableColors(Dictionary<string, Color> colors)
        {
            _nodeVariableColors = colors;
            _highlightCodeDirty = true;
            _imguiEditor?.MarkDirtyRepaint();
        }

        public CodeEditorView()
        {
            // Заголовок
            var headerContainer = new VisualElement();
            headerContainer.style.height = HeaderHeight;
            headerContainer.style.flexDirection = FlexDirection.Row;
            headerContainer.style.alignItems = Align.Center;
            headerContainer.style.justifyContent = Justify.Center;
            headerContainer.style.marginTop = 0;
            headerContainer.style.marginBottom = 0;
            headerContainer.style.paddingTop = 0;
            headerContainer.style.paddingBottom = 0;
            headerContainer.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            headerContainer.style.borderBottomWidth = 1;
            headerContainer.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            headerContainer.style.borderTopWidth = 0;
            headerContainer.style.borderLeftWidth = 0;
            headerContainer.style.borderRightWidth = 0;

            var title = new Label("Код");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginTop = 0;
            title.style.marginBottom = 0;
            title.style.paddingTop = 0;
            title.style.paddingBottom = 0;

            headerContainer.Add(title);
            Add(headerContainer);

            _imguiEditor = new IMGUIContainer(DrawEditor)
            {
                style =
                {
                    flexGrow = 1,
                    minHeight = 0,
                    marginTop = 0,
                    marginBottom = 0,
                    paddingTop = 0,
                    paddingBottom = 0,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    backgroundColor = new Color(0.12f, 0.12f, 0.12f)
                }
            };
            _imguiEditor.AddToClassList("code-editor-imgui-container");
            _imguiEditor.focusable = true;
            _imguiEditor.RegisterCallback<KeyDownEvent>(OnTabTrickleDown, TrickleDown.TrickleDown);
            Add(_imguiEditor);

            style.flexGrow = 1;
            style.minHeight = 0;
            style.marginLeft = 0;
            style.marginRight = 0;
            style.marginTop = 0;
            style.marginBottom = 0;
            style.paddingTop = 0;
            style.paddingBottom = 0;
            style.borderTopWidth = 0;
            style.borderBottomWidth = 0;
            style.borderLeftWidth = 0;
            style.borderRightWidth = 0;

            RegisterCallback<KeyDownEvent>(OnTabTrickleDown, TrickleDown.TrickleDown);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMoveTrickleDown, TrickleDown.TrickleDown);
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmitTrickleDown, TrickleDown.TrickleDown);

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            // Периодический репейнт для мигания курсора (~530 мс) — только когда редактор в фокусе.
            // Также используется для фиксации burst-снимка при паузе в печати.
            _imguiEditor.schedule.Execute(() =>
            {
                CommitBurstIfIdle();
                if (IsCodeEditorFocused()) _imguiEditor.MarkDirtyRepaint();
            }).Every(530);

            RebuildLineMetadata();
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            var root = panel?.visualTree;
            if (root == null) return;
            root.RegisterCallback<KeyDownEvent>(OnRootTabTrickleDown,    TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnRootUndoRedoKeyDown,   TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(OnRootNavigationMove, TrickleDown.TrickleDown);
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            var root = evt.originPanel?.visualTree;
            if (root == null) return;
            root.UnregisterCallback<KeyDownEvent>(OnRootTabTrickleDown,    TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyDownEvent>(OnRootUndoRedoKeyDown,   TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationMoveEvent>(OnRootNavigationMove, TrickleDown.TrickleDown);
        }

        private bool IsCodeEditorFocused()
        {
            if (panel == null) return false;
            var focused = panel.focusController?.focusedElement as VisualElement;
            if (focused == null) return false;
            return focused == _imguiEditor || focused == this ||
                   (focused is VisualElement ve && ve.FindCommonAncestor(_imguiEditor) == _imguiEditor);
        }

        private void OnRootTabTrickleDown(KeyDownEvent evt)
        {
            if (!IsCodeEditorFocused()) return;
            SwallowTabAndScheduleIndent(evt);
        }

        /// <summary>
        /// Перехватывает Ctrl+Z / Ctrl+Y (и Cmd+Z / Cmd+Y на macOS) на фазе trickle-down
        /// ДО того как IMGUI обработает их нативным undo — мы ставим наш pending-флаг.
        /// </summary>
        private void OnRootUndoRedoKeyDown(KeyDownEvent evt)
        {
            if (!IsCodeEditorFocused()) return;
            bool ctrl = evt.ctrlKey || evt.commandKey;
            if (!ctrl) return;

            if (evt.keyCode == KeyCode.Z)
            {
                _pendingUndo = true;
                evt.StopImmediatePropagation();
                evt.StopPropagation();
                _imguiEditor.MarkDirtyRepaint();
            }
            else if (evt.keyCode == KeyCode.Y)
            {
                _pendingRedo = true;
                evt.StopImmediatePropagation();
                evt.StopPropagation();
                _imguiEditor.MarkDirtyRepaint();
            }
        }

        private void OnRootNavigationMove(NavigationMoveEvent evt)
        {
            if (!IsCodeEditorFocused()) return;
            evt.StopImmediatePropagation();
            evt.StopPropagation();
        }

        private void OnTabTrickleDown(KeyDownEvent evt)
        {
            if (!IsCodeEditorFocused()) return;
            SwallowTabAndScheduleIndent(evt);
        }

        private void SwallowTabAndScheduleIndent(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab && evt.character != '\t') return;
            evt.StopImmediatePropagation();
            evt.StopPropagation();
            if (evt.shiftKey)
                _pendingShiftTabDedent = true;
            else
                _pendingTabIndent = true;
            _imguiEditor.MarkDirtyRepaint();
        }

        private void OnNavigationMoveTrickleDown(NavigationMoveEvent evt)
        {
            evt.StopImmediatePropagation();
            evt.StopPropagation();
        }

        private void OnNavigationSubmitTrickleDown(NavigationSubmitEvent evt)
        {
            evt.StopImmediatePropagation();
            evt.StopPropagation();
        }

        public new void Clear()
        {
            Code = string.Empty;
            _scrollPosition = Vector2.zero;
        }

        private void RebuildHighlightIfNeeded()
        {
            if (!_highlightCodeDirty) return;
            _highlightCodeDirty = false;
            _highlightedCode = SyntaxHighlighter.BuildHighlightedText(_code, _nodeVariableColors);
        }

        private void DrawEditor()
        {
            EnsureStyles();
            RebuildHighlightIfNeeded();

            var viewportRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorLikeDrawRect(viewportRect, new Color(0.12f, 0.12f, 0.12f));
            DrawEditorContents(viewportRect);
        }

        private void DrawEditorContents(Rect viewportRect)
        {
            if (Event.current.type == EventType.Repaint)
                _lastValidViewportRect = viewportRect;

            Rect rectToUse = _lastValidViewportRect.width > 1f ? _lastValidViewportRect : viewportRect;

            const float scrollBarAllowance = 24f;
            float contentWidth = ComputeScrollContentWidth(rectToUse);
            
            float actualLineH = GetActualLineHeight();
            // Высота контента: минимум – видимая область, максимум – необходимая высота по строкам
            float requiredHeight = _lineCount * actualLineH + OuterVerticalPadding * 2f + _codeStyle.padding.top + _codeStyle.padding.bottom;
            float contentHeight = Mathf.Max(rectToUse.height - scrollBarAllowance, requiredHeight);

            var contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

            _scrollPosition = GUI.BeginScrollView(viewportRect, _scrollPosition, contentRect, true, true);
            bool textChanged = DrawScrollContent(contentRect, actualLineH);
            GUI.EndScrollView();

            if (textChanged && IsCodeEditorFocused())
            {
                // После вставки/TAB обновились _maxLineLength / _lineCount — ширина контента должна совпадать с этим кадром,
                // иначе maxScrollX занижен и скролл резко «уезжает влево».
                AdjustScrollToCaret(rectToUse);
            }
        }

        /// <summary>Ширина виртуального контента ScrollView по текущим метаданным строк (должна вызываться после RebuildLineMetadata).</summary>
        private float ComputeScrollContentWidth(Rect viewportRect)
        {
            const float scrollBarAllowance = 24f;
            if (_codeStyle == null) EnsureStyles();
            float charWidth = Mathf.Max(6f, _codeStyle.CalcSize(new GUIContent("M")).x);
            float requiredWidth = GutterWidth + OuterHorizontalPadding * 2f + CodeGapFromGutter + (_maxLineLength * charWidth) + RightExtraSpace;
            return Mathf.Max(viewportRect.width - scrollBarAllowance, requiredWidth);
        }

        private bool DrawScrollContent(Rect contentRect, float lineHeight)
        {
            bool textChanged = false;

            // Idle-burst commit: если пользователь не печатал ~0.8 с — фиксируем snapshot
            CommitBurstIfIdle();

            // Undo/Redo обрабатываем в самом начале IMGUI-кадра, чтобы editor-state был свежим
            if (_pendingUndo) { ApplyPendingUndo(); return true; }
            if (_pendingRedo) { ApplyPendingRedo(); return true; }

            var gutterRect = new Rect(contentRect.x, contentRect.y, GutterWidth, contentRect.height);
            EditorLikeDrawRect(gutterRect, new Color(0.10f, 0.10f, 0.10f));
            EditorLikeDrawRect(new Rect(gutterRect.xMax - 1f, gutterRect.y, 1f, gutterRect.height), new Color(0.22f, 0.22f, 0.22f));

            var lineRect = new Rect(gutterRect.x + 4f, gutterRect.y + OuterVerticalPadding, GutterWidth - 10f,
                Mathf.Max(lineHeight, contentRect.height - OuterVerticalPadding * 2f));
            GUI.Label(lineRect, _lineNumbers, _lineNumberStyle);

            float codeX = gutterRect.xMax + CodeGapFromGutter;
            var codeRect = new Rect(codeX, contentRect.y + OuterVerticalPadding,
                Mathf.Max(1f, contentRect.width - codeX - OuterHorizontalPadding),
                Mathf.Max(lineHeight, contentRect.height - OuterVerticalPadding * 2f));

            EditorGUIUtility.AddCursorRect(codeRect, MouseCursor.Text);

            // Перехватываем Enter в IMGUI до TextArea, чтобы сохранить отступ текущей строки.
            // TextArea вставит '\n', мы добавим indent следом в ApplyPendingAutoIndent.
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                string.Equals(GUI.GetNameOfFocusedControl(), CodeControlName))
            {
                var preEditor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                if (preEditor != null)
                    _pendingAutoIndent = GetCurrentLineIndent(preEditor.cursorIndex);
            }

            // 1. TextArea: рисует фон + курсор + выделение; сам текст прозрачен
            // Сохраняем состояние ДО TextArea, чтобы при изменении текста знать "старый" курсор для burst
            string preTextAreaCode   = _code;
            int    preTextAreaCursor = _capturedCursorIndex;

            GUI.SetNextControlName(CodeControlName);
            string next = GUI.TextArea(codeRect, _code, _transparentInputStyle);
            if (next != null && next.Contains('\r'))
                next = next.Replace("\r", "");

            var editorSnap = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editorSnap != null)
            {
                _capturedCursorIndex = editorSnap.cursorIndex;
                _capturedSelectIndex = editorSnap.selectIndex;
                _capturedHasSelection = editorSnap.hasSelection;
            }

            if (!string.Equals(next, _code))
            {
                // Пользователь напечатал что-то — обновляем burst-tracking
                OnTypingDetected(preTextAreaCode, preTextAreaCursor);

                _code = next;
                _codeContent.text = _code;
                RebuildLineMetadata();
                _highlightCodeDirty = true;
                RebuildHighlightIfNeeded();
                textChanged = true;
            }
            else
            {
                // Текст не изменился — Enter не был вставлен, сбрасываем pending-indent
                _pendingAutoIndent = null;
            }

            // Tab-вставку применяем после TextArea, чтобы брать актуальный TextEditor/cursorIndex.
            if (_pendingTabIndent && ApplyPendingTabIndent())
                textChanged = true;

            // Shift+Tab — дедент текущей строки
            if (_pendingShiftTabDedent && ApplyPendingShiftTabDedent())
                textChanged = true;

            // Auto-indent после Enter — добавляем отступ текущей строки
            if (_pendingAutoIndent != null && ApplyPendingAutoIndent())
                textChanged = true;

            // 2. Label поверх TextArea: прозрачный фон, цветной rich-text
            GUI.Label(codeRect, _highlightedCode ?? string.Empty, _richTextStyle);

            // 3. Выделение и курсор поверх Label
            DrawCustomSelection(codeRect);
            DrawCustomCursor(codeRect);

            return textChanged;
        }

        private void DrawCustomSelection(Rect codeRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!string.Equals(GUI.GetNameOfFocusedControl(), CodeControlName)) return;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor == null) return;
            if (editor.cursorIndex == editor.selectIndex) return;

            int start = Mathf.Clamp(Mathf.Min(editor.cursorIndex, editor.selectIndex), 0, _code.Length);
            int end = Mathf.Clamp(Mathf.Max(editor.cursorIndex, editor.selectIndex), 0, _code.Length);

            GetLineAndColumn(start, out int startLine, out int startCol);
            GetLineAndColumn(end, out int endLine, out int endCol);

            float lineH = GetActualLineHeight();
            Color selColor = GUI.skin.settings.selectionColor;
            if (selColor.a < 0.05f)
                selColor = new Color(0.24f, 0.49f, 0.90f, 0.55f);

            var prevColor = GUI.color;
            GUI.color = selColor;

            for (int line = startLine; line <= endLine; line++)
            {
                int lineStartIdx = GetLineStartIndex(line);
                int lineEndIdx = GetLineEndIndex(line);

                int selStartIdx = Mathf.Max(start, lineStartIdx);
                int selEndIdx = Mathf.Min(end, lineEndIdx);

                Vector2 startPos = _transparentInputStyle.GetCursorPixelPosition(codeRect, _codeContent, selStartIdx);
                Vector2 endPos = _transparentInputStyle.GetCursorPixelPosition(codeRect, _codeContent, selEndIdx);

                float xStart = startPos.x;
                float xEnd = endPos.x;
                if (xEnd <= xStart) xEnd = xStart + 2f;

                float y = startPos.y;
                var rect = new Rect(xStart, y, xEnd - xStart, lineH);
                if (codeRect.Overlaps(rect))
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }

        private int GetLineStartIndex(int lineIndex)
        {
            if (lineIndex <= 0) return 0;
            int currentLine = 0;
            for (int i = 0; i < _code.Length; i++)
            {
                if (_code[i] == '\n')
                {
                    currentLine++;
                    if (currentLine == lineIndex)
                        return i + 1;
                }
            }
            return _code.Length;
        }

        private int GetLineEndIndex(int lineIndex)
        {
            int currentLine = 0;
            for (int i = 0; i < _code.Length; i++)
            {
                if (_code[i] == '\n')
                {
                    if (currentLine == lineIndex)
                        return i;
                    currentLine++;
                }
            }
            return _code.Length;
        }

        /// <summary>
        /// Перерисовывает курсор поверх rich-text метки.
        /// TextArea уже нарисовал курсор под Label — этот метод восстанавливает его видимость.
        /// </summary>
        private void DrawCustomCursor(Rect codeRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!string.Equals(GUI.GetNameOfFocusedControl(), CodeControlName)) return;

            // Мигание: 0.53s вкл / 0.53s выкл, с обнулением фазы при движении каретки/выделения.
            double phase = (EditorApplication.timeSinceStartup - _caretBlinkStartTime) % 1.06;
            if (phase > 0.53) return;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor == null) return;
            UpdateCaretBlinkState(editor);

            int cursorIndex = Mathf.Clamp(editor.cursorIndex, 0, _code.Length);
            Vector2 cursorPos = _transparentInputStyle.GetCursorPixelPosition(codeRect, _codeContent, cursorIndex);
            
            float cursorH = GetActualLineHeight();
            var cursorRect = new Rect(cursorPos.x, cursorPos.y, 1f, cursorH);

            // Страховка: не рисуем курсор вне области редактора.
            if (!codeRect.Overlaps(cursorRect))
                return;

            Color cursorColor = GUI.skin.settings.cursorColor;
            if (cursorColor.a < 0.05f)
                cursorColor = new Color(0.8f, 0.8f, 0.8f, 1f);

            var prevColor = GUI.color;
            GUI.color = cursorColor;
            GUI.DrawTexture(cursorRect, Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        private void UpdateCaretBlinkState(TextEditor editor)
        {
            bool focused = string.Equals(GUI.GetNameOfFocusedControl(), CodeControlName);
            bool moved = editor.cursorIndex != _lastCursorIndex || editor.selectIndex != _lastSelectIndex;

            if (_caretBlinkStartTime < 0d || moved || focused != _wasCaretFocused)
                _caretBlinkStartTime = EditorApplication.timeSinceStartup;

            _lastCursorIndex = editor.cursorIndex;
            _lastSelectIndex = editor.selectIndex;
            _wasCaretFocused = focused;
        }

        private bool ApplyPendingTabIndent()
        {
            _pendingTabIndent = false;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor == null) return false;
            _code ??= string.Empty;
            int cursor = Mathf.Clamp(_capturedCursorIndex, 0, _code.Length);

            // Сохраняем состояние до изменения, чтобы Ctrl+Z мог его восстановить
            BeforeManualEdit(cursor);
            int select = Mathf.Clamp(_capturedSelectIndex, 0, _code.Length);
            bool hasSelection = _capturedHasSelection && cursor != select;

            int start = hasSelection ? Mathf.Min(cursor, select) : cursor;
            int end = hasSelection ? Mathf.Max(cursor, select) : cursor;
            const string indent = "    ";
            _code = end > start
                ? _code.Remove(start, end - start).Insert(start, indent)
                : _code.Insert(start, indent);
            _codeContent.text = _code;
            int newCursor = start + indent.Length;
            editor.text = _code;
            editor.cursorIndex = newCursor;
            editor.selectIndex = newCursor;
            RebuildLineMetadata();
            _highlightCodeDirty = true;
            RebuildHighlightIfNeeded();
            GUI.changed = true;
            return true;
        }

        /// <summary>
        /// Shift+Tab: удаляет до 4 пробелов с начала строки под курсором.
        /// </summary>
        private bool ApplyPendingShiftTabDedent()
        {
            _pendingShiftTabDedent = false;
            _code ??= string.Empty;

            int cursor = Mathf.Clamp(_capturedCursorIndex, 0, _code.Length);

            // Сохраняем состояние до изменения, чтобы Ctrl+Z мог его восстановить
            BeforeManualEdit(cursor);
            int lineIdx = GetLineAtIndex(cursor);
            int lineStart = GetLineStartIndex(lineIdx);

            // Сколько пробелов можно убрать (максимум 4)
            int spacesToRemove = 0;
            while (spacesToRemove < 4 && lineStart + spacesToRemove < _code.Length &&
                   _code[lineStart + spacesToRemove] == ' ')
                spacesToRemove++;

            if (spacesToRemove == 0) return false;

            _code = _code.Remove(lineStart, spacesToRemove);
            _codeContent.text = _code;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor != null)
            {
                int newCursor = Mathf.Max(lineStart, cursor - spacesToRemove);
                editor.text         = _code;
                editor.cursorIndex  = newCursor;
                editor.selectIndex  = newCursor;
            }

            RebuildLineMetadata();
            _highlightCodeDirty = true;
            RebuildHighlightIfNeeded();
            GUI.changed = true;
            return true;
        }

        /// <summary>
        /// Auto-indent: после того как TextArea вставил '\n', добавляем отступ предыдущей строки.
        /// </summary>
        private bool ApplyPendingAutoIndent()
        {
            var indent = _pendingAutoIndent;
            _pendingAutoIndent = null;

            if (string.IsNullOrEmpty(indent)) return false;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor == null) return false;

            int cursor = Mathf.Clamp(editor.cursorIndex, 0, _code.Length);
            _code = _code.Insert(cursor, indent);
            _codeContent.text = _code;

            editor.text        = _code;
            editor.cursorIndex = cursor + indent.Length;
            editor.selectIndex = cursor + indent.Length;

            RebuildLineMetadata();
            _highlightCodeDirty = true;
            RebuildHighlightIfNeeded();
            GUI.changed = true;
            return true;
        }

        // ── Undo / Redo helpers ───────────────────────────────────────────────

        /// <summary>
        /// Вызывается перед программным изменением текста (Tab / Shift+Tab).
        /// Фиксирует активный burst (если есть), затем кладёт текущее состояние в undo-стек.
        /// </summary>
        private void BeforeManualEdit(int snapshotCursor)
        {
            if (_burstActive)
            {
                // Кладём состояние ДО burst, чтобы дополнительный Ctrl+Z мог добраться до него
                _redoStack.Clear();
                _undoStack.Add((_burstStartText, _burstStartCursor));
                if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
                _burstActive = false;
            }
            // Теперь кладём состояние перед самим Tab/Shift+Tab
            _redoStack.Clear();
            _undoStack.Add((_code, snapshotCursor));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
        }

        /// <summary>
        /// Вызывается при каждом изменении текста через печать (не через Tab/Shift+Tab).
        /// Если burst ещё не начался — запоминаем состояние ДО этого изменения.
        /// </summary>
        private void OnTypingDetected(string preCode, int preCursor)
        {
            _lastTypeTime = EditorApplication.timeSinceStartup;
            if (!_burstActive)
            {
                _burstStartText   = preCode;
                _burstStartCursor = preCursor;
                _burstActive      = true;
                _redoStack.Clear();   // новый ввод сбрасывает redo
            }
        }

        /// <summary>
        /// Фиксирует burst в undo-стек, если с последней клавиши прошло ≥ 0.8 с.
        /// Вызывается в начале каждого IMGUI-кадра и из периодического таймера.
        /// </summary>
        private void CommitBurstIfIdle()
        {
            if (!_burstActive) return;
            if (EditorApplication.timeSinceStartup - _lastTypeTime < 0.8) return;
            _undoStack.Add((_burstStartText, _burstStartCursor));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _burstActive = false;
        }

        private void ApplyPendingUndo()
        {
            _pendingUndo = false;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            int currentCursor = editor != null ? Mathf.Clamp(editor.cursorIndex, 0, _code.Length) : 0;

            string restoreText;
            int    restoreCursor;

            if (_burstActive)
            {
                // Undo в середине burst → откатываем к состоянию ДО burst
                restoreText   = _burstStartText;
                restoreCursor = _burstStartCursor;
                _burstActive  = false;
                _redoStack.Add((_code, currentCursor));
                if (_redoStack.Count > MaxUndoHistory) _redoStack.RemoveAt(0);
            }
            else if (_undoStack.Count > 0)
            {
                _redoStack.Add((_code, currentCursor));
                if (_redoStack.Count > MaxUndoHistory) _redoStack.RemoveAt(0);
                var top = _undoStack[_undoStack.Count - 1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                restoreText   = top.text;
                restoreCursor = top.cursor;
            }
            else
            {
                return; // нечего отменять
            }

            ApplySnapshot(restoreText, restoreCursor, editor);
        }

        private void ApplyPendingRedo()
        {
            _pendingRedo = false;
            if (_redoStack.Count == 0) return;

            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            int currentCursor = editor != null ? Mathf.Clamp(editor.cursorIndex, 0, _code.Length) : 0;

            if (_burstActive)
            {
                _undoStack.Add((_burstStartText, _burstStartCursor));
                if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
                _burstActive = false;
            }

            _undoStack.Add((_code, currentCursor));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);

            var state = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            ApplySnapshot(state.text, state.cursor, editor);
        }

        /// <summary>Восстанавливает текст и позицию курсора из snapshot.</summary>
        private void ApplySnapshot(string text, int cursor, TextEditor editor)
        {
            _code = text ?? string.Empty;
            _codeContent.text = _code;
            if (editor != null)
            {
                editor.text = _code;
                int safeC = Mathf.Clamp(cursor, 0, _code.Length);
                editor.cursorIndex = safeC;
                editor.selectIndex = safeC;
            }
            RebuildLineMetadata();
            _highlightCodeDirty = true;
            RebuildHighlightIfNeeded();
            GUI.changed = true;
        }

        /// <summary>Возвращает ведущие пробелы строки, в которой находится <paramref name="cursorIndex"/>.</summary>
        private string GetCurrentLineIndent(int cursorIndex)
        {
            if (string.IsNullOrEmpty(_code)) return "";
            int lineIdx   = GetLineAtIndex(cursorIndex);
            int lineStart = GetLineStartIndex(lineIdx);
            int i = lineStart;
            while (i < _code.Length && _code[i] == ' ') i++;
            return _code.Substring(lineStart, i - lineStart);
        }

        /// <summary>Возвращает номер строки (0-based) для позиции в строке кода.</summary>
        private int GetLineAtIndex(int index)
        {
            int line = 0;
            int clampedIndex = Mathf.Min(index, _code?.Length ?? 0);
            for (int i = 0; i < clampedIndex; i++)
                if (_code[i] == '\n') line++;
            return line;
        }

        private void EnsureStyles()
        {
            if (_codeStyle == null)
            {
                _codeStyle = new GUIStyle(GUI.skin.textArea)
                {
                    wordWrap = false,
                    richText = false,
                    border = new RectOffset(0, 0, 0, 0)
                };
                _codeStyle.font = Font.CreateDynamicFontFromOSFont("Consolas", 13) ??
                                  Font.CreateDynamicFontFromOSFont("Courier New", 13);
                _codeStyle.fontSize = 13;
                _codeStyle.padding = new RectOffset(6, 6, 4, 4);
                _codeStyle.margin = new RectOffset(0, 0, 0, 0);
            }

            if (_lineNumberStyle == null)
            {
                _lineNumberStyle = new GUIStyle(_codeStyle)
                {
                    alignment = TextAnchor.UpperRight,
                    normal = { textColor = new Color(0.45f, 0.45f, 0.45f) },
                    border = new RectOffset(0, 0, 0, 0)
                };
                _lineNumberStyle.padding = new RectOffset(0, 2, _codeStyle.padding.top, _codeStyle.padding.bottom);
            }

            if (_richTextStyle == null)
            {
                _richTextStyle = new GUIStyle(_codeStyle) { richText = true };
                _richTextStyle.normal.background   = null;
                _richTextStyle.focused.background  = null;
                _richTextStyle.hover.background    = null;
                _richTextStyle.active.background   = null;
                _richTextStyle.onNormal.background  = null;
                _richTextStyle.onFocused.background = null;
                _richTextStyle.onHover.background   = null;
                _richTextStyle.onActive.background  = null;
                var defColor = SyntaxHighlighter.DefaultTextColor;
                _richTextStyle.normal.textColor  = defColor;
                _richTextStyle.focused.textColor = defColor;
                _richTextStyle.hover.textColor   = defColor;
                _richTextStyle.active.textColor  = defColor;
            }

            if (_transparentInputStyle == null)
            {
                _transparentInputStyle = new GUIStyle(_codeStyle) { richText = false };
                _transparentInputStyle.normal.textColor   = Color.clear;
                _transparentInputStyle.focused.textColor  = Color.clear;
                _transparentInputStyle.hover.textColor    = Color.clear;
                _transparentInputStyle.active.textColor   = Color.clear;
                _transparentInputStyle.onNormal.textColor  = Color.clear;
                _transparentInputStyle.onFocused.textColor = Color.clear;
                _transparentInputStyle.onHover.textColor   = Color.clear;
                _transparentInputStyle.onActive.textColor  = Color.clear;
            }
        }

        private void AdjustScrollToCaret(Rect viewportRect)
        {
            if (!string.Equals(GUI.GetNameOfFocusedControl(), CodeControlName)) return;
            if (_codeStyle == null) return;

            var textEditor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (textEditor == null) return;

            Rect rectToUse = _lastValidViewportRect.width > 1f ? _lastValidViewportRect : viewportRect;
            if (rectToUse.width <= 1f) return;

            const float scrollBarAllowance = 24f;
            float visibleW = Mathf.Max(1f, rectToUse.width - scrollBarAllowance);
            float contentWidth = ComputeScrollContentWidth(rectToUse);
            float maxScrollX = Mathf.Max(0f, contentWidth - visibleW);

            int cursorIndex = Mathf.Clamp(textEditor.cursorIndex, 0, _code.Length);

            Vector2 localCursorPos = _transparentInputStyle.GetCursorPixelPosition(new Rect(0, 0, 10000, 10000), _codeContent, cursorIndex);

            float lineH = GetActualLineHeight();
            float caretY = OuterVerticalPadding + localCursorPos.y;
            float visibleTop = _scrollPosition.y;
            float visibleBottom = visibleTop + rectToUse.height - 40f;
            const float verticalMargin = 10f;

            if (caretY + lineH + verticalMargin > visibleBottom)
                _scrollPosition.y = Mathf.Clamp(
                    caretY + lineH + verticalMargin - (rectToUse.height - 40f),
                    0f, float.MaxValue);
            else if (caretY - verticalMargin < visibleTop)
                _scrollPosition.y = Mathf.Max(0f, caretY - verticalMargin);

            float caretContentX = GutterWidth + CodeGapFromGutter + localCursorPos.x;

            const float horizontalMargin = 24f;
            float left = _scrollPosition.x;
            float right = left + visibleW;

            if (caretContentX < left + horizontalMargin)
                _scrollPosition.x = Mathf.Clamp(caretContentX - horizontalMargin, 0f, maxScrollX);
            else if (caretContentX > right - horizontalMargin)
                _scrollPosition.x = Mathf.Clamp(caretContentX - visibleW + horizontalMargin, 0f, maxScrollX);

            _scrollPosition.x = Mathf.Clamp(_scrollPosition.x, 0f, maxScrollX);
            _scrollPosition.y = Mathf.Max(0f, _scrollPosition.y);
        }

        private string GetLineText(int lineNumber)
        {
            if (_code == null) return "";
            int currentLine = 0;
            int start = 0;
            for (int i = 0; i < _code.Length; i++)
            {
                if (_code[i] == '\n')
                {
                    if (currentLine == lineNumber)
                        return _code.Substring(start, i - start);
                    start = i + 1;
                    currentLine++;
                }
            }
            if (currentLine == lineNumber)
                return _code.Substring(start);
            return "";
        }

        private void GetLineAndColumn(int index, out int line, out int column)
        {
            line = 0;
            column = 0;
            for (int i = 0; i < index; i++)
            {
                char c = _code[i];
                if (c == '\n')
                {
                    line++;
                    column = 0;
                }
                else if (c != '\r')
                    column++;
            }
        }

        private void RebuildLineMetadata()
        {
            int lines = 1;
            int currentLength = 0;
            int maxLength = 0;
            for (int i = 0; i < _code.Length; i++)
            {
                char c = _code[i];
                if (c == '\n')
                {
                    lines++;
                    if (currentLength > maxLength) maxLength = currentLength;
                    currentLength = 0;
                    continue;
                }
                if (c != '\r')
                    currentLength++;
            }
            if (currentLength > maxLength) maxLength = currentLength;
            _lineCount = Mathf.Max(1, lines);
            _maxLineLength = Mathf.Max(1, maxLength);
            var sb = new StringBuilder(_lineCount * 4);
            for (int i = 1; i <= _lineCount; i++)
            {
                sb.Append(i);
                if (i < _lineCount) sb.Append('\n');
            }
            _lineNumbers = sb.ToString();
        }

        private static void EditorLikeDrawRect(Rect rect, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }
    }
}
