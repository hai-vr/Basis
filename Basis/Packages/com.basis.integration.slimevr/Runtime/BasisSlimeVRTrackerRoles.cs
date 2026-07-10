#if BASIS_FRAMEWORK_EXISTS
using System;
using System.Collections.Generic;
using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Integration.SlimeVR
{
    /// <summary>
    /// Bypasses the manual tracker calibration for trackers that already announce their body part.
    /// Two conventions are honored: SlimeVR's virtual SteamVR trackers carry it in their serial
    /// ("human://WAIST", "human://LEFT_FOOT", ...), and SteamVR encodes any tracker's user-assigned
    /// role — whatever the brand — in its controller type ("vive_tracker_waist", ...). When such
    /// trackers appear this forces the matching Basis role through a runtime tracker-role override
    /// and runs the standard FullBodyCalibration pass once the set has settled — the classifier
    /// binds the forced roles without scoring while the pass still captures the tracker-to-bone
    /// offsets, per-effector rotation calibration and bend hints, exactly like a menu-triggered
    /// calibration. Trackers announcing nothing keep going through the normal geometric
    /// classification in the same pass.
    /// </summary>
    public static class BasisSlimeVRTrackerRoles
    {
        private const string SerialPrefix = "human://";
        private const string ControllerTypePrefix = "vive_tracker_";
        private const float ScanIntervalSeconds = 2f;
        private const float CalibrationCooldownSeconds = 10f;

        /// <summary>
        /// SlimeVR serial body-part token to Basis role. SlimeVR's KNEE/ELBOW trackers ride the
        /// upper-leg/upper-arm segment ends, which are Basis's LowerLeg/LowerArm tracker roles.
        /// HEAD and the HANDs are deliberately absent: the HMD and controllers own those bones.
        /// </summary>
        private static readonly Dictionary<string, BasisBoneTrackedRole> RolesBySerialPart =
            new Dictionary<string, BasisBoneTrackedRole>(StringComparer.OrdinalIgnoreCase)
        {
            { "WAIST", BasisBoneTrackedRole.Hips },
            { "CHEST", BasisBoneTrackedRole.Chest },
            { "LEFT_FOOT", BasisBoneTrackedRole.LeftFoot },
            { "RIGHT_FOOT", BasisBoneTrackedRole.RightFoot },
            { "LEFT_KNEE", BasisBoneTrackedRole.LeftLowerLeg },
            { "RIGHT_KNEE", BasisBoneTrackedRole.RightLowerLeg },
            { "LEFT_ELBOW", BasisBoneTrackedRole.LeftLowerArm },
            { "RIGHT_ELBOW", BasisBoneTrackedRole.RightLowerArm },
        };

        /// <summary>
        /// SteamVR controller-type role token to Basis role, the generic path for any tracker
        /// whose role was assigned in SteamVR settings. handed/camera/keyboard are intentionally
        /// absent (not body parts), and so are wrist/ankle: Basis's LowerArm/LowerLeg roles expect
        /// elbow/knee placement, so those are safer left to the geometric classifier.
        /// </summary>
        private static readonly Dictionary<string, BasisBoneTrackedRole> RolesByControllerType =
            new Dictionary<string, BasisBoneTrackedRole>(StringComparer.OrdinalIgnoreCase)
        {
            { "waist", BasisBoneTrackedRole.Hips },
            { "chest", BasisBoneTrackedRole.Chest },
            { "left_foot", BasisBoneTrackedRole.LeftFoot },
            { "right_foot", BasisBoneTrackedRole.RightFoot },
            { "left_knee", BasisBoneTrackedRole.LeftLowerLeg },
            { "right_knee", BasisBoneTrackedRole.RightLowerLeg },
            { "left_elbow", BasisBoneTrackedRole.LeftLowerArm },
            { "right_elbow", BasisBoneTrackedRole.RightLowerArm },
            { "left_shoulder", BasisBoneTrackedRole.LeftShoulder },
            { "right_shoulder", BasisBoneTrackedRole.RightShoulder },
        };

        private static readonly HashSet<string> _ownedOverrideIds = new HashSet<string>();
        private static readonly List<string> _staleOverrideIds = new List<string>();
        private static float _nextScanTime;
        private static float _pendingSince = -1f;
        private static float _lastCalibrationTime = float.NegativeInfinity;
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // SlimeVR's virtual trackers only exist where a local SteamVR can run.
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

            if (_hooked)
            {
                return;
            }
            _hooked = true;
            BasisDeviceManagement.OnDeviceManagementLoop += Scan;
        }

        private static void Scan()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextScanTime)
            {
                return;
            }
            _nextScanTime = now + ScanIntervalSeconds;

            if (!BasisSlimeVRSettings.AutoAssignRoles.RawValue)
            {
                ReleaseAllOverrides();
                return;
            }
            BasisDeviceManagement management = BasisDeviceManagement.Instance;
            if (management == null
                || BasisLocalPlayer.Instance == null
                || BasisLocalPlayer.Instance.LocalAvatarDriver == null)
            {
                return;
            }

            bool anyNeedsRole = false;
            _staleOverrideIds.Clear();
            _staleOverrideIds.AddRange(_ownedOverrideIds);

            int count = management.AllInputDevices.Count;
            for (int Index = 0; Index < count; Index++)
            {
                BasisInput input = management.AllInputDevices[Index];
                if (input == null || input.IsLinked || !TryMapInput(input, out BasisBoneTrackedRole role))
                {
                    continue;
                }

                BasisTrackerRoleOverride.SetRuntimeOverride(input.UniqueDeviceIdentifier, role);
                _ownedOverrideIds.Add(input.UniqueDeviceIdentifier);
                _staleOverrideIds.Remove(input.UniqueDeviceIdentifier);

                // Only count trackers the calibration settings would actually let take the role,
                // otherwise a role disabled in Tracker Settings would re-trigger forever.
                bool roleEnabled = Basis.BasisUI.BasisSettingsDefaults.EnableFBT.RawValue
                    && Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration(role);
                if (roleEnabled && (!input.TryGetRole(out BasisBoneTrackedRole current) || current != role))
                {
                    anyNeedsRole = true;
                }
            }

            // Ids that no longer resolve to a live SlimeVR tracker (SteamVR restarts hand out new
            // device indices) must not linger — a reused index could inherit the wrong role.
            for (int Index = 0; Index < _staleOverrideIds.Count; Index++)
            {
                BasisTrackerRoleOverride.ClearRuntimeOverride(_staleOverrideIds[Index]);
                _ownedOverrideIds.Remove(_staleOverrideIds[Index]);
            }

            if (!anyNeedsRole)
            {
                _pendingSince = -1f;
                return;
            }

            // First sighting arms the pass; firing on the next scan gives the rest of the tracker
            // set a scan interval to arrive so one calibration covers them all.
            if (_pendingSince < 0f)
            {
                _pendingSince = now;
                return;
            }
            if (now - _lastCalibrationTime < CalibrationCooldownSeconds)
            {
                return;
            }

            _pendingSince = -1f;
            _lastCalibrationTime = now;
            BasisDebug.Log("SlimeVR trackers announced their body parts; running automatic full-body calibration", BasisDebug.LogTag.Device);
            BasisAvatarIKStageCalibration.FullBodyCalibration();
        }

        private static void ReleaseAllOverrides()
        {
            if (_ownedOverrideIds.Count == 0)
            {
                return;
            }
            foreach (string id in _ownedOverrideIds)
            {
                BasisTrackerRoleOverride.ClearRuntimeOverride(id);
            }
            _ownedOverrideIds.Clear();
            _pendingSince = -1f;
        }

        private static bool TryMapInput(BasisInput input, out BasisBoneTrackedRole role)
        {
            // SlimeVR's serial convention is the most specific claim, so it wins over the
            // SteamVR role (they agree on SlimeVR trackers anyway — SlimeVR sets both).
            string serial = input.DeviceSerial;
            if (!string.IsNullOrEmpty(serial) && serial.StartsWith(SerialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return RolesBySerialPart.TryGetValue(serial.Substring(SerialPrefix.Length).Trim(), out role);
            }

            string controllerType = input.DeviceControllerType;
            if (!string.IsNullOrEmpty(controllerType) && controllerType.StartsWith(ControllerTypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return RolesByControllerType.TryGetValue(controllerType.Substring(ControllerTypePrefix.Length).Trim(), out role);
            }

            role = BasisBoneTrackedRole.CenterEye;
            return false;
        }
    }
}
#endif
