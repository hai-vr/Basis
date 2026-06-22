using System;
using UnityEngine;

namespace Basis.BasisUI
{
    internal static class PanelSectionToggleHelpers
    {
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
    }
}
