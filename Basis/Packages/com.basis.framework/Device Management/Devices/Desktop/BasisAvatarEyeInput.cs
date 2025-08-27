using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Basis.Scripts.Device_Management.Devices.Desktop
{
    public class BasisAvatarEyeInput : BasisInput
    {
        public Camera Camera;
        public BasisLocalAvatarDriver AvatarDriver;
        public static BasisAvatarEyeInput Instance;

        [Header("Rotation")]
        public float rotationSpeed = 1f;
        public float rotationY; // pitch (X euler)
        public float rotationX; // yaw   (Y euler)
        public float minimumY = -89f;
        public float maximumY = 50f;

        [Header("Injected Offsets")]
        public float InjectedX = 0;
        public float InjectedZ = 0;
        public float InjectedZRot = 0;

        [Header("Mouse/Look")]
        public Vector2 LookRotationVector = Vector2.zero;

        [Header("Neck Pivot (from Eye -> Neck, local avatar space, meters)")]
        // This offset is defined in the avatar's local space and will be scaled by the avatar's lossyScale.
        // Typical values: y ~ -0.09 .. -0.16 (down), z ~ -0.05 .. -0.12 (back).
        public Vector3 PivotFromEye = new Vector3(0f, -0.12f, -0.08f);

        private readonly BasisLocks.LockContext CrouchingLock = BasisLocks.GetContext(BasisLocks.Crouching);
        private readonly BasisLocks.LockContext LookRotationLock = BasisLocks.GetContext(BasisLocks.LookRotation);

        public BasisLocalVirtualSpineDriver BasisVirtualSpine = new BasisLocalVirtualSpineDriver();

        public bool HasEyeEvents = false;

        public void Initialize(string ID = "Desktop Eye", string subSystems = "BasisDesktopManagement")
        {
            BasisDebug.Log("Initializing Avatar Eye", BasisDebug.LogTag.Input);

            if (BasisLocalPlayer.Instance.LocalAvatarDriver != null)
            {
                BasisDebug.Log("Using Configured Height " + BasisLocalPlayer.Instance.CurrentHeight.SelectedPlayerHeight, BasisDebug.LogTag.Input);
                ScaledDeviceCoord.position = new Vector3(InjectedX, BasisLocalPlayer.Instance.CurrentHeight.SelectedPlayerHeight, InjectedZ);
            }
            else
            {
                BasisDebug.Log("Using Fallback Height " + BasisLocalPlayer.FallbackSize, BasisDebug.LogTag.Input);
                ScaledDeviceCoord.position = new Vector3(InjectedX, BasisLocalPlayer.FallbackSize, InjectedZ);
            }

            ScaledDeviceCoord.rotation = Quaternion.identity;

            InitalizeTracking(ID, ID, subSystems, true, BasisBoneTrackedRole.CenterEye);

            if (BasisHelpers.CheckInstance(Instance))
            {
                Instance = this;
            }

            PlayerInitialized();

            if (HasEyeEvents == false)
            {
                BasisLocalPlayer.OnLocalAvatarChanged += PlayerInitialized;
                BasisCursorManagement.OnCursorStateChange += OnCursorStateChange;
                BasisPointRaycaster.UseWorldPosition = false;
                BasisVirtualSpine.Initialize();
                HasEyeEvents = true;
            }
        }

        private void OnCursorStateChange(CursorLockMode cursor, bool newCursorVisible)
        {
            BasisDebug.Log("cursor changed to : " + cursor + " | Cursor Visible : " + newCursorVisible, BasisDebug.LogTag.Input);
            if (cursor == CursorLockMode.Locked)
            {
                LookRotationLock.Remove(nameof(BasisCursorManagement));
            }
            else
            {
                LookRotationLock.Add(nameof(BasisCursorManagement));
            }
        }

        public new void OnDestroy()
        {
            if (HasEyeEvents)
            {
                BasisLocalPlayer.OnLocalAvatarChanged -= PlayerInitialized;
                BasisCursorManagement.OnCursorStateChange -= OnCursorStateChange;
                HasEyeEvents = false;
                BasisVirtualSpine.DeInitialize();
            }
            base.OnDestroy();
        }

        public void PlayerInitialized()
        {
            BasisLocalInputActions.Instance.AvatarEyeInput = this;
            AvatarDriver = BasisLocalPlayer.Instance.LocalAvatarDriver;
            Camera = BasisLocalCameraDriver.Instance.Camera;

            BasisDeviceManagement Device = BasisDeviceManagement.Instance;
            int count = Device.BasisLockToInputs.Count;
            for (int Index = 0; Index < count; Index++)
            {
                Device.BasisLockToInputs[Index].FindRole();
            }
        }

        public new void OnDisable()
        {
            BasisLocalPlayer.OnLocalAvatarChanged -= PlayerInitialized;
            base.OnDisable();
        }

        public void SetLookRotationVector(Vector2 delta)
        {
            LookRotationVector = delta;
        }

        public void HandleLookRotation(Vector2 lookVector)
        {
            BasisPointRaycaster.ScreenPoint = Mouse.current.position.value;

            if (!isActiveAndEnabled || LookRotationLock)
            {
                return;
            }

            // Yaw
            rotationX += lookVector.x * rotationSpeed;
            // Pitch (invert mouse Y as usual)
            rotationY -= lookVector.y * rotationSpeed;
        }

        public override void DoPollData()
        {
            if (!hasRoleAssigned)
                return;

            if (!LookRotationVector.Equals(Vector2.zero))
            {
                HandleLookRotation(LookRotationVector);
            }

            if (BasisLocalInputActions.Instance != null)
            {
                BasisLocalInputActions.Instance.InputState.CopyTo(CurrentInputState);
            }
            var Player = BasisLocalPlayer.Instance;
            // keep yaw tidy; let it wrap to avoid float growth
            rotationX = Mathf.Repeat(rotationX, 360f);

            // clamp pitch (do NOT modulo pitch; that breaks clamping)
            rotationY = Mathf.Clamp(rotationY, minimumY, maximumY);

            // Build target rotation (pitch around X, yaw around Y)
            Quaternion targetRot = Quaternion.Euler(rotationY, rotationX, InjectedZRot);

            // Base eye position at rest (before rotation pivot)
            float baseEyeHeight = Player.LocalAvatarDriver.ActiveAvatarEyeHeight();
            Vector3 baseEyeWorld = new Vector3(InjectedX, baseEyeHeight, InjectedZ);

            // --- SCALE-AWARE PIVOT OFFSET ---
            // Scale the local-space pivot offset by the avatar's lossyScale so the arc length matches avatar size.
            float avatarLossy = Player.CurrentHeight.SelectedAvatarToAvatarDefaultScale;

            PivotFromEye = BasisHelpers.AvatarPositionConversion(Player.BasisAvatar.AvatarEyePosition);
            PivotFromEye.y = 0;//new Vector3(0f, -0.12f, -0.08f);
            PivotFromEye.z = -PivotFromEye.z;

            Player.LocalAvatarDriver.ActiveAvatarEyeHeight();
            Vector3 scaledPivotFromEye = new Vector3(
                PivotFromEye.x * avatarLossy,
                PivotFromEye.y * avatarLossy,
                PivotFromEye.z * avatarLossy
            );

            // Rotate around pivot: Eye' = Pivot + R * (EyeBase - Pivot), with Pivot = EyeBase + scaledPivotFromEye
            Vector3 pivotWorld = baseEyeWorld + scaledPivotFromEye;
            Vector3 rotatedEyeWorld = pivotWorld + (targetRot * (-scaledPivotFromEye));

            // Apply crouch vertical adjustment after rotation so crouch affects final eye height consistently
            if (!CrouchingLock)
            {
                var crouchMinimum = Player.LocalCharacterDriver.MinimumCrouchPercent;
                float heightAdjustment = (1 - crouchMinimum) * Player.LocalCharacterDriver.CrouchBlend + crouchMinimum;
                rotatedEyeWorld.y -= Control.TposeLocalScaled.position.y * (1 - heightAdjustment);
            }

            // Write out unscaled (tracker space) coords
            UnscaledDeviceCoord.position = rotatedEyeWorld;
            UnscaledDeviceCoord.rotation = targetRot;

            // Mirror to scaled
            ScaledDeviceCoord.position = UnscaledDeviceCoord.position;
            ScaledDeviceCoord.rotation = UnscaledDeviceCoord.rotation;

            ControlOnlyAsDevice();
            UpdatePlayerControl();
        }

        public override void ShowTrackedVisual()
        {
            if (BasisVisualTracker == null && LoadedDeviceRequest == null)
            {
                DeviceSupportInformation Match = BasisDeviceManagement.Instance.BasisDeviceNameMatcher.GetAssociatedDeviceMatchableNames(CommonDeviceIdentifier);
                if (Match.CanDisplayPhysicalTracker)
                {
                    LoadModelWithKey(Match.DeviceID);
                }
                else
                {
                    if (UseFallbackModel())
                    {
                        LoadModelWithKey(FallbackDeviceID);
                    }
                }
            }
        }

        public override void PlayHaptic(float duration = 0.25F, float amplitude = 0.5F, float frequency = 0.5F)
        {
        }

        public override void PlaySoundEffect(string SoundEffectName, float Volume)
        {
            PlaySoundEffectDefaultImplementation(SoundEffectName, Volume);
        }
    }
}
