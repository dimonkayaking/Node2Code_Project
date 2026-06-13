using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GraphProcessor;
using CustomVisualScripting.Integration;
using CustomVisualScripting.Integration.Models;
using CustomVisualScripting.Windows.Views;
using CustomVisualScripting.Editor.Nodes.Base;
using CustomVisualScripting.Editor.Nodes.Views;
using CustomVisualScripting.Runtime.Execution;
using VisualScripting.Core.Models;
using VisualScripting.Core.Parsers;
using CustomToolbar = CustomVisualScripting.Windows.Views.ToolbarView;
using CustomVisualScripting.Editor;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Methods;

namespace CustomVisualScripting.Editor.Windows
{
    public partial class VisualScriptingWindow : EditorWindow
    {
        public static VisualScriptingWindow ActiveWindow { get; private set; }
        private const float MinNodeWidth = 220f;
        private const float MinNodeHeight = 120f;
        private const float AutoLayoutSpacingX = 280f;
        private const float AutoLayoutSpacingY = 180f;
        private const float AutoLayoutColumnGap = 60f;
        private const float AutoLayoutRowGap = 40f;
        private const float OverlapResolveMargin = 24f;

        private CompleteGraphData _currentGraph;
        private BaseGraph _internalGraph;
        private FilteredCreateMenuBaseGraphView _graphView;
        private VisualElement _graphContainer;
        
        private CodeEditorView _codeEditor;
        private CustomToolbar _toolbar;
        private ErrorPanel _errorPanel;
        private ConsoleView _consoleView;
        
        private string _currentFilePath;
        private bool _hasUnsavedChanges = false;
        
        private CSharpProcessRunner _csharpRunner;
        private bool _forceAutoLayoutNextUpdate;
        private bool _collapseFlowSubspacesOnNextRebuild;
        
        [MenuItem("Tools/Node2Code")]
        public static void OpenWindow()
        {
            var window = GetWindow<VisualScriptingWindow>();
            window.titleContent = new GUIContent("Node2Code");
            window.minSize = new Vector2(900, 600);
            window.Show();
        }
        
        private void OnEnable()
        {
            ActiveWindow = this;
            ParserBridge.Initialize();
            GeneratorBridge.Initialize();
            Application.logMessageReceived += OnLogMessageReceived;
            ClassRegistry.OnChanged  += OnClassRegistryChanged;
            MethodRegistry.OnChanged += OnMethodRegistryChanged;
            _csharpRunner = new CSharpProcessRunner();
            _csharpRunner.OnOutput += OnCSharpRunnerOutput;

            _currentGraph = new CompleteGraphData();
            _hasUnsavedChanges = false;
        }

        private void OnDisable()
        {
            if (ReferenceEquals(ActiveWindow, this))
                ActiveWindow = null;
            Application.logMessageReceived -= OnLogMessageReceived;
            ClassRegistry.OnChanged  -= OnClassRegistryChanged;
            MethodRegistry.OnChanged -= OnMethodRegistryChanged;
            if (_csharpRunner != null)
            {
                _csharpRunner.OnOutput -= OnCSharpRunnerOutput;
                _csharpRunner.Dispose();
                _csharpRunner = null;
            }
            CleanupGraph();
        }

