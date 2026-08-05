using Basis.Network.Core.Compression;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Player;
using Basis.Scripts.UI.NamePlate;
using GatorDragonGames.JigglePhysics;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Drives setup and runtime behavior for a remote player's avatar:
    /// calibration, TPose swap-in/out, nameplate/mouth job registration,
    /// jiggle physics setup, and renderer configuration.
    /// </summary>
    [System.Serializable]
    public class BasisRemoteAvatarDriver : BasisAvatarDriver
    {
        // Remote calibration is the main-thread half of every avatar load, reload, far LOD swap
        // and range re-entry, and it reported one number for ~20 different stages. These split it
        // so a load-in spike attributes to the stage that owns it instead of "calibration".
        static readonly ProfilerMarker sMarkerCalibrate = new ProfilerMarker("BasisDriver.Avatar.Calibrate");
        static readonly ProfilerMarker sMarkerTpose = new ProfilerMarker("BasisDriver.Avatar.Calibrate.Tpose");
        static readonly ProfilerMarker sMarkerDetect = new ProfilerMarker("BasisDriver.Avatar.Calibrate.DetectReferences");
        static readonly ProfilerMarker sMarkerBoneData = new ProfilerMarker("BasisDriver.Avatar.Calibrate.BoneData");
        static readonly ProfilerMarker sMarkerBodyFit = new ProfilerMarker("BasisDriver.Avatar.Calibrate.BodyFit");
        static readonly ProfilerMarker sMarkerFace = new ProfilerMarker("BasisDriver.Avatar.Calibrate.Face");
        static readonly ProfilerMarker sMarkerRenderers = new ProfilerMarker("BasisDriver.Avatar.Calibrate.Renderers");
        static readonly ProfilerMarker sMarkerRegister = new ProfilerMarker("BasisDriver.Avatar.Calibrate.BoneJobRegister");
        // BoneJobRegister measured as ~92% of calibration, so it gets split again. Three
        // candidates live under it and they have completely different fixes: a main-thread job
        // fence, TransformAccessArray mutation of the SyncBoneCount x players skeleton array, and
        // the per-player interpolation slot seed (which fences the interpolation job separately).
        static readonly ProfilerMarker sMarkerRegisterSlot = new ProfilerMarker("BasisDriver.Avatar.Calibrate.BoneJobRegister.SlotSeed");
        static readonly ProfilerMarker sMarkerRegisterAdd = new ProfilerMarker("BasisDriver.Avatar.Calibrate.BoneJobRegister.Add");
        static readonly ProfilerMarker sMarkerJiggle = new ProfilerMarker("BasisDriver.Avatar.Calibrate.Jiggle");

        /// <summary>
        /// Invoked after calibration completes successfully.
        /// </summary>
        public Action CalibrationComplete;

        /// <summary>
        /// Cached transform references (head, hips, etc.) auto-detected at calibration.
        /// </summary>
        [SerializeField]
        public BasisTransformMapping References = new BasisTransformMapping();

        /// <summary>
        /// This avatar's curl/splay → finger rotation map. Rebuilt on every avatar change;
        /// <see cref="HandGridGeneration"/> ticks with it so buffers decoded against the previous
        /// avatar re-expand instead of holding stale finger rotations across the swap.
        /// </summary>
        public readonly BasisHandPoseGrid HandGrid = new BasisHandPoseGrid();
        public int HandGridGeneration = 1;

        /// <summary>
        /// All skinned renderers under the avatar's animator (filled during calibration).
        /// </summary>
        public SkinnedMeshRenderer[] SkinnedMeshRenderer;

        /// <summary>
        /// The associated high-level player wrapper for this avatar.
        /// </summary>
        public IBasisPlayer Player;

        /// <summary>
        /// Whether event hookups (like visibility checks) were made.
        /// </summary>
        public bool HasEvents = false;

        /// <summary>
        /// Cached length of <see cref="SkinnedMeshRenderer"/> to avoid repeated property lookups.
        /// </summary>
        public int SkinnedMeshRendererLength;

        /// <summary>
        /// Initial avatar local scale captured during calibration. Its one consumer was the bone
        /// job's nameplate height, which divided the live root scale by it to get a resize ratio and
        /// multiplied a fixed 1.2 m by that; the measured placement stores model units instead, so
        /// nothing reads this now. Kept because it is a captured fact about the avatar that a
        /// serialized driver field exposes to inspectors and external tooling.
        /// </summary>
        public Vector3 AvatarInitialScale = Vector3.one;

        /// <summary>
        /// Hips → nameplate-bottom distance for this avatar in model units, measured at calibration
        /// while the rig is guaranteed to be in T-pose. 0 until measured. Cached rather than
        /// recomputed per registration because the renderer-bounds half of the measurement is only
        /// meaningful in T-pose: re-registering a live avatar mid-wave would read its raised arms as
        /// the top of its head. See <see cref="CaptureNamePlateAnchor"/>.
        /// </summary>
        public float NamePlateHeightAboveHipsModel;

        /// <summary>
        /// Tracks whether this avatar has been registered with the remote bone job system.
        /// </summary>
        public bool InBoneDriver = false;

        /// <summary>
        /// Jiggle rigs on the current avatar (filled during calibration).
        /// </summary>
        /// <summary>Filtered out of the content-harvest snapshot by BasisAvatarFactory at load;
        /// include-inactive, entries can be destroyed later — null-and-activity gate on use.</summary>
        public JiggleRig[] JiggleRigs = Array.Empty<JiggleRig>();
        private static Vector3[] sJiggleRootsBeforeSnap = Array.Empty<Vector3>();

        /// <summary>
        /// The wearer's networked body fit (see Basis.IK.BasisBodyFitCore). They stretch/collapse their
        /// own avatar's arm, leg and spine segments to match their real proportions; without replaying
        /// that here every remote would render them at the avatar's authored proportions instead.
        /// Identity (all 1) is a no-op end to end.
        /// </summary>
        public Basis.IK.BasisBodyFitResult AppliedBodyFit = Basis.IK.BasisBodyFitResult.Identity;

        readonly Transform[] _fitBones = new Transform[Basis.IK.BasisBodyFitApply.BoneCount];
        readonly Vector3[] _fitRestLocal = new Vector3[Basis.IK.BasisBodyFitApply.BoneCount];
        readonly float[] _fitScales = new float[Basis.IK.BasisBodyFitApply.BoneCount];
        bool _fitRestCaptured;

        /// <summary>
        /// Performs remote-avatar calibration and registers it with the job system.
        /// Initializes TPose, references, face visibility, eye/blink drivers, and physics colliders.
        /// </summary>
        /// <param name="RemotePlayer">The remote player whose avatar is being configured.</param>
        public void RemoteCalibration(BasisRemotePlayer RemotePlayer)
        {
            if (!IsAble(RemotePlayer))
            {
                return;
            }
            else
            {
                // BasisDebug.Log("RemoteCalibration Underway", BasisDebug.LogTag.Avatar);
            }

            Player = RemotePlayer;

            // Cache renderers and prep avatar layer/tpose
            SkinnedMeshRenderer = Player.BasisAvatar.SkinnedMeshRenderers;
            if (SkinnedMeshRenderer == null)
            {
                var renders = Player.BasisAvatar.Renders;
                var skinnedCount = 0;
                for (var i = 0; i < renders.Length; i++)
                {
                    if (renders[i] is SkinnedMeshRenderer)
                    {
                        skinnedCount++;
                    }
                }
                SkinnedMeshRenderer = new SkinnedMeshRenderer[skinnedCount];
                var skinnedWriteIndex = 0;
                for (var i = 0; i < renders.Length; i++)
                {
                    if (renders[i] is SkinnedMeshRenderer skinnedMeshRenderer)
                    {
                        SkinnedMeshRenderer[skinnedWriteIndex++] = skinnedMeshRenderer;
                    }
                }
            }
            SkinnedMeshRendererLength = SkinnedMeshRenderer.Length;
            // Far avatars are skipped: BasisFarLodGenerator captures the payload skeleton under the
            // same "Animated TPose" controller this would apply, and BuildAvatar writes those baked
            // locals straight onto the bones — the hierarchy is already in the pose before we get
            // here. The pair costs two runtimeAnimatorController assignments (an animator rebind
            // each) plus a full humanoid Animator.Update, per install, on the transmit tick.
            NeedsTposeReset = !Player.BasisAvatar.IsFarLodAvatar;
            if (NeedsTposeReset)
            {
                using (sMarkerTpose.Auto())
                {
                    PutAvatarIntoTPose();
                }
            }

            RemotePlayer.BasisAvatar.HumanScale = RemotePlayer.BasisAvatar.Animator.humanScale;
            RemotePlayer.BasisAvatar.Animator.applyRootMotion = false;
            RemotePlayer.BasisAvatar.Animator.updateMode = AnimatorUpdateMode.Normal;
            RemotePlayer.BasisAvatar.Animator.speed = 0;
            RemotePlayer.BasisAvatar.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            AvatarInitialScale = Player.BasisAvatar.transform.localScale;

            // Auto-detect bone refs and record TPose. Pass Animator.transform so
            // References.AnimatorRoot caches the actual animator root — downstream
            // calibration steps then read References.AnimatorRoot instead of going
            // through the Animator.transform property each time.
            // Twist bones are detected on remotes too: a networked body fit scales the forearm/upper-arm
            // segments, and the twist helpers sit partway along those segments. Scaling the arm without
            // moving the twists leaves them at the wrong fraction of a now-longer bone, which shows up as
            // mesh distortion around the elbow. Cost is a one-time child-name search per arm at load.
            using (sMarkerDetect.Auto())
            {
                BasisTransformMapping.AutoDetectReferences(Player.BasisAvatar.Animator, Player.BasisAvatar.Animator.transform, ref References, detectArmTwist: true, humanoidBones: Player.BasisAvatar.TransformStorage?.HumanoidBones);
                BasisAvatarModelCache.RecordPosesCached(References, Player.BasisAvatar.Animator);
            }

            // ── Capture T-pose bone rotations and bone transforms for the receiver ──
            // This enables direct bone transform writes (no SetHumanPose needed).
            using (sMarkerBoneData.Auto())
            {
                CaptureReceiverBoneData(RemotePlayer);
            }

            // Capture the fresh authored bind, then apply this player's body fit. Order matters: the
            // rest capture must see authored local positions, so it runs before any fit is written.
            // Seeding from CACM covers every path that supplies an avatar record — a live avatar change,
            // initial load, and the server's late-join replay all set it before calibration runs — while
            // a fit-only update that arrived since is already in AppliedBodyFit and survives the reseed.
            using (sMarkerBodyFit.Auto())
            {
                SeedBodyFitFromAvatarRecord(RemotePlayer);
                CaptureBodyFitRestLocal();
                ApplyRemoteBodyFit();
            }

            // Register authored motion (drives non-humanoid transforms the bone job / IK don't touch); rest captured at the current TPose.
            var authoredMotions = RemotePlayer.BasisAvatar.AuthoredMotions;
            if (authoredMotions != null)
            {
                for (int i = 0; i < authoredMotions.Length; i++)
                {
                    BasisAuthoredMotionSystem.Register(authoredMotions[i]);
                }
            }

            // Face visibility setup. Not every avatar has a face mesh (far avatars, generic
            // imports without face wiring) — those skip visibility tracking entirely.
            Player.FaceIsVisible = false;
            if (RemotePlayer.BasisAvatar == null)
            {
                BasisDebug.LogError("Missing Avatar On Remote", BasisDebug.LogTag.Avatar);
            }
            SkinnedMeshRenderer faceVisemeMesh = RemotePlayer.BasisAvatar.FaceVisemeMesh;
            using (sMarkerFace.Auto())
            {
                if (Player.FaceRenderer != null)
                {
                    // Mute before the deferred destroy: the outgoing avatar's renderer fires a
                    // final OnBecameInvisible during its end-of-frame teardown, and that late
                    // notification would stomp the visibility state (and face driver) just set
                    // up for the incoming avatar.
                    Player.FaceRenderer.Check = null;
                    GameObject.Destroy(Player.FaceRenderer);
                    Player.FaceRenderer = null;
                }
                if (faceVisemeMesh != null)
                {
                    Player.UpdateFaceVisibility(faceVisemeMesh.isVisible);
                    Player.FaceRenderer = BasisHelpers.GetOrAddComponent<BasisMeshRendererCheck>(faceVisemeMesh.gameObject);
                    Player.FaceRenderer.Check += Player.UpdateFaceVisibility;
                }
                else
                {
                    BasisDebug.Log("Missing Face for " + Player.DisplayName, BasisDebug.LogTag.Avatar);
                    Player.UpdateFaceVisibility(false);
                }

                // Blink + eyes
                // Initialize unconditionally — Initialize handles a missing blink mesh
                // gracefully (sets BlinkingEnabled = false) and eye calibration still runs
                // for avatars that only have eye bones.
                RemotePlayer.RemoteFaceDriver.Initialize(Player, RemotePlayer.BasisAvatar);
            }
            using (sMarkerRenderers.Auto())
            {
                // Renderer perf flags
                RemoteRenderMeshSettings(BasisLayerMapper.RemoteAvatarLayer, SkinnedMeshRendererLength, SkinnedMeshRenderer);
                // Seed the skin LOD for the distance this avatar loaded at — ChangeMeshLOD is only
                // edge-triggered on LOD boundary crossings, so a reload at a stable distance never
                // reaches these fresh renderers.
                BasisAvatarSkinLOD.Apply(SkinnedMeshRenderer, SkinnedMeshRendererLength, RemotePlayer.CurrentLodLevel);
                // Snapshot the authored shadow modes before anything reduces them, then seed the tier.
                BasisAvatarShadowLOD.Capture(RemotePlayer);
                BasisAvatarShadowLOD.Apply(RemotePlayer, RemotePlayer.CurrentLodLevel);
                Basis.Scripts.Rendering.BasisAvatarVisibility.Register(RemotePlayer);
            }

            RemotePlayer.BasisAvatar.Animator.logWarnings = false;

            // Register with the RemoteBoneJobSystem (including skeleton bones for job-based apply).
            // Use the cached References.AnimatorRoot rather than walking through
            // RemotePlayer.BasisAvatar.Animator.transform on each line.
            var receiver = RemotePlayer.NetworkReceiver;
            Transform animatorRoot = References.AnimatorRoot;
            // Sampled before the network rescale below writes this same transform — jiggle collider
            // radii are authored in metres against this scale and rebased off it at build time.
            ColliderScaleReference = animatorRoot.localScale;
            using (sMarkerRegister.Auto())
            {
                RegisterAvatarWithBoneJobSystem(RemotePlayer, snapToNetworkPose: false);
            }

            // player.RemoteBoneDriver.InitializeFromAvatar(player);
            RemotePlayer.BasisAvatar.Animator.enabled = false;

            using (sMarkerJiggle.Auto())
            {
                SetupAvatarJiggleColliders();
            }
            if (NeedsTposeReset)
            {
                using (sMarkerTpose.Auto())
                {
                    ResetAvatarAnimator();
                }
            }

            // JiggleRigs is filtered out of the content-harvest snapshot by BasisAvatarFactory at
            // load — no walk here, and recalibrations reuse the same stored set. The set is
            // include-inactive, so the loops gate on activity the way the old active-only scan did.
            int jiggleRigCount = JiggleRigs.Length;
            if (sJiggleRootsBeforeSnap.Length < jiggleRigCount)
            {
                sJiggleRootsBeforeSnap = new Vector3[jiggleRigCount];
            }
            Vector3[] jiggleRootsBeforeSnap = sJiggleRootsBeforeSnap;
            for (int Index = 0; Index < jiggleRigCount; Index++)
            {
                JiggleRig snapRig = JiggleRigs[Index];
                if (snapRig == null || !snapRig.gameObject.activeInHierarchy)
                {
                    continue;
                }
                var jiggleRoot = snapRig.GetJiggleRigData().rootBone;
                if (jiggleRoot != null)
                {
                    jiggleRootsBeforeSnap[Index] = jiggleRoot.position;
                }
            }

            // TPose hips locals for the root derivation below (also fed to the job system
            // inside RegisterAvatarWithBoneJobSystem).
            float3 tposeHipsLocalPos;
            quaternion tposeHipsLocalRot;
            if (References.TposeLocal.TryGetValue(HumanBodyBones.Hips, out var hipsTposeLocal))
            {
                tposeHipsLocalPos = hipsTposeLocal.position;
                tposeHipsLocalRot = hipsTposeLocal.rotation;
            }
            else
            {
                tposeHipsLocalPos = float3.zero;
                tposeHipsLocalRot = quaternion.identity;
            }

            // Apply scale before snapping any pose so localScale is in place for
            // the first frame; UpdateAllAvatarsJob's HasScaleChange tick would
            // otherwise overwrite the network scale on the first iteration.
            // GetLatestNetworkPose returns HIPS world (high-precision channel,
            // also what the server reduction system reads).
            receiver.GetLatestNetworkPose(out var hipsWorldPos, out var hipsWorldRot, out var networkScale);
            animatorRoot.localScale = networkScale;
            BasisRemoteNetworkDriver.SeedScaleState(receiver.playerId, networkScale);

            // Derive an approximate root pose using the same inverse math as
            // BulkCopyHipsAndDeriveJob (assumes hips is effectively a child of
            // root — typical Unity humanoid). Only used to put the hierarchy
            // somewhere sensible; the hips world apply right after this is
            // hierarchy-agnostic and overrides any approximation error.
            // conjugate, not inverse — unit quaternions only.
            quaternion rootRot = math.mul(hipsWorldRot, math.conjugate(tposeHipsLocalRot));
            float3 scaledLocal = (float3)networkScale * tposeHipsLocalPos;
            float3 rootPos = (float3)hipsWorldPos - math.mul(rootRot, scaledLocal);
            animatorRoot.SetPositionAndRotation(rootPos, rootRot);

            // Snap hips world directly. SetPositionAndRotation walks the actual
            // parent chain, so an intermediate Armature (or any other node)
            // doesn't disturb the result — hips lands exactly at the received
            // high-precision world pose.
            References.Hips.SetPositionAndRotation(hipsWorldPos, hipsWorldRot);

            // Initialize any jiggle rigs. Performance-limit enforcement lives in
            // BasisAvatarPerformanceLimits.TrimExcessComponents (called earlier by
            // BasisAvatarFactory.InitializePlayerAvatar), so by the time we get
            // here the tree has already been trimmed to the allowed count — this
            // loop just wires up whatever's left.
            using (sMarkerJiggle.Auto())
            {
                for (int Index = 0; Index < jiggleRigCount; Index++)
                {
                    JiggleRig Rig = JiggleRigs[Index];
                    if (Rig == null || !Rig.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    Rig.HasAnimatedParameters = false;
                    Rig.OnInitialize();
                    var jiggleRoot = Rig.GetJiggleRigData().rootBone;
                    if (jiggleRoot != null)
                    {
                        Rig.Teleport(jiggleRoot.position - jiggleRootsBeforeSnap[Index]);
                    }
                }
            }

            CalibrationComplete?.Invoke();

            // Seed the far LOD for the distance this avatar loaded at — the transmit tick's
            // swap check is edge-triggered, so a far-away load would otherwise start at full
            // detail. Also drops any far LOD belonging to the previous avatar.
            BasisAvatarFarLOD.SeedAfterCalibration(RemotePlayer);

            BasisFiniteWatchdog.CheckpointRemote("RemoteCalibration/Complete", RemotePlayer);
        }

        /// <summary>
        /// (Re)registers the real avatar's transforms with the bone job system. Split out of
        /// <see cref="RemoteCalibration"/> so the far LOD swap can restore the registration
        /// without a full recalibration. With <paramref name="snapToNetworkPose"/> the avatar is
        /// also snapped onto the latest network pose (scale, derived root, hips world) and the
        /// jiggle rigs are teleported by the travel delta — used when the avatar wakes back up
        /// after the far LOD hid it.
        /// </summary>
        public void RegisterAvatarWithBoneJobSystem(BasisRemotePlayer RemotePlayer, bool snapToNetworkPose)
        {
            var receiver = RemotePlayer.NetworkReceiver;
            if (receiver == null || RemotePlayer.BasisAvatar == null || References?.AnimatorRoot == null ||
                receiver.BoneTransforms == null || !receiver.BoneDecodePre.IsCreated || !receiver.BoneDecodePost.IsCreated)
            {
                // The old avatar is already gone by the time re-registration runs (swap order
                // destroys first). Aborting while still registered would leave every bone
                // TransformAccessArray pointing at the dying hierarchy — same recovery as the
                // factory's catch path.
                RemotePlayer.RemoveFromBoneDriver();
                return;
            }
            Transform animatorRoot = References.AnimatorRoot;

            // No remove here any more. A re-registration keeps the same row and the same
            // SyncBoneCount skeleton slots, so AddRemotePlayer re-points them in place; tearing
            // the entry down first cost SyncBoneCount RemoveAtSwapBack calls against the biggest
            // TransformAccessArray in the system and measured 11.28ms on a crowded instance.
            // AddRemotePlayer still falls back to a real remove if the incoming transforms are
            // unusable, so a dying hierarchy can never stay registered.

            // TPose hips localPosition + the hips decode pair feed the per-frame
            // BulkCopyHipsAndDeriveJob (the inline inverse derivation that
            // turns the received hips world pose into a derived root world
            // pose). Hips world itself is then applied directly via
            // ApplyHipsWorldJob, which is hierarchy-agnostic — these TPose
            // values are only used for the (best-effort) root derivation,
            // not for writing the hips bone's local transform anymore.
            float3 tposeHipsLocalPos = References.TposeLocal.TryGetValue(HumanBodyBones.Hips, out var hipsTposeLocal)
                ? (float3)hipsTposeLocal.position
                : float3.zero;

            // Hips rides in the packet tail rather than the bone block, but it is carried in the
            // same rig-neutral space, so it needs the same decode pair. Built here rather than
            // inside AddRemotePlayer because the rest frame has to be normalised by this avatar's
            // character basis first — the raw TposeFromRoot rotation the registration already
            // carries is in the animator root's frame, which is not shared between rigs. Getting
            // this one wrong tilts hipsLocalRot, which the job divides out of the hips world pose
            // to derive the root, so the whole avatar shifts rather than just its pelvis.
            BasisGenericBoneRotationUtils.BuildDecodeOperators(
                BasisGenericBoneRotationUtils.GetRestFrame(References, HumanBodyBones.Hips),
                BasisGenericBoneRotationUtils.GetRestLocal(References, HumanBodyBones.Hips),
                out quaternion hipsDecodePre, out quaternion hipsDecodePost);

            // Initialize this player's interpolation slot before registering it with the bone
            // job system. The bone Schedule reads _filtered*[playerId] earlier in LateUpdate than
            // BeginWrite's lazy init runs (LateUpdate tail), so a cached/fallback avatar that
            // calibrates within a frame of joining would otherwise be read from uninitialized
            // memory and pose as NaN.
            using (sMarkerRegisterSlot.Auto())
            {
                BasisRemoteNetworkDriver.EnsureSlotInitialized(receiver.playerId);
            }
            // T-pose only. The calibration path is the one that arrives here with the rig posed and
            // References freshly recorded; a far-LOD wake-up or any other re-registration reuses
            // what that measured rather than re-reading a live pose.
            if (!snapToNetworkPose || NamePlateHeightAboveHipsModel <= 0f)
            {
                CaptureNamePlateAnchor(RemotePlayer);
            }

            using (sMarkerRegisterAdd.Auto())
            {
                RemoteBoneJobSystem.AddRemotePlayer(
                    key: receiver.playerId,
                    remotePlayerRoot: animatorRoot,
                    head: References.head,
                    hips: References.Hips,
                    tposeHead: References.TposeFromRoot[HumanBodyBones.Head],
                    tposeHips: References.TposeFromRoot[HumanBodyBones.Hips],
                    tposeHipsLocalPos: tposeHipsLocalPos,
                    hipsDecodePre: hipsDecodePre,
                    hipsDecodePost: hipsDecodePost,
                    // Handed over in the frame the authored Vector2 is already in: (height, forward)
                    // above the animator root, root-relative RENDERED metres. These used to be pushed
                    // through the translation-only ConvertFromLocalSpace overload into "world" and
                    // subtracted back out inside AddRemotePlayer, which cancelled the root translation
                    // but never applied the root ROTATION — so the authored forward offset pointed
                    // along world +Z instead of out of the avatar's face. The head operand has to come
                    // from TposeWorld, not TposeFromRoot: only TposeWorld keeps the root's scale in,
                    // which is what makes the two sides of the subtraction the same kind of metre.
                    authoredCenterEyeLocal: BasisHelpers.AvatarPositionConversion(RemotePlayer.BasisAvatar.AvatarEyePosition),
                    authoredMouthLocal: BasisHelpers.AvatarPositionConversion(RemotePlayer.BasisAvatar.AvatarMouthPosition),
                    tposeHeadWorld: References.TposeWorld[HumanBodyBones.Head].position,
                    tposeRootScale: References.RootScale,
                    NamePlate: RemotePlayer.NamePlateTransformProvider?.Invoke(),
                    AvatarScale: animatorRoot,
                    MouthTransform: RemotePlayer.MouthTransform,
                    namePlateHeightAboveHipsModel: NamePlateHeightAboveHipsModel,
                    boneDecodePre: receiver.BoneDecodePre,
                    boneDecodePost: receiver.BoneDecodePost,
                    boneTransforms: receiver.BoneTransforms
                );
            }
            InBoneDriver = true;
            // The plate pushes its own pose gate into the bone system, but its Initialize runs
            // before NetworkReceiver is linked, so that first push has no key to resolve. This is
            // the earliest point where both the receiver and the plate's TAA slot exist.
            RemotePlayer.OnNamePlateActiveStateShouldRefresh?.Invoke();

            if (!snapToNetworkPose)
            {
                BasisFiniteWatchdog.CheckpointRemote("RemoteRegister/PostBoneJobRegistration", RemotePlayer);
                return;
            }

            Vector3 hipsBeforeSnap = References.Hips.position;
            receiver.GetLatestNetworkPose(out var hipsWorldPos, out var hipsWorldRot, out var networkScale);
            animatorRoot.localScale = networkScale;
            BasisRemoteNetworkDriver.SeedScaleState(receiver.playerId, networkScale);
            // conjugate, not inverse — unit quaternions only. The rest hips LOCAL rotation, not a
            // generic-space value: this snap has no network rotation to decode, it just backs the
            // root out of the hips world pose with the avatar at rest.
            quaternion rootRot = math.mul(hipsWorldRot,
                math.conjugate(BasisGenericBoneRotationUtils.GetRestLocal(References, HumanBodyBones.Hips)));
            float3 scaledLocal = (float3)networkScale * tposeHipsLocalPos;
            float3 rootPos = (float3)hipsWorldPos - math.mul(rootRot, scaledLocal);
            animatorRoot.SetPositionAndRotation(rootPos, rootRot);
            References.Hips.SetPositionAndRotation(hipsWorldPos, hipsWorldRot);

            // Teleport jiggle rigs by the travel delta so they don't whip across the distance
            // the player covered while the avatar was asleep.
            Vector3 jiggleDelta = (Vector3)hipsWorldPos - hipsBeforeSnap;
            int jiggleRigCount = JiggleRigs.Length;
            for (int Index = 0; Index < jiggleRigCount; Index++)
            {
                JiggleRig rig = JiggleRigs[Index];
                if (rig == null || !rig.gameObject.activeInHierarchy)
                {
                    continue;
                }
                rig.Teleport(jiggleDelta);
            }

            BasisFiniteWatchdog.CheckpointRemote("RemoteRegister/PostNetworkPoseSnap (far-LOD wake)", RemotePlayer);
        }

        /// <summary>
        /// Measures how far above the hips this avatar's nameplate belongs, once, at T-pose.
        ///
        /// Everything is gathered in ONE frame — root-relative RENDERED metres, i.e. heights above
        /// the animator root, which is at the avatar's feet. <c>References.TposeWorld</c> is stored
        /// in exactly that frame ("no division by localScale") and
        /// <c>BasisAvatar.AvatarEyePosition.x</c> is authored in it, so the bones and the eye need no
        /// conversion. Renderer bounds are world-space and are brought in by subtracting the root's
        /// world height; that is a pure Y subtraction rather than a full inverse transform because a
        /// remote is registered standing upright, and the crown clamp inside
        /// <see cref="BasisNamePlateAnchorMath"/> bounds the error if one somehow is not.
        ///
        /// Falls back to the old fixed height only when the rig has neither a hips nor a head bone
        /// recorded, which would mean the avatar has no humanoid mapping at all.
        /// </summary>
        private void CaptureNamePlateAnchor(BasisRemotePlayer RemotePlayer)
        {
            float rootScaleY = References.RootScale.y;
            if (float.IsNaN(rootScaleY) || float.IsInfinity(rootScaleY) || rootScaleY <= 1e-6f)
            {
                rootScaleY = 1f;
            }

            if (References.TposeWorld == null ||
                !References.TposeWorld.TryGetValue(HumanBodyBones.Hips, out var tposeHips) ||
                !References.TposeWorld.TryGetValue(HumanBodyBones.Head, out var tposeHead))
            {
                NamePlateHeightAboveHipsModel = BasisNamePlateAnchorMath.LegacyHeightAboveHips / rootScaleY;
                return;
            }

            float eyeAboveRoot = RemotePlayer.BasisAvatar != null ? RemotePlayer.BasisAvatar.AvatarEyePosition.x : 0f;
            bool hasBounds = TryGetTposeRenderedTop(RemotePlayer, out float boundsTopAboveRoot);

            NamePlateHeightAboveHipsModel = BasisNamePlateAnchorMath.MeasureHeightAboveHipsModelUnits(
                hipsAboveRoot: tposeHips.position.y,
                headAboveRoot: tposeHead.position.y,
                eyeAboveRoot: eyeAboveRoot,
                boundsTopAboveRoot: boundsTopAboveRoot,
                hasBounds: hasBounds,
                rootScaleY: rootScaleY);
        }

        /// <summary>
        /// Highest renderer bounds top on the avatar, expressed as a height above the animator root.
        /// This is what puts the plate above hair, ears, horns and hats instead of above the bare
        /// skull the bone-and-eye estimate describes. In T-pose the arms are horizontal, so the only
        /// things that reach over the head are head geometry and deliberately-tall props — and the
        /// caller's clamp is what keeps the latter from dragging the plate into the sky.
        /// </summary>
        private bool TryGetTposeRenderedTop(BasisRemotePlayer RemotePlayer, out float topAboveRoot)
        {
            topAboveRoot = 0f;
            Transform animatorRoot = References.AnimatorRoot;
            if (animatorRoot == null)
            {
                return false;
            }

            float rootY = animatorRoot.position.y;
            float highest = float.NegativeInfinity;

            // Renders is the authored full set (body, clothing, accessories); the skinned array is
            // the fallback for avatars that only ever populated that one.
            Renderer[] renderers = RemotePlayer.BasisAvatar != null ? RemotePlayer.BasisAvatar.Renders : null;
            int rendererCount = renderers != null ? renderers.Length : 0;
            for (int Index = 0; Index < rendererCount; Index++)
            {
                Renderer renderer = renderers[Index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                float top = renderer.bounds.max.y;
                if (top > highest)
                {
                    highest = top;
                }
            }

            if (float.IsNegativeInfinity(highest))
            {
                for (int Index = 0; Index < SkinnedMeshRendererLength; Index++)
                {
                    SkinnedMeshRenderer renderer = SkinnedMeshRenderer[Index];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    float top = renderer.bounds.max.y;
                    if (top > highest)
                    {
                        highest = top;
                    }
                }
            }

            if (float.IsNegativeInfinity(highest) || float.IsNaN(highest))
            {
                return false;
            }

            topAboveRoot = highest - rootY;
            return true;
        }

        /// <summary>
        /// Captures this avatar's rest pose and bone Transform references for all 54 humanoid bones.
        /// Populates the receiver's BoneDecodePre/BoneDecodePost operator tables and BoneTransforms
        /// so that Apply() can write bone transforms directly without SetHumanPose.
        ///
        /// The operators are what convert the rig-neutral network rotations into THIS rig's local
        /// rotations (BasisGenericBoneRotation). They need two rest quantities per bone — the
        /// parent-relative rest rotation (TposeLocal) and the root-relative one (TposeFromRoot) —
        /// and both come out of the same BasisTransformMapping.RecordPoses capture, so they always
        /// describe one consistent T-pose.
        ///
        /// Must be called while the avatar is in T-pose (before ResetAvatarAnimator).
        /// </summary>
        private unsafe void CaptureReceiverBoneData(BasisRemotePlayer remotePlayer)
        {
            var receiver = remotePlayer.NetworkReceiver;
            var animator = remotePlayer.BasisAvatar.Animator;

            // Fingers arrive as ten curl/splay scalars and are expanded through the grid baked from
            // THIS avatar, which is what makes them correctly proportioned for whatever rig we are
            // actually drawing rather than the sender's. Interned per Avatar asset, so a crowd in
            // matching avatars pays for one bake.
            if (!BasisHandPoseGrid.TryAcquire(animator, BasisHandPoseGrid.DefaultIncrement, HandGrid))
            {
                BasisDebug.LogError("Failed to acquire hand pose grid; remote fingers will hold their bind pose.",
                    BasisDebug.LogTag.Avatar);
            }
            HandGridGeneration++;
            int boneCount = BasisBoneRotationCompression.SyncBoneCount;

            // Reused across recalibrations rather than disposed and reallocated: boneCount is the
            // constant SyncBoneCount, so the old buffers are always the right size, and every slot
            // is overwritten below. Nothing holds these across the call — AddRemotePlayer snapshots
            // the operators with ToArray() and the transforms are only read during registration —
            // so a swap avoids a Persistent alloc plus a 54-element managed array per avatar load,
            // which at crowd load-in churn is the allocation, not the work.
            EnsureDecodeBuffer(ref receiver.BoneDecodePre, boneCount);
            EnsureDecodeBuffer(ref receiver.BoneDecodePost, boneCount);
            if (receiver.BoneTransforms == null || receiver.BoneTransforms.Length != boneCount)
            {
                receiver.BoneTransforms = new Transform[boneCount];
            }
            quaternion* preOut = (quaternion*)receiver.BoneDecodePre.GetUnsafePtr();
            quaternion* postOut = (quaternion*)receiver.BoneDecodePost.GetUnsafePtr();
            Transform[] boneTransforms = receiver.BoneTransforms;
            int[] writeOrder = BasisBoneRotationCompression.BONE_WRITE_ORDER;

            // Both rest tables are deterministic per Avatar asset — only bone transforms are
            // per-instance — so prefer the per-model cache and fall back to the live dictionaries.
            // TposeLocal and TposeFromRoot must come from the SAME capture: mixing a cached T with
            // a freshly measured F would build an operator pair describing two different rest
            // poses, which is a silent, small, everywhere-at-once pose error.
            EntityId cacheKey = BasisAvatarModelCache.GetKey(animator);
            var cacheEntry = cacheKey != EntityId.None ? BasisAvatarModelCache.GetOrCreate(cacheKey) : null;
            bool hasCachedRest = cacheEntry?.TposeLocal != null && cacheEntry.TposeFromRoot != null;

            quaternion[] restLocalByBone = hasCachedRest ? cacheEntry.TposeLocal.Rotations : null;
            quaternion[] restFrameByBone = hasCachedRest ? cacheEntry.TposeFromRoot.Rotations : null;

            // The rest frames on both sides of this branch are RAW root-space rotations, which are
            // not a shared frame between two differently-authored rigs — see
            // BasisGenericBoneRotationUtils.GetCharacterBasis. The cached forward/up belongs to the
            // same capture as the cached rotations, so take the basis from whichever source the
            // rotations came from rather than always from the live mapping.
            quaternion characterBasis = hasCachedRest
                ? BasisGenericBoneRotationUtils.GetCharacterBasis(
                    cacheEntry.TposeFromRoot.AvatarForward, cacheEntry.TposeFromRoot.AvatarUp)
                : BasisGenericBoneRotationUtils.GetCharacterBasis(References);

            for (int slot = 0; slot < boneCount; slot++)
            {
                int boneEnum = writeOrder[slot];
                var humanbone = (HumanBodyBones)boneEnum;

                if (!References.GetTransform(humanbone, out var transform))
                {
                    // The avatar has no transform for this humanoid bone. This branch used to
                    // be absent and the slot was left at the fresh allocation's zero-init — an
                    // invalid (0,0,0,0) rotation that only stayed harmless because the null
                    // transform drove sSkeletonValid to 0. The buffers are reused now, so an
                    // unwritten slot would register the PREVIOUS avatar's bone transform with
                    // valid = 1 and drive a dead hierarchy. Write the empty slot explicitly.
                    preOut[slot] = quaternion.identity;
                    postOut[slot] = quaternion.identity;
                    boneTransforms[slot] = null;
                    continue;
                }

                quaternion restLocal, restFrame;
                if (hasCachedRest)
                {
                    restLocal = restLocalByBone[boneEnum];
                    restFrame = BasisGenericBoneRotationUtils.NormalizeRestFrame(restFrameByBone[boneEnum], characterBasis);
                }
                else
                {
                    restLocal = BasisGenericBoneRotationUtils.GetRestLocal(References, humanbone);
                    restFrame = BasisGenericBoneRotationUtils.GetRestFrame(References, humanbone, characterBasis);
                }

                if (slot >= BasisBoneRotationCompression.WireBoneSlotCount)
                {
                    // Finger slots carry no generic-space rotation since v47 — BasisHandPoseGrid
                    // writes this rig's LOCAL rotation straight into them. Identity operators make
                    // the compose job's DecodePre * value * DecodePost a pass-through instead of a
                    // generic round trip that would only undo itself.
                    preOut[slot] = quaternion.identity;
                    postOut[slot] = quaternion.identity;
                    boneTransforms[slot] = transform;
                    continue;
                }

                BasisGenericBoneRotationUtils.BuildDecodeOperators(restFrame, restLocal,
                    out preOut[slot], out postOut[slot]);
                boneTransforms[slot] = transform;
            }

            // Store both rest tables for other instances of this avatar. StorePosesToCache fills
            // the same two slots from RecordPosesCached; whichever runs first wins and the other
            // is a no-op, so the pair can never come from two different captures.
            if (cacheEntry != null && !hasCachedRest)
            {
                int totalBones = (int)HumanBodyBones.LastBone;
                if (cacheEntry.TposeLocal == null)
                {
                    cacheEntry.TposeLocal = new BasisAvatarModelCache.TposeLocalData
                    {
                        Rotations = SnapshotRotations(References.TposeLocal, totalBones, out var localPositions),
                        Positions = localPositions
                    };
                }
                if (cacheEntry.TposeFromRoot == null)
                {
                    cacheEntry.TposeFromRoot = new BasisAvatarModelCache.TposeFromRootData
                    {
                        Rotations = SnapshotRotations(References.TposeFromRoot, totalBones, out var rootPositions),
                        Positions = rootPositions,
                        AvatarForward = References.AvatarForwards,
                        AvatarUp = References.AvatarUpwards,
                        AvatarRight = References.AvatarRightwards
                    };
                }
            }
        }

        /// <summary>
        /// Allocates (or keeps) a persistent per-slot operator buffer of exactly
        /// <paramref name="boneCount"/> entries.
        /// </summary>
        private static void EnsureDecodeBuffer(ref NativeArray<quaternion> buffer, int boneCount)
        {
            if (buffer.IsCreated && buffer.Length == boneCount)
            {
                return;
            }
            if (buffer.IsCreated)
            {
                buffer.Dispose();
            }
            buffer = new NativeArray<quaternion>(boneCount, Allocator.Persistent);
        }

        /// <summary>
        /// Flattens one of the calibration pose dictionaries into the by-bone arrays the model
        /// cache stores. Absent bones become (identity, zero), matching StorePosesToCache.
        /// </summary>
        private static quaternion[] SnapshotRotations(
            System.Collections.Generic.Dictionary<HumanBodyBones, Basis.Scripts.Common.BasisCalibratedCoords> source,
            int totalBones, out Unity.Mathematics.float3[] positions)
        {
            var rotations = new quaternion[totalBones];
            positions = new Unity.Mathematics.float3[totalBones];
            for (int i = 0; i < totalBones; i++)
            {
                if (source != null && source.TryGetValue((HumanBodyBones)i, out var coords))
                {
                    rotations[i] = coords.rotation;
                    positions[i] = coords.position;
                }
                else
                {
                    rotations[i] = quaternion.identity;
                    positions[i] = Unity.Mathematics.float3.zero;
                }
            }
            return rotations;
        }

        /// <summary>
        /// Applies the wearer's networked body fit to this remote avatar. Safe to call before the avatar
        /// has loaded — the fit is stored and replayed by RemoteCalibration once the bind is captured.
        /// Main thread only (touches Transform.localPosition).
        /// </summary>
        public void SetBodyFit(in Basis.IK.BasisBodyFitResult fit)
        {
            AppliedBodyFit = fit;
            ApplyRemoteBodyFit();
        }

        private void SeedBodyFitFromAvatarRecord(BasisRemotePlayer RemotePlayer)
        {
            if (RemotePlayer == null)
            {
                return;
            }
            AppliedBodyFit = Basis.IK.BasisBodyFitNetworking.ToFitResult(
                RemotePlayer.CACM.ArmScale, RemotePlayer.CACM.LegScale, RemotePlayer.CACM.TorsoScale);
        }

        /// <summary>
        /// Snapshots the authored local positions of every fitted bone. Keying off this copy rather than
        /// the live value is what makes re-applying idempotent — writing rest*scale over an already
        /// scaled transform would compound the fit every time a new one arrived.
        /// </summary>
        private void CaptureBodyFitRestLocal()
        {
            Basis.IK.BasisBodyFitApply.CollectBones(References, _fitBones);
            for (int i = 0; i < _fitBones.Length; i++)
            {
                Transform bone = _fitBones[i];
                _fitRestLocal[i] = bone != null ? bone.localPosition : Vector3.zero;
            }
            _fitRestCaptured = true;
        }

        private void ApplyRemoteBodyFit()
        {
            if (!_fitRestCaptured)
            {
                return;
            }

            Basis.IK.BasisBodyFitApply.CollectScales(in AppliedBodyFit, _fitScales);
            for (int i = 0; i < _fitBones.Length; i++)
            {
                Transform bone = _fitBones[i];
                if (bone != null)
                {
                    bone.localPosition = _fitRestLocal[i] * _fitScales[i];
                }
            }
        }

        /// <summary>
        /// True while the avatar is temporarily swapped to a TPose animator.
        /// </summary>
        public bool CurrentlyTposing;

        /// <summary>
        /// Set by <see cref="RemoteCalibration"/> when it swapped the animator into TPose, so the
        /// restore at the end of calibration is skipped for avatars that never needed the swap.
        /// </summary>
        private bool NeedsTposeReset;

        /// <summary>
        /// Stores the original animator controller while TPose is active.
        /// </summary>
        public RuntimeAnimatorController SavedruntimeAnimatorController;

        /// <summary>
        /// Loads and applies a TPose controller to the avatar's animator,
        /// forcing an update so bone poses are consistent for reference capture.
        /// </summary>
        public void PutAvatarIntoTPose()
        {
            // BasisDebug.Log("PutAvatarIntoTPose", BasisDebug.LogTag.Avatar);
            CurrentlyTposing = true;
            if (SavedruntimeAnimatorController == null)
            {
                SavedruntimeAnimatorController = Player.BasisAvatar.Animator.runtimeAnimatorController;
            }

            Player.BasisAvatar.Animator.runtimeAnimatorController = BasisPlayerFactory.TposeController;
            ForceUpdateAnimator(Player.BasisAvatar.Animator);
        }

        /// <summary>
        /// Forces the animator to advance by <see cref="Time.deltaTime"/> to apply state changes immediately.
        /// </summary>
        /// <param name="Anim">Animator to update.</param>
        public void ForceUpdateAnimator(Animator Anim)
        {
            // Specify the time you want the Animator to update to (in seconds)
            float desiredTime = Time.deltaTime;

            // Call the Update method to force the Animator to update to the desired time
            Anim.Update(desiredTime);
        }

        /// <summary>
        /// Restores the original animator controller after TPose operations and clears flags.
        /// </summary>
        public void ResetAvatarAnimator()
        {
            // BasisDebug.Log("ResetAvatarAnimator", BasisDebug.LogTag.Avatar);
            Player.BasisAvatar.Animator.runtimeAnimatorController = SavedruntimeAnimatorController;
            SavedruntimeAnimatorController = null;
            CurrentlyTposing = false;
        }

        /// <summary>
        /// Rebuilds jiggle rig colliders based on player settings (async).
        /// Removes existing colliders, fetches settings, then conditionally adds new ones.
        /// </summary>
        private int JiggleColliderSetupGeneration;

        public async void SetupAvatarJiggleColliders()
        {
            RemoveJiggleRigColliders();
            int generation = ++JiggleColliderSetupGeneration;
            BasisPlayerSettingsData BasisPlayerSettingsData = await BasisPlayerSettingsManager.RequestPlayerSettings(Player.UUID);
            if (Player == null || Player.IsDestroyed)
            {
                return;
            }
            if (generation != JiggleColliderSetupGeneration)
            {
                return;
            }
            if (BasisPlayerSettingsData.AvatarInteraction && Player.IsConsideredFallBackAvatar == false)
            {
                AddJiggleRigColliders(References, allowColliderLOD: true);
            }
        }

        /// <summary>
        /// Validates that the provided remote player and its avatar/animator are present.
        /// </summary>
        /// <param name="remotePlayer">Remote player to test.</param>
        /// <returns>True if calibration may proceed; otherwise false.</returns>
        public bool IsAble(BasisRemotePlayer remotePlayer)
        {
            if (IsNull(remotePlayer.BasisAvatar))
            {
                return false;
            }
            if (IsNull(remotePlayer.BasisAvatar.Animator))
            {
                return false;
            }
            if (remotePlayer == null || remotePlayer.IsDestroyed)
            {
                BasisDebug.LogError("Missing Object during calibration");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Logs and returns whether the provided Unity object reference is null.
        /// </summary>
        /// <param name="obj">Unity object to test.</param>
        /// <returns>True if null; otherwise false.</returns>
        public bool IsNull(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                BasisDebug.LogError("Missing Object during calibration");
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
