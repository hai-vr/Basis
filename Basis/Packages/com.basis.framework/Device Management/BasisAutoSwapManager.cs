using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Basis.Scripts.Device_Management
{
    /// <summary>
    /// Monitors VR headset user-presence (proximity sensor / isPresent) and triggers
    /// automatic soft-swaps between VR and Desktop mode without shutting down the XR runtime.
    /// Attach to the same GameObject as <see cref="BasisDeviceManagement"/>.
    /// </summary>
    public class BasisAutoSwapManager : MonoBehaviour
    {
        /// <summary>
        /// The committed presence state. A swap only fires when <see cref="_lastKnownPresence"/>
        /// differs from this for at least <see cref="DebounceSeconds"/>.
        /// </summary>
        private bool _lastKnownPresence = true;

        /// <summary>
        /// Accumulates time while the detected presence differs from <see cref="_lastKnownPresence"/>.
        /// Resets whenever presence matches the committed state again (flicker rejection).
        /// </summary>
        private float _stateTimer;

        /// <summary>
        /// How long the headset must remain in the new state before triggering a swap.
        /// </summary>
        public float DebounceSeconds = 1.5f;

        /// <summary>
        /// Whether this manager is actively polling headset presence.
        /// </summary>
        private bool _isMonitoring;

        /// <summary>
        /// Guard flag to prevent overlapping swap operations.
        /// </summary>
        private bool _swapInProgress;

        public void StartMonitoring()
        {
            _isMonitoring = true;
            _lastKnownPresence = CheckHeadsetPresence();
            _stateTimer = 0f;
            BasisDebug.Log("AutoSwap: Monitoring started", BasisDebug.LogTag.Device);
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _stateTimer = 0f;
            BasisDebug.Log("AutoSwap: Monitoring stopped", BasisDebug.LogTag.Device);
        }

        private void Update()
        {
            if (!_isMonitoring || _swapInProgress) return;

            // Only monitor when auto swap is enabled
            if (!BasisSettingsSystem.LoadBool("autoswap_enabled", false))
            {
                _stateTimer = 0f;
                return;
            }

            // Only act when we're either in VR or soft-swapped to Desktop
            var dm = BasisDeviceManagement.Instance;
            if (dm == null) return;
            if (!BasisDeviceManagement.IsCurrentModeVR() && !dm.IsSoftSwapped) return;

            bool currentPresence = CheckHeadsetPresence();

            if (currentPresence != _lastKnownPresence)
            {
                _stateTimer += Time.deltaTime;

                if (_stateTimer >= DebounceSeconds)
                {
                    _lastKnownPresence = currentPresence;
                    _stateTimer = 0f;
                    OnPresenceChanged(currentPresence);
                }
            }
            else
            {
                // Presence matches committed state — reset debounce
                _stateTimer = 0f;
            }
        }

        /// <summary>
        /// Queries the XR subsystem for headset user-presence via <see cref="CommonUsages.userPresence"/>.
        /// Falls back to the last known state if no presence data is available (e.g., HMD lacks proximity sensor).
        /// </summary>
        private bool CheckHeadsetPresence()
        {
            var devices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, devices);

            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].TryGetFeatureValue(CommonUsages.userPresence, out bool isPresent))
                {
                    return isPresent;
                }
            }

            // No presence data available — return last known state to avoid false triggers
            return _lastKnownPresence;
        }

        private async void OnPresenceChanged(bool isPresent)
        {
            var dm = BasisDeviceManagement.Instance;
            if (dm == null) return;

            _swapInProgress = true;
            try
            {
                if (!isPresent)
                {
                    BasisDebug.Log("AutoSwap: Headset removed — switching to Desktop", BasisDebug.LogTag.Device);
                    await dm.SoftSwitchToDesktop();
                }
                else
                {
                    BasisDebug.Log("AutoSwap: Headset detected — switching to VR", BasisDebug.LogTag.Device);
                    await dm.SoftSwitchToVR();
                }
            }
            catch (System.Exception e)
            {
                BasisDebug.LogError($"AutoSwap: Swap failed — {e}", BasisDebug.LogTag.Device);
            }
            finally
            {
                _swapInProgress = false;
            }
        }
    }
}
