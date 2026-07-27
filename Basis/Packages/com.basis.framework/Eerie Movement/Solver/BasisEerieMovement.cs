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
    public partial struct BasisEerieMovement : Unity.Jobs.IJob
    {
        public const float k_Epsilon = 1e-5f; // or 0.00001f
        public const float k_MinMag = 1e-6f;// or 0.000001f
        public const float k_SqrEpsilon = 1e-8f;// or 0.00000001f
        public const int Count = 22;
        public const int UpperChestSlot = Count - 1;
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
        // Scapulohumeral coupling: girdle share of the humeral swing, and the clamp on the applied girdle rotation.
        public float shoulderCoupleRatio, shoulderMaxDeg;
        // Anatomical shoulder slide: past shoulderSlideStartDeg of chest yaw the girdle counter-rotates by
        // `fraction` of the excess, capped at shoulderSlideMaxDeg.
        public float shoulderSlideStartDeg, shoulderSlideMaxDeg, shoulderSlideFraction;
        // Chest-as-secondary-IK-target: pull weight, solver iterations, head-restore sweeps per iteration, the
        // cap on the spine's positional pull, and the distance past which a chest target is treated as a glitch.
        public float chestIkWeight, chestPosPullMaxDeg, chestPullMaxDist;
        public int chestIkIterations, chestIkHeadRestoreSweeps;
        // Chest share of the arm-swing torso follow; the upper chest takes the remainder.
        public float chestFollowChestShare;
        // Mid-thoracic bend stiffness for the spine CCD: the swing of the mid joints is scaled down by this
        // (ends unaffected) so a lean curves at the flexible lumbar + cervical and stays firm through the
        // ribcage, distributing the bend instead of kinking at one joint. 0 = uniform (off).
        public float thoracicBendStiffen;
        // Width of the spine CCD's taut band as a fraction of the hips->head chain length (~11 mm on a
        // 1.7 m avatar). Must comfortably exceed the compressions an upright head commands through the
        // neck-pivot lever (quadratic in pitch: ~1.4 mm at 8 deg, ~5.6 mm at 20 deg) — those are the
        // noise-scale demands that sat the solver on its full-extension singularity. See SolveSequentialSpineIK.
        public float spineTautBandFrac;
        // Lateral bend -> a little same-side axial rotation in the pre-bend, so a sustained lean reads as an
        // organic spinal coupling rather than a pure hinge. Small; clamped by the lateral limit downstream.
        public float bendTwistCoupling;
        // Ceiling on the posterior pelvic shift, as a fraction of T-pose spine length: ~25 cm on a 0.55 m
        // spine, the top of the measured range for a real full forward bend. Eased into, never a step.
        public float trunkCounterbalanceMaxSpineFrac;
        // Cap on how far the neck may lead a gaze ahead of the spine chain.
        public float neckGazeFollowMaxDeg;
        // One Euro parameters for a knee whose pole comes from a tracker: a higher floor than the standing
        // path, and 4x the beta so real shin motion isn't lagged.
        public float trackedKneeSwivelMinCutoffHz, trackedKneeSwivelBeta, trackedKneeSwivelDerivCutoffHz;
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

        /// <summary>
        /// The frame. Each pass lives in its own file next to the cores it drives -- spine in
        /// Spine/, shoulders and arms in Arms/, legs and toes in Legs/, the bone-write helpers in
        /// BasisEerieMovement.Shared.cs. The ORDER here is the contract: the spine places the torso the
        /// girdle hangs off, the girdle places the shoulders the arms hang off, and the legs run before
        /// the arms because the arm pass collides against the torso the spine has already settled.
        /// </summary>
        public void ProcessAnimation(BasisPoseStream stream)
        {
            CaptureCalibrationOffsets();
            SolveSpinePass(stream);
            SolveShoulderPass(stream);
            SolveLegPass(stream);
            SolveArmPass(stream);
            SolveToePass(stream);
            ApplyTrackerOverrides(stream);
        }

        // Per-frame reads so FBT recalibration (which updates these on the constraint data)
        // reaches the running job; the originals were copied once at job build (issue #531).
        void CaptureCalibrationOffsets()
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
            enabledLeftShoulder = enabledRightShoulder = false;
            offsetRotationHead = offsetRotationLeftFoot = offsetRotationRightFoot = Quaternion.identity;
            offsetRotationLeftHand = offsetRotationRightHand = Quaternion.identity;

            playerUp = Vector3.up;

            // Avatar-measured; the rig driver overwrites both once the T-pose spine is known.
            MinHeadSpineHeight = 0f;
            minFactor = 0.95f;
            maxFactor = 1.05f;
            spineMaxIterations = 20;
            spineTolerance = 0.001f;

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
            useNeuralPole = false;

            shoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            shoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
            shoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            shoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;
            shoulderCoupleRatio = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderCoupleRatio.RawValue;
            shoulderMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderMaxDeg.RawValue;
            shoulderSlideStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideStartDeg.RawValue;
            shoulderSlideMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideMaxDeg.RawValue;
            shoulderSlideFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideFraction.RawValue;
            thoracicBendStiffen = Basis.BasisUI.BasisSettingsDefaults.FBIKThoracicBendStiffen.RawValue;
            spineTautBandFrac = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTautBandFrac.RawValue;
            bendTwistCoupling = Basis.BasisUI.BasisSettingsDefaults.FBIKBendTwistCoupling.RawValue;
            neckGazeFollowMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckGazeFollowMaxDeg.RawValue;
            trunkCounterbalanceMaxSpineFrac = Basis.BasisUI.BasisSettingsDefaults.FBIKTrunkCounterbalanceMaxFrac.RawValue;
            chestIkWeight = Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkWeight.RawValue;
            chestIkIterations = Mathf.Max(1, Mathf.RoundToInt(Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkIterations.RawValue));
            chestIkHeadRestoreSweeps = Mathf.Max(1, Mathf.RoundToInt(Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkHeadRestoreSweeps.RawValue));
            chestPosPullMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestPosPullMaxDeg.RawValue;
            chestPullMaxDist = Basis.BasisUI.BasisSettingsDefaults.FBIKChestPullMaxDist.RawValue;
            chestFollowChestShare = Basis.BasisUI.BasisSettingsDefaults.FBIKChestFollowChestShare.RawValue;
            trackedKneeSwivelMinCutoffHz = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelMinCutoffHz.RawValue;
            trackedKneeSwivelBeta = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelBeta.RawValue;
            trackedKneeSwivelDerivCutoffHz = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelDerivCutoffHz.RawValue;

            maxBendDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxBendDeg.RawValue;
            MaxChestDeltaProperty = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxChestDelta.RawValue;
            spineBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendPitch.RawValue;
            spineBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendYaw.RawValue;
            spineBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendRoll.RawValue;
            upperChestBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendPitch.RawValue;
            upperChestBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendYaw.RawValue;
            upperChestBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendRoll.RawValue;
            hipHingeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeStartDeg.RawValue;
            hipHingeMaxAddDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeMaxAddDeg.RawValue;
            chestSpringHz = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringHz.RawValue;
            chestSpringDamping = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringDamping.RawValue;
            spineMaxForwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxForwardDeg.RawValue;
            spineMaxBackwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxBackwardDeg.RawValue;
            spineMaxLateralDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxLateralDeg.RawValue;
            spineSquishBoost = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineSquishBoost.RawValue;
            spineGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineGazeFollow.RawValue;
            neckGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckGazeFollow.RawValue;
            moveBodyBackWhenCrouching = Basis.BasisUI.BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching.RawValue;
            crouchDepth = 0f;
            standingHeadHeight = 0f; // 0 = sit-back inert until the rig driver packs the real height
            trunkCounterbalance = Basis.BasisUI.BasisSettingsDefaults.FBIKTrunkCounterbalance.RawValue;
            swingSmoothRateDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowSwingEnabled.RawValue
                ? Basis.BasisUI.BasisSettingsDefaults.FBIKSwingSmoothRate.RawValue
                : 0f;
            chestArmSwingFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingFactor.RawValue;
            chestArmSwingMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingMaxDeg.RawValue;
            lowerArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKLowerArmTwistFraction.RawValue;
            upperArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperArmTwistFraction.RawValue;

            anatDifferentialStiffness = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatDifferentialStiffness.RawValue;
            anatShoulderSlide = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatShoulderSlide.RawValue;
            anatCervicalLordosis = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatCervicalLordosis.RawValue;
            anatPelvicTwistRouting = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatPelvicTwistRouting.RawValue;
            spineAnatomicalRom = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineAnatomicalRom.RawValue;
            chestIkTarget = Basis.BasisUI.BasisSettingsDefaults.FBIKChestIKTarget.RawValue;
            legSwivelSmoothing = Basis.BasisUI.BasisSettingsDefaults.FBIKLegSwivelSmoothing.RawValue;
            kneeFootPoleHold = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootPoleHold.RawValue;
            kneeFootPoleConditioning = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootPoleConditioning.RawValue;
            lordosisPitchGainDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisPitchGainDeg.RawValue;
            lordosisBaseDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisBaseDeg.RawValue;
            lordosisNeckShare = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisNeckShare.RawValue;
            lordosisMaxHeadPitchDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisMaxHeadPitchDeg.RawValue;
            lordosisExtremeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeStartDeg.RawValue;
            lordosisExtremeFullDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeFullDeg.RawValue;
            lordosisExtremeRollForwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollForwardMaxDeg.RawValue;
            lordosisExtremeRollBackwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollBackwardMaxDeg.RawValue;
            lordosisExtremeHipsHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalMax.RawValue;
            lordosisExtremeChestHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalMax.RawValue;
            lordosisExtremeHipsDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownMax.RawValue;
            lordosisExtremeChestDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownMax.RawValue;
            lordosisExtremeHipsDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownLookUp.RawValue;
            lordosisExtremeChestDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownLookUp.RawValue;
            // 1.0 (was 0.8), retuned against the mocap corpus: full relax is strictly better measured —
            // closer to the human spine AND a quieter standing noise floor. See FBIKSpineCCDRelax.
            spineCCDRelax = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineCCDRelax.RawValue;
            neckMaxConeDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckMaxConeDeg.RawValue;
            spineTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTwistKeep.RawValue;
            spineNeckTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineNeckTwistKeep.RawValue;

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
