using Basis;
using Basis.BasisUI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Basis.BasisUI.SettingsProvider;

public static class SettingsProviderIK
{
    private static PanelDropdown dropdownIKMode;
    private static PanelDropdown dropdownSeatedMode;

    private const string SeatedMode_Seated = "Seated Mode";

    private static readonly List<PanelToggle> _euroToggleUIs = new();
    private static readonly List<PanelToggle> _trackerLerpToggleUIs = new();

    private static PanelDropdown _boneDropdown;

    private struct BoneUIGroup
    {
        public string Name;
        public RectTransform RootToHide; // what we toggle active
        public PanelToggle SmoothPos;
        public PanelToggle SmoothRot;
        public PanelToggle EuroPos;
        public PanelToggle EuroRot;
    }

    // ------------------
    // IK & Input
    // ------------------
    public static PanelTabPage IKTab(PanelTabGroup tabGroup)
    {
        var tab = new BasisTabBuilder(tabGroup, "IK Tab", AddressableAssets.Sprites.Settings);

        var ik = tab.Group(
            "Calibration & IK",
            "Fine tuning for calibration, scaling, and IK filtering.",
            AddressableAssets.Sprites.Settings);

        dropdownSeatedMode = ik.DropdownInt(
            "Seated Mode",
            new List<string> { "Standing Mode", SeatedMode_Seated },
            BasisSettingsDefaults.SeatedMode);

        // Tooltip / help text
        dropdownSeatedMode.Descriptor.SetDescription(
            "Select Mode to Reference for Human Scaling"
        );

        dropdownIKMode = ik.DropdownInt(
            "Full Body IK Mode",
            new List<string> { "Eye Height", "Arm Distance" },
            BasisSettingsDefaults.IKMode);

        dropdownIKMode.Descriptor.SetDescription(
            "headset height or arm reach."
        );

        var customScaleToggle = ik.Toggle("Custom Scale", BasisSettingsDefaults.CustomScale);

        // If your Slider builder returns a PanelSlider, you can set descriptions too.
        // If it does NOT return anything in your API, leave as-is or add a returning overload later.
        var avatarScaleSlider = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Avatar Height Scale", 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
            BasisSettingsDefaults.SelectedScale);

        dropdownIKMode.OnValueChanged += _ => EvaluateInteractables();
        dropdownSeatedMode.OnValueChanged += _ => EvaluateInteractables();
        EvaluateInteractables();

