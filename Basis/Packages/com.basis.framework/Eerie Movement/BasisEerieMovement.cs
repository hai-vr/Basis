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
        public const float k_ShoulderCoupleRatio = 0.4f;
        public const float k_ShoulderMaxDeg = 25f;
        public const float k_ChestIkWeight = 0.5f;
        public const int k_ChestIkIters = 8;
        public const int k_ChestIkHeadRestoreSweeps = 2;
        public const int Count = 22;
        public const int UpperChestSlot = Count - 1;
        public const float k_TrackedKneeSwivelMinCutoffHz = 1.5f;  // held-still smoothing floor (vs 1.0 standing)
        public const float k_TrackedKneeSwivelBeta = 0.20f;        // 4x standing: opens fast so real shin motion isn't lagged
        public const float k_TrackedKneeSwivelDerivCutoffHz = 1.0f;
        public const float k_NeckGazeFollowMaxDeg = 18f;
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
TargetRotationLeftShoulder, TargetRotationRightShoulder;
        public Quaternion targetOffsetHead, targetOffsetChest, targetOffsetLeftToe,
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
protectElbow, collideTrackedElbow, useNeuralPole,
elbowDragEnabled,
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
        public bool kneeFootPoleHold, kneeFootPoleConditioning;
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
        public NativeArray<int> swingSmoothState;
        public NativeArray<Vector3> swingHintBend;
        public NativeArray<Vector3> swingHintAxis;
        public NativeArray<Vector3> swingHintDrag;
        public NativeArray<Quaternion> swingHintBodyRot;
        public NativeArray<int> swingHintInit;
        public NativeArray<Vector3> legSwivelRaw;
        public NativeArray<Vector3> legSwivelSmooth;
        public NativeArray<int> legSwivelInit;
        public NativeArray<BasisLegDiagnostics> legDiagnostics;
        public float ikLockMode;
        public bool shoulderSolveEnabled;
        public bool shoulderShrugEnabled;
        // T-pose baked reference data for shoulder solve
        public Vector3 TposeLeftShoulderLocalDir, TposeRightShoulderLocalDir;
        public Quaternion TposeLeftShoulderRot, TposeRightShoulderRot;
        public Quaternion TposeChestRot;
        public float TposeShoulderToHandLeft, TposeShoulderToHandRight;
        public float TposeClavicleLenLeft, TposeClavicleLenRight;
        public float TposeShoulderToElbowLeft, TposeShoulderToElbowRight;
        public BasisPoseStream Stream;

        public void Execute() => ProcessAnimation(Stream);

        public void ProcessAnimation(BasisPoseStream stream)
        {

            // Per-frame reads so FBT recalibration (which updates these on the constraint data)
            // reaches the running job; the originals were copied once at job build (issue #531).
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

            // 2) Shoulder pre-solve: elevate/protract based on hand targets before arm IK
            if (shoulderSolveEnabled)
            {
                SolveShoulder(stream, HandleLeftShoulder, enabledLeftShoulder, targetPositionLeftHand, hintPositionLeftHand, hintWeightLeftHand, TposeLeftShoulderLocalDir, TposeLeftShoulderRot, TposeChestRot, TposeShoulderToHandLeft, TposeClavicleLenLeft, TposeShoulderToElbowLeft, true);
                SolveShoulder(stream, HandleRightShoulder, enabledRightShoulder, targetPositionRightHand, hintPositionRightHand, hintWeightRightHand, TposeRightShoulderLocalDir, TposeRightShoulderRot, TposeChestRot, TposeShoulderToHandRight, TposeClavicleLenRight, TposeShoulderToElbowRight, false);
            }
            else
            {
                ApplyRotation(stream, enabledLeftShoulder, HandleLeftShoulder, TargetRotationLeftShoulder, targetOffsetLeftShoulder);
                ApplyRotation(stream, enabledRightShoulder, HandleRightShoulder, TargetRotationRightShoulder, targetOffsetRightShoulder);
            }
            if (anatShoulderSlide)
            {
                ApplyShoulderSlide(stream);
            }

            // 3) Legs: two-bone IK with bend normal preference
            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, KneeBendPrefLeft, hintIsTrackerLeftLowerLeg, footIsTrackerLeftLeg, 0);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, KneeBendPrefRight, hintIsTrackerRightLowerLeg, footIsTrackerRightLeg, 1);

            // 4) Hands: two-bone IK with collision + elbow protection. bodyRight (shoulder->shoulder) orients
            // the torso's elliptical collision cross-section; shared by both arms so it is computed once here.
            Vector3 bodyRight = (HandleLeftUpperArm.IsValid(stream) && HandleRightUpperArm.IsValid(stream))
                ? HandleRightUpperArm.GetPosition(stream) - HandleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            SolveHand(stream, enabledLeftHand, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand, hintWeightLeftHand, targetOffsetLeftHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingLeftElbow);
            SolveHand(stream, enabledRightHand, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand, hintWeightRightHand, targetOffsetRightHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingRightElbow);

            // Arm pop continuity: rate-limit the elbow swing so a torso-collision change eases in
            // instead of popping in one frame. Runs before arm twist (which reads the arm pose).
            float swingRate = swingSmoothRateDeg;
            float swingDt = stream.deltaTime;
            if (enabledLeftHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingLeftElbow, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, swingRate, swingDt);
            }

            if (enabledRightHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingRightElbow, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, swingRate, swingDt);
            }

            // 4b) Arm twist distribution: spread wrist/elbow roll along the optional twist bones
            // so the mesh doesn't pinch at the wrist when the hand rotates.
            float lowerTwist = lowerArmTwistFraction;
            float upperTwist = upperArmTwistFraction;
            SolveArmTwist(stream, HandleLeftLowerArm, HandleLeftHand, HandleLeftLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleRightLowerArm, HandleRightHand, HandleRightLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftUpperArmTwist, upperTwist);
            SolveArmTwist(stream, HandleRightUpperArm, HandleRightLowerArm, HandleRightUpperArmTwist, upperTwist);

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
                ApplyArmSwingChestFollow(stream);
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

            // ==========================================================================================
            // PHASE B -- THE CHEST AS A SECONDARY IK TARGET. The loop above placed the HEAD (primary,
            // welded to the HMD); the chest position fell out of it as a free FK consequence. Now pull the
            // chest bone onto its own target and RESTORE the head with the joints above the chest, which
            // have spare DOF. The head is never traded for the chest. Bit-identical to head-only above when
            // the chest target is off (weight 0). See SolveChestTarget.
            // ==========================================================================================
            SolveChestTarget(stream, headTargetPos, firstJoint, lastJoint, chainLen, jointSpan,
                cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);

            ChainHeadToSpine[tipIdx].SetRotation(stream, finalHeadRot);
        }
        // One CCD step aiming the head tip from joint `i` -- the exact body of the Phase A loop, extracted so
        // Phase B's head-restore reuses it verbatim (a copy would drift). Shapes the reach (twist graded root
        // -> tip, mid-thoracic stiffened), relaxes, applies the cones, then the anatomy guard LAST.
        void ReachHeadJoint(BasisPoseStream stream, int i, Vector3 headTargetPos, int firstJoint, int chainLen,
            float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp, float ccdRelax,
            float neckCone, float chestCone)
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

            // LAST, so it sees the outcome of every other constraint on this joint, not just the
            // CCD's own step. The cones above are reach heuristics; this is anatomy.
            GuardSpineJoint(stream, i);
        }
        void SolveChestTarget(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint, int lastJoint,
            int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp,
            float ccdRelax, float neckCone, float chestCone)
        {
            // Off (toggle false -> weight 0): return before touching a single bone, so the head-only solve
            // above is the whole story, bit for bit. This is the "same usability" guarantee.
            if (!chestIkTarget)
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
        // ==============================================================================================
        // THE ANATOMICAL ENVELOPE. Pulls one spine joint back inside the range of motion its real vertebrae
        // have. See BasisSpineAnatomyCore for the measurements and BasisSpineAnatomy for the table.
        //
        // WHY IT LIVES INSIDE THE CCD LOOP. The CCD is what actually places the head, and before this it
        // rotated the spine, chest and upperChest with NO per-joint limit whatsoever -- its only constraints
        // were a cone on the neck and a cone on the chest. So a limit applied BEFORE the CCD is a suggestion
        // the CCD is free to ignore, which is exactly what happened to BasisSpineBendCore.ClampAsymmetric.
        // And a limit applied AFTER the CCD would drag the head off the HMD, which is not negotiable.
        //
        // Applied per-joint INSIDE the loop, the residual simply redistributes onto the other vertebrae on
        // the next sweep -- which is what a real spine does when you ask one segment for more than it has.
        // The head still converges, because the CCD still gets the last word on it.
        //
        // The chain runs head -> hips, so joint `i`'s PARENT is `i + 1`.
        // ==============================================================================================
        void GuardSpineJoint(BasisPoseStream stream, int i)
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
        void GuardSpineChain(BasisPoseStream stream)
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
        void ClampNeckCone(BasisPoseStream stream, int neckIdx, float maxConeDeg)
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
        // Mid-thoracic bend stiffness for the spine CCD: the swing of the mid joints is scaled down by this
        // (ends unaffected) so a lean curves at the flexible lumbar + cervical and stays firm through the
        // ribcage, distributing the bend instead of kinking at one joint. 0 = uniform (off).
        const float k_ThoracicBendStiffen = 0.3f;
        // Width of the spine CCD's taut band as a fraction of the hips->head chain length (~11 mm on a
        // 1.7 m avatar). Must comfortably exceed the compressions an upright head commands through the
        // neck-pivot lever (quadratic in pitch: ~1.4 mm at 8 deg, ~5.6 mm at 20 deg) — those are the
        // noise-scale demands that sat the solver on its full-extension singularity. See SolveSequentialSpineIK.
        const float k_SpineTautBandFrac = 0.015f;
        // Lateral bend -> a little same-side axial rotation in the pre-bend, so a sustained lean reads as an
        // organic spinal coupling rather than a pure hinge. Small; clamped by the lateral limit downstream.
        const float k_BendTwistCoupling = 0.15f;
        const float k_ChestPosPullMaxDeg = 20f;
        const float k_ChestPullMaxDistSqr = 0.25f;
        const float k_ChestFollowChestShare = 0.6f;
        void ClampChestCone(BasisPoseStream stream, int chestIdx, float maxConeDeg)
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
        void BiasSpineTowardChest(BasisPoseStream stream)
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
        // Pre-distributes the hips→head bend onto spine and upperChest in hips-local space, split
        // into independent pitch / yaw / roll contributions so anisotropic human ranges of motion
        // can be respected (lumbar twists very little, cervical twists a lot, forward bend ≫ back).
        // Pipeline: (chest spring smooths target) → (decompose bend into pitch/roll, twist into yaw)
        //   → (per-axis weight) → (asymmetric clamp) → (apply as hips-local delta).
        // The chest→neck→head two-bone solve afterwards handles whatever residual reach remains.
        // The neck, estimated RIGIDLY off the head target, and therefore EXACTLY invariant to a gaze: if the
        // head orbits the neck by Q then Q's two lever arms cancel algebraically (written out in full inside
        // DistributeSpineBend). Every consumer that wants to know where the TORSO is must read this and not
        // headTargetPos -- the HMD sits forward of the neck pivot, so the raw head target reports a lean the
        // moment you look down. Shared by the spine bend, the postural counterbalance and the hip hinge so
        // the three cannot drift apart.
        Vector3 ComputeNeckCue(Vector3 headTargetPos)
        {
            return headTargetPos + (targetRotationHead * targetOffsetHead) * TposeHeadToNeckLocal;
        }
        // Wrapper for BasisTrunkCounterbalanceCore: the pelvis travels back as the trunk folds forward, so the
        // bend happens at the hip instead of the torso folding down into itself. The cap scales with the
        // avatar's own spine (MinHeadSpineHeight is the T-pose hips->head chain), so it is avatar-relative
        // rather than a fixed number of metres. Gating (no hip tracker) is the caller's, as with ApplyHipHinge.
        Vector3 ApplyTrunkCounterbalance(Vector3 neckCue, Vector3 hipsPos, Vector3 playerUp, out float flexionFrac)
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
        // Ceiling on the posterior pelvic shift, as a fraction of T-pose spine length: ~25 cm on a 0.55 m
        // spine, the top of the measured range for a real full forward bend. Eased into, never a step.
        const float k_TrunkCounterbalanceMaxSpineFrac = 0.45f;
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

            // ==========================================================================================
            // THE SPINE IS CUED OFF THE NECK, NOT THE HEAD. This is the fix for "looking down forces chest
            // to rotate".
            //
            // BasisSpineBendCore bends the spine by the angle between hips->chest and hips->CUE. Hand it the
            // HEAD and you have handed it a point that is not on the spine at all -- the head sits on the END
            // of the neck and ORBITS it when you nod. So a user who gazes down without moving their torso by
            // one millimetre still swings the head target forward and down, the hips->head vector tips over,
            // and the solver bends the spine to a lean that never happened. Measured on a T-posed adult with
            // the torso held byte-identical: a 45 deg glance down invents 4.4 deg of chest pitch, 60 deg
            // invents 8.4 deg, 75 deg invents 10.4 deg. (BasisSpineGazeContaminationTests.)
            //
            // The neck, estimated RIGIDLY off the head, is exactly invariant to that nod. Write it out: if
            // the head orbits the neck by Q, then
            //     estimatedNeck = (neck + Q*(head-neck)) + (Q*headRot) * inv(headRot)*(neck-head)
            //                   = neck + Q*(head-neck) + Q*(neck-head)
            //                   = neck
            // -- the two lever arms cancel, algebraically, for ANY Q. Not damped, not faded, not clamped:
            // CANCELLED. A gaze cannot move this cue, so it cannot bend the spine, so there is nothing left
            // to tune. BasisSpineGazeContaminationTests pins it at exactly zero.
            //
            // A real human's chest pitches -0.05 deg per degree of gaze -- i.e. not at all -- so zero is not
            // an approximation of the right answer here, it IS the right answer.
            //
            // It also disarms a SECOND bug for free. ComputeSquishMultiplier amplifies the spine's rotation
            // as hips->cue COMPRESSES (x1.42 at 25% compression), and gazing down was shortening hips->HEAD
            // -- so the phantom bend was being multiplied by a phantom squish. The neck does not move on a
            // gaze, so neither does the squish. RestLen moves to hips->NECK to match: the spine spans the
            // spine, and the head was never part of it.
            // ==========================================================================================
            Vector3 neckCue = ComputeNeckCue(headTargetPos);

            // A LITTLE REAL SPINE. neckCue is invariant to a pure gaze (the head orbits the neck by Q, the
            // rigid re-attachment un-orbits it -- that is the look-down-stability fix, chest pitch 0.000 deg
            // on any gaze). But that reads as a rigid mannequin under a swiveling head on desktop. Blend the
            // cue a fraction back toward the ACTUAL head: on a look-down the head has orbited forward+down, so
            // the cue tips that way and the chest folds a touch. 0 = rigid, 1 = the full (phantom) follow. A
            // real chest does NOT fold on gaze (corpus: -0.05 deg/deg), so this is a deliberate desktop-feel
            // knob, small by default, and it costs nothing with a chest tracker (the pitch weight is zeroed).
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

            // A tracked chest already measures torso lean, so the head-position-derived forward/lateral
            // pre-bend is redundant -- and looking down swings the HMD forward of the neck, which it
            // misreads as a lean and hunches the chest forward (the squish boost compounds it). Drop the
            // lean (pitch/roll) and let the tracked chest + the spine chain own it; keep the facing twist.
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

            // Apply the delta in the SAME bind-cancelled frame the core measured it in (hipsRot * inv(bind)),
            // not the raw hips-bone frame. On an identity bind this is hipsRot exactly, so it is bit-identical
            // for the usual rigs; on a rig bound rolled/axis-swapped it stops the anatomically-framed bend from
            // being re-applied about the bone's rolled axes (which leaned the chest sideways by 10-14 deg).
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
        // Critically-damped spring on the head target consumed by DistributeSpineBend. Lets the
        // body lag slightly behind quick head moves without affecting the head bone itself.
        // Uses implicit Euler so it stays stable at high Hz / low fps where explicit Euler blows
        // up (omega * dt > 1 → divergent oscillation → NaN → corrupted quaternions downstream).
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
        // Pelvis tilts forward to share the lean past the threshold. Without this, a deep forward
        // reach makes the spine swallow the entire bend and everything above the hips folds.
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
        // `fade` is 1 - sin(trunk flexion) from the postural counterbalance. This term reads head HEIGHT, so
        // it cannot tell a squat from a waist-fold and would double-count the pelvis travel the counterbalance
        // has already applied; fading it out as the trunk folds lets each own the posture it describes -- the
        // crouch sit-back for a squat with an upright trunk, the counterbalance for a bend.
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
        void ApplyShoulderSlide(BasisPoseStream stream)
        {
            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Quaternion chestRot = HandleChest.GetRotation(stream);
            Quaternion chestLocal = Quaternion.Inverse(hipsRot) * chestRot;
            float chestYaw = BasisTwistSolveCore.SignedTwistAngleDeg(chestLocal, Vector3.up);

            const float threshold = 30f;
            const float maxCounter = 15f;
            const float fraction = 0.4f;
            float excess = Mathf.Abs(chestYaw) - threshold;
            if (excess <= 0f)
                return;

            float counterYaw = -Mathf.Sign(chestYaw) * Mathf.Min(excess * fraction, maxCounter);
            ApplyShoulderYaw(stream, HandleLeftShoulder, hipsRot, counterYaw);
            ApplyShoulderYaw(stream, HandleRightShoulder, hipsRot, counterYaw);
        }
        void ApplyShoulderYaw(BasisPoseStream stream, BasisBoneHandle shoulder, Quaternion hipsRot, float yawDeg)
        {
            if (!shoulder.IsValid(stream))
                return;
            Quaternion delta = hipsRot * Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.Inverse(hipsRot);
            shoulder.SetRotation(stream, delta * shoulder.GetRotation(stream));
        }
        void ApplyArmSwingChestFollow(BasisPoseStream stream)
        {
            float factor = chestArmSwingFactor;
            if (factor <= 0f)
            {
                return;
            }

            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            bool leftEnabled = enabledLeftHand > 0f;
            bool rightEnabled = enabledRightHand > 0f;
            if (!leftEnabled && !rightEnabled)
            {
                return;
            }

            Vector3 leftPos = leftEnabled ? targetPositionLeftHand : Vector3.zero;
            Vector3 rightPos = rightEnabled ? targetPositionRightHand : Vector3.zero;
            Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
            Vector3 hipsPos = HandleHips.GetPosition(stream);
            Quaternion hipsAnat = HandleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            Vector3 localMid = invHipsAnat * (handMid - hipsPos);

            float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
            float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;

            Vector3 localMidChest = invHipsAnat * (handMid - HandleChest.GetPosition(stream));
            float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;

            float maxDeg = chestArmSwingMaxDeg;
            if (maxDeg > 0f)
            {
                yawDeg = Mathf.Clamp(yawDeg, -maxDeg, maxDeg);
                pitchDeg = Mathf.Clamp(pitchDeg, -maxDeg, maxDeg);
            }

            Quaternion local = Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, Vector3.right);
            Quaternion deltaWorld = hipsAnat * local * invHipsAnat;

            if (HandleUpperChest.IsValid(stream))
            {
                Quaternion chestPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, k_ChestFollowChestShare);
                Quaternion upperPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, 1f - k_ChestFollowChestShare);
                HandleChest.SetRotation(stream, chestPart * HandleChest.GetRotation(stream));
                HandleUpperChest.SetRotation(stream, upperPart * HandleUpperChest.GetRotation(stream));
            }
            else
            {
                HandleChest.SetRotation(stream, deltaWorld * HandleChest.GetRotation(stream));
            }
        }
        void SolveArmTwist(BasisPoseStream stream, BasisBoneHandle parent, BasisBoneHandle child, BasisBoneHandle twist, float fraction)
        {
            if (!twist.IsValid(stream) || fraction <= 0f)
                return;
            if (!parent.IsValid(stream) || !child.IsValid(stream))
                return;

            Vector3 parentPos = parent.GetPosition(stream);
            Vector3 childPos = child.GetPosition(stream);
            float positionFraction = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, twist.GetPosition(stream));

            BasisTwistSolveInput input;
            input.ParentRotation = parent.GetRotation(stream);
            input.ChildRotation = child.GetRotation(stream);
            input.ParentToChild = childPos - parentPos;
            input.Fraction = positionFraction * fraction;

            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult result);
            if (result.Apply)
            {
                twist.SetRotation(stream, result.TwistWorldRotation);
            }
        }
        public void SolveShoulder(BasisPoseStream stream, BasisBoneHandle shoulderHandle, bool hasShoulderTrackerProp, Vector3 handTargetPosProp, Vector3 hintPosProp, bool hintWeightProp, Vector3 tposeArmDir, Quaternion tposeShoulderRot, Quaternion tposeChestRot, float tposeArmLength, float tposeClavicleLen, float tposeElbowLen, bool isLeft)
        {
            if (!shoulderHandle.IsValid(stream))
            {
                return;
            }

            Quaternion trackerRot = isLeft ? TargetRotationLeftShoulder : TargetRotationRightShoulder;

            BasisShoulderSolveInput input;
            input.ShoulderPos = shoulderHandle.GetPosition(stream);
            input.HandTargetPos = handTargetPosProp;
            input.ElbowPos = hintPosProp;
            input.HasElbow = hintWeightProp;
            input.HasShoulderTracker = hasShoulderTrackerProp;
            input.ChestRot = HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity;
            input.TposeChestRot = tposeChestRot;
            input.TposeShoulderRot = tposeShoulderRot;
            input.TposeArmDirWorld = tposeArmDir;
            input.TposeArmLength = tposeArmLength;
            input.TposeClavicleLength = tposeClavicleLen;
            input.TposeElbowLength = tposeElbowLen;
            input.ShrugEnabled = shoulderShrugEnabled;
            input.ElevationFactor = shoulderElevationFactor;
            input.ProtractionFactor = shoulderProtractionFactor;
            input.CoupleRatio = k_ShoulderCoupleRatio;
            input.MaxShoulderDeg = k_ShoulderMaxDeg;
            input.TrackerFinal = trackerRot * (isLeft ? targetOffsetLeftShoulder : targetOffsetRightShoulder);
            input.IsLeft = isLeft;

            BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult result);
            if (result.Apply)
            {
                shoulderHandle.SetRotation(stream, result.ShoulderRotation);
            }
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

        public void ApplyRotation(BasisPoseStream stream, bool enabledProp, BasisBoneHandle handle, Quaternion targetRotProp, Quaternion RotationOffset)
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
        public void SolveTwoBoneIKArms(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, BasisAffineTransform hint, bool hintWeight, bool hintIsTracker, Quaternion targetOffset)
        {
            BasisArmSolveInput input = default;
            root.GetPositionAndRotation(stream, out Vector3 shoulderPos, out Quaternion shoulderRot);
            mid.GetPositionAndRotation(stream, out Vector3 elbowPos, out Quaternion elbowRot);
            tip.GetPositionAndRotation(stream, out Vector3 handPos, out Quaternion handRot);
            input.Shoulder = shoulderPos;
            input.Elbow = elbowPos;
            input.Hand = handPos;
            input.RootRotation = shoulderRot;
            input.MidRotation = elbowRot;
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint.translation;
            input.HintWeight = hintWeight;
            input.TargetOffset = targetOffset;
            input.PlayerUp = playerUp;
            input.HintIsTracker = hintIsTracker;
            input.HintMaxStepDeg = float.MaxValue;
            input.TipRotation = handRot;
            input.HintRotation = hintIsTracker ? hint.rotation : default;

            BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
        }
        /// <summary>
        /// The ARM's body frame, live, from BONE POSITIONS: shoulder line for right, chest->neck for up.
        ///
        /// From POSITIONS, not from the chest bone's ROTATION, and that is the whole reason it transfers. A
        /// bone's local axes are a rig convention, so a frame taken from rotations is fitted to one skeleton and
        /// no other. It also deletes the old frame's entire problem: ArmBendFrame had to strip the chest's YAW
        /// (or head-gaze chest twist swept the lookup and flipped the elbow pole) and then spring-smooth the hips
        /// to stop hip sway wobbling the derived elbow. A position frame has no yaw to strip -- the shoulder line
        /// IS the yaw -- so both the twist-extraction and the hip-frame spring go away.
        /// </summary>
        BasisSwivelFrame BuildArmFrame(BasisPoseStream stream)
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
        public static void SegmentSegmentClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a <= k_SqrEpsilon && e <= k_SqrEpsilon)
            {
                s = t = 0.0f; c1 = p1; c2 = p2; return;
            }
            if (a <= k_SqrEpsilon)
            {
                s = 0.0f; t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= k_SqrEpsilon)
                {
                    t = 0.0f; s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    if (denom != 0.0f) s = Mathf.Clamp01((b * f - c * e) / denom);
                    else s = 0.0f;

                    t = (b * s + f) / e;
                    if (t < 0.0f) { t = 0.0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1.0f) { t = 1.0f; s = Mathf.Clamp01((b - c) / a); }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
        public static Vector3 CapsuleCapsuleResolve(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float r2, Vector3 playerUp)
        {
            SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
            Vector3 n = c1 - c2;
            float dSqr = Vector3.Dot(n, n);
            float rSum = r1 + r2;

            if (dSqr >= rSum * rSum) return Vector3.zero;

            Vector3 normal;
            if (dSqr > k_SqrEpsilon) normal = n / Mathf.Sqrt(dSqr);
            else
            {
                Vector3 axis = (q2 - p2);
                normal = Vector3.Normalize(Vector3.Cross(axis, playerUp));
                if (normal.sqrMagnitude < k_MinMag)
                {
                    normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
                }

                if (normal.sqrMagnitude < k_MinMag)
                {
                    normal = playerUp;
                }
            }

            float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
            float penetration = (rSum - d);
            return normal * penetration;
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
        void ApplySwingContinuity(BasisPoseStream stream, int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos, float rateDegPerSec, float dt)
        {
            if (!swingContinuityInit.IsCreated || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            Vector3 a = root.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);

            BasisSwingContinuityState state;
            state.LastDir = swingLastDir[slot];
            state.LastAxis = swingLastAxis[slot];
            state.LastTarget = swingLastTarget[slot];
            state.SmoothState = swingSmoothState[slot];
            state.Seeded = swingContinuityInit[slot] != 0;
            int collided = swingCollided.IsCreated ? swingCollided[slot] : 0;

            BasisSwingContinuityCore.Step(state, a, b, c, targetPos, collided, rateDegPerSec, dt, out BasisSwingContinuityResult r);
            if (!r.Valid)
            {
                return;
            }

            if (r.ApplySwing)
            {
                Quaternion preservedHandRot = tip.GetRotation(stream);
                SwingElbowAroundAC(stream, root, mid, tip, a + r.NewDir);
                tip.SetPosition(stream, c);
                tip.SetRotation(stream, preservedHandRot);
            }

            swingLastDir[slot] = r.State.LastDir;
            swingLastAxis[slot] = r.State.LastAxis;
            swingLastTarget[slot] = r.State.LastTarget;
            swingSmoothState[slot] = r.State.SmoothState;
            swingContinuityInit[slot] = 1;
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
        /// <summary>
        /// Evaluates the Two-Bone IK algorithm.
        /// </summary>
        /// <param name="stream">The animation stream to work on.</param>
        /// <param name="root">The transform handle for the root transform.</param>
        /// <param name="mid">The transform handle for the mid transform.</param>
        /// <param name="tip">The transform handle for the tip transform.</param>
        /// <param name="target">The transform handle for the target transform.</param>
        /// <param name="hint">The world-space hint (pole) position.</param>
        /// <param name="HasHint">The weight for which hint transform has an effect on IK calculations. This is a value in between 0 and 1.</param>
        /// <param name="targetOffset">The offset applied to the target transform.</param>
        /// <summary>Returns the shin roll applied to the mid bone, so a preserved (untracked) foot can be carried
        /// by it. Identity whenever no shin roll ran.</summary>
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

            // Solve at full strength toward the IK target
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
                if (BasisSwivelHintCore.LegHint(frame, hipPos, target.translation, legLen, isLeft,
                                                out Vector3 modelHint, out float conf, useNeuralPole))
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
            Quaternion shinRoll = SolveTwoBone(stream, root, mid, tip, target, hint, hintW, targetOffset, bendNormal, hintDistrust, legSlot,hintIsTrackerProp ? hintRotProp : default, hintIsTrackerProp, KneeAnteriorRef);
            if (posWeight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), posWeight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), posWeight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), posWeight));
            }
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
                        conditionOnPole: !hintIsTrackerProp && (!footDerivedPole || kneeFootPoleConditioning),
                        holdWhenSingular: !footDerivedPole || kneeFootPoleHold);
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
        public void SolveHand(BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, Quaternion hintRotProp, bool hintWeightProp, Quaternion targetOffset, BasisBoneHandle chestStart, BasisBoneHandle chestEnd, float chestRadius, float collisionSkin, bool collisionsEnabled, float handRadius, float handSkin, bool protectElbow, bool collideTrackedElbow, Vector3 bodyRight, int swingSlot)
        {
            // Written `!(w > 0)` so a NaN weight takes the reject branch rather than solving on garbage.
            float weight = enabledProp;
            if (!(weight > 0f))
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
            // Read inputs
            Vector3 tgtPos = targetPosProp;
            Quaternion tgtRot = targetRotProp;
            Vector3 hintPos = hintPosProp;
            Quaternion hintRot = hintRotProp;
            var target = new BasisAffineTransform(tgtPos, tgtRot);
            var hint = new BasisAffineTransform(hintPos, hintRot);
            bool hasHint = hintWeightProp;
            bool usedModel = false;

            if (!hasHint)
            {
                BasisSwivelFrame frame = BuildArmFrame(stream);

                Vector3 shoulderPos = root.GetPosition(stream);
                float upperLen = (mid.GetPosition(stream) - shoulderPos).magnitude;
                float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
                float armLen = upperLen + lowerLen;
                bool isLeft = swingSlot == k_SwingLeftElbow;
                if (BasisSwivelHintCore.ArmHint(frame, shoulderPos, tgtPos, armLen, isLeft,
                                                out Vector3 modelHint, out _, useNeuralPole))
                {
                    Vector3 curAxisV = tgtPos - shoulderPos;
                    Vector3 rawBendV = modelHint - shoulderPos;
                    float axLen = curAxisV.magnitude;
                    float rbLen = rawBendV.magnitude;
                    if (axLen > 1e-5f && rbLen > 1e-5f)
                    {
                        Vector3 curAxis = curAxisV / axLen;
                        Vector3 rawBend = rawBendV / rbLen;
                        bool seeded = swingHintInit[swingSlot] != 0;
                        Vector3 cappedBend = seeded
                            ? (Vector3)BasisElbowSwingCapCore.Apply(swingHintBend[swingSlot], swingHintAxis[swingSlot],
                                                                    curAxis, rawBend, BasisElbowSwingCapCore.MaxGain)
                            : rawBend;
                        swingHintBend[swingSlot] = cappedBend;
                        swingHintAxis[swingSlot] = curAxis;
                        Quaternion bodyRot = HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : Quaternion.identity;

                        Vector3 outBend = cappedBend;
                        if (elbowDragEnabled && seeded)
                        {
                            Quaternion bodyDelta = bodyRot * Quaternion.Inverse(swingHintBodyRot[swingSlot]);
                            outBend = (Vector3)BasisElbowDragCore.Apply(swingHintDrag[swingSlot], bodyDelta, curAxis, cappedBend,
                                                                       BasisElbowDragCore.Alpha(elbowDragHz, stream.deltaTime));
                        }
                        swingHintDrag[swingSlot] = outBend;
                        swingHintBodyRot[swingSlot] = bodyRot;
                        swingHintInit[swingSlot] = 1;
                        modelHint = shoulderPos + 0.5f * armLen * outBend;
                    }

                    hint = new BasisAffineTransform(modelHint, hintRot);
                    hasHint = true;
                    usedModel = true;
                }
            }
            if (!usedModel)
            {
                swingHintInit[swingSlot] = 0;
            }
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hasHint, hasHint && !usedModel, targetOffset);
            int collisionState = 0;
            bool doCollisions = collisionsEnabled && chestStart.IsValid(stream) && chestEnd.IsValid(stream);
            bool elbowTrackerForced = hasHint && !usedModel;
            if (doCollisions && protectElbow && (!elbowTrackerForced || collideTrackedElbow))
            {
                BasisElbowProtectInput epi = default;
                epi.Shoulder = root.GetPosition(stream);
                epi.Elbow = mid.GetPosition(stream);
                epi.Hand = tip.GetPosition(stream);
                epi.HasHips = HandleHips.IsValid(stream);
                epi.HasSpine = HandleSpine.IsValid(stream);
                epi.HipsPos = epi.HasHips ? HandleHips.GetPosition(stream) : Vector3.zero;
                epi.SpinePos = epi.HasSpine ? HandleSpine.GetPosition(stream) : Vector3.zero;
                epi.ChestPos = chestStart.GetPosition(stream);
                epi.NeckPos = chestEnd.GetPosition(stream);
                epi.ChestRadiusBase = chestRadius;
                epi.CollisionSkin = collisionSkin;
                epi.HandRadius = handRadius;
                epi.HandSkin = handSkin;
                epi.PlayerUp = playerUp;
                epi.BodyRight = bodyRight;

                BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
                if (epr.Engaged)
                {
                    tip.GetPositionAndRotation(stream, out Vector3 preservedHandPos, out Quaternion preservedHandRot);
                    SwingElbowAroundAC(stream, root, mid, tip, epr.DesiredElbow);
                    tip.SetPosition(stream, preservedHandPos);
                    tip.SetRotation(stream, preservedHandRot);
                }
                collisionState = epr.CollisionState;
            }

            if (swingCollided.IsCreated)
            {
                swingCollided[swingSlot] = collisionState;
            }

            if (weight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), weight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), weight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), weight));
            }
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

            shoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            shoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
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
            standingHeadHeight = 0f; // 0 = sit-back inert until the rig driver packs the real height
            trunkCounterbalance = BasisTrunkCounterbalanceCore.DerivedGain;
            swingSmoothRateDeg = 720f;
            chestArmSwingFactor = 0.3f;
            chestArmSwingMaxDeg = 15f;
            lowerArmTwistFraction = 0.5f;
            upperArmTwistFraction = 0.3f;

            anatDifferentialStiffness = false;
            anatShoulderSlide = false;
            anatCervicalLordosis = false;
            anatPelvicTwistRouting = false;
            spineAnatomicalRom = false;
            chestIkTarget = false;
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
            // 1.0 (was 0.8), retuned against the mocap corpus: full relax is strictly better measured —
            // closer to the human spine AND a quieter standing noise floor. See FBIKSpineCCDRelax.
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
            TposeChestRot = Mapping.chest != null ? Mapping.chest.rotation : Quaternion.identity;
            TposeLeftShoulderLocalDir = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? (Mapping.leftUpperArm.position - Mapping.leftShoulder.position).normalized : Vector3.left;
            TposeRightShoulderLocalDir = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? (Mapping.RightUpperArm.position - Mapping.RightShoulder.position).normalized : Vector3.right;
            // 0.6 m is an adult arm; on a small avatar it is the same shoulder-inert / shrug-latched failure
            // a stale bake produces, so the fallback tracks avatar size too.
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

            // Pair each slot with its bone handle, in HumanBodyBones order.
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
            swingSmoothState = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingHintBend = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintAxis = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintDrag = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintBodyRot = new NativeArray<Quaternion>(k_SwingCount, Allocator.Persistent);
            swingHintInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            legSwivelRaw = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelSmooth = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelInit = new NativeArray<int>(2, Allocator.Persistent);
            legDiagnostics = new NativeArray<BasisLegDiagnostics>(2, Allocator.Persistent);
        }
        void BuildSpineAnatomy(Transform[] chain, BasisTransformMapping Mapping)
        {
            int n = chain.Length;
            ChainSpineRestFrames = new NativeArray<BasisSpineRestFrame>(n, Allocator.Persistent);
            ChainSpineRoms = new NativeArray<BasisSpineRom>(n, Allocator.Persistent);
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

                ChainSpineRestFrames[i] = BasisSpineAnatomy.BuildRestFrame(
                    bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                ChainSpineRoms[i] = BasisSpineAnatomy.Rom(segment);
            }
        }
        public void GenerateHeadToSpine(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            var HeadToSpine = Mapping.Upperchest != null
                ? new Transform[] { Mapping.head, Mapping.neck, Mapping.Upperchest, Mapping.chest, Mapping.spine, Mapping.Hips }
                : new Transform[] { Mapping.head, Mapping.neck, Mapping.chest, Mapping.spine, Mapping.Hips };
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
        public void RescaleTposeScalars(float newScale)
        {
            if (float.IsNaN(newScale) || float.IsInfinity(newScale) || newScale <= 0f || TposeBakeScale <= 0f)
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
            if (swingSmoothState.IsCreated) swingSmoothState.Dispose();
            if (swingHintBend.IsCreated) swingHintBend.Dispose();
            if (swingHintAxis.IsCreated) swingHintAxis.Dispose();
            if (swingHintDrag.IsCreated) swingHintDrag.Dispose();
            if (swingHintBodyRot.IsCreated) swingHintBodyRot.Dispose();
            if (swingHintInit.IsCreated) swingHintInit.Dispose();
            if (legDiagnostics.IsCreated) legDiagnostics.Dispose();
            if (legSwivelRaw.IsCreated) legSwivelRaw.Dispose();
            if (legSwivelSmooth.IsCreated) legSwivelSmooth.Dispose();
            if (legSwivelInit.IsCreated) legSwivelInit.Dispose();
        }
    }
}
