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
using CustomToolbar = CustomVisualScripting.Windows.Views.ToolbarView;
using CustomVisualScripting.Editor;
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
                {
                    _consoleView.AddMessage(cleaned, unityRelayType);
                }

                if (_toolbar != null && type == LogType.Error && !shouldRelayToUnity)
                {
                    _toolbar.SetStatusError("Ошибка выполнения C#");
                }
            };
        }
        
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_consoleView != null)
            {
                _consoleView.AddMessage(condition, type);
            }
        }
        
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

            // Импортируем методы, найденные при парсинге (inline-локальные функции)
            ImportDiscoveredMethods(result.DiscoveredMethods);

            _errorPanel.Clear();

            _currentGraph = new CompleteGraphData();
            _currentGraph = GraphConverter.LogicToComplete(result.Graph, _currentGraph);
            _hasUnsavedChanges = true;
            _forceAutoLayoutNextUpdate = true;
            _collapseFlowSubspacesOnNextRebuild = true;
            ResetTabsToFileOnly();
            
            RecreateGraphView();
            
            _toolbar.SetStatusSuccess($"Создано нод: {result.Graph.Nodes.Count}, связей: {result.Graph.Edges.Count}");
        }
        
        private void OnGenerate()
        {
            _toolbar.SetStatusWarning("Генерация...");

            SyncFullGraphFromView();
            SyncAllMethodRuntimes();

            string code = GeneratorBridge.GenerateWithMethods(_currentGraph.LogicGraph, ToMethodInfos(MethodRegistry.Methods));
            _codeEditor.Code = code;
            UpdateCodeEditorSyntaxColors();

            _toolbar.SetStatusSuccess("Код сгенерирован");
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
            SyncFullGraphFromView();
            SyncAllMethodRuntimes();
            var code = GeneratorBridge.GenerateWithMethods(_currentGraph.LogicGraph, ToMethodInfos(MethodRegistry.Methods));
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
            string code = GeneratorBridge.GenerateWithMethods(_currentGraph.LogicGraph, ToMethodInfos(MethodRegistry.Methods));
            _codeEditor.Code = code;
            SaveCodeToPath(_currentFilePath, code);
            SaveMethodsToPath(GetMethodsFilePath(_currentFilePath));
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
            string code = GeneratorBridge.GenerateWithMethods(_currentGraph.LogicGraph, ToMethodInfos(MethodRegistry.Methods));
            _codeEditor.Code = code;
            _currentFilePath = path;
            RefreshFileTabTitle();
            SaveCodeToPath(path, code);
            SaveMethodsToPath(GetMethodsFilePath(path));
        }

        private bool HasCurrentFilePath() => !string.IsNullOrWhiteSpace(_currentFilePath);

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

                // Загружаем методы сначала, чтобы парсер мог распознать их вызовы
                LoadMethodsFromPath(GetMethodsFilePath(path));

                var result = ParserBridge.ParseWithMethods(code, ToMethodInfos(MethodRegistry.Methods));
                if (result.HasErrors)
                {
                    _errorPanel.ShowErrors(result.Errors);
                    _toolbar.SetStatusError($"Ошибок: {result.Errors.Count}");
                    return;
                }

                // Импортируем методы, найденные при парсинге (inline-локальные функции)
                ImportDiscoveredMethods(result.DiscoveredMethods);

                _errorPanel.Clear();
                _currentGraph = new CompleteGraphData();
                _currentGraph = GraphConverter.LogicToComplete(result.Graph, _currentGraph);
                _hasUnsavedChanges = false;
                _collapseFlowSubspacesOnNextRebuild = true;
                RecreateGraphView();
                _toolbar.SetStatusSuccess($"Загружено и распарсено: {Path.GetFileName(path)}");
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
            ResetTabsToFileOnly();
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
        private static IEnumerable<MethodInfo> ToMethodInfos(IEnumerable<MethodDefinition> defs)
        {
            if (defs == null) yield break;
            foreach (var m in defs)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.Id)) continue;
                yield return new MethodInfo
                {
                    Id         = m.Id,
                    Name       = m.Name,
                    ReturnType = m.ReturnType ?? "void",
                    ParamNames = m.Parameters?.ConvertAll(p => p.Name) ?? new System.Collections.Generic.List<string>(),
                    ParamTypes = m.Parameters?.ConvertAll(p => p.Type) ?? new System.Collections.Generic.List<string>(),
                    BodyGraph  = m.BodyGraph
                };
            }
        }

    }
}