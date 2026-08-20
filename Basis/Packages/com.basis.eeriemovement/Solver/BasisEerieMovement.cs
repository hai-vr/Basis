using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
namespace Basis.IK
{
    [Unity.Burst.BurstCompile]
    public partial struct BasisEerieMovement : Unity.Jobs.IJob
    {
        public const float k_Epsilon = 1e-5f;
        public const float k_MinMag = 1e-6f;
        public const float k_SqrEpsilon = 1e-8f;

        public const int Count = 22;
        public const int UpperChestSlot = Count - 1;
        public FixedList512Bytes<Vector3> slotPositions;
        public FixedList512Bytes<Quaternion> slotRotations;
        public FixedList512Bytes<Quaternion> slotOffsets;
        public FixedList64Bytes<bool> slotWeights;

        public BasisBoneHandle handleHips, handleSpine, handleChest, handleUpperChest, handleNeck, handleHead;
        public BasisBoneHandle handleLeftShoulder, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand;
        public BasisBoneHandle handleRightShoulder, handleRightUpperArm, handleRightLowerArm, handleRightHand;
        public BasisBoneHandle handleLeftUpperArmTwist, handleLeftLowerArmTwist;
        public BasisBoneHandle handleRightUpperArmTwist, handleRightLowerArmTwist;
        public BasisBoneHandle handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot, handleLeftToe;
        public BasisBoneHandle handleRightUpperLeg, handleRightLowerLeg, handleRightFoot, handleRightToe;

        public NativeArray<BasisBoneHandle> chainHeadToSpine;
        public NativeArray<BasisSpineRestFrame> chainSpineRestFrames;

        public int chainChestIdx;

        public Vector3 targetPositionHead, targetPositionHips;
        public Quaternion targetRotationHead, targetRotationHips, targetRotationChest;

        public Vector3 targetPositionChest, targetPositionChestRaw;
        public Vector3 playerUp;

        public Vector3 targetPositionLeftHand, hintPositionLeftHand;
        public Vector3 targetPositionRightHand, hintPositionRightHand;
        public Quaternion targetRotationLeftHand, hintRotationLeftHand;
        public Quaternion targetRotationRightHand, hintRotationRightHand;
        public Quaternion targetRotationLeftShoulder, targetRotationRightShoulder;

        public Vector3 targetPositionLeftLowerLeg, hintPositionLeftLowerLeg;
        public Vector3 targetPositionRightLowerLeg, hintPositionRightLowerLeg;
        public Quaternion targetRotationLeftLowerLeg, hintRotationLeftLowerLeg;
        public Quaternion targetRotationRightLowerLeg, hintRotationRightLowerLeg;
        public Vector3 kneeBendPrefLeft, kneeBendPrefRight, kneeAnteriorRef;
        public Quaternion leftDrivenTargetRot, rightDrivenTargetRot;
        public float leftToeBendDeg, rightToeBendDeg;
        public Vector3 leftToeBendAxis, rightToeBendAxis;

        public Quaternion offsetRotationHips, offsetRotationHead, offsetRotationChest;
        public Quaternion offsetRotationLeftFoot, offsetRotationRightFoot;
        public Quaternion offsetRotationLeftToe, offsetRotationRightToe;
        public Quaternion offsetRotationLeftShoulder, offsetRotationRightShoulder;
        public Quaternion offsetRotationLeftHand, offsetRotationRightHand;

        public float enabledLeftHand, enabledRightHand;
        public float enabledLeftLowerLeg, enabledRightLowerLeg;
        public float hintWeightLeftLowerLeg, hintWeightRightLowerLeg;
        public bool hintWeightLeftHand, hintWeightRightHand;
        public bool enabledSpineIK, enabledLeftShoulder, enabledRightShoulder;
        public bool leftToeEnabled, rightToeEnabled;
        public bool hasChestTracker, hasHipsTracker;
        public bool proneBodyPose;
        public bool hintIsTrackerLeftLowerLeg, hintIsTrackerRightLowerLeg;
        public bool footIsTrackerLeftLeg, footIsTrackerRightLeg;

        public float tposeBakeScale;
        public Vector3 tposeLengthNeckToHips, tposeHeadToNeckLocal;
        public Vector3 tposeLeftShoulderLocalDir, tposeRightShoulderLocalDir;
        public Quaternion tposeLeftShoulderRot, tposeRightShoulderRot, tposeChestRot;
        public float tposeShoulderToHandLeft, tposeShoulderToHandRight;
        public float tposeClavicleLenLeft, tposeClavicleLenRight;
        public float tposeShoulderToElbowLeft, tposeShoulderToElbowRight;

