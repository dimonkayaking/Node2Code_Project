using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    internal static class SubspaceHeaderLinkRow
    {
        /// <summary>
        /// Зона коллапса ноды (~стрелка справа) + небольшой зазор до блока ссылок с <c>right: 30</c>.
        /// </summary>
        private const float TitleReserveRightExtrasPx = 54f;

        /// <summary>Фиксированный зазор между текстом заголовка и блоком ссылок.</summary>
        private const float TitleTitleLinkGapPx = 11f;

        /// <summary>
        /// Ищем текст заголовка ноды GraphProcessor: по имени, по классу, затем первый не-absolute <see cref="Label"/>.
        /// </summary>
        private static Label ResolveTitleLabel(VisualElement titleContainer)
        {
            var byName = titleContainer.Q<Label>("title-label");
            if (byName != null)
                return byName;

            foreach (var lab in titleContainer.Query<Label>().ToList())
            {
                if (lab.ClassListContains("title-label"))
                    return lab;
            }

            foreach (var child in titleContainer.Children())
            {
                if (child is not Label lab)
                    continue;
                if (child.style.position == Position.Absolute)
                    continue;
                return lab;
            }

            return null;
        }

        private static void ClearTitleReserve(VisualElement titleContainer)
        {
            titleContainer.style.paddingRight = 0;
            titleContainer.style.maxWidth = StyleKeyword.Null;

            var titleLabel = ResolveTitleLabel(titleContainer);
            if (titleLabel != null)
            {
                titleLabel.style.marginRight = 0;
                titleLabel.style.maxWidth = StyleKeyword.Null;
            }
        }

        /// <param name="titleContainer">Шапка ноды (GraphProcessor): резервируем место под блок ссылок.</param>
        public static VisualElement Create(VisualElement titleContainer, string nodeId,
            params (string label, SubspaceKind kind)[] links)
        {
            var row = new VisualElement();
            row.AddToClassList("node-subspace-links-row");
            row.style.position = Position.Absolute;
            row.style.right = 30;
            row.style.top = 2;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            for (var i = 0; i < links.Length; i++)
            {
                var capture = links[i];
                var btn = new Button(() =>
                    VisualScriptingWindow.ActiveWindow?.OpenSubspaceFromNode(nodeId, capture.kind))
                {
                    text = capture.label
                };
                btn.AddToClassList("node-subspace-link");
                if (i > 0)
                    btn.style.marginLeft = 4;
                row.Add(btn);
            }

            var paddingRetryCount = 0;

            void UpdateTitlePadding(GeometryChangedEvent _ = null)
            {
                float w = row.resolvedStyle.width;
                if ((float.IsNaN(w) || w < 1f) && row.worldBound.width > 1f)
                    w = row.worldBound.width;

                if (float.IsNaN(w))
                    w = 0f;

                float reserve = w + TitleReserveRightExtrasPx + TitleTitleLinkGapPx;
                var titleLabel = ResolveTitleLabel(titleContainer);

                if (titleLabel != null)
                {
                    titleLabel.style.marginRight = reserve;
                    float cw = titleContainer.resolvedStyle.width;
                    if (!float.IsNaN(cw) && cw > 1f)
                        titleLabel.style.maxWidth = Mathf.Max(40f, cw - reserve);
                }
                else
                    titleContainer.style.paddingRight = reserve;

                if (w < 1f && paddingRetryCount < 6)
                {
                    paddingRetryCount++;
                    row.schedule.Execute(() => UpdateTitlePadding()).ExecuteLater(100);
                }
            }

            row.RegisterCallback<GeometryChangedEvent>(UpdateTitlePadding);
            row.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                UpdateTitlePadding();
                row.schedule.Execute(() => UpdateTitlePadding()).ExecuteLater(0);
                row.schedule.Execute(() => UpdateTitlePadding()).ExecuteLater(50);
            });
            row.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                row.UnregisterCallback<GeometryChangedEvent>(UpdateTitlePadding);
                ClearTitleReserve(titleContainer);
            });

            return row;
        }
    }
}
