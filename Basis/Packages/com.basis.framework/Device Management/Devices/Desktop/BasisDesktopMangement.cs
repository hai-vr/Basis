using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Windows;

namespace Basis.Scripts.Device_Management.Devices.Desktop
{
    /// <summary>
    /// Provides device management logic for desktop-based usage of the Basis SDK.  
    /// Handles initialization and cleanup of eye input simulation when running without XR hardware.
    /// </summary>
    [Serializable]
    public class BasisDesktopManagement : BasisBaseTypeManagement
    {
        /// <summary>
        /// Reference to the <see cref="BasisAvatarEyeInput"/> component 
        /// created for simulating desktop eye tracking input.
        /// </summary>
        public BasisAvatarEyeInput BasisAvatarEyeInput;

        /// <summary>
        /// Identifier string for the desktop eye device.
        /// </summary>
        public const string DesktopEye = "Desktop Eye";
        public const string OnScreenControls = "OnScreenControls";
        public bool AlwaysSpawnHeadsUpControls;
        public GameObject Controls;
        /// <summary>
        /// Starts the Basis SDK for desktop mode.  
        /// If no <see cref="BasisAvatarEyeInput"/> exists, it creates one and attaches it
        /// under the <see cref="BasisLocalPlayer"/> object (if present).  
        /// Also locks the cursor for desktop interaction.
        /// </summary>
        public override void StartSDK()
        {
            if (BasisAvatarEyeInput == null)
            {
                BasisLocalCameraDriver.AllowXRRenderering(false);

                GameObject gameObject = new GameObject(DesktopEye);
                if (BasisLocalPlayer.Instance != null)
                {
                    gameObject.transform.parent = BasisLocalPlayer.Instance.transform;
                }

                BasisAvatarEyeInput = gameObject.AddComponent<BasisAvatarEyeInput>();
                BasisAvatarEyeInput.Initialize(DesktopEye, nameof(BasisDesktopManagement));
                BasisDeviceManagement.Instance.TryAdd(BasisAvatarEyeInput);
            }
            if (BasisDeviceManagement.IsMobilehardware() || AlwaysSpawnHeadsUpControls)
            {
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op = Addressables.InstantiateAsync(OnScreenControls, BasisLocalCameraDriver.Instance.transform, true);
                Controls = op.WaitForCompletion();
                Controls.transform.SetLocalPositionAndRotation(new Vector3(0, 0, 0.3f), Quaternion.identity);
            }
            else
            {
                if (Controls != null)
                {
                    Addressables.ReleaseInstance(Controls);
                }
            }

            EnhancedTouchSupport.Enable();
            UnityEngine.InputSystem.EnhancedTouch.Touch.onFingerDown += onFingerDown;
            BasisCursorManagement.LockCursor(nameof(BasisAvatarEyeInput));
        }

        private void onFingerDown(Finger finger)
        {
            foreach (BasisTouchInputDevice input in Inputs)
            {
                if (input.Finger.index == finger.index)
                {
                    return;
                }
            }
            BasisTouchInputDevice TouchInput;
            switch (finger.index)
            {
                case 0:
                    TouchInput = CreateTouchInput($"{finger.index}", "Finger Input", BasisBoneTrackedRole.LeftHand, false);
                    break;
                case 1:
                    TouchInput = CreateTouchInput($"{finger.index}", "Finger Input", BasisBoneTrackedRole.LeftHand, false);
                    break;
                case 3:
                    TouchInput = CreateTouchInput($"{finger.index}", "Finger Input", BasisBoneTrackedRole.LeftHand, false);
                    break;
                default:
                    TouchInput = CreateTouchInput($"{finger.index}", "Finger Input", BasisBoneTrackedRole.LeftHand, false);
                    break;
            }
            TouchInput.Input = BasisAvatarEyeInput;
            TouchInput.Finger = finger;
        }

        public List<BasisTouchInputDevice> Inputs  = new List<BasisTouchInputDevice>();
        public BasisTouchInputDevice CreateTouchInput(string UniqueID,string UnUniqueID,BasisBoneTrackedRole Role = BasisBoneTrackedRole.LeftHand,bool hasrole = false,string subSystems = "BasisTouchInput")
        {
            // Root GameObject representing the device
            GameObject gameObject = new GameObject(UniqueID);
            gameObject.transform.parent = BasisLocalPlayer.Instance.transform;

            // Attach simulated input component
            BasisTouchInputDevice BasisInput = gameObject.AddComponent<BasisTouchInputDevice>();
            BasisInput.InitalizeTracking(UniqueID, UnUniqueID, subSystems, hasrole, Role,true);

            // Track in local list and global device management
            if (!Inputs.Contains(BasisInput))
            {
                Inputs.Add(BasisInput);
            }
            BasisDeviceManagement.Instance.TryAdd(BasisInput);

            return BasisInput;
        }

        /// <summary>
        /// Stops the Basis SDK for desktop mode.  
        /// Removes the desktop eye input device from <see cref="BasisDeviceManagement"/> and destroys its component.
        /// </summary>
        public override void StopSDK()
        {
            BasisDeviceManagement.Instance.RemoveDevicesFrom(nameof(BasisDesktopManagement), DesktopEye);

            if (BasisAvatarEyeInput != null)
            {
                GameObject.Destroy(BasisAvatarEyeInput);
            }

            BasisAvatarEyeInput.Instance = null;
            BasisAvatarEyeInput = null;
            if (Controls != null)
            {
                Addressables.ReleaseInstance(Controls);
            }
            foreach (BasisTouchInputDevice Device in Inputs)
            {
                BasisDeviceManagement.Instance.RemoveDevicesFrom(nameof(BasisDesktopManagement), Device.UniqueDeviceIdentifier);
            }
            Inputs.Clear();
            UnityEngine.InputSystem.EnhancedTouch.Touch.onFingerDown -= onFingerDown;
            EnhancedTouchSupport.Disable();
        }

        /// <summary>
        /// Determines whether the desktop device can boot based on the provided request string.
        /// </summary>
        /// <param name="BootRequest">A string representing the requested boot device type.</param>
        /// <returns>
        /// <c>true</c> if the boot request matches <see cref="BasisConstants.Desktop"/>; otherwise, <c>false</c>.
        /// </returns>
        public override bool IsDeviceBootable(string BootRequest)
        {
            if (BootRequest == BasisConstants.Desktop)
            {
                return true;
            }
            return false;
        }
    }
}
