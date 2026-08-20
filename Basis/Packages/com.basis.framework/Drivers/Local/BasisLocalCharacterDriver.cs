using Basis.Scripts.Animator_Driver;
using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using System;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using static Basis.Scripts.BasisSdk.Players.BasisPlayer;
namespace Basis.Scripts.BasisCharacterController
{
    [System.Serializable]
    public class BasisLocalCharacterDriver
    {
        public BasisLocalPlayer LocalPlayer;
        [System.NonSerialized] public BasisLocalAnimatorDriver LocalAnimatorDriver;
        public CharacterController characterController;
        public Vector3 bottomPointLocalSpace;
        public Vector3 LastBottomPoint;
        public bool groundedPlayer;
        [SerializeField] public float MaximumMovementSpeed = 4;
        [SerializeField] public float DefaultMovementSpeed = 2.5f;
        [SerializeField] public float MinimumMovementSpeed = 0.5f;
        [SerializeField] public float ProneMovementSpeed = 0.35f;
        [SerializeField, Range(0f, 1f)] public float MinimumCrouchPercent = 0.5f;
        [SerializeField, Range(0f, 1f)] public float MinimumPronePercent = 0.15f;
        [SerializeField] public float gravityValue = -9.81f;
        [SerializeField] public float RaycastDistance = 0.2f;
        [SerializeField] public float MinimumColliderSize = 0.01f;
        public SimulationHandler JustJumped;
        public SimulationHandler JustLanded;
        public bool LastWasGrounded = true;
        public bool IsFalling;
        public bool IsJumpHeld = false;
        public bool IsDescendHeld = false;
        public bool HasJumpAction = false;
        public float jumpHeight = 1.0f; // Jump height set to 1 meter
        public float currentVerticalSpeed = 0f; // Vertical speed of the character
        /// <summary>
        /// Temporary hips offset applied on landing to simulate impact absorption.
        /// Eases toward <see cref="landingCrouchTarget"/> then recovers to zero.
        /// </summary>
        [System.NonSerialized] public float landingCrouchEffect;
        [System.NonSerialized] public float landingCrouchTarget;
        [SerializeField] public float landingDescentSpeed = 15f;
        [SerializeField] public float landingRecoverySpeed = 6f;
        [SerializeField] public float landingImpactScale = 0.06f;
        [SerializeField] public float maxLandingCrouchEffect = 0.35f;
        /// <summary>
        /// Duration in seconds after leaving the ground during which the player can still jump.
        /// Helps with unreliable grounded detection on slopes and near ledges.
        /// </summary>
        [SerializeField] public float coyoteTimeDuration = 0.15f;
        [System.NonSerialized] public float coyoteTimeCounter;
        /// <summary>
        /// Whether the player is allowed to jump — true when grounded or within the coyote time window.
        /// </summary>
        public bool CanJump => groundedPlayer || coyoteTimeCounter > 0f;
        /// <summary>
        /// Grace period before the falling state triggers, preventing animation flicker on slopes.
        /// </summary>
        [SerializeField] public float fallingGracePeriod = 0.1f;
        [System.NonSerialized] public float airborneTimer;

        // --- Movement Mode Management ---
        public enum Mode
        {
            Walk,
            Fly,
            NoClip,
        }
        private BasisWalkMovementMode _walkMode = new BasisWalkMovementMode();
        private BasisFlyMovementMode _flyMode = new BasisFlyMovementMode();
        private BasisNoClipMovementMode _noClipMode = new BasisNoClipMovementMode();
        [System.NonSerialized] public IMovementMode CurrentMode;
        [System.NonSerialized] public Mode CurrentModeKind = Mode.Walk;

        [System.NonSerialized] public float BaselineJumpHeight = 1f;
        [System.NonSerialized] public float BaselineWalkSpeed = 2.5f;
        [System.NonSerialized] public float BaselineRunSpeed = 4f;
        [System.NonSerialized] public float BaselineMinimumSpeed = 0.5f;
        [System.NonSerialized] public float BaselineGravity = -9.81f;
        [System.NonSerialized] public Mode BaselineMode = Mode.Walk;
        [System.NonSerialized] private int _appliedOverrideVersion = -1;
        public delegate void ModeChangedHandler(Mode newMode);
        public ModeChangedHandler ModeChanged;
        public void SetMode(Mode mode)
        {
            if (CurrentModeKind == mode && CurrentMode != null) return;
            CurrentMode?.Exit(this);
            CurrentModeKind = mode;
            CurrentMode = mode switch
            {
                Mode.Fly => _flyMode,
                Mode.NoClip => _noClipMode,
                _ => _walkMode,
            };
            airborneTimer = 0f;
            coyoteTimeCounter = 0f;
            StanceSpeedBlend = CrouchBlend;
            StanceSpeedProne = IsProne;
            TakeoffStanceDrop = GetCrouchHeightDrop();
            AirborneStanceLift = 0f;
            CurrentMode.Enter(this);
            UpdateMovementSpeed(UseMaxSpeed);
            ModeChanged?.Invoke(mode);
        }

