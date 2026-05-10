using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Comparison;
using CustomVisualScripting.Editor.Nodes.Conversion;
using CustomVisualScripting.Editor.Nodes.Debug;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Nodes.Literals;
using CustomVisualScripting.Editor.Nodes.Logic;
using CustomVisualScripting.Editor.Nodes.Math;
using CustomVisualScripting.Editor.Nodes.Unity;
using CustomVisualScripting.Editor.Windows;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    public class SubGraphPanel : VisualElement
    {
        public event Action OnChanged;
        public event Action<SubGraphPanel, Vector2> OnPanelResized;
        private const float MinPanelWidth = 520f;
        private const float MinPanelHeight = 180f;

        // Nested graphs force node rect to min size; flow nodes need room for SubGraphPanel chrome
        // (MinPanelHeight per panel + title/ports) or headers/bodies overlap visually.
        private const float NestedFlowMinWidthIfWhile = 540f;
        private const float NestedFlowMinHeightIfWhile = 520f;
        private const float NestedFlowMinWidthElse = 540f;
        private const float NestedFlowMinHeightElse = 320f;
        private const float NestedFlowMinWidthFor = 640f;
        private const float NestedFlowMinHeightFor = 600f;

        private readonly string _title;
        private readonly bool _isConditionPanel;
        private readonly bool _verticalResizeOnly;
        /// <summary>Если false — только подпись секции без стрелки и без сворачивания строки заголовка.</summary>
        private readonly bool _showHeaderCollapseToggle;
        private GraphData _subGraph;

        private Label _toggleLabel;
        private VisualElement _content;
        private BaseGraph _internalGraph;
        private FilteredCreateMenuBaseGraphView _graphView;
        private IVisualElementScheduledItem _syncTicker;
        private EventCallback<GeometryChangedEvent> _contentViewportGeometryCb;
        private bool _isExpanded = true;
        private bool _isSyncing;
        private bool _didFirstGeometryRebuild;

        private const float ContentViewportMinLayoutPx = 80f;

        /// <summary>Совпадает с вычитанием «шапки» в MakePanelResizable: контент = высота панели − это значение.</summary>
        private const float HeaderChromePixels = 28f;
        private float _storedFlexGrow;
        private float _storedOuterHeight;
        public SubGraphPanel(
            string title,
            GraphData subGraph,
            bool isConditionPanel,
            bool verticalResizeOnly = false,
            bool showHeaderCollapseToggle = true)
        {
            _title = title;
            _subGraph = subGraph ?? new GraphData();
            _isConditionPanel = isConditionPanel;
            _verticalResizeOnly = verticalResizeOnly;
            _showHeaderCollapseToggle = showHeaderCollapseToggle;

            BuildUI();
            Rebuild();
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                SyncBackFromGraphView();
                DisposeGraph();
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_graphView == null)
                {
                    _didFirstGeometryRebuild = false;
                    Rebuild();
                }
            });
        }

        public GraphData SubGraph => _subGraph;

        /// <summary>True, если область графа развёрнута (не только строка заголовка).</summary>
        public bool IsGraphExpanded => _isExpanded;

        /// <summary>
        /// Вызывать, когда вложенная панель снова получила ненулевую область (например разворот тела flow-ноды):
        /// пересчитывает камеру GraphView так же, как после открытия вкладки подпространства.
        /// </summary>
        public void RefreshGraphViewport()
        {
            if (_graphView == null || !_isExpanded)
                return;

            void ResetViewportTransform()
            {
                if (_graphView == null)
                    return;

                var r = _graphView.layout;
                if (float.IsNaN(r.width) || float.IsNaN(r.height) || r.width < 10f || r.height < 10f)
                {
                    _graphView.schedule.Execute(ResetViewportTransform).ExecuteLater(50);
                    return;
                }

                _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);
            }

            _graphView.schedule.Execute(ResetViewportTransform).ExecuteLater(0);
            _graphView.schedule.Execute(ResetViewportTransform).ExecuteLater(50);
            _graphView.schedule.Execute(ResetViewportTransform).ExecuteLater(150);
        }

        public void SetSubGraph(GraphData subGraph)
        {
            _subGraph = subGraph ?? new GraphData();
            _didFirstGeometryRebuild = false;
            Rebuild();
        }

        public void Rebuild()
        {
            DisposeGraph();
            CreateGraphViewFromSubGraph();
            
            if (_isExpanded && _graphView != null)
            {
                _graphView.schedule.Execute(() =>
                {
                    if (_graphView != null)
                        _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);
                }).ExecuteLater(10);
            }
        }

        private void BuildUI()
        {
            style.marginTop = 4;
            style.marginBottom = 4;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor =
                new Color(0.3f, 0.3f, 0.3f);
            style.borderTopLeftRadius = style.borderTopRightRadius =
                style.borderBottomLeftRadius = style.borderBottomRightRadius = 4;
            style.minWidth = MinPanelWidth;
            style.minHeight = MinPanelHeight;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 4;
            header.style.borderTopLeftRadius = 4;
            header.style.borderTopRightRadius = 4;

            if (_showHeaderCollapseToggle)
            {
                _toggleLabel = new Label("\u25BC");
                _toggleLabel.style.width = 16;
                _toggleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                _toggleLabel.style.fontSize = 10;
                _toggleLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                header.Add(_toggleLabel);

                header.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button == 0)
                    {
                        ToggleExpanded();
                        e.StopPropagation();
                    }
                });
            }

            var titleLabel = new Label(_title);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.color = Color.white;
            titleLabel.style.fontSize = 11;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

            Add(header);

            _content = new VisualElement();
            _content.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            _content.style.borderBottomLeftRadius = 4;
            _content.style.borderBottomRightRadius = 4;
            _content.style.minHeight = MinPanelHeight;
            _content.style.overflow = Overflow.Hidden;
            Add(_content);

            MakePanelResizable();
        }

        private void ToggleExpanded()
        {
            if (_isExpanded)
            {
                _storedFlexGrow = resolvedStyle.flexGrow;
                _storedOuterHeight = resolvedStyle.height;
            }

            _isExpanded = !_isExpanded;

            if (_isExpanded)
            {
                ApplyExpandedPanelLayout();
                if (_graphView != null)
                {
                    _graphView.schedule.Execute(() =>
                    {
                        if (_graphView != null)
                            _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);
                    }).ExecuteLater(10);
                }
            }
            else
            {
                ApplyCollapsedPanelLayout();
            }

            if (_toggleLabel != null)
                _toggleLabel.text = _isExpanded ? "\u25BC" : "\u25B6";
            OnPanelResized?.Invoke(this, new Vector2(resolvedStyle.width, resolvedStyle.height));
        }

        private void ApplyExpandedPanelLayout()
        {
            _content.style.display = DisplayStyle.Flex;
            _content.style.minHeight = MinPanelHeight;
            style.minHeight = MinPanelHeight;
            style.flexGrow = _storedFlexGrow;

            if (_storedOuterHeight > HeaderChromePixels + 2f)
            {
                style.height = _storedOuterHeight;
                _content.style.height =
                    Mathf.Max(MinPanelHeight - 10f, _storedOuterHeight - HeaderChromePixels);
            }
            else
            {
                style.height = StyleKeyword.Auto;
                _content.style.height = StyleKeyword.Auto;
            }
        }

        private void ApplyCollapsedPanelLayout()
        {
            _content.style.display = DisplayStyle.None;
            _content.style.minHeight = 0;
            _content.style.height = StyleKeyword.Auto;

            style.flexGrow = 0;
            style.minHeight = HeaderChromePixels;
            style.height = HeaderChromePixels;
        }

        /// <summary>После Rebuild граф заново создан — повторно скрыть тело, если панель была свёрнута.</summary>
        private void ApplyCollapsedVisualStateIfNeeded()
        {
            if (_isExpanded)
                return;
            ApplyCollapsedPanelLayout();
        }

        private void CreateGraphViewFromSubGraph()
        {
            UnregisterContentViewportGeometryHook();
            _content.Clear();
            _internalGraph = ScriptableObject.CreateInstance<BaseGraph>();
            _internalGraph.hideFlags = HideFlags.HideAndDontSave;
            Undo.ClearUndo(_internalGraph);

            var nodeMap = new Dictionary<string, CustomBaseNode>();

            foreach (var nodeData in _subGraph.Nodes)
            {
                var node = CreateNodeFromData(nodeData);
                if (node == null)
                    continue;

                node.NodeId = nodeData.Id;
                node.InitializeFromData(nodeData);
                if (node.GUID != node.NodeId)
                    node.SetGUID(node.NodeId);

                ApplyNodeLiteralValues(node, nodeData);
                _internalGraph.AddNode(node);
                nodeMap[nodeData.Id] = node;
            }

            var ownerWindow = (EditorWindow)VisualScriptingWindow.ActiveWindow
                              ?? EditorWindow.focusedWindow
                              ?? Resources.FindObjectsOfTypeAll<VisualScriptingWindow>().FirstOrDefault();
            _graphView = new FilteredCreateMenuBaseGraphView(ownerWindow);
            _graphView.Initialize(_internalGraph);
            _graphView.style.flexGrow = 1;
            _graphView.style.minHeight = MinPanelHeight - 10f;
            _graphView.graphViewChanged += OnGraphViewChanged;
            _syncTicker = _graphView.schedule.Execute(SyncBackFromGraphView).Every(300);

            RestoreEdges(nodeMap);
            _content.Add(_graphView);
            GraphDataViewSync.ApplySavedVisualLayout(_subGraph, _graphView);
            ConfigureNodeViewSizing(_graphView.nodeViews);
            RefreshNodeViewsLayout(_graphView.nodeViews);
            GraphViewAutoLayout.ApplyIfNeededForNestedGraph(_subGraph, _graphView.nodeViews, MeasureNestedGraphCell);
            RefreshNodeViewsLayout(_graphView.nodeViews);
            _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);

            // One extra deferred pass after mount: GraphView/Ports geometry settles asynchronously.
            void DeferredLayoutPass()
            {
                if (_graphView == null)
                    return;
                ConfigureNodeViewSizing(_graphView.nodeViews);
                GraphViewAutoLayout.ApplyIfNeededForNestedGraph(_subGraph, _graphView.nodeViews, MeasureNestedGraphCell);
                RefreshNodeViewsLayout(_graphView.nodeViews);
                _graphView.UpdateViewTransform(Vector3.zero, Vector3.one);
            }

            _graphView.schedule.Execute(DeferredLayoutPass).ExecuteLater(50);
            _graphView.schedule.Execute(DeferredLayoutPass).ExecuteLater(200);

            if (!_didFirstGeometryRebuild)
                RegisterContentViewportGeometryHook();

            ApplyCollapsedVisualStateIfNeeded();
        }

        private void RegisterContentViewportGeometryHook()
        {
            if (_content == null)
                return;
            UnregisterContentViewportGeometryHook();
            _contentViewportGeometryCb = OnContentFirstSizedForViewport;
            _content.RegisterCallback(_contentViewportGeometryCb);
        }

        private void UnregisterContentViewportGeometryHook()
        {
            if (_contentViewportGeometryCb == null || _content == null)
                return;
            _content.UnregisterCallback(_contentViewportGeometryCb);
            _contentViewportGeometryCb = null;
        }

        private void OnContentFirstSizedForViewport(GeometryChangedEvent evt)
        {
            if (_graphView == null || !_isExpanded || _didFirstGeometryRebuild)
                return;
            var r = evt.newRect;
            if (r.width < ContentViewportMinLayoutPx || r.height < ContentViewportMinLayoutPx)
                return;

            _didFirstGeometryRebuild = true;
            UnregisterContentViewportGeometryHook();

            // Сохраняем текущие позиции (включая только что посчитанные AutoLayout) в _subGraph,
            // чтобы Rebuild через ApplySavedVisualLayout их восстановил.
            SyncBackFromGraphView();

            Rebuild();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_isSyncing)
                return change;

            SyncBackFromGraphView();
            return change;
        }

        private void SyncBackFromGraphView()
        {
            if (_graphView == null || _internalGraph == null)
                return;
                
            var r = _graphView.layout;
            if (!_isExpanded || float.IsNaN(r.width) || float.IsNaN(r.height) || r.width < 10f || r.height < 10f)
                return;

            _isSyncing = true;
            try
            {
                var graphNodes = _internalGraph.nodes.OfType<CustomBaseNode>().ToList();
                GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(_subGraph, graphNodes, _graphView);
                GraphDataViewSync.SaveVisualLayoutToGraphData(_subGraph, _internalGraph, _graphView);
            }
            finally
            {
                _isSyncing = false;
            }

            OnChanged?.Invoke();
        }

        private void RestoreEdges(Dictionary<string, CustomBaseNode> nodeMap)
        {
            GraphViewEdgeRestore.RestoreEdges(_graphView, _subGraph.Edges, nodeMap, validatePortDirections: false);
        }

        private static void ApplyNodeLiteralValues(CustomBaseNode node, NodeData nodeData)
        {
            if (node is IntNode intNode && int.TryParse(nodeData.Value, out int intVal))
                intNode.intValue = intVal;
            else if (node is FloatNode floatNode &&
                     float.TryParse(nodeData.Value, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                floatNode.floatValue = floatVal;
            else if (node is BoolNode boolNode && bool.TryParse(nodeData.Value, out bool boolVal))
                boolNode.boolValue = boolVal;
            else if (node is StringNode stringNode)
                stringNode.stringValue = nodeData.Value;
        }

        private CustomBaseNode CreateNodeFromData(NodeData data) =>
            EditorNodeFactory.Create(data,
                _isConditionPanel
                    ? EditorNodeFactoryContext.SubGraphConditionPanel
                    : EditorNodeFactoryContext.Default);

        private void DisposeGraph()
        {
            UnregisterContentViewportGeometryHook();
            if (_graphView != null)
            {
                _syncTicker?.Pause();
                _syncTicker = null;
                _graphView.graphViewChanged -= OnGraphViewChanged;
                _graphView.Dispose();
                _graphView = null;
            }

            if (_internalGraph != null)
            {
                Undo.ClearUndo(_internalGraph);
                ScriptableObject.DestroyImmediate(_internalGraph);
                _internalGraph = null;
            }
        }

        private static void ResolveSubGraphNodeMinSizes(BaseNodeView nodeView, out float minW, out float minH)
        {
            var resolved = NodeViewBoundsUtils.ResolveSyncMinBounds(nodeView);
            minW = resolved.minW;
            minH = resolved.minH;
            if (TryGetNestedSubGraphFlowMinDimensions(nodeView, out var nestedW, out var nestedH))
            {
                minW = Mathf.Max(minW, nestedW);
                minH = Mathf.Max(minH, nestedH);
            }
        }

        /// <summary>Размер ячейки для DAG-раскладки в подпространстве (вкладка и вложенная панель).</summary>
        public static (float Width, float Height) MeasureNestedGraphCell(BaseNodeView view)
        {
            ResolveSubGraphNodeMinSizes(view, out var mw, out var mh);
            var rect = view.GetPosition();
            return (Mathf.Max(rect.width, mw), Mathf.Max(rect.height, mh));
        }

        private static void ConfigureNodeViewSizing(IEnumerable<BaseNodeView> nodeViews)
        {
            foreach (var nodeView in nodeViews)
            {
                if (nodeView == null)
                    continue;

                ResolveSubGraphNodeMinSizes(nodeView, out var minW, out var minH);

                NodeViewBoundsUtils.ApplyNodeMinStyle(nodeView, minW, minH);
                NodeViewBoundsUtils.DisableGraphViewPortCollapse(nodeView);
                NodeViewBoundsUtils.MakeNodeEdgesResizable(nodeView);

                var rect = nodeView.GetPosition();
                var xy = NodeViewBoundsUtils.GetAuthoritativeNodeTopLeft(nodeView);
                var width = Mathf.Max(Mathf.Max(rect.width, 0f), minW);
                var height = Mathf.Max(Mathf.Max(rect.height, 0f), minH);
                nodeView.SetPosition(new Rect(xy.x, xy.y, width, height));
            }
        }

        /// <summary>
        /// Крупные минимумы только для flow-нод с вложенными панелями; остальные берут <see cref="NodeViewBoundsUtils.ResolveSyncMinBounds"/>.
        /// </summary>
        private static bool TryGetNestedSubGraphFlowMinDimensions(BaseNodeView nodeView, out float minW, out float minH)
        {
            minW = 0f;
            minH = 0f;

            switch (nodeView.nodeTarget)
            {
                case IfNode:
                case WhileNode:
                    minW = NestedFlowMinWidthIfWhile;
                    minH = NestedFlowMinHeightIfWhile;
                    return true;
                case ElseNode:
                    minW = NestedFlowMinWidthElse;
                    minH = NestedFlowMinHeightElse;
                    return true;
                case ForNode:
                    minW = NestedFlowMinWidthFor;
                    minH = NestedFlowMinHeightFor;
                    return true;
                default:
                    return false;
            }
        }

        private static void RefreshNodeViewsLayout(IReadOnlyList<BaseNodeView> nodeViews)
        {
            if (nodeViews == null)
                return;

            foreach (var nodeView in nodeViews)
            {
                if (nodeView == null)
                    continue;

                NodeViewBoundsUtils.DisableGraphViewPortCollapse(nodeView);
                ResolveSubGraphNodeMinSizes(nodeView, out var minW, out var minH);
                NodeViewBoundsUtils.ApplyNodeMinStyle(nodeView, minW, minH);
                NodeViewBoundsUtils.SyncNodeRectToLayout(nodeView, minW, minH);
            }
        }

        private void MakePanelResizable()
        {
            bool resizingBottom = false;
            bool resizingRight = false;

            var bottomResizer = new VisualElement();
            bottomResizer.style.position = Position.Absolute;
            bottomResizer.style.bottom = -4;
            bottomResizer.style.left = 0;
            bottomResizer.style.right = 0;
            bottomResizer.style.height = 8;
            bottomResizer.pickingMode = PickingMode.Position;
            bottomResizer.tooltip = "Потяните, чтобы изменить высоту панели";
            bottomResizer.RegisterCallback<PointerEnterEvent>(_ =>
                EditorUiPointerCursor.TryApply(bottomResizer, MouseCursor.ResizeVertical));
            bottomResizer.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!resizingBottom)
                    EditorUiPointerCursor.Clear(bottomResizer);
            });
            Add(bottomResizer);

            var rightResizer = new VisualElement();
            rightResizer.style.position = Position.Absolute;
            rightResizer.style.right = -4;
            rightResizer.style.top = 0;
            rightResizer.style.bottom = 0;
            rightResizer.style.width = 8;
            rightResizer.pickingMode = PickingMode.Position;
            rightResizer.tooltip = "Потяните, чтобы изменить ширину панели";
            rightResizer.RegisterCallback<PointerEnterEvent>(_ =>
                EditorUiPointerCursor.TryApply(rightResizer, MouseCursor.ResizeHorizontal));
            rightResizer.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!resizingRight)
                    EditorUiPointerCursor.Clear(rightResizer);
            });
            if (!_verticalResizeOnly)
                Add(rightResizer);

            var bottomDrag = new PointerRootDragSession(
                delta =>
                {
                    if (!resizingBottom)
                        return;
                    float rh = resolvedStyle.minHeight.value;
                    float minH = rh > 2f ? rh : MinPanelHeight;
                    float height = Mathf.Max(minH, resolvedStyle.height + delta.y);
                    style.height = height;
                    _content.style.height = height - 28f;
                    OnPanelResized?.Invoke(this, new Vector2(resolvedStyle.width, height));
                },
                () =>
                {
                    resizingBottom = false;
                    EditorUiPointerCursor.Clear(bottomResizer);
                });

            var rightDrag = new PointerRootDragSession(
                delta =>
                {
                    if (!resizingRight)
                        return;
                    float rw = resolvedStyle.minWidth.value;
                    float minW = rw > 2f ? rw : MinPanelWidth;
                    float width = Mathf.Max(minW, resolvedStyle.width + delta.x);
                    style.width = width;
                    OnPanelResized?.Invoke(this, new Vector2(width, resolvedStyle.height));
                },
                () =>
                {
                    resizingRight = false;
                    EditorUiPointerCursor.Clear(rightResizer);
                });

            bottomResizer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                resizingBottom = true;
                if (!bottomDrag.TryBeginFromPointer(bottomResizer, evt))
                {
                    resizingBottom = false;
                    return;
                }

                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            bottomResizer.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                if (!bottomDrag.TryBeginFromMouse(bottomResizer, evt))
                    return;
                resizingBottom = true;
                evt.StopPropagation();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            bottomResizer.RegisterCallback<DetachFromPanelEvent>(_ => bottomDrag.Teardown());

            rightResizer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                resizingRight = true;
                if (!rightDrag.TryBeginFromPointer(rightResizer, evt))
                {
                    resizingRight = false;
                    return;
                }

                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            rightResizer.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                if (!rightDrag.TryBeginFromMouse(rightResizer, evt))
                    return;
                resizingRight = true;
                evt.StopPropagation();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            rightResizer.RegisterCallback<DetachFromPanelEvent>(_ => rightDrag.Teardown());
        }
    }
}
