using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using UnityEngine;

namespace Basis.BasisUI
{
    public class BasisMenuMover : MonoBehaviour
    {
        /// <summary>
        /// Which mode the panel group uses for placement.
        /// </summary>
        public enum PanelGroupRootMode
        {
            Floating,
            World,
            Eye,
            LeftHand,       // VR Only
            RightHand,      // VR Only

            /// <summary>
            /// VR-focused: menu spawns at eye pose, then sticks to playspace movement (no head bob),
            /// using a captured playspace-local anchor.
            /// </summary>
            PlaySpaceStable,
        }

        [Serializable]
        public struct RootModeOffset
        {
            public Vector3 Position;
            public Vector3 EulerRotation;
            public float Scale;
            public Quaternion Rotation => Quaternion.Euler(EulerRotation);
        }

        [Header("References")]
        public RectTransform GroupOffset;

        [Header("Settings")]
        public PanelGroupRootMode VRMode = PanelGroupRootMode.PlaySpaceStable;
        public PanelGroupRootMode DesktopRootMode = PanelGroupRootMode.Eye;
        public PanelGroupRootMode InUse = PanelGroupRootMode.Eye;

        [Tooltip("Base UI scale (menu sizing)")]
        public float RootScale = 0.0005f;

        [Header("Offsets are multiplied against the Player Eye Height.\nAssign your values assuming a height of 1 meter.")]
        public RootModeOffset WorldOffset;
        public RootModeOffset HeadOffset;
        public RootModeOffset LeftHandOffset;
        public RootModeOffset RightHandOffset;
        public RootModeOffset FloatingOffset;

        [Header("Floating")]
        public Vector3 VRRootOffset;

        private BasisLocalBoneControl _leftHandControl;
        private BasisLocalBoneControl _rightHandControl;

        private bool _hasLocalCreationEvent;
        private bool _hasLocalMoveEvent;

        private const float MIN_Z_SCALE = 0.01f;

        // --- PlaySpaceStable state (from v1) ---
        private bool _stableHasAnchor;
        private Vector3 _stableLocalPos;
        private Quaternion _stableLocalRot = Quaternion.identity;

        private void OnEnable()
        {
            // Local player init
            if (BasisLocalPlayer.Instance)
            {
                OnLocalPlayerCreated();
            }
            else
            {
                BasisLocalPlayer.OnLocalPlayerInitalized += OnLocalPlayerCreated;
                _hasLocalCreationEvent = true;
            }
            BasisDeviceManagement.OnBootModeChanged += OnBootModeChanged;
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnAvatarHeightChange;

            // In case we enabled late and player already exists
            if (BasisLocalPlayer.Instance)
            {
                OnAvatarHeightChange();
            }
        }

        private void OnDisable()
        {
            // Unsubscribe safely
            if (BasisLocalPlayer.Instance)
            {
                BasisLocalPlayer.Instance.OnAvatarSwitched -= OnAvatarHeightChange;
            }

            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnAvatarHeightChange;

            if (_hasLocalCreationEvent)
            {
                BasisLocalPlayer.OnLocalPlayerInitalized -= OnLocalPlayerCreated;
                _hasLocalCreationEvent = false;
            }

            BasisDeviceManagement.OnBootModeChanged -= OnBootModeChanged;

            SetMovementCallback(false);
        }

        private void OnDestroy()
        {
            // OnDisable should handle it, but this makes teardown bulletproof.
            if (BasisLocalPlayer.Instance)
            {
                BasisLocalPlayer.Instance.OnAvatarSwitched -= OnAvatarHeightChange;
            }
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnAvatarHeightChange;

            if (_hasLocalCreationEvent)
            {
                BasisLocalPlayer.OnLocalPlayerInitalized -= OnLocalPlayerCreated;
            }

            if (_hasLocalMoveEvent)
            {
                BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(120, UpdateUILocation);
            }

            BasisDeviceManagement.OnBootModeChanged -= OnBootModeChanged;
        }

        private void OnLocalPlayerCreated()
        {
            // Avatar swap + height changes
            BasisLocalPlayer.Instance.OnAvatarSwitched += OnAvatarHeightChange;

            // Bone refs
            BasisLocalPlayer.Instance.LocalBoneDriver.FindBone(out _leftHandControl, BasisBoneTrackedRole.LeftHand);
            BasisLocalPlayer.Instance.LocalBoneDriver.FindBone(out _rightHandControl, BasisBoneTrackedRole.RightHand);

            // Apply current mode
            SetRootMode(GetFindCurrentMode());
        }

        private void OnBootModeChanged(string _)
        {
            SetRootMode(GetFindCurrentMode());
        }

