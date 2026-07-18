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
using UnityEngine.Jobs;
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

        [System.NonSerialized] public PlayableGraph PlayableGraph;
        [System.NonSerialized] public readonly BasisPoseSkeleton PoseSkeleton = new BasisPoseSkeleton();
        [System.NonSerialized] public BasisFullIKConstraintJob IKJob;
        [System.NonSerialized] public bool IKJobCreated;
        public GameObject MainRig;
        public bool RigLayerActive = true;
        public static bool DebugPoseStream;
        int _poseChecksRemaining;
        public BasisFullBodyIK BasisFullIKConstraint;

        /// <summary>
        /// The FBIK hand target offsets (landmark frame -> hand bone frame), as plain quaternions.
        ///
        /// MediaPipe needs these to cancel FBIK's offset -- it emits an already-finished BONE rotation, so the
        /// solve's own `target * offset` would apply the palm->bone map a second time. But BasisFullBodyIK derives
        /// from RigConstraint&lt;,,&gt;, so reading `.data` from another package forces com.basis.mediapipe to take a hard
        /// dependency on Unity.Animation.Rigging just to fetch two quaternions. Handing them out from here -- inside
        /// the assembly that already references Rigging -- keeps that dependency where it belongs.
        ///
        /// Identity when there is no constraint yet, which is the correct no-op: an uncalibrated offset must not
        /// rotate anything.
        /// </summary>
        public Quaternion LeftHandIKOffset => BasisFullIKConstraint != null ? BasisFullIKConstraint.data.m_CalibratedRotationLeftHand : Quaternion.identity;
        public Quaternion RightHandIKOffset => BasisFullIKConstraint != null ? BasisFullIKConstraint.data.m_CalibratedRotationRightHand : Quaternion.identity;

        private BasisLocalPlayer localPlayer;
        private BasisTransformMapping basisTransformMapping;

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

        // Smoothed butterfly-knee hint (laying-down knee splay from tracked feet; see BasisButterflyKneeCore)
        private static Vector3 smoothedLeftButterflyHint, smoothedRightButterflyHint;
        private static float smoothedLeftButterflyWeight, smoothedRightButterflyWeight;
        private const float ButterflyKneeSmoothRate = 8f;

        // Smoothed knee-forward hint (upright knee azimuth following the tracked foot's toe; see BasisKneeForwardCore)
        private static Vector3 smoothedLeftKneeFwdHint, smoothedRightKneeFwdHint;
        private static float smoothedLeftKneeFwdWeight, smoothedRightKneeFwdWeight;
        private const float KneeForwardSmoothRate = 10f;

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

        // ── FOOT ROTATION KILL SWITCH ──
        // false => hand SolveLegs the zero-quaternion sentinel, which makes it keep the ANIMATION's foot rotation.
        // That is the long-standing, known-good behaviour: no heel-strike / toe-off / slope adaptation, and a
        // planted foot pivots with the body -- but locomotion is guaranteed intact.
        // true  => drive the foot's rotation from the foot placement driver (SafeFootTargetRotation).
        //
        // ENABLED 2026-07-18. The prerequisites the OFF default was waiting on are now met:
        //  - the project BUILDS (dotnet build "Basis Framework.csproj" clean);
        //  - the math is TESTED (BasisFootFrameTests, 10/10 green: rest reproduces the T-pose rotation so it
        //    cannot come out toes-up, the offset pre-cancel survives the solve's own multiply, swing pitch
        //    plantarflexes at toe-off / dorsiflexes at heel-strike, NaN degrades to the sentinel);
        //  - the footAlign CAPTURE ORDERING is verified correct -- BasisLocalFootDriver.InitializeVariables()
        //    (-> CaptureFootAlignment) runs at BasisLocalAvatarDriver:229, BEFORE ResetAvatarAnimator() at :236,
        //    so it captures the flat T-pose foot (unlike the arm bake, which was the opposite order and wrong).
        // SafeFootTargetRotation still degrades to the sentinel (= this old behaviour) on any NaN/degeneracy, so
        // the floor is exactly what OFF gave. ⚠ VERIFY IN-HEADSET: stand still, arms down -- the feet must sit
        // flat and naturally toed-out, NOT toes-up/tilted; a planted foot must HOLD as you turn, not pivot.
        // Flip back to false if the un-discard misbehaves.
        // static readonly, NOT const: a const would make the ternaries below compile-time-constant and raise
        // CS0429 (unreachable expression code) under warnings-as-errors. The JIT folds this away just the same.
        private static readonly bool FootRotationFromDriver = true;

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

        // Post-IK world-pose publish: solved bones read via IJobParallelForTransform, rest via _ikFallbackControls.
        private TransformAccessArray _ikPublishTransforms;
        private BasisLocalBoneControl[] _ikPublishControls;
        private BasisLocalBoneControl[] _ikFallbackControls;
        private NativeArray<float3> _ikPublishPositions;
        private NativeArray<quaternion> _ikPublishRotations;
        public void Initialize(BasisLocalPlayer localPlayer, BasisTransformMapping references)
        {
            this.localPlayer = localPlayer;
            basisTransformMapping = references;
            timeAccumulator = 0f;
        }
        public void BuildBuilder()
        {
            if (localPlayer?.BasisAvatar?.Animator == null || BasisFullIKConstraint == null)
            {
                BasisDebug.LogError("Missing Localplayer || Avatar || Animator || constraint");
                return;
            }

            Animator animator = localPlayer.BasisAvatar.Animator;
            PlayableGraph = animator.playableGraph;
            PlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            PoseSkeleton.Build(animator.transform, CollectIKBones(BasisFullIKConstraint.data));
            PoseSkeleton.SetTranslationFree(BasisFullIKConstraint.data.hips);
            IKJob = BasisFullBodyJobBinder.Create(PoseSkeleton, ref BasisFullIKConstraint.DataRef);
            IKJobCreated = true;
            _poseChecksRemaining = 3;

            ResetSmoothingState();
        }

        public void SetBodySettings()
        {
            // Drop the prior recalibration first: a never-calibrated avatar then uses its own uncalibrated
            // (animator-relative) setup capture from CreateBasisFullBodyRIG.
            HasRecalibratedRotationOffsets = false;
            SpineProportionRatio = 1f;
            HasSpineProportionCapturePending = false;
            var rigGO = CreateOrGetRig("Main IK");
            Spine(rigGO);
            BasisLocalBoneControl.HasEvents = true;
            // Keep FBT rotation calibration across avatar swaps: re-derive this avatar's per-effector offsets
            // from the stored calibration reference. No-op until the user has calibrated.
            ApplyCalibrationToCurrentAvatar();

            BuildIKPublishArrays();
        }

        public void CleanupBeforeContinue()
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnPlayersHeightChangedNextFrame;
            DisposeFilterArrays();
            DisposeIKPublishArrays();

            if (IKJobCreated)
            {
                BasisFullBodyJobBinder.Destroy(IKJob);
                IKJob = default;
                IKJobCreated = false;
            }
            PoseSkeleton.Dispose();

            if (MainRig == null)
            {
                return;
            }

            GameObject.Destroy(MainRig);
            MainRig = null;
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
            if (currentlyTposing)
            {
                RigLayerActive = false;
                return;
            }

            RigLayerActive = true;

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

            // Per-avatar smoothing state: a new avatar must not inherit the previous one's
            // mid-flight foot-IK blend, butterfly hint/weight, or stationary hysteresis.
            smoothedLeftButterflyHint = smoothedRightButterflyHint = Vector3.zero;
            smoothedLeftButterflyWeight = smoothedRightButterflyWeight = 0f;
            smoothedLeftKneeFwdHint = smoothedRightKneeFwdHint = Vector3.zero;
            smoothedLeftKneeFwdWeight = smoothedRightKneeFwdWeight = 0f;
            footIKBlendWeightLeft = footIKBlendWeightRight = footIKBlendWeight = 0f;
            stationaryTimer = 0f;
        }
        public void SimulateIKDestinations(float deltaTime)
        {
            if (BasisFullIKConstraint == null || !IKJobCreated)
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

            if (HasSpineProportionCapturePending)
            {
                HasSpineProportionCapturePending = false;
                CaptureSpineProportion(headData.position, hipsData.position);
            }

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
            Vector3 playerUpDir = BasisLocalPlayer.localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            unsafe
            {
                float3* pOut = (float3*)_posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)_rotOutputs.GetUnsafeReadOnlyPtr();

                hipsPos = pOut[S_Hips];
                hipsRot = rOut[S_Hips];
                hipsPos -= playerUpDir * localPlayer.LocalCharacterDriver.landingCrouchEffect;
                data.PositionHips = hipsPos;
                data.RotationHips = hipsRot;
                data.HasHipsTracker = hipsHaveTracker;
                // Per frame, not just on OnHasRigChanged: the weight moves continuously while a source fades.
                data.EnabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
                data.EnabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);

                data.PositionHead = pOut[S_Head];
                data.RotationHead = rOut[S_Head];

                // ── LEFT FOOT ──
                if (leftHasTracker)
                {
                    data.LeftFootPosition = pOut[S_LeftFoot];
                    data.LeftFootRotation = rOut[S_LeftFoot];
                    // Re-assert full weight every frame: HasTracked can flip (occlusion, dropout)
                    // without firing OnHasRigChanged, and the foot-sim branch below writes fractional
                    // blend weights that would otherwise stick when the tracker returns.
                    data.EnableLeftLeg = 1f;
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    data.LeftFootPosition = footDriver.LeftFootPosition;
                    // Foot rotation is LIVE again. It used to be discarded via the zero-quaternion sentinel
                    // (-> SolveLegs kept the animation rotation) because feeding it produced a toes-up foot -- the
                    // driver was handing over a frame built from the BODY's axes, which are not the foot bone's.
                    // FootRotation() now re-seats that frame through the bone's calibrated rest orientation
                    // (footAlign), so a standing foot reproduces its rest rotation exactly. With it live we finally
                    // get: a planted foot HELD in the world (it no longer pivots as the body turns), heel-strike /
                    // toe-off through the swing, and slope adaptation.
                    // PRE-CANCEL THE CALIBRATION OFFSET. SolveLegs hands targetOffsetLeftFoot to SolveTwoBone, which
                    // applies it to the target as `target * offset` -- because the TRACKER path feeds a tracker
                    // rotation, and the offset is what maps the tracker's frame onto the bone's frame. The foot
                    // driver has no tracker: it already emits the finished BONE rotation, so that offset is pure
                    // surplus and lands the foot at footRot*offset. It is CALIBRATED PER AVATAR, which is exactly
                    // why the error is a different wrong angle on every rig instead of a constant one.
                    //
                    // Multiplying by its inverse here makes the solve's own `target * offset` collapse back to the
                    // rotation we meant: (footRot * offset^-1) * offset == footRot.
                    //
                    // This is the "toes-up" that got foot rotation switched off in the first place -- the sentinel
                    // on the zero quaternion existed to dodge this exact multiply, not to dodge a bad frame.
                    data.LeftFootRotation = FootRotationFromDriver
                        ? SafeFootTargetRotation(footDriver.LeftFootRotation, data.M_CalibrationLeftFootRotation)
                        : PreserveTipSentinel;
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
                    data.EnableRightLeg = 1f;
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    data.RightFootPosition = footDriver.RightFootPosition;
                    data.RightFootRotation = FootRotationFromDriver
                        ? SafeFootTargetRotation(footDriver.RightFootRotation, data.M_CalibrationRightFootRotation)
                        : PreserveTipSentinel;
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

                // ── HIP BOB + LATERAL SWAY + PELVIS ROTATION ──
                // All three are gated on !hipsHaveTracker: with a hip tracker the pelvis is the user's own, and
                // synthesising gait motion on top of it would fight their real body. (This is gait-driven pelvis
                // motion in the ABSENCE of a tracker -- it is not, and must not become, tracker tilt stabilisation.)
                if (footIKBlendWeight > 0.001f && footDriverReady && !hipsHaveTracker)
                {
                    data.PositionHips += playerUpDir * (footDriver.ComputeHipBob() * footIKBlendWeight);
                    data.PositionHips += footDriver.ComputeHipSway() * footIKBlendWeight;

                    // Axial rotation + frontal list, blended in by weight so it fades with the rest of foot IK.
                    Quaternion pelvis = Quaternion.Slerp(Quaternion.identity, footDriver.ComputePelvisDelta(), footIKBlendWeight);
                    data.RotationHips = pelvis * data.RotationHips;
                }

                // ── CHEST (head hint) ──
                chestPos = pOut[S_Chest];
                chestRot = rOut[S_Chest];
                // The chest IK target needs the ACTUAL chest, before the head-hint bias below (which shoves it
                // ~8cm 'up in chest frame' to steer the head solve). Pinning the chest to the biased value
                // leaned the whole torso.
                data.ChestPositionRaw = chestPos;
                if (!trackerBendNormal)
                    chestPos = ApplyHintBias(BasisBoneTrackedRole.Chest, chestPos, chestRot);
                data.ChestPosition = chestPos;
                data.ChestRotation = chestRot;

                // ── KNEE POLE (tracked feet, no knee tracker): foot-forward azimuth + butterfly splay ──
                bool butterflyEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKnees.RawValue;
                float butterflyMaxOpenDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKneeMaxOpenDeg.RawValue;
                float butterflySupineFloor = 1f; // merged toggle: butterfly knees works both supine and upright when enabled
                bool kneeFollowsFoot = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFollowsFoot.RawValue;
                float kneeFootCoupling = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootFollowUpright.RawValue;
                Vector3 hipsForwardDir = hipsRot * Vector3.forward;
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
                    data.EnableLeftLowerLeg = 1f;
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    data.PositionLeftLowerLeg = footDriver.LeftKneeHint;
                    data.EnableLeftLowerLeg = footIKBlendWeightLeft;
                }
                else if (leftFootTracked)
                {
                    Vector3 lBendDir = hipsForwardDir;
                    Vector3 lKneeFwdHint = default;
                    float lKneeFwdWeight = 0f;
                    bool lHaveKneeFwd = kneeFollowsFoot && TryComputeKneeForward(
                        hipsRot, kneeFootCoupling, playerUpDir, deltaTime,
                        data.LeftUpperLeg, data.LeftLowerLeg, data.LeftFootPosition, data.LeftFootRotation,
                        ref smoothedLeftKneeFwdHint, ref smoothedLeftKneeFwdWeight,
                        out lKneeFwdHint, out lKneeFwdWeight, out lBendDir);

                    if (butterflyEnabled && TryComputeButterflyKnee(
                        true, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime, lBendDir,
                        data.LeftUpperLeg, data.LeftLowerLeg, data.LeftFootPosition, data.LeftFootRotation,
                        ref smoothedLeftButterflyHint, ref smoothedLeftButterflyWeight,
                        out Vector3 lButterflyHint, out float lButterflyWeight))
                    {
                        data.PositionLeftLowerLeg = lButterflyHint;
                        data.EnableLeftLowerLeg = lButterflyWeight;
                    }
                    else if (lHaveKneeFwd && lKneeFwdWeight > 0.001f)
                    {
                        data.PositionLeftLowerLeg = lKneeFwdHint;
                        data.EnableLeftLowerLeg = lKneeFwdWeight;
                    }
                    else
                    {
                        data.EnableLeftLowerLeg = 0f;
                    }
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
                    data.EnableRightLowerLeg = 1f;
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    data.PositionRightLowerLeg = footDriver.RightKneeHint;
                    data.EnableRightLowerLeg = footIKBlendWeightRight;
                }
                else if (rightFootTracked)
                {
                    Vector3 rBendDir = hipsForwardDir;
                    Vector3 rKneeFwdHint = default;
                    float rKneeFwdWeight = 0f;
                    bool rHaveKneeFwd = kneeFollowsFoot && TryComputeKneeForward(
                        hipsRot, kneeFootCoupling, playerUpDir, deltaTime,
                        data.RightUpperLeg, data.RightLowerLeg, data.RightFootPosition, data.RightFootRotation,
                        ref smoothedRightKneeFwdHint, ref smoothedRightKneeFwdWeight,
                        out rKneeFwdHint, out rKneeFwdWeight, out rBendDir);

                    if (butterflyEnabled && TryComputeButterflyKnee(
                        false, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime, rBendDir,
                        data.RightUpperLeg, data.RightLowerLeg, data.RightFootPosition, data.RightFootRotation,
                        ref smoothedRightButterflyHint, ref smoothedRightButterflyWeight,
                        out Vector3 rButterflyHint, out float rButterflyWeight))
                    {
                        data.PositionRightLowerLeg = rButterflyHint;
                        data.EnableRightLowerLeg = rButterflyWeight;
                    }
                    else if (rHaveKneeFwd && rKneeFwdWeight > 0.001f)
                    {
                        data.PositionRightLowerLeg = rKneeFwdHint;
                        data.EnableRightLowerLeg = rKneeFwdWeight;
                    }
                    else
                    {
                        data.EnableRightLowerLeg = 0f;
                    }
                }
                else
                {
                    data.EnableRightLowerLeg = 0f;
                }

                // Tell the leg solve which knee poles are physical trackers (jittery, and pole-amplified by
                // the solve) so it applies the responsive output-swivel smoothing on that path. Computed hints
                // (foot driver / butterfly) are already smooth and stay untouched.
                data.LeftLowerLegHintIsTracker = leftLLHasTracker;
                data.RightLowerLegHintIsTracker = rightLLHasTracker;

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
                data.OutGoingLeftToeRotation = rOut[S_LeftToe];
                data.OutGoingRightToeRotation = rOut[S_RightToe];

                // ── SHOULDERS (rotation only) ──
                data.LeftShoulderRotation = rOut[S_LeftShoulder];
                data.RightShoulderRotation = rOut[S_RightShoulder];
            }

            // ── DERIVED BEND PREFS ──
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
            // Pull the latest tunable settings into data every frame so slider changes flow into
            // the IK job. Without this the job runs on the boot-time snapshot from Spine().
            ApplyTuningSettings(ref data);

            BasisFullIKConstraint.data = data;
            PlayableGraph.Evaluate(deltaTime);
            RunIKSolve(deltaTime);

            // Publish each bone control's post-IK world pose (the rendered bone) into IKWorldData so consumers can
            // follow the solved bone instead of the pre-IK target. Bones with no solved transform fall back to
            // OutgoingWorldData.
            PublishIKWorldData();

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
        public static Vector3 ApplyHintBias(BasisBoneTrackedRole hintRole, Vector3 rawPos, Quaternion rawRot)
        {
            if (BasisHintBiasStore.TryGet(hintRole, out var localOffset))
            {
                return rawPos + rawRot * localOffset;
            }

            return rawPos;
        }

        private void PublishIKWorldData()
        {
            if (!_ikPublishTransforms.isCreated || _ikPublishControls == null
                || _ikPublishTransforms.length != _ikPublishControls.Length)
            {
                PublishIKWorldDataMainThread();
                return;
            }

            if (_ikPublishControls.Length > 0)
            {
                new BasisReadBoneWorldPoseJob
                {
                    Positions = _ikPublishPositions,
                    Rotations = _ikPublishRotations,
                }.Schedule(_ikPublishTransforms).Complete();

                for (int i = 0; i < _ikPublishControls.Length; i++)
                {
                    _ikPublishControls[i].SetIKWorldData(_ikPublishPositions[i], _ikPublishRotations[i]);
                }
            }

            var fallback = _ikFallbackControls;
            for (int i = 0; i < fallback.Length; i++)
            {
                var world = fallback[i].OutgoingWorldData;
                fallback[i].SetIKWorldData(world.position, world.rotation);
            }
        }

        private void BuildIKPublishArrays()
        {
            DisposeIKPublishArrays();

            var m = BasisLocalAvatarDriver.Mapping;
            if (m == null) return;

            (BasisLocalBoneControl control, Transform bone, bool has)[] entries =
            {
                (BasisLocalBoneDriver.HeadControl, m.head, m.Hashead),
                (BasisLocalBoneDriver.NeckControl, m.neck, m.Hasneck),
                (BasisLocalBoneDriver.ChestControl, m.chest, m.Haschest),
                (BasisLocalBoneDriver.SpineControl, m.spine, m.Hasspine),
                (BasisLocalBoneDriver.HipsControl, m.Hips, m.HasHips),

                (BasisLocalBoneDriver.LeftShoulderControl, m.leftShoulder, m.HasleftShoulder),
                (BasisLocalBoneDriver.LeftLowerArmControl, m.leftLowerArm, m.HasleftLowerArm),
                (BasisLocalBoneDriver.LeftHandControl, m.leftHand, m.HasleftHand),
                (BasisLocalBoneDriver.RightShoulderControl, m.RightShoulder, m.HasRightShoulder),
                (BasisLocalBoneDriver.RightLowerArmControl, m.RightLowerArm, m.HasRightLowerArm),
                (BasisLocalBoneDriver.RightHandControl, m.rightHand, m.HasrightHand),

                (BasisLocalBoneDriver.LeftUpperLegControl, m.LeftUpperLeg, m.HasLeftUpperLeg),
                (BasisLocalBoneDriver.LeftLowerLegControl, m.LeftLowerLeg, m.HasLeftLowerLeg),
                (BasisLocalBoneDriver.LeftFootControl, m.leftFoot, m.HasleftFoot),
                (BasisLocalBoneDriver.LeftToeControl, m.leftToe, m.HasleftToes),
                (BasisLocalBoneDriver.RightUpperLegControl, m.RightUpperLeg, m.HasRightUpperLeg),
                (BasisLocalBoneDriver.RightLowerLegControl, m.RightLowerLeg, m.HasRightLowerLeg),
                (BasisLocalBoneDriver.RightFootControl, m.rightFoot, m.HasrightFoot),
                (BasisLocalBoneDriver.RightToeControl, m.rightToe, m.HasrightToes),

                (BasisLocalBoneDriver.EyeControl, null, false),
                (BasisLocalBoneDriver.MouthControl, null, false),
            };

            var solvedTransforms = new List<Transform>(entries.Length);
            var solvedControls = new List<BasisLocalBoneControl>(entries.Length);
            var fallbackControls = new List<BasisLocalBoneControl>(4);

            foreach (var e in entries)
            {
                if (e.control == null) continue;
                if (e.has && e.bone != null)
                {
                    solvedTransforms.Add(e.bone);
                    solvedControls.Add(e.control);
                }
                else
                {
                    fallbackControls.Add(e.control);
                }
            }

            _ikPublishControls = solvedControls.ToArray();
            _ikFallbackControls = fallbackControls.ToArray();
            _ikPublishTransforms = new TransformAccessArray(solvedTransforms.ToArray());
            _ikPublishPositions = new NativeArray<float3>(_ikPublishControls.Length, Allocator.Persistent);
            _ikPublishRotations = new NativeArray<quaternion>(_ikPublishControls.Length, Allocator.Persistent);
        }

        private void DisposeIKPublishArrays()
        {
            if (_ikPublishTransforms.isCreated) _ikPublishTransforms.Dispose();
            if (_ikPublishPositions.IsCreated) _ikPublishPositions.Dispose();
            if (_ikPublishRotations.IsCreated) _ikPublishRotations.Dispose();
            _ikPublishControls = null;
            _ikFallbackControls = null;
        }

        // Publishes every bone control's post-IK world pose (the rendered bone) into IKWorldData. Uses the solved
        // animator transform when the avatar has that bone; otherwise falls back to the pre-IK OutgoingWorldData
        // (center-eye, mouth, or any bone the avatar lacks).
        private static void PublishIKWorldDataMainThread()
        {
            var m = BasisLocalAvatarDriver.Mapping;
            if (m == null) return;

            PublishBoneIK(BasisLocalBoneDriver.HeadControl, m.head, m.Hashead);
            PublishBoneIK(BasisLocalBoneDriver.NeckControl, m.neck, m.Hasneck);
            PublishBoneIK(BasisLocalBoneDriver.ChestControl, m.chest, m.Haschest);
            PublishBoneIK(BasisLocalBoneDriver.SpineControl, m.spine, m.Hasspine);
            PublishBoneIK(BasisLocalBoneDriver.HipsControl, m.Hips, m.HasHips);

            PublishBoneIK(BasisLocalBoneDriver.LeftShoulderControl, m.leftShoulder, m.HasleftShoulder);
            PublishBoneIK(BasisLocalBoneDriver.LeftLowerArmControl, m.leftLowerArm, m.HasleftLowerArm);
            PublishBoneIK(BasisLocalBoneDriver.LeftHandControl, m.leftHand, m.HasleftHand);
            PublishBoneIK(BasisLocalBoneDriver.RightShoulderControl, m.RightShoulder, m.HasRightShoulder);
            PublishBoneIK(BasisLocalBoneDriver.RightLowerArmControl, m.RightLowerArm, m.HasRightLowerArm);
            PublishBoneIK(BasisLocalBoneDriver.RightHandControl, m.rightHand, m.HasrightHand);

            PublishBoneIK(BasisLocalBoneDriver.LeftUpperLegControl, m.LeftUpperLeg, m.HasLeftUpperLeg);
            PublishBoneIK(BasisLocalBoneDriver.LeftLowerLegControl, m.LeftLowerLeg, m.HasLeftLowerLeg);
            PublishBoneIK(BasisLocalBoneDriver.LeftFootControl, m.leftFoot, m.HasleftFoot);
            PublishBoneIK(BasisLocalBoneDriver.LeftToeControl, m.leftToe, m.HasleftToes);
            PublishBoneIK(BasisLocalBoneDriver.RightUpperLegControl, m.RightUpperLeg, m.HasRightUpperLeg);
            PublishBoneIK(BasisLocalBoneDriver.RightLowerLegControl, m.RightLowerLeg, m.HasRightLowerLeg);
            PublishBoneIK(BasisLocalBoneDriver.RightFootControl, m.rightFoot, m.HasrightFoot);
            PublishBoneIK(BasisLocalBoneDriver.RightToeControl, m.rightToe, m.HasrightToes);

            // No humanoid transform for these — publish the pre-IK world pose so IKWorldData is still valid.
            PublishBoneIK(BasisLocalBoneDriver.EyeControl, null, false);
            PublishBoneIK(BasisLocalBoneDriver.MouthControl, null, false);
        }

        private static void PublishBoneIK(BasisLocalBoneControl control, Transform bone, bool has)
        {
            if (control == null) return;
            if (has && bone != null)
            {
                bone.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                control.SetIKWorldData(position, rotation);
            }
            else
            {
                var world = control.OutgoingWorldData;
                control.SetIKWorldData(world.position, world.rotation);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ExpAlpha(float hz, float dt)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(0.0001f, hz) * Mathf.Max(0.000001f, dt));
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

            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnPlayersHeightChangedNextFrame;
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
                d.EnabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);

            BasisLocalBoneDriver.RightHandControl.OnHasRigChanged += (hasRig) =>
            {
                var d = BasisFullIKConstraint.data;
                d.EnabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
                BasisFullIKConstraint.data = d;
            };
            data.EnabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);

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

            // Initialize offsets and weights per override slot. Slots are HumanBodyBones values:
            // 0..20 plus UpperChest (54) — NOT a contiguous 0..Count range, which would touch
            // LeftEye (21, silently ignored) and skip UpperChest entirely.
            for (int i = 0; i < BasisFullBodyData.Count; i++)
            {
                int slot = i <= (int)HumanBodyBones.RightToes ? i : (int)HumanBodyBones.UpperChest;
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
        // Per-avatar spine proportion match. SpineProportionRatio = wearer torso / avatar torso, captured once
        // after a hips-tracker calibration (transient; SetBodySettings resets it). AppliedSpineProportion is
        // the clamped scale actually baked into the local avatar (via a humanoid rebuild) AND sent to remotes;
        // it PERSISTS across the rebuild's re-calibration and is only reset on a genuine new-avatar load.
        // SpineProportionApplied guards the one-time rebuild. 1 = matched / off (a no-op).
        public static float SpineProportionRatio = 1f;
        public static float AppliedSpineProportion = 1f;
        public static bool SpineProportionApplied;
        public static bool HasSpineProportionCapturePending;
        public static Quaternion RecalibratedHead, RecalibratedHips, RecalibratedChest;
        public static Quaternion RecalibratedLeftFoot, RecalibratedRightFoot;
        public static Quaternion RecalibratedLeftToe, RecalibratedRightToe;
        public static Quaternion RecalibratedLeftShoulder, RecalibratedRightShoulder;

        // Snapshots the wearer's real torso against the avatar's torso the frame after a hips-tracker
        // calibration, as a straight head-to-hips ratio. headWorld/hipsWorld are the tracker-driven bone
        // outputs (the real torso the trackers impose); TposeLocalScaled is the avatar's scaled rest pose
        // (the avatar torso) -- both are current-scale world metres, so the ratio is scale-free. With no
        // hips tracker the pelvis is synthesized, not pinned, so the correction does not apply: left at 1.
        private static void CaptureSpineProportion(Vector3 headWorld, Vector3 hipsWorld)
        {
            SpineProportionRatio = 1f;
            if (BasisLocalBoneDriver.HipsControl == null
                || BasisLocalBoneDriver.HipsControl.HasTracked != BasisHasTracked.HasTracker)
            {
                return;
            }
            Vector3 headRest = BasisLocalBoneDriver.HeadControl.TposeLocalScaled.position;
            Vector3 hipsRest = BasisLocalBoneDriver.HipsControl.TposeLocalScaled.position;
            float avatarTorso = Vector3.Distance(headRest, hipsRest);
            float userTorso = Vector3.Distance(headWorld, hipsWorld);
            SpineProportionRatio = BasisSpineProportionCore.ComputeRatio(userTorso, avatarTorso);

            // ==== SPINE PROPORTION DEFORMATION DISABLED 2026-07-18 (revisit later). Uncomment to re-enable the
            //      one-time local humanoid Avatar rebuild that bakes the clamped scale into the spine. ====
            // if (SpineProportionApplied
            //     || !Basis.BasisUI.BasisSettingsDefaults.FBIKSpineProportionMatch.RawValue)
            // {
            //     return;
            // }
            // float scale = BasisSpineProportionCore.ComputeScale(
            //     SpineProportionRatio, Basis.BasisUI.BasisSettingsDefaults.FBIKSpineProportionMaxScale.RawValue);
            // if (Mathf.Abs(scale - 1f) < k_SpineProportionRebuildThreshold)
            // {
            //     return;
            // }
            // AppliedSpineProportion = scale;
            // SpineProportionApplied = true;
            // Basis.Scripts.Device_Management.BasisDeviceManagement.EnqueueOnMainThread(() =>
            //     BasisLocalPlayer.Instance?.LocalAvatarDriver?.RebuildAvatarForSpineProportion(scale));
        }
        // Below this a rebuild is not worth the hitch -- the avatar already matches the wearer closely enough.
        const float k_SpineProportionRebuildThreshold = 0.01f;

        private static void ApplyTuningSettings(ref BasisFullBodyData data)
        {
            // The IK job reads PlayerUp for the hip hinge, crouch offset, arm solve and elbow protect.
            // Nothing ever assigned it, so it sat at the SetDefaultValues world up while the foot driver
            // used the real root up -- the two halves of the solve disagreed whenever the root was tilted
            // (play-space flip, seats/vehicles). Identical to Vector3.up for an upright root.
            Vector3 rootUp = BasisLocalPlayer.localToWorldMatrix.MultiplyVector(Vector3.up);
            data.PlayerUp = rootUp.sqrMagnitude > 1e-8f ? rootUp.normalized : Vector3.up;
            data.MaxBendDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxBendDeg.RawValue;
            data.MaxChestDelta = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxChestDelta.RawValue;
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
            data.SpineMaxForwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxForwardDeg.RawValue;
            data.SpineMaxBackwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxBackwardDeg.RawValue;
            data.SpineMaxLateralDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxLateralDeg.RawValue;
            data.SpineSquishBoost = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineSquishBoost.RawValue;
            data.SpineGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineGazeFollow.RawValue;
            data.NeckGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckGazeFollow.RawValue;
            data.MoveBodyBackWhenCrouching = Basis.BasisUI.BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching.RawValue;
            data.SwingSmoothRateDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowSwingEnabled.RawValue
                ? Basis.BasisUI.BasisSettingsDefaults.FBIKSwingSmoothRate.RawValue
                : 0f;
            data.SpineCCDRelax = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineCCDRelax.RawValue;
            data.SpineTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTwistKeep.RawValue;
            data.SpineNeckTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineNeckTwistKeep.RawValue;
            // ==== SPINE PROPORTION DEFORMATION DISABLED 2026-07-18 (revisit later). Uncomment to broadcast the
            //      applied spine scale so remotes re-space their copy by the same amount. ====
            // Basis.Scripts.Networking.BasisSpineProportionNetworking.UpdateLocalScale(AppliedSpineProportion);
            data.NeckMaxConeDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckMaxConeDeg.RawValue;
            data.ChestArmSwingFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingFactor.RawValue;
            data.ChestArmSwingMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingMaxDeg.RawValue;
            data.LowerArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKLowerArmTwistFraction.RawValue;
            data.UpperArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperArmTwistFraction.RawValue;
            data.AnatDifferentialStiffness = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatDifferentialStiffness.RawValue;
            data.AnatShoulderSlide = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatShoulderSlide.RawValue;
            data.AnatCervicalLordosis = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatCervicalLordosis.RawValue;
            data.AnatPelvicTwistRouting = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatPelvicTwistRouting.RawValue;
            data.SpineAnatomicalRom = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineAnatomicalRom.RawValue;
            data.ChestIKTarget = Basis.BasisUI.BasisSettingsDefaults.FBIKChestIKTarget.RawValue;
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
            data.UseNeuralPole = Basis.BasisUI.BasisSettingsDefaults.FBIKNeuralPole.RawValue;
            data.CollideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;
            data.ShoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            data.ShoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
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
                data.EnabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
                data.EnabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
                data.HintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
                data.HintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
                data.WeightChest = HasRigLayer(BasisLocalBoneDriver.ChestControl);
                data.EnabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
                data.EnabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
                BasisFullIKConstraint.data = data;
            }
        }
        /// <summary>
        /// The zero quaternion. SolveLegs reads it as "position-only foot IK": it keeps the foot's pre-solve
        /// (animation) rotation. It is the system's existing, well-defined "I have no usable rotation for you".
        /// </summary>
        public static readonly Quaternion PreserveTipSentinel = new Quaternion(0f, 0f, 0f, 0f);

        /// <summary>
        /// The foot target rotation to hand SolveLegs, with the per-avatar calibration offset pre-cancelled --
        /// or the preserve-tip sentinel if the result is not a usable rotation.
        ///
        /// WHY THIS EXISTS: a NaN here does not degrade the rig, it KILLS it. SolveLegs decides "no rotation
        /// supplied" with `sqrMagnitude(tRot) &lt; 0.5f` -- and NaN &lt; 0.5f is FALSE, so a NaN target does not trip
        /// that guard. It flows into SolveTwoBone, NaNs the leg bone rotations, and from there the rig never
        /// recovers: zeroing EnableLeftLeg only stops us WRITING, it cannot un-poison what is already written.
        /// That is exactly "the legs stop falling back to the animator when I move, and never come back".
        ///
        /// Two ways a NaN gets in, and both are guarded:
        ///   - the OFFSET is degenerate. A serialized Quaternion defaults to (0,0,0,0), not identity, and
        ///     Quaternion.Inverse divides by the squared norm -- inverting it yields NaN. There is a real window
        ///     for this: BasisAnimationRiggingHelper only assigns the offset when the avatar HAS that foot mapped,
        ///     and recalibration/avatar-swap rewrite it live.
        ///   - the foot driver's own rotation is degenerate (a LookRotation on a collapsed frame).
        ///
        /// NOTE THE COMPARISON SHAPE: `!(x > k)`, never `x &lt; k`. NaN compares false to EVERYTHING, so a `&lt;` test
        /// ACCEPTS NaN. That is precisely the bug in SolveLegs' preserveTip check, and the first version of this
        /// guard repeated it. Negating a `>` rejects NaN, zero and denormals in one test.
        ///
        /// Falling back to the SENTINEL rather than identity matters: identity would hand the solve a confidently
        /// WRONG foot rotation, while the sentinel restores exactly the old, known-good behaviour (the animation's
        /// foot rotation). Foot rotation degrades; walking never breaks.
        /// </summary>
        public static Quaternion SafeFootTargetRotation(Quaternion footRot, Quaternion offset)
        {
            float offSqr = offset.x * offset.x + offset.y * offset.y + offset.z * offset.z + offset.w * offset.w;
            if (!(offSqr > 0.5f)) return PreserveTipSentinel;

            Quaternion result = footRot * Quaternion.Inverse(offset);
            float resSqr = result.x * result.x + result.y * result.y + result.z * result.z + result.w * result.w;
            if (!(resSqr > 0.5f)) return PreserveTipSentinel;

            return result;
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
        /// Hand IK weight. Unlike the other limbs this is not a straight on/off: the layer must be there AND the
        /// producer says how far in it is, so a source that comes and goes (webcam tracking) can fade rather than
        /// pop. Clamped, and written so a NaN weight collapses to 0 instead of reaching the Burst job.
        /// </summary>
        private static float HandRigWeight(BasisLocalBoneControl control)
        {
            if (control == null || control.HasRigLayer != BasisHasRigLayer.HasRigLayer) return 0f;
            float w = control.RigLayerWeight;
            return w > 0f ? (w < 1f ? w : 1f) : 0f;
        }

        /// <summary>
        /// Butterfly knees: laying on your back with a foot tracker but no knee tracker, the tracked foot tilts
        /// outward (soles toward each other) and pulls in toward the pelvis, so the knee should fall open
        /// laterally. Computes the outward knee pole via <see cref="BasisButterflyKneeCore"/>, smoothed to avoid
        /// pops. Returns false (and the knee falls back to the default sagittal bend) when the pose isn't a
        /// butterfly. The open angle is clamped to the hip's natural max-open inside the core.
        /// </summary>
        private static bool TryComputeButterflyKnee(
            bool isLeft, Quaternion hipsRot, Vector3 playerUp, float maxOpenDeg, float supineFloor, float dt, Vector3 defaultBendDir,
            Transform upperLeg, Transform lowerLeg, Vector3 footPos, Quaternion footRot,
            ref Vector3 smoothedHint, ref float smoothedWeight,
            out Vector3 hintPos, out float weight)
        {
            hintPos = default;
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
            input.DefaultBendDir = defaultBendDir.sqrMagnitude > 1e-6f ? defaultBendDir : hipsForward; // foot-corrected sagittal base (BasisKneeForwardCore); falls back to belly
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
            weight = smoothedWeight;
            return true;
        }

        /// <summary>
        /// Knee-forward azimuth: with a tracked foot but no knee tracker, aim the knee pole along the FOOT's toe
        /// direction instead of straight body-forward, so turning a foot turns the knee. See
        /// <see cref="BasisKneeForwardCore"/> for the standing-vs-supine model. Outputs the sagittal bend direction
        /// (feeds butterfly's default bend) plus a knee hint pole for the non-butterfly path, smoothed to shave
        /// foot-tracker yaw jitter.
        /// </summary>
        private static bool TryComputeKneeForward(
            Quaternion hipsRot, float coupling, Vector3 playerUp, float dt,
            Transform upperLeg, Transform lowerLeg, Vector3 footPos, Quaternion footRot,
            ref Vector3 smoothedBendDir, ref float smoothedWeight,
            out Vector3 hintPos, out float weight, out Vector3 bendDir)
        {
            hintPos = default;
            weight = 0f;
            bendDir = hipsRot * Vector3.forward;
            if (upperLeg == null || lowerLeg == null)
            {
                smoothedWeight = 0f;
                return false;
            }

            Vector3 hipPos = upperLeg.position;

            BasisKneeForwardInput input;
            input.HipPosition = hipPos;
            input.FootPosition = footPos;
            input.FootForwardDir = footRot * Vector3.forward;    // foot toe direction
            input.BodyForwardDir = hipsRot * Vector3.forward;
            input.PlayerUp = playerUp;
            input.UpperLength = Vector3.Distance(hipPos, lowerLeg.position);
            input.Coupling = coupling;
            input.Strength = 1f;

            BasisKneeForwardCore.Solve(input, out BasisKneeForwardResult result);

            float alpha = 1f - Mathf.Exp(-KneeForwardSmoothRate * dt);
            if (smoothedBendDir.sqrMagnitude < 1e-6f)
                smoothedBendDir = result.BendDir;
            else
                smoothedBendDir = Vector3.Slerp(smoothedBendDir.normalized, result.BendDir, alpha);
            smoothedWeight = Mathf.Lerp(smoothedWeight, result.HintWeight, alpha);

            bendDir = smoothedBendDir.sqrMagnitude > 1e-6f ? smoothedBendDir.normalized : result.BendDir;

            Vector3 mid = (hipPos + footPos) * 0.5f;
            float radius = input.UpperLength > 1e-5f ? input.UpperLength : 0.4f;
            hintPos = mid + bendDir * radius;
            weight = smoothedWeight;
            return weight > 0.001f;
        }

        public GameObject CreateOrGetRig(string role)
        {
            if (localPlayer?.BasisAvatar?.Animator == null)
            {
                return null;
            }

            var anim = localPlayer.BasisAvatar.Animator;
            MainRig = BasisAnimationRiggingHelper.CreateAndSetParent(anim.transform, $"Rig {role}");
            return MainRig;
        }

        static List<Transform> CollectIKBones(BasisFullBodyData d) => new List<Transform>
        {
            d.hips, d.spine, d.chest, d.upperChest, d.neck, d.head,
            d.LeftShoulder, d.RightShoulder,
            d.leftUpperArm, d.leftLowerArm, d.LeftHand,
            d.RightUpperArm, d.RightLowerArm, d.RightHand,
            d.LeftUpperArmTwist, d.LeftLowerArmTwist,
            d.RightUpperArmTwist, d.RightLowerArmTwist,
            d.LeftUpperLeg, d.LeftLowerLeg, d.leftFoot,
            d.RightUpperLeg, d.RightLowerLeg, d.RightFoot,
            d.LeftToe, d.RightToe,
        };

        void RunIKSolve(float deltaTime)
        {
            if (!RigLayerActive || !IKJobCreated || !PoseSkeleton.IsCreated)
            {
                return;
            }

            PoseSkeleton.ScheduleGather().Complete();

            if (DebugPoseStream || Basis.BasisUI.BasisSettingsDefaults.DevPoseStreamDebug.RawValue || _poseChecksRemaining > 0)
            {
                if (_poseChecksRemaining > 0)
                {
                    _poseChecksRemaining--;
                }
                BasisDebug.Log($"[PoseStream] {PoseSkeleton.ValidateAgainstTransforms()}");
            }

            BasisFullBodyJobBinder.Sync(ref IKJob, ref BasisFullIKConstraint.DataRef);
            IKJob.Stream = PoseSkeleton.Stream;
            IKJob.Stream.deltaTime = deltaTime;
            IKJob.jobWeight = 1f;
            IKJob.Run();

            PoseSkeleton.ScheduleScatter().Complete();
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
