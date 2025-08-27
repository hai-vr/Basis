using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// The TwoBoneIK constraint data with tracker-stabilized bend plane.
    /// </summary>
    [System.Serializable]
    public struct BasisTwoBoneIKConstraintData : IAnimationJobData, BasisITwoBoneIKConstraintData
    {
        [SerializeField] Transform m_Root;
        [SerializeField] Transform m_Mid;
        [SerializeField] Transform m_Tip;

        [SyncSceneToStream, SerializeField] public Vector3 TargetPosition;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotation;
        [SyncSceneToStream, SerializeField] public Vector3 HintPosition;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotation;

        Vector3 BasisITwoBoneIKConstraintData.targetPosition => TargetPosition;
        Quaternion BasisITwoBoneIKConstraintData.targetRotation => TargetRotation;

        Vector3 BasisITwoBoneIKConstraintData.hintPosition => HintPosition;
        Quaternion BasisITwoBoneIKConstraintData.HintRotation => HintRotation;

        [SyncSceneToStream, SerializeField] bool m_HintWeight;

        // --- NEW: tracker & stabilization knobs ---
        [SyncSceneToStream, SerializeField, Range(0f, 1f)]
        float m_TrackerBlend;           // 0 = ignore tracker orientation, 1 = fully trust
        [SyncSceneToStream, SerializeField]
        float m_MaxMidDeltaDeg;         // clamp per-eval mid joint delta in degrees
        [SyncSceneToStream, SerializeField]
        float m_MinAxisSqrMag;          // epsilon for bend axis fallback
        [SyncSceneToStream, SerializeField]
        Vector3 m_TrackerForward;       // world-space forward from chest tracker

        public Transform root { get => m_Root; set => m_Root = value; }
        public Transform mid { get => m_Mid; set => m_Mid = value; }
        public Transform tip { get => m_Tip; set => m_Tip = value; }
        public bool hintWeight { get => m_HintWeight; set => m_HintWeight = value; }

        public float TrackerBlend { get => m_TrackerBlend; set => m_TrackerBlend = Mathf.Clamp01(value); }
        public float MaxMidDeltaDeg { get => m_MaxMidDeltaDeg; set => m_MaxMidDeltaDeg = Mathf.Max(0f, value); }
        public float MinAxisSqrMag { get => m_MinAxisSqrMag; set => m_MinAxisSqrMag = Mathf.Max(0f, value); }
        public Vector3 TrackerForward { get => m_TrackerForward; set => m_TrackerForward = value; }

        string BasisITwoBoneIKConstraintData.hintWeightFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeight));
        string BasisITwoBoneIKConstraintData.TargetpositionVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition));
        string BasisITwoBoneIKConstraintData.TargetrotationVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation));
        string BasisITwoBoneIKConstraintData.HintpositionVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPosition));
        string BasisITwoBoneIKConstraintData.HintrotationVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotation));
        string BasisITwoBoneIKConstraintData.HintDirectionProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintDirection));

        // NEW: property names for tracker/stability fields
        string BasisITwoBoneIKConstraintData.TrackerBlendFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_TrackerBlend));
        string BasisITwoBoneIKConstraintData.MaxMidDeltaDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MaxMidDeltaDeg));
        string BasisITwoBoneIKConstraintData.MinAxisSqrMagFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MinAxisSqrMag));
        string BasisITwoBoneIKConstraintData.TrackerForwardVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_TrackerForward));

        [SyncSceneToStream, SerializeField] public Vector3 M_CalibratedOffset;
        [SyncSceneToStream, SerializeField] public Vector3 M_CalibratedRotation;

        public Vector3 CalibratedOffset => M_CalibratedOffset;
        public Vector3 CalibratedRotation => M_CalibratedRotation;

        [SyncSceneToStream, SerializeField] public Vector3 m_HintDirection;
        Vector3 BasisITwoBoneIKConstraintData.HintDirection => m_HintDirection;

        bool IAnimationJobData.IsValid() =>
            (m_Tip != null && m_Mid != null && m_Root != null && m_Tip.IsChildOf(m_Mid) && m_Mid.IsChildOf(m_Root));

        void IAnimationJobData.SetDefaultValues()
        {
            m_Root = null;
            m_Mid = null;
            m_Tip = null;
            m_HintWeight = true;

            // sensible defaults for stabilization
            m_TrackerBlend = 0.75f;
            m_MaxMidDeltaDeg = 25f;
            m_MinAxisSqrMag = 1e-6f;
            m_TrackerForward = Vector3.forward;
        }
    }

    [DisallowMultipleComponent, AddComponentMenu("Animation Rigging/Two Bone IK Constraint (Basis/Stabilized)")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/constraints/TwoBoneIKConstraint.html")]
    public class BasisTwoBoneIKConstraint : RigConstraint<BasisTwoBoneIKConstraintJob, BasisTwoBoneIKConstraintData, BasisTwoBoneIKConstraintJobBinder<BasisTwoBoneIKConstraintData>>
    {
        protected override void OnValidate()
        {
            base.OnValidate();
            m_Data.hintWeight = m_Data.hintWeight;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct BasisTwoBoneIKConstraintJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle root;
        public ReadWriteTransformHandle mid;
        public ReadWriteTransformHandle tip;

        public Vector3Property hintPosition;
        public Vector3Property targetPosition;

        public Vector4Property hintRotation;
        public Vector4Property targetRotation;

        public AffineTransform targetOffset;
        public BoolProperty hintWeight;
        public FloatProperty jobWeight { get; set; }

        public Vector3Property BendNormal;

        // NEW binds
        public FloatProperty trackerBlend;
        public FloatProperty maxMidDeltaDeg;
        public FloatProperty minAxisSqrMag;
        public Vector3Property trackerForward;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float w = jobWeight.Get(stream);
            if (w > 0f)
            {
                AffineTransform target = new AffineTransform(
                    targetPosition.Get(stream),
                    Vector4ToRotation(targetRotation.Get(stream))
                );

                AffineTransform hint = new AffineTransform(
                    hintPosition.Get(stream),
                    Vector4ToRotation(hintRotation.Get(stream))
                );

                Vector3 bendNormalPrev = BendNormal.Get(stream);

                // NEW: stabilization inputs
                float blend = Mathf.Clamp01(trackerBlend.Get(stream));// : 0.75f;
                float maxDelta =  Mathf.Max(0f, maxMidDeltaDeg.Get(stream));// : 25f;
                float minAxisMag2 =  Mathf.Max(0f, minAxisSqrMag.Get(stream));// : 1e-6f;
                Vector3 trackerFwd =  trackerForward.Get(stream);// : Vector3.forward;

                BasisAnimationRuntimeUtils.SolveTwoBoneIKLegsAndTorso_Stabilized(
                    stream, root, mid, tip,
                    target, hint, hintWeight.Get(stream), targetOffset,
                    bendNormalPrev,
                    trackerFwd, blend, minAxisMag2, maxDelta
                );
            }
            else
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, root);
                BasisAnimationRuntimeUtils.PassThrough(stream, mid);
                BasisAnimationRuntimeUtils.PassThrough(stream, tip);
            }
        }

        public Quaternion Vector4ToRotation(Vector4 r)
        {
            return new Quaternion(r.x, r.y, r.z, r.w);
        }
    }

    public interface BasisITwoBoneIKConstraintData
    {
        Transform root { get; }
        Transform mid { get; }
        Transform tip { get; }

        Vector3 targetPosition { get; }
        Quaternion targetRotation { get; }
        Vector3 hintPosition { get; }
        Quaternion HintRotation { get; }

        Vector3 CalibratedOffset { get; }
        Vector3 CalibratedRotation { get; }
        string hintWeightFloatProperty { get; }

        string TargetpositionVector3Property { get; }
        string TargetrotationVector3Property { get; }

        string HintpositionVector3Property { get; }
        string HintrotationVector3Property { get; }
        string HintDirectionProperty { get; }

        Vector3 HintDirection { get; }

        // NEW properties we bind
        string TrackerBlendFloatProperty { get; }
        string MaxMidDeltaDegFloatProperty { get; }
        string MinAxisSqrMagFloatProperty { get; }
        string TrackerForwardVector3Property { get; }
    }

    public class BasisTwoBoneIKConstraintJobBinder<T> : AnimationJobBinder<BasisTwoBoneIKConstraintJob, T> where T : struct, IAnimationJobData, BasisITwoBoneIKConstraintData
    {
        public override BasisTwoBoneIKConstraintJob Create(Animator animator, ref T data, Component component)
        {
            BasisTwoBoneIKConstraintJob job = new BasisTwoBoneIKConstraintJob
            {
                root = ReadWriteTransformHandle.Bind(animator, data.root),
                mid = ReadWriteTransformHandle.Bind(animator, data.mid),
                tip = ReadWriteTransformHandle.Bind(animator, data.tip),

                targetPosition = Vector3Property.Bind(animator, component, data.TargetpositionVector3Property),
                targetRotation = Vector4Property.Bind(animator, component, data.TargetrotationVector3Property),

                hintPosition = Vector3Property.Bind(animator, component, data.HintpositionVector3Property),
                hintRotation = Vector4Property.Bind(animator, component, data.HintrotationVector3Property),

                targetOffset = AffineTransform.identity
            };

            job.targetOffset.translation = data.CalibratedOffset;
            job.targetOffset.rotation = Quaternion.Euler(data.CalibratedRotation);

            job.hintWeight = BoolProperty.Bind(animator, component, data.hintWeightFloatProperty);
            job.BendNormal = Vector3Property.Bind(animator, component, data.HintDirectionProperty);

            // NEW binds
            job.trackerBlend = FloatProperty.Bind(animator, component, data.TrackerBlendFloatProperty);
            job.maxMidDeltaDeg = FloatProperty.Bind(animator, component, data.MaxMidDeltaDegFloatProperty);
            job.minAxisSqrMag = FloatProperty.Bind(animator, component, data.MinAxisSqrMagFloatProperty);
            job.trackerForward = Vector3Property.Bind(animator, component, data.TrackerForwardVector3Property);

            return job;
        }

        public override void Destroy(BasisTwoBoneIKConstraintJob job) { }
    }
}
