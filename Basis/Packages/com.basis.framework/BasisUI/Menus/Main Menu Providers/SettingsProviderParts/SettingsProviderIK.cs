using Basis;
using Basis.BasisUI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static Basis.BasisUI.SettingsProvider;

public static class SettingsProviderIK
{
    private static PanelDropdown dropdownIKMode;
    private static PanelDropdown dropdownSeatedMode;

    private const string SeatedMode_Seated = "Seated Mode";

    // Prevent feedback loops when the master toggle is pushing changes into all children
    private static bool _bulkEuroUpdating;

    // Keep references to the *UI toggles* for Euro Pos/Rot so we can flip them all at once
    private static readonly List<PanelToggle> _euroToggleUIs = new();

    // All "tracker lerp" toggles (Smooth Pos / Smooth Rot) go in here so we can hide/show them
    private static readonly List<PanelToggle> _trackerLerpToggleUIs = new();

    // Cache current IK tab descriptor so we can rebuild layout after toggling visibility
    private static PanelElementDescriptor _ikTabDescriptorForRebuild;

    // ------------------
    // IK & Input
    // ------------------
    public static PanelTabPage IKTab(PanelTabGroup tabGroup)
    {
        var tab = new BasisTabBuilder(tabGroup, "IK Tab", AddressableAssets.Sprites.Settings);

        var ik = tab.Group(
            "Calibration & IK",
            "Settings for Fine Tuning Calibration and IK",
            AddressableAssets.Sprites.Settings);

        // Cache descriptor so our static visibility function can rebuild cleanly
        _ikTabDescriptorForRebuild = tab.Descriptor;

        dropdownSeatedMode = ik.DropdownInt(
            "Seated Mode",
            new List<string> { "Standing Mode", SeatedMode_Seated },
            BasisSettingsDefaults.SeatedMode);

        dropdownIKMode = ik.DropdownInt(
            "Full Body IK Mode",
            new List<string> { "Eye Height", "Arm Distance" },
            BasisSettingsDefaults.IKMode);

        ik.Toggle("Custom Scale", BasisSettingsDefaults.CustomScale);

        ik.Slider(
            PanelSlider.SliderSettings.Advanced("Avatar height Scale", 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
            BasisSettingsDefaults.SelectedScale);

        dropdownIKMode.OnValueChanged += _ => EvaluateInteractables();
        dropdownSeatedMode.OnValueChanged += _ => EvaluateInteractables();
        EvaluateInteractables();

        // One-Euro params
        ik.Slider(PanelSlider.SliderSettings.Advanced("MinCutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw), BasisSettingsDefaults.FBIKMinCutoff);
        ik.Slider(PanelSlider.SliderSettings.Advanced("Beta", 0f, 10f, false, 2, ValueDisplayMode.Raw), BasisSettingsDefaults.FBIKBeta);
        ik.Slider(PanelSlider.SliderSettings.Advanced("DerivativeCutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw), BasisSettingsDefaults.FBIKDerivativeCutoff);
        ik.Slider(PanelSlider.SliderSettings.Advanced("Position Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw), BasisSettingsDefaults.FBIKPositionSmoothingHz);
        ik.Slider(PanelSlider.SliderSettings.Advanced("Rotation Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw), BasisSettingsDefaults.FBIKRotationSmoothingHz);

        // Clear lists each time tab is built (prevents duplicates on rebuild)
        _trackerLerpToggleUIs.Clear();
        _euroToggleUIs.Clear();

        // ---- MASTER EURO TOGGLE (ONE TO RULE THEM ALL) ----
        PanelToggle masterEuro = ik.Toggle("Euro Filtering (All Bones)", BasisSettingsDefaults.FBIKEuroAll);

        masterEuro.OnValueChanged += enabled =>
        {
            if (_bulkEuroUpdating) return;

            _bulkEuroUpdating = true;
            try
            {
                foreach (var t in _euroToggleUIs)
                {
                    t.SetValueWithoutNotify(enabled);
                }

                // If your PanelToggle requires notify to write bindings, use SetValue(enabled) instead.
            }
            finally
            {
                _bulkEuroUpdating = false;
            }

            UpdateMasterEuroDescription(masterEuro);
        };
        AddFBIKToggles(ik);
        SyncMasterEuroFromChildren(masterEuro);

        tab.Descriptor.ForceRebuild();
        return tab.Tab;
    }
    private static void AddFBIKToggles(BasisGroupBuilder g)
    {
        var blocks = new (string name,
            BasisSettingsBinding<bool> smoothPos,
            BasisSettingsBinding<bool> smoothRot,
            BasisSettingsBinding<bool> euroPos,
            BasisSettingsBinding<bool> euroRot)[]
        {
                ("Hips",      BasisSettingsDefaults.FBIKHipsSmoothPos,      BasisSettingsDefaults.FBIKHipsSmoothRot,      BasisSettingsDefaults.FBIKHipsEuroPos,      BasisSettingsDefaults.FBIKHipsEuroRot),
                ("Head",      BasisSettingsDefaults.FBIKHeadSmoothPos,      BasisSettingsDefaults.FBIKHeadSmoothRot,      BasisSettingsDefaults.FBIKHeadEuroPos,      BasisSettingsDefaults.FBIKHeadEuroRot),
                ("Left Foot", BasisSettingsDefaults.FBIKLeftFootSmoothPos,  BasisSettingsDefaults.FBIKLeftFootSmoothRot,  BasisSettingsDefaults.FBIKLeftFootEuroPos,  BasisSettingsDefaults.FBIKLeftFootEuroRot),
                ("Right Foot",BasisSettingsDefaults.FBIKRightFootSmoothPos, BasisSettingsDefaults.FBIKRightFootSmoothRot, BasisSettingsDefaults.FBIKRightFootEuroPos, BasisSettingsDefaults.FBIKRightFootEuroRot),
                ("Chest",     BasisSettingsDefaults.FBIKChestSmoothPos,     BasisSettingsDefaults.FBIKChestSmoothRot,     BasisSettingsDefaults.FBIKChestEuroPos,     BasisSettingsDefaults.FBIKChestEuroRot),
                ("Left Lower Leg",  BasisSettingsDefaults.FBIKLeftLowerLegSmoothPos,  BasisSettingsDefaults.FBIKLeftLowerLegSmoothRot,  BasisSettingsDefaults.FBIKLeftLowerLegEuroPos,  BasisSettingsDefaults.FBIKLeftLowerLegEuroRot),
                ("Right Lower Leg", BasisSettingsDefaults.FBIKRightLowerLegSmoothPos, BasisSettingsDefaults.FBIKRightLowerLegSmoothRot, BasisSettingsDefaults.FBIKRightLowerLegEuroPos, BasisSettingsDefaults.FBIKRightLowerLegEuroRot),
                ("Left Hand",  BasisSettingsDefaults.FBIKLeftHandSmoothPos,  BasisSettingsDefaults.FBIKLeftHandSmoothRot,  BasisSettingsDefaults.FBIKLeftHandEuroPos,  BasisSettingsDefaults.FBIKLeftHandEuroRot),
                ("Right Hand", BasisSettingsDefaults.FBIKRightHandSmoothPos, BasisSettingsDefaults.FBIKRightHandSmoothRot, BasisSettingsDefaults.FBIKRightHandEuroPos, BasisSettingsDefaults.FBIKRightHandEuroRot),
                ("Left Lower Arm",  BasisSettingsDefaults.FBIKLeftLowerArmSmoothPos,  BasisSettingsDefaults.FBIKLeftLowerArmSmoothRot,  BasisSettingsDefaults.FBIKLeftLowerArmEuroPos,  BasisSettingsDefaults.FBIKLeftLowerArmEuroRot),
                ("Right Lower Arm", BasisSettingsDefaults.FBIKRightLowerArmSmoothPos, BasisSettingsDefaults.FBIKRightLowerArmSmoothRot, BasisSettingsDefaults.FBIKRightLowerArmEuroPos, BasisSettingsDefaults.FBIKRightLowerArmEuroRot),
                ("Left Toe",   BasisSettingsDefaults.FBIKLeftToeSmoothPos,   BasisSettingsDefaults.FBIKLeftToeSmoothRot,   BasisSettingsDefaults.FBIKLeftToeEuroPos,   BasisSettingsDefaults.FBIKLeftToeEuroRot),
                ("Right Toe",  BasisSettingsDefaults.FBIKRightToeSmoothPos,  BasisSettingsDefaults.FBIKRightToeSmoothRot,  BasisSettingsDefaults.FBIKRightToeEuroPos,  BasisSettingsDefaults.FBIKRightToeEuroRot),
                ("Left Shoulder",  BasisSettingsDefaults.FBIKLeftShoulderSmoothPos,  BasisSettingsDefaults.FBIKLeftShoulderSmoothRot,  BasisSettingsDefaults.FBIKLeftShoulderEuroPos,  BasisSettingsDefaults.FBIKLeftShoulderEuroRot),
                ("Right Shoulder", BasisSettingsDefaults.FBIKRightShoulderSmoothPos, BasisSettingsDefaults.FBIKRightShoulderSmoothRot, BasisSettingsDefaults.FBIKRightShoulderEuroPos, BasisSettingsDefaults.FBIKRightShoulderEuroRot),
        };

        int length = blocks.Length;
        for (int Index = 0; Index < length; Index++)
        {
            (string name, BasisSettingsBinding<bool> smoothPos, BasisSettingsBinding<bool> smoothRot, BasisSettingsBinding<bool> euroPos, BasisSettingsBinding<bool> euroRot) b = blocks[Index];
            // These are your "tracker lerp" toggles — we gate them behind Advanced Lerp Options
            PanelToggle smoothPosToggle = g.Toggle($"{b.name} Smooth Pos", b.smoothPos);
            PanelToggle smoothRotToggle = g.Toggle($"{b.name} Smooth Rot", b.smoothRot);

            _trackerLerpToggleUIs.Add(smoothPosToggle);
            _trackerLerpToggleUIs.Add(smoothRotToggle);

            // Euro toggles (always visible, but controlled by the Euro master)
            PanelToggle euroPosToggle = g.Toggle($"{b.name} Euro Pos", b.euroPos);
            PanelToggle euroRotToggle = g.Toggle($"{b.name} Euro Rot", b.euroRot);

            _euroToggleUIs.Add(euroPosToggle);
            _euroToggleUIs.Add(euroRotToggle);

            euroPosToggle.OnValueChanged += _ => OnAnyEuroChildChanged();
            euroRotToggle.OnValueChanged += _ => OnAnyEuroChildChanged();
        }
    }
    private static void OnAnyEuroChildChanged()
    {
        if (_bulkEuroUpdating) return;

        bool allOn = _euroToggleUIs.Count > 0 && _euroToggleUIs.All(t => t.ToggleComponent.isOn);
        BasisSettingsDefaults.FBIKEuroAll.SetValue(allOn);
    }

    private static void SyncMasterEuroFromChildren(PanelToggle masterEuro)
    {
        if (_euroToggleUIs.Count == 0)
        {
            return;
        }

        bool allOn = _euroToggleUIs.All(t => t.ToggleComponent.isOn);
        bool allOff = _euroToggleUIs.All(t => !t.ToggleComponent.isOn);

        _bulkEuroUpdating = true;
        try
        {
            masterEuro.SetValueWithoutNotify(allOn);
            BasisSettingsDefaults.FBIKEuroAll.SetValue(allOn);
        }
        finally
        {
            _bulkEuroUpdating = false;
        }
    }

    private static void UpdateMasterEuroDescription(PanelToggle masterEuro)
    {
        if (_euroToggleUIs.Count == 0)
        {
            return;
        }

        bool allOn = _euroToggleUIs.All(t => t.ToggleComponent.isOn);
        bool allOff = _euroToggleUIs.All(t => !t.ToggleComponent.isOn);
    }
    private static void EvaluateInteractables()
    {
        if (dropdownSeatedMode == null || dropdownIKMode == null)
        {
            return;
        }

        string seatedValue = GetCurrentText(dropdownSeatedMode);
        bool isSeated = seatedValue == SeatedMode_Seated;

        SetDropdownInteractable(dropdownIKMode, !isSeated);
    }

    private static string GetCurrentText(PanelDropdown dd)
    {
        return dd.DropdownComponent.options[dd.DropdownComponent.value].text;
    }

    private static void SetDropdownInteractable(PanelDropdown dd, bool interactable)
    {
        dd.DropdownComponent.interactable = interactable;
    }
}
