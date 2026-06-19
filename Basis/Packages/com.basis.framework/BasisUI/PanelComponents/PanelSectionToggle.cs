using System;
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
        private const float BreaklineContainerHeight = 4f;
        private const float BreaklineHeight = 2f;
        private const float BreaklineHorizontalInset = 12f;

        /// <summary>Guard against scaling inappropriately large or sentinel height values.</summary>
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
        private GameObject _breaklineObject;
        private Image _breaklineImage;
        private string _title = string.Empty;

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
            MarkSectionToggleRow();
            ConfigureArrowIndicator();
            ApplyCompactHeight();
            CreateCollapsedBreakline();
            UpdateArrow();
            RefreshSiblingBreaklines();
        }

        public override void OnReleaseEvent()
        {
            ReleaseBreakline();
            base.OnReleaseEvent();
        }

        public override void AssignBinding(BasisSettingsBinding<bool> binding)
        {
            base.AssignBinding(binding);
            ToggleComponent?.SetIsOnWithoutNotify(binding.RawValue);
            RefreshSiblingBreaklines();
        }

        public override void SetValue(bool value)
        {
            base.SetValue(value);
            ToggleComponent?.SetIsOnWithoutNotify(value);
            OnExpandedChanged?.Invoke(value);
            RefreshSiblingBreaklines();
        }

        public override void SetValueWithoutNotify(bool value)
        {
            base.SetValueWithoutNotify(value);
            ToggleComponent?.SetIsOnWithoutNotify(value);
            RefreshSiblingBreaklines();
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
            UpdateBreaklineColor();
        }

        public void BindToToggle(BasisSettingsBinding<bool> binding)
        {
            if (binding == null)
            {
                SetValueWithoutNotify(false);
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

        protected override void ApplyValue()
        {
            base.ApplyValue();
            UpdateArrow();
            UpdateBreakline();
        }

        private void MarkSectionToggleRow()
        {
            PanelSectionToggleMarker marker = GetComponent<PanelSectionToggleMarker>();
            if (marker == null)
            {
                marker = gameObject.AddComponent<PanelSectionToggleMarker>();
            }

            marker.Toggle = this;
        }

        private void ConfigureArrowIndicator()
        {
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

            GameObject arrowObject = new GameObject("Section Arrow", typeof(RectTransform));
            arrowObject.layer = arrowParent.gameObject.layer;
            RectTransform arrowTransform = arrowObject.GetComponent<RectTransform>();
            arrowTransform.SetParent(arrowParent, false);
            arrowTransform.anchorMin = Vector2.zero;
            arrowTransform.anchorMax = Vector2.one;
            arrowTransform.offsetMin = Vector2.zero;
            arrowTransform.offsetMax = Vector2.zero;

            _arrowLabel = arrowObject.AddComponent<TextMeshProUGUI>();
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

        private void ApplyCompactHeight()
        {
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

        private void CreateCollapsedBreakline()
        {
            if (rectTransform == null || rectTransform.parent == null)
            {
                return;
            }

            GameObject breaklineObject = new GameObject("Section Breakline", typeof(RectTransform), typeof(LayoutElement), typeof(PanelSectionBreaklineMarker));
            breaklineObject.layer = gameObject.layer;

            RectTransform breaklineTransform = breaklineObject.GetComponent<RectTransform>();
            breaklineTransform.SetParent(rectTransform.parent, false);
            breaklineTransform.SetSiblingIndex(rectTransform.GetSiblingIndex() + 1);
            breaklineTransform.anchorMin = new Vector2(0f, 0.5f);
            breaklineTransform.anchorMax = new Vector2(1f, 0.5f);
            breaklineTransform.pivot = new Vector2(0.5f, 0.5f);
            breaklineTransform.sizeDelta = new Vector2(0f, BreaklineContainerHeight);

            LayoutElement layout = breaklineObject.GetComponent<LayoutElement>();
            layout.minHeight = BreaklineContainerHeight;
            layout.preferredHeight = BreaklineContainerHeight;

            GameObject lineObject = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineObject.layer = breaklineObject.layer;

            RectTransform lineTransform = lineObject.GetComponent<RectTransform>();
            lineTransform.SetParent(breaklineTransform, false);
            lineTransform.anchorMin = new Vector2(0f, 0.5f);
            lineTransform.anchorMax = new Vector2(1f, 0.5f);
            lineTransform.pivot = new Vector2(0.5f, 0.5f);
            lineTransform.offsetMin = new Vector2(BreaklineHorizontalInset, -BreaklineHeight * 0.5f);
            lineTransform.offsetMax = new Vector2(-BreaklineHorizontalInset, BreaklineHeight * 0.5f);

            _breaklineImage = lineObject.GetComponent<Image>();
            _breaklineImage.raycastTarget = false;
            ApplyBreaklineImageStyle();
            UpdateBreaklineColor();

            _breaklineObject = breaklineObject;
        }

        private void ApplyBreaklineImageStyle()
        {
            if (_breaklineImage == null || Background is not Image sourceImage)
            {
                return;
            }

            _breaklineImage.sprite = sourceImage.sprite;
            _breaklineImage.material = sourceImage.material;
            _breaklineImage.type = sourceImage.type;
            _breaklineImage.fillCenter = sourceImage.fillCenter;
            _breaklineImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
            _breaklineImage.preserveAspect = false;
        }

        private void UpdateBreaklineColor()
        {
            if (_breaklineImage == null)
            {
                return;
            }

            Color color = Descriptor?.TitleLabel != null ? Descriptor.TitleLabel.color : Color.white;
            color.a = Mathf.Max(color.a * 0.6f, 0.45f);
            _breaklineImage.color = color;
        }

        private void UpdateBreakline()
        {
            if (_breaklineObject != null)
            {
                _breaklineObject.SetActive(!Expanded && HasNextSectionToggle());
            }
        }

        private bool HasNextSectionToggle()
        {
            RectTransform row = rectTransform;
            if (row == null || row.parent == null)
            {
                return false;
            }

            Transform parent = row.parent;
            int startIndex = row.GetSiblingIndex() + 1;
            for (int i = startIndex; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == null)
                {
                    continue;
                }

                if (sibling.GetComponent<PanelSectionBreaklineMarker>() != null)
                {
                    continue;
                }

                if (!sibling.gameObject.activeSelf)
                {
                    continue;
                }

                return sibling.GetComponent<PanelSectionToggleMarker>() != null;
            }

            return false;
        }

        private void RefreshSiblingBreaklines()
        {
            Transform parent = rectTransform != null ? rectTransform.parent : null;
            if (parent == null)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                PanelSectionToggleMarker marker = parent.GetChild(i).GetComponent<PanelSectionToggleMarker>();
                marker?.Toggle?.UpdateBreakline();
            }
        }

        private void ReleaseBreakline()
        {
            if (_breaklineObject == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_breaklineObject);
            _breaklineObject = null;
            _breaklineImage = null;
        }

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

    internal sealed class PanelSectionBreaklineMarker : MonoBehaviour
    {
    }
}
