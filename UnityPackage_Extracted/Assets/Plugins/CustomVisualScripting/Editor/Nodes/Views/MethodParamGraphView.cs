using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using CustomVisualScripting.Editor.Nodes.Methods;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Граф параметров метода. В отличие от основного <see cref="FilteredCreateMenuBaseGraphView"/>
    /// показывает в контекстном меню только ноды категории "Method/" (т. е. MethodParamNode)
    /// и генерирует уникальные имена при создании через ПКМ.
    /// </summary>
    public sealed class MethodParamGraphView : FilteredCreateMenuBaseGraphView
    {
        public MethodParamGraphView(EditorWindow window) : base(window) { }

        public override GraphContext GraphContext => GraphContext.MethodParam;

        /// <summary>Ссылка на body-граф метода — задаётся из CreateMethodRuntime.</summary>
        public FilteredCreateMenuBaseGraphView BodyGraphView { get; set; }

        /// <summary>
        /// Скрываем всё, кроме Method/* (MethodParamNode и др. ноды параметров).
        /// </summary>
        protected override bool ShouldHideMenuPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            return !path.StartsWith("Method/");
        }

        /// <summary>
        /// Переопределяем контекстное меню: при создании MethodParamNode
        /// автоматически присваиваем уникальное имя (param, param1, param2…).
        /// </summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 mouseGraphPos = contentViewContainer.WorldToLocal(evt.mousePosition);

            evt.menu.AppendAction("Добавить параметр", _ =>
            {
                // Собираем уже используемые имена
                var existing = new HashSet<string>(
                    graph?.nodes.OfType<MethodParamNode>().Select(pn => pn.ParamName)
                    ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                string name = "param";
                if (existing.Contains(name))
                {
                    int n = 1;
                    while (existing.Contains($"param{n}")) n++;
                    name = $"param{n}";
                }

                var pn = new MethodParamNode { ParamName = name, ParamType = "int" };
                pn.NodeId = Guid.NewGuid().ToString();
                pn.SetGUID(pn.NodeId);
                pn.position = new Rect(mouseGraphPos.x - 100, mouseGraphPos.y - 50, 200f, 80f);
                AddNode(pn);
            });
        }
    }
}
