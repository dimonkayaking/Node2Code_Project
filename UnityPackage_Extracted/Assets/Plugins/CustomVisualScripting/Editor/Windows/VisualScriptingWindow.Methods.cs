using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GraphProcessor;
using Newtonsoft.Json;
using CustomVisualScripting.Editor.Methods;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor;
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
        // ─── Префикс вкладки метода ──────────────────────────────────────────
        private const string MethodTabPrefix = "method:";

        // ─── Рантаймы вкладок методов ────────────────────────────────────────
        private readonly Dictionary<string, MethodTabRuntime> _methodTabRuntimes =
            new(StringComparer.Ordinal);

        private sealed class MethodTabRuntime
        {
            public MethodDefinition Definition;
            public VisualElement     Container;          // TwoPaneSplitView (param-граф | body-граф)

            // Верхний граф — параметры метода (MethodParamNode)
            public MethodParamGraphView ParamGraphView;
            public BaseGraph            ParamInternalGraph;

            // Нижний граф — тело метода
            public FilteredCreateMenuBaseGraphView BodyGraphView;
            public BaseGraph                       BodyInternalGraph;

            public IVisualElementScheduledItem SyncTicker;
        }

        // ─── Сериализация методов ─────────────────────────────────────────────
        [Serializable]
        private class MethodListWrapper
        {
            public List<MethodDefinition> Methods = new();
        }

        // ─── Публичное API ────────────────────────────────────────────────────

        /// <summary>Открывает вкладку редактирования метода. Если уже открыта — активирует её.</summary>
        public void OpenMethodTab(string methodId)
        {
            if (string.IsNullOrWhiteSpace(methodId)) return;
            var def = MethodRegistry.GetById(methodId);
            if (def == null) return;

            var tabId = MethodTabPrefix + methodId;

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

            if (!_methodTabRuntimes.ContainsKey(tabId))
                CreateMethodRuntime(tabId, def);

            ActivateTab(tabId);
        }

        /// <summary>Закрывает вкладку метода.</summary>
        public void CloseMethodTab(string methodId) => CloseTab(MethodTabPrefix + methodId);

        /// <summary>Обновляет заголовок вкладки после переименования метода.</summary>
        public void RefreshMethodTabTitle(string methodId, string newName)
        {
            var tabId = MethodTabPrefix + methodId;
            var tab = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (tab == null) return;
            tab.Title = newName;
            RenderTabs();
        }

        // ─── Сохранение / загрузка ────────────────────────────────────────────

        internal void SyncAllMethodRuntimes()
        {
            foreach (var rt in _methodTabRuntimes.Values)
                SyncMethodRuntime(rt);
        }

        // Настройки Newtonsoft: без TypeNameHandling (всё — конкретные типы), игнорируем null и циклы.
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting            = Formatting.Indented,
            NullValueHandling     = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        internal void SaveMethodsToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var wrapper = new MethodListWrapper { Methods = MethodRegistry.Methods.ToList() };
                File.WriteAllText(path, JsonConvert.SerializeObject(wrapper, _jsonSettings));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[VS] Не удалось сохранить методы: {e.Message}");
            }
        }

        internal void LoadMethodsFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MethodRegistry.Clear();
                return;
            }
            try
            {
                var wrapper = JsonConvert.DeserializeObject<MethodListWrapper>(
                    File.ReadAllText(path), _jsonSettings);
                if (wrapper?.Methods != null) MethodRegistry.ReplaceAll(wrapper.Methods);
                else                          MethodRegistry.Clear();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[VS] Не удалось загрузить методы: {e.Message}");
                MethodRegistry.Clear();
            }
        }

        internal static string GetMethodsFilePath(string csFilePath)
        {
            if (string.IsNullOrWhiteSpace(csFilePath)) return null;
            return Path.Combine(
                Path.GetDirectoryName(csFilePath) ?? "",
                Path.GetFileNameWithoutExtension(csFilePath) + ".methods.json");
        }

        // ─── Создание рантайма метода ─────────────────────────────────────────

        private void CreateMethodRuntime(string tabId, MethodDefinition def)
        {
            var runtime = new MethodTabRuntime { Definition = def };

            // ── Верхний граф: параметры ───────────────────────────────────────
            runtime.ParamInternalGraph = ScriptableObject.CreateInstance<BaseGraph>();
            var paramNodeMap = new Dictionary<string, CustomBaseNode>();

            bool paramGraphHasNodes = def.ParamGraph?.Nodes != null && def.ParamGraph.Nodes.Count > 0;

            if (paramGraphHasNodes)
            {
                // Восстанавливаем MethodParamNode из сохранённых данных
                foreach (var nd in def.ParamGraph.Nodes)
                {
                    var cn = EditorNodeFactory.Create(nd);
                    if (cn == null) continue;
                    cn.NodeId = nd.Id;
                    cn.InitializeFromData(nd);
                    if (cn.GUID != cn.NodeId) cn.SetGUID(cn.NodeId);
                    runtime.ParamInternalGraph.AddNode(cn);
                    paramNodeMap[nd.Id] = cn;
                }
            }
            else if (def.Parameters?.Count > 0)
            {
                // Первый открытый рантайм: создаём ноды параметров из списка MethodDefinition.Parameters
                float xOffset = 40f;
                foreach (var param in def.Parameters)
                {
                    var pn = new MethodParamNode { ParamName = param.Name, ParamType = param.Type };
                    pn.NodeId  = Guid.NewGuid().ToString();
                    pn.SetGUID(pn.NodeId);
                    pn.position = new Rect(xOffset, 30f, 200f, 80f);
                    xOffset += 230f;
                    runtime.ParamInternalGraph.AddNode(pn);
                    paramNodeMap[pn.NodeId] = pn;
                }
            }

            // GraphView параметров — только "Method/" категории в контекстном меню
            runtime.ParamGraphView = new MethodParamGraphView(this);
            runtime.ParamGraphView.NodeViewAdded += OnNodeViewAdded;
            runtime.ParamGraphView.Initialize(runtime.ParamInternalGraph);
            runtime.ParamGraphView.style.flexGrow = 1;
            runtime.ParamGraphView.graphViewChanged += change =>
            {
                // graphViewChanged срабатывает на удаление и рёбра, но НЕ на AddNode.
                // Поэтому здесь обрабатываем только удаление параметров.
                SyncMethodRuntime(runtime);
                SyncBodyParamReferences(runtime);
                return change;
            };

            // NodeViewAdded опрашивается каждые 16 мс и срабатывает при появлении новой ноды,
            // чего graphViewChanged не делает при AddNode. Так добавление параметра через ПКМ
            // немедленно отражается в теле метода.
            runtime.ParamGraphView.NodeViewAdded += nv =>
            {
                if (nv?.nodeTarget is not MethodParamNode) return;
                SyncMethodRuntime(runtime);
                SyncBodyParamReferences(runtime);
            };

            if (def.ParamGraph?.Edges != null && paramNodeMap.Count > 0)
                GraphViewEdgeRestore.RestoreEdges(runtime.ParamGraphView, def.ParamGraph.Edges,
                    paramNodeMap, validatePortDirections: false);

            if (paramGraphHasNodes)
                GraphDataViewSync.ApplySavedVisualLayout(def.ParamGraph, runtime.ParamGraphView);

            ConfigureNodeViewSizing(runtime.ParamGraphView.nodeViews);
            runtime.ParamGraphView.UpdateViewTransform(Vector3.zero, Vector3.one);
            runtime.ParamGraphView.FrameAll();

            // ── Нижний граф: тело метода ──────────────────────────────────────
            runtime.BodyInternalGraph = ScriptableObject.CreateInstance<BaseGraph>();
            var bodyNodeMap = new Dictionary<string, CustomBaseNode>();

            if (def.BodyGraph?.Nodes != null)
            {
                foreach (var nd in def.BodyGraph.Nodes)
                {
                    var cn = EditorNodeFactory.Create(nd);
                    if (cn == null) continue;
                    cn.NodeId = nd.Id;
                    cn.InitializeFromData(nd);
                    if (cn.GUID != cn.NodeId) cn.SetGUID(cn.NodeId);
                    ApplyLiteralValues(cn, nd);
                    runtime.BodyInternalGraph.AddNode(cn);
                    bodyNodeMap[nd.Id] = cn;
                }
            }

            runtime.BodyGraphView = new MethodBodyGraphView(this);
            runtime.BodyGraphView.NodeViewAdded += OnNodeViewAdded;
            runtime.BodyGraphView.Initialize(runtime.BodyInternalGraph);
            runtime.BodyGraphView.style.flexGrow = 1;
            runtime.BodyGraphView.graphViewChanged += change =>
            {
                SyncMethodRuntime(runtime);
                return change;
            };

            if (def.BodyGraph?.Edges != null && bodyNodeMap.Count > 0)
                GraphViewEdgeRestore.RestoreEdges(runtime.BodyGraphView, def.BodyGraph.Edges,
                    bodyNodeMap, validatePortDirections: false);

            GraphDataViewSync.ApplySavedVisualLayout(def.BodyGraph, runtime.BodyGraphView);
            ConfigureNodeViewSizing(runtime.BodyGraphView.nodeViews);
            runtime.BodyGraphView.UpdateViewTransform(Vector3.zero, Vector3.one);

            // ── Отложенная авто-раскладка тела метода ────────────────────────
            // Выполняется через один кадр, когда GraphProcessor уже создал NodeView
            // и их реальные размеры доступны через GetPosition().
            // Если в GraphData уже есть осмысленное визуальное расположение (сохранённое
            // вручную пользователем), авто-раскладка не применяется.
            runtime.BodyGraphView.schedule.Execute(() =>
            {
                if (runtime.BodyGraphView?.nodeViews == null || runtime.BodyInternalGraph == null)
                    return;

                ConfigureNodeViewSizing(runtime.BodyGraphView.nodeViews);

                bool hasSavedLayout = GraphViewAutoLayout.HasMeaningfulVisualLayout(
                    runtime.Definition.BodyGraph,
                    runtime.BodyGraphView.nodeViews.Count);

                if (!hasSavedLayout)
                {
                    GraphViewAutoLayout.ApplyIfNeededForNestedGraph(
                        runtime.Definition.BodyGraph,
                        runtime.BodyGraphView.nodeViews,
                        GraphViewAutoLayout.MeasureMainGraphCell);
                }

                runtime.BodyGraphView.FrameAll();
            }).ExecuteLater(1);

            // ── Сборка контейнера ─────────────────────────────────────────────
            //
            //  TwoPaneSplitView (Vertical)
            //  ├── Pane 0: paramArea (Column, overflow:Hidden)
            //  │    ├── paramHeader  (32px, flexShrink:0)
            //  │    └── ParamGraphView (flexGrow:1)
            //  └── Pane 1: bodyArea (Column)
            //       └── BodyGraphView (flexGrow:1)
            //
            // ┌─────────────────────────────────────────┐
            // │ Параметры метода      [+ Добавить]      │  ← header (32px, фиксирован)
            // ├─────────────────────────────────────────┤
            // │  param graph view  (верхний пейн ~200px)│
            // ├═════════════════════════════════════════╡  ← resizer
            // │  body graph view   (нижний пейн)        │
            // └─────────────────────────────────────────┘
            //
            // overflow:Hidden на paramArea гарантирует, что ноды GraphView
            // не перекрывают шапку визуально. Обёртки (VisualElement) вокруг
            // каждого GraphView обязательны — TwoPaneSplitView не работает
            // корректно с двумя «голыми» GraphView как прямыми дочерьми.

            // Верхний пейн: шапка + param-граф
            var paramArea = new VisualElement();
            paramArea.style.flexGrow      = 1;
            paramArea.style.flexDirection = FlexDirection.Column;
            paramArea.style.overflow      = Overflow.Hidden; // clips graph nodes, prevents header overlap

            var paramHeader = BuildParamAreaHeader();
            paramHeader.style.height     = 32f;  // фиксированная высота шапки
            paramHeader.style.flexShrink = 0;
            paramArea.Add(paramHeader);
            paramArea.Add(runtime.ParamGraphView); // flexGrow=1, заполняет остаток

            // Нижний пейн: шапка + body-граф
            var bodyArea = new VisualElement();
            bodyArea.style.flexGrow      = 1;
            bodyArea.style.flexDirection = FlexDirection.Column;
            bodyArea.style.overflow      = Overflow.Hidden;

            var bodyHeader = BuildBodyAreaHeader();
            bodyHeader.style.height     = 32f;
            bodyHeader.style.flexShrink = 0;
            bodyArea.Add(bodyHeader);
            bodyArea.Add(runtime.BodyGraphView); // flexGrow=1

            // Разделитель между двумя пейнами
            var splitView = new TwoPaneSplitView(0, 220f, TwoPaneSplitViewOrientation.Vertical);
            splitView.style.flexGrow = 1;
            splitView.Add(paramArea);
            splitView.Add(bodyArea);

            runtime.Container = splitView;

            // Даём param-графу знать о body-графе (для RMB → «Добавить в тело»)
            runtime.ParamGraphView.BodyGraphView = runtime.BodyGraphView;

            // Инжектируем ноды-ссылки на параметры и поля класса в body-граф
            SyncBodyParamReferences(runtime);
            SyncBodyFieldReferences(runtime);

            // Периодический тикер синхронизации
            runtime.SyncTicker =
                runtime.BodyGraphView.schedule.Execute(() => SyncMethodRuntime(runtime)).Every(300);

            _methodTabRuntimes[tabId] = runtime;
        }

        /// <summary>Строит заголовок панели параметров (только лейбл, без кнопки).</summary>
        private static VisualElement BuildParamAreaHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection   = FlexDirection.Row;
            header.style.alignItems      = Align.Center;
            header.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            header.style.paddingLeft     = 8;
            header.style.paddingRight    = 8;
            header.style.paddingTop      = 4;
            header.style.paddingBottom   = 4;

            var label = new Label("Параметры метода");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.color    = new Color(0.7f, 0.9f, 1f);
            label.style.flexGrow = 1;
            header.Add(label);

            return header;
        }

        /// <summary>Строит заголовок панели тела метода.</summary>
        private static VisualElement BuildBodyAreaHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection   = FlexDirection.Row;
            header.style.alignItems      = Align.Center;
            header.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            header.style.paddingLeft     = 8;
            header.style.paddingRight    = 8;
            header.style.paddingTop      = 4;
            header.style.paddingBottom   = 4;

            var label = new Label("Тело метода");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.color    = new Color(1f, 0.85f, 0.5f);
            label.style.flexGrow = 1;
            header.Add(label);

            return header;
        }

        // ─── Синхронизация рантайма ──────────────────────────────────────────

        private void SyncMethodRuntime(MethodTabRuntime runtime)
        {
            if (runtime?.Definition == null) return;

            // ── Param-граф → ParamGraph + Parameters ─────────────────────────
            if (runtime.ParamGraphView != null && runtime.ParamInternalGraph != null)
            {
                var allParamNodes = runtime.ParamInternalGraph.nodes.OfType<CustomBaseNode>().ToList();
                GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(
                    runtime.Definition.ParamGraph, allParamNodes, runtime.ParamGraphView);
                GraphDataViewSync.SaveVisualLayoutToGraphData(
                    runtime.Definition.ParamGraph, runtime.ParamInternalGraph, runtime.ParamGraphView);

                // Обновляем плоский список Parameters из живых MethodParamNode
                runtime.Definition.Parameters.Clear();
                foreach (var pn in runtime.ParamInternalGraph.nodes.OfType<MethodParamNode>())
                {
                    runtime.Definition.Parameters.Add(new ParameterDefinition
                    {
                        Name = pn.ParamName,
                        Type = pn.ParamType
                    });
                }
            }

            // ── Body-граф → BodyGraph ─────────────────────────────────────────
            if (runtime.BodyGraphView != null && runtime.BodyInternalGraph != null)
            {
                var bodyNodes = runtime.BodyInternalGraph.nodes.OfType<CustomBaseNode>().ToList();
                GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(
                    runtime.Definition.BodyGraph, bodyNodes, runtime.BodyGraphView);
                GraphDataViewSync.SaveVisualLayoutToGraphData(
                    runtime.Definition.BodyGraph, runtime.BodyInternalGraph, runtime.BodyGraphView);
            }

            _hasUnsavedChanges = true;
        }

        // ─── Синхронизация по требованию (для попапа) ────────────────────────

        /// <summary>
        /// Принудительно синхронизирует рантайм метода прямо сейчас (граф → def.Parameters).
        /// Вызывается перед открытием попапа редактирования, чтобы попап получил актуальный список параметров.
        /// </summary>
        public void ForceSyncMethodRuntime(string methodId)
        {
            var tabId = MethodTabPrefix + methodId;
            if (_methodTabRuntimes.TryGetValue(tabId, out var runtime))
                SyncMethodRuntime(runtime);
        }

        /// <summary>
        /// Обновляет живой граф параметров из <see cref="MethodDefinition.Parameters"/>
        /// (def → граф). Вызывается после того как попап сохранил изменения,
        /// чтобы граф отразил отредактированный список параметров.
        /// </summary>
        public void SyncParamGraphFromDefinition(string methodId)
        {
            var tabId = MethodTabPrefix + methodId;
            if (!_methodTabRuntimes.TryGetValue(tabId, out var runtime)) return;
            if (runtime.ParamGraphView == null || runtime.ParamInternalGraph == null) return;

            var def           = runtime.Definition;
            var existingNodes = runtime.ParamInternalGraph.nodes.OfType<MethodParamNode>().ToList();

            // Сохраняем позиции старых нод для переиспользования
            var savedPositions = existingNodes.Select(n => n.position).ToList();

            // Удаляем все старые ноды параметров через GraphView (убирает и вид, и данные)
            foreach (var oldNode in existingNodes)
            {
                try   { runtime.ParamGraphView.RemoveNode(oldNode); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"[VS] SyncParamGraph RemoveNode: {ex.Message}"); }
            }

            // Добавляем ноды заново по актуальному def.Parameters
            for (int i = 0; i < def.Parameters.Count; i++)
            {
                var param = def.Parameters[i];
                var pn    = new MethodParamNode { ParamName = param.Name, ParamType = param.Type };
                pn.NodeId = Guid.NewGuid().ToString();
                pn.SetGUID(pn.NodeId);
                pn.position = i < savedPositions.Count
                    ? savedPositions[i]
                    : new Rect(40f + i * 230f, 30f, 200f, 80f);
                try   { runtime.ParamGraphView.AddNode(pn); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"[VS] SyncParamGraph AddNode: {ex.Message}"); }
            }

            // Сохраняем обновлённое состояние в def.ParamGraph и синхронизируем ссылки на параметры в body-графе
            runtime.ParamGraphView.schedule.Execute(() =>
            {
                SyncMethodRuntime(runtime);
                SyncBodyParamReferences(runtime);
            }).ExecuteLater(150);
        }

        // ─── Параметры в теле метода ─────────────────────────────────────────

        /// <summary>
        /// Синхронизирует ноды-ссылки на параметры в графе тела метода.
        /// Для каждого параметра из <see cref="MethodDefinition.Parameters"/> в body-граф
        /// добавляется <see cref="MethodParamNode"/> с выходным портом «value».
        /// Пользователь может соединять эти ноды с другими нодами (например, сложить x + y).
        /// Параметры, которых больше нет — удаляются вместе со своими связями.
        /// </summary>
        private void SyncBodyParamReferences(MethodTabRuntime runtime)
        {
            if (runtime?.Definition == null) return;
            if (runtime.BodyGraphView == null || runtime.BodyInternalGraph == null) return;

            var def           = runtime.Definition;
            var currentParams = def.Parameters ?? new List<ParameterDefinition>();

            // Все MethodParamNode в body-графе — это авто-инжектированные ссылки на параметры.
            var existingRefs = runtime.BodyInternalGraph.nodes
                .OfType<MethodParamNode>()
                .ToList();

            var existingByName = existingRefs
                .ToDictionary(n => n.ParamName, StringComparer.Ordinal);
            var currentNames = new HashSet<string>(
                currentParams.Select(p => p.Name), StringComparer.Ordinal);

            // Удаляем ноды для параметров, которые были удалены
            foreach (var node in existingRefs)
            {
                if (!currentNames.Contains(node.ParamName))
                {
                    try   { runtime.BodyGraphView.RemoveNode(node); }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"[VS] SyncBodyParamRefs RemoveNode: {ex.Message}");
                    }
                }
                else
                {
                    // Обновляем тип, если он изменился
                    var p = currentParams.First(x => x.Name == node.ParamName);
                    node.ParamType = p.Type;
                }
            }

            // Добавляем ноды для новых параметров
            int existingCount = existingRefs.Count(n => currentNames.Contains(n.ParamName));
            for (int i = 0; i < currentParams.Count; i++)
            {
                var param = currentParams[i];
                if (existingByName.ContainsKey(param.Name)) continue; // уже есть

                var pn = new MethodParamNode { ParamName = param.Name, ParamType = param.Type };
                // Стабильный ID: не дублируется при повторных открытиях
                pn.NodeId = "_paramref_" + param.Name;
                pn.SetGUID(pn.NodeId);
                // Располагаем слева, стопкой по вертикали
                pn.position = new Rect(40f, 40f + existingCount * 120f, 200f, 80f);
                existingCount++;

                try   { runtime.BodyGraphView.AddNode(pn); }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[VS] SyncBodyParamRefs AddNode: {ex.Message}");
                }
            }
        }

        // ─── Импорт методов из парсера ───────────────────────────────────────

        /// <summary>
        /// Импортирует методы, обнаруженные парсером как inline-локальные функции,
        /// в <see cref="MethodRegistry"/>. Существующие методы обновляются (параметры,
        /// тип возврата, тело), новые — добавляются.
        /// </summary>
        internal void ImportDiscoveredMethods(IEnumerable<MethodInfo> discovered)
        {
            if (discovered == null) return;

            int count = 0;
            foreach (var mi in discovered)
            {
                if (string.IsNullOrWhiteSpace(mi.Id) || string.IsNullOrWhiteSpace(mi.Name)) continue;

                var existing = MethodRegistry.GetById(mi.Id);
                if (existing != null)
                {
                    existing.Name       = mi.Name;
                    existing.ReturnType = mi.ReturnType ?? "void";
                    existing.Parameters = BuildParamDefs(mi);
                    if (!string.IsNullOrEmpty(mi.ClassId))
                        existing.ClassId = mi.ClassId;
                    if (mi.BodyGraph?.Nodes?.Count > 0)
                        existing.BodyGraph = mi.BodyGraph;
                    MethodRegistry.Update(existing);
                }
                else
                {
                    var def = new MethodDefinition
                    {
                        Id         = mi.Id,
                        Name       = mi.Name,
                        ReturnType = mi.ReturnType ?? "void",
                        ClassId    = mi.ClassId ?? "",
                        Parameters = BuildParamDefs(mi),
                        BodyGraph  = mi.BodyGraph ?? new GraphData(),
                        ParamGraph = new GraphData()
                    };
                    MethodRegistry.Add(def);
                }
                count++;
            }

            if (count > 0)
                UnityEngine.Debug.Log($"[VS] Импортировано inline-методов: {count}");
        }

        // ─── Поля класса в теле метода ───────────────────────────────────────

        /// <summary>
        /// Синхронизирует ноды-ссылки на статические поля класса в body-графе метода.
        /// Для каждого поля из <see cref="ClassDefinition.Fields"/> добавляется
        /// <see cref="Nodes.Methods.FieldRefNode"/> со стабильным ID <c>"_fieldref_" + field.Id</c>.
        /// Поля, которых больше нет, — удаляются.
        /// </summary>
        private void SyncBodyFieldReferences(MethodTabRuntime runtime)
        {
            if (runtime?.Definition == null) return;
            if (runtime.BodyGraphView == null || runtime.BodyInternalGraph == null) return;

            var classId = runtime.Definition.ClassId;
            if (string.IsNullOrEmpty(classId)) return;

            var classDef = Classes.ClassRegistry.GetById(classId);
            var classFields = classDef?.Fields ?? new System.Collections.Generic.List<Classes.FieldDefinition>();

            // Все FieldRefNode в body-графе — авто-инжектированные ссылки на поля.
            var existingRefs = runtime.BodyInternalGraph.nodes
                .OfType<Nodes.Methods.FieldRefNode>()
                .ToList();

            // existingById может иметь пустой ключ для нод, инжектированных парсером
            // (parser не задаёт FieldId — используем "").
            var existingById = new Dictionary<string, Nodes.Methods.FieldRefNode>(StringComparer.Ordinal);
            foreach (var n in existingRefs)
                existingById[n.FieldId ?? ""] = n;

            var currentIds = new HashSet<string>(classFields.Select(f => f.Id), StringComparer.Ordinal);

            // Для нод с пустым FieldId (инжектированных парсером) — сопоставляем по имени
            // или по стабильному ID-суффиксу вида "_fieldref_<fieldName>".
            // После матча обновляем FieldId и регистрируем в existingById, чтобы не удалять
            // ноду и не создавать дубликат.
            const string FieldRefPrefix = "_fieldref_";
            foreach (var node in existingRefs)
            {
                if (!string.IsNullOrEmpty(node.FieldId)) continue;

                // Первичный матч: по FieldName
                var match = classFields.FirstOrDefault(f => f.Name == node.FieldName);

                // Вторичный матч: по суффиксу NodeId ("_fieldref_<fieldName>")
                if (match == null && node.NodeId != null && node.NodeId.StartsWith(FieldRefPrefix, StringComparison.Ordinal))
                {
                    var nameFromId = node.NodeId.Substring(FieldRefPrefix.Length);
                    match = classFields.FirstOrDefault(f =>
                        string.Equals(f.Name, nameFromId, StringComparison.Ordinal));
                }

                if (match != null)
                {
                    node.FieldId   = match.Id;
                    node.FieldType = match.Type;
                    node.FieldName = match.Name;
                    existingById[match.Id] = node; // регистрируем под корректным GUID
                }
            }

            // Удаляем только ноды для полей, которых больше нет в классе.
            // Ноды с активными соединениями НЕ удаляем — они инжектированы парсером
            // и несут реальные связи (exec-цепочка, data-рёбра).
            foreach (var node in existingRefs)
            {
                if (!currentIds.Contains(node.FieldId))
                {
                    // Проверяем, есть ли у ноды активные соединения в графе
                    bool hasConnections = runtime.BodyInternalGraph.edges
                        .Any(e => e.inputNode == node || e.outputNode == node);

                    if (hasConnections)
                    {
                        // Нода подключена — не удаляем. Пытаемся перепривязать по имени.
                        var rescue = classFields.FirstOrDefault(f =>
                            string.Equals(f.Name, node.FieldName, StringComparison.Ordinal));
                        if (rescue != null)
                        {
                            node.FieldId   = rescue.Id;
                            node.FieldType = rescue.Type;
                            existingById[rescue.Id] = node;
                        }
                        continue;
                    }

                    try   { runtime.BodyGraphView.RemoveNode(node); }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"[VS] SyncBodyFieldRefs RemoveNode: {ex.Message}");
                    }
                }
                else
                {
                    var f = classFields.First(x => x.Id == node.FieldId);
                    node.FieldType = f.Type;
                    node.FieldName = f.Name;
                }
            }

            // Добавляем ноды только для полей, которых ещё нет
            int count = existingRefs.Count(n => currentIds.Contains(n.FieldId));
            for (int i = 0; i < classFields.Count; i++)
            {
                var field = classFields[i];
                if (existingById.ContainsKey(field.Id)) continue;

                var fn = new Nodes.Methods.FieldRefNode
                {
                    FieldId   = field.Id,
                    FieldName = field.Name,
                    FieldType = field.Type
                };
                fn.NodeId = "_fieldref_" + field.Id;
                fn.SetGUID(fn.NodeId);
                fn.position = new UnityEngine.Rect(260f, 40f + count * 120f, 200f, 80f);
                count++;

                try   { runtime.BodyGraphView.AddNode(fn); }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[VS] SyncBodyFieldRefs AddNode: {ex.Message}");
                }
            }
        }

        private static List<ParameterDefinition> BuildParamDefs(MethodInfo mi)
        {
            var result = new List<ParameterDefinition>();
            var names = mi.ParamNames ?? new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                result.Add(new ParameterDefinition
                {
                    Name = names[i],
                    Type = (mi.ParamTypes != null && i < mi.ParamTypes.Count)
                        ? mi.ParamTypes[i] : "int"
                });
            }
            return result;
        }

        // ─── Удаление рантаймов ──────────────────────────────────────────────

        internal void DisposeMethodRuntime(string tabId)
        {
            if (!_methodTabRuntimes.TryGetValue(tabId, out var runtime)) return;
            SyncMethodRuntime(runtime);
            TearDownMethodRuntimeGraph(runtime);
            _methodTabRuntimes.Remove(tabId);
        }

        internal void DisposeAllMethodRuntimes()
        {
            foreach (var key in _methodTabRuntimes.Keys.ToList())
                DisposeMethodRuntime(key);
            _methodTabRuntimes.Clear();
        }

        private void TearDownMethodRuntimeGraph(MethodTabRuntime runtime)
        {
            if (runtime == null) return;

            runtime.SyncTicker?.Pause();
            runtime.SyncTicker = null;

            if (runtime.ParamGraphView != null)
            {
                runtime.ParamGraphView.NodeViewAdded -= OnNodeViewAdded;
                runtime.ParamGraphView.Dispose();
                runtime.ParamGraphView = null;
            }
            if (runtime.ParamInternalGraph != null)
            {
                DestroyImmediate(runtime.ParamInternalGraph);
                runtime.ParamInternalGraph = null;
            }

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