        public void OnAvatarHeightChange()
        {
            SetRootMode(GetFindCurrentMode());
        }

        public void OnAvatarHeightChange(BasisHeightDriver.HeightModeChange change)
        {
            // v1 had a special case for T-pose. Keep it: only rescale if not T-pose.
            if (change != BasisHeightDriver.HeightModeChange.OnTpose)
            {
                SetRootMode(GetFindCurrentMode());
            }
        }

        public PanelGroupRootMode GetFindCurrentMode()
        {
            if (BasisDeviceManagement.IsUserInDesktop())
            {
                return DesktopRootMode;
            }

            if (BasisDeviceManagement.IsCurrentModeVR())
            {
                return VRMode;
            }

            return DesktopRootMode;
        }

        /// <summary>
        /// Apply the offset for the Current Root Mode.
        /// This also subscribes to the player's movement callback if needed.
        /// </summary>
        public void SetRootMode(PanelGroupRootMode mode)
        {
            InUse = mode;

            // Reset playspace-stable anchor when switching into/out of it
            if (InUse != PanelGroupRootMode.PlaySpaceStable)
            {
                _stableHasAnchor = false;
            }

            switch (InUse)
            {
                case PanelGroupRootMode.World:
                    SetMovementCallback(false);
                    SetRootOffset(WorldOffset);
                    break;

                case PanelGroupRootMode.Eye:
                    SetMovementCallback(true);
                    UpdateUILocation(PanelGroupRootMode.Eye);
                    break;

                case PanelGroupRootMode.LeftHand:
                    SetMovementCallback(true);
                    SetRootOffset(LeftHandOffset);
                    break;

                case PanelGroupRootMode.RightHand:
                    SetMovementCallback(true);
                    SetRootOffset(RightHandOffset);
                    break;

                case PanelGroupRootMode.Floating:
                    SetMovementCallback(false);
                    SetRootOffset(FloatingOffset);
                    UpdateUILocation(PanelGroupRootMode.Floating);
                    break;

                case PanelGroupRootMode.PlaySpaceStable:
                    SetMovementCallback(true);
                    SetRootOffsetForPlaySpaceStable(); // includes VR distance behavior + scale-only
                    _stableHasAnchor = false;          // force recapture
                    UpdateUILocation(PanelGroupRootMode.PlaySpaceStable);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void SetMovementCallback(bool value)
        {
            if (value == _hasLocalMoveEvent)
            {
                return;
            }

            if (value)
            {
                BasisLocalPlayer.AfterSimulateOnLate.AddAction(120, UpdateUILocation);
            }
            else
            {
                BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(120, UpdateUILocation);
            }

            _hasLocalMoveEvent = value;
        }

        private void SetRootOffset(RootModeOffset offset)
        {
            float playerHeight = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

            GroupOffset.SetLocalPositionAndRotation(offset.Position, offset.Rotation);

            Vector3 offsetScale = Vector3.one * (offset.Scale * RootScale);
            offsetScale.z = Mathf.Max(MIN_Z_SCALE, offsetScale.z);
            GroupOffset.localScale = offsetScale;

            // Root is avatar-compensated
            transform.localScale = Vector3.one * playerHeight;
        }

        private void SetEyeOffset(float scaleFactor)
        {
            float playerHeight = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;

            Vector3 scaledOffset = Vector3.Scale(HeadOffset.Position, new Vector3(scaleFactor, scaleFactor, 1f));
            GroupOffset.SetLocalPositionAndRotation(scaledOffset, HeadOffset.Rotation);

            Vector3 offsetScale = Vector3.one * (HeadOffset.Scale * RootScale * scaleFactor);
            offsetScale.z = Mathf.Max(MIN_Z_SCALE, offsetScale.z);
            GroupOffset.localScale = offsetScale;

            transform.localScale = Vector3.one * playerHeight;
        }

        /// <summary>
        /// PlaySpaceStable distance is controlled ONLY by GroupOffset (like v1).
        /// We keep the "VR distance" default here: 0.5m forward in local space when VR.
        /// </summary>
        private void SetRootOffsetForPlaySpaceStable()
        {
            GroupOffset.SetLocalPositionAndRotation(new Vector3(0f, 0f, 0.5f), Quaternion.identity);

            ApplyScaleOnly();
        }

        private void ApplyScaleOnly()
        {
            // 1) UI group scale (menu sizing)
            Vector3 offsetScale = Vector3.one * RootScale;
            offsetScale.z = Mathf.Max(MIN_Z_SCALE, offsetScale.z);
            GroupOffset.localScale = offsetScale;

            // 2) Root scale (avatar-to-default compensation)
            transform.localScale = Vector3.one * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        }

        private void UpdateUILocation()
        {
            UpdateUILocation(InUse);
        }

        private void UpdateUILocation(PanelGroupRootMode mode)
        {
            switch (mode)
            {
                case PanelGroupRootMode.World:
                    // Static in world space; GroupOffset handles position relative to root.
                    break;

                case PanelGroupRootMode.Eye:
                    if (!BasisLocalCameraDriver.HasInstance)
                    {
                        break;
                    }
                    float fieldOfView = BasisLocalCameraDriver.CameraInstance.fieldOfView;
                    float tanFOV = Mathf.Tan((Mathf.Deg2Rad * fieldOfView) / 2f);

                    // Menu was designed at 80 FOV
                    const float designerMenuScale = 80f;
                    float tanFOVBase = Mathf.Tan((Mathf.Deg2Rad * designerMenuScale) / 2f);
                    float scaleFactor = tanFOV / tanFOVBase;

                    BasisLocalCameraDriver.GetPositionAndRotation(out Vector3 Position, out Quaternion Rotation);
                    transform.SetPositionAndRotation(Position, Rotation);

                    SetEyeOffset(scaleFactor);
                    break;

                case PanelGroupRootMode.LeftHand:
                    if (_leftHandControl == null)
                    {
                        break;
                    }
                    BasisCalibratedCoords leftData = _leftHandControl.OutgoingWorldData;
                    transform.SetPositionAndRotation(leftData.position, leftData.rotation);
                    break;

                case PanelGroupRootMode.RightHand:
                    if (_rightHandControl == null)
                    {
                        break;
                    }
                    BasisCalibratedCoords rightData = _rightHandControl.OutgoingWorldData;
                    transform.SetPositionAndRotation(rightData.position, rightData.rotation);
                    break;

                case PanelGroupRootMode.Floating:
                    if (!BasisLocalCameraDriver.HasInstance)
                    {
                        break;
                    }
                    BasisLocalCameraDriver.GetPositionAndRotation(out Vector3 CameraPosition, out Quaternion CameraRotation);
                    Rotation = Quaternion.LookRotation(CameraRotation * Vector3.forward, Vector3.up);
                    transform.SetPositionAndRotation(CameraPosition + VRRootOffset, Rotation);
                    break;

                case PanelGroupRootMode.PlaySpaceStable:
                    UpdateUILocationPlaySpace();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        private void UpdateUILocationPlaySpace()
        {
            CaptureStableAnchorIfNeeded();

            if (!_stableHasAnchor)
            {
                return;
            }

            BasisLocalPlayer.Instance.PlayerSelf.GetPositionAndRotation(out Vector3 playPosWS, out Quaternion playRotWS);

            // Apply playspace transform to captured playspace-local anchor
            transform.SetPositionAndRotation( playPosWS + (playRotWS * _stableLocalPos), playRotWS * _stableLocalRot);
        }

        private static float ExtractPitchDegreesNoRoll(Quaternion localRot)
        {
            // Pitch from forward.y (roll-proof).
            Vector3 fwd = localRot * Vector3.forward;
            float pitchRad = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f));
            return pitchRad * Mathf.Rad2Deg;
        }

        private void CaptureStableAnchorIfNeeded()
        {
            if (_stableHasAnchor)
            {
                return;
            }

            if (!BasisLocalCameraDriver.HasInstance)
            {
                return;
            }

            BasisLocalPlayer.Instance.PlayerSelf.GetPositionAndRotation(out Vector3 playPosWS, out Quaternion playRotWS);

            // Camera pose (head/eye)
            BasisLocalCameraDriver.GetPositionAndRotation(out Vector3 camPosWS, out Quaternion camRotWS);

            // Head rotation in playspace-local space
            Quaternion headLocal = Quaternion.Inverse(playRotWS) * camRotWS;

            float pitch = -ExtractPitchDegreesNoRoll(headLocal);

            // yaw then pitch (pitch around local X)
            Quaternion spawnLocalRotNoRoll = Quaternion.Euler(0f, headLocal.eulerAngles.y, 0f) * Quaternion.Euler(pitch, 0f, 0f);

            Quaternion spawnRotWS = playRotWS * spawnLocalRotNoRoll;

            // Place the root at the spawn pose once (then we follow playspace)
            transform.SetPositionAndRotation(camPosWS, spawnRotWS);

            // Cache playspace-local anchor
            _stableLocalPos = Quaternion.Inverse(playRotWS) * (camPosWS - playPosWS);
            _stableLocalRot = spawnLocalRotNoRoll;

            _stableHasAnchor = true;
        }
    }
}
