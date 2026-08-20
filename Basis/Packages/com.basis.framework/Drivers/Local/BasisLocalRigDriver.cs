using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Basis.IK;
using UnityEngine.Jobs;
using UnityEngine.Playables;
using static Basis.Scripts.Avatar.BasisAvatarIKStageCalibration;
using static BasisHeightDriver;
using Basis.Scripts.Debugging;
using Unity.Profiling;
namespace Basis.Scripts.Drivers
{
    [Serializable]
    public partial class BasisLocalRigDriver
    {
        public static float MinCutoff = 5.5f, Beta = 3.25f, DerivativeCutoff = 3f, SmoothingStrength = 1f;
        [System.NonSerialized] public PlayableGraph PlayableGraph;
        [System.NonSerialized] public readonly BasisPoseSkeleton PoseSkeleton = new BasisPoseSkeleton();
        [System.NonSerialized] public readonly BasisLocomotionPoseSystem LocomotionPose = new BasisLocomotionPoseSystem();
        [System.NonSerialized] public BasisEerieMovement IKJob;
        [System.NonSerialized] public bool IKJobCreated;
        public bool RigLayerActive = true;
        [System.NonSerialized] public bool IKDataReady;
        // Scheduled FBIK solve state. SimulateIKDestinations schedules the solve to a worker and
        // returns; CompleteIKSolve (BasisLocalPlayer.FinishSimulate, after the remote-side stages
        // in BasisEventDriver) joins it and runs the scatter/publish tail. Anything that touches
        // PoseSkeleton.Stream or the job's native arrays outside that window must call
        // CompleteSolveIfPending first.
        JobHandle ikSolveHandle;
        bool ikSolveScheduled, ikScatterPending, ikPublishPending;
        public Quaternion LeftHandIKOffset => IKDataReady ? IKJob.offsetRotationLeftHand : Quaternion.identity;
        public Quaternion RightHandIKOffset => IKDataReady ? IKJob.offsetRotationRightHand : Quaternion.identity;
        private BasisLocalPlayer localPlayer;
        public BasisTransformMapping basisTransformMapping;
        // Keep this order stable forever.
        // These indices drive your toggle arrays AND which filter instance is used.
        public const int S_Hips = 0, S_Head = 1, S_LeftFoot = 2, S_RightFoot = 3, S_Chest = 4, S_LeftLowerLeg = 5, S_RightLowerLeg = 6, S_LeftHand = 7, S_RightHand = 8, S_LeftLowerArm = 9, S_RightLowerArm = 10, S_LeftToe = 11, S_RightToe = 12, S_LeftShoulder = 13, S_RightShoulder = 14, SlotCount = 15;
        static readonly string[] SlotNames =
        {
            "Hips", "Head", "LeftFoot", "RightFoot", "Chest", "LeftLowerLeg", "RightLowerLeg", "LeftHand", "RightHand", "LeftLowerArm", "RightLowerArm", "LeftToe", "RightToe", "LeftShoulder", "RightShoulder",
        };
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void WatchdogCheckFilterSlots(string stage)
        {
            if (!BasisFiniteWatchdog.Enabled || !posInputs.IsCreated)
            {
                return;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                string slot = i < SlotNames.Length ? SlotNames[i] : i.ToString();
                if (BasisFiniteWatchdog.IsNonFinite((Vector3)posInputs[i]))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK raw position input, slot '{slot}' (bone control OutGoingData, playspace local)", posInputs[i].ToString());
                    return;
                }
                if (BasisFiniteWatchdog.IsNonFinite((Quaternion)rotInputs[i]))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK raw rotation input, slot '{slot}' (bone control OutGoingData, playspace local)", rotInputs[i].ToString());
                    return;
                }
                BasisEuroVec3State posState = euroPosStates[i];
                if (BasisFiniteWatchdog.IsNonFinite((Vector3)posState.hatX) || BasisFiniteWatchdog.IsNonFinite((Vector3)posState.hatDx))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK one-euro POSITION state latched, slot '{slot}' — raw input is finite, so this slot was poisoned on an earlier frame and can never recover", $"hatX={posState.hatX} hatDx={posState.hatDx} mode={posModeNative[i]}");
                    return;
                }
                BasisEuroQuatState rotState = euroRotStates[i];
                if (BasisFiniteWatchdog.IsNonFinite((Quaternion)rotState.prev) || BasisFiniteWatchdog.IsNonFinite((Vector3)rotState.logVecState.hatX) || BasisFiniteWatchdog.IsNonFinite((Vector3)rotState.logVecState.hatDx))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK one-euro ROTATION state latched, slot '{slot}'", $"prev={rotState.prev} hatX={rotState.logVecState.hatX} hatDx={rotState.logVecState.hatDx} mode={rotModeNative[i]}");
                    return;
                }
                if (BasisFiniteWatchdog.IsNonFinite((Vector3)fallbackPosStates[i]))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK fallback position state, slot '{slot}'", fallbackPosStates[i].ToString());
                    return;
                }
                if (BasisFiniteWatchdog.IsNonFinite((Vector3)posOutputs[i]))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK filtered position OUTPUT, slot '{slot}' — input and state are finite, so the filter produced it", $"{posOutputs[i]} mode={posModeNative[i]} tuning={posTuning[i]}");
                    return;
                }
                if (BasisFiniteWatchdog.IsNonFinite((Quaternion)rotOutputs[i]))
                {
                    BasisFiniteWatchdog.ReportValue(stage, $"IK filtered rotation OUTPUT, slot '{slot}'", $"{rotOutputs[i]} mode={rotModeNative[i]} tuning={rotTuning[i]}");
                    return;
                }
            }
        }
        System.IntPtr watchdogStreamPtr;
        string watchdogStreamStage;
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private unsafe void WatchdogCheckPoseStream(string stage)
        {
            if (!BasisFiniteWatchdog.Enabled || !PoseSkeleton.IsCreated)
            {
                return;
            }
            var stream = PoseSkeleton.Stream;
            Transform[] nodes = PoseSkeleton.Nodes;
            // A rebuild between two checks swaps the whole stream out from under them, which reads
            // as "the solve wrote garbage" when in fact the checks are looking at different buffers.
            // The allocation address is the cheapest identity the stream has.
            System.IntPtr streamPtr = (System.IntPtr)stream.LocalRotation.GetUnsafeReadOnlyPtr();
            bool bufferReplaced = watchdogStreamPtr != System.IntPtr.Zero && streamPtr != watchdogStreamPtr;
            string previousStage = watchdogStreamStage;
            watchdogStreamPtr = streamPtr;
            watchdogStreamStage = stage;
            System.Text.StringBuilder bad = null;
            int badCount = 0;
            string firstNode = null;
            for (int i = 0; i < stream.Count; i++)
            {
                bool badPosition = BasisFiniteWatchdog.IsNonFinite((Vector3)stream.LocalPosition[i]);
                bool badRotation = BasisFiniteWatchdog.IsNonFinite((Quaternion)stream.LocalRotation[i]);
                if (!badPosition && !badRotation)
                {
                    continue;
                }
                string node = nodes != null && i < nodes.Length && nodes[i] != null ? nodes[i].name : i.ToString();
                badCount++;
                if (bad == null)
                {
                    bad = new System.Text.StringBuilder(512);
                    firstNode = node;
                }
                // Every bad node, not just the first: one bad node means a solver pass wrote it,
                // while a whole chain (or the entire stream) means the buffer itself was replaced
                // or never seeded. The two need completely different fixes and the first-hit-only
                // report cannot tell them apart.
                if (badCount <= 12)
                {
                    bad.Append($"\n    [{i}] '{node}' localPosition={stream.LocalPosition[i]} localRotation={stream.LocalRotation[i]} " + $"translationFree={stream.TranslationFree[i] != 0} bindLength={stream.BindLength[i]} " + $"fitScale={PoseSkeleton.FitScale[i]} writable={System.Array.IndexOf(PoseSkeleton.WriteIndices, i) >= 0}");
                }
            }
            if (bad == null)
            {
                return;
            }
            BasisFiniteWatchdog.ReportValue(stage, $"IK pose stream, first bad node '{firstNode}'", $"{badCount}/{stream.Count} node(s) bad, fitActive={PoseSkeleton.FitActive}, " + $"scatterPending={ikScatterPending}, publishPending={ikPublishPending}, solveScheduled={ikSolveScheduled}" + (bufferReplaced ? $"\n    ** THE STREAM BUFFER WAS REPLACED since '{previousStage}' — the rig was rebuilt mid-frame, so these values are fresh allocation memory, not solve output. **" : $"\n    (same stream buffer as '{previousStage ?? "<first check>"}')") + bad);
        }
        // Smoothing enable toggles (position + rotation)
        public static bool[] SmoothPos = new bool[SlotCount], SmoothRot = new bool[SlotCount];
        // One Euro enable toggles (position + rotation)
        public static bool[] EuroPos = new bool[SlotCount], EuroRot = new bool[SlotCount];
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
        private static float footIKBlendWeightLeft = 0f, footIKBlendWeightRight = 0f;
        private static float footIKBlendWeight = 0f; // min of left/right, used for hip bob
        private const float FootIKBlendInSpeed = 20f;  // ~50ms to fully engage
        private const float FootIKBlendOutSpeed = 15f; // ~67ms to fully disengage
        private static float stationaryTimer = 0f;
        private const float StationaryDelaySeconds = 0.15f;
        private static readonly bool LocomotionFootIK = false, FootRotationFromDriver = true;
        private NativeArray<float3> posInputs, posOutputs;
        private NativeArray<quaternion> rotInputs, rotOutputs;
        private NativeArray<byte> posModeNative, rotModeNative;
        private NativeArray<float4> posTuning, rotTuning;
        private NativeArray<float3> fallbackPosStates;
        private NativeArray<quaternion> fallbackRotStates;
        private NativeArray<BasisEuroVec3State> euroPosStates;
        private NativeArray<BasisEuroQuatState> euroRotStates;
        // Post-IK world-pose publish: solved bones read via IJobParallelForTransform, rest via ikFallbackControls.
        private TransformAccessArray ikPublishTransforms;
        private BasisLocalBoneControl[] ikPublishControls, ikFallbackControls;
        private NativeArray<float3> ikPublishPositions;
        private NativeArray<quaternion> ikPublishRotations;
        public void Initialize(BasisLocalPlayer localPlayer, BasisTransformMapping references)
        {
            this.localPlayer = localPlayer;
            basisTransformMapping = references;
            timeAccumulator = 0f;
        }
        public void BuildBuilder()
        {
            if (localPlayer?.BasisAvatar?.Animator == null || !IKDataReady)
            {
                BasisDebug.LogError("Missing Localplayer || Avatar || Animator || constraint");
                return;
            }
            Animator animator = localPlayer.BasisAvatar.Animator;
            PlayableGraph = animator.playableGraph;
            PlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            LocomotionPose.CompleteIfPending();
            CompleteSolveIfPending();
            // The rebuild below disposes the pose stream and allocates a new one, so any scatter or
            // publish still owed from the solve scheduled earlier this frame refers to a buffer that
            // no longer exists. CompleteSolveIfPending only retires the job handle; left set, these
            // two send CompleteIKSolve on to scatter the FRESH stream, whose LocalRotation is
            // zero-filled allocation memory that nothing in the rebuild path writes (RefreshBodyFit
            // fills positions only). A zero quaternion is finite, so it passes every IsFinite guard
            // and only turns into NaN when Unity normalizes it composing the bone's world matrix —
            // which is the "Invalid AABB / IsFinite(distanceForSort)" storm.
            ikScatterPending = false;
            ikPublishPending = false;
            PoseSkeleton.Build(animator.transform, CollectIKBones(basisTransformMapping));
            PoseSkeleton.SetTranslationFree(basisTransformMapping.Hips);
            BasisEerieMovementSetup.Create(ref IKJob, PoseSkeleton, basisTransformMapping);
            IKJobCreated = true;
            ResetSmoothingState();
            RefreshBodyFit();
            LocomotionPose.OnRigBuilt();
        }
        public void RefreshBodyFit()
        {
            if (!PoseSkeleton.IsCreated || basisTransformMapping == null)
            {
                return;
            }
            // The locomotion pose job writes the stream on a worker; join it before the fit paths below
            // touch Stream.LocalPosition from the main thread.
            LocomotionPose.CompleteIfPending();
            CompleteSolveIfPending();
            if (!Basis.BasisUI.BasisSettingsDefaults.FBIKBodyFit.RawValue)
            {
                if (PoseSkeleton.FitActive)
                {
                    PoseSkeleton.ResetFit();
                    PoseSkeleton.WriteFittedLocalPositions();
                }
                AppliedBodyFit = BasisBodyFitResult.Identity;
                BasisBodyFitNetworking.UpdateLocalFit(in AppliedBodyFit);
                BasisLocalPlayer.Instance?.BasisLocalFootDriver?.RefreshBodyFitScale();
                return;
            }
            var measurements = new BasisBodyFitMeasurements
            {
                PlayerEyeHeight = BasisHeightDriver.PlayerEyeHeight, PlayerArmSpan = BasisHeightDriver.PlayerArmSpan, PlayerHipHeight = BasisHeightDriver.PlayerHipHeight, AvatarEyeHeight = BasisHeightDriver.AvatarEyeHeight, AvatarArmSpan = BasisHeightDriver.AvatarArmSpan, AvatarHipHeight = BasisHeightDriver.AvatarHipHeight, AvatarLegSpan = BasisHeightDriver.AvatarLegSpan, AvatarSpineSpan = BasisHeightDriver.AvatarSpineSpan, AvatarShoulderWidth = BasisHeightDriver.AvatarShoulderWidth,
                // Measure the residual against the scale that was actually applied, so the fit completes
                // that scale instead of pulling against it. Zero in the legacy height modes, which makes
                // the fit fall back to the eye ratio it has always used.
                UniformScale = BasisHeightDriver.AppliedUniformScale,
            };
            BasisBodyFitResult fit = BasisBodyFitCore.Solve( measurements, Basis.BasisUI.BasisSettingsDefaults.FBIKBodyFitMaxDeviation.RawValue);
            BasisBodyFitApply.Apply(PoseSkeleton, basisTransformMapping, fit);
            AppliedBodyFit = fit;
            // Remotes render the authored avatar unless they are told these scales — the pose channel
            // carries rotations only, never segment lengths. Send-on-change lives in the networking
            // class; this runs on every rig build and settings change, most of which are no-ops.
            BasisBodyFitNetworking.UpdateLocalFit(in fit);
            // Push the new lengths onto the bone transforms right now rather than waiting for the next
            // CompleteIKSolve scatter. Calibration captures its tracker offsets against live bone positions
            // (see BasisAvatarIKStageCalibration's one-scale-frame note), so a fit that lands a frame
            // later would leave every captured offset describing a body the avatar no longer has.
            PoseSkeleton.WriteFittedLocalPositions();
            BasisLocalPlayer.Instance?.BasisLocalFootDriver?.RefreshBodyFitScale();
            if (fit.HasArmFit)
            {
                BasisDebug.Log($"Body fit: arms scaled {fit.ArmScale:F4}", BasisDebug.LogTag.IK);
            }
            else
            {
                BasisDebug.Log($"Body fit: arms not fitted - {BasisBodyFitCore.Describe(fit.ArmStatus)}", BasisDebug.LogTag.IK);
            }
            if (fit.HasBodyFit)
            {
                BasisDebug.Log($"Body fit: legs scaled {fit.LegScale:F4}, spine scaled {fit.TorsoScale:F4}", BasisDebug.LogTag.IK);
            }
            else
            {
                BasisDebug.Log($"Body fit: legs and spine not fitted - {BasisBodyFitCore.Describe(fit.BodyStatus)}", BasisDebug.LogTag.IK);
            }
        }
        public static BasisBodyFitResult AppliedBodyFit = BasisBodyFitResult.Identity;
        public void SetBodySettings()
        {
            // Drop the prior recalibration first: a never-calibrated avatar then uses its own uncalibrated
            // (animator-relative) setup capture from CreateBasisFullBodyRIG.
            HasRecalibratedRotationOffsets = false;
            Spine();
            BasisLocalBoneControl.HasEvents = true;
            // Keep FBT rotation calibration across avatar swaps: re-derive this avatar's per-effector offsets
            // from the stored calibration reference. No-op until the user has calibrated.
            ApplyCalibrationToCurrentAvatar();
            BuildIKPublishArrays();
        }
        public void CleanupBeforeContinue()
        {
            CompleteSolveIfPending();
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnPlayersHeightChangedNextFrame;
            LocomotionPose.Dispose();
            DisposeFilterArrays();
            DisposeIKPublishArrays();
            if (IKJobCreated)
            {
                IKJob.Destroy();
                IKJob = default;
                IKJobCreated = false;
            }
            PoseSkeleton.Dispose();
            IKDataReady = false;
        }
        private void EnsureFilterArrays()
        {
            if (posInputs.IsCreated) return;
            posInputs = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            posOutputs = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            rotInputs = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            rotOutputs = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            posModeNative = new NativeArray<byte>(SlotCount, Allocator.Persistent);
            rotModeNative = new NativeArray<byte>(SlotCount, Allocator.Persistent);
            posTuning = new NativeArray<float4>(SlotCount, Allocator.Persistent);
            rotTuning = new NativeArray<float4>(SlotCount, Allocator.Persistent);
            fallbackPosStates = new NativeArray<float3>(SlotCount, Allocator.Persistent);
            fallbackRotStates = new NativeArray<quaternion>(SlotCount, Allocator.Persistent);
            euroPosStates = new NativeArray<BasisEuroVec3State>(SlotCount, Allocator.Persistent);
            euroRotStates = new NativeArray<BasisEuroQuatState>(SlotCount, Allocator.Persistent);
            // quaternion default-constructs to all-zeros which isn't a valid rotation; seed to identity.
            for (int i = 0; i < SlotCount; i++)
            {
                rotInputs[i] = quaternion.identity;
                rotOutputs[i] = quaternion.identity;
                fallbackRotStates[i] = quaternion.identity;
            }
        }
        private void DisposeFilterArrays()
        {
            if (posInputs.IsCreated) posInputs.Dispose();
            if (posOutputs.IsCreated) posOutputs.Dispose();
            if (rotInputs.IsCreated) rotInputs.Dispose();
            if (rotOutputs.IsCreated) rotOutputs.Dispose();
            if (posModeNative.IsCreated) posModeNative.Dispose();
            if (rotModeNative.IsCreated) rotModeNative.Dispose();
            if (posTuning.IsCreated) posTuning.Dispose();
            if (rotTuning.IsCreated) rotTuning.Dispose();
            if (fallbackPosStates.IsCreated) fallbackPosStates.Dispose();
            if (fallbackRotStates.IsCreated) fallbackRotStates.Dispose();
            if (euroPosStates.IsCreated) euroPosStates.Dispose();
            if (euroRotStates.IsCreated) euroRotStates.Dispose();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte PickMode(bool smoothEnabled, bool euroEnabled)
        {
            if (!smoothEnabled) return (byte)BasisFilterMode.Passthrough;
            return euroEnabled ? (byte)BasisFilterMode.Euro : (byte)BasisFilterMode.Fallback;
        }
        private static readonly float4[] groupPosTuning = new float4[BasisSmoothingProfiles.GroupCount], groupRotTuning = new float4[BasisSmoothingProfiles.GroupCount];
        private static readonly bool[] groupOff = new bool[BasisSmoothingProfiles.GroupCount];
        private static readonly BasisTrackingHardware[] groupHardware = new BasisTrackingHardware[BasisSmoothingProfiles.GroupCount];
        private static void ResolveGroupHardware()
        {
            for (int Index = 0; Index < groupHardware.Length; Index++)
            {
                groupHardware[Index] = BasisTrackingHardware.Unknown;
            }
            BasisDeviceManagement manager = BasisDeviceManagement.Instance;
            if (manager == null)
            {
                return;
            }
            var devices = manager.AllInputDevices;
            for (int Index = 0; Index < devices.Count; Index++)
            {
                BasisInput device = devices[Index];
                // Linked halves are averaged into a virtual midpoint that carries their hardware already;
                // counting them too would say nothing new.
                if (device == null || device.IsLinked)
                {
                    continue;
                }
                if (!device.TryGetRole(out BasisBoneTrackedRole role) || !BasisSmoothingProfiles.TryGetGroupForRole(role, out int group))
                {
                    continue;
                }
                if ((byte)device.TrackingHardware > (byte)groupHardware[group])
                {
                    groupHardware[group] = device.TrackingHardware;
                }
            }
        }
        private static bool AnyGroupIsAuto(Basis.BasisUI.BasisSettingsDefaults.SmoothingGroupBindings[] groups)
        {
            for (int Index = 0; Index < BasisSmoothingProfiles.GroupCount; Index++)
            {
                if (BasisSmoothingProfiles.IsAuto(groups[Index].Preset.RawValue))
                {
                    return true;
                }
            }
            return false;
        }
        private static void ResolveSmoothingGroups(float deltaTime)
        {
            var groups = Basis.BasisUI.BasisSettingsDefaults.FBIKSmoothingGroups;
            if (AnyGroupIsAuto(groups))
            {
                ResolveGroupHardware();
            }
            for (int Index = 0; Index < BasisSmoothingProfiles.GroupCount; Index++)
            {
                var bindings = groups[Index];
                string preset = bindings.Preset.RawValue;
                // Auto resolves to a real preset up front, so everything below is unchanged by it.
                if (BasisSmoothingProfiles.IsAuto(preset))
                {
                    preset = BasisSmoothingProfiles.PresetForHardware(groupHardware[Index]);
                }
                groupOff[Index] = BasisSmoothingProfiles.IsOff(preset);
                BasisSmoothingProfile profile;
                float strength;
                if (BasisSmoothingProfiles.IsCustom(preset))
                {
                    profile = new BasisSmoothingProfile( bindings.MinCutoff.RawValue, bindings.Beta.RawValue, DerivativeCutoff, bindings.PositionHz.RawValue, bindings.RotationHz.RawValue);
                    strength = Mathf.Max(1f, bindings.Strength.RawValue);
                }
                else
                {
                    if (!BasisSmoothingProfiles.TryGetPreset(preset, out profile))
                    {
                        profile = new BasisSmoothingProfile(MinCutoff, Beta, DerivativeCutoff, PositionSmoothingHz, RotationSmoothingHz);
                    }
                    strength = Mathf.Max(1f, SmoothingStrength);
                }
                float minCutoff = profile.MinCutoff / strength, dCutoff = profile.DerivativeCutoff / strength;
                groupPosTuning[Index] = new float4(minCutoff, profile.Beta, dCutoff, ExpAlpha(profile.PositionHz / strength, deltaTime));
                groupRotTuning[Index] = new float4(minCutoff, profile.Beta, dCutoff, ExpAlpha(profile.RotationHz / strength, deltaTime));
            }
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
            RestoreAllTrackers();
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
            if (euroPosStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) euroPosStates[i] = default;
            }
            if (euroRotStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) euroRotStates[i] = default;
            }
            if (fallbackRotStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) fallbackRotStates[i] = quaternion.identity;
            }
            if (fallbackPosStates.IsCreated)
            {
                for (int i = 0; i < SlotCount; i++) fallbackPosStates[i] = float3.zero;
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
            if (!ikPublishTransforms.isCreated || ikPublishControls == null || ikPublishTransforms.length != ikPublishControls.Length)
            {
                PublishIKWorldDataMainThread();
                return;
            }
            if (ikPublishControls.Length > 0)
            {
                // Read-only inline run: Schedule().Complete() on one line paid a dispatch
                // and a fence for ~17 transforms with zero overlap.
                new BasisReadBoneWorldPoseJob
                {
                    Positions = ikPublishPositions, Rotations = ikPublishRotations,
                }.RunReadOnly(ikPublishTransforms);
                for (int i = 0; i < ikPublishControls.Length; i++)
                {
                    ikPublishControls[i].SetIKWorldData(ikPublishPositions[i], ikPublishRotations[i]);
                }
            }
            var fallback = ikFallbackControls;
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
                (BasisLocalBoneDriver.HeadControl, m.head, m.Hashead), (BasisLocalBoneDriver.NeckControl, m.neck, m.Hasneck), (BasisLocalBoneDriver.ChestControl, m.chest, m.Haschest), (BasisLocalBoneDriver.SpineControl, m.spine, m.Hasspine), (BasisLocalBoneDriver.HipsControl, m.Hips, m.HasHips), (BasisLocalBoneDriver.LeftShoulderControl, m.leftShoulder, m.HasleftShoulder), (BasisLocalBoneDriver.LeftLowerArmControl, m.leftLowerArm, m.HasleftLowerArm), (BasisLocalBoneDriver.LeftHandControl, m.leftHand, m.HasleftHand), (BasisLocalBoneDriver.RightShoulderControl, m.RightShoulder, m.HasRightShoulder), (BasisLocalBoneDriver.RightLowerArmControl, m.RightLowerArm, m.HasRightLowerArm), (BasisLocalBoneDriver.RightHandControl, m.rightHand, m.HasrightHand), (BasisLocalBoneDriver.LeftUpperLegControl, m.LeftUpperLeg, m.HasLeftUpperLeg), (BasisLocalBoneDriver.LeftLowerLegControl, m.LeftLowerLeg, m.HasLeftLowerLeg), (BasisLocalBoneDriver.LeftFootControl, m.leftFoot, m.HasleftFoot), (BasisLocalBoneDriver.LeftToeControl, m.leftToe, m.HasleftToes), (BasisLocalBoneDriver.RightUpperLegControl, m.RightUpperLeg, m.HasRightUpperLeg), (BasisLocalBoneDriver.RightLowerLegControl, m.RightLowerLeg, m.HasRightLowerLeg), (BasisLocalBoneDriver.RightFootControl, m.rightFoot, m.HasrightFoot), (BasisLocalBoneDriver.RightToeControl, m.rightToe, m.HasrightToes), (BasisLocalBoneDriver.EyeControl, null, false), (BasisLocalBoneDriver.MouthControl, null, false),
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
            ikPublishControls = solvedControls.ToArray();
            ikFallbackControls = fallbackControls.ToArray();
            ikPublishTransforms = new TransformAccessArray(solvedTransforms.ToArray());
            ikPublishPositions = new NativeArray<float3>(ikPublishControls.Length, Allocator.Persistent);
            ikPublishRotations = new NativeArray<quaternion>(ikPublishControls.Length, Allocator.Persistent);
        }
        private void DisposeIKPublishArrays()
        {
            if (ikPublishTransforms.isCreated) ikPublishTransforms.Dispose();
            if (ikPublishPositions.IsCreated) ikPublishPositions.Dispose();
            if (ikPublishRotations.IsCreated) ikPublishRotations.Dispose();
            ikPublishControls = null;
            ikFallbackControls = null;
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
                bone.GetPose(out Vector3 position, out Quaternion rotation);
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
            ref BasisEerieMovement Data = ref IKJob;
            SetHandCollisionScale(ref Data, BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale);
        }
        public static void SetHandCollisionScale(ref BasisEerieMovement BodyData, float Scale)
        {
            // Pull the live slider values so a height change keeps tuning consistent with
            // ApplyTuningSettings (which does the same per-frame).
            BodyData.handSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue * Scale;
            BodyData.handRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue * Scale;
            BodyData.chestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue * Scale;
            BodyData.collisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue * Scale;
            BasisCalibratedCoords hips = BasisLocalBoneDriver.HipsControl.TposeLocalScaled, spine = BasisLocalBoneDriver.SpineControl.TposeLocalScaled, chest = BasisLocalBoneDriver.ChestControl.TposeLocalScaled, neck = BasisLocalBoneDriver.NeckControl.TposeLocalScaled, head = BasisLocalBoneDriver.HeadControl.TposeLocalScaled;
            float minHeadSpineHeight = 0f;
            minHeadSpineHeight += Vector3.Distance(hips.position, spine.position);
            minHeadSpineHeight += Vector3.Distance(spine.position, chest.position);
            minHeadSpineHeight += Vector3.Distance(chest.position, neck.position);
            minHeadSpineHeight += Vector3.Distance(neck.position, head.position);
            BodyData.minHeadSpineHeight = minHeadSpineHeight;
            // minHeadSpineHeight above was the only baked metre scalar this handler refreshed; the arm,
            // clavicle and neck-cue scalars are measured in the same one-shot rig build and were left to go
            // stale on every rescale. Same event, same fix.
            BodyData.RescaleTposeScalars(Scale);
        }
        public void Spine()
        {
            if (localPlayer?.BasisAvatar?.Animator == null)
            {
                return;
            }
            if (IKJobCreated)
            {
                IKJob.Destroy();
                IKJobCreated = false;
            }
            IKJob = default;
            BasisAnimationRiggingHelper.CreateBasisFullBodyRIG(localPlayer, basisTransformMapping, ref IKJob);
            IKDataReady = true;
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnPlayersHeightChangedNextFrame;
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnPlayersHeightChangedNextFrame;
            OnPlayersHeightChangedNextFrame( HeightModeChange.OnTpose);
            ref BasisEerieMovement data = ref IKJob;
            // Legs enabled by presence
            BasisLocalBoneDriver.LeftFootControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);
            };
            data.enabledLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);
            BasisLocalBoneDriver.RightFootControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);
            };
            data.enabledRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);
            BasisLocalBoneDriver.LeftLowerLegControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.hintWeightLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);
            };
            data.hintWeightLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);
            BasisLocalBoneDriver.RightLowerLegControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.hintWeightRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);
            };
            data.hintWeightRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);
            // Toes
            BasisLocalBoneDriver.LeftToeControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.leftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);
            };
            data.leftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);
            BasisLocalBoneDriver.RightToeControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.rightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);
            };
            data.rightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);
            // Hands
            BasisLocalBoneDriver.LeftHandControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
            };
            data.enabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
            BasisLocalBoneDriver.RightHandControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
            };
            data.enabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
            // Lower arms (hand hints)
            BasisLocalBoneDriver.LeftLowerArmControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.hintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
            };
            data.hintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
            BasisLocalBoneDriver.RightLowerArmControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.hintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
            };
            data.hintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
            // Chest (head hint)
            BasisLocalBoneDriver.ChestControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.hasChestTracker = HasRigLayer(BasisLocalBoneDriver.ChestControl);
            };
            data.hasChestTracker = HasRigLayer(BasisLocalBoneDriver.ChestControl);
            // Chest (head hint)
            BasisLocalBoneDriver.LeftShoulderControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
            };
            data.enabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
            // Chest (head hint)
            BasisLocalBoneDriver.RightShoulderControl.OnHasRigChanged += (hasRig) =>
            {
                ref BasisEerieMovement d = ref IKJob;
                d.enabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
            };
            data.enabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
            // Initialize offsets and weights per override slot. Slots are HumanBodyBones values:
            // 0..20 plus UpperChest (54) — NOT a contiguous 0..Count range, which would touch
            // LeftEye (21, silently ignored) and skip UpperChest entirely.
            for (int i = 0; i < BasisEerieMovement.Count; i++)
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
            data.minFactor = 0.95f;
            data.maxFactor = 1.05f;
            ApplyTuningSettings(ref data);
        }
        public static bool HasRecalibratedRotationOffsets;
        public static Quaternion RecalibratedHead, RecalibratedHips, RecalibratedChest;
        public static Quaternion RecalibratedLeftFoot, RecalibratedRightFoot;
        public static Quaternion RecalibratedLeftToe, RecalibratedRightToe;
        public static Quaternion RecalibratedLeftShoulder, RecalibratedRightShoulder;
        private static void ApplyTuningSettings(ref BasisEerieMovement data)
        {
            Vector3 rootUp = BasisLocalPlayer.localToWorldMatrix.MultiplyVector(Vector3.up);
            data.playerUp = rootUp.sqrMagnitude > 1e-8f ? rootUp.normalized : Vector3.up;
            data.maxBendDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxBendDeg.RawValue;
            data.maxChestDeltaDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKMaxChestDelta.RawValue;
            data.spineBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendPitch.RawValue;
            data.spineBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendYaw.RawValue;
            data.spineBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineBendRoll.RawValue;
            data.upperChestBendPitch = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendPitch.RawValue;
            data.upperChestBendYaw = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendYaw.RawValue;
            data.upperChestBendRoll = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperChestBendRoll.RawValue;
            data.hipHingeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeStartDeg.RawValue;
            data.hipHingeMaxAddDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKHipHingeMaxAddDeg.RawValue;
            data.chestSpringHz = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringHz.RawValue;
            data.chestSpringDamping = Basis.BasisUI.BasisSettingsDefaults.FBIKChestSpringDamping.RawValue;
            data.spineMaxForwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxForwardDeg.RawValue;
            data.spineMaxBackwardDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxBackwardDeg.RawValue;
            data.spineMaxLateralDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineMaxLateralDeg.RawValue;
            data.spineSquishBoost = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineSquishBoost.RawValue;
            data.spineGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineGazeFollow.RawValue;
            data.neckGazeFollow = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckGazeFollow.RawValue;
            data.neckExtensionDamp = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckExtensionDamp.RawValue;
            data.neckFlexionDamp = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckFlexionDamp.RawValue;
            data.moveBodyBackWhenCrouching = Basis.BasisUI.BasisSettingsDefaults.FBIKMoveBodyBackWhenCrouching.RawValue;
            data.trunkCounterbalance = Basis.BasisUI.BasisSettingsDefaults.FBIKTrunkCounterbalance.RawValue;
            data.swingSmoothRateDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowSwingEnabled.RawValue ? Basis.BasisUI.BasisSettingsDefaults.FBIKSwingSmoothRate.RawValue : 0f;
            data.spineCCDRelax = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineCCDRelax.RawValue;
            data.spineTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTwistKeep.RawValue;
            data.spineNeckTwistKeep = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineNeckTwistKeep.RawValue;
            data.neckMaxConeDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckMaxConeDeg.RawValue;
            data.chestArmSwingFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingFactor.RawValue;
            data.chestArmSwingMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestArmSwingMaxDeg.RawValue;
            data.lowerArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKLowerArmTwistFraction.RawValue;
            data.upperArmTwistFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKUpperArmTwistFraction.RawValue;
            data.anatDifferentialStiffness = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatDifferentialStiffness.RawValue;
            data.anatShoulderSlide = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatShoulderSlide.RawValue;
            data.anatCervicalLordosis = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatCervicalLordosis.RawValue;
            data.anatPelvicTwistRouting = Basis.BasisUI.BasisSettingsDefaults.FBIKAnatPelvicTwistRouting.RawValue;
            data.spineAnatomicalRom = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineAnatomicalRom.RawValue;
            data.chestIkTarget = Basis.BasisUI.BasisSettingsDefaults.FBIKChestIKTarget.RawValue;
            data.legSwivelSmoothing = Basis.BasisUI.BasisSettingsDefaults.FBIKLegSwivelSmoothing.RawValue;
            data.kneeFootPoleHold = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootPoleHold.RawValue;
            data.kneeFootPoleConditioning = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootPoleConditioning.RawValue;
            data.lordosisPitchGainDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisPitchGainDeg.RawValue;
            data.lordosisBaseDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisBaseDeg.RawValue;
            data.lordosisNeckShare = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisNeckShare.RawValue;
            data.lordosisMaxHeadPitchDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisMaxHeadPitchDeg.RawValue;
            data.lordosisExtremeStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeStartDeg.RawValue;
            data.lordosisExtremeFullDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeFullDeg.RawValue;
            data.lordosisExtremeRollForwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollForwardMaxDeg.RawValue;
            data.lordosisExtremeRollBackwardMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeRollBackwardMaxDeg.RawValue;
            // Avatar scale, shared by every metre-valued tuning value below.
            float collisionScale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            // These six are METRES added straight to the hips/chest world position in ApplyCervicalLordosis,
            // so they must scale with the avatar. Unscaled they were a fixed ~2.5 cm pelvis and ~4 cm chest
            // shove at every size — double the body-relative displacement at 0.5x, and since the chest term
            // is 1.6x the hips term the torso visibly sheared. The Deg values above are angles; leave them.
            data.lordosisExtremeHipsHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalMax.RawValue * collisionScale;
            data.lordosisExtremeChestHorizontalMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalMax.RawValue * collisionScale;
            data.lordosisExtremeHipsHorizontalLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsHorizontalLookUp.RawValue * collisionScale;
            data.lordosisExtremeChestHorizontalLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestHorizontalLookUp.RawValue * collisionScale;
            data.lordosisExtremeHipsDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownMax.RawValue * collisionScale;
            data.lordosisExtremeChestDownMax = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownMax.RawValue * collisionScale;
            data.lordosisExtremeHipsDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeHipsDownLookUp.RawValue * collisionScale;
            data.lordosisExtremeChestDownLookUp = Basis.BasisUI.BasisSettingsDefaults.FBIKLordosisExtremeChestDownLookUp.RawValue * collisionScale;
            // Toggles + shoulder-solve params that previously only flowed at init. Without these
            // here, flipping the matching toggle/slider in the IK panel left the animation job
            // running on the boot-time snapshot.
            data.collisionsEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionsEnabled.RawValue;
            data.protectElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKProtectElbow.RawValue;
           // data.useNeuralPole = Basis.BasisUI.BasisSettingsDefaults.FBIKNeuralPole.RawValue;
            data.collideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;
          //  data.wristAxialBound = Basis.BasisUI.BasisSettingsDefaults.FBIKWristAxialBound.RawValue;
            data.elbowDragEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDrag.RawValue;
            data.elbowDragHz = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDragHz.RawValue;
            data.shoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            data.shoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
           // data.shoulderRetractionEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderRetraction.RawValue;
            data.shoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            data.shoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;
            data.shoulderCoupleRatio = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderCoupleRatio.RawValue;
            data.shoulderMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderMaxDeg.RawValue;
            data.shoulderSlideStartDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideStartDeg.RawValue;
            data.shoulderSlideMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideMaxDeg.RawValue;
            data.shoulderSlideFraction = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSlideFraction.RawValue;
            data.thoracicBendStiffen = Basis.BasisUI.BasisSettingsDefaults.FBIKThoracicBendStiffen.RawValue;
            data.spineTautBandFrac = Basis.BasisUI.BasisSettingsDefaults.FBIKSpineTautBandFrac.RawValue;
            data.bendTwistCoupling = Basis.BasisUI.BasisSettingsDefaults.FBIKBendTwistCoupling.RawValue;
            data.neckGazeFollowMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckGazeFollowMaxDeg.RawValue;
            data.trunkCounterbalanceMaxSpineFrac = Basis.BasisUI.BasisSettingsDefaults.FBIKTrunkCounterbalanceMaxFrac.RawValue;
            data.chestIkWeight = Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkWeight.RawValue;
            data.chestIkIterations = Mathf.Max(1, Mathf.RoundToInt(Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkIterations.RawValue));
            data.chestIkHeadRestoreSweeps = Mathf.Max(1, Mathf.RoundToInt(Basis.BasisUI.BasisSettingsDefaults.FBIKChestIkHeadRestoreSweeps.RawValue));
            data.chestPosPullMaxDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKChestPosPullMaxDeg.RawValue;
            data.chestPullMaxDist = Basis.BasisUI.BasisSettingsDefaults.FBIKChestPullMaxDist.RawValue;
            data.chestFollowChestShare = Basis.BasisUI.BasisSettingsDefaults.FBIKChestFollowChestShare.RawValue;
            data.trackedKneeSwivelMinCutoffHz = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelMinCutoffHz.RawValue;
            data.trackedKneeSwivelBeta = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelBeta.RawValue;
            data.trackedKneeSwivelDerivCutoffHz = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackedKneeSwivelDerivCutoffHz.RawValue;
            // Collision capsule dimensions × avatar scale. Slider defaults now match the
            // hardcoded values previously in SetHandCollisionScale, so this is the canonical path.
            data.handRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue * collisionScale;
            data.handSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue * collisionScale;
            data.chestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue * collisionScale;
            data.collisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue * collisionScale;
            if (HasRecalibratedRotationOffsets)
            {
                data.offsetRotationHead = RecalibratedHead;
                data.offsetRotationHips = RecalibratedHips;
                data.offsetRotationChest = RecalibratedChest;
                data.offsetRotationLeftFoot = RecalibratedLeftFoot;
                data.offsetRotationRightFoot = RecalibratedRightFoot;
                data.offsetRotationLeftToe = RecalibratedLeftToe;
                data.offsetRotationRightToe = RecalibratedRightToe;
                data.offsetRotationLeftShoulder = RecalibratedLeftShoulder;
                data.offsetRotationRightShoulder = RecalibratedRightShoulder;
            }
        }
        public void DisableAllTrackers()
        {
            if (IKDataReady)
            {
                ref BasisEerieMovement data = ref IKJob;
                data.enabledLeftLowerLeg = 0f;
                data.enabledRightLowerLeg = 0f;
                data.hintWeightLeftLowerLeg = 0f;
                data.hintWeightRightLowerLeg = 0f;
                data.leftToeEnabled = false;
                data.rightToeEnabled = false;
                // data.enabledLeftHand = false;
                // data.enabledRightHand = false;
                data.hintWeightLeftHand = false;
                data.hintWeightRightHand = false;
                data.hasChestTracker = false;
                data.hasHipsTracker = false;
                data.enabledLeftShoulder = false;
                data.enabledRightShoulder = false;
            }
        }
        public bool TryGetLegDiagnostics(int slot, out Basis.IK.BasisLegDiagnostics diagnostics)
        {
            if (IKJobCreated && IKJob.legDiagnostics.IsCreated && (uint)slot < (uint)IKJob.legDiagnostics.Length)
            {
                diagnostics = IKJob.legDiagnostics[slot];
                return true;
            }
            diagnostics = default;
            return false;
        }
        public void RestoreAllTrackers()
        {
            if (IKDataReady)
            {
                ref BasisEerieMovement data = ref IKJob;
                data.enabledLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftFootControl);
                data.enabledRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightFootControl);
                data.hintWeightLeftLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.LeftLowerLegControl);
                data.hintWeightRightLowerLeg = HasRigLayerFloat(BasisLocalBoneDriver.RightLowerLegControl);
                data.leftToeEnabled = HasRigLayer(BasisLocalBoneDriver.LeftToeControl);
                data.rightToeEnabled = HasRigLayer(BasisLocalBoneDriver.RightToeControl);
                data.enabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
                data.enabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
                data.hintWeightLeftHand = HasRigLayer(BasisLocalBoneDriver.LeftLowerArmControl);
                data.hintWeightRightHand = HasRigLayer(BasisLocalBoneDriver.RightLowerArmControl);
                data.hasChestTracker = HasRigLayer(BasisLocalBoneDriver.ChestControl);
                data.enabledLeftShoulder = HasRigLayer(BasisLocalBoneDriver.LeftShoulderControl);
                data.enabledRightShoulder = HasRigLayer(BasisLocalBoneDriver.RightShoulderControl);
            }
        }
        public static readonly Quaternion PreserveTipSentinel = new Quaternion(0f, 0f, 0f, 0f);
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
        private static float HandRigWeight(BasisLocalBoneControl control)
        {
            if (control == null || control.HasRigLayer != BasisHasRigLayer.HasRigLayer) return 0f;
            float w = control.RigLayerWeight;
            return w > 0f ? (w < 1f ? w : 1f) : 0f;
        }
        private static bool TryComputeButterflyKnee( bool isLeft, Quaternion hipsRot, Vector3 playerUp, float maxOpenDeg, float supineFloor, float dt, Vector3 defaultBendDir, Transform upperLeg, Transform lowerLeg, Vector3 footPos, Quaternion footRot, ref Vector3 smoothedHint, ref float smoothedWeight, out Vector3 hintPos, out float weight)
        {
            hintPos = default;
            weight = 0f;
            if (upperLeg == null || lowerLeg == null)
            {
                smoothedWeight = 0f;
                return false;
            }
            Vector3 hipPos = upperLeg.position, hipsRight = hipsRot * Vector3.right, hipsForward = hipsRot * Vector3.forward;
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
        private static bool TryComputeKneeForward( Quaternion hipsRot, float coupling, float smoothRate, Vector3 playerUp, float dt, Transform upperLeg, Transform lowerLeg, Vector3 footPos, Quaternion footRot, ref Vector3 smoothedBendDir, ref float smoothedWeight, out Vector3 hintPos, out float weight, out Vector3 bendDir)
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
            float alpha = 1f - Mathf.Exp(-smoothRate * dt);
            if (smoothedBendDir.sqrMagnitude < 1e-6f) smoothedBendDir = result.BendDir;
            else smoothedBendDir = Vector3.Slerp(smoothedBendDir.normalized, result.BendDir, alpha);
            smoothedWeight = Mathf.Lerp(smoothedWeight, result.HintWeight, alpha);
            bendDir = smoothedBendDir.sqrMagnitude > 1e-6f ? smoothedBendDir.normalized : result.BendDir;
            Vector3 mid = (hipPos + footPos) * 0.5f;
            float radius = input.UpperLength > 1e-5f ? input.UpperLength : 0.4f;
            hintPos = mid + bendDir * radius;
            weight = smoothedWeight;
            return weight > 0.001f;
        }
        static List<Transform> CollectIKBones(BasisTransformMapping d) => new List<Transform>
        {
            d.Hips, d.spine, d.chest, d.Upperchest, d.neck, d.head, d.leftShoulder, d.RightShoulder, d.leftUpperArm, d.leftLowerArm, d.leftHand, d.RightUpperArm, d.RightLowerArm, d.rightHand, d.leftUpperArmTwist, d.leftLowerArmTwist, d.RightUpperArmTwist, d.RightLowerArmTwist, d.LeftUpperLeg, d.LeftLowerLeg, d.leftFoot, d.RightUpperLeg, d.RightLowerLeg, d.rightFoot, d.leftToe, d.rightToe,
        };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideUsage(HumanBodyBones bone, bool enabled)
        {
            ref BasisEerieMovement data = ref IKJob;
            data.SetWeight((int)bone, enabled);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideData(HumanBodyBones bone, in Vector3 position, in Quaternion rotation)
        {
            ref BasisEerieMovement data = ref IKJob;
            data.SetTargetPosition((int)bone, position);
            data.SetTargetRotation((int)bone, rotation);
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
        bool frameFbtEnabled, frameLeftFootHasTracker, frameRightFootHasTracker, frameLeftLowerLegHasTracker, frameRightLowerLegHasTracker, frameHipsHasTracker, frameTrackerBendNormal, frameFootDriverReady, frameFootSimScheduled, frameLeftWantFootIK, frameRightWantFootIK, frameNotifyFootReengage;
        Quaternion frameHipsRotation;
        Vector3 framePlayerUpDirection;
        public void ScheduleLocomotionPose(BasisLocalPlayer player, float deltaTime)
        {
            CompleteSolveIfPending();
            Animator animator = player?.BasisAvatar != null ? player.BasisAvatar.Animator : null;
            BasisLocoParams frameParams = player.LocalAnimatorDriver.GetLocoParams();
            LocomotionPose.Schedule(this, animator, in frameParams, deltaTime);
        }
        static readonly ProfilerMarker sMarkerIKDestPrep = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.Prep");
        static readonly ProfilerMarker sMarkerIKDestFootSchedule = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.FootSchedule");
        static readonly ProfilerMarker sMarkerIKDestGatherTargets = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.GatherTargets");
        static readonly ProfilerMarker sMarkerIKDestFilters = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.Filters");
        static readonly ProfilerMarker sMarkerIKDestFootJoin = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.FootJoin");
        static readonly ProfilerMarker sMarkerIKDestBuildTargets = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.BuildIKTargets");
        static readonly ProfilerMarker sMarkerIKDestLocoJoin = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.LocoPoseJoin");
        static readonly ProfilerMarker sMarkerIKDestPoseGather = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.PoseGather");
        static readonly ProfilerMarker sMarkerIKDestApplyFit = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.ApplyFit");
        static readonly ProfilerMarker sMarkerIKDestSolve = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.Solve");
        static readonly ProfilerMarker sMarkerIKDestSolveJoin = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.SolveJoin");
        static readonly ProfilerMarker sMarkerIKDestSolveGizmos = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.SolveGizmos");
        static readonly ProfilerMarker sMarkerIKDestPoseScatter = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.PoseScatter");
        static readonly ProfilerMarker sMarkerIKDestPublish = new ProfilerMarker("BasisDriver.LocalPlayer.IKDest.PublishWorldData");
        public void SimulateIKDestinations(float deltaTime)
        {
            if (!IKDataReady || !IKJobCreated)
            {
                return;
            }
            if (!PlayableGraph.IsValid())
            {
                return;
            }
            timeAccumulator += Mathf.Max(deltaTime, 1e-6f);
            Step01PrepareFilters(deltaTime);
            Step02ScheduleFootSim(deltaTime);
            Step03GatherTrackerTargets();
            Step04SmoothTrackerTargets(Mathf.Max(deltaTime, 1e-6f));
            Step05AdvanceFootIKBlend(deltaTime);
            Step06JoinFootSim();
            Step07BuildTorsoTargets();
            Step08BuildFootTargets();
            Step09BuildGaitPelvis();
            Step10BuildKneePoles(deltaTime);
            Step11BuildToeTargets();
            Step12BuildKneeBendPreferences();
            Step13BuildArmTargets();
            ApplyTuningSettings(ref IKJob);
            bool basePoseInStream = Step16JoinBasePose();
            Step17ScheduleSolve(deltaTime, basePoseInStream);
            ikPublishPending = true;
        }
        void Step01PrepareFilters(float deltaTime)
        {
            sMarkerIKDestPrep.Begin();
            EnsureFilterArrays();
            ResolveSmoothingGroups(deltaTime);
            sMarkerIKDestPrep.End();
        }
        void Step02ScheduleFootSim(float deltaTime)
        {
            sMarkerIKDestFootSchedule.Begin();
            frameFbtEnabled = Basis.BasisUI.BasisSettingsDefaults.EnableFBT.RawValue;
            frameLeftFootHasTracker = frameFbtEnabled && (BasisLocalBoneDriver.LeftFootControl.HasTracked == BasisHasTracked.HasTracker || BasisLocalBoneDriver.LeftUpperLegControl.HasTracked == BasisHasTracked.HasTracker);
            frameRightFootHasTracker = frameFbtEnabled && (BasisLocalBoneDriver.RightFootControl.HasTracked == BasisHasTracked.HasTracker || BasisLocalBoneDriver.RightUpperLegControl.HasTracked == BasisHasTracked.HasTracker);
            bool locomotionAnimActive = localPlayer.LocalCharacterDriver.MovementVector.sqrMagnitude > 0.001f;
            if (locomotionAnimActive) stationaryTimer = 0f;
            else stationaryTimer += deltaTime;
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            frameFootDriverReady = footDriver.IsInitialized;
            bool isStationaryEnough = stationaryTimer >= StationaryDelaySeconds, footIKSetting = Basis.BasisUI.BasisSettingsDefaults.FootIKEnabled.RawValue;
            bool footIKReady = frameFootDriverReady && (LocomotionFootIK || isStationaryEnough) && footIKSetting && !localPlayer.LocalCharacterDriver.IsProne;
            frameLeftWantFootIK = footIKReady && !frameLeftFootHasTracker;
            frameRightWantFootIK = footIKReady && !frameRightFootHasTracker;
            bool leftOrRightDrive = !frameLeftFootHasTracker || !frameRightFootHasTracker;
            frameFootSimScheduled = false;
            if (frameFootDriverReady && leftOrRightDrive)
            {
                footDriver.ScheduleSimulate(deltaTime);
                frameFootSimScheduled = true;
            }
            sMarkerIKDestFootSchedule.End();
        }
        void Step03GatherTrackerTargets()
        {
            sMarkerIKDestGatherTargets.Begin();
            BasisCalibratedCoords hipsData = BasisLocalBoneDriver.HipsControl.OutGoingData, headData = BasisLocalBoneDriver.HeadControl.OutGoingData, leftFootData = BasisLocalBoneDriver.LeftFootControl.OutGoingData, rightFootData = BasisLocalBoneDriver.RightFootControl.OutGoingData, chestData = BasisLocalBoneDriver.ChestControl.OutGoingData, leftLLData = BasisLocalBoneDriver.LeftLowerLegControl.OutGoingData, rightLLData = BasisLocalBoneDriver.RightLowerLegControl.OutGoingData, leftHandData = BasisLocalBoneDriver.LeftHandControl.OutGoingData, rightHandData = BasisLocalBoneDriver.RightHandControl.OutGoingData, leftLAData = BasisLocalBoneDriver.LeftLowerArmControl.OutGoingData, rightLAData = BasisLocalBoneDriver.RightLowerArmControl.OutGoingData, leftToeData = BasisLocalBoneDriver.LeftToeControl.OutGoingData, rightToeData = BasisLocalBoneDriver.RightToeControl.OutGoingData;
            Quaternion leftShoulderRot = BasisLocalBoneDriver.LeftShoulderControl.OutGoingData.rotation, rightShoulderRot = BasisLocalBoneDriver.RightShoulderControl.OutGoingData.rotation;
            unsafe
            {
                float3* posPtr = (float3*)posInputs.GetUnsafePtr();
                quaternion* rotPtr = (quaternion*)rotInputs.GetUnsafePtr();
                byte* posModePtr = (byte*)posModeNative.GetUnsafePtr();
                byte* rotModePtr = (byte*)rotModeNative.GetUnsafePtr();
                float4* posTunePtr = (float4*)posTuning.GetUnsafePtr();
                float4* rotTunePtr = (float4*)rotTuning.GetUnsafePtr();
                BasisEuroVec3State* euroPosPtr = (BasisEuroVec3State*)euroPosStates.GetUnsafePtr();
                BasisEuroQuatState* euroRotPtr = (BasisEuroQuatState*)euroRotStates.GetUnsafePtr();
                float3* fallbackPosPtr = (float3*)fallbackPosStates.GetUnsafePtr();
                quaternion* fallbackRotPtr = (quaternion*)fallbackRotStates.GetUnsafePtr();
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
                for (int i = 0; i < SlotCount; i++)
                {
                    byte group = BasisSmoothingProfiles.SlotGroup[i];
                    posTunePtr[i] = groupPosTuning[group];
                    rotTunePtr[i] = groupRotTuning[group];
                    byte newPosMode = (byte)BasisFilterMode.Passthrough;
                    byte newRotMode = (byte)BasisFilterMode.Passthrough;
                    if (!groupOff[group])
                    {
                        newPosMode = PickMode(SmoothPos[i], EuroPos[i]);
                        newRotMode = PickMode(SmoothRot[i], EuroRot[i]);
                    }
                    if (newPosMode != posModePtr[i])
                    {
                        euroPosPtr[i] = default;
                        fallbackPosPtr[i] = posPtr[i];
                    }
                    if (newRotMode != rotModePtr[i])
                    {
                        euroRotPtr[i] = default;
                        fallbackRotPtr[i] = rotPtr[i];
                    }
                    posModePtr[i] = newPosMode;
                    rotModePtr[i] = newRotMode;
                }
                posModePtr[S_LeftShoulder] = (byte)BasisFilterMode.Passthrough;
                posModePtr[S_RightShoulder] = (byte)BasisFilterMode.Passthrough;
            }
            if (!hasFallbackState)
            {
                hasFallbackState = true;
                fallbackPosStates.CopyFrom(posInputs);
                fallbackRotStates.CopyFrom(rotInputs);
            }
            sMarkerIKDestGatherTargets.End();
        }
        void Step04SmoothTrackerTargets(float safeDt)
        {
            sMarkerIKDestFilters.Begin();
            Matrix4x4 playspaceMatrix = BasisLocalPlayer.localToWorldMatrix;
            var posJob = new BasisBatchPositionFilterJob
            {
                mode = posModeNative, rawInputs = posInputs, tuning = posTuning, euroStates = euroPosStates, fallbackStates = fallbackPosStates, outputs = posOutputs, dt = safeDt, playspaceToWorld = playspaceMatrix,
            };
            var rotJob = new BasisBatchRotationFilterJob
            {
                mode = rotModeNative, rawInputs = rotInputs, tuning = rotTuning, euroStates = euroRotStates, fallbackStates = fallbackRotStates, outputs = rotOutputs, dt = safeDt, playspaceRotation = playspaceMatrix.rotation,
            };
            posJob.Run(SlotCount);
            rotJob.Run(SlotCount);
            sMarkerIKDestFilters.End();
            WatchdogCheckFilterSlots("IKDest/PostFilters");
        }
        void Step05AdvanceFootIKBlend(float deltaTime)
        {
            float leftBlendTarget = frameLeftWantFootIK ? 1f : 0f, rightBlendTarget = frameRightWantFootIK ? 1f : 0f;
            if (frameLeftFootHasTracker) footIKBlendWeightLeft = 0f;
            if (frameRightFootHasTracker) footIKBlendWeightRight = 0f;
            float leftPrevBlend = footIKBlendWeightLeft, rightPrevBlend = footIKBlendWeightRight;
            footIKBlendWeightLeft = Mathf.MoveTowards(footIKBlendWeightLeft, leftBlendTarget, (frameLeftWantFootIK ? FootIKBlendInSpeed : FootIKBlendOutSpeed) * deltaTime);
            footIKBlendWeightRight = Mathf.MoveTowards(footIKBlendWeightRight, rightBlendTarget, (frameRightWantFootIK ? FootIKBlendInSpeed : FootIKBlendOutSpeed) * deltaTime);
            footIKBlendWeight = Mathf.Min(footIKBlendWeightLeft, footIKBlendWeightRight);
            frameNotifyFootReengage = frameFootDriverReady && ((leftPrevBlend < 0.001f && footIKBlendWeightLeft >= 0.001f) || (rightPrevBlend < 0.001f && footIKBlendWeightRight >= 0.001f));
            frameLeftLowerLegHasTracker = frameFbtEnabled && BasisLocalBoneDriver.LeftLowerLegControl.HasTracked == BasisHasTracked.HasTracker;
            frameRightLowerLegHasTracker = frameFbtEnabled && BasisLocalBoneDriver.RightLowerLegControl.HasTracked == BasisHasTracked.HasTracker;
            frameHipsHasTracker = frameFbtEnabled && BasisLocalBoneDriver.HipsControl.HasTracked == BasisHasTracked.HasTracker;
            frameTrackerBendNormal = Basis.BasisUI.BasisSettingsDefaults.FBIKTrackerBendNormal.RawValue;
        }
        void Step06JoinFootSim()
        {
            sMarkerIKDestFootJoin.Begin();
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            if (frameFootSimScheduled) footDriver.CompleteSimulate();
            if (frameNotifyFootReengage) footDriver.NotifyReEngaging();
            if (frameFootSimScheduled) footDriver.ScheduleSurfaceProbes();
            sMarkerIKDestFootJoin.End();
        }
        void Step07BuildTorsoTargets()
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            Vector3 hipsPos, chestPos;
            Quaternion chestRot;
            Vector3 playerUpScaled = BasisLocalPlayer.localToWorldMatrix.MultiplyVector(Vector3.up);
            float playerUpScale = playerUpScaled.magnitude;
            Vector3 playerUpDir = playerUpScale > 1e-6f ? playerUpScaled / playerUpScale : Vector3.up;
            framePlayerUpDirection = playerUpDir;
            unsafe
            {
                float3* pOut = (float3*)posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)rotOutputs.GetUnsafeReadOnlyPtr();
                hipsPos = pOut[S_Hips];
                frameHipsRotation = rOut[S_Hips];
                hipsPos -= playerUpDir * localPlayer.LocalCharacterDriver.landingCrouchEffect;
                data.targetPositionHips = hipsPos;
                data.targetRotationHips = frameHipsRotation;
                data.hasHipsTracker = frameHipsHasTracker;
                // Only without a real pelvis tracker: a tracked hips must stay pinned even while prone.
                data.proneBodyPose = localPlayer.LocalCharacterDriver.IsProne && !frameHipsHasTracker;
                // Per frame, not just on OnHasRigChanged: the weight moves continuously while a source fades.
                data.enabledLeftHand = HandRigWeight(BasisLocalBoneDriver.LeftHandControl);
                data.enabledRightHand = HandRigWeight(BasisLocalBoneDriver.RightHandControl);
                data.targetPositionHead = pOut[S_Head];
                data.targetRotationHead = rOut[S_Head];
                float restHeadLocalY = BasisLocalBoneDriver.HeadControl.TposeLocalScaled.position.y;
                data.standingHeadHeight = Mathf.Max(0f, restHeadLocalY * playerUpScale);
                if (localPlayer.LocalSeatDriver.IsSeated || localPlayer.LocalCharacterDriver.IsProne || playerUpScale <= 1e-6f)
                {
                    data.crouchDepth = 0f;
                }
                else
                {
                    float headLocalY = BasisLocalPlayer.localToWorldMatrix.inverse.MultiplyPoint3x4((Vector3)pOut[S_Head]).y;
                    data.crouchDepth = Mathf.Max(0f, (restHeadLocalY - headLocalY) * playerUpScale);
                }
                chestPos = pOut[S_Chest];
                chestRot = rOut[S_Chest];
                data.targetPositionChestRaw = chestPos;
                chestPos = ApplyHintBias(BasisBoneTrackedRole.Chest, chestPos, chestRot);
                data.targetPositionChest = chestPos;
                data.targetRotationChest = chestRot;
            }
            sMarkerIKDestBuildTargets.End();
        }
        void Step08BuildFootTargets()
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            bool footDriverReady = frameFootDriverReady, leftHasTracker = frameLeftFootHasTracker, rightHasTracker = frameRightFootHasTracker;
            unsafe
            {
                float3* pOut = (float3*)posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)rotOutputs.GetUnsafeReadOnlyPtr();
                // ── LEFT FOOT ──
                data.footIsTrackerLeftLeg = leftHasTracker;
                if (leftHasTracker)
                {
                    data.targetPositionLeftLowerLeg = pOut[S_LeftFoot];
                    data.targetRotationLeftLowerLeg = rOut[S_LeftFoot];
                    data.enabledLeftLowerLeg = 1f;
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    data.targetPositionLeftLowerLeg = footDriver.LeftFootPosition;
                    data.targetRotationLeftLowerLeg = FootRotationFromDriver ? SafeFootTargetRotation(footDriver.LeftFootRotation, data.offsetRotationLeftFoot) : PreserveTipSentinel;
                    data.enabledLeftLowerLeg = footIKBlendWeightLeft;
                }
                else
                {
                    data.enabledLeftLowerLeg = 0f;
                }
                // ── RIGHT FOOT ──
                data.footIsTrackerRightLeg = rightHasTracker;
                if (rightHasTracker)
                {
                    data.targetPositionRightLowerLeg = pOut[S_RightFoot];
                    data.targetRotationRightLowerLeg = rOut[S_RightFoot];
                    data.enabledRightLowerLeg = 1f;
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    data.targetPositionRightLowerLeg = footDriver.RightFootPosition;
                    data.targetRotationRightLowerLeg = FootRotationFromDriver ? SafeFootTargetRotation(footDriver.RightFootRotation, data.offsetRotationRightFoot) : PreserveTipSentinel;
                    data.enabledRightLowerLeg = footIKBlendWeightRight;
                }
                else
                {
                    data.enabledRightLowerLeg = 0f;
                }
                if (BasisFootRotationDebug.Enabled)
                {
                    if (basisTransformMapping.leftFoot != null) BasisFootRotationDebug.Record("L", Time.time, footIKBlendWeightLeft, !leftHasTracker && footIKBlendWeightLeft > 0.001f && footDriverReady, basisTransformMapping.leftFoot.rotation, data.targetRotationLeftLowerLeg, data.offsetRotationLeftFoot, BasisLocalBoneDriver.LeftFootControl.OutGoingData.rotation, BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation, (Quaternion)rOut[S_LeftFoot], footDriverReady ? footDriver.LeftFootRotation : Quaternion.identity);
                    if (basisTransformMapping.rightFoot != null) BasisFootRotationDebug.Record("R", Time.time, footIKBlendWeightRight, !rightHasTracker && footIKBlendWeightRight > 0.001f && footDriverReady, basisTransformMapping.rightFoot.rotation, data.targetRotationRightLowerLeg, data.offsetRotationRightFoot, BasisLocalBoneDriver.RightFootControl.OutGoingData.rotation, BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation, (Quaternion)rOut[S_RightFoot], footDriverReady ? footDriver.RightFootRotation : Quaternion.identity);
                }
            }
            sMarkerIKDestBuildTargets.End();
        }
        void Step09BuildGaitPelvis()
        {
            if (!(footIKBlendWeight > 0.001f && frameFootDriverReady && !frameHipsHasTracker))
            {
                return;
            }
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            data.targetPositionHips += framePlayerUpDirection * (footDriver.ComputeHipBob() * footIKBlendWeight);
            data.targetPositionHips += footDriver.ComputeHipSway() * footIKBlendWeight;
            Quaternion pelvis = Quaternion.Slerp(Quaternion.identity, footDriver.ComputePelvisDelta(), footIKBlendWeight);
            data.targetRotationHips = pelvis * data.targetRotationHips;
            sMarkerIKDestBuildTargets.End();
        }
        void Step10BuildKneePoles(float deltaTime)
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            bool footDriverReady = frameFootDriverReady, fbtEnabled = frameFbtEnabled, leftHasTracker = frameLeftFootHasTracker, rightHasTracker = frameRightFootHasTracker, leftLLHasTracker = frameLeftLowerLegHasTracker, rightLLHasTracker = frameRightLowerLegHasTracker;
            Quaternion hipsRot = frameHipsRotation;
            Vector3 playerUpDir = framePlayerUpDirection;
            unsafe
            {
                float3* pOut = (float3*)posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)rotOutputs.GetUnsafeReadOnlyPtr();
                bool butterflyEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKnees.RawValue;
                float butterflyMaxOpenDeg = Basis.BasisUI.BasisSettingsDefaults.FBIKButterflyKneeMaxOpenDeg.RawValue;
                float butterflySupineFloor = 1f; // merged toggle: butterfly knees works both supine and upright when enabled
                bool kneeFollowsFoot = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFollowsFoot.RawValue;
                float kneeFootCoupling = Basis.BasisUI.BasisSettingsDefaults.FBIKKneeFootFollowUpright.RawValue, kneeFwdSmoothRate = KneeForwardSmoothRate;
                Vector3 hipsForwardDir = hipsRot * Vector3.forward;
                bool leftFootTracked = fbtEnabled && BasisLocalBoneDriver.LeftFootControl.HasTracked == BasisHasTracked.HasTracker, rightFootTracked = fbtEnabled && BasisLocalBoneDriver.RightFootControl.HasTracked == BasisHasTracked.HasTracker;
                if (leftLLHasTracker)
                {
                    Vector3 lllPos = pOut[S_LeftLowerLeg];
                    Quaternion lllRot = rOut[S_LeftLowerLeg];
                    lllPos = ApplyHintBias(BasisBoneTrackedRole.LeftLowerLeg, lllPos, lllRot);
                    data.hintPositionLeftLowerLeg = lllPos;
                    data.hintWeightLeftLowerLeg = 1f;
                }
                else if (footIKBlendWeightLeft > 0.001f && footDriverReady)
                {
                    data.hintPositionLeftLowerLeg = footDriver.LeftKneeHint;
                    data.hintWeightLeftLowerLeg = footIKBlendWeightLeft;
                }
                else if (leftFootTracked)
                {
                    Vector3 lBendDir = hipsForwardDir, lKneeFwdHint = default;
                    float lKneeFwdWeight = 0f;
                    bool lHaveKneeFwd = kneeFollowsFoot && TryComputeKneeForward( hipsRot, kneeFootCoupling, kneeFwdSmoothRate, playerUpDir, deltaTime, basisTransformMapping.LeftUpperLeg, basisTransformMapping.LeftLowerLeg, data.targetPositionLeftLowerLeg, data.targetRotationLeftLowerLeg, ref smoothedLeftKneeFwdHint, ref smoothedLeftKneeFwdWeight, out lKneeFwdHint, out lKneeFwdWeight, out lBendDir);
                    if (butterflyEnabled && TryComputeButterflyKnee( true, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime, lBendDir, basisTransformMapping.LeftUpperLeg, basisTransformMapping.LeftLowerLeg, data.targetPositionLeftLowerLeg, data.targetRotationLeftLowerLeg, ref smoothedLeftButterflyHint, ref smoothedLeftButterflyWeight, out Vector3 lButterflyHint, out float lButterflyWeight))
                    {
                        data.hintPositionLeftLowerLeg = lButterflyHint;
                        data.hintWeightLeftLowerLeg = lButterflyWeight;
                    }
                    else if (lHaveKneeFwd && lKneeFwdWeight > 0.001f)
                    {
                        data.hintPositionLeftLowerLeg = lKneeFwdHint;
                        data.hintWeightLeftLowerLeg = lKneeFwdWeight;
                    }
                    else
                    {
                        data.hintWeightLeftLowerLeg = 0f;
                    }
                }
                else
                {
                    data.hintWeightLeftLowerLeg = 0f;
                }
                // ── RIGHT LOWER LEG ──
                if (rightLLHasTracker)
                {
                    Vector3 rllPos = pOut[S_RightLowerLeg];
                    Quaternion rllRot = rOut[S_RightLowerLeg];
                    rllPos = ApplyHintBias(BasisBoneTrackedRole.RightLowerLeg, rllPos, rllRot);
                    data.hintPositionRightLowerLeg = rllPos;
                    data.hintWeightRightLowerLeg = 1f;
                }
                else if (footIKBlendWeightRight > 0.001f && footDriverReady)
                {
                    data.hintPositionRightLowerLeg = footDriver.RightKneeHint;
                    data.hintWeightRightLowerLeg = footIKBlendWeightRight;
                }
                else if (rightFootTracked)
                {
                    Vector3 rBendDir = hipsForwardDir, rKneeFwdHint = default;
                    float rKneeFwdWeight = 0f;
                    bool rHaveKneeFwd = kneeFollowsFoot && TryComputeKneeForward( hipsRot, kneeFootCoupling, kneeFwdSmoothRate, playerUpDir, deltaTime, basisTransformMapping.RightUpperLeg, basisTransformMapping.RightLowerLeg, data.targetPositionRightLowerLeg, data.targetRotationRightLowerLeg, ref smoothedRightKneeFwdHint, ref smoothedRightKneeFwdWeight, out rKneeFwdHint, out rKneeFwdWeight, out rBendDir);
                    if (butterflyEnabled && TryComputeButterflyKnee( false, hipsRot, playerUpDir, butterflyMaxOpenDeg, butterflySupineFloor, deltaTime, rBendDir, basisTransformMapping.RightUpperLeg, basisTransformMapping.RightLowerLeg, data.targetPositionRightLowerLeg, data.targetRotationRightLowerLeg, ref smoothedRightButterflyHint, ref smoothedRightButterflyWeight, out Vector3 rButterflyHint, out float rButterflyWeight))
                    {
                        data.hintPositionRightLowerLeg = rButterflyHint;
                        data.hintWeightRightLowerLeg = rButterflyWeight;
                    }
                    else if (rHaveKneeFwd && rKneeFwdWeight > 0.001f)
                    {
                        data.hintPositionRightLowerLeg = rKneeFwdHint;
                        data.hintWeightRightLowerLeg = rKneeFwdWeight;
                    }
                    else
                    {
                        data.hintWeightRightLowerLeg = 0f;
                    }
                }
                else
                {
                    data.hintWeightRightLowerLeg = 0f;
                }
                data.hintIsTrackerLeftLowerLeg = leftLLHasTracker;
                data.hintIsTrackerRightLowerLeg = rightLLHasTracker;
                if (BasisLegCrouchDebug.Enabled)
                {
                    if (basisTransformMapping.LeftUpperLeg != null && basisTransformMapping.LeftLowerLeg != null && basisTransformMapping.leftFoot != null)
                    {
                        Vector3 hipL = basisTransformMapping.LeftUpperLeg.position, kneeL = basisTransformMapping.LeftLowerLeg.position;
                        float legLenL = Vector3.Distance(hipL, kneeL) + Vector3.Distance(kneeL, basisTransformMapping.leftFoot.position);
                        BasisLegCrouchDebug.Record("L", Time.time, !leftHasTracker && footIKBlendWeightLeft > 0.001f && footDriverReady, legLenL, hipL, data.targetPositionLeftLowerLeg, data.hintPositionLeftLowerLeg, kneeL);
                    }
                    if (basisTransformMapping.RightUpperLeg != null && basisTransformMapping.RightLowerLeg != null && basisTransformMapping.rightFoot != null)
                    {
                        Vector3 hipR = basisTransformMapping.RightUpperLeg.position, kneeR = basisTransformMapping.RightLowerLeg.position;
                        float legLenR = Vector3.Distance(hipR, kneeR) + Vector3.Distance(kneeR, basisTransformMapping.rightFoot.position);
                        BasisLegCrouchDebug.Record("R", Time.time, !rightHasTracker && footIKBlendWeightRight > 0.001f && footDriverReady, legLenR, hipR, data.targetPositionRightLowerLeg, data.hintPositionRightLowerLeg, kneeR);
                    }
                }
            }
            sMarkerIKDestBuildTargets.End();
        }
        void Step11BuildToeTargets()
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            BasisLocalFootDriver footDriver = localPlayer.BasisLocalFootDriver;
            bool footDriverReady = frameFootDriverReady;
            unsafe
            {
                quaternion* rOut = (quaternion*)rotOutputs.GetUnsafeReadOnlyPtr();
                // ── TOES ──
                data.leftDrivenTargetRot = rOut[S_LeftToe];
                data.rightDrivenTargetRot = rOut[S_RightToe];
            }
            if (footIKBlendWeightLeft > 0.001f && footDriverReady)
            {
                data.leftToeBendDeg = footDriver.LeftToeBendDegrees * footIKBlendWeightLeft;
                data.leftToeBendAxis = footDriver.LeftToeBendAxis;
            }
            else
            {
                data.leftToeBendDeg = 0f;
                data.leftToeBendAxis = Vector3.zero;
            }
            if (footIKBlendWeightRight > 0.001f && footDriverReady)
            {
                data.rightToeBendDeg = footDriver.RightToeBendDegrees * footIKBlendWeightRight;
                data.rightToeBendAxis = footDriver.RightToeBendAxis;
            }
            else
            {
                data.rightToeBendDeg = 0f;
                data.rightToeBendAxis = Vector3.zero;
            }
            sMarkerIKDestBuildTargets.End();
        }
        void Step12BuildKneeBendPreferences()
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            bool leftLLHasTracker = frameLeftLowerLegHasTracker, rightLLHasTracker = frameRightLowerLegHasTracker, trackerBendNormal = frameTrackerBendNormal;
            Quaternion hipsRot = frameHipsRotation;
            data.hintRotationLeftLowerLeg = (leftLLHasTracker && BasisLimbRollStore.TryGet(BasisBoneTrackedRole.LeftLowerLeg, out var leftToBone)) ? BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData.rotation * leftToBone : default;
            data.hintRotationRightLowerLeg = (rightLLHasTracker && BasisLimbRollStore.TryGet(BasisBoneTrackedRole.RightLowerLeg, out var rightToBone)) ? BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData.rotation * rightToBone : default;
            Vector3 hipsRight = hipsRot * Vector3.right;
            data.kneeAnteriorRef = hipsRight;
            if (trackerBendNormal)
            {
                data.kneeBendPrefLeft = (leftLLHasTracker && BasisBendNormalStore.TryGet(BasisBoneTrackedRole.LeftLowerLeg, out var leftAxis)) ? BasisTrackerBendNormalCore.ResolveWorldNormal(BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData.rotation, leftAxis, hipsRight) : hipsRight;
                data.kneeBendPrefRight = (rightLLHasTracker && BasisBendNormalStore.TryGet(BasisBoneTrackedRole.RightLowerLeg, out var rightAxis)) ? BasisTrackerBendNormalCore.ResolveWorldNormal(BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData.rotation, rightAxis, hipsRight) : hipsRight;
            }
            else
            {
                data.kneeBendPrefLeft = hipsRight;
                data.kneeBendPrefRight = hipsRight;
            }
            sMarkerIKDestBuildTargets.End();
        }
        void Step13BuildArmTargets()
        {
            sMarkerIKDestBuildTargets.Begin();
            ref BasisEerieMovement data = ref IKJob;
            Vector3 llaPos, rlaPos;
            Quaternion llaRot, rlaRot;
            unsafe
            {
                float3* pOut = (float3*)posOutputs.GetUnsafeReadOnlyPtr();
                quaternion* rOut = (quaternion*)rotOutputs.GetUnsafeReadOnlyPtr();
                // ── HANDS ──
                data.targetPositionLeftHand = pOut[S_LeftHand];
                data.targetRotationLeftHand = rOut[S_LeftHand];
                data.targetPositionRightHand = pOut[S_RightHand];
                data.targetRotationRightHand = rOut[S_RightHand];
                llaPos = pOut[S_LeftLowerArm];
                llaRot = rOut[S_LeftLowerArm];
                data.hintPositionLeftHand = llaPos;
                data.hintRotationLeftHand = BasisLimbRollStore.TryGet(BasisBoneTrackedRole.LeftLowerArm, out var leftArmToBone) ? llaRot * leftArmToBone : default;
                rlaPos = pOut[S_RightLowerArm];
                rlaRot = rOut[S_RightLowerArm];
                data.hintPositionRightHand = rlaPos;
                data.hintRotationRightHand = BasisLimbRollStore.TryGet(BasisBoneTrackedRole.RightLowerArm, out var rightArmToBone) ? rlaRot * rightArmToBone : default;
                // ── SHOULDERS (rotation only) ──
                data.targetRotationLeftShoulder = rOut[S_LeftShoulder];
                data.targetRotationRightShoulder = rOut[S_RightShoulder];
            }
            sMarkerIKDestBuildTargets.End();
        }
        bool Step16JoinBasePose()
        {
            sMarkerIKDestLocoJoin.Begin();
            bool streamPrefilled = LocomotionPose.TryComplete(PoseSkeleton);
            sMarkerIKDestLocoJoin.End();
            return streamPrefilled;
        }
        void Step17ScheduleSolve(float deltaTime, bool streamPrefilled)
        {
            if (!RigLayerActive || !IKJobCreated || !PoseSkeleton.IsCreated)
            {
                return;
            }
            if (!streamPrefilled)
            {
                sMarkerIKDestPoseGather.Begin();
                PoseSkeleton.GatherNow();
                sMarkerIKDestPoseGather.End();
            }
            WatchdogCheckPoseStream(streamPrefilled ? "IKDest/PreFit (stream prefilled by locomotion pose)" : "IKDest/PreFit (stream gathered from bones)");
            sMarkerIKDestApplyFit.Begin();
            PoseSkeleton.ApplyFit();
            sMarkerIKDestApplyFit.End();
            WatchdogCheckPoseStream("IKDest/PostApplyFit (body-fit rest positions)");
            sMarkerIKDestSolve.Begin();
            IKJob.poseStream = PoseSkeleton.Stream;
            IKJob.poseStream.deltaTime = deltaTime;
            BasisIKSolveGizmos.Prepare(ref IKJob);
            ikSolveHandle = IKJob.Schedule();
            ikSolveScheduled = true;
            ikScatterPending = true;
            JobHandle.ScheduleBatchedJobs();
            sMarkerIKDestSolve.End();
        }
        public void CompleteIKSolve()
        {
            bool solveRan = Step18JoinSolve();
            Step19DrainSolveGizmos(solveRan);
            Step20ScatterPose();
            if (!ikPublishPending)
            {
                return;
            }
            ikPublishPending = false;
            Step21PublishWorldData();
            Step22SampleRecorders();
        }
        bool Step18JoinSolve()
        {
            bool solveRan = ikSolveScheduled;
            if (ikSolveScheduled)
            {
                sMarkerIKDestSolveJoin.Begin();
                ikSolveHandle.Complete();
                ikSolveScheduled = false;
                sMarkerIKDestSolveJoin.End();
            }
            return solveRan;
        }
        void Step19DrainSolveGizmos(bool solveRan)
        {
            sMarkerIKDestSolveGizmos.Begin();
            if (solveRan)
            {
                BasisIKSolveGizmos.Drain(ref IKJob, BasisLocalCameraDriver.Position);
            }
            else
            {
                BasisIKSolveGizmos.Hide();
            }
            sMarkerIKDestSolveGizmos.End();
        }
        void Step20ScatterPose()
        {
            if (!ikScatterPending)
            {
                return;
            }
            ikScatterPending = false;
            // Leg diagnostics are written INSIDE the job, so read them after the join.
            if (BasisLegSwivelDebug.Enabled)
            {
                if (TryGetLegDiagnostics(0, out Basis.IK.BasisLegDiagnostics dl))
                {
                    BasisLegSwivelDebug.Record("L", Time.time, dl, BendVsAnteriorDeg(IKJob.kneeBendPrefLeft));
                }
                if (TryGetLegDiagnostics(1, out Basis.IK.BasisLegDiagnostics dr))
                {
                    BasisLegSwivelDebug.Record("R", Time.time, dr, BendVsAnteriorDeg(IKJob.kneeBendPrefRight));
                }
            }
            WatchdogCheckPoseStream("IKDest/PostSolve (stream, pre-scatter)");
            sMarkerIKDestPoseScatter.Begin();
            PoseSkeleton.ScatterNow();
            sMarkerIKDestPoseScatter.End();
            BasisFiniteWatchdog.Checkpoint("IKDest/PostPoseScatter (FBIK solve output)");
        }
        void Step21PublishWorldData()
        {
            sMarkerIKDestPublish.Begin();
            PublishIKWorldData();
            sMarkerIKDestPublish.End();
        }
        void Step22SampleRecorders()
        {
            ref BasisEerieMovement data = ref IKJob;
            if (BasisCalibrationDebugRecorder.RuntimeActive)
            {
                BasisCalibrationDebugRecorder.RuntimeBone("head", BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation, data.offsetRotationHead, BasisLocalAvatarDriver.Mapping.head);
                BasisCalibrationDebugRecorder.RuntimeBone("hips", BasisLocalBoneDriver.HipsControl.OutgoingWorldData.rotation, data.offsetRotationHips, BasisLocalAvatarDriver.Mapping.Hips);
                BasisCalibrationDebugRecorder.RuntimeBone("leftFoot", BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation, data.offsetRotationLeftFoot, BasisLocalAvatarDriver.Mapping.leftFoot);
                BasisCalibrationDebugRecorder.RuntimeBone("rightFoot", BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation, data.offsetRotationRightFoot, BasisLocalAvatarDriver.Mapping.rightFoot);
                Transform animRoot = localPlayer?.BasisAvatar?.Animator != null ? localPlayer.BasisAvatar.Animator.transform : null;
                BasisCalibrationDebugRecorder.RuntimeEndFrame(localPlayer != null ? localPlayer.transform : null, animRoot);
            }
            if (BasisArmIKRuntimeRecorder.Active)
            {
                var armMap = BasisLocalAvatarDriver.Mapping;
                BasisArmIKRuntimeRecorder.Sample( armMap.leftUpperArm, armMap.leftLowerArm, armMap.leftHand, armMap.RightUpperArm, armMap.RightLowerArm, armMap.rightHand, data.targetPositionLeftHand, data.targetPositionRightHand, data.hintPositionLeftHand, data.hintPositionRightHand, data.hintWeightLeftHand, data.hintWeightRightHand);
            }
        }
        public void CompleteSolveIfPending()
        {
            if (ikSolveScheduled)
            {
                ikSolveHandle.Complete();
                ikSolveScheduled = false;
            }
        }
        float BendVsAnteriorDeg(Vector3 bendNormal)
        {
            Vector3 anterior = IKJob.kneeAnteriorRef;
            if (bendNormal.sqrMagnitude < 1e-8f || anterior.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }
            return Vector3.Angle(bendNormal, anterior);
        }
    }
}