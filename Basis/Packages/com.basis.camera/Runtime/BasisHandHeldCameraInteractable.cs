using Basis.Scripts.Common;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.InputSystem;
using Basis.Scripts.Device_Management;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Cinematics;

/// <summary>
/// Interactable handheld/fly camera controller:
/// - Pins the capture camera to handheld, playspace, or world space
/// - Provides a desktop “fly” mode with smoothed movement/rotation, momentum, and auto-leveling
/// - Locks/unlocks player controls while interacting
/// </summary>
public abstract partial class BasisHandHeldCameraInteractable : BasisPickupInteractable
{
    /// <summary>Owning handheld camera component and metadata.</summary>
    public BasisHandHeldCamera HHC;

    /// <summary>Reference to the camera UI for orientation updates.</summary>
    private BasisHandHeldCameraUI cameraUI;

    [Header("Camera Settings")]
    /// <summary>Space to which the capture camera is pinned.</summary>
    public CameraPinSpace PinSpace = CameraPinSpace.HandHeld;

    [Header("Flying Camera Settings")]
    /// <summary>Base fly speed (units/second).</summary>
    public float flySpeed = 2f;

    /// <summary>Multiplier applied when fast-move is held.</summary>
    public float flyFastMultiplier = 3f;

    /// <summary>Acceleration toward target velocity.</summary>
    public float flyAcceleration = 10f;

    /// <summary>Deceleration factor when no input (used with momentum).</summary>
    public float flyDeceleration = 8f;

    /// <summary>Position smoothing factor while flying.</summary>
    public float flyMovementSmoothing = 12f;

    [Header("Camera Rotation")]
    /// <summary>Mouse sensitivity for fly rotation.</summary>
    public float mouseSensitivity = 0.5f;

    /// <summary>Smoothing applied to fly rotation changes.</summary>
    [Range(5f, 25f)]
    public float rotationSmoothing = 15f;

    /// <summary>Degrees per second the VR fly stick yaws the camera at full deflection.</summary>
    [Range(15f, 240f)]
    public float vrFlyTurnSpeed = 90f;

    /// <summary>Metres per second the VR fly stick raises or lowers the camera at full deflection.</summary>
    [Range(0.25f, 8f)]
    public float vrFlyElevationSpeed = 2f;

    [Header("Cinematic Controls")]
    /// <summary>Whether to use momentum/inertia for movement.</summary>
    public bool useMomentum = true;

    /// <summary>How quickly momentum falls off.</summary>
    [Range(2f, 12f)]
    public float inertiaDamping = 5f;

    /// <summary>Automatically level the horizon by easing camera roll toward zero.</summary>
    public bool useAutoLeveling = false;

    /// <summary>Strength of the auto-leveling force.</summary>
    public float autoLevelStrength = 2f;

    /// <summary>Extra damping applied to cinematic motion.</summary>
    [Range(0.1f, 0.9f)]
    public float cinematicDamping = 0.8f;

    [Header("VR Handheld Stabilization")]
    public bool useVRHandheldSmoothing = false;
    public bool onlySmoothWhenStreamingToDesktop = true;

    [Range(1f, 30f)]
    public float vrHandheldPositionSmoothing = 12f;

    [Range(1f, 30f)]
    public float vrHandheldRotationSmoothing = 14f;

    private Vector3 smoothedHandheldWorldPos;
    private Quaternion smoothedHandheldWorldRot = Quaternion.identity;
    private bool handheldSmoothingInitialized = false;

    [Header("Smooth Drag")]
    /// <summary>
    /// While the camera is held, the body eases toward the hand rather than being locked to it, so
    /// dragging it swings it in behind the move and lets it settle once the hand stops.
    /// </summary>
    public bool useSmoothDrag = false;

    /// <summary>Seconds the body takes to close the distance to the hand.</summary>
    [Range(MinSmoothDragDamping, MaxSmoothDragDamping)]
    public float smoothDragPositionDamping = 0.4f;

    /// <summary>Seconds the body takes to close the angle to the hand.</summary>
    [Range(MinSmoothDragDamping, MaxSmoothDragDamping)]
    public float smoothDragRotationDamping = 0.5f;

    /// <summary>How far the body may ever trail the hand, in metres at default avatar height.</summary>
    [Range(MinSmoothDragDistance, MaxSmoothDragDistance)]
    public float smoothDragMaxDistance = 0.25f;

    public const float MinSmoothDragDamping = 0.05f;
    public const float MaxSmoothDragDamping = 1.5f;
    public const float MinSmoothDragDistance = 0.05f;
    public const float MaxSmoothDragDistance = 1f;

    private Vector3 smoothDragPosition;
    private Quaternion smoothDragRotation = Quaternion.identity;
    private bool smoothDragInitialized;

    /// <summary>
    /// Sets how long the body takes to reach the hand, clamped back into the range the panel
    /// promises — a settings file is text on disk and can name any number at all.
    /// </summary>
    public void SetSmoothDragPositionDamping(float seconds)
        => smoothDragPositionDamping = Mathf.Clamp(seconds, MinSmoothDragDamping, MaxSmoothDragDamping);

    /// <inheritdoc cref="SetSmoothDragPositionDamping"/>
    public void SetSmoothDragRotationDamping(float seconds)
        => smoothDragRotationDamping = Mathf.Clamp(seconds, MinSmoothDragDamping, MaxSmoothDragDamping);

    /// <inheritdoc cref="SetSmoothDragPositionDamping"/>
    public void SetSmoothDragMaxDistance(float metres)
        => smoothDragMaxDistance = Mathf.Clamp(metres, MinSmoothDragDistance, MaxSmoothDragDistance);

    // --- internal values / locks ---
    private readonly BasisLocks.LockContext LookLock = BasisLocks.GetContext(BasisLocks.LookRotation);
    private readonly BasisLocks.LockContext MovementLock = BasisLocks.GetContext(BasisLocks.Movement);
    private readonly BasisLocks.LockContext CrouchingLock = BasisLocks.GetContext(BasisLocks.Crouching);

    /// <summary>Capture camera’s starting local position (handheld mode baseline).</summary>
    private Vector3 cameraStartingLocalPos;

    /// <summary>Capture camera’s starting local rotation (handheld mode baseline).</summary>
    private Quaternion cameraStartingLocalRot;

    // Modes / orientation
    private BasisCameraOrientation currentOrientation = BasisCameraOrientation.Landscape;
    private float orientationCheckCooldown = 0f;

    [SerializeReference] private BasisParentConstraint cameraPinConstraint;
    [SerializeReference] private BasisFlyCamera flyCamera;

    private const float cameraDefaultScale = 0.00015f;

    [Tooltip("Fraction of the desktop view the camera is allowed to cover before it is scaled down.")]
    [Range(0.1f, 1f)] public float desktopScreenFitFraction = 0.85f;

    [Tooltip("Fraction of the spawn offset the camera is pulled toward the desktop eye. Lower is closer.")]
    public float desktopEyeDistanceScale = 0.325f;

    private Vector3 desktopSpawnOffset = Vector3.forward;
    private Quaternion desktopOffsetRotation = Quaternion.identity;

    [Header("Follow Target")]
    [Tooltip("Continuously focus depth of field on the follow subject, keeping them sharp as they move.")]
    public bool autoFocusFollowSubject = false;

    [Tooltip("Network id of the remote player to follow. Only meaningful while followTargetBound is set.")]
    public ushort followTargetPlayerId = 0;

    /// <summary>
    /// Whether <see cref="followTargetPlayerId"/> is bound to a networked player. Every net id is a
    /// valid target — LiteNetLib hands peer ids out from zero up, so the first player to join is id
    /// 0 — which is why the binding is carried by this flag rather than by a reserved id value.
    /// Read it through <see cref="TryGetFollowTargetPlayer"/> rather than testing the id.
    /// </summary>
    public bool followTargetBound = false;

    /// <summary>Bind the follow target to a networked player.</summary>
    public void SetFollowTargetPlayer(ushort netId)
    {
        followTargetPlayerId = netId;
        followTargetBound = true;
    }

    /// <summary>Release the follow target back to the local player.</summary>
    public void ClearFollowTargetPlayer()
    {
        followTargetPlayerId = 0;
        followTargetBound = false;
    }

    /// <summary>The net id being followed, when one is bound. False means the local player.</summary>
    public bool TryGetFollowTargetPlayer(out ushort netId)
    {
        netId = followTargetPlayerId;
        return followTargetBound;
    }

    public bool IsFollowingRemotePlayer => followTargetBound;

    /// <summary>Resolved pose of whoever the camera follows — local player or a remote — in world space.</summary>
    private struct FollowSubject
    {
        public bool Valid;
        public bool IsRemote;       // resolved from a remote player rather than the local one
        public Vector3 AnchorPos;   // where the camera is placed relative to (centre of mass or root)
        public Vector3 GroundPos;   // the subject's feet, for the feet-relative aim helper
        public Quaternion Yaw;      // yaw-only facing of the subject
        public Vector3 LookPoint;   // head-height point to aim at and focus on
        public float Scale;         // avatar-to-default scale for offset sizing
    }

    /// <summary>True while the fly controls have the camera, so it is off in the world somewhere.</summary>
    public bool IsFlying => pauseMove || isVRFlying;

    /// <inheritdoc/>
    protected override bool DesktopMiddleClickReserved => true;

    /// <summary>
    /// Whether fly mode is armed. The panel's toggle reads and writes this, and it is the one
    /// answer for both platforms — desktop parks the player and hands the camera to the mouse and
    /// keyboard, VR hands it to a controller.
    /// </summary>
    public bool IsFlyModeEnabled => IsFlying;

    /// <summary>
    /// Arms or disarms fly mode from the settings panel. This is the only way into flight in VR;
    /// desktop's middle click enters through the same pair of helpers, so however it was started
    /// the camera lands in one state and the player's locks are balanced.
    /// </summary>
    public void SetFlyModeEnabled(bool enabled)
    {
        if (enabled)
        {
            EnterFlyMode();
        }
        else
        {
            ExitFlyMode();
        }
    }