        // Arm twist binds, each pair in its own arm bone's frame: ...TwistBind is the helper's authored
        // rotation, ...ChildBind the authored rotation of the bone whose roll drives it (hand for the
        // forearm helper, forearm for the upper-arm one). Scale-free, so RescaleTpose leaves them alone.
        public Quaternion tposeLeftLowerArmTwistBind, tposeLeftLowerArmChildBind;
        public Quaternion tposeRightLowerArmTwistBind, tposeRightLowerArmChildBind;
        public Quaternion tposeLeftUpperArmTwistBind, tposeLeftUpperArmChildBind;
        public Quaternion tposeRightUpperArmTwistBind, tposeRightUpperArmChildBind;

        public BasisIKLockMode ikLockMode;
        public int spineMaxIterations;
        public float spineTolerance;
        public float minHeadSpineHeight, maxBendDeg, minFactor, maxFactor, maxChestDeltaDeg;
        public float spineBendPitch, spineBendYaw, spineBendRoll;
        public float upperChestBendPitch, upperChestBendYaw, upperChestBendRoll;
        public float spineMaxForwardDeg, spineMaxBackwardDeg, spineMaxLateralDeg;
        public float spineSquishBoost, spineGazeFollow, neckGazeFollow;

        public float neckExtensionDamp;
        public float neckFlexionDamp;
        public float spineCCDRelax, neckMaxConeDeg, spineTwistKeep, spineNeckTwistKeep;
        public float chestSpringHz, chestSpringDamping;
        public float hipHingeStartDeg, hipHingeMaxAddDeg;
        public float moveBodyBackWhenCrouching, crouchDepth, standingHeadHeight;
        public float trunkCounterbalance;

        public float trunkCounterbalanceMaxSpineFrac;

        public float thoracicBendStiffen;

        public float spineTautBandFrac;

        public float bendTwistCoupling;

        public float neckGazeFollowMaxDeg;

        public bool chestIkTarget;
        public float chestIkWeight, chestPosPullMaxDeg, chestPullMaxDist;
        public int chestIkIterations, chestIkHeadRestoreSweeps;

        public float chestArmSwingFactor, chestArmSwingMaxDeg, chestFollowChestShare;

        public bool anatDifferentialStiffness, anatShoulderSlide, anatCervicalLordosis, anatPelvicTwistRouting;
        public bool spineAnatomicalRom;
        public float lordosisPitchGainDeg, lordosisBaseDeg, lordosisNeckShare, lordosisMaxHeadPitchDeg;
        public float lordosisExtremeStartDeg, lordosisExtremeFullDeg;
        public float lordosisExtremeRollForwardMaxDeg, lordosisExtremeRollBackwardMaxDeg;
        public float lordosisExtremeHipsHorizontalMax, lordosisExtremeChestHorizontalMax;
        public float lordosisExtremeHipsHorizontalLookUp, lordosisExtremeChestHorizontalLookUp;
        public float lordosisExtremeHipsDownMax, lordosisExtremeChestDownMax;
        public float lordosisExtremeHipsDownLookUp, lordosisExtremeChestDownLookUp;

        public bool shoulderSolveEnabled, shoulderShrugEnabled;
        public float shoulderElevationFactor, shoulderProtractionFactor;

        public float shoulderCoupleRatio, shoulderMaxDeg;

        public float shoulderSlideStartDeg, shoulderSlideMaxDeg, shoulderSlideFraction;
        public float lowerArmTwistFraction, upperArmTwistFraction;
        public float swingSmoothRateDeg;
        public bool protectElbow, collideTrackedElbow, elbowDragEnabled;
        public float elbowDragHz;

        public bool legSwivelSmoothing, kneeFootPoleHold, kneeFootPoleConditioning;

        public float trackedKneeSwivelMinCutoffHz, trackedKneeSwivelBeta, trackedKneeSwivelDerivCutoffHz;

        public bool collisionsEnabled;
        public float chestRadius, collisionSkin, handRadius, handSkin;

        public NativeArray<BasisChestSpringState> chestSpring;
        public const int k_SwingLeftElbow = 0, k_SwingRightElbow = 1, k_SwingCount = 2;
        public NativeArray<BasisSwingContinuityState> swingContinuity;
        public NativeArray<BasisArmSlotState> armState;
        public NativeArray<BasisLegSlotState> legState;
        public NativeArray<BasisLegDiagnostics> legDiagnostics;

        public BasisPoseStream poseStream;

