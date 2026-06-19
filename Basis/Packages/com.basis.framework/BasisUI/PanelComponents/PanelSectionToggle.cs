using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Basis.BasisUI.Styling;

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
        private PanelSectionToggleMarker _marker;
        private bool _ownsBreakline;
        private PanelSectionToggle _breaklineOwnerRef;
        private GameObject _originalBreaklineObject;
        private Image _originalBreaklineImage;

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
            CreateBreaklineAbove();
            UpdateArrow();
            UpdateBreakline();
        }

        public override void OnReleaseEvent()
        {
            // Tell the owner of the breakline above us to release it
            if (_breaklineOwnerRef != null)
            {
                _breaklineOwnerRef.ReleaseBreaklineForToggle(this);
                _breaklineOwnerRef = null;
            }

            if (_breaklineObject != null)
            {
                ReleaseBreakline();
            }

            // Release the original breakline that was overwritten
            if (_originalBreaklineObject != null)
            {
                UnityEngine.Object.Destroy(_originalBreaklineObject);
                _originalBreaklineObject = null;
                _originalBreaklineImage = null;
            }

            base.OnReleaseEvent();
        }

        public override void AssignBinding(BasisSettingsBinding<bool> binding)
        {
            base.AssignBinding(binding);
            ToggleComponent?.SetIsOnWithoutNotify(binding.RawValue);
            UpdateBreakline();
        }

        public override void SetValue(bool value)
        {
            base.SetValue(value);
            ToggleComponent?.SetIsOnWithoutNotify(value);
            OnExpandedChanged?.Invoke(value);
            UpdateBreakline();
        }

        public override void SetValueWithoutNotify(bool value)
        {
            base.SetValueWithoutNotify(value);
            ToggleComponent?.SetIsOnWithoutNotify(value);
            UpdateBreakline();
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
            if (!TryGetComponent(out _marker))
            {
                _marker = gameObject.AddComponent<PanelSectionToggleMarker>();
            }

            _marker.Toggle = this;
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

        /// <summary>
        /// Create a breakline above this toggle. If the previous sibling is a
        /// PanelSectionToggle, the breakline goes between them and ownership
        /// is passed to that previous toggle. Otherwise the breakline sits
        /// above the first toggle in the group and this toggle owns it.
        /// </summary>
        private void CreateBreaklineAbove()
        {
            if (rectTransform == null || rectTransform.parent == null)
            {
                return;
            }

            int currentIndex = rectTransform.GetSiblingIndex();
            Transform previousSibling = currentIndex > 0
                ? rectTransform.parent.GetChild(currentIndex - 1)
                : null;

            PanelSectionToggle owner = null;

            if (previousSibling != null && previousSibling.TryGetComponent(out PanelSectionToggle prevToggle))
            {
                owner = prevToggle;
            }

            GameObject breaklineObject = new GameObject("Section Breakline", typeof(RectTransform), typeof(LayoutElement), typeof(PanelSectionBreaklineMarker));
            breaklineObject.layer = gameObject.layer;

            RectTransform breaklineTransform = breaklineObject.GetComponent<RectTransform>();
            breaklineTransform.SetParent(rectTransform.parent, false);
            breaklineTransform.SetSiblingIndex(currentIndex); // Above current toggle
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

            Image breaklineImage = lineObject.GetComponent<Image>();
            breaklineImage.raycastTarget = false;

            // Apply style from the source toggle's Background image.
            // When owner != null, owner is the source; otherwise self is the source.
            Image sourceImage = (owner?.Background as Image) ?? Background as Image;
            ApplyBreaklineImageStyleFrom(breaklineImage, sourceImage);

            Color color = UiStyleSettings.GetActivePalette()?.LayerColor ?? Color.white;
            breaklineImage.color = color;
            // Preserve the original breakline
            _originalBreaklineObject = _breaklineObject;
            _originalBreaklineImage = _breaklineImage;
            if (owner != null)
            {
                owner.TakeBreaklineOwnership(breaklineObject, breaklineImage);
                _breaklineOwnerRef = owner;
            }
            else
            {
                _breaklineObject = breaklineObject;
                _breaklineImage = breaklineImage;
                _ownsBreakline = false;
            }
        }

        private void ApplyBreaklineImageStyleFrom(Image targetImage, Image sourceImage)
        {
            if (targetImage == null || sourceImage == null)
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

        private void UpdateBreaklineColor()
        {
            if (_breaklineImage == null)
            {
                return;
            }

            Color color = UiStyleSettings.GetActivePalette()?.AccentColor ?? Color.white;
            _breaklineImage.color = color;
        }

        private void UpdateBreakline()
        {
            if (_breaklineObject == null)
            {
                return;
            }

            if (_ownsBreakline)
            {
                // Breakline is below this toggle — apply expand check.
                _breaklineObject.SetActive(!Expanded);
            }
            else
            {
                // Breakline is above this toggle — always visible.
                _breaklineObject.SetActive(true);
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

        /// <summary>
        /// Called by a sibling when it assigns a breakline to us. We take ownership.
        /// </summary>
        internal void TakeBreaklineOwnership(GameObject breaklineObject, Image breaklineImage)
        {
            _breaklineObject = breaklineObject;
            _breaklineImage = breaklineImage;
            _ownsBreakline = true;
        }

        /// <summary>
        /// Called by a sibling when it is being destroyed. Releases the breakline
        /// that this toggle owns (the one above us, between us and the caller).
        /// </summary>
        internal void ReleaseBreaklineForToggle(PanelSectionToggle caller)
        {
            if (_ownsBreakline && _breaklineObject != null)
            {
                UnityEngine.Object.Destroy(_breaklineObject);
                _breaklineObject = null;
                _breaklineImage = null;
                _ownsBreakline = false;
            }

            // Also release the original breakline that was overwritten
            if (_originalBreaklineObject != null)
            {
                UnityEngine.Object.Destroy(_originalBreaklineObject);
                _originalBreaklineObject = null;
                _originalBreaklineImage = null;
            }

            // Clear the caller's references so it doesn't keep a Missing ref.
            caller._breaklineObject = null;
            caller._breaklineImage = null;
            caller._breaklineOwnerRef = null;
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
