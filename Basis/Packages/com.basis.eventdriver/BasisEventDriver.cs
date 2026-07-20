using Basis.BTween;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Constraints;
using Basis.Scripts.Debugging;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Transmitters;
using Basis.BasisUI;
using Basis.Scripts.UI;
using Basis.Scripts.UI.NamePlate;
using GatorDragonGames.JigglePhysics;
using HVR.Basis.Comms;
using SteamAudio;
using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using static Basis.EventDriver.BasisEventDriverProfileSections;
using Prof = Basis.EventDriver.BasisEventDriverMarkers;

namespace Basis.EventDriver
{
    /// <summary>
    /// Central per-frame driver that coordinates device actions, networking compute/apply,
    /// physics scheduling for JigglePhysics, and various local simulation hooks.
    /// </summary>
    [DefaultExecutionOrder(-31950)]
    public partial class BasisEventDriver : MonoBehaviour
    {
        // ── Platform flag (single #if, used as runtime bool everywhere else) ──
        public static readonly bool IsHeadlessClient =
#if UNITY_SERVER
        true;
#else
            false;
#endif
        private static readonly List<Camera> JiggleCullCameras = new List<Camera>(8);
        // Profiler section IDs live in BasisEventDriverProfileSections (pulled in via `using static`).
        // ── Partial method declarations (calls are stripped in non-editor builds) ──
        partial void ProfileLateUpdateInit();
        partial void ProfileBegin(int section);
        partial void ProfileBegin2();
        partial void ProfileEnd(int section);
        partial void ProfileEnd2(int section);
        partial void ProfileLateUpdateFinish();
        partial void ProfileBeforeRenderInit();
        partial void ProfileBeforeRenderFinish();
        /// <summary>
        /// Accumulator used to track elapsed time since the last interval tick.
        /// </summary>
        public float timeSinceLastUpdate = 0f;
        /// <summary>
        /// Frame delta time (scaled).
        /// </summary>
        public float DeltaTime;
        /// <summary>
        /// Current time as a double (scaled), mirrored from <see cref="Time.timeAsDouble"/>.
        /// </summary>
        public double TimeAsDouble;
        /// <summary>
        /// Fixed-step time as a double, mirrored from <see cref="Time.fixedTimeAsDouble"/>.
        /// </summary>
        public double fixedTimeAsDouble;
        /// <summary>
        /// Fixed-step delta time in seconds.
        /// </summary>
        public float fixedDeltaTime;
        /// <summary>
        /// Unscaled frame delta time in seconds.
        /// </summary>
        public float unscaledDeltaTime;
        /// <summary>
        /// realtimeSinceStartupAsDouble
        /// </summary>
        public double realtimeSinceStartupAsDouble;
        /// <summary>
        /// material we use to display jiggle physics visually
        /// </summary>
        [SerializeField]
        private UnityEngine.Material proceduralMaterial;
        /// <summary>
        /// mesh we use to display around the jiggle physics
        /// </summary>
        [SerializeField]
        private Mesh sphereMesh;
        /// <summary>
        /// mesh we use to display capsule jiggle physics colliders
        /// </summary>
        [SerializeField]
        private Mesh capsuleMesh;
        /// <summary>
        /// Instance of Basis Event Driver
        /// </summary>
        public static BasisEventDriver Instance;
        public static event Action OnUpdate;
        public static event Action OnLateUpdate;

        private static Action _onUpdateCachedDelegate;
        private static Delegate[] _onUpdateInvocationList = System.Array.Empty<Delegate>();
        private static Action _onLateUpdateCachedDelegate;
        private static Delegate[] _onLateUpdateInvocationList = System.Array.Empty<Delegate>();

        private static void ResetEventCallbacks()
        {
            OnUpdate = null;
            OnLateUpdate = null;
            _onUpdateCachedDelegate = null;
            _onUpdateInvocationList = System.Array.Empty<Delegate>();
            _onLateUpdateCachedDelegate = null;
            _onLateUpdateInvocationList = System.Array.Empty<Delegate>();
        }

        private static void InvokeEventCallbacks(Action callbacks, string callbackName, ref Action cachedDelegate, ref Delegate[] cachedInvocationList)
        {
            if (!ReferenceEquals(callbacks, cachedDelegate))
            {
                cachedDelegate = callbacks;
                cachedInvocationList = callbacks == null ? System.Array.Empty<Delegate>() : callbacks.GetInvocationList();
            }

            Delegate[] invocationList = cachedInvocationList;
            int invocationCount = invocationList.Length;
            for (int index = 0; index < invocationCount; index++)
            {
                Delegate callback = invocationList[index];
                try
                {
                    ((Action)callback).Invoke();
                }
                catch (Exception ex)
                {
                    BasisDebug.LogErrorOnce(
                        $"BasisEventDriver.{callbackName} callback "
                        + $"{callback.Method.DeclaringType?.FullName}.{callback.Method.Name} "
                        + $"failed: {ex}",
                        BasisDebug.LogTag.Event);
                }
            }
        }

