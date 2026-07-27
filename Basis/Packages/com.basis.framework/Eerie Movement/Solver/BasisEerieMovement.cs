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
        // ===== Numeric tolerances =====
        public const float k_Epsilon = 1e-5f;
        public const float k_MinMag = 1e-6f;
        public const float k_SqrEpsilon = 1e-8f;

        // ===== Per-bone override slots, in HumanBodyBones order =====
        public const int Count = 22;
        public const int UpperChestSlot = Count - 1;
        public FixedList128Bytes<BasisBoneHandle> slotHandles;
        public FixedList512Bytes<Vector3> slotPositions;
        public FixedList512Bytes<Quaternion> slotRotations;
        public FixedList512Bytes<Quaternion> slotOffsets;
        public FixedList64Bytes<bool> slotWeights;

        // ===== Bone handles =====
        public BasisBoneHandle handleHips, handleSpine, handleChest, handleUpperChest, handleNeck, handleHead;
        public BasisBoneHandle handleLeftShoulder, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand;
        public BasisBoneHandle handleRightShoulder, handleRightUpperArm, handleRightLowerArm, handleRightHand;
        public BasisBoneHandle handleLeftUpperArmTwist, handleLeftLowerArmTwist;
        public BasisBoneHandle handleRightUpperArmTwist, handleRightLowerArmTwist;
        public BasisBoneHandle handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot, handleLeftToe;
        public BasisBoneHandle handleRightUpperLeg, handleRightLowerLeg, handleRightFoot, handleRightToe;
        // Head -> hips, tip first. The CCD chain, with its per-joint rest frames and ranges of motion.
        public NativeArray<BasisBoneHandle> chainHeadToSpine;
        public NativeArray<BasisSpineRestFrame> chainSpineRestFrames;
        public NativeArray<BasisSpineRom> chainSpineRoms;

        // ===== Per-frame targets: spine =====
        public Vector3 targetPositionHead, targetPositionHips;
        public Quaternion targetRotationHead, targetRotationHips, targetRotationChest;
        // targetPositionChest is head-hint biased; the Raw one is not. SolveChestTarget must use Raw --
        // pinning to the biased one dragged the torso ~8 cm up in desktop / no-tracker mode.
        public Vector3 targetPositionChest, targetPositionChestRaw;
        public Vector3 playerUp;

        // ===== Per-frame targets: arms =====
        public Vector3 targetPositionLeftHand, hintPositionLeftHand;
        public Vector3 targetPositionRightHand, hintPositionRightHand;
        public Quaternion targetRotationLeftHand, hintRotationLeftHand;
        public Quaternion targetRotationRightHand, hintRotationRightHand;
        public Quaternion targetRotationLeftShoulder, targetRotationRightShoulder;

        // ===== Per-frame targets: legs and toes =====
        public Vector3 targetPositionLeftLowerLeg, hintPositionLeftLowerLeg;
        public Vector3 targetPositionRightLowerLeg, hintPositionRightLowerLeg;
        public Quaternion targetRotationLeftLowerLeg, hintRotationLeftLowerLeg;
        public Quaternion targetRotationRightLowerLeg, hintRotationRightLowerLeg;
        public Vector3 kneeBendPrefLeft, kneeBendPrefRight, kneeAnteriorRef;
        public Quaternion leftDrivenTargetRot, rightDrivenTargetRot;
        public float leftToeBendDeg, rightToeBendDeg;
        public Vector3 leftToeBendAxis, rightToeBendAxis;

        // ===== Calibration rotation offsets =====
        // offsetRotation* are the inputs the driver re-applies every frame (issue #531); targetOffset* are
        // the copies CaptureCalibrationOffsets takes at the top of the solve.
        public Quaternion offsetRotationHips, offsetRotationHead, offsetRotationChest;
        public Quaternion offsetRotationLeftFoot, offsetRotationRightFoot;
        public Quaternion offsetRotationLeftToe, offsetRotationRightToe;
        public Quaternion offsetRotationLeftShoulder, offsetRotationRightShoulder;
        public Quaternion offsetRotationLeftHand, offsetRotationRightHand;
        public Quaternion targetOffsetHead, targetOffsetChest;
        public Quaternion targetOffsetLeftFoot, targetOffsetRightFoot;
        public Quaternion targetOffsetLeftToe, targetOffsetRightToe;
        public Quaternion targetOffsetLeftShoulder, targetOffsetRightShoulder;
        public Quaternion targetOffsetLeftHand, targetOffsetRightHand;

        // ===== Effector weights and tracker presence =====
        public float enabledLeftHand, enabledRightHand;
        public float enabledLeftLowerLeg, enabledRightLowerLeg;
        public float hintWeightLeftLowerLeg, hintWeightRightLowerLeg;
        public bool hintWeightLeftHand, hintWeightRightHand;
        public bool enabledSpineIK, enabledLeftShoulder, enabledRightShoulder;
        public bool leftToeEnabled, rightToeEnabled;
        public bool hasChestTracker, hasHipsTracker;
        public bool hintIsTrackerLeftLowerLeg, hintIsTrackerRightLowerLeg;
        public bool footIsTrackerLeftLeg, footIsTrackerRightLeg;

        // ===== T-pose bake =====
        // Measured at tposeBakeScale; RescaleTposeScalars carries them across an avatar resize.
        public float tposeBakeScale;
        public Vector3 tposeLengthHeadToHips, tposeLengthNeckToHips, tposeHeadToNeckLocal;
        public Vector3 tposeLeftShoulderLocalDir, tposeRightShoulderLocalDir;
        public Quaternion tposeLeftShoulderRot, tposeRightShoulderRot, tposeChestRot;
        public float tposeShoulderToHandLeft, tposeShoulderToHandRight;
        public float tposeClavicleLenLeft, tposeClavicleLenRight;
        public float tposeShoulderToElbowLeft, tposeShoulderToElbowRight;

        // ===== Tunables: spine =====
        public BasisIKLockMode ikLockMode;
        public int spineMaxIterations;
        public float spineTolerance;
        public float minHeadSpineHeight, maxBendDeg, minFactor, maxFactor, maxChestDeltaDeg;
        public float spineBendPitch, spineBendYaw, spineBendRoll;
        public float upperChestBendPitch, upperChestBendYaw, upperChestBendRoll;
        public float spineMaxForwardDeg, spineMaxBackwardDeg, spineMaxLateralDeg;
        public float spineSquishBoost, spineGazeFollow, neckGazeFollow;
        public float spineCCDRelax, neckMaxConeDeg, spineTwistKeep, spineNeckTwistKeep;
        public float chestSpringHz, chestSpringDamping;
        public float hipHingeStartDeg, hipHingeMaxAddDeg;
        public float moveBodyBackWhenCrouching, crouchDepth, standingHeadHeight;
        public float trunkCounterbalance;
        // Ceiling on the posterior pelvic shift, as a fraction of T-pose spine length: ~25 cm on a 0.55 m
        // spine, the top of the measured range for a real full forward bend. Eased into, never a step.
        public float trunkCounterbalanceMaxSpineFrac;
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
        // Cap on how far the neck may lead a gaze ahead of the spine chain.
        public float neckGazeFollowMaxDeg;
        // Chest-as-secondary-IK-target: pull weight, solver iterations, head-restore sweeps per iteration, the
        // cap on the spine's positional pull, and the distance past which a chest target is treated as a glitch.
        public bool chestIkTarget;
        public float chestIkWeight, chestPosPullMaxDeg, chestPullMaxDist;
        public int chestIkIterations, chestIkHeadRestoreSweeps;
        // Chest share of the arm-swing torso follow; the upper chest takes the remainder.
        public float chestArmSwingFactor, chestArmSwingMaxDeg, chestFollowChestShare;
        // Anatomy toggles, and the cervical lordosis curve that rides on anatCervicalLordosis.
        public bool anatDifferentialStiffness, anatShoulderSlide, anatCervicalLordosis, anatPelvicTwistRouting;
        public bool spineAnatomicalRom;
        public float lordosisPitchGainDeg, lordosisBaseDeg, lordosisNeckShare, lordosisMaxHeadPitchDeg;
        public float lordosisExtremeStartDeg, lordosisExtremeFullDeg;
        public float lordosisExtremeRollForwardMaxDeg, lordosisExtremeRollBackwardMaxDeg;
        public float lordosisExtremeHipsHorizontalMax, lordosisExtremeChestHorizontalMax;
        public float lordosisExtremeHipsDownMax, lordosisExtremeChestDownMax;
        public float lordosisExtremeHipsDownLookUp, lordosisExtremeChestDownLookUp;

        // ===== Tunables: shoulders and arms =====
        public bool shoulderSolveEnabled, shoulderShrugEnabled;
        public float shoulderElevationFactor, shoulderProtractionFactor;
        // Scapulohumeral coupling: girdle share of the humeral swing, and the clamp on the applied girdle rotation.
        public float shoulderCoupleRatio, shoulderMaxDeg;
        // Anatomical shoulder slide: past shoulderSlideStartDeg of chest yaw the girdle counter-rotates by
        // shoulderSlideFraction of the excess, capped at shoulderSlideMaxDeg.
        public float shoulderSlideStartDeg, shoulderSlideMaxDeg, shoulderSlideFraction;
        public float lowerArmTwistFraction, upperArmTwistFraction;
        public float swingSmoothRateDeg;
        public bool protectElbow, collideTrackedElbow, elbowDragEnabled, useNeuralPole;
        public float elbowDragHz;

        // ===== Tunables: legs =====
        public bool legSwivelSmoothing, kneeFootPoleHold, kneeFootPoleConditioning;
        // One Euro parameters for a knee whose pole comes from a tracker: a higher floor than the standing
        // path, and 4x the beta so real shin motion isn't lagged.
        public float trackedKneeSwivelMinCutoffHz, trackedKneeSwivelBeta, trackedKneeSwivelDerivCutoffHz;

        // ===== Tunables: collision =====
        public bool collisionsEnabled;
        public float chestRadius, collisionSkin, handRadius, handSkin;

        // ===== Solver scratch, persistent across frames =====
        public NativeArray<Vector3> chestSpringState;
        public NativeArray<int> chestSpringInit;
        public const int k_SwingLeftElbow = 0, k_SwingRightElbow = 1, k_SwingLeftKnee = 2, k_SwingRightKnee = 3, k_SwingCount = 4;
        public NativeArray<Vector3> swingLastDir, swingLastAxis, swingLastTarget;
        public NativeArray<Vector3> swingHintBend, swingHintAxis, swingHintDrag;
        public NativeArray<Quaternion> swingHintBodyRot;
        public NativeArray<int> swingContinuityInit, swingCollided, swingSmoothState, swingHintInit;
        public NativeArray<Vector3> legSwivelRaw, legSwivelSmooth;
        public NativeArray<int> legSwivelInit;
        public NativeArray<BasisLegDiagnostics> legDiagnostics;

        // ===== The pose being solved =====
        public BasisPoseStream poseStream;

        public void Execute() => ProcessAnimation(poseStream);

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
            hasChestTracker = true;
            hintWeightLeftLowerLeg = hintWeightRightLowerLeg = 1f;
            enabledSpineIK = true;
            hasHipsTracker = false;
            footIsTrackerLeftLeg = footIsTrackerRightLeg = false;
            enabledLeftLowerLeg = enabledRightLowerLeg = 1f;
            hintIsTrackerLeftLowerLeg = hintIsTrackerRightLowerLeg = false;
            ikLockMode = BasisIKLockMode.LockHead;

            hintWeightLeftHand = hintWeightRightHand = true;
            enabledLeftHand = enabledRightHand = 1f;
            enabledLeftShoulder = enabledRightShoulder = false;
            offsetRotationHead = offsetRotationLeftFoot = offsetRotationRightFoot = Quaternion.identity;
            offsetRotationLeftHand = offsetRotationRightHand = Quaternion.identity;

            playerUp = Vector3.up;

            // Avatar-measured; the rig driver overwrites both once the T-pose spine is known.
            minHeadSpineHeight = 0f;
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
            rightToeEnabled = false;

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
            maxChestDeltaDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxChestDelta.RawValue;
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
            handleHips = BindHandle(skeleton, Mapping.Hips);
            handleChest = BindHandle(skeleton, Mapping.chest);
            handleNeck = BindHandle(skeleton, Mapping.neck);
            handleHead = BindHandle(skeleton, Mapping.head);
            handleLeftUpperLeg = BindHandle(skeleton, Mapping.LeftUpperLeg);
            handleLeftLowerLeg = BindHandle(skeleton, Mapping.LeftLowerLeg);
            handleLeftFoot = BindHandle(skeleton, Mapping.leftFoot);
            handleRightUpperLeg = BindHandle(skeleton, Mapping.RightUpperLeg);
            handleRightLowerLeg = BindHandle(skeleton, Mapping.RightLowerLeg);
            handleRightFoot = BindHandle(skeleton, Mapping.rightFoot);
            handleLeftToe = BindHandle(skeleton, Mapping.leftToe);
            handleRightToe = BindHandle(skeleton, Mapping.rightToe);
            handleLeftUpperArm = BindHandle(skeleton, Mapping.leftUpperArm);
            handleLeftLowerArm = BindHandle(skeleton, Mapping.leftLowerArm);
            handleLeftHand = BindHandle(skeleton, Mapping.leftHand);
            handleRightUpperArm = BindHandle(skeleton, Mapping.RightUpperArm);
            handleRightLowerArm = BindHandle(skeleton, Mapping.RightLowerArm);
            handleRightHand = BindHandle(skeleton, Mapping.rightHand);
            handleLeftUpperArmTwist = BindHandle(skeleton, Mapping.leftUpperArmTwist);
            handleLeftLowerArmTwist = BindHandle(skeleton, Mapping.leftLowerArmTwist);
            handleRightUpperArmTwist = BindHandle(skeleton, Mapping.RightUpperArmTwist);
            handleRightLowerArmTwist = BindHandle(skeleton, Mapping.RightLowerArmTwist);
            handleSpine = BindHandle(skeleton, Mapping.spine);
            handleUpperChest = BindHandle(skeleton, Mapping.Upperchest);
            handleLeftShoulder = BindHandle(skeleton, Mapping.leftShoulder);
            handleRightShoulder = BindHandle(skeleton, Mapping.RightShoulder);

            // Baked T-pose data for shoulder solve
            tposeLeftShoulderRot = Mapping.leftShoulder != null ? Mapping.leftShoulder.rotation : Quaternion.identity;
            tposeRightShoulderRot = Mapping.RightShoulder != null ? Mapping.RightShoulder.rotation : Quaternion.identity;
            tposeChestRot = Mapping.chest != null ? Mapping.chest.rotation : Quaternion.identity;
            tposeLeftShoulderLocalDir = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? (Mapping.leftUpperArm.position - Mapping.leftShoulder.position).normalized : Vector3.left;
            tposeRightShoulderLocalDir = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? (Mapping.RightUpperArm.position - Mapping.RightShoulder.position).normalized : Vector3.right;
            // 0.6 m is an adult arm; on a small avatar it is the same shoulder-inert / shrug-latched failure
            // a stale bake produces, so the fallback tracks avatar size too.
            float fallbackArmLength = 0.6f * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            tposeShoulderToHandLeft = (Mapping.leftShoulder != null && Mapping.leftHand != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftHand.position) : fallbackArmLength;
            tposeShoulderToHandRight = (Mapping.RightShoulder != null && Mapping.rightHand != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.rightHand.position) : fallbackArmLength;
            tposeClavicleLenLeft = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftUpperArm.position) : 0f;
            tposeClavicleLenRight = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightUpperArm.position) : 0f;
            tposeShoulderToElbowLeft = (Mapping.leftShoulder != null && Mapping.leftLowerArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftLowerArm.position) : 0f;
            tposeShoulderToElbowRight = (Mapping.RightShoulder != null && Mapping.RightLowerArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightLowerArm.position) : 0f;

            // Pair each slot with its bone handle, in HumanBodyBones order.
            slotHandles.Length = Count;
            slotHandles[0] = handleHips;
            slotHandles[1] = handleLeftUpperLeg;
            slotHandles[2] = handleRightUpperLeg;
            slotHandles[3] = handleLeftLowerLeg;
            slotHandles[4] = handleRightLowerLeg;
            slotHandles[5] = handleLeftFoot;
            slotHandles[6] = handleRightFoot;
            slotHandles[7] = handleSpine;
            slotHandles[8] = handleChest;
            slotHandles[9] = handleNeck;
            slotHandles[10] = handleHead;
            slotHandles[11] = handleLeftShoulder;
            slotHandles[12] = handleRightShoulder;
            slotHandles[13] = handleLeftUpperArm;
            slotHandles[14] = handleRightUpperArm;
            slotHandles[15] = handleLeftLowerArm;
            slotHandles[16] = handleRightLowerArm;
            slotHandles[17] = handleLeftHand;
            slotHandles[18] = handleRightHand;
            slotHandles[19] = handleLeftToe;
            slotHandles[20] = handleRightToe;
            slotHandles[UpperChestSlot] = handleUpperChest;

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
            chainSpineRestFrames = new NativeArray<BasisSpineRestFrame>(n, Allocator.Persistent);
            chainSpineRoms = new NativeArray<BasisSpineRom>(n, Allocator.Persistent);
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

                chainSpineRestFrames[i] = BasisSpineAnatomy.BuildRestFrame(
                    bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                chainSpineRoms[i] = BasisSpineAnatomy.Rom(segment);
            }
        }
        public void GenerateHeadToSpine(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            var HeadToSpine = Mapping.Upperchest != null
                ? new Transform[] { Mapping.head, Mapping.neck, Mapping.Upperchest, Mapping.chest, Mapping.spine, Mapping.Hips }
                : new Transform[] { Mapping.head, Mapping.neck, Mapping.chest, Mapping.spine, Mapping.Hips };
            int SpineToHeadLength = HeadToSpine.Length;
            chainHeadToSpine = new NativeArray<BasisBoneHandle>(SpineToHeadLength, Allocator.Persistent);
            BuildSpineAnatomy(HeadToSpine, Mapping);

            for (int i = 0; i < SpineToHeadLength; i++)
            {
                chainHeadToSpine[i] = skeleton.Bind(HeadToSpine[i]);
            }
            if (Mapping.Hips != null && Mapping.head != null)
            {
                tposeLengthHeadToHips = (Mapping.head.position - Mapping.Hips.position);
            }
            else
            {
                tposeLengthHeadToHips = Vector3.zero;
            }
            if (Mapping.head != null && Mapping.neck != null)
            {
                tposeHeadToNeckLocal = Quaternion.Inverse(Mapping.head.rotation) * (Mapping.neck.position - Mapping.head.position);
            }
            else
            {
                tposeHeadToNeckLocal = Vector3.zero;
            }

            if (Mapping.Hips != null && Mapping.neck != null)
            {
                tposeLengthNeckToHips = (Mapping.neck.position - Mapping.Hips.position);
            }
            else
            {
                tposeLengthNeckToHips = tposeLengthHeadToHips;
            }

            // Record the size these were measured at, so a later rescale can carry them along.
            tposeBakeScale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        }
        public void RescaleTposeScalars(float newScale)
        {
            if (float.IsNaN(newScale) || float.IsInfinity(newScale) || newScale <= 0f || tposeBakeScale <= 0f)
            {
                return;
            }

            float k = newScale / tposeBakeScale;
            if (Mathf.Abs(k - 1f) < 1e-6f)
            {
                return;
            }

            tposeShoulderToHandLeft *= k;
            tposeShoulderToHandRight *= k;
            tposeClavicleLenLeft *= k;
            tposeClavicleLenRight *= k;
            tposeShoulderToElbowLeft *= k;
            tposeShoulderToElbowRight *= k;
            tposeLengthHeadToHips *= k;
            tposeHeadToNeckLocal *= k;
            tposeLengthNeckToHips *= k;

            tposeBakeScale = newScale;
        }
        static BasisBoneHandle BindHandle(BasisPoseSkeleton skeleton, Transform t) => (t != null) ? skeleton.Bind(t) : default;
        public void Destroy()
        {
            if (chainHeadToSpine.IsCreated) chainHeadToSpine.Dispose();
            if (chainSpineRestFrames.IsCreated) chainSpineRestFrames.Dispose();
            if (chainSpineRoms.IsCreated) chainSpineRoms.Dispose();

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
