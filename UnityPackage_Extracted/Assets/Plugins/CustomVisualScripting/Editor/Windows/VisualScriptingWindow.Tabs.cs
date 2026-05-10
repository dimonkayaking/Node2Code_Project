using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Debug;
using CustomVisualScripting.Editor.Nodes.Flow;
using CustomVisualScripting.Editor.Nodes.Literals;
using CustomVisualScripting.Editor.Nodes.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor;

namespace CustomVisualScripting.Editor.Windows
{
    public partial class VisualScriptingWindow
    {
        private const string FileTabId = "file-tab";

        private readonly List<TabDescriptor> _tabs = new();
        private readonly Dictionary<string, SubspaceRuntime> _subspaceRuntimes = new(StringComparer.Ordinal);
        private string _activeTabId = FileTabId;
        private VisualElement _tabStrip;
        private VisualElement _graphHost;

        private sealed class TabDescriptor
        {
            public string Id;
            public string Title;
            public bool Closable;
            public string NodeId;
            public SubspaceKind? SubspaceKind;
        }

        private sealed class SubspaceRuntime
        {
            public GraphData SubGraph;
            public BaseGraph InternalGraph;
            public FilteredCreateMenuBaseGraphView GraphView;
            public IVisualElementScheduledItem SyncTicker;
        }

        private void InitializeTabsState()
        {
            _tabs.Clear();
            _tabs.Add(new TabDescriptor
            {
                Id = FileTabId,
                Title = GetFileTabTitle(),
                Closable = false
            });
            _activeTabId = FileTabId;
        }

        private void BuildGraphAreaWithTabs(VisualElement parent)
        {
            _tabStrip = new VisualElement();
            _tabStrip.AddToClassList("graph-tab-strip");
            _tabStrip.style.marginBottom = 0;
            parent.Add(_tabStrip);

            _graphHost = new VisualElement();
            _graphHost.AddToClassList("graph-tab-host");
            _graphHost.style.flexGrow = 1;
            _graphHost.style.marginTop = 0;
            parent.Add(_graphHost);

            RefreshFileTabTitle();
            RenderTabs();
        }

        private void RefreshFileTabTitle()
        {
            var fileTab = _tabs.FirstOrDefault(t => t.Id == FileTabId);
            if (fileTab != null)
                fileTab.Title = GetFileTabTitle();
            RenderTabs();
        }

        private string GetFileTabTitle()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return "Новый файл";
            return System.IO.Path.GetFileName(_currentFilePath);
        }

        private void RenderTabs()
        {
            if (_tabStrip == null) return;
            _tabStrip.Clear();
            foreach (var tab in _tabs)
            {
                var tabRoot = new VisualElement();
                tabRoot.AddToClassList("graph-tab-item");
                if (string.Equals(tab.Id, _activeTabId, StringComparison.Ordinal))
                    tabRoot.AddToClassList("graph-tab-item--active");

                var titleButton = new Button(() => ActivateTab(tab.Id)) { text = tab.Title };
                titleButton.AddToClassList("graph-tab-title");
                tabRoot.Add(titleButton);

                if (tab.Closable)
                {
                    var closeButton = new Button(() => CloseTab(tab.Id)) { text = "x" };
                    closeButton.AddToClassList("graph-tab-close");
                    tabRoot.Add(closeButton);
                }
                _tabStrip.Add(tabRoot);
            }
        }

        private void ActivateTab(string tabId)
        {
            var previousTabId = _activeTabId;
            if (!string.Equals(previousTabId, tabId, StringComparison.Ordinal))
            {
                if (!string.Equals(previousTabId, FileTabId, StringComparison.Ordinal) &&
                    _subspaceRuntimes.TryGetValue(previousTabId, out var leavingRuntime))
                    SyncSubspaceRuntime(leavingRuntime);
            }

            _activeTabId = tabId;
            RenderTabs();

            var switchedToTab = !string.Equals(previousTabId, tabId, StringComparison.Ordinal);
            if (!string.Equals(tabId, FileTabId, StringComparison.Ordinal) &&
                _subspaceRuntimes.ContainsKey(tabId) &&
                switchedToTab)
                RebuildSubspaceRuntimeGraph(tabId);

            DisplayActiveTabContent();
        }

