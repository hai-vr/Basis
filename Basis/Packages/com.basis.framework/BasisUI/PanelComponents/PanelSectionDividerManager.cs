using System.Collections.Generic;
using Basis.BasisUI.Styling;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    internal sealed class PanelSectionDividerManager
    {
        private const float DividerContainerHeight = 4f;
        private const float DividerHeight = 2f;
        private const float DividerHorizontalInset = 12f;
        private static readonly Color FallbackDividerColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        private readonly PanelSectionToggle _owner;
        private readonly List<PanelSectionContentMarker> _contentMarkers;

        private GameObject _dividerAbove;
        private Image _dividerAboveImage;
        private PanelSectionToggle _dividerAboveController;
        private bool _ownsDividerAbove;
        private GameObject _dividerBelow;
        private Image _dividerBelowImage;
        private bool _ownsDividerBelow;

        public PanelSectionDividerManager(PanelSectionToggle owner, List<PanelSectionContentMarker> contentMarkers)
        {
            _owner = owner;
            _contentMarkers = contentMarkers;
        }

        public void CreateDividerAbove()
        {
            RectTransform rectTransform = _owner.rectTransform;
            if (_dividerAbove != null || rectTransform == null || rectTransform.parent == null)
            {
                return;
            }

            int currentIndex = rectTransform.GetSiblingIndex();
            PanelSectionToggle previousToggle = ResolvePreviousSectionToggle(currentIndex, out bool previousToggleIsDirectNeighbor);
            if (previousToggle != null && !previousToggleIsDirectNeighbor)
            {
                // Content separates these sections, so the previous section needs
                // its own bottom divider instead of sharing this section's top divider.
                if (previousToggle.DividerManager.EnsureOwnedDividerBelow())
                {
                    return;
                }
            }

            GameObject divider = CreateDividerObject(currentIndex, previousToggle);
            Image dividerImage = ResolveDividerImage(divider);

            _dividerAbove = divider;
            _dividerAboveImage = dividerImage;
            _dividerAboveController = previousToggleIsDirectNeighbor ? previousToggle : null;
            _ownsDividerAbove = true;

            if (previousToggle != null && previousToggleIsDirectNeighbor)
            {
                previousToggle.DividerManager.SetDividerBelow(divider, dividerImage);
            }

            UpdateVisibility();
        }

        public void Release()
        {
            if (_ownsDividerAbove)
            {
                ClearSiblingReferenceTo(_dividerAbove);
                DestroyDivider(ref _dividerAbove, ref _dividerAboveImage);
            }
            else
            {
                _dividerAbove = null;
                _dividerAboveImage = null;
            }

            _dividerAboveController = null;
            _ownsDividerAbove = false;

            if (_ownsDividerBelow)
            {
                DestroyDivider(ref _dividerBelow, ref _dividerBelowImage);
            }
            else
            {
                // Shared _dividerBelow is the next section's _dividerAbove and
                // is owned by that next toggle.
                _dividerBelow = null;
                _dividerBelowImage = null;
            }

            _ownsDividerBelow = false;
        }

        public void UpdateVisibility()
        {
            if (_dividerAbove != null && _dividerAboveController == null)
            {
                _dividerAbove.SetActive(true);
            }

            if (_dividerBelow != null)
            {
                _dividerBelow.SetActive(!_owner.Expanded);
            }
        }

        private bool EnsureOwnedDividerBelow()
        {
            if (_dividerBelow != null)
            {
                UpdateVisibility();
                return true;
            }

            if (!TryGetDividerBelowSiblingIndex(out int siblingIndex))
            {
                return false;
            }

            GameObject divider = CreateDividerObject(siblingIndex, _owner);
            SetDividerBelow(divider, ResolveDividerImage(divider), true);
            return true;
        }

        private bool TryGetDividerBelowSiblingIndex(out int siblingIndex)
        {
            siblingIndex = -1;
            RectTransform rectTransform = _owner.rectTransform;
            if (rectTransform == null || rectTransform.parent == null)
            {
                return false;
            }

            Transform parent = rectTransform.parent;
            int lastOwnedIndex = rectTransform.GetSiblingIndex();
            for (int i = 0; i < _contentMarkers.Count; i++)
            {
                PanelSectionContentMarker marker = _contentMarkers[i];
                if (marker == null || marker.Owner != _owner || marker.transform.parent != parent)
                {
                    continue;
                }

                lastOwnedIndex = Mathf.Max(lastOwnedIndex, marker.transform.GetSiblingIndex());
            }

            siblingIndex = Mathf.Min(lastOwnedIndex + 1, parent.childCount);
            return true;
        }

        private void SetDividerBelow(GameObject divider, Image image, bool ownsDivider = false)
        {
            if (_dividerBelow != null && _dividerBelow != divider)
            {
                if (_ownsDividerBelow)
                {
                    DestroyDivider(ref _dividerBelow, ref _dividerBelowImage);
                }
                else
                {
                    BasisDebug.LogWarning($"{nameof(PanelSectionToggle)} divider below was replaced.");
                }
            }

            _dividerBelow = divider;
            _dividerBelowImage = image;
            _ownsDividerBelow = ownsDivider;
            UpdateVisibility();
        }

        private PanelSectionToggle ResolvePreviousSectionToggle(int currentIndex, out bool isDirectNeighbor)
        {
            isDirectNeighbor = false;
            Transform parent = _owner.rectTransform.parent;
            bool skippedSibling = false;
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == null || sibling.TryGetComponent(out PanelSectionBreaklineMarker _))
                {
                    skippedSibling = true;
                    continue;
                }

                if (sibling.TryGetComponent(out PanelSectionToggle previousToggle))
                {
                    isDirectNeighbor = !skippedSibling;
                    return previousToggle;
                }

                if (sibling.TryGetComponent(out PanelSectionContentMarker contentMarker))
                {
                    if (contentMarker.Owner != null && contentMarker.Owner != _owner)
                    {
                        skippedSibling = true;
                        return contentMarker.Owner;
                    }

                    skippedSibling = true;
                    continue;
                }

                return null;
            }

            return null;
        }

        private void ClearSiblingReferenceTo(GameObject divider)
        {
            RectTransform rectTransform = _owner.rectTransform;
            if (divider == null || rectTransform == null || rectTransform.parent == null)
            {
                return;
            }

            Transform parent = rectTransform.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (!parent.GetChild(i).TryGetComponent(out PanelSectionToggle markerOwner))
                {
                    continue;
                }

                PanelSectionDividerManager manager = markerOwner.DividerManager;
                if (manager._dividerBelow == divider)
                {
                    manager._dividerBelow = null;
                    manager._dividerBelowImage = null;
                    manager.UpdateVisibility();
                }
            }
        }

        private GameObject CreateDividerObject(int siblingIndex, PanelSectionToggle styleSource)
        {
            GameObject dividerObject = new GameObject("Section Divider", typeof(RectTransform), typeof(LayoutElement), typeof(PanelSectionBreaklineMarker));
            dividerObject.layer = _owner.gameObject.layer;

            dividerObject.TryGetComponent(out RectTransform dividerTransform);
            dividerTransform.SetParent(_owner.rectTransform.parent, false);
            dividerTransform.SetSiblingIndex(siblingIndex);
            dividerTransform.anchorMin = new Vector2(0f, 0.5f);
            dividerTransform.anchorMax = new Vector2(1f, 0.5f);
            dividerTransform.pivot = new Vector2(0.5f, 0.5f);
            dividerTransform.sizeDelta = new Vector2(0f, DividerContainerHeight);

            dividerObject.TryGetComponent(out LayoutElement layout);
            layout.minHeight = DividerContainerHeight;
            layout.preferredHeight = DividerContainerHeight;

            GameObject lineObject = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineObject.layer = dividerObject.layer;

            lineObject.TryGetComponent(out RectTransform lineTransform);
            lineTransform.SetParent(dividerTransform, false);
            lineTransform.anchorMin = new Vector2(0f, 0.5f);
            lineTransform.anchorMax = new Vector2(1f, 0.5f);
            lineTransform.pivot = new Vector2(0.5f, 0.5f);
            lineTransform.offsetMin = new Vector2(DividerHorizontalInset, -DividerHeight * 0.5f);
            lineTransform.offsetMax = new Vector2(-DividerHorizontalInset, DividerHeight * 0.5f);

            lineObject.TryGetComponent(out Image dividerImage);
            dividerImage.raycastTarget = false;
            ApplyDividerImageStyle(dividerImage, styleSource);
            dividerImage.color = GetDividerColor();

            return dividerObject;
        }

        private static Image ResolveDividerImage(GameObject divider)
        {
            return divider != null ? divider.GetComponentInChildren<Image>(true) : null;
        }

        private void ApplyDividerImageStyle(Image targetImage, PanelSectionToggle styleSource)
        {
            if (targetImage == null)
            {
                return;
            }

            Image sourceImage =
                styleSource?.GetDividerSourceImage()
                ?? _owner.GetDividerSourceImage();

            if (sourceImage == null)
            {
                return;
            }

            targetImage.sprite = sourceImage.sprite;
            targetImage.material = sourceImage.material;
            targetImage.type = sourceImage.type;
            targetImage.fillCenter = sourceImage.fillCenter;
            targetImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
            targetImage.preserveAspect = false;
        }

        private static Color GetDividerColor()
        {
            UiStylePalette palette = UiStyleSettings.GetActivePalette();
            return palette != null ? palette.AccentColor : FallbackDividerColor;
        }

        private static void DestroyDivider(ref GameObject divider, ref Image image)
        {
            if (divider != null)
            {
                ClearEditorSelectionIfTargeting(divider);
                Object.Destroy(divider);
            }

            divider = null;
            image = null;
        }

        internal static void ClearEditorSelectionIfTargeting(GameObject root)
        {
#if UNITY_EDITOR
            if (root == null)
            {
                return;
            }

            Object[] selectedObjects = UnityEditor.Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                return;
            }

            Object[] filteredSelection = System.Array.FindAll(
                selectedObjects,
                selectedObject => !IsEditorSelectionTargeting(root, selectedObject));

            if (filteredSelection.Length != selectedObjects.Length)
            {
                UnityEditor.Selection.objects = filteredSelection;
            }
#endif
        }

#if UNITY_EDITOR
        private static bool IsEditorSelectionTargeting(GameObject root, Object selectedObject)
        {
            if (selectedObject == null)
            {
                return false;
            }

            GameObject selectedGameObject = selectedObject as GameObject;
            if (selectedObject is Component selectedComponent)
            {
                selectedGameObject = selectedComponent.gameObject;
            }

            return selectedGameObject != null
                && (selectedGameObject == root || selectedGameObject.transform.IsChildOf(root.transform));
        }
#endif
    }
}
