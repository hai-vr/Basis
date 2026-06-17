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
        public PanelElementDescriptor HeightDescription;
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
            Button.OnClicked += Calibrate;
            Button.Descriptor.SetTitle(BasisLocalization.Get("calibration.calibrate"));
            Button.Descriptor.SetTooltip(BasisLocalization.Get("calibration.calibrate.tooltip"));

            // Calibration quality report — filled in after a calibration completes.
            _reportGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            _reportGroup.SetTitle("Calibration Report");
            _reportGroup.SetDescription(BasisCalibrationQualityReport.HasReport ? BasisCalibrationQualityReport.Summary : "Calibrate to see a quality report.");

            HeightDescription = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            HeightDescription.SetTitle(BasisLocalization.Get("calibration.additionalHeight"));
            HeightDescription.SetDescription(FormatAdditionalHeight());
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= RefreshAdditionalHeightLabel;
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame += RefreshAdditionalHeightLabel;

            var nudgeHeightToggle = PanelToggle.CreateNewEntry(container);
            nudgeHeightToggle.Descriptor.SetTitle(BasisLocalization.Get("calibration.nudgeStandingHeight"));
            nudgeHeightToggle.Descriptor.SetTooltip(BasisLocalization.Get("calibration.nudgeStandingHeight.tooltip"));

            var Description = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            Description.SetTitle(BasisLocalization.Get("calibration.pullTriggers"));

            var MinusButton = PanelButton.CreateNew(Description.ContentParent);
            MinusButton.OnClicked += DecreasePlayerSize;
            MinusButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.decreaseHeight"));
            MinusButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.decreaseHeight.tooltip"));

            var PlusButton = PanelButton.CreateNew(Description.ContentParent);
            PlusButton.OnClicked += IncreasePlayerSize;
            PlusButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.increaseHeight"));
            PlusButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.increaseHeight.tooltip"));

            Description.gameObject.SetActive(false);
            nudgeHeightToggle.SetValueWithoutNotify(false);
            nudgeHeightToggle.OnValueChanged += visible =>
            {
                Description.gameObject.SetActive(visible);
                layout.ForceRebuild();
            };

            // Persistent standing eye-height correction: the once-and-done version of the +/- nudge above.
            // Bridges a systematic measured-eye-height shortfall (seen on OpenVR: avatar feels too tall) so
            // the gap is corrected once instead of nudged every calibration. 0 = off; survives restarts/swaps.
            var eyeHeightCorrectionSlider = PanelSlider.CreateAndBind(
                container,
                PanelSlider.SliderSettings.Advanced("Standing Eye Height Correction", -0.20f, 0.20f, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.CalibrationStandingEyeHeightMeters);
            if (eyeHeightCorrectionSlider != null)
            {
                eyeHeightCorrectionSlider.Descriptor.SetTooltip(
                    "Persistent correction added to your measured standing eye height before scaling. If the avatar " +
                    "feels too tall and you nudge up every calibration, set this once to that amount (e.g. +0.10 m). " +
                    "0 = off. Unlike the +/- nudge, this survives restarts and avatar swaps.");
            }

            // Lock-in guides toggle (shrinking spheres + foot-forward guide while calibrating).
            var lockInGuidesToggle = PanelToggle.CreateNewEntry(container);
            lockInGuidesToggle.Descriptor.SetTitle(BasisLocalization.Get("calibration.lockInGuides"));
            lockInGuidesToggle.Descriptor.SetTooltip(BasisLocalization.Get("calibration.lockInGuides.tooltip"));
            lockInGuidesToggle.SetValueWithoutNotify(BasisCalibrationLockInVisualizer.Enabled);
            lockInGuidesToggle.OnValueChanged += value => BasisCalibrationLockInVisualizer.Enabled = value;

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

            // Pose-tolerant calibration (experimental): reconstruct a bent calibration from elbow/knee trackers.
            var poseCompToggle = PanelToggle.CreateNewEntry(container);
            poseCompToggle.Descriptor.SetTitle("Pose Compensation (elbow/knee)");
            poseCompToggle.Descriptor.SetTooltip("Use elbow/knee trackers to reconstruct a bent calibration pose, so you don't have to hold a perfect straight T-pose. Experimental — A/B test it.");
            poseCompToggle.AssignBinding(BasisSettingsDefaults.CalibrationPoseCompensation);

            // Playspace Mover quick enable (full options live under Body Tracking settings)
            var playspaceMoverToggle = PanelToggle.CreateNewEntry(container);
            playspaceMoverToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.title"));
            playspaceMoverToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.enable.tooltip"));
            playspaceMoverToggle.AssignBinding(BasisSettingsDefaults.EnablePlayspaceMover);

            var playspaceResetButton = PanelButton.CreateNew(container);
            playspaceResetButton.Descriptor.SetTitle(BasisLocalization.Get("settings.bodyTracking.playspaceMover.reset"));
            playspaceResetButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.bodyTracking.playspaceMover.reset.tooltip"));
            playspaceResetButton.OnClicked += BasisLocalPlayspaceMover.ResetOffset;

            // Pitch calibration toggle
            _pitchToggleButton = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, container);
            _pitchToggleButton.OnClicked += TogglePitchCalibration;
            _pitchToggleButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.pitchLabel.tooltip"));
            UpdatePitchToggleLabel();

            // Navigate to Body Tracking settings
            var bodyTrackingSettingsButton = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, container);
            bodyTrackingSettingsButton.Descriptor.SetTitle(BasisLocalization.Get("calibration.bodyTrackingSettings"));
            bodyTrackingSettingsButton.Descriptor.SetTooltip(BasisLocalization.Get("calibration.bodyTrackingSettings.tooltip"));
            bodyTrackingSettingsButton.OnClicked += () => SettingsProvider.OpenBodyTrackingTab();

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

            // Per-user additional height adjustment
            BasisHeightDriver.AdditionalPlayerHeight = 0f;
            BasisSettingsDefaults.CalibrationStandingEyeHeightMeters.ResetToDefault();
            BasisHeightDriver.ApplyScaleAndHeight();

            // Refresh on-screen labels for the controls we just reset
            HeightDescription.SetDescription(FormatAdditionalHeight());
            UpdatePitchToggleLabel();
        }
        /// <summary>
        /// tracker balls
        /// </summary>
        public void IncreasePlayerSize()
        {
            BasisHeightDriver.NudgeStandingHeight(0.1f);
            HeightDescription.SetDescription(FormatAdditionalHeight());
        }
        public void DecreasePlayerSize()
        {
            BasisHeightDriver.NudgeStandingHeight(-0.1f);
            HeightDescription.SetDescription(FormatAdditionalHeight());
        }
        public void ApplyAndUpdateUI()
        {
            HeightDescription.SetDescription(FormatAdditionalHeight());
            BasisHeightDriver.ApplyScaleAndHeight();
        }

        private void RefreshAdditionalHeightLabel(BasisHeightDriver.HeightModeChange _)
        {
            if (HeightDescription == null || HeightDescription.IsReleased)
            {
                return;
            }
            HeightDescription.SetDescription(FormatAdditionalHeight());
        }

        private static string FormatScaleMeters(float meters) => meters.ToString("0.##") + " m";

        private const float NudgeWarnThreshold = 0.2f;

        private static string FormatAdditionalHeight()
        {
            float height = BasisHeightDriver.CurrentStandingHeightNudge;
            string text = $"{height:F2}";
            if (Mathf.Abs(height) > NudgeWarnThreshold)
            {
                text += "  " + BasisLocalization.Get("calibration.nudgeWarning");
            }
            return text;
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
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= RefreshAdditionalHeightLabel;
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
            BoundButton.OnInstanceReleased += () =>
            {
                BasisDeviceManagement.OnBootModeChanged -= BootModeChanged;
                BasisSettingsDefaults.EnableFBT.OnChanged -= FBTToggleChanged;
            };
            EvaluateButtonVisibility();
        }

        private void BootModeChanged(string _) => EvaluateButtonVisibility();
        private void FBTToggleChanged(bool _) => EvaluateButtonVisibility();

        private void EvaluateButtonVisibility()
        {
            bool inVR = !BasisDeviceManagement.IsUserInDesktop();
            bool fbtEnabled = BasisSettingsDefaults.EnableFBT.RawValue;
            BoundButton.gameObject.SetActive(inVR && fbtEnabled);
        }
    }
}
