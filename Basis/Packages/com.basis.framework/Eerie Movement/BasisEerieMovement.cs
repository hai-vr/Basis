using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
    [Unity.Burst.BurstCompile]
    public struct BasisEerieMovement : Unity.Jobs.IJob
    {
        public const float k_Epsilon = 1e-5f; // or 0.00001f
        public const float k_MinMag = 1e-6f;// or 0.000001f
        public const float k_SqrEpsilon = 1e-8f;// or 0.00000001f

        public BasisBoneHandle HandleChest, HandleNeck, HandleHead,
  HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot,
  HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot,
  HandleHips, HandleSpine, HandleUpperChest,
            HandleLeftShoulder, HandleRightShoulder,

  HandleLeftToe, HandleRightToe,
  HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand,
  HandleRightUpperArm, HandleRightLowerArm, HandleRightHand,
  HandleLeftUpperArmTwist, HandleLeftLowerArmTwist,
  HandleRightUpperArmTwist, HandleRightLowerArmTwist;

        public Vector3 targetPositionHead, TargetChestPosition, TargetChestPositionRaw, playerUp, KneeBendPrefLeft, KneeBendPrefRight, KneeAnteriorRef,
targetPositionLeftLowerLeg, hintPositionLeftLowerLeg,
targetPositionRightLowerLeg, hintPositionRightLowerLeg,
targetPositionHips,
targetPositionLeftHand, hintPositionLeftHand,
targetPositionRightHand, hintPositionRightHand;

        public Quaternion targetRotationHead, targetChestRotation,
targetRotationLeftLowerLeg,
targetRotationRightLowerLeg,
targetRotationHips, offsetRotationHips,
offsetRotationHead, offsetRotationChest, offsetRotationLeftFoot, offsetRotationRightFoot,
offsetRotationLeftToe, offsetRotationRightToe, offsetRotationLeftShoulder, offsetRotationRightShoulder,
offsetRotationLeftHand, offsetRotationRightHand,
leftDrivenTargetRot, rightDrivenTargetRot,
targetRotationLeftHand, hintRotationLeftHand,
targetRotationRightHand, hintRotationRightHand,
hintRotationLeftLowerLeg, hintRotationRightLowerLeg,
TargetRotationLeftShoulder, TargetRotationRightShoulder, targetOffsetHead, targetOffsetChest, targetOffsetLeftToe,
            targetOffsetRightToe, targetOffsetLeftShoulder, targetOffsetRightShoulder, targetOffsetLeftFoot,
            targetOffsetRightFoot, targetOffsetLeftHand, targetOffsetRightHand;

        public float
enabledLeftLowerLeg, enabledRightLowerLeg,
hintWeightLeftLowerLeg, hintWeightRightLowerLeg,
enabledLeftHand, enabledRightHand;

        public bool
HasChestTracker, hasHipsTracker, enabledSpineIK,
            enabledLeftShoulder, enabledRightShoulder,

leftToeEnabled, RightToeEnabled,
hintWeightLeftHand,
hintWeightRightHand,
protectElbow, collideTrackedElbow,
elbowDragEnabled,
wristAxialBound,
collisionsEnabled;

        public float elbowDragHz;

        public float leftToeBendDeg, rightToeBendDeg;
        public Vector3 leftToeBendAxis, rightToeBendAxis;
        public FixedList512Bytes<Vector3> slotPositions;
        public FixedList512Bytes<Quaternion> slotRotations;
        public FixedList512Bytes<Quaternion> slotOffsets;
        public FixedList64Bytes<bool> slotWeights;
        public FixedList128Bytes<BasisBoneHandle> slotHandles;
        public NativeArray<BasisBoneHandle> ChainHeadToSpine;
        public NativeArray<BasisSpineRestFrame> ChainSpineRestFrames;
        public NativeArray<BasisSpineRom> ChainSpineRoms;
        public int spineMaxIterations;
        public float spineTolerance;
        public Vector3 TposeLengthHeadToHips;
        public Vector3 TposeHeadToNeckLocal;
        public Vector3 TposeLengthNeckToHips;
        public float TposeBakeScale;
        public float handRadius, handSkin, chestRadius, collisionSkin, MinHeadSpineHeight, maxBendDeg, minFactor, maxFactor, MaxChestDeltaProperty;
        public float shoulderElevationFactor, shoulderProtractionFactor;
        public float spineBendPitch, spineBendYaw, spineBendRoll;
        public float upperChestBendPitch, upperChestBendYaw, upperChestBendRoll;
        public float hipHingeStartDeg, hipHingeMaxAddDeg;
        public float chestSpringHz, chestSpringDamping;
        public float spineMaxForwardDeg, spineMaxBackwardDeg, spineMaxLateralDeg;
        public float spineSquishBoost;
        public float spineGazeFollow;
        public float neckGazeFollow;
        public float moveBodyBackWhenCrouching;
        public float crouchDepth;
        public float standingHeadHeight;
        public float trunkCounterbalance;
        public float swingSmoothRateDeg;
        public float chestArmSwingFactor, chestArmSwingMaxDeg;
        public float lowerArmTwistFraction, upperArmTwistFraction;
        public bool anatDifferentialStiffness, anatShoulderSlide, anatCervicalLordosis, anatPelvicTwistRouting, legSwivelSmoothing;
        public bool spineAnatomicalRom;
        public bool chestIkTarget;
        public bool hintIsTrackerLeftLowerLeg, hintIsTrackerRightLowerLeg;
        public bool footIsTrackerLeftLeg, footIsTrackerRightLeg;
        public float lordosisPitchGainDeg;
        public float lordosisBaseDeg, lordosisNeckShare, lordosisMaxHeadPitchDeg;
        public float lordosisExtremeStartDeg, lordosisExtremeFullDeg;
        public float lordosisExtremeRollForwardMaxDeg, lordosisExtremeRollBackwardMaxDeg;
        public float lordosisExtremeHipsHorizontalMax, lordosisExtremeChestHorizontalMax;
        public float lordosisExtremeHipsDownMax, lordosisExtremeChestDownMax;
        public float lordosisExtremeHipsDownLookUp, lordosisExtremeChestDownLookUp;
        public float spineCCDRelax, neckMaxConeDeg, spineTwistKeep, spineNeckTwistKeep;
        public NativeArray<Vector3> chestSpringState;
        public NativeArray<int> chestSpringInit;
        public const int k_SwingLeftElbow = 0, k_SwingRightElbow = 1, k_SwingLeftKnee = 2, k_SwingRightKnee = 3, k_SwingCount = 4;
        public NativeArray<Vector3> swingLastDir;
        public NativeArray<Vector3> swingLastAxis;
        public NativeArray<Vector3> swingLastTarget;
        public NativeArray<int> swingContinuityInit;
        public NativeArray<int> swingCollided;
        public NativeArray<float> swingSwivelDeg;
        public NativeArray<int> swingGuardSide;
        public NativeArray<int> swingSmoothState;
        public NativeArray<Vector3> swingHintBend;
        public NativeArray<Vector3> swingHintAxis;
        public NativeArray<float> swingHintReach;
        public NativeArray<Vector3> swingHintDrag;
        public NativeArray<Quaternion> swingHintBodyRot;
        public NativeArray<int> swingHintInit;
        public NativeArray<Vector3> swingPoleAnchor;
        public NativeArray<Quaternion> swingPoleAnchorRot;
        public NativeArray<int> swingPoleAnchorInit;
        public NativeArray<Vector3> legSwivelRaw;
        public NativeArray<Vector3> legSwivelSmooth;
        public NativeArray<int> legSwivelInit;
        public NativeArray<BasisLegDiagnostics> legDiagnostics;
        public NativeArray<BasisArmDiagnostics> armDiagnostics;
        public bool armDiagnosticsEnabled;
        public float ikLockMode;
        public bool shoulderSolveEnabled;
        public bool shoulderShrugEnabled;
        public bool shoulderRetractionEnabled;
        public bool shoulderRhythmEnabled;
        public Quaternion TposeLeftUpperArmRot, TposeRightUpperArmRot;
        public Quaternion TposeLeftLowerArmRot, TposeRightLowerArmRot;
        public Quaternion TposeLeftHandRot, TposeRightHandRot;
        public Vector3 TposeLeftHumerusDir, TposeRightHumerusDir;
        public Vector3 TposeLeftHumerusRefAxis, TposeRightHumerusRefAxis;
        public Vector3 TposeLeftShoulderLocalDir, TposeRightShoulderLocalDir;
        public Quaternion TposeLeftShoulderRot, TposeRightShoulderRot;
        public Quaternion TposeChestRot;
        public Quaternion TposeChestBind;
        public float TposeShoulderToHandLeft, TposeShoulderToHandRight;
        public float TposeClavicleLenLeft, TposeClavicleLenRight;
        public float TposeShoulderToElbowLeft, TposeShoulderToElbowRight;
        public BasisPoseStream Stream;
        public const float k_ThoracicBendStiffen = 0.3f;
        public const float k_SpineTautBandFrac = 0.015f;
        public const float k_BendTwistCoupling = 0.15f;
        public const float k_ChestPosPullMaxDeg = 20f;
        public const float k_ChestPullMaxDistSqr = 0.25f;
        public const float k_ChestFollowChestShare = 0.6f;
        public const int k_ChestReassertHeadRestoreSweeps = 2;
        public const int k_ChestReassertBarrierProbes = 5;
        public const float k_ChestReassertMaxHeadErr = 0.010f;
        public const float k_ChestIkWeight = 0.5f;
        public const int k_ChestIkIters = 8;
        public const int k_ChestIkHeadRestoreSweeps = 2;
        const float k_TrunkCounterbalanceMaxSpineFrac = 0.45f;
        const float k_NeckGazeFollowMaxDeg = 18f;
        const float k_TrackedKneeSwivelMinCutoffHz = 1.5f;  // held-still smoothing floor (vs 1.0 standing)
        const float k_TrackedKneeSwivelBeta = 0.20f;        // 4x standing: opens fast so real shin motion isn't lagged
        const float k_TrackedKneeSwivelDerivCutoffHz = 1.0f;
        public const int Count = 22;
        // Slots are HumanBodyBones values: 0..RightToes map directly, UpperChest (54) maps to the last slot.
        public const int UpperChestSlot = Count - 1;
        public void Execute() => ProcessAnimation(Stream);
        public void ProcessAnimation(BasisPoseStream stream)
        {
            targetOffsetHead = offsetRotationHead;
            targetOffsetChest = offsetRotationChest;
            targetOffsetLeftFoot = offsetRotationLeftFoot;
            targetOffsetRightFoot = offsetRotationRightFoot;
            targetOffsetLeftToe = offsetRotationLeftToe;
            targetOffsetRightToe = offsetRotationRightToe;
            targetOffsetLeftShoulder = offsetRotationLeftShoulder;
            targetOffsetRightShoulder = offsetRotationRightShoulder;
            targetOffsetLeftHand = offsetRotationLeftHand;
            targetOffsetRightHand = offsetRotationRightHand;

            // 1) Spine: hips + chest/neck/head chain
            SolveSpine(stream);

            // 1b) Anatomy modifiers that act on the spine after the main solve.
            if (anatCervicalLordosis)
            {
                ApplyCervicalLordosis(stream);
            }

            BasisEerieArms.SolveArms(ref this, stream);

            // 3) Legs: two-bone IK with bend normal preference
            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, KneeBendPrefLeft, hintIsTrackerLeftLowerLeg, footIsTrackerLeftLeg, 0);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, KneeBendPrefRight, hintIsTrackerRightLowerLeg, footIsTrackerRightLeg, 1);
            // 5) Toes. A toe TRACKER wins outright; otherwise the procedural surface bend from the foot driver
            // articulates the toe over stair noses, kerbs and ramps.
            if (leftToeEnabled) ApplyRotation(stream, true, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            else ApplyToeSurfaceBend(stream, HandleLeftToe, leftToeBendDeg, leftToeBendAxis);

            if (RightToeEnabled) ApplyRotation(stream, true, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);
            else ApplyToeSurfaceBend(stream, HandleRightToe, rightToeBendDeg, rightToeBendAxis);

            // 6) Generic per-bone overrides (direct tracker control)
            for (int i = 0; i < slotHandles.Length; i++)
            {
                Apply(stream, slotHandles[i], slotPositions[i], slotRotations[i], slotOffsets[i], slotWeights[i]);
            }
        }
        public void SolveSpine(BasisPoseStream stream)
        {
            if (!enabledSpineIK)
            {
                return;
            }
            // ---- Read targets ----
            Vector3 headTargetPos = targetPositionHead;
            Vector3 hipsTargetPos = targetPositionHips;

            Quaternion headTargetRot = targetRotationHead;
            Quaternion hipsTargetRot = targetRotationHips;
            Quaternion offsetHips = offsetRotationHips;
            Quaternion chestTargetRot = targetChestRotation;

            Quaternion hipDesired = hipsTargetRot * offsetHips;
            Quaternion chestDesired = chestTargetRot * targetOffsetChest;

            float restDist = MinHeadSpineHeight;
            int lockMode = (int)ikLockMode;
            Vector3 up = playerUp;

            // Lock mode determines how hips position relates to head position:
            // 0 = LockHips:  Hips are the anchor; apply hips directly, no head-relative clamping.
            // 1 = LockHead:  Head is the anchor; hips ride at rest spine length along the spine's own axis.
            // 2 = LockBoth:  Both independently positioned; spine must accommodate (original behavior).
            switch (lockMode)
            {
                case 0: // LockHips - hips are authoritative, skip head-relative clamping
                    break;

                case 1: // LockHead - head is the anchor; the spine may not compress below its rest length, allow stretching further
                    {
                        Vector3 headToHips = hipsTargetPos - headTargetPos;
                        float spineLen = headToHips.magnitude;
                        if (spineLen < restDist)
                        {
                            Vector3 spineDir = spineLen > k_Epsilon ? headToHips / spineLen : hipsTargetRot * Vector3.down;
                            hipsTargetPos = headTargetPos + spineDir * restDist;
                        }
                    }
                    break;

                default: // LockBoth (2) - original behavior: clamp hips relative to head
                    hipsTargetPos = AntiContortionist(headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, restDist);
                    hipsTargetPos = MitigateSpineBuckling(headTargetPos, hipsTargetRot, hipsTargetPos, restDist, up);
                    float MaxBendDeg = maxBendDeg;
                    hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                    hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor, maxFactor, up);
                    break;
            }
            Vector3 neckCue = ComputeNeckCue(headTargetPos);
            float crouchFade = 1f;
            if (!hasHipsTracker)
            {
                hipsTargetPos = ApplyTrunkCounterbalance(neckCue, hipsTargetPos, up, out float flexionFrac);
                crouchFade = 1f - flexionFrac;
            }
            hipsTargetPos = ApplyCrouchBodyOffset(stream, headTargetPos, hipsTargetPos, hipDesired, up, crouchFade);
            targetPositionHips = hipsTargetPos;
            if (!hasHipsTracker)
            {
                hipDesired = ApplyHipHinge(stream, neckCue, hipsTargetPos, hipDesired, up);
            }

            // Apply hips driver if valid
            if (HandleHips.IsValid(stream))
            {
                HandleHips.SetPosition(stream, hipsTargetPos);
                HandleHips.SetRotation(stream, hipDesired);
            }
            if (HasChestTracker && HandleChest.IsValid(stream))
            {
                // Neck rotation produced by your spine IK pass – we keep this
                Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;

                // Spine as an extra reference if available (nice stabiliser)
                Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;

                float Value = MaxChestDeltaProperty;
                // Clamp relative to neck and spine
                Quaternion clampedChestRot = ClampRotation(chestDesired, neckRot, Value);
                clampedChestRot = ClampRotation(clampedChestRot, spineRot, Value);

                HandleChest.SetRotation(stream, clampedChestRot);

                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

                DistributeSpineBend(stream, headPos);
                BiasSpineTowardChest(stream);
                GuardSpineChain(stream);
                SolveSequentialSpineIK(stream, headPos, headRot);
            }
            else if (HandleChest.IsValid(stream) && HandleNeck.IsValid(stream) && HandleHead.IsValid(stream))
            {
                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

                DistributeSpineBend(stream, headPos);
                BasisEerieArms.ApplyArmSwingChestFollow(ref this, stream);
                GuardSpineChain(stream);
                SolveSequentialSpineIK(stream, headPos, headRot);
            }
        }
        public void SolveSequentialSpineIK(BasisPoseStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
        {
            if (!ChainHeadToSpine.IsCreated || ChainHeadToSpine.Length < 3)
                return;

            int chainLen = ChainHeadToSpine.Length;
            const int tipIdx = 0;
            const int firstJoint = 1;
            int lastJoint = chainLen - 2;

            for (int i = 0; i < chainLen; i++)
            {
                if (!ChainHeadToSpine[i].IsValid(stream))
                    return;
            }

            int maxIters = Mathf.Max(1, spineMaxIterations);
            float tolerance = Mathf.Max(0f, spineTolerance);
            float tolSqr = tolerance * tolerance;
            {
                Vector3 rootPos = ChainHeadToSpine[chainLen - 1].GetPosition(stream);
                float chainReach = 0f;
                for (int i = 0; i < chainLen - 1; i++)
                {
                    chainReach += (ChainHeadToSpine[i].GetPosition(stream) - ChainHeadToSpine[i + 1].GetPosition(stream)).magnitude;
                }
                Vector3 rootToTarget = headTargetPos - rootPos;
                float targetDist = rootToTarget.magnitude;
                if (targetDist > k_Epsilon && chainReach > k_Epsilon)
                {
                    float compression = chainReach - targetDist;
                    float commandedDist;
                    if (compression > 0f)
                    {
                        float band = k_SpineTautBandFrac * chainReach;
                        commandedDist = chainReach - compression * compression * compression / (compression * compression + band * band);
                    }
                    else
                    {
                        commandedDist = chainReach;
                    }
                    headTargetPos = rootPos + rootToTarget * (commandedDist / targetDist);
                }
            }

            float ccdRelax = spineCCDRelax;
            float lumbarTwistKeep = spineTwistKeep;
            float cervicalTwistKeep = spineNeckTwistKeep;
            // Body-relative twist axis (hips-up), NOT world-up: vertical standing, horizontal lying down, so
            // the relax strips the same anatomical axial-twist DOF in any orientation. Falls back to playerUp.
            Quaternion hipsTwistRot = HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : Quaternion.identity;
            Vector3 ccdUp = hipsTwistRot * Vector3.up;
            if (ccdUp.sqrMagnitude < k_SqrEpsilon) ccdUp = playerUp;
            float jointSpan = Mathf.Max(1, lastJoint - firstJoint);
            float neckCone = neckMaxConeDeg;
            float chestCone = MaxChestDeltaProperty;
            Quaternion finalHeadRot = headTargetRot * targetOffsetHead;

            for (int iter = 0; iter < maxIters; iter++)
            {
                Vector3 tipPos = ChainHeadToSpine[tipIdx].GetPosition(stream);
                if ((headTargetPos - tipPos).sqrMagnitude < tolSqr)
                    break;

                // Walk from root-side (spine) toward tip-side (neck) so the longer-lever joints
                // take the bigger swing first; later passes through the loop fine-tune with the
                // shorter levers.
                for (int i = lastJoint; i >= firstJoint; i--)
                {
                    ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                        cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                }
            }
            SolveChestTarget(stream, headTargetPos, firstJoint, lastJoint, chainLen, jointSpan,
                cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);

            ReassertTrackedChest(stream, headTargetPos, firstJoint, chainLen, jointSpan,
                cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);

            ChainHeadToSpine[tipIdx].SetRotation(stream, finalHeadRot);
        }
        // One CCD step aiming the head tip from joint `i` -- the exact body of the Phase A loop, extracted so
        // Phase B's head-restore reuses it verbatim (a copy would drift). Shapes the reach (twist graded root
        // -> tip, mid-thoracic stiffened), relaxes, applies the cones, then the anatomy guard LAST.
        public void ReachHeadJoint(BasisPoseStream stream, int i, Vector3 headTargetPos, int firstJoint, int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp, float ccdRelax, float neckCone, float chestCone)
        {
            const int tipIdx = 0;
            Vector3 jointPos = ChainHeadToSpine[i].GetPosition(stream);
            Vector3 curTipPos = ChainHeadToSpine[tipIdx].GetPosition(stream);

            Vector3 cur = curTipPos - jointPos;
            Vector3 tgt = headTargetPos - jointPos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion delta = BasisQuaternionExt.FromToRotation(cur, tgt);
            float t = (i - firstJoint) / jointSpan;
            float jointTwistKeep = Mathf.Lerp(cervicalTwistKeep, lumbarTwistKeep, t);
            float jointSwingScale = 1f - k_ThoracicBendStiffen * (1f - Mathf.Abs(2f * t - 1f));
            delta = BasisTwistSolveCore.ShapeReachStep(delta, ccdUp, jointTwistKeep, jointSwingScale);
            delta = Quaternion.Slerp(Quaternion.identity, delta, ccdRelax);
            ChainHeadToSpine[i].SetRotation(stream, delta * ChainHeadToSpine[i].GetRotation(stream));

            if (i == firstJoint)
            {
                ClampNeckCone(stream, i, neckCone);
            }
            else if (chainLen >= 5 && i == chainLen - 3)
            {
                ClampChestCone(stream, i, chestCone);
            }
            GuardSpineJoint(stream, i);
        }
        public void ReassertTrackedChest(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint,
            int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp,
            float ccdRelax, float neckCone, float chestCone)
        {
            if (!HasChestTracker || !HandleChest.IsValid(stream))
                return;

            int chestBoneIdx = chainLen - 3;
            if (chestBoneIdx <= firstJoint || chestBoneIdx >= chainLen)
                return;

            Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;
            Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;
            float maxDelta = MaxChestDeltaProperty;

            Quaternion solvedChestRot = HandleChest.GetRotation(stream);
            Quaternion chestDesired = targetChestRotation * targetOffsetChest;
            Quaternion clampedChestRot = ClampRotation(chestDesired, neckRot, maxDelta);
            clampedChestRot = ClampRotation(clampedChestRot, spineRot, maxDelta);
            float baseHeadErrSqr = (headTargetPos - ChainHeadToSpine[0].GetPosition(stream)).sqrMagnitude;
            float headTolSqr = Mathf.Max(k_ChestReassertMaxHeadErr * k_ChestReassertMaxHeadErr, baseHeadErrSqr);
            float accepted = 0f;
            float lo = 0f, hi = 1f;

            for (int probe = 0; probe < k_ChestReassertBarrierProbes; probe++)
            {
                float t = probe == 0 ? 1f : 0.5f * (lo + hi);

                HandleChest.SetRotation(stream, Quaternion.Slerp(solvedChestRot, clampedChestRot, t));
                for (int sweep = 0; sweep < k_ChestReassertHeadRestoreSweeps; sweep++)
                {
                    for (int i = chestBoneIdx - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }

                bool headHeld = (headTargetPos - ChainHeadToSpine[0].GetPosition(stream)).sqrMagnitude <= headTolSqr;
                if (headHeld)
                {
                    accepted = t;
                    lo = t;
                    if (probe == 0)
                        return;   // the tracker cost the head nothing: the pose already standing is the answer
                }
                else
                {
                    hi = t;
                }
            }

            {
                HandleChest.SetRotation(stream, Quaternion.Slerp(solvedChestRot, clampedChestRot, accepted));
                for (int sweep = 0; sweep < k_ChestReassertHeadRestoreSweeps; sweep++)
                {
                    for (int i = chestBoneIdx - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }
            }
        }
        public void SolveChestTarget(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint, int lastJoint, int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp, float ccdRelax, float neckCone, float chestCone)
        {
            if (!chestIkTarget || !HasChestTracker)
                return;

            int chestBoneIdx = chainLen - 3;   // the Chest bone
            // Need a real Spine joint below the chest to move it, and real upper joints to restore the head.
            if (chestBoneIdx < firstJoint || lastJoint <= firstJoint || lastJoint <= chestBoneIdx)
                return;

            // THE RAW chest, not the head-hint-biased TargetChestPosition -- pinning to the biased one dragged
            // the torso ~8cm up and leaned the body in desktop / no-tracker mode.
            Vector3 chestTargetPos = TargetChestPositionRaw;
            Vector3 chestBonePos = ChainHeadToSpine[chestBoneIdx].GetPosition(stream);
            // A chest target that is wildly far from the FK chest is a glitching tracker or an unset target;
            // chasing it would wreck the torso. Fall back to the head-only chest. Same guard the old
            // BiasSpineTowardChest used, and the anatomy guard below bounds whatever does get through.
            if ((chestTargetPos - chestBonePos).sqrMagnitude > k_ChestPullMaxDistSqr)
                return;

            // The Spine is the root end of the chain, so its shaping params are those of index lastJoint.
            float spineT = (lastJoint - firstJoint) / jointSpan;
            float spineTwistKeep = Mathf.Lerp(cervicalTwistKeep, lumbarTwistKeep, spineT);
            float spineSwingScale = 1f - k_ThoracicBendStiffen * (1f - Mathf.Abs(2f * spineT - 1f));

            for (int citer = 0; citer < k_ChestIkIters; citer++)
            {
                // 1) rotate the Spine so the Chest bone slides toward its target.
                Vector3 spinePos = ChainHeadToSpine[lastJoint].GetPosition(stream);
                Vector3 cCur = ChainHeadToSpine[chestBoneIdx].GetPosition(stream) - spinePos;
                Vector3 cTgt = chestTargetPos - spinePos;
                if (cCur.sqrMagnitude > k_SqrEpsilon && cTgt.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion cDelta = BasisQuaternionExt.FromToRotation(cCur, cTgt);
                    cDelta = BasisTwistSolveCore.ShapeReachStep(cDelta, ccdUp, spineTwistKeep, spineSwingScale);
                    // Relax x weight: a gentler chest pull lets the head-restore keep pace, which is exactly
                    // why the moderate weight preserves the head where a full pull loosened it.
                    cDelta = Quaternion.Slerp(Quaternion.identity, cDelta, ccdRelax * k_ChestIkWeight);
                    ChainHeadToSpine[lastJoint].SetRotation(stream, cDelta * ChainHeadToSpine[lastJoint].GetRotation(stream));
                    GuardSpineJoint(stream, lastJoint);
                }

                // 2) restore the head with the UPPER joints only (chest and above -- never the Spine, which
                // now owns the chest). They have far more DOF than the head needs, so the head returns to
                // target without disturbing the chest the Spine just placed.
                for (int sweep = 0; sweep < k_ChestIkHeadRestoreSweeps; sweep++)
                {
                    for (int i = lastJoint - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }
            }
        }
        public void GuardSpineJoint(BasisPoseStream stream, int i)
        {
            if (!spineAnatomicalRom)
            {
                return;
            }
            if (!ChainSpineRestFrames.IsCreated || i < 0 || i >= ChainSpineRestFrames.Length)
            {
                return;
            }

            BasisSpineRestFrame frame = ChainSpineRestFrames[i];
            if (!frame.Valid)
            {
                return;   // the head and the hips: commanded, not solved. Never guarded.
            }

            int parent = i + 1;
            if (parent >= ChainHeadToSpine.Length || !ChainHeadToSpine[parent].IsValid(stream) || !ChainHeadToSpine[i].IsValid(stream))
            {
                return;
            }

            Quaternion parentRot = ChainHeadToSpine[parent].GetRotation(stream);
            Quaternion boneRot = ChainHeadToSpine[i].GetRotation(stream);
            Quaternion local = BasisSpineAnatomyCore.Conj(parentRot) * boneRot;

            Quaternion clamped = BasisSpineAnatomyCore.Clamp(local, frame, ChainSpineRoms[i], out BasisSpineClampInfo info);
            if (!info.Touched)
            {
                return;   // legal pose: the bone is not written at all, so it cannot be perturbed.
            }

            ChainHeadToSpine[i].SetRotation(stream, parentRot * clamped);
        }
        // A full sweep of the envelope over every solved vertebra. Run right after DistributeSpineBend so
        // the CCD starts from a legal spine -- the CCD breaks out early when the head is already on target,
        // and on those frames it would otherwise never look at the pre-bend's output at all.
        public void GuardSpineChain(BasisPoseStream stream)
        {
            if (!ChainHeadToSpine.IsCreated || ChainHeadToSpine.Length < 3)
            {
                return;
            }
            for (int i = 1; i <= ChainHeadToSpine.Length - 2; i++)
            {
                GuardSpineJoint(stream, i);
            }
        }
        // Constrains the neck (chain index neckIdx) to within maxConeDeg of the chest→neck
        // direction. Enforced in-loop so chest/spine take the slack on the next CCD sweep.
        public void ClampNeckCone(BasisPoseStream stream, int neckIdx, float maxConeDeg)
        {
            Vector3 chestPos = ChainHeadToSpine[neckIdx + 1].GetPosition(stream);
            Vector3 neckPos = ChainHeadToSpine[neckIdx].GetPosition(stream);
            Vector3 headPos = ChainHeadToSpine[0].GetPosition(stream);

            Vector3 parentDir = neckPos - chestPos;
            Vector3 boneDir = headPos - neckPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
            {
                return;
            }

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            ChainHeadToSpine[neckIdx].SetRotation(stream, correction * ChainHeadToSpine[neckIdx].GetRotation(stream));
        }
        public void ClampChestCone(BasisPoseStream stream, int chestIdx, float maxConeDeg)
        {
            Vector3 spinePos = ChainHeadToSpine[chestIdx + 1].GetPosition(stream);
            Vector3 chestPos = ChainHeadToSpine[chestIdx].GetPosition(stream);
            Vector3 childPos = ChainHeadToSpine[chestIdx - 1].GetPosition(stream);

            Vector3 parentDir = chestPos - spinePos;
            Vector3 boneDir = childPos - chestPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
                return;

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
                return;

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
                return;

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            ChainHeadToSpine[chestIdx].SetRotation(stream, correction * ChainHeadToSpine[chestIdx].GetRotation(stream));
        }
        public void BiasSpineTowardChest(BasisPoseStream stream)
        {
            if (!HandleSpine.IsValid(stream) || !HandleChest.IsValid(stream))
                return;

            Vector3 chestTargetPos = TargetChestPosition;
            Vector3 spinePos = HandleSpine.GetPosition(stream);
            Vector3 chestPos = HandleChest.GetPosition(stream);

            if ((chestTargetPos - chestPos).sqrMagnitude > k_ChestPullMaxDistSqr)
                return;

            Vector3 cur = chestPos - spinePos;
            Vector3 tgt = chestTargetPos - spinePos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion pull = ClampRotation(BasisQuaternionExt.FromToRotation(cur, tgt), Quaternion.identity, k_ChestPosPullMaxDeg);
            HandleSpine.SetRotation(stream, pull * HandleSpine.GetRotation(stream));
        }
        public Vector3 ComputeNeckCue(Vector3 headTargetPos)
        {
            return headTargetPos + (targetRotationHead * targetOffsetHead) * TposeHeadToNeckLocal;
        }
        public Vector3 ApplyTrunkCounterbalance(Vector3 neckCue, Vector3 hipsPos, Vector3 playerUp, out float flexionFrac)
        {
            BasisTrunkCounterbalanceInput input;
            input.HipsPos = hipsPos;
            input.NeckCue = neckCue;
            input.PlayerUp = playerUp;
            input.Gain = trunkCounterbalance;
            input.MaxShift = k_TrunkCounterbalanceMaxSpineFrac * MinHeadSpineHeight;
            BasisTrunkCounterbalanceCore.Solve(input, out BasisTrunkCounterbalanceResult result);
            flexionFrac = result.FlexionFrac;
            return result.HipsPos;
        }
        public void DistributeSpineBend(BasisPoseStream stream, Vector3 headTargetPos)
        {
            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            bool hasSpine = HandleSpine.IsValid(stream);
            bool hasUpper = HandleUpperChest.IsValid(stream);
            if (!hasSpine && !hasUpper)
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Vector3 neckCue = ComputeNeckCue(headTargetPos);
            Vector3 spineCue = Vector3.Lerp(neckCue, headTargetPos, Mathf.Clamp01(spineGazeFollow));

            Quaternion hipsBind = offsetRotationHips;

            BasisSpineBendInput input;
            input.HipsRot = hipsRot;
            input.HipsPos = HandleHips.GetPosition(stream);
            input.ChestPos = HandleChest.GetPosition(stream);
            input.SmoothedHead = ApplyChestSpring(stream, spineCue);
            input.HipsBind = hipsBind;
            input.HeadTargetRot = targetRotationHead;
            input.SpineMaxForwardDeg = spineMaxForwardDeg;
            input.SpineMaxBackwardDeg = spineMaxBackwardDeg;
            input.SpineMaxLateralDeg = spineMaxLateralDeg;
            input.SpineBendPitch = spineBendPitch;
            input.SpineBendYaw = spineBendYaw;
            input.SpineBendRoll = spineBendRoll;
            input.UpperBendPitch = upperChestBendPitch;
            input.UpperBendYaw = upperChestBendYaw;
            input.UpperBendRoll = upperChestBendRoll;
            input.AnatDifferentialStiffness = anatDifferentialStiffness;
            input.AnatPelvicTwistRouting = anatPelvicTwistRouting;
            input.SquishBoost = spineSquishBoost;
            input.RestLen = TposeLengthNeckToHips.magnitude;   // the spine spans hips->NECK; the head was never part of it
            input.BendTwistCoupling = k_BendTwistCoupling;
            input.HasSpine = hasSpine;
            input.HasUpper = hasUpper;
            if (HasChestTracker)
            {
                input.SpineBendPitch = 0f;
                input.SpineBendRoll = 0f;
                input.UpperBendPitch = 0f;
                input.UpperBendRoll = 0f;
            }

            BasisSpineBendCore.Solve(input, out BasisSpineBendResult r);
            if (r.EarlyOut)
            {
                return;
            }
            Quaternion hipsAnat = hipsRot * Quaternion.Inverse(hipsBind);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            if (r.WriteSpine)
            {
                Quaternion deltaWorld = hipsAnat * Quaternion.Euler(r.SpineEuler) * invHipsAnat;
                HandleSpine.SetRotation(stream, deltaWorld * HandleSpine.GetRotation(stream));
            }
            if (r.WriteUpper)
            {
                Quaternion deltaWorld = hipsAnat * Quaternion.Euler(r.UpperEuler) * invHipsAnat;
                HandleUpperChest.SetRotation(stream, deltaWorld * HandleUpperChest.GetRotation(stream));
            }
        }
        Vector3 ApplyChestSpring(BasisPoseStream stream, Vector3 headTargetPos)
        {
            if (!chestSpringState.IsCreated || !chestSpringInit.IsCreated)
            {
                return headTargetPos;
            }

            float hz = chestSpringHz;
            if (hz <= 0f)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }
            if (chestSpringInit[0] == 0)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }

            float dt = stream.deltaTime;
            if (dt <= 0f)
                return chestSpringState[0];

            BasisChestSpringCore.Step(chestSpringState[0], chestSpringState[1], headTargetPos, dt, hz,
                chestSpringDamping, out Vector3 newPos, out Vector3 newVel);

            // Defensive: if upstream input has produced a NaN, re-seed instead of poisoning the rig.
            if (!IsFinite(newPos) || !IsFinite(newVel))
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                return headTargetPos;
            }

            chestSpringState[0] = newPos;
            chestSpringState[1] = newVel;
            return newPos;
        }
        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        Quaternion ApplyHipHinge(BasisPoseStream stream, Vector3 headPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUp)
        {
            BasisHipHingeInput input;
            input.HeadPos = headPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.PlayerUp = playerUp;
            input.StartDeg = hipHingeStartDeg;
            input.MaxAddDeg = hipHingeMaxAddDeg;
            BasisHipHingeCore.Solve(input, out BasisHipHingeResult result);
            return result.HipsRot;
        }
        Vector3 ApplyCrouchBodyOffset(BasisPoseStream stream, Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir, float fade)
        {
            if (HasChestTracker || hasHipsTracker)
            {
                return hipsPos;
            }

            BasisCrouchOffsetInput input;
            input.HeadTargetPos = headTargetPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.Bind = offsetRotationHips;
            input.PlayerUp = playerUpDir;
            input.Factor = moveBodyBackWhenCrouching;
            input.RestDist = MinHeadSpineHeight;
            input.CrouchDepth = crouchDepth;
            input.StandingHeadHeight = standingHeadHeight;
            input.Fade = fade;
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        public void ApplyCervicalLordosis(BasisPoseStream stream)
        {
            if (!HandleNeck.IsValid(stream))
            {
                return;
            }

            Vector3 referenceUp;
            if (HandleChest.IsValid(stream))
            {
                Vector3 chestToNeck = HandleNeck.GetPosition(stream) - HandleChest.GetPosition(stream);
                referenceUp = chestToNeck.sqrMagnitude > k_SqrEpsilon
                    ? chestToNeck.normalized
                    : HandleChest.GetRotation(stream) * Vector3.up;
            }
            else
            {
                Vector3 up = playerUp;
                referenceUp = up.sqrMagnitude < k_SqrEpsilon ? Vector3.up : up.normalized;
            }

            BasisCervicalInput input;
            input.BaseDeg = lordosisBaseDeg;
            input.NeckShare = Mathf.Clamp01(lordosisNeckShare);
            input.MaxHeadPitchDeg = lordosisMaxHeadPitchDeg;
            input.ExtremeStartDeg = lordosisExtremeStartDeg;
            input.ExtremeFullDeg = lordosisExtremeFullDeg;
            input.ExtremeRollForwardMaxDeg = lordosisExtremeRollForwardMaxDeg;
            input.ExtremeRollBackwardMaxDeg = lordosisExtremeRollBackwardMaxDeg;
            input.ExtremeHipsHorizontalMax = lordosisExtremeHipsHorizontalMax;
            input.ExtremeChestHorizontalMax = lordosisExtremeChestHorizontalMax;
            input.ExtremeHipsDownMax = lordosisExtremeHipsDownMax;
            input.ExtremeChestDownMax = lordosisExtremeChestDownMax;
            input.ExtremeHipsDownLookUp = lordosisExtremeHipsDownLookUp;
            input.ExtremeChestDownLookUp = lordosisExtremeChestDownLookUp;
            input.PitchGainDeg = Mathf.Max(0f, lordosisPitchGainDeg);
            input.ReferenceUp = referenceUp;
            input.HeadTargetRot = targetRotationHead;
            input.HasUpperChest = HandleUpperChest.IsValid(stream);

            BasisCervicalSolveCore.Solve(input, out BasisCervicalResult result);
            if (result.EarlyOut)
            {
                if (HandleHead.IsValid(stream))
                {
                    HandleHead.SetPosition(stream, targetPositionHead);
                    HandleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
                }
                return;
            }

            Vector3 shoulderRight = (HandleLeftUpperArm.IsValid(stream) && HandleRightUpperArm.IsValid(stream))
                ? HandleRightUpperArm.GetPosition(stream) - HandleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            bool hasShoulderRight = shoulderRight.sqrMagnitude > k_SqrEpsilon;
            if (hasShoulderRight)
            {
                shoulderRight.Normalize();
            }

            BasisBoneHandle bendHandle = input.HasUpperChest ? HandleUpperChest : HandleChest;
            if (bendHandle.IsValid(stream) && result.BhDeg != 0f)
            {
                Quaternion bhRot = bendHandle.GetRotation(stream);
                Vector3 bhAxis = hasShoulderRight ? shoulderRight : bhRot * Vector3.right;
                bendHandle.SetRotation(stream, Quaternion.AngleAxis(result.BhDeg, bhAxis) * bhRot);
            }

            if (result.HasExtreme)
            {
                Quaternion refRot = HandleHips.IsValid(stream)
                    ? HandleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips)
                    : (HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity);
                Vector3 refForward = refRot * Vector3.forward;
                Vector3 refDown = -(refRot * Vector3.up);

                if (HandleHips.IsValid(stream))
                {
                    Vector3 hipsOffset = refForward * result.HipsForwardAmount + refDown * result.HipsDownAmount;
                    HandleHips.SetPosition(stream, HandleHips.GetPosition(stream) + hipsOffset);
                }

                if (HandleChest.IsValid(stream))
                {
                    Vector3 chestOffset = refForward * result.ChestForwardAmount + refDown * result.ChestDownAmount;
                    HandleChest.SetPosition(stream, HandleChest.GetPosition(stream) + chestOffset);
                }
            }
            float extraNeckDeg = Mathf.Clamp01(neckGazeFollow) * k_NeckGazeFollowMaxDeg * result.LookDownFrac;
            float totalNeckDeg = result.NeckDeg + extraNeckDeg;
            if (totalNeckDeg != 0f)
            {
                Quaternion neckRotCurrent = HandleNeck.GetRotation(stream);
                Vector3 neckAxis = hasShoulderRight ? shoulderRight : neckRotCurrent * Vector3.right;
                HandleNeck.SetRotation(stream, Quaternion.AngleAxis(totalNeckDeg, neckAxis) * neckRotCurrent);
            }

            if (HandleHead.IsValid(stream))
            {
                HandleHead.SetPosition(stream, targetPositionHead);
                HandleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
            }
        }
        static void BakeHumerusTwistBind(Transform upperArm, Transform lowerArm, out Quaternion bindRot, out Vector3 bindDir, out Vector3 refAxis)
        {
            bindRot = Quaternion.identity;
            bindDir = Vector3.zero;
            refAxis = Vector3.zero;
            if (upperArm == null || lowerArm == null)
            {
                return;
            }

            Vector3 dir = lowerArm.position - upperArm.position;
            if (dir.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            bindRot = upperArm.rotation;
            bindDir = dir.normalized;

            Vector3 localBone = Quaternion.Inverse(bindRot) * bindDir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            refAxis = perp.sqrMagnitude > k_SqrEpsilon ? perp.normalized : Vector3.zero;
        }
        public static Vector3 ClampHipsAroundHead(Vector3 headPos, Vector3 hipsPos, float restDistance, float minFactor, float maxFactor, Vector3 playerUp)
        {
            Vector3 headToHips = hipsPos - headPos;
            float dist = headToHips.magnitude;
            float minD = restDistance * minFactor;
            float maxD = restDistance * maxFactor;
            if (dist < k_Epsilon)
            {
                return headPos - minD * playerUp; // degenerate: place the hips straight below the head
            }

            Vector3 dir = headToHips / dist;
            float upDot = Vector3.Dot(dir, playerUp);
            if (upDot > 0f)
            {
                Vector3 horiz = dir - playerUp * upDot;
                dir = horiz.sqrMagnitude > k_SqrEpsilon ? horiz.normalized : -playerUp;
            }

            return headPos + dir * Mathf.Clamp(dist, minD, maxD);
        }
        public static Vector3 EnforceSpineBendLimit(Vector3 headPos, Vector3 hipsPos, float maxBendDeg, Vector3 playerUp)
        {
            if (maxBendDeg <= 0f)
            {
                return hipsPos;
            }

            Vector3 diff = hipsPos - headPos;
            if (diff.sqrMagnitude < k_MinMag)
            {
                return hipsPos;
            }

            Vector3 up = playerUp;

            // Decompose head→hips into a downward drop (along -up) and a horizontal lean.
            float down = Vector3.Dot(diff, -up);  // signed: hips are below the head when > 0
            Vector3 lateral = diff + up * down;   // diff minus the (-up * down) vertical part
            float lateralLen = lateral.magnitude;
            float coneTan = Mathf.Tan(Mathf.Min(maxBendDeg, 89.9f) * Mathf.Deg2Rad);
            float minDown = lateralLen / Mathf.Max(coneTan, k_MinMag);
            if (down >= minDown)
            {
                return hipsPos;
            }

            return headPos - up * minDown + lateral;
        }
        public static Vector3 AntiContortionist(Vector3 headPos, Quaternion headRot, Vector3 hipsPos, Quaternion hipsRot, float restDistance)
        {
            Vector3 headFwd = headRot * Vector3.forward;
            Vector3 hipsFwd = hipsRot * Vector3.forward;
            float facingSimilarity = Vector3.Dot(headFwd, hipsFwd);

            float minDistFactor = Mathf.Lerp(0.2f, 0.85f, Mathf.Clamp01((facingSimilarity + 1f) * 0.5f));
            float minDist = restDistance * minDistFactor;

            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist < minDist && currentDist > k_Epsilon)
            {
                return headPos + diff * (minDist / currentDist);
            }
            return hipsPos;
        }
        public static Vector3 MitigateSpineBuckling(Vector3 headPos, Quaternion hipsRot, Vector3 hipsPos, float restDistance, Vector3 playerUp)
        {
            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist >= restDistance || currentDist < k_Epsilon)
                return hipsPos;

            Vector3 hipsUp = hipsRot * Vector3.up;
            Vector3 spineDir = (headPos - hipsPos).normalized;

            float tension = Mathf.Clamp01(Vector3.Dot(hipsUp, spineDir));
            float compression = 1f - (currentDist / restDistance);

            float pushAmount = compression * tension * restDistance * 0.5f;
            return hipsPos - playerUp * pushAmount;
        }
        public static Quaternion ClampRotation(Quaternion current, Quaternion reference, float maxAngleDeg)
        {
            // Angle between the two orientations
            float angle = Quaternion.Angle(reference, current);
            if (angle <= maxAngleDeg)
            {
                return current;
            }

            // Scale back toward the reference so the final difference is exactly maxAngleDeg
            float t = maxAngleDeg / Mathf.Max(angle, k_Epsilon);
            return Quaternion.Slerp(reference, current, t);
        }
        public void ApplyToeSurfaceBend(BasisPoseStream stream, BasisBoneHandle handle, float bendDeg, Vector3 axis)
        {
            if (!handle.IsValid(stream)) return;
            if (Mathf.Abs(bendDeg) < 0.01f || axis.sqrMagnitude < 1e-6f) return;

            Quaternion current = handle.GetRotation(stream);
            handle.SetRotation(stream, Quaternion.AngleAxis(-bendDeg, axis.normalized) * current);
        }
        public static void ApplyRotation(BasisPoseStream stream, bool enabledProp, BasisBoneHandle handle, Quaternion targetRotProp, Quaternion RotationOffset)
        {
            if (!handle.IsValid(stream))
            {
                return;
            }

            if (enabledProp)
            {
                handle.SetRotation(stream, targetRotProp * RotationOffset);
            }
        }
        public BasisSwivelFrame BuildArmFrame(BasisPoseStream stream)
        {
            if (!HandleLeftUpperArm.IsValid(stream) || !HandleRightUpperArm.IsValid(stream)
                || !HandleChest.IsValid(stream) || !HandleNeck.IsValid(stream))
            {
                return default;   // Valid = false; the caller leaves the arm on the solver's own fallback pole
            }

            return BasisSwivelHintCore.BuildFrame(
                HandleLeftUpperArm.GetPosition(stream), HandleRightUpperArm.GetPosition(stream),
                HandleChest.GetPosition(stream), HandleNeck.GetPosition(stream));
        }
        BasisSwivelFrame BuildLegFrame(BasisPoseStream stream)
        {
            if (!HandleLeftUpperLeg.IsValid(stream) || !HandleRightUpperLeg.IsValid(stream)
                || !HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return default;
            }

            return BasisSwivelHintCore.BuildFrame(
                HandleLeftUpperLeg.GetPosition(stream), HandleRightUpperLeg.GetPosition(stream),
                HandleHips.GetPosition(stream), HandleChest.GetPosition(stream));
        }
        public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float abSqr = Vector3.Dot(ab, ab);
            if (abSqr <= k_SqrEpsilon)
            {
                return a;
            }

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
            return a + ab * t;
        }
        public void ReGuardElbowAnatomy(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int swingSlot, Vector3 bodyRight)
        {
            if (!root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            Vector3 a = root.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);
            float totalLen = (b - a).magnitude + (c - b).magnitude;
            if (totalLen <= k_Epsilon)
            {
                return;
            }

            BasisSwivelFrame torsoFrame = BuildArmFrame(stream);
            Vector3 guardUp = torsoFrame.Valid ? torsoFrame.Up : playerUp;
            bool sideSlot = (uint)swingSlot < (uint)k_SwingCount && swingGuardSide.IsCreated;
            Vector3 lateralOut = swingSlot == k_SwingLeftElbow ? -bodyRight : bodyRight;
            int prevSide = sideSlot ? swingGuardSide[swingSlot] : 0;
            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(a, b, c, guardUp, totalLen,
                lateralOut, prevSide, out int sideUsed);
            if (sideSlot && sideUsed != 0)
            {
                swingGuardSide[swingSlot] = sideUsed;
            }
            if (guardSwivel == 0f)
            {
                return;
            }

            Vector3 ac = c - a;
            if (ac.sqrMagnitude <= k_SqrEpsilon)
            {
                return;
            }

            Quaternion guard = Quaternion.AngleAxis(guardSwivel * Mathf.Rad2Deg, ac.normalized);
            root.SetRotation(stream, guard * root.GetRotation(stream));
            mid.SetRotation(stream, guard * mid.GetRotation(stream));
        }
        public static void SwingElbowAroundAC(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 desiredB)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);

            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= k_SqrEpsilon) return;

            Vector3 n = AC / Mathf.Sqrt(acSqr);
            Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Sqr = Vector3.Dot(v1, v1);
            float v2Sqr = Vector3.Dot(v2, v2);
            if (v1Sqr <= k_SqrEpsilon || v2Sqr <= k_SqrEpsilon) return;

            v1 /= Mathf.Sqrt(v1Sqr);
            v2 /= Mathf.Sqrt(v2Sqr);

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot);
            Vector3 cross = Vector3.Cross(v1, v2);
            float dir = Mathf.Sign(Vector3.Dot(cross, n));
            Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

            root.SetRotation(stream, swing * root.GetRotation(stream));
        }
        public static Vector3 PushOutFromCapsule(Vector3 p, Vector3 a, Vector3 b, float radiusWithSkin, Vector3 playerUp)
        {
            Vector3 q = ClosestPointOnSegment(p, a, b);
            Vector3 qp = p - q;
            float dSqr = Vector3.Dot(qp, qp);
            if (dSqr >= radiusWithSkin * radiusWithSkin) return p;
            float d = Mathf.Sqrt(Mathf.Max(dSqr, k_SqrEpsilon));
            Vector3 n = (d > 0f) ? (qp / d) : playerUp;
            return q + n * radiusWithSkin;
        }
        public Quaternion SolveTwoBone(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, Vector3 hint, float hintWeight, Quaternion targetOffset, Vector3 BendNormal, float hintDistrust = 0f, int diagSlot = -1, Quaternion hintRotation = default, bool hintIsTracker = false, Vector3 anteriorNormal = default)
        {
            BasisLegSolveInput input = default;
            root.GetPositionAndRotation(stream, out Vector3 rootPos, out Quaternion rootRot);
            mid.GetPositionAndRotation(stream, out Vector3 midPos, out Quaternion midRot);
            input.Root = rootPos;
            input.Mid = midPos;
            input.Tip = tip.GetPosition(stream);
            input.RootRotation = rootRot;
            input.MidRotation = midRot;
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint;
            input.HintWeight = hintWeight;
            input.HintDistrust = hintDistrust;
            input.TargetOffset = targetOffset;
            input.BendNormal = BendNormal;
            // ANTERIOR stays body-frame even when BendNormal rides a lower-leg tracker: otherwise tibial
            // rotation spins the guard's reference and drags a legal knee into its compression band.
            input.AnteriorNormal = anteriorNormal;
            input.HintRotation = hintRotation;
            input.HintIsTracker = hintIsTracker;

            BasisLegSolveCore.Solve(input, out BasisLegSolveResult result);

            if (diagSlot >= 0 && legDiagnostics.IsCreated && diagSlot < legDiagnostics.Length)
            {
                BasisLegDiagnostics d = legDiagnostics[diagSlot];
                d.ReachRatio = result.ReachRatio;
                d.KneeAngleDeg = result.KneeAngleDeg;
                d.AxisSource = result.AxisSource;
                d.HintApplied = result.HintApplied ? 1f : 0f;
                d.HintDistrust = hintDistrust;
                d.ShinRollDeg = result.ShinRollDeg;
                legDiagnostics[diagSlot] = d;
            }

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
            return result.MidPostRoll;
        }
        public void SolveLegs(BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, Quaternion hintRotProp, float hintWeightProp, Quaternion targetOffset, Vector3 bendNormalProp, bool hintIsTrackerProp, bool footIsTrackerProp, int legSlot)
        {
            float posWeight = enabledProp;
            if (posWeight <= 0f)
            {
                return;
            }

            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                return;
            }
            Quaternion origRootRot = root.GetRotation(stream);
            Quaternion origMidRot = mid.GetRotation(stream);
            Quaternion origTipRot = tip.GetRotation(stream);
            Quaternion tRot = targetRotProp;
            float tRotSqrLen = tRot.x * tRot.x + tRot.y * tRot.y + tRot.z * tRot.z + tRot.w * tRot.w;
            bool preserveTip = !(tRotSqrLen > 0.5f);
            if (preserveTip) tRot = origTipRot;
            float hintW = hintWeightProp;

            BasisAffineTransform target = new BasisAffineTransform(targetPosProp, tRot);
            Vector3 hint = hintPosProp;
            Vector3 bendNormal = bendNormalProp;

            float hintDistrust = 0f;
            bool usedModelHint = false;
            bool fabricatedLeg = !hintIsTrackerProp && !footIsTrackerProp;
            if (!(hintW > 0f) || fabricatedLeg)
            {
                BasisSwivelFrame frame = BuildLegFrame(stream);

                Vector3 hipPos = root.GetPosition(stream);
                float upperLen = (mid.GetPosition(stream) - hipPos).magnitude;
                float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
                float legLen = upperLen + lowerLen;
                bool isLeft = legSlot == 0;

                // The confidence is used as POLE distrust, never as a fade of hintW -- hintW is discontinuous
                // at zero, and that jump is the pop the earlier weight-fade attempt measured (70 -> 65) and
                // wrongly blamed on the idea rather than the mechanism. See BasisSwivelHintCore.LegModelTrust.
                if (BasisSwivelHintCore.LegHint(frame, hipPos, target.translation, legLen, isLeft, out Vector3 modelHint, out float conf))
                {
                    hint = modelHint;
                    hintW = 1f;
                    usedModelHint = true;
                    if (legDiagnostics.IsCreated && legSlot < legDiagnostics.Length)
                    {
                        BasisLegDiagnostics d = legDiagnostics[legSlot];
                        d.ModelHintUsed = 1f;
                        d.ModelConfidence = conf;
                        legDiagnostics[legSlot] = d;
                    }
                    hintDistrust = 1f - BasisSwivelHintCore.LegModelTrust(conf);
                }
            }
            Quaternion shinRoll = SolveTwoBone(stream, root, mid, tip, target, hint, hintW, targetOffset, bendNormal, hintDistrust, legSlot,
                                               hintIsTrackerProp ? hintRotProp : default, hintIsTrackerProp, KneeAnteriorRef);
            // Rotation-only fade: the solve produces rotations, so blending positions here would
            // translate bones off the FK chain (dislocated foot) mid-fade.
            if (posWeight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), posWeight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), posWeight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), posWeight));
            }
            // Position-only foot: keep the animation rotation, but CARRIED BY THE SHIN ROLL. A shin tracker with
            // no foot tracker still rolls the shin, and a real foot rides its shin -- restoring the raw animation
            // rotation would leave the ankle counter-twisted by exactly the roll, which is the artifact this
            // whole change exists to remove, just with the sign flipped.
            if (preserveTip)
            {
                Quaternion carriedTip = shinRoll * origTipRot;
                tip.SetRotation(stream, posWeight < 1f ? Quaternion.Slerp(origTipRot, carriedTip, posWeight) : carriedTip);
            }

            RecordHipDiagnostics(stream, root, mid, legSlot);
            if (legSwivelSmoothing)
            {
                if (hintIsTrackerProp || footIsTrackerProp)
                {
                    bool footDerivedPole = !hintIsTrackerProp && footIsTrackerProp && !usedModelHint;
                    SmoothKneeSwivel(stream, root, mid, tip, legSlot, stream.deltaTime,
                        k_TrackedKneeSwivelMinCutoffHz, k_TrackedKneeSwivelBeta, k_TrackedKneeSwivelDerivCutoffHz,
                        conditionOnPole: !hintIsTrackerProp && !footDerivedPole,
                        holdWhenSingular: !footDerivedPole);
                }
                else
                {
                    SmoothKneeSwivel(stream, root, mid, tip, legSlot, stream.deltaTime,
                        BasisSwivelFilterCore.MinCutoffHz, BasisSwivelFilterCore.Beta, BasisSwivelFilterCore.DerivCutoffHz,
                        conditionOnPole: true, holdWhenSingular: true);
                }
            }
        }
        void RecordHipDiagnostics(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, int slot)
        {
            if (!legDiagnostics.IsCreated || slot < 0 || slot >= legDiagnostics.Length || !HandleHips.IsValid(stream))
            {
                return;
            }

            Vector3 femur = mid.GetPosition(stream) - root.GetPosition(stream);
            if (!(femur.sqrMagnitude > 1e-8f))
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Quaternion hipsInv = Quaternion.Inverse(hipsRot);
            Vector3 femurLocal = (hipsInv * femur).normalized;

            BasisLegDiagnostics d = legDiagnostics[slot];
            // Pelvis frame: -Y is straight down the leg, +Z forward, +X the player's right.
            d.HipFlexionDeg = Mathf.Atan2(femurLocal.z, -femurLocal.y) * Mathf.Rad2Deg;
            d.HipAbductionDeg = Mathf.Atan2(femurLocal.x, -femurLocal.y) * Mathf.Rad2Deg;
            d.FemurTwistDeg = TwistDeg(hipsInv * root.GetRotation(stream), femurLocal);
            legDiagnostics[slot] = d;
        }
        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > 1e-8f))
            {
                return 0f;
            }

            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(BasisPoseStream stream, BasisBoneHandle h, Vector3 p, Quaternion r, Quaternion o, bool sw)
        {
            if (h.IsValid(stream))
            {
                if (sw)
                {

                    Vector3 targetPos = p;
                    Quaternion targetRot = r;
                    Quaternion offsetRot = o;
                    Quaternion finalRot = targetRot * offsetRot;

                    h.SetPosition(stream, targetPos);
                    h.SetRotation(stream, finalRot);
                }
            }
        }
        void SmoothKneeSwivel(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int slot, float dt, float minCutoffHz, float beta, float derivCutoffHz, bool conditionOnPole, bool holdWhenSingular)
        {
            if (!legSwivelInit.IsCreated || slot < 0 || slot >= legSwivelInit.Length || !HandleHips.IsValid(stream))
            {
                return;
            }
            BasisSwivelSmootherInput input = default;
            input.Root = root.GetPosition(stream);
            input.Mid = mid.GetPosition(stream);
            input.Tip = tip.GetPosition(stream);
            input.BodyRotation = HandleHips.GetRotation(stream);
            // A standing leg hangs along the AC axis, so Vector3.down (the arm's ref) is colinear and
            // degenerate here. Reference off body forward (the knee bulges forward); body right as the fallback.
            input.ReferenceLocal = Vector3.forward;
            input.FallbackLocal = Vector3.right;
            input.TransportHomeLocal = Vector3.down;
            input.Dt = dt;
            input.MinCutoffHz = minCutoffHz;
            input.Beta = beta;
            input.DerivCutoffHz = derivCutoffHz;
            input.ConditionOnPole = conditionOnPole;
            input.SingularMinCutoffHz = BasisSwivelFilterCore.MinCutoffHz;
            input.GuardAnteriorHalfSpace = true;
            input.AnteriorSoftDeg = BasisLegSolveCore.KneeAnteriorSoftDeg;
            input.AnteriorHardDeg = BasisLegSolveCore.KneeAnteriorHardDeg;
            input.HoldWhenSingular = holdWhenSingular;
            input.HoldCondLo = BasisSwivelSmootherCore.DefaultHoldCondLo;
            input.HoldCondHi = BasisSwivelSmootherCore.DefaultHoldCondHi;
            input.State = new BasisSwivelFilterState { Raw = legSwivelRaw[slot].x, Vel = legSwivelRaw[slot].y, Smooth = legSwivelSmooth[slot].x };
            input.Seeded = legSwivelInit[slot] != 0;

            BasisSwivelSmootherCore.Solve(input, out BasisSwivelSmootherResult result);
            if (legDiagnostics.IsCreated && slot < legDiagnostics.Length)
            {
                BasisLegDiagnostics d = legDiagnostics[slot];
                d.RawSwivelDeg = result.RawSwivelDeg;
                d.SmoothSwivelDeg = result.SmoothSwivelDeg;
                d.Conditioning = result.Conditioning;
                d.HoldGate = result.HoldGate;
                d.AnteriorGuardApplied = result.AnteriorGuardApplied ? 1f : 0f;
                d.Seeded = result.Seeded ? 1f : 0f;
                legDiagnostics[slot] = d;
            }
            if (result.WriteState)
            {
                legSwivelRaw[slot] = new Vector3(result.State.Raw, result.State.Vel, 0f);
                legSwivelSmooth[slot] = new Vector3(result.State.Smooth, 0f, 0f);
                legSwivelInit[slot] = 1;
            }
            if (!result.Valid)
            {
                return;
            }

            Vector3 preFoot = input.Tip;
            Quaternion preFootRot = tip.GetRotation(stream);
            SwingElbowAroundAC(stream, root, mid, tip, result.DesiredMid);
            tip.SetPosition(stream, preFoot);
            tip.SetRotation(stream, preFootRot);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Slot(int humanBodyBone)
        {
            if (humanBodyBone >= 0 && humanBodyBone <= (int)HumanBodyBones.RightToes)
            {
                return humanBodyBone;
            }
            return humanBodyBone == (int)HumanBodyBones.UpperChest ? UpperChestSlot : -1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetPosition(int idx, in Vector3 v)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotPositions.Length)
            {
                slotPositions[s] = v;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotRotations.Length)
            {
                slotRotations[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOffsetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotOffsets.Length)
            {
                slotOffsets[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWeight(int idx, bool State)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotWeights.Length)
            {
                slotWeights[s] = State;
            }
        }
        public void SetDefaultValues()
        {
            HasChestTracker = true;
            hintWeightLeftLowerLeg = hintWeightRightLowerLeg = 1f;
            enabledSpineIK = true;
            hasHipsTracker = false;
            footIsTrackerLeftLeg = footIsTrackerRightLeg = false;
            enabledLeftLowerLeg = enabledRightLowerLeg = 1f;
            hintIsTrackerLeftLowerLeg = hintIsTrackerRightLowerLeg = false;
            ikLockMode = (float)BasisIKLockMode.LockHead;

            hintWeightLeftHand = hintWeightRightHand = true;
            enabledLeftHand = enabledRightHand = 1f;
            offsetRotationHead = offsetRotationLeftFoot = offsetRotationRightFoot = Quaternion.identity;
            offsetRotationLeftHand = offsetRotationRightHand = Quaternion.identity;

            playerUp = Vector3.up;

            targetPositionHips = Vector3.zero;
            targetRotationHips = Quaternion.identity;
            offsetRotationHips = Quaternion.identity;

            // Integrated driven TR defaults

            leftDrivenTargetRot = rightDrivenTargetRot = Quaternion.identity;
            leftToeEnabled = false;
            RightToeEnabled = false;

            // Chest/hand capsule defaults — read from persisted settings
            chestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue;
            collisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue;
            collisionsEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionsEnabled.RawValue;
            handRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue;
            handSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue;
            protectElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKProtectElbow.RawValue;
            elbowDragEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDrag.RawValue;
            elbowDragHz = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDragHz.RawValue;
            collideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;
            wristAxialBound = Basis.BasisUI.BasisSettingsDefaults.FBIKWristAxialBound.RawValue;

            shoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            shoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
            shoulderRetractionEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderRetraction.RawValue;
            shoulderRhythmEnabled = false;
            shoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            shoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;

            spineBendPitch = 0.45f;
            spineBendYaw = 0.10f;
            spineBendRoll = 0.35f;
            upperChestBendPitch = 0.25f;
            upperChestBendYaw = 0.30f;
            upperChestBendRoll = 0.20f;
            hipHingeStartDeg = 40f;
            hipHingeMaxAddDeg = 52f;
            chestSpringHz = 12f;
            chestSpringDamping = 1f;
            spineMaxForwardDeg = 60f;
            spineMaxBackwardDeg = 25f;
            spineMaxLateralDeg = 25f;
            spineSquishBoost = 0.5f;
            spineGazeFollow = 0.25f;
            neckGazeFollow = 0.3f;
            moveBodyBackWhenCrouching = 1f;
            crouchDepth = 0f;
            standingHeadHeight = 0f;
            trunkCounterbalance = BasisTrunkCounterbalanceCore.DerivedGain;
            swingSmoothRateDeg = 720f;
            chestArmSwingFactor = 0.3f;
            chestArmSwingMaxDeg = 15f;
            lowerArmTwistFraction = 0.5f;
            upperArmTwistFraction = 0.3f;

            anatDifferentialStiffness = true;
            anatShoulderSlide = true;
            anatCervicalLordosis = true;
            anatPelvicTwistRouting = true;
            spineAnatomicalRom = true;
            chestIkTarget = true;
            legSwivelSmoothing = true;
            lordosisPitchGainDeg = 8f;
            lordosisBaseDeg = 5f;
            lordosisNeckShare = 0.65f;
            lordosisMaxHeadPitchDeg = 80f;
            lordosisExtremeStartDeg = 50f;
            lordosisExtremeFullDeg = 80f;
            lordosisExtremeRollForwardMaxDeg = 10f;
            lordosisExtremeRollBackwardMaxDeg = 4f;
            lordosisExtremeHipsHorizontalMax = 0.025f;
            lordosisExtremeChestHorizontalMax = 0.04f;
            lordosisExtremeHipsDownMax = 0.015f;
            lordosisExtremeChestDownMax = 0.025f;
            lordosisExtremeHipsDownLookUp = 0.0005f;
            lordosisExtremeChestDownLookUp = 0.001f;
            spineCCDRelax = 1.0f;
            neckMaxConeDeg = 45f;
            spineTwistKeep = 0.25f;
            spineNeckTwistKeep = 0.9f;

            // Slots: identity rotations, zero positions, weights disabled.
            slotPositions.Length = Count;
            slotRotations.Length = Count;
            slotOffsets.Length = Count;
            slotWeights.Length = Count;
            for (int i = 0; i < Count; i++)
            {
                slotPositions[i] = Vector3.zero;
                slotRotations[i] = Quaternion.identity;
                slotOffsets[i] = Quaternion.identity;
                slotWeights[i] = false;
            }
        }

        public void Create(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            HandleHips = BindHandle(skeleton, Mapping.Hips);
            HandleChest = BindHandle(skeleton, Mapping.chest);
            HandleNeck = BindHandle(skeleton, Mapping.neck);
            HandleHead = BindHandle(skeleton, Mapping.head);
            HandleLeftUpperLeg = BindHandle(skeleton, Mapping.LeftUpperLeg);
            HandleLeftLowerLeg = BindHandle(skeleton, Mapping.LeftLowerLeg);
            HandleLeftFoot = BindHandle(skeleton, Mapping.leftFoot);
            HandleRightUpperLeg = BindHandle(skeleton, Mapping.RightUpperLeg);
            HandleRightLowerLeg = BindHandle(skeleton, Mapping.RightLowerLeg);
            HandleRightFoot = BindHandle(skeleton, Mapping.rightFoot);
            HandleLeftToe = BindHandle(skeleton, Mapping.leftToe);
            HandleRightToe = BindHandle(skeleton, Mapping.rightToe);
            HandleLeftUpperArm = BindHandle(skeleton, Mapping.leftUpperArm);
            HandleLeftLowerArm = BindHandle(skeleton, Mapping.leftLowerArm);
            HandleLeftHand = BindHandle(skeleton, Mapping.leftHand);
            HandleRightUpperArm = BindHandle(skeleton, Mapping.RightUpperArm);
            HandleRightLowerArm = BindHandle(skeleton, Mapping.RightLowerArm);
            HandleRightHand = BindHandle(skeleton, Mapping.rightHand);
            HandleLeftUpperArmTwist = BindHandle(skeleton, Mapping.leftUpperArmTwist);
            HandleLeftLowerArmTwist = BindHandle(skeleton, Mapping.leftLowerArmTwist);
            HandleRightUpperArmTwist = BindHandle(skeleton, Mapping.RightUpperArmTwist);
            HandleRightLowerArmTwist = BindHandle(skeleton, Mapping.RightLowerArmTwist);
            HandleSpine = BindHandle(skeleton, Mapping.spine);
            HandleUpperChest = BindHandle(skeleton, Mapping.Upperchest);
            HandleLeftShoulder = BindHandle(skeleton, Mapping.leftShoulder);
            HandleRightShoulder = BindHandle(skeleton, Mapping.RightShoulder);

            // Baked T-pose data for shoulder solve
            TposeLeftShoulderRot = Mapping.leftShoulder != null ? Mapping.leftShoulder.rotation : Quaternion.identity;
            TposeRightShoulderRot = Mapping.RightShoulder != null ? Mapping.RightShoulder.rotation : Quaternion.identity;
            BakeHumerusTwistBind(Mapping.leftUpperArm, Mapping.leftLowerArm,
                out TposeLeftUpperArmRot, out TposeLeftHumerusDir, out TposeLeftHumerusRefAxis);
            BakeHumerusTwistBind(Mapping.RightUpperArm, Mapping.RightLowerArm,
                out TposeRightUpperArmRot, out TposeRightHumerusDir, out TposeRightHumerusRefAxis);

            TposeLeftHandRot = Mapping.leftHand != null ? Mapping.leftHand.rotation : default;
            TposeRightHandRot = Mapping.rightHand != null ? Mapping.rightHand.rotation : default;
            TposeLeftLowerArmRot = Mapping.leftLowerArm != null ? Mapping.leftLowerArm.rotation : Quaternion.identity;
            TposeRightLowerArmRot = Mapping.RightLowerArm != null ? Mapping.RightLowerArm.rotation : Quaternion.identity;

            TposeChestRot = Mapping.Upperchest != null ? Mapping.Upperchest.rotation
                          : Mapping.chest != null ? Mapping.chest.rotation
                          : Quaternion.identity;
            TposeChestBind = (Mapping.HasAnimatorRoot && Mapping.AnimatorRoot != null
                ? Quaternion.Inverse(Mapping.AnimatorRoot.rotation)
                : Quaternion.identity) * TposeChestRot;
            TposeLeftShoulderLocalDir = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? (Mapping.leftUpperArm.position - Mapping.leftShoulder.position).normalized : Vector3.left;
            TposeRightShoulderLocalDir = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? (Mapping.RightUpperArm.position - Mapping.RightShoulder.position).normalized : Vector3.right;

            float fallbackArmLength = 0.6f * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            TposeShoulderToHandLeft = (Mapping.leftShoulder != null && Mapping.leftHand != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftHand.position) : fallbackArmLength;
            TposeShoulderToHandRight = (Mapping.RightShoulder != null && Mapping.rightHand != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.rightHand.position) : fallbackArmLength;
            TposeClavicleLenLeft = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftUpperArm.position) : 0f;
            TposeClavicleLenRight = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightUpperArm.position) : 0f;
            TposeShoulderToElbowLeft = (Mapping.leftShoulder != null && Mapping.leftLowerArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftLowerArm.position) : 0f;
            TposeShoulderToElbowRight = (Mapping.RightShoulder != null && Mapping.RightLowerArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightLowerArm.position) : 0f;

            slotHandles.Length = Count;
            slotHandles[0] = HandleHips;
            slotHandles[1] = HandleLeftUpperLeg;
            slotHandles[2] = HandleRightUpperLeg;
            slotHandles[3] = HandleLeftLowerLeg;
            slotHandles[4] = HandleRightLowerLeg;
            slotHandles[5] = HandleLeftFoot;
            slotHandles[6] = HandleRightFoot;
            slotHandles[7] = HandleSpine;
            slotHandles[8] = HandleChest;
            slotHandles[9] = HandleNeck;
            slotHandles[10] = HandleHead;
            slotHandles[11] = HandleLeftShoulder;
            slotHandles[12] = HandleRightShoulder;
            slotHandles[13] = HandleLeftUpperArm;
            slotHandles[14] = HandleRightUpperArm;
            slotHandles[15] = HandleLeftLowerArm;
            slotHandles[16] = HandleRightLowerArm;
            slotHandles[17] = HandleLeftHand;
            slotHandles[18] = HandleRightHand;
            slotHandles[19] = HandleLeftToe;
            slotHandles[20] = HandleRightToe;
            slotHandles[UpperChestSlot] = HandleUpperChest;

            GenerateHeadToSpine(skeleton, Mapping);
            spineMaxIterations = 20;
            spineTolerance = 0.001f;
            chestSpringState = new NativeArray<Vector3>(2, Allocator.Persistent);
            chestSpringInit = new NativeArray<int>(1, Allocator.Persistent);

            swingLastDir = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingLastAxis = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingLastTarget = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingContinuityInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingCollided = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingSwivelDeg = new NativeArray<float>(k_SwingCount, Allocator.Persistent);
            swingGuardSide = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            for (int s = 0; s < k_SwingCount; s++)
            {
                swingSwivelDeg[s] = float.NaN;
            }

            swingSmoothState = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingHintBend = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintAxis = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintReach = new NativeArray<float>(k_SwingCount, Allocator.Persistent);
            swingHintDrag = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintBodyRot = new NativeArray<Quaternion>(k_SwingCount, Allocator.Persistent);
            swingHintInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchor = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchorRot = new NativeArray<Quaternion>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchorInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            legSwivelRaw = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelSmooth = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelInit = new NativeArray<int>(2, Allocator.Persistent);
            legDiagnostics = new NativeArray<BasisLegDiagnostics>(2, Allocator.Persistent);
            armDiagnostics = new NativeArray<BasisArmDiagnostics>(2, Allocator.Persistent);
        }
        void BuildSpineAnatomy(Transform[] chain, BasisTransformMapping Mapping)
        {
            int n = chain.Length;
            ChainSpineRestFrames = new NativeArray<BasisSpineRestFrame>(n, Allocator.Persistent);
            ChainSpineRoms = new NativeArray<BasisSpineRom>(n, Allocator.Persistent);

            // The subject's RIGHT, from the shoulders. A body-wide fact -- NOT a bone's local axis, which is
            // a rig convention and does not transfer between avatars. This project has been bitten by that
            // repeatedly; it is why the arm swivel model is position-only.
            if (Mapping.leftUpperArm == null || Mapping.RightUpperArm == null)
            {
                return;   // every frame stays Valid=false, so the guard is a no-op. Decline, never guess.
            }
            Vector3 hipsRight = Mapping.RightUpperArm.position - Mapping.leftUpperArm.position;

            for (int i = 1; i <= n - 2; i++)   // skip the head (0) and the hips (n-1)
            {
                Transform bone = chain[i];
                Transform child = chain[i - 1];    // the chain runs tip -> root, so the CHILD is i-1
                Transform parent = chain[i + 1];
                if (bone == null || child == null || parent == null)
                {
                    continue;
                }

                BasisSpineSegment segment;
                if (bone == Mapping.spine)
                {
                    segment = BasisSpineSegment.Lumbar;
                }
                else if (bone == Mapping.chest)
                {
                    segment = BasisSpineSegment.LowerThoracic;
                }
                else if (bone == Mapping.Upperchest)
                {
                    segment = BasisSpineSegment.UpperThoracic;
                }
                else if (bone == Mapping.neck)
                {
                    segment = BasisSpineSegment.Cervical;
                }
                else
                {
                    continue;
                }

                ChainSpineRestFrames[i] = BasisSpineAnatomy.BuildRestFrame(bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                ChainSpineRoms[i] = BasisSpineAnatomy.Rom(segment);
            }
        }
        public void GenerateHeadToSpine(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            var HeadToSpine = Mapping.Upperchest != null ? new Transform[] { Mapping.head, Mapping.neck, Mapping.Upperchest, Mapping.chest, Mapping.spine, Mapping.Hips } : new Transform[] { Mapping.head, Mapping.neck, Mapping.chest, Mapping.spine, Mapping.Hips };
            int SpineToHeadLength = HeadToSpine.Length;
            ChainHeadToSpine = new NativeArray<BasisBoneHandle>(SpineToHeadLength, Allocator.Persistent);
            BuildSpineAnatomy(HeadToSpine, Mapping);

            for (int i = 0; i < SpineToHeadLength; i++)
            {
                ChainHeadToSpine[i] = skeleton.Bind(HeadToSpine[i]);
            }
            if (Mapping.Hips != null && Mapping.head != null)
            {
                TposeLengthHeadToHips = (Mapping.head.position - Mapping.Hips.position);
            }
            else
            {
                TposeLengthHeadToHips = Vector3.zero;
            }
            if (Mapping.head != null && Mapping.neck != null)
            {
                TposeHeadToNeckLocal = Quaternion.Inverse(Mapping.head.rotation) * (Mapping.neck.position - Mapping.head.position);
            }
            else
            {
                TposeHeadToNeckLocal = Vector3.zero;
            }

            if (Mapping.Hips != null && Mapping.neck != null)
            {
                TposeLengthNeckToHips = (Mapping.neck.position - Mapping.Hips.position);
            }
            else
            {
                TposeLengthNeckToHips = TposeLengthHeadToHips;
            }

            // Record the size these were measured at, so a later rescale can carry them along.
            TposeBakeScale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        }
        /// <summary>
        /// Carries the baked Tpose* scalars to a new avatar size. They are DENOMINATORS of ratio tests whose
        /// numerators are read live, so a stale value does not degrade the test — it saturates it: the
        /// shoulder solve goes inert (rawReach never reaches ReachEngage), the shrug latches at maximum on
        /// the elbow-tracker path, squishMult pins at 1+boost, and ComputeNeckCue lands at the wrong distance
        /// from the head, which mis-cues DistributeSpineBend, ApplyTrunkCounterbalance and ApplyHipHinge.
        /// All of it inverts above 1x. No-ops before the first bake and when the size has not moved.
        /// </summary>
        public void RescaleTposeScalars(float newScale)
        {
            if (float.IsNaN(newScale) || float.IsInfinity(newScale) || newScale <= 0f)
            {
                return;
            }
            if (TposeBakeScale <= 0f)
            {
                return;
            }
            float k = newScale / TposeBakeScale;
            if (Mathf.Abs(k - 1f) < 1e-6f)
            {
                return;
            }
            TposeShoulderToHandLeft *= k;
            TposeShoulderToHandRight *= k;
            TposeClavicleLenLeft *= k;
            TposeClavicleLenRight *= k;
            TposeShoulderToElbowLeft *= k;
            TposeShoulderToElbowRight *= k;
            TposeLengthHeadToHips *= k;
            TposeHeadToNeckLocal *= k;
            TposeLengthNeckToHips *= k;
            TposeBakeScale = newScale;
        }
        static BasisBoneHandle BindHandle(BasisPoseSkeleton skeleton, Transform t) => (t != null) ? skeleton.Bind(t) : default;
        public void Destroy()
        {
            if (ChainHeadToSpine.IsCreated) ChainHeadToSpine.Dispose();
            if (ChainSpineRestFrames.IsCreated) ChainSpineRestFrames.Dispose();
            if (ChainSpineRoms.IsCreated) ChainSpineRoms.Dispose();
            if (chestSpringState.IsCreated) chestSpringState.Dispose();
            if (chestSpringInit.IsCreated) chestSpringInit.Dispose();
            if (swingLastDir.IsCreated) swingLastDir.Dispose();
            if (swingLastAxis.IsCreated) swingLastAxis.Dispose();
            if (swingLastTarget.IsCreated) swingLastTarget.Dispose();
            if (swingContinuityInit.IsCreated) swingContinuityInit.Dispose();
            if (swingCollided.IsCreated) swingCollided.Dispose();
            if (swingSwivelDeg.IsCreated) swingSwivelDeg.Dispose();
            if (swingGuardSide.IsCreated) swingGuardSide.Dispose();
            if (swingSmoothState.IsCreated) swingSmoothState.Dispose();
            if (swingHintBend.IsCreated) swingHintBend.Dispose();
            if (swingHintAxis.IsCreated) swingHintAxis.Dispose();
            if (swingHintReach.IsCreated) swingHintReach.Dispose();
            if (swingHintDrag.IsCreated) swingHintDrag.Dispose();
            if (swingHintBodyRot.IsCreated) swingHintBodyRot.Dispose();
            if (swingHintInit.IsCreated) swingHintInit.Dispose();
            if (swingPoleAnchor.IsCreated) swingPoleAnchor.Dispose();
            if (swingPoleAnchorRot.IsCreated) swingPoleAnchorRot.Dispose();
            if (swingPoleAnchorInit.IsCreated) swingPoleAnchorInit.Dispose();
            if (legDiagnostics.IsCreated) legDiagnostics.Dispose();
            if (armDiagnostics.IsCreated) armDiagnostics.Dispose();
            if (legSwivelRaw.IsCreated) legSwivelRaw.Dispose();
            if (legSwivelSmooth.IsCreated) legSwivelSmooth.Dispose();
            if (legSwivelInit.IsCreated) legSwivelInit.Dispose();
        }
    }
}