        public static bool StateOfOnRenderBefore = false;
        /// <summary>
        /// Unity enable hook. Subscribes render callbacks (client), initializes scene and network drivers.
        /// </summary>
        public void OnEnable()
        {
            Instance = this;
            if (!IsHeadlessClient)
            {
                Application.onBeforeRender += OnBeforeRender;
            }
            BasisOpenLipSyncDriver.BeginInitialize();
            BasisSceneFactory.Initialize();
            Basis.Scripts.Networking.Sync.BasisSyncDriver.Initialize();
            RemoteBoneJobSystem.Initialize();
            BasisOpenLipSyncDriver.EndInitialize();
        }

        /// <summary>
        /// Unity destroy hook. Cleans up network/physics resources and unsubscribes callbacks.
        /// </summary>
        public void OnDestroy()
        {
            try
            {
                BasisOpenLipSyncDriver.Shutdown();
                Basis.Scripts.Networking.Sync.BasisSyncDriver.OnDestroy();
                Application.onBeforeRender -= OnBeforeRender;
                RemoteBoneJobSystem.Dispose();
                BasisAuthoredMotionSystem.Dispose();
                BasisConstraintSystem.Dispose();
                BasisAvatarBufferPool.Deinitialize();
            }
            finally
            {
                if (ReferenceEquals(Instance, this))
                {
                    Instance = null;
                    ResetEventCallbacks();
                }
            }
        }

        /// <summary>
        /// Unity disable hook. Unsubscribes from the before-render callback on clients.
        /// </summary>
        public void OnDisable()
        {
            if (!IsHeadlessClient)
            {
                Application.onBeforeRender -= OnBeforeRender;
            }
        }
        /// <summary>
        /// Unity update loop. Drains main-thread actions, advances network simulation (compute),
        /// schedules remote interpolation, updates input on clients, and runs periodic tasks.
        /// </summary>
        public void Update()
        {
            try { UpdateBody(); }
            catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver.Update failed: {ex}", BasisDebug.LogTag.Event); }
        }

        private void UpdateBody()
        {
            using var updateScope = Prof.Update.Auto();

            DeltaTime = Time.deltaTime;
            unscaledDeltaTime = Time.unscaledDeltaTime;
            realtimeSinceStartupAsDouble = Time.realtimeSinceStartupAsDouble;
            TimeAsDouble = Time.timeAsDouble;

            // Join the network compute kicked off at the tail of the previous LateUpdate, before
            // the main-thread action drain and join/leave lifecycle below mutate any receiver.
            using (Prof.NetworkCompleteCompute.Auto())
            {
                BasisNetworkManagement.CompleteNetworkCompute(DeltaTime);
            }

            using (Prof.FrameClockTick.Auto())
            {
                BasisFrameClock.Tick(unscaledDeltaTime);
            }

            if (BasisLocalPlayer.PlayerReady)
            {
                using (Prof.VisemeSimulate.Auto())
                {
                    BasisLocalPlayer.Instance.LocalVisemeDriver.Simulate(DeltaTime);
                }
            }
            // Drain everything that arrived from worker threads
            using (Prof.MainThreadActions.Auto())
            {
                while (BasisDeviceManagement.mainThreadActions.TryDequeue(out System.Action action))
                {
                    try
                    {
                        action.Invoke();
                    }
                    catch (Exception ex)
                    {
                        BasisDebug.LogError(
                            $"MainThread action failed: {ex}",
                            BasisDebug.LogTag.Event
                        );
                    }
                }
            }
            // Player join/leave work is budgeted separately so a mass disconnect
            // (hundreds of players at once) can't chain N synchronous GameObject.Destroy
            // calls in a single frame and stall the renderer.
            using (Prof.LifecycleQueue.Auto())
            {
                BasisNetworkHandleRemoval.ProcessLifecycleQueue(BasisNetworkHandleRemoval.LifecycleBudgetPerFrame);
            }
            if (!IsHeadlessClient)
            {
                using (Prof.InputSystemUpdate.Auto())
                {
                    InputSystem.Update();
                }
            }

            using (Prof.OscAcquisition.Auto())
            {
                OSCAcquisitionServer.Simulate();
            }
            using (Prof.PerformanceLimits.Auto())
            {
                SMModuleAvatarPerformanceLimits.Simulate();
            }
            using (Prof.DebugOptions.Auto())
            {
                SMModuleDebugOptions.Simulate();
            }
            using (Prof.GazeFoveationAuto.Auto())
            {
                Basis.Scripts.Device_Management.EyeTracking.BasisGazeFoveationAutoDriver.Simulate();
            }
            using (Prof.HighPlayerCap.Auto())
            {
                BasisHighPlayerCapPerformanceMode.Simulate();
            }
            using (Prof.OnUpdateCallbacks.Auto())
            {
                InvokeEventCallbacks(OnUpdate, nameof(OnUpdate), ref _onUpdateCachedDelegate, ref _onUpdateInvocationList);
            }
            timeSinceLastUpdate += DeltaTime;
        }

