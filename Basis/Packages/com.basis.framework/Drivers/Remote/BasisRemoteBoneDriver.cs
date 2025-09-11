using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
namespace Basis.Scripts.Drivers
{
    [Serializable]
    public class BasisRemoteBoneDriver
    {
        // Config / references
        public int ControlsLength;
        public BasisRemotePlayer RemotePlayer;
        public Transform RemotePlayerTransform { get; private set; }
        private BasisRemoteBoneControl Head;
        private BasisRemoteBoneControl Hips;
        private BasisRemoteBoneControl Mouth;
        [SerializeField] public BasisRemoteBoneControl[] Controls;
        [SerializeField] public BasisBoneTrackedRole[] trackedRoles;
        public bool HasControls;
        public const float DefaultGizmoSize = 0.05f;
        // Caches to avoid O(n) scans
        // role -> index and control -> index maps (populated in CreateInitialArrays/AddRange)
        Dictionary<BasisBoneTrackedRole, int> _roleToIndex;
        Dictionary<BasisRemoteBoneControl, int> _controlToIndex;
        // Scale caches
        Vector3 _lastScale = Vector3.zero;
        Vector3 _lastInitialScale = Vector3.zero;
        #region Initialization
        public BasisCalibratedCoords GetHeadPosition()
        {
            return Head.OutGoingData;
        }
        public BasisCalibratedCoords GetHipsPosition()
        {
            return Hips.OutGoingData;
        }
        public BasisCalibratedCoords GetMouthPosition()
        {
            return Mouth.OutGoingData;
        }
        public Vector3 GetMouthTposePosition()
        {
            return Mouth.TposeLocalScaled.position;
        }
        public void OnCalibration(BasisRemotePlayer remotePlayer)
        {
            // Use the incoming parameter directly; don't rely on stale RemotePlayer
            RemotePlayer = remotePlayer;

            // Cache transform for repeated use
            RemotePlayerTransform = RemotePlayer.transform;
        }

        public void CreateInitialArrays(bool isLocal)
        {
            // Reset
            trackedRoles = Array.Empty<BasisBoneTrackedRole>();
            Controls = Array.Empty<BasisRemoteBoneControl>();

            // Determine role count
            int length = isLocal
                ? Enum.GetValues(typeof(BasisBoneTrackedRole)).Length
                : 6;

            // Build arrays without LINQ/Concat
            var newControls = new BasisRemoteBoneControl[length + (isLocal ? 0 : 1)];
            var newRoles = new BasisBoneTrackedRole[length + (isLocal ? 0 : 1)];

            for (int i = 0; i < length; i++)
            {
                SetupRole(i, Color.aliceBlue, out BasisRemoteBoneControl control, out BasisBoneTrackedRole role);
                newControls[i] = control;
                newRoles[i] = role;
            }

            if (!isLocal)
            {
                // Historically index 22 has been used externally; keep behavior
                SetupRole(22, Color.blue, out BasisRemoteBoneControl extraControl, out BasisBoneTrackedRole extraRole);
                newControls[length] = extraControl;
                newRoles[length] = extraRole;
            }

            AddRange(newControls, newRoles);

            HasControls = true;
            InitializeGizmos();

            // Role maps might not exist yet (CreateInitialArrays handles normally), but guard anyway.
            EnsureMaps();

            FindBone(out Head, BasisBoneTrackedRole.Head);
            FindBone(out Hips, BasisBoneTrackedRole.Hips);
            FindBone(out Mouth, BasisBoneTrackedRole.Mouth);
            Head.HasTracked = BasisHasTracked.HasTracker;
            Hips.HasTracked = BasisHasTracked.HasTracker;
        }

        public void AddRange(BasisRemoteBoneControl[] newControls, BasisBoneTrackedRole[] newRoles)
        {
            // Allocate once and copy (avoid Concat)
            int oldLen = Controls?.Length ?? 0;
            int addLen = newControls.Length;

            var combinedControls = new BasisRemoteBoneControl[oldLen + addLen];
            var combinedRoles = new BasisBoneTrackedRole[oldLen + addLen];

            if (oldLen > 0)
            {
                Array.Copy(Controls, 0, combinedControls, 0, oldLen);
                Array.Copy(trackedRoles, 0, combinedRoles, 0, oldLen);
            }

            Array.Copy(newControls, 0, combinedControls, oldLen, addLen);
            Array.Copy(newRoles, 0, combinedRoles, oldLen, addLen);

            Controls = combinedControls;
            trackedRoles = combinedRoles;
            ControlsLength = Controls.Length;

            // Rebuild maps
            RebuildMaps();
        }

