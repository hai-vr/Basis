using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    public class CalibrationProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new CalibrationProvider());
        }

        public override string Title => BasisLocalization.Get("menu.provider.calibration");
        public override string IconAddress => AddressableAssets.Sprites.Calibrate;
        public override int Order => 70;

        public override bool Hidden => false;

        private readonly Dictionary<BasisInput, Action> _triggerDelegates = new();

        private BasisInput _leftHand;
        private BasisInput _rightHand;

        private bool _leftPressed;
        private bool _rightPressed;
        private bool _calibrated;

        // Pitch calibration state
        private enum PitchCalibrationStep
        {
            None,
            WaitingForUp,
            WaitingForDown,
            WaitingForForward
        }
        private PitchCalibrationStep _pitchStep = PitchCalibrationStep.None;
        private Vector2 _pitchUp;
        private Vector2 _pitchDown;

        public PanelButton Button;
        private PanelButton _pitchToggleButton;
        private PanelElementDescriptor _reportGroup;
        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                new BasisMenuPanel.PanelData
                {
                    Title = this.Title,
                    PanelSize = new Vector2(600, 1025),
                    PanelPosition = new Vector3(450, 25, 0),
                },
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            panel.OnInstanceReleased += CancelActiveCalibration;

            RectTransform container = panel.Descriptor.ContentParent;

            PanelElementDescriptor layout = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            container = layout.ContentParent;

            Button = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, container);
            Button.OnClicked += OnCalibrateButtonClicked;
            Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrate"));
            Button.Descriptor.SetTooltip(BasisLocalization.Get("calibration.calibrate.tooltip"));

            // Calibration quality report — filled in after a calibration completes.
            _reportGroup = null;
            if (BasisSettingsDefaults.DevShowCalibrationDebug.RawValue)
            {
                _reportGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
                _reportGroup.SetTitle("Calibration Report");
                _reportGroup.SetDescription(BasisCalibrationQualityReport.HasReport ? BasisCalibrationQualityReport.Summary : "Calibrate to see a quality report.");
            }

            // Calibration modes (moved here from Body Tracking settings): seated/standing, avatar scaling, spine lock.
            var seatedModeDropdown = PanelDropdown.CreateNewEntry(container);
            seatedModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.seatedMode"));
            seatedModeDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.seatedMode.tooltip"));
            seatedModeDropdown.AssignLocalizedEntries(
                new List<string> { SettingsProviderIK.SeatedMode_Standing, SettingsProviderIK.SeatedMode_Seated },
                new List<string> { "settings.bodyTracking.seatedMode.standing", "settings.bodyTracking.seatedMode.seated" });
            seatedModeDropdown.AssignBinding(BasisSettingsDefaults.SitStand);

            var scalingModeDropdown = PanelDropdown.CreateNewEntry(container);
            scalingModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.ikMode"));
            scalingModeDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.ikMode.tooltip"));
            scalingModeDropdown.AssignLocalizedEntries(
                new List<string> { "Eye Height", "Arm Distance" },
                new List<string> { "settings.bodyTracking.ikMode.eyeHeight", "settings.bodyTracking.ikMode.armDistance" });
            scalingModeDropdown.AssignBinding(BasisSettingsDefaults.IKMode);

            var spineLockModeDropdown = PanelDropdown.CreateNewEntry(container);
            spineLockModeDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.spineLockMode"));
            spineLockModeDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.spineLockMode.tooltip"));
            spineLockModeDropdown.AssignLocalizedEntries(
                new List<string> { "Lock Hips", "Lock Head", "Lock Both" },
                new List<string> { "settings.bodyTracking.spineLock.hips", "settings.bodyTracking.spineLock.head", "settings.bodyTracking.spineLock.both" });
            spineLockModeDropdown.AssignBinding(BasisSettingsDefaults.IKLockMode);

            // Slim calibration panel: inset each dropdown control's left edge so its label isn't squished.
            NarrowDropdownForPanel(seatedModeDropdown);
            NarrowDropdownForPanel(scalingModeDropdown);
            NarrowDropdownForPanel(spineLockModeDropdown);

            // Avatar Scaling Mode is moot in seated mode (a fixed height is used), so disable it there.
            void UpdateScalingModeInteractable()
            {
                bool isSeated = seatedModeDropdown.DropdownComponent.options[seatedModeDropdown.DropdownComponent.value].text == SettingsProviderIK.SeatedMode_Seated;
                scalingModeDropdown.SetInteractable(!isSeated,
                    isSeated ? BasisLocalization.Get("settings.bodyTracking.ikMode.disabledSeated") : null);
            }
            seatedModeDropdown.OnValueChanged += _ => UpdateScalingModeInteractable();
            UpdateScalingModeInteractable();

            // Persistent Eye Height Modifier, gated behind a toggle. Bridges a systematic measured-eye-height
            // shortfall (seen on OpenVR: avatar feels too tall) so the gap is corrected once. Survives restarts/swaps.
            var eyeHeightCorrectionToggle = PanelToggle.CreateNewEntry(container);
            eyeHeightCorrectionToggle.Descriptor.SetTitle("Eye Height Modifier");
            eyeHeightCorrectionToggle.Descriptor.SetTooltip(
                "Enable a persistent modifier added to your measured standing eye height before scaling. If the " +
                "avatar feels too tall, turn this on and raise the slider to bridge the gap. Survives restarts and avatar swaps.");
            eyeHeightCorrectionToggle.AssignBinding(BasisSettingsDefaults.EnableStandingEyeHeightCorrection);

            var eyeHeightCorrectionSlider = PanelSlider.CreateAndBind(
                container,
                PanelSlider.SliderSettings.Advanced("Eye Height Modifier", BasisHeightDriver.StandingHeightCorrectionMin, BasisHeightDriver.StandingHeightCorrectionMax, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.CalibrationStandingEyeHeightMeters);
            if (eyeHeightCorrectionSlider != null)
            {
                eyeHeightCorrectionSlider.Descriptor.SetTooltip(
                    "Persistent modifier added to your measured standing eye height before scaling. If the avatar " +
                    "feels too tall, raise this to bridge the gap (e.g. +0.10 m). Survives restarts and avatar swaps.");
                eyeHeightCorrectionSlider.gameObject.SetActive(BasisSettingsDefaults.EnableStandingEyeHeightCorrection.RawValue);
                eyeHeightCorrectionToggle.OnValueChanged += visible =>
                {
                    eyeHeightCorrectionSlider.gameObject.SetActive(visible);
                    layout.ForceRebuild();
                };
            }

            // Nudge Standing Height: gated behind its own toggle. Quick ± buttons for a SEPARATE standing-height
            // nudge (the old AdditionalPlayerHeight), fed through DeviceScale independently of the Eye Height Modifier.
            var nudgeToggle = PanelToggle.CreateNewEntry(container);
            nudgeToggle.Descriptor.SetTitle(BasisLocalization.Get("calibration.nudgeStandingHeight"));
            nudgeToggle.Descriptor.SetTooltip(BasisLocalization.Get("calibration.nudgeStandingHeight.tooltip"));
            nudgeToggle.AssignBinding(BasisSettingsDefaults.EnableStandingHeightNudge);

            var nudgeGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);

            // Show the live nudge value (read fresh from the persisted setting) instead of a static warning, so it
            // always matches the real value — including after avatar swaps or closing/reopening the menu.
            void UpdateNudgeReadout() => nudgeGroup.SetDescription(FormatNudgeMeters(BasisSettingsDefaults.AdditionalPlayerHeight.RawValue));
            UpdateNudgeReadout();

            void NudgeStandingHeight(float deltaMeters)
            {
                float next = Mathf.Clamp(
                    BasisSettingsDefaults.AdditionalPlayerHeight.RawValue + deltaMeters,
                    -NudgeStandingHeightLimitMeters,
                    NudgeStandingHeightLimitMeters);

                // Adjusts only the nudge (AdditionalPlayerHeight) — fed through the DeviceScale denominator,
                // separate from the Eye Height Modifier. SetValue persists and re-applies height via SMModuleCalibration.
                BasisSettingsDefaults.AdditionalPlayerHeight.SetValue(next);
                UpdateNudgeReadout();
            }

            var decreaseHeightButton = PanelButton.CreateNew(nudgeGroup.ContentParent);
            decreaseHeightButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.decreaseHeight"));
            decreaseHeightButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.decreaseHeight.tooltip"));
            decreaseHeightButton.OnClicked += () => NudgeStandingHeight(-NudgeStandingHeightStepMeters);

            var increaseHeightButton = PanelButton.CreateNew(nudgeGroup.ContentParent);
            increaseHeightButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.increaseHeight"));
            increaseHeightButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.increaseHeight.tooltip"));
            increaseHeightButton.OnClicked += () => NudgeStandingHeight(NudgeStandingHeightStepMeters);

            nudgeGroup.gameObject.SetActive(BasisSettingsDefaults.EnableStandingHeightNudge.RawValue);
            nudgeToggle.OnValueChanged += visible =>
            {
                nudgeGroup.gameObject.SetActive(visible);
                layout.ForceRebuild();
            };

            // Lock-in guides toggle (shrinking spheres + foot-forward guide while calibrating).
            if (BasisSettingsDefaults.DevShowCalibrationDebug.RawValue)
            {
                var lockInGuidesToggle = PanelToggle.CreateNewEntry(container);
                lockInGuidesToggle.Descriptor.SetTitle(BasisLocalization.Get("calibration.lockInGuides"));
                lockInGuidesToggle.Descriptor.SetTooltip(BasisLocalization.Get("calibration.lockInGuides.tooltip"));
                lockInGuidesToggle.SetValueWithoutNotify(BasisCalibrationLockInVisualizer.Enabled);
                lockInGuidesToggle.OnValueChanged += value => BasisCalibrationLockInVisualizer.Enabled = value;
            }

            // Avatar scale
            var customScaleToggle = PanelToggle.CreateNewEntry(container);
            customScaleToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.customScale"));
            customScaleToggle.Descriptor.SetTooltip(BasisLocalization.Get("calibration.customScale.tooltip"));
            customScaleToggle.AssignBinding(BasisSettingsDefaults.CustomScale);

            var avatarScaleSlider = PanelSlider.CreateAndBind(
                container,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.bodyTracking.avatarHeightScale"), 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.SelectedScale);
            if (avatarScaleSlider != null)
            {
                avatarScaleSlider.Descriptor.SetTooltip(FormatScaleMeters(avatarScaleSlider.Value));
                avatarScaleSlider.OnValueChanged += value => avatarScaleSlider.Descriptor.SetTooltip(FormatScaleMeters(value));
                avatarScaleSlider.gameObject.SetActive(BasisSettingsDefaults.CustomScale.RawValue);
                customScaleToggle.OnValueChanged += visible =>
                {
                    avatarScaleSlider.gameObject.SetActive(visible);
                    layout.ForceRebuild();
                };
            }

            // Pitch calibration toggle
            _pitchToggleButton = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, container);
            _pitchToggleButton.OnClicked += TogglePitchCalibration;
            _pitchToggleButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.pitchLabel.tooltip"));
            UpdatePitchToggleLabel();

            // Reset Calibration (restores defaults for calibration-only state, including hidden pitch data)
            var resetButton = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, container);
            resetButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.reset"));
            resetButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.resetDescription"));
            resetButton.OnClicked += PromptResetCalibration;
        }

        private void PromptResetCalibration()
        {
            BasisMainMenu.Instance.OpenDialogue(
                BasisLocalization.Get("calibration.reset"),
                BasisLocalization.Get("calibration.resetConfirm"),
                BasisLocalization.Get("ui.reset"),
                BasisLocalization.Get("ui.cancel"),
                value =>
                {
                    if (!value)
                    {
                        return;
                    }

                    ResetCalibration();
                });
        }

        private void ResetCalibration()
        {
            // Pitch calibration toggle (binding + module-static used by Calibrate())
            BasisSettingsDefaults.PitchCalibration.ResetToDefault();
            SMModuleCalibration.PitchCalibrationEnabled = BasisSettingsDefaults.PitchCalibration.RawValue;

            // Captured pitch calibration result (hidden backend state)
            BasisHeightDriver.HasPitchCalibratedHeight = false;
            BasisHeightDriver.PitchCalibratedEyeHeight = BasisHeightDriver.FallbackHeightInMeters;

            BasisSettingsDefaults.CalibrationStandingEyeHeightMeters.ResetToDefault();
            BasisSettingsDefaults.EnableStandingEyeHeightCorrection.ResetToDefault();
            BasisSettingsDefaults.EnableStandingHeightNudge.ResetToDefault();
            BasisSettingsDefaults.AdditionalPlayerHeight.ResetToDefault();
            BasisHeightDriver.HasUserCalibratedHeight = false;
            BasisAutoScaleEstimator.Reset();
            BasisHeightDriver.ApplyScaleAndHeight();

            UpdatePitchToggleLabel();
        }
        private static string FormatScaleMeters(float meters) => meters.ToString("0.##") + " m";
        private static string FormatNudgeMeters(float meters) => "Current: " + meters.ToString("+0.00;-0.00;0.00") + " m";

        // The dropdown control prefab is sized for the wide settings page; in the slim calibration panel its
        // label gets squished. Inset the control's left edge (the RectTransform "Left" field) so the title has room.
        private const float CalibrationDropdownLeftInset = 200f;
        private const float NudgeStandingHeightStepMeters = 0.05f;
        private const float NudgeStandingHeightLimitMeters = 0.5f;
        private static void NarrowDropdownForPanel(PanelDropdown dropdown)
        {
            if (dropdown == null || dropdown.DropdownComponent == null)
            {
                return;
            }
            if (dropdown.DropdownComponent.transform is RectTransform rt)
            {
                rt.offsetMin = new Vector2(CalibrationDropdownLeftInset, rt.offsetMin.y);
            }
        }

        private void TogglePitchCalibration()
        {
            SMModuleCalibration.PitchCalibrationEnabled = !SMModuleCalibration.PitchCalibrationEnabled;
            UpdatePitchToggleLabel();
        }

        private void UpdatePitchToggleLabel()
        {
            if (_pitchToggleButton != null)
            {
                string state = BasisLocalization.Get(SMModuleCalibration.PitchCalibrationEnabled ? "ui.on" : "ui.off");
                _pitchToggleButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.pitchLabel", state));
            }
        }

        private void OnCalibrateButtonClicked()
        {
            if (BasisDeviceManagement.IsUserInDesktop() && _triggerDelegates.Count > 0 && !_calibrated)
            {
                OnTriggersConfirmed();
                return;
            }

            Calibrate();
        }

        public void Calibrate()
        {
            var localplayer = BasisLocalPlayer.Instance;
            if (localplayer == null)
            {
                return;
            }
            BasisUINeedsVisibleTrackers.Add(localplayer);
            // kept because you had it (even if unused)
            var localBoneDriver = localplayer.LocalBoneDriver;

            _calibrated = false;
            _leftPressed = false;
            _rightPressed = false;

            if (SMModuleCalibration.PitchCalibrationEnabled && !SMModuleSitStand.IsSteatedMode)
            {
                // Start pitch calibration flow: look up → look down → look forward
                _pitchStep = PitchCalibrationStep.WaitingForUp;
                Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.pitch.up"));
                SubscribeToTriggers();
            }
            else
            {
                // Standard single-pose calibration — clear any stale pitch data
                _pitchStep = PitchCalibrationStep.None;
                BasisHeightDriver.HasPitchCalibratedHeight = false;
                Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrating"));
                localplayer.LocalAvatarDriver.PutAvatarIntoTPose();
                BasisCalibrationLockInVisualizer.Begin();
                SubscribeToTriggers();
            }
        }

        private void SubscribeToTriggers()
        {
            UnsubscribeAll();

            bool hasLeft = BasisDeviceManagement.Instance.FindDevice(out BasisInput leftHand, BasisBoneTrackedRole.LeftHand);
            bool hasRight = BasisDeviceManagement.Instance.FindDevice(out BasisInput rightHand, BasisBoneTrackedRole.RightHand);

            if (hasLeft && hasRight)
            {
                _leftHand = leftHand;
                _rightHand = rightHand;
                Subscribe(_leftHand, () => OnTriggerChanged(_leftHand));
                Subscribe(_rightHand, () => OnTriggerChanged(_rightHand));
            }
            else
            {
                foreach (BasisInput device in BasisDeviceManagement.Instance.AllInputDevices)
                {
                    Subscribe(device, () => OnTriggerChanged(device));
                }
            }
        }

        private void Subscribe(BasisInput device, Action handler)
        {
            _triggerDelegates[device] = handler;
            device.CurrentInputState.OnTriggerChanged += handler;
        }

        private void UnsubscribeAll()
        {
            foreach (KeyValuePair<BasisInput, Action> entry in _triggerDelegates)
            {
                entry.Key.CurrentInputState.OnTriggerChanged -= entry.Value;
            }

            _triggerDelegates.Clear();

            _leftHand = null;
            _rightHand = null;
        }

        private void CancelActiveCalibration()
        {
            UnsubscribeAll();
            BasisCalibrationLockInVisualizer.End();
            _pitchStep = PitchCalibrationStep.None;
            _leftPressed = false;
            _rightPressed = false;

            if (BasisLocalPlayer.Instance == null)
            {
                return;
            }

            if (!_calibrated && BasisLocalAvatarDriver.CurrentlyTposing)
            {
                BasisLocalPlayer.Instance.LocalAvatarDriver.ResetAvatarAnimator();
                BasisLocalPlayer.Instance.LocalRigDriver.RigLayer.active = true;
            }

            BasisUINeedsVisibleTrackers.Remove(BasisLocalPlayer.Instance);

            if (Button != null && !Button.IsReleased)
            {
                Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrate"));
            }
        }

        private void OnTriggerChanged(BasisInput device)
        {
            // The calibration panel (and its Button) can be released while trigger
            // subscriptions are still active — e.g. a scene load fires input events
            // after the menu has been torn down. Stop listening and bail so we never
            // dereference a destroyed Button.
            if (Button == null || Button.IsReleased)
            {
                CancelActiveCalibration();
                return;
            }

            if (_calibrated)
                return;

            float trigger = device.CurrentInputState.Trigger;

            // If we have both hands, require BOTH triggers pressed
            if (_leftHand != null && _rightHand != null)
            {
                if (device == _leftHand)
                    _leftPressed = (trigger >= 0.9f);

                if (device == _rightHand)
                    _rightPressed = (trigger >= 0.9f);

                if (_leftPressed && _rightPressed)
                    OnTriggersConfirmed();

                return;
            }

            // Fallback: any device trigger pressed
            if (trigger >= 0.9f)
            {
                OnTriggersConfirmed();
            }
        }

        private void OnTriggersConfirmed()
        {
            if (_calibrated)
                return;

            switch (_pitchStep)
            {
                case PitchCalibrationStep.WaitingForUp:
                    if (!BasisLocalHeightCalculator.CaptureHMDPitchSample(out float upPitch, out float upY) || upY <= 0f)
                    {
                        // No device, fall back to standard calibration
                        BasisDebug.LogWarning("Pitch calibration: no HMD for up sample, falling back to standard.", BasisDebug.LogTag.Avatar);
                        StartStandardCalibration();
                        return;
                    }
                    _pitchUp = new Vector2(upPitch, upY);
                    _pitchStep = PitchCalibrationStep.WaitingForDown;
                    Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.pitch.down"));
                    // Reset trigger state for next step
                    _leftPressed = false;
                    _rightPressed = false;
                    break;

                case PitchCalibrationStep.WaitingForDown:
                    if (!BasisLocalHeightCalculator.CaptureHMDPitchSample(out float downPitch, out float downY) || downY <= 0f)
                    {
                        BasisDebug.LogWarning("Pitch calibration: no HMD for down sample, falling back to standard.", BasisDebug.LogTag.Avatar);
                        StartStandardCalibration();
                        return;
                    }
                    _pitchDown = new Vector2(downPitch, downY);
                    _pitchStep = PitchCalibrationStep.WaitingForForward;
                    Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.pitch.forward"));
                    _leftPressed = false;
                    _rightPressed = false;
                    break;

                case PitchCalibrationStep.WaitingForForward:
                    if (!BasisLocalHeightCalculator.CaptureHMDPitchSample(out float forwardPitch, out float forwardY) || forwardY <= 0f)
                    {
                        BasisDebug.LogWarning("Pitch calibration: no HMD for forward sample, falling back to standard.", BasisDebug.LogTag.Avatar);
                        StartStandardCalibration();
                        return;
                    }
                    // Compute corrected height and store it
                    float corrected = BasisLocalHeightCalculator.ComputePitchCalibratedHeight(_pitchUp, _pitchDown, new Vector2(forwardPitch, forwardY));
                    BasisHeightDriver.PitchCalibratedEyeHeight = corrected;
                    BasisHeightDriver.HasPitchCalibratedHeight = true;
                    _pitchStep = PitchCalibrationStep.None;
                    // Now proceed with standard full-body calibration using the corrected height
                    StartStandardCalibration();
                    break;

                case PitchCalibrationStep.None:
                default:
                    CalibrateOnce();
                    break;
            }
        }

        private void StartStandardCalibration()
        {
            _pitchStep = PitchCalibrationStep.None;
            Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrating"));
            BasisLocalPlayer.Instance.LocalAvatarDriver.PutAvatarIntoTPose();
            BasisCalibrationLockInVisualizer.Begin();
            // Reset trigger state so they need to press again for final calibration
            _leftPressed = false;
            _rightPressed = false;
            // Subscribe fresh for the final trigger press
            SubscribeToTriggers();
        }

        private void CalibrateOnce()
        {
            if (_calibrated)
                return;

            _calibrated = true;

            UnsubscribeAll();
            BasisCalibrationLockInVisualizer.End();
            BasisAvatarIKStageCalibration.FullBodyCalibration();
            BasisUINeedsVisibleTrackers.Remove(BasisLocalPlayer.Instance);
            Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrate"));

            BasisCalibrationQualityReport.Capture();
            if (_reportGroup != null)
            {
                _reportGroup.SetTitle(BasisCalibrationQualityReport.HasReport ? $"Calibration Report  —  {BasisCalibrationQualityReport.Grade}" : "Calibration Report");
                _reportGroup.SetDescription(BasisCalibrationQualityReport.HasReport ? BasisCalibrationQualityReport.Summary : "Calibration report unavailable.");
            }
        }

        public override void OnButtonCreated(PanelButton button)
        {
            base.OnButtonCreated(button);
            BasisDeviceManagement.OnBootModeChanged += BootModeChanged;
            BasisSettingsDefaults.EnableFBT.OnChanged += FBTToggleChanged;
            SetDeviceListSubscription(true);
            BoundButton.OnInstanceReleased += () =>
            {
                BasisDeviceManagement.OnBootModeChanged -= BootModeChanged;
                BasisSettingsDefaults.EnableFBT.OnChanged -= FBTToggleChanged;
                SetDeviceListSubscription(false);
            };
            EvaluateButtonVisibility();
        }

        private void BootModeChanged(string _) => EvaluateButtonVisibility();
        private void FBTToggleChanged(bool _) => EvaluateButtonVisibility();

        private void SetDeviceListSubscription(bool subscribe)
        {
            BasisDeviceManagement manager = BasisDeviceManagement.Instance;
            if (manager == null)
            {
                return;
            }
            if (subscribe)
            {
                manager.AllInputDevices.OnListChanged += EvaluateButtonVisibility;
            }
            else
            {
                manager.AllInputDevices.OnListChanged -= EvaluateButtonVisibility;
            }
        }

        private void EvaluateButtonVisibility()
        {
            if (BoundButton == null || BoundButton.IsReleased)
            {
                return;
            }

            bool show = !BasisDeviceManagement.IsUserInDesktop()
                || (BasisSettingsDefaults.EnableFBT.RawValue && HasNonCameraBodyTrackers());
            BoundButton.gameObject.SetActive(show);
        }

        /// <summary>
        /// True when at least one real or simulated full-body tracker is present that isn't
        /// camera/optical (MediaPipe) tracking. Webcam trackers flag themselves via
        /// <see cref="BasisInput.IsCameraTracked"/> and are excluded here.
        /// </summary>
        private static bool HasNonCameraBodyTrackers()
        {
            BasisDeviceManagement manager = BasisDeviceManagement.Instance;
            if (manager == null)
            {
                return false;
            }

            BasisObservableList<BasisInput> devices = manager.AllInputDevices;
            for (int i = 0; i < devices.Count; i++)
            {
                BasisInput device = devices[i];
                if (device == null || device.IsCameraTracked)
                {
                    continue;
                }
                if (device.TryGetRole(out BasisBoneTrackedRole role)
                    && BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
