using System.Collections.Generic;
using Basis.Scripts.Avatar;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.BasisUI
{
    public static class SettingsProviderDeviceOffsets
    {
        private const string NoneEntry = "none";
        private const string PositionTooltip = "settings.developer.deviceOffsets.position.tooltip";
        private const string RotationTooltip = "settings.developer.deviceOffsets.rotation.tooltip";

        public static void Build(RectTransform container, PanelElementDescriptor descriptor)
        {
            PanelSectionToggle section = PanelSectionToggle.CreateNewEntry(container);
            section.SetTitle(BasisLocalization.Get("settings.developer.group.deviceOffsets"));
            int start = container.childCount;

            PanelDropdown deviceDropdown = PanelDropdown.CreateNewEntry(container);
            deviceDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.deviceOffsets.device"));
            deviceDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.deviceOffsets.device.tooltip"));

            float limit = BasisDeviceOffsetMath.PositionLimit;
            PanelSlider[] position =
            {
                CreateSlider(container, "settings.developer.deviceOffsets.positionX", PositionTooltip, -limit, limit, 3, ValueDisplayMode.Meters),
                CreateSlider(container, "settings.developer.deviceOffsets.positionY", PositionTooltip, -limit, limit, 3, ValueDisplayMode.Meters),
                CreateSlider(container, "settings.developer.deviceOffsets.positionZ", PositionTooltip, -limit, limit, 3, ValueDisplayMode.Meters),
            };
            PanelSlider[] rotation =
            {
                CreateSlider(container, "settings.developer.deviceOffsets.rotationX", RotationTooltip, -180f, 180f, 1, ValueDisplayMode.Degrees),
                CreateSlider(container, "settings.developer.deviceOffsets.rotationY", RotationTooltip, -180f, 180f, 1, ValueDisplayMode.Degrees),
                CreateSlider(container, "settings.developer.deviceOffsets.rotationZ", RotationTooltip, -180f, 180f, 1, ValueDisplayMode.Degrees),
            };

            PanelButton grabButton = PanelButton.CreateNew(container);
            grabButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.deviceOffsets.grab.tooltip"));

            PanelButton resetButton = PanelButton.CreateNew(container);
            resetButton.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.deviceOffsets.reset"));
            resetButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.deviceOffsets.reset.tooltip"));

            object source = new object();
            List<string> keys = new List<string>();
            List<string> labels = new List<string>();
            string assignedSignature = null;
            bool syncing = false;
            bool refreshing = false;
            bool driven = false;
            bool subscribed = false;
            var devices = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance.AllInputDevices : null;

            bool IsReleased()
            {
                return grabButton == null || grabButton.IsReleased;
            }

            string CurrentKey()
            {
                string key = BasisDeviceOffsetEditor.SelectedKey;
                return key != null && keys.Contains(key) ? key : null;
            }

            bool HasOffset(string key)
            {
                return BasisDeviceOffsets.TryGet(key, out Vector3 offsetPosition, out Quaternion offsetRotation) && !BasisDeviceOffsetMath.IsIdentity(offsetPosition, offsetRotation);
            }

            void ShowValues(string key)
            {
                BasisDeviceOffsets.TryGet(key, out Vector3 offsetPosition, out Quaternion offsetRotation);
                bool drive = key != null && BasisDeviceOffsetEditor.IsGrabbing && BasisDeviceOffsetEditor.TargetKey == key;
                Vector3 euler = offsetRotation.eulerAngles;
                syncing = true;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (drive != driven)
                    {
                        position[axis].SetExternalDrive(drive);
                        rotation[axis].SetExternalDrive(drive);
                    }
                    position[axis].SetValueWithoutNotify(offsetPosition[axis]);
                    rotation[axis].SetValueWithoutNotify(BasisDeviceOffsetMath.WrapDegrees(euler[axis]));
                }
                driven = drive;
                syncing = false;
            }

            void ShowControls(string key)
            {
                bool hasKey = key != null;
                deviceDropdown.SetInteractable(keys.Count > 0);
                for (int axis = 0; axis < 3; axis++)
                {
                    position[axis].SetInteractable(hasKey);
                    rotation[axis].SetInteractable(hasKey);
                }
                bool editing = BasisDeviceOffsetEditor.IsEditing;
                grabButton.Descriptor.SetTitle(BasisLocalization.Get(editing ? "settings.developer.deviceOffsets.grab.stop" : "settings.developer.deviceOffsets.grab"));
                if (grabButton.ButtonStyling != null)
                {
                    grabButton.ButtonStyling.ShowIndicator(editing);
                }
                bool canGrab = editing || (keys.Count > 0 && BasisDeviceOffsetEditor.HasGrabbingHand());
                grabButton.SetInteractable(canGrab, canGrab ? null : BasisLocalization.Get("settings.developer.deviceOffsets.grab.unavailable"));
                resetButton.SetInteractable(HasOffset(key));
            }

            void Refresh()
            {
                if (IsReleased())
                {
                    Unsubscribe();
                    return;
                }
                if (refreshing)
                {
                    return;
                }
                refreshing = true;
                CollectDevices(keys, labels);
                string key = CurrentKey();
                if (key == null && keys.Count > 0)
                {
                    key = keys[0];
                    BasisDeviceOffsetEditor.Select(key);
                }
                syncing = true;
                string signature = string.Join("\n", keys);
                if (signature != assignedSignature)
                {
                    assignedSignature = signature;
                    if (keys.Count == 0)
                    {
                        deviceDropdown.AssignEntries(new List<string> { NoneEntry }, new List<string> { BasisLocalization.Get("settings.developer.deviceOffsets.none") });
                    }
                    else
                    {
                        deviceDropdown.AssignEntries(new List<string>(keys), new List<string>(labels));
                    }
                }
                deviceDropdown.SetValueWithoutNotify(key ?? NoneEntry);
                syncing = false;
                ShowValues(key);
                ShowControls(key);
                refreshing = false;
            }

            void HandleOffsetChanged(string changedKey, object changeSource)
            {
                if (IsReleased())
                {
                    Unsubscribe();
                    return;
                }
                string key = CurrentKey();
                if (ReferenceEquals(changeSource, source) || (changedKey != null && changedKey != key))
                {
                    return;
                }
                ShowValues(key);
                resetButton.SetInteractable(HasOffset(key));
            }

            void HandleChanged()
            {
                Refresh();
            }

            void Unsubscribe()
            {
                if (!subscribed)
                {
                    return;
                }
                subscribed = false;
                if (devices != null)
                {
                    devices.OnListChanged -= HandleChanged;
                }
                BasisTrackerPairing.OnPairingsChanged -= HandleChanged;
                BasisLocalAvatarDriver.CalibrationComplete -= HandleChanged;
                BasisAvatarIKStageCalibration.OnFullBodyCalibrated -= HandleChanged;
                BasisDeviceOffsets.OnOffsetChanged -= HandleOffsetChanged;
                BasisDeviceOffsetEditor.OnStateChanged -= HandleChanged;
            }

            void ApplyFromSliders(bool persist)
            {
                string key = CurrentKey();
                if (syncing || key == null)
                {
                    return;
                }
                Vector3 offsetPosition = new Vector3(position[0].SliderComponent.value, position[1].SliderComponent.value, position[2].SliderComponent.value);
                Quaternion offsetRotation = Quaternion.Euler(rotation[0].SliderComponent.value, rotation[1].SliderComponent.value, rotation[2].SliderComponent.value);
                BasisDeviceOffsets.Set(key, offsetPosition, offsetRotation, persist, source);
                resetButton.SetInteractable(HasOffset(key));
            }

            void WireSlider(PanelSlider slider)
            {
                slider.SliderComponent.onValueChanged.AddListener(_ => ApplyFromSliders(false));
                slider.OnValueChanged += _ => ApplyFromSliders(true);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                WireSlider(position[axis]);
                WireSlider(rotation[axis]);
            }

            deviceDropdown.OnValueChanged += value =>
            {
                if (!syncing)
                {
                    BasisDeviceOffsetEditor.Select(value == NoneEntry ? null : value);
                }
            };
            grabButton.OnClicked += () => BasisDeviceOffsetEditor.SetEditing(!BasisDeviceOffsetEditor.IsEditing);
            resetButton.OnClicked += () =>
            {
                string key = CurrentKey();
                if (key != null)
                {
                    BasisDeviceOffsets.Clear(key, null);
                }
            };

            Refresh();

            if (devices != null)
            {
                devices.OnListChanged += HandleChanged;
            }
            BasisTrackerPairing.OnPairingsChanged += HandleChanged;
            BasisLocalAvatarDriver.CalibrationComplete += HandleChanged;
            BasisAvatarIKStageCalibration.OnFullBodyCalibrated += HandleChanged;
            BasisDeviceOffsets.OnOffsetChanged += HandleOffsetChanged;
            BasisDeviceOffsetEditor.OnStateChanged += HandleChanged;
            subscribed = true;
            grabButton.OnInstanceReleased += Unsubscribe;

            PanelSectionToggleHelpers.FinalizeBoxedSectionFromIndex(section, container, start, false, visible =>
            {
                if (visible)
                {
                    Refresh();
                }
                descriptor.ForceRebuild();
            });
        }

        private static PanelSlider CreateSlider(RectTransform container, string titleKey, string tooltipKey, float min, float max, int decimals, ValueDisplayMode mode)
        {
            PanelSlider slider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, container);
            slider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(BasisLocalization.Get(titleKey), min, max, false, decimals, mode));
            slider.Descriptor.SetTooltip(BasisLocalization.Get(tooltipKey));
            slider.SetResetDefault(0f);
            slider.SetValueWithoutNotify(0f);
            return slider;
        }

        private static void CollectDevices(List<string> keys, List<string> labels)
        {
            keys.Clear();
            labels.Clear();
            BasisDeviceManagement management = BasisDeviceManagement.Instance;
            if (management == null)
            {
                return;
            }
            var devices = management.AllInputDevices;
            int count = devices.Count;
            for (int index = 0; index < count; index++)
            {
                BasisInput input = devices[index];
                if (input == null || string.IsNullOrEmpty(input.DeviceOffsetKey) || keys.Contains(input.DeviceOffsetKey) || !input.TryGetRole(out BasisBoneTrackedRole role))
                {
                    continue;
                }
                keys.Add(input.DeviceOffsetKey);
                labels.Add(BasisDeviceOffsetEditor.RoleLabel(role) + ": " + input.CommonDeviceIdentifier);
            }
        }
    }
}
