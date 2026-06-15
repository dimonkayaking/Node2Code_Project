using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Literals;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Nodes.Views;
using CustomVisualScripting.Integration.Models;
using CustomVisualScripting.Windows.Views;
using VisualScripting.Core.Models;
using CustomToolbar = CustomVisualScripting.Windows.Views.ToolbarView;
using CustomVisualScripting.Editor;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Methods;

namespace CustomVisualScripting.Editor.Windows
{
    public partial class VisualScriptingWindow
    {
        private NodeToolbarView _nodeToolbar;
        private TwoPaneSplitView _graphAreaSplitter;

        // Константы удалены – они определены в основном файле VisualScriptingWindow.cs

        private void CleanupGraph()
        {
            DisposeAllSubspaceRuntimes();
            DisposeAllMethodRuntimes();
            if (_graphView != null)
            {
                _graphView.graphViewChanged -= OnGraphViewChanged;
                _graphView.NodeViewAdded -= OnNodeViewAdded;
                _graphView.Dispose();
                _graphView = null;
            }
            if (_internalGraph != null)
            {
                DestroyImmediate(_internalGraph);
                _internalGraph = null;
            }
        }

        private void CreateGUI()
        {
            _currentGraph = new CompleteGraphData();
            _hasUnsavedChanges = false;
            InitializeTabsState();

            var root = rootVisualElement;
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/CustomVisualScripting/Windows/Styles/WindowStyles.uss");
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                root.styleSheets.Add(styleSheet);

            _toolbar = new CustomToolbar();
            _toolbar.ParseButton.clicked += OnParse;
            _toolbar.GenerateButton.clicked += OnGenerate;
            _toolbar.RunButton.clicked += OnRun;
            _toolbar.StopButton.clicked += OnStop;
            _toolbar.SaveButton.clicked += OnSave;
            _toolbar.SaveAsButton.clicked += OnSaveAs;
            _toolbar.LoadButton.clicked += OnLoad;
            _toolbar.ClearButton.clicked += OnClear;
            root.Add(_toolbar);

            var splitView = new TwoPaneSplitView(0, 350, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            splitView.style.marginTop = 0;
            splitView.style.marginBottom = 0;
            splitView.style.paddingTop = 0;
            splitView.style.paddingBottom = 0;

            _codeEditor = new CodeEditorView();
            splitView.Add(_codeEditor);

            var rightArea = new VisualElement();
            rightArea.style.flexGrow = 1;
            rightArea.style.flexDirection = FlexDirection.Column;
            rightArea.style.marginTop = 0;
            rightArea.style.marginBottom = 0;
            rightArea.style.paddingTop = 0;
            rightArea.style.paddingBottom = 0;

            BuildGraphAreaWithTabs(rightArea);

            _graphAreaSplitter = new TwoPaneSplitView(1, 200, TwoPaneSplitViewOrientation.Horizontal);
            _graphAreaSplitter.style.flexGrow = 1;
            _graphAreaSplitter.style.marginTop = 0;
            _graphAreaSplitter.style.marginBottom = 0;

            _graphHost.RemoveFromHierarchy();
            _graphAreaSplitter.Add(_graphHost);

            var tempStub = new VisualElement();
            _graphAreaSplitter.Add(tempStub);

            rightArea.Add(_graphAreaSplitter);
            splitView.Add(rightArea);
            root.Add(splitView);

            _errorPanel = new ErrorPanel();
            root.Add(_errorPanel);

            _consoleView = new ConsoleView();
            _consoleView.style.marginTop = 0;
            root.Add(_consoleView);

            _toolbar.SetStatusNormal("Готов к работе");

            // При первом открытии создаём класс Program + метод Main если реестры пусты
            EnsureProgramClassExists();

            UpdateGraphView();

            _graphAreaSplitter.Remove(tempStub);
            _nodeToolbar = new NodeToolbarView(_graphView);
            _graphAreaSplitter.Add(_nodeToolbar);

            float savedWidth = EditorPrefs.GetFloat("NodeToolbarWidthPref", 200f);
            _graphAreaSplitter.fixedPaneInitialDimension = savedWidth;
            _graphAreaSplitter.RegisterCallback<GeometryChangedEvent>(evt =>
                EditorPrefs.SetFloat("NodeToolbarWidthPref", _graphAreaSplitter.fixedPaneInitialDimension));

            // Глобальные горячие клавиши окна
            root.RegisterCallback<KeyDownEvent>(OnGlobalKeyDown, TrickleDown.TrickleDown);
        }

        private void OnGlobalKeyDown(KeyDownEvent evt)
        {
            // Не перехватываем если фокус в текстовом поле (кроме кода — там Enter/S нужны)
            bool ctrl = evt.ctrlKey || evt.commandKey;

            // F5 или Ctrl+Enter → Run
            if (evt.keyCode == KeyCode.F5 ||
                (ctrl && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)))
            {
                if (_toolbar != null && _toolbar.RunButton.enabledSelf)
                {
                    OnRun();
                    evt.StopPropagation();
                }
                return;
            }

            // Ctrl+S → Сохранить (Ctrl+Shift+S → Сохранить как)
            if (ctrl && evt.keyCode == KeyCode.S)
            {
                if (evt.shiftKey)
                    OnSaveAs();
                else
                    OnSave();
                evt.StopPropagation();
            }
        }

