#if BASIS_FRAMEWORK_EXISTS
using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using solarxr_protocol.rpc;
using UnityEngine;

namespace Basis.Integration.SlimeVR
{
    /// <summary>
    /// Runtime hub for the SlimeVR integration. Boots itself, keeps a background SolarXR client
    /// connected to any local SlimeVR server, and applies the reported body proportions to the
    /// Basis height system so users with SlimeVR never need to run a manual height calibration:
    /// eye height and arm span arrive already measured (and already refined by SlimeVR's own
    /// autobone / height calibration). Also exposes live tracker/battery state and SlimeVR's
    /// reset actions for other systems (HUDs, menus, bindings).
    /// </summary>
    public static class BasisSlimeVRBridge
    {
        public static bool IsConnected { get; private set; }
        public static bool HasBodyMetrics { get; private set; }
        public static BasisSlimeVRBodyMetrics LastBodyMetrics { get; private set; }
        public static SlimeVRSkeletonConfig LastSkeletonConfig { get; private set; }

        /// <summary>Latest tracker snapshots (physical devices first, then synthetic). Main-thread only.</summary>
        public static readonly List<SlimeVRTrackerSnapshot> Trackers = new List<SlimeVRTrackerSnapshot>();

        public static event Action<bool> OnConnectionChanged;
        public static event Action<BasisSlimeVRBodyMetrics> OnBodyMetricsChanged;
        public static event Action OnTrackersUpdated;

        private static BasisSolarXRClient _client;
        private static bool _hooked;
        private static bool _pendingSeatedApply;

        private static readonly object _trackerSwapLock = new object();
        private static List<SlimeVRTrackerSnapshot> _incomingTrackers;
        private static bool _trackerFlushQueued;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // The SlimeVR server runs on the same machine, so only desktop platforms can reach it.
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    break;
                default:
                    return;
            }

            if (!_hooked)
            {
                _hooked = true;
                Application.quitting += Shutdown;
                BasisSlimeVRSettings.Enable.OnChanged += OnEnableSettingChanged;
                BasisSlimeVRSettings.ApplyBodyMeasurements.OnChanged += OnApplySettingChanged;
                BasisSlimeVRSettings.Transport.OnChanged += OnTransportSettingChanged;
                BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnPlayersHeightChanged;
            }