        private void CloseTab(string tabId)
        {
            if (string.Equals(tabId, FileTabId, StringComparison.Ordinal)) return;
            DisposeSubspaceRuntime(tabId);
            _tabs.RemoveAll(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (string.Equals(_activeTabId, tabId, StringComparison.Ordinal))
                _activeTabId = FileTabId;
            RenderTabs();
            DisplayActiveTabContent();
        }

        private void DisplayActiveTabContent()
        {
            if (_graphHost == null) return;
            _graphHost.Clear();
            if (string.Equals(_activeTabId, FileTabId, StringComparison.Ordinal))
            {
                if (_graphView != null)
                    _graphHost.Add(_graphView);
                return;
            }
            if (_subspaceRuntimes.TryGetValue(_activeTabId, out var runtime) && runtime?.GraphView != null)
                _graphHost.Add(runtime.GraphView);
        }

        public void OpenSubspaceFromNode(string nodeId, SubspaceKind subspaceKind)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;
            if (!string.Equals(_activeTabId, FileTabId, StringComparison.Ordinal)) return;
            var descriptor = EnsureSubspaceTab(nodeId, subspaceKind);
            if (descriptor == null) return;
            if (!_subspaceRuntimes.ContainsKey(descriptor.Id))
                CreateSubspaceRuntime(descriptor);
            ActivateTab(descriptor.Id);
        }

        private TabDescriptor EnsureSubspaceTab(string nodeId, SubspaceKind subspaceKind)
        {
            var tabId = $"{nodeId}:{subspaceKind.ToString().ToLowerInvariant()}";
            var existing = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (existing != null) return existing;
            var nodeData = FindNodeDataOrSyncFromView(nodeId);
            if (nodeData == null) return null;
            var descriptor = new TabDescriptor
            {
                Id = tabId,
                NodeId = nodeId,
                SubspaceKind = subspaceKind,
                Closable = true,
                Title = BuildSubspaceTabTitle(nodeData, subspaceKind)
            };
            _tabs.Add(descriptor);
            RenderTabs();
            return descriptor;
        }

        private string BuildSubspaceTabTitle(NodeData nodeData, SubspaceKind subspaceKind)
        {
            var nodeName = string.IsNullOrWhiteSpace(nodeData?.VariableName)
                ? nodeData?.Type.ToString()
                : nodeData.VariableName;
            return $"{nodeName} - {GetSubspaceDisplayName(nodeData, subspaceKind)}";
        }

        private static string GetSubspaceDisplayName(NodeData nodeData, SubspaceKind subspaceKind)
        {
            if (nodeData?.Type == NodeType.FlowFor && subspaceKind == SubspaceKind.Condition)
                return "Граница";

            return subspaceKind switch
            {
                SubspaceKind.Condition => "Условие",
                SubspaceKind.Body => "Тело",
                SubspaceKind.Init => "Объявление",
                SubspaceKind.Increment => "Шаг",
                _ => subspaceKind.ToString()
            };
        }

        /// <summary>
        /// Регистрирует рантайм вкладки подпространства; граф строится в <see cref="RebuildSubspaceRuntimeGraph"/> при активации вкладки.
        /// </summary>
        private void CreateSubspaceRuntime(TabDescriptor tab)
        {
            var nodeData = FindNodeDataOrSyncFromView(tab.NodeId);
            if (nodeData == null || !tab.SubspaceKind.HasValue) return;
            var subGraph = GetSubGraphRef(nodeData, tab.SubspaceKind.Value);
            if (subGraph == null) return;
            _subspaceRuntimes[tab.Id] = new SubspaceRuntime
            {
                SubGraph = subGraph
            };
        }

        private static void TearDownSubspaceRuntimeGraph(SubspaceRuntime runtime)
        {
            if (runtime == null) return;
            runtime.SyncTicker?.Pause();
            runtime.SyncTicker = null;
            if (runtime.GraphView != null)
            {
                runtime.GraphView.Dispose();
                runtime.GraphView = null;
            }

            if (runtime.InternalGraph != null)
            {
                DestroyImmediate(runtime.InternalGraph);
                runtime.InternalGraph = null;
            }
        }