        private void OnCSharpRunnerOutput(string message, LogType type)
        {
            var cleaned = message;
            var unityRelayType = type;
            var shouldRelayToUnity = false;

            if (!string.IsNullOrEmpty(message))
            {
                if (message.StartsWith(CSharpProcessRunner.UnityDebugLogErrorMarker, StringComparison.Ordinal))
                {
                    cleaned = message.Substring(CSharpProcessRunner.UnityDebugLogErrorMarker.Length);
                    unityRelayType = LogType.Error;
                    shouldRelayToUnity = true;
                }
                else if (message.StartsWith(CSharpProcessRunner.UnityDebugLogWarningMarker, StringComparison.Ordinal))
                {
                    cleaned = message.Substring(CSharpProcessRunner.UnityDebugLogWarningMarker.Length);
                    unityRelayType = LogType.Warning;
                    shouldRelayToUnity = true;
                }
                else if (message.StartsWith(CSharpProcessRunner.UnityDebugLogMarker, StringComparison.Ordinal))
                {
                    cleaned = message.Substring(CSharpProcessRunner.UnityDebugLogMarker.Length);
                    unityRelayType = LogType.Log;
                    shouldRelayToUnity = true;
                }
            }

            if (shouldRelayToUnity)
            {
                switch (unityRelayType)
                {
                    case LogType.Error:
                        UnityEngine.Debug.LogError(cleaned);
                        break;
                    case LogType.Warning:
                        UnityEngine.Debug.LogWarning(cleaned);
                        break;
                    default:
                        UnityEngine.Debug.Log(cleaned);
                        break;
                }
            }

            EditorApplication.delayCall += () =>
            {
                if (_consoleView != null)
                    _consoleView.AddMessage(cleaned, unityRelayType);

                // Ошибки компиляции/выполнения → в ErrorPanel над консолью
                if (unityRelayType == LogType.Error && _errorPanel != null)
                    _errorPanel.AddError(cleaned);

                if (_toolbar != null && type == LogType.Error && !shouldRelayToUnity)
                    _toolbar.SetStatusError("Ошибка выполнения C#");
            };
        }
        
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_consoleView != null)
            {
                _consoleView.AddMessage(condition, type);
            }
        }
        
        // Флаг подавления обработчиков реестров во время массового импорта
        private bool _suppressRegistryHandlers;

        private void OnParse()
        {
            _toolbar.SetStatusWarning("Парсинг...");

            var result = ParserBridge.ParseWithMethods(_codeEditor.Code, ToMethodInfos(MethodRegistry.Methods));

            if (result.HasErrors)
            {
                _errorPanel.ShowErrors(result.Errors);
                _toolbar.SetStatusError($"Ошибок: {result.Errors.Count}");
                return;
            }

            _errorPanel.Clear();

            if (result.HasClassWrapper)
            {
                ImportClassBasedParseResult(result);
                _toolbar.SetStatusSuccess($"Разобрано классов: {result.DiscoveredClasses.Count}, методов: {MethodRegistry.Methods.Count}");
            }
            else
            {
                // Плоский код без классов — старое поведение
                ImportDiscoveredMethods(result.DiscoveredMethods);
                _currentGraph = new CompleteGraphData();
                _currentGraph = GraphConverter.LogicToComplete(result.Graph, _currentGraph);
                _hasUnsavedChanges = true;
                _forceAutoLayoutNextUpdate = true;
                _collapseFlowSubspacesOnNextRebuild = true;
                ResetTabsToFileOnly();
                RecreateGraphView();
                _toolbar.SetStatusSuccess($"Создано нод: {result.Graph.Nodes.Count}");
            }
        }

        /// <summary>
        /// Импортирует результат парсинга class-based кода:
        /// создаёт ClassDefinition для каждого класса, MethodDefinition для каждого метода,
        /// и перестраивает главный граф с ClassNode-нодами.
        /// </summary>
        private void ImportClassBasedParseResult(ParseResult result)
        {
            _suppressRegistryHandlers = true;
            try
            {
                // 1. Создаём/находим ClassDefinition для каждого обнаруженного класса
                //    и синхронизируем поля (сохраняем ID по имени для стабильных FieldRef-ссылок)
                var classMap = new Dictionary<string, ClassDefinition>(StringComparer.Ordinal);
                foreach (var parsedClass in result.DiscoveredClasses)
                {
                    var existing = ClassRegistry.Classes.FirstOrDefault(
                        c => string.Equals(c.Name, parsedClass.Name, StringComparison.Ordinal));

                    ClassDefinition def;
                    if (existing != null)
                    {
                        def = existing;
                    }
                    else
                    {
                        def = new ClassDefinition { Name = parsedClass.Name };
                        ClassRegistry.Add(def);
                    }

                    // Синхронизируем поля: сохраняем ID для полей с совпадающим именем,
                    // создаём новые для добавленных, удаляем убранные.
                    var existingByName = def.Fields
                        .ToDictionary(f => f.Name, StringComparer.Ordinal);
                    def.Fields.Clear();
                    foreach (var pf in parsedClass.Fields)
                    {
                        if (existingByName.TryGetValue(pf.Name, out var ef))
                        {
                            ef.Type         = pf.Type;
                            ef.DefaultValue = pf.DefaultValue;
                            def.Fields.Add(ef);
                        }
                        else
                        {
                            def.Fields.Add(new Classes.FieldDefinition
                            {
                                Name         = pf.Name,
                                Type         = pf.Type,
                                DefaultValue = pf.DefaultValue
                            });
                        }
                    }

                    classMap[parsedClass.Name] = def;
                }

                // 1b. Синхронизируем BaseClassId: разрешаем имя родителя → его ClassDefinition.Id
                foreach (var parsedClass in result.DiscoveredClasses)
                {
                    if (string.IsNullOrEmpty(parsedClass.BaseClassName)) continue;
                    if (!classMap.TryGetValue(parsedClass.Name, out var childDef)) continue;
                    if (!classMap.TryGetValue(parsedClass.BaseClassName, out var parentDef)) continue;
                    childDef.BaseClassId = parentDef.Id;
                }

                // 2. Строим маппинг: имя метода → имя класса
                var methodToClass = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var parsedClass in result.DiscoveredClasses)
                    foreach (var methodName in parsedClass.MethodNames)
                        methodToClass[methodName] = parsedClass.Name;

                // 3. Ставим ClassId на DiscoveredMethods до ImportDiscoveredMethods
                foreach (var mi in result.DiscoveredMethods)
                {
                    if (methodToClass.TryGetValue(mi.Name, out var clsName) &&
                        classMap.TryGetValue(clsName, out var classDef))
                        mi.ClassId = classDef.Id;
                }

                // 4. Импортируем методы (Add, Reset, GetValue и т.п.)
                ImportDiscoveredMethods(result.DiscoveredMethods);

                // 5. Создаём/обновляем метод Main с телом из result.Graph
                if (!string.IsNullOrEmpty(result.MainClassName) &&
                    classMap.TryGetValue(result.MainClassName, out var mainClass))
                {
                    const string mainMethodId = "__classfn__Main";
                    var existingMain = MethodRegistry.GetById(mainMethodId);
                    if (existingMain != null)
                    {
                        existingMain.BodyGraph = result.Graph;
                        existingMain.ClassId   = mainClass.Id;
                        MethodRegistry.Update(existingMain);
                    }
                    else
                    {
                        MethodRegistry.Add(new MethodDefinition
                        {
                            Id         = mainMethodId,
                            Name       = "Main",
                            ReturnType = "void",
                            ClassId    = mainClass.Id,
                            BodyGraph  = result.Graph,
                            ParamGraph = new GraphData()
                        });
                    }
                }
            }
            finally
            {
                _suppressRegistryHandlers = false;
            }

            // 6. Перестраиваем главный граф: только ClassNode-ноды
            _currentGraph = new CompleteGraphData();
            RebuildMainGraphWithClassNodes();

            _hasUnsavedChanges = true;
            _forceAutoLayoutNextUpdate = true;
            ResetTabsToFileOnly();
            RecreateGraphView();
        }
        
        private void OnGenerate()
        {
            _toolbar.SetStatusWarning("Генерация...");

            SyncFullGraphFromView();
            SyncAllMethodRuntimes();

            string code = GenerateCurrentCode();
            _codeEditor.Code = code;
            UpdateCodeEditorSyntaxColors();

            _toolbar.SetStatusSuccess("Код сгенерирован");
        }

        /// <summary>
        /// Генерирует код по текущему состоянию реестров.
        /// Если в ClassRegistry есть классы — используется GenerateWithClasses (ООП-режим).
        /// Иначе — старый GenerateWithMethods для совместимости с плоским кодом.
        /// </summary>
        private string GenerateCurrentCode()
        {
            if (ClassRegistry.Classes.Count > 0)
            {
                return GeneratorBridge.GenerateWithClasses(
                    ToClassInfos(ClassRegistry.GetAll()),
                    ToMethodInfos(MethodRegistry.Methods));
            }
            return GeneratorBridge.GenerateWithMethods(
                _currentGraph.LogicGraph, ToMethodInfos(MethodRegistry.Methods));
        }
        
        private async void OnRun()
        {
            if (_currentGraph?.LogicGraph == null || _currentGraph.LogicGraph.Nodes.Count == 0)
            {
                _toolbar.SetStatusError("Нет графа для выполнения");
                return;
            }

            if (_csharpRunner == null)
            {
                _toolbar.SetStatusError("Runner не инициализирован");
                return;
            }

            if (_csharpRunner.IsRunning)
            {
                _toolbar.SetStatusWarning("Выполнение уже запущено");
                return;
            }
            
            _toolbar.SetRunMode(true);
            _toolbar.SetStatusWarning("Выполнение...");
            _errorPanel?.Clear();   // сбрасываем ошибки предыдущего запуска
            SyncFullGraphFromView();
            SyncAllMethodRuntimes();
            var code = GenerateCurrentCode();
            _codeEditor.Code = code;

            try
            {
                var exitCode = await _csharpRunner.RunAsync(code);
                EditorApplication.delayCall += () =>
                {
                    _toolbar.SetRunMode(false);
                    _toolbar.SetStatusSuccess(exitCode == 0
                        ? "Выполнение завершено"
                        : $"Выполнение завершено с ошибкой ({exitCode})");
                };
            }
            catch (Exception e)
            {
                EditorApplication.delayCall += () =>
                {
                    _toolbar.SetRunMode(false);
                    _toolbar.SetStatusError($"Ошибка: {e.Message}");
                    Debug.LogError($"[VS] Ошибка выполнения: {e.Message}");
                };
            }
        }
        
        private void OnStop()
        {
            _csharpRunner?.Stop();
            _toolbar.SetRunMode(false);
            _toolbar.SetStatusNormal("Выполнение остановлено");
        }
        
        private void OnSave()
        {
            if (!HasCurrentFilePath())
            {
                OnSaveAs();
                return;
            }

            SyncFullGraphFromView();
            SyncAllMethodRuntimes();
            SyncAllClassRuntimes();
            string code = EnsureRequiredUsings(GenerateCurrentCode());
            _codeEditor.Code = code;
            SaveCodeToPath(_currentFilePath, code);
            SaveMethodsToPath(GetMethodsFilePath(_currentFilePath));
            SaveClassesToPath(GetClassesFilePath(_currentFilePath));
        }

        private void OnSaveAs()
        {
            string defaultName = HasCurrentFilePath()
                ? Path.GetFileName(_currentFilePath)
                : "Script.cs";

            string path = EditorUtility.SaveFilePanel("Сохранить код как", Application.dataPath, defaultName, "cs");
            if (string.IsNullOrEmpty(path)) return;

            SyncFullGraphFromView();
            SyncAllMethodRuntimes();
            SyncAllClassRuntimes();
            string code = EnsureRequiredUsings(GenerateCurrentCode());
            _codeEditor.Code = code;
            _currentFilePath = path;
            RefreshFileTabTitle();
            SaveCodeToPath(path, code);
            SaveMethodsToPath(GetMethodsFilePath(path));
            SaveClassesToPath(GetClassesFilePath(path));
        }

        private bool HasCurrentFilePath() => !string.IsNullOrWhiteSpace(_currentFilePath);

        // ─── Авто-using при сохранении ────────────────────────────────────────

        private static readonly (System.Text.RegularExpressions.Regex pattern, string ns)[] UsingRules =
        {
            (new System.Text.RegularExpressions.Regex(@"\bConsole\.", System.Text.RegularExpressions.RegexOptions.Compiled),
             "System"),
            (new System.Text.RegularExpressions.Regex(@"\bMath\.", System.Text.RegularExpressions.RegexOptions.Compiled),
             "System"),
            (new System.Text.RegularExpressions.Regex(@"\bDebug\.Log", System.Text.RegularExpressions.RegexOptions.Compiled),
             "UnityEngine"),
            (new System.Text.RegularExpressions.Regex(@"\b(List|Dictionary|HashSet|Queue|Stack)<", System.Text.RegularExpressions.RegexOptions.Compiled),
             "System.Collections.Generic"),
            (new System.Text.RegularExpressions.Regex(@"\.(Select|Where|FirstOrDefault|OrderBy|Any|All)\(", System.Text.RegularExpressions.RegexOptions.Compiled),
             "System.Linq"),
        };

        /// <summary>
        /// Добавляет отсутствующие using-директивы в начало кода на основе
        /// обнаруженных паттернов использования.
        /// </summary>
        private static string EnsureRequiredUsings(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;

            // Собираем уже присутствующие using-и
            var present = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    code, @"^\s*using\s+([\w.]+)\s*;",
                    System.Text.RegularExpressions.RegexOptions.Multiline))
                present.Add(m.Groups[1].Value);

            // Определяем, какие using нужно добавить.
            // HashSet гарантирует отсутствие дублей, если несколько правил дают один namespace
            // (например Console. и Math. оба требуют "System").
            var toAdd = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var (pattern, ns) in UsingRules)
            {
                if (!present.Contains(ns) && pattern.IsMatch(code))
                    toAdd.Add(ns);
            }

            if (toAdd.Count == 0) return code;

            var sb = new System.Text.StringBuilder();
            foreach (var ns in toAdd.OrderBy(x => x))   // стабильный порядок
                sb.AppendLine($"using {ns};");
            sb.AppendLine();
            sb.Append(code);
            return sb.ToString();
        }

        private void SaveCodeToPath(string path, string code)
        {
            try
            {
                File.WriteAllText(path, code);
                _toolbar.SetStatusSuccess($"Сохранено: {Path.GetFileName(path)}");
                _hasUnsavedChanges = false;
            }
            catch (Exception e)
            {
                _toolbar.SetStatusError($"Ошибка сохранения: {e.Message}");
            }
        }
        
        private void OnLoad()
        {
            string path = EditorUtility.OpenFilePanel("Загрузить C# код", Application.dataPath, "cs");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var code = File.ReadAllText(path);
                _currentFilePath = path;
                _codeEditor.Code = code;
                ResetTabsToFileOnly();
                RefreshFileTabTitle();

                // Загружаем классы и методы до парсинга
                LoadClassesFromPath(GetClassesFilePath(path));
                LoadMethodsFromPath(GetMethodsFilePath(path));

                var result = ParserBridge.ParseWithMethods(code, ToMethodInfos(MethodRegistry.Methods));
                if (result.HasErrors)
                {
                    _errorPanel.ShowErrors(result.Errors);
                    _toolbar.SetStatusError($"Ошибок: {result.Errors.Count}");
                    return;
                }

                _errorPanel.Clear();

                if (result.HasClassWrapper)
                {
                    ImportClassBasedParseResult(result);
                    _hasUnsavedChanges = false;
                    _toolbar.SetStatusSuccess($"Загружено: {Path.GetFileName(path)} ({result.DiscoveredClasses.Count} кл., {MethodRegistry.Methods.Count} мет.)");
                }
                else
                {
                    ImportDiscoveredMethods(result.DiscoveredMethods);
                    _currentGraph = new CompleteGraphData();
                    _currentGraph = GraphConverter.LogicToComplete(result.Graph, _currentGraph);
                    _hasUnsavedChanges = false;
                    _collapseFlowSubspacesOnNextRebuild = true;
                    ResetTabsToFileOnly();
                    RecreateGraphView();
                    _toolbar.SetStatusSuccess($"Загружено и распарсено: {Path.GetFileName(path)}");
                }
            }
            catch (Exception e)
            {
                _toolbar.SetStatusError($"Ошибка загрузки: {e.Message}");
            }
        }
        
        private void OnClear()
        {
            _codeEditor.Clear();
            _currentGraph = new CompleteGraphData();
            _errorPanel.Clear();
            _currentFilePath = null;
            _hasUnsavedChanges = false;
            _collapseFlowSubspacesOnNextRebuild = false;
            MethodRegistry.Clear();
            ClassRegistry.Clear();
            ResetTabsToFileOnly();

            // Создаём стартовый класс Program + метод Main
            EnsureProgramClassExists();

            RecreateGraphView();
            _toolbar.SetStatusNormal("Очищено");
        }
        
        private void SyncFullGraphFromView()
        {
            if (_graphView == null || _internalGraph == null) return;

            var graphNodes = _internalGraph.nodes.OfType<CustomBaseNode>().ToList();
            GraphDataViewSync.SyncGraphDataNodesAndEdgesFromView(_currentGraph.LogicGraph, graphNodes, _graphView);

            SaveVisualNodePositions();
            _hasUnsavedChanges = true;
        }
        
        private void SaveVisualNodePositions()
        {
            if (_currentGraph?.VisualNodes == null || _graphView == null || _internalGraph == null)
                return;
            
            _currentGraph.VisualNodes.Clear();

            foreach (var customNode in _internalGraph.nodes.OfType<CustomBaseNode>())
            {
                if (!_graphView.nodeViewsPerNode.TryGetValue(customNode, out var nodeView))
                    continue;

                _currentGraph.VisualNodes.Add(new VisualNodeData
                {
                    NodeId = customNode.NodeId,
                    Position = nodeView.GetPosition().position,
                    IsCollapsed = false
                });
            }
        }
        
        private CustomBaseNode CreateNodeFromData(NodeData data) =>
            EditorNodeFactory.Create(data);

        // ─── Конвертация MethodDefinition → MethodInfo ────────────────────────
        /// <summary>
        /// Преобразует Editor-модели методов в Core-DTO для передачи в ParserBridge / GeneratorBridge.
        /// Вынесено сюда, чтобы Integration-сборка не зависела от Editor-сборки.
        /// </summary>
        /// <summary>
        /// Реагирует на изменения ClassRegistry.
        /// Если класс был удалён — убирает его ClassNode с главного графа и перестраивает view.
        /// Также удаляет из MethodRegistry методы удалённого класса.
        /// </summary>
        /// <summary>
        /// Реагирует на изменения MethodRegistry: обновляет все ClassNodeView на главном графе.
        /// Вызывается из окна напрямую, чтобы гарантировать обновление даже если событие
        /// пришло из контекста popup-окна (CreateMethodPopup.ShowUtility).
        /// </summary>
        private void OnMethodRegistryChanged()
        {
            if (_suppressRegistryHandlers || _graphView == null) return;
            foreach (var nodeView in _graphView.nodeViews)
            {
                if (nodeView is Nodes.Views.ClassNodeView classView)
                    classView.RefreshContent();
            }

            // Имена методов могли измениться — обновляем подсветку в редакторе кода
            UpdateCodeEditorSyntaxColors();
        }

        private void OnClassRegistryChanged()
        {
            if (_suppressRegistryHandlers || _currentGraph?.LogicGraph == null) return;

            var existingIds = new HashSet<string>(ClassRegistry.Classes.Select(c => c.Id));

            // Удаляем ClassNode-ноды удалённых классов
            bool classDeleted = _currentGraph.LogicGraph.Nodes
                .RemoveAll(n => n.Type == NodeType.ClassNode && !existingIds.Contains(n.Value)) > 0;

            if (classDeleted)
            {
                // Чистим висячие рёбра
                var nodeIds = new HashSet<string>(_currentGraph.LogicGraph.Nodes.Select(n => n.Id));
                _currentGraph.LogicGraph.Edges
                    .RemoveAll(e => !nodeIds.Contains(e.FromNodeId) || !nodeIds.Contains(e.ToNodeId));

                // Удаляем методы, чей класс больше не существует
                var orphanMethods = MethodRegistry.Methods
                    .Where(m => !string.IsNullOrEmpty(m.ClassId) && !existingIds.Contains(m.ClassId))
                    .Select(m => m.Id)
                    .ToList();
                foreach (var id in orphanMethods)
                    MethodRegistry.Remove(id);

                if (_graphView != null)
                    RecreateGraphView();
            }
            else
            {
                // Класс не удалён — обновляем содержимое ClassNodeView (поля, переименование)
                if (_graphView != null)
                    foreach (var nodeView in _graphView.nodeViews)
                        if (nodeView is Nodes.Views.ClassNodeView classView)
                            classView.RefreshContent();

                // Сразу обновляем FieldRef-ноды во всех открытых вкладках методов
                foreach (var runtime in _methodTabRuntimes.Values)
                    SyncBodyFieldReferences(runtime);
            }

            // Имена классов могли измениться — обновляем подсветку в редакторе кода
            UpdateCodeEditorSyntaxColors();
        }

        private static IEnumerable<ClassInfo> ToClassInfos(IEnumerable<ClassDefinition> defs)
        {
            if (defs == null) yield break;
            var list = defs.ToList();
            var byId = list
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
                .ToDictionary(c => c.Id, StringComparer.Ordinal);

            foreach (var c in list)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Id)) continue;

                // Разрешаем имя родителя: пользовательский класс → MonoBehaviour → ничего
                string baseName = "";
                if (!string.IsNullOrEmpty(c.BaseClassId) &&
                    byId.TryGetValue(c.BaseClassId, out var parentDef))
                    baseName = parentDef.Name;
                else if (c.InheritsMonoBehaviour)
                    baseName = "MonoBehaviour";

                yield return new ClassInfo
                {
                    Id       = c.Id,
                    Name     = c.Name,
                    BaseName = baseName,
                    Fields   = c.Fields?.ConvertAll(f => new ClassFieldData
                    {
                        Name         = f.Name,
                        Type         = f.Type,
                        DefaultValue = f.DefaultValue
                    }) ?? new System.Collections.Generic.List<ClassFieldData>()
                };
            }
        }

        private static IEnumerable<MethodInfo> ToMethodInfos(IEnumerable<MethodDefinition> defs)
        {
            if (defs == null) yield break;
            foreach (var m in defs)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.Id)) continue;
                yield return new MethodInfo
                {
                    Id            = m.Id,
                    Name          = m.Name,
                    ReturnType    = m.ReturnType ?? "void",
                    ClassId       = m.ClassId    ?? "",
                    ClassName     = ClassRegistry.GetById(m.ClassId)?.Name ?? "",
                    ParamNames    = m.Parameters?.ConvertAll(p => p.Name)         ?? new System.Collections.Generic.List<string>(),
                    ParamTypes    = m.Parameters?.ConvertAll(p => p.Type)         ?? new System.Collections.Generic.List<string>(),
                    ParamDefaults = m.Parameters?.ConvertAll(p => p.DefaultValue) ?? new System.Collections.Generic.List<string>(),
                    BodyGraph     = m.BodyGraph
                };
            }
        }

    }
}