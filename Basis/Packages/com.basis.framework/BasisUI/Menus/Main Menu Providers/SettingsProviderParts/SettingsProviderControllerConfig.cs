using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static BasisActionDriver;

public static class SettingsProviderControllerConfig
{
    public static PanelTabPage OpenControllerConfig(PanelTabGroup tabGroup)
    {
        PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        PanelElementDescriptor descriptor = tab.Descriptor;
        RectTransform container = descriptor.ContentParent;

        // Gameplay & Input (starts expanded)
        PanelSlider sliderSnapTurnAngleRef = null;
        PanelSlider sliderSmoothTurnSpeedRef = null;
        SettingsProviderKeyboardBindings.CreateCollapsibleSection(
            container, BasisLocalization.Get("settings.controls.gameplay.title"), BasisLocalization.Get("settings.controls.gameplay.description"), group =>
        {
            PanelDropdown dropdownDominantHand = PanelDropdown.CreateNewEntry(group);
            dropdownDominantHand.Descriptor.SetTitle(BasisLocalization.Get("settings.controls.dominantHand"));
            dropdownDominantHand.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.dominantHand.tooltip"));
            dropdownDominantHand.AssignEntries(new List<string> { BasisDominantHand.Right, BasisDominantHand.Left });
            dropdownDominantHand.AssignBinding(BasisSettingsDefaults.DominantHand);

            PanelToggle toggleInvertMouse = PanelToggle.CreateNewEntry(group);
            toggleInvertMouse.Descriptor.SetTitle(BasisLocalization.Get("settings.controls.invertMouse"));
            toggleInvertMouse.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.invertMouse.tooltip"));
            toggleInvertMouse.AssignBinding(BasisSettingsDefaults.InvertMouse);

            PanelSlider mousesensitivty = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.mouseSensitivity"), 0, 2f, false, 2, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.mousesensitivty);
            mousesensitivty.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.mouseSensitivity.tooltip"));

            PanelSlider scrollSpeedSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.scrollSpeed"), 20, 300, true, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.ScrollSpeed);
            scrollSpeedSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.scrollSpeed.tooltip"));

            PanelToggle snapturntoggle = PanelToggle.CreateNewEntry(group);
            snapturntoggle.Descriptor.SetTitle(BasisLocalization.Get("settings.controls.snapTurn"));
            snapturntoggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.snapTurn.tooltip"));
            snapturntoggle.AssignBinding(BasisSettingsDefaults.usesnapturn);

            sliderSnapTurnAngleRef = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.snapTurnAngle"), 0, 120, true, 0, ValueDisplayMode.Degrees),
                BasisSettingsDefaults.SnapTurnAngle);
            sliderSnapTurnAngleRef.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.snapTurnAngle.tooltip"));

            sliderSmoothTurnSpeedRef = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.smoothTurnSpeed"), 50, 400, true, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.SmoothTurnSpeed);
            sliderSmoothTurnSpeedRef.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.smoothTurnSpeed.tooltip"));

            snapturntoggle.OnValueChanged += isOn =>
            {
                sliderSnapTurnAngleRef.Descriptor.SetActive(isOn);
                sliderSmoothTurnSpeedRef.Descriptor.SetActive(!isOn);
                group.ForceRebuild();
            };
        }, startExpanded: false);

        // Apply initial visibility AFTER CreateCollapsibleSection's SetContentActive pass,
        // which would otherwise re-activate both sliders when the section starts expanded.
        bool snapOn = BasisSettingsDefaults.usesnapturn.RawValue;
        sliderSnapTurnAngleRef.Descriptor.SetActive(snapOn);
        sliderSmoothTurnSpeedRef.Descriptor.SetActive(!snapOn);

        // Deadzone - General
        SettingsProviderKeyboardBindings.CreateCollapsibleSection(
            container, BasisLocalization.Get("settings.controls.generalDeadzone.title"), BasisLocalization.Get("settings.controls.generalDeadzone.description"), group =>
        {
            PanelSlider controllerDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.radialDeadZone"), 0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.ControllerDeadZone);
            controllerDeadZoneSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.radialDeadZone.tooltip"));
            controllerDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
        });

        // Horizontal Comfort
        SettingsProviderKeyboardBindings.CreateCollapsibleSection(
            container, BasisLocalization.Get("settings.controls.yawComfort.title"),
            BasisLocalization.Get("settings.controls.yawComfort.description"), group =>
        {
            PanelSlider minHorizontalDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.xDeadZoneMin"), 0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Basexdeadzone);
            minHorizontalDeadZoneSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.xDeadZoneMin.tooltip"));

            PanelSlider horizontalGateStrengthSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.xGateFullY"), 0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Extraxdeadzoneatfully);
            horizontalGateStrengthSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.xGateFullY.tooltip"));

            PanelSlider wingCurveSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.gateCurve"), 0f, 3f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Wingexponent);
            wingCurveSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.gateCurve.tooltip"));

            minHorizontalDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
            horizontalGateStrengthSlider.OnValueChanged += _ => UpdatePreview();
            wingCurveSlider.OnValueChanged += _ => UpdatePreview();
        });