        private void RecreateGraphView()
        {
            // Явно отсоединяем старый view до Dispose, чтобы Unity не
            // рендерил его после пересоздания (иначе старые ноды «висят»)
            _graphView?.RemoveFromHierarchy();
            CleanupGraph();
            _graphHost?.Clear();
            UpdateGraphView();
        }

        private void UpdateGraphView()
        {
            _graphHost?.Clear();

            try
            {
                _internalGraph = ScriptableObject.CreateInstance<BaseGraph>();
                var nodeMap = new Dictionary<string, BaseNode>();

                if (_currentGraph?.LogicGraph?.Nodes != null)
                {
                    foreach (var nodeData in _currentGraph.LogicGraph.Nodes)
                    {
                        var node = CreateNodeFromData(nodeData);
                        if (node == null) continue;
                        node.NodeId = nodeData.Id;
                        node.InitializeFromData(nodeData);
                        if (node.GUID != node.NodeId) node.SetGUID(node.NodeId);
                        if (node is IntNode intNode && int.TryParse(nodeData.Value, out int intVal))
                            intNode.intValue = intVal;
                        else if (node is FloatNode floatNode && float.TryParse(nodeData.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                            floatNode.floatValue = floatVal;
                        else if (node is BoolNode boolNode && bool.TryParse(nodeData.Value, out bool boolVal))
                            boolNode.boolValue = boolVal;
                        else if (node is StringNode stringNode)
                            stringNode.stringValue = nodeData.Value;
                        if (node is IfNode ifNode)
                        {
                            ifNode.conditionSubGraph = nodeData.ConditionSubGraph ?? new GraphData();
                            ifNode.bodySubGraph = nodeData.BodySubGraph ?? new GraphData();
                        }
                        else if (node is ElseNode elseNode)
                        {
                            elseNode.bodySubGraph = nodeData.BodySubGraph ?? new GraphData();
                        }
                        else if (node is ForNode forNode)
                        {
                            forNode.initSubGraph = nodeData.InitSubGraph ?? new GraphData();
                            forNode.conditionSubGraph = nodeData.ConditionSubGraph ?? new GraphData();
                            forNode.incrementSubGraph = nodeData.IncrementSubGraph ?? new GraphData();
                            forNode.bodySubGraph = nodeData.BodySubGraph ?? new GraphData();
                        }
                        else if (node is WhileNode whileNode)
                        {
                            whileNode.conditionSubGraph = nodeData.ConditionSubGraph ?? new GraphData();
                            whileNode.bodySubGraph = nodeData.BodySubGraph ?? new GraphData();
                        }
                        _internalGraph.AddNode(node);
                        nodeMap[nodeData.Id] = node;
                    }
                }

                _graphView = new MainClassGraphView(this);
                _graphView.NodeViewAdded += OnNodeViewAdded;
                _graphView.Initialize(_internalGraph);
                _graphView.style.flexGrow = 1;
                _graphView.graphViewChanged += OnGraphViewChanged;

                if (_currentGraph?.LogicGraph?.Edges != null && nodeMap.Count > 0)
                    GraphViewEdgeRestore.RestoreEdges(_graphView, _currentGraph.LogicGraph.Edges, nodeMap);

                if (_currentGraph?.VisualNodes != null)
                {
                    foreach (var nodeView in _graphView.nodeViews)
                    {
                        if (nodeView.nodeTarget is not CustomBaseNode customNode) continue;
                        var visualNode = _currentGraph.VisualNodes.FirstOrDefault(v => v.NodeId == customNode.NodeId);
                        if (visualNode != null)
                            nodeView.SetPosition(new Rect(visualNode.Position, Vector2.zero));
                    }
                }

                if (_collapseFlowSubspacesOnNextRebuild)
                {
                    CollapseFlowSubspaceNodes(_graphView.nodeViews);
                    _collapseFlowSubspacesOnNextRebuild = false;
                }

                ConfigureNodeViewSizing(_graphView.nodeViews);
                if (_forceAutoLayoutNextUpdate)
                {
                    GraphViewAutoLayout.ApplyDagAutoLayout(
                        _currentGraph.LogicGraph,
                        _graphView.nodeViews,
                        GraphViewAutoLayout.MeasureMainGraphCell,
                        GraphViewAutoLayout.AutoLayoutSpacingX,
                        GraphViewAutoLayout.AutoLayoutSpacingY,
                        40f,
                        40f,
                        GraphViewAutoLayout.AutoLayoutColumnGap,
                        GraphViewAutoLayout.AutoLayoutRowGap,
                        GraphViewAutoLayout.DefaultMinNodeWidth);
                    GraphViewAutoLayout.ResolveOverlaps(_graphView.nodeViews);
                    _forceAutoLayoutNextUpdate = false;
                }
                else
                {
                    AutoLayoutIfNeeded();
                }

                SyncNodeBoundsToLayout(_graphView.nodeViews);
                _graphView.schedule.Execute(() =>
                {
                    if (_graphView?.nodeViews == null) return;
                    SyncNodeBoundsToLayout(_graphView.nodeViews);
                }).ExecuteLater(0);

                _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);
                _graphView.FrameAll();

                _graphHost?.Add(_graphView);
                DisplayActiveTabContent();

                // Обновляем ссылку у существующей панели (не пересоздаём)
                if (_nodeToolbar != null)
                {
                    _nodeToolbar.UpdateGraphView(_graphView);
                }

                _toolbar.SetStatusSuccess($"Граф готов — {_internalGraph.nodes.Count} нод");
                UpdateCodeEditorSyntaxColors();
            }
            catch (Exception e)
            {
                Debug.LogError($"[VS] Ошибка создания графа: {e.Message}\n{e.StackTrace}");
                ShowTextualGraph();
            }
        }

        private void ShowTextualGraph()
        {
            var info = new VisualElement();
            info.style.marginTop = 10;
            info.style.marginLeft = 10;
            info.style.flexGrow = 1;
            var label = new Label($"Граф: {_currentGraph.LogicGraph.Nodes.Count} нод, {_currentGraph.LogicGraph.Edges.Count} связей");
            label.style.color = Color.white;
            label.style.fontSize = 14;
            info.Add(label);
            _graphHost?.Add(info);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(ActiveWindow, this)) ActiveWindow = null;
            if (_hasUnsavedChanges)
            {
                bool save = EditorUtility.DisplayDialog("Несохранённые изменения", "Хотите сохранить граф перед закрытием?", "Сохранить", "Не сохранять");
                if (save) OnSave();
            }
            CleanupGraph();
        }

