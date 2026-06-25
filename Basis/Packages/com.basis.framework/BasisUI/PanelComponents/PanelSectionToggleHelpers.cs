using System;
using UnityEngine;

namespace Basis.BasisUI
{
    internal static class PanelSectionToggleHelpers
    {
        /// <summary>
        /// Creates a titled content group for a section toggle and registers it for divider ownership.
        /// Callers should add child controls while the returned group is still active, then finalize it.
        /// </summary>
        public static PanelElementDescriptor CreateCollapsibleContentGroup(
            PanelSectionToggle sectionToggle,
            RectTransform parent,
            string title,
            bool showGroupTitle = true)
        {
            sectionToggle.SetTitle(title);

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group,
                parent);
            if (showGroupTitle)
            {
                group.SetTitle(title);
            }
            else if (group.Header != null)
            {
                group.Header.gameObject.SetActive(false);
            }

            sectionToggle.RegisterContentContainer(group);
            return group;
        }

        /// <summary>
        /// Applies the initial expanded state and wires a section toggle to show or hide its content group.
        /// The optional callback is for parent layout rebuilds or other caller-specific side effects.
        /// </summary>
        public static void FinalizeCollapsibleGroup(
            PanelSectionToggle sectionToggle,
            PanelElementDescriptor group,
            bool defaultOpen,
            Action<bool> onExpandedChanged = null)
        {
            if (sectionToggle == null || group == null)
            {
                return;
            }

            group.gameObject.SetActive(defaultOpen);
            sectionToggle.SetExpandedWithoutNotify(defaultOpen);
            sectionToggle.OnExpandedChanged += visible =>
            {
                group.gameObject.SetActive(visible);
                onExpandedChanged?.Invoke(visible);
            };
        }

        /// <summary>
        /// Registers one or more existing controls or groups as content under a section toggle.
        /// Use this when content rows are already created and should collapse without adding another group.
        /// </summary>
        public static void FinalizeCollapsibleContents(
            PanelSectionToggle sectionToggle,
            bool defaultOpen,
            Action<bool> onExpandedChanged,
            params Component[] contents)
        {
            if (sectionToggle == null || contents == null)
            {
                return;
            }

            for (int i = 0; i < contents.Length; i++)
            {
                Component content = contents[i];
                if (content != null)
                {
                    sectionToggle.RegisterContentContainer(content);
                }
            }

            SetContentsActive(contents, defaultOpen);
            sectionToggle.SetExpandedWithoutNotify(defaultOpen);
            sectionToggle.OnExpandedChanged += visible =>
            {
                SetContentsActive(contents, visible);
                onExpandedChanged?.Invoke(visible);
            };
        }

        /// <summary>
        /// Applies the same active state to every registered content item.
        /// </summary>
        private static void SetContentsActive(Component[] contents, bool active)
        {
            for (int i = 0; i < contents.Length; i++)
            {
                SetContentActive(contents[i], active);
            }
        }

        /// <summary>
        /// Uses the panel descriptor when available so panel components follow existing UI activation behavior.
        /// </summary>
        private static void SetContentActive(Component content, bool active)
        {
            switch (content)
            {
                case null:
                    return;
                case PanelComponent panelComponent:
                    panelComponent.Descriptor.SetActive(active);
                    return;
                case PanelElementDescriptor descriptor:
                    descriptor.SetActive(active);
                    return;
                default:
                    content.gameObject.SetActive(active);
                    return;
            }
        }
    }
}
