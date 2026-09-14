using System;
using System.Collections.Generic;
using System.Text;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Device_Management
{
    public static class BasisDeviceOffsetEditor
    {
        public const int TickPriority = 200;
        public const float GrabRadius = 0.15f;
        private const float HandleSize = 0.045f;
        private const float PhysicalSize = 0.02f;
        private const float AxisLength = 0.08f;
        private const float LineWidth = 0.004f;
        private const float ThinLineWidth = 0.002f;
        private const float LabelScale = 0.012f;
        private static readonly Color IdleColor = new Color(0.55f, 0.8f, 1f, 1f);
        private static readonly Color HoverColor = new Color(1f, 0.85f, 0.35f, 1f);
        private static readonly Color HeldColor = new Color(0.4f, 1f, 0.55f, 1f);
        private static readonly Color PhysicalColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color AxisXColor = new Color(1f, 0.35f, 0.35f, 1f);
        private static readonly Color AxisYColor = new Color(0.45f, 1f, 0.45f, 1f);
        private static readonly Color AxisZColor = new Color(0.45f, 0.6f, 1f, 1f);
        private static readonly BasisGizmoSet gizmos = new BasisGizmoSet("DeviceOffsetHandles");
        private static readonly List<BasisInput> targets = new List<BasisInput>();
        private static readonly List<BasisInput> hands = new List<BasisInput>();
        private static readonly List<BasisInput> staleHands = new List<BasisInput>();
        private static readonly Dictionary<BasisInput, bool> gripLatch = new Dictionary<BasisInput, bool>();
        private static readonly Dictionary<BasisInput, BasisInput> hoverByHand = new Dictionary<BasisInput, BasisInput>();
        private static readonly BasisInput[] grabbers = new BasisInput[2];
        private static string[] roleLabels;
        private static int grabberCount;
        private static int anchorMode;
        private static BasisInput anchorLead;
        private static Vector3 anchorPosition;
        private static Quaternion anchorRotation = Quaternion.identity;
        private static Vector3 frameUp = Vector3.up;
        private static bool registered;
        private static bool hooked;

        public static event Action OnStateChanged;
        public static bool IsEditing { get; private set; }
        public static string SelectedKey { get; private set; }
        public static string TargetKey { get; private set; }
        public static BasisInput Target { get; private set; }
        public static bool IsGrabbing => grabberCount > 0;

        public static void Select(string key)
        {
            if (SelectedKey == key)
            {
                return;
            }
            SelectedKey = key;
            OnStateChanged?.Invoke();
        }

        public static void SetEditing(bool editing)
        {
            if (IsEditing == editing)
            {
                return;
            }
            IsEditing = editing;
            if (editing)
            {
                if (!registered)
                {
                    BasisLocalPlayer.AfterSimulateOnLate.AddAction(TickPriority, Tick);
                    registered = true;
                }
                EnsureMasterHook();
            }
            else
            {
                EndGrab();
                if (registered)
                {
                    BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(TickPriority, Tick);
                    registered = false;
                }
                gizmos.Clear();
                gripLatch.Clear();
                hoverByHand.Clear();
            }
            OnStateChanged?.Invoke();
        }

        public static bool IsCapturing(BasisInput input)
        {
            if (grabberCount == 0 || input == null)
            {
                return false;
            }
            for (int index = 0; index < grabberCount; index++)
            {
                if (ReferenceEquals(grabbers[index], input))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasGrabbingHand()
        {
            BasisDeviceManagement management = BasisDeviceManagement.Instance;
            if (management == null)
            {
                return false;
            }
            var devices = management.AllInputDevices;
            int count = devices.Count;
            for (int index = 0; index < count; index++)
            {
                if (IsHand(devices[index]))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsTargetable(BasisInput input)
        {
            return input != null && input.HasEvents && !string.IsNullOrEmpty(input.DeviceOffsetKey) && input.TryGetRole(out _) && !(input is BasisInputController && input.TrackingHardware == BasisTrackingHardware.Optical);
        }

        public static string RoleLabel(BasisBoneTrackedRole role)
        {
            if (roleLabels == null)
            {
                BasisBoneTrackedRole[] roles = (BasisBoneTrackedRole[])Enum.GetValues(typeof(BasisBoneTrackedRole));
                int highest = 0;
                for (int index = 0; index < roles.Length; index++)
                {
                    highest = Mathf.Max(highest, (int)roles[index]);
                }
                roleLabels = new string[highest + 1];
                for (int index = 0; index < roles.Length; index++)
                {
                    roleLabels[(int)roles[index]] = SplitWords(roles[index].ToString());
                }
            }
            int slot = (int)role;
            return slot >= 0 && slot < roleLabels.Length && roleLabels[slot] != null ? roleLabels[slot] : role.ToString();
        }

        private static bool IsHand(BasisInput input)
        {
            return input != null && input.HasEvents && input.TryGetRole(out BasisBoneTrackedRole role) && (role == BasisBoneTrackedRole.LeftHand || role == BasisBoneTrackedRole.RightHand);
        }

        private static void Tick()
        {
            BasisLocalPlayer player = BasisLocalPlayer.Instance;
            BasisDeviceManagement management = BasisDeviceManagement.Instance;
            if (!IsEditing || player == null || management == null || !BasisLocalPlayer.PlayerReady)
            {
                return;
            }
            Collect(management);
            ReleaseGrabbers();
            HandlePresses();
            if (grabberCount > 0)
            {
                Drive();
            }
            Draw(player);
        }

        private static void Collect(BasisDeviceManagement management)
        {
            targets.Clear();
            hands.Clear();
            var devices = management.AllInputDevices;
            int count = devices.Count;
            for (int index = 0; index < count; index++)
            {
                BasisInput input = devices[index];
                if (IsHand(input))
                {
                    hands.Add(input);
                }
                if (IsTargetable(input))
                {
                    targets.Add(input);
                }
            }
        }

        private static void ReleaseGrabbers()
        {
            if (grabberCount == 0)
            {
                return;
            }
            if (Target == null || !targets.Contains(Target) || Target.DeviceOffsetKey != TargetKey)
            {
                EndGrab();
                return;
            }
            int kept = 0;
            for (int index = 0; index < grabberCount; index++)
            {
                BasisInput grabber = grabbers[index];
                if (grabber != null && hands.Contains(grabber) && grabber.CurrentInputState.GripButton)
                {
                    grabbers[kept++] = grabber;
                }
                else if (grabber != null)
                {
                    grabber.PlayHaptic(0.03f, 0.3f);
                }
            }
            for (int index = kept; index < grabbers.Length; index++)
            {
                grabbers[index] = null;
            }
            grabberCount = kept;
            if (kept == 0)
            {
                EndGrab();
            }
        }

        private static void HandlePresses()
        {
            int count = hands.Count;
            for (int index = 0; index < count; index++)
            {
                BasisInput hand = hands[index];
                bool down = hand.CurrentInputState.GripButton;
                bool pressed = gripLatch.TryGetValue(hand, out bool wasDown) && down && !wasDown;
                gripLatch[hand] = down;
                if (IsCapturing(hand))
                {
                    UpdateHover(hand, null);
                    continue;
                }
                BasisInput hovered = FindNearestTarget(hand);
                UpdateHover(hand, hovered);
                if (!pressed || hovered == null || BasisLocalPlayspaceMover.IsHandHoldingObject(hand))
                {
                    continue;
                }
                if (grabberCount == 0)
                {
                    BeginGrab(hovered, hand);
                }
                else if (grabberCount == 1 && ReferenceEquals(hovered, Target))
                {
                    grabbers[grabberCount++] = hand;
                    hand.PlayHaptic(0.05f, 0.5f);
                }
            }
            PruneLatches();
        }

        private static BasisInput FindNearestTarget(BasisInput hand)
        {
            hand.GetPhysicalUnscaledPose(out Vector3 handPosition, out _);
            BasisInput nearest = null;
            float best = GrabRadius * GrabRadius;
            int count = targets.Count;
            for (int index = 0; index < count; index++)
            {
                BasisInput target = targets[index];
                if (ReferenceEquals(target, hand))
                {
                    continue;
                }
                target.GetPhysicalUnscaledPose(out Vector3 physicalPosition, out Quaternion physicalRotation);
                float distance = (physicalPosition + (physicalRotation * target.DeviceOffsetPosition) - handPosition).sqrMagnitude;
                if (target is BasisInputController controller)
                {
                    distance = Mathf.Min(distance, (controller.UnscaledHandTarget - handPosition).sqrMagnitude);
                }
                if (distance < best)
                {
                    best = distance;
                    nearest = target;
                }
            }
            return nearest;
        }

        private static void UpdateHover(BasisInput hand, BasisInput hovered)
        {
            hoverByHand.TryGetValue(hand, out BasisInput previous);
            if (ReferenceEquals(previous, hovered))
            {
                return;
            }
            hoverByHand[hand] = hovered;
            if (hovered != null)
            {
                hand.PlayHaptic(0.02f, 0.15f);
            }
        }

        private static bool IsHovered(BasisInput target)
        {
            foreach (KeyValuePair<BasisInput, BasisInput> pair in hoverByHand)
            {
                if (ReferenceEquals(pair.Value, target))
                {
                    return true;
                }
            }
            return false;
        }

        private static void BeginGrab(BasisInput target, BasisInput hand)
        {
            Target = target;
            TargetKey = target.DeviceOffsetKey;
            grabbers[0] = hand;
            grabberCount = 1;
            anchorMode = 0;
            anchorLead = null;
            SelectedKey = TargetKey;
            hand.PlayHaptic(0.06f, 0.6f);
            OnStateChanged?.Invoke();
        }

        private static void EndGrab()
        {
            if (TargetKey == null)
            {
                return;
            }
            string key = TargetKey;
            grabbers[0] = null;
            grabbers[1] = null;
            grabberCount = 0;
            anchorMode = 0;
            anchorLead = null;
            Target = null;
            TargetKey = null;
            BasisDeviceOffsets.Persist(key);
            OnStateChanged?.Invoke();
        }

        private static void Drive()
        {
            GetManipulationFrame(out Vector3 framePosition, out Quaternion frameRotation, out int mode);
            Target.GetPhysicalUnscaledPose(out Vector3 physicalPosition, out Quaternion physicalRotation);
            if (mode != anchorMode || !ReferenceEquals(anchorLead, grabbers[0]))
            {
                BasisDeviceOffsetMath.Compose(physicalPosition, physicalRotation, Target.DeviceOffsetPosition, Target.DeviceOffsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
                BasisDeviceOffsetMath.Relative(framePosition, frameRotation, virtualPosition, virtualRotation, out anchorPosition, out anchorRotation);
                anchorMode = mode;
                anchorLead = grabbers[0];
                return;
            }
            BasisDeviceOffsetMath.Compose(framePosition, frameRotation, anchorPosition, anchorRotation, out Vector3 heldPosition, out Quaternion heldRotation);
            BasisDeviceOffsetMath.SolveOffset(physicalPosition, physicalRotation, heldPosition, heldRotation, out Vector3 offsetPosition, out Quaternion offsetRotation);
            BasisDeviceOffsets.Set(TargetKey, offsetPosition, offsetRotation, false, null);
        }

        private static void GetManipulationFrame(out Vector3 position, out Quaternion rotation, out int mode)
        {
            grabbers[0].GetPhysicalUnscaledPose(out position, out rotation);
            mode = 1;
            if (grabberCount < 2)
            {
                return;
            }
            grabbers[1].GetPhysicalUnscaledPose(out Vector3 secondPosition, out Quaternion secondRotation);
            if (BasisDeviceOffsetMath.TryBuildTwoHandFrame(position, rotation, secondPosition, secondRotation, frameUp, out Vector3 framePosition, out Quaternion frameRotation))
            {
                position = framePosition;
                rotation = frameRotation;
                frameUp = frameRotation * Vector3.up;
                mode = 2;
            }
        }

        private static void Draw(BasisLocalPlayer player)
        {
            Transform root = player.transform;
            Matrix4x4 rootMatrix = root.localToWorldMatrix;
            Quaternion rootRotation = root.rotation;
            float deviceScale = Sanitize(BasisHeightDriver.DeviceScale);
            float nodeScale = Sanitize(BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale);
            float handleSize = HandleSize * nodeScale;
            float axisLength = AxisLength * nodeScale;
            Vector3 viewer = BasisLocalCameraDriver.Position;
            gizmos.Begin();
            int count = targets.Count;
            for (int index = 0; index < count; index++)
            {
                BasisInput target = targets[index];
                target.GetPhysicalUnscaledPose(out Vector3 physicalPosition, out Quaternion physicalRotation);
                BasisDeviceOffsetMath.Compose(physicalPosition, physicalRotation, target.DeviceOffsetPosition, target.DeviceOffsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
                ToWorld(rootMatrix, rootRotation, deviceScale, virtualPosition, virtualRotation, out Vector3 handle, out Quaternion handleRotation);
                Color color = ReferenceEquals(target, Target) ? HeldColor : IsHovered(target) ? HoverColor : IdleColor;
                gizmos.Sphere(handle, handleSize, color);
                gizmos.Line(handle, handle + (handleRotation * (Vector3.right * axisLength)), AxisXColor, LineWidth);
                gizmos.Line(handle, handle + (handleRotation * (Vector3.up * axisLength)), AxisYColor, LineWidth);
                gizmos.Line(handle, handle + (handleRotation * (Vector3.forward * axisLength)), AxisZColor, LineWidth);
                if (target.HasDeviceOffset)
                {
                    ToWorld(rootMatrix, rootRotation, deviceScale, physicalPosition, physicalRotation, out Vector3 physical, out _);
                    gizmos.Line(physical, handle, PhysicalColor, ThinLineWidth);
                    gizmos.Sphere(physical, PhysicalSize * nodeScale, PhysicalColor);
                }
                if (target.TryGetRole(out BasisBoneTrackedRole role))
                {
                    gizmos.Label(handle + (Vector3.up * (handleSize * 1.5f)), RoleLabel(role), color, viewer, LabelScale * nodeScale);
                }
            }
            gizmos.End();
        }

        private static void ToWorld(Matrix4x4 rootMatrix, Quaternion rootRotation, float deviceScale, Vector3 unscaledPosition, Quaternion unscaledRotation, out Vector3 position, out Quaternion rotation)
        {
            Vector3 local = BasisInput.OffsetCoords.position + (BasisInput.OffsetCoords.rotation * (unscaledPosition * deviceScale));
            Quaternion localRotation = BasisInput.OffsetCoords.rotation * unscaledRotation;
            BasisLocalPlayspaceMover.ApplyFlipToLocalPose(ref local, ref localRotation);
            position = rootMatrix.MultiplyPoint3x4(local);
            rotation = rootRotation * localRotation;
        }

        private static float Sanitize(float scale)
        {
            return scale > 1e-4f && !float.IsNaN(scale) && !float.IsInfinity(scale) ? scale : 1f;
        }

        private static void PruneLatches()
        {
            if (gripLatch.Count <= hands.Count && hoverByHand.Count <= hands.Count)
            {
                return;
            }
            staleHands.Clear();
            foreach (BasisInput hand in gripLatch.Keys)
            {
                if (!hands.Contains(hand))
                {
                    staleHands.Add(hand);
                }
            }
            foreach (BasisInput hand in hoverByHand.Keys)
            {
                if (!hands.Contains(hand) && !staleHands.Contains(hand))
                {
                    staleHands.Add(hand);
                }
            }
            for (int index = 0; index < staleHands.Count; index++)
            {
                gripLatch.Remove(staleHands[index]);
                hoverByHand.Remove(staleHands[index]);
            }
            staleHands.Clear();
        }

        private static string SplitWords(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length + 4);
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (index > 0 && char.IsUpper(character) && char.IsLower(text[index - 1]))
                {
                    builder.Append(' ');
                }
                builder.Append(character);
            }
            return builder.ToString();
        }

        private static void EnsureMasterHook()
        {
            if (hooked)
            {
                return;
            }
            BasisGizmoManager.OnUseGizmosChanged += OnMasterGizmoToggle;
            hooked = true;
        }

        private static void OnMasterGizmoToggle(bool state)
        {
            if (!state)
            {
                gizmos.Forget();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (registered)
            {
                BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(TickPriority, Tick);
                registered = false;
            }
            IsEditing = false;
            SelectedKey = null;
            Target = null;
            TargetKey = null;
            grabbers[0] = null;
            grabbers[1] = null;
            grabberCount = 0;
            anchorMode = 0;
            anchorLead = null;
            gripLatch.Clear();
            hoverByHand.Clear();
            gizmos.Forget();
            OnStateChanged = null;
        }
    }
}