        public Vector2 Rotation;
        public bool HasEvents = false;
        public float pushPower = 1f;
        private const float CrouchDeltaCoefficient = 0.01f;
        private const float SnapTurnAbsoluteThreshold = 0.8f;
        private bool isSnapTurning;
        public Vector3 CurrentPosition;
        public Quaternion CurrentRotation;
        public CollisionFlags Flags;
        public float radius;

        // Inputs of the last CalculateCharacterSize() call. CharacterController.height
        // and .center are skipped when none of these have changed (bit-exact compare —
        // not Vector3 ==, which uses an epsilon and would let sub-epsilon drift slip
        // through and pop the collider once the drift accumulated past threshold).
        private Vector3 _sizeCache_EyePos;
        private bool _sizeCache_HasEye;
        private float _sizeCache_Radius;
        private bool _sizeCache_Valid;

        private float _appliedRadius = float.NaN;
        private float _appliedSkinWidth = float.NaN;
        private float _appliedHeight = float.NaN;
        private Vector3 _appliedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
        private float _appliedStepOffset = float.NaN;
        public Vector2 MovementVector { get; private set; }
        /// <summary>
        /// A value between 0 and 1 representing the relative speed of player movement.
        /// </summary>
        [field: SerializeField] public float MovementSpeedScale { get; private set; }
        [field: SerializeField] public float MovementSpeedBoost { get; private set; }
        /// <summary>
        /// A value between 0 and 1 representing the character's crouch state, where 0 is fully crouched and 1 is fully standing.
        /// </summary>
        public float CrouchBlend = 1f;
        /// <summary>
        /// Value updated by <see cref="SetCrouchBlendDelta"/> which triggers <see cref="UpdateCrouchBlend"/> implicitly each simulation frame.
        /// This is generally used by event based input systems where a start and stop event are called, but per-frame updates are not.
        /// </summary>
        public float CrouchBlendDelta = 0f;
        /// <summary>
        /// <see cref="CrouchBlend"/> as the horizontal speed model reads it. Republished from the live blend
        /// every grounded frame by <see cref="SyncStanceSpeedSource"/> and then frozen for the whole airborne
        /// window, so a stance change made in mid-air cannot alter horizontal speed.
        /// </summary>
        [System.NonSerialized] public float StanceSpeedBlend = 1f;
        /// <summary>
        /// <see cref="IsProne"/> as the horizontal speed model reads it. Latched alongside <see cref="StanceSpeedBlend"/>.
        /// </summary>
        [System.NonSerialized] public bool StanceSpeedProne;
        /// <summary>
        /// Viewpoint drop, in metres of player-root space, that the stance was applying at the moment the feet
        /// last left the ground. <see cref="ConsumeStanceLift"/> measures against this.
        /// </summary>
        [System.NonSerialized] public float TakeoffStanceDrop;
        /// <summary>
        /// Root lift, in metres, currently applied to hold the viewpoint on its ballistic arc while the legs tuck.
        /// </summary>
        [System.NonSerialized] public float AirborneStanceLift;
        /// <summary>
        /// Indicates whether the character is considered crouching based on the CrouchBlend value being less than the defined threshold.
        /// </summary>
        public bool IsCrouching => CrouchBlend <= LocalAnimatorDriver.CrouchThreshold;
        /// <summary>
        /// When true the locomotion animator plays the prone set and movement drops to crawl speed. Toggled by <see cref="ProneToggle"/>; crouching input clears it.
        /// </summary>
        public bool IsProne = false;
        public bool IsRunning => CurrentSpeed > DefaultMovementSpeed;
        public bool UseMaxSpeed => BasisLocalInputActions.IsRunHeld;
        public bool CanPushRigidbodys = false;
        public bool IsEnabled
        {
            get
            {
                return isEnabled;
            }

            set
            {
                isEnabled = value;
                Validate();
                CalculateCharacterSize();
                characterController.enabled = value;
                TakeoffStanceDrop = GetCrouchHeightDrop();
                AirborneStanceLift = 0f;
            }
        }

