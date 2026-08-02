using System.Collections.Generic;
using Basis.Cinematics;
using UnityEngine;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// Panel surface for the cinematic rig: the shot list and its body/aim/composition settings,
    /// the placed dolly queue, and the capture background.
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        private static readonly string[] BodyModeLabels = { "Follow Offset", "Framing", "Orbit", "Dolly Track", "Locked Off" };
        private static readonly string[] AimModeLabels = { "Composition", "Hard Look At", "Manual", "Match Subject", "Free" };
        private static readonly string[] BindingModeLabels = { "Turns With Subject", "World Axes", "Keep Current Side" };
        private static readonly string[] BlendStyleLabels = { "Cut", "Linear", "Ease In Out", "Ease In", "Ease Out", "Hard In", "Hard Out" };
        private static readonly string[] NoiseProfileLabels = { "Off", "Handheld", "Documentary", "Drone", "Shaky", "Custom" };
        private static readonly string[] BackgroundModeLabels = { "World", "Green Screen", "Blue Screen", "Black", "White", "Magenta", "Custom" };

        private PanelSectionToggle _cinematicSection;
        private PanelElementDescriptor _cinematicGroup;
        private PanelToggle _cinematicToggle;
        private PanelDropdown _shotDropdown;
        private PanelSlider _shotOrderSlider;
        private PanelDropdown _bodyModeDropdown;
        private PanelDropdown _aimModeDropdown;
        private PanelDropdown _bindingModeDropdown;
        private PanelSlider _shotPrioritySlider;
        private PanelSlider _shotOffsetXSlider;
        private PanelSlider _shotOffsetYSlider;
        private PanelSlider _shotOffsetZSlider;
        private PanelSlider _shotDampXSlider;
        private PanelSlider _shotDampYSlider;
        private PanelSlider _shotDampZSlider;
        private PanelSlider _shotRotDampSlider;
        private PanelDropdown _blendStyleDropdown;
        private PanelSlider _blendTimeSlider;
        private readonly List<int> _shotIds = new List<int>();
        private int _lastShotRosterHash = -1;
        private bool? _lastCinematic;

        private PanelSectionToggle _compositionSection;
        private PanelElementDescriptor _compositionGroup;
        private PanelSlider _screenXSlider;
        private PanelSlider _screenYSlider;
        private PanelSlider _deadZoneWidthSlider;
        private PanelSlider _deadZoneHeightSlider;
        private PanelSlider _softZoneWidthSlider;
        private PanelSlider _softZoneHeightSlider;
        private PanelSlider _composerDampHSlider;
        private PanelSlider _composerDampVSlider;
        private PanelSlider _lookAheadSlider;
        private PanelSlider _framingSizeSlider;
        private PanelToggle _framingZoomToggle;
        private PanelToggle _occlusionToggle;
        private PanelToggle _targetGroupToggle;
        private PanelToggle _guidesToggle;
        private bool _showGuides = true;

        private PanelSectionToggle _orbitSection;
        private PanelElementDescriptor _orbitGroup;
        private PanelSlider _orbitHeadingSlider;
        private PanelSlider _orbitVerticalSlider;
        private PanelSlider _orbitTopHeightSlider;
        private PanelSlider _orbitTopRadiusSlider;
        private PanelSlider _orbitMidHeightSlider;
        private PanelSlider _orbitMidRadiusSlider;
        private PanelSlider _orbitBottomHeightSlider;
        private PanelSlider _orbitBottomRadiusSlider;
        private PanelToggle _orbitFollowHeadingToggle;

        private PanelSectionToggle _noiseSection;
        private PanelElementDescriptor _noiseGroup;
        private PanelDropdown _noiseProfileDropdown;
        private PanelSlider _noiseAmplitudeSlider;
        private PanelSlider _noiseFrequencySlider;

        private PanelSectionToggle _dollySection;
        private PanelElementDescriptor _dollyGroup;
        private PanelDropdown _waypointDropdown;
        private PanelSlider _waypointOrderSlider;
        private PanelToggle _dollyLoopToggle;
        private PanelToggle _dollyVisibleToggle;
        private PanelToggle _dollyGridSnapToggle;
        private PanelSlider _dollyGridSizeSlider;
        private PanelToggle _dollyAutoTrackToggle;
        private PanelSlider _dollyPositionSlider;
        private PanelSlider _dollySpeedSlider;
        private PanelElementDescriptor _dollyEmptyState;
        private readonly List<string> _waypointKeys = new List<string>();
        private int _selectedWaypointIndex;
        private int _lastWaypointCount = -1;

        private PanelSectionToggle _backgroundSection;
        private PanelElementDescriptor _backgroundGroup;
        private PanelDropdown _backgroundModeDropdown;
        private PanelToggle _backgroundKeepWorldToggle;
        private PanelSlider _backgroundRedSlider;
        private PanelSlider _backgroundGreenSlider;
        private PanelSlider _backgroundBlueSlider;

        /// <summary>The shot the panel edits: whichever one the director has live.</summary>
        private BasisCameraShot EditedShot => _activeCamera?.GetLiveShot();

        private void BuildCinematicSections(RectTransform parent)
        {
            BuildCinematicGroup(parent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_cinematicSection, _cinematicGroup, true, OnSectionExpanded);

            BuildCompositionGroup(parent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_compositionSection, _compositionGroup, false, OnSectionExpanded);

            BuildOrbitGroup(parent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_orbitSection, _orbitGroup, false, OnSectionExpanded);

            BuildNoiseGroup(parent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_noiseSection, _noiseGroup, false, OnSectionExpanded);

            BuildDollyGroup(parent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_dollySection, _dollyGroup, false, OnSectionExpanded);
        }

        private void BuildCinematicGroup(RectTransform parent)
        {
            _cinematicSection = PanelSectionToggle.CreateNewEntry(parent);
            _cinematicGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _cinematicSection, parent, BasisLocalization.Get("camera.cinematic"), false);
            RectTransform content = _cinematicGroup.ContentParent;

            _cinematicToggle = PanelToggle.CreateNewEntry(content);
            _cinematicToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.cinematicEnabled"));
            _cinematicToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.cinematicEnabled.description"));
            _cinematicToggle.OnValueChanged = v => _activeCamera?.SetCinematicEnabled(v);

            _shotDropdown = PanelDropdown.CreateNewEntry(content);
            _shotDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.shot"));
            _shotDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.shot.description"));
            _shotDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _shotDropdown == null) return;
                int index = _shotDropdown.Index;
                if (index >= 0 && index < _shotIds.Count)
                {
                    _activeCamera.SelectShot(_shotIds[index]);
                    SeedShotControls();
                }
            };

            RectTransform shotRow = PanelElementDescriptor.BuildActionRow(content, "CameraShotRow");

            PanelButton addShot = PanelButton.CreateNew(shotRow);
            addShot.Descriptor.SetTitle(BasisLocalization.Get("camera.addShot"));
            addShot.OnClicked += () =>
            {
                if (_activeCamera == null) return;
                BasisCameraShot shot = _activeCamera.AddShot();
                _activeCamera.SelectShot(shot.id);
                _lastShotRosterHash = -1;
                RefreshShotList();
            };

            PanelButton duplicateShot = PanelButton.CreateNew(shotRow);
            duplicateShot.Descriptor.SetTitle(BasisLocalization.Get("camera.duplicateShot"));
            duplicateShot.OnClicked += () =>
            {
                BasisCameraShot live = EditedShot;
                if (_activeCamera == null || live == null) return;
                BasisCameraShot copy = _activeCamera.DuplicateShot(live.id);
                if (copy != null) _activeCamera.SelectShot(copy.id);
                _lastShotRosterHash = -1;
                RefreshShotList();
            };

            PanelButton removeShot = PanelButton.CreateNew(shotRow);
            removeShot.Descriptor.SetTitle(BasisLocalization.Get("camera.deleteShot"));
            removeShot.OnClicked += () =>
            {
                BasisCameraShot live = EditedShot;
                if (_activeCamera == null || live == null) return;
                _activeCamera.RemoveShot(live.id);
                _activeCamera.SelectShot(-1);
                _lastShotRosterHash = -1;
                RefreshShotList();
            };

            _shotOrderSlider = PanelSlider.CreateNew(content);
            _shotOrderSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.shotOrder"), 1f, 12f, true, 0, ValueDisplayMode.Raw));
            _shotOrderSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.shotOrder.description"));
            _shotOrderSlider.OnValueChanged = v =>
            {
                BasisCameraShot live = EditedShot;
                if (_activeCamera == null || live == null) return;
                if (_activeCamera.MoveShot(live.id, Mathf.RoundToInt(v) - 1))
                {
                    _lastShotRosterHash = -1;
                    RefreshShotList();
                }
            };

            _shotPrioritySlider = PanelSlider.CreateNew(content);
            _shotPrioritySlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.shotPriority"), 0f, 20f, true, 0, ValueDisplayMode.Raw));
            _shotPrioritySlider.Descriptor.SetDescription(BasisLocalization.Get("camera.shotPriority.description"));
            _shotPrioritySlider.OnValueChanged = v => EditShot(shot => shot.priority = Mathf.RoundToInt(v));

            _bodyModeDropdown = PanelDropdown.CreateNewEntry(content);
            _bodyModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.bodyMode"));
            _bodyModeDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.bodyMode.description"));
            _bodyModeDropdown.AssignEntries(new List<string>(BodyModeLabels));
            _bodyModeDropdown.OnValueChanged = _ =>
            {
                int index = _bodyModeDropdown != null ? _bodyModeDropdown.Index : -1;
                if (index < 0) return;
                EditShot(shot => shot.bodyMode = (BasisCameraBodyMode)index);
                RefreshCinematicVisibility();
            };

            _aimModeDropdown = PanelDropdown.CreateNewEntry(content);
            _aimModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.aimMode"));
            _aimModeDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.aimMode.description"));
            _aimModeDropdown.AssignEntries(new List<string>(AimModeLabels));
            _aimModeDropdown.OnValueChanged = _ =>
            {
                int index = _aimModeDropdown != null ? _aimModeDropdown.Index : -1;
                if (index < 0) return;
                EditShot(shot => shot.aimMode = (BasisCameraAimMode)index);
                RefreshCinematicVisibility();
            };

            _bindingModeDropdown = PanelDropdown.CreateNewEntry(content);
            _bindingModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.bindingMode"));
            _bindingModeDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.bindingMode.description"));
            _bindingModeDropdown.AssignEntries(new List<string>(BindingModeLabels));
            _bindingModeDropdown.OnValueChanged = _ =>
            {
                int index = _bindingModeDropdown != null ? _bindingModeDropdown.Index : -1;
                if (index >= 0) EditShot(shot => shot.bindingMode = (BasisCameraBindingMode)index);
            };

            _shotOffsetXSlider = PanelSlider.CreateNew(content);
            _shotOffsetXSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.shotOffsetX"), -5f, 5f, false, 2, ValueDisplayMode.Meters));
            _shotOffsetXSlider.OnValueChanged = v => EditShot(shot => shot.positionOffset.x = v);

            _shotOffsetYSlider = PanelSlider.CreateNew(content);
            _shotOffsetYSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.shotOffsetY"), -3f, 3f, false, 2, ValueDisplayMode.Meters));
            _shotOffsetYSlider.OnValueChanged = v => EditShot(shot => shot.positionOffset.y = v);

            _shotOffsetZSlider = PanelSlider.CreateNew(content);
            _shotOffsetZSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.shotOffsetZ"), -8f, 8f, false, 2, ValueDisplayMode.Meters));
            _shotOffsetZSlider.OnValueChanged = v => EditShot(shot => shot.positionOffset.z = v);

            _shotDampXSlider = PanelSlider.CreateNew(content);
            _shotDampXSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dampSideways"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _shotDampXSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.damp.description"));
            _shotDampXSlider.OnValueChanged = v => EditShot(shot => shot.positionDamping.x = v);

            _shotDampYSlider = PanelSlider.CreateNew(content);
            _shotDampYSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dampVertical"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _shotDampYSlider.OnValueChanged = v => EditShot(shot => shot.positionDamping.y = v);

            _shotDampZSlider = PanelSlider.CreateNew(content);
            _shotDampZSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dampApproach"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _shotDampZSlider.OnValueChanged = v => EditShot(shot => shot.positionDamping.z = v);

            _shotRotDampSlider = PanelSlider.CreateNew(content);
            _shotRotDampSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dampRotation"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _shotRotDampSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.dampRotation.description"));
            _shotRotDampSlider.OnValueChanged = v => EditShot(shot => shot.rotationDamping = new Vector3(v, v, v * 2f));

            _blendStyleDropdown = PanelDropdown.CreateNewEntry(content);
            _blendStyleDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.blendStyle"));
            _blendStyleDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.blendStyle.description"));
            _blendStyleDropdown.AssignEntries(new List<string>(BlendStyleLabels));
            _blendStyleDropdown.OnValueChanged = _ =>
            {
                int index = _blendStyleDropdown != null ? _blendStyleDropdown.Index : -1;
                if (index >= 0) EditShot(shot => shot.blendStyle = (BasisCameraBlendStyle)index);
            };

            _blendTimeSlider = PanelSlider.CreateNew(content);
            _blendTimeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.blendTime"), 0f, 8f, false, 2, ValueDisplayMode.Raw));
            _blendTimeSlider.OnValueChanged = v => EditShot(shot => shot.blendTime = v);
        }

        private void BuildCompositionGroup(RectTransform parent)
        {
            _compositionSection = PanelSectionToggle.CreateNewEntry(parent);
            _compositionGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _compositionSection, parent, BasisLocalization.Get("camera.composition"), false);
            RectTransform content = _compositionGroup.ContentParent;

            _guidesToggle = PanelToggle.CreateNewEntry(content);
            _guidesToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.showGuides"));
            _guidesToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.showGuides.description"));
            _guidesToggle.SetValueWithoutNotify(_showGuides);
            _guidesToggle.OnValueChanged = v =>
            {
                _showGuides = v;
                RefreshCompositionGuides();
            };

            _screenXSlider = PanelSlider.CreateNew(content);
            _screenXSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.screenX"), 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _screenXSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.screenX.description"));
            _screenXSlider.OnValueChanged = v => EditShot(shot => shot.composer.screenX = v);

            _screenYSlider = PanelSlider.CreateNew(content);
            _screenYSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.screenY"), 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _screenYSlider.OnValueChanged = v => EditShot(shot => shot.composer.screenY = v);

            _deadZoneWidthSlider = PanelSlider.CreateNew(content);
            _deadZoneWidthSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.deadZoneWidth"), 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _deadZoneWidthSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.deadZone.description"));
            _deadZoneWidthSlider.OnValueChanged = v => EditShot(shot => shot.composer.deadZoneWidth = v);

            _deadZoneHeightSlider = PanelSlider.CreateNew(content);
            _deadZoneHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.deadZoneHeight"), 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _deadZoneHeightSlider.OnValueChanged = v => EditShot(shot => shot.composer.deadZoneHeight = v);

            _softZoneWidthSlider = PanelSlider.CreateNew(content);
            _softZoneWidthSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.softZoneWidth"), 0f, 2f, false, 2, ValueDisplayMode.Raw));
            _softZoneWidthSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.softZone.description"));
            _softZoneWidthSlider.OnValueChanged = v => EditShot(shot => shot.composer.softZoneWidth = v);

            _softZoneHeightSlider = PanelSlider.CreateNew(content);
            _softZoneHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.softZoneHeight"), 0f, 2f, false, 2, ValueDisplayMode.Raw));
            _softZoneHeightSlider.OnValueChanged = v => EditShot(shot => shot.composer.softZoneHeight = v);

            _composerDampHSlider = PanelSlider.CreateNew(content);
            _composerDampHSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.composerDampH"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _composerDampHSlider.OnValueChanged = v => EditShot(shot => shot.composer.horizontalDamping = v);

            _composerDampVSlider = PanelSlider.CreateNew(content);
            _composerDampVSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.composerDampV"), 0f, 4f, false, 2, ValueDisplayMode.Raw));
            _composerDampVSlider.OnValueChanged = v => EditShot(shot => shot.composer.verticalDamping = v);

            _lookAheadSlider = PanelSlider.CreateNew(content);
            _lookAheadSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.lookAhead"), 0f, 1.5f, false, 2, ValueDisplayMode.Raw));
            _lookAheadSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.lookAhead.description"));
            _lookAheadSlider.OnValueChanged = v => EditShot(shot => shot.lookAheadTime = v);

            _framingSizeSlider = PanelSlider.CreateNew(content);
            _framingSizeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.subjectSize"), 0.05f, 1f, false, 2, ValueDisplayMode.Raw));
            _framingSizeSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.subjectSize.description"));
            _framingSizeSlider.OnValueChanged = v => EditShot(shot => shot.framingScreenFraction = v);

            _framingZoomToggle = PanelToggle.CreateNewEntry(content);
            _framingZoomToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.framingZoom"));
            _framingZoomToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.framingZoom.description"));
            _framingZoomToggle.OnValueChanged = v => EditShot(shot => shot.framingUsesZoom = v);

            _occlusionToggle = PanelToggle.CreateNewEntry(content);
            _occlusionToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.avoidOcclusion"));
            _occlusionToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.avoidOcclusion.description"));
            _occlusionToggle.OnValueChanged = v => EditShot(shot => shot.avoidOcclusion = v);

            _targetGroupToggle = PanelToggle.CreateNewEntry(content);
            _targetGroupToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.targetGroup"));
            _targetGroupToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.targetGroup.description"));
            _targetGroupToggle.OnValueChanged = v =>
            {
                if (_activeCamera == null) return;
                _activeCamera.useTargetGroup = v;
                if (v) RebuildTargetGroupFromRoster();
            };
        }

        private void BuildOrbitGroup(RectTransform parent)
        {
            _orbitSection = PanelSectionToggle.CreateNewEntry(parent);
            _orbitGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _orbitSection, parent, BasisLocalization.Get("camera.orbit"), false);
            RectTransform content = _orbitGroup.ContentParent;

            _orbitFollowHeadingToggle = PanelToggle.CreateNewEntry(content);
            _orbitFollowHeadingToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.orbitFollowHeading"));
            _orbitFollowHeadingToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.orbitFollowHeading.description"));
            _orbitFollowHeadingToggle.OnValueChanged = v => EditShot(shot => shot.orbit.followSubjectHeading = v);

            _orbitHeadingSlider = PanelSlider.CreateNew(content);
            _orbitHeadingSlider.SetSliderSettings(PanelSlider.SliderSettings.Degrees(
                BasisLocalization.Get("camera.orbitHeading"), -180f, 180f, false, 1));
            _orbitHeadingSlider.OnValueChanged = v => EditShot(shot => shot.orbit.heading = v);

            _orbitVerticalSlider = PanelSlider.CreateNew(content);
            _orbitVerticalSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitVertical"), 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _orbitVerticalSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.orbitVertical.description"));
            _orbitVerticalSlider.OnValueChanged = v => EditShot(shot => shot.orbit.verticalAxis = v);

            _orbitTopHeightSlider = PanelSlider.CreateNew(content);
            _orbitTopHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitTopHeight"), -1f, 5f, false, 2, ValueDisplayMode.Meters));
            _orbitTopHeightSlider.OnValueChanged = v => EditShot(shot => shot.orbit.top.height = v);

            _orbitTopRadiusSlider = PanelSlider.CreateNew(content);
            _orbitTopRadiusSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitTopRadius"), 0f, 8f, false, 2, ValueDisplayMode.Meters));
            _orbitTopRadiusSlider.OnValueChanged = v => EditShot(shot => shot.orbit.top.radius = v);

            _orbitMidHeightSlider = PanelSlider.CreateNew(content);
            _orbitMidHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitMidHeight"), -2f, 4f, false, 2, ValueDisplayMode.Meters));
            _orbitMidHeightSlider.OnValueChanged = v => EditShot(shot => shot.orbit.middle.height = v);

            _orbitMidRadiusSlider = PanelSlider.CreateNew(content);
            _orbitMidRadiusSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitMidRadius"), 0f, 8f, false, 2, ValueDisplayMode.Meters));
            _orbitMidRadiusSlider.OnValueChanged = v => EditShot(shot => shot.orbit.middle.radius = v);

            _orbitBottomHeightSlider = PanelSlider.CreateNew(content);
            _orbitBottomHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitBottomHeight"), -3f, 3f, false, 2, ValueDisplayMode.Meters));
            _orbitBottomHeightSlider.OnValueChanged = v => EditShot(shot => shot.orbit.bottom.height = v);

            _orbitBottomRadiusSlider = PanelSlider.CreateNew(content);
            _orbitBottomRadiusSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.orbitBottomRadius"), 0f, 8f, false, 2, ValueDisplayMode.Meters));
            _orbitBottomRadiusSlider.OnValueChanged = v => EditShot(shot => shot.orbit.bottom.radius = v);
        }

        private void BuildNoiseGroup(RectTransform parent)
        {
            _noiseSection = PanelSectionToggle.CreateNewEntry(parent);
            _noiseGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _noiseSection, parent, BasisLocalization.Get("camera.noise"), false);
            RectTransform content = _noiseGroup.ContentParent;

            _noiseProfileDropdown = PanelDropdown.CreateNewEntry(content);
            _noiseProfileDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.noiseProfile"));
            _noiseProfileDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.noiseProfile.description"));
            _noiseProfileDropdown.AssignEntries(new List<string>(NoiseProfileLabels));
            _noiseProfileDropdown.OnValueChanged = _ =>
            {
                int index = _noiseProfileDropdown != null ? _noiseProfileDropdown.Index : -1;
                if (index < 0) return;
                EditShot(shot =>
                {
                    float amplitude = shot.noise.amplitudeGain;
                    float frequency = shot.noise.frequencyGain;
                    shot.noise = BasisCameraNoiseSettings.ForProfile((BasisCameraNoiseProfile)index);
                    if (index != 0)
                    {
                        shot.noise.amplitudeGain = amplitude <= 0f ? 1f : amplitude;
                        shot.noise.frequencyGain = frequency <= 0f ? 1f : frequency;
                    }
                });
            };

            _noiseAmplitudeSlider = PanelSlider.CreateNew(content);
            _noiseAmplitudeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.noiseAmplitude"), 0f, 3f, false, 2, ValueDisplayMode.Raw));
            _noiseAmplitudeSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.noiseAmplitude.description"));
            _noiseAmplitudeSlider.OnValueChanged = v => EditShot(shot => shot.noise.amplitudeGain = v);

            _noiseFrequencySlider = PanelSlider.CreateNew(content);
            _noiseFrequencySlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.noiseFrequency"), 0f, 3f, false, 2, ValueDisplayMode.Raw));
            _noiseFrequencySlider.OnValueChanged = v => EditShot(shot => shot.noise.frequencyGain = v);
        }

        private void BuildDollyGroup(RectTransform parent)
        {
            _dollySection = PanelSectionToggle.CreateNewEntry(parent);
            _dollyGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _dollySection, parent, BasisLocalization.Get("camera.dolly"), false);
            RectTransform content = _dollyGroup.ContentParent;

            _dollyEmptyState = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            _dollyEmptyState.SetTitle(BasisLocalization.Get("camera.dollyEmpty"));
            _dollyEmptyState.SetDescription(BasisLocalization.Get("camera.dollyEmpty.description"));

            RectTransform placeRow = PanelElementDescriptor.BuildActionRow(content, "CameraDollyPlaceRow");

            PanelButton placeAtCamera = PanelButton.CreateNew(placeRow);
            placeAtCamera.Descriptor.SetTitle(BasisLocalization.Get("camera.placeWaypointHere"));
            placeAtCamera.Descriptor.SetDescription(BasisLocalization.Get("camera.placeWaypointHere.description"));
            placeAtCamera.OnClicked += () =>
            {
                if (_activeCamera == null) return;
                _activeCamera.SpawnWaypointAtCamera();
                _selectedWaypointIndex = _activeCamera.DollyWaypointCount - 1;
                _lastWaypointCount = -1;
                RefreshWaypointList();
            };

            PanelButton placeAtPlayer = PanelButton.CreateNew(placeRow);
            placeAtPlayer.Descriptor.SetTitle(BasisLocalization.Get("camera.placeWaypointAtMe"));
            placeAtPlayer.Descriptor.SetDescription(BasisLocalization.Get("camera.placeWaypointAtMe.description"));
            placeAtPlayer.OnClicked += () =>
            {
                if (_activeCamera == null) return;
                _activeCamera.SpawnWaypointInFrontOfPlayer();
                _selectedWaypointIndex = _activeCamera.DollyWaypointCount - 1;
                _lastWaypointCount = -1;
                RefreshWaypointList();
            };

            _waypointDropdown = PanelDropdown.CreateNewEntry(content);
            _waypointDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.waypoint"));
            _waypointDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.waypoint.description"));
            _waypointDropdown.OnValueChanged = _ =>
            {
                if (_waypointDropdown == null) return;
                int index = _waypointDropdown.Index;
                if (index >= 0)
                {
                    _selectedWaypointIndex = index;
                    SeedWaypointOrderSlider();
                }
            };

            _waypointOrderSlider = PanelSlider.CreateNew(content);
            _waypointOrderSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.waypointOrder"), 1f, 32f, true, 0, ValueDisplayMode.Raw));
            _waypointOrderSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.waypointOrder.description"));
            _waypointOrderSlider.OnValueChanged = v =>
            {
                if (_activeCamera == null) return;
                int target = Mathf.RoundToInt(v) - 1;
                if (_activeCamera.SetWaypointQueuePosition(_selectedWaypointIndex, target))
                {
                    _selectedWaypointIndex = Mathf.Clamp(target, 0, _activeCamera.DollyWaypointCount - 1);
                    _lastWaypointCount = -1;
                    RefreshWaypointList();
                }
            };

            RectTransform manageRow = PanelElementDescriptor.BuildActionRow(content, "CameraDollyManageRow");

            PanelButton deleteWaypoint = PanelButton.CreateNew(manageRow);
            deleteWaypoint.Descriptor.SetTitle(BasisLocalization.Get("camera.deleteWaypoint"));
            deleteWaypoint.OnClicked += () =>
            {
                if (_activeCamera == null) return;
                if (_activeCamera.RemoveWaypoint(_selectedWaypointIndex))
                {
                    _selectedWaypointIndex = Mathf.Max(0, _selectedWaypointIndex - 1);
                    _lastWaypointCount = -1;
                    RefreshWaypointList();
                }
            };

            PanelButton clearTrack = PanelButton.CreateNew(manageRow);
            clearTrack.Descriptor.SetTitle(BasisLocalization.Get("camera.clearTrack"));
            clearTrack.OnClicked += () =>
            {
                if (_activeCamera == null) return;
                _activeCamera.ClearDollyTrack();
                _selectedWaypointIndex = 0;
                _lastWaypointCount = -1;
                RefreshWaypointList();
            };

            _dollyLoopToggle = PanelToggle.CreateNewEntry(content);
            _dollyLoopToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.dollyLoop"));
            _dollyLoopToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.dollyLoop.description"));
            _dollyLoopToggle.OnValueChanged = v =>
            {
                if (_activeCamera?.DollyTrack != null) _activeCamera.DollyTrack.Looped = v;
            };

            _dollyVisibleToggle = PanelToggle.CreateNewEntry(content);
            _dollyVisibleToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.dollyVisible"));
            _dollyVisibleToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.dollyVisible.description"));
            _dollyVisibleToggle.OnValueChanged = v => _activeCamera?.DollyTrack?.SetVisible(v);

            _dollyGridSnapToggle = PanelToggle.CreateNewEntry(content);
            _dollyGridSnapToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.dollyGridSnap"));
            _dollyGridSnapToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.dollyGridSnap.description"));
            _dollyGridSnapToggle.OnValueChanged = v =>
            {
                if (_activeCamera?.DollyTrack == null) return;
                _activeCamera.DollyTrack.SetGridSnap(v, _activeCamera.DollyTrack.GridSize);
            };

            _dollyGridSizeSlider = PanelSlider.CreateNew(content);
            _dollyGridSizeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dollyGridSize"), 0.05f, 2f, false, 2, ValueDisplayMode.Meters));
            _dollyGridSizeSlider.OnValueChanged = v =>
            {
                if (_activeCamera?.DollyTrack == null) return;
                _activeCamera.DollyTrack.SetGridSnap(_activeCamera.DollyTrack.GridSnap, v);
            };

            _dollyAutoTrackToggle = PanelToggle.CreateNewEntry(content);
            _dollyAutoTrackToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.dollyAutoTrack"));
            _dollyAutoTrackToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.dollyAutoTrack.description"));
            _dollyAutoTrackToggle.OnValueChanged = v => EditShot(shot => shot.dollyAutoTrack = v);

            _dollyPositionSlider = PanelSlider.CreateNew(content);
            _dollyPositionSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dollyPosition"), 0f, 32f, false, 2, ValueDisplayMode.Raw));
            _dollyPositionSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.dollyPosition.description"));
            _dollyPositionSlider.OnValueChanged = v => EditShot(shot => shot.dollyPosition = v);

            _dollySpeedSlider = PanelSlider.CreateNew(content);
            _dollySpeedSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.dollySpeed"), -3f, 3f, false, 2, ValueDisplayMode.Raw));
            _dollySpeedSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.dollySpeed.description"));
            _dollySpeedSlider.OnValueChanged = v => EditShot(shot => shot.dollySpeed = v);
        }

        private void BuildBackgroundGroup(RectTransform parent)
        {
            _backgroundSection = PanelSectionToggle.CreateNewEntry(parent);
            _backgroundGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _backgroundSection, parent, BasisLocalization.Get("camera.background"), false);
            RectTransform content = _backgroundGroup.ContentParent;

            _backgroundModeDropdown = PanelDropdown.CreateNewEntry(content);
            _backgroundModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.backgroundMode"));
            _backgroundModeDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.backgroundMode.description"));
            _backgroundModeDropdown.AssignEntries(new List<string>(BackgroundModeLabels));
            _backgroundModeDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _backgroundModeDropdown == null) return;
                int index = _backgroundModeDropdown.Index;
                if (index < 0) return;
                _activeCamera.SetBackgroundMode((BasisCameraBackgroundMode)index);
                RefreshBackgroundVisibility();
            };

            _backgroundKeepWorldToggle = PanelToggle.CreateNewEntry(content);
            _backgroundKeepWorldToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.backgroundKeepWorld"));
            _backgroundKeepWorldToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.backgroundKeepWorld.description"));
            _backgroundKeepWorldToggle.OnValueChanged = v => _activeCamera?.SetBackgroundKeepsWorld(v);

            _backgroundRedSlider = PanelSlider.CreateNew(content);
            _backgroundRedSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.backgroundRed"), 0f, 1f, false, 3, ValueDisplayMode.Raw));
            _backgroundRedSlider.OnValueChanged = v => SetBackgroundChannel(0, v);

            _backgroundGreenSlider = PanelSlider.CreateNew(content);
            _backgroundGreenSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.backgroundGreen"), 0f, 1f, false, 3, ValueDisplayMode.Raw));
            _backgroundGreenSlider.OnValueChanged = v => SetBackgroundChannel(1, v);

            _backgroundBlueSlider = PanelSlider.CreateNew(content);
            _backgroundBlueSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.backgroundBlue"), 0f, 1f, false, 3, ValueDisplayMode.Raw));
            _backgroundBlueSlider.OnValueChanged = v => SetBackgroundChannel(2, v);
        }

        private void SetBackgroundChannel(int channel, float value)
        {
            if (_activeCamera == null) return;
            Color color = _activeCamera.backgroundCustomColor;
            color[channel] = value;
            _activeCamera.SetBackgroundCustomColor(color);
        }

        /// <summary>
        /// Applies an edit to the live shot. Every shot control routes through here so a control
        /// touched while no shot exists is a no-op rather than a null reference.
        /// </summary>
        private void EditShot(System.Action<BasisCameraShot> edit)
        {
            BasisCameraShot shot = EditedShot;
            if (shot == null) return;
            edit(shot);
        }

        /// <summary>Fills the target group with everyone currently in the instance, local included.</summary>
        private void RebuildTargetGroupFromRoster()
        {
            if (_activeCamera == null) return;

            _activeCamera.TargetGroup.Clear();
            _activeCamera.TargetGroup.Add(0);
            foreach (var pair in Basis.Scripts.Networking.BasisNetworkPlayers.RemotePlayers)
            {
                if (pair.Value != null) _activeCamera.TargetGroup.Add(pair.Key);
            }
        }

        // The shot list changes from panel buttons and from the camera itself, and the live shot can
        // change by priority without the panel touching anything - so both lists are rebuilt from a
        // cheap hash on the tick rather than from an event on every mutation site.
        private void RefreshShotList()
        {
            if (_shotDropdown == null || _activeCamera?.Director == null) return;

            var shots = _activeCamera.Director.Shots;
            int hash = shots.Count * 397;
            for (int Index = 0; Index < shots.Count; Index++)
            {
                hash = hash * 31 + shots[Index].id;
                hash = hash * 31 + (shots[Index].name?.GetHashCode() ?? 0);
            }
            hash = hash * 31 + _activeCamera.Director.LiveShotId;

            if (hash == _lastShotRosterHash) return;
            _lastShotRosterHash = hash;

            _shotIds.Clear();
            var keys = new List<string>();
            var labels = new List<string>();
            for (int Index = 0; Index < shots.Count; Index++)
            {
                _shotIds.Add(shots[Index].id);
                keys.Add(shots[Index].id.ToString());
                labels.Add($"{Index + 1}. {shots[Index].name}");
            }

            if (keys.Count == 0)
            {
                _shotDropdown.AssignEntries(new List<string> { "-" }, new List<string> { BasisLocalization.Get("camera.noShots") });
                ForceLayoutRebuild(_cinematicGroup);
                return;
            }

            _shotDropdown.AssignEntries(keys, labels);

            int liveIndex = _shotIds.IndexOf(_activeCamera.Director.LiveShotId);
            if (liveIndex < 0) liveIndex = 0;
            _shotDropdown.SetValueWithoutNotify(keys[liveIndex]);

            SeedShotControls();
            ForceLayoutRebuild(_cinematicGroup);
        }

        private void RefreshWaypointList()
        {
            if (_waypointDropdown == null || _activeCamera?.DollyTrack == null) return;

            int count = _activeCamera.DollyWaypointCount;
            if (count == _lastWaypointCount) return;
            _lastWaypointCount = count;

            _waypointKeys.Clear();
            var labels = new List<string>();
            for (int Index = 0; Index < count; Index++)
            {
                _waypointKeys.Add(Index.ToString());
                labels.Add($"{BasisLocalization.Get("camera.waypoint")} {Index + 1}");
            }

            bool hasAny = count > 0;
            _dollyEmptyState?.gameObject.SetActive(!hasAny);
            _waypointDropdown.gameObject.SetActive(hasAny);
            _waypointOrderSlider?.gameObject.SetActive(hasAny);

            if (hasAny)
            {
                _waypointDropdown.AssignEntries(_waypointKeys, labels);
                _selectedWaypointIndex = Mathf.Clamp(_selectedWaypointIndex, 0, count - 1);
                _waypointDropdown.SetValueWithoutNotify(_waypointKeys[_selectedWaypointIndex]);
                SeedWaypointOrderSlider();
            }

            ForceLayoutRebuild(_dollyGroup);
        }

        private void SeedWaypointOrderSlider()
        {
            if (_waypointOrderSlider == null || _activeCamera == null) return;
            _waypointOrderSlider.SetValueWithoutNotify(_selectedWaypointIndex + 1);
        }

        /// <summary>
        /// Pushes the live shot's values into every shot control. Called when the selection changes
        /// rather than every frame, so a drag is never fought mid-gesture.
        /// </summary>
        private void SeedShotControls()
        {
            BasisCameraShot shot = EditedShot;
            if (shot == null) return;

            _bodyModeDropdown?.SetValueWithoutNotify(BodyModeLabels[(int)shot.bodyMode]);
            _aimModeDropdown?.SetValueWithoutNotify(AimModeLabels[(int)shot.aimMode]);
            _bindingModeDropdown?.SetValueWithoutNotify(BindingModeLabels[(int)shot.bindingMode]);
            _blendStyleDropdown?.SetValueWithoutNotify(BlendStyleLabels[(int)shot.blendStyle]);
            _noiseProfileDropdown?.SetValueWithoutNotify(NoiseProfileLabels[(int)shot.noise.profile]);

            _shotOrderSlider?.SetValueWithoutNotify(_activeCamera.Director.IndexOf(shot.id) + 1);
            _shotPrioritySlider?.SetValueWithoutNotify(shot.priority);
            _blendTimeSlider?.SetValueWithoutNotify(shot.blendTime);

            _shotOffsetXSlider?.SetValueWithoutNotify(shot.positionOffset.x);
            _shotOffsetYSlider?.SetValueWithoutNotify(shot.positionOffset.y);
            _shotOffsetZSlider?.SetValueWithoutNotify(shot.positionOffset.z);

            _shotDampXSlider?.SetValueWithoutNotify(shot.positionDamping.x);
            _shotDampYSlider?.SetValueWithoutNotify(shot.positionDamping.y);
            _shotDampZSlider?.SetValueWithoutNotify(shot.positionDamping.z);
            _shotRotDampSlider?.SetValueWithoutNotify(shot.rotationDamping.x);

            _screenXSlider?.SetValueWithoutNotify(shot.composer.screenX);
            _screenYSlider?.SetValueWithoutNotify(shot.composer.screenY);
            _deadZoneWidthSlider?.SetValueWithoutNotify(shot.composer.deadZoneWidth);
            _deadZoneHeightSlider?.SetValueWithoutNotify(shot.composer.deadZoneHeight);
            _softZoneWidthSlider?.SetValueWithoutNotify(shot.composer.softZoneWidth);
            _softZoneHeightSlider?.SetValueWithoutNotify(shot.composer.softZoneHeight);
            _composerDampHSlider?.SetValueWithoutNotify(shot.composer.horizontalDamping);
            _composerDampVSlider?.SetValueWithoutNotify(shot.composer.verticalDamping);

            _lookAheadSlider?.SetValueWithoutNotify(shot.lookAheadTime);
            _framingSizeSlider?.SetValueWithoutNotify(shot.framingScreenFraction);
            _framingZoomToggle?.SetValueWithoutNotify(shot.framingUsesZoom);
            _occlusionToggle?.SetValueWithoutNotify(shot.avoidOcclusion);

            _orbitFollowHeadingToggle?.SetValueWithoutNotify(shot.orbit.followSubjectHeading);
            _orbitHeadingSlider?.SetValueWithoutNotify(shot.orbit.heading);
            _orbitVerticalSlider?.SetValueWithoutNotify(shot.orbit.verticalAxis);
            _orbitTopHeightSlider?.SetValueWithoutNotify(shot.orbit.top.height);
            _orbitTopRadiusSlider?.SetValueWithoutNotify(shot.orbit.top.radius);
            _orbitMidHeightSlider?.SetValueWithoutNotify(shot.orbit.middle.height);
            _orbitMidRadiusSlider?.SetValueWithoutNotify(shot.orbit.middle.radius);
            _orbitBottomHeightSlider?.SetValueWithoutNotify(shot.orbit.bottom.height);
            _orbitBottomRadiusSlider?.SetValueWithoutNotify(shot.orbit.bottom.radius);

            _noiseAmplitudeSlider?.SetValueWithoutNotify(shot.noise.amplitudeGain);
            _noiseFrequencySlider?.SetValueWithoutNotify(shot.noise.frequencyGain);

            _dollyAutoTrackToggle?.SetValueWithoutNotify(shot.dollyAutoTrack);
            _dollyPositionSlider?.SetValueWithoutNotify(shot.dollyPosition);
            _dollySpeedSlider?.SetValueWithoutNotify(shot.dollySpeed);

            RefreshCinematicVisibility();
        }

        /// <summary>Seeds the controls that read camera state rather than shot state.</summary>
        private void SeedCinematicCameraControls()
        {
            if (_activeCamera == null) return;

            _cinematicToggle?.SetValueWithoutNotify(_activeCamera.cinematicEnabled);
            _targetGroupToggle?.SetValueWithoutNotify(_activeCamera.useTargetGroup);

            if (_activeCamera.DollyTrack != null)
            {
                _dollyLoopToggle?.SetValueWithoutNotify(_activeCamera.DollyTrack.Looped);
                _dollyVisibleToggle?.SetValueWithoutNotify(_activeCamera.DollyTrack.Visible);
                _dollyGridSnapToggle?.SetValueWithoutNotify(_activeCamera.DollyTrack.GridSnap);
                _dollyGridSizeSlider?.SetValueWithoutNotify(_activeCamera.DollyTrack.GridSize);
            }

            _backgroundModeDropdown?.SetValueWithoutNotify(BackgroundModeLabels[(int)_activeCamera.backgroundMode]);
            _backgroundKeepWorldToggle?.SetValueWithoutNotify(_activeCamera.backgroundKeepsWorld);
            _backgroundRedSlider?.SetValueWithoutNotify(_activeCamera.backgroundCustomColor.r);
            _backgroundGreenSlider?.SetValueWithoutNotify(_activeCamera.backgroundCustomColor.g);
            _backgroundBlueSlider?.SetValueWithoutNotify(_activeCamera.backgroundCustomColor.b);

            RefreshBackgroundVisibility();
        }

        /// <summary>Hides the controls the live shot's body and aim modes do not use.</summary>
        private void RefreshCinematicVisibility()
        {
            BasisCameraShot shot = EditedShot;
            if (shot == null) return;

            bool composer = shot.aimMode == BasisCameraAimMode.Composer;
            bool framing = shot.bodyMode == BasisCameraBodyMode.Framing;
            bool orbit = shot.bodyMode == BasisCameraBodyMode.Orbital;
            bool dolly = shot.bodyMode == BasisCameraBodyMode.Dolly;
            bool offsetDriven = shot.bodyMode == BasisCameraBodyMode.Transposer || framing;

            _screenXSlider?.gameObject.SetActive(composer);
            _screenYSlider?.gameObject.SetActive(composer);
            _deadZoneWidthSlider?.gameObject.SetActive(composer);
            _deadZoneHeightSlider?.gameObject.SetActive(composer);
            _softZoneWidthSlider?.gameObject.SetActive(composer);
            _softZoneHeightSlider?.gameObject.SetActive(composer);
            _composerDampHSlider?.gameObject.SetActive(composer);
            _composerDampVSlider?.gameObject.SetActive(composer);

            _framingSizeSlider?.gameObject.SetActive(framing);
            _framingZoomToggle?.gameObject.SetActive(framing);

            _shotOffsetXSlider?.gameObject.SetActive(offsetDriven);
            _shotOffsetYSlider?.gameObject.SetActive(offsetDriven);
            _shotOffsetZSlider?.gameObject.SetActive(offsetDriven);
            _bindingModeDropdown?.gameObject.SetActive(offsetDriven);

            // The orbit section stays put whatever the mode. FinalizeCollapsibleGroup owns a
            // section's active state and persists its open/closed flag, so hiding the section here
            // would fight it and lose the user's expanded state.
            _orbitSection?.Descriptor.SetDescription(orbit
                ? string.Empty
                : BasisLocalization.Get("camera.orbit.inactive"));

            _dollyAutoTrackToggle?.gameObject.SetActive(dolly);
            _dollyPositionSlider?.gameObject.SetActive(dolly && !shot.dollyAutoTrack);
            _dollySpeedSlider?.gameObject.SetActive(dolly && !shot.dollyAutoTrack);

            ForceLayoutRebuild(_cinematicGroup);
            ForceLayoutRebuild(_compositionGroup);
            ForceLayoutRebuild(_dollyGroup);
        }

        private void RefreshBackgroundVisibility()
        {
            if (_activeCamera == null) return;

            bool colour = _activeCamera.backgroundMode != BasisCameraBackgroundMode.World;
            bool custom = _activeCamera.backgroundMode == BasisCameraBackgroundMode.Custom;

            _backgroundKeepWorldToggle?.gameObject.SetActive(colour);
            _backgroundRedSlider?.gameObject.SetActive(custom);
            _backgroundGreenSlider?.gameObject.SetActive(custom);
            _backgroundBlueSlider?.gameObject.SetActive(custom);

            ForceLayoutRebuild(_backgroundGroup);
        }

        private void TickCinematicSections()
        {
            if (_activeCamera == null) return;

            SyncToggle(_cinematicToggle, _activeCamera.cinematicEnabled, ref _lastCinematic);
            RefreshShotList();
            RefreshWaypointList();
            RefreshCompositionGuides();
        }

        private void ClearCinematicReferences()
        {
            // The guides are children of the preview image, which the panel destroys with itself.
            // Dropping the handles without clearing _guidesBuilt would leave the next open
            // believing it already had them and drawing nothing.
            DestroyCompositionGuides();
            _guidesToggle = null;

            _cinematicSection = null;
            _cinematicGroup = null;
            _cinematicToggle = null;
            _shotDropdown = null;
            _shotOrderSlider = null;
            _bodyModeDropdown = null;
            _aimModeDropdown = null;
            _bindingModeDropdown = null;
            _shotPrioritySlider = null;
            _shotOffsetXSlider = null;
            _shotOffsetYSlider = null;
            _shotOffsetZSlider = null;
            _shotDampXSlider = null;
            _shotDampYSlider = null;
            _shotDampZSlider = null;
            _shotRotDampSlider = null;
            _blendStyleDropdown = null;
            _blendTimeSlider = null;
            _shotIds.Clear();
            _lastShotRosterHash = -1;
            _lastCinematic = null;

            _compositionSection = null;
            _compositionGroup = null;
            _screenXSlider = null;
            _screenYSlider = null;
            _deadZoneWidthSlider = null;
            _deadZoneHeightSlider = null;
            _softZoneWidthSlider = null;
            _softZoneHeightSlider = null;
            _composerDampHSlider = null;
            _composerDampVSlider = null;
            _lookAheadSlider = null;
            _framingSizeSlider = null;
            _framingZoomToggle = null;
            _occlusionToggle = null;
            _targetGroupToggle = null;

            _orbitSection = null;
            _orbitGroup = null;
            _orbitHeadingSlider = null;
            _orbitVerticalSlider = null;
            _orbitTopHeightSlider = null;
            _orbitTopRadiusSlider = null;
            _orbitMidHeightSlider = null;
            _orbitMidRadiusSlider = null;
            _orbitBottomHeightSlider = null;
            _orbitBottomRadiusSlider = null;
            _orbitFollowHeadingToggle = null;

            _noiseSection = null;
            _noiseGroup = null;
            _noiseProfileDropdown = null;
            _noiseAmplitudeSlider = null;
            _noiseFrequencySlider = null;

            _dollySection = null;
            _dollyGroup = null;
            _waypointDropdown = null;
            _waypointOrderSlider = null;
            _dollyLoopToggle = null;
            _dollyVisibleToggle = null;
            _dollyGridSnapToggle = null;
            _dollyGridSizeSlider = null;
            _dollyAutoTrackToggle = null;
            _dollyPositionSlider = null;
            _dollySpeedSlider = null;
            _dollyEmptyState = null;
            _waypointKeys.Clear();
            _selectedWaypointIndex = 0;
            _lastWaypointCount = -1;

            _backgroundSection = null;
            _backgroundGroup = null;
            _backgroundModeDropdown = null;
            _backgroundKeepWorldToggle = null;
            _backgroundRedSlider = null;
            _backgroundGreenSlider = null;
            _backgroundBlueSlider = null;
        }
    }
}
