#if BASIS_FRAMEWORK_EXISTS
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Integration.MetaBodyTracking
{
    /// <summary>
    /// A Basis input device whose pose is one joint of the headset's body tracking solve. Created and
    /// driven by <see cref="BasisMetaBodyTrackerSource"/>. It carries a "metabody://..." serial so
    /// role assignment (BasisAnnouncedTrackerRoles) and calibration treat it like any other tracker
    /// that announces its body part; this class only supplies poses.
    /// </summary>
    public class BasisMetaBodyTrackerInput : BasisInput
    {
        /// <summary>Joint this device reads; the feature resolves its pose once per frame.</summary>
        public BasisMetaBodyJoint Joint;

        public void Initialize(string uniqueID, string unUniqueID, string subSystem, string deviceSerial, BasisMetaBodyJoint joint)
        {
            Joint = joint;
            DeviceSerial = deviceSerial;
            TrackingHardware = BasisTrackingHardware.Estimated;
            InitializeTracking(uniqueID, unUniqueID, subSystem, false, BasisBoneTrackedRole.CenterEye);
        }

        public override void RenderPollData()
        {
            if (!PollPose())
            {
                return;
            }
            UpdateInputEvents();
            ComputeRaycastDirection(ScaledDeviceCoord.position, ScaledDeviceCoord.rotation, Quaternion.identity);
        }

        public override void LateDoPollData()
        {
            PollPose();
        }

        private bool PollPose()
        {
            BasisMetaBodyTrackingFeature.EnsureLocated();
            if (!BasisMetaBodyTrackingFeature.TryGetJoint(Joint, out Vector3 position, out Quaternion rotation))
            {
                return false;
            }

            // The joints are located against the OpenXR app space, the same space the runtime reports
            // the head and controllers in, so they take the same seated/play-space vertical shift.
            ComputeUnscaledDeviceCoord(ref UnscaledDeviceCoord, position);
            UnscaledDeviceCoord.rotation = rotation;
            ConvertToScaledDeviceCoord();
            ControlOnlyAsDevice();
            return true;
        }

        public override void ShowTrackedVisual()
        {
            // Nothing physical to draw for a solved joint; role gizmos still render from the bone system.
        }

        public override void PlayHaptic(float duration = 0.25F, float amplitude = 0.5F, float frequency = 0.5F)
        {
        }

        public override void PlaySoundEffect(string SoundEffectName, float Volume)
        {
            PlaySoundEffectDefaultImplementation(SoundEffectName, Volume);
        }
    }
}
#endif
