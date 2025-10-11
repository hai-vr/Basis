using Basis.Scripts.Animator_Driver;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using System.Collections.Generic;
using Unity.Mathematics;
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
        public Transform BasisLocalPlayerTransform;

        public Vector3 bottomPointLocalSpace;
        public Vector3 LastBottomPoint;

        public bool groundedPlayer;
        public bool LastWasGrounded = true;
        public bool IsFalling;

        public SimulationHandler JustJumped;
        public SimulationHandler JustLanded;

        [SerializeField] public float MaximumMovementSpeed = 4;
        [SerializeField] public float DefaultMovementSpeed = 2.5f;
        [SerializeField] public float MinimumMovementSpeed = 0.5f;
        [SerializeField, Range(0f, 1f)] public float MinimumCrouchPercent = 0.5f;
        [SerializeField] public float gravityValue = -9.81f;
        [SerializeField] public float RaycastDistance = 0.2f;
        [SerializeField] public float MinimumColliderSize = 0.01f;

        public float jumpHeight = 1.0f;
        public float currentVerticalSpeed = 0f;

        public Vector2 Rotation;
        public float RotationSpeed = 200;
        public float pushPower = 1f;

        public const float CrouchDeltaCoefficient = 0.01f;
        public const float SnapTurnAbsoluteThreshold = 0.8f;
        public bool UseSnapTurn => SMModuleControllerSettings.SnapTurnAngle != -1;
        public float SnapTurnAngle => SMModuleControllerSettings.SnapTurnAngle;
        public bool isSnapTurning;

        public Vector3 CurrentPosition;
        public Quaternion CurrentRotation;
        public CollisionFlags Flags;

        public Vector2 MovementVector { get; private set; }
        [field: SerializeField] public float MovementSpeedScale { get; private set; }
        [field: SerializeField] public float MovementSpeedBoost { get; private set; }
        public float DefaultMovementSpeedMultiplier = 0.625f;
        public float MaximumMovementSpeedBoost = 1.6f;

        public float CrouchBlend = 1f;
        public float CrouchBlendDelta = 0f;

        public bool IsCrouching => CrouchBlend <= LocalAnimatorDriver.CrouchThreshold;
        public float CurrentSpeed;
        public bool HasJumpAction = false;

        public Quaternion currentRotation;
        public float eyeHeight;

        public BasisLocks.LockContext MovementLock = BasisLocks.GetContext(BasisLocks.Movement);
        public BasisLocks.LockContext CrouchingLock = BasisLocks.GetContext(BasisLocks.Crouching);

        // === Movement modes ===
        public enum Mode { Walk, Fly, NoClip }
        public readonly Dictionary<Mode, IMovementMode> _modes = new();
        public IMovementMode CurrentMode { get; private set; }
        public Mode CurrentModeKind { get; private set; } = Mode.Walk;

        public bool HasEvents { get; private set; }
        public bool IsEnabled = true;

        public void Initialize(BasisLocalPlayer localPlayer)
        {
            LocalPlayer = localPlayer;
            BasisLocalPlayerTransform = LocalPlayer.transform;
            LocalAnimatorDriver = localPlayer.LocalAnimatorDriver;

            characterController.minMoveDistance = 0;
            characterController.skinWidth = 0.01f;

            MaximumMovementSpeedBoost = MaximumMovementSpeed / DefaultMovementSpeed;
            SetMovementSpeedMultiplier(GetMultiplierForMovementSpeed(DefaultMovementSpeed));

            // Register movement modes
            _modes[Mode.Walk] = new BasisWalkMovementMode();
            _modes[Mode.Fly] = new BasisFlyMovementMode();
            _modes[Mode.NoClip] = new BasisNoClipMovementMode();

            // Activate default mode
            SetMode(Mode.Walk);

            HasEvents = true;
        }
        public void SetMode(Mode mode)
        {
            if (CurrentMode != null)
            {
                BasisDebug.Log($"Character controller Mode Unloading {CurrentMode.Name}");
                CurrentMode.Exit(this);
            }
            CurrentModeKind = mode;
            CurrentMode = _modes[mode];
            BasisDebug.Log($"Character controller Mode Set {CurrentMode.Name}");
            CurrentMode.Enter(this);
        }

        public void DeInitalize()
        {
            if (HasEvents)
            {
                CurrentMode?.Exit(this);
                HasEvents = false;
            }
        }

        public void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (CurrentMode?.Collision == CollisionHandling.Ghost) return;

            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;

            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
            body.AddForce(pushDir * pushPower, ForceMode.Impulse);
        }

        public void SimulateMovement(float deltaTime)
        {
            if (!IsEnabled) return;

            LastBottomPoint = bottomPointLocalSpace;
            CalculateCharacterSize();

            // Delegate to mode for movement + gravity + jump handling
            CurrentMode.Tick(this, deltaTime);

            // Rotation (shared across modes; unchanged from your logic)
            float rotationAmount;
            if (UseSnapTurn)
            {
                var isAboveThreshold = math.abs(Rotation.x) > SnapTurnAbsoluteThreshold;
                if (isAboveThreshold != isSnapTurning)
                {
                    isSnapTurning = isAboveThreshold;
                    rotationAmount = isSnapTurning ? math.sign(Rotation.x) * SnapTurnAngle : 0f;
                }
                else
                {
                    rotationAmount = 0f;
                }
            }
            else
            {
                rotationAmount = Rotation.x * RotationSpeed * deltaTime;
            }

            Vector3 pivot = BasisLocalBoneDriver.EyeControl.OutgoingWorldData.position;
            Vector3 directionToPivot = CurrentPosition - pivot;
            Quaternion rotation = Quaternion.AngleAxis(rotationAmount, Vector3.up);
            Vector3 rotatedDirection = rotation * directionToPivot;
            Vector3 finalPosition = pivot + rotatedDirection;

            BasisLocalPlayerTransform.SetPositionAndRotation(finalPosition, rotation * CurrentRotation);

            float heightOffset = (characterController.height / 2) - characterController.radius;
            bottomPointLocalSpace = finalPosition + (characterController.center - new Vector3(0, heightOffset, 0));
        }

        public void HandleJumpRequest()
        {
            // external trigger
            if (groundedPlayer && !HasJumpAction) HasJumpAction = true;
        }

        public void GroundCheck()
        {
            groundedPlayer = characterController.isGrounded;
            IsFalling = !groundedPlayer;

            if (groundedPlayer && !LastWasGrounded)
            {
                JustLanded?.Invoke();
                currentVerticalSpeed = 0f;
            }
            LastWasGrounded = groundedPlayer;
        }

        public void CrouchToggle()
        {
            CrouchBlend = CrouchingLock || CrouchBlend <= LocalAnimatorDriver.CrouchThreshold ? 1f : 0f;
            UpdateMovementSpeed(BasisLocalInputActions.Instance.IsRunHeld);
        }

        public void SetCrouchBlendDelta(float delta) => CrouchBlendDelta = delta;

        public void UpdateCrouchBlend(float delta)
        {
            CrouchBlend = CrouchingLock ? 1f : math.clamp(CrouchBlend + delta * CrouchDeltaCoefficient, 0, 1);
            UpdateMovementSpeed(BasisLocalInputActions.Instance.IsRunHeld);
        }

        public void UpdateMovementSpeed(bool maxSpeed)
        {
            var topSpeed = maxSpeed ? 1f : DefaultMovementSpeedMultiplier;
            var boostSpeed = maxSpeed ? MaximumMovementSpeedBoost : 1f;
            MovementSpeedBoost = (1 - CrouchBlend) * boostSpeed;
            SetMovementSpeedMultiplier(topSpeed * CrouchBlend * MovementVector.magnitude);
        }

        public float GetMultiplierForMovementSpeed(float speed) => math.unlerp(MinimumMovementSpeed, MaximumMovementSpeed, speed);

        public void SetMovementSpeedMultiplier(float multiplier, bool constrain = true)
        {
            MovementSpeedScale = constrain ? math.clamp(multiplier, 0, 1) : multiplier;
        }

        public void SetMovementVector(Vector2 movement) => MovementVector = movement;

        public void CalculateCharacterSize()
        {
            eyeHeight = BasisLocalBoneDriver.HasEye
                ? BasisLocalBoneDriver.EyeControl.OutGoingData.position.y
                : BasisLocalPlayer.FallbackSize;

            float adjustedHeight = Mathf.Max(eyeHeight, MinimumColliderSize);
            SetCharacterHeight(adjustedHeight);
        }

        public void SetCharacterHeight(float height)
        {
            characterController.height = height;
            float skinModifiedHeight = height / 2f;

            characterController.center = BasisLocalBoneDriver.HasEye
                ? new Vector3(BasisLocalBoneDriver.EyeControl.OutGoingData.position.x, skinModifiedHeight, BasisLocalBoneDriver.EyeControl.OutGoingData.position.z)
                : new Vector3(0, skinModifiedHeight, 0);
        }
    }
}
