using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Staged-edit tracker for one settings section that commits through an Apply button.
    ///
    /// <para>Each control is registered alongside the live value it mirrors. While any control
    /// differs from its live value the section card and its header tint toward
    /// <see cref="BasisPanelTint.Caution"/> and the header gains an "unsaved" suffix, so an admin
    /// can see which sections still need an Apply without scrolling through them. Because dirty is
    /// derived from a comparison rather than a latch, editing a field back to its original value
    /// clears the tint on its own, and so does the server echoing the value the Apply just sent.</para>
    /// </summary>
    public sealed class PanelSectionDirtyState
    {
        /// <summary>Slider/float comparisons that land within this are treated as unchanged.</summary>
        private const float FloatEpsilon = 0.0001f;

        private readonly List<Func<bool>> _comparers = new();
        private readonly List<PanelButton> _applyButtons = new();

        private BasisPanelTint.Handle _cardTint;
        private BasisPanelTint.Handle _headerTint;
        private PanelSectionToggle _sectionToggle;
        private string _cleanTitle;
        private bool _edited;

        /// <summary>True while at least one watched control differs from the live value it mirrors.</summary>
        public bool Edited => _edited;

        /// <summary>
        /// Points the tint at a section built by
        /// <see cref="PanelSectionToggleHelpers.FinalizeBoxedSectionFromIndex"/>. Capture happens
        /// here, so call this before anything else recolors the card.
        /// </summary>
        public void Attach(PanelSectionToggle sectionToggle, PanelElementDescriptor card)
        {
            _sectionToggle = sectionToggle;
            _cleanTitle = sectionToggle != null && sectionToggle.Descriptor != null
                ? sectionToggle.Descriptor.Title
                : null;

            _cardTint = BasisPanelTint.Capture(card);
            _headerTint = BasisPanelTint.Capture(sectionToggle != null ? sectionToggle.Descriptor : null);
        }

        /// <summary>
        /// Registers an Apply button so it greys out while there is nothing to save. Any number of
        /// buttons can be registered — sections carry one at the top and one at the bottom.
        /// </summary>
        public void RegisterApplyButton(PanelButton button)
        {
            if (button == null) return;
            _applyButtons.Add(button);
            ApplyState(false);
        }

        public void WatchToggle(PanelToggle toggle, Func<bool> liveValue)
        {
            if (toggle == null || liveValue == null) return;
            _comparers.Add(() => toggle.Value != liveValue());
            toggle.OnValueChanged += _ => Reevaluate();
        }

        public void WatchSlider(PanelSlider slider, Func<float> liveValue)
        {
            if (slider == null || liveValue == null) return;
            _comparers.Add(() => Mathf.Abs(slider.Value - liveValue()) > FloatEpsilon);
            slider.OnValueChanged += _ => Reevaluate();
        }

        public void WatchDropdown(PanelDropdown dropdown, Func<string> liveValue)
        {
            if (dropdown == null || liveValue == null) return;
            _comparers.Add(() => !string.Equals(dropdown.Value ?? string.Empty, liveValue() ?? string.Empty, StringComparison.Ordinal));
            dropdown.OnValueChanged += _ => Reevaluate();
        }

        /// <summary>
        /// Watches a text field. The underlying input is hooked directly rather than through
        /// <see cref="PanelDataComponent{T}.OnValueChanged"/> so the section tints as the admin
        /// types, not only once the field loses focus.
        /// </summary>
        public void WatchText(PanelTextField field, Func<string> liveValue)
        {
            if (field == null || liveValue == null) return;

            TMP_InputField input = field.GetComponentInChildren<TMP_InputField>(true);
            _comparers.Add(() =>
            {
                string current = input != null ? input.text : field.Value;
                return !string.Equals(current ?? string.Empty, liveValue() ?? string.Empty, StringComparison.Ordinal);
            });

            if (input != null) input.onValueChanged.AddListener(_ => Reevaluate());
            else field.OnValueChanged += _ => Reevaluate();
        }

        /// <summary>
        /// Watches a numeric text field against a live number, so "32" and "32.0" don't read as an
        /// edit. Unparseable text always counts as edited — the admin has typed something the Apply
        /// would have to reject or coerce, and that is worth flagging.
        /// </summary>
        public void WatchNumericText(PanelTextField field, Func<float> liveValue)
        {
            if (field == null || liveValue == null) return;

            TMP_InputField input = field.GetComponentInChildren<TMP_InputField>(true);
            _comparers.Add(() =>
            {
                string current = input != null ? input.text : field.Value;
                if (!float.TryParse(current, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    return true;
                }
                return Mathf.Abs(parsed - liveValue()) > FloatEpsilon;
            });

            if (input != null) input.onValueChanged.AddListener(_ => Reevaluate());
            else field.OnValueChanged += _ => Reevaluate();
        }

        /// <summary>
        /// Recomputes the dirty flag from the registered comparisons and repaints. Call after a
        /// user edit (the Watch helpers do this themselves) and after the server pushes new state,
        /// so an applied change stops reading as unsaved.
        /// </summary>
        public void Reevaluate()
        {
            bool edited = false;
            for (int i = 0; i < _comparers.Count; i++)
            {
                if (_comparers[i]())
                {
                    edited = true;
                    break;
                }
            }

            if (edited == _edited) return;
            _edited = edited;
            ApplyState(true);
        }

        private void ApplyState(bool animate)
        {
            if (_edited)
            {
                BasisPanelTint.Apply(_cardTint, BasisPanelTint.Caution, animate);
                BasisPanelTint.Apply(_headerTint, BasisPanelTint.Caution, animate);
            }
            else
            {
                BasisPanelTint.Clear(_cardTint, animate);
                BasisPanelTint.Clear(_headerTint, animate);
            }

            if (_sectionToggle != null && !string.IsNullOrEmpty(_cleanTitle))
            {
                _sectionToggle.SetTitle(_edited
                    ? $"{_cleanTitle} {BasisLocalization.Get("settings.admin.unsavedSuffix")}"
                    : _cleanTitle);
            }

            for (int i = 0; i < _applyButtons.Count; i++)
            {
                PanelButton button = _applyButtons[i];
                if (button == null) continue;
                button.SetInteractable(_edited, BasisLocalization.Get("settings.admin.apply.nothingToSave"));
            }
        }
    }
}
