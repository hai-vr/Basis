using Basis.Scripts.Device_Management.Devices.Simulation;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using System.Collections.Generic;
using Basis.Scripts.Device_Management.Devices.Desktop;
public class BasisAnimationsToIK : MonoBehaviour
{
    public Animator Animator;
    private readonly List<TrackerBinding> trackerBindings = new List<TrackerBinding>();
    private class TrackerBinding
    {
        public BasisInputXRSimulate Tracker;
        public Transform Bone;
        public Vector3 LocalOffsetPosition;
        public Quaternion LocalOffsetRotation;
    }
    void OnEnable()
    {
        trackerBindings.Clear();

        BasisSimulateXR simulated = FindAnyObjectByType<BasisSimulateXR>();
        if (simulated == null || Animator == null)
        {
            Debug.LogWarning("BasisAnimationsToIK: Missing SimulateXR or Animator.");
            return;
        }

        foreach (BasisInputXRSimulate tracker in simulated.Inputs)
        {
            if (tracker == null)
            {
                continue;
            }

            if (tracker.TryGetRole(out BasisBoneTrackedRole role) && BasisAvatarDriver.TryConvertToHumanoidRole(role, out HumanBodyBones humanoidBone))
            {
                Transform bone = Animator.GetBoneTransform(humanoidBone);
                if (bone == null)
                {
                    BasisDebug.LogWarningOnce("BasisAnimationsToIK.MissingBone." + humanoidBone, $"BasisAnimationsToIK: avatar has no {humanoidBone} bone for tracker role {role}; tracker {tracker.name} not bound.", BasisDebug.LogTag.IK);
                    continue;
                }

                Transform followTransform = tracker.FollowMovement;

                bone.GetPositionAndRotation(out var bonePos, out var boneRot);

                followTransform.GetPositionAndRotation(out Vector3 trackerPos, out Quaternion trackerRot);

                Quaternion localOffsetRot = Quaternion.Inverse(boneRot) * trackerRot;

                Vector3 localOffsetPos = Quaternion.Inverse(boneRot) * (trackerPos - bonePos);

                if (humanoidBone == HumanBodyBones.LeftHand || humanoidBone == HumanBodyBones.RightHand)
                {
                    trackerBindings.Add(new TrackerBinding
                    {
                        Tracker = tracker,
                        Bone = bone,
                        LocalOffsetPosition = Vector3.zero,
                        LocalOffsetRotation = Quaternion.identity,
                    });
                }
                else
                {
                    trackerBindings.Add(new TrackerBinding
                    {
                        Tracker = tracker,
                        Bone = bone,
                        LocalOffsetPosition = localOffsetPos,
                        LocalOffsetRotation = localOffsetRot
                    });
                }

                Debug.Log($"Mapped {tracker.name} → {humanoidBone} with offset.");
            }
        }
    }
    void Update()
    {
        for (int Index = 0; Index < trackerBindings.Count; Index++)
        {
            TrackerBinding b = trackerBindings[Index];
            if (b.Tracker == null || b.Bone == null)
            {
                continue;
            }

            b.Bone.GetPositionAndRotation(out var bonePos, out var boneRot);

            Quaternion targetRot = boneRot * b.LocalOffsetRotation;
            Vector3 targetPos = bonePos + boneRot * b.LocalOffsetPosition;

            b.Tracker.FollowMovement.SetPositionAndRotation(targetPos, targetRot);
        }
        if (Animator == null)
        {
            return;
        }
        Transform Trans = Animator.GetBoneTransform(HumanBodyBones.Head);
        if (Trans == null || BasisDesktopEye.Instance == null)
        {
            BasisDebug.LogErrorOnce("BasisAnimationsToIK: missing Head bone or BasisDesktopEye.Instance; desktop eye coordinate not updated.", BasisDebug.LogTag.IK);
            return;
        }
        BasisDesktopEye.Instance.ScaledDeviceCoord.position = Trans.position;
        BasisDesktopEye.Instance.ScaledDeviceCoord.rotation = Trans.rotation;
    }
}