            if (BasisSlimeVRSettings.Enable.RawValue)
            {
                StartClient();
            }
        }

        private static void StartClient()
        {
            if (_client != null)
            {
                return;
            }
            _client = new BasisSolarXRClient
            {
                Transport = SelectedTransport,
                Log = message => BasisDebug.Log(message, BasisDebug.LogTag.Device),
                LogError = message => BasisDebug.LogError(message, BasisDebug.LogTag.Device),
                ConnectionChanged = connected => RunOnMainThread(() => HandleConnectionChanged(connected)),
                SkeletonConfigReceived = config => RunOnMainThread(() => HandleSkeletonConfig(config)),
                TrackersReceived = QueueTrackers
            };
            _client.Start();
            BasisDebug.Log($"SlimeVR integration enabled, watching for a local SlimeVR server ({_client.Transport})", BasisDebug.LogTag.Device);
        }

        private static void StopClient()
        {
            if (_client == null)
            {
                return;
            }
            _client.Dispose();
            _client = null;
            if (IsConnected)
            {
                IsConnected = false;
                OnConnectionChanged?.Invoke(false);
            }
            Trackers.Clear();
            OnTrackersUpdated?.Invoke();
        }

        private static void Shutdown()
        {
            StopClient();
        }

        // ---- Public actions (usable from menus, bindings, other systems) ----

        /// <summary>Re-request the skeleton config right now instead of waiting for the next poll.</summary>
        public static void RefreshBodyMeasurements() => _client?.RequestSkeletonConfig();

        /// <summary>SlimeVR yaw reset (straighten trackers), same as the SlimeVR GUI button.</summary>
        public static void TriggerYawReset() => _client?.RequestReset(ResetType.Yaw);

        /// <summary>SlimeVR full reset.</summary>
        public static void TriggerFullReset() => _client?.RequestReset(ResetType.Full);

        /// <summary>SlimeVR mounting reset.</summary>
        public static void TriggerMountingReset() => _client?.RequestReset(ResetType.Mounting);

        // ---- Worker-to-main-thread plumbing ----

        private static void RunOnMainThread(Action action)
        {
            BasisDeviceManagement.mainThreadActions.Enqueue(action);
        }

        private static void QueueTrackers(List<SlimeVRTrackerSnapshot> trackers)
        {
            // Latest-wins swap with a single queued flush so a stalled main thread never
            // accumulates datafeed updates.
            lock (_trackerSwapLock)
            {
                _incomingTrackers = trackers;
                if (_trackerFlushQueued)
                {
                    return;
                }
                _trackerFlushQueued = true;
            }
            RunOnMainThread(FlushTrackers);
        }

        private static void FlushTrackers()
        {
            List<SlimeVRTrackerSnapshot> latest;
            lock (_trackerSwapLock)
            {
                latest = _incomingTrackers;
                _incomingTrackers = null;
                _trackerFlushQueued = false;
            }
            if (latest == null)
            {
                return;
            }
            Trackers.Clear();
            Trackers.AddRange(latest);
            OnTrackersUpdated?.Invoke();
        }

        private static void HandleConnectionChanged(bool connected)
        {
            IsConnected = connected;
            if (!connected)
            {
                Trackers.Clear();
                OnTrackersUpdated?.Invoke();
            }
            OnConnectionChanged?.Invoke(connected);
        }

        private static void HandleSkeletonConfig(SlimeVRSkeletonConfig config)
        {
            LastSkeletonConfig = config;
            LastBodyMetrics = BasisSlimeVRBodyMetrics.Derive(config);
            HasBodyMetrics = true;
            OnBodyMetricsChanged?.Invoke(LastBodyMetrics);
            TryApplyBodyMeasurements(LastBodyMetrics);
        }

        // ---- Applying measurements to the Basis height system ----

        private static void OnEnableSettingChanged(bool enabled)
        {
            if (enabled)
            {
                StartClient();
            }
            else
            {
                StopClient();
            }
        }

        private static void OnApplySettingChanged(bool apply)
        {
            if (apply && HasBodyMetrics)
            {
                TryApplyBodyMeasurements(LastBodyMetrics);
            }
        }

        private static SolarXRTransportKind SelectedTransport =>
            string.Equals(BasisSlimeVRSettings.Transport.RawValue, BasisSlimeVRSettings.TransportPipe, StringComparison.OrdinalIgnoreCase)
                ? SolarXRTransportKind.Pipe
                : SolarXRTransportKind.WebSocket;

        private static void OnTransportSettingChanged(string _)
        {
            if (_client == null)
            {
                return;
            }
            StopClient();
            StartClient();
        }

        private static void OnPlayersHeightChanged(BasisHeightDriver.HeightModeChange mode)
        {
            // A seated stint blocks applying (seated mode swaps in a virtual standing eye height);
            // catch up as soon as the player is standing again.
            if (mode == BasisHeightDriver.HeightModeChange.OnSitStandChanged
                && !SMModuleSitStand.IsSteatedMode
                && _pendingSeatedApply
                && HasBodyMetrics)
            {
                _pendingSeatedApply = false;
                TryApplyBodyMeasurements(LastBodyMetrics);
            }
        }

        private static void TryApplyBodyMeasurements(BasisSlimeVRBodyMetrics metrics)
        {
            if (!BasisSlimeVRSettings.ApplyBodyMeasurements.RawValue)
            {
                return;
            }
            if (!metrics.HasEyeHeight && !metrics.HasArmSpan)
            {
                return;
            }
            if (SMModuleSitStand.IsSteatedMode)
            {
                _pendingSeatedApply = true;
                return;
            }

            // SlimeVR's user height is the raw HMD height above the SteamVR floor — the same
            // device origin the live capture samples — so the eye-mode denominator still needs
            // the backend's device-origin->eye lift. CalculatePlayerEyeHeight is the only other
            // writer of that lift, and a genuine SlimeVR height suppresses that capture on avatar
            // loads; without this refresh the applied scale would depend on whether a manual
            // calibration happened to run first this session.
            var headInput = BasisLocalCameraDriver.Instance?.BasisLockToInput?.BasisInput;
            if (headInput != null)
            {
                BasisHeightDriver.PlayerCenterEyeVerticalOffset = headInput.CenterEyeVerticalOffset;
            }

            bool changed = false;

            float eyeHeight = metrics.EyeHeightMeters;
            bool eyePlausible = eyeHeight >= BasisHeightDriver.MinPlausibleBodyMeasure
                && eyeHeight <= BasisHeightDriver.MaxPlausibleBodyMeasure;
            if (eyePlausible
                && (Mathf.Abs(BasisHeightDriver.PlayerEyeHeight - eyeHeight) > 0.005f
                    || !BasisHeightDriver.HasGenuinePlayerEyeHeight
                    || !BasisHeightDriver.HasUserCalibratedHeight))
            {
                BasisHeightDriver.PlayerEyeHeight = eyeHeight;
                Basis.BasisUI.BasisSettingsDefaults.SavedPlayerEyeHeight.SetValue(eyeHeight);
                changed = true;
            }

            float armSpan = metrics.ControllerSpanMeters;
            if (Basis.BasisUI.BasisSettingsDefaults.FBIKArmHeightRatioEnabled.RawValue)
            {
                // The arm-height-ratio override owns the span (CapturePlayerHeight derives it
                // from the eye height); keep that invariant against the just-applied eye instead
                // of stomping the override with the measured controller span every config poll.
                armSpan = BasisHeightDriver.PlayerEyeHeight
                    * Mathf.Max(0.1f, Basis.BasisUI.BasisSettingsDefaults.FBIKArmHeightRatio.RawValue);
            }
            bool spanPlausible = armSpan >= BasisHeightDriver.MinPlausibleBodyMeasure
                && armSpan <= BasisHeightDriver.MaxPlausibleBodyMeasure;
            if (spanPlausible && Mathf.Abs(BasisHeightDriver.PlayerArmSpan - armSpan) > 0.005f)
            {
                BasisHeightDriver.PlayerArmSpan = armSpan;
                Basis.BasisUI.BasisSettingsDefaults.SavedPlayerArmSpan.SetValue(armSpan);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            // The measurements come from SlimeVR's calibrated skeleton, so they count as a real,
            // user-calibrated body size: the live auto-scale estimator must not fight them and
            // avatar loads must reuse them instead of re-polling a stance-dependent HMD sample.
            BasisHeightDriver.HasGenuinePlayerEyeHeight = true;
            BasisHeightDriver.HasUserCalibratedHeight = true;

            if (BasisLocalPlayer.Instance != null)
            {
                BasisHeightDriver.ApplyScaleAndHeight();
            }

            BasisDebug.Log($"Applied SlimeVR body measurements: {metrics}", BasisDebug.LogTag.Device);
        }
    }
}
#endif
