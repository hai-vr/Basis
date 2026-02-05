using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.Rendering.DebugUI;

namespace Basis.BasisUI
{
    public class CalibrationProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new CalibrationProvider());
        }

        public override string Title => "Calibrate";
        public override string IconAddress => AddressableAssets.Sprites.Calibrate;
        public override int Order => 10;

        private readonly Dictionary<BasisInput, Action> _triggerDelegates = new();

        private BasisInput _leftHand;
        private BasisInput _rightHand;

        private bool _leftPressed;
        private bool _rightPressed;
        private bool _calibrated;

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.Instance.ActiveMenu.ReleaseInstance();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                new BasisMenuPanel.PanelData
                {
                    Title = this.Title,
                    PanelSize = new Vector2(500, 500),
                    PanelPosition = default
                },
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            RectTransform container = panel.Descriptor.ContentParent;

            PanelElementDescriptor layout =PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            container = layout.ContentParent;

            var Button = PanelButton.CreateNew(container);
            Button.OnClicked += Calibrate;
            Button.Descriptor.SetTitle("Calibrate");

            HeightDescription = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            HeightDescription.SetTitle("Additional Height");
            HeightDescription.SetDescription(AdditionalHeight);

            var MinusButton = PanelButton.CreateNew(container);
            MinusButton.OnClicked += DecreasePlayerSize;
            MinusButton.Descriptor.SetTitle("Remove 0.01f Height");

            var PlusButton = PanelButton.CreateNew(container);
            PlusButton.OnClicked += IncreasePlayerSize;
            PlusButton.Descriptor.SetTitle("Add 0.01f Height");
        }
        public PanelElementDescriptor HeightDescription;
        public string AdditionalHeight = $"{BasisHeightDriver.AdditionalPlayerHeight:F2}";
        /// <summary>
        /// tracker balls
        /// </summary>
        public void IncreasePlayerSize()
        {
            BasisHeightDriver.AdditionalPlayerHeight += 0.1f;
            HeightDescription.DescriptionLabel.text = AdditionalHeight;
            BasisHeightDriver.ApplyScaleAndHeight();
        }
        public void DecreasePlayerSize()
        {
            BasisHeightDriver.AdditionalPlayerHeight -= 0.1f;
            HeightDescription.DescriptionLabel.text = AdditionalHeight;
            BasisHeightDriver.ApplyScaleAndHeight();
        }
        public void Calibrate()
        {
            if (BasisLocalAvatarDriver.CurrentlyTposing)
            {
                return;
            }

            var localplayer = BasisLocalPlayer.Instance;
            BasisUINeedsVisibleTrackers.Instance.Add(localplayer);
            // kept because you had it (even if unused)
            var localBoneDriver = localplayer.LocalBoneDriver;

            localplayer.LocalAvatarDriver.PutAvatarIntoTPose();

            bool hasLeft = BasisDeviceManagement.Instance.FindDevice(out BasisInput leftHand, BasisBoneTrackedRole.LeftHand);
            bool hasRight = BasisDeviceManagement.Instance.FindDevice(out BasisInput rightHand, BasisBoneTrackedRole.RightHand);

            // Safety: clear any old subscriptions before adding new ones
            UnsubscribeAll();

            _calibrated = false;
            _leftPressed = false;
            _rightPressed = false;

            if (hasLeft && hasRight)
            {
                _leftHand = leftHand;
                _rightHand = rightHand;

                // Subscribe ONLY to left + right. Calibrate when BOTH pressed.
                Subscribe(_leftHand, () => OnTriggerChanged(_leftHand));
                Subscribe(_rightHand, () => OnTriggerChanged(_rightHand));
            }
            else
            {
                // Fallback: controllers missing -> behave as normal (any trigger >= 0.9 calibrates)
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

        private void OnTriggerChanged(BasisInput device)
        {
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
                    CalibrateOnce();

                return;
            }

            // Fallback: any device trigger pressed
            if (trigger >= 0.9f)
            {
                CalibrateOnce();
            }
        }

        private void CalibrateOnce()
        {
            if (_calibrated)
                return;

            _calibrated = true;

            UnsubscribeAll();
            BasisAvatarIKStageCalibration.FullBodyCalibration();
            BasisUINeedsVisibleTrackers.Instance.Remove(BasisLocalPlayer.Instance);
        }

        public override void OnButtonCreated(PanelButton button)
        {
            base.OnButtonCreated(button);
            BasisDeviceManagement.OnBootModeChanged += BootModeChanged;
            BoundButton.OnInstanceReleased += () => BasisDeviceManagement.OnBootModeChanged -= BootModeChanged;
            CheckUserForVR();
        }

        private void BootModeChanged(string _) => CheckUserForVR();

        private void CheckUserForVR()
        {
            BoundButton.gameObject.SetActive(!BasisDeviceManagement.IsUserInDesktop());
        }
    }
}