        public void SetupRole(int index, Color color, out BasisRemoteBoneControl basisBoneControl, out BasisBoneTrackedRole role)
        {
            role = (BasisBoneTrackedRole)index;

            var c = new BasisRemoteBoneControl();
            c.OutGoingData.position = Vector3.zero;
            c.OutGoingData.rotation = Quaternion.identity;
            c.HasBone = true;
            FillOutBasicInformation(c, color);

            basisBoneControl = c;
        }

        public void FillOutBasicInformation(BasisRemoteBoneControl control, Color color)
        {
            control.Color = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureMaps()
        {
            _roleToIndex ??= new Dictionary<BasisBoneTrackedRole, int>(64);
            _controlToIndex ??= new Dictionary<BasisRemoteBoneControl, int>(64);
        }

        void RebuildMaps()
        {
            EnsureMaps();
            _roleToIndex.Clear();
            _controlToIndex.Clear();

            for (int i = 0; i < ControlsLength; i++)
            {
                var role = trackedRoles[i];
                var control = Controls[i];
                _roleToIndex[role] = i;
                if (control != null)
                    _controlToIndex[control] = i;
            }
        }

        #endregion

        #region Runtime / Simulation
        public void SimulateAndApplyRemote(Vector3 nowScale)
        {
            var driver = RemotePlayer.RemoteAvatarDriver;//now will never be null.
            Vector3 initialScale = driver.AvatarInitalScale;

            // Only rescale T-pose locals if scale changed (avoid per-frame work)
            if (_lastInitialScale != initialScale || _lastScale != nowScale)
            {
                _lastInitialScale = initialScale;
                _lastScale = nowScale;

                for (int Index = 0; Index < ControlsLength; Index++)
                {
                    BasisRemoteBoneControl control = Controls[Index];
                    if (control == null) continue;

                    // Apply relative scale to T-pose local position
                    control.TposeLocalScaled.position = Vector3.Scale(control.TposeLocal.position, nowScale);
                }
            }

            RemotePlayer.OnPreSimulateBones?.Invoke();

            // Sequence devices
            for (int i = 0; i < ControlsLength; i++)
            {
                var c = Controls[i];
                if (c.hasTrackerDriver == BasisHasTracked.HasTracker)
                {
                    c.OutGoingData.rotation = c.IncomingData.rotation;
                    c.OutGoingData.position = c.IncomingData.position;
                }
                else
                {
                    if (c.HasTarget)
                    {
                        var targetRotation = c.Target.OutGoingData.rotation;
                        var targetPosition = c.Target.OutGoingData.position;

                        Vector3 offset = targetRotation * c.ScaledOffset;
                        c.OutGoingData.position = targetPosition + offset;

                        c.OutGoingData.rotation = targetRotation;
                    }
                    else
                    {
                        c.OutGoingData.rotation = c.IncomingData.rotation;
                        c.OutGoingData.position = c.IncomingData.position;
                    }
                }
            }
            if (SMModuleDebugOptions.UseGizmos)
            {
                DrawGizmos();
            }
            Vector3 rrt = RemotePlayerTransform.position;

            Common.BasisTransformMapping references = RemotePlayer.RemoteAvatarDriver.References;

            // World rotations of head and hips
            Quaternion HeadWorldRotation = references.head.rotation;
            Quaternion HipsWorldRotation = references.Hips.rotation;
            // T-pose world rotations
            Quaternion TposeHeadWorldRotation = references.TposeHead.rotation;
            Quaternion TposeHipsWorldRotation = references.TposeHips.rotation;

            // Remove T-pose influence
            Head.IncomingData.rotation = TposeHeadWorldRotation * HeadWorldRotation;
            Hips.IncomingData.rotation = TposeHipsWorldRotation * HipsWorldRotation;

            Head.IncomingData.position = references.head.position - rrt;
            Hips.IncomingData.position = references.Hips.position - rrt;
        }
        #endregion

        #region Gizmos

        public void InitializeGizmos()
        {
            BasisGizmoManager.OnUseGizmosChanged -= UpdateGizmoUsage; // prevent double-subscribe
            BasisGizmoManager.OnUseGizmosChanged += UpdateGizmoUsage;
        }

        public void DeInitializeGizmos()
        {
            BasisGizmoManager.OnUseGizmosChanged -= UpdateGizmoUsage;
        }

        public void DrawGizmos()
        {
            for (int i = 0; i < ControlsLength; i++)
            {
                var c = Controls[i];
                if (c != null) DrawGizmos(c);
            }
        }

        public void UpdateGizmoUsage(bool state)
        {
            BasisDebug.Log("Running Bone Driver Gizmos", BasisDebug.LogTag.Gizmo);

            float scale = BasisLocalPlayer.Instance != null
                ? BasisLocalPlayer.Instance.CurrentHeight.SelectedAvatarToAvatarDefaultScale
                : 1f;

            for (int i = 0; i < ControlsLength; i++)
            {
                var control = Controls[i];
                if (control == null) continue;

                var role = trackedRoles[i];

                if (state)
                {
                    if (role == BasisBoneTrackedRole.CenterEye && !Application.isEditor)
                        continue;

                    Vector3 bonePos = control.OutGoingData.position;

                    if (control.HasTarget)
                    {
                        if (BasisGizmoManager.CreateLineGizmo(role.ToString(), out control.LineDrawIndex, bonePos, control.Target.OutGoingData.position, 0.03f * scale, control.Color))
                        {
                            control.HasLineDraw = true;
                        }
                    }

                    if (BasisGizmoManager.CreateSphereGizmo(role.ToString(), out control.GizmoReference, bonePos, DefaultGizmoSize * scale, control.Color))
                    {
                        control.HasGizmo = true;
                    }
                }
                else
                {
                    control.HasGizmo = false;
                }
            }
        }

        public void DrawGizmos(BasisRemoteBoneControl control)
        {
            if (control == null || !control.HasBone) return;

            Vector3 bonePosition = control.OutGoingData.position;

            if (control.HasTarget && control.HasLineDraw)
            {
                BasisGizmoManager.UpdateLineGizmo(control.LineDrawIndex, bonePosition, control.Target.OutGoingData.position);
            }

            if (FindTrackedRole(control, out BasisBoneTrackedRole role))
            {
                if (role == BasisBoneTrackedRole.CenterEye)
                {
                    // Ignore center eye to avoid VR issues
                    return;
                }

                if (control.HasGizmo)
                {
                    if (!BasisGizmoManager.UpdateSphereGizmo(control.GizmoReference, bonePosition))
                    {
                        control.HasGizmo = false;
                    }
                }
            }

            if (BasisLocalAvatarDriver.CurrentlyTposing && FindTrackedRole(control, out BasisBoneTrackedRole role2))
            {
                if (role2 == BasisBoneTrackedRole.CenterEye)
                {
                    // Ignore center eye to avoid VR issues
                    return;
                }

                if (BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role2))
                {
                    float scale = BasisLocalPlayer.Instance != null
                        ? BasisLocalPlayer.Instance.CurrentHeight.SelectedAvatarToAvatarDefaultScale
                        : 1f;
                }
            }
        }
        #endregion