    /// <summary>
    /// Takes the camera out into the world and blocks the player's own look, move and crouch, so
    /// the stick drives the camera rather than the avatar. Shared by desktop's middle click and the
    /// panel toggle; safe to call while already flying.
    /// </summary>
    private void EnterFlyMode()
    {
        if (IsFlying) return;

        string className = nameof(BasisHandHeldCameraInteractable);

        // Flight and a position modifier both drive the same world pin, and the stack wins inside
        // MoveCameraFlying. Written straight onto the stack rather than through SetPositionModifier,
        // which would call back into ExitFlyMode from underneath this one.
        if (Modifiers.DrivesPosition)
        {
            Modifiers.positionModifier = BasisCameraPositionModifier.FreeFly;
        }

        LookLock.Add(className);
        MovementLock.Add(className);
        CrouchingLock.Add(className);

        DetachFromHand();

        if (HHC != null && HHC.captureCamera != null)
        {
            HHC.captureCamera.transform.GetPositionAndRotation(out Vector3 heldPosition, out Quaternion heldRotation);
            SeedPose(heldPosition, heldRotation);
        }

        if (BasisDeviceManagement.IsUserInDesktop())
        {
            pauseMove = true;
            flyCamera?.Enable();
            return;
        }

        isVRFlying = true;

        // Seed the aim from where the camera already points, so arming it does not snap.
        SeedOperatorAimFromCurrentRotation();
    }

    /// <summary>
    /// Points the operator's pitch and yaw at wherever the camera is now, so whatever takes the
    /// rotation channel next continues from the live shot rather than from a stale aim.
    /// </summary>
    private void SeedOperatorAimFromCurrentRotation()
    {
        Vector3 euler = smoothedRotation.eulerAngles;
        currentPitch = targetPitch = NormalizeAngle(euler.x);
        currentYaw = targetYaw = NormalizeAngle(euler.y);
    }