        /// <summary>
        /// Пересобирает GraphView вкладки из актуального <see cref="GraphData"/> (в т.ч. после правок на основной вкладке).
        /// Не вызывает <see cref="SyncSubspaceRuntime"/> перед очисткой — источник истины берём из модели, а не из устаревшего view.
        /// </summary>
        private void RebuildSubspaceRuntimeGraph(string tabId)
        {
            if (!_subspaceRuntimes.TryGetValue(tabId, out var runtime)) return;
            var tab = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (tab == null || !tab.SubspaceKind.HasValue) return;

            var nodeData = FindNodeDataOrSyncFromView(tab.NodeId);
            if (nodeData == null) return;
            var subGraph = GetSubGraphRef(nodeData, tab.SubspaceKind.Value);
            if (subGraph == null) return;

            runtime.SubGraph = subGraph;
            TearDownSubspaceRuntimeGraph(runtime);

            runtime.InternalGraph = ScriptableObject.CreateInstance<BaseGraph>();
            var nodeMap = new Dictionary<string, CustomBaseNode>();
            foreach (var sourceNode in subGraph.Nodes)
            {
                var customNode = CreateNodeFromData(sourceNode);
                if (customNode == null) continue;
                customNode.NodeId = sourceNode.Id;
                customNode.InitializeFromData(sourceNode);
                if (customNode.GUID != customNode.NodeId)
                    customNode.SetGUID(customNode.NodeId);
                ApplyLiteralValues(customNode, sourceNode);
                runtime.InternalGraph.AddNode(customNode);
                nodeMap[sourceNode.Id] = customNode;
            }

            runtime.GraphView = new FilteredCreateMenuBaseGraphView(this);
            runtime.GraphView.Initialize(runtime.InternalGraph);
            runtime.GraphView.style.flexGrow = 1;
            runtime.GraphView.graphViewChanged += change =>
            {
                SyncSubspaceRuntime(runtime);
                return change;
            };
            GraphViewEdgeRestore.RestoreEdges(runtime.GraphView, subGraph.Edges, nodeMap, validatePortDirections: false);
            GraphDataViewSync.ApplySavedVisualLayout(subGraph, runtime.GraphView);
            ConfigureNodeViewSizing(runtime.GraphView.nodeViews);
            GraphViewAutoLayout.ApplyIfNeededForNestedGraph(subGraph, runtime.GraphView.nodeViews,
                SubGraphPanel.MeasureNestedGraphCell);
            runtime.GraphView.UpdateViewTransform(Vector3.zero, Vector3.one);
            runtime.GraphView.FrameAll();
            runtime.SyncTicker = runtime.GraphView.schedule.Execute(() => SyncSubspaceRuntime(runtime)).Every(300);
        }

        private void SyncSubspaceRuntime(SubspaceRuntime runtime)
        {
            if (runtime?.GraphView == null || runtime.InternalGraph == null || runtime.SubGraph == null) return;
            var graphNodes = runtime.InternalGraph.nodes.OfType<CustomBaseNode>().ToList();
            GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(runtime.SubGraph, graphNodes, runtime.GraphView);
            GraphDataViewSync.SaveVisualLayoutToGraphData(runtime.SubGraph, runtime.InternalGraph, runtime.GraphView);
            _hasUnsavedChanges = true;
        }

        private static void ApplyLiteralValues(CustomBaseNode node, NodeData nodeData)
        {
            if (node is IntNode intNode && int.TryParse(nodeData.Value, out var intVal))
                intNode.intValue = intVal;
            else if (node is FloatNode floatNode &&
                     float.TryParse(nodeData.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
                floatNode.floatValue = floatVal;
            else if (node is BoolNode boolNode && bool.TryParse(nodeData.Value, out var boolVal))
                boolNode.boolValue = boolVal;
            else if (node is StringNode stringNode)
                stringNode.stringValue = nodeData.Value;
        }

        private NodeData FindNodeData(string nodeId)
        {
            return _currentGraph?.LogicGraph?.Nodes?.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        }

        private NodeData FindNodeDataOrSyncFromView(string nodeId)
        {
            var nodeData = FindNodeData(nodeId);
            if (nodeData != null) return nodeData;

            if (_graphView != null && _internalGraph != null)
            {
                SyncFullGraphFromView();
                return FindNodeData(nodeId);
            }

            return null;
        }

        private static GraphData GetSubGraphRef(NodeData nodeData, SubspaceKind subspaceKind)
        {
            switch (subspaceKind)
            {
                case SubspaceKind.Condition:
                    nodeData.ConditionSubGraph ??= new GraphData();
                    return nodeData.ConditionSubGraph;
                case SubspaceKind.Body:
                    nodeData.BodySubGraph ??= new GraphData();
                    return nodeData.BodySubGraph;
                case SubspaceKind.Init:
                    nodeData.InitSubGraph ??= new GraphData();
                    return nodeData.InitSubGraph;
                case SubspaceKind.Increment:
                    nodeData.IncrementSubGraph ??= new GraphData();
                    return nodeData.IncrementSubGraph;
                default:
                    return null;
            }
        }

        private void ResetTabsToFileOnly()
        {
            DisposeAllSubspaceRuntimes();
            _tabs.Clear();
            _tabs.Add(new TabDescriptor
            {
                Id = FileTabId,
                Title = GetFileTabTitle(),
                Closable = false
            });
            _activeTabId = FileTabId;
            RenderTabs();
            DisplayActiveTabContent();
        }

        private void DisposeAllSubspaceRuntimes()
        {
            foreach (var key in _subspaceRuntimes.Keys.ToList())
                DisposeSubspaceRuntime(key);
            _subspaceRuntimes.Clear();
        }

        private void DisposeSubspaceRuntime(string tabId)
        {
            if (!_subspaceRuntimes.TryGetValue(tabId, out var runtime)) return;
            SyncSubspaceRuntime(runtime);
            TearDownSubspaceRuntimeGraph(runtime);
            _subspaceRuntimes.Remove(tabId);
        }
    }
}