        private void ConfigureNodeViewSizing(IEnumerable<BaseNodeView> nodeViews)
        {
            foreach (var nodeView in nodeViews)
            {
                if (nodeView == null) continue;
                var mins = NodeViewBoundsUtils.ResolveSyncMinBounds(nodeView);
                NodeViewBoundsUtils.ApplyNodeMinStyle(nodeView, mins.minW, mins.minH);
                NodeViewBoundsUtils.DisableGraphViewPortCollapse(nodeView);
                NodeViewBoundsUtils.MakeNodeEdgesResizable(nodeView);
                var rect = nodeView.GetPosition();
                var xy = NodeViewBoundsUtils.GetAuthoritativeNodeTopLeft(nodeView);
                var width = Mathf.Max(rect.width, mins.minW);
                var height = Mathf.Max(rect.height, mins.minH);
                nodeView.SetPosition(new Rect(xy.x, xy.y, width, height));
                nodeView.UnregisterCallback<GeometryChangedEvent>(OnNodeGeometryChanged);
                nodeView.RegisterCallback<GeometryChangedEvent>(OnNodeGeometryChanged);
            }
        }

        private static void CollapseFlowSubspaceNodes(IEnumerable<BaseNodeView> nodeViews)
        {
            foreach (var nodeView in nodeViews)
            {
                switch (nodeView)
                {
                    case IfNodeView ifNodeView: ifNodeView.SetPanelsExpanded(false); break;
                    case ElseNodeView elseNodeView: elseNodeView.SetPanelsExpanded(false); break;
                    case ForNodeView forNodeView: forNodeView.SetPanelsExpanded(false); break;
                    case WhileNodeView whileNodeView: whileNodeView.SetPanelsExpanded(false); break;
                }
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_graphView?.nodeViews != null)
                ConfigureNodeViewSizing(_graphView.nodeViews);
            UpdateCodeEditorSyntaxColors();
            return change;
        }