    /// <summary>
    /// Hands the camera and the player's controls back. Safe to call when not flying.
    ///
    /// <para>VR returns the pin to the hand, which is where the prop was left rather than where the
    /// player is — "Teleport To Me" in the panel is the way back when it was dropped out of reach.
    /// Desktop leaves the pin in world space, as it always has: the camera stays put and the head
    /// constraint picks the body back up.</para>
    /// </summary>
    private void ExitFlyMode()
    {
        if (!IsFlying) return;

        string className = nameof(BasisHandHeldCameraInteractable);

        if (pauseMove)
        {
            pauseMove = false;
            flyCamera?.Disable();
        }

        if (isVRFlying)
        {
            isVRFlying = false;

            // Only flight's own detach is undone here. A camera the user put on an anchor stays on
            // it: they chose where it sits, and landing it back in their hand would undo that.
            if (PinSpace == CameraPinSpace.WorldSpace)
            {
                PinSpace = CameraPinSpace.HandHeld;
            }
        }

        if (!LookLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove LookLock");
        if (!MovementLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove MovementLock");
        if (!CrouchingLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove CrouchingLock");

        velocityMomentum = Vector3.zero;
        rotationMomentum = 0f;
    }

    /// <summary>
    /// True whenever the camera is not pinned to the hand — flying, following, or world/playspace
    /// pinned. This is the "the camera has left your hand" condition the follow marker keys off.
    /// </summary>
    public bool IsDetachedFromHand => PinSpace != CameraPinSpace.HandHeld;

    /// <summary>
    /// Takes the camera out of the hand without choosing an anchor for it.
    ///
    /// <para>A camera already on one keeps it: arming flight or fitting a modifier is not a reason
    /// to drop the vehicle somebody bolted the camera to.</para>
    /// </summary>
    private void DetachFromHand()
    {
        if (PinSpace == CameraPinSpace.HandHeld)
        {
            PinSpace = CameraPinSpace.WorldSpace;
        }
    }

    /// <summary>Who the strafe history was measured on. Only read alongside <see cref="lastFollowAnchorWasRemote"/>.</summary>
    private ushort lastFollowAnchorSubject;

    /// <summary>Whether <see cref="lastFollowAnchorSubject"/> names a remote, so net id 0 cannot alias the local player.</summary>
    private bool lastFollowAnchorWasRemote;

    /// <summary>Whether the last solve was framing a group, so switching modes also drops the history.</summary>
    private bool lastFollowAnchorWasGroup;

    /// <summary>
    /// Every intermediate the follow solve produces, so the debug gizmos can draw what the
    /// solve actually computed rather than re-deriving it (a re-derivation drifts silently the
    /// moment the solve changes). Only filled while <see cref="CaptureFollowGizmoSample"/> is on.
    /// </summary>
    internal struct FollowGizmoSample
    {
        public bool Valid;
        public bool Live;
        public Vector3 AnchorPos;
        public Vector3 GroundPos;
        public Vector3 LookPoint;
        public Quaternion Yaw;
        public float Scale;
        public Vector3 AuthoredOffset;
        public Vector3 AppliedOffset;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public float LateralSpeed;
        public float CloseIn;
        public bool Snapped;
    }

    /// <summary>Set by the gizmo visualiser while the follow layer is on; keeps the solve free otherwise.</summary>
    internal bool CaptureFollowGizmoSample;

    private FollowGizmoSample followGizmoSample;
    private int followGizmoSampleFrame = -1;

    /// <summary>
    /// Records the solve's own intermediates for the debug gizmos, so they draw what the solve
    /// actually computed rather than re-deriving it.
    /// </summary>
    private void CaptureModifierGizmoSample(in BasisCameraSubject subject, in BasisCameraPose pose)
    {
        if (!CaptureFollowGizmoSample || !subject.Valid)
        {
            return;
        }

        Vector3 authored = modifiers.positionModifier == BasisCameraPositionModifier.FrameSubject
            ? modifiers.framing.directionOffset
            : modifiers.follow.positionOffset;

        Vector3 applied = authored;
        if (modifiers.positionModifier == BasisCameraPositionModifier.FollowSubject)
        {
            float reference = BasisCameraModifierSolver.LateralTrackingReferenceSpeed * subject.Scale;
            float closeIn = modifiers.follow.lateralTracking *
                Mathf.Clamp01(Mathf.Abs(modifierState.SmoothedLateralSpeed) / Mathf.Max(1e-4f, reference));
            applied.x *= 1f - closeIn;
        }

        followGizmoSample = new FollowGizmoSample
        {
            Valid = true,
            Live = true,
            AnchorPos = subject.AnchorPos,
            GroundPos = subject.GroundPos,
            LookPoint = subject.LookPoint,
            Yaw = subject.Yaw,
            Scale = subject.Scale,
            AuthoredOffset = authored,
            AppliedOffset = applied,
            TargetPosition = pose.Position,
            TargetRotation = pose.Rotation,
            LateralSpeed = modifierState.SmoothedLateralSpeed,
            CloseIn = authored.x != 0f ? 1f - applied.x / authored.x : 0f,
            Snapped = false,
        };
        followGizmoSampleFrame = Time.frameCount;
    }

    /// <summary>
    /// Drops every modifier and pins the camera at an explicit world pose, holding it there so it
    /// stops chasing anything and can be picked up. The transform is set immediately as well as
    /// the smoothed pin targets, so there is no ease-in from wherever it was.
    /// </summary>
    public void PlaceWorldPinned(Vector3 position, Quaternion rotation)
    {
        InitializeModifiers();
        modifiers.positionModifier = BasisCameraPositionModifier.FreeFly;
        modifiers.rotationModifier = BasisCameraRotationModifier.FreeLook;
        ClearAnchorTarget();
        PinSpace = CameraPinSpace.WorldSpace;
        SeedPose(position, rotation);
        if (HHC != null && HHC.captureCamera != null)
        {
            HHC.captureCamera.transform.SetPositionAndRotation(position, rotation);
        }
    }

    /// <summary>Sets the capture field of view directly. (Follow distance is a dolly, not a lens zoom.)</summary>
    public void SetFieldOfView(float value)
    {
        if (HHC != null && HHC.captureCamera != null)
        {
            HHC.captureCamera.fieldOfView = value;
        }
    }

    /// <summary>
    /// Resolves who the camera follows into a world pose. A remote target that has left falls back
    /// to the local player, so a disconnect can never strand the camera aimed at empty space.
    /// </summary>
    private FollowSubject ResolveFollowSubject()
    {
        if (TryGetFollowTargetPlayer(out ushort netId))
        {
            if (Basis.Scripts.Networking.BasisNetworkPlayers.RemotePlayers.TryGetValue(netId, out var remote) &&
                remote != null && !remote.IsDestroyed)
            {
                if (TryResolveRemoteSubject(netId, remote, out FollowSubject remoteSubject))
                {
                    return remoteSubject;
                }
            }
            else
            {
                ClearFollowTargetPlayer();
            }
        }

        return ResolveLocalSubject();
    }

    private FollowSubject ResolveLocalSubject()
    {
        if (BasisLocalPlayer.Instance == null)
        {
            return default;
        }

        // Anchor to the player root, not AvatarTransform. The latter is the loaded avatar model
        // (BasisAvatarFactory assigns avatar.transform), so it carries every IK correction and
        // foot-plant as shake, its rotation is slammed to identity on teleport, and it is
        // replaced on every avatar swap. The root is what locomotion actually moves.
        BasisLocalPlayer.Instance.transform.GetPositionAndRotation(out Vector3 rootPos, out Quaternion anchorRot);
        Quaternion anchorYaw = FlattenToYaw(anchorRot);

        float scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

        // Height is measured from calibrated eye level, not the feet, so a zero offset films you
        // level with your eyeline on any avatar. GetTposeHeadHeight is already avatar-scaled, and
        // being calibration-derived it does not bob with crouching the way the live head does.
        Vector3 anchorPos = rootPos + Vector3.up * GetTposeHeadHeight();

        float hipsHeight = GetTposeHipsHeight();
        if (subjectSettings.anchorToBody && hipsHeight > 0f && BasisLocalBoneDriver.HipsControl != null)
        {
            // Centre of mass. The hips carry their own height, so lift by only the calibrated
            // eye-above-hips gap and a zero offset still sits on your eyeline. Vertical now
            // tracks crouching, which the playspace root cannot do.
            Vector3 hips = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.position;
            anchorPos = hips + Vector3.up * Mathf.Max(0f, GetTposeHeadHeight() - hipsHeight);
        }

        return new FollowSubject
        {
            Valid = true,
            AnchorPos = anchorPos,
            GroundPos = rootPos,
            Yaw = anchorYaw,
            LookPoint = anchorPos + Vector3.up * (subjectSettings.aimHeightOffset * scale),
            Scale = scale,
        };
    }

    /// <summary>
    /// The yaw-only rotation matching a full rotation's heading, continuous through vertical.
    ///
    /// <para><c>eulerAngles.y</c> is not: that decomposition clamps pitch to ±90°, so it jumps the
    /// yaw by 180° the moment its source tips past vertical — and so does a plain flat projection
    /// of the forward axis, which is what forward genuinely does there. A head reaches vertical
    /// every time someone looks near-straight up or down, and the flip threw the follow camera to
    /// the far side of its subject and straight back again. The top of the head lies flat exactly
    /// where forward stops carrying a heading and crosses the pole continuously, so take whichever
    /// of the two has more heading left in it.</para>
    /// </summary>
    private static Quaternion FlattenToYaw(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        Vector3 up = rotation * Vector3.up;

        Vector3 flat = new Vector3(forward.x, 0f, forward.z);
        // Pitched past vertical the head is upside down relative to its heading, so the flattened
        // up axis points backwards along it; the sign of forward's tilt is which side it is on.
        Vector3 fromUp = new Vector3(up.x, 0f, up.z) * (forward.y > 0f ? -1f : 1f);
        if (fromUp.sqrMagnitude > flat.sqrMagnitude)
        {
            flat = fromUp;
        }

        if (flat.sqrMagnitude < 1e-6f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    /// <summary>Nominal root-to-head height used only while a remote has no avatar root to read.</summary>
    private const float RemoteFallbackHeadHeight = 1.5f;

    /// <summary>Last body yaw read off a remote's avatar root, and which net id it came from.</summary>
    private Quaternion lastRemoteBodyYaw = Quaternion.identity;
    private ushort lastRemoteBodyYawSubject;

    /// <summary>
    /// Resolves a remote's world pose, or false while it has no transforms to read yet — still
    /// joining, or between avatars — so the caller holds the target instead of dropping it.
    /// </summary>
    /// <remarks>
    /// A remote has no player root object: <c>PlayerSelf</c> is only ever assigned for the local
    /// player. The root here is the avatar's animator transform, which the remote bone jobs write
    /// a full world pose to every frame. Its yaw is body facing; the mouth marker's is head
    /// facing, which would swing the camera every time the subject glanced sideways.
    /// </remarks>
    private bool TryResolveRemoteSubject(ushort netId, BasisRemotePlayer remote, out FollowSubject subject)
    {
        subject = default;

        Transform root = remote.AvatarAnimatorTransform;
        Transform headTransform = remote.MouthTransform;
        if (root == null)
        {
            // The mouth marker is created at the world origin the moment the player joins, and is
            // only ever moved once the bone job system has them registered against a loaded
            // avatar. Reading it before that flew the camera off to 0,0,0 and held it there —
            // so treat an unregistered remote as "nothing to read yet", which keeps the target
            // and films the local player for the frame instead.
            if (headTransform == null || !RemoteBoneJobSystem.TryGetSOutIndex(netId, out _))
            {
                return false;
            }
        }

        // Remotes are network-interpolated, so these are already smooth — no IK shake to dodge,
        // and no local T-pose to read, so height comes from the synced head transform when present.
        Vector3 rootPos;
        Quaternion yaw;
        if (root != null)
        {
            root.GetPositionAndRotation(out rootPos, out Quaternion rootRot);

            // The root's own rotation is NOT which way they are facing. The remote pipeline backs
            // that pose out of the hips' PARENT, so it carries whatever the exporter baked between
            // the animator and the skeleton, and the animator root is not the anatomical frame
            // either — a model authored facing −Z is a legal humanoid rig. Neither shows in the
            // avatar (hips are applied in world space and the mesh follows them), so the camera was
            // the only thing that could see it: on those rigs the shot set up 180° out and filmed
            // the subject from behind. The correction is a per-avatar constant measured at
            // calibration, and identity on a rig with hips straight off a +Z-facing root.
            yaw = FlattenToYaw(rootRot * (remote.RemoteAvatarDriver != null
                ? remote.RemoteAvatarDriver.DerivedRootToCharacterBasis
                : Quaternion.identity));
            lastRemoteBodyYaw = yaw;
            lastRemoteBodyYawSubject = netId;
        }
        else
        {
            headTransform.GetPositionAndRotation(out rootPos, out Quaternion headRot);
            rootPos -= Vector3.up * RemoteFallbackHeadHeight;

            // The mouth marker's rotation is head facing, so driving the shot from it swings the
            // camera around the subject on every glance. Hold the last yaw read off their avatar
            // root; the head only seeds a subject we have never had a body yaw for at all. It is
            // rig-dependent the same way the root is and cannot be corrected — reaching here means
            // there is no avatar to have measured — but it only ever covers the frames before one
            // has loaded.
            yaw = lastRemoteBodyYawSubject == netId ? lastRemoteBodyYaw : FlattenToYaw(headRot);
        }

        Vector3 lookPoint = headTransform != null
            ? headTransform.position
            : rootPos + Vector3.up * RemoteFallbackHeadHeight;

        // Remote avatar scale is not published as a ratio, but root-to-synced-head is the same
        // measurement the local path takes off the T-pose, so the offsets and the teleport
        // threshold size to a tall or a tiny avatar the way they already do for your own. Below
        // knee height it is not a body, so fall back rather than frame the shot from garbage.
        float headHeight = lookPoint.y - rootPos.y;
        float scale = headHeight > 0.2f ? Mathf.Min(headHeight / RemoteFallbackHeadHeight, 20f) : 1f;

        subject = new FollowSubject
        {
            Valid = true,
            IsRemote = true,
            AnchorPos = new Vector3(rootPos.x, lookPoint.y, rootPos.z),
            GroundPos = rootPos,
            Yaw = yaw,
            // Head height (the synced head transform), matching the local path's default.
            LookPoint = new Vector3(rootPos.x, lookPoint.y + subjectSettings.aimHeightOffset, rootPos.z),
            Scale = scale,
        };
        return true;
    }

    /// <summary>Focus point (world head-height of the follow subject) for the DoF auto-focus, or null.</summary>
    public bool TryGetFollowFocusPoint(out Vector3 point)
    {
        FollowSubject subject = ResolveFollowSubject();
        point = subject.LookPoint;
        return subject.Valid;
    }

    /// <summary>
    /// The follow solve's own intermediates for this frame. When the solve did not run — follow is
    /// off, or the camera is in hand — the subject is resolved fresh and the offset is the authored
    /// one with no lateral close-in (which needs frame history), flagged by <c>Live == false</c> so
    /// the drawing can present it as a preview of where follow *would* place the camera.
    /// </summary>
    internal bool TryGetFollowGizmoSample(out FollowGizmoSample sample)
    {
        if (followGizmoSampleFrame == Time.frameCount && followGizmoSample.Valid)
        {
            sample = followGizmoSample;
            return true;
        }

        FollowSubject subject = ResolveFollowSubject();
        if (!subject.Valid)
        {
            sample = default;
            return false;
        }

        Vector3 authoredOffset = Modifiers.positionModifier == BasisCameraPositionModifier.FrameSubject
            ? Modifiers.framing.directionOffset
            : Modifiers.follow.positionOffset;
        Vector3 targetPosition = subject.AnchorPos + subject.Yaw * (authoredOffset * subject.Scale);
        Vector3 toSubject = subject.LookPoint - targetPosition;
        Quaternion targetRotation = toSubject.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(toSubject, Vector3.up)
            : subject.Yaw;

        sample = new FollowGizmoSample
        {
            Valid = true,
            Live = false,
            AnchorPos = subject.AnchorPos,
            GroundPos = subject.GroundPos,
            LookPoint = subject.LookPoint,
            Yaw = subject.Yaw,
            Scale = subject.Scale,
            AuthoredOffset = authoredOffset,
            AppliedOffset = authoredOffset,
            TargetPosition = targetPosition,
            TargetRotation = targetRotation * Quaternion.Euler(Modifiers.lookAt.rotationOffset),
        };
        return true;
    }

    /// <summary>The pin constraint's current offset pose — the pose follow and fly both write into.</summary>
    internal void GetPinnedTargetPose(out Vector3 position, out Quaternion rotation)
    {
        position = smoothedPosition;
        rotation = smoothedRotation;
    }

    /// <summary>
    /// Height of the head above the avatar root in its T-pose, already scaled to the avatar
    /// as worn. Falls back to the measured avatar height before a T-pose snapshot exists.
    /// </summary>
    /// <summary>
    /// Height of the hips above the avatar root in its T-pose, already scaled to the avatar as
    /// worn. Returns 0 when no snapshot exists yet, which callers treat as "unavailable".
    /// </summary>
    private static float GetTposeHipsHeight()
    {
        if (BasisLocalAvatarDriver.HasTposeBoneSnapshot)
        {
            BasisLocalBoneControl hips = BasisLocalBoneDriver.HipsControl;
            if (hips != null && hips.TposeLocalScaled.position.y > 0.01f)
            {
                return hips.TposeLocalScaled.position.y;
            }
        }
        return 0f;
    }

    private static float GetTposeHeadHeight()
    {
        if (BasisLocalAvatarDriver.HasTposeBoneSnapshot)
        {
            BasisLocalBoneControl head = BasisLocalBoneDriver.HeadControl;
            if (head != null && head.TposeLocalScaled.position.y > 0.01f)
            {
                return head.TposeLocalScaled.position.y;
            }
        }
        return BasisHeightDriver.SelectedScaledAvatarHeight;
    }

    private float appliedCameraScale = -1f;

    /// <summary>
    /// User resize from the two-hand pickup gesture, held as a ratio of <see cref="GetBaseCameraScale"/>
    /// so it survives avatar height changes and the desktop fit, which both rewrite the base size.
    /// </summary>
    private float userScaleMultiplier = 1f;

    private float GetDesktopFitScale()
    {
        if (BasisDeviceManagement.IsCurrentModeVR() || !BasisLocalCameraDriver.HasInstance)
        {
            return float.PositiveInfinity;
        }

        Camera playerCamera = BasisLocalCameraDriver.CameraInstance;
        if (playerCamera == null || playerCamera.aspect <= 0f)
        {
            return float.PositiveInfinity;
        }

        if (transform is not RectTransform rootRect)
        {
            return float.PositiveInfinity;
        }

        Vector2 size = rootRect.rect.size;
        if (size.x <= 0f || size.y <= 0f)
        {
            return float.PositiveInfinity;
        }

        playerCamera.transform.GetPositionAndRotation(out Vector3 eyePos, out Quaternion eyeRot);
        float distance = Vector3.Dot(transform.position - eyePos, eyeRot * Vector3.forward);

        return ComputeDesktopFitScale(size, distance, playerCamera.fieldOfView, playerCamera.aspect, desktopScreenFitFraction);
    }

    /// <summary>
    /// Largest root scale at which a rect of <paramref name="rectSize"/> local units still fits
    /// inside the view frustum at <paramref name="distance"/>, times <paramref name="fitFraction"/>.
    /// Returns +infinity for degenerate input so callers treat it as "no constraint".
    /// <para>
    /// Pure and static so the geometry can be tested without a camera or a scene — the same
    /// reason <see cref="Basis.BasisUI.BasisMenuMover.GetEyeModeScaleFactor"/> was extracted.
    /// </para>
    /// </summary>
    public static float ComputeDesktopFitScale(Vector2 rectSize, float distance, float verticalFovDegrees, float aspect, float fitFraction)
    {
        if (rectSize.x <= 0f || rectSize.y <= 0f || aspect <= 0f || verticalFovDegrees <= 0f || verticalFovDegrees >= 180f)
        {
            return float.PositiveInfinity;
        }

        if (distance <= 0.01f)
        {
            return float.PositiveInfinity;
        }

        float frustumHeight = 2f * distance * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = frustumHeight * aspect;

        return Mathf.Min(frustumWidth / rectSize.x, frustumHeight / rectSize.y) * fitFraction;
    }

    /// <summary>
    /// Size the camera takes with no user resize applied: avatar-relative in VR, frustum-fit on desktop.
    /// </summary>
    private float GetBaseCameraScale()
    {
        float scale = cameraDefaultScale * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

        float desktopFit = GetDesktopFitScale();
        if (!float.IsPositiveInfinity(desktopFit))
        {
            scale = desktopFit;
        }

        return scale;
    }

    private void ApplyCameraScale()
    {
        float scale = GetBaseCameraScale() * userScaleMultiplier;

        if (Mathf.Approximately(scale, appliedCameraScale))
        {
            return;
        }

        appliedCameraScale = scale;
        transform.localScale = Vector3.one * scale;
    }

    /// <inheritdoc/>
    protected override float GestureScaleReference => GetBaseCameraScale();

    /// <inheritdoc/>
    protected override void ApplyGestureScaleStep(BasisTransform.Direction scaleDirection, float stepSize, float minScale, float maxScale)
    {
        float baseScale = GetBaseCameraScale();
        if (baseScale <= 0f)
        {
            return;
        }

        float step = scaleDirection == BasisTransform.Direction.Embiggen ? stepSize : -stepSize;
        float stepped = Mathf.Clamp(baseScale * userScaleMultiplier + step, minScale, maxScale);

        userScaleMultiplier = stepped / baseScale;
        ApplyCameraScale();
    }

    private bool isPlayerManuallyUnlocked = false;
    private bool desktopSetup = false;
    private CameraPinSpace previousPinState = CameraPinSpace.HandHeld;

    // Motion state
    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 targetVelocity = Vector3.zero;
    private Vector3 velocityMomentum = Vector3.zero;
    private float rotationMomentum = 0f;

    // Rotation state
    private float currentPitch = 0f;
    private float currentYaw = 0f;
    private float targetPitch = 0f;
    private float targetYaw = 0f;

    // Smoothed transform (for pin constraint offset)
    private Vector3 smoothedPosition = Vector3.zero;
    private Quaternion smoothedRotation = Quaternion.identity;

    /// <summary>
    /// The pose the operator's own controls hold, before any modifier has run.
    ///
    /// <para>Kept apart from <see cref="smoothedPosition"/> because the finished pose can carry an
    /// effect on top of it. Integrating the next frame from a pose that already has shake in it
    /// folds every frame's wander into the base, and the camera random-walks away from where the
    /// stick left it.</para>
    /// </summary>
    private Vector3 operatorPosition = Vector3.zero;
    private Quaternion operatorRotation = Quaternion.identity;

    /// <summary>Puts the operator's pose and the published pose at the same place.</summary>
    private void SeedPose(Vector3 position, Quaternion rotation)
    {
        operatorPosition = smoothedPosition = position;
        operatorRotation = smoothedRotation = rotation;
    }

    private bool pauseMove = false;

    // VR fly mode state
    private bool isVRFlying = false;

    /// <summary>Last frame's desktop middle-click state, so the toggle fires on the press edge only.</summary>
    private bool desktopMiddleClickPrev;

    private bool selfieRotationEnabled = false;

    /// <summary>
    /// Which frame the camera sits in — its anchor, as the panel calls it.
    ///
    /// <para>Everything but <see cref="HandHeld"/> holds a world pose that is carried along by the
    /// anchor's own movement, so the three of them differ only in what the anchor is: nothing, you,
    /// or something you picked.</para>
    /// </summary>
    public enum CameraPinSpace
    {
        /// <summary>Parented to the handheld object (local transform preserved).</summary>
        HandHeld,
        /// <summary>Rides the local player, so the camera keeps station with you as you move.</summary>
        PlaySpace,
        /// <summary>Free in world space, held wherever it was left.</summary>
        WorldSpace,
        /// <summary>Rides whatever <see cref="AnchorKind"/> names — a vehicle, a platform, a player.</summary>
        Attached,
    }

    /// <summary>What an <see cref="CameraPinSpace.Attached"/> anchor is riding.</summary>
    public enum CameraAnchorKind
    {
        /// <summary>Nothing picked yet. The camera holds still, exactly as a world anchor does.</summary>
        None,
        /// <summary>A player in the instance, local or remote.</summary>
        Player,
        /// <summary>Any transform in the scene: a vehicle, a moving platform, a prop.</summary>
        Object,
    }

    [Header("Anchor")]
    /// <summary>
    /// Whether a playspace anchor rides your body rather than your playspace origin.
    ///
    /// <para>Off, the camera keeps station with your locomotion and your teleports but holds still
    /// while you take physical steps — the steadier of the two, and the one a tripod wants. On, it
    /// rides your centre of mass, so walking the room carries it with you. Position only: hip twist
    /// would swing the shot around, so the frame's facing stays the playspace's.</para>
    /// </summary>
    public bool anchorFollowsBody = false;

    /// <summary>What an attached anchor is riding, if anything.</summary>
    public CameraAnchorKind AnchorKind { get; private set; } = CameraAnchorKind.None;

    /// <summary>Network id of the anchored player. Only meaningful while <see cref="AnchorKind"/> is Player.</summary>
    public ushort AnchorPlayerId { get; private set; }

    /// <summary>
    /// Whether <see cref="AnchorPlayerId"/> names a remote. Carried as a flag rather than a
    /// reserved id, because net id 0 is the first player to join and not a spare value.
    /// </summary>
    public bool AnchorPlayerIsRemote { get; private set; }

    private Transform anchorObject;

    /// <summary>What to call the anchored object in the panel and the readout.</summary>
    public string AnchorLabel { get; private set; } = string.Empty;

    /// <summary>
    /// True while an anchor is selected but cannot be read — a remote between avatars, or an object
    /// that has been destroyed. The camera holds where it is rather than snapping anywhere.
    /// </summary>
    public bool AnchorTargetLost { get; private set; }

    private Vector3 anchorReferencePosition;
    private Quaternion anchorReferenceRotation = Quaternion.identity;
    private bool hasAnchorReference;

    /// <summary>Whether the camera's pose is being carried by something that can move.</summary>
    public bool IsAnchorMoving => PinSpace == CameraPinSpace.PlaySpace ||
        (PinSpace == CameraPinSpace.Attached && AnchorKind != CameraAnchorKind.None);

    /// <summary>
    /// Puts the camera on an anchor. The pose is kept: switching anchor changes what carries the
    /// camera, never where it is, so a shot lined up stays lined up.
    /// </summary>
    public void SetAnchorSpace(CameraPinSpace space)
    {
        if (PinSpace == space) return;

        PinSpace = space;
        hasAnchorReference = false;
        AnchorTargetLost = false;
    }

    /// <summary>Anchors to a player, and switches the camera onto that anchor.</summary>
    public void SetAnchorToPlayer(ushort netId, bool isRemote)
    {
        AnchorKind = CameraAnchorKind.Player;
        AnchorPlayerId = netId;
        AnchorPlayerIsRemote = isRemote;
        anchorObject = null;
        AnchorLabel = string.Empty;
        hasAnchorReference = false;
        AnchorTargetLost = false;
        PinSpace = CameraPinSpace.Attached;
    }

    /// <summary>
    /// Anchors to a transform, and switches the camera onto that anchor. The reference is live and
    /// deliberately not persisted: a vehicle in one world is nothing in the next.
    /// </summary>
    public void SetAnchorToObject(Transform target, string label)
    {
        if (target == null)
        {
            ClearAnchorTarget();
            return;
        }

        AnchorKind = CameraAnchorKind.Object;
        anchorObject = target;
        AnchorLabel = string.IsNullOrEmpty(label) ? target.name : label;
        AnchorPlayerId = 0;
        AnchorPlayerIsRemote = false;
        hasAnchorReference = false;
        AnchorTargetLost = false;
        PinSpace = CameraPinSpace.Attached;
    }

    /// <summary>Drops the anchor target, leaving the camera holding wherever it now is.</summary>
    public void ClearAnchorTarget()
    {
        AnchorKind = CameraAnchorKind.None;
        anchorObject = null;
        AnchorLabel = string.Empty;
        AnchorPlayerId = 0;
        AnchorPlayerIsRemote = false;
        hasAnchorReference = false;
        AnchorTargetLost = false;

        if (PinSpace == CameraPinSpace.Attached)
        {
            PinSpace = CameraPinSpace.WorldSpace;
        }
    }

    /// <summary>
    /// The world frame the camera is riding this frame, or false when there is nothing to ride.
    ///
    /// <para>A world anchor answers false along with a lost one: both mean "nothing carries the
    /// camera", and the pose the operator already holds is the right answer for both. Pure — the
    /// gizmos draw from this every frame, and a read that repaired state would make what is drawn
    /// depend on whether anyone was looking.</para>
    /// </summary>
    public bool TryResolveAnchorPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        switch (PinSpace)
        {
            case CameraPinSpace.PlaySpace:
                if (!TryResolveLocalAnchor(anchorFollowsBody, out position, out rotation)) return false;

                // Desktop look yaw lives on the simulated eye rather than the player root. Treat it
                // as playspace yaw so a pinned camera turns with the player's desktop facing too.
                if (BasisDeviceManagement.IsUserInDesktop() && BasisDesktopEye.Instance != null)
                {
                    rotation *= Quaternion.AngleAxis(BasisDesktopEye.Instance.rotationYaw, Vector3.up);
                }

                return true;

            case CameraPinSpace.Attached:
                switch (AnchorKind)
                {
                    case CameraAnchorKind.Player:
                        return AnchorPlayerIsRemote
                            ? TryResolveRemoteAnchor(AnchorPlayerId, out position, out rotation)
                            : TryResolveLocalAnchor(anchorFollowsBody, out position, out rotation);

                    case CameraAnchorKind.Object:
                        if (anchorObject == null) return false;

                        anchorObject.GetPositionAndRotation(out position, out rotation);
                        return true;
                }

                return false;
        }

        return false;
    }

    /// <summary>
    /// The local player as an anchor frame: the playspace root, or the hips when the anchor is set
    /// to follow the body.
    /// </summary>
    private bool TryResolveLocalAnchor(bool followBody, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (BasisLocalPlayer.Instance == null) return false;

        // The player root, not the avatar model — see ResolveLocalSubject. The avatar carries every
        // IK correction as shake, is slammed to identity on teleport, and is replaced on a swap.
        BasisLocalPlayer.Instance.transform.GetPositionAndRotation(out position, out rotation);

        if (followBody && BasisLocalBoneDriver.HipsControl != null)
        {
            position = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.position;
            rotation = FlattenToYaw(rotation);
        }

        return true;
    }

    private readonly RaycastHit[] anchorPickHits = new RaycastHit[8];

    /// <summary>How far under the camera an anchor pick will look for something to stand on.</summary>
    public const float AnchorSurfaceProbeDistance = 6f;

    /// <summary>How far ahead of the camera an anchor pick will look for something to ride.</summary>
    public const float AnchorViewProbeDistance = 60f;

    /// <summary>
    /// Anchors to whatever the camera is standing over — a vehicle's deck, a moving platform, a lift.
    /// </summary>
    public bool TryAnchorToSurfaceBelow() => TryAnchorToPick(Vector3.down, AnchorSurfaceProbeDistance);

    /// <summary>Anchors to whatever the camera is pointed at, for a thing you cannot stand on.</summary>
    public bool TryAnchorToViewTarget()
    {
        Transform lens = HHC != null && HHC.captureCamera != null ? HHC.captureCamera.transform : transform;
        return TryAnchorToPick(lens.forward, AnchorViewProbeDistance);
    }

    /// <summary>
    /// Casts for something to ride and anchors to it.
    ///
    /// <para>The rigidbody is preferred over the collider that was hit: a vehicle is a hull of many
    /// colliders and only the body actually moves, so anchoring to the panel that happened to be
    /// under the camera would ride a transform that never goes anywhere. The camera's own hull is
    /// skipped, or every pick would anchor the camera to itself.</para>
    /// </summary>
    private bool TryAnchorToPick(Vector3 direction, float maxDistance)
    {
        if (HHC == null || HHC.captureCamera == null) return false;
        if (direction.sqrMagnitude < 1e-8f) return false;

        if (!occlusionMaskBuilt)
        {
            BuildOcclusionMask();
        }

        Vector3 origin = HHC.captureCamera.transform.position;
        int count = Physics.RaycastNonAlloc(
            origin, direction.normalized, anchorPickHits, maxDistance, occlusionMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestDistance = float.MaxValue;
        for (int Index = 0; Index < count; Index++)
        {
            RaycastHit hit = anchorPickHits[Index];
            Transform candidate = hit.rigidbody != null ? hit.rigidbody.transform : hit.collider.transform;
            if (candidate == null || candidate.IsChildOf(transform)) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = candidate;
            }
        }

        if (best == null) return false;

        SetAnchorToObject(best, best.name);
        return true;
    }

    /// <summary>A remote as an anchor frame, or false while they have no avatar root to read.</summary>
    private bool TryResolveRemoteAnchor(ushort netId, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!Basis.Scripts.Networking.BasisNetworkPlayers.RemotePlayers.TryGetValue(netId, out BasisRemotePlayer remote) || remote == null)
        {
            return false;
        }

        Transform root = remote.AvatarAnimatorTransform;
        if (root == null) return false;

        root.GetPositionAndRotation(out position, out rotation);
        return true;
    }

    /// <summary>
    /// Unity Start override: sets up locks, desktop state, captures camera references,
    /// subscribes to lifecycle events, and initializes constraints/fly controller.
    /// </summary>
    public new void Start()
    {
        base.Start();

        // force rigid ref null, pickup will use raw transform instead
        RigidRef = null;

        // disable base desktop “zoop”/rotate
        DesktopZoopSpeed = 0;
        DesktopRotateSpeed = 0;

        CanSelfSteal = false;

        // Desktop: lock player look/move for UI selection
        AcquireCursorLock();

        if (HHC.captureCamera == null)
        {
            HHC.captureCamera = gameObject.GetComponentInChildren<Camera>(true);
        }
        if (HHC.captureCamera == null)
        {
            BasisDebug.LogError($"Camera not found in children of {nameof(BasisHandHeldCamera)}, camera pinning will be broken");
        }
        else
        {
            HHC.captureCamera.transform.GetLocalPositionAndRotation(out cameraStartingLocalPos, out cameraStartingLocalRot);
        }

        OnInteractStartEvent.AddListener( OnInteractDesktopTweak );
        BasisDeviceManagement.OnBootModeChanged += OnBootModeChanged;

        BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;

        // scale camera to avatar size
        ApplyCameraScale();

        // run after player movement and after every device transform has been applied
        BasisLocalPlayer.AfterSimulateOnRender.AddAction(202, UpdateCamera);

        cameraPinConstraint = new BasisParentConstraint
        {
            sources = new BasisConstraintSourceData[] { new() { weight = 1f } },
            Enabled = false
        };

        flyCamera = new BasisFlyCamera();

        InitializeModifiers();
    }

    /// <summary>Assigns the UI instance so orientation changes can be reflected.</summary>
    public void SetCameraUI(BasisHandHeldCameraUI ui) => cameraUI = ui;

    /// <summary>Desktop tweak to disable pickup’s internal update loop while in desktop mode.</summary>
    private void OnInteractDesktopTweak(BasisInput _input)
    {
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            // don’t poll pickup input update
            RequiresUpdateLoop = false;
        }
    }

    /// <summary>Rescales the camera when the local player’s avatar height changes.</summary>
    private void OnHeightChanged(BasisHeightDriver.HeightModeChange HeightModeChange)
    {
        // Must match the factor used when the prop is first sized, or this handler (which fires on every
        // avatar swap and scale change) permanently overwrites it with a different one. ScaledToMatchValue
        // is the authored->target ratio and is 1.0 for any avatar that was not auto-scaled, which left a
        // naturally-small avatar holding a full adult-sized camera.
        ApplyCameraScale();
    }

    /// <summary>
    /// Per-frame camera update (runs after player movement). Handles desktop head binding,
    /// initializes desktop constraint, and always updates pinning & fly movement where applicable.
    /// </summary>
    private void UpdateCamera()
    {
        if (this == null || HHC == null)
        {
            return;
        }

        bool inDesktop = BasisDeviceManagement.IsUserInDesktop();
        CheckCameraOrientation();
        TickDollyTrack();

        if (inDesktop)
        {
            if (Inputs.desktopCenterEye.Source == null) return;

            flyCamera.DetectInput();

            Inputs.desktopCenterEye.Source.transform.GetPositionAndRotation(out Vector3 inPos, out Quaternion inRot);

            if (BasisLocalCameraDriver.HasInstance)
            {
                PollDesktopControl(Inputs.desktopCenterEye.Source);

                if (!desktopSetup)
                {
                    // Camera constrains itself to initial spawn position until destroyed.
                    InteractableEnabled = false;

                    // compute initial offset in eye space
                    transform.GetPositionAndRotation(out Vector3 startPos, out Quaternion startRot);
                    desktopSpawnOffset = Quaternion.Inverse(inRot) * (startPos - inPos);
                    desktopOffsetRotation = Quaternion.Inverse(inRot) * startRot;

                    InputConstraint.Enabled = true;

                    desktopSetup = true;
                }

                // Pull it closer to the player's camera.
                InputConstraint.SetOffsetPositionAndRotation(0, desktopSpawnOffset * desktopEyeDistanceScale, desktopOffsetRotation);
            }
            else
            {
                return;
            }

            // always constrain to head movement
            InputConstraint.UpdateSourcePositionAndRotation(0, inPos, inRot);
            if (InputConstraint.Evaluate(out Vector3 pos, out Quaternion rot))
            {
                transform.SetPositionAndRotation(pos, rot);
            }
        }

        // After the scale, which measures the desktop fit from the prop's own distance to the eye
        // and would follow the trail in and out otherwise; before the pin, which is what carries
        // the trail through to the capture camera.
        ApplyCameraScale();

        UpdateSmoothDrag();

        // Update pinning regardless of desktop/head-constraint logic
        PollCameraPin(Inputs.desktopCenterEye.Source);
    }
    public void SetSelfieRotationEnabled(bool enabled)
    {
        selfieRotationEnabled = enabled;
        handheldSmoothingInitialized = false;
    }
    /// <summary>Detects landscape vs portrait by camera roll and triggers UI orientation updates.</summary>
    private void CheckCameraOrientation()
    {
        if (Time.time < orientationCheckCooldown)
            return;

        if (HHC == null || HHC.captureCamera == null)
            return;

        Transform orientationSource =
            (HHC.HandHeld != null && HHC.HandHeld.uiOrientationReference != null)
                ? HHC.HandHeld.uiOrientationReference.transform
            : (HHC.HandHeld != null && HHC.HandHeld.cameraReference != null)
                ? HHC.HandHeld.cameraReference.transform
                : HHC.captureCamera.transform;

        Vector3 right = orientationSource.right;
        Vector3 up = orientationSource.up;

        BasisCameraOrientation newOrientation;

        if (Mathf.Abs(up.y) >= Mathf.Abs(right.y))
        {
            newOrientation = up.y >= 0f
                ? BasisCameraOrientation.Landscape
                : BasisCameraOrientation.LandscapeFlipped;
        }
        else
        {
            bool portraitCW = right.y >= 0f;

            newOrientation = portraitCW
                ? BasisCameraOrientation.PortraitCW
                : BasisCameraOrientation.PortraitCCW;
        }

        if (newOrientation != currentOrientation)
        {
            currentOrientation = newOrientation;
            orientationCheckCooldown = Time.time + 0.2f;
            HandleOrientationChanged(currentOrientation);
        }
    }

    /// <summary>Applies the new orientation to the UI and logs it.</summary>
    private void HandleOrientationChanged(BasisCameraOrientation newOrientation)
    {
        if (cameraUI != null)
        {
            cameraUI.SetUIOrientation(newOrientation);
        }
        BasisDebug.Log($"[Camera UI] Orientation changed to {newOrientation}");
    }

    /// <inheritdoc />
    public override bool IsInteractingWith(BasisInput input)
    {
        var found = Inputs.FindExcludeExtras(input);
        return found.HasValue && found.Value.GetState() == BasisInteractInputState.Interacting;
    }

    /// <inheritdoc />
    public override bool IsHoveredBy(BasisInput input)
    {
        var found = Inputs.FindExcludeExtras(input);
        return found.HasValue && found.Value.GetState() == BasisInteractInputState.Hovering;
    }

    /// <summary>
    /// Pins the capture camera to the hand, or holds it on its anchor and applies fly motion
    /// through an internal parent-constraint.
    ///
    /// <para>Every detached anchor runs the same path: the remembered poses are carried through
    /// whatever move the anchor made since last frame, the operator and the stack then move the
    /// camera in world space as they always have, and the constraint publishes that world pose from
    /// an identity source. A world pin is simply the case where the anchor never moves.</para>
    /// </summary>
    private void PollCameraPin(BasisInput DesktopEye)
    {
        if (HHC.captureCamera == null) return;

        if (PinSpace == CameraPinSpace.HandHeld)
        {
            if (previousPinState != CameraPinSpace.HandHeld)
            {
                cameraPinConstraint.Enabled = false;
                cameraPinConstraint.UpdateSourcePositionAndRotation(0, Vector3.zero, Quaternion.identity);
                cameraPinConstraint.SetOffsetPositionAndRotation(0, Vector3.zero, Quaternion.identity);
                handheldSmoothingInitialized = false;
            }

            hasAnchorReference = false;
            UpdateVRHandheldSmoothing();
            previousPinState = PinSpace;
            return;
        }

        // PinSpace is a public field and is written directly in places, so the reference the
        // transport measures from is dropped on any change of anchor rather than only in the
        // setters. Carrying a stale one would apply a whole anchor's worth of move in one frame.
        if (previousPinState != PinSpace)
        {
            hasAnchorReference = false;
        }

        if (previousPinState == CameraPinSpace.HandHeld)
        {
            cameraPinConstraint.Enabled = true;
            HHC.captureCamera.transform.GetPositionAndRotation(out Vector3 heldPos, out Quaternion heldRot);
            SeedPose(heldPos, heldRot);
        }

        TickAnchorTransport();

        MoveCameraFlying();

        cameraPinConstraint.UpdateSourcePositionAndRotation(0, Vector3.zero, Quaternion.identity);
        cameraPinConstraint.SetOffsetPositionAndRotation(0, smoothedPosition, smoothedRotation);

        if (cameraPinConstraint.Evaluate(out Vector3 pinPos, out Quaternion pinRot))
        {
            HHC.captureCamera.transform.SetPositionAndRotation(pinPos, pinRot);
        }

        previousPinState = PinSpace;
    }

    /// <summary>
    /// Resolves the anchor and carries every remembered pose through the move it made since last
    /// frame. A world pin resolves to a frame that never moves, so it falls through this doing
    /// nothing rather than needing a case of its own.
    /// </summary>
    private void TickAnchorTransport()
    {
        // A destroyed transform can never come back, so the target is dropped here rather than
        // held. A remote between avatars is the opposite case and keeps its binding, the way the
        // follow subject does.
        if (PinSpace == CameraPinSpace.Attached && AnchorKind == CameraAnchorKind.Object && anchorObject == null)
        {
            ClearAnchorTarget();
            AnchorTargetLost = true;
            return;
        }

        bool resolved = TryResolveAnchorPose(out Vector3 anchorPos, out Quaternion anchorRot);
        AnchorTargetLost = IsAnchorMoving && !resolved;

        if (!resolved)
        {
            hasAnchorReference = false;
            return;
        }

        if (hasAnchorReference &&
            BasisCameraAnchorMath.HasMoved(anchorReferencePosition, anchorReferenceRotation, anchorPos, anchorRot))
        {
            TransportRememberedPoses(anchorReferencePosition, anchorReferenceRotation, anchorPos, anchorRot);
        }

        anchorReferencePosition = anchorPos;
        anchorReferenceRotation = anchorRot;
        hasAnchorReference = true;
    }

    /// <summary>
    /// Moves every pose the camera holds between frames onto the anchor's new frame — the
    /// operator's pose, the published one, the fly rig's own heading and momentum, and the solver's
    /// memory. Anything left behind would pull the camera back off the anchor on the next step.
    /// </summary>
    private void TransportRememberedPoses(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
    {
        operatorPosition = BasisCameraAnchorMath.TransportPoint(operatorPosition, fromPosition, fromRotation, toPosition, toRotation);
        smoothedPosition = BasisCameraAnchorMath.TransportPoint(smoothedPosition, fromPosition, fromRotation, toPosition, toRotation);
        operatorRotation = BasisCameraAnchorMath.TransportRotation(operatorRotation, fromRotation, toRotation);
        smoothedRotation = BasisCameraAnchorMath.TransportRotation(smoothedRotation, fromRotation, toRotation);

        // The desktop fly rig rebuilds its rotation from these two floats every frame, so a
        // transported quaternion alone would be overwritten before it was ever published. Pitch is
        // not carried: the rig stores pitch and yaw about world axes with roll pinned to zero, so
        // an anchor that tips has no representation in it to be carried into.
        currentYaw = BasisCameraAnchorMath.TransportHeading(currentYaw, fromRotation, toRotation);
        targetYaw = BasisCameraAnchorMath.TransportHeading(targetYaw, fromRotation, toRotation);

        currentVelocity = BasisCameraAnchorMath.TransportDirection(currentVelocity, fromRotation, toRotation);
        targetVelocity = BasisCameraAnchorMath.TransportDirection(targetVelocity, fromRotation, toRotation);
        velocityMomentum = BasisCameraAnchorMath.TransportDirection(velocityMomentum, fromRotation, toRotation);

        modifierState.Transport(fromPosition, fromRotation, toPosition, toRotation);
    }

    /// <summary>
    /// Destroys self on boot mode changes to avoid managing inputs/state across modes.
    /// </summary>
    public void OnBootModeChanged(string mode)
    {
        Destroy(gameObject);
    }

    /// <summary>
    /// Handles fly-mode toggling and desktop player lock/unlock cues based on mouse input.
    /// Middle click enters/exits fly mode; right mouse temporarily unlocks player controls.
    /// </summary>
    private void PollDesktopControl(BasisInput DesktopEye)
    {
        if (DesktopEye == null) return;
        bool inDesktop = BasisDeviceManagement.IsUserInDesktop();
        if (!inDesktop) return;
        if (HHC != null && HHC.IsCameraHidden) return;

        string className = nameof(BasisHandHeldCameraInteractable);

        bool isMiddleClick = DesktopEye.CurrentInputState.Secondary2DAxisClick;
        bool isRightClickHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

        // Enter/exit fly mode on the press edge, so the button is free for WASD and mouse-look once
        // flight is running. The panel's switch drives the same pair, and either can undo the other.
        if (isMiddleClick && !desktopMiddleClickPrev)
        {
            if (IsFlying) ExitFlyMode();
            else EnterFlyMode();
        }
        desktopMiddleClickPrev = isMiddleClick;

        // Temporary manual unlock while holding RMB (when not flying)
        if (!pauseMove)
        {
            if (isRightClickHeld && !isPlayerManuallyUnlocked)
            {
                isPlayerManuallyUnlocked = true;
                UnlockPlayer(className);
            }
            else if (!isRightClickHeld && isPlayerManuallyUnlocked)
            {
                isPlayerManuallyUnlocked = false;
                if (inDesktop)
                    LockPlayer(className);
            }
        }
    }

    /// <summary>The stick that translates the flying camera: the left hand.</summary>
    private bool TryGetFlyMoveInput(out BasisInputState state)
        => TryGetFlyStick(BasisBoneTrackedRole.LeftHand, out state);

    /// <summary>The stick that yaws the flying camera and drives its elevation: the right hand.</summary>
    private bool TryGetFlyTurnInput(out BasisInputState state)
        => TryGetFlyStick(BasisBoneTrackedRole.RightHand, out state);

    /// <summary>
    /// Reads one hand's live input state, re-resolved every frame rather than latched —
    /// <see cref="BasisInputWrapper"/> is a struct, so a copy keeps reporting the state it was
    /// taken with. <see cref="BasisInputSources.TryGetByRole"/> answers true for either hand
    /// whether or not that slot has a device in it, so the null checks are the real test.
    /// </summary>
    private bool TryGetFlyStick(BasisBoneTrackedRole role, out BasisInputState state)
    {
        if (Inputs.TryGetByRole(role, out BasisInputWrapper wrapper) &&
            wrapper.Source != null && wrapper.BoneControl != null)
        {
            state = wrapper.Source.CurrentInputState;
            return state != null;
        }

        state = null;
        return false;
    }

    /// <summary>
    /// Retrieves the VR hand currently interacting with this object.
    /// </summary>
    private bool GetActiveVRInput(out BasisInputWrapper wrapper)
    {
        if (Inputs.leftHand.GetState() == BasisInteractInputState.Interacting)
        {
            wrapper = Inputs.leftHand;
            return true;
        }
        if (Inputs.rightHand.GetState() == BasisInteractInputState.Interacting)
        {
            wrapper = Inputs.rightHand;
            return true;
        }
        wrapper = default;
        return false;
    }

    /// <summary>Releases any player locks this interactable has taken.</summary>
    public void ReleasePlayerLocks()
    {
        string className = nameof(BasisHandHeldCameraInteractable);
        UnlockPlayer(className);
        isPlayerManuallyUnlocked = false;
    }

    /// <summary>
    /// Takes the camera's cursor-unlock request and, on desktop, its look/move locks, so the
    /// panel is clickable. Paired with <see cref="ReleaseCursorLock"/>.
    /// </summary>
    public void AcquireCursorLock()
    {
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            LockPlayer(nameof(BasisHandHeldCameraInteractable));
        }

        BasisCursorManagement.UnlockCursor(nameof(BasisHandHeldCamera), false);
    }

    /// <summary>
    /// Drops the camera's cursor-unlock request. If someone else still wants the cursor free —
    /// the main menu, most often — the cursor stays free and look has to stay blocked with it,
    /// which is what the camera's own look lock was doing until it was released. Without this
    /// the cursor is loose and mouse-look is live at the same time, so navigating the settings
    /// UI spins the player.
    /// </summary>
    public void ReleaseCursorLock()
    {
        BasisCursorManagement.LockCursor(nameof(BasisHandHeldCamera));

        if (BasisDeviceManagement.IsUserInDesktop() && Cursor.lockState != CursorLockMode.Locked)
        {
            LookLock.Add(nameof(BasisCursorManagement));
        }
    }

    /// <summary>Applies look/move locks to the player (desktop).</summary>
    private void LockPlayer(string className)
    {
        LookLock.Add(className);
        MovementLock.Add(className);
        // CrouchingLock.Add(className);
    }

    /// <summary>Removes look/move locks from the player (desktop).</summary>
    private void UnlockPlayer(string className)
    {
        LookLock.Remove(className);
        MovementLock.Remove(className);
        // CrouchingLock.Remove(className);
    }

    /// <summary>
    /// One step of camera motion. The operator's own controls run for whichever channels the
    /// modifier stack has not claimed, and the stack then solves over the top of them — so a
    /// hand-flown camera that keeps somebody framed is just a stack with the position slot empty,
    /// rather than a mode of its own.
    /// </summary>
    private void MoveCameraFlying()
    {
        float deltaTime = Time.deltaTime;

        // Selfie-stick grip: while the follow puck is held it is the master, so the camera snaps
        // to it. Releasing falls straight back through to the stack / fly below on the next frame.
        if (HHC != null && HHC.TryGetFollowPipPose(out Vector3 pipPos, out Quaternion pipRot))
        {
            SeedPose(pipPos, pipRot);

            // The stack keeps up with the puck rather than holding wherever it last solved, so
            // letting go eases on from where the camera actually is instead of sweeping back.
            ModifierState.Seed(pipPos, pipRot, GetCaptureFov());
            return;
        }

        BasisCameraModifierStack stack = Modifiers;
        bool drivesPosition = stack.DrivesPosition;
        bool drivesRotation = stack.DrivesRotation;

        if (!drivesPosition || !drivesRotation)
        {
            MoveCameraOperator(deltaTime, !drivesPosition, !drivesRotation);
        }

        if (stack.DrivesAnything)
        {
            MoveCameraModifiers(deltaTime);
        }
        else
        {
            smoothedPosition = operatorPosition;
            smoothedRotation = operatorRotation;
        }
    }

    /// <summary>
    /// The operator's own fly controls: input, acceleration, momentum and auto-levelling, for the
    /// channels the stack has left them.
    /// </summary>
    private void MoveCameraOperator(float deltaTime, bool applyPosition, bool applyRotation)
    {
        if (applyPosition)
        {
            if (HandleMovementInput(out Vector3 inputMovement, out float speedMultiplier))
            {
                UpdateMovement(inputMovement, speedMultiplier, deltaTime);
            }
            else if (useMomentum)
            {
                ApplyInertia(deltaTime);
            }
            else
            {
                currentVelocity = Vector3.zero;
                targetVelocity = Vector3.zero;
            }
        }

        if (applyRotation && HandleRotationInput(deltaTime, out Vector2 rotationDelta))
        {
            UpdateRotation(rotationDelta, deltaTime);
        }

        ApplySmoothedPosition(deltaTime, applyPosition, applyRotation);
    }

    /// <summary>
    /// Reads fly movement inputs and outputs a movement vector + speed multiplier. X and Z are the
    /// horizontal plane, normalized together so diagonals are not faster; Y is elevation.
    /// </summary>
    private bool HandleMovementInput(out Vector3 movement, out float speedMultiplier)
    {
        movement = Vector3.zero;
        speedMultiplier = 1f;

        if (isVRFlying)
        {
            bool hasMove = TryGetFlyMoveInput(out BasisInputState moveState);
            Vector2 stick = hasMove ? moveState.Primary2DAxisDeadZoned : Vector2.zero;

            Vector3 planar = new Vector3(stick.x, 0f, stick.y);
            if (planar.magnitude > 1f)
                planar.Normalize();

            float climb = TryGetFlyTurnInput(out BasisInputState turnState)
                ? turnState.Primary2DAxisDeadZoned.y
                : 0f;

            movement = new Vector3(planar.x, climb, planar.z);

            if (movement.magnitude < 0.01f)
                return false;

            speedMultiplier = hasMove && moveState.GripButton ? flyFastMultiplier : 1f;
            return true;
        }
        else
        {
            // Desktop path: read keyboard input
            var horizontalInput = flyCamera.horizontalMoveInput;
            var verticalInput = flyCamera.verticalMoveInput;
            var isFastMovement = flyCamera.isFastMovement;

            movement = new Vector3(horizontalInput.x, verticalInput, horizontalInput.y);

            if (movement.magnitude < 0.01f)
                return false;

            // prevent faster diagonal movement
            if (movement.magnitude > 1f)
                movement.Normalize();

            speedMultiplier = isFastMovement ? flyFastMultiplier : 1f;
            return true;
        }
    }

    /// <summary>Converts input to world velocity and applies acceleration and momentum.</summary>
    private void UpdateMovement(Vector3 inputMovement, float speedMultiplier, float deltaTime)
    {
        if (isVRFlying)
        {
            Vector3 planar = FlyYawFrame() * new Vector3(inputMovement.x, 0f, inputMovement.z);

            targetVelocity = ((planar * flySpeed) + (Vector3.up * (inputMovement.y * vrFlyElevationSpeed)))
                * speedMultiplier;
        }
        else
        {
            // Desktop: move relative to the camera's current orientation.
            targetVelocity = HHC.captureCamera.transform.rotation * inputMovement * flySpeed * speedMultiplier;
        }

        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, flyAcceleration * deltaTime);

        if (useMomentum)
        {
            velocityMomentum = Vector3.Lerp(velocityMomentum, currentVelocity * 0.1f, deltaTime * 2f);
        }
    }

    /// <summary>
    /// The level frame the VR fly stick pushes against: the camera's own yaw, off the pose this
    /// component publishes rather than the capture transform, which the pin only writes later.
    /// </summary>
    private Quaternion FlyYawFrame()
    {
        Vector3 forward = smoothedRotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = -(smoothedRotation * Vector3.up);
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    /// <summary>Applies exponential deceleration when no movement input is present.</summary>
    private void ApplyInertia(float deltaTime)
    {
        float decelerationFactor = Mathf.Pow(cinematicDamping, deltaTime * flyDeceleration);
        currentVelocity *= decelerationFactor;

        velocityMomentum = Vector3.Lerp(velocityMomentum, Vector3.zero, inertiaDamping * deltaTime);

        if (currentVelocity.magnitude < 0.01f)
        {
            currentVelocity = Vector3.zero;
            velocityMomentum = Vector3.zero;
        }
    }

    /// <summary>Reads fly rotation input and outputs the delta if significant.</summary>
    private bool HandleRotationInput(float deltaTime, out Vector2 rotationDelta)
    {
        rotationDelta = Vector2.zero;

        if (isVRFlying)
        {
            targetPitch = 0f;

            if (!TryGetFlyTurnInput(out BasisInputState turnState))
                return false;

            float yawInput = turnState.Primary2DAxisDeadZoned.x;
            if (Mathf.Abs(yawInput) < 0.01f)
                return false;

            rotationDelta = new Vector2(yawInput * vrFlyTurnSpeed * deltaTime, 0f);
            return true;
        }

        // Desktop: mouse delta
        var mouseInput = flyCamera.mouseInput;

        if (mouseInput.magnitude < 0.001f)
            return false;

        rotationDelta = mouseInput * mouseSensitivity;
        return true;
    }

    /// <summary>Updates target yaw/pitch from input and builds rotation momentum.</summary>
    private void UpdateRotation(Vector2 rotationDelta, float deltaTime)
    {
        targetYaw += rotationDelta.x;
        targetPitch -= rotationDelta.y;

        targetPitch = Mathf.Clamp(targetPitch, -90f, 90f);
        targetYaw = NormalizeAngle(targetYaw);

        float rotationSpeed = rotationDelta.magnitude;
        rotationMomentum = Mathf.Lerp(rotationMomentum, rotationSpeed * 0.1f, deltaTime * 5f);
    }

    /// <summary>
    /// Rebuilds <paramref name="target"/> with its roll eased toward level, keeping the pitch and
    /// yaw it was aimed at. Roll is measured on <paramref name="applied"/> — the rotation the camera
    /// was given last frame — so repeated frames converge on a flat horizon rather than shaving a
    /// fixed fraction off the incoming roll.
    /// </summary>
    private Quaternion LevelRoll(Quaternion target, Quaternion applied, float deltaTime)
    {
        Vector3 targetEuler = target.eulerAngles;
        float roll = Mathf.Lerp(NormalizeAngle(applied.eulerAngles.z), 0f, autoLevelStrength * deltaTime);

        return Quaternion.Euler(NormalizeAngle(targetEuler.x), NormalizeAngle(targetEuler.y), roll);
    }

    /// <summary>
    /// Integrates velocity into <see cref="operatorPosition"/> and applies smoothed rotation
    /// with momentum-influenced smoothing.
    /// </summary>
    private void ApplySmoothedPosition(float deltaTime, bool applyPosition = true, bool applyRotation = true)
    {
        if (applyPosition)
        {
            Vector3 finalVelocity = currentVelocity + (useMomentum ? velocityMomentum : Vector3.zero);
            operatorPosition += finalVelocity * deltaTime;
        }

        if (!applyRotation)
        {
            return;
        }

        float enhancedRotationSmoothness = rotationSmoothing + rotationMomentum;

        currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, enhancedRotationSmoothness * deltaTime);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, enhancedRotationSmoothness * deltaTime);

        Quaternion targetRotationQuat = Quaternion.Euler(currentPitch, currentYaw, 0f);
        operatorRotation = Quaternion.Slerp(operatorRotation, targetRotationQuat, rotationSmoothing * deltaTime);
    }