        // One-Euro params (global)
        var minCutoff = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Min Cutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKMinCutoff);
        if (minCutoff != null)
            minCutoff.Descriptor.SetDescription("smoothing when barely moving.");

        var beta = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Beta", 0f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKBeta);
        if (beta != null)
            beta.Descriptor.SetDescription("How Quickly We Catchup");

        var derivativeCutoff = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Derivative Cutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKDerivativeCutoff);
        if (derivativeCutoff != null)
            derivativeCutoff.Descriptor.SetDescription("Lower = steadier");

        var posHz = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Position Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKPositionSmoothingHz);
        if (posHz != null)
            posHz.Descriptor.SetDescription("Lower Hz = more smoothing/lag,");

        var rotHz = ik.Slider(
            PanelSlider.SliderSettings.Advanced("Rotation Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
            BasisSettingsDefaults.FBIKRotationSmoothingHz);
        if (rotHz != null)
            rotHz.Descriptor.SetDescription("Lower Hz reduces wobble");

        // Clear lists each time tab is built (prevents duplicates on rebuild)
        _trackerLerpToggleUIs.Clear();
        _euroToggleUIs.Clear();

        AddFBIKTogglesCompact(ik);

        SyncMasterEuroFromChildren();

        tab.Descriptor.ForceRebuild();
        return tab.Tab;
    }

    private static PanelToggle _uiSmoothPos;
    private static PanelToggle _uiSmoothRot;
    private static PanelToggle _uiEuroPos;
    private static PanelToggle _uiEuroRot;
    private static PanelElementDescriptor _boneEditorGroup;
    private static PanelElementDescriptor _boneEuroEditorGroup;

    private struct BoneBindings
    {
        public string Name;
        public BasisSettingsBinding<bool> SmoothPos;
        public BasisSettingsBinding<bool> SmoothRot;
        public BasisSettingsBinding<bool> EuroPos;
        public BasisSettingsBinding<bool> EuroRot;
    }

    private static readonly List<BoneBindings> _bones = new();

    private static void AddFBIKTogglesCompact(BasisGroupBuilder g)
    {
        var blocks = new (string name,
            BasisSettingsBinding<bool> smoothPos,
            BasisSettingsBinding<bool> smoothRot,
            BasisSettingsBinding<bool> euroPos,
            BasisSettingsBinding<bool> euroRot)[]
        {
            ("Hips", BasisSettingsDefaults.FBIKHipsSmoothPos, BasisSettingsDefaults.FBIKHipsSmoothRot, BasisSettingsDefaults.FBIKHipsEuroPos, BasisSettingsDefaults.FBIKHipsEuroRot),
            ("Head", BasisSettingsDefaults.FBIKHeadSmoothPos, BasisSettingsDefaults.FBIKHeadSmoothRot, BasisSettingsDefaults.FBIKHeadEuroPos, BasisSettingsDefaults.FBIKHeadEuroRot),
            ("Left Foot", BasisSettingsDefaults.FBIKLeftFootSmoothPos, BasisSettingsDefaults.FBIKLeftFootSmoothRot, BasisSettingsDefaults.FBIKLeftFootEuroPos, BasisSettingsDefaults.FBIKLeftFootEuroRot),
            ("Right Foot", BasisSettingsDefaults.FBIKRightFootSmoothPos, BasisSettingsDefaults.FBIKRightFootSmoothRot, BasisSettingsDefaults.FBIKRightFootEuroPos, BasisSettingsDefaults.FBIKRightFootEuroRot),
            ("Chest", BasisSettingsDefaults.FBIKChestSmoothPos, BasisSettingsDefaults.FBIKChestSmoothRot, BasisSettingsDefaults.FBIKChestEuroPos, BasisSettingsDefaults.FBIKChestEuroRot),
            ("Left Lower Leg", BasisSettingsDefaults.FBIKLeftLowerLegSmoothPos, BasisSettingsDefaults.FBIKLeftLowerLegSmoothRot, BasisSettingsDefaults.FBIKLeftLowerLegEuroPos, BasisSettingsDefaults.FBIKLeftLowerLegEuroRot),
            ("Right Lower Leg", BasisSettingsDefaults.FBIKRightLowerLegSmoothPos, BasisSettingsDefaults.FBIKRightLowerLegSmoothRot, BasisSettingsDefaults.FBIKRightLowerLegEuroPos, BasisSettingsDefaults.FBIKRightLowerLegEuroRot),
            ("Left Hand", BasisSettingsDefaults.FBIKLeftHandSmoothPos, BasisSettingsDefaults.FBIKLeftHandSmoothRot, BasisSettingsDefaults.FBIKLeftHandEuroPos, BasisSettingsDefaults.FBIKLeftHandEuroRot),
            ("Right Hand", BasisSettingsDefaults.FBIKRightHandSmoothPos, BasisSettingsDefaults.FBIKRightHandSmoothRot, BasisSettingsDefaults.FBIKRightHandEuroPos, BasisSettingsDefaults.FBIKRightHandEuroRot),
            ("Left Lower Arm", BasisSettingsDefaults.FBIKLeftLowerArmSmoothPos, BasisSettingsDefaults.FBIKLeftLowerArmSmoothRot, BasisSettingsDefaults.FBIKLeftLowerArmEuroPos, BasisSettingsDefaults.FBIKLeftLowerArmEuroRot),
            ("Right Lower Arm", BasisSettingsDefaults.FBIKRightLowerArmSmoothPos, BasisSettingsDefaults.FBIKRightLowerArmSmoothRot, BasisSettingsDefaults.FBIKRightLowerArmEuroPos, BasisSettingsDefaults.FBIKRightLowerArmEuroRot),
            ("Left Toe", BasisSettingsDefaults.FBIKLeftToeSmoothPos, BasisSettingsDefaults.FBIKLeftToeSmoothRot, BasisSettingsDefaults.FBIKLeftToeEuroPos, BasisSettingsDefaults.FBIKLeftToeEuroRot),
            ("Right Toe", BasisSettingsDefaults.FBIKRightToeSmoothPos, BasisSettingsDefaults.FBIKRightToeSmoothRot, BasisSettingsDefaults.FBIKRightToeEuroPos, BasisSettingsDefaults.FBIKRightToeEuroRot),
            ("Left Shoulder", BasisSettingsDefaults.FBIKLeftShoulderSmoothPos, BasisSettingsDefaults.FBIKLeftShoulderSmoothRot, BasisSettingsDefaults.FBIKLeftShoulderEuroPos, BasisSettingsDefaults.FBIKLeftShoulderEuroRot),
            ("Right Shoulder", BasisSettingsDefaults.FBIKRightShoulderSmoothPos, BasisSettingsDefaults.FBIKRightShoulderSmoothRot, BasisSettingsDefaults.FBIKRightShoulderEuroPos, BasisSettingsDefaults.FBIKRightShoulderEuroRot),
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
                EuroRot = b.euroRot
            });
        }

        var boneNames = _bones.Select(b => b.Name).ToList();

        _boneDropdown = g.DropdownInt(
            "Bone",
            boneNames,
            BasisSettingsDefaults.SelectedBone
        );

        _boneDropdown.OnValueChanged += _ => RebindBoneEditor();

        // LERP smoothing group
        _boneEditorGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            g.Group.ContentParent);

        _boneEditorGroup.SetTitle("Bone Smoothing");
        _boneEditorGroup.SetDescription("Simple smoothing (LERP). Reduces jitter but adds a small delay.");

        // One Euro group
        _boneEuroEditorGroup = PanelElementDescriptor.CreateNew(
            PanelElementDescriptor.ElementStyles.Group,
            g.Group.ContentParent);

        _boneEuroEditorGroup.SetTitle("Bone Filtering (One Euro)");
        _boneEuroEditorGroup.SetDescription("Adaptive smoothing: smooth when still, responsive when moving fast.");

        _uiSmoothPos = PanelToggle.CreateNewEntry(_boneEditorGroup.ContentParent);
        _uiSmoothPos.Descriptor.SetTitle("Smooth Position");
        _uiSmoothPos.Descriptor.SetDescription("Blends bone position over time to reduce jitter (adds mild latency).");

        _uiSmoothRot = PanelToggle.CreateNewEntry(_boneEditorGroup.ContentParent);
        _uiSmoothRot.Descriptor.SetTitle("Smooth Rotation");
        _uiSmoothRot.Descriptor.SetDescription("Blends bone rotation over time to reduce wobble (adds mild latency).");

        _uiEuroPos = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroPos.Descriptor.SetTitle("Euro Filtering (Position)");
        _uiEuroPos.Descriptor.SetDescription("less jitter at rest, less lag in motion.");

        _uiEuroRot = PanelToggle.CreateNewEntry(_boneEuroEditorGroup.ContentParent);
        _uiEuroRot.Descriptor.SetTitle("Euro Filtering (Rotation)");
        _uiEuroRot.Descriptor.SetDescription("reduces micro-wobble while staying responsive.");

        RebindBoneEditor();
    }
    private static void RebindBoneEditor()
    {
        if (_boneDropdown == null || _bones.Count == 0)
            return;

        int index = _boneDropdown.DropdownComponent.value;
        index = Mathf.Clamp(index, 0, _bones.Count - 1);
        var bone = _bones[index];
        _uiSmoothPos.AssignBinding(bone.SmoothPos);
        _uiSmoothRot.AssignBinding(bone.SmoothRot);
        _uiEuroPos.AssignBinding(bone.EuroPos);
        _uiEuroRot.AssignBinding(bone.EuroRot);

        SyncMasterEuroFromChildren();
    }

    private static void SyncMasterEuroFromChildren()
    {
        if (_bones.Count == 0) return;

        bool allOn = _bones.All(b =>
            b.EuroPos.RawValue &&
            b.EuroRot.RawValue);

        BasisSettingsDefaults.FBIKEuroAll.SetValue(allOn);
    }

    private static void EvaluateInteractables()
    {
        if (dropdownSeatedMode == null || dropdownIKMode == null)
            return;

        string seatedValue = GetCurrentText(dropdownSeatedMode);
        bool isSeated = seatedValue == SeatedMode_Seated;

        SetDropdownInteractable(dropdownIKMode, !isSeated);
    }

    private static string GetCurrentText(PanelDropdown dd) =>
        dd.DropdownComponent.options[dd.DropdownComponent.value].text;

    private static void SetDropdownInteractable(PanelDropdown dd, bool interactable) =>
        dd.DropdownComponent.interactable = interactable;
}
