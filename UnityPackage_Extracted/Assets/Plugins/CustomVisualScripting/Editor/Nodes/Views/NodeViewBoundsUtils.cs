using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GraphProcessor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VisualScripting.Core.Models;
using CustomVisualScripting.Editor.Nodes.Base;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Flow-ноды с вложенными панелями: минимальный размер зависит от <c>_panelsExpanded</c>.
    /// </summary>
    public interface IFlowSubGraphNodeMinBounds
    {
        (float minW, float minH) GetResolvedMinBounds();
    }

    /// <summary>
    /// Синхронизация Rect ноды с фактическим layout и политика collapse:
    /// для «литеральных» нод см. <see cref="CollapsibleBodyGraphNodeView"/>.
    /// </summary>
    public static class NodeViewBoundsUtils
    {
        public const float FlowIfWhileMinWidth = 350f;
        public const float FlowIfWhileMinHeight = 200f;
        /// <summary>Полное сворачивание ноды (заголовок + порты): узкая полоска без лишней ширины.</summary>
        public const float FlowIfWhileCollapsedMinWidth = 228f;
        public const float FlowIfWhileCollapsedMinHeight = 72f;

        public const float FlowElseMinWidth = 350f;
        /// <summary>Базовый минимум без лишней полосы снизу; фактическая высота определяется телом/subgraph.</summary>
        public const float FlowElseMinHeight = 152f;
        public const float FlowElseCollapsedMinWidth = 188f;
        public const float FlowElseCollapsedMinHeight = 72f;

        public const float FlowForMinWidth = 600f;
        public const float FlowForMinHeight = 260f;
        public const float FlowForCollapsedMinWidth = 268f;
        public const float FlowForCollapsedMinHeight = 78f;

        /// <summary>Свернута только строка Объявление/Граница/Шаг — «Тело» и строка заголовка «Условия» остаются.</summary>
        public const float FlowForConditionsStripHiddenMinHeight = 220f;

        /// <summary>If/While: свёрнуты обе подпанели (Условие и Тело) — только заголовки подграфов.</summary>
        public const float FlowIfWhileAllSubPanelsCollapsedMinHeight = 118f;

        /// <summary>For: скрыта строка условий и свёрнуто «Тело» — заголовок «Условия» + заголовок «Тело».</summary>
        public const float FlowForConditionsHiddenAndBodyCollapsedMinHeight = 118f;

        public const float DefaultGraphNodeMinWidth = 220f;
        public const float DefaultGraphNodeMinHeight = 88f;

        /// <summary>
        /// Ноды без своего UI (только порты GraphView): большой minHeight даёт пустой низ («подбородок»).
        /// </summary>
        public const float CompactDataNodeMinWidth = 200f;
        public const float CompactDataNodeMinHeight = 52f;

        static readonly ConditionalWeakTable<BaseNodeView, object> s_chromeRepairInstalled = new();
        static readonly ConditionalWeakTable<BaseNodeView, object> s_outlineHandlersInstalled = new();

        // Голубые цвета совпадают с Unity GraphView selection border.
        private static readonly Color SelectedNodeOutlineColor = new Color(68f / 255f, 192f / 255f, 255f / 255f, 1.0f);
        private static readonly Color HoveredNodeOutlineColor  = new Color(68f / 255f, 192f / 255f, 255f / 255f, 0.5f);

        /// <summary>
        /// Категория Flow: оставляем стандартное сворачивание ноды GraphView (плашки портов).
        /// Все остальные (math, logic, conversion, Mathf, Unity, Debug, литералы): сворачивание GraphView отключаем;
        /// у литералов сворачивается только тело через <see cref="CollapsibleBodyGraphNodeView"/>.
        /// </summary>
        public static bool IsFlowCategoryNodeType(NodeType t) =>
            t is NodeType.FlowIf or NodeType.FlowElse or NodeType.FlowFor or NodeType.FlowWhile
                or NodeType.ConsoleWriteLine or NodeType.DebugLog;

        /// <summary>
        /// Стандартный collapse GraphView (стрелка у заголовка, сворачивающая плашки портов)
        /// отключён у всех нод без исключений — ни Flow, ни Debug, ни Math/Logic и т.д.
        /// </summary>
        public static bool ShouldStripUnityPortCollapseChrome(BaseNodeView nodeView) =>
            nodeView != null;

        /// <summary>
        /// Левый верх ноды в координатах содержимого графа. У «тяжёлых» NodeView до первого прохода layout
        /// <see cref="GraphElement.GetPosition"/> часто даёт (0,0), хотя в модели узла уже записана точка создания из меню.
        /// </summary>
        public static Vector2 GetAuthoritativeNodeTopLeft(BaseNodeView nodeView)
        {
            if (nodeView?.nodeTarget == null)
                return Vector2.zero;

            var viewRect = nodeView.GetPosition();
            var modelRect = nodeView.nodeTarget.position;

            const float eps = 0.5f;
            bool viewOriginWrong =
                Mathf.Abs(viewRect.x) < eps &&
                Mathf.Abs(viewRect.y) < eps;

            bool modelHasCorner =
                Mathf.Abs(modelRect.x) >= eps ||
                Mathf.Abs(modelRect.y) >= eps;

            if (viewOriginWrong && modelHasCorner)
                return new Vector2(modelRect.xMin, modelRect.yMin);

            return new Vector2(viewRect.x, viewRect.y);
        }

        /// <summary>
        /// Только убрать стрелку сворачивания портов после <c>RefreshPorts</c> (не для Flow).
        /// </summary>
        public static void StripCollapseChromeAfterPossibleRefreshPorts(BaseNodeView nodeView)
        {
            if (nodeView == null || !ShouldStripUnityPortCollapseChrome(nodeView))
                return;

            nodeView.capabilities &= ~Capabilities.Collapsible;
            nodeView.expanded = true;
            RemoveUnityGraphViewCollapseChrome(nodeView);
            HideTitleButtonContainerChrome(nodeView);
        }

        /// <summary>
        /// Единый проход: при необходимости убрать collapse UI GraphView, подправить отступы/разделитель, синхронизировать rect с layout.
        /// После <c>RefreshPorts()</c> Unity снова вставляет <c>collapse-button</c> у не-Flow нод.
        /// </summary>
        public static void PerformFullNodeAppearanceFix(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            ApplyNodeOutlineColor(nodeView);

            if (ShouldStripUnityPortCollapseChrome(nodeView))
            {
                nodeView.capabilities &= ~Capabilities.Collapsible;
                nodeView.expanded = true;
                RemoveUnityGraphViewCollapseChrome(nodeView);
                HideTitleButtonContainerChrome(nodeView);
            }

            StripGraphNodeBottomPadding(nodeView);
            EnsurePortSectionBottomDivider(nodeView);

            var mins = ResolveSyncMinBounds(nodeView);
            ApplyNodeMinStyle(nodeView, mins.minW, mins.minH);
            SyncNodeRectToLayout(nodeView, mins.minW, mins.minH);
            ShrinkNodeRectToMeasuredLayout(nodeView, mins.minW, mins.minH);
            RefreshPortDividerVisualColor(nodeView);
        }

        static void HideTitleButtonContainerChrome(BaseNodeView nodeView)
        {
            if (nodeView.titleButtonContainer == null)
                return;

            nodeView.titleButtonContainer.style.display = DisplayStyle.None;
            nodeView.titleButtonContainer.style.width = 0;
            nodeView.titleButtonContainer.style.minWidth = 0;
            nodeView.titleButtonContainer.style.height = 0;
            nodeView.titleButtonContainer.style.minHeight = 0;
            nodeView.titleButtonContainer.style.marginLeft = 0;
            nodeView.titleButtonContainer.style.paddingLeft = 0;
            nodeView.titleButtonContainer.style.overflow = Overflow.Hidden;

            try
            {
                nodeView.titleButtonContainer.Clear();
            }
            catch
            {
                /* ignore */
            }
        }

        static void InstallPersistentChromeRepairIfNeeded(BaseNodeView nodeView)
        {
            if (nodeView == null || s_chromeRepairInstalled.TryGetValue(nodeView, out _))
                return;

            s_chromeRepairInstalled.Add(nodeView, new object());

            void DelayedPerform(int framesLater) =>
                nodeView.schedule.Execute(() => PerformFullNodeAppearanceFix(nodeView)).ExecuteLater(framesLater);

            DelayedPerform(0);
            DelayedPerform(1);
            DelayedPerform(2);
            DelayedPerform(5);
            DelayedPerform(15);
            DelayedPerform(30);

            nodeView.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                nodeView.schedule.Execute(() => PerformFullNodeAppearanceFix(nodeView)).ExecuteLater(0);
            });

            nodeView.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                nodeView.schedule.Execute(() => PerformFullNodeAppearanceFix(nodeView)).ExecuteLater(0);
            });

            // Selection state меняется после клика/drag-select: обновляем цвет рамки после смены selected.
            nodeView.RegisterCallback<MouseDownEvent>(_ =>
            {
                nodeView.schedule.Execute(() => PerformFullNodeAppearanceFix(nodeView)).ExecuteLater(0);
            });
            nodeView.RegisterCallback<MouseUpEvent>(_ =>
            {
                nodeView.schedule.Execute(() => PerformFullNodeAppearanceFix(nodeView)).ExecuteLater(0);
            });
        }

        /// <summary>
        /// Отключает сворачивание плашек портов и один раз ставит отложенные проходы + колбэки на layout.
        /// </summary>
        public static void DisableGraphViewPortCollapse(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            PerformFullNodeAppearanceFix(nodeView);
            InstallPersistentChromeRepairIfNeeded(nodeView);
        }

        /// <summary>
        /// Unity GraphView Node (см. UnityCsReference Node.cs): имя элемента со стрелкой и Clickable(ToggleCollapse).
        /// </summary>
        const string UnityGraphViewCollapseButtonElementName = "collapse-button";

        static void GatherVisualTree(VisualElement root, List<VisualElement> into)
        {
            if (root == null)
                return;
            into.Add(root);
            for (int i = 0; i < root.childCount; i++)
                GatherVisualTree(root.ElementAt(i), into);
        }

        /// <summary>
        /// Удаляет из дерева стандартные контролы сворачивания портов и остатки кнопок collapse/expand у списков портов.
        /// </summary>
        static void RemoveUnityGraphViewCollapseChrome(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            var tree = new List<VisualElement>();
            GatherVisualTree(nodeView, tree);

            foreach (var ve in tree.Where(e => e.name == UnityGraphViewCollapseButtonElementName).ToList())
            {
                try
                {
                    ve.RemoveFromHierarchy();
                }
                catch
                {
                    /* ignore */
                }
            }

            tree.Clear();
            GatherVisualTree(nodeView, tree);

            const string stripClass = "unity-graph-view-node__title-button-strip";
            foreach (var ve in tree.Where(e => e.GetClasses().Contains(stripClass)).ToList())
            {
                try
                {
                    ve.RemoveFromHierarchy();
                }
                catch
                {
                    /* ignore */
                }
            }

            RemoveResidualCollapseExpandPickerButtons(nodeView);
        }

        /// <summary>Остаточные кнопки collapse/expand у строк портов — удаляем из иерархии.</summary>
        static void RemoveResidualCollapseExpandPickerButtons(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            foreach (var btn in nodeView.Query<Button>().ToList())
            {
                var cls = string.Join(" ", btn.GetClasses());
                if (!cls.Contains("collapse") && !cls.Contains("expand"))
                    continue;
                try
                {
                    btn.RemoveFromHierarchy();
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        internal const string PortSectionBottomDividerName = "vs-port-section-bottom-divider";

        /// <summary>
        /// Линия под плашками связей: только у литералов (int/float/bool/string) и только при развёрнутом теле ноды.
        /// </summary>
        static bool ShouldShowPortSectionBottomDivider(BaseNodeView nodeView)
        {
            if (nodeView?.nodeTarget is not CustomBaseNode cn)
                return false;
            if (cn.NodeType is not (NodeType.LiteralInt or NodeType.LiteralFloat
                    or NodeType.LiteralBool or NodeType.LiteralString))
                return false;
            if (nodeView is CollapsibleBodyGraphNodeView col)
                return col.IsLiteralBodyExpanded;
            return false;
        }

        /// <summary>
        /// Линия между блоком портов и телом ноды — цвет как у разделителя под заголовком (серая тема GraphView).
        /// </summary>
        public static void EnsurePortSectionBottomDivider(BaseNodeView nodeView)
        {
            if (nodeView?.mainContainer == null || nodeView.controlsContainer == null)
                return;

            if (nodeView.controlsContainer.parent != nodeView.mainContainer)
                return;

            for (int i = nodeView.mainContainer.childCount - 1; i >= 0; i--)
            {
                var ch = nodeView.mainContainer.ElementAt(i);
                if (ch != null && ch.name == PortSectionBottomDividerName)
                    ch.RemoveFromHierarchy();
            }

            if (!ShouldShowPortSectionBottomDivider(nodeView))
                return;

            var sep = new VisualElement { name = PortSectionBottomDividerName };
            sep.style.height = 1;
            sep.style.backgroundColor = ResolveGraphNodeHorizontalRuleColor(nodeView);
            sep.style.marginLeft = 0;
            sep.style.marginRight = 0;
            sep.style.marginTop = 0;
            sep.style.marginBottom = 2;
            sep.style.flexShrink = 0;
            sep.style.flexGrow = 0;
            sep.style.width = Length.Percent(100);

            var idx = nodeView.mainContainer.IndexOf(nodeView.controlsContainer);
            if (idx >= 0)
                nodeView.mainContainer.Insert(idx, sep);
        }

        /// <summary>Подтягивает цвет разделителя после первого layout (border у title тогда уже известен).</summary>
        public static void RefreshPortDividerVisualColor(BaseNodeView nodeView)
        {
            if (nodeView?.mainContainer == null)
                return;
            for (int i = 0; i < nodeView.mainContainer.childCount; i++)
            {
                var ch = nodeView.mainContainer.ElementAt(i);
                if (ch != null && ch.name == PortSectionBottomDividerName)
                {
                    ch.style.backgroundColor = ResolveGraphNodeHorizontalRuleColor(nodeView);
                    break;
                }
            }
        }

        /// <summary>Тот же серый, что у линии между заголовком и портами (как у <c>unity-graph-view-node__contents</c> border-top).</summary>
        private static Color ResolveGraphNodeHorizontalRuleColor(BaseNodeView nodeView)
        {
            // Именно эта граница рисуется под заголовком над полоской портов в тёмной теме GraphView.
            var contents = nodeView.Q(className: "unity-graph-view-node__contents");
            if (contents != null)
            {
                var btc = contents.resolvedStyle.borderTopColor;
                if (btc.a > 0.03f)
                    return btc;
            }

            foreach (var row in nodeView.Query<VisualElement>(className: "unity-graph-view-node__top-input--horizontal").ToList())
            {
                var bb = row.resolvedStyle.borderTopColor;
                if (bb.a > 0.03f)
                    return bb;
                bb = row.resolvedStyle.borderBottomColor;
                if (bb.a > 0.03f)
                    return bb;
                break;
            }

            var tc = nodeView.titleContainer;
            if (tc != null)
            {
                var bc = tc.resolvedStyle.borderBottomColor;
                if (bc.a > 0.03f)
                    return bc;
            }

            if (nodeView.mainContainer != null)
            {
                var mt = nodeView.mainContainer.resolvedStyle.borderTopColor;
                if (mt.a > 0.03f)
                    return mt;
            }

            return new Color(0.22f, 0.22f, 0.22f, 1f);
        }

        /// <summary>
        /// Убирает лишний нижний отступ («подбородок») у контейнеров ноды.
        /// </summary>
        public static void StripGraphNodeBottomPadding(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            void ZeroBottom(VisualElement ve)
            {
                if (ve == null)
                    return;
                ve.style.paddingBottom = 0;
                ve.style.marginBottom = 0;
            }

            ZeroBottom(nodeView.mainContainer);
            ZeroBottom(nodeView.controlsContainer);
            nodeView.style.paddingBottom = 0;
            nodeView.style.marginBottom = 0;

            var contents = nodeView.Q(className: "unity-graph-view-node__contents");
            if (contents != null)
            {
                contents.style.minHeight = 0;
                contents.style.paddingBottom = 0;
                contents.style.marginBottom = 0;
            }

            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__top-input--horizontal").ToList())
                StripPortStripChrome(ve);
            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__top-input--vertical").ToList())
                StripPortStripChrome(ve);
            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__bottom-output--horizontal").ToList())
                StripPortStripChrome(ve);
            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__bottom-output--vertical").ToList())
                StripPortStripChrome(ve);
            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__top-container").ToList())
                StripPortStripChrome(ve);
            foreach (var ve in nodeView.Query<VisualElement>(className: "unity-graph-view-node__bottom-container").ToList())
                StripPortStripChrome(ve);
        }

        static void StripPortStripChrome(VisualElement ve)
        {
            if (ve == null)
                return;
            ve.style.paddingBottom = 0;
            ve.style.marginBottom = 0;
            ve.style.minHeight = 0;
        }

        /// <summary>
        /// Урезает rect по фактическому layout (убирает лишнюю высоту после перетаскивания).
        /// </summary>
        public static void ShrinkNodeRectToMeasuredLayout(BaseNodeView nodeView, float minW, float minH)
        {
            if (nodeView == null)
                return;

            var rect = nodeView.GetPosition();
            var xy = GetAuthoritativeNodeTopLeft(nodeView);
            float lh = nodeView.layout.height;
            float lw = nodeView.layout.width;
            if (lh < 2f)
                return;

            float nh = Mathf.Max(minH, lh);
            float nw = Mathf.Max(minW, lw >= 2f ? lw : rect.width);

            bool sizeUnchanged = nh >= rect.height - 0.75f && nw >= rect.width - 0.75f;
            bool posUnchanged =
                Mathf.Abs(xy.x - rect.x) < 0.5f &&
                Mathf.Abs(xy.y - rect.y) < 0.5f;

            if (sizeUnchanged && posUnchanged)
                return;

            // FIX: Защита от NaN
            if (float.IsNaN(xy.x) || float.IsNaN(xy.y) || float.IsNaN(nw) || float.IsNaN(nh))
                return;

            nodeView.SetPosition(new Rect(xy.x, xy.y, nw, nh));
            nodeView.RefreshPorts();
        }

        private static bool IsCompactDataNodeType(NodeType t) =>
            t is NodeType.MathAdd or NodeType.MathSubtract or NodeType.MathMultiply
                or NodeType.MathDivide or NodeType.MathModulo
                or NodeType.CompareEqual or NodeType.CompareGreater or NodeType.CompareLess
                or NodeType.CompareNotEqual or NodeType.CompareGreaterOrEqual or NodeType.CompareLessOrEqual
                or NodeType.LogicalAnd or NodeType.LogicalOr or NodeType.LogicalNot
                or NodeType.IntParse or NodeType.FloatParse or NodeType.ToStringConvert
                or NodeType.MathfAbs or NodeType.MathfMax or NodeType.MathfMin;

        public static (float minW, float minH) ResolveSyncMinBounds(BaseNodeView nodeView)
        {
            if (nodeView is IFlowSubGraphNodeMinBounds flow)
                return flow.GetResolvedMinBounds();

            if (nodeView is CollapsibleBodyGraphNodeView body)
                return body.GetLiteralBoundsMins();

            if (nodeView.nodeTarget is CustomBaseNode cn &&
                IsCompactDataNodeType(cn.NodeType))
                return (CompactDataNodeMinWidth, CompactDataNodeMinHeight);

            return (DefaultGraphNodeMinWidth, DefaultGraphNodeMinHeight);
        }

        public static void ApplyNodeMinStyle(BaseNodeView nodeView, float minW, float minH)
        {
            if (nodeView == null)
                return;
            nodeView.style.minWidth = minW;
            nodeView.style.minHeight = minH;
            
            // Сбросим жестко заданные размеры (inline style), которые не дают ноде сжаться
            nodeView.style.width = StyleKeyword.Auto;
            nodeView.style.height = StyleKeyword.Auto;
        }

        /// <summary>
        /// У flow-нод внутри корневого NodeView есть <see cref="SubGraphPanel"/> с вложенным GraphView;
        /// <see cref="VisualElement.Q(string)"/> по имени selection-border находит рамку вложенной ноды,
        /// а не внешнюю рамку самой flow-ноды.
        /// </summary>
        private static VisualElement ResolveOwnSelectionBorder(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return null;

            foreach (var ve in EnumerateDescendants(nodeView))
            {
                if (ve.name != "selection-border")
                    continue;
                if (!HasAncestorWhere(ve, static x => x is SubGraphPanel))
                    return ve;
            }

            return null;
        }

        private static IEnumerable<VisualElement> EnumerateDescendants(VisualElement root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.ElementAt(i);
                yield return child;
                foreach (var d in EnumerateDescendants(child))
                    yield return d;
            }
        }

        private static bool HasAncestorWhere(VisualElement element, Func<VisualElement, bool> predicate)
        {
            for (var p = element.parent; p != null; p = p.parent)
            {
                if (predicate(p))
                    return true;
            }

            return false;
        }

        private static void ApplyNodeOutlineColor(BaseNodeView nodeView)
        {
            if (nodeView?.nodeTarget is not CustomBaseNode customNode)
                return;

            // #selection-border — элемент контура ноды (GraphProcessor/Unity).
            var selBorder = ResolveOwnSelectionBorder(nodeView);
            if (selBorder == null)
                return;

            // Убираем прежний border с mainContainer (старая версия), чтобы не было двух рамок.
            if (nodeView.mainContainer != null)
            {
                nodeView.mainContainer.style.borderTopWidth    = StyleKeyword.Null;
                nodeView.mainContainer.style.borderRightWidth  = StyleKeyword.Null;
                nodeView.mainContainer.style.borderBottomWidth = StyleKeyword.Null;
                nodeView.mainContainer.style.borderLeftWidth   = StyleKeyword.Null;
                nodeView.mainContainer.style.borderTopColor    = StyleKeyword.Null;
                nodeView.mainContainer.style.borderRightColor  = StyleKeyword.Null;
                nodeView.mainContainer.style.borderBottomColor = StyleKeyword.Null;
                nodeView.mainContainer.style.borderLeftColor   = StyleKeyword.Null;
            }

            var categoryColor = ResolveNodeOutlineColor(customNode.NodeType);

            // Текущее состояние: выбранная нода → синий, остальное → цвет категории.
            var currentColor = nodeView.selected ? SelectedNodeOutlineColor : categoryColor;
            SetOutlineBorder(selBorder, 2f, currentColor);

            // Регистрируем hover-колбэки один раз на ноду.
            if (s_outlineHandlersInstalled.TryGetValue(nodeView, out _))
                return;

            s_outlineHandlersInstalled.Add(nodeView, new object());

            var capturedBorder   = selBorder;
            var capturedCategory = categoryColor;

            nodeView.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!nodeView.selected)
                    SetOutlineBorder(capturedBorder, 2f, HoveredNodeOutlineColor);
            });
            nodeView.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!nodeView.selected)
                    SetOutlineBorder(capturedBorder, 2f, capturedCategory);
            });
        }

        /// <summary>
        /// Только обновляет цвет рамки ноды без полного appearance-fix.
        /// Используется при глобальном сбросе выделения (клик по пустому пространству).
        /// </summary>
        public static void RefreshNodeOutlineColor(BaseNodeView nodeView)
        {
            if (nodeView?.nodeTarget is not CustomBaseNode customNode)
                return;

            var selBorder = ResolveOwnSelectionBorder(nodeView);
            if (selBorder == null)
                return;

            var color = nodeView.selected
                ? SelectedNodeOutlineColor
                : ResolveNodeOutlineColor(customNode.NodeType);

            SetOutlineBorder(selBorder, 2f, color);
        }

        private static void SetOutlineBorder(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth    = width;
            element.style.borderRightWidth  = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth   = width;
            element.style.borderTopColor    = color;
            element.style.borderRightColor  = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor   = color;
        }

        private static Color ResolveNodeOutlineColor(NodeType type)
        {
            switch (type)
            {
                // Литералы
                case NodeType.LiteralInt:
                case NodeType.LiteralFloat:
                case NodeType.LiteralBool:
                case NodeType.LiteralString:
                    return Hex("#4CAF50");

                // Математика
                case NodeType.MathAdd:
                case NodeType.MathSubtract:
                case NodeType.MathMultiply:
                case NodeType.MathDivide:
                case NodeType.MathModulo:
                case NodeType.MathfAbs:
                case NodeType.MathfMax:
                case NodeType.MathfMin:
                    return Hex("#2196F3");

                // Сравнения
                case NodeType.CompareEqual:
                case NodeType.CompareNotEqual:
                case NodeType.CompareGreater:
                case NodeType.CompareLess:
                case NodeType.CompareGreaterOrEqual:
                case NodeType.CompareLessOrEqual:
                    return Hex("#FF9800");

                // Логика
                case NodeType.LogicalAnd:
                case NodeType.LogicalOr:
                case NodeType.LogicalNot:
                    return Hex("#9C27B0");

                // Управление потоком
                case NodeType.FlowIf:
                case NodeType.FlowElse:
                case NodeType.FlowFor:
                case NodeType.FlowWhile:
                    return Hex("#F44336");

                // Ввод/Вывод
                case NodeType.ConsoleWriteLine:
                case NodeType.DebugLog:
                    return Hex("#FFFFFF");

                // Конвертация
                case NodeType.IntParse:
                case NodeType.FloatParse:
                case NodeType.ToStringConvert:
                    return Hex("#FFC107");

                // Unity
                case NodeType.UnityGetPosition:
                case NodeType.UnitySetPosition:
                case NodeType.UnityVector3:
                    return Hex("#8D6E63");

                default:
                    return Hex("#9E9E9E");
            }
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var parsed)
                ? parsed
                : new Color(0.62f, 0.62f, 0.62f, 1f);
        }

        /// <summary>
        /// Убирает лишний min-height у области контента, когда подпанели полностью скрыты (меньше «подбородка»).
        /// </summary>
        public static void SetFlowControlsMinHeightForCollapse(VisualElement controlsContainer, bool panelsExpanded)
        {
            if (controlsContainer == null)
                return;
            controlsContainer.style.minHeight = panelsExpanded ? StyleKeyword.Auto : 0;
        }

        public static void ForceSnapNodeSize(BaseNodeView nodeView, float w, float h)
        {
            if (nodeView == null)
                return;

            var xy = GetAuthoritativeNodeTopLeft(nodeView);
            nodeView.SetPosition(new Rect(xy.x, xy.y, w, h));
            nodeView.RefreshPorts();
        }

        /// <summary>
        /// Два прохода синхронизации: после скрытия блоков layout GraphView обновляется с задержкой —
        /// второй проход убирает лишнюю высоту/hit-area у ноды.
        /// Если <paramref name="forceSnapExactResolvedMin"/> true — принудительно задаём размер как min bounds
        /// (иначе SyncNodeRectToLayout может сохранять старый rect из‑за запаздывающего layout.height).
        /// </summary>
        public static void RunFlowBoundsSyncTwice(
            BaseNodeView nodeView,
            Func<(float minW, float minH)> getResolvedMinBounds,
            Func<bool> collapseWholePanelsHideSync,
            Func<bool> forceSnapExactResolvedMin = null)
        {
            if (nodeView == null)
                return;

            void Step()
            {
                var (minW, minH) = getResolvedMinBounds();
                ApplyNodeMinStyle(nodeView, minW, minH);

                bool wholeHidden = collapseWholePanelsHideSync();
                bool tightBoth = forceSnapExactResolvedMin != null && forceSnapExactResolvedMin.Invoke();

                if (wholeHidden || tightBoth)
                    ForceSnapNodeSize(nodeView, minW, minH);
                else
                    SyncNodeRectToLayout(nodeView, minW, minH);

                ShrinkNodeRectToMeasuredLayout(nodeView, minW, minH);
                RefreshPortDividerVisualColor(nodeView);

                // ForceSnapNodeSize → RefreshPorts() снова вставляет collapse-button из Unity Node.
                StripCollapseChromeAfterPossibleRefreshPorts(nodeView);
            }

            Step();
            nodeView.schedule.Execute(Step).ExecuteLater(1);
            nodeView.schedule.Execute(Step).ExecuteLater(3);
            nodeView.schedule.Execute(Step).ExecuteLater(8);

            nodeView.schedule.Execute(() =>
            {
                if (!collapseWholePanelsHideSync())
                    return;

                var (cw, ch) = getResolvedMinBounds();
                ApplyNodeMinStyle(nodeView, cw, ch);
                SyncWholeNodeCollapsedToLayout(nodeView, cw, ch);
            }).ExecuteLater(15);
        }

        /// <summary>
        /// После скрытия всего controlsContainer подгоняем rect под фактический layout (без искусственного запаса снизу).
        /// </summary>
        public static void SyncWholeNodeCollapsedToLayout(BaseNodeView nodeView, float minW, float minH)
        {
            if (nodeView == null)
                return;

            float layoutW = nodeView.layout.width;
            float layoutH = nodeView.layout.height;
            float resW = nodeView.resolvedStyle.width;
            float resH = nodeView.resolvedStyle.height;

            float contentW = layoutW >= 1f ? layoutW : Mathf.Max(minW, resW >= 1f ? resW : minW);
            float contentH = layoutH >= 1f ? layoutH : Mathf.Max(minH, resH >= 1f ? resH : minH);

            float w = Mathf.Max(minW, contentW);
            float h = Mathf.Max(minH, contentH);

            var xy = GetAuthoritativeNodeTopLeft(nodeView);
            if (float.IsNaN(w) || float.IsInfinity(w) || float.IsNaN(h) || float.IsInfinity(h))
                return;
                
            // FIX: Защита от NaN в координатах
            if (float.IsNaN(xy.x) || float.IsNaN(xy.y))
                return;

            nodeView.SetPosition(new Rect(xy.x, xy.y, w, h));
            nodeView.RefreshPorts();
        }

        /// <summary>
        /// Подгоняет размер ноды под контент (layout / resolvedStyle), не удерживая старый rect.
        /// </summary>
        public static void SyncNodeRectToLayout(BaseNodeView nodeView, float minWidth, float minHeight)
        {
            if (nodeView == null)
                return;

            var rect = nodeView.GetPosition();
            var xyTopLeft = GetAuthoritativeNodeTopLeft(nodeView);
            float layoutW = nodeView.layout.width;
            float layoutH = nodeView.layout.height;
            float resW = nodeView.resolvedStyle.width;
            float resH = nodeView.resolvedStyle.height;

            // Не брать Max(layout, resolved): resolved часто раздувает высоту («подбородок») после перетаскивания.
            float contentW = layoutW >= 1f ? layoutW : Mathf.Max(rect.width, minWidth);
            float contentH = layoutH >= 1f ? layoutH : Mathf.Max(rect.height, minHeight);

            if (layoutW < 1f && resW >= 1f)
                contentW = Mathf.Max(contentW, Mathf.Min(resW, rect.width > 1f ? rect.width : resW));
            if (layoutH < 1f && resH >= 1f)
                contentH = Mathf.Max(contentH, Mathf.Min(resH, rect.height > 1f ? rect.height : resH));

            float w = Mathf.Max(minWidth, contentW);
            float h = Mathf.Max(minHeight, contentH);

            if (float.IsNaN(w) || float.IsInfinity(w) || float.IsNaN(h) || float.IsInfinity(h))
                return;

            bool sizeUnchanged = Mathf.Abs(w - rect.width) < 0.5f && Mathf.Abs(h - rect.height) < 0.5f;
            bool posUnchanged =
                Mathf.Abs(xyTopLeft.x - rect.x) < 0.5f &&
                Mathf.Abs(xyTopLeft.y - rect.y) < 0.5f;

            if (sizeUnchanged && posUnchanged)
                return;

            // FIX: Если xyTopLeft.x или y это NaN (например, из-за скрытого контейнера), не ломаем координаты
            if (float.IsNaN(xyTopLeft.x) || float.IsNaN(xyTopLeft.y))
                return;

            nodeView.SetPosition(new Rect(xyTopLeft.x, xyTopLeft.y, w, h));
            nodeView.RefreshPorts();
        }

        public static void MakeNodeEdgesResizable(BaseNodeView nodeView)
        {
            if (nodeView == null)
                return;

            foreach (var child in nodeView.Children())
            {
                if (child is EdgeResizer) return; // already added
            }

            // Remove native resizer and standard layout constraints
            nodeView.capabilities &= ~Capabilities.Resizable;

            var topResizer = new EdgeResizer(EdgeResizerDirection.Top, delta => {
                var r = nodeView.GetPosition();
                var mins = ResolveSyncMinBounds(nodeView);
                float newY = r.y + delta.y;
                float newH = r.height - delta.y;
                if (newH >= mins.minH) {
                    nodeView.SetPosition(new Rect(r.x, newY, r.width, newH));
                }
            });
            var bottomResizer = new EdgeResizer(EdgeResizerDirection.Bottom, delta => {
                var r = nodeView.GetPosition();
                var mins = ResolveSyncMinBounds(nodeView);
                float newH = r.height + delta.y;
                if (newH >= mins.minH) {
                    nodeView.SetPosition(new Rect(r.x, r.y, r.width, newH));
                }
            });
            var leftResizer = new EdgeResizer(EdgeResizerDirection.Left, delta => {
                var r = nodeView.GetPosition();
                var mins = ResolveSyncMinBounds(nodeView);
                float newX = r.x + delta.x;
                float newW = r.width - delta.x;
                if (newW >= mins.minW) {
                    nodeView.SetPosition(new Rect(newX, r.y, newW, r.height));
                }
            });
            var rightResizer = new EdgeResizer(EdgeResizerDirection.Right, delta => {
                var r = nodeView.GetPosition();
                var mins = ResolveSyncMinBounds(nodeView);
                float newW = r.width + delta.x;
                if (newW >= mins.minW) {
                    nodeView.SetPosition(new Rect(r.x, r.y, newW, r.height));
                }
            });

            nodeView.Add(topResizer);
            nodeView.Add(bottomResizer);
            nodeView.Add(leftResizer);
            nodeView.Add(rightResizer);

            topResizer.BringToFront();
            bottomResizer.BringToFront();
            leftResizer.BringToFront();
            rightResizer.BringToFront();
        }
    }
}
