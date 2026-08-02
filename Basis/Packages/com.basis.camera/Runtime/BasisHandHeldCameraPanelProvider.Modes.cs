using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// The mode picker and the section colouring it drives.
    ///
    /// <para>The panel carries sixteen sections across the six tabs behind this one, which is more
    /// than anyone can hold in their head at once. A mode narrows that down twice over: it says
    /// what the camera is for, and it paints every section with the part that section plays — set
    /// by this mode, yours to set, or doing nothing here. Nothing is disabled; a control that has
    /// been coloured "does nothing" still works, it just tells you first.</para>
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        private PanelDropdown _modeDropdown;
        private BasisCameraMode? _lastShownMode;

        /// <summary>
        /// One tintable section: the header bar and the card of rows under it. Both are tinted so a
        /// collapsed section still carries its colour — collapsed is when the colour is doing the
        /// most work, because the rows that would otherwise explain the section are not on screen.
        /// </summary>
        private sealed class SectionTintTarget
        {
            public BasisCameraPanelSection Kind;
            public Image Header;
            public Image Card;

            // The colour the palette gave each graphic, captured the first time it is tinted. The
            // applied colour is kept alongside so an outside write — the user recolouring the UI
            // palette re-runs every style component — can be spotted and the baseline retaken,
            // rather than the tint being layered onto itself until the section turns to mud.
            public Color HeaderBaseline;
            public Color CardBaseline;
            public Color HeaderApplied;
            public Color CardApplied;
            public bool HasBaseline;
        }

        private readonly List<SectionTintTarget> _sectionTints = new List<SectionTintTarget>();

        /// <summary>
        /// The Mode tab: one row, the picker. Its description is the selected mode's own, so the
        /// page says what you are in and what that means without repeating either.
        ///
        /// <para>A page of its own rather than a row in the navigation column. That column is 350
        /// wide while the labelled dropdown prefab reserves 500 for its control alone, so the row
        /// overhung the column and its own label collapsed to nothing behind the control; moving the
        /// label to its own card fixed the width but left the picker past the bottom of a column
        /// that does not scroll.</para>
        /// </summary>
        private void BuildModeTab(RectTransform parent)
        {
            _modeDropdown = PanelDropdown.CreateNewEntry(parent);
            _modeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.modePreset"));
            // A tooltip, not a line of the page. What modes are for is read once; what the current
            // one does is read every time, and only one of the two earns the space under the row.
            _modeDropdown.Descriptor.SetTooltip(BasisLocalization.Get("camera.modePreset.description"));
            // The dropdown's value is the localization key, not the translated text: two modes that
            // happened to translate to the same words would otherwise be indistinguishable to the
            // value lookup, and the selection would follow the language.
            List<string> keys = BuildModeKeys();
            _modeDropdown.AssignLocalizedEntries(keys, keys);
            _modeDropdown.OnValueChanged = _ => OnModeSelected();
        }

        private static List<string> BuildModeKeys()
        {
            List<string> keys = new List<string>();
            for (int Index = 0; Index < BasisCameraModes.Ordered.Length; Index++)
            {
                keys.Add(BasisCameraModes.Get(BasisCameraModes.Ordered[Index]).TitleKey);
            }

            return keys;
        }

        private void OnModeSelected()
        {
            if (_activeCamera == null || _modeDropdown == null) return;

            int index = _modeDropdown.Index;
            if (index < 0 || index >= BasisCameraModes.Ordered.Length) return;

            BasisCameraMode mode = BasisCameraModes.Ordered[index];

            // Custom is a state the camera arrives at, not one it can be sent to: there is nothing
            // to apply. Picking it means "leave my settings alone", so put the dropdown back to
            // whatever the camera actually is and let the tick settle the label.
            if (mode == BasisCameraMode.Custom)
            {
                RefreshModeVisuals(force: true);
                return;
            }

            _activeCamera.ApplyCameraMode(mode);

            // A preset writes values the panel is already showing, so every control it touched is
            // now stale. Re-seed from the camera rather than from the preset — the camera is what
            // clamped, rejected or rounded them.
            ApplyActiveCameraToControls();
            RefreshModeVisuals(force: true);
        }

        /// <summary>
        /// Files every built section for tinting. Called once the tabs are built, since the section
        /// and group handles are assigned as each tab is populated.
        /// </summary>
        private void RegisterSectionTints()
        {
            _sectionTints.Clear();

            AddSectionTint(BasisCameraPanelSection.Actions, _actionSection, _actionGroup);
            AddSectionTint(BasisCameraPanelSection.Lens, _lensSection, _lensGroup);
            AddSectionTint(BasisCameraPanelSection.DepthOfField, _dofSection, _dofGroup);
            AddSectionTint(BasisCameraPanelSection.Colour, _colorSection, _colorGroup);
            AddSectionTint(BasisCameraPanelSection.Effects, _effectsSection, _effectsGroup);
            AddSectionTint(BasisCameraPanelSection.Output, _outputSection, _outputGroup);
            AddSectionTint(BasisCameraPanelSection.Follow, _followSection, _followGroup);
            AddSectionTint(BasisCameraPanelSection.Cinematic, _cinematicSection, _cinematicGroup);
            AddSectionTint(BasisCameraPanelSection.Composition, _compositionSection, _compositionGroup);
            AddSectionTint(BasisCameraPanelSection.Orbit, _orbitSection, _orbitGroup);
            AddSectionTint(BasisCameraPanelSection.Noise, _noiseSection, _noiseGroup);
            AddSectionTint(BasisCameraPanelSection.Dolly, _dollySection, _dollyGroup);
            AddSectionTint(BasisCameraPanelSection.Background, _backgroundSection, _backgroundGroup);
            AddSectionTint(BasisCameraPanelSection.Layers, _layersSection, _layersGroup);
            AddSectionTint(BasisCameraPanelSection.Performance, _performanceSection, _performanceGroup);
            AddSectionTint(BasisCameraPanelSection.Gizmos, _gizmoSection, _gizmoGroup);
        }

        private void AddSectionTint(
            BasisCameraPanelSection kind,
            PanelSectionToggle section,
            PanelElementDescriptor group)
        {
            Image header = section != null && section.Descriptor != null
                ? section.Descriptor.ElementBaseImage
                : null;
            Image card = group != null ? group.ElementBaseImage : null;

            // Not every element prefab ships a background graphic, and a section can be absent
            // entirely on a platform that compiles its contents out.
            if (header == null && card == null) return;

            _sectionTints.Add(new SectionTintTarget { Kind = kind, Header = header, Card = card });
        }

        /// <summary>
        /// Repaints every section for the given mode. Safe to call every tick: each graphic is only
        /// written when its colour is not already the one this method last chose.
        /// </summary>
        private void ApplySectionTints(BasisCameraMode mode)
        {
            for (int Index = 0; Index < _sectionTints.Count; Index++)
            {
                SectionTintTarget target = _sectionTints[Index];

                // Sections are destroyed with the panel and rebuilt on the next open, so a stale
                // entry here means a rebuild raced this tick rather than anything being wrong.
                if (target.Header == null && target.Card == null) continue;

                if (!target.HasBaseline)
                {
                    if (target.Header != null) target.HeaderBaseline = target.Header.color;
                    if (target.Card != null) target.CardBaseline = target.Card.color;
                    target.HasBaseline = true;
                }

                TintGraphic(target.Header, mode, target.Kind, ref target.HeaderBaseline, ref target.HeaderApplied);
                TintGraphic(target.Card, mode, target.Kind, ref target.CardBaseline, ref target.CardApplied);
            }
        }

        private static void TintGraphic(
            Image image,
            BasisCameraMode mode,
            BasisCameraPanelSection section,
            ref Color baseline,
            ref Color applied)
        {
            if (image == null) return;

            // Anything other than what was written last time came from outside this panel — a
            // palette change is the one that happens in practice — so that colour is the new
            // baseline. Without this the tint would be blended on top of the previous tint.
            if (image.color != applied) baseline = image.color;

            Color tinted = BasisCameraModes.TintFor(mode, section, baseline);
            if (image.color != tinted) image.color = tinted;
            applied = tinted;
        }

        /// <summary>
        /// Brings the dropdown, the blurb and the section colours in line with the camera's mode.
        /// Change-gated on the mode, because the tints are a per-section write and the description
        /// is a text layout — neither is worth redoing on a tick that changed nothing.
        /// </summary>
        private void RefreshModeVisuals(bool force = false)
        {
            if (_activeCamera == null) return;

            BasisCameraMode mode = _activeCamera.CameraMode;
            if (!force && _lastShownMode == mode) return;
            _lastShownMode = mode;

            BasisCameraModeDescriptor descriptor = BasisCameraModes.Get(mode);

            if (_modeDropdown != null)
            {
                if (System.Array.IndexOf(BasisCameraModes.Ordered, mode) >= 0)
                {
                    _modeDropdown.SetValueWithoutNotify(descriptor.TitleKey);
                }

                // The row's own description, not a card under it: the control already names the
                // mode, so a second copy of the name and a second paragraph said nothing new.
                _modeDropdown.Descriptor.SetDescription(BasisLocalization.Get(descriptor.DescriptionKey));
            }

            ApplySectionTints(mode);
        }

        /// <summary>
        /// Per-tick half: re-derives the mode from the live camera so a setting changed anywhere —
        /// this panel, the prop's own HUD, or another mode's controls — moves the label to Custom,
        /// and re-asserts the tints so an outside repaint cannot leave the page half-coloured.
        /// </summary>
        private void TickModeState()
        {
            if (_activeCamera == null) return;

            if (_activeCamera.RefreshCameraMode())
            {
                RefreshModeVisuals(force: true);
                return;
            }

            ApplySectionTints(_activeCamera.CameraMode);
        }

        private void ClearModeReferences()
        {
            _modeDropdown = null;
            _lastShownMode = null;
            _sectionTints.Clear();
        }
    }
}
