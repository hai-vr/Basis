using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;
using static Basis.Scripts.Avatar.BasisAvatarIKStageCalibration;
using static BasisHeightDriver;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Local rig driver that wires up Unity Animation Rigging constraints for a player avatar,
    /// filters tracker noise (One Euro Filter), and manually evaluates the rig graph each frame.
    /// Sets up spine, head, hands, feet, and toes, and toggles layers based on available rigs.
    /// </summary>
    [Serializable]
    public class BasisLocalRigDriver
    {
        /// <summary>
        /// Lower = more smoothing; Higher = more responsive. (0.01f, 10f)
        /// </summary>
        public static float MinCutoff = 5.5f;

        /// <summary>
        /// How much to raise cutoff when motion is fast (reduces lag during quick moves). (0f, 10f)
        /// </summary>
        public static float Beta = 3.25f;

        /// <summary>
        /// Cutoff for derivative smoothing. (0.01f, 10f)
        /// </summary>
        public static float DerivativeCutoff = 3f;

        /// <summary>
        /// Global smoothing strength multiplier (1-100). Divides MinCutoff and Hz values
        /// to amplify filtering. Higher = stronger smoothing but more latency.
        /// </summary>
        public static float SmoothingStrength = 1f;

        public RigBuilder Builder;
        public List<RigTransform> AdditionalTransforms = new List<RigTransform>();
        [System.NonSerialized] public PlayableGraph PlayableGraph;
        public Rig MainRig;
        public RigLayer RigLayer;
        public BasisFullBodyIK BasisFullIKConstraint;

        private BasisLocalPlayer localPlayer;
        private BasisTransformMapping basisTransformMapping;

        private static readonly IKOneEuroFilterQuaternion fRotHips = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotHead = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftFoot = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightFoot = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotChest = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftLowerLeg = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightLowerLeg = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftHand = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightHand = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftLowerArm = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightLowerArm = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftToe = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightToe = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotLeftShoulder = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);
        private static readonly IKOneEuroFilterQuaternion fRotRightShoulder = new IKOneEuroFilterQuaternion(MinCutoff, Beta, DerivativeCutoff);

        private static readonly OneEuroFilterVector3 fPosHips = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosHead = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosLeftFoot = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosRightFoot = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosChest = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosLeftLowerLeg = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosRightLowerLeg = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosLeftHand = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosRightHand = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosLeftLowerArm = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosRightLowerArm = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosLeftToe = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private static readonly OneEuroFilterVector3 fPosRightToe = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);

        // Keep this order stable forever.
        // These indices drive your toggle arrays AND which filter instance is used.
        public const int S_Hips = 0;
        public const int S_Head = 1;
        public const int S_LeftFoot = 2;
        public const int S_RightFoot = 3;
        public const int S_Chest = 4;
        public const int S_LeftLowerLeg = 5;
        public const int S_RightLowerLeg = 6;
        public const int S_LeftHand = 7;
        public const int S_RightHand = 8;
        public const int S_LeftLowerArm = 9;
        public const int S_RightLowerArm = 10;
        public const int S_LeftToe = 11;
        public const int S_RightToe = 12;
        public const int S_LeftShoulder = 13;
        public const int S_RightShoulder = 14;

        public const int SlotCount = 15;

        // Smoothing enable toggles (position + rotation)
        public static bool[] SmoothPos = new bool[SlotCount];
        public static bool[] SmoothRot = new bool[SlotCount];

        // One Euro enable toggles (position + rotation)
        public static bool[] EuroPos = new bool[SlotCount];
        public static bool[] EuroRot = new bool[SlotCount];

        // Fallback smoothing when smoothing is ON but Euro is OFF
        [Range(0.01f, 60f)] public static float PositionSmoothingHz = 20f;
        [Range(0.01f, 60f)] public static float RotationSmoothingHz = 25f;

        public double timeAccumulator;

        public static Vector3 sPosHips, sPosHead, sPosLeftFoot, sPosRightFoot, sPosChest, sPosLeftLowerLeg, sPosRightLowerLeg;
        public static Vector3 sPosLeftHand, sPosRightHand, sPosLeftLowerArm, sPosRightLowerArm, sPosLeftToe, sPosRightToe;

        public static Quaternion sRotHips, sRotHead, sRotLeftFoot, sRotRightFoot, sRotChest, sRotLeftLowerLeg, sRotRightLowerLeg;
        public static Quaternion sRotLeftHand, sRotRightHand, sRotLeftLowerArm, sRotRightLowerArm, sRotLeftToe, sRotRightToe;
        public static Quaternion sRotLeftShoulder, sRotRightShoulder;

        public static bool hasFallbackState;

        // Smoothed knee hint rotations for foot-driver path (prevents upper leg snapping)
        private static Quaternion smoothedLeftKneeRot = Quaternion.identity;
        private static Quaternion smoothedRightKneeRot = Quaternion.identity;

        // Smoothed butterfly-knee hint (laying-down knee splay from tracked feet; see BasisButterflyKneeCore)
        private static Vector3 smoothedLeftButterflyHint, smoothedRightButterflyHint;
        private static float smoothedLeftButterflyWeight, smoothedRightButterflyWeight;
        private const float ButterflyKneeSmoothRate = 8f;

        // Per-foot blend weights for transitioning IK in/out (0 = animation, 1 = foot driver)
        private static float footIKBlendWeightLeft = 0f;
        private static float footIKBlendWeightRight = 0f;
        private static float footIKBlendWeight = 0f; // max of left/right, used for hip bob
        private const float FootIKBlendInSpeed = 20f;  // ~50ms to fully engage
        private const float FootIKBlendOutSpeed = 15f; // ~67ms to fully disengage

        // Hysteresis: require stationary for this long before engaging foot IK.
        // Prevents single-frame flicker at jump apex or during speed oscillations.
        private static float stationaryTimer = 0f;
        private const float StationaryDelaySeconds = 0.15f;

        // Batched filter job state — one slot per S_* index (shoulder slot in position arrays is unused).
        private NativeArray<float3> _posInputs;
        private NativeArray<float3> _posOutputs;
        private NativeArray<quaternion> _rotInputs;
        private NativeArray<quaternion> _rotOutputs;
        private NativeArray<byte> _posModeNative;
        private NativeArray<byte> _rotModeNative;
        private NativeArray<float3> _fallbackPosStates;
        private NativeArray<quaternion> _fallbackRotStates;
        private NativeArray<BasisEuroVec3State> _euroPosStates;
        private NativeArray<BasisEuroQuatState> _euroRotStates;
        public void Initialize(BasisLocalPlayer localPlayer, BasisTransformMapping references)
        {
            this.localPlayer = localPlayer;
            basisTransformMapping = references;
            timeAccumulator = 0f;
        }
        public void BuildBuilder()
        {
            if (localPlayer?.BasisAvatar?.Animator == null || Builder == null)
            {
                BasisDebug.LogError("Missing Localplayer || Avatar || Animator || builder");
                return;
            }

            PlayableGraph = localPlayer.BasisAvatar.Animator.playableGraph;
            PlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            Builder.Build(PlayableGraph);

            ResetSmoothingState();
        }

        public void SetBodySettings()
        {
            // Drop the prior recalibration first: a never-calibrated avatar then uses its own uncalibrated
            // (animator-relative) setup capture from CreateBasisFullBodyRIG.
            HasRecalibratedRotationOffsets = false;
            var rigGO = CreateOrGetRig("Main IK", true, out MainRig, out RigLayer);
            Spine(rigGO);
            BasisLocalBoneControl.HasEvents = true;
            // Keep FBT rotation calibration across avatar swaps: re-derive this avatar's per-effector offsets
            // from the stored calibration reference. No-op until the user has calibrated.
            ApplyCalibrationToCurrentAvatar();
        }

        public void CleanupBeforeContinue()
        {
            DisposeFilterArrays();

            if (MainRig == null)
            {
                return;
            }

            GameObject.Destroy(MainRig.gameObject);
            MainRig = null;
            RigLayer = default;
        }

        private void EnsureFilterArrays()
        {
            if (_posInputs.IsCreated) return;
            _posInputs = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            _posOutputs = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            _rotInputs = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            _rotOutputs = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            _posModeNative = new NativeArray<byte>(SlotCount, Allocator.Persistent);
            _rotModeNative = new NativeArray<byte>(SlotCount, Allocator.Persistent);
            _fallbackPosStates = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            _fallbackRotStates = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            _euroPosStates = new NativeArray<BasisEuroVec3State>(SlotCount, Allocator.Persistent);
            _euroRotStates = new NativeArray<BasisEuroQuatState>(SlotCount, Allocator.Persistent);

            // quaternion default-constructs to all-zeros which isn't a valid rotation; seed to identity.
            for (int i = 0; i < SlotCount; i++)
            {
                _rotInputs[i] = quaternion.identity;
                _rotOutputs[i] = quaternion.identity;
                _fallbackRotStates[i] = quaternion.identity;
            }
        }

        private void DisposeFilterArrays()
        {
            if (_posInputs.IsCreated) _posInputs.Dispose();
            if (_posOutputs.IsCreated) _posOutputs.Dispose();
            if (_rotInputs.IsCreated) _rotInputs.Dispose();
            if (_rotOutputs.IsCreated) _rotOutputs.Dispose();
            if (_posModeNative.IsCreated) _posModeNative.Dispose();
            if (_rotModeNative.IsCreated) _rotModeNative.Dispose();
            if (_fallbackPosStates.IsCreated) _fallbackPosStates.Dispose();
            if (_fallbackRotStates.IsCreated) _fallbackRotStates.Dispose();
            if (_euroPosStates.IsCreated) _euroPosStates.Dispose();
            if (_euroRotStates.IsCreated) _euroRotStates.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte PickMode(bool smoothEnabled, bool euroEnabled)
        {
            if (!smoothEnabled) return (byte)BasisFilterMode.Passthrough;
            return euroEnabled ? (byte)BasisFilterMode.Euro : (byte)BasisFilterMode.Fallback;
        }
        public void OnTPose() => OnTPose(BasisLocalAvatarDriver.CurrentlyTposing);

        public void OnTPose(bool currentlyTposing)
        {
            if (Builder == null)
            {
                BasisDebug.LogWarning($"{nameof(BasisLocalRigDriver)}: Trying to T-pose while Builder is null!");
                return;
            }

            // While in T-pose, disable all rig layers
            if (currentlyTposing)
            {
                foreach (var layer in Builder.layers)
                {
                    if (layer != null)
                    {
                        layer.active = false;
                    }
                }

                return;
            }

            // Notify controls when exiting T-pose
            var driver = BasisLocalPlayer.Instance?.LocalBoneDriver;
            if (driver?.Controls == null)
            {
                return;
            }

            foreach (var control in driver.Controls)
            {
                control?.OnHasRigChanged?.Invoke(control.HasRigLayer == BasisHasRigLayer.HasRigLayer);
            }
        }
        public void ResetSmoothingState()
        {
            timeAccumulator = 0;
            hasFallbackState = false;

            // Reset batched filter state — identity rotations to avoid lerping from zero quats.
            if (_euroPosStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) _euroPosStates[i] = default;
            }
            if (_euroRotStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) _euroRotStates[i] = default;
            }
            if (_fallbackRotStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) _fallbackRotStates[i] = quaternion.identity;
            }
            if (_fallbackPosStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) _fallbackPosStates[i] = float3.zero;
            }

            // Legacy managed Euro filters — still reset in case any live call path uses them.
            fRotHips.Reset();
            fRotHead.Reset();
            fRotLeftFoot.Reset();
            fRotRightFoot.Reset();
            fRotChest.Reset();
            fRotLeftLowerLeg.Reset();
            fRotRightLowerLeg.Reset();
            fRotLeftHand.Reset();
            fRotRightHand.Reset();
            fRotLeftLowerArm.Reset();
            fRotRightLowerArm.Reset();
            fRotLeftToe.Reset();
            fRotRightToe.Reset();
            fRotLeftShoulder.Reset();
            fRotRightShoulder.Reset();
        }
        public void SimulateIKDestinations(float deltaTime)
        {
            if (BasisFullIKConstraint == null || Builder == null)
            {
                return;
            }

            if (!PlayableGraph.IsValid())
            {
                return;
            }

            timeAccumulator += Mathf.Max(deltaTime, 1e-6f);

            EnsureFilterArrays();

            // Fallback smoothing alphas are identical for every slot this frame;
            // compute once instead of running Mathf.Exp per call.
            float smoothingStrength = Mathf.Max(1f, SmoothingStrength);
            float fallbackPosAlpha = ExpAlpha(PositionSmoothingHz / smoothingStrength, deltaTime);
            float fallbackRotAlpha = ExpAlpha(RotationSmoothingHz / smoothingStrength, deltaTime);
            float effectiveMinCutoff = MinCutoff / smoothingStrength;
            float effectiveDCutoff = DerivativeCutoff / smoothingStrength;
            float safeDt = Mathf.Max(deltaTime, 1e-6f);

            // ── 1. Gather raw inputs from bone controls (main thread only) ──
            var hipsData = BasisLocalBoneDriver.HipsControl.OutgoingWorldData;
            var headData = BasisLocalBoneDriver.HeadControl.OutgoingWorldData;
            var leftFootData = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData;
            var rightFootData = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData;
            var chestData = BasisLocalBoneDriver.ChestControl.OutgoingWorldData;
            var leftLLData = BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData;
            var rightLLData = BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData;
            var leftHandData = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData;
            var rightHandData = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData;
            var leftLAData = BasisLocalBoneDriver.LeftLowerArmControl.OutgoingWorldData;
            var rightLAData = BasisLocalBoneDriver.RightLowerArmControl.OutgoingWorldData;
            var leftToeData = BasisLocalBoneDriver.LeftToeControl.OutgoingWorldData;
            var rightToeData = BasisLocalBoneDriver.RightToeControl.OutgoingWorldData;
            Quaternion leftShoulderRot = BasisLocalBoneDriver.LeftShoulderControl.OutgoingWorldData.rotation;
            Quaternion rightShoulderRot = BasisLocalBoneDriver.RightShoulderControl.OutgoingWorldData.rotation;

            // NativeArray indexer does a safety-handle check on every call. For ~60 sequential
            // writes per frame we cache the pointers once and stream values through UnsafeUtility.
            unsafe
            {
                float3* posPtr = (float3*)_posInputs.GetUnsafePtr();
                quaternion* rotPtr = (quaternion*)_rotInputs.GetUnsafePtr();
                byte* posModePtr = (byte*)_posModeNative.GetUnsafePtr();
                byte* rotModePtr = (byte*)_rotModeNative.GetUnsafePtr();

                posPtr[S_Hips] = hipsData.position;                 rotPtr[S_Hips] = hipsData.rotation;
                posPtr[S_Head] = headData.position;                 rotPtr[S_Head] = headData.rotation;
                posPtr[S_LeftFoot] = leftFootData.position;         rotPtr[S_LeftFoot] = leftFootData.rotation;
                posPtr[S_RightFoot] = rightFootData.position;       rotPtr[S_RightFoot] = rightFootData.rotation;
                posPtr[S_Chest] = chestData.position;               rotPtr[S_Chest] = chestData.rotation;
                posPtr[S_LeftLowerLeg] = leftLLData.position;       rotPtr[S_LeftLowerLeg] = leftLLData.rotation;
                posPtr[S_RightLowerLeg] = rightLLData.position;     rotPtr[S_RightLowerLeg] = rightLLData.rotation;
                posPtr[S_LeftHand] = leftHandData.position;         rotPtr[S_LeftHand] = leftHandData.rotation;
                posPtr[S_RightHand] = rightHandData.position;       rotPtr[S_RightHand] = rightHandData.rotation;
                posPtr[S_LeftLowerArm] = leftLAData.position;       rotPtr[S_LeftLowerArm] = leftLAData.rotation;
                posPtr[S_RightLowerArm] = rightLAData.position;     rotPtr[S_RightLowerArm] = rightLAData.rotation;
                posPtr[S_LeftToe] = leftToeData.position;           rotPtr[S_LeftToe] = leftToeData.rotation;
                posPtr[S_RightToe] = rightToeData.position;         rotPtr[S_RightToe] = rightToeData.rotation;
                posPtr[S_LeftShoulder] = float3.zero;                rotPtr[S_LeftShoulder] = leftShoulderRot;
                posPtr[S_RightShoulder] = float3.zero;               rotPtr[S_RightShoulder] = rightShoulderRot;

                // ── 2. Compute filter modes from toggles ──
                for (int i = 0; i < SlotCount; i++)
                {
                    posModePtr[i] = PickMode(SmoothPos[i], EuroPos[i]);
                    rotModePtr[i] = PickMode(SmoothRot[i], EuroRot[i]);
                }
                // Shoulders have no position target — always passthrough to skip wasted work.
                posModePtr[S_LeftShoulder] = (byte)BasisFilterMode.Passthrough;
                posModePtr[S_RightShoulder] = (byte)BasisFilterMode.Passthrough;
            }

            // ── 3. On first use, seed fallback states from live inputs so we don't lerp from zero ──
            if (!hasFallbackState)
            {
                hasFallbackState = true;
                _fallbackPosStates.CopyFrom(_posInputs);
                _fallbackRotStates.CopyFrom(_rotInputs);
            }

            // ── 4. Schedule batched filter jobs ──
            var posJob = new BasisBatchPositionFilterJob
            {
                mode = _posModeNative,
                rawInputs = _posInputs,
                euroStates = _euroPosStates,
                fallbackStates = _fallbackPosStates,
                outputs = _posOutputs,
                dt = safeDt,
                minCutoff = effectiveMinCutoff,
                beta = Beta,
                dCutoff = effectiveDCutoff,
                fallbackAlpha = fallbackPosAlpha,
            };
            var rotJob = new BasisBatchRotationFilterJob
            {
                mode = _rotModeNative,
                rawInputs = _rotInputs,
                euroStates = _euroRotStates,
                fallbackStates = _fallbackRotStates,
                outputs = _rotOutputs,
                dt = safeDt,
                minCutoff = effectiveMinCutoff,
                beta = Beta,
                dCutoff = effectiveDCutoff,
                fallbackAlpha = fallbackRotAlpha,
            };
            JobHandle posHandle = posJob.Schedule(SlotCount, 4);
            JobHandle rotHandle = rotJob.Schedule(SlotCount, 4);

            // ── 5. Schedule foot sim in parallel with filters ──
            bool fbtEnabled = Basis.BasisUI.BasisSettingsDefaults.EnableFBT.RawValue;
            bool leftHasTracker = fbtEnabled && (BasisLocalBoneDriver.LeftFootControl.HasTracked == BasisHasTracked.HasTracker
                || BasisLocalBoneDriver.LeftUpperLegControl.HasTracked == BasisHasTracked.HasTracker);
            bool rightHasTracker = fbtEnabled && (BasisLocalBoneDriver.RightFootControl.HasTracked == BasisHasTracked.HasTracker
                || BasisLocalBoneDriver.RightUpperLegControl.HasTracked == BasisHasTracked.HasTracker);

            bool locomotionAnimActive = localPlayer.LocalCharacterDriver.MovementVector.sqrMagnitude > 0.001f;
            if (locomotionAnimActive) stationaryTimer = 0f;
            else stationaryTimer += deltaTime;

            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            bool footDriverReady = footDriver.IsInitialized;
            bool isStationaryEnough = stationaryTimer >= StationaryDelaySeconds;
            bool footIKSetting = Basis.BasisUI.BasisSettingsDefaults.FootIKEnabled.RawValue;
            bool footIKReady = footDriverReady && isStationaryEnough && footIKSetting;
            bool leftWantIK = footIKReady && !leftHasTracker;
            bool rightWantIK = footIKReady && !rightHasTracker;
            bool leftOrRightDrive = !leftHasTracker || !rightHasTracker;

            bool footSimScheduled = false;
            if (footDriverReady && leftOrRightDrive)
            {
                footDriver.ScheduleSimulate(deltaTime);
                footSimScheduled = true;
            }

            // ── 6. Main-thread bookkeeping runs parallel with filter + foot jobs ──
            float leftBlendTarget = leftWantIK ? 1f : 0f;
            float rightBlendTarget = rightWantIK ? 1f : 0f;
            if (leftHasTracker) footIKBlendWeightLeft = 0f;
            if (rightHasTracker) footIKBlendWeightRight = 0f;

            float leftPrevBlend = footIKBlendWeightLeft;
            float rightPrevBlend = footIKBlendWeightRight;
            footIKBlendWeightLeft = Mathf.MoveTowards(footIKBlendWeightLeft, leftBlendTarget,
                (leftWantIK ? FootIKBlendInSpeed : FootIKBlendOutSpeed) * deltaTime);
            footIKBlendWeightRight = Mathf.MoveTowards(footIKBlendWeightRight, rightBlendTarget,
                (rightWantIK ? FootIKBlendInSpeed : FootIKBlendOutSpeed) * deltaTime);
            footIKBlendWeight = Mathf.Max(footIKBlendWeightLeft, footIKBlendWeightRight);

            bool notifyReengage = footDriverReady &&
                ((leftPrevBlend < 0.001f && footIKBlendWeightLeft >= 0.001f)
                 || (rightPrevBlend < 0.001f && footIKBlendWeightRight >= 0.001f));

            bool leftLLHasTracker = fbtEnabled && BasisLocalBoneDriver.LeftLowerLegControl.HasTracked == BasisHasTracked.HasTracker;
            bool rightLLHasTracker = fbtEnabled && BasisLocalBoneDriver.RightLowerLegControl.HasTracked == BasisHasTracked.HasTracker;
            bool hipsHaveTracker = fbtEnabled && BasisLocalBoneDriver.HipsControl.HasTracked == BasisHasTracked.HasTracker;
            bool trackerBendNormal = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackerBendNormal.RawValue;

            // ── 7. Wait for jobs ──
            JobHandle.CombineDependencies(posHandle, rotHandle).Complete();
            if (footSimScheduled) footDriver.CompleteSimulate();

            // NotifyReEngaging reads live bone control data (not foot sim output), but kept after
            // completion so all foot state is coherent when the next sim starts.
            if (notifyReengage) footDriver.NotifyReEngaging();

            // ── 8. Scatter filter outputs into BasisFullBodyData ──
            BasisFullBodyData data = BasisFullIKConstraint.data;

            // Pull out pointers once; avoids per-slot safety-handle checks on each indexer read.
            Vector3 hipsPos;
            Quaternion hipsRot;
            Vector3 chestPos;
            Quaternion chestRot;
            Vector3 llaPos, rlaPos;
            Quaternion llaRot, rlaRot;
            unsafe
            {
                float3* pOut = (float3*)_posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)_rotOutputs.GetUnsafeReadOnlyPtr();

                hipsPos = pOut[S_Hips];
                hipsRot = rOut[S_Hips];
                hipsPos.y -= localPlayer.LocalCharacterDriver.landingCrouchEffect;
                data.PositionHips = hipsPos;
                data.RotationHips = hipsRot;
                data.HasHipsTracker = hipsHaveTracker;

                data.PositionHead = pOut[S_Head];
                data.RotationHead = rOut[S_Head];

                // ── LEFT FOOT ──
                if (leftHasTracker)
                {
                    data.LeftFootPosition = pOut[S_LeftFoot];
                    data.LeftFootRotation = rOut[S_LeftFoot];
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    data.LeftFootPosition = footDriver.LeftFootPosition;
                    // Position-only foot IK: zero-quaternion sentinel -> SolveLegs keeps the foot's correct
                    // pre-solve (animation) rotation instead of applying target*offset (which came out toes-up).
                    data.LeftFootRotation = new Quaternion(0f, 0f, 0f, 0f);
                    data.EnableLeftLeg = footIKBlendWeightLeft;
                }
                else
                {
                    data.EnableLeftLeg = 0f;
                }

                // ── RIGHT FOOT ──
                if (rightHasTracker)
                {
                    data.RightFootPosition = pOut[S_RightFoot];
                    data.RightFootRotation = rOut[S_RightFoot];
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    data.RightFootPosition = footDriver.RightFootPosition;
                    data.RightFootRotation = new Quaternion(0f, 0f, 0f, 0f);
                    data.EnableRightLeg = footIKBlendWeightRight;
                }
                else
                {
                    data.EnableRightLeg = 0f;
                }

                if (BasisFootRotationDebug.Enabled)
                {
                    if (data.leftFoot != null)
                        BasisFootRotationDebug.Record("L", Time.time, footIKBlendWeightLeft,
                            !leftHasTracker && footIKBlendWeightLeft > 0.001f && footDriverReady,
                            data.leftFoot.rotation, data.LeftFootRotation, data.M_CalibrationLeftFootRotation,
                            BasisLocalBoneDriver.LeftFootControl.OutGoingData.rotation,
                            BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation,
                            (Quaternion)rOut[S_LeftFoot], footDriverReady ? footDriver.LeftFootRotation : Quaternion.identity);
                    if (data.RightFoot != null)
                        BasisFootRotationDebug.Record("R", Time.time, footIKBlendWeightRight,
                            !rightHasTracker && footIKBlendWeightRight > 0.001f && footDriverReady,
                            data.RightFoot.rotation, data.RightFootRotation, data.M_CalibrationRightFootRotation,
                            BasisLocalBoneDriver.RightFootControl.OutGoingData.rotation,
                            BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation,
                            (Quaternion)rOut[S_RightFoot], footDriverReady ? footDriver.RightFootRotation : Quaternion.identity);
                }

                // ── HIP BOB ──
                if (footIKBlendWeight > 0.001f && footDriverReady && !hipsHaveTracker)
                {
                    data.PositionHips = new Vector3(data.PositionHips.x,
                        data.PositionHips.y + footDriver.ComputeHipBob() * footIKBlendWeight,
                        data.PositionHips.z);
                }

                // ── CHEST (head hint) ──
                chestPos = pOut[S_Chest];
                chestRot = rOut[S_Chest];
                if (!trackerBendNormal)
                    chestPos = ApplyHintBias(BasisBoneTrackedRole.Chest, chestPos, chestRot);
                data.ChestPosition = chestPos;
                data.ChestRotation = chestRot;

                // ── BUTTERFLY KNEES (laying-down knee splay from tracked feet with no knee tracker) ──
                bool butterflyEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKnees.RawValue;
                float butterflyMaxOpenDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKneeMaxOpenDeg.RawValue;
                float butterflySupineFloor = 1f; // merged toggle: butterfly knees works both supine and upright when enabled
                Vector3 playerUpDir = BasisLocalPlayer.localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
                bool leftFootTracked = fbtEnabled && BasisLocalBoneDriver.LeftFootControl.HasTracked == BasisHasTracked.HasTracker;
                bool rightFootTracked = fbtEnabled && BasisLocalBoneDriver.RightFootControl.HasTracked == BasisHasTracked.HasTracker;

                // ── LEFT LOWER LEG ──
                if (leftLLHasTracker)
                {
                    Vector3 lllPos = pOut[S_LeftLowerLeg];
                    Quaternion lllRot = rOut[S_LeftLowerLeg];
                    if (!trackerBendNormal)
                        lllPos = ApplyHintBias(BasisBoneTrackedRole.LeftLowerLeg, lllPos, lllRot);
                    data.PositionLeftLowerLeg = lllPos;
                    data.RotationLeftLowerLeg = lllRot;
                    data.EnableLeftLowerLeg = 1f;
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    Quaternion targetRotL = ComputeKneeHintRotation(data.PositionHips, data.LeftFootPosition, footDriver.LeftKneeHint);
                    float kneeRotAlpha = 1f - Mathf.Exp(-8f * deltaTime);
                    smoothedLeftKneeRot = Quaternion.Slerp(smoothedLeftKneeRot, targetRotL, kneeRotAlpha);
                    data.PositionLeftLowerLeg = footDriver.LeftKneeHint;
                    data.RotationLeftLowerLeg = smoothedLeftKneeRot;
                    data.EnableLeftLowerLeg = footIKBlendWeightLeft;
                }
                else if (butterflyEnabled && leftFootTracked && TryComputeButterflyKnee(
                    true, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime,
                    data.LeftUpperLeg, data.LeftLowerLeg, data.LeftFootPosition, data.LeftFootRotation,
                    ref smoothedLeftButterflyHint, ref smoothedLeftButterflyWeight,
                    out Vector3 lButterflyHint, out Quaternion lButterflyRot, out float lButterflyWeight))
                {
                    data.PositionLeftLowerLeg = lButterflyHint;
                    data.RotationLeftLowerLeg = lButterflyRot;
                    data.EnableLeftLowerLeg = lButterflyWeight;
                }
                else
                {
                    data.EnableLeftLowerLeg = 0f;
                }

                // ── RIGHT LOWER LEG ──
                if (rightLLHasTracker)
                {
                    Vector3 rllPos = pOut[S_RightLowerLeg];
                    Quaternion rllRot = rOut[S_RightLowerLeg];
                    if (!trackerBendNormal)
                        rllPos = ApplyHintBias(BasisBoneTrackedRole.RightLowerLeg, rllPos, rllRot);
                    data.PositionRightLowerLeg = rllPos;
                    data.RotationRightLowerLeg = rllRot;
                    data.EnableRightLowerLeg = 1f;
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    Quaternion targetRotR = ComputeKneeHintRotation(data.PositionHips, data.RightFootPosition, footDriver.RightKneeHint);
                    float kneeRotAlpha = 1f - Mathf.Exp(-8f * deltaTime);
                    smoothedRightKneeRot = Quaternion.Slerp(smoothedRightKneeRot, targetRotR, kneeRotAlpha);
                    data.PositionRightLowerLeg = footDriver.RightKneeHint;
                    data.RotationRightLowerLeg = smoothedRightKneeRot;
                    data.EnableRightLowerLeg = footIKBlendWeightRight;
                }
                else if (butterflyEnabled && rightFootTracked && TryComputeButterflyKnee(
                    false, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime,
                    data.RightUpperLeg, data.RightLowerLeg, data.RightFootPosition, data.RightFootRotation,
                    ref smoothedRightButterflyHint, ref smoothedRightButterflyWeight,
                    out Vector3 rButterflyHint, out Quaternion rButterflyRot, out float rButterflyWeight))
                {
                    data.PositionRightLowerLeg = rButterflyHint;
                    data.RotationRightLowerLeg = rButterflyRot;
                    data.EnableRightLowerLeg = rButterflyWeight;
                }
                else
                {
                    data.EnableRightLowerLeg = 0f;
                }

                if (BasisLegCrouchDebug.Enabled)
                {
                    if (data.LeftUpperLeg != null && data.LeftLowerLeg != null && data.leftFoot != null)
                    {
                        Vector3 hipL = data.LeftUpperLeg.position, kneeL = data.LeftLowerLeg.position;
                        float legLenL = Vector3.Distance(hipL, kneeL) + Vector3.Distance(kneeL, data.leftFoot.position);
                        BasisLegCrouchDebug.Record("L", Time.time, !leftHasTracker && footIKBlendWeightLeft > 0.001f && footDriverReady,
                            legLenL, hipL, data.LeftFootPosition, data.PositionLeftLowerLeg, kneeL);
                    }
                    if (data.RightUpperLeg != null && data.RightLowerLeg != null && data.RightFoot != null)
                    {
                        Vector3 hipR = data.RightUpperLeg.position, kneeR = data.RightLowerLeg.position;
                        float legLenR = Vector3.Distance(hipR, kneeR) + Vector3.Distance(kneeR, data.RightFoot.position);
                        BasisLegCrouchDebug.Record("R", Time.time, !rightHasTracker && footIKBlendWeightRight > 0.001f && footDriverReady,
                            legLenR, hipR, data.RightFootPosition, data.PositionRightLowerLeg, kneeR);
                    }
                }

                // ── HANDS ──
                data.PositionLeftHand = pOut[S_LeftHand];
                data.RotationLeftHand = rOut[S_LeftHand];
                data.PositionRightHand = pOut[S_RightHand];
                data.RotationRightHand = rOut[S_RightHand];

                // ── LOWER ARMS (elbow hints) ──
                // NOTE: no ApplyHintBias here -- a tracker-local lower-arm offset swings with forearm pronation
                // (the forearm rolls about its own axis) and keys off a solver-overwritten bone, which pops the
                // elbow. The knees keep their bias only because the knee is a hinge. Elbow-tracker conditioning
                // is handled solver-side (BasisArmSolveCore HintIsTracker), not by a tracker-local offset.
                llaPos = pOut[S_LeftLowerArm];
                llaRot = rOut[S_LeftLowerArm];
                data.LeftLowerArmPosition = llaPos;
                data.LeftLowerArmRotation = llaRot;

                rlaPos = pOut[S_RightLowerArm];
                rlaRot = rOut[S_RightLowerArm];
                data.RightLowerArmPosition = rlaPos;
                data.RightLowerArmRotation = rlaRot;

                // ── TOES ──
                data.OutGoingLeftToePosition = pOut[S_LeftToe];
                data.OutGoingLeftToeRotation = rOut[S_LeftToe];
                data.OutGoingRightToePosition = pOut[S_RightToe];
                data.OutGoingRightToeRotation = rOut[S_RightToe];

                // ── SHOULDERS (rotation only) ──
                data.LeftShoulderRotation = rOut[S_LeftShoulder];
                data.RightShoulderRotation = rOut[S_RightShoulder];
            }

            // ── DERIVED BEND PREFS ──
            Vector3 fwdC = chestRot * Vector3.forward;
            Vector3 outC = chestRot * Vector3.right;
            Vector3 upC = chestRot * Vector3.up;

            Vector3 fwd = hipsRot * Vector3.forward;
            Vector3 outR = hipsRot * Vector3.right;
            Vector3 up = hipsRot * Vector3.up;
            Vector3 hipsRight = hipsRot * Vector3.right;
            if (trackerBendNormal)
            {
                data.KneeBendPrefLeft = (leftLLHasTracker && BasisBendNormalStore.TryGet(BasisBoneTrackedRole.LeftLowerLeg, out var leftAxis))
                    ? BasisTrackerBendNormalCore.ResolveWorldNormal(BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData.rotation, leftAxis, hipsRight)
                    : hipsRight;
                data.KneeBendPrefRight = (rightLLHasTracker && BasisBendNormalStore.TryGet(BasisBoneTrackedRole.RightLowerLeg, out var rightAxis))
                    ? BasisTrackerBendNormalCore.ResolveWorldNormal(BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData.rotation, rightAxis, hipsRight)
                    : hipsRight;
            }
            else
            {
                data.KneeBendPrefLeft = hipsRight;
                data.KneeBendPrefRight = hipsRight;
            }
            data.SpineBendNormal = (fwd * spineBendNormalWeights.x
                + outR * spineBendNormalWeights.y
                + up * spineBendNormalWeights.z).normalized;

            // Pull the latest tunable settings into data every frame so slider changes flow into
            // the IK job. Without this the job runs on the boot-time snapshot from Spine().
            ApplyTuningSettings(ref data);

            BasisFullIKConstraint.data = data;
            Builder.SyncLayers();
            PlayableGraph.Evaluate(deltaTime);

            // Developer diagnostics: after the graph solves, sample the live head/hips/feet solve
            // (target fed to IK, calibrated offset, predicted product, observed bone pose) plus the
            // live avatar roots, so the runtime flip can be observed rather than only predicted.
            if (BasisCalibrationDebugRecorder.RuntimeActive)
            {
                BasisCalibrationDebugRecorder.RuntimeBone("head", BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation, data.m_CalibratedRotationHead, BasisLocalAvatarDriver.Mapping.head);
                BasisCalibrationDebugRecorder.RuntimeBone("hips", BasisLocalBoneDriver.HipsControl.OutgoingWorldData.rotation, data.OffsetRotationHips, BasisLocalAvatarDriver.Mapping.Hips);
                BasisCalibrationDebugRecorder.RuntimeBone("leftFoot", BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation, data.M_CalibrationLeftFootRotation, BasisLocalAvatarDriver.Mapping.leftFoot);
                BasisCalibrationDebugRecorder.RuntimeBone("rightFoot", BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation, data.M_CalibrationRightFootRotation, BasisLocalAvatarDriver.Mapping.rightFoot);
                Transform animRoot = localPlayer?.BasisAvatar?.Animator != null ? localPlayer.BasisAvatar.Animator.transform : null;
                BasisCalibrationDebugRecorder.RuntimeEndFrame(localPlayer != null ? localPlayer.transform : null, animRoot);
            }

            // Arm-IK jitter capture: log the solved shoulder/elbow/hand + the IK inputs (hand target, elbow
            // hint) each frame so a held-still capture shows which one actually moves. No-op unless armed.
            if (BasisArmIKRuntimeRecorder.Active)
            {
                var armMap = BasisLocalAvatarDriver.Mapping;
                BasisArmIKRuntimeRecorder.Sample(
                    armMap.leftUpperArm, armMap.leftLowerArm, armMap.leftHand,
                    armMap.RightUpperArm, armMap.RightLowerArm, armMap.rightHand,
                    data.PositionLeftHand, data.PositionRightHand,
                    data.LeftLowerArmPosition, data.RightLowerArmPosition,
                    data.HintWeightLeftHand, data.HintWeightRightHand);
            }
        }
        [SerializeField] private Vector3 spineBendNormalWeights = new Vector3(1f, 0f, 0f);
        public static Vector3 ApplyHintBias(BasisBoneTrackedRole hintRole, Vector3 rawPos, Quaternion rawRot)
        {
            if (BasisHintBiasStore.TryGet(hintRole, out var localOffset))
            {
                return rawPos + rawRot * localOffset;
            }

            return rawPos;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ExpAlpha(float hz, float dt)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(0.0001f, hz) * Mathf.Max(0.000001f, dt));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateEuroSettings()
        {
            float strength = Mathf.Max(1f, SmoothingStrength);
            float effectiveMinCutoff = MinCutoff / strength;
            float effectiveDCutoff = DerivativeCutoff / strength;

            // Position filters
            fPosHips.minCutoff = effectiveMinCutoff; fPosHips.beta = Beta; fPosHips.dCutoff = effectiveDCutoff;
            fPosHead.minCutoff = effectiveMinCutoff; fPosHead.beta = Beta; fPosHead.dCutoff = effectiveDCutoff;
            fPosLeftFoot.minCutoff = effectiveMinCutoff; fPosLeftFoot.beta = Beta; fPosLeftFoot.dCutoff = effectiveDCutoff;
            fPosRightFoot.minCutoff = effectiveMinCutoff; fPosRightFoot.beta = Beta; fPosRightFoot.dCutoff = effectiveDCutoff;
            fPosChest.minCutoff = effectiveMinCutoff; fPosChest.beta = Beta; fPosChest.dCutoff = effectiveDCutoff;
            fPosLeftLowerLeg.minCutoff = effectiveMinCutoff; fPosLeftLowerLeg.beta = Beta; fPosLeftLowerLeg.dCutoff = effectiveDCutoff;
            fPosRightLowerLeg.minCutoff = effectiveMinCutoff; fPosRightLowerLeg.beta = Beta; fPosRightLowerLeg.dCutoff = effectiveDCutoff;
            fPosLeftHand.minCutoff = effectiveMinCutoff; fPosLeftHand.beta = Beta; fPosLeftHand.dCutoff = effectiveDCutoff;
            fPosRightHand.minCutoff = effectiveMinCutoff; fPosRightHand.beta = Beta; fPosRightHand.dCutoff = effectiveDCutoff;
            fPosLeftLowerArm.minCutoff = effectiveMinCutoff; fPosLeftLowerArm.beta = Beta; fPosLeftLowerArm.dCutoff = effectiveDCutoff;
            fPosRightLowerArm.minCutoff = effectiveMinCutoff; fPosRightLowerArm.beta = Beta; fPosRightLowerArm.dCutoff = effectiveDCutoff;
            fPosLeftToe.minCutoff = effectiveMinCutoff; fPosLeftToe.beta = Beta; fPosLeftToe.dCutoff = effectiveDCutoff;
            fPosRightToe.minCutoff = effectiveMinCutoff; fPosRightToe.beta = Beta; fPosRightToe.dCutoff = effectiveDCutoff;

            // Rotation filters
            fRotHips.minCutoff = effectiveMinCutoff; fRotHips.beta = Beta; fRotHips.dCutoff = effectiveDCutoff;
            fRotHead.minCutoff = effectiveMinCutoff; fRotHead.beta = Beta; fRotHead.dCutoff = effectiveDCutoff;
            fRotLeftFoot.minCutoff = effectiveMinCutoff; fRotLeftFoot.beta = Beta; fRotLeftFoot.dCutoff = effectiveDCutoff;
            fRotRightFoot.minCutoff = effectiveMinCutoff; fRotRightFoot.beta = Beta; fRotRightFoot.dCutoff = effectiveDCutoff;
            fRotChest.minCutoff = effectiveMinCutoff; fRotChest.beta = Beta; fRotChest.dCutoff = effectiveDCutoff;
            fRotLeftLowerLeg.minCutoff = effectiveMinCutoff; fRotLeftLowerLeg.beta = Beta; fRotLeftLowerLeg.dCutoff = effectiveDCutoff;
            fRotRightLowerLeg.minCutoff = effectiveMinCutoff; fRotRightLowerLeg.beta = Beta; fRotRightLowerLeg.dCutoff = effectiveDCutoff;
            fRotLeftHand.minCutoff = effectiveMinCutoff; fRotLeftHand.beta = Beta; fRotLeftHand.dCutoff = effectiveDCutoff;
            fRotRightHand.minCutoff = effectiveMinCutoff; fRotRightHand.beta = Beta; fRotRightHand.dCutoff = effectiveDCutoff;
            fRotLeftLowerArm.minCutoff = effectiveMinCutoff; fRotLeftLowerArm.beta = Beta; fRotLeftLowerArm.dCutoff = effectiveDCutoff;
            fRotRightLowerArm.minCutoff = effectiveMinCutoff; fRotRightLowerArm.beta = Beta; fRotRightLowerArm.dCutoff = effectiveDCutoff;
            fRotLeftToe.minCutoff = effectiveMinCutoff; fRotLeftToe.beta = Beta; fRotLeftToe.dCutoff = effectiveDCutoff;
            fRotRightToe.minCutoff = effectiveMinCutoff; fRotRightToe.beta = Beta; fRotRightToe.dCutoff = effectiveDCutoff;
            fRotLeftShoulder.minCutoff = effectiveMinCutoff; fRotLeftShoulder.beta = Beta; fRotLeftShoulder.dCutoff = effectiveDCutoff;
            fRotRightShoulder.minCutoff = effectiveMinCutoff; fRotRightShoulder.beta = Beta; fRotRightShoulder.dCutoff = effectiveDCutoff;
        }
        private void OnPlayersHeightChangedNextFrame(HeightModeChange HeightModeChange)
        {
            var Data = BasisFullIKConstraint.data;
            SetHandCollisionScale(ref Data, BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale);
            BasisFullIKConstraint.data = Data;
        }
        public static void SetHandCollisionScale(ref BasisFullBodyData BodyData, float Scale)
        {
            // Pull the live slider values so a height change keeps tuning consistent with
            // ApplyTuningSettings (which does the same per-frame).
            BodyData.HandSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue * Scale;
            BodyData.HandRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue * Scale;
            BodyData.ChestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue * Scale;
            BodyData.CollisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue * Scale;

            var hips = BasisLocalBoneDriver.HipsControl.TposeLocalScaled;
            var spine = BasisLocalBoneDriver.SpineControl.TposeLocalScaled;
            var chest = BasisLocalBoneDriver.ChestControl.TposeLocalScaled;

            var neck = BasisLocalBoneDriver.NeckControl.TposeLocalScaled;
            var head = BasisLocalBoneDriver.HeadControl.TposeLocalScaled;


            float minHeadSpineHeight = 0f;
            minHeadSpineHeight += Vector3.Distance(hips.position, spine.position);
            minHeadSpineHeight += Vector3.Distance(spine.position, chest.position);
            minHeadSpineHeight += Vector3.Distance(chest.position, neck.position);
            minHeadSpineHeight += Vector3.Distance(neck.position, head.position);

            BodyData.minHeadSpineHeight = minHeadSpineHeight;
        }
        public void Spine(GameObject mainRig)
        {
            if (localPlayer == null || mainRig == null)
            {
                return;
            }

            BasisAnimationRiggingHelper.CreateBasisFullBodyRIG(localPlayer,  mainRig, basisTransformMapping, out BasisFullIKConstraint);

            BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnPlayersHeightChangedNextFrame;
            OnPlayersHeightChangedNextFrame( HeightModeChange.OnTpose);

            var data = BasisFullIKConstraint.data;

            // Legs enabled by presence
            BasisLocalBoneDriver.LeftFootControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnableLeftLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnableLeftLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);

            BasisLocalBoneDriver.RightFootControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnableRightLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnableRightLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);

            BasisLocalBoneDriver.LeftLowerLegControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnableLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnableLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);

            BasisLocalBoneDriver.RightLowerLegControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnableRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnableRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);

            // Toes
            BasisLocalBoneDriver.LeftToeControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.LeftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);
                BasisFullIKConstraint.data = d;
            };
            data.LeftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);

            BasisLocalBoneDriver.RightToeControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.RightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);
                BasisFullIKConstraint.data = d;
            };
            data.RightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);

            // Hands
            BasisLocalBoneDriver.LeftHandControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnabledLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftHandControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftHandControl);

            BasisLocalBoneDriver.RightHandControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnabledRightHand = HasRigLayer(BasisLocalBoneDriver.RightHandControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledRightHand = HasRigLayer(BasisLocalBoneDriver.RightHandControl);

            // Lower arms (hand hints)
            BasisLocalBoneDriver.LeftLowerArmControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.HintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
                BasisFullIKConstraint.data = d;
            };
            data.HintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);

            BasisLocalBoneDriver.RightLowerArmControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.HintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
                BasisFullIKConstraint.data = d;
            };
            data.HintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);

            // Chest (head hint)
            BasisLocalBoneDriver.ChestControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.WeightChest = HasRigLayer(BasisLocalBoneDriver.ChestControl);
                BasisFullIKConstraint.data = d;
            };
            data.WeightChest = HasRigLayer(BasisLocalBoneDriver.ChestControl);

            // Chest (head hint)
            BasisLocalBoneDriver.LeftShoulderControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);

            // Chest (head hint)
            BasisLocalBoneDriver.RightShoulderControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);

            // Initialize offsets and weights per humanoid bone
            int totalBones = BasisFullBodyData.Count;
            for (int slot = 0; slot < totalBones; slot++)
            {
                var bone = (HumanBodyBones)slot;
                var t = ResolveHumanoidBoneTransform(bone);
                if (t == null)
                {
                    continue;
                }

                data.SetWeight(slot, false);
                data.SetOffsetRotation(slot, t.rotation);
                data.SetTargetRotation(slot, t.rotation);
            }
            data.MinFactor = 0.95f;
            data.MaxFactor = 1.05f;
            ApplyTuningSettings(ref data);

            BasisFullIKConstraint.data = data;
        }

        // Pulls every live-tunable BasisSettingsBinding into the IK data. Called from Spine() at
        // init AND from SimulateIKDestinations every frame so slider changes flow into the
        // animation job. Without the per-frame call, sliders update RawValue but the IK keeps
        // running on the boot-time snapshot.
        // Issue #531: FBT-recalibrated per-effector rotation offsets. CreateBasisFullBodyRIG captures
        // these once at rig build against the pre-calibration frame; a one-shot runtime write to the
        // [SyncSceneToStream] data field does NOT persist (it reverts to the serialized setup value),
        // so FullBodyCalibration stashes the freshly recomputed values here and ApplyTuningSettings
        // re-applies them every frame — the same persistent path the tuning sliders use. Cleared on
        // rig (re)build so a new avatar uses its own setup capture until the user calibrates.
        public static bool HasRecalibratedRotationOffsets;
        public static Quaternion RecalibratedHead, RecalibratedHips, RecalibratedChest;
        public static Quaternion RecalibratedLeftFoot, RecalibratedRightFoot;
        public static Quaternion RecalibratedLeftToe, RecalibratedRightToe;
        public static Quaternion RecalibratedLeftShoulder, RecalibratedRightShoulder;

        private static void ApplyTuningSettings(ref BasisFullBodyData data)
        {
            data.MaxBendDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxBendDeg.RawValue;
            data.StruggleStart = Basis.BasisUI.BasisSettingsDefaults.FBIKStruggleStart.RawValue;
            data.StruggleEnd = Basis.BasisUI.BasisSettingsDefaults.FBIKStruggleEnd.RawValue;
            data.MaxChestDelta = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxChestDelta.RawValue;
            data.MaxHipDelta = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxHipDelta.RawValue;
            data.SpineBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendPitch.RawValue;
            data.SpineBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendYaw.RawValue;
            data.SpineBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendRoll.RawValue;
            data.UpperChestBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendPitch.RawValue;
            data.UpperChestBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendYaw.RawValue;
            data.UpperChestBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendRoll.RawValue;
            data.HipHingeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeStartDeg.RawValue;
            data.HipHingeMaxAddDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeMaxAddDeg.RawValue;
            data.ChestSpringHz = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringHz.RawValue;
            data.ChestSpringDamping = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringDamping.RawValue;
            data.HipFrameSpringHz = Basis.BasisUI.BasisSettingsDefaults.FBIKHipFrameSpringHz.RawValue;
            data.HipFrameSpringDamping = Basis.BasisUI.BasisSettingsDefaults.FBIKHipFrameSpringDamping.RawValue;
            data.ElbowFlareMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowFlareMaxDeg.RawValue;
            data.ElbowFlareInwardGain = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowFlareInwardGain.RawValue;
            data.ElbowFlareFullRollDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowFlareFullRollDeg.RawValue;
            data.SpineMaxForwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxForwardDeg.RawValue;
            data.SpineMaxBackwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxBackwardDeg.RawValue;
            data.SpineMaxLateralDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxLateralDeg.RawValue;
            data.SpineSquishBoost = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineSquishBoost.RawValue;
            data.MoveBodyBackWhenCrouching = Basis.BasisUI.BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching.RawValue;
            data.SwingSmoothRateDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowSwingEnabled.RawValue
                ? Basis.BasisUI.BasisSettingsDefaults.FBIKSwingSmoothRate.RawValue
                : 0f;
            data.SpineCCDRelax = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineCCDRelax.RawValue;
            data.SpineTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTwistKeep.RawValue;
            data.SpineNeckTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineNeckTwistKeep.RawValue;
            data.NeckMaxConeDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckMaxConeDeg.RawValue;
            data.ChestArmSwingFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingFactor.RawValue;
            data.ChestArmSwingMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingMaxDeg.RawValue;
            data.LowerArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKLowerArmTwistFraction.RawValue;
            data.UpperArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperArmTwistFraction.RawValue;
            data.AnatDifferentialStiffness = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatDifferentialStiffness.RawValue;
            data.AnatShoulderSlide = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatShoulderSlide.RawValue;
            data.AnatCervicalLordosis = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatCervicalLordosis.RawValue;
            data.AnatPelvicTwistRouting = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatPelvicTwistRouting.RawValue;
            data.LegSwivelSmoothing = Basis.BasisUI.BasisSettingsDefaults.FBIKLegSwivelSmoothing.RawValue;
            data.LordosisPitchGainDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisPitchGainDeg.RawValue;
            data.LordosisBaseDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisBaseDeg.RawValue;
            data.LordosisNeckShare = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisNeckShare.RawValue;
            data.LordosisMaxHeadPitchDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisMaxHeadPitchDeg.RawValue;
            data.LordosisExtremeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeStartDeg.RawValue;
            data.LordosisExtremeFullDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeFullDeg.RawValue;
            data.LordosisExtremeRollForwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollForwardMaxDeg.RawValue;
            data.LordosisExtremeRollBackwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollBackwardMaxDeg.RawValue;
            data.LordosisExtremeHipsHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalMax.RawValue;
            data.LordosisExtremeChestHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalMax.RawValue;
            data.LordosisExtremeHipsDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownMax.RawValue;
            data.LordosisExtremeChestDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownMax.RawValue;
            data.LordosisExtremeHipsDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownLookUp.RawValue;
            data.LordosisExtremeChestDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownLookUp.RawValue;

            // Toggles + shoulder-solve params that previously only flowed at init. Without these
            // here, flipping the matching toggle/slider in the IK panel left the animation job
            // running on the boot-time snapshot.
            data.CollisionsEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionsEnabled.RawValue;
            data.ProtectElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKProtectElbow.RawValue;
            data.CollideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;
            data.UseHandCapsule = Basis.BasisUI.BasisSettingsDefaults.FBIKUseHandCapsule.RawValue;
            data.ShoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            data.ShoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            data.ShoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;

            // Collision capsule dimensions × avatar scale. Slider defaults now match the
            // hardcoded values previously in SetHandCollisionScale, so this is the canonical path.
            float collisionScale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            data.HandRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue * collisionScale;
            data.HandSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue * collisionScale;
            data.ChestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue * collisionScale;
            data.CollisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue * collisionScale;

            if (HasRecalibratedRotationOffsets)
            {
                data.m_CalibratedRotationHead = RecalibratedHead;
                data.OffsetRotationHips = RecalibratedHips;
                data.m_CalibratedRotationChest = RecalibratedChest;
                data.M_CalibrationLeftFootRotation = RecalibratedLeftFoot;
                data.M_CalibrationRightFootRotation = RecalibratedRightFoot;
                data.m_CalibratedRotationLeftToe = RecalibratedLeftToe;
                data.m_CalibratedRotationRightToe = RecalibratedRightToe;
                data.m_CalibratedRotationLeftShoulder = RecalibratedLeftShoulder;
                data.m_CalibratedRotationRightShoulder = RecalibratedRightShoulder;
            }
        }
        public void DisableAllTrackers()
        {
            if (BasisFullIKConstraint != null)
            {
                var data = BasisFullIKConstraint.data;
                data.EnableLeftLeg = 0f;
                data.EnableRightLeg = 0f;
                data.EnableLeftLowerLeg = 0f;
                data.EnableRightLowerLeg = 0f;
                data.LeftToeEnabled = false;
                data.RightToeEnabled = false;
                // data.EnabledLeftHand = false;
                // data.EnabledRightHand = false;
                data.HintWeightLeftHand = false;
                data.HintWeightRightHand = false;
                data.WeightChest = false;
                data.HasHipsTracker = false;
                data.EnabledLeftShoulder = false;
                data.EnabledRightShoulder = false;
                BasisFullIKConstraint.data = data;
            }
        }
        /// <summary>
        /// Re-applies the full-body IK constraint weights from each bone's current rig-layer
        /// state — the inverse of <see cref="DisableAllTrackers"/>. PutAvatarIntoTPose disables
        /// these so the T-pose read isn't dragged by trackers; FullBodyCalibration restores them
        /// as a side effect of (re)assigning roles, but any flow that enters/exits T-pose WITHOUT
        /// a full calibration must call this or the arm hints / chest / shoulders / legs stay
        /// stuck at zero weight (the avatar and controller arms look broken until the next
        /// calibrate). HasHipsTracker is omitted on purpose — the per-frame Simulate recomputes it.
        /// </summary>
        public void RestoreAllTrackers()
        {
            if (BasisFullIKConstraint != null)
            {
                var data = BasisFullIKConstraint.data;
                data.EnableLeftLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);
                data.EnableRightLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);
                data.EnableLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);
                data.EnableRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);
                data.LeftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);
                data.RightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);
                data.EnabledLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftHandControl);
                data.EnabledRightHand = HasRigLayer(BasisLocalBoneDriver.RightHandControl);
                data.HintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
                data.HintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
                data.WeightChest = HasRigLayer(BasisLocalBoneDriver.ChestControl);
                data.EnabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
                data.EnabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
                BasisFullIKConstraint.data = data;
            }
        }
        private static bool HasRigLayer(BasisLocalBoneControl control)
        {
            return control.HasRigLayer == BasisHasRigLayer.HasRigLayer;
        }

        private static float HasRigLayerFloat(BasisLocalBoneControl control)
        {
            return control.HasRigLayer == BasisHasRigLayer.HasRigLayer ? 1f : 0f;
        }

        /// <summary>
        /// Compute a knee bend normal from the hip→foot→kneeHint triangle.
        /// The normal of this triangle defines the plane the knee should bend in.
        /// Falls back to the provided default if the triangle is degenerate.
        /// </summary>
        private static Vector3 ComputeKneeBendNormal(Vector3 hip, Vector3 foot, Vector3 kneeHint, Vector3 fallback)
        {
            Vector3 hipToFoot = foot - hip;
            Vector3 hipToKnee = kneeHint - hip;
            Vector3 normal = Vector3.Cross(hipToFoot, hipToKnee);
            return normal.sqrMagnitude > 1e-8f ? normal.normalized : fallback;
        }

        /// <summary>
        /// Compute a smooth rotation for the knee hint from the hip-knee-foot triangle.
        /// Forward = knee→foot direction, Up = derived from the bend plane.
        /// This prevents snapping that occurs with Quaternion.identity.
        /// </summary>
        private static Quaternion ComputeKneeHintRotation(Vector3 hip, Vector3 foot, Vector3 kneeHint)
        {
            Vector3 kneeToFoot = foot - kneeHint;
            Vector3 kneeToHip = hip - kneeHint;

            if (kneeToFoot.sqrMagnitude < 1e-8f || kneeToHip.sqrMagnitude < 1e-8f)
                return Quaternion.identity;

            // Forward: along the shin (knee toward foot)
            Vector3 fwd = kneeToFoot.normalized;

            // Up: perpendicular to the bend plane, pointing away from the bend
            Vector3 bendNormal = Vector3.Cross(kneeToHip, kneeToFoot);
            Vector3 up = Vector3.Cross(fwd, bendNormal);

            if (up.sqrMagnitude < 1e-8f)
                up = Vector3.up;
            else
                up.Normalize();

            return Quaternion.LookRotation(fwd, up);
        }

        /// <summary>
        /// Butterfly knees: laying on your back with a foot tracker but no knee tracker, the tracked foot tilts
        /// outward (soles toward each other) and pulls in toward the pelvis, so the knee should fall open
        /// laterally. Computes the outward knee pole via <see cref="BasisButterflyKneeCore"/>, smoothed to avoid
        /// pops. Returns false (and the knee falls back to the default sagittal bend) when the pose isn't a
        /// butterfly. The open angle is clamped to the hip's natural max-open inside the core.
        /// </summary>
        private static bool TryComputeButterflyKnee(
            bool isLeft, Quaternion hipsRot, Vector3 playerUp, float maxOpenDeg, float supineFloor, float dt,
            Transform upperLeg, Transform lowerLeg, Vector3 footPos, Quaternion footRot,
            ref Vector3 smoothedHint, ref float smoothedWeight,
            out Vector3 hintPos, out Quaternion hintRot, out float weight)
        {
            hintPos = default;
            hintRot = Quaternion.identity;
            weight = 0f;
            if (upperLeg == null || lowerLeg == null)
            {
                smoothedWeight = 0f;
                return false;
            }

            Vector3 hipPos = upperLeg.position;
            Vector3 hipsRight = hipsRot * Vector3.right;
            Vector3 hipsForward = hipsRot * Vector3.forward;

            BasisButterflyKneeInput input;
            input.HipPosition = hipPos;
            input.FootPosition = footPos;
            input.FootInstepDir = footRot * Vector3.up;          // foot "up" = instep normal (the sole faces -this)
            input.OutwardDir = isLeft ? -hipsRight : hipsRight;
            input.DefaultBendDir = hipsForward;                  // sagittal knee dir (belly; toward ceiling when supine)
            input.PlayerUp = playerUp;
            input.TorsoFacingDir = hipsForward;                  // belly . playerUp -> on-your-back factor
            input.UpperLength = Vector3.Distance(hipPos, lowerLeg.position);
            input.LowerLength = Vector3.Distance(lowerLeg.position, footPos);
            input.MaxOpenDeg = maxOpenDeg;
            input.Strength = 1f;
            input.SupineFloor = supineFloor;

            BasisButterflyKneeCore.Solve(input, out BasisButterflyKneeResult result);

            // Smooth the pole + weight so noisy tilt / recline signals can't pop the knee.
            float alpha = 1f - Mathf.Exp(-ButterflyKneeSmoothRate * dt);
            if (smoothedWeight <= 0.0001f && result.HintWeight <= 0.0001f)
            {
                // Fully inactive: track the rest pole so we don't lerp a stale hint in on the next engage.
                smoothedHint = result.KneeHint;
                smoothedWeight = 0f;
                return false;
            }
            smoothedHint = Vector3.Lerp(smoothedHint, result.KneeHint, alpha);
            smoothedWeight = Mathf.Lerp(smoothedWeight, result.HintWeight, alpha);

            if (smoothedWeight <= 0.001f)
            {
                return false;
            }

            hintPos = smoothedHint;
            hintRot = ComputeKneeHintRotation(hipPos, footPos, smoothedHint);
            weight = smoothedWeight;
            return true;
        }

        public GameObject CreateOrGetRig(string role, bool enabled, out Rig rig, out RigLayer rigLayer)
        {
            rig = null;
            rigLayer = default;

            if (localPlayer?.BasisAvatar?.Animator == null)
            {
                return null;
            }

            if (Builder != null)
            {
                foreach (var layer in Builder.layers)
                {
                    if (layer?.rig != null && layer.rig.name == $"Rig {role}")
                    {
                        rig = layer.rig;
                        rigLayer = layer;
                        return layer.rig.gameObject;
                    }
                }
            }

            var anim = localPlayer.BasisAvatar.Animator;
            GameObject rigGO = BasisAnimationRiggingHelper.CreateAndSetParent(anim.transform, $"Rig {role}");

            rig = BasisHelpers.GetOrAddComponent<Rig>(rigGO);
            rigLayer = new RigLayer(rig, enabled);

            if (Builder == null)
            {
                Builder = BasisHelpers.GetOrAddComponent<RigBuilder>(anim.gameObject);
            }

            Builder.layers.Add(rigLayer);

            return rigGO;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideUsage(HumanBodyBones bone, bool enabled)
        {
            var data = BasisFullIKConstraint.data;
            data.SetWeight((int)bone, enabled);
            BasisFullIKConstraint.data = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideData(HumanBodyBones bone, in Vector3 position, in Quaternion rotation)
        {
            var data = BasisFullIKConstraint.data;
            data.SetTargetPosition((int)bone, position);
            data.SetTargetRotation((int)bone, rotation);
            BasisFullIKConstraint.data = data;
        }
        private Transform ResolveHumanoidBoneTransform(HumanBodyBones bone)
        {
            // Prefer references map if available
            if (BasisLocalAvatarDriver.Mapping != null && BasisLocalAvatarDriver.Mapping.GetTransform(bone, out Transform refT))
            {
                return refT;
            }
            // Fallback to Animator
            var animator = localPlayer?.BasisAvatar?.Animator;
            return animator != null ? animator.GetBoneTransform(bone) : null;
        }
    }
}
