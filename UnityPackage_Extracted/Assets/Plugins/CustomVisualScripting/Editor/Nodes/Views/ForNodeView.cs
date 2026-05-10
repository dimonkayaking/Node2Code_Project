using UnityEngine;
using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    [NodeCustomEditor(typeof(ForNode))]
    public class ForNodeView : BaseNodeView, IFlowSubGraphNodeMinBounds
    {
        private ForNode _node;
        private SubGraphPanel _initPanel;
        private SubGraphPanel _conditionPanel;
        private SubGraphPanel _incrementPanel;
        private SubGraphPanel _bodyPanel;
        private VisualElement _panelsContainer;
        /// <summary>Оболочка как у <see cref="SubGraphPanel"/>: рамка + скругления вокруг шапки «Условия» и строки панелей.</summary>
        private VisualElement _conditionsSectionFrame;
        private VisualElement _conditionsHeaderRow;
        private VisualElement _conditionsRow;
        private Label _conditionsCollapseToggle;
        private Label _collapseToggle;
        private VisualElement _subspaceLinksRow;
        private bool _panelsExpanded = true;
        private bool _conditionsExpanded = true;
        private IVisualElementScheduledItem _syncBoundsTask;

        public override void Enable()
        {
            base.Enable();

            _node = nodeTarget as ForNode;
            if (_node == null) return;
            RehydrateSubGraphsFromActiveWindow();
            CleanupUi();

            if (controlsContainer == null)
            {
                controlsContainer = new VisualElement();
                controlsContainer.name = "controls";
                mainContainer.Add(controlsContainer);
            }

            _collapseToggle = new Label("\u25BC");
            _collapseToggle.style.position = Position.Absolute;
            _collapseToggle.style.right = 8;
            _collapseToggle.style.top = 4;
            _collapseToggle.style.fontSize = 12;
            _collapseToggle.style.color = new Color(0.7f, 0.7f, 0.7f);
            _collapseToggle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _collapseToggle.style.width = 20;
            _collapseToggle.style.height = 20;
            _collapseToggle.RegisterCallback<MouseDownEvent>(e =>
            {
                TogglePanels();
                e.StopPropagation();
            });
            titleContainer.Add(_collapseToggle);

            _subspaceLinksRow = SubspaceHeaderLinkRow.Create(
                titleContainer,
                _node.NodeId,
                ("\u041E\u0431\u044A\u044F\u0432\u043B\u0435\u043D\u0438\u0435", SubspaceKind.Init),
                ("\u0413\u0440\u0430\u043D\u0438\u0446\u0430", SubspaceKind.Condition),
                ("\u0428\u0430\u0433", SubspaceKind.Increment),
                ("\u0422\u0435\u043B\u043E", SubspaceKind.Body));
            titleContainer.Add(_subspaceLinksRow);

            _panelsContainer = new VisualElement();
            _panelsContainer.style.flexDirection = FlexDirection.Column;
            _panelsContainer.style.minWidth = 600;
            _panelsContainer.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));

            _conditionsSectionFrame = new VisualElement();
            _conditionsSectionFrame.style.flexDirection = FlexDirection.Column;
            _conditionsSectionFrame.style.marginTop = 4;
            _conditionsSectionFrame.style.marginBottom = 4;
            _conditionsSectionFrame.style.borderTopWidth = 1;
            _conditionsSectionFrame.style.borderBottomWidth = 1;
            _conditionsSectionFrame.style.borderLeftWidth = 1;
            _conditionsSectionFrame.style.borderRightWidth = 1;
            var frameBorder = new Color(0.3f, 0.3f, 0.3f);
            _conditionsSectionFrame.style.borderTopColor = frameBorder;
            _conditionsSectionFrame.style.borderBottomColor = frameBorder;
            _conditionsSectionFrame.style.borderLeftColor = frameBorder;
            _conditionsSectionFrame.style.borderRightColor = frameBorder;
            _conditionsSectionFrame.style.borderTopLeftRadius = 4;
            _conditionsSectionFrame.style.borderTopRightRadius = 4;
            _conditionsSectionFrame.style.borderBottomLeftRadius = 4;
            _conditionsSectionFrame.style.borderBottomRightRadius = 4;
            _conditionsSectionFrame.style.overflow = Overflow.Hidden;

            _conditionsHeaderRow = new VisualElement();
            _conditionsHeaderRow.style.flexDirection = FlexDirection.Row;
            _conditionsHeaderRow.style.alignItems = Align.Center;
            _conditionsHeaderRow.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            _conditionsHeaderRow.style.paddingLeft = 8;
            _conditionsHeaderRow.style.paddingRight = 8;
            _conditionsHeaderRow.style.paddingTop = 4;
            _conditionsHeaderRow.style.paddingBottom = 4;

            _conditionsCollapseToggle = new Label("\u25BC");
            _conditionsCollapseToggle.style.width = 16;
            _conditionsCollapseToggle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _conditionsCollapseToggle.style.fontSize = 10;
            _conditionsCollapseToggle.style.color = new Color(0.7f, 0.7f, 0.7f);
            _conditionsCollapseToggle.RegisterCallback<MouseDownEvent>(e =>
            {
                ToggleConditionsStrip();
                e.StopPropagation();
            });

            var conditionsTitle = new Label("\u0423\u0441\u043B\u043E\u0432\u0438\u044F");
            conditionsTitle.style.flexGrow = 1;
            conditionsTitle.style.color = Color.white;
            conditionsTitle.style.fontSize = 11;
            conditionsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;

            _conditionsHeaderRow.Add(_conditionsCollapseToggle);
            _conditionsHeaderRow.Add(conditionsTitle);

            _conditionsRow = new VisualElement();
            _conditionsRow.style.flexDirection = FlexDirection.Row;
            _conditionsRow.style.justifyContent = Justify.FlexStart;
            _conditionsRow.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));

            _initPanel = new SubGraphPanel("\u041E\u0431\u044A\u044F\u0432\u043B\u0435\u043D\u0438\u0435", _node.initSubGraph, false, false, showHeaderCollapseToggle: false);
            _initPanel.style.flexGrow = 1;
            _initPanel.style.flexShrink = 1;
            _initPanel.style.minWidth = 140;
            _initPanel.style.marginRight = 4;
            _initPanel.OnChanged += OnSubGraphChanged;
            _initPanel.OnPanelResized += OnTopRowPanelResized;
            _conditionsRow.Add(_initPanel);

            _conditionPanel = new SubGraphPanel("\u0413\u0440\u0430\u043D\u0438\u0446\u0430", _node.conditionSubGraph, true, false, showHeaderCollapseToggle: false);
            _conditionPanel.style.flexGrow = 1;
            _conditionPanel.style.flexShrink = 1;
            _conditionPanel.style.minWidth = 140;
            _conditionPanel.style.marginRight = 4;
            _conditionPanel.OnChanged += OnSubGraphChanged;
            _conditionPanel.OnPanelResized += OnTopRowPanelResized;
            _conditionsRow.Add(_conditionPanel);

            _incrementPanel = new SubGraphPanel("\u0428\u0430\u0433", _node.incrementSubGraph, false, false, showHeaderCollapseToggle: false);
            _incrementPanel.style.flexGrow = 1;
            _incrementPanel.style.flexShrink = 1;
            _incrementPanel.style.minWidth = 140;
            _incrementPanel.OnChanged += OnSubGraphChanged;
            _incrementPanel.OnPanelResized += OnTopRowPanelResized;
            _conditionsRow.Add(_incrementPanel);

            _conditionsSectionFrame.Add(_conditionsHeaderRow);
            _conditionsSectionFrame.Add(_conditionsRow);
            _panelsContainer.Add(_conditionsSectionFrame);

            _bodyPanel = new SubGraphPanel(
                "\u0422\u0435\u043B\u043E",
                _node.bodySubGraph,
                false,
                verticalResizeOnly: true);
            _bodyPanel.OnChanged += OnSubGraphChanged;
            _bodyPanel.OnPanelResized += OnBodyPanelResized;
            _bodyPanel.style.alignSelf = Align.Stretch;
            _bodyPanel.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            _panelsContainer.Add(_bodyPanel);

            controlsContainer.Add(_panelsContainer);

            title = "For Loop";

            NodeViewBoundsUtils.DisableGraphViewPortCollapse(this);
            RequestBoundsSync();

            schedule.Execute(() =>
            {
                if (_node == null) return;
                if (_initPanel != null) _initPanel.SetSubGraph(_node.initSubGraph);
                if (_conditionPanel != null) _conditionPanel.SetSubGraph(_node.conditionSubGraph);
                if (_incrementPanel != null) _incrementPanel.SetSubGraph(_node.incrementSubGraph);
                if (_bodyPanel != null) _bodyPanel.SetSubGraph(_node.bodySubGraph);
            }).ExecuteLater(50);
        }

        private void RehydrateSubGraphsFromActiveWindow()
        {
            var window = VisualScriptingWindow.ActiveWindow;
            if (window == null || _node == null || string.IsNullOrEmpty(_node.NodeId))
                return;
            if (!window.TryGetNodeDataById(_node.NodeId, out var nodeData))
                return;

            _node.initSubGraph = nodeData.InitSubGraph ?? _node.initSubGraph ?? new VisualScripting.Core.Models.GraphData();
            _node.conditionSubGraph = nodeData.ConditionSubGraph ?? _node.conditionSubGraph ?? new VisualScripting.Core.Models.GraphData();
            _node.incrementSubGraph = nodeData.IncrementSubGraph ?? _node.incrementSubGraph ?? new VisualScripting.Core.Models.GraphData();
            _node.bodySubGraph = nodeData.BodySubGraph ?? _node.bodySubGraph ?? new VisualScripting.Core.Models.GraphData();
        }

        private void ToggleConditionsStrip()
        {
            _conditionsExpanded = !_conditionsExpanded;
            _conditionsRow.style.display = _conditionsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _conditionsCollapseToggle.text = _conditionsExpanded ? "\u25BC" : "\u25B6";
            RequestBoundsSync();
        }

        private void TogglePanels()
        {
            _panelsExpanded = !_panelsExpanded;
            _panelsContainer.style.display = _panelsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _collapseToggle.text = _panelsExpanded ? "\u25BC" : "\u25B6";
            NodeViewBoundsUtils.SetFlowControlsMinHeightForCollapse(controlsContainer, _panelsExpanded);
            if (_panelsExpanded)
            {
                _initPanel?.RefreshGraphViewport();
                _conditionPanel?.RefreshGraphViewport();
                _incrementPanel?.RefreshGraphViewport();
                _bodyPanel?.RefreshGraphViewport();
                schedule.Execute(() =>
                {
                    if (!_panelsExpanded)
                        return;
                    _initPanel?.RefreshGraphViewport();
                    _conditionPanel?.RefreshGraphViewport();
                    _incrementPanel?.RefreshGraphViewport();
                    _bodyPanel?.RefreshGraphViewport();
                }).ExecuteLater(120);
            }

            RequestBoundsSync();
        }

        public void SetPanelsExpanded(bool expanded)
        {
            if (_panelsExpanded == expanded)
                return;
            TogglePanels();
        }

        private void OnSubGraphChanged()
        {
            _node.initSubGraph = _initPanel.SubGraph;
            _node.conditionSubGraph = _conditionPanel.SubGraph;
            _node.incrementSubGraph = _incrementPanel.SubGraph;
            _node.bodySubGraph = _bodyPanel.SubGraph;
            RequestBoundsSync();
        }

        private void OnTopRowPanelResized(SubGraphPanel source, Vector2 size)
        {
            float syncedHeight = size.y;
            foreach (var panel in new[] { _initPanel, _conditionPanel, _incrementPanel })
            {
                if (panel == null || panel == source)
                    continue;
                panel.style.height = syncedHeight;
            }

            RequestBoundsSync();
        }

        private void OnBodyPanelResized(SubGraphPanel _, Vector2 __)
        {
            RequestBoundsSync();
        }

        public (float minW, float minH) GetResolvedMinBounds()
        {
            if (!_panelsExpanded)
                return (NodeViewBoundsUtils.FlowForCollapsedMinWidth, NodeViewBoundsUtils.FlowForCollapsedMinHeight);
            if (!_conditionsExpanded && !_bodyPanel.IsGraphExpanded)
                return (NodeViewBoundsUtils.FlowForMinWidth, NodeViewBoundsUtils.FlowForConditionsHiddenAndBodyCollapsedMinHeight);
            if (!_conditionsExpanded)
                return (NodeViewBoundsUtils.FlowForMinWidth, NodeViewBoundsUtils.FlowForConditionsStripHiddenMinHeight);
            return (NodeViewBoundsUtils.FlowForMinWidth, NodeViewBoundsUtils.FlowForMinHeight);
        }

        private void RequestBoundsSync()
        {
            _syncBoundsTask?.Pause();
            _syncBoundsTask = schedule.Execute(() =>
            {
                NodeViewBoundsUtils.RunFlowBoundsSyncTwice(this, GetResolvedMinBounds, () => !_panelsExpanded,
                    () => !_conditionsExpanded && !_bodyPanel.IsGraphExpanded);
            });
            _syncBoundsTask.ExecuteLater(10);
        }

        private void CleanupUi()
        {
            _syncBoundsTask?.Pause();
            _syncBoundsTask = null;

            if (_initPanel != null)
            {
                _initPanel.OnChanged -= OnSubGraphChanged;
                _initPanel.OnPanelResized -= OnTopRowPanelResized;
            }

            if (_conditionPanel != null)
            {
                _conditionPanel.OnChanged -= OnSubGraphChanged;
                _conditionPanel.OnPanelResized -= OnTopRowPanelResized;
            }

            if (_incrementPanel != null)
            {
                _incrementPanel.OnChanged -= OnSubGraphChanged;
                _incrementPanel.OnPanelResized -= OnTopRowPanelResized;
            }

            if (_bodyPanel != null)
            {
                _bodyPanel.OnChanged -= OnSubGraphChanged;
                _bodyPanel.OnPanelResized -= OnBodyPanelResized;
            }

            if (_collapseToggle != null && _collapseToggle.parent == titleContainer)
                titleContainer.Remove(_collapseToggle);
            if (_subspaceLinksRow != null && _subspaceLinksRow.parent == titleContainer)
                titleContainer.Remove(_subspaceLinksRow);

            _panelsContainer?.RemoveFromHierarchy();

            _initPanel = null;
            _conditionPanel = null;
            _incrementPanel = null;
            _bodyPanel = null;
            _conditionsSectionFrame = null;
            _conditionsHeaderRow = null;
            _conditionsRow = null;
            _conditionsCollapseToggle = null;
            _panelsContainer = null;
            _collapseToggle = null;
            _subspaceLinksRow = null;
        }
    }
}