        [System.NonSerialized] public BasisLocks.LockContext MovementLock = BasisLocks.GetContext(BasisLocks.Movement);
        [System.NonSerialized] public BasisLocks.LockContext LookRotationLock = BasisLocks.GetContext(BasisLocks.LookRotation);
        [System.NonSerialized] public BasisLocks.LockContext CrouchingLock = BasisLocks.GetContext(BasisLocks.Crouching);
        public Transform BasisLocalPlayerTransform;
        private bool isEnabled = true;
        public float CurrentSpeed;
        public void DeInitialize()
        {
            BasisLocomotionOverrides.RemoveAll(true);
            CurrentMode?.Exit(this);
            CurrentMode = null;
            if (HasEvents)
            {
                HasEvents = false;
            }
        }
        public void Initialize(BasisLocalPlayer localPlayer)
        {
            LocalPlayer = localPlayer;
            BasisLocalPlayerTransform = localPlayer.transform;
            LocalAnimatorDriver = localPlayer.LocalAnimatorDriver;
            characterController.minMoveDistance = 0;
            ApplySkinWidth(0.01f);
            if (!HasEvents)
            {
                HasEvents = true;
            }
            SetMovementSpeedMultiplier(GetMultiplierForMovementSpeed(DefaultMovementSpeed));
            Validate();
            CalculateCharacterSize();
            CaptureLocomotionBaselines();
            SetMode(Mode.Walk);
            ApplyLocomotionOverrides(true);
        }

        /// <summary>
        /// Snapshots the authored values the override stack layers over. Refuses to run while an
        /// override is live, because the live fields are that override's output — capturing them would
        /// bake it into the baseline and it would never release.
        /// </summary>
        public void CaptureLocomotionBaselines()
        {
            if (BasisLocomotionOverrides.Count != 0)
            {
                return;
            }

            BaselineJumpHeight = jumpHeight;
            BaselineWalkSpeed = DefaultMovementSpeed;
            BaselineRunSpeed = MaximumMovementSpeed;
            BaselineMinimumSpeed = MinimumMovementSpeed;
            BaselineGravity = gravityValue;
            BaselineMode = CurrentModeKind;
            _appliedOverrideVersion = -1;
        }

        /// <summary>
        /// Folds the resolved <see cref="BasisLocomotionOverrides"/> stack onto the live driver values,
        /// falling back to the authored baseline for every field no override claims. Skipped entirely
        /// when the stack has not changed since the last apply.
        /// </summary>
        public void ApplyLocomotionOverrides(bool force = false)
        {
            int version = BasisLocomotionOverrides.Version;
            if (!force && version == _appliedOverrideVersion)
            {
                return;
            }
            _appliedOverrideVersion = version;

            BasisLocomotionEffective effective = BasisLocomotionOverrides.Flatten(
                BasisLocomotionOverrides.Resolve(),
                new BasisLocomotionBaseline
                {
                    JumpHeight = BaselineJumpHeight,
                    WalkSpeed = BaselineWalkSpeed,
                    RunSpeed = BaselineRunSpeed,
                    MinimumSpeed = BaselineMinimumSpeed,
                    Gravity = BaselineGravity,
                    Mode = BaselineMode,
                });

            jumpHeight = effective.JumpHeight;
            gravityValue = effective.Gravity;
            MinimumMovementSpeed = effective.MinimumSpeed;
            DefaultMovementSpeed = effective.WalkSpeed;
            MaximumMovementSpeed = effective.RunSpeed;
            UpdateMovementSpeed(UseMaxSpeed);

            SetMode(effective.Mode);
        }

        public void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (CanPushRigidbodys)
            {
                // Check if the hit object has a Rigidbody and if it is not kinematic
                Rigidbody body = hit.collider.attachedRigidbody;

                if (body == null || body.isKinematic)
                {
                    return;
                }

                // Ensure we're only pushing objects in the horizontal plane
                Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);

