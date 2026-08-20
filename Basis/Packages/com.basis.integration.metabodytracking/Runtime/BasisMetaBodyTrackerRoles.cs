#if BASIS_FRAMEWORK_EXISTS
using System;
using Basis.Scripts.Avatar;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Integration.MetaBodyTracking
{
    /// <summary>
    /// Registers the body tracking announcement convention with the framework's announced-role
    /// scanner (<see cref="BasisAnnouncedTrackerRoles"/>): these devices carry their body part in
    /// their serial ("metabody://WAIST", "metabody://LEFT_FOOT", ...). Gated by AutoBindTrackers
    /// (default on); with it off the serial still claims the device so it falls through to the
    /// normal manual calibration rather than to the SteamVR-role path.
    /// </summary>
    public static class BasisMetaBodyTrackerRoles
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            BasisAnnouncedTrackerRoles.RegisterSource(MapSerial);
        }

        private static BasisAnnouncedTrackerRoles.RoleClaim MapSerial(BasisInput input, out BasisBoneTrackedRole role)
        {
            role = BasisBoneTrackedRole.CenterEye;
            string serial = input.DeviceSerial;
            if (string.IsNullOrEmpty(serial) || !serial.StartsWith(BasisMetaBodyTrackerSource.SerialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return BasisAnnouncedTrackerRoles.RoleClaim.NotMine;
            }
            if (!BasisMetaBodyTrackingSettings.AutoBindTrackers.RawValue)
            {
                return BasisAnnouncedTrackerRoles.RoleClaim.Suppress;
            }
            string token = serial.Substring(BasisMetaBodyTrackerSource.SerialPrefix.Length).Trim();
            return BasisMetaBodyTrackerSource.TryGetRoleForSerialToken(token, out role)
                ? BasisAnnouncedTrackerRoles.RoleClaim.Mapped
                : BasisAnnouncedTrackerRoles.RoleClaim.Suppress;
        }
    }
}
#endif
