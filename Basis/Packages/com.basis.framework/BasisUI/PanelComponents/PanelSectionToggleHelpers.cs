using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    public static class PanelSectionToggleHelpers
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

            string stateKey = ResolveSectionKey(sectionToggle);
            bool effectiveOpen = stateKey != null ? BasisMenuStateMemory.GetSection(stateKey, defaultOpen) : defaultOpen;

            group.gameObject.SetActive(effectiveOpen);
            sectionToggle.SetExpandedWithoutNotify(effectiveOpen);
            sectionToggle.OnExpandedChanged += visible =>
            {
                group.gameObject.SetActive(visible);
                if (stateKey != null)
                {
                    BasisMenuStateMemory.SetSection(stateKey, visible);
                }
                onExpandedChanged?.Invoke(visible);
            };
        }

        /// <summary>
        /// Builds a section whose collapsible content is added directly under the bar (no
        /// nested group box). The toggle title is the section header; every child added to
        /// <paramref name="container"/> by <paramref name="buildContent"/> collapses with it.
        /// For sections with conditional sub-visibility, re-apply it inside
        /// <paramref name="onExpandedChanged"/> (guard on the visible flag).
        /// </summary>
        public static PanelSectionToggle CreateCollapsibleFlatSection(
            RectTransform container,
            string title,
            Action buildContent,
            bool startExpanded = false,
            Action<bool> onExpandedChanged = null)
        {
            PanelSectionToggle sectionToggle = PanelSectionToggle.CreateNewEntry(container);
            sectionToggle.SetTitle(title);

            int start = container.childCount;
            buildContent?.Invoke();

            int count = container.childCount - start;
            Component[] contents = new Component[count < 0 ? 0 : count];
            for (int i = 0; i < contents.Length; i++)
            {
                contents[i] = container.GetChild(start + i);
            }

            FinalizeCollapsibleContents(sectionToggle, startExpanded, onExpandedChanged, contents);
            return sectionToggle;
        }

        /// <summary>
        /// Builds a flat section whose content is created when it expands and destroyed when it
        /// collapses, so nothing is paid for a section the user never opens and the rows re-read
        /// live state every time it is reopened. Use this instead of
        /// <see cref="CreateCollapsibleFlatSection"/> for sections that are expensive to build or
        /// that snapshot runtime data. <paramref name="buildContent"/> adds its rows to
        /// <paramref name="container"/> as usual; they are moved directly beneath the header.
        /// </summary>
        public static PanelSectionToggle CreateLazyFlatSection(
            RectTransform container,
            string title,
            Action buildContent,
            bool startExpanded = false,
            Action<bool> onExpandedChanged = null)
        {
            PanelSectionToggle sectionToggle = PanelSectionToggle.CreateNewEntry(container);
            sectionToggle.SetTitle(title);

            string stateKey = ResolveSectionKey(sectionToggle);
            bool effectiveOpen = stateKey != null ? BasisMenuStateMemory.GetSection(stateKey, startExpanded) : startExpanded;

            List<GameObject> content = new();

            void Populate()
            {
                int start = container.childCount;
                buildContent?.Invoke();

                for (int i = start; i < container.childCount; i++)
                {
                    content.Add(container.GetChild(i).gameObject);
                }

                // The builder appends to the end of the tab; walk the new rows back up so they
                // sit under this section's header rather than below every later section.
                int insertAt = sectionToggle.transform.GetSiblingIndex() + 1;
                for (int i = 0; i < content.Count; i++)
                {
                    content[i].transform.SetSiblingIndex(insertAt + i);
                }
            }

            void Clear()
            {
                for (int i = 0; i < content.Count; i++)
                {
                    GameObject row = content[i];
                    if (row == null)
                    {
                        continue;
                    }

                    // Destroy is deferred to end of frame; deactivate first so the dying rows
                    // drop out of the layout pass that this collapse triggers.
                    row.SetActive(false);
                    UnityEngine.Object.Destroy(row);
                }

                content.Clear();
            }

            if (effectiveOpen)
            {
                Populate();
            }

            sectionToggle.SetExpandedWithoutNotify(effectiveOpen);
            sectionToggle.OnExpandedChanged += visible =>
            {
                Clear();
                if (visible)
                {
                    Populate();
                }

                if (stateKey != null)
                {
                    BasisMenuStateMemory.SetSection(stateKey, visible);
                }

                onExpandedChanged?.Invoke(visible);
            };

            return sectionToggle;
        }

        /// <summary>
        /// Flat-section finalizer for inline call sites: registers every child added to
        /// <paramref name="container"/> at or after <paramref name="startIndex"/> as the
        /// section's collapsible content (no nested group box). Capture the index right
        /// after creating the section toggle, add the rows directly to the container, then
        /// call this. Re-apply any conditional sub-visibility inside
        /// <paramref name="onExpandedChanged"/> (guard on the visible flag).
        /// </summary>
        public static void FinalizeFlatSectionFromIndex(
            PanelSectionToggle sectionToggle,
            RectTransform container,
            int startIndex,
            bool startExpanded,
            Action<bool> onExpandedChanged = null)
        {
            int count = container.childCount - startIndex;
            Component[] contents = new Component[count < 0 ? 0 : count];
            for (int i = 0; i < contents.Length; i++)
            {
                contents[i] = container.GetChild(startIndex + i);
            }

            FinalizeCollapsibleContents(sectionToggle, startExpanded, onExpandedChanged, contents);
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

            string stateKey = ResolveSectionKey(sectionToggle);
            bool effectiveOpen = stateKey != null ? BasisMenuStateMemory.GetSection(stateKey, defaultOpen) : defaultOpen;

            SetContentsActive(contents, effectiveOpen);
            sectionToggle.SetExpandedWithoutNotify(effectiveOpen);
            sectionToggle.OnExpandedChanged += visible =>
            {
                SetContentsActive(contents, visible);
                if (stateKey != null)
                {
                    BasisMenuStateMemory.SetSection(stateKey, visible);
                }
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

        private static string ResolveSectionKey(PanelSectionToggle sectionToggle)
        {
            if (!BasisMenuStateMemory.Enabled || !BasisMenuStateMemory.HasScope)
            {
                return null;
            }

            string title = sectionToggle != null && sectionToggle.Descriptor != null ? sectionToggle.Descriptor.Title : null;
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            return BasisMenuStateMemory.NextSectionKey(title);
        }
    }
}