        // Vertical
        SettingsProviderKeyboardBindings.CreateCollapsibleSection(
            container, BasisLocalization.Get("settings.controls.pitchComfort.title"), BasisLocalization.Get("settings.controls.pitchComfort.description"), group =>
        {
            PanelSlider verticalDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                group,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.controls.lookYDeadZone"), 0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Ydeadzone);
            verticalDeadZoneSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.lookYDeadZone.tooltip"));
            verticalDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
        });

        // Keyboard Bindings & Remapping
        SettingsProviderKeyboardBindings.BuildKeyboardBindingsUI(tab);

        // Action Bindings
        SettingsProviderKeyboardBindings.CreateCollapsibleSection(
            container, BasisLocalization.Get("settings.controls.actionBindings.title", BasisDeviceManagement.StaticCurrentMode),
            BasisLocalization.Get("settings.controls.actionBindings.description"), group =>
        {
            BuildBindingsUI(group.ContentParent);
        });

        SettingsProvider.AddResetPageButton(container, "settings.tab.controls", () =>
        {
            ResetControlsDefaults();
            BasisActionDriver.ResetBindingsToDefaultsAsyncIgnored();
            if (BasisLocalInputActions.Asset != null)
            {
                SettingsProviderKeyboardBindings.ResetAllBindings(BasisLocalInputActions.Asset);
            }
        });

        descriptor.ForceRebuild();
        return tab;
    }

    private static void UpdatePreview()
    {
        // wire up to butterflygatepreview one day
    }

    private static void ResetControlsDefaults()
    {
        BasisSettingsDefaults.DominantHand.ResetToDefault();
        BasisSettingsDefaults.InvertMouse.ResetToDefault();
        BasisSettingsDefaults.mousesensitivty.ResetToDefault();
        BasisSettingsDefaults.usesnapturn.ResetToDefault();
        BasisSettingsDefaults.SnapTurnAngle.ResetToDefault();
        BasisSettingsDefaults.SmoothTurnSpeed.ResetToDefault();
        BasisSettingsDefaults.ScrollSpeed.ResetToDefault();
        BasisSettingsDefaults.ControllerDeadZone.ResetToDefault();
        BasisSettingsDefaults.Basexdeadzone.ResetToDefault();
        BasisSettingsDefaults.Extraxdeadzoneatfully.ResetToDefault();
        BasisSettingsDefaults.Wingexponent.ResetToDefault();
        BasisSettingsDefaults.Ydeadzone.ResetToDefault();
    }

    private static void BuildBindingsUI(RectTransform container)
    {
        var roles = (BasisBoneTrackedRole[])Enum.GetValues(typeof(BasisBoneTrackedRole));
        var roleNames = roles.Select(r => PrettyEnumName(r.ToString())).ToArray();

        var actions = ((ActionId[])Enum.GetValues(typeof(ActionId)))
            .Where(a => a != ActionId.Count)
            .ToArray();

        var actionNames = actions.Select(a => PrettyEnumName(a.ToString())).ToList();

        var selectorGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        selectorGroup.SetTitle($"Select Action For {BasisDeviceManagement.StaticCurrentMode}");
        selectorGroup.SetDescription("Choose an action to edit its bound roles.");

        PanelDropdown actionDropdown = PanelDropdown.CreateNewEntry(selectorGroup.ContentParent);
        actionDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.controls.action"));
        actionDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.controls.action.tooltip"));
        actionDropdown.AssignEntries(actionNames);

        var rolesGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        rolesGroup.SetTitle(BasisLocalization.Get("settings.controls.roles"));

        var roleToggles = new PanelToggle[roles.Length];

        bool updatingUI = false;
        ActionId currentAction = actions.Length > 0 ? actions[0] : ActionId.Count;

        for (int i = 0; i < roles.Length; i++)
        {
            var role = roles[i];

            PanelToggle t = PanelToggle.CreateNewEntry(rolesGroup.ContentParent);
            t.Descriptor.SetTitle(roleNames[i]);

            t.OnValueChanged += async isOn =>
            {
                if (updatingUI)
                {
                    return;
                }

                if (isOn)
                {
                    BasisActionDriver.Bind(currentAction, role);
                }
                else
                {
                    BasisActionDriver.Unbind(currentAction, role);
                }

                await BasisActionDriver.SaveFromDriver();
            };

            roleToggles[i] = t;
        }

        actionDropdown.DropdownComponent.onValueChanged.AddListener(index =>
        {
            currentAction = actions[Mathf.Clamp(index, 0, actions.Length - 1)];
            RefreshRoleTogglesFromDriver(roles, roleToggles, ref updatingUI, currentAction);
        });

        RefreshRoleTogglesFromDriver(roles, roleToggles, ref updatingUI, currentAction);
    }

    private static void RefreshRoleTogglesFromDriver(
        BasisBoneTrackedRole[] roles,
        PanelToggle[] roleToggles,
        ref bool updatingUI,
        ActionId currentAction)
    {
        updatingUI = true;
        var bound = BasisActionDriver.GetBindings(currentAction);

        for (int i = 0; i < roles.Length; i++)
            roleToggles[i].SetValueWithoutNotify(bound.Contains(roles[i]));

        updatingUI = false;
    }

    private static string PrettyEnumName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) { return raw; }
        var chars = new List<char>(raw.Length + 8);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1]))))
            {
                chars.Add(' ');
            }
            chars.Add(c);
        }
        return new string(chars.ToArray());
    }
}
