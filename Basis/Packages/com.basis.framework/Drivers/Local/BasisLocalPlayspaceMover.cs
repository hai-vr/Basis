using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Lets a VR user grab and drag (and optionally rotate/scale) their play space by holding a
    /// configurable controller input, the way naelstrof's VRPlayspaceMover does. Gated behind the
    /// Body Tracking "Playspace Mover" toggle and driven each frame after character movement.
    ///
    /// The drag is applied incrementally through the character controller, so it composes with
    /// normal locomotion instead of blocking it. One held hand drags (translation). Two hands holding
    /// the main input scale (drives the custom avatar scale, not the play space); two hands holding the
    /// rotation input rotate (yaw). The net drag is tracked so <see cref="ResetOffset"/> can undo it
    /// while leaving normal locomotion intact.
    /// </summary>
    public static class BasisLocalPlayspaceMover
    {
        public const string InputGrip = "Grip";
        public const string InputTrigger = "Trigger";
        public const string InputPrimary = "Primary";
        public const string InputSecondary = "Secondary";

        public const string HandBoth = "Both";
        public const string HandLeft = "Left";
        public const string HandRight = "Right";

        private const float MinHeight = 0.1f;
        private const float MaxHeight = 5f;
        private const float TriggerThreshold = 0.5f;

        private static bool _grabbing;
        private static bool _capLeft;
        private static bool _capRight;
        private static Vector3 _prevLeftLocal;
        private static Vector3 _prevRightLocal;

        private static bool _scaling;
        private static Vector3 _grabLeftUnscaled;
        private static Vector3 _grabRightUnscaled;
        private static float _grabBaseHeight;

        private static bool _scaleDirty;
        private static float _pendingScaleHeight;

        // Net translation the mover has applied, so it can be undone on demand.
        private static Vector3 _offsetPos;

        private static BasisLocks.LockContext _movementLock;
        private static bool _hasMovementLock;

        /// <summary>Total play-space drag currently applied (world units).</summary>
        public static Vector3 CurrentOffset => _offsetPos;

        public static void Simulate(BasisLocalPlayer player, float deltaTime)
        {
            if (player == null || BasisLocalPlayer.PlayerReady == false
                || BasisSettingsDefaults.EnablePlayspaceMover.RawValue == false
                || BasisDeviceManagement.IsCurrentModeVR() == false
                || player.LocalSeatDriver.IsSeated)
            {
                Stop();
                return;
            }

            if (_hasMovementLock == false)
            {
                _movementLock = BasisLocks.GetContext(BasisLocks.Movement);
                _hasMovementLock = true;
            }
            if (_movementLock)
            {
                Stop();
                return;
            }

            string handMode = BasisSettingsDefaults.PlayspaceMoverHand.RawValue;
            string mainInput = BasisSettingsDefaults.PlayspaceMoverInput.RawValue;
            string rotateInput = BasisSettingsDefaults.PlayspaceMoverRotateInput.RawValue;
            bool allowLeft = handMode != HandRight;
            bool allowRight = handMode != HandLeft;

            GatherHand(BasisBoneTrackedRole.LeftHand, mainInput, rotateInput, out bool leftPresent, out bool leftMain, out bool leftRotate, out Vector3 leftLocal, out Vector3 leftUnscaled);
            GatherHand(BasisBoneTrackedRole.RightHand, mainInput, rotateInput, out bool rightPresent, out bool rightMain, out bool rightRotate, out Vector3 rightLocal, out Vector3 rightUnscaled);

            bool left = leftPresent && (leftMain || leftRotate) && allowLeft;
            bool right = rightPresent && (rightMain || rightRotate) && allowRight;
            int count = (left ? 1 : 0) + (right ? 1 : 0);

            if (count == 0)
            {
                Stop();
                return;
            }

            if (_grabbing == false || left != _capLeft || right != _capRight)
            {
                Capture(left, right, leftLocal, rightLocal);
            }

            bool allowRotate = BasisSettingsDefaults.PlayspaceMoverRotate.RawValue;
            bool allowScale = BasisSettingsDefaults.PlayspaceMoverScale.RawValue;

            player.transform.GetPositionAndRotation(out Vector3 pcur, out Quaternion qcur);

            Vector3 newPos;
            Quaternion newRot;

            if (count == 1)
            {
                _scaling = false;
                CommitScaleIfPending();
                Vector3 handNow = left ? leftLocal : rightLocal;
                Vector3 handPrev = left ? _prevLeftLocal : _prevRightLocal;
                newPos = pcur + (qcur * (handPrev - handNow));
                newRot = qcur;
            }
            else
            {
                bool doRotate = allowRotate && leftRotate && rightRotate;
                bool doScale = allowScale && leftMain && rightMain && doRotate == false;

                Vector3 lNow = pcur + (qcur * leftLocal);
                Vector3 rNow = pcur + (qcur * rightLocal);
                Vector3 lPrev = pcur + (qcur * _prevLeftLocal);
                Vector3 rPrev = pcur + (qcur * _prevRightLocal);
                Vector3 midNow = (lNow + rNow) * 0.5f;
                Vector3 midPrev = (lPrev + rPrev) * 0.5f;

                Quaternion yawM = Quaternion.identity;
                if (doRotate)
                {
                    Vector3 aFlat = new Vector3(lNow.x - rNow.x, 0f, lNow.z - rNow.z);
                    Vector3 bFlat = new Vector3(lPrev.x - rPrev.x, 0f, lPrev.z - rPrev.z);
                    if (aFlat.sqrMagnitude > 1e-6f && bFlat.sqrMagnitude > 1e-6f)
                    {
                        yawM = Quaternion.FromToRotation(aFlat.normalized, bFlat.normalized);
                    }
                }

                newPos = (yawM * (pcur - midNow)) + midPrev;
                newRot = yawM * qcur;

                if (doScale)
                {
                    if (_scaling == false)
                    {
                        CaptureScaleBaseline(leftUnscaled, rightUnscaled);
                        _scaling = true;
                    }
                    ApplyScaleGesture(leftUnscaled, rightUnscaled);
                }
                else
                {
                    _scaling = false;
                    CommitScaleIfPending();
                }
            }

            Apply(player, pcur, newPos, newRot);
            _prevLeftLocal = leftLocal;
            _prevRightLocal = rightLocal;
        }

        /// <summary>
        /// Undoes the net play-space drag (returns to where the user was before dragging),
        /// leaving custom scale and normal locomotion untouched.
        /// </summary>
        public static void ResetOffset()
        {
            var player = BasisLocalPlayer.Instance;
            if (player != null && _offsetPos.sqrMagnitude > 1e-8f)
            {
                player.transform.GetPositionAndRotation(out Vector3 p, out Quaternion r);
                player.Teleport(p - _offsetPos, r);
            }
            _offsetPos = Vector3.zero;
        }

        private static void ApplyScaleGesture(Vector3 leftUnscaled, Vector3 rightUnscaled)
        {
            // Measure from the unscaled (pre-player-scaling) device poses so changing the scale
            // live does not feed back into the hand distance and run the scale away.
            float grabDist = (_grabLeftUnscaled - _grabRightUnscaled).magnitude;
            float curDist = (leftUnscaled - rightUnscaled).magnitude;
            if (grabDist < 1e-4f || curDist < 1e-4f) return;

            float target = Mathf.Clamp(_grabBaseHeight * (curDist / grabDist), MinHeight, MaxHeight);

            // Drive the full height/scale recompute live (same path the avatar-scale slider uses) so
            // the avatar, camera eye height, and scaled controller positions all update immediately.
            SMModuleCalibration.ApplyCustomScale = true;
            SMModuleCalibration.SelectedScale = target;
            BasisHeightDriver.ApplyScaleAndHeight();

            _pendingScaleHeight = target;
            _scaleDirty = true;
        }

        private static void CommitScaleIfPending()
        {
            if (_scaleDirty == false) return;
            _scaleDirty = false;
            BasisSettingsDefaults.CustomScale.SetValue(true);
            BasisSettingsDefaults.SelectedScale.SetValue(_pendingScaleHeight);
        }

        private static void Stop()
        {
            _scaling = false;
            CommitScaleIfPending();
            _grabbing = false;
        }

        private static void Capture(bool left, bool right, Vector3 leftLocal, Vector3 rightLocal)
        {
            _grabbing = true;
            _capLeft = left;
            _capRight = right;
            _prevLeftLocal = leftLocal;
            _prevRightLocal = rightLocal;
        }

        private static void CaptureScaleBaseline(Vector3 leftUnscaled, Vector3 rightUnscaled)
        {
            _grabLeftUnscaled = leftUnscaled;
            _grabRightUnscaled = rightUnscaled;
            _grabBaseHeight = BasisSettingsDefaults.CustomScale.RawValue
                ? BasisSettingsDefaults.SelectedScale.RawValue
                : BasisHeightDriver.SelectedUnScaledAvatarHeight;
            if (_grabBaseHeight < 1e-3f) _grabBaseHeight = BasisHeightDriver.FallbackHeightInMeters;
        }

        private static void Apply(BasisLocalPlayer player, Vector3 pcur, Vector3 newPos, Quaternion newRot)
        {
            Transform t = player.transform;
            var driver = player.LocalCharacterDriver;
            Vector3 delta = newPos - pcur;

            // Keep vertical position under normal gravity/grounding — the play-space drag is horizontal,
            // so the character controller still resolves floor height, steps, and falls while dragging.
            delta.y = 0f;

            // Move through the character controller so the drag composes with normal locomotion
            // (both call Move this frame) and the controller keeps its internal position in sync.
            if (driver.characterController != null && driver.characterController.enabled)
            {
                driver.characterController.Move(delta);
                t.rotation = newRot;
            }
            else
            {
                t.SetPositionAndRotation(newPos, newRot);
            }

            t.GetPositionAndRotation(out Vector3 finalPos, out Quaternion finalRot);
            BasisLocalPlayer.localToWorldMatrix = Matrix4x4.TRS(finalPos, finalRot, t.lossyScale);
            driver.CurrentPosition = finalPos;
            driver.CurrentRotation = finalRot;

            _offsetPos += finalPos - pcur;
        }

        private static void GatherHand(BasisBoneTrackedRole role, string mainInput, string rotateInput, out bool present, out bool mainHeld, out bool rotateHeld, out Vector3 local, out Vector3 unscaled)
        {
            present = false;
            mainHeld = false;
            rotateHeld = false;
            local = Vector3.zero;
            unscaled = Vector3.zero;

            if (BasisDeviceManagement.Instance == null) return;
            var devices = BasisDeviceManagement.Instance.AllInputDevices;
            if (devices == null) return;

            foreach (BasisInput device in devices)
            {
                if (device == null || device.HasControl == false) continue;
                if (device.TryGetRole(out BasisBoneTrackedRole r) == false || r != role) continue;

                present = true;
                // Grip is also the pickup input — while this hand is holding an interactable,
                // don't let it drive the mover until the object is released.
                if (IsHandHoldingObject(device))
                {
                    mainHeld = false;
                    rotateHeld = false;
                }
                else
                {
                    mainHeld = IsHeld(device.CurrentInputState, mainInput);
                    rotateHeld = IsHeld(device.CurrentInputState, rotateInput);
                }
                local = device.Control.OutGoingData.position;
                unscaled = device.UnscaledDeviceCoord.position;
                return;
            }
        }

        private static bool IsHandHoldingObject(BasisInput device)
        {
            var interact = BasisPlayerInteract.Instance;
            if (interact == null) return false;
            var inputs = interact.InteractInputs;
            if (inputs == null) return false;

            for (int i = 0; i < inputs.Length; i++)
            {
                BasisInteractInput ii = inputs[i];
                if (ii.input == null || ii.lastTarget == null) continue;
                if (ii.IsInput(device) && ii.lastTarget.IsInteractingWith(device))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsHeld(BasisInputState state, string inputMode)
        {
            switch (inputMode)
            {
                case InputTrigger: return state.Trigger >= TriggerThreshold;
                case InputPrimary: return state.PrimaryButtonGetState;
                case InputSecondary: return state.SecondaryButtonGetState;
                default: return state.GripButton;
            }
        }
    }
}
