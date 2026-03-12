using Basis;
using Basis.BasisUI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SettingsProviderIK
{
    private static PanelDropdown dropdownIKMode;
    private static PanelDropdown dropdownIKLockMode;
    private static PanelDropdown dropdownSeatedMode;

    public const string SeatedMode_Seated = "Seated Mode";
    public const string SeatedMode_Standing = "Standing Mode";

    private static readonly List<PanelToggle> _euroToggleUIs = new();
    private static readonly List<PanelToggle> _trackerLerpToggleUIs = new();

    private static PanelDropdown _boneDropdown;

    private static PanelToggle _uiSmoothPos;
    private static PanelToggle _uiSmoothRot;
    private static PanelToggle _uiEuroPos;
    private static PanelToggle _uiEuroRot;
    private static PanelSlider _uiCalibSphereScale;
    private static PanelElementDescriptor _boneEditorGroup;
    private static PanelElementDescriptor _boneEuroEditorGroup;

    private struct BoneBindings
    {
        public string Name;
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
        tabDesc.SetTitle("Body Tracking");
        tabDesc.SetIcon(AddressableAssets.Sprites.Settings);

        // --- Group: "Body Tracking" (replaces tab.Group(...)) ---
        var ikGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            tabDesc.ContentParent);

        ikGroup.SetTitle("Body Tracking");
        ikGroup.SetDescription("Fine-tuning for avatar scaling, calibration, and IK smoothing");
        ikGroup.SetIcon(AddressableAssets.Sprites.Settings);

        var ikParent = ikGroup.ContentParent;

        // --- Seated Mode dropdown ---
        dropdownSeatedMode = PanelDropdown.CreateNewEntry(ikParent);
        dropdownSeatedMode.Descriptor.SetTitle("Seated / Standing Mode");
        dropdownSeatedMode.Descriptor.SetDescription(
            "Select the reference pose used for body scaling"
        );
        dropdownSeatedMode.AssignEntries(new List<string> { SeatedMode_Standing, SeatedMode_Seated });
        dropdownSeatedMode.AssignBinding(BasisSettingsDefaults.SitStand);

        // --- IK mode dropdown ---
        dropdownIKMode = PanelDropdown.CreateNewEntry(ikParent);
        dropdownIKMode.Descriptor.SetTitle("Full Body IK Mode");
        dropdownIKMode.AssignEntries(new List<string> { "Eye Height", "Arm Distance" });
        dropdownIKMode.AssignBinding(BasisSettingsDefaults.IKMode);
        dropdownIKMode.Descriptor.SetDescription(
            "Determines how body scale is calculated."
        );

        // --- IK Lock Mode dropdown ---
        dropdownIKLockMode = PanelDropdown.CreateNewEntry(ikParent);
        dropdownIKLockMode.Descriptor.SetTitle("Spine Lock Mode");
        dropdownIKLockMode.AssignEntries(new List<string> { "Lock Hips", "Lock Head", "Lock Both" });
        dropdownIKLockMode.AssignBinding(BasisSettingsDefaults.IKLockMode);
        dropdownIKLockMode.Descriptor.SetDescription(
            "Lock Hips: Hips are the anchor, Lock Head: Head is the anchor."
        //"Controls how the spine IK chain resolves the relationship between head and hips.\n\n"// +
        //  "Lock Hips: Hips are the anchor. Prevents spine curvature from leg movement. Best for full-body tracking.\n" +
        //  "Lock Head: Head is the anchor. Hips are derived below head. Best for HMD-only or 3-point tracking.\n" +
        //  "Lock Both: Both head and hips are independent. Spine stretches to connect them."
        );

        // --- Custom scale toggle ---
        var customScaleToggle = PanelToggle.CreateNewEntry(ikParent);
        customScaleToggle.Descriptor.SetTitle("Custom Scale");
        customScaleToggle.AssignBinding(BasisSettingsDefaults.CustomScale);
        customScaleToggle.Descriptor.SetDescription("Enables manual override of automatic body scaling.");

        // --- Avatar scale slider ---
        var avatarScaleSlider = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Avatar Height Scale", 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
            BasisSettingsDefaults.SelectedScale);

        if (avatarScaleSlider != null)
        {
            avatarScaleSlider.Descriptor.SetDescription(
                "Manually adjusts avatar height when Custom Scale is enabled. " +
                "This affects perceived size only and does not change tracking accuracy."
            );
        }

        dropdownIKMode.OnValueChanged += _ => EvaluateInteractables();
        dropdownSeatedMode.OnValueChanged += _ => EvaluateInteractables();
        EvaluateInteractables();

        // ------------------
        // One Euro (Global)
        // ------------------
        var smoothingStrength = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Smoothing Strength", 1f, 100f, false, 1, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKSmoothingStrength);

        if (smoothingStrength != null)
        {
            smoothingStrength.Descriptor.SetDescription(
                "Global multiplier for all smoothing filters.\n\n" +
                "1x = default. Higher values greatly increase smoothing but add latency.\n" +
                "WARNING: Values above 10x may cause noticeable input delay."
            );
        }

        var minCutoff = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Min Cutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKMinCutoff);

        if (minCutoff != null)
        {
            minCutoff.Descriptor.SetDescription(
                "Controls smoothing strength when movement is very small.\n\n" +
                "Higher values make the avatar steadier when still, but slower to start moving."
            );
        }

        var beta = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Beta", 0f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKBeta);

        if (beta != null)
        {
            beta.Descriptor.SetDescription(
                "Controls how aggressively smoothing is reduced during fast motion.\n\n" +
                "Higher values reduce lag during quick movement, but may reintroduce jitter."
            );
        }

        var derivativeCutoff = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Derivative Cutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKDerivativeCutoff);

        if (derivativeCutoff != null)
        {
            derivativeCutoff.Descriptor.SetDescription(
                "Controls how much motion speed affects smoothing behavior.\n\n" +
                "Lower values are steadier; higher values feel more responsive but noisier."
            );
        }

        var posHz = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Position Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKPositionSmoothingHz);

        if (posHz != null)
        {
            posHz.Descriptor.SetDescription(
                "Global position smoothing frequency.\n\n" +
                "Lower Hz increases smoothing and latency. Higher Hz feels more immediate but may jitter."
            );
        }

        var rotHz = PanelSlider.CreateAndBind(
            ikParent,
            PanelSlider.SliderSettings.Advanced("Rotation Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKRotationSmoothingHz);

        if (rotHz != null)
        {
            rotHz.Descriptor.SetDescription(
                "Global rotation smoothing frequency.\n\n" +
                "Lower Hz reduces micro-wobble but adds delay. Higher Hz feels snappier but may shimmer."
            );
        }

        _trackerLerpToggleUIs.Clear();
        _euroToggleUIs.Clear();

        AddFBIKTogglesCompact(ikParent);

        SyncMasterEuroFromChildren();
        // ONE RESET BUTTON FOR THIS PAGE
        SettingsProvider.AddResetPageButton(tabDesc.ContentParent, "Body Tracking", ResetIkDefaults);

        tabDesc.ForceRebuild();
        return tabPage;
    }

    private static void ResetIkDefaults()
    {
        // Main IK / calibration controls
        BasisSettingsDefaults.SitStand.ResetToDefault();
        BasisSettingsDefaults.IKMode.ResetToDefault();
        BasisSettingsDefaults.IKLockMode.ResetToDefault();
        BasisSettingsDefaults.CustomScale.ResetToDefault();
        BasisSettingsDefaults.SelectedScale.ResetToDefault();

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

        // Per-bone toggles and calibration sphere scale
        foreach (var b in _bones)
        {
            b.SmoothPos.ResetToDefault();
            b.SmoothRot.ResetToDefault();
            b.EuroPos.ResetToDefault();
            b.EuroRot.ResetToDefault();
            b.CalibSphereScale?.ResetToDefault();
        }

        // Refresh the editor bindings + derived master state + interactables
        RebindBoneEditor();
        EvaluateInteractables();
        SyncMasterEuroFromChildren();
    }

    private static void AddFBIKTogglesCompact(RectTransform parent)
    {
        var blocks = new (string name,
            BasisSettingsBinding<bool> smoothPos,
            BasisSettingsBinding<bool> smoothRot,
            BasisSettingsBinding<bool> euroPos,
            BasisSettingsBinding<bool> euroRot,
            BasisSettingsBinding<float> calibSphereScale)[]
        {
            ("Hips", BasisSettingsDefaults.FBIKHipsSmoothPos, BasisSettingsDefaults.FBIKHipsSmoothRot, BasisSettingsDefaults.FBIKHipsEuroPos, BasisSettingsDefaults.FBIKHipsEuroRot, BasisSettingsDefaults.CalibSphereScaleHips),
            ("Head", BasisSettingsDefaults.FBIKHeadSmoothPos, BasisSettingsDefaults.FBIKHeadSmoothRot, BasisSettingsDefaults.FBIKHeadEuroPos, BasisSettingsDefaults.FBIKHeadEuroRot, null),
            ("Left Foot", BasisSettingsDefaults.FBIKLeftFootSmoothPos, BasisSettingsDefaults.FBIKLeftFootSmoothRot, BasisSettingsDefaults.FBIKLeftFootEuroPos, BasisSettingsDefaults.FBIKLeftFootEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftFoot),
            ("Right Foot", BasisSettingsDefaults.FBIKRightFootSmoothPos, BasisSettingsDefaults.FBIKRightFootSmoothRot, BasisSettingsDefaults.FBIKRightFootEuroPos, BasisSettingsDefaults.FBIKRightFootEuroRot, BasisSettingsDefaults.CalibSphereScaleRightFoot),
            ("Chest", BasisSettingsDefaults.FBIKChestSmoothPos, BasisSettingsDefaults.FBIKChestSmoothRot, BasisSettingsDefaults.FBIKChestEuroPos, BasisSettingsDefaults.FBIKChestEuroRot, BasisSettingsDefaults.CalibSphereScaleChest),
            ("Left Lower Leg", BasisSettingsDefaults.FBIKLeftLowerLegSmoothPos, BasisSettingsDefaults.FBIKLeftLowerLegSmoothRot, BasisSettingsDefaults.FBIKLeftLowerLegEuroPos, BasisSettingsDefaults.FBIKLeftLowerLegEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftLowerLeg),
            ("Right Lower Leg", BasisSettingsDefaults.FBIKRightLowerLegSmoothPos, BasisSettingsDefaults.FBIKRightLowerLegSmoothRot, BasisSettingsDefaults.FBIKRightLowerLegEuroPos, BasisSettingsDefaults.FBIKRightLowerLegEuroRot, BasisSettingsDefaults.CalibSphereScaleRightLowerLeg),
            ("Left Hand", BasisSettingsDefaults.FBIKLeftHandSmoothPos, BasisSettingsDefaults.FBIKLeftHandSmoothRot, BasisSettingsDefaults.FBIKLeftHandEuroPos, BasisSettingsDefaults.FBIKLeftHandEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftHand),
            ("Right Hand", BasisSettingsDefaults.FBIKRightHandSmoothPos, BasisSettingsDefaults.FBIKRightHandSmoothRot, BasisSettingsDefaults.FBIKRightHandEuroPos, BasisSettingsDefaults.FBIKRightHandEuroRot, BasisSettingsDefaults.CalibSphereScaleRightHand),
            ("Left Lower Arm", BasisSettingsDefaults.FBIKLeftLowerArmSmoothPos, BasisSettingsDefaults.FBIKLeftLowerArmSmoothRot, BasisSettingsDefaults.FBIKLeftLowerArmEuroPos, BasisSettingsDefaults.FBIKLeftLowerArmEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftLowerArm),
            ("Right Lower Arm", BasisSettingsDefaults.FBIKRightLowerArmSmoothPos, BasisSettingsDefaults.FBIKRightLowerArmSmoothRot, BasisSettingsDefaults.FBIKRightLowerArmEuroPos, BasisSettingsDefaults.FBIKRightLowerArmEuroRot, BasisSettingsDefaults.CalibSphereScaleRightLowerArm),
            ("Left Toe", BasisSettingsDefaults.FBIKLeftToeSmoothPos, BasisSettingsDefaults.FBIKLeftToeSmoothRot, BasisSettingsDefaults.FBIKLeftToeEuroPos, BasisSettingsDefaults.FBIKLeftToeEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftToes),
            ("Right Toe", BasisSettingsDefaults.FBIKRightToeSmoothPos, BasisSettingsDefaults.FBIKRightToeSmoothRot, BasisSettingsDefaults.FBIKRightToeEuroPos, BasisSettingsDefaults.FBIKRightToeEuroRot, BasisSettingsDefaults.CalibSphereScaleRightToes),
            ("Left Shoulder", BasisSettingsDefaults.FBIKLeftShoulderSmoothPos, BasisSettingsDefaults.FBIKLeftShoulderSmoothRot, BasisSettingsDefaults.FBIKLeftShoulderEuroPos, BasisSettingsDefaults.FBIKLeftShoulderEuroRot, BasisSettingsDefaults.CalibSphereScaleLeftShoulder),
            ("Right Shoulder", BasisSettingsDefaults.FBIKRightShoulderSmoothPos, BasisSettingsDefaults.FBIKRightShoulderSmoothRot, BasisSettingsDefaults.FBIKRightShoulderEuroPos, BasisSettingsDefaults.FBIKRightShoulderEuroRot, BasisSettingsDefaults.CalibSphereScaleRightShoulder),
        };

        _bones.Clear();
        foreach (var b in blocks)
        {
            _bones.Add(new BoneBindings
            {
                Name = b.name,
                SmoothPos = b.smoothPos,
                SmoothRot = b.smoothRot,
                EuroPos = b.euroPos,
                EuroRot = b.euroRot,
                CalibSphereScale = b.calibSphereScale
            });
        }

        var boneNames = _bones.Select(b => b.Name).ToList();
        _boneDropdown = PanelDropdown.CreateNewEntry(parent);
        _boneDropdown.Descriptor.SetTitle("Bone");
        _boneDropdown.AssignEntries(boneNames);
        _boneDropdown.AssignBinding(BasisSettingsDefaults.SelectedBone);
        _boneDropdown.Descriptor.SetDescription("Select which bone’s smoothing and filtering settings are shown below.");
        _boneDropdown.OnValueChanged += _ => RebindBoneEditor();

        _boneEditorGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);

        _boneEditorGroup.SetTitle("Bone Smoothing");
        _boneEditorGroup.SetDescription("Reduces jitter but always adds a small amount of delay.");

        _boneEuroEditorGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            parent);

        _boneEuroEditorGroup.SetTitle("Bone Filtering (One Euro)");
        _boneEuroEditorGroup.SetDescription(
            "Adaptive smoothing that changes based on motion speed. " +
            "Stable when still, responsive during fast movement."
        );

        _uiSmoothPos = PanelToggle.CreateNewEntry(_boneEditorGroup.ContentParent);
        _uiSmoothPos.Descriptor.SetTitle("Smooth Position");
        _uiSmoothPos.Descriptor.SetDescription("Blends this bone’s position over time to reduce jitter.");

        _uiSmoothRot = PanelToggle.CreateNewEntry(_boneEditorGroup.ContentParent);
        _uiSmoothRot.Descriptor.SetTitle("Smooth Rotation");
        _uiSmoothRot.Descriptor.SetDescription("Blends this bone’s rotation over time to reduce wobble.");

        _uiEuroPos = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroPos.Descriptor.SetTitle("Euro Filtering (Position)");
        _uiEuroPos.Descriptor.SetDescription("Steady at rest with minimal lag during motion. ");

        _uiEuroRot = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroRot.Descriptor.SetTitle("Euro Filtering (Rotation)");
        _uiEuroRot.Descriptor.SetDescription("Reduces micro-wobble while remaining responsive.");

        _uiCalibSphereScale = PanelSlider.CreateAndBind(
            _boneEditorGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Calibration Sphere Scale", 0.1f, 5f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.CalibSphereScaleHips);

        if (_uiCalibSphereScale != null)
        {
            _uiCalibSphereScale.Descriptor.SetDescription(
                "Adjusts the calibration sphere size for this bone. " +
                "Larger spheres make it easier for trackers to attach during calibration. " +
                "1.0 = default size."
            );
        }

        RebindBoneEditor();
    }

    private static void RebindBoneEditor()
    {
        if (_boneDropdown == null || _bones.Count == 0)
            return;

        int index = Mathf.Clamp(_boneDropdown.DropdownComponent.value, 0, _bones.Count - 1);
        var bone = _bones[index];

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

        _boneEditorGroup.ForceRebuild();
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

    private static void EvaluateInteractables()
    {
        if (dropdownSeatedMode == null || dropdownIKMode == null)
            return;

        bool isSeated = GetCurrentText(dropdownSeatedMode) == SeatedMode_Seated;
        SetDropdownInteractable(dropdownIKMode, !isSeated);
    }

    private static string GetCurrentText(PanelDropdown dd)
        => dd.DropdownComponent.options[dd.DropdownComponent.value].text;

    private static void SetDropdownInteractable(PanelDropdown dd, bool interactable)
        => dd.DropdownComponent.interactable = interactable;
}
