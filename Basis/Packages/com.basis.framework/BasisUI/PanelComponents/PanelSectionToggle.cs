using System;
using System.Collections.Generic;
using Basis.BasisUI.Styling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public sealed class PanelSectionToggle : PanelDataComponent<bool>
    {
        private const string CollapsedArrow = ">";
        private const string ExpandedArrow = "\u25bc";
        private const float CompactHeightScale = 0.8f;
        private const float DividerContainerHeight = 4f;
        private const float DividerHeight = 2f;
        private const float DividerHorizontalInset = 12f;

        // Avoid scaling inappropriately large or sentinel preferred-height values.
        private const float MaxPreferredHeightThreshold = 1000f;

        public static class Styles
        {
            public static string Default => "Packages/com.basis.sdk/Prefabs/Panel Elements/PE Section Toggle.prefab";
            public static string Entry => "Packages/com.basis.sdk/Prefabs/Panel Elements/PE Section Toggle - Entry Variant.prefab";
        }

        public Toggle ToggleComponent;
        public RectTransform ToggleVisual;

        [Header("Visual Elements")]
        public Graphic Background;

        private TextMeshProUGUI _arrowLabel;
        private GameObject _generatedArrowObject;
        private string _title = string.Empty;
        private PanelSectionToggleMarker _marker;
        private readonly List<PanelSectionContentMarker> _contentMarkers = new();

        private GameObject _dividerAbove;
        private Image _dividerAboveImage;
        private PanelSectionToggle _dividerAboveController;
        private GameObject _dividerBelow;
        private Image _dividerBelowImage;
        private bool _ownsDividerBelow;

        private bool _created;
        private bool _compactHeightApplied;

        protected override Selectable InteractableTarget => ToggleComponent;
        public bool Expanded => Value;
        public event Action<bool> OnExpandedChanged;

        public static PanelSectionToggle CreateNew(Component parent) =>
            CreateNew<PanelSectionToggle>(Styles.Default, parent);

        public static PanelSectionToggle CreateNewEntry(Component parent) =>
            CreateNew<PanelSectionToggle>(Styles.Entry, parent);

        public static PanelSectionToggle CreateNew(Component parent, string style) =>
            CreateNew<PanelSectionToggle>(style, parent);

        protected override void OnEnable()
        {
            base.OnEnable();
            if (SettingsBinding != null)
            {
                SetValueWithoutNotify(SettingsBinding.RawValue);
            }
        }

        public override void OnCreateEvent()
        {
            base.OnCreateEvent();

            if (_created)
            {
                RefreshVisualState();
                return;
            }

            _created = true;
            MarkSectionToggleRow();
            ConfigureArrowIndicator();
            ApplyCompactHeightOnce();
            CreateDividerAbove();
            RefreshVisualState();
        }

        public override void OnReleaseEvent()
        {
            ClearSiblingReferenceTo(_dividerAbove);
            DestroyDivider(ref _dividerAbove, ref _dividerAboveImage);
            _dividerAboveController = null;

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

            if (_generatedArrowObject != null)
            {
                DestroyGeneratedObject(ref _generatedArrowObject);
                _arrowLabel = null;
            }

            if (_marker != null)
            {
                _marker.Toggle = null;
                _marker = null;
            }

            ClearRegisteredContentMarkers();

            _created = false;

            base.OnReleaseEvent();
        }

        public override void AssignBinding(BasisSettingsBinding<bool> binding)
        {
            if (binding == null)
            {
                Debug.LogError($"{nameof(PanelSectionToggle)} requires a non-null binding.");
                SetExpandedWithoutNotify(false);
                return;
            }

            base.AssignBinding(binding);
            ToggleComponent?.SetIsOnWithoutNotify(binding.RawValue);
            RefreshVisualState();
        }

        public override void SetValue(bool value)
        {
            ToggleComponent?.SetIsOnWithoutNotify(value);
            base.SetValue(value);
            OnExpandedChanged?.Invoke(value);
            RefreshVisualState();
        }

        public override void SetValueWithoutNotify(bool value)
        {
            ToggleComponent?.SetIsOnWithoutNotify(value);
            base.SetValueWithoutNotify(value);
            RefreshVisualState();
        }

        public override void OnComponentUsed()
        {
            base.OnComponentUsed();
            SetValue(ToggleComponent != null && ToggleComponent.isOn);
        }

        public void SetTitle(string title)
        {
            _title = title ?? string.Empty;
            Descriptor?.SetTitle(_title);
        }

        public void BindToToggle(BasisSettingsBinding<bool> binding)
        {
            if (binding == null)
            {
                Debug.LogError($"{nameof(PanelSectionToggle)} requires a non-null binding.");
                SetExpandedWithoutNotify(false);
                return;
            }

            AssignBinding(binding);
        }

        public void SetExpanded(bool expanded)
        {
            SetValue(expanded);
        }

        public void SetExpandedWithoutNotify(bool expanded)
        {
            SetValueWithoutNotify(expanded);
        }

        public void RegisterContentContainer(Component contentContainer)
        {
            if (contentContainer == null)
            {
                return;
            }

            PanelSectionContentMarker marker = contentContainer.GetComponent<PanelSectionContentMarker>();
            if (marker == null)
            {
                marker = contentContainer.gameObject.AddComponent<PanelSectionContentMarker>();
            }

            marker.Owner = this;
            if (!_contentMarkers.Contains(marker))
            {
                _contentMarkers.Add(marker);
            }
        }

        protected override void ApplyValue()
        {
            base.ApplyValue();
            UpdateArrow();
            UpdateDividerVisibility();
        }

        private void RefreshVisualState()
        {
            UpdateArrow();
            UpdateDividerVisibility();
        }

        private void MarkSectionToggleRow()
        {
            if (!TryGetComponent(out _marker))
            {
                _marker = gameObject.AddComponent<PanelSectionToggleMarker>();
            }

            _marker.Toggle = this;
        }

        private void ConfigureArrowIndicator()
        {
            if (_arrowLabel != null)
            {
                return;
            }

            RectTransform arrowParent = ResolveArrowParent();
            if (arrowParent == null)
            {
                return;
            }

            if (ToggleVisual != null)
            {
                ToggleVisual.gameObject.SetActive(false);
            }

            if (Background != null)
            {
                Background.gameObject.SetActive(false);
            }

            _generatedArrowObject = new GameObject("Section Arrow", typeof(RectTransform));
            _generatedArrowObject.layer = arrowParent.gameObject.layer;

            RectTransform arrowTransform = _generatedArrowObject.GetComponent<RectTransform>();
            arrowTransform.SetParent(arrowParent, false);
            arrowTransform.anchorMin = Vector2.zero;
            arrowTransform.anchorMax = Vector2.one;
            arrowTransform.offsetMin = Vector2.zero;
            arrowTransform.offsetMax = Vector2.zero;

            _arrowLabel = _generatedArrowObject.AddComponent<TextMeshProUGUI>();
            _arrowLabel.raycastTarget = false;
            _arrowLabel.alignment = TextAlignmentOptions.Center;
            _arrowLabel.textWrappingMode = TextWrappingModes.NoWrap;

            TextMeshProUGUI titleLabel = Descriptor?.TitleLabel;
            if (titleLabel != null)
            {
                _arrowLabel.font = titleLabel.font;
                _arrowLabel.fontSharedMaterial = titleLabel.fontSharedMaterial;
                _arrowLabel.color = titleLabel.color;
                _arrowLabel.fontSize = titleLabel.fontSize;
                _arrowLabel.fontStyle = titleLabel.fontStyle;
            }
        }

        private void ApplyCompactHeightOnce()
        {
            if (_compactHeightApplied)
            {
                return;
            }

            _compactHeightApplied = true;

            LayoutElement[] layouts = GetComponentsInChildren<LayoutElement>(true);
            for (int i = 0; i < layouts.Length; i++)
            {
                ScaleHeight(layouts[i]);
            }

            ScaleRectHeight(rectTransform);
            ScaleRectHeight(Descriptor?.Header);
        }

        private static void ScaleHeight(LayoutElement layout)
        {
            if (layout == null)
            {
                return;
            }

            if (layout.minHeight > 0f)
            {
                layout.minHeight *= CompactHeightScale;
            }

            if (layout.preferredHeight > 0f && layout.preferredHeight < MaxPreferredHeightThreshold)
            {
                layout.preferredHeight *= CompactHeightScale;
            }
        }

        private static void ScaleRectHeight(RectTransform rectTransform)
        {
            if (rectTransform == null || rectTransform.sizeDelta.y <= 0f)
            {
                return;
            }

            Vector2 size = rectTransform.sizeDelta;
            size.y *= CompactHeightScale;
            rectTransform.sizeDelta = size;
        }

        private void CreateDividerAbove()
        {
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
                previousToggle.EnsureOwnedDividerBelow(currentIndex);
                currentIndex++;
            }

            GameObject divider = CreateDividerObject(currentIndex, previousToggle);
            Image dividerImage = ResolveDividerImage(divider);

            _dividerAbove = divider;
            _dividerAboveImage = dividerImage;
            _dividerAboveController = previousToggleIsDirectNeighbor ? previousToggle : null;

            if (previousToggle != null && previousToggleIsDirectNeighbor)
            {
                previousToggle.SetDividerBelow(divider, dividerImage);
            }

            UpdateDividerVisibility();
        }

        private GameObject CreateDividerObject(int siblingIndex, PanelSectionToggle styleSource)
        {
            GameObject dividerObject = new GameObject("Section Divider", typeof(RectTransform), typeof(LayoutElement), typeof(PanelSectionBreaklineMarker));
            dividerObject.layer = gameObject.layer;

            RectTransform dividerTransform = dividerObject.GetComponent<RectTransform>();
            dividerTransform.SetParent(rectTransform.parent, false);
            dividerTransform.SetSiblingIndex(siblingIndex);
            dividerTransform.anchorMin = new Vector2(0f, 0.5f);
            dividerTransform.anchorMax = new Vector2(1f, 0.5f);
            dividerTransform.pivot = new Vector2(0.5f, 0.5f);
            dividerTransform.sizeDelta = new Vector2(0f, DividerContainerHeight);

            LayoutElement layout = dividerObject.GetComponent<LayoutElement>();
            layout.minHeight = DividerContainerHeight;
            layout.preferredHeight = DividerContainerHeight;

            GameObject lineObject = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineObject.layer = dividerObject.layer;

            RectTransform lineTransform = lineObject.GetComponent<RectTransform>();
            lineTransform.SetParent(dividerTransform, false);
            lineTransform.anchorMin = new Vector2(0f, 0.5f);
            lineTransform.anchorMax = new Vector2(1f, 0.5f);
            lineTransform.pivot = new Vector2(0.5f, 0.5f);
            lineTransform.offsetMin = new Vector2(DividerHorizontalInset, -DividerHeight * 0.5f);
            lineTransform.offsetMax = new Vector2(-DividerHorizontalInset, DividerHeight * 0.5f);

            Image dividerImage = lineObject.GetComponent<Image>();
            dividerImage.raycastTarget = false;
            ApplyDividerImageStyle(dividerImage, styleSource);
            dividerImage.color = GetDividerColor();

            return dividerObject;
        }

        private static Image ResolveDividerImage(GameObject divider)
        {
            return divider != null ? divider.GetComponentInChildren<Image>(true) : null;
        }

        private void EnsureOwnedDividerBelow(int siblingIndex)
        {
            if (_dividerBelow != null)
            {
                UpdateDividerVisibility();
                return;
            }

            GameObject divider = CreateDividerObject(siblingIndex, this);
            SetDividerBelow(divider, ResolveDividerImage(divider), true);
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
                    Debug.LogWarning($"{nameof(PanelSectionToggle)} divider below was replaced.");
                }
            }

            _dividerBelow = divider;
            _dividerBelowImage = image;
            _ownsDividerBelow = ownsDivider;
            UpdateDividerVisibility();
        }

        private void UpdateDividerVisibility()
        {
            if (_dividerAbove != null && _dividerAboveController == null)
            {
                _dividerAbove.SetActive(true);
            }

            if (_dividerBelow != null)
            {
                _dividerBelow.SetActive(!Expanded);
            }
        }

        private PanelSectionToggle ResolvePreviousSectionToggle(int currentIndex, out bool isDirectNeighbor)
        {
            isDirectNeighbor = false;
            Transform parent = rectTransform.parent;
            bool skippedSibling = false;
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == null || sibling.GetComponent<PanelSectionBreaklineMarker>() != null)
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
                    return contentMarker.Owner;
                }

                return null;
            }

            return null;
        }

        private void ClearSiblingReferenceTo(GameObject divider)
        {
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

                if (markerOwner._dividerBelow == divider)
                {
                    markerOwner._dividerBelow = null;
                    markerOwner._dividerBelowImage = null;
                    markerOwner.UpdateDividerVisibility();
                }
            }
        }

        private void ClearRegisteredContentMarkers()
        {
            for (int i = 0; i < _contentMarkers.Count; i++)
            {
                PanelSectionContentMarker marker = _contentMarkers[i];
                if (marker != null && marker.Owner == this)
                {
                    marker.Owner = null;
                }
            }

            _contentMarkers.Clear();
        }

        private void ApplyDividerImageStyle(Image targetImage, PanelSectionToggle styleSource)
        {
            if (targetImage == null)
            {
                return;
            }

            Image sourceImage =
                styleSource?.GetDividerSourceImage()
                ?? GetDividerSourceImage();

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

        private Image GetDividerSourceImage()
        {
            return (Background as Image) ?? (ToggleComponent?.targetGraphic as Image);
        }

        private static Color GetDividerColor()
        {
            UiStylePalette palette = UiStyleSettings.GetActivePalette();
            return palette != null ? palette.AccentColor : Color.white;
        }

        private static void DestroyDivider(ref GameObject divider, ref Image image)
        {
            if (divider != null)
            {
                ClearEditorSelectionIfTargeting(divider);
                UnityEngine.Object.Destroy(divider);
            }

            divider = null;
            image = null;
        }

        private static void DestroyGeneratedObject(ref GameObject generatedObject)
        {
            if (generatedObject != null)
            {
                ClearEditorSelectionIfTargeting(generatedObject);
                UnityEngine.Object.Destroy(generatedObject);
            }

            generatedObject = null;
        }

        private static void ClearEditorSelectionIfTargeting(GameObject root)
        {
#if UNITY_EDITOR
            if (root == null)
            {
                return;
            }

            UnityEngine.Object[] selectedObjects = UnityEditor.Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                return;
            }

            UnityEngine.Object[] filteredSelection = Array.FindAll(
                selectedObjects,
                selectedObject => !IsEditorSelectionTargeting(root, selectedObject));

            if (filteredSelection.Length != selectedObjects.Length)
            {
                UnityEditor.Selection.objects = filteredSelection;
            }
#endif
        }

#if UNITY_EDITOR
        private static bool IsEditorSelectionTargeting(GameObject root, UnityEngine.Object selectedObject)
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

        private RectTransform ResolveArrowParent()
        {
            if (Background != null && Background.transform.parent is RectTransform backgroundParent)
            {
                return backgroundParent;
            }

            if (ToggleVisual != null)
            {
                Transform knobParent = ToggleVisual.parent;
                if (knobParent != null && knobParent.parent is RectTransform switchParent)
                {
                    return switchParent;
                }

                if (knobParent is RectTransform directParent)
                {
                    return directParent;
                }
            }

            return rectTransform;
        }

        private void UpdateArrow()
        {
            if (_arrowLabel != null)
            {
                _arrowLabel.SetText(Expanded ? ExpandedArrow : CollapsedArrow);
            }
        }
    }

    internal sealed class PanelSectionToggleMarker : MonoBehaviour
    {
        public PanelSectionToggle Toggle;
    }

    internal sealed class PanelSectionContentMarker : MonoBehaviour
    {
        public PanelSectionToggle Owner;
    }

    internal sealed class PanelSectionBreaklineMarker : MonoBehaviour
    {
    }
}