        #region Lookups

        /// <summary>
        /// O(1) role lookup without extra allocations. Returns false and null control if not found.
        /// </summary>
        public bool FindBone(out BasisRemoteBoneControl control, BasisBoneTrackedRole role)
        {
            control = null;
            if (_roleToIndex != null && _roleToIndex.TryGetValue(role, out int idx))
            {
                if ((uint)idx < (uint)ControlsLength)
                {
                    control = Controls[idx];
                    return control != null;
                }
            }
            return false;
        }

        /// <summary>
        /// O(1) control->role lookup without allocations.
        /// </summary>
        public bool FindTrackedRole(BasisRemoteBoneControl control, out BasisBoneTrackedRole role)
        {
            role = BasisBoneTrackedRole.CenterEye;

            if (control == null || _controlToIndex == null) return false;

            if (_controlToIndex.TryGetValue(control, out int idx))
            {
                if ((uint)idx < (uint)ControlsLength)
                {
                    role = trackedRoles[idx];
                    return true;
                }
            }

            return false;
        }

        #endregion
        public void CreateRotationalLock(BasisRemoteBoneControl addToBone, BasisRemoteBoneControl target)
        {
            if (addToBone == null) return;

            addToBone.Target = target;
            if (target != null)
            {
                addToBone.Offset = addToBone.TposeLocalScaled.position - target.TposeLocalScaled.position;
                addToBone.ScaledOffset = addToBone.Offset;
                addToBone.HasTarget = true;
            }
            else
            {
                addToBone.Offset = Vector3.zero;
                addToBone.ScaledOffset = Vector3.zero;
                addToBone.HasTarget = false;
            }
        }
        public void SetInitialData(Transform Transform, BasisRemoteBoneControl bone, BasisBoneTrackedRole Role, Vector3 WorldTpose)
        {
            bone.OutGoingData.position = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(Transform, WorldTpose);
            bone.TposeLocal.position = bone.OutGoingData.position;
            bone.TposeLocal.rotation = bone.OutGoingData.rotation;
            if (BasisRemoteAvatarDriver.IsApartOfSpineVertical(Role))
            {
                bone.OutGoingData.position = new Vector3(0, bone.OutGoingData.position.y, bone.OutGoingData.position.z);
                bone.TposeLocal.position = bone.OutGoingData.position;
            }
            if (Role == BasisBoneTrackedRole.Hips)
            {
                bone.TposeLocal.rotation = quaternion.identity;
            }
            bone.TposeLocalScaled.position = bone.TposeLocal.position;
            bone.TposeLocalScaled.rotation = bone.TposeLocal.rotation;
        }
        public void SetAndCreateLock(BasisBoneTrackedRole LockToBoneRole, BasisBoneTrackedRole AssignedTo)
        {
            if (FindBone(out BasisRemoteBoneControl AssignedToAddToBone, AssignedTo) == false)
            {
                BasisDebug.LogError("Cant Find Bone " + AssignedTo);
            }
            if (FindBone(out BasisRemoteBoneControl LockToBone, LockToBoneRole) == false)
            {
                BasisDebug.LogError("Cant Find Bone " + LockToBoneRole);
            }
            CreateRotationalLock(AssignedToAddToBone, LockToBone);
        }
        public void CalculateTransformPositions(BasisRemotePlayer basisPlayer)
        {
            Animator animator = basisPlayer.BasisAvatar.Animator;
            Transform rootTransform = animator.transform;
            float3 Position = rootTransform.position;
            for (int Index = 0; Index < ControlsLength; Index++)
            {
                var control = Controls[Index];
                var role = trackedRoles[Index];

                switch (trackedRoles[Index])
                {
                    case BasisBoneTrackedRole.CenterEye:
                        {
                            GetWorldSpacePos(BasisHelpers.AvatarPositionConversion(basisPlayer.BasisAvatar.AvatarEyePosition), Position, out float3 world);
                            basisPlayer.RemoteBoneDriver.SetInitialData(rootTransform, control, role, world);
                            break;
                        }

                    case BasisBoneTrackedRole.Mouth:
                        {
                            GetWorldSpacePos(BasisHelpers.AvatarPositionConversion(basisPlayer.BasisAvatar.AvatarMouthPosition), Position, out float3 world);
                            basisPlayer.RemoteBoneDriver.SetInitialData(rootTransform, control, role, world);
                            break;
                        }

                    default:
                        {
                            if (BasisDeviceManagement.Instance.FBBD.FindBone(out BasisFallBone fallback, trackedRoles[Index]))
                            {
                                if (BasisRemoteAvatarDriver.TryConvertToHumanoidRole(trackedRoles[Index], out HumanBodyBones human))
                                {
                                    GetBoneRotAndPos(basisPlayer.transform, basisPlayer.BasisAvatar, human, fallback.PositionPercentage, out quaternion _, out float3 world, out bool _);
                                    basisPlayer.RemoteBoneDriver.SetInitialData(rootTransform, control, role, world);
                                }
                                else
                                {
                                    BasisDebug.LogError("cant Convert to humanbodybone " + trackedRoles[Index]);
                                }
                            }
                            else
                            {
                                BasisDebug.LogError("cant find Fallback Bone for " + trackedRoles[Index]);
                            }

                            break;
                        }
                }
            }
            SetAndCreateLock(BasisBoneTrackedRole.Head, BasisBoneTrackedRole.Neck);
            SetAndCreateLock(BasisBoneTrackedRole.Head, BasisBoneTrackedRole.CenterEye);
            SetAndCreateLock(BasisBoneTrackedRole.Head, BasisBoneTrackedRole.Mouth);
            SetAndCreateLock(BasisBoneTrackedRole.Neck, BasisBoneTrackedRole.Chest);
            SetAndCreateLock(BasisBoneTrackedRole.Chest, BasisBoneTrackedRole.Spine);
            SetAndCreateLock(BasisBoneTrackedRole.Spine, BasisBoneTrackedRole.Hips);
        }
        public void GetWorldSpacePos(Vector3 localAvatarSpace, Vector3 AnimatorPosition, out float3 position)
        {
            position = BasisHelpers.ConvertFromLocalSpace(localAvatarSpace, AnimatorPosition);
        }
        public void GetBoneRotAndPos(Transform driver, BasisAvatar BasisAvatar, HumanBodyBones bone, Vector3 heightPercentage, out quaternion Rotation, out float3 Position, out bool UsedFallback)
        {
            if (BasisAvatar.Animator.avatar != null && BasisAvatar.Animator.avatar.isHuman)
            {
                Transform boneTransform = BasisAvatar.Animator.GetBoneTransform(bone);
                if (boneTransform == null)
                {
                    Rotation = driver.rotation;
                    Position = driver.position;
                    Position += CalculateFallbackOffset(bone, ActiveAvatarEyeHeight(BasisAvatar), heightPercentage);
                    UsedFallback = true;
                }
                else
                {
                    UsedFallback = false;
                    boneTransform.GetPositionAndRotation(out Vector3 VPosition, out Quaternion QRotation);
                    Position = VPosition;
                    Rotation = QRotation;
                }
            }
            else
            {
                Rotation = driver.rotation;
                Position = driver.position;
                Position = new Vector3(0, Position.y, 0);
                Position += CalculateFallbackOffset(bone, ActiveAvatarEyeHeight(BasisAvatar), heightPercentage);
                Position = new Vector3(0, Position.y, 0);
                UsedFallback = true;
            }
        }
        public float ActiveAvatarEyeHeight(BasisAvatar BasisAvatar)
        {
            if (BasisAvatar != null)
            {
                return BasisAvatar.AvatarEyePosition.x;
            }
            else
            {
                return BasisLocalPlayer.FallbackSize;
            }
        }
        public float3 CalculateFallbackOffset(HumanBodyBones bone, float fallbackHeight, float3 heightPercentage)
        {
            Vector3 height = fallbackHeight * heightPercentage;
            return bone == HumanBodyBones.Hips ? math.mul(height, -Vector3.up) : math.mul(height, Vector3.up);
        }
        [System.Serializable]
        public class BasisRemoteBoneControl
        {
            [NonSerialized]
            public BasisRemoteBoneControl Target;
            public bool HasLineDraw;
            public int LineDrawIndex;
            public bool HasTarget = false;
            public float3 Offset;
            public float3 ScaledOffset;
            [SerializeField]
            public BasisCalibratedCoords OutGoingData = new BasisCalibratedCoords();
            [SerializeField]
            public BasisCalibratedCoords TposeLocal = new BasisCalibratedCoords();
            //the scaled tpose is tpose * the avatar height change
            [SerializeField]
            public BasisCalibratedCoords TposeLocalScaled = new BasisCalibratedCoords();
            [SerializeField]
            public BasisCalibratedCoords IncomingData = new BasisCalibratedCoords();
            public int GizmoReference = -1;
            public bool HasGizmo = false;
            [SerializeField]
            [HideInInspector]
            private Color gizmoColor = Color.blue;
            // Backing fields for the properties
            [SerializeField]
            public BasisHasTracked hasTrackerDriver = BasisHasTracked.HasNoTracker;
            // Properties with get/set accessors
            public BasisHasTracked HasTracked
            {
                get => hasTrackerDriver;
                set
                {
                    hasTrackerDriver = value;
                }
            }
            public Color Color { get => gizmoColor; set => gizmoColor = value; }
            public bool HasBone { get; internal set; }
        }

    }
}
