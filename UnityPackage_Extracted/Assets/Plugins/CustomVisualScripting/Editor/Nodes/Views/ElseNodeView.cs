using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    [NodeCustomEditor(typeof(ElseNode))]
    public class ElseNodeView : BaseNodeView, IFlowSubGraphNodeMinBounds
    {
        private ElseNode _node;
        private SubGraphPanel _bodyPanel;
        private VisualElement _panelsContainer;
        private Label _collapseToggle;
        private VisualElement _subspaceLinksRow;
        private bool _panelsExpanded = true;
        private IVisualElementScheduledItem _syncBoundsTask;

        public override void Enable()
        {
            base.Enable();

            _node = nodeTarget as ElseNode;
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
            _collapseToggle.style.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
            _collapseToggle.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
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
                ("\u0422\u0435\u043B\u043E", SubspaceKind.Body));
            titleContainer.Add(_subspaceLinksRow);

            _panelsContainer = new VisualElement();
            _panelsContainer.style.minWidth = 350;

            _bodyPanel = new SubGraphPanel(
                "\u0422\u0435\u043B\u043E",
                _node.bodySubGraph,
                false);
            _bodyPanel.OnChanged += OnSubGraphChanged;
            _bodyPanel.OnPanelResized += OnPanelResized;
            _panelsContainer.Add(_bodyPanel);

            controlsContainer.Add(_panelsContainer);

            title = "Else";

            NodeViewBoundsUtils.DisableGraphViewPortCollapse(this);
            RequestBoundsSync();

            schedule.Execute(() =>
            {
                if (_node == null) return;
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

            _node.bodySubGraph = nodeData.BodySubGraph ?? _node.bodySubGraph ?? new VisualScripting.Core.Models.GraphData();
        }

        private void TogglePanels()
        {
            _panelsExpanded = !_panelsExpanded;
            _panelsContainer.style.display = _panelsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _collapseToggle.text = _panelsExpanded ? "\u25BC" : "\u25B6";
            NodeViewBoundsUtils.SetFlowControlsMinHeightForCollapse(controlsContainer, _panelsExpanded);
            if (_panelsExpanded)
            {
                _bodyPanel?.RefreshGraphViewport();
                schedule.Execute(() =>
                {
                    if (!_panelsExpanded)
                        return;
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
            _node.bodySubGraph = _bodyPanel.SubGraph;
            RequestBoundsSync();
        }

        private void OnPanelResized(SubGraphPanel _, UnityEngine.Vector2 __)
        {
            RequestBoundsSync();
        }

        public (float minW, float minH) GetResolvedMinBounds() =>
            _panelsExpanded
                ? (NodeViewBoundsUtils.FlowElseMinWidth, NodeViewBoundsUtils.FlowElseMinHeight)
                : (NodeViewBoundsUtils.FlowElseCollapsedMinWidth, NodeViewBoundsUtils.FlowElseCollapsedMinHeight);

        private void RequestBoundsSync()
        {
            _syncBoundsTask?.Pause();
            _syncBoundsTask = schedule.Execute(() =>
            {
                NodeViewBoundsUtils.RunFlowBoundsSyncTwice(this, GetResolvedMinBounds, () => !_panelsExpanded);
            });
            _syncBoundsTask.ExecuteLater(10);
        }

        private void CleanupUi()
        {
            _syncBoundsTask?.Pause();
            _syncBoundsTask = null;

            if (_bodyPanel != null)
            {
                _bodyPanel.OnChanged -= OnSubGraphChanged;
                _bodyPanel.OnPanelResized -= OnPanelResized;
            }

            if (_collapseToggle != null && _collapseToggle.parent == titleContainer)
                titleContainer.Remove(_collapseToggle);
            if (_subspaceLinksRow != null && _subspaceLinksRow.parent == titleContainer)
                titleContainer.Remove(_subspaceLinksRow);

            _panelsContainer?.RemoveFromHierarchy();

            _bodyPanel = null;
            _panelsContainer = null;
            _collapseToggle = null;
            _subspaceLinksRow = null;
        }
    }
}