                // Apply the force to the object
                body.AddForce(pushDir * pushPower, ForceMode.Impulse);
            }
        }
        static readonly ProfilerMarker sMarkerMoveSize = new ProfilerMarker("BasisDriver.LocalPlayer.Move.Size");
        static readonly ProfilerMarker sMarkerMoveMode = new ProfilerMarker("BasisDriver.LocalPlayer.Move.Mode");
        static readonly ProfilerMarker sMarkerMoveTurn = new ProfilerMarker("BasisDriver.LocalPlayer.Move.Turn");
        public static readonly ProfilerMarker MovePhysicsMarker = new ProfilerMarker("BasisDriver.LocalPlayer.Move.Physics");

        public void SimulateMovement(float DeltaTime)
        {
            if (!IsEnabled)
            {

                // If you want basis localToWorld using the *new* pose:
                BasisLocalPose.GetPose(BasisPoseSlot.PlayerRoot, BasisLocalPlayerTransform, out Vector3 Position, out Quaternion Rotation);
                BasisLocalPlayer.localToWorldMatrix = Matrix4x4.TRS(Position, Rotation, BasisLocalPose.GetLossyScale(BasisPoseSlot.PlayerRoot, BasisLocalPlayerTransform));
                return;
            }
            sMarkerMoveSize.Begin();
            ApplyLocomotionOverrides();
            BasisScriptedPlayerInput.ApplyLocomotion(this);
            LastBottomPoint = bottomPointLocalSpace;
            CalculateCharacterSize();
            // Two-phase landing impact: ease into dip, then ease back up
            if (landingCrouchTarget > 0f)
            {
                // Phase 1: descend toward peak impact
                landingCrouchEffect = Mathf.Lerp(landingCrouchEffect, landingCrouchTarget, landingDescentSpeed * DeltaTime);
                if (landingCrouchTarget - landingCrouchEffect < 0.01f)
                {
                    landingCrouchTarget = 0f;
                }
            }
            else if (landingCrouchEffect > 0f)
            {
                // Phase 2: recover back to standing
                landingCrouchEffect = Mathf.Lerp(landingCrouchEffect, 0f, landingRecoverySpeed * DeltaTime);
                if (landingCrouchEffect < 0.001f) landingCrouchEffect = 0f;
            }
            sMarkerMoveSize.End();

            // Delegate movement, gravity, and ground checking to the active mode.
            sMarkerMoveMode.Begin();
            if (CurrentMode != null)
            {
                CurrentMode.Tick(this, DeltaTime);
            }
            else
            {
                HandleMovement(DeltaTime);
                GroundCheck(DeltaTime);
            }
            sMarkerMoveMode.End();

            sMarkerMoveTurn.Begin();
            // Calculate the rotation amount for this frame
            bool lookLocked = LookRotationLock;
            float rotationAmount;
            if (SMModuleControllerSettings.UsingSnapTurnAngle && BasisDeviceManagement.IsCurrentModeVR())
            {
                var isAboveThreshold = math.abs(Rotation.x) > SnapTurnAbsoluteThreshold;
                if (isAboveThreshold != isSnapTurning)
                {
                    isSnapTurning = isAboveThreshold;
                    if (isSnapTurning)
                    {
                        rotationAmount = math.sign(Rotation.x) * SMModuleControllerSettings.SnapTurnAngle;
                    }
                    else
                    {
                        rotationAmount = 0f;
                    }
                }
                else
                {
                    rotationAmount = 0f;
                }
            }
            else
            {
                rotationAmount = Rotation.x * SMModuleControllerSettings.SmoothTurnSpeed * DeltaTime;
            }

            if (lookLocked)
            {
                rotationAmount = 0f;
            }

            Vector3 newPos;
            Quaternion newRot;
            if (rotationAmount != 0f)
            {
                // Get the current rotation and position of the player
                Vector3 pivot = BasisLocalBoneDriver.EyeControl.OutgoingWorldData.position;
                Vector3 upAxis = Vector3.up;

                // Calculate direction from the pivot to the current position
                Vector3 directionToPivot = CurrentPosition - pivot;

                // Calculate rotation quaternion based on the rotation amount and axis
                Quaternion rotation = Quaternion.AngleAxis(rotationAmount, upAxis);

                // Apply rotation to the direction vector
                Vector3 rotatedDirection = rotation * directionToPivot;

                newPos = pivot + rotatedDirection;
                newRot = rotation * CurrentRotation;

                BasisLocalPlayerTransform.SetPose(newPos, newRot);
            }
            else
            {
                newPos = CurrentPosition;
                newRot = CurrentRotation;
            }

            float HeightOffset = (_appliedHeight / 2) - radius;
            bottomPointLocalSpace = newPos + (_appliedCenter - new Vector3(0, HeightOffset, 0));

            // If you want basis localToWorld using the *new* pose:
            BasisLocalPlayer.localToWorldMatrix = Matrix4x4.TRS(newPos, newRot, BasisLocalPose.GetLossyScale(BasisPoseSlot.PlayerRoot, BasisLocalPlayerTransform));
            sMarkerMoveTurn.End();
        }

        public float GetVerticalMovement()
        {
            float moveLocal = BasisLocalInputActions.MoveLocalUpDown.ReadValue<float>();
            float ascend = IsJumpHeld ? 1.0f : 0.0f;
            float descend = (IsDescendHeld || BasisLocalInputActions.IsCrouchHeld) ? -1.0f : 0.0f;
            return Mathf.Clamp(moveLocal + ascend + descend, -1.0f, 1.0f);
        }

        public void HandleJumpRequest()
        {
            if (CanJump && !HasJumpAction)
            {
                HasJumpAction = true;
            }
        }
        public void GroundCheck(float deltaTime)
        {
            groundedPlayer = characterController.isGrounded;

            if (groundedPlayer)
            {
                airborneTimer = 0f;
                IsFalling = false;

                if (!LastWasGrounded)
                {
                    float fallSpeed = Mathf.Abs(currentVerticalSpeed);
                    // Suppress hip dip in FBT to avoid fighting real hip tracker data on landing.
                    // The comment already named the right question -- the code was asking a different one. The dip is
                    // applied to the HIPS (BasisLocalRigDriver subtracts landingCrouchEffect from hipsPos), so only a
                    // HIPS tracker has anything to be fought. HasFBIKTrackers is true for a chest/shoulder/elbow
                    // tracker too, and those leave the pelvis fully IK-derived -- there the dip is correct and wanted.
                    if (!(BasisAvatarIKStageCalibration.HasHipsFBIKTracker && Basis.BasisUI.BasisSettingsDefaults.DisableAnimationsInFBT.RawValue))
                    {
                        landingCrouchTarget = Mathf.Clamp(fallSpeed * landingImpactScale, 0f, maxLandingCrouchEffect) * BasisHeightDriver.AvatarToDefaultRatioScaled;
                    }
                    JustLanded?.Invoke();
                    currentVerticalSpeed = 0f;
                }
            }
            else
            {
                // Only trigger the falling state after a grace period to prevent
                // animation flickering on slopes and during ground-type transitions.
                airborneTimer += deltaTime;
                IsFalling = airborneTimer > fallingGracePeriod;

                // Grant coyote time on the frame we leave the ground,
                // but only when walking off (not after an active jump).
                if (LastWasGrounded && currentVerticalSpeed <= 0f)
                {
                    coyoteTimeCounter = coyoteTimeDuration;
                    currentVerticalSpeed = -2f; // Smooth ledge transition without terminal velocity
                }
                else if (coyoteTimeCounter > 0f)
                {
                    coyoteTimeCounter -= deltaTime;
                }
            }

            LastWasGrounded = groundedPlayer;
            SyncStanceSpeedSource();
        }

        /// <summary>
        /// Republishes the live stance into the horizontal speed model while the feet are on the ground, and
        /// holds the last grounded value for as long as they are not.
        ///
        /// Horizontal motion is rebuilt from input every frame with no velocity state behind it, so a stance
        /// change lands on the very next frame's displacement. On the ground that is the intended crouch
        /// slowdown. In the air it is not — nothing the legs do mid-jump changes horizontal velocity — yet
        /// crouching there dropped a running player from <see cref="MaximumMovementSpeed"/> to just above the
        /// walk floor in a single frame. Fly and noclip have no ground to leave, so they always track live.
        /// </summary>
        public void SyncStanceSpeedSource()
        {
            if (CurrentModeKind == Mode.Walk && !groundedPlayer)
            {
                return;
            }
            if (StanceSpeedBlend == CrouchBlend && StanceSpeedProne == IsProne)
            {
                return;
            }
            StanceSpeedBlend = CrouchBlend;
            StanceSpeedProne = IsProne;
            UpdateMovementSpeed(UseMaxSpeed);
        }

        /// <summary>
        /// Metres the crouch blend currently lowers the viewpoint by, in player-root space — the same term the
        /// desktop and headless eye providers subtract from the head before placing the eye. Zero in VR, where
        /// the headset reports a real head and no synthetic drop is applied, and zero before the head bone
        /// exists. Deliberately reads <see cref="CrouchBlend"/> only: prone is a whole-body pose change, not a
        /// leg tuck, so it is left out of the airborne compensation.
        /// </summary>
        public float GetCrouchHeightDrop()
        {
            if (BasisDeviceManagement.IsCurrentModeVR())
            {
                return 0f;
            }
            var head = BasisLocalBoneDriver.HeadControl;
            if (head == null)
            {
                return 0f;
            }
            return CrouchHeightDrop(head.TposeLocalScaled.position.y, MinimumCrouchPercent, CrouchBlend);
        }

        /// <summary>
        /// Pure form of <see cref="GetCrouchHeightDrop"/>: metres a crouch blend lowers a viewpoint sitting
        /// <paramref name="headHeight"/> above the root. Zero for a head height that is not a usable number.
        /// </summary>
        public static float CrouchHeightDrop(float headHeight, float minimumCrouchPercent, float crouchBlend)
        {
            if (float.IsNaN(headHeight) || float.IsInfinity(headHeight) || headHeight <= 0f)
            {
                return 0f;
            }
            return headHeight * (1f - math.clamp(minimumCrouchPercent, 0f, 1f)) * (1f - math.clamp(crouchBlend, 0f, 1f));
        }

        /// <summary>
        /// Root lift owed this frame so that a stance change made in mid-air moves the FEET rather than the head,
        /// returned as a delta to fold into the frame's vertical movement.
        ///
        /// The viewpoint is placed relative to the player root and the root is what the jump arc acts on, so
        /// lowering the stance in the air dragged the camera down off that arc. Lifting the root by exactly the
        /// amount the viewpoint drops holds the head still and raises the capsule's bottom instead. The capsule
        /// shrinks by the same amount, so its top does not move and the swept volume never exceeds the standing
        /// one. Measured against the stance held at takeoff, so extending the legs again pushes the feet back
        /// down rather than dropping the head.
        /// </summary>
        public float ConsumeStanceLift()
        {
            return ResolveStanceLift(groundedPlayer, GetCrouchHeightDrop(), ref TakeoffStanceDrop, ref AirborneStanceLift);
        }

        /// <summary>
        /// Pure form of <see cref="ConsumeStanceLift"/>. While grounded the feet are pinned and the head is
        /// free to move, which is the ordinary crouch, so the lift is discarded and the takeoff reference
        /// re-armed against the stance the next jump will leave in. While airborne the lift tracks the change
        /// in viewpoint drop since takeoff, and only the frame-to-frame difference is returned to be moved.
        /// </summary>
        public static float ResolveStanceLift(bool grounded, float currentDrop, ref float takeoffDrop, ref float appliedLift)
        {
            if (grounded)
            {
                takeoffDrop = currentDrop;
                appliedLift = 0f;
                return 0f;
            }

            float target = currentDrop - takeoffDrop;
            float delta = target - appliedLift;
            appliedLift = target;
            return delta;
        }

        public void CrouchToggle()
        {
            IsProne = false;
            // check what the animator driver considers to be crouching, and standup if crouch threshold is matched, otherwise, full crouch
            CrouchBlend = CrouchingLock || CrouchBlend <= LocalAnimatorDriver.CrouchThreshold ? 1f : 0f;
            UpdateMovementSpeed(UseMaxSpeed);
        }

        public void ProneToggle()
        {
            if (CrouchingLock) return;
            IsProne = !IsProne;
            UpdateMovementSpeed(UseMaxSpeed);
        }

        /// <summary>
        /// 0..1 multiplier applied to the head/eye height for the current stance:
        /// 1 standing, down to <see cref="MinimumCrouchPercent"/> via CrouchBlend, or <see cref="MinimumPronePercent"/> while prone.
        /// </summary>
        public float GetStanceHeightPercent()
        {
            if (IsProne) return MinimumPronePercent;
            return (1f - MinimumCrouchPercent) * CrouchBlend + MinimumCrouchPercent;
        }

        public void SetCrouchBlendDelta(float delta)
        {
            CrouchBlendDelta = delta;
        }

        public void UpdateCrouchBlend(float delta)
        {
            if (CrouchingLock) return;
            CrouchBlend = math.clamp(CrouchBlend + delta * CrouchDeltaCoefficient, 0, 1);
            UpdateMovementSpeed(UseMaxSpeed);
        }

        public void UpdateMovementSpeed(bool maxSpeed)
        {
            var topSpeed = GetMultiplierForMovementSpeed(maxSpeed ? MaximumMovementSpeed : DefaultMovementSpeed);
            var boostSpeed = maxSpeed ? MaximumMovementSpeed / DefaultMovementSpeed : 1f;
            // inverse of crouch blend so standing is the least value, multiply by the boost that running gives
            MovementSpeedBoost = (1 - StanceSpeedBlend) * boostSpeed;
            SetMovementSpeedMultiplier(topSpeed * StanceSpeedBlend * MovementVector.magnitude);
        }

        public float GetMultiplierForMovementSpeed(float speed)
        {
            return math.unlerp(MinimumMovementSpeed, MaximumMovementSpeed, speed);
        }
        public void SetMovementSpeedMultiplier(float multiplier, bool constrain = true)
        {
            MovementSpeedScale = multiplier;
            if (constrain) MovementSpeedScale = math.clamp(MovementSpeedScale, 0, 1);
        }

        public void SetMovementVector(Vector2 movement)
        {
            MovementVector = movement;
        }
        /// <summary>
        /// Horizontal facing that movement input is expressed in: the viewpoint (CenterEye) — the HMD in VR,
        /// the mouse-look camera on desktop. Deliberately NOT the head bone. The head bone is an avatar-side
        /// output that normally just copies the eye rotation, so the two agree until something overrides it —
        /// camera tracking writing a Head-role tracker, or a real head tracker — and then the player walks off
        /// at the angle their physical head is turned instead of where the camera points.
        /// </summary>
        public static Quaternion GetMovementFacing()
        {
            // Project view forward onto horizontal plane (avoids gimbal lock near ±90° pitch)
            Quaternion viewRotation = BasisLocalBoneDriver.EyeControl.OutgoingWorldData.rotation;
            Vector3 flatForward = viewRotation * Vector3.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = -(viewRotation * Vector3.up);
                flatForward.y = 0f;
            }
            return Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        }
        public void HandleMovement(float DeltaTime)
        {
            Quaternion flattenedRotation = GetMovementFacing();

            if (CrouchBlendDelta != 0) UpdateCrouchBlend(CrouchBlendDelta);
            // Calculate horizontal movement direction
            Vector3 horizontalMoveDirection = new Vector3(MovementVector.x, 0, MovementVector.y).normalized;

            CurrentSpeed = math.lerp(MinimumMovementSpeed, MaximumMovementSpeed, MovementSpeedScale) + MinimumMovementSpeed * MovementSpeedBoost;
            if (StanceSpeedProne) CurrentSpeed = ProneMovementSpeed;

            Vector3 totalMoveDirection = flattenedRotation * horizontalMoveDirection * CurrentSpeed * DeltaTime;
            if (MovementLock)
            {
                HasJumpAction = false;
                totalMoveDirection = Vector3.zero;
            }


            // Handle jumping and falling
            if (CanJump && HasJumpAction)
            {
                // jumpHeight is an apex in metres, so it scales linearly with the avatar. That is exactly
                // Froude-correct: v0 = sqrt(2gh), so h proportional to L gives v0 proportional to sqrt(L).
                // Unscaled, a half-size avatar jumped 2.30 leg-lengths against an adult's 1.15 — the cause
                // BasisFootSimulateJob already names above its airborne-detection fix.
                currentVerticalSpeed = Mathf.Sqrt(jumpHeight * AvatarSizeRatio() * -2f * gravityValue);
                coyoteTimeCounter = 0f; // Consume coyote time to prevent double jumps
                JustJumped?.Invoke();
            }
            else
            {
                currentVerticalSpeed += gravityValue * DeltaTime;
            }

            // Terminal velocity. This clamped against gravityValue — an m/s^2 constant used as an m/s
            // cap, which also meant terminal was reached after exactly 1.0 s of fall on every avatar.
            // Speeds go as sqrt(g*L), so the cap tracks avatar size by the sqrt of the ratio. The
            // default-size value is the old 9.81 so scale-1 behaviour is unchanged.
            currentVerticalSpeed = Mathf.Max(currentVerticalSpeed, -TerminalVelocity());


            HasJumpAction = false;
            totalMoveDirection.y = currentVerticalSpeed * DeltaTime;

            // Move character
            Flags = characterController.Move(totalMoveDirection);
            // PhysX writes the root transform directly; the pose cache cannot observe it.
            BasisLocalPose.InvalidateAll();
            BasisLocalPlayerTransform.GetPose(out CurrentPosition, out CurrentRotation);
        }
        // Authored (unscaled) capsule dimensions, captured once. CalculateCharacterSize writes
        // avatar-scaled values back onto the controller, so re-reading the controller on a later
        // call would compound the scale factor every time the avatar is resized.
        private float _authoredRadius = -1f;
        private float _authoredSkinWidth = -1f;
        private float _authoredStepOffset = -1f;

        /// <summary>Guarded avatar size ratio; 1 when the height driver has nothing sane to report.</summary>
        public static float AvatarSizeRatio()
        {
            float s = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            return (float.IsNaN(s) || float.IsInfinity(s) || s <= 0f) ? 1f : s;
        }

        /// <summary>Fall-speed cap at default avatar size, in m/s. Was implicitly abs(gravityValue).</summary>
        private const float TerminalVelocityAtDefaultSize = 9.81f;

        /// <summary>Fall-speed cap for this avatar. Speeds scale as sqrt(g*L).</summary>
        public static float TerminalVelocity() => TerminalVelocityAtDefaultSize * Mathf.Sqrt(AvatarSizeRatio());

        /// <summary>
        /// Locomotion speed multiplier. Gait is modelled on the Froude number v/sqrt(g*L) and every step
        /// parameter is derived from the avatar's own leg, but movement speed was a fixed m/s — so a small
        /// avatar's v-hat inflated by 1/sqrt(scale), pinning speedScale and urgencyT at maximum and roughly
        /// doubling step cadence at half size. Scaling speed by sqrt(ratio) holds v-hat constant, so cadence,
        /// bob, sway and double-support all match at every size (0.5x size keeps 71% of speed, not 50%).
        /// </summary>
        public static float LocomotionSpeedScale() => Mathf.Sqrt(AvatarSizeRatio());

        public void Validate()
        {
            radius = characterController.radius;
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
            {
                radius = 0.1f;
            }

            if (_authoredRadius <= 0f)
            {
                _authoredRadius = radius;
                _authoredSkinWidth = characterController.skinWidth;
                _authoredStepOffset = characterController.stepOffset;
            }

            ApplyRadius(radius);
        }

        private void ApplyRadius(float value)
        {
            if (_appliedRadius == value) return;
            _appliedRadius = value;
            characterController.radius = value;
        }

        private void ApplySkinWidth(float value)
        {
            if (_appliedSkinWidth == value) return;
            _appliedSkinWidth = value;
            characterController.skinWidth = value;
        }

        private void ApplyHeight(float value)
        {
            if (_appliedHeight == value) return;
            _appliedHeight = value;
            characterController.height = value;
        }

        private void ApplyCenter(Vector3 value)
        {
            if (_appliedCenter.x == value.x && _appliedCenter.y == value.y && _appliedCenter.z == value.z) return;
            _appliedCenter = value;
            characterController.center = value;
        }

        private void ApplyStepOffset(float value)
        {
            if (_appliedStepOffset == value) return;
            _appliedStepOffset = value;
            characterController.stepOffset = value;
        }

        public void CalculateCharacterSize()
        {
            bool hasEye = BasisLocalBoneDriver.HasEye;
            Vector3 eyePos = hasEye
                ? BasisLocalBoneDriver.EyeControl.OutGoingData.position
                : default;

            // Capsule radius and skin are authored in metres at default avatar size, and the player root
            // is never scaled (the avatar transform is), so without this they stay adult-sized on every
            // avatar — and the 2*radius height floor below then exceeds a small avatar's whole body.
            // Derived from the authored values, never from the controller's current (already scaled) ones.
            float sizeRatio = AvatarSizeRatio();
            if (_authoredRadius > 0f)
            {
                radius = _authoredRadius * sizeRatio;
            }

            // Bit-exact change check — Vector3 == uses an epsilon (~9.99e-11 squared)
            // which would silently swallow sub-epsilon eye drift; the height stays
            // stale until the drift clears the threshold and then snaps, which reads
            // as jitter. Component-wise float compares catch every bit change so the
            // collider tracks the eye smoothly.
            if (_sizeCache_Valid
                && hasEye == _sizeCache_HasEye
                && radius == _sizeCache_Radius
                && eyePos.x == _sizeCache_EyePos.x
                && eyePos.y == _sizeCache_EyePos.y
                && eyePos.z == _sizeCache_EyePos.z)
            {
                return;
            }

            float rawEyeHeight = hasEye ? eyePos.y : BasisHeightDriver.FallbackHeightInMeters;

            // Validate tracking data
            if (float.IsNaN(rawEyeHeight) || float.IsInfinity(rawEyeHeight) || rawEyeHeight <= 0f)
            {
                rawEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            }

            // Enforce minimum collider size
            if (rawEyeHeight < MinimumColliderSize)
            {
                rawEyeHeight = MinimumColliderSize;
            }

            if (_authoredRadius > 0f)
            {
                ApplyRadius(radius);
                ApplySkinWidth(_authoredSkinWidth * sizeRatio);
            }

            // Ensure height is valid relative to radius
            float minHeight = 2f * radius + 0.001f;
            float finalHeight = Mathf.Max(rawEyeHeight, minHeight);

            ApplyHeight(finalHeight);

            float halfHeight = finalHeight * 0.5f;

            // Offset the capsule down by skinWidth so the collider bottom
            // (including its skin shell) sits flush with the floor instead
            // of hovering skinWidth above it.
            float skinCompensation = _appliedSkinWidth;

            if (hasEye)
            {
                ApplyCenter(new Vector3(eyePos.x, halfHeight - skinCompensation, eyePos.z));
            }
            else
            {
                ApplyCenter(new Vector3(0f, halfHeight - skinCompensation, 0f));
            }

            // Clamp stepOffset to something sane relative to height
            float maxStep = (finalHeight + 2f * radius) - 0.001f;
            maxStep = Mathf.Max(0f, maxStep);
            maxStep = Mathf.Min(maxStep, finalHeight * 0.25f);

            // Assignment, not Min-against-itself: Mathf.Min is monotonic decreasing, so shrinking the
            // avatar and growing it back left stepOffset stuck at the small value for the whole session.
            float desiredStep = _authoredStepOffset > 0f ? _authoredStepOffset * sizeRatio : maxStep;
            ApplyStepOffset(Mathf.Min(desiredStep, maxStep));

            _sizeCache_HasEye = hasEye;
            _sizeCache_EyePos = eyePos;
            _sizeCache_Radius = radius;
            _sizeCache_Valid = true;
        }
    }
}
