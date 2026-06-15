using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Pairing;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
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
            public static void Clear() => LocalOffset.Clear();
        }

        /// <summary>
        /// Read-only snapshot of the most recent constellation calibration pass. Populated
        /// each time FullBodyCalibration runs and consumed by the editor visualizer. Live
        /// runtime never reads this — flipping any field cannot affect avatar behavior.
        /// </summary>
        public static class ConstellationDebug
        {
            public class DebugSample
            {
                public string DeviceId;
                public Vector3 BodyLocal;        // unscaled, body-relative; z = depth
                public Vector3 RawUnscaled;      // raw world-space unscaled pose at calibration time
                public float HeightRatio;
                public float LateralRatio;
                public bool Assigned;
                public BasisBoneTrackedRole AssignedRole;
                public float AssignedScore;
                public BasisBoneTrackedRole BestAnyRole;   // top-scoring role even if rejected
                public float BestAnyScore;
                public bool NearOrigin;          // raw unscaled was ≈ (0,0,0); strong indicator of a stale/missing device poll
            }

            public class DebugPrior
            {
                public BasisBoneTrackedRole Role;
                public float ExpectedHeight;
                public float ExpectedLateral;
                public float HeightSigma;
                public float LateralSigma;
                public bool Enabled;             // matches BasisSettingsDefaults toggle at calibration time
                public int AssignedSampleIndex;  // -1 if no tracker bound to this role
            }

            public static bool HasSnapshot;
            public static double Timestamp;
            public static string Status = "no calibration captured yet";
            public static float EyeHeight;
            public static float ArmReach;
            public static Vector3 BodyOrigin;
            public static Quaternion BodyRotation;
            public static readonly List<DebugSample> Samples = new List<DebugSample>(16);
            public static readonly List<DebugPrior> Priors = new List<DebugPrior>(16);

            public static float AcceptThreshold => BasisConstellationClassifier.AcceptThreshold;

            public static void Reset(string reason)
            {
                HasSnapshot = false;
                Timestamp = 0;
                Status = reason;
                EyeHeight = 0;
                ArmReach = 0;
                BodyOrigin = Vector3.zero;
                BodyRotation = Quaternion.identity;
                Samples.Clear();
                Priors.Clear();
            }
        }
        private struct TrackerSample
        {
            public BasisInput Input;
            public float HeightRatio;   // y / eyeHeight: 0 ≈ floor, 1 ≈ HMD
            public float LateralRatio;  // signed x / eyeHeight: +x = body's right
            public Vector3 BodyLocal;   // raw body-relative position (unscaled); z = depth, kept for debug visualization only
            public bool NearOrigin;     // tracker's UnscaledDeviceCoord came back ≈ Vector3.zero — almost always a stale/missing poll
        }

        private readonly struct BoneRolePrior
        {
            public readonly BasisBoneTrackedRole Role;
            public readonly float ExpectedHeightRatio;
            public readonly float ExpectedLateralRatio;
            public readonly float HeightSigma;
            public readonly float LateralSigma;

            public BoneRolePrior(BasisBoneTrackedRole role, float h, float lat, float hSigma, float latSigma)
            {
                Role = role;
                ExpectedHeightRatio = h;
                ExpectedLateralRatio = lat;
                HeightSigma = hSigma;
                LateralSigma = latSigma;
            }
        }
        /// <summary>
        /// data for ik calibration
        /// </summary>
        public class BasisCalibrationData
        {
            [SerializeField]
            public BasisInput BasisInput;
            public float Distance;
            public int SideSign; // -1 left, +1 right, 0 center/unknown
        }
        /// <summary>
        /// If Any trackers are actively connected to the IK system
        /// </summary>
        public static bool HasFBIKTrackers = false;
        /// <summary>
        /// Builds a tracker→role assignment from the player's T-pose constellation alone.
        /// The avatar is no longer the source of truth for "where should this tracker be";
        /// instead the HMD defines a body frame and each tracker is classified by its
        /// height-above-floor and lateral offset, normalized to the calibrated player eye
        /// height. ComputeHints below still consults the avatar (chest/hips reference
        /// rotations), but the role-matching pass itself is avatar-independent — so the
        /// same trackers map the same way whether the user wears a child avatar or a
        /// three-meter giant.
        /// </summary>
        /// <summary>Raised at the end of <see cref="FullBodyCalibration"/>, after tracker roles have been (re)assigned.</summary>
        public static System.Action OnFullBodyCalibrated;

        public static void FullBodyCalibration()
        {
            BasisCalibrationDebugRecorder.Begin("fbt_recalib");
            try
            {
                BasisHeightDriver.OnAvatarFBCalibration();//avatar height is good,player height is needed
                HasFBIKTrackers = false;
                BasisHintBiasStore.Clear();
                BasisDeviceManagement.UnassignFBTrackers();
                BasisLocalPlayer.Instance.LocalBoneDriver.SimulateAndApplyWithoutLerp(BasisLocalPlayer.Instance);

                // Avatar still goes into T-pose because ComputeHints reads chest/hips reference
                // rotations from it. The classifier itself doesn't touch the avatar.
                BasisLocalPlayer.Instance.LocalAvatarDriver.PutAvatarIntoTPose();
                BasisLocalPlayer.Instance.DriveTpose();

                Dictionary<BasisBoneTrackedRole, Transform> storedRoleTransforms = BasisLocalPlayer.Instance.LocalAvatarDriver.StoredRolesTransforms;

                try
                {
                    ClassifyAndAssignTrackersFromTPose();
                    LogConstellation();

                    // IMPORTANT: simulate once AFTER assignments so the bone controls reflect new tracker bindings.
                    BasisLocalPlayer.Instance.LocalBoneDriver.SimulateAndApplyWithoutLerp(BasisLocalPlayer.Instance);

                    LogFbikRotationCalibration();
                    RecomputeFbikRotationCalibration();

                    ComputeHints(storedRoleTransforms);
                }
                finally
                {
                    BasisLocalPlayer.Instance.LocalAvatarDriver.ResetAvatarAnimator();
                    BasisLocalPlayer.Instance.LocalRigDriver.RigLayer.active = true;
                }

                BasisLocalPlayer.Instance.LocalAnimatorDriver.AssignHipsFBTracker();

                // Refresh the per-role calibration spheres so they re-anchor to the
                // newly stored avatar bone transforms. No-op when the ShowGizmos
                // master toggle is off; the toggle path rebuilds when it flips on.
                BasisLocalPlayer.Instance.LocalBoneDriver.RebuildCalibrationSpheres();

                OnFullBodyCalibrated?.Invoke();
            }
            finally
            {
                BasisCalibrationDebugRecorder.Flush();
            }
        }

        // Issue #531 rotation diagnostic: dumps the FBIK per-effector rotation calibration
        // (frozen at rig setup in CreateBasisFullBodyRIG, never recomputed on recalibration)
        // alongside what a fresh recompute against the current T-pose bone/avatar frames would
        // produce. If 'frozen' stays constant across recalibrations while 'freshRecompute'
        // tracks the moved tracker, the freeze is the rotation residue.
        private static void LogFbikRotationCalibration()
        {
            var rig = BasisLocalPlayer.Instance.LocalRigDriver;
            if (rig == null || rig.BasisFullIKConstraint == null) return;
            var data = rig.BasisFullIKConstraint.data;
            Common.BasisTransformMapping Mapping = BasisLocalAvatarDriver.Mapping;

            LogFbikRole("Head", data.m_CalibratedRotationHead, BasisLocalBoneDriver.HeadControl, Mapping.head);
            LogFbikRole("Hips", data.OffsetRotationHips, BasisLocalBoneDriver.HipsControl, Mapping.Hips);
            LogFbikRole("Chest", data.m_CalibratedRotationChest, BasisLocalBoneDriver.ChestControl, Mapping.chest);
            LogFbikRole("LeftFoot", data.M_CalibrationLeftFootRotation, BasisLocalBoneDriver.LeftFootControl, Mapping.leftFoot);
            LogFbikRole("RightFoot", data.M_CalibrationRightFootRotation, BasisLocalBoneDriver.RightFootControl, Mapping.rightFoot);
            LogFbikRole("LeftToe", data.m_CalibratedRotationLeftToe, BasisLocalBoneDriver.LeftToeControl, Mapping.leftToe);
            LogFbikRole("RightToe", data.m_CalibratedRotationRightToe, BasisLocalBoneDriver.RightToeControl, Mapping.rightToe);
        }

        private static void LogFbikRole(string label, Quaternion frozen, BasisLocalBoneControl control, Transform avatarBone)
        {
            BasisCalibrationDebugRecorder.Rotation("FbikRotCalib", label, "frozen", frozen);
            if (control != null)
            {
                BasisCalibrationDebugRecorder.Rotation("FbikRotCalib", label, "boneOutWorld", control.OutgoingWorldData.rotation);
            }
            if (avatarBone != null)
            {
                BasisCalibrationDebugRecorder.Rotation("FbikRotCalib", label, "avatarBoneWorld", avatarBone.rotation);
            }
            if (control != null && avatarBone != null)
            {
                Quaternion fresh = Quaternion.Inverse(control.OutgoingWorldData.rotation) * avatarBone.rotation;
                BasisCalibrationDebugRecorder.Rotation("FbikRotCalib", label, "freshRecompute", fresh);
            }
        }

        // Issue #531 rotation fix: CreateBasisFullBodyRIG captures the FBIK per-effector rotation
        // offsets once at avatar load against the pre-calibration bone frame and never refreshes
        // them. The constraint applies finalRot = boneOutgoingWorld * offset, so a stale offset
        // leaves the avatar rotated off by the body-frame drift since load (only an app restart
        // re-captured it). Recompute them here, in T-pose after the post-assignment simulate,
        // with the same formula CreateBasisFullBodyRIG uses, so finalRot lands back on the avatar
        // T-pose for the frame the user actually calibrated in.
        private static void RecomputeFbikRotationCalibration()
        {
            var rig = BasisLocalPlayer.Instance.LocalRigDriver;
            if (rig == null || rig.BasisFullIKConstraint == null) return;
            var data = rig.BasisFullIKConstraint.data;
            Common.BasisTransformMapping Mapping = BasisLocalAvatarDriver.Mapping;

            BasisLocalRigDriver.RecalibratedHead = FreshRotationOffset(BasisLocalBoneDriver.HeadControl, Mapping.head, data.m_CalibratedRotationHead);
            BasisLocalRigDriver.RecalibratedHips = FreshRotationOffset(BasisLocalBoneDriver.HipsControl, Mapping.Hips, data.OffsetRotationHips);
            BasisLocalRigDriver.RecalibratedChest = FreshRotationOffset(BasisLocalBoneDriver.ChestControl, Mapping.chest, data.m_CalibratedRotationChest);
            BasisLocalRigDriver.RecalibratedLeftFoot = FreshRotationOffset(BasisLocalBoneDriver.LeftFootControl, Mapping.leftFoot, data.M_CalibrationLeftFootRotation);
            BasisLocalRigDriver.RecalibratedRightFoot = FreshRotationOffset(BasisLocalBoneDriver.RightFootControl, Mapping.rightFoot, data.M_CalibrationRightFootRotation);
            BasisLocalRigDriver.RecalibratedLeftToe = FreshRotationOffset(BasisLocalBoneDriver.LeftToeControl, Mapping.leftToe, data.m_CalibratedRotationLeftToe);
            BasisLocalRigDriver.RecalibratedRightToe = FreshRotationOffset(BasisLocalBoneDriver.RightToeControl, Mapping.rightToe, data.m_CalibratedRotationRightToe);
            BasisLocalRigDriver.RecalibratedLeftShoulder = FreshRotationOffset(BasisLocalBoneDriver.LeftShoulderControl, Mapping.leftShoulder, data.m_CalibratedRotationLeftShoulder);
            BasisLocalRigDriver.RecalibratedRightShoulder = FreshRotationOffset(BasisLocalBoneDriver.RightShoulderControl, Mapping.RightShoulder, data.m_CalibratedRotationRightShoulder);
            BasisLocalRigDriver.HasRecalibratedRotationOffsets = true;
        }

        private static Quaternion FreshRotationOffset(BasisLocalBoneControl control, Transform avatarBone, Quaternion current)
        {
            if (control == null || avatarBone == null) return current;
            return Quaternion.Inverse(control.OutgoingWorldData.rotation) * avatarBone.rotation;
        }

        // Arm-tracker calibration diagnostic: dumps every constellation sample (including the
        // UNASSIGNED ones the OffsetCapture rows can't show) plus the per-role priors, so a tracker
        // that "refused to calibrate" reveals its reason — NearOrigin (stale/zero poll), its best-fit
        // role + score (below the ~-9 accept threshold = didn't fit any role), or which role it lost to.
        private static void LogConstellation()
        {
            if (!BasisCalibrationDebugRecorder.Enabled) return;

            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            var samples = ConstellationDebug.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                ConstellationDebug.DebugSample s = samples[i];
                string note = $"dev={s.DeviceId} nearOrigin={s.NearOrigin} assigned={s.Assigned} role={s.AssignedRole} bestAny={s.BestAnyRole} bestScore={s.BestAnyScore.ToString("F2", ci)}";
                BasisCalibrationDebugRecorder.Pose("Constellation", "sample" + i.ToString(ci), "sample",
                    new Vector3(s.HeightRatio, s.LateralRatio, s.BestAnyScore), Quaternion.identity, Vector3.one, note);
            }

            var priors = ConstellationDebug.Priors;
            for (int p = 0; p < priors.Count; p++)
            {
                ConstellationDebug.DebugPrior pr = priors[p];
                string note = $"enabled={pr.Enabled} assignedSampleIdx={pr.AssignedSampleIndex}";
                BasisCalibrationDebugRecorder.Pose("Constellation", pr.Role.ToString(), "prior",
                    new Vector3(pr.ExpectedHeight, pr.ExpectedLateral, 0f), Quaternion.identity,
                    new Vector3(pr.HeightSigma, pr.LateralSigma, 0f), note);
            }

            // Every input device, including ones the classifier filters out before scoring (linked
            // pair-half, role-pinned, disabled-role, or polled at origin) so a tracker that never
            // becomes a sample is still visible — that's where a "refused to calibrate" tracker hides.
            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance.AllInputDevices : null;
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    BasisInput input = devices[i];
                    if (input == null) continue;
                    bool hasRole = input.TryGetRole(out BasisBoneTrackedRole r);
                    bool pinned = input.DeviceMatchSettings != null && input.DeviceMatchSettings.HasTrackedRole;
                    bool fbCapable = BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(r);
                    Vector3 uns = input.UnscaledDeviceCoord.position;
                    bool nearOrigin = uns.sqrMagnitude < ConstellationNearOriginEpsilonSqr;
                    string note = $"common={input.CommonDeviceIdentifier} role={(hasRole ? r.ToString() : "(none)")} fbRole={(hasRole && fbCapable)} linked={input.IsLinked} pinned={pinned} nearOrigin={nearOrigin} dev={input.UniqueDeviceIdentifier}";
                    BasisCalibrationDebugRecorder.Pose("Devices", "device" + i.ToString(ci), "device",
                        uns, input.UnscaledDeviceCoord.rotation, Vector3.one, note);
                }
            }

            BasisCalibrationDebugRecorder.Meta("ConstellationStatus", ConstellationDebug.Status);
        }

        /// <summary>
        /// Classifies every free FB-trackable device by its position in the player's T-pose
        /// and assigns roles. Pure geometry — no avatar lookup, no tracker metadata.
        /// </summary>
        private static void ClassifyAndAssignTrackersFromTPose()
        {
            ConstellationDebug.Reset("calibration in progress");
            ConstellationDebug.Timestamp = System.DateTime.UtcNow.ToOADate();

            if (!TryGetHmdPose(out Vector3 hmdUnscaledPos, out Quaternion hmdUnscaledRot, out BasisInput hmdDevice))
            {
                ConstellationDebug.Status = "HMD pose unavailable — no trackers assigned";
                BasisDebug.LogError("FBIK constellation calibration: HMD pose unavailable, no trackers assigned", BasisDebug.LogTag.Input);
                return;
            }

            // All positions in this classifier are read from UnscaledDeviceCoord (raw playspace,
            // pre-scale). PlayerEyeHeight lives in the same frame, so HeightRatios reduce to
            // tracker height above the playspace floor as a fraction of the player's eye height
            // — independent of the avatar's DeviceScale. Reading transform.position here would
            // mix world (post-scale) with PlayerEyeHeight (pre-scale) and cause Hips↔Chest to
            // flip whenever the avatar isn't the same size as the player.
            float eyeHeight = Mathf.Max(BasisHeightDriver.PlayerEyeHeight, 0.5f);
            float floorY = hmdUnscaledPos.y - eyeHeight;

            // Body forward = HMD facing projected onto the horizontal plane. In T-pose the
            // player should be looking straight ahead, so the projection is well defined.
            Vector3 hmdFwdHoriz = hmdUnscaledRot * Vector3.forward;
            hmdFwdHoriz.y = 0f;
            if (hmdFwdHoriz.sqrMagnitude < 1e-4f) hmdFwdHoriz = BasisLocalPlayer.Instance.transform.forward;
            hmdFwdHoriz.Normalize();

            Quaternion bodyRot = Quaternion.LookRotation(hmdFwdHoriz, Vector3.up);
            Quaternion bodyRotInv = Quaternion.Inverse(bodyRot);
            Vector3 bodyOrigin = new Vector3(hmdUnscaledPos.x, floorY, hmdUnscaledPos.z);

            ConstellationDebug.EyeHeight = eyeHeight;
            ConstellationDebug.BodyOrigin = bodyOrigin;
            ConstellationDebug.BodyRotation = bodyRot;

            List<TrackerSample> samples = CollectFreeFbTrackerSamples(bodyOrigin, bodyRotInv, eyeHeight, hmdDevice);
            CaptureSampleSnapshots(samples);

            if (samples.Count == 0)
            {
                ConstellationDebug.Status = "no free FB-trackable devices found";
                ConstellationDebug.HasSnapshot = true;
                return;
            }

            float calibrationTolerance = GetCalibrationTolerance();

            // Map the live samples to the pure classifier's scalar inputs, resolving any user
            // role override per tracker. BasisConstellationClassifier owns the priors, scoring,
            // and greedy assignment (shared with the editor Tracker Placement Sweep); this method
            // just feeds it live device data and applies the roles it returns.
            int sampleCount = samples.Count;
            BasisConstellationSample[] classifierSamples = new BasisConstellationSample[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                TrackerSample ts = samples[i];
                int forcedRole = -1;
                if (ts.Input != null && !ts.NearOrigin && TryResolveOverride(ts.Input, out BasisBoneTrackedRole overrideRole))
                {
                    forcedRole = (int)overrideRole;
                }
                classifierSamples[i] = new BasisConstellationSample
                {
                    HeightRatio = ts.HeightRatio,
                    LateralRatio = ts.LateralRatio,
                    DepthLocal = ts.BodyLocal.z,
                    NearOrigin = ts.NearOrigin,
                    ForcedRole = forcedRole,
                };
            }

            BasisConstellationHand leftHand = ReadHandForClassifier(BasisBoneTrackedRole.LeftHand, bodyOrigin, bodyRotInv, eyeHeight);
            BasisConstellationHand rightHand = ReadHandForClassifier(BasisBoneTrackedRole.RightHand, bodyOrigin, bodyRotInv, eyeHeight);

            BasisConstellationResult classified = BasisConstellationClassifier.Classify(
                classifierSamples, sampleCount, leftHand, rightHand, calibrationTolerance,
                Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration);

            ConstellationDebug.ArmReach = classified.ArmReach;

            // Snapshot the full recentered prior set (pre enabled-filter) for the visualizer and
            // in-VR spheres, exactly as the old inline path did before the toggle filter.
            CapturePriorSnapshots(classified.Priors);

            bool anyRoleEnabled = false;
            for (int i = 0; i < classified.Priors.Length; i++)
            {
                if (Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration(classified.Priors[i].Role))
                {
                    anyRoleEnabled = true;
                    break;
                }
            }
            if (!anyRoleEnabled)
            {
                ConstellationDebug.Status = "all FB roles disabled in calibration toggles";
                ComputeBestAnyFits(samples);
                ConstellationDebug.HasSnapshot = true;
                return;
            }

            // Apply the classifier's decisions. Devices were already unassigned in
            // CollectFreeFbTrackerSamples, so one ApplyTrackerCalibration per device lands the
            // final (post toe-swap) role; the bone controls are simulated by the caller next.
            for (int i = 0; i < sampleCount; i++)
            {
                if (!classified.TryGetRole(i, out BasisBoneTrackedRole role))
                {
                    continue;
                }
                BasisInput input = samples[i].Input;
                if (input == null)
                {
                    continue;
                }
                float score = classified.AssignedScore[i];
                BasisDebug.Log($"FBIK constellation: '{input.UniqueDeviceIdentifier}' -> {role} (h={samples[i].HeightRatio:F2}, lat={samples[i].LateralRatio:F2}, score={score:F2}, {classified.AssignedKind[i]})", BasisDebug.LogTag.Input);
                input.ApplyTrackerCalibration(role);
                HasFBIKTrackers = true;
                RecordAssignment(i, role, score);
            }

            ComputeBestAnyFits(samples);
            ConstellationDebug.Status = $"{classified.AssignedCount} of {samples.Count} tracker(s) assigned";
            ConstellationDebug.HasSnapshot = true;
        }

        /// <summary>
        /// Walks samples, applies any user-set role override for each tracker
        /// directly (skipping the constellation scoring), and marks both the
        /// sample and the chosen role as used so the greedy classifier can't
        /// touch them. For virtual midpoint samples we also accept an override
        /// recorded against either physical half — the user pairs trackers in
        /// the UI by physical id, and the merged virtual is what calibration
        /// sees, so we have to bridge between the two ids here.
        /// </summary>
        private static int ApplyForcedRoleOverrides(List<TrackerSample> samples, BoneRolePrior[] priors, bool[] sampleUsed, bool[] roleUsed)
        {
            int forced = 0;
            for (int s = 0; s < samples.Count; s++)
            {
                if (sampleUsed[s]) continue;
                TrackerSample sample = samples[s];
                if (sample.Input == null) continue;
                if (sample.NearOrigin) continue;

                if (!TryResolveOverride(sample.Input, out BasisBoneTrackedRole forcedRole))
                {
                    continue;
                }

                int roleIdx = -1;
                for (int r = 0; r < priors.Length; r++)
                {
                    if (priors[r].Role == forcedRole)
                    {
                        roleIdx = r;
                        break;
                    }
                }
                // Override targets a role the classifier wouldn't normally
                // assign (toggled off, or off-list like Spine). The user
                // explicitly asked for it, so honor the override and just skip
                // the roleUsed bookkeeping — the classifier was never going to
                // pick that role anyway.
                if (roleIdx >= 0 && roleUsed[roleIdx])
                {
                    BasisDebug.Log($"FBIK constellation: override for '{sample.Input.UniqueDeviceIdentifier}' -> {forcedRole} skipped (role already taken by another override)", BasisDebug.LogTag.Input);
                    continue;
                }

                BasisDebug.Log($"FBIK constellation: '{sample.Input.UniqueDeviceIdentifier}' -> {forcedRole} (forced override)", BasisDebug.LogTag.Input);
                sample.Input.ApplyTrackerCalibration(forcedRole);
                sampleUsed[s] = true;
                if (roleIdx >= 0) roleUsed[roleIdx] = true;
                HasFBIKTrackers = true;

                // ConstellationDebug records the assignment with score=+inf to
                // mark "this didn't go through scoring". Negative scores in the
                // visualizer mean "tight fit"; +inf reads as "no fit needed".
                RecordAssignment(s, forcedRole, float.PositiveInfinity);
                forced++;
            }
            return forced;
        }

        /// <summary>
        /// Gives Hips first dibs: binds the best-fitting free tracker to Hips before the greedy
        /// pass so the pelvis anchor is claimed ahead of every other role. The accept threshold
        /// still applies, so only a real torso-height tracker is eligible. No-op when Hips is
        /// toggled off, already taken by a forced override, or nothing clears the threshold.
        /// </summary>
        private static int AssignHipsFirst(List<TrackerSample> samples, BoneRolePrior[] priors, bool[] sampleUsed, bool[] roleUsed)
        {
            int hipsIdx = FindPriorIndex(priors, BasisBoneTrackedRole.Hips);
            if (hipsIdx < 0 || roleUsed[hipsIdx]) return 0;

            float bestScore = ConstellationAcceptThreshold;
            int bestSampleIdx = -1;
            for (int s = 0; s < samples.Count; s++)
            {
                if (sampleUsed[s]) continue;
                TrackerSample sample = samples[s];
                if (sample.NearOrigin) continue;
                float score = ScoreSampleAgainstRole(sample, priors[hipsIdx]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSampleIdx = s;
                }
            }
            if (bestSampleIdx < 0) return 0;

            TrackerSample chosen = samples[bestSampleIdx];
            BasisDebug.Log($"FBIK constellation: '{chosen.Input.UniqueDeviceIdentifier}' -> Hips (first dibs, h={chosen.HeightRatio:F2}, lat={chosen.LateralRatio:F2}, score={bestScore:F2})", BasisDebug.LogTag.Input);
            chosen.Input.ApplyTrackerCalibration(BasisBoneTrackedRole.Hips);
            sampleUsed[bestSampleIdx] = true;
            roleUsed[hipsIdx] = true;
            HasFBIKTrackers = true;
            RecordAssignment(bestSampleIdx, BasisBoneTrackedRole.Hips, bestScore);
            return 1;
        }

        /// <summary>
        /// Resolves a user-set override for the given input. Plain inputs use
        /// their own id. Virtual midpoints check both physical halves so an
        /// override saved against either tracker before pairing still applies.
        /// </summary>
        private static bool TryResolveOverride(BasisInput input, out BasisBoneTrackedRole role)
        {
            if (BasisTrackerRoleOverride.TryGetOverride(input.UniqueDeviceIdentifier, out role))
            {
                return true;
            }
            if (input is BasisVirtualMidpointInput midpoint)
            {
                if (midpoint.PartnerA != null
                    && BasisTrackerRoleOverride.TryGetOverride(midpoint.PartnerA.UniqueDeviceIdentifier, out role))
                {
                    return true;
                }
                if (midpoint.PartnerB != null
                    && BasisTrackerRoleOverride.TryGetOverride(midpoint.PartnerB.UniqueDeviceIdentifier, out role))
                {
                    return true;
                }
            }
            role = BasisBoneTrackedRole.CenterEye;
            return false;
        }

        private static void CaptureSampleSnapshots(List<TrackerSample> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                TrackerSample s = samples[i];
                string id = s.Input != null ? s.Input.UniqueDeviceIdentifier : null;
                Vector3 raw = s.Input != null ? s.Input.UnscaledDeviceCoord.position : Vector3.zero;
                ConstellationDebug.Samples.Add(new ConstellationDebug.DebugSample
                {
                    DeviceId = string.IsNullOrEmpty(id) ? "(unknown)" : id,
                    BodyLocal = s.BodyLocal,
                    RawUnscaled = raw,
                    HeightRatio = s.HeightRatio,
                    LateralRatio = s.LateralRatio,
                    Assigned = false,
                    NearOrigin = s.NearOrigin,
                });
            }
        }

        private static void CapturePriorSnapshots(BasisConstellationPrior[] priors)
        {
            for (int i = 0; i < priors.Length; i++)
            {
                BoneRolePrior p = priors[i];
                ConstellationDebug.Priors.Add(new ConstellationDebug.DebugPrior
                {
                    Role = p.Role,
                    ExpectedHeight = p.ExpectedHeightRatio,
                    ExpectedLateral = p.ExpectedLateralRatio,
                    HeightSigma = p.HeightSigma,
                    LateralSigma = p.LateralSigma,
                    Enabled = Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration(p.Role),
                    AssignedSampleIndex = -1,
                });
            }
        }

        private static void RecordAssignment(int sampleIdx, BasisBoneTrackedRole role, float score)
        {
            if (sampleIdx < 0 || sampleIdx >= ConstellationDebug.Samples.Count) return;
            ConstellationDebug.DebugSample ds = ConstellationDebug.Samples[sampleIdx];
            ds.Assigned = true;
            ds.AssignedRole = role;
            ds.AssignedScore = score;
            UpdatePriorAssignment(role, sampleIdx);
        }

        private static void UpdatePriorAssignment(BasisBoneTrackedRole role, int sampleIdx)
        {
            for (int p = 0; p < ConstellationDebug.Priors.Count; p++)
            {
                if (ConstellationDebug.Priors[p].Role == role)
                {
                    ConstellationDebug.Priors[p].AssignedSampleIndex = sampleIdx;
                    break;
                }
            }
        }

        private static void ApplyToeForwardConstraint(List<TrackerSample> samples, BasisBoneTrackedRole footRole, BasisBoneTrackedRole toeRole)
        {
            int footIdx = -1, toesIdx = -1;
            for (int i = 0; i < ConstellationDebug.Samples.Count; i++)
            {
                ConstellationDebug.DebugSample ds = ConstellationDebug.Samples[i];
                if (!ds.Assigned) continue;
                if (ds.AssignedRole == footRole) footIdx = i;
                else if (ds.AssignedRole == toeRole) toesIdx = i;
            }
            if (footIdx < 0 || toesIdx < 0) return;

            float footZ = samples[footIdx].BodyLocal.z;
            float toesZ = samples[toesIdx].BodyLocal.z;
            if (footZ <= toesZ + ConstellationToeForwardEpsilon) return;

            BasisInput footInput = samples[footIdx].Input;
            BasisInput toesInput = samples[toesIdx].Input;
            footInput.UnAssignFullBodyTrackers();
            toesInput.UnAssignFullBodyTrackers();
            footInput.ApplyTrackerCalibration(toeRole);
            toesInput.ApplyTrackerCalibration(footRole);

            ConstellationDebug.Samples[footIdx].AssignedRole = toeRole;
            ConstellationDebug.Samples[toesIdx].AssignedRole = footRole;
            UpdatePriorAssignment(toeRole, footIdx);
            UpdatePriorAssignment(footRole, toesIdx);

            BasisDebug.Log($"FBIK constellation: swap {footRole}/{toeRole} — '{footInput.UniqueDeviceIdentifier}' (z={footZ:F2}) sat forward of '{toesInput.UniqueDeviceIdentifier}' (z={toesZ:F2})", BasisDebug.LogTag.Input);
        }

        private static void ComputeBestAnyFits(List<TrackerSample> samples)
        {
            // Best-scoring prior for each sample regardless of acceptance — lets the
            // visualizer answer "where would this tracker have gone if nothing else
            // were competing for that role?"
            for (int s = 0; s < samples.Count; s++)
            {
                if (s >= ConstellationDebug.Samples.Count) break;
                TrackerSample sample = samples[s];
                float best = float.NegativeInfinity;
                BasisBoneTrackedRole bestRole = BasisBoneTrackedRole.Hips;
                for (int p = 0; p < ConstellationDebug.Priors.Count; p++)
                {
                    ConstellationDebug.DebugPrior dp = ConstellationDebug.Priors[p];
                    float dh = (sample.HeightRatio - dp.ExpectedHeight) / dp.HeightSigma;
                    float dl = (sample.LateralRatio - dp.ExpectedLateral) / dp.LateralSigma;
                    float score = -(dh * dh + dl * dl);
                    if (score > best) { best = score; bestRole = dp.Role; }
                }
                ConstellationDebug.Samples[s].BestAnyRole = bestRole;
                ConstellationDebug.Samples[s].BestAnyScore = best;
            }
        }

        private static bool TryGetHmdPose(out Vector3 unscaledPos, out Quaternion unscaledRot, out BasisInput hmdDevice)
        {
            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance.AllInputDevices;
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                BasisInput input = devices[i];
                if (input == null) continue;
                if (!input.TryGetRole(out BasisBoneTrackedRole role)) continue;
                if (role == BasisBoneTrackedRole.CenterEye || role == BasisBoneTrackedRole.Head)
                {
                    // UnscaledDeviceCoord only refreshes when LateDoPollData runs. If FullBodyCalibration
                    // is invoked outside the normal frame loop (UI button during Update, etc.) the cached
                    // value can be stale or zero — force a fresh poll before reading.
                    input.LatePollData();
                    unscaledPos = input.UnscaledDeviceCoord.position;
                    unscaledRot = input.UnscaledDeviceCoord.rotation;
                    hmdDevice = input;
                    return true;
                }
            }
            unscaledPos = Vector3.zero;
            unscaledRot = Quaternion.identity;
            hmdDevice = null;
            return false;
        }

        private static List<TrackerSample> CollectFreeFbTrackerSamples(Vector3 bodyOrigin, Quaternion bodyRotInv, float eyeHeight, BasisInput hmdDevice)
        {
            List<TrackerSample> samples = new List<TrackerSample>(16);
            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance.AllInputDevices;
            int count = devices.Count;
            for (int Index = 0; Index < count; Index++)
            {
                BasisInput input = devices[Index];
                if (input == null)
                {
                    BasisDebug.LogError("Missing Input this should never occur!", BasisDebug.LogTag.IK);
                    continue;
                }
                // Never reassign the HMD itself — even if it has no role assigned, its
                // position would otherwise score against Chest and we'd happily glue the
                // headset to the player's torso.
                if (input == hmdDevice) continue;

                // Linked half of a tracker pair — the merged virtual midpoint device
                // emits the sample for the pair, so the physical bases bail out here
                // and never compete for a role on their own.
                if (input.IsLinked) continue;

                // Devices the matcher pinned to a role (HMD, named hand controllers) keep
                // their role no matter what.
                if (input.DeviceMatchSettings != null && input.DeviceMatchSettings.HasTrackedRole) continue;

                // Devices currently bound to a role that the user has not enabled for
                // calibration (e.g. controllers acting as hands, or shoulders by default)
                // are off-limits — only roles ticked in the bone editor participate.
                if (input.TryGetRole(out BasisBoneTrackedRole existing) && !Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration(existing))
                {
                    continue;
                }

                // UnassignFBTrackers() at the top of FullBodyCalibration already cleared any
                // prior FB role; this is defensive in case a tracker came online late.
                input.UnAssignFullBodyTrackers();

                // Force a fresh poll so UnscaledDeviceCoord reflects the current device pose. A stale
                // (zero) read here would classify the tracker at HeightRatio ≈ 0 and pin it to a foot
                // role, dragging the avatar into the floor.
                input.LatePollData();
                Vector3 unscaledPos = input.UnscaledDeviceCoord.position;
                bool nearOrigin = unscaledPos.sqrMagnitude < ConstellationNearOriginEpsilonSqr;
                if (nearOrigin)
                {
                    string id = string.IsNullOrEmpty(input.UniqueDeviceIdentifier) ? "(unknown)" : input.UniqueDeviceIdentifier;
                    BasisDebug.LogError($"FBIK constellation: tracker '{id}' polled at world origin ({unscaledPos.x:F3},{unscaledPos.y:F3},{unscaledPos.z:F3}). UnscaledDeviceCoord likely never populated — check the device's LateDoPollData. This tracker will not classify into any role.", BasisDebug.LogTag.Input);
                }
                Vector3 local = bodyRotInv * (unscaledPos - bodyOrigin);
                samples.Add(new TrackerSample
                {
                    Input = input,
                    HeightRatio = local.y / eyeHeight,
                    LateralRatio = local.x / eyeHeight,
                    BodyLocal = local,
                    NearOrigin = nearOrigin,
                });
            }
            return samples;
        }

        /// <summary>
        /// Returns the largest absolute lateral ratio among arm-height trackers, or a
        /// typical-adult fallback. Adapts shoulder/elbow priors to the player's actual
        /// arm length so the same code works for kids and tall adults.
        /// </summary>
        private static float EstimateArmReach(List<TrackerSample> samples)
        {
            float maxAbs = 0f;
            int n = samples.Count;
            for (int i = 0; i < n; i++)
            {
                TrackerSample s = samples[i];
                if (s.HeightRatio < ConstellationArmHeightFloor) continue;
                float lAbs = Mathf.Abs(s.LateralRatio);
                if (lAbs < ConstellationArmLateralFloor) continue;
                if (lAbs > maxAbs) maxAbs = lAbs;
            }
            return maxAbs > ConstellationArmLateralFloor ? maxAbs : ConstellationDefaultArmReachRatio;
        }

        private static BoneRolePrior[] BuildPriors(float armReach, float stanceReach, float toleranceScale)
        {
            // Heights are fractions of player eye height. Lateral is signed — negative is
            // the body's left. Sigmas control how forgiving each axis is; bigger sigma
            // means more permissive. Every sigma is multiplied by toleranceScale (the
            // user-facing "calibration tolerance" knob, 1 = stock) so a player with
            // atypical proportions can widen every acceptance region at once.
            //
            // Leg lateral tracks measured stance width the same way arm lateral tracks
            // measured reach: a wide stance, a narrow stance, or a child's hips all put the
            // foot/knee trackers at a different |x|, and a fixed ±0.10 prior pushes the
            // outliers out of band. stanceReach is the measured value (falling back to the
            // old 0.10 constant when no foot-height tracker is present), so a typical stance
            // is unchanged while wide/narrow stances now fit. legLatSigma's floor keeps the
            // default (stance ≈ 0.10) at the original 0.07 — it only ever widens from there.
            //
            // Toes sit slightly closer to the floor than a foot-strap tracker (which mounts on
            // top of the shoe / over the laces). Tighter height sigma keeps a foot-strap tracker
            // at h≈0.05 firmly on Foot, while a tracker on the toe at h≈0.02 wins Toes. Greedy
            // global-best handles the disambiguation when both trackers exist on the same side.
            float legLat = stanceReach;
            float legLatSigma = Mathf.Max(stanceReach * 0.7f, 0.07f);
            return new BoneRolePrior[]
            {
                // Centered torso bones — height is the discriminator.
                new BoneRolePrior(BasisBoneTrackedRole.Hips,           h: 0.55f, lat: 0f,                 hSigma: 0.10f * toleranceScale, latSigma: 0.10f * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.Chest,          h: 0.78f, lat: 0f,                 hSigma: 0.08f * toleranceScale, latSigma: 0.10f * toleranceScale),

                // Legs — toes near floor, feet just above, knees mid-shin. Lateral scales with stance.
                new BoneRolePrior(BasisBoneTrackedRole.LeftToes,       h: 0.02f, lat: -legLat,            hSigma: 0.04f * toleranceScale, latSigma: legLatSigma * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.RightToes,      h: 0.02f, lat: +legLat,            hSigma: 0.04f * toleranceScale, latSigma: legLatSigma * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.LeftFoot,       h: 0.05f, lat: -legLat,            hSigma: 0.08f * toleranceScale, latSigma: legLatSigma * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.RightFoot,      h: 0.05f, lat: +legLat,            hSigma: 0.08f * toleranceScale, latSigma: legLatSigma * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.LeftLowerLeg,   h: 0.27f, lat: -legLat,            hSigma: 0.10f * toleranceScale, latSigma: legLatSigma * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.RightLowerLeg,  h: 0.27f, lat: +legLat,            hSigma: 0.10f * toleranceScale, latSigma: legLatSigma * toleranceScale),

                // Arms in T-pose share approximate height; lateral position discriminates
                // shoulder vs elbow vs (the implied hand controller out past the elbow).
                // Lateral priors scale with measured reach so this works for any arm length.
                //
                // Lateral sigma is also scaled by armReach (×0.08) so the 3σ acceptance
                // band can never cross the body midline. Shoulder/lower-arm trackers are
                // physically anchored to one side of the body — letting them score
                // against the opposite-side prior is just noise. Concretely with
                // ×0.08: shoulder 3σ extent = armReach × [0.06, 0.54], lower-arm 3σ
                // extent = armReach × [0.41, 0.89] — both clear of the midline for
                // any armReach value.
                new BoneRolePrior(BasisBoneTrackedRole.LeftShoulder,   h: 0.88f, lat: -armReach * 0.30f,  hSigma: 0.08f * toleranceScale, latSigma: armReach * 0.08f * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.RightShoulder,  h: 0.88f, lat: +armReach * 0.30f,  hSigma: 0.08f * toleranceScale, latSigma: armReach * 0.08f * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.LeftLowerArm,   h: 0.88f, lat: -armReach * 0.65f,  hSigma: 0.10f * toleranceScale, latSigma: armReach * 0.12f * toleranceScale),
                new BoneRolePrior(BasisBoneTrackedRole.RightLowerArm,  h: 0.88f, lat: +armReach * 0.65f,  hSigma: 0.10f * toleranceScale, latSigma: armReach * 0.12f * toleranceScale),
            };
        }

        /// <summary>
        /// User-facing FBIK calibration tolerance: a multiplier applied to every prior's
        /// sigma in <see cref="BuildPriors"/>. 1 = stock behavior (identical acceptance
        /// regions); higher widens every band for players whose proportions, tracker mounts,
        /// or calibration pose fall outside the typical envelope. Clamped so a maxed slider
        /// can't collapse role discrimination entirely.
        /// </summary>
        private static float GetCalibrationTolerance()
        {
            float t = Basis.BasisUI.BasisSettingsDefaults.CalibrationTolerance.RawValue;
            if (t < ConstellationMinCalibrationTolerance) t = ConstellationMinCalibrationTolerance;
            if (t > ConstellationMaxCalibrationTolerance) t = ConstellationMaxCalibrationTolerance;
            return t;
        }

        /// <summary>
        /// Largest absolute lateral ratio among foot-height trackers (the stance-defining
        /// trackers), or the typical-adult fallback. Lets the leg priors track the player's
        /// actual stance the way <see cref="EstimateArmReach"/> tracks arm length — a wide
        /// or narrow stance otherwise pushes the foot/knee trackers out of a fixed ±0.10 band.
        /// Knees (h≈0.27) and everything above are excluded by the height ceiling, so only
        /// real feet/toes vote on stance width.
        /// </summary>
        private static float EstimateStanceWidth(List<TrackerSample> samples)
        {
            float maxAbs = 0f;
            int n = samples.Count;
            for (int i = 0; i < n; i++)
            {
                TrackerSample s = samples[i];
                if (s.NearOrigin) continue;
                if (s.HeightRatio > ConstellationFootHeightCeiling) continue;
                float lAbs = Mathf.Abs(s.LateralRatio);
                if (lAbs < ConstellationStanceLateralFloor) continue;
                if (lAbs > maxAbs) maxAbs = lAbs;
            }
            return maxAbs > ConstellationStanceLateralFloor ? maxAbs : ConstellationDefaultStanceRatio;
        }

        /// <summary>
        /// Re-centers the Foot/Toes height priors on the player's actually-measured
        /// foot-tracker height. Ankle-strap height, boot height, and shin-mounted pucks all
        /// shift where a "foot" tracker sits — a fixed h=0.05 prior with a tight sigma rejects
        /// anything strapped high on the ankle. The two leg roles closest to the floor are the
        /// tight, failure-prone ones, so anchoring them to measured data — while keeping the
        /// toe band a fixed step below the foot band so feet still bind before toes — fixes
        /// high-mounted setups without touching the knee/hip rows. No-op when no foot-height
        /// tracker is present, and a no-op for a standard ~0.05 mount (so no regression there).
        /// </summary>
        private static void ApplyMeasuredFootHeightPriors(BoneRolePrior[] priors, List<TrackerSample> samples)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                TrackerSample s = samples[i];
                if (s.NearOrigin) continue;
                if (s.HeightRatio > ConstellationFootHeightCeiling) continue;
                if (s.HeightRatio < -ConstellationFootHeightCeiling) continue; // discard far-sub-floor (bad) reads
                sum += s.HeightRatio;
                count++;
            }
            if (count == 0) return;

            float footH = Mathf.Clamp(sum / count, ConstellationFootHeightMin, ConstellationFootHeightMax);
            float toeH = Mathf.Max(footH - ConstellationToeBelowFoot, 0f);

            SetPriorHeight(priors, BasisBoneTrackedRole.LeftFoot, footH);
            SetPriorHeight(priors, BasisBoneTrackedRole.RightFoot, footH);
            SetPriorHeight(priors, BasisBoneTrackedRole.LeftToes, toeH);
            SetPriorHeight(priors, BasisBoneTrackedRole.RightToes, toeH);
        }

        private static void SetPriorHeight(BoneRolePrior[] priors, BasisBoneTrackedRole role, float newHeight)
        {
            int idx = FindPriorIndex(priors, role);
            if (idx < 0) return;
            priors[idx] = new BoneRolePrior(role, newHeight, priors[idx].ExpectedLateralRatio, priors[idx].HeightSigma, priors[idx].LateralSigma);
        }

        /// <summary>
        /// Overrides the LeftLowerArm / RightLowerArm prior centers with the midpoint
        /// between that side's hand controller and shoulder. Hand controllers keep a pinned
        /// role, so their pose is trustworthy even in a sloppy calibration stance — the elbow
        /// sits roughly halfway down the arm, so the hand→shoulder midpoint tracks the real
        /// forearm far better than a fixed T-pose point. No-ops per side when that hand isn't
        /// present (hand-tracking off, tracker-only hand, or a stale poll): the static
        /// <see cref="BuildPriors"/> center stands in.
        /// </summary>
        private static void ApplyElbowMidpointPriors(BoneRolePrior[] priors, List<TrackerSample> samples, Vector3 bodyOrigin, Quaternion bodyRotInv, float eyeHeight)
        {
            ApplyElbowMidpointForSide(priors, samples, BasisBoneTrackedRole.LeftHand, BasisBoneTrackedRole.LeftShoulder, BasisBoneTrackedRole.LeftLowerArm, sideSign: -1, bodyOrigin, bodyRotInv, eyeHeight);
            ApplyElbowMidpointForSide(priors, samples, BasisBoneTrackedRole.RightHand, BasisBoneTrackedRole.RightShoulder, BasisBoneTrackedRole.RightLowerArm, sideSign: +1, bodyOrigin, bodyRotInv, eyeHeight);
        }

        private static void ApplyElbowMidpointForSide(
            BoneRolePrior[] priors,
            List<TrackerSample> samples,
            BasisBoneTrackedRole handRole,
            BasisBoneTrackedRole shoulderRole,
            BasisBoneTrackedRole lowerArmRole,
            int sideSign,
            Vector3 bodyOrigin, Quaternion bodyRotInv, float eyeHeight)
        {
            int elbowIdx = FindPriorIndex(priors, lowerArmRole);
            if (elbowIdx < 0) return; // lower-arm role isn't in the prior set

            if (!TryGetHandBodyLocalRatios(handRole, bodyOrigin, bodyRotInv, eyeHeight, out float handHeight, out float handLateral))
            {
                // No usable hand pose this side. The static BuildPriors center sits at
                // armReach×0.65, but armReach is by construction the outermost arm-height
                // tracker — i.e. the elbow itself — so that center lands ~3σ inboard of where
                // the elbow actually is and the tracker fails to bind. Re-center on the
                // measured outermost same-side arm tracker instead (clamped to its own side of
                // the midline). Falls through to the static prior only when this side has no
                // arm-height tracker at all.
                if (TryGetOutermostArmSampleLateral(samples, sideSign, out float measuredLat))
                {
                    float latSigmaFallback = priors[elbowIdx].LateralSigma;
                    float minMagFallback = ConstellationElbowMidlineSigmaGuard * latSigmaFallback;
                    float clampedFallback = sideSign < 0 ? Mathf.Min(measuredLat, -minMagFallback) : Mathf.Max(measuredLat, minMagFallback);
                    priors[elbowIdx] = new BoneRolePrior(
                        lowerArmRole,
                        priors[elbowIdx].ExpectedHeightRatio,
                        clampedFallback,
                        priors[elbowIdx].HeightSigma,
                        latSigmaFallback);
                    BasisDebug.Log($"FBIK constellation: {lowerArmRole} prior re-centered on measured arm tracker (lat={clampedFallback:F2}); no hand pose this side", BasisDebug.LogTag.Input);
                }
                return;
            }

            // Shoulder anchor: reuse the shoulder prior's expected position so the elbow
            // stays consistent with the shoulder region. (Shoulders are always present in
            // the freshly-built prior list — the calibration-toggle filter runs later — so
            // the fallback is purely defensive.)
            float shoulderHeight, shoulderLateral;
            int shoulderIdx = FindPriorIndex(priors, shoulderRole);
            if (shoulderIdx >= 0)
            {
                shoulderHeight = priors[shoulderIdx].ExpectedHeightRatio;
                shoulderLateral = priors[shoulderIdx].ExpectedLateralRatio;
            }
            else
            {
                shoulderHeight = priors[elbowIdx].ExpectedHeightRatio;
                shoulderLateral = priors[elbowIdx].ExpectedLateralRatio;
            }

            float t = ConstellationElbowShoulderBlend;
            float elbowHeight = Mathf.Lerp(shoulderHeight, handHeight, t);
            float elbowLateral = Mathf.Lerp(shoulderLateral, handLateral, t);

            // Keep the elbow region on its own side of the midline. Pulling the center too
            // far inward (hands held across the body) would let the 3σ band cross x=0 and
            // pick up the opposite arm's tracker; clamp the magnitude so the band's inner
            // edge stays at/beyond the centerline, matching BuildPriors' "never cross the
            // midline" intent.
            float latSigma = priors[elbowIdx].LateralSigma;
            float minMag = ConstellationElbowMidlineSigmaGuard * latSigma;
            float clampedLateral = sideSign < 0 ? Mathf.Min(elbowLateral, -minMag) : Mathf.Max(elbowLateral, minMag);

            priors[elbowIdx] = new BoneRolePrior(
                lowerArmRole,
                elbowHeight,
                clampedLateral,
                priors[elbowIdx].HeightSigma,
                latSigma);

            BasisDebug.Log($"FBIK constellation: {lowerArmRole} prior re-centered on hand→shoulder midpoint (h={elbowHeight:F2}, lat={clampedLateral:F2}; hand h={handHeight:F2}, lat={handLateral:F2})", BasisDebug.LogTag.Input);
        }

        /// <summary>
        /// Finds the device currently bound to <paramref name="handRole"/> and returns its
        /// body-local height/lateral ratios in the same playspace frame the classifier uses
        /// (UnscaledDeviceCoord, normalized to eye height). Returns false when no such device
        /// exists or it polled at the world origin (a pose it never actually wrote).
        /// </summary>
        private static bool TryGetHandBodyLocalRatios(BasisBoneTrackedRole handRole, Vector3 bodyOrigin, Quaternion bodyRotInv, float eyeHeight, out float heightRatio, out float lateralRatio)
        {
            heightRatio = 0f;
            lateralRatio = 0f;

            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance.AllInputDevices;
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                BasisInput input = devices[i];
                if (input == null) continue;
                if (!input.TryGetRole(out BasisBoneTrackedRole role) || role != handRole) continue;

                // Same fresh-poll discipline as the HMD and free-tracker reads in this pass.
                input.LatePollData();
                Vector3 unscaledPos = input.UnscaledDeviceCoord.position;
                if (unscaledPos.sqrMagnitude < ConstellationNearOriginEpsilonSqr)
                {
                    return false; // hand never wrote a real pose — don't anchor the elbow to it
                }

                Vector3 local = bodyRotInv * (unscaledPos - bodyOrigin);
                heightRatio = local.y / eyeHeight;
                lateralRatio = local.x / eyeHeight;
                return true;
            }
            return false;
        }

        private static int FindPriorIndex(BoneRolePrior[] priors, BasisBoneTrackedRole role)
        {
            for (int i = 0; i < priors.Length; i++)
            {
                if (priors[i].Role == role) return i;
            }
            return -1;
        }

        // Returns true when dependsOn is already taken, or isn't in the prior list at all
        // (the toggle disabled it, so there's nothing for the dependent role to wait on).
        private static bool IsRolePreconditionMet(BoneRolePrior[] priors, bool[] roleUsed, BasisBoneTrackedRole dependsOn)
        {
            for (int i = 0; i < priors.Length; i++)
            {
                if (priors[i].Role == dependsOn)
                {
                    return roleUsed[i];
                }
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="role"/> may be assigned given which roles are already taken.
        /// Feet→toes is enforced in BOTH the main and leftover passes — a single low tracker
        /// must never take a Toes slot and leave FootControl.HasTracked false (which kicks the
        /// rig into procedural foot IK). The torso/leg/arm ordering chains (Chest waits on Hips,
        /// LowerLeg waits on its same-side Foot, Shoulder waits on LowerArm) only matter while the main greedy pass is still
        /// placing best-fit trackers, so they're dropped in the leftover pass: a well-placed
        /// knee or shoulder then still binds even when its anchor never calibrated.
        /// </summary>
        private static bool IsAssignmentAllowed(BasisBoneTrackedRole role, BoneRolePrior[] priors, bool[] roleUsed, bool leftoverPass)
        {
            if (role == BasisBoneTrackedRole.LeftToes
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.LeftFoot))
            {
                return false;
            }
            if (role == BasisBoneTrackedRole.RightToes
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.RightFoot))
            {
                return false;
            }

            if (leftoverPass)
            {
                return true;
            }

            if (role == BasisBoneTrackedRole.Chest
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.Hips))
            {
                return false;
            }
            if (role == BasisBoneTrackedRole.LeftLowerLeg
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.LeftFoot))
            {
                return false;
            }
            if (role == BasisBoneTrackedRole.RightLowerLeg
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.RightFoot))
            {
                return false;
            }
            if (role == BasisBoneTrackedRole.LeftShoulder
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.LeftLowerArm))
            {
                return false;
            }
            if (role == BasisBoneTrackedRole.RightShoulder
                && !IsRolePreconditionMet(priors, roleUsed, BasisBoneTrackedRole.RightLowerArm))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Largest-|lateral| arm-height sample on the requested side, used to re-center the
        /// elbow prior when no hand controller pose is available. The elbow is the outermost
        /// free tracker at arm height, so its measured lateral is a far better center than the
        /// static armReach×0.65 guess. Returns false when this side has no arm-height tracker.
        /// </summary>
        private static bool TryGetOutermostArmSampleLateral(List<TrackerSample> samples, int sideSign, out float lateral)
        {
            lateral = 0f;
            float bestAbs = 0f;
            bool found = false;
            for (int i = 0; i < samples.Count; i++)
            {
                TrackerSample s = samples[i];
                if (s.NearOrigin) continue;
                if (s.HeightRatio < ConstellationArmHeightFloor) continue;
                if (sideSign < 0 && s.LateralRatio >= 0f) continue;
                if (sideSign > 0 && s.LateralRatio <= 0f) continue;
                float a = Mathf.Abs(s.LateralRatio);
                if (a > bestAbs)
                {
                    bestAbs = a;
                    lateral = s.LateralRatio;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Second assignment pass: bind any still-free tracker to its nearest still-free role
        /// at a relaxed threshold. The main greedy pass drops a tracker the instant it can't
        /// clear a tight 3σ fit — common for atypical proportions, high-mounted ankle straps,
        /// or a limb held off the calibration pose — and an unbound tracker is worse than a
        /// slightly-off one. Feet still bind before toes here; near-origin (never-polled)
        /// samples stay excluded so a stale device can't be force-fit onto the body.
        /// </summary>
        private static int AssignLeftoverTrackers(List<TrackerSample> samples, BoneRolePrior[] priors, bool[] sampleUsed, bool[] roleUsed)
        {
            float leftoverThreshold = ConstellationAcceptThreshold * ConstellationLeftoverThresholdScale;
            int assigned = 0;
            while (true)
            {
                float bestScore = leftoverThreshold;
                int bestSampleIdx = -1;
                int bestRoleIdx = -1;
                for (int s = 0; s < samples.Count; s++)
                {
                    if (sampleUsed[s]) continue;
                    TrackerSample sample = samples[s];
                    if (sample.NearOrigin) continue;
                    for (int r = 0; r < priors.Length; r++)
                    {
                        if (roleUsed[r]) continue;
                        if (!IsAssignmentAllowed(priors[r].Role, priors, roleUsed, leftoverPass: true)) continue;
                        float score = ScoreSampleAgainstRole(sample, priors[r]);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSampleIdx = s;
                            bestRoleIdx = r;
                        }
                    }
                }
                if (bestSampleIdx < 0) break;

                BasisBoneTrackedRole role = priors[bestRoleIdx].Role;
                TrackerSample chosen = samples[bestSampleIdx];
                BasisDebug.Log($"FBIK constellation (leftover): '{chosen.Input.UniqueDeviceIdentifier}' -> {role} (h={chosen.HeightRatio:F2}, lat={chosen.LateralRatio:F2}, score={bestScore:F2})", BasisDebug.LogTag.Input);
                chosen.Input.ApplyTrackerCalibration(role);
                sampleUsed[bestSampleIdx] = true;
                roleUsed[bestRoleIdx] = true;
                HasFBIKTrackers = true;
                RecordAssignment(bestSampleIdx, role, bestScore);
                assigned++;
            }
            return assigned;
        }

        private static float ScoreSampleAgainstRole(TrackerSample sample, BoneRolePrior prior)
        {
            float dh = (sample.HeightRatio - prior.ExpectedHeightRatio) / prior.HeightSigma;
            float dl = (sample.LateralRatio - prior.ExpectedLateralRatio) / prior.LateralSigma;
            return -(dh * dh + dl * dl);
        }

        // -9 ≈ a combined 3-sigma fit. A tracker that can't beat this against any role is
        // left unassigned rather than forced into a bad slot.
        private const float ConstellationAcceptThreshold = -9f;
        private const float ConstellationArmHeightFloor = 0.65f;
        private const float ConstellationArmLateralFloor = 0.20f;
        // Anything closer than 1 cm from world (0,0,0) is treated as "the device never wrote
        // a real pose into UnscaledDeviceCoord". A real tracker basically never sits exactly
        // on the playspace origin, so this is a safe smoke-test threshold.
        private const float ConstellationNearOriginEpsilonSqr = 1e-4f;
        // Half arm-span as a fraction of eye height for a typical adult — used as a fallback
        // when no arm-height tracker is present to measure the player's own reach.
        private const float ConstellationDefaultArmReachRatio = 0.55f;
        private const float ConstellationToeForwardEpsilon = 0.02f;
        // Where the elbow prior sits along the hand→shoulder line. 0.5 = true midpoint (the
        // elbow sits ~halfway down a roughly-straight arm); raise toward 1 to bias the
        // region toward the hand, lower toward 0 to bias it toward the shoulder.
        private const float ConstellationElbowShoulderBlend = 0.5f;
        // Floor on the re-centered elbow's lateral magnitude, in units of its own lateral
        // sigma, so the region's inner edge can't cross the body midline onto the other arm.
        // 3σ matches the accept threshold.
        private const float ConstellationElbowMidlineSigmaGuard = 3.0f;
        // Leg lateral priors scale with measured stance the way arm priors scale with reach.
        // Default matches the legacy fixed ±0.10 lateral, so a typical stance is unchanged.
        private const float ConstellationDefaultStanceRatio = 0.10f;
        // Trackers at/under this height ratio are treated as feet/toes when measuring stance
        // width and foot-mount height (knees sit at ~0.27, so this cleanly excludes them).
        private const float ConstellationFootHeightCeiling = 0.18f;
        // Ignore near-centerline noise when measuring stance width.
        private const float ConstellationStanceLateralFloor = 0.03f;
        // Clamp range for the measured foot-mount height re-anchor.
        private const float ConstellationFootHeightMin = 0.0f;
        private const float ConstellationFootHeightMax = 0.16f;
        // Toe band sits this far below the (measured) foot band so feet still bind first.
        private const float ConstellationToeBelowFoot = 0.03f;
        // Leftover pass accepts fits up to this multiple of the main threshold (−9 → −15.3,
        // ≈3.9σ) so still-unbound trackers land on their nearest open role.
        private const float ConstellationLeftoverThresholdScale = 1.7f;
        // Bounds for the user-facing calibration tolerance (a sigma multiplier).
        private const float ConstellationMinCalibrationTolerance = 1.0f;
        private const float ConstellationMaxCalibrationTolerance = 3.0f;
        /// <summary>
        /// gets a roles dictionary with the roles and transforms
        /// </summary>
        /// <returns></returns>
        public static Dictionary<BasisBoneTrackedRole, Transform> GetAllRolesAsTransform()
        {
            Common.BasisTransformMapping Mapping = BasisLocalAvatarDriver.Mapping;
            Dictionary<BasisBoneTrackedRole, Transform> transforms = new Dictionary<BasisBoneTrackedRole, Transform>
    {
        { BasisBoneTrackedRole.Hips,Mapping.Hips },
      //  { BasisBoneTrackedRole.Spine, Mapping.spine },
        { BasisBoneTrackedRole.Chest, Mapping.chest },
    //    { BasisBoneTrackedRole.Upperchest, BasisLocalPlayer.Instance.AvatarDriver.References.Upperchest },
      //  { BasisBoneTrackedRole.Neck, Mapping.neck },
        { BasisBoneTrackedRole.Head, Mapping.head },
       // { BasisBoneTrackedRole.CenterEye, LeftEye },
       // { BasisBoneTrackedRole.RightEye, RightEye },

        { BasisBoneTrackedRole.LeftShoulder, Mapping.leftShoulder },
        { BasisBoneTrackedRole.RightShoulder, Mapping.RightShoulder },

      // { BasisBoneTrackedRole.LeftUpperArm, Mapping.leftUpperArm },
      // { BasisBoneTrackedRole.RightUpperArm,Mapping. RightUpperArm },

        { BasisBoneTrackedRole.RightLowerArm, Mapping.RightLowerArm },
        { BasisBoneTrackedRole.LeftLowerArm, Mapping.leftLowerArm },

        { BasisBoneTrackedRole.LeftHand, Mapping.leftHand },
        { BasisBoneTrackedRole.RightHand, Mapping.rightHand },

      //  { BasisBoneTrackedRole.LeftUpperLeg,Mapping.LeftUpperLeg },
       { BasisBoneTrackedRole.LeftLowerLeg,Mapping. LeftLowerLeg },
      //  { BasisBoneTrackedRole.RightUpperLeg, Mapping.RightUpperLeg },
        { BasisBoneTrackedRole.RightLowerLeg,Mapping. RightLowerLeg },

        { BasisBoneTrackedRole.LeftFoot, Mapping.leftFoot },
        { BasisBoneTrackedRole.LeftToes,Mapping. leftToe },

        { BasisBoneTrackedRole.RightFoot, Mapping.rightFoot },
        { BasisBoneTrackedRole.RightToes,Mapping. rightToe },
            };

            return transforms;
        }
        /// <summary>
        /// Per-role radius used for tracker debug gizmos (BasisLocalBoneDriver). The
        /// constellation classifier in FullBodyCalibration no longer consults these
        /// values — they survive only as visualization hints.
        /// </summary>
        public static float MaxDistanceBeforeTrackerIsIrrelivant(BasisBoneTrackedRole role)
        {

            switch (role)
            {
                case BasisBoneTrackedRole.CenterEye:
                    return 0;

                case BasisBoneTrackedRole.Head:
                    return 0;

                case BasisBoneTrackedRole.Neck:
                    return 0;
                case BasisBoneTrackedRole.Mouth:
                    return 0;
                case BasisBoneTrackedRole.Spine:
                    return 0;
                case BasisBoneTrackedRole.Chest:
                    return 0.4f;
                case BasisBoneTrackedRole.Hips:
                    return 0.5f;

                case BasisBoneTrackedRole.LeftLowerLeg:
                    return 0.55f;
                case BasisBoneTrackedRole.RightLowerLeg:
                    return 0.55f;

                case BasisBoneTrackedRole.LeftFoot:
                    return 0.4f;
                case BasisBoneTrackedRole.RightFoot:
                    return 0.4f;

                case BasisBoneTrackedRole.LeftShoulder:
                    return 0.45f;
                case BasisBoneTrackedRole.RightShoulder:
                    return 0.45f;

                case BasisBoneTrackedRole.LeftUpperLeg:
                    return 0.3f;
                case BasisBoneTrackedRole.RightUpperLeg:
                    return 0.3f;

                case BasisBoneTrackedRole.LeftLowerArm:
                    return 0.55f;
                case BasisBoneTrackedRole.RightLowerArm:
                    return 0.55f;

                case BasisBoneTrackedRole.LeftHand:
                    return 0.2f;
                case BasisBoneTrackedRole.RightHand:
                    return 0.2f;

                case BasisBoneTrackedRole.LeftToes:
                    return 0.2f;
                case BasisBoneTrackedRole.RightToes:
                    return 0.2f;

                case BasisBoneTrackedRole.LeftUpperArm:
                    return 0;
                case BasisBoneTrackedRole.RightUpperArm:
                    return 0;
                default:
                    BasisDebug.LogError($"Unknown role {role}");
                    return 0;
            }
        }

        // Cache for the default-armReach prior list. Rebuilt only when eyeHeight
        // moves enough to change the armReach scaling — avoids per-frame allocs
        // while still picking up calibration / scale changes.
        private static readonly List<ConstellationDebug.DebugPrior> _defaultPriorsCache = new List<ConstellationDebug.DebugPrior>(16);
        private static float _defaultPriorsArmReach = -1f;

        /// <summary>
        /// Returns the body-frame anchor and per-role priors used to render
        /// calibration acceptance-region gizmos. Always prefers a live HMD pose
        /// so the regions track wherever the player is now (rather than freezing
        /// at the last calibration snapshot). Falls back to the snapshot frame if
        /// no HMD device is available, or returns false if there's no usable
        /// frame source at all.
        /// </summary>
        /// <param name="bodyOrigin">World-space anchor: HMD position projected to the floor.</param>
        /// <param name="bodyRotation">Body forward rotation: HMD facing flattened to horizontal.</param>
        /// <param name="eyeHeight">Eye height (meters), used to convert ratio-space priors to world distances.</param>
        /// <param name="priors">Per-role priors. Snapshot priors when a calibration has run, default-armReach priors otherwise.</param>
        public static bool TryGetCalibrationVisualizationFrame(
            out Vector3 bodyOrigin,
            out Quaternion bodyRotation,
            out float eyeHeight,
            out IReadOnlyList<ConstellationDebug.DebugPrior> priors)
        {
            // The classifier scores in playspace (real-world) ratios of
            // PlayerEyeHeight. After it picks roles, each tracker pose is
            // lifted onto the avatar via DeviceScale (BasisInput.ConvertToScaledDeviceCoord:
            // scaledPos = OffsetCoords + unscaledPos * DeviceScale). The
            // visualization mirrors that pipeline: priors stay cached against
            // PlayerEyeHeight (real-world), but body anchor + region sizing
            // get the same DeviceScale projection real trackers get post-
            // classification. Without the multiply, regions sit at the player's
            // real-world position (e.g. ~0.09 m world) instead of on the avatar.
            float playerEye = Mathf.Max(BasisHeightDriver.PlayerEyeHeight, 1.0f);
            float deviceScale = BasisHeightDriver.DeviceScale;
            if (deviceScale <= 0f) deviceScale = 1f;
            float worldEyeHeight = playerEye * deviceScale;

            IReadOnlyList<ConstellationDebug.DebugPrior> resolvedPriors =
                (ConstellationDebug.HasSnapshot && ConstellationDebug.Priors.Count > 0)
                    ? (IReadOnlyList<ConstellationDebug.DebugPrior>)ConstellationDebug.Priors
                    : BuildDefaultPriorsList(playerEye);

            // Anchor on the HMD pose. Read passively from UnscaledDeviceCoord —
            // we deliberately do NOT call LatePollData because that would
            // re-run the device poll mid-render and stomp on height calibration
            // (and anything else reading device state in the same frame).
            // The cached value reflects the most recent normal frame poll, which
            // is plenty fresh for visualization.
            if (TryReadHmdPosePassive(out Vector3 hmdPosPlayspace, out Quaternion hmdRotPlayspace))
            {
                Vector3 fwd = hmdRotPlayspace * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                {
                    fwd = Vector3.forward;
                }
                fwd.Normalize();
                Quaternion bodyRotPlayspace = Quaternion.LookRotation(fwd, Vector3.up);

                // Lift HMD playspace → avatar-world. Multiplying by DeviceScale
                // before the locomotion matrix matches BasisInput.ConvertToScaledDeviceCoord
                // (scaledPos = OffsetCoords + unscaledPos * DeviceScale). The
                // matrix carries position + rotation from teleport / smooth-move;
                // its lossyScale stays 1 because avatar scale lives on the
                // avatar transform inside the root, applied here via DeviceScale.
                Matrix4x4 l2w = BasisLocalPlayer.localToWorldMatrix;
                Vector3 hmdWorld = l2w.MultiplyPoint3x4(hmdPosPlayspace * deviceScale);
                bodyRotation = l2w.rotation * bodyRotPlayspace;
                bodyOrigin = new Vector3(hmdWorld.x, hmdWorld.y - worldEyeHeight, hmdWorld.z);
                eyeHeight = worldEyeHeight;
                priors = resolvedPriors;
                return true;
            }

            // Last-ditch: snapshot frame lifted to world for the case where
            // HMD pose isn't available (e.g., before device discovery).
            if (ConstellationDebug.HasSnapshot)
            {
                Matrix4x4 l2w = BasisLocalPlayer.localToWorldMatrix;
                bodyOrigin = l2w.MultiplyPoint3x4(ConstellationDebug.BodyOrigin * deviceScale);
                bodyRotation = l2w.rotation * ConstellationDebug.BodyRotation;
                eyeHeight = worldEyeHeight;
                priors = ConstellationDebug.Priors;
                return true;
            }

            bodyOrigin = Vector3.zero;
            bodyRotation = Quaternion.identity;
            eyeHeight = 1f;
            priors = null;
            return false;
        }

        /// <summary>
        /// Passive HMD pose read — same lookup as <see cref="TryGetHmdPose"/>
        /// but without the <c>LatePollData()</c> call. Safe to invoke from
        /// per-frame render paths since it never re-runs the device poll.
        /// </summary>
        private static bool TryReadHmdPosePassive(out Vector3 unscaledPos, out Quaternion unscaledRot)
        {
            BasisDeviceManagement manager = BasisDeviceManagement.Instance;
            if (manager == null)
            {
                unscaledPos = Vector3.zero;
                unscaledRot = Quaternion.identity;
                return false;
            }

            BasisObservableList<BasisInput> devices = manager.AllInputDevices;
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                BasisInput input = devices[i];
                if (input == null) continue;
                if (!input.TryGetRole(out BasisBoneTrackedRole role)) continue;
                if (role == BasisBoneTrackedRole.CenterEye || role == BasisBoneTrackedRole.Head)
                {
                    unscaledPos = input.UnscaledDeviceCoord.position;
                    unscaledRot = input.UnscaledDeviceCoord.rotation;
                    return true;
                }
            }

            unscaledPos = Vector3.zero;
            unscaledRot = Quaternion.identity;
            return false;
        }

        private static IReadOnlyList<ConstellationDebug.DebugPrior> BuildDefaultPriorsList(float eyeHeight)
        {
            float armReach = BasisConstellationClassifier.DefaultArmReachRatio * eyeHeight;
            // Re-build only when armReach moves enough to matter — guards against
            // every-frame allocations from the per-frame DrawGizmos path.
            if (_defaultPriorsArmReach < 0f || Mathf.Abs(armReach - _defaultPriorsArmReach) > 0.001f)
            {
                _defaultPriorsCache.Clear();
                BasisConstellationPrior[] built = BasisConstellationClassifier.BuildPriors(armReach, BasisConstellationClassifier.DefaultStanceRatio * eyeHeight, GetCalibrationTolerance());
                for (int i = 0; i < built.Length; i++)
                {
                    BoneRolePrior p = built[i];
                    _defaultPriorsCache.Add(new ConstellationDebug.DebugPrior
                    {
                        Role = p.Role,
                        ExpectedHeight = p.ExpectedHeightRatio,
                        ExpectedLateral = p.ExpectedLateralRatio,
                        HeightSigma = p.HeightSigma,
                        LateralSigma = p.LateralSigma,
                        Enabled = Basis.BasisUI.BasisSettingsDefaults.IsRoleEnabledForCalibration(p.Role),
                        AssignedSampleIndex = -1,
                    });
                }
                _defaultPriorsArmReach = armReach;
            }
            return _defaultPriorsCache;
        }

        /// <summary>
        /// Legacy ordered role list from the radius-based matcher. The constellation
        /// classifier no longer reads this — kept public for external consumers that
        /// still rely on it as a "trackable FB roles" enumeration.
        /// </summary>
        public static BasisBoneTrackedRole[] desiredOrder = new BasisBoneTrackedRole[]
        {
        BasisBoneTrackedRole.LeftHand,
        BasisBoneTrackedRole.RightHand,

        BasisBoneTrackedRole.LeftToes,
        BasisBoneTrackedRole.RightToes,

        BasisBoneTrackedRole.LeftShoulder,
        BasisBoneTrackedRole.RightShoulder,

        BasisBoneTrackedRole.RightFoot,
        BasisBoneTrackedRole.LeftFoot,

        BasisBoneTrackedRole.LeftLowerArm,
        BasisBoneTrackedRole.RightLowerArm,

        BasisBoneTrackedRole.LeftLowerLeg,
        BasisBoneTrackedRole.RightLowerLeg,

        BasisBoneTrackedRole.Hips,
        BasisBoneTrackedRole.Chest,
        };
        public static void ComputeHints(Dictionary<BasisBoneTrackedRole, Transform> storedRoleTransforms)
        {
            // 9) Bake "hint push up/out" offsets at calibration time
            //    We store offsets in tracker-local space so they rotate with the tracker at runtime.
            //    Then BasisLocalRigDriver applies: hintPos = rawPos + rawRot * localOffset;

            // Grab reference rotations from the avatar in T-pose (stable)
            Quaternion chestRefRot = Quaternion.identity;
            Quaternion hipsRefRot = Quaternion.identity;

            if (storedRoleTransforms.TryGetValue(BasisBoneTrackedRole.Chest, out var chestT) && chestT != null)
            {
                chestRefRot = chestT.rotation;
            }

            if (storedRoleTransforms.TryGetValue(BasisBoneTrackedRole.Hips, out var hipsT) && hipsT != null)
            {
                hipsRefRot = hipsT.rotation;
            }

            // Choose push magnitudes (tweakable)
            float hs = BasisHeightDriver.ScaledToMatchValue;
            float kneePush = 0.10f * hs;
            float headPush = 0.08f * hs;

            // Optional clamp so calibration can never store insane offsets
            float maxPush = 0.25f * hs;
            // Chest-as-head-hint bias (push "up" in chest frame)
            {
                var chestCtrl = BasisLocalBoneDriver.ChestControl;
                Quaternion trackerRot = chestCtrl.OutgoingWorldData.rotation;

                Vector3 worldUp = chestRefRot * Vector3.up;
                Vector3 localUp = Quaternion.Inverse(trackerRot) * worldUp;
                Vector3 localOffset = (localUp.sqrMagnitude < 1e-8f ? Vector3.up : localUp.normalized) * headPush;
                localOffset = Vector3.ClampMagnitude(localOffset, maxPush);

                BasisHintBiasStore.Set(BasisBoneTrackedRole.Chest, localOffset);
            }
            // Knee hints (lower legs) — often better with a touch of forward
            {
                var lll = BasisLocalBoneDriver.LeftLowerLegControl;
                {
                    float fwdWeight = 1;
                    if (lll.HasTracked == BasisHasTracked.HasTracker)
                    {
                        fwdWeight = 0.55f;
                    }
                    Quaternion trackerRot = lll.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, hipsRefRot, isLeft: true, distanceMeters: kneePush, outWeight: 0, upWeight: 0.25f, fwdWeight);
                    localOffset = Vector3.ClampMagnitude(localOffset, maxPush);
                    BasisHintBiasStore.Set(BasisBoneTrackedRole.LeftLowerLeg, localOffset);
                }

                var rll = BasisLocalBoneDriver.RightLowerLegControl;
                {
                    float fwdWeight = 1;
                    if (rll.HasTracked == BasisHasTracked.HasTracker)
                    {
                         fwdWeight = 0.55f;
                    }
                    Quaternion trackerRot = rll.OutgoingWorldData.rotation;
                    Vector3 localOffset = ComputeHintBiasLocal(trackerRot, hipsRefRot, isLeft: false, distanceMeters: kneePush, outWeight: 0, upWeight: 0.25f, fwdWeight);
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
            float fwdWeight = 0.00f         // optional: add a bit of forward if you want knees/elbows forward
        )
        {
            Vector3 up = referenceWorldRot * Vector3.up;
            Vector3 outDir = referenceWorldRot * (isLeft ? Vector3.left : Vector3.right);
            Vector3 fwd = referenceWorldRot * Vector3.forward;

            Vector3 worldDir = (outDir * outWeight + up * upWeight + fwd * fwdWeight);
            if (worldDir.sqrMagnitude < 1e-8f) worldDir = up;
            worldDir.Normalize();

            // Convert desired world push into tracker-local direction
            Vector3 localDir = Quaternion.Inverse(trackerWorldRot) * worldDir;
            if (localDir.sqrMagnitude < 1e-8f) localDir = Vector3.up;

            return localDir.normalized * distanceMeters;
        }
    }
}
