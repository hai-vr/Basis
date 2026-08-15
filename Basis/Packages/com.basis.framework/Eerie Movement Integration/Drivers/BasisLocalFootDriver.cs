using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public partial class BasisLocalFootDriver
{
    [Header("Ground Detection")]
    private LayerMask groundLayers;

    [Header("Prediction")]
    [Tooltip("How far ahead (in step-durations) to predict the step target.")]
    [SerializeField, Range(0.3f, 2.0f)]
    private float predictionFactor = 0.9f;
    [Tooltip("Velocity bias on ideal position (fraction of velocity applied as forward offset).")]
    [SerializeField, Range(0.0f, 0.5f)]
    private float velocityBiasFactor = 0.18f;
    [Tooltip("Lead offset multiplier for the stepping foot (fraction of velocityBiasFactor).")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float leadOffsetFactor = 0.6f;
    [Tooltip("Max velocity offset as fraction of leg length.")]
    [SerializeField, Range(0.1f, 0.6f)]
    private float maxVelocityOffsetFraction = 0.35f;
    [Tooltip("Max step prediction distance as fraction of leg length.")]
    [SerializeField, Range(0.1f, 0.6f)]
    private float maxPredictionFraction = 0.35f;

    [Header("Smoothing")]
    [SerializeField, Range(5f, 60f)]
    private float plantedLerpSpeed = 40f;
    [SerializeField, Range(5f, 40f)]
    private float rotationLerpSpeed = 16f;
    [Tooltip("Velocity smoothing rate when accelerating.")]
    [SerializeField, Range(1f, 30f)]
    private float velocitySmoothAccel = 25f;
    [Tooltip("Velocity smoothing rate when decelerating.")]
    [SerializeField, Range(10f, 100f)]
    private float velocitySmoothDecel = 50f;
    [Tooltip("Body forward smoothing rate when moving.")]
    [SerializeField, Range(1f, 20f)]
    private float bodyFwdRateMoving = 6f;
    [Tooltip("Body forward smoothing rate when stationary.")]
    [SerializeField, Range(0.5f, 10f)]
    private float bodyFwdRateStationary = 2.5f;
    [Tooltip("Knee hint lerp speed.")]
    [SerializeField, Range(1f, 30f)]
    private float kneeHintLerpSpeed = 10f;

    [Header("Foot Rotation Limits")]
    [Tooltip("Max slope tilt (ankle roll/pitch).")]
    [SerializeField, Range(0f, 60f)]
    private float maxFootTiltDegrees = 35f;
    [Tooltip("Max yaw deviation from body forward (toe-out / toe-in). Humans ~15-20 deg.")]
    [SerializeField, Range(0f, 45f)]
    private float maxFootYawDegrees = 18f;

    [Header("Step Proportions (multipliers for avatar-scaled derivation)")]
    [Tooltip("Ray sphere radius as fraction of foot length.")]
    [SerializeField, Range(0.1f, 0.6f)]
    private float raySphereRadiusMul = 0.3f;
    [Tooltip("Foot height offset as fraction of ankle height.")]
    [SerializeField, Range(0.05f, 0.5f)]
    private float footHeightOffsetMul = 0.2f;
    [Tooltip("Step trigger distance as fraction of avg leg length.")]
    [SerializeField, Range(0.02f, 0.2f)]
    private float stepTriggerMul = 0.18f;
    [Tooltip("Stride scale as fraction of avg leg length.")]
    [SerializeField, Range(0.02f, 0.25f)]
    private float strideScaleMul = 0.15f;
    [Tooltip("Step height as fraction of avg shin length.")]
    [SerializeField, Range(0.05f, 0.4f)]
    private float stepHeightMul = 0.30f;
    [Tooltip("Slow step duration as fraction of pendulum period.")]
    [SerializeField, Range(0.1f, 0.6f)]
    private float stepDurSlowMul = 0.30f;
    [Tooltip("Fast step duration as fraction of pendulum period.")]
    [SerializeField, Range(0.05f, 0.4f)]
    private float stepDurFastMul = 0.18f;
    [Tooltip("Fast speed reference multiplier.")]
    [SerializeField, Range(0.5f, 2.5f)]
    private float fastSpeedMul = 1.2f;

    [Header("Step Arc Shape")]
    [Tooltip("Step arc lift exponent (controls how quickly foot rises).")]
    [SerializeField, Range(0.2f, 1.5f)]
    private float stepArcLiftExp = 0.6f;
    [Tooltip("Step arc drop exponent (controls how quickly foot falls).")]
    [SerializeField, Range(0.5f, 3.0f)]
    private float stepArcDropExp = 1.4f;
    [Tooltip("Min dynamic step height at slow speed (fraction of max).")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float stepHeightMinFraction = 0.4f;
    [Tooltip("Stride length (fraction of leg) at which step lift reaches its full height.")]
    [SerializeField, Range(0.2f, 0.8f)]
    private float stepHeightStrideRefFraction = 0.45f;

    [Header("Idle Behavior")]
    [Tooltip("Speed below which player is considered idle.")]
    [SerializeField, Range(0.01f, 0.2f)]
    private float idleSpeedThreshold = 0.05f;
    [Tooltip("Extra step trigger distance when idle (fraction of stepTriggerDist).")]
    [SerializeField, Range(0.0f, 1.5f)]
    private float idleBoostFraction = 0.5f;
    [Tooltip("Max body yaw since plant before triggering a step (degrees). Also sets the yaw rate at which steps go full-fast (fastYawRef = 0.5 * this / stepDurFast).")]
    [SerializeField, Range(10f, 90f)]
    private float maxPlantedYawDegrees = 20f;

    [Header("Side Enforcement")]
    [Tooltip("Ideal position side enforcement as fraction of half stance.")]
    [SerializeField, Range(0.1f, 0.6f)]
    private float idealSideEnforceFraction = 0.3f;
    [Tooltip("Step target side enforcement as fraction of stance width.")]
    [SerializeField, Range(0.05f, 0.4f)]
    private float stepTargetSideFraction = 0.15f;
    [Tooltip("Final foot side enforcement as fraction of half stance.")]
    [SerializeField, Range(0.05f, 0.5f)]
    private float footSideEnforceFraction = 0.2f;

    [Header("Vertical Correction")]
    [Tooltip("Max vertical drift before feet snap (fraction of hipToFoot).")]
    [SerializeField, Range(0.1f, 0.5f)]
    private float maxVerticalDriftFraction = 0.25f;

    [Header("Knee Hints")]
    [Tooltip("Knee forward push as fraction of avg thigh length.")]
    [SerializeField, Range(0.1f, 0.8f)]
    private float kneeForwardPushFraction = 0.4f;
    [Tooltip("Knee min side enforcement as fraction of half stance.")]
    [SerializeField, Range(0.01f, 0.2f)]
    private float kneeMinSideFraction = 0.05f;

    [Header("Body Forward Weights")]
    [Tooltip("Hips weight in body forward computation.")]
    [SerializeField, Range(0f, 5f)]
    private float bodyFwdHipsWeight = 3f;
    [Tooltip("Chest weight in body forward computation.")]
    [SerializeField, Range(0f, 5f)]
    private float bodyFwdChestWeight = 2f;
    [Tooltip("Head weight in body forward computation.")]
    [SerializeField, Range(0f, 5f)]
    private float bodyFwdHeadWeight = 1f;

    [Header("Hip Bob")]
    [Tooltip("Max hip bob amplitude as fraction of hipToFoot.")]
    [SerializeField, Range(0.0f, 0.1f)]
    private float hipBobFraction = 0.02f;

    [Header("Calibrated (read-only, from T-pose)")]
    [SerializeField] private float stanceWidth;
    [SerializeField] private float hipToFoot;
    [SerializeField] private float leftLegLen;
    [SerializeField] private float rightLegLen;
    [SerializeField] private float leftThighLen;
    [SerializeField] private float leftShinLen;
    [SerializeField] private float rightThighLen;
    [SerializeField] private float rightShinLen;
    [SerializeField] private float footLength;
    [SerializeField] private float ankleHeight;
    [SerializeField] private float upperLegToFootVertical;

    private float baseStanceWidth;
    private float baseHipToFoot;
    private float baseLeftThighLen, baseLeftShinLen, baseLeftLegLen;
    private float baseRightThighLen, baseRightShinLen, baseRightLegLen;
    private float baseFootLength;
    private float baseAnkleHeight;
    private float baseUpperLegToFootVertical;

    [Header("Derived Step Parameters (read-only)")]
    [SerializeField] private float stepTriggerDist;
    [SerializeField] private float strideScale;
    [SerializeField] private float stepHeightCalc;
    [SerializeField] private float stepDurSlow;
    [SerializeField] private float stepDurFast;
    [SerializeField] private float raySphereRadius;
    [SerializeField] private float footHeightOffset;
    [SerializeField] private float fastSpeedRef;

    private Transform avatarTransform;
    private Transform hips;
    private BasisFootState left;
    private BasisFootState right;
    private float rayCastRange;
    private Quaternion footAlignLeft = Quaternion.identity;
    private Quaternion footAlignRight = Quaternion.identity;
    private Collider _selfCollider;
    private Transform _selfRoot;
    private Vector3 cachedPlayerUp = Vector3.up;
    private Vector3 cachedPlayerFwd = Vector3.forward;
    private Vector3 cachedPlayerRight = Vector3.right;

    private Vector3 smoothedVelocity;
    private float prevHeadYaw;

    private NativeArray<BasisFootNativeState> _nativeFeet;
    private NativeArray<BasisFootSimState> _nativeSimState;
    private NativeArray<BasisFootSimInput> _nativeInput;
    private NativeArray<BasisFootSimOutput> _nativeOutput;
    private JobHandle _jobHandle;
    private bool _jobScheduled;

    private const int k_ProbeRays = 4;
    private const int k_ProbeMaxHits = 8;
    private NativeArray<RaycastCommand> _probeCommands;
    private NativeArray<RaycastHit> _probeResults;
    private JobHandle _probeHandle;
    private bool _probePending;
    private int _probeNextFoot;
    private float _probeElapsedLeft, _probeElapsedRight;

    private struct BasisFootProbePlan
    {
        public int foot;
        public Vector3 up, fwd, right;
        public float heelD, ballD, toeD, halfW;
        public float hipsUpComp;
    }
    private BasisFootProbePlan _probePlan;

    private BasisFootSimParams _cachedParams;
    private bool _paramsDirty = true;

    public static float SplayWhenCrouchedPercentage = 1f;

    internal const float SettledPlantedTime = 10f;

    public bool IsInitialized { get; private set; }

    public void NotifyReEngaging()
    {
        DiscardPendingProbes();
        var lf = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData;
        left.currentPos = left.plantedPos = lf.position;
        left.phase = BasisFootPhase.Planted;
        var rf = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData;
        right.currentPos = right.plantedPos = rf.position;
        right.phase = BasisFootPhase.Planted;

        left.plantedTime = right.plantedTime = SettledPlantedTime;

        if (left.bone != null) left.currentRot = left.plantedRot = left.stepStartRot = left.bone.rotation;
        if (right.bone != null) right.currentRot = right.plantedRot = right.stepStartRot = right.bone.rotation;

        left.plantedBodyFwd = Vector3.zero;
        right.plantedBodyFwd = Vector3.zero;

        if (_nativeFeet.IsCreated)
        {
            _nativeFeet[0] = FootStateToNative(left, _nativeFeet[0]);
            _nativeFeet[1] = FootStateToNative(right, _nativeFeet[1]);
        }
    }

    public void Teleport(Vector3 delta)
    {
        if (!IsInitialized)
        {
            return;
        }
        if (_jobScheduled)
        {
            _jobHandle.Complete();
            _jobScheduled = false;
        }
        DiscardPendingProbes();

        ShiftFoot(left, delta);
        ShiftFoot(right, delta);

        if (_nativeFeet.IsCreated)
        {
            _nativeFeet[0] = FootStateToNative(left, _nativeFeet[0]);
            _nativeFeet[1] = FootStateToNative(right, _nativeFeet[1]);
        }
        if (_nativeSimState.IsCreated)
        {
            var sim = _nativeSimState[0];
            sim.prevHeadPos += (float3)delta;
            _nativeSimState[0] = sim;
        }
    }

    private static void ShiftFoot(BasisFootState f, Vector3 delta)
    {
        f.plantedPos += delta;
        f.currentPos += delta;
        f.idealPos += delta;
        f.stepStartPos += delta;
        f.stepTargetPos += delta;
        f.kneeHint += delta;
    }

    public Vector3 LeftFootPosition => left.currentPos;
    public Quaternion LeftFootRotation => left.currentRot;
    public Vector3 RightFootPosition => right.currentPos;
    public Quaternion RightFootRotation => right.currentRot;
    public Vector3 LeftKneeHint => left.kneeHint;
    public Vector3 RightKneeHint => right.kneeHint;
    public Vector3 LeftPlantedPos => left.plantedPos;
    public Vector3 RightPlantedPos => right.plantedPos;
    public float LeftStepTimer => left.stepTimer;
    public float RightStepTimer => right.stepTimer;
    public bool LastGroundHit { get; private set; }
    public float LastGroundUp { get; private set; }
    public float HipsUp { get; private set; }

    private enum BasisFootPhase { Planted, Stepping }
    public void InitializeVariables()
    {
        BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;

        avatarTransform = BasisLocalPlayer.Instance.AvatarTransform;
        var mapping = BasisLocalAvatarDriver.Mapping;
        hips = mapping.Hips;

        var lf = mapping.leftFoot;
        var rf = mapping.rightFoot;
        left = new BasisFootState("Left", lf, -1);
        right = new BasisFootState("Right", rf, +1);

        CaptureFootAlignment(lf, rf);

        left.thigh = mapping.HasLeftUpperLeg ? mapping.LeftUpperLeg : (lf != null ? lf.parent != null ? lf.parent.parent : null : null);
        left.shin = mapping.HasLeftLowerLeg ? mapping.LeftLowerLeg : (lf != null ? lf.parent : null);
        right.thigh = mapping.HasRightUpperLeg ? mapping.RightUpperLeg : (rf != null ? rf.parent != null ? rf.parent.parent : null : null);
        right.shin = mapping.HasRightLowerLeg ? mapping.RightLowerLeg : (rf != null ? rf.parent : null);

        var cc = BasisLocalPlayer.Instance.LocalCharacterDriver.characterController;
        int ccLayer = cc.gameObject.layer;
        _selfCollider = cc;
        _selfRoot = BasisLocalPlayer.Instance.transform;

        int mask = 0;
        for (int Index = 0; Index < 32; Index++)
        {
            if (Physics.GetIgnoreLayerCollision(ccLayer, Index))
            {
                continue;
            }
            mask |= (1 << Index);
        }

        groundLayers = mask;

        MeasureFromCalibration(mapping);
        StoreBaseMeasurements();

        DeriveStepParameters();

        left.thighLen = leftThighLen;
        left.shinLen = leftShinLen;
        left.legLength = leftLegLen;
        right.thighLen = rightThighLen;
        right.shinLen = rightShinLen;
        right.legLength = rightLegLen;

        rayCastRange = Mathf.Max(hipToFoot + ankleHeight, Mathf.Max(leftLegLen, rightLegLen)) * 2.15f;

        Matrix4x4 ltw = BasisLocalPlayer.localToWorldMatrix;
        cachedPlayerUp = ltw.MultiplyVector(Vector3.up).normalized;
        cachedPlayerFwd = ltw.MultiplyVector(Vector3.forward).normalized;
        cachedPlayerRight = ltw.MultiplyVector(Vector3.right).normalized;

        InitPose(left);
        InitPose(right);

        var hc = BasisLocalBoneDriver.HeadControl;
        Vector3 headPos = hc.OutgoingWorldData.position;
        Vector3 bodyFwd = BasisLocalPose.GetRotation(BasisPoseSlot.AvatarRoot, avatarTransform) * Vector3.forward;
        Vector3 bodyRight = Vector3.Cross(cachedPlayerUp, bodyFwd).normalized;

        DisposeNativeArrays();
        _nativeFeet = new NativeArray<BasisFootNativeState>(2, Allocator.Persistent);
        _nativeSimState = new NativeArray<BasisFootSimState>(1, Allocator.Persistent);
        _nativeInput = new NativeArray<BasisFootSimInput>(1, Allocator.Persistent);
        _nativeOutput = new NativeArray<BasisFootSimOutput>(1, Allocator.Persistent);
        _probeCommands = new NativeArray<RaycastCommand>(k_ProbeRays, Allocator.Persistent);
        _probeResults = new NativeArray<RaycastHit>(k_ProbeRays * k_ProbeMaxHits, Allocator.Persistent);
        _probePending = false;
        _probeNextFoot = 0;
        _probeElapsedLeft = _probeElapsedRight = 0f;

        prevHeadYaw = HeadYaw();
        _nativeSimState[0] = new BasisFootSimState
        {
            prevHeadPos = headPos,
            prevHeadYaw = prevHeadYaw,
            smoothedVelocity = float3.zero,
            smoothedBodyFwd = bodyFwd,
            smoothedBodyRight = bodyRight,
        };

        _nativeFeet[0] = FootStateToNative(left, _nativeFeet[0]);
        _nativeFeet[1] = FootStateToNative(right, _nativeFeet[1]);

        BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;
        IsInitialized = true;
    }

    public void Dispose()
    {
        BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;
        if (_jobScheduled)
        {
            _jobHandle.Complete();
            _jobScheduled = false;
        }
        DiscardPendingProbes();
        DisposeNativeArrays();
        IsInitialized = false;
    }

    private void DiscardPendingProbes()
    {
        if (_probePending)
        {
            _probeHandle.Complete();
            _probePending = false;
        }
    }

    private void DisposeNativeArrays()
    {
        DiscardPendingProbes();
        if (_nativeFeet.IsCreated) _nativeFeet.Dispose();
        if (_nativeSimState.IsCreated) _nativeSimState.Dispose();
        if (_nativeInput.IsCreated) _nativeInput.Dispose();
        if (_nativeOutput.IsCreated) _nativeOutput.Dispose();
        if (_probeCommands.IsCreated) _probeCommands.Dispose();
        if (_probeResults.IsCreated) _probeResults.Dispose();
    }

    private static BasisFootNativeState FootStateToNative(BasisFootState f, in BasisFootNativeState prev)
    {
        return new BasisFootNativeState
        {
            sideSign = f.sideSign,
            thighLen = f.thighLen,
            shinLen = f.shinLen,
            legLength = f.legLength,
            phase = f.phase == BasisFootPhase.Planted ? 0 : 1,
            plantedPos = f.plantedPos,
            plantedRot = f.plantedRot,
            plantedBodyFwd = f.plantedBodyFwd,
            stepStartPos = f.stepStartPos,
            stepTargetPos = f.stepTargetPos,
            stepStartRot = f.stepStartRot,
            stepTimer = f.stepTimer,
            stepDur = f.stepDur,
            plantedTime = f.plantedTime,
            idealPos = f.idealPos,
            filteredNormal = f.filteredNormal,
            currentPos = f.currentPos,
            currentRot = f.currentRot,
            kneeHint = f.kneeHint,
            // Native-only state (no managed mirror): preserve across rebuilds. Zeroing landRot fed
            // slerp((0,0,0,0), plantedRot) in the planted flat-blend after a Teleport/height change.
            toeBendDeg = prev.toeBendDeg,
            toeBendAxis = prev.toeBendAxis,
            stepArcScale = prev.stepArcScale,
            landRot = math.lengthsq(prev.landRot.value) > 0.5f ? prev.landRot : (quaternion)f.currentRot,
        };
    }

    private void NativeToFootState(in BasisFootNativeState n, BasisFootState f)
    {
        f.plantedPos = n.plantedPos;
        f.plantedRot = n.plantedRot;
        f.plantedBodyFwd = n.plantedBodyFwd;
        f.stepStartPos = n.stepStartPos;
        f.stepTargetPos = n.stepTargetPos;
        f.stepStartRot = n.stepStartRot;
        f.stepTimer = n.stepTimer;
        f.stepDur = n.stepDur;
        f.plantedTime = n.plantedTime;
        f.idealPos = n.idealPos;
        f.filteredNormal = n.filteredNormal;
        f.currentPos = n.currentPos;
        f.currentRot = n.currentRot;
        f.kneeHint = n.kneeHint;
        f.phase = n.phase == 0 ? BasisFootPhase.Planted : BasisFootPhase.Stepping;
    }

    private BasisFootSimParams BuildParams()
    {
        return new BasisFootSimParams
        {
            predictionFactor = predictionFactor,
            velocityBiasFactor = velocityBiasFactor,
            leadOffsetFactor = leadOffsetFactor,
            maxVelocityOffsetFraction = maxVelocityOffsetFraction,
            maxPredictionFraction = maxPredictionFraction,
            plantedLerpSpeed = plantedLerpSpeed,
            rotationLerpSpeed = rotationLerpSpeed,
            velocitySmoothAccel = velocitySmoothAccel,
            velocitySmoothDecel = velocitySmoothDecel,
            bodyFwdRateMoving = bodyFwdRateMoving,
            bodyFwdRateStationary = bodyFwdRateStationary,
            kneeHintLerpSpeed = kneeHintLerpSpeed,
            maxFootTiltDegrees = maxFootTiltDegrees,
            maxFootYawDegrees = maxFootYawDegrees,
            stepArcLiftExp = stepArcLiftExp,
            stepArcDropExp = stepArcDropExp,
            stepHeightMinFraction = stepHeightMinFraction,
            stepHeightStrideRefFraction = stepHeightStrideRefFraction,

            idleSpeedThreshold = idleSpeedThreshold * (fastSpeedRef / 2.921f),
            idleBoostFraction = idleBoostFraction,
            maxPlantedYawDegrees = maxPlantedYawDegrees,
            idealSideEnforceFraction = idealSideEnforceFraction,
            stepTargetSideFraction = stepTargetSideFraction,
            footSideEnforceFraction = footSideEnforceFraction,
            maxVerticalDriftFraction = maxVerticalDriftFraction,
            kneeForwardPushFraction = kneeForwardPushFraction,
            kneeMinSideFraction = kneeMinSideFraction,
            bodyFwdHipsWeight = bodyFwdHipsWeight,
            bodyFwdChestWeight = bodyFwdChestWeight,
            bodyFwdHeadWeight = bodyFwdHeadWeight,
            hipBobFraction = hipBobFraction,
            stanceWidth = stanceWidth,
            hipToFoot = hipToFoot,
            leftLegLen = leftLegLen,
            rightLegLen = rightLegLen,
            leftThighLen = leftThighLen,
            leftShinLen = leftShinLen,
            rightThighLen = rightThighLen,
            rightShinLen = rightShinLen,
            footLength = footLength,
            ankleHeight = ankleHeight,
            footAlignLeft = footAlignLeft,
            footAlignRight = footAlignRight,
            stepTriggerDist = stepTriggerDist,
            strideScale = strideScale,
            stepHeightCalc = stepHeightCalc,
            stepDurSlow = stepDurSlow,
            stepDurFast = stepDurFast,
            raySphereRadius = raySphereRadius,
            footHeightOffset = footHeightOffset,
            fastSpeedRef = fastSpeedRef,
            rayCastRange = rayCastRange,
        };
    }
    private void MeasureFromCalibration(BasisTransformMapping mapping)
    {
        var tpose = mapping.TposeWorld;
        bool hasHips = TryTP(tpose, HumanBodyBones.Hips, out Vector3 tH);
        bool hasLUL = TryTP(tpose, HumanBodyBones.LeftUpperLeg, out Vector3 tLUL);
        bool hasRUL = TryTP(tpose, HumanBodyBones.RightUpperLeg, out Vector3 tRUL);
        bool hasLLL = TryTP(tpose, HumanBodyBones.LeftLowerLeg, out Vector3 tLLL);
        bool hasRLL = TryTP(tpose, HumanBodyBones.RightLowerLeg, out Vector3 tRLL);
        bool hasLF = TryTP(tpose, HumanBodyBones.LeftFoot, out Vector3 tLF);
        bool hasRF = TryTP(tpose, HumanBodyBones.RightFoot, out Vector3 tRF);
        bool hasLT = TryTP(tpose, HumanBodyBones.LeftToes, out Vector3 tLT);
        bool hasRT = TryTP(tpose, HumanBodyBones.RightToes, out Vector3 tRT);

        if (hasLF && hasRF)
        {
            Vector3 d = tRF - tLF; d.y = 0f;
            stanceWidth = d.magnitude;
        }
        else
        {
            FallbackStanceWidth();
        }

        if (hasHips && hasLF && hasRF)
        {
            hipToFoot = Mathf.Abs(tH.y - (tLF.y + tRF.y) * 0.5f);
        }
        else
        {
            FallbackHipToFoot();
        }

        if (hasLUL && hasLLL && hasLF)
        {
            leftThighLen = Vector3.Distance(tLUL, tLLL);
            leftShinLen = Vector3.Distance(tLLL, tLF);
        }
        else if (hasHips && hasLF)
        {
            float t = Vector3.Distance(tH, tLF);
            leftThighLen = t * 0.55f;
            leftShinLen = t * 0.45f;
        }
        else
        {
            FallbackLegLens(true);
        }

        leftLegLen = leftThighLen + leftShinLen;

        if (hasRUL && hasRLL && hasRF)
        {
            rightThighLen = Vector3.Distance(tRUL, tRLL);
            rightShinLen = Vector3.Distance(tRLL, tRF);
        }
        else if (hasHips && hasRF)
        {
            float t = Vector3.Distance(tH, tRF);
            rightThighLen = t * 0.55f;
            rightShinLen = t * 0.45f;
        }
        else
        {
            FallbackLegLens(false);
        }

        rightLegLen = rightThighLen + rightShinLen;

        if (hasLF && hasLT && hasRF && hasRT)
        {
            footLength = (Vector3.Distance(tLF, tLT) + Vector3.Distance(tRF, tRT)) * 0.5f;
        }
        else if (hasLF && hasLT)
        {
            footLength = Vector3.Distance(tLF, tLT);
        }
        else if (hasRF && hasRT)
        {
            footLength = Vector3.Distance(tRF, tRT);
        }
        else
        {
            footLength = hipToFoot * 0.15f;
        }

        if (hasLF && hasRF)
        {
            float avgFootY = (tLF.y + tRF.y) * 0.5f;

            if (hasLT || hasRT)
            {
                float groundRefY;
                if (hasLT && hasRT)
                    groundRefY = (tLT.y + tRT.y) * 0.5f;
                else if (hasLT)
                    groundRefY = tLT.y;
                else
                    groundRefY = tRT.y;

                ankleHeight = Mathf.Max(0.01f, avgFootY - groundRefY);
            }
            else
            {
                ankleHeight = Mathf.Max(0.01f, hipToFoot * 0.05f);
            }
        }
        else
        {
            ankleHeight = Mathf.Max(0.01f, hipToFoot * 0.05f);
        }

        if (hasLUL && hasRUL && hasLF && hasRF)
        {
            float leftV = Mathf.Abs(tLUL.y - tLF.y);
            float rightV = Mathf.Abs(tRUL.y - tRF.y);
            upperLegToFootVertical = (leftV + rightV) * 0.5f;
        }
        else
        {
            upperLegToFootVertical = hipToFoot;
        }

        stanceWidth = Mathf.Max(0.04f, stanceWidth);
        hipToFoot = Mathf.Max(0.15f, hipToFoot);
        leftLegLen = Mathf.Max(0.15f, leftLegLen);
        rightLegLen = Mathf.Max(0.15f, rightLegLen);
        footLength = Mathf.Max(0.02f, footLength);
        ankleHeight = Mathf.Max(0.005f, ankleHeight);
    }

    private void DeriveStepParameters()
    {
        float avgLeg = (leftLegLen + rightLegLen) * 0.5f;
        float avgShin = (leftShinLen + rightShinLen) * 0.5f;

        const float k_RefLeg = 0.87f;
        float pendulum = Mathf.PI * Mathf.Sqrt(avgLeg / 9.81f);
        float speedRef = Mathf.Sqrt(avgLeg * 9.81f);

        raySphereRadius = Mathf.Clamp(footLength * raySphereRadiusMul, avgLeg * (0.02f / k_RefLeg), avgLeg * (0.12f / k_RefLeg));

        float desiredOffset = ankleHeight * footHeightOffsetMul;
        float straightLegLimit = upperLegToFootVertical + ankleHeight - avgLeg;
        footHeightOffset = Mathf.Clamp(Mathf.Min(desiredOffset, straightLegLimit), avgLeg * (0.001f / k_RefLeg), avgLeg * (0.05f / k_RefLeg));

        stepTriggerDist = Mathf.Clamp(avgLeg * stepTriggerMul, avgLeg * (0.04f / k_RefLeg), avgLeg * (0.18f / k_RefLeg));

        strideScale = Mathf.Clamp(avgLeg * strideScaleMul, avgLeg * (0.02f / k_RefLeg), avgLeg * (0.22f / k_RefLeg));

        stepHeightCalc = Mathf.Clamp(avgShin * stepHeightMul, avgLeg * (0.03f / k_RefLeg), avgLeg * (0.20f / k_RefLeg));

        stepDurSlow = Mathf.Clamp(pendulum * stepDurSlowMul, pendulum * (0.10f / 0.9356f), pendulum * (0.30f / 0.9356f));
        stepDurFast = Mathf.Clamp(pendulum * stepDurFastMul, pendulum * (0.06f / 0.9356f), pendulum * (0.18f / 0.9356f));

        fastSpeedRef = Mathf.Clamp(fastSpeedMul * speedRef, speedRef * (1.0f / 2.921f), speedRef * 2.5f);

        _paramsDirty = true;
    }
    private void StoreBaseMeasurements()
    {
        baseStanceWidth = stanceWidth;
        baseHipToFoot = hipToFoot;
        baseLeftThighLen = leftThighLen;
        baseLeftShinLen = leftShinLen;
        baseLeftLegLen = leftLegLen;
        baseRightThighLen = rightThighLen;
        baseRightShinLen = rightShinLen;
        baseRightLegLen = rightLegLen;
        baseFootLength = footLength;
        baseAnkleHeight = ankleHeight;
        baseUpperLegToFootVertical = upperLegToFootVertical;
    }

    public void RefreshBodyFitScale()
    {
        if (!IsInitialized)
        {
            return;
        }
        ApplyScaleToMeasurements(BasisHeightDriver.ScaledToMatchValue);
    }

    private static float BodyFitLegScale()
    {
        var fit = BasisLocalRigDriver.AppliedBodyFit;
        if (!fit.HasBodyFit)
        {
            return 1f;
        }
        float legScale = fit.LegScale;
        return legScale > 0f && !float.IsNaN(legScale) && !float.IsInfinity(legScale) ? legScale : 1f;
    }

    private void ApplyScaleToMeasurements(float scale)
    {
        float legScale = scale * BodyFitLegScale();
        stanceWidth = baseStanceWidth * scale;
        hipToFoot = baseHipToFoot * legScale;
        leftThighLen = baseLeftThighLen * legScale;
        leftShinLen = baseLeftShinLen * legScale;
        leftLegLen = baseLeftLegLen * legScale;
        rightThighLen = baseRightThighLen * legScale;
        rightShinLen = baseRightShinLen * legScale;
        rightLegLen = baseRightLegLen * legScale;
        footLength = baseFootLength * scale;
        ankleHeight = baseAnkleHeight * scale;
        upperLegToFootVertical = baseUpperLegToFootVertical * legScale;

        DeriveStepParameters();
        left.thighLen = leftThighLen;
        left.shinLen = leftShinLen;
        left.legLength = leftLegLen;
        right.thighLen = rightThighLen;
        right.shinLen = rightShinLen;
        right.legLength = rightLegLen;

        rayCastRange = Mathf.Max(hipToFoot + ankleHeight, Mathf.Max(leftLegLen, rightLegLen)) * 2.15f;
        _paramsDirty = true;

        if (_nativeFeet.IsCreated)
        {
            var ln = _nativeFeet[0]; ln.thighLen = leftThighLen; ln.shinLen = leftShinLen; ln.legLength = leftLegLen; _nativeFeet[0] = ln;
            var rn = _nativeFeet[1]; rn.thighLen = rightThighLen; rn.shinLen = rightShinLen; rn.legLength = rightLegLen; _nativeFeet[1] = rn;
        }
    }
    private void OnHeightChanged(BasisHeightDriver.HeightModeChange mode)
    {
        if (!IsInitialized)
        {
            return;
        }

        DiscardPendingProbes();
        ApplyScaleToMeasurements(BasisHeightDriver.ScaledToMatchValue);

        ReSnapFoot(left);
        ReSnapFoot(right);

        if (_nativeFeet.IsCreated)
        {
            _nativeFeet[0] = FootStateToNative(left, _nativeFeet[0]);
            _nativeFeet[1] = FootStateToNative(right, _nativeFeet[1]);
        }
    }

    private void ReSnapFoot(BasisFootState f)
    {
        if (f.bone == null) return;

        Vector3 origin = f.bone.position + cachedPlayerUp * (hipToFoot * 0.33f);
        if (GroundCast(origin, -cachedPlayerUp, rayCastRange, 0f, Vector3.Dot(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), cachedPlayerUp), out RaycastHit hit))
        {
            Vector3 snapped = hit.point + hit.normal * footHeightOffset;
            f.currentPos = f.plantedPos = f.idealPos = snapped;
            f.filteredNormal = hit.normal;
        }
    }

    public void Simulate(float dt)
    {
        ScheduleSimulate(dt);
        CompleteSimulate();
        ScheduleSurfaceProbes();
    }

    public unsafe void ScheduleSimulate(float dt)
    {
        _jobScheduled = false;
        if (!IsInitialized || dt <= 0f) return;

        Matrix4x4 ltw = BasisLocalPlayer.localToWorldMatrix;
        cachedPlayerUp = ltw.MultiplyVector(Vector3.up).normalized;
        cachedPlayerFwd = ltw.MultiplyVector(Vector3.forward).normalized;
        cachedPlayerRight = ltw.MultiplyVector(Vector3.right).normalized;

        var headData = BasisLocalBoneDriver.HeadControl.OutgoingWorldData;
        var hipsData = BasisLocalBoneDriver.HipsControl.OutgoingWorldData;
        var chestCtrl = BasisLocalBoneDriver.ChestControl;
        Vector3 hipsPosition = BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips);
        float hipsUpComponent = Vector3.Dot(hipsPosition, cachedPlayerUp);
        bool groundHit = GroundCast(hipsPosition, -cachedPlayerUp, rayCastRange, 0f, hipsUpComponent, out RaycastHit ch);
        LastGroundHit = groundHit;
        LastGroundUp = groundHit ? Vector3.Dot(ch.point, cachedPlayerUp) : float.NaN;
        HipsUp = hipsUpComponent;

        if (SurfaceProbesEnabled)
        {
            ApplySurfaceProbes(dt);
        }

        Quaternion avatarRotation = BasisLocalPose.GetRotation(BasisPoseSlot.AvatarRoot, avatarTransform);
        ref BasisFootSimInput inputSlot = ref UnsafeUtility.ArrayElementAsRef<BasisFootSimInput>(_nativeInput.GetUnsafePtr(), 0);
        inputSlot = new BasisFootSimInput
        {
            dt = dt,
            headPos = headData.position,
            hipsPos = hipsPosition,
            hipsRot = hipsData.rotation,
            chestRot = chestCtrl.OutgoingWorldData.rotation,
            headRot = headData.rotation,
            avatarForward = avatarRotation * Vector3.forward,
            avatarRight = avatarRotation * Vector3.right,
            hasChest = chestCtrl != null,
            groundHit = groundHit,
            groundPoint = groundHit ? (float3)ch.point : float3.zero,
            splayWhenCrouched = SplayWhenCrouchedPercentage,
            playerUp = cachedPlayerUp,
        };

        if (_paramsDirty)
        {
            _cachedParams = BuildParams();
            _paramsDirty = false;
        }
        var job = new BasisFootSimulateJob
        {
            p = _cachedParams,
            feet = _nativeFeet,
            simState = _nativeSimState,
            input = _nativeInput,
            output = _nativeOutput,
        };
        _jobHandle = job.Schedule();
        _jobScheduled = true;

        JobHandle.ScheduleBatchedJobs();
    }

    public unsafe void CompleteSimulate()
    {
        if (!_jobScheduled) return;
        _jobHandle.Complete();
        _jobScheduled = false;

        ref BasisFootNativeState leftN = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 0);
        ref BasisFootNativeState rightN = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 1);

        if (leftN.wantsStep)
        {
            FinalizeStep(ref leftN);
            leftN.wantsStep = false;
        }
        if (rightN.wantsStep)
        {
            FinalizeStep(ref rightN);
            rightN.wantsStep = false;
        }

        NativeToFootState(in leftN, left);
        NativeToFootState(in rightN, right);

        ref readonly BasisFootSimState simOut = ref UnsafeUtility.AsRef<BasisFootSimState>(_nativeSimState.GetUnsafeReadOnlyPtr());
        smoothedVelocity = simOut.smoothedVelocity;
    }

    public bool IsSimulationPending => _jobScheduled;

    private unsafe void FinalizeStep(ref BasisFootNativeState f)
    {
        ref readonly BasisFootSimState sim = ref UnsafeUtility.AsRef<BasisFootSimState>(_nativeSimState.GetUnsafeReadOnlyPtr());
        float3 velFlat = (float3)ProjectHorizontal(sim.smoothedVelocity);
        float speed = math.length(velFlat);
        float fastYawRef = Mathf.Max(1f, 0.5f * maxPlantedYawDegrees / Mathf.Max(0.01f, stepDurFast));
        float absYawRate = Mathf.Abs(sim.smoothedYawRateDeg);
        float yawPacing = Mathf.Clamp01(absYawRate / fastYawRef);

        float urgencyT = Mathf.Clamp01(f.stepUrgency);

        f.phase = 1;
        f.stepStartPos = f.currentPos;

        f.stepStartRot = f.currentRot;
        f.stepTimer = 0f;
        f.stepDur = Mathf.Lerp(stepDurSlow, stepDurFast, urgencyT);

        f.stepArcScale = BasisFootSimulateJob.TurnStepArcFloor * yawPacing;

        float hipsUpComp = Vector3.Dot(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), cachedPlayerUp);
        Vector3 targetXZ = f.predictedTargetXZ;
        Vector3 rayOrig = targetXZ + cachedPlayerUp * rayCastRange * 0.5f;
        if (GroundCast(rayOrig, -cachedPlayerUp, rayCastRange, raySphereRadius, hipsUpComp, out RaycastHit hit))
        {
            f.stepTargetPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            float targetUpComp = hipsUpComp - hipToFoot - ankleHeight + footHeightOffset;
            Vector3 targetFlat = ProjectHorizontal(targetXZ);
            f.stepTargetPos = targetFlat + cachedPlayerUp * targetUpComp;
        }

        float3 bodyFwd = sim.smoothedBodyFwd;
        Vector3 rawR = Vector3.Cross(cachedPlayerUp, (Vector3)(float3)bodyFwd).normalized;
        if (rawR.sqrMagnitude < 0.001f) rawR = Vector3.right;

        Vector3 stp = f.stepTargetPos;

        float stpUpComp = Vector3.Dot(stp, cachedPlayerUp);
        Vector3 hipsFlat = ProjectHorizontal(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips));
        Vector3 hGround = hipsFlat + cachedPlayerUp * stpUpComp;
        EnforceSide(ref stp, hGround, rawR, f.sideSign, stanceWidth * stepTargetSideFraction);
        f.stepTargetPos = stp;
    }

    private static readonly RaycastHit[] s_groundHits = new RaycastHit[8];

    private bool IsSelfCollider(Collider c)
    {
        if (c == null) return true;
        if (_selfCollider != null && c == _selfCollider) return true;
        return _selfRoot != null && c.transform.IsChildOf(_selfRoot);
    }

    private bool GroundCast(Vector3 origin, Vector3 dir, float maxDist, float sphereRadius, float maxUpComponent, out RaycastHit best)
    {
        best = default;
        int count = sphereRadius > 0f
            ? Physics.SphereCastNonAlloc(origin, sphereRadius, dir, s_groundHits, maxDist, groundLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, dir, s_groundHits, maxDist, groundLayers, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestDist = float.MaxValue;
        for (int Index = 0; Index < count; Index++)
        {
            RaycastHit h = s_groundHits[Index];

            if (h.distance <= 0f) continue;
            if (h.distance >= bestDist) continue;
            if (Vector3.Dot(h.point, cachedPlayerUp) > maxUpComponent) continue;
            if (IsSelfCollider(h.collider)) continue;
            bestDist = h.distance;
            best = h;
            found = true;
        }
        return found;
    }

    public static bool SurfaceProbesEnabled = true;

    private const float k_HeelProbeFrac = 0.45f;
    private const float k_BallProbeFrac = 0.85f;
    private const float k_ToeProbeFrac = 1.30f;
    private const float k_FootHalfWidthFrac = 0.28f;

    private const float k_ToeMaxDorsiDeg = 40f;
    private const float k_ToeMaxPlantarDeg = 15f;
    private const float k_ToeBendRate = 12f;
    private const float k_SurfaceNormalRate = 14f;

    public unsafe void ScheduleSurfaceProbes()
    {
        if (!IsInitialized || !SurfaceProbesEnabled || !_probeCommands.IsCreated) return;
        DiscardPendingProbes();

        int foot = _probeNextFoot;
        _probeNextFoot ^= 1;

        ref BasisFootNativeState f = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), foot);

        if (f.phase != 0 || footLength <= 0f) return;

        Vector3 fwd = ProjectHorizontal((Vector3)f.plantedBodyFwd);
        if (fwd.sqrMagnitude < 1e-6f) fwd = ProjectHorizontal(cachedPlayerFwd);
        if (fwd.sqrMagnitude < 1e-6f) return;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(cachedPlayerUp, fwd);
        if (right.sqrMagnitude < 1e-6f) return;
        right.Normalize();

        _probePlan = new BasisFootProbePlan
        {
            foot = foot,
            up = cachedPlayerUp,
            fwd = fwd,
            right = right,
            heelD = footLength * k_HeelProbeFrac,
            ballD = footLength * k_BallProbeFrac,
            toeD = footLength * k_ToeProbeFrac,
            halfW = footLength * k_FootHalfWidthFrac,
            hipsUpComp = Vector3.Dot(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), cachedPlayerUp),
        };

        Vector3 c = (Vector3)f.currentPos;
        Vector3 lift = cachedPlayerUp * (rayCastRange * 0.5f);
        Vector3 down = -cachedPlayerUp;
        QueryParameters query = new QueryParameters(groundLayers, hitMultipleFaces: false, hitTriggers: QueryTriggerInteraction.Ignore, hitBackfaces: false);

        _probeCommands[0] = new RaycastCommand(c - fwd * _probePlan.heelD + lift, down, query, rayCastRange);
        _probeCommands[1] = new RaycastCommand(c + fwd * _probePlan.ballD + right * _probePlan.halfW + lift, down, query, rayCastRange);
        _probeCommands[2] = new RaycastCommand(c + fwd * _probePlan.ballD - right * _probePlan.halfW + lift, down, query, rayCastRange);
        _probeCommands[3] = new RaycastCommand(c + fwd * _probePlan.toeD + lift, down, query, rayCastRange);

        UnsafeUtility.MemClear(_probeResults.GetUnsafePtr(), (long)_probeResults.Length * UnsafeUtility.SizeOf<RaycastHit>());

        _probeHandle = RaycastCommand.ScheduleBatch(_probeCommands, _probeResults, k_ProbeRays, k_ProbeMaxHits);
        _probePending = true;
        JobHandle.ScheduleBatchedJobs();
    }

    private unsafe void ApplySurfaceProbes(float dt)
    {
        _probeElapsedLeft += dt;
        _probeElapsedRight += dt;

        ref BasisFootNativeState leftF = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 0);
        ref BasisFootNativeState rightF = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 1);
        if (leftF.phase != 0) leftF.toeBendDeg = Mathf.MoveTowards(leftF.toeBendDeg, 0f, k_ToeMaxDorsiDeg * dt * 4f);
        if (rightF.phase != 0) rightF.toeBendDeg = Mathf.MoveTowards(rightF.toeBendDeg, 0f, k_ToeMaxDorsiDeg * dt * 4f);

        if (!_probePending) return;
        _probeHandle.Complete();
        _probePending = false;

        int foot = _probePlan.foot;

        float elapsed = foot == 0 ? _probeElapsedLeft : _probeElapsedRight;
        if (foot == 0) _probeElapsedLeft = 0f; else _probeElapsedRight = 0f;

        ref BasisFootNativeState f = ref UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), foot);

        if (f.phase != 0) return;

        FitFootSurface(ref f, Mathf.Min(elapsed, 0.25f));
    }

    private void FitFootSurface(ref BasisFootNativeState f, float dt)
    {
        Vector3 fwd = _probePlan.fwd;
        Vector3 right = _probePlan.right;
        Vector3 up = _probePlan.up;
        float heelD = _probePlan.heelD;
        float ballD = _probePlan.ballD;
        float toeD = _probePlan.toeD;
        float halfW = _probePlan.halfW;

        bool okHeel = ResolveProbeHeight(0, out float heelH);
        bool okA = ResolveProbeHeight(1, out float ballAH);
        bool okB = ResolveProbeHeight(2, out float ballBH);
        bool okToe = ResolveProbeHeight(3, out float toeH);

        if (okHeel && okA && okB)
        {
            float ballH = (ballAH + ballBH) * 0.5f;

            Vector3 tFwd = fwd * (heelD + ballD) + up * (ballH - heelH);
            Vector3 tRight = right * (2f * halfW) + up * (ballAH - ballBH);
            Vector3 n = Vector3.Cross(tFwd, tRight);
            if (n.sqrMagnitude > 1e-8f)
            {
                n.Normalize();
                if (Vector3.Dot(n, up) < 0f) n = -n;

                Vector3 prev = (Vector3)f.filteredNormal;
                if (prev.sqrMagnitude < 1e-6f) prev = up;
                f.filteredNormal = Vector3.Slerp(prev, n, 1f - Mathf.Exp(-k_SurfaceNormalRate * dt)).normalized;
            }

            float span = Mathf.Max(1e-3f, heelD + ballD);
            float expectedToeH = ballH + (ballH - heelH) / span * (toeD - ballD);
            float toeDelta = toeH - expectedToeH;

            bool toeHasSurface = okToe && toeDelta > -footLength * 0.5f;
            if (toeHasSurface)
            {
                float bend = Mathf.Atan2(toeDelta, Mathf.Max(1e-3f, toeD - ballD)) * Mathf.Rad2Deg;
                bend = Mathf.Clamp(bend, -k_ToeMaxPlantarDeg, k_ToeMaxDorsiDeg);
                f.toeBendDeg = Mathf.Lerp(f.toeBendDeg, bend, 1f - Mathf.Exp(-k_ToeBendRate * dt));
            }
            else
            {
                f.toeBendDeg = Mathf.Lerp(f.toeBendDeg, 0f, 1f - Mathf.Exp(-k_ToeBendRate * dt));
            }

            f.toeBendAxis = right;
        }
        else
        {
            f.toeBendDeg = Mathf.Lerp(f.toeBendDeg, 0f, 1f - Mathf.Exp(-k_ToeBendRate * dt));
        }
    }

    private bool ResolveProbeHeight(int slot, out float height)
    {
        int baseIndex = slot * k_ProbeMaxHits;
        float bestDist = float.MaxValue;
        bool found = false;
        height = 0f;
        for (int Index = 0; Index < k_ProbeMaxHits; Index++)
        {
            RaycastHit h = _probeResults[baseIndex + Index];

            if (h.distance <= 0f) continue;
            if (h.distance >= bestDist) continue;
            float up = Vector3.Dot(h.point, _probePlan.up);
            if (up > _probePlan.hipsUpComp) continue;
            if (IsSelfCollider(h.collider)) continue;
            bestDist = h.distance;
            height = up;
            found = true;
        }
        return found;
    }

    private Vector3 ProjectHorizontal(Vector3 v)
    {
        return v - cachedPlayerUp * Vector3.Dot(v, cachedPlayerUp);
    }

    private float HDist(Vector3 a, Vector3 b)
    {
        return ProjectHorizontal(a - b).magnitude;
    }

    private static void EnforceSide(ref Vector3 idealPos, Vector3 center, Vector3 bodyRight, int sideSign, float minDist)
    {
        Vector3 toIdeal = idealPos - center;
        float lateral = Vector3.Dot(toIdeal, bodyRight);

        float required = sideSign * minDist;
        if (sideSign > 0 && lateral < required)
        {
            idealPos += bodyRight * (required - lateral);
        }
        else if (sideSign < 0 && lateral > -minDist)
        {
            idealPos -= bodyRight * (lateral + minDist);
        }
    }

    private float HeadYaw()
    {
        var hc = BasisLocalBoneDriver.HeadControl;
        Vector3 fwd = ProjectHorizontal(hc.OutgoingWorldData.rotation * Vector3.forward);
        if (fwd.sqrMagnitude < 0.001f) return prevHeadYaw;
        return Mathf.Atan2(Vector3.Dot(fwd, cachedPlayerRight), Vector3.Dot(fwd, cachedPlayerFwd)) * Mathf.Rad2Deg;
    }

    private Vector3 BodyForward()
    {
        Vector3 accumulated = Vector3.zero;
        float totalWeight = 0f;

        var hipsCtrl = BasisLocalBoneDriver.HipsControl;
        Vector3 hipsFwd = ProjectHorizontal(hipsCtrl.OutgoingWorldData.rotation * Vector3.forward);
        if (hipsFwd.sqrMagnitude > 0.001f)
        {
            accumulated += hipsFwd.normalized * bodyFwdHipsWeight;
            totalWeight += bodyFwdHipsWeight;
        }

        var chestCtrl = BasisLocalBoneDriver.ChestControl;
        if (chestCtrl != null)
        {
            Vector3 chestFwd = ProjectHorizontal(chestCtrl.OutgoingWorldData.rotation * Vector3.forward);
            if (chestFwd.sqrMagnitude > 0.001f)
            {
                accumulated += chestFwd.normalized * bodyFwdChestWeight;
                totalWeight += bodyFwdChestWeight;
            }
        }

        Vector3 headFwd = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation * Vector3.forward;
        Vector3 headFlat = ProjectHorizontal(headFwd);
        if (headFlat.sqrMagnitude > 0.1f)
        {
            accumulated += headFlat.normalized * bodyFwdHeadWeight;
            totalWeight += bodyFwdHeadWeight;
        }

        if (totalWeight > 0f)
        {
            accumulated /= totalWeight;
            if (accumulated.sqrMagnitude > 0.001f)
                return accumulated.normalized;
        }

        return BasisLocalPose.GetRotation(BasisPoseSlot.AvatarRoot, avatarTransform) * Vector3.forward;
    }

    private void CaptureFootAlignment(Transform lf, Transform rf)
    {
        footAlignLeft = Quaternion.identity;
        footAlignRight = Quaternion.identity;
        if (avatarTransform == null) return;

        Quaternion avatarRot = BasisLocalPose.GetRotation(BasisPoseSlot.AvatarRoot, avatarTransform);
        Vector3 avatarUp = avatarRot * Vector3.up;
        Quaternion restFrame = BuildFootFrame(avatarRot * Vector3.forward, avatarUp, avatarUp);
        Quaternion invRest = Quaternion.Inverse(restFrame);

        if (lf != null) footAlignLeft = invRest * lf.GetRotation();
        if (rf != null) footAlignRight = invRest * rf.GetRotation();
    }

    private Quaternion BuildFootFrame(Vector3 bodyFwd, Vector3 normal, Vector3 up)
    {
        if (normal.sqrMagnitude < 0.001f)
        {
            normal = up;
        }

        Vector3 fwd = Vector3.ProjectOnPlane(bodyFwd, normal);
        if (fwd.sqrMagnitude < 1e-6f)
        {
            fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
        }

        fwd.Normalize();

        Quaternion surfaceRot = Quaternion.LookRotation(fwd, normal);
        Quaternion uprightRot = Quaternion.LookRotation(fwd, up);
        float tiltAngle = Quaternion.Angle(uprightRot, surfaceRot);
        return tiltAngle > 0.01f
            ? Quaternion.Slerp(uprightRot, surfaceRot, Mathf.Clamp01(maxFootTiltDegrees / tiltAngle))
            : uprightRot;
    }

    private Quaternion FootRotation(Vector3 bodyFwd, Vector3 normal, Quaternion footAlign)
    {
        if (normal.sqrMagnitude < 0.001f)
        {
            normal = cachedPlayerUp;
        }

        Quaternion result = BuildFootFrame(bodyFwd, normal, cachedPlayerUp);

        Vector3 footFwd = result * Vector3.forward;
        Vector3 footFwdFlat = ProjectHorizontal(footFwd);
        Vector3 bodyFwdFlat = ProjectHorizontal(bodyFwd);

        if (footFwdFlat.sqrMagnitude > 1e-6f && bodyFwdFlat.sqrMagnitude > 1e-6f)
        {
            footFwdFlat.Normalize();
            bodyFwdFlat.Normalize();

            float yawAngle = Vector3.SignedAngle(bodyFwdFlat, footFwdFlat, cachedPlayerUp);
            if (Mathf.Abs(yawAngle) > maxFootYawDegrees)
            {
                float clampedYaw = Mathf.Clamp(yawAngle, -maxFootYawDegrees, maxFootYawDegrees);
                float correction = clampedYaw - yawAngle;
                result = Quaternion.AngleAxis(correction, cachedPlayerUp) * result;
            }
        }

        return result * footAlign;
    }
    private static bool TryTP(System.Collections.Generic.Dictionary<HumanBodyBones, BasisCalibratedCoords> tp, HumanBodyBones b, out Vector3 p)
    {
        p = Vector3.zero;
        if (!tp.TryGetValue(b, out var c) || c.position == Vector3.zero)
        {
            return false;
        }
        p = c.position;
        return true;
    }

    private void FallbackStanceWidth()
    {
        if (left.bone == null || right.bone == null)
        {
            stanceWidth = 0.2f;
        }
        else
        {
            stanceWidth = Mathf.Max(0.04f, ProjectHorizontal(right.bone.position - left.bone.position).magnitude);
        }
    }
    private void FallbackHipToFoot()
    {
        if (hips != null && left.bone != null && right.bone != null)
        {
            float hipsAlongUp = Vector3.Dot(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), cachedPlayerUp);
            float feetAlongUp = (Vector3.Dot(left.bone.position, cachedPlayerUp) + Vector3.Dot(right.bone.position, cachedPlayerUp)) * 0.5f;
            hipToFoot = Mathf.Max(0.15f, Mathf.Abs(hipsAlongUp - feetAlongUp));
        }
        else
        {
            hipToFoot = 0.85f;
        }
    }
    private void FallbackLegLens(bool isLeft)
    {
        float total = hipToFoot;
        float th = total * 0.55f, sh = total * 0.45f;
        if (isLeft)
        {
            leftThighLen = th;
            leftShinLen = sh;
        }
        else
        {
            rightThighLen = th;
            rightShinLen = sh;
        }
    }

    private void InitPose(BasisFootState f)
    {
        if (f.bone == null)
        {
            return;
        }

        Vector3 bp = f.bone.position;
        if (GroundCast(bp + cachedPlayerUp * (hipToFoot * 0.33f), -cachedPlayerUp, rayCastRange, 0f, Vector3.Dot(BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), cachedPlayerUp), out RaycastHit hit))
        {
            f.currentPos = f.plantedPos = f.idealPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            f.currentPos = f.plantedPos = f.idealPos = bp;
            f.filteredNormal = cachedPlayerUp;
        }
        Vector3 fwd = avatarTransform != null ? BasisLocalPose.GetRotation(BasisPoseSlot.AvatarRoot, avatarTransform) * Vector3.forward : Vector3.forward;
        f.currentRot = f.plantedRot = f.stepStartRot = FootRotation(fwd, f.filteredNormal, f.sideSign < 0 ? footAlignLeft : footAlignRight);
        f.phase = BasisFootPhase.Planted;
        f.kneeHint = (BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips) + f.currentPos) * 0.5f + fwd * (f.thighLen > 0 ? f.thighLen * 0.4f : 0.12f);
    }

    public float ComputeHipBob()
    {
        if (!IsInitialized || !_nativeOutput.IsCreated) return 0f;
        return _nativeOutput[0].hipBob;
    }

    public Vector3 ComputeHipSway()
    {
        if (!IsInitialized || !_nativeOutput.IsCreated) return Vector3.zero;
        return _nativeOutput[0].hipSway;
    }

    public Quaternion ComputePelvisDelta()
    {
        if (!IsInitialized || !_nativeOutput.IsCreated) return Quaternion.identity;
        return _nativeOutput[0].pelvisDelta;
    }

    public unsafe float LeftToeBendDegrees => IsInitialized && _nativeFeet.IsCreated
        ? UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 0).toeBendDeg : 0f;
    public unsafe float RightToeBendDegrees => IsInitialized && _nativeFeet.IsCreated
        ? UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 1).toeBendDeg : 0f;

    public unsafe Vector3 LeftToeBendAxis => IsInitialized && _nativeFeet.IsCreated
        ? (Vector3)UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 0).toeBendAxis : Vector3.zero;
    public unsafe Vector3 RightToeBendAxis => IsInitialized && _nativeFeet.IsCreated
        ? (Vector3)UnsafeUtility.ArrayElementAsRef<BasisFootNativeState>(_nativeFeet.GetUnsafePtr(), 1).toeBendAxis : Vector3.zero;

    public bool LeftIsPlanted => left.phase == BasisFootPhase.Planted;
    public bool RightIsPlanted =>  right.phase == BasisFootPhase.Planted;
    public float LeftStepProgress => left.phase == BasisFootPhase.Stepping ? Mathf.Clamp01(left.stepTimer / left.stepDur) : 0f;
    public float RightStepProgress => right.phase == BasisFootPhase.Stepping ? Mathf.Clamp01(right.stepTimer / right.stepDur) : 0f;
    public Vector3 LeftIdealPos => left.idealPos;
    public Vector3 RightIdealPos => right.idealPos;
    public Vector3 LeftStepTarget => left.stepTargetPos;
    public Vector3 RightStepTarget => right.stepTargetPos;
    public Vector3 SmoothedVelocity => smoothedVelocity;
    public float Speed => smoothedVelocity.magnitude;
    public Vector3 HipsPosition => BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips);
    public float CalibratedStanceWidth => stanceWidth;
    public float CalibratedHipToFoot => hipToFoot;
    public float CalibratedLeftLeg => leftLegLen;
    public float CalibratedRightLeg => rightLegLen;
    public float CalibratedFootLength => footLength;
    public float CalibratedAnkleHeight => ankleHeight;
    public float DerivedStepHeight => stepHeightCalc;
    public float DerivedStepTrigger => stepTriggerDist;
    public float DerivedFastSpeed => fastSpeedRef;
    private static readonly int[] _gCurrent = { -1, -1 };
    private static readonly int[] _gForward = { -1, -1 };
    private static readonly int[] _gIdeal = { -1, -1 };
    private static readonly int[] _gPlantIdeal = { -1, -1 };
    private static readonly int[] _gStepArc = { -1, -1 };
    private static readonly int[] _gStepTarget = { -1, -1 };
    private static readonly int[] _gKnee = { -1, -1 };
    private static readonly int[] _gHipFoot = { -1, -1 };
    private static readonly int[] _gLabel = { -1, -1 };
    private static int _gBodyForward = -1;
    private static int _gVelocity = -1;
    private static bool _gizmosCreated;
    private static bool _gizmosVisible;
    private static bool _gizmoHooked;
    private static readonly Vector3[] _stepArcBuf = new Vector3[17];
    private const float FootGizmoLineWidth = 0.004f;

    public void UpdateGizmos(bool show, bool showLabels, Vector3 cameraPos)
    {
        EnsureGizmoHook();

        if (!show || !IsInitialized || left == null || right == null)
        {
            SetGizmosVisible(false);
            return;
        }

        EnsureGizmosCreated();

        UpdateFootGizmos(0, left, new Color(0.2f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.1f), showLabels, cameraPos);
        UpdateFootGizmos(1, right, new Color(0.2f, 0.5f, 1f), new Color(1f, 0.5f, 0.1f), showLabels, cameraPos);

        if (hips != null)
        {
            Vector3 hp = BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips);
            Vector3 bf = BodyForward();
            BasisGizmoManager.UpdateLineGizmo(_gBodyForward, hp, hp + bf * 0.4f);
            BasisGizmoManager.SetGizmoActive(_gBodyForward, true);

            if (smoothedVelocity.sqrMagnitude > 0.01f)
            {
                BasisGizmoManager.UpdateLineGizmo(_gVelocity, hp, hp + smoothedVelocity * 0.5f);
                BasisGizmoManager.SetGizmoActive(_gVelocity, true);
            }
            else
            {
                BasisGizmoManager.SetGizmoActive(_gVelocity, false);
            }
        }
        else
        {
            BasisGizmoManager.SetGizmoActive(_gBodyForward, false);
            BasisGizmoManager.SetGizmoActive(_gVelocity, false);
        }

        _gizmosVisible = true;
    }

    private void UpdateFootGizmos(int slot, BasisFootState f, Color plantCol, Color stepCol, bool showLabels, Vector3 cameraPos)
    {
        bool stepping = f.phase == BasisFootPhase.Stepping;
        Color c = stepping ? stepCol : plantCol;

        BasisGizmoManager.UpdateSphereGizmo(_gCurrent[slot], f.currentPos, Vector3.one * 0.04f);
        BasisGizmoManager.UpdateGizmoColor(_gCurrent[slot], c);
        BasisGizmoManager.SetGizmoActive(_gCurrent[slot], true);

        BasisGizmoManager.UpdateLineGizmo(_gForward[slot], f.currentPos, f.currentPos + f.currentRot * Vector3.forward * 0.07f);
        BasisGizmoManager.SetGizmoActive(_gForward[slot], true);

        BasisGizmoManager.UpdateSphereGizmo(_gIdeal[slot], f.idealPos, Vector3.one * 0.03f);
        BasisGizmoManager.UpdateGizmoColor(_gIdeal[slot], c * 0.4f);
        BasisGizmoManager.SetGizmoActive(_gIdeal[slot], true);

        BasisGizmoManager.UpdateLineGizmo(_gPlantIdeal[slot], f.plantedPos, f.idealPos);
        BasisGizmoManager.SetGizmoActive(_gPlantIdeal[slot], true);

        if (stepping)
        {
            const int seg = 16;
            _stepArcBuf[0] = f.stepStartPos;
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg;
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(f.stepStartPos, f.stepTargetPos, e);
                float lift = Mathf.Pow(t, 0.6f) * Mathf.Pow(1f - t, 1.4f) / 0.234f;
                p += cachedPlayerUp * (Mathf.Clamp01(lift) * stepHeightCalc);
                _stepArcBuf[i] = p;
            }
            BasisGizmoManager.UpdateLineGizmo(_gStepArc[slot], _stepArcBuf);
            BasisGizmoManager.UpdateGizmoColor(_gStepArc[slot], stepCol * 0.6f);
            BasisGizmoManager.SetGizmoActive(_gStepArc[slot], true);

            BasisGizmoManager.UpdateSphereGizmo(_gStepTarget[slot], f.stepTargetPos, Vector3.one * 0.03f);
            BasisGizmoManager.SetGizmoActive(_gStepTarget[slot], true);
        }
        else
        {
            BasisGizmoManager.SetGizmoActive(_gStepArc[slot], false);
            BasisGizmoManager.SetGizmoActive(_gStepTarget[slot], false);
        }

        BasisGizmoManager.UpdateLineGizmo(_gKnee[slot], f.currentPos, f.kneeHint);
        BasisGizmoManager.SetGizmoActive(_gKnee[slot], true);

        if (hips != null)
        {
            BasisGizmoManager.UpdateLineGizmo(_gHipFoot[slot], BasisLocalPose.GetPosition(BasisPoseSlot.Hips, hips), f.currentPos);
            BasisGizmoManager.UpdateGizmoColor(_gHipFoot[slot], c * 0.3f);
            BasisGizmoManager.SetGizmoActive(_gHipFoot[slot], true);
        }
        else
        {
            BasisGizmoManager.SetGizmoActive(_gHipFoot[slot], false);
        }

        if (showLabels)
        {
            float dist = HDist(f.plantedPos, f.idealPos);
            string lbl = stepping
                ? $"{f.name} STEP {Mathf.Clamp01(f.stepTimer / f.stepDur):P0}"
                : $"{f.name} planted  drift:{dist * 100f:F1}cm";
            Vector3 labelPos = f.currentPos + cachedPlayerUp * 0.06f;
            if (_gLabel[slot] <= 0)
            {
                BasisGizmoManager.CreateTextGizmo($"FootLabel_{f.name}", out _gLabel[slot], labelPos, lbl, c);
            }
            Quaternion rot = BasisGizmoManager.BillboardRotation(labelPos, cameraPos);
            BasisGizmoManager.UpdateTextGizmo(_gLabel[slot], labelPos, rot, 0.02f * Mathf.Max(0.01f, BasisHeightDriver.ScaledToMatchValue), lbl, c);
            BasisGizmoManager.SetGizmoActive(_gLabel[slot], true);
        }
        else if (_gLabel[slot] > 0)
        {
            BasisGizmoManager.DestroyGizmo(_gLabel[slot]);
            _gLabel[slot] = -1;
        }
    }

    private static void EnsureGizmosCreated()
    {
        if (_gizmosCreated)
        {
            return;
        }
        CreateFootGizmos(0, new Color(0.2f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.1f));
        CreateFootGizmos(1, new Color(0.2f, 0.5f, 1f), new Color(1f, 0.5f, 0.1f));
        BasisGizmoManager.CreateLineGizmo("Foot_BodyForward", out _gBodyForward, Vector3.zero, Vector3.zero, FootGizmoLineWidth, new Color(1f, 1f, 1f, 0.8f));
        BasisGizmoManager.CreateLineGizmo("Foot_Velocity", out _gVelocity, Vector3.zero, Vector3.zero, FootGizmoLineWidth, new Color(1f, 0.2f, 1f, 0.8f));
        _gizmosCreated = true;
        _gizmosVisible = true;
    }

    private static void CreateFootGizmos(int slot, Color plantCol, Color stepCol)
    {
        string n = slot == 0 ? "Left" : "Right";
        BasisGizmoManager.CreateSphereGizmo($"Foot_{n}_Current", out _gCurrent[slot], Vector3.zero, 0.04f, plantCol);
        BasisGizmoManager.CreateLineGizmo($"Foot_{n}_Forward", out _gForward[slot], Vector3.zero, Vector3.zero, FootGizmoLineWidth, new Color(0.2f, 0.4f, 1f, 0.9f));
        BasisGizmoManager.CreateSphereGizmo($"Foot_{n}_Ideal", out _gIdeal[slot], Vector3.zero, 0.03f, plantCol * 0.4f);
        BasisGizmoManager.CreateLineGizmo($"Foot_{n}_PlantIdeal", out _gPlantIdeal[slot], Vector3.zero, Vector3.zero, FootGizmoLineWidth, new Color(1f, 0.4f, 0.2f, 0.5f));
        BasisGizmoManager.CreateLineGizmo($"Foot_{n}_StepArc", out _gStepArc[slot], _stepArcBuf, FootGizmoLineWidth, stepCol * 0.6f);
        BasisGizmoManager.CreateSphereGizmo($"Foot_{n}_StepTarget", out _gStepTarget[slot], Vector3.zero, 0.03f, stepCol);
        BasisGizmoManager.CreateLineGizmo($"Foot_{n}_Knee", out _gKnee[slot], Vector3.zero, Vector3.zero, FootGizmoLineWidth, new Color(0f, 1f, 1f, 0.4f));
        BasisGizmoManager.CreateLineGizmo($"Foot_{n}_HipFoot", out _gHipFoot[slot], Vector3.zero, Vector3.zero, FootGizmoLineWidth, plantCol * 0.3f);
    }

    private static void SetGizmosVisible(bool visible)
    {
        if (!_gizmosCreated || _gizmosVisible == visible)
        {
            return;
        }
        for (int slot = 0; slot < 2; slot++)
        {
            BasisGizmoManager.SetGizmoActive(_gCurrent[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gForward[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gIdeal[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gPlantIdeal[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gStepArc[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gStepTarget[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gKnee[slot], visible);
            BasisGizmoManager.SetGizmoActive(_gHipFoot[slot], visible);
            if (_gLabel[slot] > 0)
            {
                BasisGizmoManager.SetGizmoActive(_gLabel[slot], visible);
            }
        }
        BasisGizmoManager.SetGizmoActive(_gBodyForward, visible);
        BasisGizmoManager.SetGizmoActive(_gVelocity, visible);
        _gizmosVisible = visible;
    }

    private static void EnsureGizmoHook()
    {
        if (_gizmoHooked)
        {
            return;
        }
        BasisGizmoManager.OnUseGizmosChanged += OnGizmoMasterToggleChanged;
        _gizmoHooked = true;
    }

    private static void OnGizmoMasterToggleChanged(bool state)
    {
        if (!state)
        {
            ResetGizmoState();
        }
    }

    private static void ResetGizmoState()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            _gCurrent[slot] = -1;
            _gForward[slot] = -1;
            _gIdeal[slot] = -1;
            _gPlantIdeal[slot] = -1;
            _gStepArc[slot] = -1;
            _gStepTarget[slot] = -1;
            _gKnee[slot] = -1;
            _gHipFoot[slot] = -1;
            _gLabel[slot] = -1;
        }
        _gBodyForward = -1;
        _gVelocity = -1;
        _gizmosCreated = false;
        _gizmosVisible = false;
    }
}

public partial class BasisLocalFootDriver
{
    [Serializable]
    private class BasisFootState
    {
        public string name;
        public Transform bone, thigh, shin;
        public int sideSign;
        public float thighLen, shinLen, legLength;

        public BasisFootPhase phase;
        public Vector3 plantedPos;
        public Quaternion plantedRot;
        public Vector3 plantedBodyFwd;
        public Vector3 stepStartPos, stepTargetPos;

        public Quaternion stepStartRot;
        public float stepTimer, stepDur;

        public float plantedTime;

        public Vector3 idealPos, filteredNormal;
        public Vector3 currentPos;
        public Quaternion currentRot;
        public Vector3 kneeHint;

        public BasisFootState(string n, Transform b, int s)
        {
            name = n;
            bone = b;
            sideSign = s;
            filteredNormal = Vector3.up;
            phase = BasisFootPhase.Planted;
            plantedTime = SettledPlantedTime;
        }
    }
}