    /// <summary>Normalizes an angle to the range [-180, 180].</summary>
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>Clears all momentum/velocity state.</summary>
    public void ResetMomentum()
    {
        currentVelocity = Vector3.zero;
        targetVelocity = Vector3.zero;
        velocityMomentum = Vector3.zero;
        rotationMomentum = 0f;
    }
    /// <summary>
    /// Whether the body is being dragged right now. Desktop holds are a permanent head constraint,
    /// so they qualify from the moment that constraint is armed; a VR hold has to actually be in a
    /// hand. A camera that is flying or pinned away from the hand is driven by the modifier stack
    /// and is not being dragged at all.
    /// </summary>
    private bool ShouldSmoothDrag()
    {
        if (!useSmoothDrag || PinSpace != CameraPinSpace.HandHeld || IsFlying)
        {
            return false;
        }

        if (BasisDeviceManagement.IsUserInDesktop())
        {
            return desktopSetup;
        }

        return GetActiveVRInput(out _);
    }

    /// <summary>
    /// Trails the camera body behind the pose the hold has already written this frame, then leaves
    /// the trailed pose on the transform for <see cref="PollCameraPin"/> and everything parented to
    /// the prop to read. The hold writes from the hand rather than from the transform, so taking the
    /// transform as the target closes no loop.
    ///
    /// The leash is what keeps a fast drag from parting the camera from the hand: past it the body
    /// is pulled back onto the line to the hand, so the lag is a soft mount rather than a tether of
    /// unbounded length that would swing the camera through the player or the room.
    /// </summary>
    private void UpdateSmoothDrag()
    {
        transform.GetPositionAndRotation(out Vector3 targetPosition, out Quaternion targetRotation);

        if (!ShouldSmoothDrag())
        {
            smoothDragInitialized = false;
            return;
        }

        if (!smoothDragInitialized)
        {
            smoothDragPosition = targetPosition;
            smoothDragRotation = targetRotation;
            smoothDragInitialized = true;
            return;
        }

        SolveSmoothDrag(
            ref smoothDragPosition, ref smoothDragRotation, targetPosition, targetRotation,
            smoothDragPositionDamping, smoothDragRotationDamping,
            smoothDragMaxDistance * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale,
            Time.deltaTime);

        transform.SetPositionAndRotation(smoothDragPosition, smoothDragRotation);
    }

