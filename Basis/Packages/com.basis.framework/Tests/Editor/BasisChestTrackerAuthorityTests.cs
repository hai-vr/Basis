using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Basis.IK.Mocap;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    using BasisMotionClip = Basis.IK.Mocap.BasisMotionClip;

    /// <summary>
    /// "THE HEAD DRAGS MY CHEST AROUND." Gates <see cref="BasisEerieMovement.ReassertTrackedChest"/>.
    ///
    /// A chest tracker is a MEASUREMENT. SolveSpine wrote it to the chest bone ONCE, before the solve, and
    /// then every stage after it overwrote it while chasing the HEAD: DistributeSpineBend writes the Spine
    /// (the chest's PARENT), BiasSpineTowardChest writes it again, and above all SolveSequentialSpineIK's
    /// CCD rotates the chest DIRECTLY at chain index chainLen-3 plus the Spine underneath it. Nothing pulled
    /// it back -- ClampChestCone bounds the chest against its PARENT and never against the tracker, and
    /// SolveChestTarget early-returns because chestIkTarget defaults false (and even switched on it pulls
    /// chest POSITION, never ROTATION). ReassertTrackedChest re-applies the measurement at the END of the
    /// solve and restores the head using only the joints ABOVE the chest.
    ///
    /// ==============================================================================================
    /// THE METRIC IS DEGREES OF CHEST PER DEGREE OF GAZE, WITH THE TRACKER HELD FIXED.
    ///
    /// Hips pinned (hips tracker on, so all three pelvis-synthesis stages are suppressed and the pelvis is
    /// the commanded target bit for bit), chest tracker fed and held at the frame's own measurement, and
    /// then ONLY the head moves -- orbited about the neck pivot, which is exactly what a user looking down
    /// does and exactly the input the solver receives. A real human's chest pitches -0.05 deg per degree of
    /// gaze, i.e. not at all, so zero is not an approximation of the right answer here, it IS the answer.
    ///
    /// NOT pooled spine-vs-mocap POSITION error. That ruler has repeatedly misled in this repo: it sums
    /// "where the pelvis ended up" with "how the chain above it was bent", it is dominated by the former,
    /// and it cannot see a rotation complaint at all -- switching chestIkTarget on moves chest POSITION
    /// 3.23 -> 0.29 cm while chest ROTATION goes 11.38 -> 11.51, i.e. the position ruler would have called
    /// that a fix. The quantity the user is complaining about is an ANGLE, so the gate is an angle.
    ///
    /// MEASURED (10-clip CMU corpus, chest tracker + hips tracker, shipped cone 90 and anatomical ROM on):
    ///                                        before      after
    ///     chest deg per deg of GAZE           0.402      0.008
    ///     chest-vs-tracker rotation error    11.68 deg   1.45 deg
    ///     chest deg per m   of HAND            0.02      0.02      (already zero on this branch)
    /// ==============================================================================================
    ///
    /// ⚠️ THE CONTROLS ARE NOT DECORATION. A rig that silently fails to move would pass every "the chest
    /// must not move" assertion in this file, so no threshold here is allowed to stand on its own:
    ///
    ///   * the GAZE gate is paired, in the same test, with the identical sweep run at HasChestTracker =
    ///     false, which must show a LARGE coupling (measured 0.390 deg/deg -- essentially the pre-fix
    ///     number, because the pre-fix tracker branch was letting the CCD win anyway). One rig, one sweep,
    ///     one flag between them: if the harness were inert both would read zero and the test fails.
    ///   * the CHEST-VS-TRACKER gate is paired with the same measurement on the no-chest branch, where
    ///     nothing ever writes the measurement and the error must therefore be large.
    ///   * the NO-CHEST BRANCH IS ITSELF GATED. ApplyArmSwingChestFollow is by design a hand->chest
    ///     coupling and it lives on the else side of the dispatch; it must still measure ~27 deg/m. That
    ///     is what makes this change provably scoped to tracker users rather than a global chest freeze.
    ///   * the HEAD IS STILL PLACED, so the fix cannot be "pin the chest and abandon the HMD".
    ///
    /// RIG PROVENANCE: construction, corpus, gaze frame, chest frame and every solver parameter mirror
    /// BasisTrackerConfigMatrixTests bone for bone -- the one harness in this suite that drives the real
    /// public SolveSpine dispatch, so the chest branch actually executes. Two traps that harness documents
    /// and that this file inherits: MaxChestDeltaProperty MUST be set (production ships 90; unset means a
    /// zero-degree chest cone that nobody runs), and spineAnatomicalRom is only real once the envelope is
    /// BAKED (GuardSpineJoint also returns when ChainSpineRestFrames is not created).
    /// </summary>
    public sealed class BasisChestTrackerAuthorityTests
    {
        static string CorpusDir => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");

        // BasisSpineCorpusAccuracyTests.k_Clips / BasisTrackerConfigMatrixTests.k_Clips -- the same ten the
        // published rulers and the figures quoted above were all measured on.
        static readonly (string dir, string name)[] k_Clips =
        {
            ("", "26_09"), ("", "143_11"), ("", "69_70"), ("", "143_18"),
            ("posture", "13_04"), ("posture", "14_20"), ("posture", "26_10"),
            ("posture", "56_07"), ("posture", "77_06"), ("posture", "82_05"),
        };

        static readonly BasisMocapJoint[] k_ChainJoints =
        {
            BasisMocapJoint.Hips, BasisMocapJoint.Spine, BasisMocapJoint.Chest,
            BasisMocapJoint.UpperChest, BasisMocapJoint.Neck, BasisMocapJoint.Head,
        };

        // The hands feed ApplyArmSwingChestFollow (which early-returns with the hands disabled) and the
        // upper legs give the pelvis a facing. A clip without them cannot exercise the no-chest branch.
        static readonly BasisMocapJoint[] k_RequiredExtra =
        {
            BasisMocapJoint.LeftHand, BasisMocapJoint.RightHand,
            BasisMocapJoint.LeftUpperLeg, BasisMocapJoint.RightUpperLeg,
        };

        // The probe inputs. Gaze angles span a glance to a full look-down; the coupling is normalised per
        // degree so all four are directly comparable and pool honestly. Hand offsets are a lateral swing of
        // both controllers, normalised per metre.
        static readonly float[] k_GazeDegrees = { 15f, 30f, 45f, 60f };
        static readonly float[] k_HandOffsetsM = { 0.15f, 0.30f, 0.45f };

        // ---------------------------------------------------------------------------- shipped thresholds

        /// <summary>⚠️ RE-BASED ON THE SHIPPED DESIGN, NOT ON THE BEST NUMBER THE STAGE CAN PRODUCE.
        /// An unbounded reassert measures 0.008 and was rejected IN A HEADSET -- "the chest is now able to
        /// be rotated in a way that pulls it off the head". What ships instead walks the chest toward the
        /// tracker only as far as the head can still be restored inside k_ChestReassertMaxHeadErr (10 mm),
        /// which measures 0.125 against 0.402 before the fix. So the gate sits between: 1.6x clear of the
        /// shipped figure, 3.2x clear of the pre-fix one, and it would still catch a full regression.
        /// DO NOT tighten this toward 0.008 -- that number is only reachable by giving the chest authority
        /// the head cannot afford, which is the defect this whole stage was re-written to avoid.</summary>
        const float k_MaxTrackedGazeCoupling = 0.20f;

        /// <summary>The control. Measured 0.390 deg/deg with HasChestTracker false; anything near the
        /// tracked figure means the harness is not actually moving the head.</summary>
        const float k_MinControlGazeCoupling = 0.15f;

        /// <summary>Measured 1.68 deg at the shipped 10 mm head budget (1.44 unbounded, which does not
        /// ship); 11.38 with the ReassertTrackedChest call removed and nothing else changed, against the
        /// 11.68 originally published. 4 leaves 2.4x headroom and still fails a regression to shipped.</summary>
        const float k_MaxTrackedChestError = 4f;

        /// <summary>The control: on the no-chest branch nothing ever writes the measurement, and it
        /// measures 17.30 deg.</summary>
        const float k_MinControlChestError = 5f;

        /// <summary>ApplyArmSwingChestFollow measures 27.07 deg/m on the no-chest branch and must stay
        /// live -- this change is scoped to tracker users.</summary>
        const float k_MinNoChestHandCoupling = 10f;

        /// <summary>Measured 0.02 deg/m with a chest tracker: the arm swing follow is skipped on that
        /// branch by design, and the reassert must not introduce a new hand path.</summary>
        const float k_MaxTrackedHandCoupling = 1f;

        /// <summary>Smoke alarm on the head pin. The head is welded to the HMD and the reassert restores it
        /// with the joints above the chest; if it were being traded away for the chest this would blow.
        /// Measured 0.94 cm mean / 2.99 p95 with a chest tracker, 0.78 / 2.29 without.</summary>
        const float k_MaxHeadErrorCm = 4f;

        // ------------------------------------------------------------------------------------ the corpus

        static List<BasisMotionClip> LoadClips()
        {
            if (!Directory.Exists(CorpusDir)) Assert.Ignore($"no mocap corpus at {CorpusDir}");
            var clips = new List<BasisMotionClip>();
            foreach ((string dir, string name) in k_Clips)
            {
                string path = Path.Combine(CorpusDir, dir, name + ".bvh");
                if (File.Exists(path) && BasisBvhLoader.TryLoad(path, out BasisMotionClip c, out _)
                    && k_ChainJoints.All(c.Has) && k_RequiredExtra.All(c.Has))
                {
                    clips.Add(c);
                }
            }
            if (clips.Count == 0) Assert.Ignore("no corpus clip carries the spine chain plus hands and upper legs");
            return clips;
        }

        static int MostUprightFrame(BasisMotionClip c)
        {
            int best = 0;
            float bestY = float.MinValue;
            for (int f = 0; f < c.FrameCount; f++)
            {
                float y = c.Get(f, BasisMocapJoint.Head).Position.y;
                if (y > bestY) { bestY = y; best = f; }
            }
            return best;
        }

        static Vector3 PelvisForward(BasisMotionClip c, int f, Vector3 fallback)
        {
            Vector3 right = c.Get(f, BasisMocapJoint.RightUpperLeg).Position - c.Get(f, BasisMocapJoint.LeftUpperLeg).Position;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-8f) return fallback;
            return Vector3.Cross(right.normalized, Vector3.up);
        }

        // Gaze frame from pure geometry -- up along the live neck->head axis, forward from the pelvis
        // facing -- so nothing depends on the BVH's joint bind conventions.
        static Quaternion GazeFrame(Vector3 neckPos, Vector3 headPos, Vector3 pelvisFwd)
        {
            Vector3 up = headPos - neckPos;
            if (up.sqrMagnitude < 1e-10f) return Quaternion.identity;
            up.Normalize();
            Vector3 rightAxis = Vector3.Cross(up, pelvisFwd);
            if (rightAxis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 fwd = Vector3.Cross(rightAxis.normalized, up);
            return Quaternion.LookRotation(fwd, up);
        }

        // WHAT A CHEST TRACKER ACTUALLY REPORTS, reconstructed from geometry rather than from the BVH's
        // chest bone rotation: up is the chest->upperChest segment the strap sits on, forward comes from
        // the pelvis facing. A bone's rotation is a modelling convention and does not transfer; a segment's
        // direction is anatomy and does. Same reasoning as the geometric gaze frame above.
        static Quaternion ChestFrame(BasisMotionClip c, int f, Vector3 pelvisFwd)
        {
            Vector3 chest = c.Get(f, BasisMocapJoint.Chest).Position;
            Vector3 upper = c.Get(f, BasisMocapJoint.UpperChest).Position;
            Vector3 up = upper - chest;
            if (up.sqrMagnitude < 1e-10f) return Quaternion.identity;
            up.Normalize();
            Vector3 rightAxis = Vector3.Cross(up, pelvisFwd);
            if (rightAxis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 fwd = Vector3.Cross(rightAxis.normalized, up);
            return Quaternion.LookRotation(fwd, up);
        }

        // --------------------------------------------------------------------------------------- the rig

        sealed class Rig : System.IDisposable
        {
            public GameObject Root;
            public Transform[] Bones;                 // Hips..Head, parallel to k_ChainJoints
            public BasisPoseSkeleton Skeleton;
            public NativeArray<BasisBoneHandle> Chain;
            public NativeArray<BasisSpineRestFrame> RestFrames;
            public NativeArray<BasisSpineRom> Roms;
            public BasisEerieMovement Job;
            public BasisBoneHandle ChestHandle;
            public BasisBoneHandle HeadHandle;
            public Quaternion RestGaze;
            public Quaternion RestChest;              // the subject's own rest chest frame; the feed is a delta off this
            public Quaternion RestYaw;

            public void Dispose()
            {
                if (Chain.IsCreated) Chain.Dispose();
                if (RestFrames.IsCreated) RestFrames.Dispose();
                if (Roms.IsCreated) Roms.Dispose();
                Skeleton?.Dispose();
                if (Root != null) Object.DestroyImmediate(Root);
            }
        }

        /// <summary>
        /// BasisTrackerConfigMatrixTests.BuildRig, mirrored. Per clip the chain is rebuilt from THAT
        /// subject's own segment vectors at their most upright frame, so proportions and natural curvature
        /// are the subject's and not an invented stack. Every bone binds at identity rotation, which is why
        /// the chest and pelvis feeds are DELTAS off the subject's own rest frames.
        ///
        /// standingHeadHeight / crouchDepth are deliberately absent: every row here runs with a hips
        /// tracker, and ApplyCrouchBodyOffset returns immediately when `HasChestTracker || hasHipsTracker`,
        /// so there is no crouch stage on either branch to feed. The pelvis-synthesis stages being off is
        /// the point -- it is what "hips pinned" means and what makes a chest delta attributable.
        /// </summary>
        static Rig BuildRig(BasisMotionClip clip, int restFrame)
        {
            var rig = new Rig { Root = new GameObject($"ChestAuthorityRig_{clip.Name}") };
            rig.Bones = new Transform[k_ChainJoints.Length];
            Transform parent = rig.Root.transform;
            Vector3 prev = clip.Get(restFrame, BasisMocapJoint.Hips).Position;
            for (int i = 0; i < k_ChainJoints.Length; i++)
            {
                var go = new GameObject(k_ChainJoints[i].ToString());
                go.transform.SetParent(parent, false);
                Vector3 world = clip.Get(restFrame, k_ChainJoints[i]).Position;
                go.transform.localPosition = i == 0 ? world : world - prev;
                prev = world;
                rig.Bones[i] = go.transform;
                parent = go.transform;
            }

            rig.Skeleton = new BasisPoseSkeleton();
            rig.Skeleton.Build(rig.Bones[0], rig.Bones);
            rig.Skeleton.GatherNow();

            // ⚠️ THE SOLVER'S CHAIN RUNS TIP -> ROOT: [Head, Neck, UpperChest, Chest, Spine, Hips]. The
            // parent of index i is i+1, lastJoint = chainLen-2 is the Spine and the CHEST is chainLen-3.
            // A root->tip harness would rotate the NECK where it thinks it is rotating the chest -- a joint
            // ABOVE the chest, which cannot move it -- and would measure nothing at all.
            rig.Chain = new NativeArray<BasisBoneHandle>(6, Allocator.Persistent);
            for (int i = 0; i < 6; i++) rig.Chain[i] = rig.Skeleton.Bind(rig.Bones[5 - i]);

            Vector3 restHips = clip.Get(restFrame, BasisMocapJoint.Hips).Position;
            Vector3 restNeck = clip.Get(restFrame, BasisMocapJoint.Neck).Position;
            Vector3 restHead = clip.Get(restFrame, BasisMocapJoint.Head).Position;
            Vector3 restFwd = PelvisForward(clip, restFrame, Vector3.forward);
            rig.RestGaze = GazeFrame(restNeck, restHead, restFwd);
            rig.RestChest = ChestFrame(clip, restFrame, restFwd);
            rig.RestYaw = Quaternion.LookRotation(new Vector3(restFwd.x, 0f, restFwd.z).normalized, Vector3.up);

            BuildSpineAnatomy(rig, clip, restFrame);

            rig.ChestHandle = rig.Skeleton.Bind(rig.Bones[2]);
            rig.HeadHandle = rig.Skeleton.Bind(rig.Bones[5]);

            // ⚠️ MIRRORS BasisFullIKConstraintJob.Create, AND IT IS LOAD-BEARING. ReassertTrackedChest
            // early-returns unless this scratch buffer exists -- it snapshots the chest and the joints above
            // it so a trial weight can be rolled back. Nothing in this suite calls Create, so a harness that
            // forgets it measures a stage that never ran, and the whole file would report the PRE-fix solver
            // while every assertion still looked healthy. This is the same trap as spineAnatomicalRom, whose
            // guard also early-returns unless ChainSpineRestFrames.IsCreated: the flag is not the feature,
            // the allocated buffer is. Verified by construction -- with the buffer absent this sweep returns
            // 0.402 deg/deg and 11.38 deg, bit-identical to deleting the call outright.

            rig.Job = new BasisEerieMovement
            {
                ChainHeadToSpine = rig.Chain,
                HandleHips = rig.Skeleton.Bind(rig.Bones[0]),
                HandleSpine = rig.Skeleton.Bind(rig.Bones[1]),
                HandleChest = rig.Skeleton.Bind(rig.Bones[2]),
                HandleUpperChest = rig.Skeleton.Bind(rig.Bones[3]),
                HandleNeck = rig.Skeleton.Bind(rig.Bones[4]),
                HandleHead = rig.Skeleton.Bind(rig.Bones[5]),
                targetOffsetHead = Quaternion.identity,
                targetOffsetChest = Quaternion.identity,
                offsetRotationHips = Quaternion.identity,
                playerUp = Vector3.up,
                // Off, its shipped default -- and the reason SolveChestTarget cannot be what pins the
                // chest: it early-returns here, and even switched on it pulls POSITION, never ROTATION.
                chestIkTarget = false,
                // Production ships TRUE, and the envelope below is BAKED so the flag is not decorative:
                // GuardSpineJoint returns immediately unless ChainSpineRestFrames.IsCreated.
                spineAnatomicalRom = true,
                // Set per sweep in Measure.
                HasChestTracker = false,
                hasHipsTracker = true,
                // SolveSpine's own gate. Without it the entry point returns before touching a bone.
                enabledSpineIK = true,
                // LockHips: the pelvis TARGET is authoritative and never clamped against the head, so with
                // the hips tracker on the pelvis is the commanded pose exactly. Hips pinned.
                ikLockMode = (float)BasisIKLockMode.LockHips,
                // Rest anatomy for the pre-bend's gaze-invariant neck cue and its rest length.
                TposeHeadToNeckLocal = Quaternion.Inverse(rig.RestGaze) * (restNeck - restHead),
                TposeLengthNeckToHips = restNeck - restHips,
                MinHeadSpineHeight = (restHead - restHips).magnitude,
                // Production bend weights / limits (BasisFullIKConstraintJob.SetDefaultValues).
                spineBendPitch = 0.45f, spineBendYaw = 0.10f, spineBendRoll = 0.35f,
                upperChestBendPitch = 0.25f, upperChestBendYaw = 0.30f, upperChestBendRoll = 0.20f,
                spineMaxForwardDeg = 60f, spineMaxBackwardDeg = 25f, spineMaxLateralDeg = 25f,
                spineSquishBoost = 0.5f, spineGazeFollow = 0.25f,
                // Pelvis synthesis constants. All three stages are suppressed by the hips tracker; fed so
                // the rig is the shipped rig rather than one whose gates are invisible.
                hipHingeStartDeg = 40f, hipHingeMaxAddDeg = 52f,
                trunkCounterbalance = BasisTrunkCounterbalanceCore.DerivedGain,
                moveBodyBackWhenCrouching = 1f,
                // Production arm-swing chest follow, live only on the NO-chest branch and only because the
                // hands are enabled here and fed below -- at the default zero weights it early-returns and
                // the control in ArmSwingChestFollow_StillDrivesTheNoChestBranch would prove nothing.
                chestArmSwingFactor = 0.3f, chestArmSwingMaxDeg = 15f,
                enabledLeftHand = 1f, enabledRightHand = 1f,
                // ⚠️ MUST BE SET. Production ships 90 (FBIKMaxChestDelta). It is BOTH the chest tracker's
                // clamp (SolveSpine, and again inside ReassertTrackedChest) AND the CCD's chest cone via
                // ClampChestCone, so leaving it at its zero default measures a solver nobody runs.
                MaxChestDeltaProperty = 90f,
                // Production CCD parameters.
                spineCCDRelax = 1.0f,
                spineTwistKeep = 0.25f,
                spineNeckTwistKeep = 0.9f,
                neckMaxConeDeg = 45f,
                spineTolerance = 0.001f,
                spineMaxIterations = 20,
                ChainSpineRestFrames = rig.RestFrames,
                ChainSpineRoms = rig.Roms,
            };

            // chestSpringState / chestSpringInit are deliberately left UNCREATED, as the reference harnesses
            // leave them: ApplyChestSpring checks IsCreated and passes the target through untouched. That
            // keeps every solve independent of every other, which is what lets the A/B below subtract.
            return rig;
        }

        /// <summary>
        /// Bakes each vertebra's anatomical rest frame and ROM parallel to the chain, mirroring
        /// BasisFullIKConstraintJob.BuildSpineAnatomy. Head (chain 0) and hips (chain 5) are left
        /// Valid=false on purpose -- commanded, not solved, therefore never guarded. The tracked chest
        /// joins them in that category at the reassert, which is why ReassertTrackedChest deliberately does
        /// not run GuardSpineJoint: measured, guarding a measurement costs 1.45 -> 5.17 deg of chest error.
        /// </summary>
        static void BuildSpineAnatomy(Rig rig, BasisMotionClip clip, int restFrame)
        {
            rig.RestFrames = new NativeArray<BasisSpineRestFrame>(6, Allocator.Persistent);
            rig.Roms = new NativeArray<BasisSpineRom>(6, Allocator.Persistent);

            // The subject's RIGHT: a body-wide fact taken from the shoulders where they exist, never a
            // bone's local axis. The upper legs are the fallback for a clip with no arm data.
            Vector3 hipsRight;
            if (clip.Has(BasisMocapJoint.LeftUpperArm) && clip.Has(BasisMocapJoint.RightUpperArm))
            {
                hipsRight = clip.Get(restFrame, BasisMocapJoint.RightUpperArm).Position
                          - clip.Get(restFrame, BasisMocapJoint.LeftUpperArm).Position;
            }
            else
            {
                hipsRight = clip.Get(restFrame, BasisMocapJoint.RightUpperLeg).Position
                          - clip.Get(restFrame, BasisMocapJoint.LeftUpperLeg).Position;
            }

            var segments = new (int chainIdx, BasisSpineSegment seg)[]
            {
                (1, BasisSpineSegment.Cervical),        // Neck
                (2, BasisSpineSegment.UpperThoracic),   // UpperChest
                (3, BasisSpineSegment.LowerThoracic),   // Chest
                (4, BasisSpineSegment.Lumbar),          // Spine
            };
            foreach ((int chainIdx, BasisSpineSegment seg) in segments)
            {
                Transform bone = rig.Bones[5 - chainIdx];
                Transform child = rig.Bones[5 - (chainIdx - 1)];
                Transform parent = rig.Bones[5 - (chainIdx + 1)];
                rig.RestFrames[chainIdx] = BasisSpineAnatomy.BuildRestFrame(
                    bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                rig.Roms[chainIdx] = BasisSpineAnatomy.Rom(seg);
            }
        }

        // ------------------------------------------------------------------------------------ the sweep

        /// <summary>
        /// One full SolveSpine through the REAL public dispatch, returning the chest bone's final world
        /// rotation and how far the head landed from where it was commanded.
        ///
        /// A pure gaze ORBITS the HMD about the neck pivot: the head target POSITION and its ROTATION both
        /// move and nothing else about the body does. That is what a user looking down actually does, and
        /// it is the input the solver actually receives -- feeding a rotation without the orbit would be a
        /// motion no headset can produce.
        /// </summary>
        static Quaternion SolveChestFor(Rig rig, BasisMotionClip clip, int f, Vector3 fwd,
            float gazeDeg, Vector3 handShift, out float headErr)
        {
            Vector3 headPos = clip.Get(f, BasisMocapJoint.Head).Position;
            Vector3 neckPos = clip.Get(f, BasisMocapJoint.Neck).Position;
            Vector3 hipsPos = clip.Get(f, BasisMocapJoint.Hips).Position;
            Quaternion gaze = GazeFrame(neckPos, headPos, fwd);

            if (gazeDeg != 0f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, fwd).normalized;   // pitch axis (look down)
                Quaternion orbit = Quaternion.AngleAxis(gazeDeg, axis);
                headPos = neckPos + orbit * (headPos - neckPos);
                gaze = orbit * gaze;
            }

            Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z);
            Quaternion liveYaw = fwdFlat.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(fwdFlat.normalized, Vector3.up)
                : rig.RestYaw;
            // The bones bind at identity, so the pelvis is commanded as a DELTA off the subject's own rest
            // facing -- the headset-only convention the corpus harnesses established.
            Quaternion deltaYaw = liveYaw * Quaternion.Inverse(rig.RestYaw);

            // Re-gather: the stream reloads from the (never scattered) rest transforms, so every solve
            // starts from rest and inherits no residue from the one before it. The A/B below subtracts two
            // solves, and that subtraction is only a measurement if they are independent.
            rig.Skeleton.GatherNow();

            rig.Job.targetPositionHead = headPos;
            rig.Job.targetRotationHead = gaze;
            rig.Job.targetPositionHips = hipsPos;
            rig.Job.targetRotationHips = deltaYaw;

            // A chest tracker IS chest data. Fed as a delta off the subject's rest chest frame to match the
            // identity bind, exactly as the pelvis yaw is. HasChestTracker with no feed is not an 8-point
            // user, it is a stale identity, and the row would measure the absence of data.
            rig.Job.targetChestRotation = ChestFrame(clip, f, fwd) * Quaternion.Inverse(rig.RestChest);
            // ⚠️ TargetChestPosition is HINT-BIASED (~8 cm of head-hint push); Raw is the unbiased one, and
            // it is Raw that SolveChestTarget reads.
            rig.Job.TargetChestPosition = clip.Get(f, BasisMocapJoint.Chest).Position;
            rig.Job.TargetChestPositionRaw = rig.Job.TargetChestPosition;

            // Two controllers. Feeds ApplyArmSwingChestFollow on the no-chest branch.
            rig.Job.targetPositionLeftHand = clip.Get(f, BasisMocapJoint.LeftHand).Position + handShift;
            rig.Job.targetPositionRightHand = clip.Get(f, BasisMocapJoint.RightHand).Position + handShift;

            rig.Job.SolveSpine(rig.Skeleton.Stream);

            headErr = (rig.HeadHandle.GetPosition(rig.Skeleton.Stream) - headPos).magnitude;
            return rig.ChestHandle.GetRotation(rig.Skeleton.Stream);
        }

        /// <summary>Everything one branch of the dispatch measures. Angles in degrees, head error in metres.</summary>
        sealed class Probe
        {
            /// <summary>Degrees the chest bone travelled per degree of pure gaze, tracker held fixed.</summary>
            public readonly List<float> GazeCoupling = new List<float>();
            /// <summary>Degrees the chest bone travelled per metre of lateral hand swing.</summary>
            public readonly List<float> HandCoupling = new List<float>();
            /// <summary>Angle between the solved chest and the chest the tracker reported.</summary>
            public readonly List<float> ChestVsTracker = new List<float>();
            /// <summary>|solved head - commanded head|, metres, over every solve in the sweep.</summary>
            public readonly List<float> HeadError = new List<float>();
            public float WorstGazeDeg;
        }

        static (float mean, float p95) Stats(List<float> e)
        {
            if (e.Count == 0) return (0f, 0f);
            var s = new List<float>(e);
            s.Sort();
            return (e.Average(), s[(int)(s.Count * 0.95f)]);
        }

        /// <summary>
        /// The whole measurement, for one setting of HasChestTracker. Identical inputs on both branches --
        /// one flag is the only difference, which is what makes the control a control.
        /// </summary>
        static Probe RunSweep(List<BasisMotionClip> clips, bool chestTracker)
        {
            var probe = new Probe();
            foreach (BasisMotionClip clip in clips)
            {
                using Rig rig = BuildRig(clip, MostUprightFrame(clip));
                rig.Job.HasChestTracker = chestTracker;

                int stride = Mathf.Max(1, clip.FrameCount / 40);
                Vector3 fallbackFwd = Vector3.forward;
                for (int f = 0; f < clip.FrameCount; f += stride)
                {
                    Vector3 fwd = PelvisForward(clip, f, fallbackFwd);
                    fallbackFwd = fwd;

                    // The baseline pose: this frame exactly as captured, nothing perturbed. Solved once and
                    // reused -- every solve re-gathers from rest, so it is a pure function of its inputs.
                    Quaternion baseChest = SolveChestFor(rig, clip, f, fwd, 0f, Vector3.zero, out float baseHeadErr);
                    probe.HeadError.Add(baseHeadErr);
                    probe.ChestVsTracker.Add(Quaternion.Angle(baseChest,
                        ChestFrame(clip, f, fwd) * Quaternion.Inverse(rig.RestChest)));

                    // ---- GAZE PROBE: pure head rotation about the neck pivot, torso byte-identical ----
                    foreach (float deg in k_GazeDegrees)
                    {
                        Quaternion gazed = SolveChestFor(rig, clip, f, fwd, deg, Vector3.zero, out float headErr);
                        float moved = Quaternion.Angle(baseChest, gazed);
                        probe.GazeCoupling.Add(moved / deg);
                        probe.HeadError.Add(headErr);
                        probe.WorstGazeDeg = Mathf.Max(probe.WorstGazeDeg, moved);
                    }

                    // ---- HAND PROBE: both hands swung laterally, head + chest + hips fixed ----
                    foreach (float m in k_HandOffsetsM)
                    {
                        Vector3 lateral = Vector3.Cross(Vector3.up, fwd).normalized * m;
                        Quaternion swung = SolveChestFor(rig, clip, f, fwd, 0f, lateral, out _);
                        probe.HandCoupling.Add(Quaternion.Angle(baseChest, swung) / m);
                    }
                }
            }
            return probe;
        }

        // The sweep is the expensive part (BVH load + ~11k solves) and all four tests read the same two
        // branches, so it runs once. Both branches are always measured, so a test can never accidentally
        // assert on a branch that was never run.
        static Probe s_Tracked;
        static Probe s_NoChest;
        static string s_Report;

        [OneTimeSetUp]
        public void Measure()
        {
            List<BasisMotionClip> clips = LoadClips();
            s_Tracked = RunSweep(clips, chestTracker: true);
            s_NoChest = RunSweep(clips, chestTracker: false);

            var sb = new StringBuilder();
            sb.AppendLine($"CHEST AUTHORITY SWEEP ({clips.Count} clips, {s_Tracked.GazeCoupling.Count} gaze samples per branch)");
            sb.AppendLine("  torso, chest tracker and hips held EXACTLY fixed; only the probe input moves.");
            sb.AppendLine($"  {"",-22} {"chest tracker",12} {"no chest tracker",17}");
            Append(sb, "gaze deg/deg (mean)", Stats(s_Tracked.GazeCoupling).mean, Stats(s_NoChest.GazeCoupling).mean);
            Append(sb, "gaze deg/deg (p95)", Stats(s_Tracked.GazeCoupling).p95, Stats(s_NoChest.GazeCoupling).p95);
            Append(sb, "worst gaze swing deg", s_Tracked.WorstGazeDeg, s_NoChest.WorstGazeDeg);
            Append(sb, "chest-vs-tracker deg", Stats(s_Tracked.ChestVsTracker).mean, Stats(s_NoChest.ChestVsTracker).mean);
            Append(sb, "hand deg/m (mean)", Stats(s_Tracked.HandCoupling).mean, Stats(s_NoChest.HandCoupling).mean);
            Append(sb, "head error cm (mean)", Stats(s_Tracked.HeadError).mean * 100f, Stats(s_NoChest.HeadError).mean * 100f);
            Append(sb, "head error cm (p95)", Stats(s_Tracked.HeadError).p95 * 100f, Stats(s_NoChest.HeadError).p95 * 100f);
            sb.AppendLine("  published before/after the fix: gaze 0.402 -> 0.008, chest-vs-tracker 11.68 -> 1.45 deg");
            s_Report = sb.ToString();
        }

        static void Append(StringBuilder sb, string label, float tracked, float noChest)
        {
            sb.AppendLine($"  {label,-22} {tracked,12:F3} {noChest,17:F3}");
        }

        [OneTimeTearDown]
        public void Release()
        {
            s_Tracked = null;
            s_NoChest = null;
            s_Report = null;
        }

        // ============================================================================== THE HEADLINE

        /// <summary>
        /// THE COMPLAINT, DIRECTLY. Chest tracker on, hips pinned, tracker held at the frame's own
        /// measurement, and only the head moves. The chest must not follow it.
        ///
        /// The second assertion is the ANTI-TAUTOLOGY CONTROL and is not optional. It runs the identical
        /// sweep on the identical rig with HasChestTracker flipped false -- the branch that has no term for
        /// the chest at all -- and requires that coupling to be LARGE. A harness whose chest simply never
        /// moved (a dead solve, an unfed target, a chain indexed root->tip so the "chest" is really the neck)
        /// would sail through the first assertion and fail this one. Both numbers come out of one function,
        /// on one rig, with one flag between them.
        /// </summary>
        [Test]
        public void TrackedChest_IsNotDraggedByTheGaze()
        {
            TestContext.WriteLine(s_Report);

            float tracked = Stats(s_Tracked.GazeCoupling).mean;
            float control = Stats(s_NoChest.GazeCoupling).mean;

            Assert.Less(tracked, k_MaxTrackedGazeCoupling,
                $"THE HEAD IS DRAGGING THE TRACKED CHEST AGAIN ({tracked:F3} deg per deg of gaze). The chest "
                + "tracker is a MEASUREMENT and the user did not move their chest; a real one pitches "
                + "-0.05 deg/deg. This measured 0.402 before ReassertTrackedChest and 0.008 after, so a "
                + "figure back up near the gate means the post-solve re-assert has been removed, moved "
                + "before the CCD, gated out, or had its head-restore widened to touch the chest or the "
                + "Spine beneath it (it may use ONLY chestBoneIdx-1 down to firstJoint). Note the obvious "
                + "knobs do NOT fix this: chestIkTarget is a POSITION pull, and turning MaxChestDeltaProperty "
                + "down goes backwards because it clamps the chest against its parent, so the Spine moves "
                + "instead and carries the chest with it.");

            Assert.Greater(control, k_MinControlGazeCoupling,
                $"THE CONTROL DID NOT MOVE ({control:F3} deg per deg of gaze on the NO-chest-tracker branch, "
                + "which has no term for the chest whatsoever and measured 0.390). This is the anti-tautology "
                + "check: the assertion above only means something if this rig can produce a large coupling "
                + "when nothing is holding the chest. A dead solve, an unfed head target, or a chain indexed "
                + "root->tip (which would rotate the NECK while believing it is rotating the chest) all read "
                + "zero here. Fix the harness before trusting anything else in this file.");
        }

        // ============================================================== THE CHEST IS WHERE IT WAS MEASURED

        /// <summary>
        /// The other half of the same claim: not merely "the chest does not move with the head" but "the
        /// chest is where the tracker says it is". Measured 11.68 deg before the fix and 1.45 after.
        ///
        /// The residual is not zero and is not supposed to be: ReassertTrackedChest re-clamps against the
        /// POST-solve neck and spine through MaxChestDeltaProperty, which is the same bound the pre-solve
        /// write has always used, and that clamp is allowed to bite.
        ///
        /// Paired with the same measurement on the no-chest branch, where nothing ever writes the tracker
        /// and the error must be large -- otherwise this would pass on a corpus where the solved chest
        /// happened to sit near the measurement anyway.
        /// </summary>
        [Test]
        public void TrackedChest_StaysOnItsTracker()
        {
            TestContext.WriteLine(s_Report);

            float tracked = Stats(s_Tracked.ChestVsTracker).mean;
            float control = Stats(s_NoChest.ChestVsTracker).mean;

            Assert.Less(tracked, k_MaxTrackedChestError,
                $"THE SOLVED CHEST IS NOT WHERE THE TRACKER SAYS IT IS ({tracked:F2} deg mean, was 11.68 "
                + "before ReassertTrackedChest and 1.45 after). The chest branch writes the measurement "
                + "BEFORE the solve and the CCD then overwrites it while chasing the head, so a regression "
                + "here means the post-solve re-assert stopped happening -- or that it is now being run "
                + "through GuardSpineJoint, which is deliberately avoided: a tracked chest is commanded, not "
                + "solved, and running the anatomical envelope over a measurement costs 1.45 -> 5.17 deg.");

            Assert.Greater(control, k_MinControlChestError,
                $"THE CONTROL IS ALREADY ON THE TRACKER ({control:F2} deg on the NO-chest branch, which never "
                + "writes the chest measurement at all). Then this corpus cannot tell a pinned chest from an "
                + "unpinned one and the assertion above proves nothing. Suspect the chest feed, the rest-frame "
                + "delta, or a chain whose index 2 is not the chest bone.");
        }

        // ========================================================= THE NO-CHEST BRANCH IS UNTOUCHED

        /// <summary>
        /// SCOPE. ReassertTrackedChest is gated on HasChestTracker, so the 3-point / hips-only user must be
        /// bit-unaffected -- and on that branch a hand->chest coupling is not a bug, it is
        /// ApplyArmSwingChestFollow doing exactly what it ships to do (measured 27.07 deg per metre of
        /// lateral hand swing). If this ever collapsed toward zero, the change would have leaked out of the
        /// tracker branch and quietly frozen every desktop user's torso.
        ///
        /// The tracked side is asserted too, from the other direction: with a chest tracker the arm-swing
        /// follow is skipped by design (that branch owns chest rotation directly) and measures 0.02 deg/m,
        /// so the reassert must not have introduced a new hand path into the chest.
        /// </summary>
        [Test]
        public void ArmSwingChestFollow_StillDrivesTheNoChestBranch()
        {
            TestContext.WriteLine(s_Report);

            float noChest = Stats(s_NoChest.HandCoupling).mean;
            float tracked = Stats(s_Tracked.HandCoupling).mean;

            Assert.Greater(noChest, k_MinNoChestHandCoupling,
                $"ApplyArmSwingChestFollow HAS GONE QUIET ON THE NO-CHEST BRANCH ({noChest:F2} deg per metre "
                + "of hand swing, measured 27.07). That stage is the whole distinguishing feature of the "
                + "else-side of the dispatch, it is what a 3-point user's chest follow IS, and the tracked-"
                + "chest fix is gated on HasChestTracker specifically so it cannot reach here. A collapse to "
                + "zero means the gate leaked -- or that the hands stopped being fed/enabled, in which case "
                + "the stage early-returns and this file is no longer testing the branch it claims to.");

            Assert.Less(tracked, k_MaxTrackedHandCoupling,
                $"THE HANDS ARE MOVING A TRACKED CHEST ({tracked:F2} deg per metre, measured 0.02). With a "
                + "chest tracker the arm-swing follow is skipped by design -- that branch owns chest rotation "
                + "directly -- so this is either the gate failing or a new hand->chest path.");
        }

        // ================================================================ THE HEAD IS STILL PLACED

        /// <summary>
        /// THE FIX MUST NOT BE "PIN THE CHEST AND ABANDON THE HMD". ReassertTrackedChest re-applies the
        /// measurement and then restores the head with the joints ABOVE the chest only -- upperChest and
        /// neck -- which have spare DOF precisely so the head can come back without disturbing what was
        /// just pinned. If that restore were dropped, every chest assertion in this file would still pass
        /// while the avatar's head sat somewhere the headset is not.
        ///
        /// The cost of the trade is real and lands on the NECK (+4.4 deg mean of bend), which is a posture
        /// question and not a placement one; this test only guards placement.
        /// </summary>
        [Test]
        public void TheHeadIsStillPinnedToItsTarget()
        {
            TestContext.WriteLine(s_Report);

            float trackedCm = Stats(s_Tracked.HeadError).mean * 100f;
            float noChestCm = Stats(s_NoChest.HeadError).mean * 100f;

            Assert.Less(trackedCm, k_MaxHeadErrorCm,
                $"THE HEAD IS NO LONGER LANDING ON ITS TARGET with a chest tracker ({trackedCm:F2} cm mean). "
                + "The head is welded to the HMD and is never traded for the chest: ReassertTrackedChest runs "
                + "k_ChestReassertHeadRestoreSweeps sweeps of ReachHeadJoint over the joints above the "
                + "chest for exactly this reason, and SolveSequentialSpineIK still pins the head ROTATION "
                + "afterwards. Suspect the restore loop bounds or the sweep count.");

            Assert.Less(noChestCm, k_MaxHeadErrorCm,
                $"the no-chest branch stopped placing the head ({noChestCm:F2} cm mean) -- that branch is not "
                + "touched by this change at all, so suspect the harness or an unrelated CCD regression.");
        }
    }
}
