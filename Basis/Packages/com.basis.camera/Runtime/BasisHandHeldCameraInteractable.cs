using Basis.Scripts.Common;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.InputSystem;
using Basis.Scripts.Device_Management;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.TransformBinders.BoneControl;

/// <summary>
/// Interactable handheld/fly camera controller:
/// - Pins the capture camera to handheld, playspace, or world space
/// - Provides a desktop “fly” mode with smoothed movement/rotation, momentum, and auto-leveling
/// - Locks/unlocks player controls while interacting
/// </summary>
public abstract class BasisHandHeldCameraInteractable : BasisPickupInteractable
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

    [Header("Cinematic Controls")]
    /// <summary>Whether to use momentum/inertia for movement.</summary>
    public bool useMomentum = true;

    /// <summary>How quickly momentum falls off.</summary>
    [Range(2f, 12f)]
    public float inertiaDamping = 5f;

    /// <summary>Automatically level pitch toward eye-height.</summary>
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

    [Header("Auto Follow")]
    [Tooltip("Flies the camera along with the player instead of holding it. Works in desktop and VR.")]
    public bool autoFollowEnabled = false;

    [Tooltip("Offset from the player in yaw-relative space, in metres at default avatar scale: X right, Y up from calibrated eye level, Z forward.")]
    public Vector3 autoFollowPositionOffset = new Vector3(0.5f, 0f, 1.4f);

    [Tooltip("Extra rotation applied after aiming, in degrees.")]
    public Vector3 autoFollowRotationOffset = Vector3.zero;

    [Tooltip("Follow your body's centre of mass (hips) so room-scale movement keeps you in frame. Off anchors to the playspace origin, which is steadier but ignores physical walking.")]
    public bool autoFollowPlayspace = true;

    [Tooltip("Aim the camera at the player rather than facing the player's forward.")]
    public bool autoFollowLookAtPlayer = true;

    [Tooltip("Shifts the aim point up or down from the midpoint between feet and head, in metres at default avatar scale.")]
    public float autoFollowLookAtHeightOffset = 0f;

    public float autoFollowPositionSmoothing = 4f;
    public float autoFollowRotationSmoothing = 6f;

    [Tooltip("Snap instead of easing when the target is further away than this, in metres at default avatar scale.")]
    public float autoFollowTeleportDistance = 10f;

    public bool IsAutoFollowing => autoFollowEnabled;

    /// <summary>True while the desktop fly controls have the camera, so it is off in the world somewhere.</summary>
    public bool IsFlying => pauseMove;

    public void SetAutoFollowEnabled(bool enabled)
    {
        if (autoFollowEnabled == enabled)
        {
            return;
        }

        autoFollowEnabled = enabled;

        if (enabled)
        {
            if (HHC != null && HHC.captureCamera != null)
            {
                HHC.captureCamera.transform.GetPositionAndRotation(out smoothedPosition, out smoothedRotation);
            }
            PinSpace = CameraPinSpace.WorldSpace;
        }
        else if (PinSpace == CameraPinSpace.WorldSpace)
        {
            PinSpace = CameraPinSpace.HandHeld;
        }
    }

    private void MoveCameraAutoFollow(float deltaTime)
    {
        if (BasisLocalPlayer.Instance == null)
        {
            return;
        }

        // Anchor to the player root, not AvatarTransform. The latter is the loaded avatar model
        // (BasisAvatarFactory assigns avatar.transform), so it carries every IK correction and
        // foot-plant as shake, its rotation is slammed to identity on teleport, and it is
        // replaced on every avatar swap. The root is what locomotion actually moves.
        BasisLocalPlayer.Instance.transform.GetPositionAndRotation(out Vector3 rootPos, out Quaternion anchorRot);
        Quaternion anchorYaw = Quaternion.Euler(0f, anchorRot.eulerAngles.y, 0f);

        float scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

        // Height is measured from calibrated eye level, not the feet, so a zero offset films you
        // level with your eyeline on any avatar. GetTposeHeadHeight is already avatar-scaled, and
        // being calibration-derived it does not bob with crouching the way the live head does.
        Vector3 anchorPos = rootPos;
        Vector3 eyeLevel = Vector3.up * GetTposeHeadHeight();

        float hipsHeight = GetTposeHipsHeight();
        if (autoFollowPlayspace && hipsHeight > 0f && BasisLocalBoneDriver.HipsControl != null)
        {
            // Centre of mass. The hips carry their own height, so lift by only the calibrated
            // eye-above-hips gap and a zero offset still sits on your eyeline. Vertical now
            // tracks crouching, which the playspace root cannot do. Falls back to the root
            // above when there is no T-pose snapshot to measure the gap from.
            anchorPos = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.position;
            eyeLevel = Vector3.up * Mathf.Max(0f, GetTposeHeadHeight() - hipsHeight);
        }

        Vector3 targetPosition = anchorPos + eyeLevel + anchorYaw * (autoFollowPositionOffset * scale);

        // The aim helper measures up from the feet, so hand it the body's ground position.
        Vector3 lookTarget = GetAutoFollowLookTarget(new Vector3(anchorPos.x, rootPos.y, anchorPos.z), scale);
        Vector3 toPlayer = lookTarget - targetPosition;

        Quaternion targetRotation = autoFollowLookAtPlayer && toPlayer.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(toPlayer, Vector3.up)
            : anchorYaw;
        targetRotation *= Quaternion.Euler(autoFollowRotationOffset);

        if (Vector3.Distance(smoothedPosition, targetPosition) > autoFollowTeleportDistance * scale)
        {
            smoothedPosition = targetPosition;
            smoothedRotation = targetRotation;
            return;
        }

        smoothedPosition = Vector3.Lerp(smoothedPosition, targetPosition, 1f - Mathf.Exp(-autoFollowPositionSmoothing * deltaTime));
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, 1f - Mathf.Exp(-autoFollowRotationSmoothing * deltaTime));
    }

    /// <summary>
    /// The point auto-follow aims at: halfway between the player's feet and head, so the shot
    /// frames the body instead of the face, shifted by
    /// <see cref="autoFollowLookAtHeightOffset"/>.
    /// <para>
    /// The height comes from the avatar's T-pose, not from the live head. Live head position
    /// moves with every crouch, lean and head-bob, and the camera would swing to chase it —
    /// so only the root translates the aim point, and its height off the ground is fixed.
    /// </para>
    /// </summary>
    private Vector3 GetAutoFollowLookTarget(Vector3 rootPosition, float scale)
    {
        float midHeight = GetTposeHeadHeight() * 0.5f;
        return rootPosition + Vector3.up * (midHeight + autoFollowLookAtHeightOffset * scale);
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
        if (distance <= 0.01f)
        {
            return float.PositiveInfinity;
        }

        float frustumHeight = 2f * distance * Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = frustumHeight * playerCamera.aspect;

        return Mathf.Min(frustumWidth / size.x, frustumHeight / size.y) * desktopScreenFitFraction;
    }

    private void ApplyCameraScale()
    {
        float scale = cameraDefaultScale * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

        float desktopFit = GetDesktopFitScale();
        if (!float.IsPositiveInfinity(desktopFit))
        {
            scale = desktopFit;
        }

        if (Mathf.Approximately(scale, appliedCameraScale))
        {
            return;
        }

        appliedCameraScale = scale;
        transform.localScale = Vector3.one * scale;
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

    private bool pauseMove = false;

    // VR fly mode state
    private bool isVRFlying = false;
    private bool vrThumbstickClickPrev = false;
    private Quaternion vrControllerRotation = Quaternion.identity;

    private bool selfieRotationEnabled = false;
    /// <summary>Where to pin the camera transform.</summary>
    public enum CameraPinSpace
    {
        /// <summary>Parented to the handheld object (local transform preserved).</summary>
        HandHeld,
        /// <summary>Pinned relative to the local player’s avatar transform.</summary>
        PlaySpace,
        /// <summary>Free in world space with no parent.</summary>
        WorldSpace,
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
        string className = nameof(BasisHandHeldCameraInteractable);
        bool inDesktop = BasisDeviceManagement.IsUserInDesktop();
        if (inDesktop)
            LockPlayer(className);

        BasisCursorManagement.UnlockCursor(nameof(BasisHandHeldCamera),false);

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

        // run after player movement
        BasisLocalPlayer.AfterSimulateOnLate.AddAction(202, UpdateCamera);

        cameraPinConstraint = new BasisParentConstraint
        {
            sources = new BasisConstraintSourceData[] { new() { weight = 1f } },
            Enabled = false
        };

        flyCamera = new BasisFlyCamera();
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
        ApplyCameraScale();

        if (inDesktop)
        {
            if (Inputs.desktopCenterEye.Source == null) return;

            flyCamera.DetectInput();

            BasisCalibratedCoords Coords = Inputs.desktopCenterEye.BoneControl.OutgoingWorldData;
            Vector3 inPos = Coords.position;
            Quaternion inRot = Coords.rotation;

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
        else
        {
            // VR mode: handle fly mode toggle and controller input
            PollVRControl();
        }

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
    /// Pins the capture camera to handheld/playspace/world and applies fly motion offsets
    /// through an internal parent-constraint.
    /// </summary>
    private void PollCameraPin(BasisInput DesktopEye)
    {
        if (HHC.captureCamera == null) return;

        switch (PinSpace)
        {
            case CameraPinSpace.HandHeld:
    if (previousPinState != CameraPinSpace.HandHeld)
    {
        cameraPinConstraint.Enabled = false;
        cameraPinConstraint.UpdateSourcePositionAndRotation(0, Vector3.zero, Quaternion.identity);
        cameraPinConstraint.SetOffsetPositionAndRotation(0, Vector3.zero, Quaternion.identity);
        handheldSmoothingInitialized = false;
    }

    UpdateVRHandheldSmoothing();
    previousPinState = PinSpace;
    return;

            case CameraPinSpace.PlaySpace:
                // Player root, not the avatar model — see MoveCameraAutoFollow. Pinning to the
                // avatar fed every IK correction into the constraint as shake, and the playspace
                // is the root by definition.
                BasisLocalPlayer.Instance.transform.GetPositionAndRotation(out Vector3 pinParentPos, out Quaternion pinParentRot);
                cameraPinConstraint.UpdateSourcePositionAndRotation(0, pinParentPos, pinParentRot);

                MoveCameraFlying();
                cameraPinConstraint.SetOffsetPositionAndRotation(0, smoothedPosition, smoothedRotation);

                if (previousPinState != CameraPinSpace.PlaySpace)
                {
                    cameraPinConstraint.Enabled = true;

                    HHC.captureCamera.transform.GetPositionAndRotation(out Vector3 camPos, out Quaternion camRot);
                    var offsetPos = Quaternion.Inverse(pinParentRot) * (camPos - pinParentPos);
                    var offsetRot = Quaternion.Inverse(pinParentRot) * camRot;
                    cameraPinConstraint.SetOffsetPositionAndRotation(0, offsetPos, offsetRot);
                }
                break;

            case CameraPinSpace.WorldSpace:
                cameraPinConstraint.UpdateSourcePositionAndRotation(0, Vector3.zero, Quaternion.identity);

                MoveCameraFlying();
                cameraPinConstraint.SetOffsetPositionAndRotation(0, smoothedPosition, smoothedRotation);

                if (previousPinState != CameraPinSpace.WorldSpace)
                {
                    cameraPinConstraint.Enabled = true;
                    HHC.captureCamera.transform.GetPositionAndRotation(out Vector3 camPos, out Quaternion camRot);
                    cameraPinConstraint.SetOffsetPositionAndRotation(0, camPos, camRot);
                }
                break;
        }

        if (cameraPinConstraint.Evaluate(out Vector3 pinPos, out Quaternion pinRot))
        {
            HHC.captureCamera.transform.SetPositionAndRotation(pinPos, pinRot);
        }

        previousPinState = PinSpace;
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

        string className = nameof(BasisHandHeldCameraInteractable);

        bool isMiddleClick = DesktopEye.CurrentInputState.Secondary2DAxisClick;
        bool isRightClickHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

        // Enter/exit fly mode
        if (isMiddleClick && !pauseMove)
        {
            pauseMove = true;
            autoFollowEnabled = false;
            LookLock.Add(className);
            MovementLock.Add(className);
            CrouchingLock.Add(className);

            PinSpace = CameraPinSpace.WorldSpace;
            flyCamera.Enable();

            HHC.captureCamera.transform.GetPositionAndRotation(out smoothedPosition, out smoothedRotation);
        }
        else if (!isMiddleClick && pauseMove)
        {
            pauseMove = false;
            if (!LookLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove LookLock");
            if (!MovementLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove MovementLock");
            if (!CrouchingLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove CrouchingLock");

            flyCamera.Disable();
            velocityMomentum = Vector3.zero;
            rotationMomentum = 0f;
        }

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

    /// <summary>
    /// VR fly-mode control: toggles fly mode on thumbstick click (edge-detected)
    /// and captures controller rotation each frame for camera aiming.
    /// </summary>
    private void PollVRControl()
    {
        if (GetActiveVRInput(out BasisInputWrapper vrInput))
        {
            BasisInputState inputState = vrInput.Source.CurrentInputState;
            string className = nameof(BasisHandHeldCameraInteractable);

            // Toggle fly mode on thumbstick click (edge detection)
            bool thumbstickClick = inputState.Primary2DAxisClick;
            if (thumbstickClick && !vrThumbstickClickPrev)
            {
                if (isVRFlying)
                {
                    // Exit VR fly mode — return camera to hand
                    isVRFlying = false;
                    if (!LookLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove LookLock");
                    if (!MovementLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove MovementLock");
                    if (!CrouchingLock.Remove(className)) BasisDebug.LogWarning($"{className} couldn't remove CrouchingLock");

                    PinSpace = CameraPinSpace.HandHeld;
                    velocityMomentum = Vector3.zero;
                    rotationMomentum = 0f;
                }
                else
                {
                    // Enter VR fly mode
                    isVRFlying = true;
                    autoFollowEnabled = false;
                    LookLock.Add(className);
                    MovementLock.Add(className);
                    CrouchingLock.Add(className);

                    PinSpace = CameraPinSpace.WorldSpace;

                    HHC.captureCamera.transform.GetPositionAndRotation(out smoothedPosition, out smoothedRotation);

                    // Initialize rotation tracking from current camera orientation
                    Vector3 euler = smoothedRotation.eulerAngles;
                    currentPitch = targetPitch = NormalizeAngle(euler.x);
                    currentYaw = targetYaw = NormalizeAngle(euler.y);
                }
            }
            vrThumbstickClickPrev = thumbstickClick;

            if (isVRFlying)
            {
                // Store VR controller rotation for movement direction and camera aim
                vrControllerRotation = vrInput.BoneControl.OutgoingWorldData.rotation;
            }
        }
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
    /// Fly camera step: handles input, acceleration/deceleration, momentum, auto-leveling,
    /// and computes smoothed position/rotation for the pin constraint offset.
    /// </summary>
    private void MoveCameraFlying()
    {
        float deltaTime = Time.deltaTime;

        if (autoFollowEnabled)
        {
            MoveCameraAutoFollow(deltaTime);
            return;
        }

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

        if (HandleRotationInput(out Vector2 rotationDelta))
        {
            UpdateRotation(rotationDelta, deltaTime);
        }

        if (useAutoLeveling)
        {
            ApplyAutoLeveling(deltaTime);
        }

        ApplySmoothedPosition(deltaTime);
    }

    /// <summary>Reads fly movement inputs and outputs a normalized movement vector + speed multiplier.</summary>
    private bool HandleMovementInput(out Vector3 movement, out float speedMultiplier)
    {
        movement = Vector3.zero;
        speedMultiplier = 1f;

        if (isVRFlying)
        {
            // VR path: read thumbstick from the interacting controller
            if (!GetActiveVRInput(out BasisInputWrapper vrInput))
                return false;

            BasisInputState state = vrInput.Source.CurrentInputState;
            Vector2 thumbstick = state.Primary2DAxisDeadZoned;

            // Thumbstick X = strafe, thumbstick Y = forward/back
            // Vertical movement comes from controller pitch (point up + push forward = fly up)
            movement = new Vector3(thumbstick.x, 0f, thumbstick.y);

            if (movement.magnitude < 0.01f)
                return false;

            if (movement.magnitude > 1f)
                movement.Normalize();

            // Grip = speed boost
            speedMultiplier = state.GripButton ? flyFastMultiplier : 1f;
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
        // In VR, move relative to controller orientation (point where you want to fly).
        // In desktop, move relative to the camera's current orientation.
        Quaternion orientationRef = isVRFlying ? vrControllerRotation : HHC.captureCamera.transform.rotation;
        Vector3 worldMovement = orientationRef * inputMovement;
        targetVelocity = worldMovement * flySpeed * speedMultiplier;
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, flyAcceleration * deltaTime);

        if (useMomentum)
        {
            velocityMomentum = Vector3.Lerp(velocityMomentum, currentVelocity * 0.1f, deltaTime * 2f);
        }
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

    /// <summary>Reads fly rotation input (mouse delta) and outputs the delta if significant.</summary>
    private bool HandleRotationInput(out Vector2 rotationDelta)
    {
        rotationDelta = Vector2.zero;

        if (isVRFlying)
        {
            // VR: drive target rotation directly from controller orientation.
            // The actual rotation is applied in ApplySmoothedPosition (1:1 mapping).
            Vector3 euler = vrControllerRotation.eulerAngles;
            targetPitch = NormalizeAngle(euler.x);
            targetYaw = NormalizeAngle(euler.y);
            return false;
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

    /// <summary>Gradually levels pitch toward zero (eye level) when enabled.</summary>
    private void ApplyAutoLeveling(float deltaTime)
    {
        float targetLevelPitch = 0f;
        float pitchDifference = targetPitch - targetLevelPitch;

        if (Mathf.Abs(pitchDifference) > 5f)
        {
            float levelingForce = -pitchDifference * autoLevelStrength * deltaTime;
            targetPitch += levelingForce;
            targetPitch = Mathf.Clamp(targetPitch, -89.8f, 89.9f);
        }
    }

    /// <summary>
    /// Integrates velocity into <see cref="smoothedPosition"/> and applies smoothed rotation
    /// with momentum-influenced smoothing.
    /// </summary>
    private void ApplySmoothedPosition(float deltaTime)
    {
        Vector3 finalVelocity = currentVelocity + (useMomentum ? velocityMomentum : Vector3.zero);
        smoothedPosition += finalVelocity * deltaTime;

        if (isVRFlying)
        {
            // VR: 1:1 controller-to-camera rotation for responsive aiming
            currentPitch = targetPitch;
            currentYaw = targetYaw;
            smoothedRotation = vrControllerRotation;
        }
        else
        {
            // Desktop: smoothed rotation with momentum
            float enhancedRotationSmoothness = rotationSmoothing + rotationMomentum;

            currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, enhancedRotationSmoothness * deltaTime);
            currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, enhancedRotationSmoothness * deltaTime);

            Quaternion targetRotationQuat = Quaternion.Euler(currentPitch, currentYaw, 0f);
            smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotationQuat, rotationSmoothing * deltaTime);
        }
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
            Quaternion prevRot = cameraTransform.rotation;
            Vector3 prevEuler = prevRot.eulerAngles;

            float pitch = NormalizeAngle(prevEuler.x);
            float yaw = NormalizeAngle(targetWorldRot.eulerAngles.y);
            float roll = NormalizeAngle(prevEuler.z);

            pitch = Mathf.Lerp(pitch, 0f, autoLevelStrength * Time.deltaTime);
            roll = Mathf.Lerp(roll, 0f, autoLevelStrength * Time.deltaTime);

            targetWorldRot = Quaternion.Euler(pitch, yaw, roll);
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

        cameraTransform.SetPositionAndRotation(smoothedHandheldWorldPos, targetWorldRot);
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

        BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(202, UpdateCamera);

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

        BasisCursorManagement.LockCursor(nameof(BasisHandHeldCamera));
        base.OnDestroy();
    }
}
