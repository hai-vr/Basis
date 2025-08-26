using Unity.Collections;
using UnityEngine;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Minimal constraint that ONLY drives the hips and head transforms (position + rotation).
    /// No spine chain, no distance buffers, just endpoints.
    /// </summary>
    [System.Serializable]
    public struct BasisHipsHeadIKConstraintData : IAnimationJobData, BasisIHipsHeadIKConstraintData
    {
        [SerializeField] Transform m_Hips;
        [SerializeField] Transform m_Head;

        [SyncSceneToStream, SerializeField] public Vector3 hipsTargetPosition;
        [SyncSceneToStream, SerializeField] public Vector3 hipsTargetRotationEuler; // degrees

        [SyncSceneToStream, SerializeField] public Vector3 headTargetPosition;
        [SyncSceneToStream, SerializeField] public Vector3 headTargetRotationEuler; // degrees

        public Transform hips { get => m_Hips; set => m_Hips = value; }
        public Transform head { get => m_Head; set => m_Head = value; }

        // Property name bindings so the binder can hook Vector3Properties.
        string BasisIHipsHeadIKConstraintData.hipsTargetPositionVector3Property
            => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(hipsTargetPosition));
        string BasisIHipsHeadIKConstraintData.hipsTargetRotationVector3Property
            => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(hipsTargetRotationEuler));

        string BasisIHipsHeadIKConstraintData.headTargetPositionVector3Property
            => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(headTargetPosition));
        string BasisIHipsHeadIKConstraintData.headTargetRotationVector3Property
            => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(headTargetRotationEuler));

        // IAnimationJobData
        bool IAnimationJobData.IsValid() => m_Hips != null && m_Head != null;

        void IAnimationJobData.SetDefaultValues()
        {
            m_Hips = null;
            m_Head = null;

            hipsTargetPosition = Vector3.zero;
            hipsTargetRotationEuler = Vector3.zero;

            headTargetPosition = Vector3.zero;
            headTargetRotationEuler = Vector3.zero;
        }
    }

    public interface BasisIHipsHeadIKConstraintData
    {
        Transform hips { get; }
        Transform head { get; }

        string hipsTargetPositionVector3Property { get; }
        string hipsTargetRotationVector3Property { get; }

        string headTargetPositionVector3Property { get; }
        string headTargetRotationVector3Property { get; }
    }

    [Unity.Burst.BurstCompile]
    public struct BasisHipsHeadIKConstraintJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle hips;
        public ReadWriteTransformHandle head;

        public Vector3Property hipsTargetPosition;
        public Vector3Property hipsTargetRotationEuler;

        public Vector3Property headTargetPosition;
        public Vector3Property headTargetRotationEuler;

        public FloatProperty jobWeight { get; set; }

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float w = jobWeight.Get(stream);
            if (w <= 0f)
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, hips);
                BasisAnimationRuntimeUtils.PassThrough(stream, head);
                return;
            }

            // Read targets
            Vector3 hipsPos = hipsTargetPosition.Get(stream);
            Vector3 hipsRotEuler = hipsTargetRotationEuler.Get(stream);
            Quaternion hipsRot = Quaternion.Euler(hipsRotEuler);

            Vector3 headPos = headTargetPosition.Get(stream);
            Vector3 headRotEuler = headTargetRotationEuler.Get(stream);
            Quaternion headRot = Quaternion.Euler(headRotEuler);

            // Apply directly
            hips.SetPosition(stream, hipsPos);
            hips.SetRotation(stream, hipsRot);

            head.SetPosition(stream, headPos);
            head.SetRotation(stream, headRot);
        }
    }

    public class BasisHipsHeadIKConstraintJobBinder<T>
        : AnimationJobBinder<BasisHipsHeadIKConstraintJob, T>
        where T : struct, IAnimationJobData, BasisIHipsHeadIKConstraintData
    {
        public override BasisHipsHeadIKConstraintJob Create(Animator animator, ref T data, Component component)
        {
            var job = new BasisHipsHeadIKConstraintJob
            {
                hips = ReadWriteTransformHandle.Bind(animator, data.hips),
                head = ReadWriteTransformHandle.Bind(animator, data.head),

                hipsTargetPosition = Vector3Property.Bind(
                    animator, component, data.hipsTargetPositionVector3Property),
                hipsTargetRotationEuler = Vector3Property.Bind(
                    animator, component, data.hipsTargetRotationVector3Property),

                headTargetPosition = Vector3Property.Bind(
                    animator, component, data.headTargetPositionVector3Property),
                headTargetRotationEuler = Vector3Property.Bind(
                    animator, component, data.headTargetRotationVector3Property),
            };

            return job;
        }

        public override void Destroy(BasisHipsHeadIKConstraintJob job)
        {
            // Nothing allocated with Allocator here.
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Animation Rigging/Hips + Head IK Constraint")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/index.html")]
    public class BasisHipsHeadIKConstraint
        : RigConstraint<BasisHipsHeadIKConstraintJob,
                        BasisHipsHeadIKConstraintData,
                        BasisHipsHeadIKConstraintJobBinder<BasisHipsHeadIKConstraintData>>
    {
        // No extra editor/debug state – this constraint only drives hips and head.
    }
}
