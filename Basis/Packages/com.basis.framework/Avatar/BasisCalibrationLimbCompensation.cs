using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// Pose-tolerant calibration pass. Run at the end of the T-pose calibration (before the avatar leaves
    /// T-pose), it reconstructs each arm/leg from its mid-joint (elbow/knee) tracker via
    /// BasisCalibrationPoseCore and:
    ///   * ALWAYS records the per-limb bend angle + quality for the calibration report (read-only), and
    ///   * when CalibrationPoseCompensation is on, re-captures the mid + end tracker offsets against the
    ///     avatar bone posed to MIRROR the player's actual bend, instead of the straight T-pose bone.
    /// The avatar's own shoulder/hip bone is the root anchor -- at calibration the avatar is scaled and
    /// head-aligned to the player, so it is a self-consistent proxy, and the forearm/shin direction (which
    /// fixes the bent limb) comes straight from the elbow+hand / knee+foot trackers. Fully guarded so a
    /// failure can never break calibration.
    /// </summary>
    public static class BasisCalibrationLimbCompensation
    {
        public struct LimbResult
        {
            public string Name;
            public bool MidPresent;   // elbow/knee tracker assigned
            public bool EndPresent;   // hand controller / foot tracker present
            public float BendDeg;     // interior angle at the mid joint at calibration (180 = straight)
            public float Quality;     // 0..1
            public bool Applied;      // pose-matched offsets were written for this limb
        }

        public static readonly List<LimbResult> LastLimbs = new List<LimbResult>(4);
        public static bool LastApplied;

        public static void Process()
        {
            LastLimbs.Clear();
            LastApplied = false;
            try
            {
                Animator animator = BasisLocalPlayer.Instance != null && BasisLocalPlayer.Instance.BasisAvatar != null
                    ? BasisLocalPlayer.Instance.BasisAvatar.Animator : null;
                if (animator == null)
                {
                    return;
                }

                bool apply = Basis.BasisUI.BasisSettingsDefaults.CalibrationPoseCompensation.RawValue;

                ProcessChain(animator, apply, "Left arm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, BasisBoneTrackedRole.LeftLowerArm, BasisBoneTrackedRole.LeftHand);
                ProcessChain(animator, apply, "Right arm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, BasisBoneTrackedRole.RightLowerArm, BasisBoneTrackedRole.RightHand);
                ProcessChain(animator, apply, "Left leg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, BasisBoneTrackedRole.LeftLowerLeg, BasisBoneTrackedRole.LeftFoot);
                ProcessChain(animator, apply, "Right leg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, BasisBoneTrackedRole.RightLowerLeg, BasisBoneTrackedRole.RightFoot);
            }
            catch (Exception e)
            {
                BasisDebug.LogWarning("Calibration pose compensation skipped: " + e.Message, BasisDebug.LogTag.Avatar);
            }
        }

        static void ProcessChain(Animator a, bool apply, string name, HumanBodyBones rootBone, HumanBodyBones midBone, HumanBodyBones endBone, BasisBoneTrackedRole midRole, BasisBoneTrackedRole endRole)
        {
            Transform rootT = a.GetBoneTransform(rootBone);
            Transform midT = a.GetBoneTransform(midBone);
            Transform endT = a.GetBoneTransform(endBone);
            if (rootT == null || midT == null || endT == null)
            {
                return;
            }

            bool hasMid = BasisDeviceManagement.Instance.FindDevice(out BasisInput midDev, midRole);
            bool hasEnd = BasisDeviceManagement.Instance.FindDevice(out BasisInput endDev, endRole);
            if (!hasEnd)
            {
                LastLimbs.Add(new LimbResult { Name = name, MidPresent = hasMid, EndPresent = false });
                return;
            }

            Vector3 avatarRoot = rootT.position;
            BasisLimbCalibrationInput input;
            input.PlayerRoot = avatarRoot;                                  // avatar shoulder/hip = player-root proxy
            input.PlayerMid = hasMid ? DeviceWorld(midDev) : midT.position;
            input.PlayerEnd = DeviceWorld(endDev);
            input.HasMid = hasMid;
            input.AvatarRoot = avatarRoot;
            input.AvatarMidTpose = midT.position;
            input.AvatarEndTpose = endT.position;
            input.AvatarMidRotTpose = midT.rotation;
            input.AvatarEndRotTpose = endT.rotation;
            input.AvatarUpperLen = Vector3.Distance(rootT.position, midT.position);
            input.AvatarLowerLen = Vector3.Distance(midT.position, endT.position);
            BasisCalibrationPoseCore.Solve(input, out BasisLimbCalibrationResult r);

            bool applied = false;
            if (apply && hasMid && r.Apply)
            {
                midDev.RecaptureOffsetWithReference(r.AvatarMidMatched);
                endDev.RecaptureOffsetWithReference(r.AvatarEndMatched);
                applied = true;
                LastApplied = true;
            }

            LastLimbs.Add(new LimbResult
            {
                Name = name, MidPresent = hasMid, EndPresent = true,
                BendDeg = r.BendAngleDeg, Quality = r.QualityScore, Applied = applied,
            });
        }

        static Vector3 DeviceWorld(BasisInput d)
        {
            return BasisLocalPlayer.localToWorldMatrix.MultiplyPoint3x4(d.ScaledDeviceCoord.position);
        }
    }
}
