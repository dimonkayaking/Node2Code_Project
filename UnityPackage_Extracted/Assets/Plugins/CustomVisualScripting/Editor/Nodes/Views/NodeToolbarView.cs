using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Methods;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    public class NodeToolbarView : VisualElement
    {
        private BaseGraphView _graphView;
        private readonly Dictionary<string, List<(string path, Type type)>> _categories;
        private VisualElement _contentContainer;

        private static readonly Dictionary<string, Color> CategoryColors = new()
        {
            { "Literals",   HexColor("#4CAF50") },
            { "Math",       HexColor("#2196F3") },
            { "Comparison", HexColor("#FF9800") },
            { "Logic",      HexColor("#9C27B0") },
            { "Flow",       HexColor("#F44336") },
            { "Conversion", HexColor("#FFC107") },
            { "Debug",      HexColor("#FFFFFF") },
            { "Unity",      HexColor("#8D6E63") }
        };

        private static readonly Color MethodColor = HexColor("#00BCD4");
        private static readonly Color ClassColor  = HexColor("#4CAF50"); // зелёный — категория «Классы»

        // Состояние текущего экрана
        private bool _showingMethodsCategory;
        private bool _showingClassesCategory;

        // Развёрнутые классы в панели методов (по ClassId)
        private readonly HashSet<string> _expandedClassIds = new(StringComparer.Ordinal);

        public NodeToolbarView(BaseGraphView graphView)
        {
            _graphView  = graphView;
            _categories = GetCategories();

            style.backgroundColor  = new Color(0.18f, 0.18f, 0.18f);
            style.borderLeftWidth  = 1;
            style.borderLeftColor  = new Color(0.3f, 0.3f, 0.3f);
            style.flexDirection    = FlexDirection.Column;
            style.paddingBottom    = 0;
            style.marginBottom     = 0;

            BuildUI();
            ShowCategories();

            MethodRegistry.OnChanged += OnMethodsChanged;
            ClassRegistry.OnChanged  += OnClassesChanged;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                MethodRegistry.OnChanged -= OnMethodsChanged;
                ClassRegistry.OnChanged  -= OnClassesChanged;
            });
        }

        public void UpdateGraphView(BaseGraphView newGraphView)
        {
            _graphView = newGraphView;
            ShowCategories();
        }

        private GraphContext GetCurrentContext() =>
            (_graphView as FilteredCreateMenuBaseGraphView)?.GraphContext ?? GraphContext.MethodBody;

        // ─── Построение UI ────────────────────────────────────────────────────

        private void BuildUI()
        {
            var header = new Label("Create Node");
            header.style.fontSize               = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color                   = Color.white;
            header.style.paddingTop              = 10;
            header.style.paddingBottom           = 10;
            header.style.paddingLeft             = 12;
            header.style.unityTextAlign          = TextAnchor.MiddleCenter;
            header.style.backgroundColor         = new Color(0.22f, 0.22f, 0.22f);
            header.style.borderBottomWidth       = 1;
            header.style.borderBottomColor       = new Color(0.3f, 0.3f, 0.3f);
            header.style.whiteSpace              = WhiteSpace.NoWrap;
            header.style.textOverflow            = TextOverflow.Ellipsis;
            header.style.overflow                = Overflow.Hidden;
            Add(header);

            var scrollView = new ScrollView();
            scrollView.style.flexGrow      = 1;
            scrollView.style.paddingBottom = 0;
            scrollView.style.marginBottom  = 0;

            _contentContainer = new VisualElement();
            _contentContainer.style.flexDirection = FlexDirection.Column;
            _contentContainer.style.paddingBottom = 0;
            _contentContainer.style.marginBottom  = 0;
            _contentContainer.style.paddingLeft   = 5;
            _contentContainer.style.paddingRight  = 5;
            _contentContainer.style.width         = Length.Percent(100);

            scrollView.Add(_contentContainer);
            Add(scrollView);
        }

        // ─── Главный экран (диспетчер по контексту) ──────────────────────────

        private void ShowCategories()
        {
            _showingMethodsCategory = false;
            _showingClassesCategory = false;
            _contentContainer.Clear();

            switch (GetCurrentContext())
            {
                case GraphContext.Main:
                    ShowMainGraphContent();
                    return;
                case GraphContext.SubspaceExpr:
                    ShowExprContent();
                    return;
                case GraphContext.MethodParam:
                    ShowParamContent();
                    return;
                default: // MethodBody, SubspaceBody
                    ShowExecutionContent();
                    return;
            }
        }

        // ─── Контент: главный граф (только классы) ────────────────────────────

        private void ShowMainGraphContent()
        {
            _contentContainer.Add(CreateClassesCategoryButton());
        }

        // ─── Контент: тело метода / подпространство (execution-ноды) ─────────

        private void ShowExecutionContent()
        {
            // Категория «Методы» — циановая
            _contentContainer.Add(CreateMethodsCategoryButton());

            // Разделитель
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            sep.style.marginTop       = 4;
            sep.style.marginBottom    = 4;
            _contentContainer.Add(sep);

            // Стандартные execution-категории
            foreach (var cat in _categories)
                _contentContainer.Add(CreateCategoryButton(cat.Key));
        }

        // ─── Контент: expression-подпространство (только чистые выражения) ───

        private static readonly HashSet<string> ExprCategories = new(StringComparer.Ordinal)
            { "Literals", "Math", "Comparison", "Logic", "Conversion" };

        private void ShowExprContent()
        {
            foreach (var cat in _categories)
            {
                if (ExprCategories.Contains(cat.Key))
                    _contentContainer.Add(CreateCategoryButton(cat.Key));
            }
        }

        // ─── Контент: param-граф (параметры добавляются только через ПКМ) ────

        private void ShowParamContent()
        {
            var hint = new Label("ПКМ → Добавить параметр");
            hint.style.fontSize    = 10;
            hint.style.color       = new Color(0.6f, 0.6f, 0.6f);
            hint.style.paddingLeft = 8;
            hint.style.paddingTop  = 10;
            hint.style.whiteSpace  = WhiteSpace.Normal;
            _contentContainer.Add(hint);
        }

        // ─── Экран классов ────────────────────────────────────────────────────

        private VisualElement CreateClassesCategoryButton()
        {
            int count = ClassRegistry.Classes.Count;
            var btn = new Button(ShowClassesCategory);
            btn.text = count > 0 ? $"Классы  ({count})" : "Классы";
            StyleCategoryButton(btn, ClassColor);
            return btn;
        }

        private void ShowClassesCategory()
        {
            _showingMethodsCategory = false;
            _showingClassesCategory = true;
            _contentContainer.Clear();

            var backBtn = new Button(ShowCategories) { text = "← Назад" };
            StyleBackButton(backBtn);
            _contentContainer.Add(backBtn);

            var titleLbl = new Label("Классы");
            titleLbl.style.fontSize               = 13;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color                   = ClassColor;
            titleLbl.style.paddingBottom           = 6;
            titleLbl.style.marginBottom            = 6;
            titleLbl.style.borderBottomWidth       = 1;
            titleLbl.style.borderBottomColor       = new Color(0.3f, 0.3f, 0.3f);
            titleLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _contentContainer.Add(titleLbl);

            var createBtn = new Button(OnCreateClassClicked) { text = "+ Создать класс" };
            createBtn.style.fontSize        = 12;
            createBtn.style.paddingTop      = 8;
            createBtn.style.paddingBottom   = 8;
            createBtn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            createBtn.style.marginBottom    = 8;
            createBtn.style.alignSelf       = Align.Stretch;
            createBtn.style.flexGrow        = 1;
            createBtn.style.width           = Length.Percent(100);
            ApplyBorder(createBtn, ClassColor);
            createBtn.RegisterCallback<MouseEnterEvent>(_ =>
                createBtn.style.backgroundColor = new Color(0.32f, 0.32f, 0.32f));
            createBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                createBtn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f));
            _contentContainer.Add(createBtn);

            var classes = ClassRegistry.Classes;
            if (classes.Count == 0)
            {
                var empty = new Label("  (нет классов)");
                empty.style.color    = new Color(0.5f, 0.5f, 0.5f);
                empty.style.fontSize = 11;
                _contentContainer.Add(empty);
                return;
            }

            foreach (var def in classes)
                _contentContainer.Add(BuildClassRow(def));
        }

        private VisualElement BuildClassRow(ClassDefinition def)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 2;
            row.style.marginBottom  = 2;

            // Кнопка «создать ClassNode»
            var placeBtn = new Button(() => CreateClassNodeOnGraph(def.Id));
            placeBtn.text                  = def.Name;
            placeBtn.tooltip               = $"Разместить ноду класса «{def.Name}» на графе";
            placeBtn.style.flexGrow        = 1;
            placeBtn.style.fontSize        = 12;
            placeBtn.style.paddingTop      = 6;
            placeBtn.style.paddingBottom   = 6;
            placeBtn.style.paddingLeft     = 8;
            placeBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            placeBtn.style.unityTextAlign  = TextAnchor.MiddleLeft;
            placeBtn.style.textOverflow    = TextOverflow.Ellipsis;
            placeBtn.style.overflow        = Overflow.Hidden;
            placeBtn.style.whiteSpace      = WhiteSpace.NoWrap;
            ApplyBorder(placeBtn, ClassColor);
            row.Add(placeBtn);

            // Кнопка «✎ открыть тело класса»
            var editBtn = new Button(() => OnOpenClassClicked(def.Id)) { text = "✎" };
            editBtn.tooltip               = "Открыть тело класса";
            editBtn.style.width           = 26;
            editBtn.style.fontSize        = 12;
            editBtn.style.paddingLeft     = 0;
            editBtn.style.paddingRight    = 0;
            editBtn.style.marginLeft      = 2;
            editBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            ApplyBorder(editBtn, new Color(0.6f, 0.6f, 0.6f));
            row.Add(editBtn);

            // Кнопка «✕ удалить»
            var delBtn = new Button(() => OnDeleteClassClicked(def.Id)) { text = "✕" };
            delBtn.tooltip               = "Удалить класс";
            delBtn.style.width           = 26;
            delBtn.style.fontSize        = 12;
            delBtn.style.paddingLeft     = 0;
            delBtn.style.paddingRight    = 0;
            delBtn.style.marginLeft      = 2;
            delBtn.style.backgroundColor = new Color(0.3f, 0.15f, 0.15f);
            ApplyBorder(delBtn, new Color(0.8f, 0.3f, 0.3f));
            row.Add(delBtn);

            return row;
        }

        // ─── Обработчики классов ──────────────────────────────────────────────

        private void OnClassesChanged()
        {
            if (_showingClassesCategory)
                ShowClassesCategory();
            else
                ShowCategories();
        }

        private void OnCreateClassClicked()
        {
            CreateClassPopup.ShowCreate(def =>
            {
                ClassRegistry.Add(def);
                VisualScriptingWindow.ActiveWindow?.OpenClassTab(def.Id);
            }, GetUniqueClassName());
        }

        private void OnOpenClassClicked(string classId)
        {
            VisualScriptingWindow.ActiveWindow?.OpenClassTab(classId);
        }

        private void OnDeleteClassClicked(string classId)
        {
            var def = ClassRegistry.GetById(classId);
            if (def == null) return;
            bool ok = EditorUtility.DisplayDialog(
                "Удалить класс",
                $"Удалить класс «{def.Name}»?\nВсе ноды этого класса станут недействительными.",
                "Удалить", "Отмена");
            if (!ok) return;
            VisualScriptingWindow.ActiveWindow?.CloseClassTab(classId);
            ClassRegistry.Remove(classId);
        }

        private void CreateClassNodeOnGraph(string classId)
        {
            if (_graphView == null || _graphView.graph == null)
            {
                UnityEngine.Debug.LogError("[NodeToolbarView] Graph is not initialized.");
                return;
            }

            var def = ClassRegistry.GetById(classId);
            if (def == null) return;

            Rect    graphRect    = _graphView.layout;
            Vector2 screenCenter = new Vector2(graphRect.width / 2f, graphRect.height / 2f);
#pragma warning disable 0618
            Vector2 pan   = (Vector2)_graphView.viewTransform.position;
            float   scale = _graphView.scale;
#pragma warning restore 0618
            Vector2 graphCenter = (screenCenter - pan) / scale;
            Vector2 finalPos    = FindFreePosition(graphCenter, 220, 100, 25f);

            var node = new ClassNode
            {
                ClassId   = def.Id,
                ClassName = def.Name
            };
            if (string.IsNullOrEmpty(node.GUID)) node.GUID = Guid.NewGuid().ToString();
            node.NodeId   = node.GUID;
            node.position = new Rect(finalPos.x, finalPos.y, 220, 100);

            try { _graphView.AddNode(node); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[NodeToolbarView] Failed to add class node: {e.Message}");
            }
        }

        private static string GetUniqueClassName()
        {
            var existing = new HashSet<string>(
                ClassRegistry.Classes.Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains("MyClass")) return "MyClass";
            int n = 1;
            while (existing.Contains($"MyClass{n}")) n++;
            return $"MyClass{n}";
        }

        // ─── Кнопка категории методов ─────────────────────────────────────────

        private VisualElement CreateMethodsCategoryButton()
        {
            int count = MethodRegistry.Methods.Count;
            var btn = new Button(() => ShowMethodsCategory());
            btn.text = count > 0 ? $"Методы  ({count})" : "Методы";
            StyleCategoryButton(btn, MethodColor);
            return btn;
        }

        // ─── Экран методов ────────────────────────────────────────────────────

        private void ShowMethodsCategory()
        {
            _showingMethodsCategory = true;
            _showingClassesCategory = false;
            _contentContainer.Clear();

            var backBtn = new Button(() => ShowCategories()) { text = "← Назад" };
            StyleBackButton(backBtn);
            _contentContainer.Add(backBtn);

            var titleLbl = new Label("Методы");
            titleLbl.style.fontSize               = 13;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color                   = MethodColor;
            titleLbl.style.paddingBottom           = 6;
            titleLbl.style.marginBottom            = 6;
            titleLbl.style.borderBottomWidth       = 1;
            titleLbl.style.borderBottomColor       = new Color(0.3f, 0.3f, 0.3f);
            titleLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _contentContainer.Add(titleLbl);

            var createBtn = new Button(OnCreateMethodClicked) { text = "+ Создать метод" };
            createBtn.style.fontSize        = 12;
            createBtn.style.paddingTop      = 8;
            createBtn.style.paddingBottom   = 8;
            createBtn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            createBtn.style.marginBottom    = 8;
            createBtn.style.alignSelf       = Align.Stretch;
            createBtn.style.flexGrow        = 1;
            createBtn.style.width           = Length.Percent(100);
            ApplyBorder(createBtn, MethodColor);
            createBtn.RegisterCallback<MouseEnterEvent>(_ =>
                createBtn.style.backgroundColor = new Color(0.32f, 0.32f, 0.32f));
            createBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                createBtn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f));
            _contentContainer.Add(createBtn);

            var methods = MethodRegistry.Methods;
            if (methods.Count == 0)
            {
                var empty = new Label("  (нет методов)");
                empty.style.color    = new Color(0.5f, 0.5f, 0.5f);
                empty.style.fontSize = 11;
                _contentContainer.Add(empty);
                return;
            }

            // Группируем методы по классу
            var classes = ClassRegistry.Classes;

            // Классы, у которых есть хотя бы один метод
            foreach (var cls in classes)
            {
                var classMethods = methods
                    .Where(m => string.Equals(m.ClassId, cls.Id, StringComparison.Ordinal))
                    .ToList();
                if (classMethods.Count == 0) continue;

                _contentContainer.Add(BuildClassGroupHeader(cls.Id, cls.Name, classMethods.Count));

                if (_expandedClassIds.Contains(cls.Id))
                {
                    foreach (var def in classMethods)
                        _contentContainer.Add(BuildMethodRow(def));
                }
            }

            // Методы без класса (legacy / ручное создание без ClassId)
            var orphans = methods
                .Where(m => string.IsNullOrEmpty(m.ClassId) ||
                            classes.All(c => !string.Equals(c.Id, m.ClassId, StringComparison.Ordinal)))
                .ToList();

            if (orphans.Count > 0)
            {
                const string orphanKey = "__orphan__";
                _contentContainer.Add(BuildClassGroupHeader(orphanKey, "Без класса", orphans.Count));
                if (_expandedClassIds.Contains(orphanKey))
                {
                    foreach (var def in orphans)
                        _contentContainer.Add(BuildMethodRow(def));
                }
            }
        }

        // ─── Заголовок группы класса ──────────────────────────────────────────

        private VisualElement BuildClassGroupHeader(string classId, string className, int methodCount)
        {
            bool expanded = _expandedClassIds.Contains(classId);

            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.marginTop       = 4;
            row.style.marginBottom    = 2;
            row.style.paddingLeft     = 4;
            row.style.paddingRight    = 4;
            row.style.paddingTop      = 4;
            row.style.paddingBottom   = 4;
            row.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f);
            row.style.borderTopLeftRadius     = 3;
            row.style.borderTopRightRadius    = 3;
            row.style.borderBottomLeftRadius  = expanded ? 0 : 3;
            row.style.borderBottomRightRadius = expanded ? 0 : 3;

            // Кнопка + / −
            string toggleIcon = expanded ? "−" : "+";
            var toggleBtn = new Button(() =>
            {
                if (_expandedClassIds.Contains(classId))
                    _expandedClassIds.Remove(classId);
                else
                    _expandedClassIds.Add(classId);
                ShowMethodsCategory();
            });
            toggleBtn.text                  = toggleIcon;
            toggleBtn.style.width           = 22;
            toggleBtn.style.height          = 22;
            toggleBtn.style.fontSize        = 14;
            toggleBtn.style.paddingLeft     = 0;
            toggleBtn.style.paddingRight    = 0;
            toggleBtn.style.paddingTop      = 0;
            toggleBtn.style.paddingBottom   = 0;
            toggleBtn.style.marginRight     = 6;
            toggleBtn.style.flexShrink      = 0;
            toggleBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            toggleBtn.style.unityTextAlign  = TextAnchor.MiddleCenter;
            ApplyBorder(toggleBtn, MethodColor);
            row.Add(toggleBtn);

            // Название класса
            var nameLabel = new Label($"{className}  ({methodCount})");
            nameLabel.style.fontSize               = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color                   = MethodColor;
            nameLabel.style.flexGrow                = 1;
            nameLabel.style.textOverflow            = TextOverflow.Ellipsis;
            nameLabel.style.overflow                = Overflow.Hidden;
            nameLabel.style.whiteSpace              = WhiteSpace.NoWrap;
            row.Add(nameLabel);

            return row;
        }

        private VisualElement BuildMethodRow(MethodDefinition def)
        {
            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.marginTop       = 0;
            row.style.marginBottom    = 1;
            row.style.paddingLeft     = 10;  // отступ — визуальная вложенность

            // Кнопка «создать call-ноду»
            var callBtn = new Button(() => CreateMethodCallNode(def.Id));
            callBtn.text                  = def.Name;
            callBtn.tooltip               = def.Signature();
            callBtn.style.flexGrow        = 1;
            callBtn.style.fontSize        = 12;
            callBtn.style.paddingTop      = 5;
            callBtn.style.paddingBottom   = 5;
            callBtn.style.paddingLeft     = 8;
            callBtn.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
            callBtn.style.unityTextAlign  = TextAnchor.MiddleLeft;
            callBtn.style.textOverflow    = TextOverflow.Ellipsis;
            callBtn.style.overflow        = Overflow.Hidden;
            callBtn.style.whiteSpace      = WhiteSpace.NoWrap;
            ApplyBorder(callBtn, new Color(0.3f, 0.6f, 0.7f));
            row.Add(callBtn);

            // Кнопка «✎ редактировать»
            var editBtn = new Button(() => OnEditMethodClicked(def.Id)) { text = "✎" };
            editBtn.tooltip               = "Редактировать";
            editBtn.style.width           = 24;
            editBtn.style.fontSize        = 11;
            editBtn.style.paddingLeft     = 0;
            editBtn.style.paddingRight    = 0;
            editBtn.style.marginLeft      = 2;
            editBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            ApplyBorder(editBtn, new Color(0.5f, 0.5f, 0.5f));
            row.Add(editBtn);

            // Кнопка «✕ удалить»
            var delBtn = new Button(() => OnDeleteMethodClicked(def.Id)) { text = "✕" };
            delBtn.tooltip               = "Удалить";
            delBtn.style.width           = 24;
            delBtn.style.fontSize        = 11;
            delBtn.style.paddingLeft     = 0;
            delBtn.style.paddingRight    = 0;
            delBtn.style.marginLeft      = 2;
            delBtn.style.backgroundColor = new Color(0.3f, 0.15f, 0.15f);
            ApplyBorder(delBtn, new Color(0.8f, 0.3f, 0.3f));
            row.Add(delBtn);

            return row;
        }

        // ─── Обработчики методов ──────────────────────────────────────────────

        private void OnMethodsChanged()
        {
            if (_showingMethodsCategory)
                ShowMethodsCategory();
            else
                ShowCategories(); // обновляем счётчик на кнопке «Методы (N)»
        }

        private void OnCreateMethodClicked()
        {
            CreateMethodPopup.ShowCreate(def =>
            {
                MethodRegistry.Add(def);
                VisualScriptingWindow.ActiveWindow?.OpenMethodTab(def.Id);
            }, GetUniqueMethodName());
        }

        private void OnEditMethodClicked(string id)
        {
            var def = MethodRegistry.GetById(id);
            if (def == null) return;

            // Принудительно синхронизируем рантайм → def.Parameters,
            // чтобы попап отображал актуальный список параметров из графа
            VisualScriptingWindow.ActiveWindow?.ForceSyncMethodRuntime(id);

            CreateMethodPopup.ShowEdit(def, updated =>
            {
                MethodRegistry.Update(updated);
                VisualScriptingWindow.ActiveWindow?.RefreshMethodTabTitle(updated.Id, updated.Name);
                // Синхронизируем граф параметров из обновлённого def.Parameters
                VisualScriptingWindow.ActiveWindow?.SyncParamGraphFromDefinition(updated.Id);
            });
        }

        private void OnDeleteMethodClicked(string id)
        {
            var def = MethodRegistry.GetById(id);
            if (def == null) return;
            bool ok = EditorUtility.DisplayDialog(
                "Удалить метод",
                $"Удалить метод «{def.Name}»?\nВсе ноды вызова этого метода станут недействительными.",
                "Удалить", "Отмена");
            if (!ok) return;
            VisualScriptingWindow.ActiveWindow?.CloseMethodTab(id);
            MethodRegistry.Remove(id);
        }

        // ─── Уникальное имя метода ────────────────────────────────────────────

        private static string GetUniqueMethodName()
        {
            var existing = new HashSet<string>(
                MethodRegistry.Methods.Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains("MyMethod")) return "MyMethod";
            int n = 1;
            while (existing.Contains($"MyMethod{n}")) n++;
            return $"MyMethod{n}";
        }

        // ─── Создание call-ноды метода ────────────────────────────────────────

        private void CreateMethodCallNode(string methodId)
        {
            if (_graphView == null || _graphView.graph == null)
            {
                UnityEngine.Debug.LogError("[NodeToolbarView] Graph is not initialized.");
                return;
            }

            var def = MethodRegistry.GetById(methodId);
            if (def == null) return;

            Rect    graphRect    = _graphView.layout;
            Vector2 screenCenter = new Vector2(graphRect.width / 2f, graphRect.height / 2f);
#pragma warning disable 0618
            Vector2 pan   = (Vector2)_graphView.viewTransform.position;
            float   scale = _graphView.scale;
#pragma warning restore 0618
            Vector2 graphCenter = (screenCenter - pan) / scale;
            Vector2 finalPos    = FindFreePosition(graphCenter, 220, 100, 25f);

            var classDef = ClassRegistry.GetById(def.ClassId);
            var node = new MethodCallNode
            {
                MethodId         = def.Id,
                MethodName       = def.Name,
                ClassName        = classDef?.Name ?? "",
                ReturnType       = def.ReturnType,
                ActiveParamCount = Mathf.Min(def.Parameters.Count, MethodCallNode.MaxParams),
                ParamNames       = new string[MethodCallNode.MaxParams],
                ParamTypes       = new string[MethodCallNode.MaxParams]
            };
            for (int i = 0; i < MethodCallNode.MaxParams; i++)
            {
                if (i < def.Parameters.Count)
                {
                    node.ParamNames[i] = def.Parameters[i].Name;
                    node.ParamTypes[i] = def.Parameters[i].Type;
                }
            }

            if (string.IsNullOrEmpty(node.GUID)) node.GUID = Guid.NewGuid().ToString();
            node.NodeId    = node.GUID;
            node.position  = new Rect(finalPos.x, finalPos.y, 220, 100);

            try { _graphView.AddNode(node); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[NodeToolbarView] Failed to add method node: {e.Message}");
            }
        }

        // ─── Стандартные категории ────────────────────────────────────────────

        private VisualElement CreateCategoryButton(string category)
        {
            var btn = new Button(() => ShowNodesForCategory(category));
            btn.text = FilteredCreateMenuBaseGraphView.TranslateCategory(category);
            StyleCategoryButton(btn, CategoryColors.TryGetValue(category, out var c) ? c : Color.gray);
            return btn;
        }

        private void ShowNodesForCategory(string category)
        {
            _contentContainer.Clear();

            var backBtn = new Button(ShowCategories) { text = "← Назад" };
            StyleBackButton(backBtn);
            _contentContainer.Add(backBtn);

            var titleLbl = new Label(FilteredCreateMenuBaseGraphView.TranslateCategory(category));
            titleLbl.style.fontSize               = 13;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color                   = Color.white;
            titleLbl.style.paddingBottom           = 8;
            titleLbl.style.marginBottom            = 8;
            titleLbl.style.borderBottomWidth       = 1;
            titleLbl.style.borderBottomColor       = new Color(0.3f, 0.3f, 0.3f);
            titleLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
            titleLbl.style.whiteSpace              = WhiteSpace.NoWrap;
            titleLbl.style.textOverflow            = TextOverflow.Ellipsis;
            titleLbl.style.overflow                = Overflow.Hidden;
            _contentContainer.Add(titleLbl);

            if (_categories.TryGetValue(category, out var nodes))
            {
                Color catColor = CategoryColors.TryGetValue(category, out var cc) ? cc : Color.gray;
                foreach (var node in nodes.OrderBy(n => n.path))
                    _contentContainer.Add(CreateNodeButton(node.type, node.path, catColor));
            }
        }

        private VisualElement CreateNodeButton(Type nodeType, string displayName, Color categoryColor)
        {
            var shortName = displayName.Split('/').Last();
            var btn = new Button(() => CreateNodeAtCenter(nodeType));
            btn.text            = shortName;
            btn.tooltip         = displayName;
            btn.style.fontSize  = 12;
            btn.style.paddingTop    = 8;
            btn.style.paddingBottom = 8;
            btn.style.paddingLeft   = 12;
            btn.style.paddingRight  = 12;
            btn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            btn.style.alignSelf   = Align.Stretch;
            btn.style.marginLeft  = 0;
            btn.style.marginRight = 0;
            btn.style.marginTop   = 2;
            btn.style.marginBottom = 2;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.flexGrow    = 1;
            btn.style.width       = Length.Percent(100);
            btn.style.whiteSpace  = WhiteSpace.NoWrap;
            btn.style.textOverflow = TextOverflow.Ellipsis;
            btn.style.overflow    = Overflow.Hidden;
            ApplyBorder(btn, categoryColor);
            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.38f, 0.38f, 0.38f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f));
            return btn;
        }

        // ─── Вспомогательные ──────────────────────────────────────────────────

        private void CreateNodeAtCenter(Type nodeType)
        {
            if (_graphView == null || _graphView.graph == null)
            {
                UnityEngine.Debug.LogError("[NodeToolbarView] Graph is not initialized.");
                return;
            }

            Rect    graphRect    = _graphView.layout;
            Vector2 screenCenter = new Vector2(graphRect.width / 2f, graphRect.height / 2f);
#pragma warning disable 0618
            Vector2 pan   = (Vector2)_graphView.viewTransform.position;
            float   scale = _graphView.scale;
#pragma warning restore 0618
            Vector2 graphCenter = (screenCenter - pan) / scale;
            Vector2 finalPos    = FindFreePosition(graphCenter, 200, 100, 25f);

            var node = (BaseNode)Activator.CreateInstance(nodeType);
            if (node == null) return;
            if (string.IsNullOrEmpty(node.GUID)) node.GUID = Guid.NewGuid().ToString();
            node.position = new Rect(finalPos.x, finalPos.y, 200, 100);

            try { _graphView.AddNode(node); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[NodeToolbarView] Failed to add node: {e.Message}");
                return;
            }
            ShowCategories();
        }

        private Vector2 FindFreePosition(Vector2 desiredCenter, float nodeWidth, float nodeHeight, float gap)
        {
            if (_graphView == null || _graphView.graph == null) return desiredCenter;
            var existing = _graphView.graph.nodes.Where(n => n != null).Select(n => n.position).ToList();

            bool Overlaps(Rect r, Rect other)
            {
                var exp = new Rect(r.x - gap, r.y - gap, r.width + gap * 2, r.height + gap * 2);
                return exp.Overlaps(other);
            }

            var proposed = new Rect(desiredCenter.x - nodeWidth / 2, desiredCenter.y - nodeHeight / 2, nodeWidth, nodeHeight);
            if (!existing.Any(r => Overlaps(proposed, r))) return desiredCenter;

            for (int attempt = 1; attempt <= 80; attempt++)
            {
                float radius = 30f * attempt;
                for (float angle = 0; angle < 360f; angle += 25f)
                {
                    float rad       = angle * Mathf.Deg2Rad;
                    Vector2 offset  = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
                    Vector2 cand    = desiredCenter + offset;
                    var     candRect = new Rect(cand.x - nodeWidth / 2, cand.y - nodeHeight / 2, nodeWidth, nodeHeight);
                    if (!existing.Any(r => Overlaps(candRect, r))) return cand;
                }
            }
            return desiredCenter + new Vector2(
                UnityEngine.Random.Range(-80f, 80f),
                UnityEngine.Random.Range(-80f, 80f));
        }

        private Dictionary<string, List<(string path, Type type)>> GetCategories()
        {
            var cats = new Dictionary<string, List<(string, Type)>>();
            foreach (var entry in NodeProvider.GetNodeMenuEntries(null))
            {
                if (ShouldHideMenuPath(entry.path)) continue;
                var cat = entry.path.Split('/')[0];
                if (!cats.ContainsKey(cat)) cats[cat] = new List<(string, Type)>();
                cats[cat].Add((entry.path, entry.type));
            }
            return cats;
        }

        private static bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("Utils/")  || path.StartsWith("Utils")  ||
                   path.StartsWith("Unity/")  || path.StartsWith("Unity")  ||
                   path.StartsWith("Method/") || path.StartsWith("Method") ||
                   path.StartsWith("Class/")  || path.StartsWith("Class");
        }

        // ─── Стилизация ──────────────────────────────────────────────────────

        private static void StyleCategoryButton(Button btn, Color borderColor)
        {
            btn.style.fontSize      = 13;
            btn.style.paddingTop    = 8;
            btn.style.paddingBottom = 8;
            btn.style.paddingLeft   = 12;
            btn.style.paddingRight  = 12;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            btn.style.marginLeft    = 0;
            btn.style.marginRight   = 0;
            btn.style.marginTop     = 4;
            btn.style.marginBottom  = 4;
            btn.style.whiteSpace    = WhiteSpace.NoWrap;
            btn.style.textOverflow  = TextOverflow.Ellipsis;
            btn.style.overflow      = Overflow.Hidden;
            btn.style.alignSelf     = Align.Stretch;
            btn.style.flexGrow      = 1;
            btn.style.width         = Length.Percent(100);
            ApplyBorder(btn, borderColor);
            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f));
        }

        private static void StyleBackButton(Button btn)
        {
            btn.style.fontSize        = 12;
            btn.style.paddingTop      = 6;
            btn.style.paddingBottom   = 6;
            btn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            btn.style.marginBottom    = 8;
            btn.style.alignSelf       = Align.Stretch;
            btn.style.flexGrow        = 1;
            btn.style.width           = Length.Percent(100);
            btn.style.whiteSpace      = WhiteSpace.NoWrap;
            btn.style.textOverflow    = TextOverflow.Ellipsis;
            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.32f, 0.32f, 0.32f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f));
        }

        private static void ApplyBorder(VisualElement el, Color color)
        {
            el.style.borderTopWidth    = 2;
            el.style.borderBottomWidth = 2;
            el.style.borderLeftWidth   = 2;
            el.style.borderRightWidth  = 2;
            el.style.borderTopColor    = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor   = color;
            el.style.borderRightColor  = color;
        }

        private static Color HexColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }
    }
}