        private void OnNodeViewAdded(BaseNodeView nodeView)
        {
            if (nodeView == null) return;
            ConfigureNodeViewSizing(new[] { nodeView });
            nodeView.schedule.Execute(() =>
            {
                if (nodeView.panel == null) return;
                ConfigureNodeViewSizing(new[] { nodeView });
                SyncNodeBoundsToLayout(nodeView);
                UpdateCodeEditorSyntaxColors();
            }).ExecuteLater(1);
        }

        // Цвет класса — совпадает с ClassColor в NodeToolbarView и кнопкой «Классы» в правой панели.
        private static readonly Color ClassNameHighlightColor  = new Color(0x4C / 255f, 0xAF / 255f, 0x50 / 255f); // #4CAF50
        // Цвет метода — совпадает с MethodColor в NodeToolbarView и кнопкой «Методы» в правой панели.
        private static readonly Color MethodNameHighlightColor = new Color(0x00 / 255f, 0xBC / 255f, 0xD4 / 255f); // #00BCD4

        internal void UpdateCodeEditorSyntaxColors()
        {
            if (_codeEditor == null) return;

            var colors = new Dictionary<string, Color>(StringComparer.Ordinal);

            // Читаем живые ноды из internal-графа (актуально при ручном добавлении нод)
            var liveNodes = _internalGraph?.nodes?.OfType<CustomBaseNode>();
            if (liveNodes != null)
            {
                foreach (var node in liveNodes)
                {
                    if (string.IsNullOrWhiteSpace(node.variableName)) continue;
                    if (colors.ContainsKey(node.variableName)) continue;
                    colors[node.variableName] = NodeViewBoundsUtils.GetNodeTypeOutlineColor(node.NodeType);
                }
            }

            // Если internal-граф пуст — берём данные из LogicGraph (после загрузки/парсинга)
            if (colors.Count == 0 && _currentGraph?.LogicGraph?.Nodes != null)
            {
                foreach (var nodeData in _currentGraph.LogicGraph.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(nodeData.VariableName)) continue;
                    if (colors.ContainsKey(nodeData.VariableName)) continue;
                    colors[nodeData.VariableName] = NodeViewBoundsUtils.GetNodeTypeOutlineColor(nodeData.Type);
                }
            }

            // Имена классов — зелёный (#4CAF50), совпадает с кнопкой «Классы» в панели справа.
            foreach (var cls in ClassRegistry.Classes)
            {
                if (string.IsNullOrWhiteSpace(cls.Name)) continue;
                colors[cls.Name] = ClassNameHighlightColor;
            }

            // Имена методов — циановый (#00BCD4), совпадает с кнопкой «Методы» в панели справа.
            foreach (var method in MethodRegistry.Methods)
            {
                if (string.IsNullOrWhiteSpace(method.Name)) continue;
                colors[method.Name] = MethodNameHighlightColor;
            }

            _codeEditor.SetNodeVariableColors(colors);
        }

        private void AutoLayoutIfNeeded()
        {
            if (_graphView == null || _graphView.nodeViews == null || _graphView.nodeViews.Count == 0)
                return;
            bool hasSavedPositions = _currentGraph?.VisualNodes != null &&
                                     _currentGraph.VisualNodes.Count >= _graphView.nodeViews.Count;
            bool hasMeaningfulSaved = hasSavedPositions && HasMeaningfulSavedPositions(_currentGraph.VisualNodes);
            GraphViewAutoLayout.ApplyIfNeededForMainGraph(_currentGraph.LogicGraph, _graphView.nodeViews,
                hasMeaningfulSaved);
        }

        private void OnNodeGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt?.currentTarget is not BaseNodeView nodeView) return;
            nodeView.schedule.Execute(() => SyncNodeBoundsToLayout(nodeView)).ExecuteLater(0);
        }

        private static void SyncNodeBoundsToLayout(IReadOnlyList<BaseNodeView> nodeViews)
        {
            if (nodeViews == null) return;
            foreach (var nodeView in nodeViews)
                SyncNodeBoundsToLayout(nodeView);
        }

        private static void SyncNodeBoundsToLayout(BaseNodeView nodeView)
        {
            if (nodeView == null) return;
            NodeViewBoundsUtils.PerformFullNodeAppearanceFix(nodeView);
        }

        private static bool HasMeaningfulSavedPositions(IReadOnlyList<VisualNodeData> visualNodes)
        {
            if (visualNodes == null || visualNodes.Count == 0) return false;
            var unique = new HashSet<string>();
            foreach (var vn in visualNodes)
            {
                var x = Mathf.RoundToInt(vn.Position.x);
                var y = Mathf.RoundToInt(vn.Position.y);
                unique.Add($"{x}:{y}");
            }
            return unique.Count > Math.Max(1, visualNodes.Count / 3);
        }
    }
}