        // Declared here rather than beside its draw methods in the Gizmos partial: instance fields
        // spread across partial declarations have no defined ordering (CS0282), and this struct is
        // a Burst job payload.
        public BasisIKGizmoRecorder gizmos;

        static unsafe ref T Ref<T>(NativeArray<T> array, int index) where T : unmanaged
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index);
        }

        BasisBoneHandle SlotHandle(int slot)
        {
            switch (slot)
            {
                case 0: return handleHips;
                case 1: return handleLeftUpperLeg;
                case 2: return handleRightUpperLeg;
                case 3: return handleLeftLowerLeg;
                case 4: return handleRightLowerLeg;
                case 5: return handleLeftFoot;
                case 6: return handleRightFoot;
                case 7: return handleSpine;
                case 8: return handleChest;
                case 9: return handleNeck;
                case 10: return handleHead;
                case 11: return handleLeftShoulder;
                case 12: return handleRightShoulder;
                case 13: return handleLeftUpperArm;
                case 14: return handleRightUpperArm;
                case 15: return handleLeftLowerArm;
                case 16: return handleRightLowerArm;
                case 17: return handleLeftHand;
                case 18: return handleRightHand;
                case 19: return handleLeftToe;
                case 20: return handleRightToe;
                case UpperChestSlot: return handleUpperChest;
                default: return BasisBoneHandle.Unbound;
            }
        }

        public void Execute() => ProcessAnimation();

        static readonly ProfilerMarker sMarkerSpinePass = new ProfilerMarker("BasisEerie.Spine");
        static readonly ProfilerMarker sMarkerShoulderPass = new ProfilerMarker("BasisEerie.Shoulders");
        static readonly ProfilerMarker sMarkerLegPass = new ProfilerMarker("BasisEerie.Legs");
        static readonly ProfilerMarker sMarkerArmPass = new ProfilerMarker("BasisEerie.Arms");
        static readonly ProfilerMarker sMarkerToePass = new ProfilerMarker("BasisEerie.Toes");
        static readonly ProfilerMarker sMarkerOverrides = new ProfilerMarker("BasisEerie.TrackerOverrides");

        public void ProcessAnimation()
        {
            poseStream.InvalidateWorldCache();
            RecordTargetGizmos();
            sMarkerSpinePass.Begin();
            SolveSpinePass();
            sMarkerSpinePass.End();
            RecordSpineGizmos();
            sMarkerShoulderPass.Begin();
            SolveShoulderPass();
            sMarkerShoulderPass.End();
            RecordShoulderGizmos();
            sMarkerLegPass.Begin();
            SolveLegPass();
            sMarkerLegPass.End();
            RecordLegGizmos();
            sMarkerArmPass.Begin();
            SolveArmPass();
            sMarkerArmPass.End();
            RecordArmGizmos();
            sMarkerToePass.Begin();
            SolveToePass();
            sMarkerToePass.End();
            RecordToeGizmos();
            sMarkerOverrides.Begin();
            ApplyTrackerOverrides();
            sMarkerOverrides.End();
            RecordOverrideGizmos();
            RecordFrameGizmos();
            RecordLimitGizmos();
            RecordReachGizmos();
            RecordNumberGizmos();
            RecordSkeletonGizmos();
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
            tposeHeadToNeckLocal *= k;
            tposeLengthNeckToHips *= k;

            tposeBakeScale = newScale;
        }
        public void Destroy()
        {
            if (chainHeadToSpine.IsCreated) chainHeadToSpine.Dispose();
            if (chainSpineRestFrames.IsCreated) chainSpineRestFrames.Dispose();
            if (chestSpring.IsCreated) chestSpring.Dispose();
            if (swingContinuity.IsCreated) swingContinuity.Dispose();
            if (armState.IsCreated) armState.Dispose();
            if (legState.IsCreated) legState.Dispose();
            if (legDiagnostics.IsCreated) legDiagnostics.Dispose();

            gizmos.Dispose();
        }
    }

    public struct BasisChestSpringState
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public bool Seeded;
    }

    public struct BasisArmSlotState
    {
        public Vector3 HintBend, HintAxis, HintDrag;
        public Quaternion HintBodyRot;
        public float HintReach;
        public bool HintSeeded;
        public Vector3 PoleDir;
        public Quaternion PoleRot;
        public bool PoleValid;
        public int Collided;
        public int GuardSide;
    }

    public struct BasisLegSlotState
    {
        public BasisSwivelFilterState Swivel;
        public bool SwivelSeeded;
    }
}