        /// <summary>
        /// Fixed-step simulation used for scene-level processing.
        /// </summary>
        public void FixedUpdate()
        {
            try { FixedUpdateBody(); }
            catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver.FixedUpdate failed: {ex}", BasisDebug.LogTag.Event); }
        }

        private void FixedUpdateBody()
        {
            using var fixedUpdateScope = Prof.FixedUpdate.Auto();

            fixedDeltaTime = Time.fixedDeltaTime;
            fixedTimeAsDouble = Time.fixedTimeAsDouble;
            if (BasisLocalPlayer.PlayerReady)
            {
                using (Prof.SceneFactorySimulate.Auto())
                {
                    BasisSceneFactory.Simulate(fixedDeltaTime);
                }
            }
        }
        // AfterAvatarChanges carries BOTH avatar-content hooks (e.g. HVR eye reads, which run
        // arbitrary per-avatar code) AND the local avatar transmit (TransmissionResults.Simulate
        // → Compress → every outgoing avatar/face packet). A bare multicast Invoke lets one
        // throwing content hook abort every later subscriber — which silently killed all avatar
        // transmission. Invoke each subscriber under its own catch instead; the invocation list
        // is cached and only rebuilt when the delegate instance changes.
        private static Action _afterAvatarChangesCachedDelegate;
        private static Delegate[] _afterAvatarChangesInvocationList = System.Array.Empty<Delegate>();

        private static void InvokeAfterAvatarChangesSafely()
        {
            Action current = BasisNetworkTransmitter.AfterAvatarChanges;
            if (!ReferenceEquals(current, _afterAvatarChangesCachedDelegate))
            {
                _afterAvatarChangesCachedDelegate = current;
                _afterAvatarChangesInvocationList = current == null ? System.Array.Empty<Delegate>() : current.GetInvocationList();
            }

            Delegate[] list = _afterAvatarChangesInvocationList;
            int count = list.Length;
            for (int Index = 0; Index < count; Index++)
            {
                try
                {
                    ((Action)list[Index])();
                }
                catch (Exception ex)
                {
                    BasisDebug.LogErrorOnce($"AfterAvatarChanges subscriber {list[Index].Method?.DeclaringType?.Name}.{list[Index].Method?.Name} failed: {ex}", BasisDebug.LogTag.Event);
                }
            }
        }

        /// <summary>
        /// LateUpdate step for device management loop, eye simulation, local player late sim,
        /// microphone updates (client), network apply, and JigglePhysics scheduling/pose/render.
        /// </summary>
        public void LateUpdate()
        {
            try { LateUpdateBody(); }
            catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver.LateUpdate failed: {ex}", BasisDebug.LogTag.Event); }
        }