    /// <summary>
    /// One step of the trail: damp toward the pose the hand is at, then pull back onto the line to
    /// it if the gap has opened past <paramref name="leash"/>.
    /// </summary>
    private static void SolveSmoothDrag(
        ref Vector3 position, ref Quaternion rotation,
        Vector3 targetPosition, Quaternion targetRotation,
        float positionDamping, float rotationDamping, float leash, float deltaTime)
    {
        position = BasisCameraDamping.Approach(position, targetPosition, positionDamping, deltaTime);
        rotation = BasisCameraDamping.ApproachRotation(rotation, targetRotation, rotationDamping, deltaTime);

        Vector3 trail = position - targetPosition;
        float distance = trail.magnitude;
        if (distance > leash && distance > 0f)
        {
            position = targetPosition + trail * (leash / distance);
        }
    }

    private void UpdateVRHandheldSmoothing()
    {
        if (HHC == null || HHC.captureCamera == null)
            return;

        bool shouldSmooth =
            !BasisDeviceManagement.IsUserInDesktop() &&
            PinSpace == CameraPinSpace.HandHeld &&
            useVRHandheldSmoothing &&
            (!onlySmoothWhenStreamingToDesktop || HHC.enableRecordingView);

        Transform cameraTransform = HHC.captureCamera.transform;
        Transform cameraParent = cameraTransform.parent;

        if (cameraParent == null)
            return;

        Vector3 targetWorldPos = cameraParent.TransformPoint(cameraStartingLocalPos);

        Quaternion localTargetRot = cameraStartingLocalRot;

        if (selfieRotationEnabled)
        {
            localTargetRot *= Quaternion.Euler(0f, 180f, 0f);
        }

        Quaternion targetWorldRot = cameraParent.rotation * localTargetRot;

        if (useAutoLeveling)
        {
            targetWorldRot = LevelRoll(targetWorldRot, cameraTransform.rotation, Time.deltaTime);
        }

        if (!shouldSmooth)
        {
            handheldSmoothingInitialized = false;
            cameraTransform.SetPositionAndRotation(targetWorldPos, targetWorldRot);
            return;
        }

        if (!handheldSmoothingInitialized)
        {
            smoothedHandheldWorldPos = targetWorldPos;
            smoothedHandheldWorldRot = targetWorldRot;
            handheldSmoothingInitialized = true;
        }

        float dt = Time.deltaTime;
        smoothedHandheldWorldPos = Vector3.Lerp(
            smoothedHandheldWorldPos,
            targetWorldPos,
            vrHandheldPositionSmoothing * dt
        );
        smoothedHandheldWorldRot = Quaternion.Slerp(
            smoothedHandheldWorldRot,
            targetWorldRot,
            vrHandheldRotationSmoothing * dt
        );

        cameraTransform.SetPositionAndRotation(smoothedHandheldWorldPos, smoothedHandheldWorldRot);
    }
    /// <summary>
    /// Unsubscribes events, releases locks, destroys highlight artifacts, shuts down fly camera,
    /// and then calls base destroy.
    /// </summary>
    public override void OnDestroy()
    {
        BasisDeviceManagement.OnBootModeChanged -= OnBootModeChanged;
        OnInteractStartEvent.RemoveListener(OnInteractDesktopTweak);
        BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;

        BasisLocalPlayer.AfterSimulateOnRender.RemoveAction(202, UpdateCamera);

        if (pauseMove || isVRFlying)
        {
            LookLock.Remove(nameof(BasisHandHeldCameraInteractable));
            MovementLock.Remove(nameof(BasisHandHeldCameraInteractable));
            CrouchingLock.Remove(nameof(BasisHandHeldCameraInteractable));
            isVRFlying = false;
        }
        if (HighlightClone != null)
        {
            Destroy(HighlightClone);
        }

        if (flyCamera != null)
        {
            flyCamera.OnDestroy();
        }

        DisposeModifiers();

        ReleaseCursorLock();
        base.OnDestroy();
    }

#if UNITY_INCLUDE_TESTS
    /// <summary>Yaw flattening, so the behaviour either side of vertical can be asserted.</summary>
    public static Quaternion FlattenToYawForTest(Quaternion rotation) => FlattenToYaw(rotation);

    /// <summary>
    /// One trail step, so the lag, the settle and the leash can be asserted without a hand, a
    /// device mode or a frame.
    /// </summary>
    public static void SolveSmoothDragForTest(
        ref Vector3 position, ref Quaternion rotation,
        Vector3 targetPosition, Quaternion targetRotation,
        float positionDamping, float rotationDamping, float leash, float deltaTime)
        => SolveSmoothDrag(ref position, ref rotation, targetPosition, targetRotation,
            positionDamping, rotationDamping, leash, deltaTime);

    /// <summary>
    /// Resolves a remote exactly as the follow solve does, surfacing the pieces of the private
    /// subject a test can pin: whether it resolved at all, the body yaw the shot is framed from,
    /// and the avatar-relative scale the authored offsets are multiplied by.
    /// </summary>
    public bool TryResolveRemoteSubjectForTest(ushort netId, BasisRemotePlayer remote,
        out Quaternion yaw, out float scale, out Vector3 anchor)
    {
        bool resolved = TryResolveRemoteSubject(netId, remote, out FollowSubject subject);
        yaw = subject.Yaw;
        scale = subject.Scale;
        anchor = subject.AnchorPos;
        return resolved;
    }
#endif
}
