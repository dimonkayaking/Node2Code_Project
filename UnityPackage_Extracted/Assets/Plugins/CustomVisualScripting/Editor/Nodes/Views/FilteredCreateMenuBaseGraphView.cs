using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraphProcessor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    public sealed class FilteredCreateMenuBaseGraphView : BaseGraphView
    {
        static readonly MethodInfo s_baseReloadView =
            typeof(BaseGraphView).GetMethod("ReloadView", BindingFlags.Instance | BindingFlags.NonPublic);

        Undo.UndoRedoCallback _registeredSafeUndoHandler;

        bool _pendingPostGraphChromeFix;
        private int _previousSelectedCount = 0;

        public event Action<BaseNodeView> NodeViewAdded;
        readonly HashSet<BaseNodeView> _trackedNodeViews = new();

        public FilteredCreateMenuBaseGraphView(EditorWindow window) : base(window)
        {
            ReplaceBaseGraphViewUndoHandlerWithNullSafeWrapper();

            var previousGraphViewChanged = graphViewChanged;
            graphViewChanged = change =>
            {
                change = previousGraphViewChanged != null ? previousGraphViewChanged(change) : change;
                SchedulePostGraphChromeFixForAllNodes();
                return change;
            };
            schedule.Execute(PollForNewNodeViews).Every(16);
            RegisterCallback<PointerDownEvent>(_ => ScheduleOutlineRefreshForAllNodes(), TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(_ => RefreshAllOutlines());
            schedule.Execute(() =>
            {
                int currentCount = selection.Count;
                if (currentCount != _previousSelectedCount)
                {
                    _previousSelectedCount = currentCount;
                    RefreshAllOutlines();
                }
            }).Every(50);
        }

        /// <summary>
        /// BaseGraphView подписывает приватный <see cref="ReloadView"/> на глобальный Undo.
        /// После уничтожения временного <see cref="BaseGraph"/> (вложенные SubGraphPanel) или при race на Undo,
        /// <c>ReloadView</c> вызывает <c>new SerializedObject(graph)</c> при <c>graph == null</c> и падает.
        /// Снимаем исходный делегат и подставляем обёртку.
        /// </summary>
        void ReplaceBaseGraphViewUndoHandlerWithNullSafeWrapper()
        {
            if (s_baseReloadView == null)
                return;

            Undo.UndoRedoCallback original;
            try
            {
                original = (Undo.UndoRedoCallback)Delegate.CreateDelegate(typeof(Undo.UndoRedoCallback), this,
                    s_baseReloadView);
            }
            catch (ArgumentException)
            {
                return;
            }

            Undo.undoRedoPerformed -= original;
            _registeredSafeUndoHandler = SafeUndoRedoPerformed;
            Undo.undoRedoPerformed += _registeredSafeUndoHandler;
        }

        void SafeUndoRedoPerformed()
        {
            if (graph == null)
                return;

            s_baseReloadView?.Invoke(this, null);
        }

        /// <summary>Снимает безопасный Undo handler до <see cref="BaseGraphView.Dispose"/>.</summary>
        public new void Dispose()
        {
            if (_registeredSafeUndoHandler != null)
            {
                Undo.undoRedoPerformed -= _registeredSafeUndoHandler;
                _registeredSafeUndoHandler = null;
            }

            base.Dispose();
        }

        private void RefreshAllOutlines()
        {
            if (nodeViews == null) return;
            foreach (var nv in nodeViews)
            {
                if (nv != null)
                    NodeViewBoundsUtils.RefreshNodeOutlineColor(nv);
            }
        }

        bool _pendingOutlineRefresh;
        void ScheduleOutlineRefreshForAllNodes()
        {
            if (_pendingOutlineRefresh) return;
            _pendingOutlineRefresh = true;
            schedule.Execute(() =>
            {
                _pendingOutlineRefresh = false;
                if (nodeViews == null) return;
                foreach (var nv in nodeViews)
                    if (nv != null) NodeViewBoundsUtils.RefreshNodeOutlineColor(nv);
            }).ExecuteLater(80);
        }

        void PollForNewNodeViews()
        {
            if (nodeViews == null || nodeViews.Count == 0) return;
            for (int i = 0; i < nodeViews.Count; i++)
            {
                var nv = nodeViews[i];
                if (nv == null) continue;
                if (!_trackedNodeViews.Add(nv)) continue;
                NodeViewBoundsUtils.PerformFullNodeAppearanceFix(nv);
                NodeViewAdded?.Invoke(nv);
                var captured = nv;
                captured.schedule.Execute(() => NodeViewBoundsUtils.PerformFullNodeAppearanceFix(captured)).ExecuteLater(0);
                captured.schedule.Execute(() => NodeViewBoundsUtils.PerformFullNodeAppearanceFix(captured)).ExecuteLater(2);
            }
            _trackedNodeViews.RemoveWhere(nv => nv == null || nv.panel == null);
        }

        void SchedulePostGraphChromeFixForAllNodes()
        {
            if (_pendingPostGraphChromeFix) return;
            _pendingPostGraphChromeFix = true;
            schedule.Execute(() =>
            {
                _pendingPostGraphChromeFix = false;
                if (nodeViews == null) return;
                foreach (var nv in nodeViews)
                    if (nv != null) NodeViewBoundsUtils.PerformFullNodeAppearanceFix(nv);
            }).ExecuteLater(1);
        }

        public override IEnumerable<(string path, Type type)> FilterCreateNodeMenuEntries()
        {
            foreach (var entry in NodeProvider.GetNodeMenuEntries(graph))
                if (!ShouldHideMenuPath(entry.path)) yield return entry;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var grouped = new Dictionary<string, List<(string fullPath, Type type)>>();
            foreach (var entry in NodeProvider.GetNodeMenuEntries(graph))
            {
                if (ShouldHideMenuPath(entry.path)) continue;
                var category = entry.path.Split('/')[0];
                if (!grouped.ContainsKey(category))
                    grouped[category] = new List<(string, Type)>();
                grouped[category].Add((entry.path, entry.type));
            }

            Vector2 mouseGraphPos = evt.localMousePosition;

            foreach (var kv in grouped.OrderBy(g => g.Key))
            {
                string category = kv.Key;
                foreach (var node in kv.Value.OrderBy(n => n.fullPath))
                {
                    string nodeName = node.fullPath.Split('/').Last();
                    Type nodeType = node.type;
                    evt.menu.AppendAction($"{category}/{nodeName}", action =>
                    {
                        CreateNodeAtPosition(nodeType, mouseGraphPos);
                    });
                }
            }
        }

        private void CreateNodeAtPosition(Type nodeType, Vector2 graphPosition)
        {
            var node = (BaseNode)Activator.CreateInstance(nodeType);
            if (node == null) return;
            if (string.IsNullOrEmpty(node.GUID))
                node.GUID = Guid.NewGuid().ToString();
            node.position = new Rect(graphPosition.x - 100, graphPosition.y - 50, 200, 100);
            AddNode(node);
        }

        private static bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("Utils/") || path.StartsWith("Utils") ||
                   path.StartsWith("Unity/") || path.StartsWith("Unity");
        }
    }
}