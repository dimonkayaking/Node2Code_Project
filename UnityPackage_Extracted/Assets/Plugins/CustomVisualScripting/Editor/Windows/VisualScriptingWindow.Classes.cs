using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GraphProcessor;
using Newtonsoft.Json;
using CustomVisualScripting.Editor;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Nodes.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Windows
{
    public partial class VisualScriptingWindow
    {
        // ─── Префикс вкладки класса ───────────────────────────────────────────
        private const string ClassTabPrefix = "class:";

        // ─── Рантаймы вкладок классов ─────────────────────────────────────────
        private readonly Dictionary<string, ClassTabRuntime> _classTabRuntimes =
            new(StringComparer.Ordinal);

        private sealed class ClassTabRuntime
        {
            public ClassDefinition Definition;
            public VisualElement   Container;

            // Граф тела класса — MethodOwnerNode-ноды, соединённые цепочкой
            public ClassBodyGraphView BodyGraphView;
            public BaseGraph          BodyInternalGraph;

            public IVisualElementScheduledItem SyncTicker;
        }

        // ─── Сериализация классов ─────────────────────────────────────────────
        [Serializable]
        private class ClassListWrapper
        {
            public List<ClassDefinition> Classes = new();
        }

        // ─── Публичное API ────────────────────────────────────────────────────

        /// <summary>Открывает вкладку редактирования тела класса. Если уже открыта — активирует.</summary>
        public void OpenClassTab(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId)) return;
            var def = ClassRegistry.GetById(classId);
            if (def == null) return;

            var tabId = ClassTabPrefix + classId;

            var existing = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (existing == null)
            {
                _tabs.Add(new TabDescriptor
                {
                    Id       = tabId,
                    Title    = def.Name,
                    Closable = true
                });
                RenderTabs();
            }

            if (!_classTabRuntimes.ContainsKey(tabId))
                CreateClassRuntime(tabId, def);

            ActivateTab(tabId);
        }

        /// <summary>Закрывает вкладку класса.</summary>
        public void CloseClassTab(string classId) => CloseTab(ClassTabPrefix + classId);

        /// <summary>Обновляет заголовок вкладки после переименования класса.</summary>
        public void RefreshClassTabTitle(string classId, string newName)
        {
            var tabId = ClassTabPrefix + classId;
            var tab = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (tab == null) return;
            tab.Title = newName;
            RenderTabs();
        }

        // ─── Сохранение / загрузка классов ───────────────────────────────────

        internal void SyncAllClassRuntimes()
        {
            foreach (var rt in _classTabRuntimes.Values)
                SyncClassRuntime(rt);
        }

        internal void SaveClassesToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var wrapper = new ClassListWrapper { Classes = ClassRegistry.Classes.ToList() };
                File.WriteAllText(path, JsonConvert.SerializeObject(wrapper, _jsonSettings));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[VS] Не удалось сохранить классы: {e.Message}");
            }
        }

        internal void LoadClassesFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ClassRegistry.Clear();
                return;
            }
            try
            {
                var wrapper = JsonConvert.DeserializeObject<ClassListWrapper>(
                    File.ReadAllText(path), _jsonSettings);
                if (wrapper?.Classes != null) ClassRegistry.ReplaceAll(wrapper.Classes);
                else                          ClassRegistry.Clear();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[VS] Не удалось загрузить классы: {e.Message}");
                ClassRegistry.Clear();
            }
        }

        internal static string GetClassesFilePath(string csFilePath)
        {
            if (string.IsNullOrWhiteSpace(csFilePath)) return null;
            return Path.Combine(
                Path.GetDirectoryName(csFilePath) ?? "",
                Path.GetFileNameWithoutExtension(csFilePath) + ".classes.json");
        }

        // ─── Создание рантайма класса ─────────────────────────────────────────

        private void CreateClassRuntime(string tabId, ClassDefinition def)
        {
            var runtime = new ClassTabRuntime { Definition = def };

            // Восстанавливаем ноды из сохранённого ClassBodyGraph
            runtime.BodyInternalGraph = ScriptableObject.CreateInstance<BaseGraph>();
            var bodyNodeMap = new Dictionary<string, CustomBaseNode>();

            if (def.ClassBodyGraph?.Nodes != null)
            {
                foreach (var nd in def.ClassBodyGraph.Nodes)
                {
                    var cn = EditorNodeFactory.Create(nd);
                    if (cn == null) continue;
                    cn.NodeId = nd.Id;
                    cn.InitializeFromData(nd);
                    if (cn.GUID != cn.NodeId) cn.SetGUID(cn.NodeId);
                    runtime.BodyInternalGraph.AddNode(cn);
                    bodyNodeMap[nd.Id] = cn;
                }
            }

            runtime.BodyGraphView = new ClassBodyGraphView(this);
            runtime.BodyGraphView.NodeViewAdded += OnNodeViewAdded;
            runtime.BodyGraphView.Initialize(runtime.BodyInternalGraph);
            runtime.BodyGraphView.style.flexGrow = 1;
            runtime.BodyGraphView.graphViewChanged += change =>
            {
                SyncClassRuntime(runtime);
                return change;
            };

            if (def.ClassBodyGraph?.Edges != null && bodyNodeMap.Count > 0)
                GraphViewEdgeRestore.RestoreEdges(
                    runtime.BodyGraphView, def.ClassBodyGraph.Edges,
                    bodyNodeMap, validatePortDirections: false);

            GraphDataViewSync.ApplySavedVisualLayout(def.ClassBodyGraph, runtime.BodyGraphView);
            ConfigureNodeViewSizing(runtime.BodyGraphView.nodeViews);
            runtime.BodyGraphView.UpdateViewTransform(Vector3.zero, Vector3.one);
            runtime.BodyGraphView.FrameAll();

            // ── Сборка контейнера ─────────────────────────────────────────────
            var bodyArea = new VisualElement();
            bodyArea.style.flexGrow      = 1;
            bodyArea.style.flexDirection = FlexDirection.Column;
            bodyArea.style.overflow      = Overflow.Hidden;

            var header = BuildClassBodyHeader(def.Name);
            header.style.height     = 32f;
            header.style.flexShrink = 0;
            bodyArea.Add(header);
            bodyArea.Add(runtime.BodyGraphView);

            runtime.Container = bodyArea;

            // Периодический тикер синхронизации
            runtime.SyncTicker =
                runtime.BodyGraphView.schedule.Execute(() => SyncClassRuntime(runtime)).Every(300);

            _classTabRuntimes[tabId] = runtime;
        }

        private static VisualElement BuildClassBodyHeader(string className)
        {
            var header = new VisualElement();
            header.style.flexDirection   = FlexDirection.Row;
            header.style.alignItems      = Align.Center;
            header.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            header.style.paddingLeft     = 8;
            header.style.paddingRight    = 8;
            header.style.paddingTop      = 4;
            header.style.paddingBottom   = 4;

            var label = new Label($"Методы класса: {className}");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.color    = new Color(0.4f, 1f, 0.55f); // зелёный
            label.style.flexGrow = 1;
            header.Add(label);

            return header;
        }

        // ─── Синхронизация рантайма класса ───────────────────────────────────

        private void SyncClassRuntime(ClassTabRuntime runtime)
        {
            if (runtime?.Definition == null) return;
            if (runtime.BodyGraphView == null || runtime.BodyInternalGraph == null) return;

            var bodyNodes = runtime.BodyInternalGraph.nodes.OfType<CustomBaseNode>().ToList();
            GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(
                runtime.Definition.ClassBodyGraph, bodyNodes, runtime.BodyGraphView);
            GraphDataViewSync.SaveVisualLayoutToGraphData(
                runtime.Definition.ClassBodyGraph, runtime.BodyInternalGraph, runtime.BodyGraphView);

            _hasUnsavedChanges = true;
        }

        // ─── Удаление рантаймов ──────────────────────────────────────────────

        internal void DisposeClassRuntime(string tabId)
        {
            if (!_classTabRuntimes.TryGetValue(tabId, out var runtime)) return;
            SyncClassRuntime(runtime);
            TearDownClassRuntimeGraph(runtime);
            _classTabRuntimes.Remove(tabId);
        }

        internal void DisposeAllClassRuntimes()
        {
            foreach (var key in _classTabRuntimes.Keys.ToList())
                DisposeClassRuntime(key);
            _classTabRuntimes.Clear();
        }

        private void TearDownClassRuntimeGraph(ClassTabRuntime runtime)
        {
            if (runtime == null) return;

            runtime.SyncTicker?.Pause();
            runtime.SyncTicker = null;

            if (runtime.BodyGraphView != null)
            {
                runtime.BodyGraphView.NodeViewAdded -= OnNodeViewAdded;
                runtime.BodyGraphView.Dispose();
                runtime.BodyGraphView = null;
            }
            if (runtime.BodyInternalGraph != null)
            {
                DestroyImmediate(runtime.BodyInternalGraph);
                runtime.BodyInternalGraph = null;
            }

            runtime.Container = null;
        }
    }
}
