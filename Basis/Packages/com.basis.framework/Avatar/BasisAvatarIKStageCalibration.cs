using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// this class handles tracker calibration onto the IK system.
    /// </summary>
    public static class BasisAvatarIKStageCalibration
    {
        public static class BasisHintBiasStore
        {
            public static readonly Dictionary<BasisBoneTrackedRole, Vector3> LocalOffset = new();
            public static void Set(BasisBoneTrackedRole role, Vector3 localOffset) => LocalOffset[role] = localOffset;
            public static bool TryGet(BasisBoneTrackedRole role, out Vector3 localOffset) => LocalOffset.TryGetValue(role, out localOffset);
        }

        /// <summary>
        /// If Any trackers are actively connected to the IK system
        /// </summary>
        public static bool HasFBIKTrackers = false;

        /// <summary>
        /// gets all roles in a desired order
        /// </summary>
        private static List<BasisBoneTrackedRole> GetAllRolesDesired()
        {
            List<BasisBoneTrackedRole> rolesToDiscover = new List<BasisBoneTrackedRole>(23);
            foreach (BasisBoneTrackedRole role in Enum.GetValues(typeof(BasisBoneTrackedRole)))
            {
                rolesToDiscover.Add(role);
            }

            Dictionary<BasisBoneTrackedRole, int> orderLookup = new Dictionary<BasisBoneTrackedRole, int>();
            for (int i = 0; i < desiredOrder.Length; i++)
                orderLookup[desiredOrder[i]] = i;

            int largeIndex = desiredOrder.Length;

            rolesToDiscover.Sort((x, y) =>
            {
                int ix = orderLookup.ContainsKey(x) ? orderLookup[x] : largeIndex;
                int iy = orderLookup.ContainsKey(y) ? orderLookup[y] : largeIndex;
                return ix.CompareTo(iy);
            });

            return rolesToDiscover;
        }

        /// <summary>
        /// does calibration of trackers
        /// </summary>
        public static void FullBodyCalibration()
        {
            BasisHeightDriver.OnAvatarFBCalibration(); // avatar height is good, player height is needed
            HasFBIKTrackers = false;

            BasisDeviceManagement.UnassignFBTrackers();
            BasisLocalPlayer.Instance.LocalBoneDriver.SimulateAndApplyWithoutLerp(BasisLocalPlayer.Instance);

            // now that we have latest * scale we can run calibration
            BasisLocalPlayer.Instance.LocalAvatarDriver.PutAvatarIntoTPose();
            BasisLocalPlayer.Instance.DriveTpose(); // update the avatars position.

            Dictionary<BasisBoneTrackedRole, Transform> storedRoleTransforms = GetAllRolesAsTransform();

            List<BasisBoneTrackedRole> rolesToDiscover = GetAllRolesDesired();

            List<BasisBoneTrackedRole> trackInputRoles = new List<BasisBoneTrackedRole>(23);

            // IMPORTANT: connectors no longer store Distance (distance is per mapping)
            List<BasisInput> connectors = new List<BasisInput>(23);

            List<BasisTrackerMapping> boneTransformMappings = new List<BasisTrackerMapping>(23);

            List<BasisBoneTrackedRole> usedRoles = new List<BasisBoneTrackedRole>(23);
            List<BasisInput> usedInputs = new List<BasisInput>(23);

            // Build list of tracker-roles we want to assign
            for (int i = 0; i < rolesToDiscover.Count; i++)
            {
                BasisBoneTrackedRole role = rolesToDiscover[i];
                if (BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role))
                {
                    trackInputRoles.Add(role);
                }
            }

            // Gather all input devices as connectors
            int allInputCount = BasisDeviceManagement.Instance.AllInputDevices.Count;
            for (int i = 0; i < allInputCount; i++)
            {
                BasisInput baseInput = BasisDeviceManagement.Instance.AllInputDevices[i];
                if (baseInput == null)
                {
                    continue;
                }

                if (baseInput.TryGetRole(out BasisBoneTrackedRole role))
                {
                    if (BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role))
                    {
                        // in use un assign first
                        baseInput.UnAssignFullBodyTrackers();
                    }
                }

                // whether it had a role or not, it's a candidate connector
                connectors.Add(baseInput);
            }

            // Choose an avatar root transform for side computation (local X)
            // Using the player transform is okay; using hips transform is often even better.
            Transform avatarRootForSide = BasisLocalPlayer.Instance.transform;
            if (storedRoleTransforms.TryGetValue(BasisBoneTrackedRole.Hips, out Transform hipsT) && hipsT != null)
            {
                avatarRootForSide = hipsT; // optional: hips as side reference
            }

            // Build mappings (one per target role) with per-mapping candidates filtered by distance + side
            for (int i = 0; i < trackInputRoles.Count; i++)
            {
                BasisBoneTrackedRole role = trackInputRoles[i];

                if (BasisLocalPlayer.Instance.LocalBoneDriver.FindBone(out BasisLocalBoneControl control, role))
                {
                    float scaledDistance = MaxDistanceBeforeTrackerIsIrrelivant(role) * BasisHeightDriver.ScaledToMatchValue;

                    if (storedRoleTransforms.TryGetValue(role, out Transform roleT) && roleT != null)
                    {
                        var mapping = new BasisTrackerMapping(
                            control,
                            roleT,
                            avatarRootForSide,
                            role,
                            connectors,
                            scaledDistance,
                            sideDeadZoneMeters: 0.03f * BasisHeightDriver.ScaledToMatchValue
                        );

                        boneTransformMappings.Add(mapping);
                    }
                    else
                    {
                        BasisDebug.LogError($"Missing Mapping in Roles Transforms {role}");
                    }
                }
                else
                {
                    BasisDebug.LogError($"Missing bone control for role {role}");
                }
            }

            // Assign trackers (greedy, but now deterministic + side-safe)
            for (int i = 0; i < boneTransformMappings.Count; i++)
            {
                BasisTrackerMapping mapping = boneTransformMappings[i];
                if (mapping.TargetControl != null)
                {
                    FindTrackersFromInputs(mapping, ref usedInputs, ref usedRoles);
                }
                else
                {
                    BasisDebug.LogError("Missing Tracker for index " + i + " with ID " + mapping);
                }
            }

            // IMPORTANT: simulate once AFTER assignments so the bone controls reflect new tracker bindings.
            BasisLocalPlayer.Instance.LocalBoneDriver.SimulateAndApplyWithoutLerp(BasisLocalPlayer.Instance);

            ComputeHints(storedRoleTransforms);

            BasisLocalPlayer.Instance.LocalAvatarDriver.ResetAvatarAnimator();
            BasisLocalPlayer.Instance.LocalRigDriver.RigLayer.active = true;
            BasisLocalPlayer.Instance.LocalAnimatorDriver.AssignHipsFBTracker();
        }
        public static Dictionary<BasisBoneTrackedRole, Transform> GetAllRolesAsTransform()
        {
            Common.BasisTransformMapping Mapping = BasisLocalAvatarDriver.Mapping;

            Dictionary<BasisBoneTrackedRole, Transform> transforms =
                new Dictionary<BasisBoneTrackedRole, Transform>
                {
            { BasisBoneTrackedRole.Hips, Mapping.Hips },
         //   { BasisBoneTrackedRole.Spine, Mapping.spine },
            { BasisBoneTrackedRole.Chest, Mapping.chest },
          //  { BasisBoneTrackedRole.Neck, Mapping.neck },
           // { BasisBoneTrackedRole.Head, Mapping.head },

            { BasisBoneTrackedRole.LeftShoulder, Mapping.leftShoulder },
            { BasisBoneTrackedRole.RightShoulder, Mapping.RightShoulder },

            { BasisBoneTrackedRole.LeftUpperArm, Mapping.leftUpperArm },
            { BasisBoneTrackedRole.RightUpperArm, Mapping.RightUpperArm },

            { BasisBoneTrackedRole.LeftLowerArm, Mapping.leftLowerArm },
            { BasisBoneTrackedRole.RightLowerArm, Mapping.RightLowerArm },

            { BasisBoneTrackedRole.LeftHand, Mapping.leftHand },
            { BasisBoneTrackedRole.RightHand, Mapping.rightHand },

            { BasisBoneTrackedRole.LeftUpperLeg, Mapping.LeftUpperLeg },
            { BasisBoneTrackedRole.RightUpperLeg, Mapping.RightUpperLeg },

            { BasisBoneTrackedRole.LeftLowerLeg, Mapping.LeftLowerLeg },
            { BasisBoneTrackedRole.RightLowerLeg, Mapping.RightLowerLeg },

            { BasisBoneTrackedRole.LeftFoot, Mapping.leftFoot },
            { BasisBoneTrackedRole.RightFoot, Mapping.rightFoot },

            { BasisBoneTrackedRole.LeftToes, Mapping.leftToe },
            { BasisBoneTrackedRole.RightToes, Mapping.rightToe },
                };

            return transforms;
        }
        /// <summary>
        /// Finds trackers from the basis input system.
        /// Uses left/right side filtering to prevent mirrored swaps.
        /// </summary>
        public static void FindTrackersFromInputs(
            BasisTrackerMapping mapping,
            ref List<BasisInput> usedInputs,
            ref List<BasisBoneTrackedRole> usedRoles)
        {
            int requiredSide = mapping.BasisBoneControlRole.SideSign(); // -1 left, +1 right, 0 center

            for (int i = 0; i < mapping.Candidates.Count; i++)
            {
                var cand = mapping.Candidates[i];
                if (cand.BasisInput == null) continue;

                if (usedInputs.Contains(cand.BasisInput)) continue;
                if (usedRoles.Contains(mapping.BasisBoneControlRole)) continue;

                // Extra safety: if role is left/right, reject opposite side candidates.
                // Unknown side (0) is allowed.
                if (requiredSide != 0 && cand.SideSign != 0 && cand.SideSign != requiredSide)
                    continue;

                usedRoles.Add(mapping.BasisBoneControlRole);
                usedInputs.Add(cand.BasisInput);

                HasFBIKTrackers = true;
                cand.BasisInput.ApplyTrackerCalibration(mapping.BasisBoneControlRole);
                break;
            }
        }

        /// <summary>
        /// each roles radius before outside of attempt
        /// </summary>
        public static float MaxDistanceBeforeTrackerIsIrrelivant(BasisBoneTrackedRole role)
        {
            switch (role)
            {
                case BasisBoneTrackedRole.CenterEye: return 0;
                case BasisBoneTrackedRole.Head: return 0;
                case BasisBoneTrackedRole.Neck: return 0;
                case BasisBoneTrackedRole.Mouth: return 0;
                case BasisBoneTrackedRole.Spine: return 0;

                case BasisBoneTrackedRole.Chest: return 0.35f;
                case BasisBoneTrackedRole.Hips: return 0.45f;

                case BasisBoneTrackedRole.LeftLowerLeg: return 0.5f;
                case BasisBoneTrackedRole.RightLowerLeg: return 0.5f;

                case BasisBoneTrackedRole.LeftFoot: return 0.35f;
                case BasisBoneTrackedRole.RightFoot: return 0.35f;

                case BasisBoneTrackedRole.LeftShoulder: return 0.3f;
                case BasisBoneTrackedRole.RightShoulder: return 0.3f;

                case BasisBoneTrackedRole.LeftUpperLeg: return 0.3f;
                case BasisBoneTrackedRole.RightUpperLeg: return 0.3f;

                case BasisBoneTrackedRole.LeftLowerArm: return 0.4f;
                case BasisBoneTrackedRole.RightLowerArm: return 0.4f;

                case BasisBoneTrackedRole.LeftHand: return 0.2f;
                case BasisBoneTrackedRole.RightHand: return 0.2f;

                case BasisBoneTrackedRole.LeftToes: return 0.2f;
                case BasisBoneTrackedRole.RightToes: return 0.2f;

                case BasisBoneTrackedRole.LeftUpperArm: return 0;
                case BasisBoneTrackedRole.RightUpperArm: return 0;

                default:
                    BasisDebug.LogError($"Unknown role {role}");
                    return 0;
            }
        }

        /// <summary>
        /// order we should build tracker pairs in
        /// </summary>
        public static BasisBoneTrackedRole[] desiredOrder = new BasisBoneTrackedRole[]
        {
            BasisBoneTrackedRole.Hips,
            BasisBoneTrackedRole.RightFoot,
            BasisBoneTrackedRole.LeftFoot,

            BasisBoneTrackedRole.LeftLowerLeg,
            BasisBoneTrackedRole.RightLowerLeg,
            BasisBoneTrackedRole.LeftLowerArm,
            BasisBoneTrackedRole.RightLowerArm,

            BasisBoneTrackedRole.CenterEye,
            BasisBoneTrackedRole.Chest,

        //    BasisBoneTrackedRole.Head,
      //      BasisBoneTrackedRole.Neck,

           // BasisBoneTrackedRole.LeftHand,
           // BasisBoneTrackedRole.RightHand,

            BasisBoneTrackedRole.LeftToes,
            BasisBoneTrackedRole.RightToes,

            BasisBoneTrackedRole.LeftUpperArm,
            BasisBoneTrackedRole.RightUpperArm,
            BasisBoneTrackedRole.LeftUpperLeg,
            BasisBoneTrackedRole.RightUpperLeg,
            BasisBoneTrackedRole.LeftShoulder,
            BasisBoneTrackedRole.RightShoulder,
        };

        public static void ComputeHints(Dictionary<BasisBoneTrackedRole, Transform> storedRoleTransforms)
        {
            Quaternion chestRefRot = Quaternion.identity;
            Quaternion hipsRefRot = Quaternion.identity;

            if (storedRoleTransforms.TryGetValue(BasisBoneTrackedRole.Chest, out var chestT) && chestT != null)
                chestRefRot = chestT.rotation;

            if (storedRoleTransforms.TryGetValue(BasisBoneTrackedRole.Hips, out var hipsT) && hipsT != null)
                hipsRefRot = hipsT.rotation;

            float hs = BasisHeightDriver.ScaledToMatchValue;

            float elbowPush = 0.12f * hs;
            float kneePush = 0.10f * hs;
            float headPush = 0.08f * hs;

            float maxPush = 0.25f * hs;

            // Chest-as-head-hint bias (push "up" in chest frame)
            {
                var chestCtrl = BasisLocalBoneDriver.ChestControl;
                if (chestCtrl != null && chestCtrl.HasTracked == BasisHasTracked.HasTracker)
                {
                    Quaternion trackerRot = chestCtrl.OutgoingWorldData.rotation;

                    Vector3 worldUp = chestRefRot * Vector3.up;
                    Vector3 localUp = Quaternion.Inverse(trackerRot) * worldUp;
                    Vector3 localOffset = (localUp.sqrMagnitude < 1e-8f ? Vector3.up : localUp.normalized) * headPush;
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);

                    BasisHintBiasStore.Set(BasisBoneTrackedRole.Chest, localOffset);
                }
                else
                {
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.Chest, Vector3.up * headPush);
                }
            }

            // Elbow hints (lower arms)
            {
                var lla = BasisLocalBoneDriver.LeftLowerArmControl;
                if (lla != null && lla.HasTracked == BasisHasTracked.HasTracker)
                {
                    Quaternion trackerRot = lla.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, chestRefRot, isLeft: true, distanceMeters: elbowPush, outWeight: 0.85f, upWeight: 0.35f, fwdWeight: 0.15f);
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.LeftLowerArm, localOffset);
                }

                var rla = BasisLocalBoneDriver.RightLowerArmControl;
                if (rla != null && rla.HasTracked == BasisHasTracked.HasTracker)
                {
                    Quaternion trackerRot = rla.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, chestRefRot, isLeft: false, distanceMeters: elbowPush, outWeight: 0.85f, upWeight: 0.35f, fwdWeight: 0.15f);
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.RightLowerArm, localOffset);
                }
            }

            // Knee hints (lower legs)
            {
                var lll = BasisLocalBoneDriver.LeftLowerLegControl;
                if (lll != null && lll.HasTracked == BasisHasTracked.HasTracker)
                {
                    Quaternion trackerRot = lll.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, hipsRefRot, isLeft: true, distanceMeters: kneePush, outWeight: 0.55f, upWeight: 0.25f, fwdWeight: 0.55f);
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.LeftLowerLeg, localOffset);
                }

                var rll = BasisLocalBoneDriver.RightLowerLegControl;
                if (rll != null && rll.HasTracked == BasisHasTracked.HasTracker)
                {
                    Quaternion trackerRot = rll.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, hipsRefRot, isLeft: false, distanceMeters: kneePush, outWeight: 0.55f, upWeight: 0.25f, fwdWeight: 0.55f);
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.RightLowerLeg, localOffset);
                }
            }
        }

        // Helper local function to compute a tracker-local offset vector that points "up and out"
        static Vector3 ComputeHintBiasLocal(
            Quaternion trackerWorldRot,
            Quaternion referenceWorldRot,   // chest for arms, hips for legs
            bool isLeft,
            float distanceMeters,           // already scaled
            float outWeight = 0.85f,
            float upWeight = 0.35f,
            float fwdWeight = 0.00f
        )
        {
            Vector3 up = referenceWorldRot * Vector3.up;
            Vector3 outDir = referenceWorldRot * (isLeft ? Vector3.left : Vector3.right);
            Vector3 fwd = referenceWorldRot * Vector3.forward;

            Vector3 worldDir = (outDir * outWeight + up * upWeight + fwd * fwdWeight);
            if (worldDir.sqrMagnitude < 1e-8f) worldDir = up;
            worldDir.Normalize();

            Vector3 localDir = Quaternion.Inverse(trackerWorldRot) * worldDir;
            if (localDir.sqrMagnitude < 1e-8f) localDir = Vector3.up;

            return localDir.normalized * distanceMeters;
        }
    }
}
