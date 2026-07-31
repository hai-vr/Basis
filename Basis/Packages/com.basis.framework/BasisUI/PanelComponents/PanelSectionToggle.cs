using System;
using System.Collections.Generic;
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
        private PanelSectionDividerManager _dividerManager;

        private bool _created;
        private bool _compactHeightApplied;

        protected override Selectable InteractableTarget => ToggleComponent;
        internal PanelSectionDividerManager DividerManager => _dividerManager ??= new PanelSectionDividerManager(this, _contentMarkers);
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

            if (ToggleComponent == null)
            {
                BasisDebug.LogError($"{nameof(PanelSectionToggle)} requires {nameof(ToggleComponent)} to be assigned.");
            }

            if (_created)
            {
                RefreshVisualState();
                return;
            }

            _created = true;
            MarkSectionToggleRow();
            ConfigureArrowIndicator();
            ApplyCompactHeightOnce();
            DividerManager.CreateDividerAbove();
        }

        public override void OnReleaseEvent()
        {
            DividerManager.Release();

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
                BasisDebug.LogError($"{nameof(PanelSectionToggle)} requires a non-null binding.");
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
                BasisDebug.LogError($"{nameof(PanelSectionToggle)} requires a non-null binding.");
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

            if (!contentContainer.TryGetComponent(out PanelSectionContentMarker marker))
            {
                marker = contentContainer.gameObject.AddComponent<PanelSectionContentMarker>();
            }

            if (marker.Owner != null && marker.Owner != this)
            {
                marker.Owner.UnregisterContentMarker(marker);
            }

            marker.Owner = this;
            if (!_contentMarkers.Contains(marker))
            {
                _contentMarkers.Add(marker);
            }
        }

        /// <summary>
        /// Fills <paramref name="results"/> with the containers registered as this section's content.
        /// Containers destroyed since they were registered — a lazy section's box after it collapsed —
        /// are skipped, so an empty result means the section currently has nothing built under it.
        /// </summary>
        public void GetContentContainers(List<Transform> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            for (int i = 0; i < _contentMarkers.Count; i++)
            {
                PanelSectionContentMarker marker = _contentMarkers[i];
                if (marker != null)
                {
                    results.Add(marker.transform);
                }
            }
        }

        protected override void ApplyValue()
        {
            base.ApplyValue();
            UpdateArrow();
            DividerManager.UpdateVisibility();
        }

        private void RefreshVisualState()
        {
            UpdateArrow();
            DividerManager.UpdateVisibility();
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

            _generatedArrowObject.TryGetComponent(out RectTransform arrowTransform);
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

        private void UnregisterContentMarker(PanelSectionContentMarker marker)
        {
            if (marker != null)
            {
                _contentMarkers.Remove(marker);
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

        internal Image GetDividerSourceImage()
        {
            return (Background as Image) ?? (ToggleComponent?.targetGraphic as Image);
        }

        private static void DestroyGeneratedObject(ref GameObject generatedObject)
        {
            if (generatedObject != null)
            {
                PanelSectionDividerManager.ClearEditorSelectionIfTargeting(generatedObject);
                UnityEngine.Object.Destroy(generatedObject);
            }

            generatedObject = null;
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

    internal sealed class PanelSectionContentMarker : MonoBehaviour
    {
        public PanelSectionToggle Owner;
    }

    internal sealed class PanelSectionBreaklineMarker : MonoBehaviour
    {
    }
}
