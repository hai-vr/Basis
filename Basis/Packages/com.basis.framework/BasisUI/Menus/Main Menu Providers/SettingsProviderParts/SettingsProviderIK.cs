using Basis;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SettingsProviderIK
{
    public const string SeatedMode_Seated = "Seated Mode";
    public const string SeatedMode_Standing = "Standing Mode";

    private static readonly List<PanelToggle> _euroToggleUIs = new();
    private static readonly List<PanelToggle> _trackerLerpToggleUIs = new();

    private static PanelDropdown _boneDropdown;

    private static PanelToggle _uiUseCalibration;
    private static PanelToggle _uiSmoothPos;
    private static PanelToggle _uiSmoothRot;
    private static PanelToggle _uiEuroPos;
    private static PanelToggle _uiEuroRot;
    private static PanelSlider _uiCalibSphereScale;
    private static PanelSlider _avatarScaleSlider;
    private static PanelElementDescriptor _boneEuroEditorGroup;

    private struct BoneBindings
    {
        public string Name;
        public BasisSettingsBinding<bool> UseCalibration;
        public BasisSettingsBinding<bool> SmoothPos;
        public BasisSettingsBinding<bool> SmoothRot;
        public BasisSettingsBinding<bool> EuroPos;
        public BasisSettingsBinding<bool> EuroRot;
        public BasisSettingsBinding<float> CalibSphereScale;
    }

    private static readonly List<BoneBindings> _bones = new();

    // ------------------
    // IK & Input
    // ------------------
    public static PanelTabPage IKTab(PanelTabGroup tabGroup)
    {
        // --- Tab (replaces BasisTabBuilder) ---
        var tabPage = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        var tabDesc = tabPage.Descriptor;
        tabDesc.SetTitle(BasisLocalization.Get("settings.tab.bodytracking"));
        tabDesc.SetIcon(AddressableAssets.Sprites.Settings);

        // --- Group: "Body Tracking" (replaces tab.Group(...)) ---
        var ikGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            tabDesc.ContentParent);

        ikGroup.SetTitle(BasisLocalization.Get("settings.tab.bodytracking"));
        ikGroup.SetIcon(AddressableAssets.Sprites.Settings);

        var ikParent = ikGroup.ContentParent;

        // --- Custom scale toggle ---
        var customScaleToggle = PanelToggle.CreateNewEntry(ikParent);
        customScaleToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.customScale"));
        customScaleToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.customScale.tooltip"));
        customScaleToggle.AssignBinding(BasisSettingsDefaults.CustomScale);

        // --- Avatar scale slider ---
        var avatarScaleSlider = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.avatarHeightScale"), 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
            BasisSettingsDefaults.SelectedScale);
        _avatarScaleSlider = avatarScaleSlider;

        if (avatarScaleSlider != null)
        {
            avatarScaleSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.avatarHeightScale.tooltip"));

            avatarScaleSlider.gameObject.SetActive(BasisSettingsDefaults.CustomScale.RawValue);
            customScaleToggle.OnValueChanged += visible =>
            {
                avatarScaleSlider.gameObject.SetActive(visible);
                tabDesc.ForceRebuild();
                ikGroup.ForceRebuild();
            };
        }

        _trackerLerpToggleUIs.Clear();
        _euroToggleUIs.Clear();

        CreateCollapsibleSection(tabDesc, ikGroup,
            BasisLocalization.Get("settings.bodyTracking.section.perBone.title"),
            BasisLocalization.Get("settings.bodyTracking.section.perBone.description"), false,
            AddFBIKTogglesCompact);

        SyncMasterEuroFromChildren();

        // ------------------
        // Playspace Mover
        // ------------------
        CreateCollapsibleSection(tabDesc, ikGroup,
            BasisLocalization.Get("settings.bodyTracking.playspaceMover.title"),
            BasisLocalization.Get("settings.bodyTracking.playspaceMover.description"), false, moverParent =>
        {
            var enableToggle = PanelToggle.CreateNewEntry(moverParent);
            enableToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.enable"));
            enableToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.enable.tooltip"));
            enableToggle.AssignBinding(BasisSettingsDefaults.EnablePlayspaceMover);

            var inputDropdown = PanelDropdown.CreateNewEntry(moverParent);
            inputDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.input"));
            inputDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.input.tooltip"));
            inputDropdown.AssignLocalizedEntries(
                new List<string> { BasisLocalPlayspaceMover.InputGrip, BasisLocalPlayspaceMover.InputTrigger, BasisLocalPlayspaceMover.InputPrimary, BasisLocalPlayspaceMover.InputSecondary },
                new List<string> { "settings.bodyTracking.playspaceMover.input.grip", "settings.bodyTracking.playspaceMover.input.trigger", "settings.bodyTracking.playspaceMover.input.primary", "settings.bodyTracking.playspaceMover.input.secondary" });
            inputDropdown.AssignBinding(BasisSettingsDefaults.PlayspaceMoverInput);

            var handDropdown = PanelDropdown.CreateNewEntry(moverParent);
            handDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.hand"));
            handDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.hand.tooltip"));
            handDropdown.AssignLocalizedEntries(
                new List<string> { BasisLocalPlayspaceMover.HandBoth, BasisLocalPlayspaceMover.HandLeft, BasisLocalPlayspaceMover.HandRight },
                new List<string> { "settings.bodyTracking.playspaceMover.hand.both", "settings.bodyTracking.playspaceMover.hand.left", "settings.bodyTracking.playspaceMover.hand.right" });
            handDropdown.AssignBinding(BasisSettingsDefaults.PlayspaceMoverHand);

            var rotateToggle = PanelToggle.CreateNewEntry(moverParent);
            rotateToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.rotate"));
            rotateToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.rotate.tooltip"));
            rotateToggle.AssignBinding(BasisSettingsDefaults.PlayspaceMoverRotate);

            var rotateInputDropdown = PanelDropdown.CreateNewEntry(moverParent);
            rotateInputDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.rotateInput"));
            rotateInputDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.rotateInput.tooltip"));
            rotateInputDropdown.AssignLocalizedEntries(
                new List<string> { BasisLocalPlayspaceMover.InputGrip, BasisLocalPlayspaceMover.InputTrigger, BasisLocalPlayspaceMover.InputPrimary, BasisLocalPlayspaceMover.InputSecondary },
                new List<string> { "settings.bodyTracking.playspaceMover.input.grip", "settings.bodyTracking.playspaceMover.input.trigger", "settings.bodyTracking.playspaceMover.input.primary", "settings.bodyTracking.playspaceMover.input.secondary" });
            rotateInputDropdown.AssignBinding(BasisSettingsDefaults.PlayspaceMoverRotateInput);

            var scaleToggle = PanelToggle.CreateNewEntry(moverParent);
            scaleToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.scale"));
            scaleToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.scale.tooltip"));
            scaleToggle.AssignBinding(BasisSettingsDefaults.PlayspaceMoverScale);

            var verticalToggle = PanelToggle.CreateNewEntry(moverParent);
            verticalToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.vertical"));
            verticalToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.vertical.tooltip"));
            verticalToggle.AssignBinding(BasisSettingsDefaults.PlayspaceMoverVertical);

            var flipToggle = PanelToggle.CreateNewEntry(moverParent);
            flipToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flip"));
            flipToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flip.tooltip"));
            flipToggle.AssignBinding(BasisSettingsDefaults.PlayspaceMoverFlip);

            var flipAxisDropdown = PanelDropdown.CreateNewEntry(moverParent);
            flipAxisDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flipAxis"));
            flipAxisDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flipAxis.tooltip"));
            flipAxisDropdown.AssignLocalizedEntries(
                new List<string> { BasisLocalPlayspaceMover.AxisRoll, BasisLocalPlayspaceMover.AxisPitch, BasisLocalPlayspaceMover.AxisYaw },
                new List<string> { "settings.bodyTracking.playspaceMover.flipAxis.roll", "settings.bodyTracking.playspaceMover.flipAxis.pitch", "settings.bodyTracking.playspaceMover.flipAxis.yaw" });
            flipAxisDropdown.AssignBinding(BasisSettingsDefaults.PlayspaceMoverFlipAxis);

            var flipAngleSlider = PanelSlider.CreateEntryAndBind(
                moverParent,
                PanelSlider.SliderSettings.Degrees(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flipAngle"), 0f, 360f, true, 0),
                BasisSettingsDefaults.PlayspaceMoverFlipAngle);
            flipAngleSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.flipAngle.tooltip"));

            var resetButton = PanelButton.CreateNew(moverParent);
            resetButton.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.reset"));
            resetButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.reset.tooltip"));
            resetButton.OnClicked += BasisLocalPlayspaceMover.ResetOffset;
        });

        // ------------------
        // Advanced IK toggle
        // ------------------
        var advancedToggle = PanelToggle.CreateNewEntry(tabDesc.ContentParent);
        advancedToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.advanced"));
        advancedToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.advanced.tooltip"));
        advancedToggle.AssignBinding(BasisSettingsDefaults.FBIKAdvancedVisible);

        var colliderGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            tabDesc.ContentParent);

        colliderGroup.SetTitle(BasisLocalization.Get("settings.bodyTracking.colliders.title"));
        colliderGroup.SetIcon(AddressableAssets.Sprites.Settings);

        var colliderParent = colliderGroup.ContentParent;

        // ============== Tracking & Input ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.tracking.title"),
            BasisLocalization.Get("settings.bodyTracking.section.tracking.description"), true, trackingParent =>
        {
            var fbtEnabledToggle = PanelToggle.CreateNewEntry(trackingParent);
            fbtEnabledToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.fbt"));
            fbtEnabledToggle.AssignBinding(BasisSettingsDefaults.EnableFBT);
            fbtEnabledToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.fbt.tooltip"));

            var oscEnabledToggle = PanelToggle.CreateNewEntry(trackingParent);
            oscEnabledToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.osc"));
            oscEnabledToggle.AssignBinding(BasisSettingsDefaults.EnableOSC);
            oscEnabledToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.osc.tooltip"));

            var faceTrackingToggle = PanelToggle.CreateNewEntry(trackingParent);
            faceTrackingToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.faceTracking.title"));
            faceTrackingToggle.AssignBinding(BasisSettingsDefaults.EnableFaceTracking);
            faceTrackingToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.faceTracking.title.tooltip"));

            var eyeTrackingToggle = PanelToggle.CreateNewEntry(trackingParent);
            eyeTrackingToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.eyeTracking.title"));
            eyeTrackingToggle.AssignBinding(BasisSettingsDefaults.EnableEyeTracking);
            eyeTrackingToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.eyeTracking.title.tooltip"));

            var footIKToggle = PanelToggle.CreateNewEntry(trackingParent);
            footIKToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.footIk"));
            footIKToggle.AssignBinding(BasisSettingsDefaults.FootIKEnabled);
            footIKToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.footIk.tooltip"));

            var disableAnimInFBTToggle = PanelToggle.CreateNewEntry(trackingParent);
            disableAnimInFBTToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.disableAnimFbt"));
            disableAnimInFBTToggle.AssignBinding(BasisSettingsDefaults.DisableAnimationsInFBT);
            disableAnimInFBTToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.disableAnimFbt.tooltip"));

            var butterflyKneesToggle = PanelToggle.CreateNewEntry(trackingParent);
            butterflyKneesToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.butterflyKnees"));
            butterflyKneesToggle.AssignBinding(BasisSettingsDefaults.FBIKButterflyKnees);
            butterflyKneesToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.butterflyKnees.tooltip"));
        });

        // ============== Body Collision ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.collision.title"),
            BasisLocalization.Get("settings.bodyTracking.section.collision.description"), false, collisionParent =>
        {
            var collisionsToggle = PanelToggle.CreateNewEntry(collisionParent);
            collisionsToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.collisionsEnabled"));
            collisionsToggle.AssignBinding(BasisSettingsDefaults.FBIKCollisionsEnabled);
            collisionsToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.collisionsEnabled.tooltip"));

            var protectElbowToggle = PanelToggle.CreateNewEntry(collisionParent);
            protectElbowToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.protectElbow.title"));
            protectElbowToggle.AssignBinding(BasisSettingsDefaults.FBIKProtectElbow);
            protectElbowToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.protectElbow.title.tooltip"));

            var collideTrackedElbowToggle = PanelToggle.CreateNewEntry(collisionParent);
            collideTrackedElbowToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.collideTrackedElbow.title"));
            collideTrackedElbowToggle.AssignBinding(BasisSettingsDefaults.FBIKCollideTrackedElbow);
            collideTrackedElbowToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.collideTrackedElbow.title.tooltip"));

            var handCapsuleToggle = PanelToggle.CreateNewEntry(collisionParent);
            handCapsuleToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.handCapsule.title"));
            handCapsuleToggle.AssignBinding(BasisSettingsDefaults.FBIKUseHandCapsule);
            handCapsuleToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.handCapsule.title.tooltip"));

            var chestRadiusSlider = PanelSlider.CreateAndBind(
                collisionParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.chestRadius.title"), 0.01f, 0.5f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKChestRadius);
            if (chestRadiusSlider != null)
            {
                chestRadiusSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.chestRadius.title.tooltip"));
            }

            var collisionSkinSlider = PanelSlider.CreateAndBind(
                collisionParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.collisionSkin.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKCollisionSkin);
            if (collisionSkinSlider != null)
            {
                collisionSkinSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.collisionSkin.title.tooltip"));
            }

            var handRadiusSlider = PanelSlider.CreateAndBind(
                collisionParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.handRadius.title"), 0f, 0.2f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKHandRadius);
            if (handRadiusSlider != null)
            {
                handRadiusSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.handRadius.title.tooltip"));
            }

            var handSkinSlider = PanelSlider.CreateAndBind(
                collisionParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.handSkin.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKHandSkin);
            if (handSkinSlider != null)
            {
                handSkinSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.handSkin.title.tooltip"));
            }
        });

        // ============== Shoulders ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.shoulders.title"),
            BasisLocalization.Get("settings.bodyTracking.section.shoulders.description"), false, shoulderParent =>
        {
            var shoulderSolveToggle = PanelToggle.CreateNewEntry(shoulderParent);
            shoulderSolveToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.shoulderSolve.title"));
            shoulderSolveToggle.AssignBinding(BasisSettingsDefaults.FBIKShoulderSolveEnabled);
            shoulderSolveToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.shoulderSolve.title.tooltip"));

            var shoulderElevSlider = PanelSlider.CreateAndBind(
                shoulderParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.shoulderElevation.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKShoulderElevation);
            if (shoulderElevSlider != null)
            {
                shoulderElevSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.shoulderElevation.title.tooltip"));
            }

            var shoulderProtSlider = PanelSlider.CreateAndBind(
                shoulderParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.shoulderProtraction.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKShoulderProtraction);
            if (shoulderProtSlider != null)
            {
                shoulderProtSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.shoulderProtraction.title.tooltip"));
            }
        });

        // ============== Arm Twist ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.armTwist.title"),
            BasisLocalization.Get("settings.bodyTracking.section.armTwist.description"), false, twistParent =>
        {
            var lowerArmTwist = PanelSlider.CreateAndBind(
                twistParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lowerArmTwist.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLowerArmTwistFraction);
            if (lowerArmTwist != null)
            {
                lowerArmTwist.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lowerArmTwist.title.tooltip"));
            }

            var upperArmTwist = PanelSlider.CreateAndBind(
                twistParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.upperArmTwist.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKUpperArmTwistFraction);
            if (upperArmTwist != null)
            {
                upperArmTwist.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.upperArmTwist.title.tooltip"));
            }
        });

        // ============== Anatomy ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.anatomy.title"),
            BasisLocalization.Get("settings.bodyTracking.section.anatomy.description"), false, anatomyParent =>
        {
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKAnatDifferentialStiffness,
                "settings.bodyTracking.anat.diffStiffness.title",
                "settings.bodyTracking.anat.diffStiffness.description");
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKAnatShoulderSlide,
                "settings.bodyTracking.anat.shoulderSlide.title",
                "settings.bodyTracking.anat.shoulderSlide.description");
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKAnatCervicalLordosis,
                "settings.bodyTracking.anat.cervicalLordosis.title",
                "settings.bodyTracking.anat.cervicalLordosis.description");
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKAnatPelvicTwistRouting,
                "settings.bodyTracking.anat.pelvicTwistRouting.title",
                "settings.bodyTracking.anat.pelvicTwistRouting.description");
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKLegSwivelSmoothing,
                "settings.bodyTracking.anat.legSwivelSmoothing.title",
                "settings.bodyTracking.anat.legSwivelSmoothing.description");
            AddAnatomyToggle(anatomyParent, BasisSettingsDefaults.FBIKTrackerBendNormal,
                "settings.bodyTracking.anat.trackerBendNormal.title",
                "settings.bodyTracking.anat.trackerBendNormal.description");
        });

        // ============== Spine: Reach Limits ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.spineReach.title"),
            BasisLocalization.Get("settings.bodyTracking.section.spineReach.description"), false, reachParent =>
        {
            var maxBendSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.maxBendDeg.title"), 0f, 180f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMaxBendDeg);
            if (maxBendSlider != null)
            {
                maxBendSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.maxBendDeg.title.tooltip"));
            }

            var struggleStartSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.struggleStart.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKStruggleStart);
            if (struggleStartSlider != null)
            {
                struggleStartSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.struggleStart.title.tooltip"));
            }

            var struggleEndSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.struggleEnd.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKStruggleEnd);
            if (struggleEndSlider != null)
            {
                struggleEndSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.struggleEnd.title.tooltip"));
            }

            var maxChestDeltaSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.maxChestDelta.title"), 0f, 180f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMaxChestDelta);
            if (maxChestDeltaSlider != null)
            {
                maxChestDeltaSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.maxChestDelta.title.tooltip"));
            }

            var maxHipDeltaSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.maxHipDelta.title"), 0f, 180f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMaxHipDelta);
            if (maxHipDeltaSlider != null)
            {
                maxHipDeltaSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.maxHipDelta.title.tooltip"));
            }

            var butterflyMaxOpenSlider = PanelSlider.CreateAndBind(
                reachParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.butterflyKneeMaxOpen.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKButterflyKneeMaxOpenDeg);
            if (butterflyMaxOpenSlider != null)
            {
                butterflyMaxOpenSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.butterflyKneeMaxOpen.title.tooltip"));
            }
        });

        // ============== Spine: Bend Distribution ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.spineBend.title"),
            BasisLocalization.Get("settings.bodyTracking.section.spineBend.description"), false, bendParent =>
        {
            var spineBendPitch = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineBendPitch.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineBendPitch);
            if (spineBendPitch != null)
            {
                spineBendPitch.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineBendPitch.title.tooltip"));
            }

            var spineBendYaw = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineBendYaw.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineBendYaw);
            if (spineBendYaw != null)
            {
                spineBendYaw.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineBendYaw.title.tooltip"));
            }

            var spineBendRoll = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineBendRoll.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineBendRoll);
            if (spineBendRoll != null)
            {
                spineBendRoll.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineBendRoll.title.tooltip"));
            }

            var upperChestBendPitch = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.upperChestBendPitch.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKUpperChestBendPitch);
            if (upperChestBendPitch != null)
            {
                upperChestBendPitch.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.upperChestBendPitch.title.tooltip"));
            }

            var upperChestBendYaw = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.upperChestBendYaw.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKUpperChestBendYaw);
            if (upperChestBendYaw != null)
            {
                upperChestBendYaw.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.upperChestBendYaw.title.tooltip"));
            }

            var upperChestBendRoll = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.upperChestBendRoll.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKUpperChestBendRoll);
            if (upperChestBendRoll != null)
            {
                upperChestBendRoll.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.upperChestBendRoll.title.tooltip"));
            }

            var spineSquishBoost = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineSquishBoost.title"), 0f, 2f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineSquishBoost);
            if (spineSquishBoost != null)
            {
                spineSquishBoost.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineSquishBoost.title.tooltip"));
            }

            var spineMaxFwd = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineMaxForward.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineMaxForwardDeg);
            if (spineMaxFwd != null)
            {
                spineMaxFwd.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineMaxForward.title.tooltip"));
            }

            var spineMaxBack = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineMaxBackward.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineMaxBackwardDeg);
            if (spineMaxBack != null)
            {
                spineMaxBack.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineMaxBackward.title.tooltip"));
            }

            var spineMaxLat = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineMaxLateral.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineMaxLateralDeg);
            if (spineMaxLat != null)
            {
                spineMaxLat.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineMaxLateral.title.tooltip"));
            }

            var neckMaxCone = PanelSlider.CreateAndBind(
                bendParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.neckMaxCone.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKNeckMaxConeDeg);
            if (neckMaxCone != null)
            {
                neckMaxCone.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.neckMaxCone.title.tooltip"));
            }
        });

        // ============== Spine: Dynamics ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.spineDynamics.title"),
            BasisLocalization.Get("settings.bodyTracking.section.spineDynamics.description"), false, dynamicsParent =>
        {
            var hipHingeStart = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.hipHingeStart.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKHipHingeStartDeg);
            if (hipHingeStart != null)
            {
                hipHingeStart.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.hipHingeStart.title.tooltip"));
            }

            var hipHingeMax = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.hipHingeMaxAdd.title"), 0f, 60f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKHipHingeMaxAddDeg);
            if (hipHingeMax != null)
            {
                hipHingeMax.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.hipHingeMaxAdd.title.tooltip"));
            }

            var moveBodyBack = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.moveBodyBackWhenCrouching.title"), 0f, 2f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching);
            if (moveBodyBack != null)
            {
                moveBodyBack.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.moveBodyBackWhenCrouching.title.tooltip"));
            }

            var elbowSwingToggle = PanelToggle.CreateNewEntry(dynamicsParent);
            elbowSwingToggle.Descriptor.SetTitle("Elbow Swing Smoothing");
            elbowSwingToggle.AssignBinding(BasisSettingsDefaults.FBIKElbowSwingEnabled);
            elbowSwingToggle.Descriptor.SetTooltip("Rate-limits the elbow/knee swing and how fast a torso-collision push eases in. Off = the elbow swings freely (test for over-damping).");

            var swingSmooth = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.swingSmoothRate.title"), 0f, 3600f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSwingSmoothRate);
            if (swingSmooth != null)
            {
                swingSmooth.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.swingSmoothRate.title.tooltip"));
            }

            var chestSpringHz = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.chestSpringHz.title"), 0f, 30f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKChestSpringHz);
            if (chestSpringHz != null)
            {
                chestSpringHz.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.chestSpringHz.title.tooltip"));
            }

            var chestSpringDamping = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.chestSpringDamping.title"), 0f, 2f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKChestSpringDamping);
            if (chestSpringDamping != null)
            {
                chestSpringDamping.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.chestSpringDamping.title.tooltip"));
            }

            var spineCcdRelax = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineCcdRelax.title"), 0.1f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineCCDRelax);
            if (spineCcdRelax != null)
            {
                spineCcdRelax.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineCcdRelax.title.tooltip"));
            }

            var spineTwistKeep = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineTwistKeep.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineTwistKeep);
            if (spineTwistKeep != null)
            {
                spineTwistKeep.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineTwistKeep.title.tooltip"));
            }

            var spineNeckTwistKeep = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.spineNeckTwistKeep.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSpineNeckTwistKeep);
            if (spineNeckTwistKeep != null)
            {
                spineNeckTwistKeep.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineNeckTwistKeep.title.tooltip"));
            }

            var chestArmSwingFactor = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.chestArmSwingFactor.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKChestArmSwingFactor);
            if (chestArmSwingFactor != null)
            {
                chestArmSwingFactor.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.chestArmSwingFactor.title.tooltip"));
            }

            var chestArmSwingMaxDeg = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.chestArmSwingMax.title"), 0f, 30f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKChestArmSwingMaxDeg);
            if (chestArmSwingMaxDeg != null)
            {
                chestArmSwingMaxDeg.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.chestArmSwingMax.title.tooltip"));
            }

            var lordosisPitchGain = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisPitchGain.title"), 0f, 30f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisPitchGainDeg);
            if (lordosisPitchGain != null)
            {
                lordosisPitchGain.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisPitchGain.title.tooltip"));
            }

            var lordosisBase = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisBase.title"), 0f, 15f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisBaseDeg);
            if (lordosisBase != null)
            {
                lordosisBase.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisBase.title.tooltip"));
            }

            var lordosisNeckShare = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisNeckShare.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisNeckShare);
            if (lordosisNeckShare != null)
            {
                lordosisNeckShare.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisNeckShare.title.tooltip"));
            }

            var lordosisMaxHeadPitch = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisMaxHeadPitch.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisMaxHeadPitchDeg);
            if (lordosisMaxHeadPitch != null)
            {
                lordosisMaxHeadPitch.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisMaxHeadPitch.title.tooltip"));
            }

            var lordosisExtremeStart = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeStart.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeStartDeg);
            if (lordosisExtremeStart != null)
            {
                lordosisExtremeStart.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeStart.title.tooltip"));
            }

            var lordosisExtremeFull = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeFull.title"), 0f, 90f, false, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeFullDeg);
            if (lordosisExtremeFull != null)
            {
                lordosisExtremeFull.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeFull.title.tooltip"));
            }

            var lordosisExtremeRollFwd = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeRollForward.title"), 0f, 30f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeRollForwardMaxDeg);
            if (lordosisExtremeRollFwd != null)
            {
                lordosisExtremeRollFwd.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeRollForward.title.tooltip"));
            }

            var lordosisExtremeRollBack = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeRollBackward.title"), 0f, 30f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeRollBackwardMaxDeg);
            if (lordosisExtremeRollBack != null)
            {
                lordosisExtremeRollBack.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeRollBackward.title.tooltip"));
            }

            var lordosisExtremeHipsHoriz = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsHoriz.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalMax);
            if (lordosisExtremeHipsHoriz != null)
            {
                lordosisExtremeHipsHoriz.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsHoriz.title.tooltip"));
            }

            var lordosisExtremeChestHoriz = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestHoriz.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalMax);
            if (lordosisExtremeChestHoriz != null)
            {
                lordosisExtremeChestHoriz.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestHoriz.title.tooltip"));
            }

            var lordosisExtremeHipsDown = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsDown.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeHipsDownMax);
            if (lordosisExtremeHipsDown != null)
            {
                lordosisExtremeHipsDown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsDown.title.tooltip"));
            }

            var lordosisExtremeChestDown = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestDown.title"), 0f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeChestDownMax);
            if (lordosisExtremeChestDown != null)
            {
                lordosisExtremeChestDown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestDown.title.tooltip"));
            }

            var lordosisExtremeHipsDownLookUp = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsDownLookUp.title"), 0f, 0.01f, false, 4, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeHipsDownLookUp);
            if (lordosisExtremeHipsDownLookUp != null)
            {
                lordosisExtremeHipsDownLookUp.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeHipsDownLookUp.title.tooltip"));
            }

            var lordosisExtremeChestDownLookUp = PanelSlider.CreateAndBind(
                dynamicsParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestDownLookUp.title"), 0f, 0.01f, false, 4, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKLordosisExtremeChestDownLookUp);
            if (lordosisExtremeChestDownLookUp != null)
            {
                lordosisExtremeChestDownLookUp.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.lordosisExtremeChestDownLookUp.title.tooltip"));
            }
        });

        // ============== Virtual Spine (no torso tracker) ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.virtualSpine.title"),
            BasisLocalization.Get("settings.bodyTracking.section.virtualSpine.description"), false, vspineParent =>
        {
            var vspineChestPitch = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineChestPitchFrac.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineChestPitchFrac);
            if (vspineChestPitch != null)
            {
                vspineChestPitch.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineChestPitchFrac.title.tooltip"));
            }

            var vspineChestRoll = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineChestRollFrac.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineChestRollFrac);
            if (vspineChestRoll != null)
            {
                vspineChestRoll.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineChestRollFrac.title.tooltip"));
            }

            var vspineSpinePitch = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineSpinePitchFrac.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineSpinePitchFrac);
            if (vspineSpinePitch != null)
            {
                vspineSpinePitch.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineSpinePitchFrac.title.tooltip"));
            }

            var vspineSpineRoll = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineSpineRollFrac.title"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineSpineRollFrac);
            if (vspineSpineRoll != null)
            {
                vspineSpineRoll.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineSpineRollFrac.title.tooltip"));
            }

            var vspineNeckRotSpeed = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineNeckRotationSpeed.title"), 0f, 100f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineNeckRotationSpeed);
            if (vspineNeckRotSpeed != null)
            {
                vspineNeckRotSpeed.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineNeckRotationSpeed.title.tooltip"));
            }

            var vspineChestRotSpeed = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineChestRotationSpeed.title"), 0f, 100f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineChestRotationSpeed);
            if (vspineChestRotSpeed != null)
            {
                vspineChestRotSpeed.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineChestRotationSpeed.title.tooltip"));
            }

            var vspineSpineRotSpeed = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineSpineRotationSpeed.title"), 0f, 100f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineSpineRotationSpeed);
            if (vspineSpineRotSpeed != null)
            {
                vspineSpineRotSpeed.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineSpineRotationSpeed.title.tooltip"));
            }

            var vspineHipsRotSpeed = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineHipsRotationSpeed.title"), 0f, 100f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineHipsRotationSpeed);
            if (vspineHipsRotSpeed != null)
            {
                vspineHipsRotSpeed.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineHipsRotationSpeed.title.tooltip"));
            }

            var vspineHipsFwd = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineHipsForwardBias.title"), -0.1f, 0.1f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineHipsForwardBias);
            if (vspineHipsFwd != null)
            {
                vspineHipsFwd.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineHipsForwardBias.title.tooltip"));
            }

            var vspineTorsoYawDeadzone = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawDeadzone.title"), 0f, 90f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineTorsoYawDeadzoneDeg);
            if (vspineTorsoYawDeadzone != null)
            {
                vspineTorsoYawDeadzone.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawDeadzone.title.tooltip"));
            }

            var vspineTorsoYawBlend = PanelSlider.CreateAndBind(
                vspineParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawBlend.title"), 1f, 60f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.VSpineTorsoYawBlendSpeed);
            if (vspineTorsoYawBlend != null)
            {
                vspineTorsoYawBlend.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawBlend.title.tooltip"));
            }

            var vspineTorsoYawPlayInVR = PanelToggle.CreateNewEntry(vspineParent);
            vspineTorsoYawPlayInVR.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawPlayInVR.title"));
            vspineTorsoYawPlayInVR.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.vspineTorsoYawPlayInVR.title.tooltip"));
            vspineTorsoYawPlayInVR.AssignBinding(BasisSettingsDefaults.VSpineTorsoYawPlayInVR);
        });

        // ============== Smoothing (One Euro) ==============
        CreateCollapsibleSection(tabDesc, colliderGroup,
            BasisLocalization.Get("settings.bodyTracking.section.smoothing.title"),
            BasisLocalization.Get("settings.bodyTracking.section.smoothing.description"), false, smoothingParent =>
        {
            var smoothingStrength = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.smoothingStrength.title"), 1f, 100f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKSmoothingStrength);
            if (smoothingStrength != null)
            {
                smoothingStrength.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.smoothingStrength.title.tooltip"));
            }

            var posHz = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.posSmoothingHz.title"), 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKPositionSmoothingHz);
            if (posHz != null)
            {
                posHz.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.posSmoothingHz.title.tooltip"));
            }

            var rotHz = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.rotSmoothingHz.title"), 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKRotationSmoothingHz);
            if (rotHz != null)
            {
                rotHz.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.rotSmoothingHz.title.tooltip"));
            }

            var minCutoff = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.minCutoff.title"), 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMinCutoff);
            if (minCutoff != null)
            {
                minCutoff.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.minCutoff.title.tooltip"));
            }

            var beta = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.beta.title"), 0f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKBeta);
            if (beta != null)
            {
                beta.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.beta.title.tooltip"));
            }

            var derivativeCutoff = PanelSlider.CreateAndBind(
                smoothingParent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.derivativeCutoff.title"), 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKDerivativeCutoff);
            if (derivativeCutoff != null)
            {
                derivativeCutoff.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.derivativeCutoff.title.tooltip"));
            }
        });

        // ONE RESET BUTTON FOR THIS PAGE
        SettingsProvider.AddResetPageButton(tabDesc.ContentParent, "Body Tracking", ResetIkDefaults);


        colliderGroup.gameObject.SetActive(BasisSettingsDefaults.FBIKAdvancedVisible.RawValue);
        advancedToggle.OnValueChanged += visible =>
        {
            colliderGroup.gameObject.SetActive(visible);
            tabDesc.ForceRebuild();
            colliderGroup.GetComponentInParent<PanelElementDescriptor>()?.ForceRebuild();
        };

        // ------------------
        // Debug Section
        // ------------------
        BuildDebugSection(tabDesc);

        tabDesc.ForceRebuild();
        return tabPage;
    }

    public static void SetAvatarScaleSliderValueWithoutNotify(float value)
    {
        if (_avatarScaleSlider == null)
        {
            return;
        }

        _avatarScaleSlider.SetValueWithoutNotify(value);
    }

    // ------------------
    // Debug Info
    // ------------------
    // One card per category. Each card's description holds all of its metrics as
    // "Label: value" lines, so the panel collapses from ~27 group cards to 6.
    private static readonly List<(string title, string[] labels, PanelElementDescriptor descriptor)> _debugCategories = new();

    private static void BuildDebugSection(PanelElementDescriptor tabDesc)
    {
        var debugToggle = PanelToggle.CreateNewEntry(tabDesc.ContentParent);
        debugToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.debugInfo"));
        debugToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.debugInfo.tooltip"));

        var debugGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            tabDesc.ContentParent);

        debugGroup.SetTitle(BasisLocalization.Get("settings.bodyTracking.heightDebug.title"));
        debugGroup.SetIcon(AddressableAssets.Sprites.Settings);

        var debugParent = debugGroup.ContentParent;

        _debugCategories.Clear();

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.playerMetrics"),
            "Player Eye Height", "Player Arm Span", "Eye Height Modifier");

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.avatarMetrics"),
            "Avatar Eye Height", "Avatar Arm Span");

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.scaledHeights"),
            "Scaled Player Height", "Scaled Avatar Height");

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.unscaledHeights"),
            "Unscaled Player Height", "Unscaled Avatar Height");

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.ratiosScaling"),
            "Player to Avatar Ratio", "Avatar to Player Ratio", "Device Scale", "Applied Up Scale", "Scaled to Match Value");

        AddDebugCategory(debugParent, BasisLocalization.Get("settings.bodyTracking.debug.calibrationState"),
            "Height Mode", "Seated Mode", "Seated Height Delta", "Pitch Calibration Enabled", "Has Pitch Calibrated Height", "Pitch Calibrated Eye Height");

        var refreshButton = PanelButton.CreateNew(debugParent);
        refreshButton.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.refreshDebug"));
        refreshButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.refreshDebug.tooltip"));
        refreshButton.OnClicked += RefreshDebugData;

        RefreshDebugData();

        debugGroup.gameObject.SetActive(false);
        debugToggle.SetValueWithoutNotify(false);
        debugToggle.OnValueChanged += visible =>
        {
            debugGroup.gameObject.SetActive(visible);
            if (visible)
            {
                RefreshDebugData();
            }
            tabDesc.ForceRebuild();
        };
    }

    private static void AddDebugCategory(RectTransform parent, string title, params string[] labels)
    {
        var card = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);
        card.SetTitle(title);
        card.SetDescription("");
        _debugCategories.Add((title, labels, card));
    }

    private static void RefreshDebugData()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (title, labels, descriptor) in _debugCategories)
        {
            sb.Clear();
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(labels[i]).Append(": ").Append(GetDebugMetric(labels[i]));
            }
            descriptor.SetDescription(sb.ToString());
        }
    }

    private static string GetDebugMetric(string label) => label switch
    {
        "Player Eye Height" => $"{BasisHeightDriver.PlayerEyeHeight:F4} m",
        "Player Arm Span" => $"{BasisHeightDriver.PlayerArmSpan:F4} m",
        "Eye Height Modifier" => $"{Basis.BasisUI.BasisSettingsDefaults.CalibrationStandingEyeHeightMeters.RawValue:F4} m",
        "Avatar Eye Height" => $"{BasisHeightDriver.AvatarEyeHeight:F4} m",
        "Avatar Arm Span" => $"{BasisHeightDriver.AvatarArmSpan:F4} m",
        "Scaled Player Height" => $"{BasisHeightDriver.SelectedScaledPlayerHeight:F4} m",
        "Scaled Avatar Height" => $"{BasisHeightDriver.SelectedScaledAvatarHeight:F4} m",
        "Unscaled Player Height" => $"{BasisHeightDriver.SelectedUnScaledPlayerHeight:F4} m",
        "Unscaled Avatar Height" => $"{BasisHeightDriver.SelectedUnScaledAvatarHeight:F4} m",
        "Player to Avatar Ratio" => $"{BasisHeightDriver.PlayerToAvatarRatioScaled:F4}",
        "Avatar to Player Ratio" => $"{BasisHeightDriver.AvatarToPlayerRatioScaled:F4}",
        "Device Scale" => $"{BasisHeightDriver.DeviceScale:F4}",
        "Applied Up Scale" => $"{BasisHeightDriver.AppliedUpScale:F4}",
        "Scaled to Match Value" => $"{BasisHeightDriver.ScaledToMatchValue:F4}",
        "Height Mode" => $"{SMModuleCalibration.HeightMode}",
        "Seated Mode" => SMModuleSitStand.IsSteatedMode ? "Seated" : "Standing",
        "Seated Height Delta" => $"{SMModuleSitStand.MissingHeightDelta:F4} m",
        "Pitch Calibration Enabled" => SMModuleCalibration.PitchCalibrationEnabled ? "Yes" : "No",
        "Has Pitch Calibrated Height" => BasisHeightDriver.HasPitchCalibratedHeight ? "Yes" : "No",
        "Pitch Calibrated Eye Height" => $"{BasisHeightDriver.PitchCalibratedEyeHeight:F4} m",
        _ => "--"
    };

    private static void ResetIkDefaults()
    {
        // Main IK / calibration controls
        BasisSettingsDefaults.SitStand.ResetToDefault();
        BasisSettingsDefaults.IKMode.ResetToDefault();
        BasisSettingsDefaults.IKLockMode.ResetToDefault();
        BasisSettingsDefaults.CustomScale.ResetToDefault();
        BasisSettingsDefaults.SelectedScale.ResetToDefault();

        // Playspace Mover
        BasisSettingsDefaults.EnablePlayspaceMover.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverInput.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverRotateInput.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverHand.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverRotate.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverScale.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverVertical.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverFlip.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverFlipAngle.ResetToDefault();
        BasisSettingsDefaults.PlayspaceMoverFlipAxis.ResetToDefault();

        // Global One Euro / smoothing parameters
        BasisSettingsDefaults.FBIKSmoothingStrength.ResetToDefault();
        BasisSettingsDefaults.FBIKMinCutoff.ResetToDefault();
        BasisSettingsDefaults.FBIKBeta.ResetToDefault();
        BasisSettingsDefaults.FBIKDerivativeCutoff.ResetToDefault();
        BasisSettingsDefaults.FBIKPositionSmoothingHz.ResetToDefault();
        BasisSettingsDefaults.FBIKRotationSmoothingHz.ResetToDefault();

        // Bone selection UI state (optional, but usually desired)
        BasisSettingsDefaults.SelectedBone.ResetToDefault();

        // If you have master toggles / global helpers:
        // This binding is set by SyncMasterEuroFromChildren(), but reset it anyway.
        BasisSettingsDefaults.FBIKEuroAll.ResetToDefault();

        // IK Collider & Tuning
        BasisSettingsDefaults.FBIKAdvancedVisible.ResetToDefault();
        BasisSettingsDefaults.FBIKCollisionsEnabled.ResetToDefault();
        BasisSettingsDefaults.FootIKEnabled.ResetToDefault();
        BasisSettingsDefaults.DisableAnimationsInFBT.ResetToDefault();
        BasisSettingsDefaults.FBIKProtectElbow.ResetToDefault();
        BasisSettingsDefaults.FBIKCollideTrackedElbow.ResetToDefault();
        BasisSettingsDefaults.FBIKUseHandCapsule.ResetToDefault();
        BasisSettingsDefaults.FBIKChestRadius.ResetToDefault();
        BasisSettingsDefaults.FBIKCollisionSkin.ResetToDefault();
        BasisSettingsDefaults.FBIKHandRadius.ResetToDefault();
        BasisSettingsDefaults.FBIKHandSkin.ResetToDefault();
        BasisSettingsDefaults.FBIKShoulderSolveEnabled.ResetToDefault();
        BasisSettingsDefaults.FBIKShoulderElevation.ResetToDefault();
        BasisSettingsDefaults.FBIKShoulderProtraction.ResetToDefault();
        BasisSettingsDefaults.FBIKMaxBendDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKStruggleStart.ResetToDefault();
        BasisSettingsDefaults.FBIKStruggleEnd.ResetToDefault();
        BasisSettingsDefaults.FBIKMaxChestDelta.ResetToDefault();
        BasisSettingsDefaults.FBIKMaxHipDelta.ResetToDefault();
        BasisSettingsDefaults.FBIKButterflyKnees.ResetToDefault();
        BasisSettingsDefaults.FBIKButterflyKneeMaxOpenDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineBendPitch.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineBendYaw.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineBendRoll.ResetToDefault();
        BasisSettingsDefaults.FBIKUpperChestBendPitch.ResetToDefault();
        BasisSettingsDefaults.FBIKUpperChestBendYaw.ResetToDefault();
        BasisSettingsDefaults.FBIKUpperChestBendRoll.ResetToDefault();
        BasisSettingsDefaults.FBIKHipHingeStartDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKHipHingeMaxAddDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching.ResetToDefault();
        BasisSettingsDefaults.FBIKSwingSmoothRate.ResetToDefault();
        BasisSettingsDefaults.FBIKElbowSwingEnabled.ResetToDefault();
        BasisSettingsDefaults.FBIKChestSpringHz.ResetToDefault();
        BasisSettingsDefaults.FBIKChestSpringDamping.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisPitchGainDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisBaseDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisNeckShare.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisMaxHeadPitchDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeStartDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeFullDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeRollForwardMaxDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeRollBackwardMaxDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalMax.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalMax.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeHipsDownMax.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeChestDownMax.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeHipsDownLookUp.ResetToDefault();
        BasisSettingsDefaults.FBIKLordosisExtremeChestDownLookUp.ResetToDefault();
        BasisSettingsDefaults.VSpineChestPitchFrac.ResetToDefault();
        BasisSettingsDefaults.VSpineChestRollFrac.ResetToDefault();
        BasisSettingsDefaults.VSpineSpinePitchFrac.ResetToDefault();
        BasisSettingsDefaults.VSpineSpineRollFrac.ResetToDefault();
        BasisSettingsDefaults.VSpineNeckRotationSpeed.ResetToDefault();
        BasisSettingsDefaults.VSpineChestRotationSpeed.ResetToDefault();
        BasisSettingsDefaults.VSpineSpineRotationSpeed.ResetToDefault();
        BasisSettingsDefaults.VSpineHipsRotationSpeed.ResetToDefault();
        BasisSettingsDefaults.VSpineHipsForwardBias.ResetToDefault();
        BasisSettingsDefaults.VSpineTorsoYawDeadzoneDeg.ResetToDefault();
        BasisSettingsDefaults.VSpineTorsoYawBlendSpeed.ResetToDefault();
        BasisSettingsDefaults.VSpineTorsoYawPlayInVR.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineMaxForwardDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineMaxBackwardDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineMaxLateralDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineSquishBoost.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineCCDRelax.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineTwistKeep.ResetToDefault();
        BasisSettingsDefaults.FBIKSpineNeckTwistKeep.ResetToDefault();
        BasisSettingsDefaults.FBIKNeckMaxConeDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKChestArmSwingFactor.ResetToDefault();
        BasisSettingsDefaults.FBIKChestArmSwingMaxDeg.ResetToDefault();
        BasisSettingsDefaults.FBIKLowerArmTwistFraction.ResetToDefault();
        BasisSettingsDefaults.FBIKUpperArmTwistFraction.ResetToDefault();
        BasisSettingsDefaults.FBIKAnatDifferentialStiffness.ResetToDefault();
        BasisSettingsDefaults.FBIKAnatShoulderSlide.ResetToDefault();
        BasisSettingsDefaults.FBIKAnatCervicalLordosis.ResetToDefault();
        BasisSettingsDefaults.FBIKAnatPelvicTwistRouting.ResetToDefault();
        BasisSettingsDefaults.FBIKLegSwivelSmoothing.ResetToDefault();
        BasisSettingsDefaults.FBIKTrackerBendNormal.ResetToDefault();

        // Per-bone toggles and calibration sphere scale
        foreach (var b in _bones)
        {
            b.UseCalibration?.ResetToDefault();
            b.SmoothPos.ResetToDefault();
            b.SmoothRot.ResetToDefault();
            b.EuroPos.ResetToDefault();
            b.EuroRot.ResetToDefault();
            b.CalibSphereScale?.ResetToDefault();
        }

        // Refresh the editor bindings + derived master state
        RebindBoneEditor();
        SyncMasterEuroFromChildren();
    }

    private static void AddFBIKTogglesCompact(RectTransform parent)
    {
        var blocks = new (string name,
            BasisSettingsBinding<bool> useCalibration,
            BasisSettingsBinding<bool> smoothPos,
            BasisSettingsBinding<bool> smoothRot,
            BasisSettingsBinding<bool> euroPos,
            BasisSettingsBinding<bool> euroRot,
            BasisSettingsBinding<float> calibSphereScale)[]
        {
            ("Hips", BasisSettingsDefaults.FBIKHipsUseCalibration, BasisSettingsDefaults.FBIKHipsSmoothPos, BasisSettingsDefaults.FBIKHipsSmoothRot, BasisSettingsDefaults.FBIKHipsEuroPos, BasisSettingsDefaults.FBIKHipsEuroRot, BasisSettingsDefaults.CalibSphereScaleHips),
            ("Head", BasisSettingsDefaults.FBIKHeadUseCalibration, BasisSettingsDefaults.FBIKHeadSmoothPos, BasisSettingsDefaults.FBIKHeadSmoothRot, BasisSettingsDefaults.FBIKHeadEuroPos, BasisSettingsDefaults.FBIKHeadEuroRot, null),
            ("Left Foot", BasisSettingsDefaults.FBIKLeftFootUseCalibration, BasisSettingsDefaults.FBIKLeftFootSmoothPos, BasisSettingsDefaults.FBIKLeftFootSmoothRot, BasisSettingsDefaults.FBIKLeftFootEuroPos, BasisSettingsDefaults.FBIKLeftFootEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftFoot),
            ("Right Foot", BasisSettingsDefaults.FBIKRightFootUseCalibration, BasisSettingsDefaults.FBIKRightFootSmoothPos, BasisSettingsDefaults.FBIKRightFootSmoothRot, BasisSettingsDefaults.FBIKRightFootEuroPos, BasisSettingsDefaults.FBIKRightFootEuroRot, BasisSettingsDefaults.CalibSphereScaleRightFoot),
            ("Chest", BasisSettingsDefaults.FBIKChestUseCalibration, BasisSettingsDefaults.FBIKChestSmoothPos, BasisSettingsDefaults.FBIKChestSmoothRot, BasisSettingsDefaults.FBIKChestEuroPos, BasisSettingsDefaults.FBIKChestEuroRot, BasisSettingsDefaults.CalibSphereScaleChest),
            ("Left Lower Leg", BasisSettingsDefaults.FBIKLeftLowerLegUseCalibration, BasisSettingsDefaults.FBIKLeftLowerLegSmoothPos, BasisSettingsDefaults.FBIKLeftLowerLegSmoothRot, BasisSettingsDefaults.FBIKLeftLowerLegEuroPos, BasisSettingsDefaults.FBIKLeftLowerLegEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftLowerLeg),
            ("Right Lower Leg", BasisSettingsDefaults.FBIKRightLowerLegUseCalibration, BasisSettingsDefaults.FBIKRightLowerLegSmoothPos, BasisSettingsDefaults.FBIKRightLowerLegSmoothRot, BasisSettingsDefaults.FBIKRightLowerLegEuroPos, BasisSettingsDefaults.FBIKRightLowerLegEuroRot, BasisSettingsDefaults.CalibSphereScaleRightLowerLeg),
            ("Left Hand", BasisSettingsDefaults.FBIKLeftHandUseCalibration, BasisSettingsDefaults.FBIKLeftHandSmoothPos, BasisSettingsDefaults.FBIKLeftHandSmoothRot, BasisSettingsDefaults.FBIKLeftHandEuroPos, BasisSettingsDefaults.FBIKLeftHandEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftHand),
            ("Right Hand", BasisSettingsDefaults.FBIKRightHandUseCalibration, BasisSettingsDefaults.FBIKRightHandSmoothPos, BasisSettingsDefaults.FBIKRightHandSmoothRot, BasisSettingsDefaults.FBIKRightHandEuroPos, BasisSettingsDefaults.FBIKRightHandEuroRot, BasisSettingsDefaults.CalibSphereScaleRightHand),
            ("Left Lower Arm", BasisSettingsDefaults.FBIKLeftLowerArmUseCalibration, BasisSettingsDefaults.FBIKLeftLowerArmSmoothPos, BasisSettingsDefaults.FBIKLeftLowerArmSmoothRot, BasisSettingsDefaults.FBIKLeftLowerArmEuroPos, BasisSettingsDefaults.FBIKLeftLowerArmEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftLowerArm),
            ("Right Lower Arm", BasisSettingsDefaults.FBIKRightLowerArmUseCalibration, BasisSettingsDefaults.FBIKRightLowerArmSmoothPos, BasisSettingsDefaults.FBIKRightLowerArmSmoothRot, BasisSettingsDefaults.FBIKRightLowerArmEuroPos, BasisSettingsDefaults.FBIKRightLowerArmEuroRot, BasisSettingsDefaults.CalibSphereScaleRightLowerArm),
            ("Left Toe", BasisSettingsDefaults.FBIKLeftToeUseCalibration, BasisSettingsDefaults.FBIKLeftToeSmoothPos, BasisSettingsDefaults.FBIKLeftToeSmoothRot, BasisSettingsDefaults.FBIKLeftToeEuroPos, BasisSettingsDefaults.FBIKLeftToeEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftToes),
            ("Right Toe", BasisSettingsDefaults.FBIKRightToeUseCalibration, BasisSettingsDefaults.FBIKRightToeSmoothPos, BasisSettingsDefaults.FBIKRightToeSmoothRot, BasisSettingsDefaults.FBIKRightToeEuroPos, BasisSettingsDefaults.FBIKRightToeEuroRot, BasisSettingsDefaults.CalibSphereScaleRightToes),
            ("Left Shoulder", BasisSettingsDefaults.FBIKLeftShoulderUseCalibration, BasisSettingsDefaults.FBIKLeftShoulderSmoothPos, BasisSettingsDefaults.FBIKLeftShoulderSmoothRot, BasisSettingsDefaults.FBIKLeftShoulderEuroPos, BasisSettingsDefaults.FBIKLeftShoulderEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftShoulder),
            ("Right Shoulder", BasisSettingsDefaults.FBIKRightShoulderUseCalibration, BasisSettingsDefaults.FBIKRightShoulderSmoothPos, BasisSettingsDefaults.FBIKRightShoulderSmoothRot, BasisSettingsDefaults.FBIKRightShoulderEuroPos, BasisSettingsDefaults.FBIKRightShoulderEuroRot, BasisSettingsDefaults.CalibSphereScaleRightShoulder),
        };

        _bones.Clear();
        foreach (var b in blocks)
        {
            _bones.Add(new BoneBindings
            {
                Name = b.name,
                UseCalibration = b.useCalibration,
                SmoothPos = b.smoothPos,
                SmoothRot = b.smoothRot,
                EuroPos = b.euroPos,
                EuroRot = b.euroRot,
                CalibSphereScale = b.calibSphereScale
            });
        }

        var boneSelectGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);
        boneSelectGroup.SetTitle(BasisLocalization.Get("settings.ik.title.perBoneSettings"));
        boneSelectGroup.SetDescription(
            "Pick a bone to inspect or tune. The toggles and sliders below apply only " +
            "to the bone you select here — switch bones to see each one's settings."
        );

        var boneNames = _bones.Select(b => b.Name).ToList();
        _boneDropdown = PanelDropdown.CreateNewEntry(boneSelectGroup.ContentParent);
        _boneDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.bone"));
        _boneDropdown.AssignEntries(boneNames);
        _boneDropdown.AssignBinding(BasisSettingsDefaults.SelectedBone);
        _boneDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.bone.tooltip"));
        _boneDropdown.OnValueChanged += _ => RebindBoneEditor();

        _boneEuroEditorGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);
        _boneEuroEditorGroup.SetTitle(BasisLocalization.Get("settings.ik.title.calibrationSmoothing"));
        _boneEuroEditorGroup.SetDescription(
            "Controls for the selected bone. Use For Calibration decides whether trackers " +
            "can be assigned to this role during full-body calibration; the smoothing and " +
            "Euro filter toggles below shape how the bone reacts to incoming motion."
        );

        _uiUseCalibration = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiUseCalibration.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.useForCalibration"));
        _uiUseCalibration.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.useForCalibration.tooltip"));

        _uiSmoothPos = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiSmoothPos.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.smoothPosition"));
        _uiSmoothPos.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.smoothPosition.tooltip"));

        _uiSmoothRot = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiSmoothRot.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.smoothRotation"));
        _uiSmoothRot.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.smoothRotation.tooltip"));

        _uiEuroPos = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroPos.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.euroFilteringPosition"));
        _uiEuroPos.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.euroFilteringPosition.tooltip"));

        _uiEuroRot = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroRot.Descriptor.SetTitle(BasisLocalization.Get("settings.ik.title.euroFilteringRotation"));
        _uiEuroRot.Descriptor.SetTooltip(BasisLocalization.Get("settings.ik.title.euroFilteringRotation.tooltip"));

        _uiCalibSphereScale = PanelSlider.CreateAndBind(
            _boneEuroEditorGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.calibSphereScale"), 0.1f, 5f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.CalibSphereScaleHips);

        if (_uiCalibSphereScale != null)
        {
            _uiCalibSphereScale.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.calibSphereScale.tooltip"));
        }

        RebindBoneEditor();
    }

    private static void RebindBoneEditor()
    {
        if (_boneDropdown == null || _bones.Count == 0)
            return;

        int index = Mathf.Clamp(_boneDropdown.DropdownComponent.value, 0, _bones.Count - 1);
        var bone = _bones[index];

        if (_uiUseCalibration != null && bone.UseCalibration != null)
        {
            _uiUseCalibration.AssignBinding(bone.UseCalibration);
        }

        _uiSmoothPos.AssignBinding(bone.SmoothPos);
        _uiSmoothRot.AssignBinding(bone.SmoothRot);
        _uiEuroPos.AssignBinding(bone.EuroPos);
        _uiEuroRot.AssignBinding(bone.EuroRot);

        bool hasCalibSphere = bone.CalibSphereScale != null;
        if (_uiCalibSphereScale != null)
        {
            _uiCalibSphereScale.gameObject.SetActive(hasCalibSphere);
            if (hasCalibSphere)
            {
                _uiCalibSphereScale.AssignBinding(bone.CalibSphereScale);
            }
        }

        _boneEuroEditorGroup.ForceRebuild();

        SyncMasterEuroFromChildren();
    }

    private static void SyncMasterEuroFromChildren()
    {
        if (_bones.Count == 0)
            return;

        bool allOn = _bones.All(b => b.EuroPos.RawValue && b.EuroRot.RawValue);
        BasisSettingsDefaults.FBIKEuroAll.SetValue(allOn);
    }

    private static void AddAnatomyToggle(RectTransform parent, BasisSettingsBinding<bool> binding, string titleKey, string descriptionKey)
    {
        var toggle = PanelToggle.CreateNewEntry(parent);
        toggle.Descriptor.SetTitle(BasisLocalization.Get(titleKey));
        // Explanatory text on hover (tooltip) instead of inline, to keep the page compact.
        toggle.Descriptor.SetTooltip(BasisLocalization.Get(descriptionKey));
        toggle.AssignBinding(binding);
    }

    private static void CreateCollapsibleSection(PanelElementDescriptor tabDesc, PanelElementDescriptor parentGroup, string title, string description, bool defaultOpen, Action<RectTransform> addContent)
    {
        var parent = parentGroup.ContentParent;

        var sectionToggle = PanelToggle.CreateNewEntry(parent);
        sectionToggle.Descriptor.SetTitle(title);
        // Section blurb on hover (tooltip) instead of inline, to keep the page compact.
        sectionToggle.Descriptor.SetTooltip(description);

        var sectionGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);
        sectionGroup.SetTitle(title);
        sectionGroup.SetIcon(AddressableAssets.Sprites.Settings);

        // Add content while the group is still active so child component Awake/Start runs and
        // their text initializes. SetActive(false) before attach would orphan their lifecycle.
        addContent(sectionGroup.ContentParent);

        sectionGroup.gameObject.SetActive(defaultOpen);
        sectionToggle.SetValueWithoutNotify(defaultOpen);

        sectionToggle.OnValueChanged += visible =>
        {
            sectionGroup.gameObject.SetActive(visible);
            tabDesc.ForceRebuild();
            parentGroup.ForceRebuild();
        };
    }

}