        private void LateUpdateBody()
        {
            using var lateUpdateScope = Prof.LateUpdate.Auto();

            ProfileLateUpdateInit();

            if (StateOfOnRenderBefore)
            {
                OnBeforeRender();
            }

            // Comms eye/Vixxy/activity actuation is pumped in front of the network-apply barrier so
            // its main-thread cost overlaps the in-flight BasisRemoteNetworkDriver interpolation
            // jobs (UpdateAllAvatarsJob + InterpolateBoneRotationsJob), which
            // SimulateNetworkApply's Apply() completes below. Vixxy actuates blendshapes/materials,
            // so it must stay ahead of BasisBlendShapeDriver and the render. VariableNetworking is
            // split off to a later barrier (see the AuthoredMotion schedule/complete below) — it
            // produces no visible write this frame, so it hides behind a different job. Safe: the
            // comms batch reads only its own networked variable state, never the remote pose/bone
            // output produced by SimulateNetworkApply.
            using (Prof.EyeTrackingSimulate.Auto())
            {
                Basis.Scripts.Device_Management.EyeTracking.BasisEyeTrackingManager.Simulate();
            }
            using (Prof.CommsActuators.Auto())
            {
                HVRCommsUpdateDriver.SimulateActuators();
            }

            using (Prof.NetworkApply.Auto())
            {
                ProfileBegin(PROF_NETWORK_APPLY);
                ProfileBegin2();
                using (Prof.NetFireBeforeApply.Auto())
                {
                    BasisLocalPlayer.FireJustBeforeNetworkApply();
                }
                ProfileEnd2(PROF_NET_FIRE_BEFORE_APPLY);
                ProfileBegin2();
                using (Prof.SyncTransmitOwned.Auto())
                {
                    Basis.Scripts.Networking.Sync.BasisSyncDriver.TransmitOwned(TimeAsDouble);
                }
                ProfileEnd2(PROF_NET_TRANSMIT_PICKUPS);
                ProfileBegin2();
#if !UNITY_SERVER && !BASIS_DISABLE_MICROPHONE
                using (Prof.MicrophoneUpdate.Auto())
                {
                    BasisLocalMicrophoneDriver.MicrophoneUpdate();
                }
#endif
                ProfileEnd2(PROF_NET_MICROPHONE);
                using (Prof.SyncScheduleRemote.Auto())
                {
                    Basis.Scripts.Networking.Sync.BasisSyncDriver.ScheduleRemote(DeltaTime);
                }
                ProfileBegin2();
                using (Prof.SyncCompleteRemote.Auto())
                {
                    Basis.Scripts.Networking.Sync.BasisSyncDriver.CompleteRemote();
                }
                ProfileEnd2(PROF_NET_COMPLETE_REMOTE_LERP);
                using (Prof.NetFireAfterRemoteSync.Auto())
                {
                    BasisLocalPlayer.FireAfterRemoteSyncInterpolated();
                }
                ProfileBegin2();
                using (Prof.NetSimulateApply.Auto())
                {
                    BasisNetworkManagement.SimulateNetworkApply();
                }
                ProfileEnd2(PROF_NET_SIMULATE_APPLY);
                ProfileEnd(PROF_NETWORK_APPLY);
            }

            // ── Device management ──
            ProfileBegin(PROF_DEVICE_MANAGEMENT);
            if (BasisDeviceManagement.HasEvents)
            {
                using (Prof.DeviceManagement.Auto())
                {
                    BasisDeviceManagement.Instance.Simulate();
                }
            }
            ProfileEnd(PROF_DEVICE_MANAGEMENT);

            // ── BTween ──
            ProfileBegin(PROF_BTWEEN);
            using (Prof.BTween.Auto())
            {
                BasisTweenManager.Simulate(realtimeSinceStartupAsDouble);
            }
            ProfileEnd(PROF_BTWEEN);

            // ── Local player ──
            ProfileBegin(PROF_LOCAL_PLAYER);
            if (BasisLocalPlayer.PlayerReady)
            {
                using (Prof.LocalPlayer.Auto())
                {
                    BasisLocalCameraDriver LocalCameraDriver = BasisLocalCameraDriver.Instance;
                    BasisLocalPlayer localplayer = BasisLocalPlayer.Instance;
                    using (Prof.FacialBlink.Auto())
                    {
                        localplayer.FacialBlinkDriver.Simulate(TimeAsDouble);
                    }
                    using (Prof.VisemeApply.Auto())
                    {
                        localplayer.LocalVisemeDriver.Apply();
                    }
                    using (Prof.LocalPlayerSimulate.Auto())
                    {
                        localplayer.Simulate(DeltaTime);
                    }
                    // Complete the finger slerp job (TransformAccessArray write) before touching the
                    // camera transform, so Simulate never overlaps jobified transform access.
                    using (Prof.LocalHandApply.Auto())
                    {
                        localplayer.LocalHandDriver.Apply();
                    }
                    using (Prof.LocalCameraSimulate.Auto())
                    {
                        LocalCameraDriver.Simulate(DeltaTime);
                    }
                    using (Prof.LocalEyeSimulate.Auto())
                    {
                        localplayer.LocalEyeDriver.Simulate(DeltaTime);
                    }
                }
            }
            ProfileEnd(PROF_LOCAL_PLAYER);

            using (Prof.RemoteBoneComplete.Auto())
            {
                BasisNetworkManagement.CompleteRemoteBoneJobSystemJobs();
            }

            // ── Remote audio simulate ──
            ProfileBegin(PROF_REMOTE_AUDIO_SIMULATE);
            using (Prof.RemoteAudioSimulate.Auto())
            {
                BasisRemoteAudioDriver.Simulate(DeltaTime);
            }
            ProfileEnd(PROF_REMOTE_AUDIO_SIMULATE);

            // Complete the eye apply here rather than right after its schedule in LocalEyeDriver.Simulate,
            // so the eye compute/apply jobs overlap the remote bone complete + remote audio simulate above.
            // Still ahead of JigglePhysics.ScheduleSimulate, so the transform write has no jiggle job to stall on.
            if (BasisLocalPlayer.PlayerReady)
            {
                using (Prof.LocalEyeApply.Auto())
                {
                    BasisLocalPlayer.Instance.LocalEyeDriver.Apply();
                }
            }

            // ── Nameplate schedule ──
            ProfileBegin(PROF_NAMEPLATE_SCHEDULE);
            using (Prof.NamePlateSchedule.Auto())
            {
                BasisRemoteNamePlateDriver.ScheduleSimulate(TimeAsDouble);
            }
            ProfileEnd(PROF_NAMEPLATE_SCHEDULE);
            using (Prof.ContentSphereSchedule.Auto())
            {
                BasisContentSphereBillboardDriver.ScheduleSimulate();
            }
#if STEAMAUDIO_ENABLED
            using (Prof.SteamAudioSchedule.Auto())
            {
                SteamAudioManager.Schedule();
            }
#endif

            // ── Remote face simulate (job schedule) ──
            ProfileBegin(PROF_REMOTE_FACE_SIMULATE);
            using (Prof.RemoteFaceSimulate.Auto())
            {
                BasisRemoteFaceManagement.Simulate(TimeAsDouble, DeltaTime);
            }
            ProfileEnd(PROF_REMOTE_FACE_SIMULATE);

            // ── Remote audio apply ──
            ProfileBegin(PROF_REMOTE_AUDIO_APPLY);
            using (Prof.RemoteAudioApply.Auto())
            {
                BasisRemoteAudioDriver.Apply();
            }
            ProfileEnd(PROF_REMOTE_AUDIO_APPLY);

            try
            {
                using (Prof.BuiltInAddresses.Auto())
                {
                    HVRBasisBuiltInAddresses.Simulate();
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogErrorOnce($"HVRBasisBuiltInAddresses.Simulate failed: {ex}", BasisDebug.LogTag.Event);
            }

            // ── BlendShape apply ──
            if (BasisSettingsDefaults.LocalHeadBlendShapes.RawValue)
            {
                using (Prof.ReadBlendShapes.Auto())
                {
                    BasisAvatarDriver.ScheduleReadBlendShapes();
                }
            }

            // ── Authored motion: write non-humanoid authored bones before jiggle samples them ──
            // Split schedule/complete (was a synchronous Complete(Schedule())) so the authored
            // transform-write job overlaps a slice of independent main-thread work instead of
            // stalling on it. VariableNetworking is that filler: it touches none of the authored
            // transforms, produces no avatar-visible write this frame, and stays ahead of the
            // AfterAvatarChanges eye read below — so its cost hides behind the job's wall-clock.
            JobHandle authoredMotionJob;
            using (Prof.AuthoredMotionSchedule.Auto())
            {
                authoredMotionJob = BasisAuthoredMotionSystem.Schedule();
            }
            using (Prof.VariableNetworking.Auto())
            {
                HVRCommsUpdateDriver.SimulateVariableNetworking();
            }
            using (Prof.AuthoredMotionComplete.Auto())
            {
                BasisAuthoredMotionSystem.Complete(authoredMotionJob);
            }

            // ── Constraints: resolve the BasisConstraint* components ──
            // Sits after authored motion (so a constraint may source an authored bone) and ahead of
            // the jiggle schedule below (so jiggle samples the constrained pose, not the stale one).
            //
            // Scheduled here but completed further down, on the far side of jiggle's preparation.
            // That preparation is main-thread work — parameter pushes, collider and tree commits —
            // and on a steady frame it reads no bone pose at all, so the constraint solve can run
            // against those same bones while it happens instead of the main thread just waiting.
            JobHandle constraintJob;
            using (Prof.ConstraintSchedule.Auto())
            {
                constraintJob = BasisConstraintSystem.Schedule();
            }

            // ── JigglePhysics schedule ──
            ProfileBegin(PROF_JIGGLE_SCHEDULE);
            using (Prof.JiggleSchedule.Auto())
            {
                using (Prof.JiggleCullCameras.Auto())
                {
                    JiggleCullCameras.Clear();
                    var jiggleCullCamera = BasisLocalCameraDriver.CameraInstance;
                    if (jiggleCullCamera != null)
                    {
                        JiggleCullCameras.Add(jiggleCullCamera);
                    }
                    BasisCullingCameraRegistry.CollectInto(JiggleCullCameras);
                    JigglePhysics.SetCullingCameras(JiggleCullCameras);
                }

                fixedDeltaTime = Time.fixedDeltaTime;

                // A pending tree rebuild measures rest lengths off live bone positions, which the solve
                // is in the middle of writing — so on those frames the overlap is given up rather than
                // letting the rebuild read half-solved poses. Rebuilds are rare; steady frames are not.
                if (JigglePhysics.WillRebuildTrees)
                {
                    using (Prof.ConstraintComplete.Auto())
                    {
                        BasisConstraintSystem.Complete(constraintJob);
                    }
                    constraintJob = default;
                }

                bool jiggleReady;
                using (Prof.JigglePrepare.Auto())
                {
                    jiggleReady = JigglePhysics.PrepareSimulate(TimeAsDouble, fixedDeltaTime);
                }
                // On rebuild frames the constraint solve was already completed above and the
                // handle zeroed — skip the empty second fence instead of logging a no-op marker.
                if (!constraintJob.Equals(default(JobHandle)))
                {
                    using (Prof.ConstraintComplete.Auto())
                    {
                        BasisConstraintSystem.Complete(constraintJob);
                    }
                }
                if (jiggleReady)
                {
                    using (Prof.JiggleDispatch.Auto())
                    {
                        JigglePhysics.DispatchSimulate();
                    }
                }
            }
            ProfileEnd(PROF_JIGGLE_SCHEDULE);

            // ── Network transmit (reads bone results via GetOutGoingMouth) ──
            ProfileBegin(PROF_NETWORK_TRANSMIT);
            using (Prof.AfterAvatarChanges.Auto())
            {
                InvokeAfterAvatarChangesSafely();
            }
            ProfileEnd(PROF_NETWORK_TRANSMIT);

            // ── JigglePhysics pose ──
            ProfileBegin(PROF_JIGGLE_POSE);
            using (Prof.JiggleSchedulePose.Auto())
            {
                JigglePhysics.SchedulePose(TimeAsDouble);
            }
            ProfileEnd(PROF_JIGGLE_POSE);
            // ── Nameplate complete ──
            ProfileBegin(PROF_NAMEPLATE_COMPLETE);
            using (Prof.NamePlateComplete.Auto())
            {
                BasisRemoteNamePlateDriver.CompleteNamePlates();
            }
            ProfileEnd(PROF_NAMEPLATE_COMPLETE);
            using (Prof.ContentSphereComplete.Auto())
            {
                BasisContentSphereBillboardDriver.Complete();
            }

            using (Prof.JoinLeaveNotification.Auto())
            {
                BasisJoinLeaveNotification.Simulate(TimeAsDouble);
            }
            using (Prof.SimulateBeacon.Auto())
            {
                IndividualPlayerProvider.SimulateBeacon(DeltaTime);
            }

            bool drawJiggle = SMModuleDebugOptions.UseGizmos && SMModuleDebugOptions.UseJiggleVisuals;
            if (drawJiggle)
            {
                using (Prof.JiggleRender.Auto())
                {
                    JigglePhysics.ScheduleRender();
                    JigglePhysics.CompleteRender(proceduralMaterial, sphereMesh, capsuleMesh);
                }
            }

            // ── Kick off pipelined network compute: runs on worker threads through the jiggle pose
            //    completion and the render gap, joined at the top of the next Update. ──
            using (Prof.NetworkBeginCompute.Auto())
            {
                BasisNetworkManagement.BeginNetworkCompute(unscaledDeltaTime);
            }

            // ── JigglePhysics complete pose ──
            // Deferred to a player-loop step just ahead of the particle update when possible, so the
            // rest of the frame overlaps the pose jobs instead of the main thread waiting on them
            // here. Nothing between this point and there reads a jiggled bone; rendering is the
            // consumer. Falls back to completing inline when the loop step could not be installed.
            ProfileBegin(PROF_JIGGLE_COMPLETE_POSE);
            if (BasisLateJiggleCompletion.Enabled)
            {
                BasisLateJiggleCompletion.MarkPosePending();
            }
            else
            {
                using (Prof.JiggleCompletePose.Auto())
                {
                    JigglePhysics.CompletePose();
                }
            }
            ProfileEnd(PROF_JIGGLE_COMPLETE_POSE);

            // ── Shadow clone blendshapes ──
            ProfileBegin(PROF_SHADOW_CLONE);
            if (BasisSettingsDefaults.LocalHeadBlendShapes.RawValue)
            {
                using (Prof.ShadowClone.Auto())
                {
                    BasisAvatarDriver.ApplyShadowCloneBlendShapes();
                }
            }
            ProfileEnd(PROF_SHADOW_CLONE);

            StateOfOnRenderBefore = true;
            if (IsHeadlessClient)
            {
                OnBeforeRender();
            }

            using (Prof.OnLateUpdateCallbacks.Auto())
            {
                InvokeEventCallbacks(OnLateUpdate, nameof(OnLateUpdate), ref _onLateUpdateCachedDelegate, ref _onLateUpdateInvocationList);
            }
            ProfileLateUpdateFinish();
        }
        /// <summary>
        /// Callback invoked before rendering each frame (client), used to run final local player
        /// render-time simulation and to publish avatar changes.
        /// </summary>
        private void OnBeforeRender()
        {
            try { OnBeforeRenderBody(); }
            catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver.OnBeforeRender failed: {ex}", BasisDebug.LogTag.Event); }
        }

        private void OnBeforeRenderBody()
        {
            using var beforeRenderScope = Prof.BeforeRender.Auto();

            ProfileBeforeRenderInit();

#if STEAMAUDIO_ENABLED
            using (Prof.SteamAudioApply.Auto())
            {
                SteamAudioManager.Apply();
            }
#endif

            // Publish the nameplate vertex transforms scheduled back in CompleteNamePlates —
            // by now the jobs have had the tail of LateUpdate and the whole post-late phase.
            using (Prof.NamePlateFinish.Auto())
            {
                BasisGlobalNamePlateRenderer.FinishFrame();
            }

            if (BasisLocalPlayer.PlayerReady)
            {
                try { using (Prof.SimulateOnRender.Auto()) BasisLocalPlayer.Instance.SimulateOnRender(); }
                catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver.SimulateOnRender failed: {ex}", BasisDebug.LogTag.Event); }


                try { using (Prof.EyeTrackingSimulate.Auto()) Basis.Scripts.Device_Management.EyeTracking.BasisEyeTrackingManager.Simulate(); }
                catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver eye-tracking simulate failed: {ex}", BasisDebug.LogTag.Event); }

                try { using (Prof.RemoteFaceApply.Auto()) BasisRemoteFaceManagement.Apply(); }
                catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver remote-face apply failed: {ex}", BasisDebug.LogTag.Event); }
#if !BASIS_DISABLE_MICROPHONE
                try { using (Prof.MicrophoneIcon.Auto()) BasisLocalCameraDriver.Instance.microphoneIconDriver.Simulate(DeltaTime); }
                catch (Exception ex) { BasisDebug.LogErrorOnce($"BasisEventDriver microphone-icon simulate failed: {ex}", BasisDebug.LogTag.Event); }
#endif
            }
            StateOfOnRenderBefore = false;

            ProfileBeforeRenderFinish();
        }

        /// <summary>
        /// Application quit hook. Disposes physics and stops microphone processing.
        /// </summary>
        public void OnApplicationQuit()
        {
            try
            {
                JigglePhysics.Dispose();
#if !BASIS_DISABLE_MICROPHONE
                BasisLocalMicrophoneDriver.StopProcessingThread();
#endif
                BasisRemoteNamePlateDriver.Dispose();
                BasisContentSphereBillboardDriver.Dispose();
            }
            finally
            {
                if (ReferenceEquals(Instance, this)) Instance = null;
                ResetEventCallbacks();
            }
        }

        public void OnDrawGizmosSelected()
        {
            if (IsHeadlessClient)
            {
                return;
            }

            JigglePhysics.OnDrawGizmos();
        }

        // ── Editor-only profiling implementation ────────────────────
        // Partial methods with no implementation are stripped by the compiler,
        // so all Profile*() calls above become zero-cost no-ops in non-editor builds.
#if UNITY_EDITOR
        private bool _profiling;
        private System.Diagnostics.Stopwatch _lateUpdateSW;
        private System.Diagnostics.Stopwatch _beforeRenderSW;

        partial void ProfileLateUpdateInit()
        {
            _profiling = BasisEventDriverProfilerData.Enabled;
            if (_profiling)
                _lateUpdateSW = System.Diagnostics.Stopwatch.StartNew();
        }

        partial void ProfileBegin(int section)
        {
            if (!_profiling) return;
            switch (section)
            {
                case PROF_REMOTE_AUDIO_SIMULATE:
                    BasisEventDriverProfilerData.RemoteAudioDriverCount = BasisRemoteAudioDriver.DriversCount;
                    break;
                case PROF_NAMEPLATE_COMPLETE:
                    BasisEventDriverProfilerData.NamePlateJobWasIncomplete = false;
                    break;
            }
            BasisEventDriverProfilerData.Begin();
        }

        partial void ProfileBegin2()
        {
            if (_profiling)
                BasisEventDriverProfilerData.Begin2();
        }

        partial void ProfileEnd(int section)
        {
            if (!_profiling) return;
            double ms = BasisEventDriverProfilerData.End();
            switch (section)
            {
                case PROF_NETWORK_APPLY: BasisEventDriverProfilerData.NetworkApplyMs = ms; break;
                case PROF_DEVICE_MANAGEMENT: BasisEventDriverProfilerData.DeviceManagementMs = ms; break;
                case PROF_REMOTE_AUDIO_SIMULATE: BasisEventDriverProfilerData.RemoteAudioSimulateMs = ms; break;
                case PROF_NAMEPLATE_SCHEDULE: BasisEventDriverProfilerData.NamePlateScheduleMs = ms; break;
                case PROF_BTWEEN: BasisEventDriverProfilerData.BTweenMs = ms; break;
                case PROF_LOCAL_PLAYER: BasisEventDriverProfilerData.LocalPlayerMs = ms; break;
                case PROF_REMOTE_FACE_SIMULATE:
                    BasisEventDriverProfilerData.RemoteFaceSimulateMs = ms;
                    BasisEventDriverProfilerData.RemoteFace_Count = BasisRemoteFaceManagement.count;
                    break;
                case PROF_REMOTE_AUDIO_APPLY: BasisEventDriverProfilerData.RemoteAudioApplyMs = ms; break;
                case PROF_JIGGLE_SCHEDULE: BasisEventDriverProfilerData.JiggleScheduleMs = ms; break;
                case PROF_NETWORK_TRANSMIT: BasisEventDriverProfilerData.NetworkTransmitMs = ms; break;
                case PROF_JIGGLE_POSE: BasisEventDriverProfilerData.JigglePoseMs = ms; break;
                case PROF_MICROPHONE: BasisEventDriverProfilerData.MicrophoneMs = ms; break;
                case PROF_NAMEPLATE_COMPLETE: BasisEventDriverProfilerData.NamePlateCompleteMs = ms; break;
                case PROF_JIGGLE_COMPLETE_POSE: BasisEventDriverProfilerData.JiggleCompletePoseMs = ms; break;
                case PROF_SHADOW_CLONE: BasisEventDriverProfilerData.ShadowCloneMs = ms; break;
            }
        }

        partial void ProfileEnd2(int section)
        {
            if (!_profiling) return;
            double ms = BasisEventDriverProfilerData.End2();
            switch (section)
            {
                case PROF_NET_TRANSMIT_PICKUPS: BasisEventDriverProfilerData.Net_TransmitPickupsMs = ms; break;
                case PROF_NET_FIRE_BEFORE_APPLY: BasisEventDriverProfilerData.Net_FireBeforeApplyMs = ms; break;
                case PROF_NET_SIMULATE_APPLY: BasisEventDriverProfilerData.Net_SimulateNetworkApplyMs = ms; break;
                case PROF_NET_COMPLETE_REMOTE_LERP: BasisEventDriverProfilerData.Net_CompleteRemoteLerpMs = ms; break;
                case PROF_NET_MICROPHONE: BasisEventDriverProfilerData.MicrophoneMs = ms; break;
            }
        }

        partial void ProfileLateUpdateFinish()
        {
            if (!_profiling) return;
            _lateUpdateSW.Stop();
            BasisEventDriverProfilerData.LateUpdateTotalMs = _lateUpdateSW.Elapsed.TotalMilliseconds;
            BasisEventDriverProfilerData.PushHistory();
        }

        partial void ProfileBeforeRenderInit()
        {
            _profiling = BasisEventDriverProfilerData.Enabled;
            if (_profiling)
            {
                BasisEventDriverProfilerData.RemoteFaceJobWasIncomplete =
                    BasisRemoteFaceManagement.HasJob && !BasisRemoteFaceManagement.handle.IsCompleted;
                _beforeRenderSW = System.Diagnostics.Stopwatch.StartNew();
            }
        }

        partial void ProfileBeforeRenderFinish()
        {
            if (!_profiling || _beforeRenderSW == null) return;
            _beforeRenderSW.Stop();
            BasisEventDriverProfilerData.OnBeforeRenderMs = _beforeRenderSW.Elapsed.TotalMilliseconds;
        }
#endif
    }
}
