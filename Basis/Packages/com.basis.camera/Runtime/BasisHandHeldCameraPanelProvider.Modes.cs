using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// The mode picker, the modes people save themselves, and the section colouring both drive.
    ///
    /// <para>The panel carries sixteen sections across the six tabs behind this one, which is more
    /// than anyone can hold in their head at once. A mode narrows that down twice over: it says
    /// what the camera is for, and it paints every section with the part that section plays — set
    /// by this mode, yours to set, or doing nothing here. Nothing is disabled; a control that has
    /// been coloured "does nothing" still works, it just tells you first.</para>
    ///
    /// <para>Four of the modes ship with the camera. The rest are made here: point the camera the
    /// way you want it, give the arrangement a name and a colour, and it joins the same dropdown
    /// and paints the same sections. The page also reads the whole settings file back as text,
    /// which is the only place in the panel where every value is visible at once — and the only
    /// way to see what a saved mode is going to give you before you pick it.</para>
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        /// <summary>
        /// Panel ticks between two checks of the saved mode and the readout.
        ///
        /// <para>Both need the whole settings file harvested off the live camera, which is the
        /// same work a save does — cheap, but not free, and not worth doing sixty or ninety times
        /// a second to notice a slider moved. Four times a second is faster than anyone can read a
        /// label change, and it is the only cost saved modes add to the tick.</para>
        /// </summary>
        private const int ModeCheckInterval = 15;

        /// <summary>Prefix that keeps a saved mode's dropdown value out of the built-in keys' namespace.</summary>
        private const string UserModePrefix = "user:";

        private PanelDropdown _modeDropdown;

        // The dropdown's rows, in its own order: the value it stores, and the saved mode that row
        // means (null on the four built-in rows). Kept beside the control rather than derived from
        // it because the value alone cannot say which half of the roster it came from.
        private readonly List<string> _modeValues = new List<string>();
        private readonly List<string> _modeUserNames = new List<string>();

        /// <summary>What the page is currently showing, so an unchanged tick repaints nothing.</summary>
        private string _lastShownKey;

        /// <summary>
        /// The mode in control, resolved once per change. Held rather than looked up because the
        /// tints are re-asserted every tick and a saved mode's descriptor is built on demand.
        /// </summary>
        private BasisCameraModeDescriptor _activeDescriptor;

        // The mode list only moves when somebody saves or removes one, so the dropdown is rebuilt
        // off the store's revision rather than on a timer. The count is watched alongside it as
        // the cheaper half of the same question — a first read of the file bumps both.
        private int _lastModeRevision = -1;
        private int _lastModeCount = -1;

        private int _modeCheckCountdown;

        // The saved-mode editor.
        private PanelSectionToggle _modeEditorSection;
        private PanelElementDescriptor _modeEditorGroup;
        private PanelTextField _modeNameField;
        private PanelImage _modeTintSwatch;
        private PanelSlider _modeTintHue;
        private PanelButton _modeSaveButton;
        private PanelButton _modeRemoveButton;
        private PanelElementDescriptor _modeStatus;

        private Color _modeTint = BasisCameraUserMode.DefaultTint;

        /// <summary>
        /// The richness the hue slider paints at. One slider is one degree of freedom, so how
        /// strong and how bright a mode's colour is has to come from somewhere else: from the mode
        /// being edited where it has one, and from the built-in family's own numbers where it does
        /// not — the four shipped tints all sit near this, so a new mode looks like it belongs
        /// beside them rather than like a different kind of thing.
        /// </summary>
        private const float DefaultTintSaturation = 0.62f;
        private const float DefaultTintValue = 0.97f;

        /// <summary>Below this a colour has no usable hue, so the slider would have nothing to move.</summary>
        private const float MinimumUsableTintSaturation = 0.05f;

        private float _modeTintSaturation = DefaultTintSaturation;
        private float _modeTintValue = DefaultTintValue;

        // The settings readout.
        private PanelSectionToggle _readoutSection;
        private PanelElementDescriptor _readoutGroup;
        private PanelElementDescriptor _readoutCard;

        /// <summary>
        /// Lines in the readout as it currently stands. The text is rewritten four times a second
        /// but its shape almost never moves — the same rows, with different numbers in them — so
        /// this is what separates "a value changed" from "the card is now a different height",
        /// which is the only one of the two that is worth reflowing the page for.
        /// </summary>
        private int _readoutLineCount = -1;

        /// <summary>
        /// This tab's own scroll content, held so a reflow can be aimed at it directly.
        ///
        /// <para>The readout is rewritten on the tick whichever tab is on screen, so it cannot use
        /// the panel's <c>ActivePageContent()</c> — from another tab that is a rect the readout is
        /// not inside, and walking out of a chain looking for it would rebuild every layout group
        /// between here and the canvas.</para>
        /// </summary>
        private RectTransform _modePageContent;

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
        /// The Mode tab: the picker, then two collapsible sections — the editor for modes of your
        /// own, and the settings readout.
        ///
        /// <para>A page of its own rather than a row in the navigation column. That column is 350
        /// wide while the labelled dropdown prefab reserves 500 for its control alone, so the row
        /// overhung the column and its own label collapsed to nothing behind the control; moving the
        /// label to its own card fixed the width but left the picker past the bottom of a column
        /// that does not scroll.</para>
        ///
        /// <para>The picker sits loose above the sections because it is what the page is for, and
        /// everything below it is about the mode it names. The two sections are collapsible like
        /// every other section in this panel, which matters most for the readout: it is fifty rows
        /// long, and being able to fold it away is the difference between a reference you open when
        /// you want it and a wall the rest of the page sits underneath.</para>
        /// </summary>
        private void BuildModeTab(RectTransform parent)
        {
            _modePageContent = parent;

            _modeDropdown = PanelDropdown.CreateNewEntry(parent);
            _modeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.modePreset"));
            RebuildModeList();
            _modeDropdown.OnValueChanged = _ => OnModeSelected();

            // Directly under the picker, and above the modes people save: the picker is what hands
            // you a body, and what is left in that body is the first thing worth knowing about the
            // choice — well before the colour a saved mode paints its sections.
            BuildBodySection(parent);

            _modeEditorSection = PanelSectionToggle.CreateNewEntry(parent);
            _modeEditorGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _modeEditorSection, parent, BasisLocalization.Get("camera.userMode.title"), false);
            BuildSavedModeEditor(_modeEditorGroup.ContentParent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(
                _modeEditorSection, _modeEditorGroup, true, OnSectionExpanded);

            _readoutSection = PanelSectionToggle.CreateNewEntry(parent);
            _readoutGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _readoutSection, parent, BasisLocalization.Get("camera.userMode.readout"), false);
            BuildSettingsReadout(_readoutGroup.ContentParent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(
                _readoutSection, _readoutGroup, true, OnSectionExpanded);

            // Another camera's panel, or another copy of this one, can add or remove a mode while
            // this page is open. The rebuild is idempotent and gated on the store's revision, so
            // the event costs nothing when it fires for a change this page already made. Removed
            // first because the store is static and this provider is a reused singleton: a panel
            // that went away without its close running would otherwise leave a second handler on
            // a static event, holding the dead page alive.
            BasisCameraUserModes.OnChanged -= OnUserModesChanged;
            BasisCameraUserModes.OnChanged += OnUserModesChanged;
        }

        /// <summary>
        /// Fills the dropdown with the four built-in modes and then everything saved, in the order
        /// they were saved.
        ///
        /// <para>Values are the built-in's localization key or the saved mode's name behind a
        /// prefix, never the text on the row: <see cref="PanelDropdown"/> resolves a selection by
        /// string-matching the entry, so two modes that read the same — a mode named "Photo", or
        /// two built-ins that translate alike — would both resolve to the first match. The prefix
        /// is what keeps a saved name from ever colliding with a key.</para>
        /// </summary>
        private bool RebuildModeList()
        {
            if (_modeDropdown == null) return false;

            // Rebuilding swaps the option list out from under an open dropdown. Unity has already
            // spawned the item toggles by then and they keep the indices they were built with, so
            // the click would land on whatever now sits at that row. The revision is deliberately
            // left alone, so the next tick after it closes picks the rebuild up again.
            if (_modeDropdown.DropdownComponent != null && _modeDropdown.DropdownComponent.IsExpanded) return false;

            _lastModeRevision = BasisCameraUserModes.Revision;
            _lastModeCount = BasisCameraUserModes.Count;

            _modeValues.Clear();
            _modeUserNames.Clear();
            List<string> labels = new List<string>();

            for (int Index = 0; Index < BasisCameraModes.Ordered.Length; Index++)
            {
                BasisCameraModeDescriptor descriptor = BasisCameraModes.Get(BasisCameraModes.Ordered[Index]);
                _modeValues.Add(descriptor.TitleKey);
                _modeUserNames.Add(null);
                labels.Add(BasisLocalization.Get(descriptor.TitleKey));
            }

            IReadOnlyList<BasisCameraUserMode> saved = BasisCameraUserModes.Modes;
            for (int Index = 0; Index < saved.Count; Index++)
            {
                // A name is shown exactly as it was typed. It is the one string on this page that
                // is not a localization key, and looking it up would translate it into someone
                // else's mode.
                _modeValues.Add(UserModePrefix + saved[Index].name);
                _modeUserNames.Add(saved[Index].name);
                labels.Add(saved[Index].name);
            }

            _modeDropdown.AssignEntries(new List<string>(_modeValues), labels);

            // The row the selection sits on has moved, so the cached key no longer describes what
            // the control is showing.
            _lastShownKey = null;
            return true;
        }

        private void OnUserModesChanged()
        {
            if (_modeDropdown == null) return;

            RebuildModeList();
            RefreshModeVisuals(force: true);
        }

        private void OnModeSelected()
        {
            if (_activeCamera == null || _modeDropdown == null) return;

            int index = _modeDropdown.Index;
            if (index < 0 || index >= _modeUserNames.Count) return;

            string userName = _modeUserNames[index];
            if (userName != null)
            {
                BasisCameraUserMode saved = BasisCameraUserModes.Find(userName);
                if (saved == null)
                {
                    // Removed since the list was built — by another panel, or by a hand edit of
                    // the file. Rebuild rather than apply nothing silently.
                    RebuildModeList();
                    RefreshModeVisuals(force: true);
                    return;
                }

                _activeCamera.ApplyUserMode(saved);
                ApplyActiveCameraToControls();
                SeedModeEditor(saved);
                RefreshModeVisuals(force: true);
                return;
            }

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

        // ---------- Modes of your own ----------

        /// <summary>
        /// Name, colour, and the two buttons, in the order you would do them: say what the mode is
        /// called, pick how it will look, then save it or take it away.
        /// </summary>
        private void BuildSavedModeEditor(RectTransform content)
        {
            // The section header carries the title, so this card is only ever the running
            // commentary — the help line to begin with, and what just happened after that.
            _modeStatus = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            ReleaseControlSlot(_modeStatus);
            _modeStatus.SetDescription(BasisLocalization.Get("camera.userMode.help"));

            _modeNameField = PanelTextField.CreateNewEntry(content);
            _modeNameField.Descriptor.SetTitle(BasisLocalization.Get("camera.userMode.name"));
            _modeNameField.Descriptor.SetTooltip(BasisLocalization.Get("camera.userMode.name.tooltip"));
            if (_modeNameField._inputField != null)
            {
                _modeNameField._inputField.characterLimit = BasisCameraUserMode.MaxNameLength;
            }

            // Typing changes which mode Remove would take, so the button has to follow the field
            // and not just the dropdown.
            _modeNameField.OnValueChanged += _ => RefreshModeButtons();

            _modeTintSwatch = PanelImage.CreateNew(content);
            _modeTintSwatch.SetSize(new Vector2(200, 30));
            _modeTintSwatch.Image.color = _modeTint;

            // One slider straight through the spectrum, the same control the UI palette pickers
            // use. A mode's colour only has to be told apart from four others at a glance, so the
            // choice worth giving is which colour — not how grey or how dark a version of it, which
            // are the two the section tint blends away anyway.
            _modeTintHue = PanelSlider.CreateNew(content);
            _modeTintHue.SetSliderSettings(new PanelSlider.SliderSettings(
                BasisLocalization.Get("camera.userMode.tint"), string.Empty, 0f, 360f, true, 0,
                ValueDisplayMode.Degrees));
            _modeTintHue.Descriptor.SetTooltip(BasisLocalization.Get("camera.userMode.tint.tooltip"));
            _modeTintHue.OnValueChanged = SetModeTintHue;
            SeedTintControls(_modeTint);

            RectTransform buttonRow = PanelElementDescriptor.BuildActionRow(content, "CameraUserModeRow");

            _modeSaveButton = PanelButton.CreateNew(buttonRow);
            _modeSaveButton.Descriptor.SetTitle(BasisLocalization.Get("camera.userMode.save"));
            _modeSaveButton.Descriptor.SetTooltip(BasisLocalization.Get("camera.userMode.save.tooltip"));
            _modeSaveButton.OnClicked += SaveUserMode;

            _modeRemoveButton = PanelButton.CreateNew(buttonRow);
            _modeRemoveButton.Descriptor.SetTitle(BasisLocalization.Get("camera.userMode.remove"));
            _modeRemoveButton.Descriptor.SetTooltip(BasisLocalization.Get("camera.userMode.remove.tooltip"));
            _modeRemoveButton.OnClicked += PromptRemoveUserMode;

            RefreshModeButtons();
        }

        /// <summary>
        /// Repaints the working colour at a new hue, keeping the strength and brightness the editor
        /// is currently carrying. Alpha is pinned opaque: the section tint blends toward this colour
        /// and takes its translucency from the palette, so an alpha here would only ever be a way to
        /// make a mode's colour do nothing.
        /// </summary>
        private void SetModeTintHue(float hue)
        {
            Color tint = Color.HSVToRGB(Mathf.Repeat(hue, 360f) / 360f, _modeTintSaturation, _modeTintValue);
            tint.a = 1f;
            _modeTint = tint;

            if (_modeTintSwatch != null) _modeTintSwatch.Image.color = tint;
        }

        /// <summary>
        /// Points the slider and the swatch at a colour without driving anything.
        ///
        /// <para>The colour's own strength and brightness are adopted alongside its hue, so a mode
        /// loaded and saved again keeps exactly the colour it had — the slider moves only the hue,
        /// and a mode too grey for a hue to mean anything is given the default richness so the
        /// control is not dead in the hand.</para>
        /// </summary>
        private void SeedTintControls(Color tint)
        {
            Color.RGBToHSV(tint, out float hue, out float saturation, out float value);

            _modeTintSaturation = saturation >= MinimumUsableTintSaturation ? saturation : DefaultTintSaturation;
            _modeTintValue = value >= MinimumUsableTintSaturation ? value : DefaultTintValue;

            _modeTint = tint;
            if (_modeTintSwatch != null) _modeTintSwatch.Image.color = tint;
            _modeTintHue?.SetValueWithoutNotify(Mathf.Round(hue * 360f));
        }

        /// <summary>Fills the editor from a saved mode, so Save on an unchanged page updates it.</summary>
        private void SeedModeEditor(BasisCameraUserMode mode)
        {
            if (mode == null) return;

            _modeNameField?.SetValueWithoutNotify(mode.name);
            SeedTintControls(mode.tint);
            RefreshModeButtons();
        }

        /// <summary>
        /// The mode the buttons act on: whatever is typed in the name field.
        ///
        /// <para>The field rather than the dropdown, because the field is what a save writes to
        /// and what a remove would take away — and because they agree whenever it matters. Picking
        /// a saved mode fills the field with its name, so the two only differ once you have typed
        /// over it, and at that point the name in front of you is the one you mean.</para>
        /// </summary>
        private string EditedModeName() => BasisCameraUserMode.SanitizeName(_modeNameField?.Value);

        private void RefreshModeButtons()
        {
            string name = EditedModeName();

            if (_modeSaveButton?.ButtonComponent != null)
            {
                _modeSaveButton.ButtonComponent.interactable = name != null && _activeCamera != null;
            }

            if (_modeRemoveButton?.ButtonComponent != null)
            {
                // Only a mode that exists can be removed. Leaving the button live for a name that
                // is not one would make "nothing happened" the answer to a destructive click.
                _modeRemoveButton.ButtonComponent.interactable = name != null && BasisCameraUserModes.Exists(name);
            }
        }

        private void SaveUserMode()
        {
            if (_activeCamera == null) return;

            string name = EditedModeName();
            if (name == null)
            {
                ShowModeMessage("camera.userMode.error.empty");
                return;
            }

            BasisCameraUserMode mode = _activeCamera.CaptureUserMode(name, _modeTint);
            if (!BasisCameraUserModes.Store(mode, out string error))
            {
                ShowModeMessage(error);
                return;
            }

            // Adopted rather than applied: these values came off this camera a line ago, so
            // applying them would rebuild the render target to arrive back where it already is.
            _activeCamera.AdoptUserMode(mode);

            RebuildModeList();
            SeedModeEditor(mode);
            RefreshModeVisuals(force: true);
            ShowModeMessage("camera.userMode.saved");
        }

        private void PromptRemoveUserMode()
        {
            string name = EditedModeName();
            if (name == null || !BasisCameraUserModes.Exists(name)) return;

            // Hold the camera across the dialogue: closing the panel clears _activeCamera.
            BasisHandHeldCamera camera = _activeCamera;

            BasisMainMenu.Instance.OpenDialogue(
                BasisLocalization.Get("camera.userMode.remove"),
                BasisLocalization.Get("camera.userMode.remove.confirm", name),
                BasisLocalization.Get("camera.userMode.remove"),
                BasisLocalization.Get("ui.cancel"),
                confirmed =>
                {
                    if (!confirmed) return;
                    if (!BasisCameraUserModes.Remove(name)) return;

                    // The camera keeps its settings — removing a mode is forgetting the name, not
                    // undoing the look — but the name has to go, or the panel would keep claiming
                    // a mode that no longer exists.
                    if (camera != null && BasisCameraUserMode.NamesMatch(camera.UserModeName, name))
                    {
                        camera.RefreshUserMode(camera.HandHeld.CaptureSettings());
                    }

                    RebuildModeList();
                    RefreshModeVisuals(force: true);
                    ShowModeMessage("camera.userMode.removed");
                });
        }

        /// <summary>
        /// Says what just happened, on the card above the controls. A line on the page rather than
        /// a toast: the two things it reports — saved, and why nothing was saved — are both about
        /// the controls it sits with.
        ///
        /// <para>The message replaces the help line rather than joining it, so the card's height
        /// moves and the rows below have to be reflowed.</para>
        /// </summary>
        private void ShowModeMessage(string key)
        {
            if (_modeStatus == null) return;

            _modeStatus.SetDescription(BasisLocalization.Get(
                string.IsNullOrEmpty(key) ? "camera.userMode.help" : key));
            RebuildModeLayout(_modeEditorGroup);
        }

        /// <summary>
        /// Reflows one of this tab's sections after its content changed height.
        ///
        /// <para>uGUI layout groups do not follow a child that grew, and the group's own root is
        /// measured by its parent before its content has resized — so the rebuild has to run
        /// outward from the rows that actually changed, innermost first, stopping at this page.
        /// </para>
        /// </summary>
        private void RebuildModeLayout(PanelElementDescriptor group)
        {
            if (group == null || _modePageContent == null) return;

            PanelElementDescriptor.RebuildLayoutChain(group.ContentParent, _modePageContent);
        }

        // ---------- The settings, as text ----------

        private void BuildSettingsReadout(RectTransform content)
        {
            _readoutCard = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);

            // The section header already names it, and the card has fifty rows to fit — so it
            // gives back both reservations it would otherwise hold beside a title it does not
            // have, and the text gets the width of the page instead of what is left of it.
            _readoutCard.SetTitle(string.Empty);
            if (_readoutCard.IconBackground != null) _readoutCard.IconBackground.SetActive(false);
            ReleaseControlSlot(_readoutCard);

            // Every character of this is a number or a label the panel wrote itself, and it is
            // rewritten four times a second — so TMP is told not to scan any of it for tags.
            _readoutCard.DisableRichText();
        }

        /// <summary>
        /// Writes the harvested file out into the readout. <see cref="PanelElementDescriptor"/>
        /// drops a description identical to the one already showing, so a camera nobody is
        /// touching costs one string compare rather than a text layout.
        /// </summary>
        private void RefreshReadout(CameraSettings live)
        {
            if (_readoutCard == null || _activeCamera == null || live == null) return;

            string text = BasisCameraSettingsReadout.Build(
                live,
                (int)_activeCamera.PinSpace,
                _activeCamera.MetaData);

            _readoutCard.SetDescription(text);

            // A changed number leaves the card exactly as tall as it was, and reflowing the page
            // four times a second for that would be most of what this tab costs. Only a change in
            // how many rows there are can move the height, and that is what is watched.
            int lines = CountLines(text);
            if (lines == _readoutLineCount) return;

            _readoutLineCount = lines;
            RebuildModeLayout(_readoutGroup);
        }

        private static int CountLines(string text)
        {
            int lines = 1;
            for (int Index = 0; Index < text.Length; Index++)
            {
                if (text[Index] == '\n') lines++;
            }

            return lines;
        }

        // ---------- Section colouring ----------

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
            AddSectionTint(BasisCameraPanelSection.Anchor, _anchorSection, _anchorGroup);
            AddSectionTint(BasisCameraPanelSection.Subject, _followSection, _followGroup);
            AddSectionTint(BasisCameraPanelSection.PositionModifier, _positionSection, _positionGroup);
            AddSectionTint(BasisCameraPanelSection.RotationModifier, _rotationSection, _rotationGroup);
            AddSectionTint(BasisCameraPanelSection.ModifierEffects, _modifierEffectsSection, _modifierEffectsGroup);
            // No header of its own to tint — the track block is a plain card inside the position slot.
            AddSectionTint(BasisCameraPanelSection.Dolly, null, _dollyGroup);
            AddSectionTint(BasisCameraPanelSection.Background, _backgroundSection, _backgroundGroup);
            AddSectionTint(BasisCameraPanelSection.Layers, _layersSection, _layersGroup);
            AddSectionTint(BasisCameraPanelSection.Performance, _performanceSection, _performanceGroup);
            AddSectionTint(BasisCameraPanelSection.Gizmos, _gizmoSection, _gizmoGroup);
            AddSectionTint(BasisCameraPanelSection.PhotoMetadata, _photoMetadataSection, _photoMetadataGroup);
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
        private void ApplySectionTints(BasisCameraModeDescriptor descriptor)
        {
            if (descriptor == null) return;

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

                TintGraphic(target.Header, descriptor, target.Kind, ref target.HeaderBaseline, ref target.HeaderApplied);
                TintGraphic(target.Card, descriptor, target.Kind, ref target.CardBaseline, ref target.CardApplied);
            }
        }

        private static void TintGraphic(
            Image image,
            BasisCameraModeDescriptor descriptor,
            BasisCameraPanelSection section,
            ref Color baseline,
            ref Color applied)
        {
            if (image == null) return;

            // Anything other than what was written last time came from outside this panel — a
            // palette change is the one that happens in practice — so that colour is the new
            // baseline. Without this the tint would be blended on top of the previous tint.
            if (image.color != applied) baseline = image.color;

            Color tinted = BasisCameraModes.TintFor(descriptor, section, baseline);
            if (image.color != tinted) image.color = tinted;
            applied = tinted;
        }

        /// <summary>
        /// The mode in control. A saved mode wins over the built-in label underneath it: the
        /// built-in is still derived, because a saved mode whose values happen to be Photo's is
        /// genuinely Photo — but the name someone chose is the one they will recognise.
        /// </summary>
        private BasisCameraModeDescriptor ResolveActiveDescriptor()
        {
            if (_activeCamera == null) return BasisCameraModes.Get(BasisCameraMode.Custom);

            string name = _activeCamera.UserModeName;
            if (!string.IsNullOrEmpty(name))
            {
                BasisCameraUserMode saved = BasisCameraUserModes.Find(name);
                if (saved != null) return BasisCameraModes.DescribeUserMode(saved);
            }

            return BasisCameraModes.Get(_activeCamera.CameraMode);
        }

        /// <summary>
        /// Brings the dropdown, the blurb and the section colours in line with the camera's mode.
        /// Change-gated on which mode is showing, because the tints are a per-section write and the
        /// description is a text layout — neither is worth redoing on a tick that changed nothing.
        /// </summary>
        private void RefreshModeVisuals(bool force = false)
        {
            if (_activeCamera == null) return;

            BasisCameraModeDescriptor descriptor = ResolveActiveDescriptor();
            string key = descriptor.IsUserMode
                ? UserModePrefix + descriptor.LiteralTitle
                : descriptor.TitleKey;

            if (!force && _lastShownKey == key) return;
            _activeDescriptor = descriptor;

            // Arriving in a saved mode fills the editor with it, so Save reads as "update the mode
            // I am in" rather than "make a second one". Only on a change of mode, so it cannot
            // overwrite a name being typed.
            if (descriptor.IsUserMode)
            {
                SeedModeEditor(BasisCameraUserModes.Find(descriptor.LiteralTitle));
            }

            // Not while it is open: moving the selection out from under an expanded dropdown
            // scrolls it and highlights a different row mid-choice.
            bool expanded = _modeDropdown?.DropdownComponent != null &&
                            _modeDropdown.DropdownComponent.IsExpanded;

            if (_modeDropdown != null)
            {
                if (!expanded && _modeValues.Contains(key))
                {
                    _modeDropdown.SetValueWithoutNotify(key);
                }

                // A tooltip, not a line of the page: the control already names the mode, so the
                // paragraph about it is worth a hover but not the height under every row.
                _modeDropdown.Descriptor.SetTooltip(BasisLocalization.Get(descriptor.DescriptionKey));
            }

            RefreshModeButtons();
            ApplySectionTints(descriptor);

            // Only recorded once the control actually shows it. Caching a key the dropdown was too
            // busy to take would leave the picker naming the mode before last, permanently.
            _lastShownKey = expanded ? null : key;
        }

        /// <summary>
        /// Per-tick half: re-derives the mode from the live camera so a setting changed anywhere —
        /// this panel, the prop's own HUD, or another mode's controls — moves the label to Custom,
        /// and re-asserts the tints so an outside repaint cannot leave the page half-coloured.
        /// </summary>
        private void TickModeState()
        {
            if (_activeCamera == null) return;

            bool changed = _activeCamera.RefreshCameraMode();

            // Somebody else's panel, or a hand edit of the file, can move the list under us. Only
            // a rebuild that actually landed counts as a change — one refused because the dropdown
            // is open leaves the revision alone and will be retried, and forcing a repaint every
            // tick until then would be writing to the control the user is reading.
            if ((BasisCameraUserModes.Revision != _lastModeRevision ||
                 BasisCameraUserModes.Count != _lastModeCount) &&
                RebuildModeList())
            {
                changed = true;
            }

            if (--_modeCheckCountdown <= 0)
            {
                _modeCheckCountdown = ModeCheckInterval;
                changed |= RefreshHarvestedState();

                // On the same beat as the readout: a wind-on is over a second long and a flash
                // recycle is six, so four looks a second is faster than either can be missed.
                RefreshBodyControls();

                // The field only reports a committed edit, so without this the Save button would
                // stay greyed out under a name that has been typed but not yet tabbed away from.
                RefreshModeButtons();
            }

            if (changed)
            {
                RefreshModeVisuals(force: true);
                return;
            }

            ApplySectionTints(_activeDescriptor);
        }

        /// <summary>
        /// One harvest, read twice: the settings readout shows it, and the saved mode is checked
        /// against it. Harvesting settles the saved mode's name as part of its own work — the same
        /// way it settles the built-in label — so the change is spotted by watching the name across
        /// the call rather than by asking a second time.
        /// </summary>
        private bool RefreshHarvestedState()
        {
            if (_activeCamera.HandHeld == null) return false;

            string before = _activeCamera.UserModeName;
            CameraSettings live = _activeCamera.HandHeld.CaptureSettings();
            RefreshReadout(live);

            return !string.Equals(before, _activeCamera.UserModeName);
        }

        private void ClearModeReferences()
        {
            BasisCameraUserModes.OnChanged -= OnUserModesChanged;

            ClearBodyReferences();

            _modeDropdown = null;
            _lastShownKey = null;
            _activeDescriptor = null;
            _modeValues.Clear();
            _modeUserNames.Clear();
            _sectionTints.Clear();

            // Forces a rebuild on the next open. The dropdown is a fresh object by then, so a
            // revision that still matched would leave it with no rows at all.
            _lastModeRevision = -1;
            _lastModeCount = -1;
            _modeCheckCountdown = 0;

            _modeEditorSection = null;
            _modeEditorGroup = null;
            _modeNameField = null;
            _modeTintSwatch = null;
            _modeTintHue = null;
            _modeSaveButton = null;
            _modeRemoveButton = null;
            _modeStatus = null;

            _readoutSection = null;
            _readoutGroup = null;
            _readoutCard = null;
            _modePageContent = null;

            // The card is rebuilt empty on the next open, so a remembered count would skip the one
            // write that actually changes its height — going from nothing to fifty rows.
            _readoutLineCount = -1;
        }
    }
}
