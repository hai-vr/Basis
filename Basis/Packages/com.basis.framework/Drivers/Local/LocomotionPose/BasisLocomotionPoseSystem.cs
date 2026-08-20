using Basis.IK;
using Basis.Scripts.Avatar;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    public sealed class BasisLocomotionPoseSystem
    {
        // ── FAST PATH SWITCH ──
        // Setting-backed (settings → Body Tracking → Job-Driven Locomotion, default ON) and read every
        // frame, so it can be A/B-toggled live: ON stops the engine Animator once the per-avatar bake is
        // ready and produces the base pose via BasisLocomotionPoseJob on a worker; OFF resumes the
        // Animator immediately (the bake is kept for instant re-enable).
        public static bool JobDrivenLocomotionPose => Basis.BasisUI.BasisSettingsDefaults.FBIKJobLocomotion.RawValue;

        // With full leg FBT and DisableAnimationsInFBT, every animator-visible bone is tracker-driven or
        // vetoed, so the base pose cannot matter — stop evaluating it entirely. Stock controller only:
        // a custom controller may drive non-humanoid transforms that a freeze would visibly kill.
        public static readonly bool FreezeAnimatorInFullFBT = true;

        // The freeze waits for the StopAll idle params to settle so it cannot latch a mid-crossfade pose.
        const float FreezeArmSeconds = 0.5f;

        static RuntimeAnimatorController sStockController;

        public static void NotifyStockControllerAssigned(RuntimeAnimatorController controller)
        {
            sStockController = controller;
        }

        public static bool IsStockController(Animator animator)
        {
            return sStockController != null && animator != null
                && ReferenceEquals(animator.runtimeAnimatorController, sStockController);
        }

        BasisLocomotionPoseBaker _baker;
        BasisLocomotionPoseBake _bake;
        BasisLocoSimState _sim;
        bool _simDirty = true;
        bool _landingLatch;
        float _freezeArmTimer;
        readonly BasisLocoContribution[] _contributionsManaged = new BasisLocoContribution[BasisLocomotionGraph.MaxContributions];
        int _contributionCount;
        NativeArray<BasisLocoContribution> _contributionsNative;
        JobHandle _handle;
        bool _scheduled;

        static readonly Unity.Profiling.ProfilerMarker sMarkerLocoGate = new Unity.Profiling.ProfilerMarker("BasisDriver.LocoPose.Gate");
        static readonly Unity.Profiling.ProfilerMarker sMarkerLocoGraphStep = new Unity.Profiling.ProfilerMarker("BasisDriver.LocoPose.GraphStep");
        static readonly Unity.Profiling.ProfilerMarker sMarkerLocoDispatch = new Unity.Profiling.ProfilerMarker("BasisDriver.LocoPose.Dispatch");

        public void NotifyLanding()
        {
            _landingLatch = true;
        }

        public void OnRigBuilt()
        {
            CompleteIfPending();
            DisposeBakeData();
            _simDirty = true;
            _landingLatch = false;
            _freezeArmTimer = 0f;
        }

        public void Schedule(BasisLocalRigDriver rig, Animator animator, in BasisLocoParams frameParams, float deltaTime)
        {
            bool frozen;
            bool fastActive;
            using (sMarkerLocoGate.Auto())
            {
                bool rigReady = rig != null && rig.IKDataReady && rig.IKJobCreated && rig.RigLayerActive && rig.PoseSkeleton.IsCreated;
                bool graphValid = rigReady && rig.PlayableGraph.IsValid();
                bool stock = IsStockController(animator);

                Step1TickBake(rig, animator, stock, rigReady);
                Step2ResolveGates(stock, rigReady, graphValid, deltaTime, out frozen, out fastActive);
            }

            if (!fastActive)
            {
                _simDirty = true;
                return;
            }

            Step4StepLocomotionGraph(in frameParams, deltaTime, frozen);
            if (_contributionCount == 0)
            {
                return;
            }

            Step5DispatchPoseJob(rig);
        }

        void Step1TickBake(BasisLocalRigDriver rig, Animator animator, bool stock, bool rigReady)
        {
            if (JobDrivenLocomotionPose && stock && rigReady && _bake == null && _baker == null)
            {
                _baker = new BasisLocomotionPoseBaker();
                _baker.Start(animator, sStockController, rig.PoseSkeleton.Nodes, rig.basisTransformMapping.Hips);
            }

            if (_baker != null && !_baker.Failed)
            {
                if (!_baker.Tick() && _baker.Done)
                {
                    _bake = _baker.TakeBake();
                    EnsureRuntimeArrays();
                    _baker.Dispose();
                    _baker = null;
                }
            }
        }

        void Step2ResolveGates(bool stock, bool rigReady, bool graphValid, float deltaTime, out bool frozen, out bool fastActive)
        {
            bool tposeLike = BasisLocalAvatarDriver.CurrentlyTposing || BasisLocalAvatarDriver.SavedruntimeAnimatorController != null;
            bool fbtConditions = FreezeAnimatorInFullFBT && stock && !tposeLike
                && BasisAvatarIKStageCalibration.HasLegFBIKTrackers
                && Basis.BasisUI.BasisSettingsDefaults.DisableAnimationsInFBT.RawValue;
            _freezeArmTimer = fbtConditions ? _freezeArmTimer + deltaTime : 0f;
            frozen = fbtConditions && _freezeArmTimer >= FreezeArmSeconds;

            fastActive = JobDrivenLocomotionPose && stock && !tposeLike && rigReady && graphValid
                && _bake != null && _bake.Ready;
        }

        void Step4StepLocomotionGraph(in BasisLocoParams frameParams, float deltaTime, bool frozen)
        {
            if (_simDirty)
            {
                _sim = BasisLocomotionGraph.DefaultSimState;
                _landingLatch = false;
                _simDirty = false;
                _contributionCount = 0;
            }

            if (frozen && _contributionCount != 0)
            {
                return;
            }

            using var _ = sMarkerLocoGraphStep.Auto();
            BasisLocoParams stepParams = frameParams;
            stepParams.LandingTrigger = _landingLatch;
            _contributionCount = BasisLocomotionGraph.Step(
                ref _sim, ref stepParams, deltaTime,
                _bake.ClipLengthsManaged, _bake.ClipLoopingManaged, _contributionsManaged);
            _landingLatch = stepParams.LandingTrigger;
            for (int i = 0; i < _contributionCount; i++)
            {
                _contributionsNative[i] = _contributionsManaged[i];
            }
        }

        void Step5DispatchPoseJob(BasisLocalRigDriver rig)
        {
            using var dispatch = sMarkerLocoDispatch.Auto();
            var job = new BasisLocomotionPoseJob
            {
                Contributions = _contributionsNative,
                ContributionCount = _contributionCount,
                Rotations = _bake.Rotations,
                HipsPositions = _bake.HipsPositions,
                SnapshotScales = _bake.SnapshotScales,
                ClipRotationOffset = _bake.ClipRotationOffset,
                ClipHipsOffset = _bake.ClipHipsOffset,
                ClipSampleCount = _bake.ClipSampleCount,
                ClipLength = _bake.ClipLength,
                RestPositions = rig.PoseSkeleton.RestLocalPosition,
                NodeCount = _bake.NodeCount,
                HipsNode = _bake.HipsNode,
                OutLocalPosition = rig.PoseSkeleton.Stream.LocalPosition,
                OutLocalRotation = rig.PoseSkeleton.Stream.LocalRotation,
                OutLocalScale = rig.PoseSkeleton.Stream.LocalScale,
            };
            _handle = job.Schedule();
            _scheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }

        public bool TryComplete(BasisPoseSkeleton skeleton)
        {
            if (!_scheduled)
            {
                return false;
            }
            _handle.Complete();
            _scheduled = false;
            skeleton.RefreshRootFromTransform();
            return true;
        }

        public void CompleteIfPending()
        {
            if (_scheduled)
            {
                _handle.Complete();
                _scheduled = false;
            }
        }

        void EnsureRuntimeArrays()
        {
            if (!_contributionsNative.IsCreated)
            {
                _contributionsNative = new NativeArray<BasisLocoContribution>(BasisLocomotionGraph.MaxContributions, Allocator.Persistent);
            }
        }

        void DisposeBakeData()
        {
            _baker?.Dispose();
            _baker = null;
            _bake?.Dispose();
            _bake = null;
            _contributionCount = 0;
        }

        public void Dispose()
        {
            CompleteIfPending();
            _baker?.Dispose();
            _baker = null;
            _bake?.Dispose();
            _bake = null;
            if (_contributionsNative.IsCreated)
            {
                _contributionsNative.Dispose();
            }
        }
    }
}
