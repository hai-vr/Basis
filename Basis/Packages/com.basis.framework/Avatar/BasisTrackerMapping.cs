using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// the Basis Tracker Mapper goal is to calculate Distances between Input Devices
    /// and produce a per-mapping candidate list (DO NOT mutate shared connector data).
    /// </summary>
    [System.Serializable]
    public class BasisTrackerMapping
    {
        [SerializeField] public BasisLocalBoneControl TargetControl;
        [SerializeField] public BasisBoneTrackedRole BasisBoneControlRole;

        /// <summary>
        /// Per-mapping computed candidates (input + computed distance + side sign).
        /// </summary>
        [SerializeField] public List<BasisCalibrationCandidate> Candidates = new();

        public Vector3 CalibrationPoint;

        public BasisTrackerMapping(
            BasisLocalBoneControl bone,
            Transform avatarRoleTransform,
            Transform avatarRootForSide, // avatar root (or hips) used to compute left/right in avatar-local space
            BasisBoneTrackedRole role,
            List<BasisInput> calibration,
            float calibrationMaxDistance,
            float sideDeadZoneMeters = 0.03f
        )
        {
            CalibrationPoint = (avatarRoleTransform == null) ? bone.OutgoingWorldData.position : avatarRoleTransform.position;

            TargetControl = bone;
            BasisBoneControlRole = role;

            Candidates = new List<BasisCalibrationCandidate>(calibration.Count);

            int requiredSide = role.SideSign(); // -1 left, +1 right, 0 center

            for (int i = 0; i < calibration.Count; i++)
            {
                var input = calibration[i];
                if (input == null)
                {
                    continue;
                }

                Vector3 inputWorldPos = input.transform.position;

                float dist = Vector3.Distance(CalibrationPoint, inputWorldPos);
                if (dist >= calibrationMaxDistance)
                {
                    continue;
                }

                int inputSide = ComputeInputSideSign(inputWorldPos, avatarRootForSide, sideDeadZoneMeters);

                // If the role is explicitly left/right, do not allow opposite-side candidates.
                // Unknown side (0) is allowed (centerline / deadzone).
                if (requiredSide != 0 && inputSide != 0 && inputSide != requiredSide)
                {
                    continue;
                }

                Candidates.Add(new BasisCalibrationCandidate
                {
                    BasisInput = input,
                    Distance = dist,
                    SideSign = inputSide
                });
            }

            Candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        private static int ComputeInputSideSign(Vector3 inputWorldPos, Transform avatarRoot, float deadZoneMeters)
        {
            if (avatarRoot == null) return 0;

            // Avatar-local X: negative = left, positive = right (Unity convention)
            Vector3 local = avatarRoot.InverseTransformPoint(inputWorldPos);

            if (Mathf.Abs(local.x) <= deadZoneMeters) return 0;
            return local.x < 0f ? -1 : 1;
        }
    }

    [System.Serializable]
    public struct BasisCalibrationCandidate
    {
        public BasisInput BasisInput;
        public float Distance;
        public int SideSign; // -1 left, +1 right, 0 center/unknown
    }
}
