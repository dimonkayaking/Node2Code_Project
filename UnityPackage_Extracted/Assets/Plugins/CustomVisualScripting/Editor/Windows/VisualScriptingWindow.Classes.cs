using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GraphProcessor;
using Newtonsoft.Json;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Nodes.Views;
using UnityEngine;
using UnityEngine.UIElements;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor.Windows
{
    public partial class VisualScriptingWindow
    {
        // ─── Префикс вкладки класса (зарезервирован для совместимости Tabs.cs) ────
        private const string ClassTabPrefix = "class:";

        // ─── Заглушка ClassTabRuntime (класс-вкладки больше не открываются) ──────
        // Словарь всегда пустой; методы ниже — no-op.
        // Необходимо для совместимости с VisualScriptingWindow.Tabs.cs.
        private readonly Dictionary<string, ClassTabRuntime> _classTabRuntimes =
            new(StringComparer.Ordinal);

        private sealed class ClassTabRuntime
        {
            public ClassDefinition Definition;
            public VisualElement   Container;
            public BaseGraphView   BodyGraphView;  // всегда null в новой схеме
        }

        // ─── Сериализация классов ─────────────────────────────────────────────────
        [Serializable]
        private class ClassListWrapper
        {
            public List<ClassDefinition> Classes = new();
        }

        // ─── Save / Load ──────────────────────────────────────────────────────────

        internal void SyncAllClassRuntimes() { /* no-op: классы хранятся в ClassRegistry */ }

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

        // ─── API совместимости (вызывается из NodeToolbarView и Tabs.cs) ──────────

        /// <summary>
        /// В новой схеме: добавляет ClassNode на главный граф (если ещё нет) и перестраивает его.
        /// Классовые вкладки (ClassBodyGraph) больше не открываются.
        /// </summary>
        public void OpenClassTab(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId)) return;
            AddClassNodeIfMissing(classId);
            RecreateGraphView();
        }

        /// <summary>Больше не используется: классовые вкладки удалены.</summary>
        public void CloseClassTab(string classId) { /* no-op */ }

        private void AddClassNodeIfMissing(string classId)
        {
            if (_currentGraph?.LogicGraph == null) return;
            var graph = _currentGraph.LogicGraph;
            if (graph.Nodes.Exists(n => n.Type == NodeType.ClassNode && n.Value == classId))
                return;

            var cls = ClassRegistry.GetById(classId);
            if (cls == null) return;

            float x = 60f + graph.Nodes.Count(n => n.Type == NodeType.ClassNode) * 320f;
            graph.Nodes.Add(new NodeData
            {
                Id           = "classnode_" + classId,
                Type         = NodeType.ClassNode,
                Value        = classId,
                VariableName = cls.Name
            });
            _currentGraph.VisualNodes?.Add(new Integration.Models.VisualNodeData
            {
                NodeId   = "classnode_" + classId,
                Position = new Vector2(x, 60f)
            });
        }

        /// <summary>Обновляет заголовок вкладки класса (если вкладка когда-либо открывалась).</summary>
        public void RefreshClassTabTitle(string classId, string newName)
        {
            var tabId = ClassTabPrefix + classId;
            var tab = _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));
            if (tab == null) return;
            tab.Title = newName;
            RenderTabs();
        }

        internal void DisposeClassRuntime(string tabId)
        {
            _classTabRuntimes.Remove(tabId);
        }

        internal void DisposeAllClassRuntimes()
        {
            _classTabRuntimes.Clear();
        }

        private static void SyncClassRuntime(ClassTabRuntime runtime) { /* no-op */ }

        // ─── Вспомогательный метод: RebuildMainGraphFromClasses ─────────────────
        // Вызывается при инициализации нового проекта или OnClear,
        // чтобы заполнить главный граф ClassNode-нодами из ClassRegistry.

        internal void RebuildMainGraphWithClassNodes()
        {
            if (_currentGraph?.LogicGraph == null) return;

            var graph = _currentGraph.LogicGraph;

            // Убираем старые ClassNode-ноды из графа (если есть)
            graph.Nodes.RemoveAll(n => n.Type == NodeType.ClassNode);
            graph.Edges.RemoveAll(e =>
                !graph.Nodes.Exists(n => n.Id == e.FromNodeId || n.Id == e.ToNodeId));

            // Добавляем ClassNode для каждого класса из реестра
            float x = 60f;
            foreach (var cls in ClassRegistry.Classes)
            {
                // Проверяем — возможно нода уже есть (Id как ClassId)
                if (graph.Nodes.Exists(n => n.Type == NodeType.ClassNode && n.Value == cls.Id))
                {
                    x += 320f;
                    continue;
                }

                graph.Nodes.Add(new NodeData
                {
                    Id           = "classnode_" + cls.Id,
                    Type         = NodeType.ClassNode,
                    Value        = cls.Id,
                    VariableName = cls.Name
                });
                _currentGraph.VisualNodes?.Add(new Integration.Models.VisualNodeData
                {
                    NodeId   = "classnode_" + cls.Id,
                    Position = new Vector2(x, 60f)
                });
                x += 320f;
            }
        }

        // ─── Автосоздание Program + Main при пустом реестре ──────────────────────

        /// <summary>
        /// Если ClassRegistry пуст — создаёт класс Program + метод Main
        /// и добавляет ClassNode на главный граф.
        /// </summary>
        // Шаблон кода по умолчанию — отображается при первом открытии плагина
        internal const string DefaultCodeTemplate =
            "class Program : MonoBehaviour\n{\n    void Start()\n    {\n        // Ваш код\n    }\n\n    void Update()\n    {\n        // Ваш код\n    }\n}";

        internal void EnsureProgramClassExists()
        {
            if (ClassRegistry.Classes.Count > 0) return;

            var program = new ClassDefinition { Name = "Program" };
            ClassRegistry.Add(program);

            // Стабильные ID совпадают с тем, что создаёт парсер при разборе class-кода
            // (BuildMethodInfoSignature → "__classfn__" + name), поэтому при повторном
            // парсинге методы обновляются, а не дублируются.
            // Start/Update — нестатические void-методы MonoBehaviour (IsStatic=false, IsPublic=false).
            CreateMonoMessageMethod(program.Id, "Start");
            CreateMonoMessageMethod(program.Id, "Update");

            RebuildMainGraphWithClassNodes();

            // Показываем сгенерированный код при первом открытии
            if (_codeEditor != null && string.IsNullOrWhiteSpace(_codeEditor.Code))
            {
                var generated = GenerateCurrentCode();
                _codeEditor.Code = string.IsNullOrWhiteSpace(generated) ? DefaultCodeTemplate : generated;
            }
        }

        /// <summary>
        /// Создаёт нестатический void-метод MonoBehaviour (Start/Update) со стабильным
        /// ID <c>"__classfn__" + name</c> и регистрирует его в <see cref="Methods.MethodRegistry"/>.
        /// </summary>
        private static void CreateMonoMessageMethod(string classId, string name)
        {
            var method = new Methods.MethodDefinition
            {
                Id         = "__classfn__" + name,
                Name       = name,
                ReturnType = "void",
                IsPublic   = false, // void Start()/Update() — без модификатора (private по умолчанию C#)
                IsStatic   = false, // нестатические методы экземпляра MonoBehaviour
                ClassId    = classId,
                BodyGraph  = new GraphData(),
                ParamGraph = new GraphData()
            };
            Methods.MethodRegistry.Add(method);
        }
    }
}
