using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Full IK + integrated dual "damped TR" driver, so everything runs in one job pass.
    /// - Three TwoBoneIK chains (Head, LeftLowerLeg, RightLowerLeg)
    /// - Minimal Hips driver (position + rotation) with calibration offset rotation
    /// - Two optional "driven" transforms (Left/Right) that take world-space TR from streamed properties
    /// </summary>
    [System.Serializable]
    public struct BasisFullIKConstraintData : IAnimationJobData
    {
        // ---------- Head ----------
        [SerializeField] Transform m_RootHead;
        [SerializeField] Transform m_MidHead;
        [SerializeField] Transform m_TipHead;

        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionHead;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationHead;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionHead;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationHead;

        [SyncSceneToStream, SerializeField] bool m_HintWeightHead;
        [SyncSceneToStream, SerializeField] bool m_EnabledHead;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetHead;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationHead;

        [SyncSceneToStream, SerializeField] public Vector3 m_HintDirection;

        public Transform rootHead { get => m_RootHead; set => m_RootHead = value; }
        public Transform midHead { get => m_MidHead; set => m_MidHead = value; }
        public Transform tipHead { get => m_TipHead; set => m_TipHead = value; }

        public bool hintWeightHead { get => m_HintWeightHead; set => m_HintWeightHead = value; }
        public bool enabledHead { get => m_EnabledHead; set => m_EnabledHead = value; }

        public string EnabledPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledHead));
        public string HintWeightBoolPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightHead));
        public string TargetPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHead));
        public string TargetRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationHead));
        public string HintPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionHead));
        public string HintRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationHead));
        public string HintDirectionProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintDirection));

        // ---------- Left Lower Leg ----------
        [SerializeField] Transform m_RootLeftLowerLeg;
        [SerializeField] Transform m_MidLeftLowerLeg;
        [SerializeField] Transform m_TipLeftLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationLeftLowerLeg;

        [SyncSceneToStream, SerializeField] bool m_HintWeightLeftLowerLeg;
        [SyncSceneToStream, SerializeField] bool m_EnabledLeftLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftLowerLeg;

        public Transform rootLeftLowerLeg { get => m_RootLeftLowerLeg; set => m_RootLeftLowerLeg = value; }
        public Transform midLeftLowerLeg { get => m_MidLeftLowerLeg; set => m_MidLeftLowerLeg = value; }
        public Transform tipLeftLowerLeg { get => m_TipLeftLowerLeg; set => m_TipLeftLowerLeg = value; }

        public bool hintWeightLeftLowerLeg { get => m_HintWeightLeftLowerLeg; set => m_HintWeightLeftLowerLeg = value; }
        public bool EnableLeftLeg { get => m_EnabledLeftLowerLeg; set => m_EnabledLeftLowerLeg = value; }

        public string EnabledPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledLeftLowerLeg));
        public string HintWeightBoolPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightLeftLowerLeg));
        public string TargetPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionLeftLowerLeg));
        public string TargetRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationLeftLowerLeg));
        public string HintPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionLeftLowerLeg));
        public string HintRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationLeftLowerLeg));

        // ---------- Right Lower Leg ----------
        [SerializeField] Transform m_RootRightLowerLeg;
        [SerializeField] Transform m_MidRightLowerLeg;
        [SerializeField] Transform m_TipRightLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationRightLowerLeg;

        [SyncSceneToStream, SerializeField] bool m_HintWeightRightLowerLeg;
        [SyncSceneToStream, SerializeField] bool m_EnabledRightLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightLowerLeg;

        public Transform rootRightLowerLeg { get => m_RootRightLowerLeg; set => m_RootRightLowerLeg = value; }
        public Transform midRightLowerLeg { get => m_MidRightLowerLeg; set => m_MidRightLowerLeg = value; }
        public Transform tipRightLowerLeg { get => m_TipRightLowerLeg; set => m_TipRightLowerLeg = value; }

        public bool hintWeightRightLowerLeg { get => m_HintWeightRightLowerLeg; set => m_HintWeightRightLowerLeg = value; }
        public bool EnableRightLeg { get => m_EnabledRightLowerLeg; set => m_EnabledRightLowerLeg = value; }

        public string EnabledPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledRightLowerLeg));
        public string HintWeightBoolPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightRightLowerLeg));
        public string TargetPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionRightLowerLeg));
        public string TargetRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationRightLowerLeg));
        public string HintPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionRightLowerLeg));
        public string HintRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationRightLowerLeg));

        // ---------- Hips (minimal driver) ----------
        [SerializeField] Transform m_Hips;
        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionHips;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationEulerHips; // kept name for compat
        [SyncSceneToStream, SerializeField] public Quaternion OffsetRotationHips;
        [SyncSceneToStream, SerializeField] bool m_EnabledHips;

        public Transform hips { get => m_Hips; set => m_Hips = value; }
        public bool enabledHips { get => m_EnabledHips; set => m_EnabledHips = value; }

        public string TargetPositionPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHips));
        public string TargetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationEulerHips));
        public string OffsetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotationHips));
        public string EnabledPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledHips));

        // ---------- Integrated Dual "Driven TR" (ex-damped) ----------
        [SerializeField] Transform m_LeftDriven;
        [SerializeField] Transform m_RightDriven;

        [SyncSceneToStream, SerializeField] public Vector3 OutGoingLeftToePosition;
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingLeftToeRotation;

        [SyncSceneToStream, SerializeField] public Vector3 OutGoingRightToePosition;
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingRightToeRotation;

        [SyncSceneToStream, SerializeField] bool m_LeftDrivenEnabled;
        [SyncSceneToStream, SerializeField] bool m_RightDrivenEnabled;

        public Transform leftDriven { get => m_LeftDriven; set => m_LeftDriven = value; }
        public Transform rightDriven { get => m_RightDriven; set => m_RightDriven = value; }
        public bool LeftToggleEnabled { get => m_LeftDrivenEnabled; set => m_LeftDrivenEnabled = value; }
        public bool RightToggleEnabled { get => m_RightDrivenEnabled; set => m_RightDrivenEnabled = value; }

        public string LeftDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftDrivenEnabled));
        public string RightDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightDrivenEnabled));
        public string LeftDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToePosition));
        public string LeftDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToeRotation));
        public string RightDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToePosition));
        public string RightDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToeRotation));

        // ---------- Validation ----------
        bool IAnimationJobData.IsValid()
        {
            bool hipsValid = m_Hips != null;

            bool head = (m_TipHead && m_MidHead && m_RootHead &&
                         m_TipHead.IsChildOf(m_MidHead) && m_MidHead.IsChildOf(m_RootHead));

            bool lLeg = (m_TipLeftLowerLeg && m_MidLeftLowerLeg && m_RootLeftLowerLeg &&
                         m_TipLeftLowerLeg.IsChildOf(m_MidLeftLowerLeg) && m_MidLeftLowerLeg.IsChildOf(m_RootLeftLowerLeg));

            bool rLeg = (m_TipRightLowerLeg && m_MidRightLowerLeg && m_RootRightLowerLeg &&
                         m_TipRightLowerLeg.IsChildOf(m_MidRightLowerLeg) && m_MidRightLowerLeg.IsChildOf(m_RootRightLowerLeg));

            // Any of these being valid is enough to run.
            return head || lLeg || rLeg || hipsValid || (m_LeftDriven != null) || (m_RightDriven != null);
        }

        void IAnimationJobData.SetDefaultValues()
        {
            m_RootHead = m_MidHead = m_TipHead = null;
            m_RootLeftLowerLeg = m_MidLeftLowerLeg = m_TipLeftLowerLeg = null;
            m_RootRightLowerLeg = m_MidRightLowerLeg = m_TipRightLowerLeg = null;

            m_Hips = null;

            m_HintWeightHead = m_HintWeightLeftLowerLeg = m_HintWeightRightLowerLeg = true;
            m_EnabledHead = m_EnabledLeftLowerLeg = m_EnabledRightLowerLeg = true;
            m_EnabledHips = true;

            m_CalibratedOffsetHead = m_CalibratedOffsetLeftLowerLeg = m_CalibratedOffsetRightLowerLeg = Vector3.zero;
            m_CalibratedRotationHead = m_CalibratedRotationLeftLowerLeg = m_CalibratedRotationRightLowerLeg = Quaternion.identity;

            m_HintDirection = Vector3.up;

            TargetPositionHips = Vector3.zero;
            TargetRotationEulerHips = Quaternion.identity;
            OffsetRotationHips = Quaternion.identity;

            // Integrated driven TR defaults
            m_LeftDriven = m_RightDriven = null;
            OutGoingLeftToePosition = OutGoingRightToePosition = Vector3.zero;
            OutGoingLeftToeRotation = OutGoingRightToeRotation = Quaternion.identity;
            m_LeftDrivenEnabled = m_RightDrivenEnabled = false;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Animation Rigging/Basis Full IK Constraint (Head + Legs + Hips + Driven TR)")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/index.html")]
    public class BasisFullIKConstraint
        : RigConstraint<BasisFullIKConstraintJob, BasisFullIKConstraintData, BasisFullIKConstraintJobBinder>
    {
        protected override void OnValidate()
        {
            base.OnValidate();
            // force serialize dirty for animated bools
            m_Data.hintWeightHead = m_Data.hintWeightHead;
            m_Data.hintWeightLeftLowerLeg = m_Data.hintWeightLeftLowerLeg;
            m_Data.hintWeightRightLowerLeg = m_Data.hintWeightRightLowerLeg;
            m_Data.enabledHips = m_Data.enabledHips;

            // new toggles
            m_Data.LeftToggleEnabled = m_Data.LeftToggleEnabled;
            m_Data.RightToggleEnabled = m_Data.RightToggleEnabled;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct BasisFullIKConstraintJob : IWeightedAnimationJob
    {
        // ----- Head -----
        public ReadWriteTransformHandle rootHead, midHead, tipHead;
        public Vector3Property targetPositionHead, hintPositionHead, bendNormalHead;
        public Vector4Property targetRotationHead, hintRotationHead;
        public AffineTransform targetOffsetHead;
        public BoolProperty hintWeightHead, enabledHead;

        // ----- Left Lower Leg -----
        public ReadWriteTransformHandle rootLeftLowerLeg, midLeftLowerLeg, tipLeftLowerLeg;
        public Vector3Property targetPositionLeftLowerLeg, hintPositionLeftLowerLeg;
        public Vector4Property targetRotationLeftLowerLeg, hintRotationLeftLowerLeg;
        public AffineTransform targetOffsetLeftLowerLeg;
        public BoolProperty hintWeightLeftLowerLeg, enabledLeftLowerLeg;

        // ----- Right Lower Leg -----
        public ReadWriteTransformHandle rootRightLowerLeg, midRightLowerLeg, tipRightLowerLeg;
        public Vector3Property targetPositionRightLowerLeg, hintPositionRightLowerLeg;
        public Vector4Property targetRotationRightLowerLeg, hintRotationRightLowerLeg;
        public AffineTransform targetOffsetRightLowerLeg;
        public BoolProperty hintWeightRightLowerLeg, enabledRightLowerLeg;

        // ----- Hips (minimal) -----
        public ReadWriteTransformHandle hipsHandle;
        public Vector3Property targetPositionHips;
        public Vector4Property targetRotationHips;
        public Vector4Property offsetRotationHips;
        public BoolProperty enabledHips;

        // ----- Integrated Dual "Driven TR" (ex-damped) -----
        public ReadWriteTransformHandle leftDrivenHandle, rightDrivenHandle;
        public Vector3Property leftDrivenTargetPos, rightDrivenTargetPos;
        public Vector4Property leftDrivenTargetRot, rightDrivenTargetRot;
        public BoolProperty LeftToggle, RightToggle;

        public FloatProperty jobWeight { get; set; }

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float w = jobWeight.Get(stream);
            if (w <= 0f)
            {
                if (hipsHandle.IsValid(stream))
                    BasisAnimationRuntimeUtils.PassThrough(stream, hipsHandle);

                Pass(stream, rootHead, midHead, tipHead);
                Pass(stream, rootLeftLowerLeg, midLeftLowerLeg, tipLeftLowerLeg);
                Pass(stream, rootRightLowerLeg, midRightLowerLeg, tipRightLowerLeg);

                if (leftDrivenHandle.IsValid(stream))
                    BasisAnimationRuntimeUtils.PassThrough(stream, leftDrivenHandle);
                if (rightDrivenHandle.IsValid(stream))
                    BasisAnimationRuntimeUtils.PassThrough(stream, rightDrivenHandle);

                return;
            }

            // --- Hips minimal driver ---
            if (enabledHips.Get(stream) && hipsHandle.IsValid(stream))
            {
                Vector3 hipPos = targetPositionHips.Get(stream);
                Quaternion hipRot = V4ToQuat(targetRotationHips.Get(stream));
                Quaternion hipOff = V4ToQuat(offsetRotationHips.Get(stream));
                hipsHandle.SetPosition(stream, hipPos);
                hipsHandle.SetRotation(stream, hipRot * hipOff); // apply offset in target space
            }
            else if (hipsHandle.IsValid(stream))
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, hipsHandle);
            }

            // --- Tri TwoBoneIK solves ---
            SolveOne(stream, enabledHead, rootHead, midHead, tipHead,
                targetPositionHead, targetRotationHead, hintPositionHead, hintRotationHead,
                hintWeightHead, targetOffsetHead, bendNormalHead);

            SolveOne(stream, enabledLeftLowerLeg, rootLeftLowerLeg, midLeftLowerLeg, tipLeftLowerLeg,
                targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg,
                hintWeightLeftLowerLeg, targetOffsetLeftLowerLeg, bendNormalHead);

            SolveOne(stream, enabledRightLowerLeg, rootRightLowerLeg, midRightLowerLeg, tipRightLowerLeg,
                targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg,
                hintWeightRightLowerLeg, targetOffsetRightLowerLeg, bendNormalHead);

            // --- Integrated "damped TR" application (world-space) ---
            ApplyDrivenTR(stream, LeftToggle, leftDrivenHandle, leftDrivenTargetPos, leftDrivenTargetRot);
            ApplyDrivenTR(stream, RightToggle, rightDrivenHandle, rightDrivenTargetPos, rightDrivenTargetRot);
        }

        static void SolveOne(
            AnimationStream stream,
            BoolProperty enabledProp,
            ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip,
            Vector3Property targetPosProp, Vector4Property targetRotProp,
            Vector3Property hintPosProp, Vector4Property hintRotProp,
            BoolProperty hintWeightProp, AffineTransform targetOffset, Vector3Property bendNormalProp)
        {
            if (!enabledProp.Get(stream))
            {
                Pass(stream, root, mid, tip);
                return;
            }

            bool rootValid = root.IsValid(stream);
            bool midValid = mid.IsValid(stream);
            bool tipValid = tip.IsValid(stream);
            if (!(rootValid && midValid && tipValid))
            {
                Pass(stream, root, mid, tip);
                return;
            }

            Quaternion tRot = V4ToQuat(targetRotProp.Get(stream));
            Quaternion hRot = V4ToQuat(hintRotProp.Get(stream));

            AffineTransform target = new AffineTransform(targetPosProp.Get(stream), tRot);
            AffineTransform hint = new AffineTransform(hintPosProp.Get(stream), hRot);
            Vector3 bendNormal = bendNormalProp.Get(stream);

            BasisAnimationRuntimeUtils.SolveTwoBone(
                stream, root, mid, tip,
                target, hint,
                hintWeightProp.Get(stream),
                targetOffset, bendNormal
            );
        }

        static void ApplyDrivenTR(
            AnimationStream stream,
            BoolProperty enabledProp,
            ReadWriteTransformHandle handle,
            Vector3Property targetPosProp,
            Vector4Property targetRotProp)
        {
            if (!handle.IsValid(stream))
                return;

            if (enabledProp.Get(stream))
            {
                var pos = targetPosProp.Get(stream);
                var rot = V4ToQuat(targetRotProp.Get(stream));
                handle.SetPosition(stream, pos);
                handle.SetRotation(stream, rot);
            }
            else
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, handle);
            }
        }

        static void Pass(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip)
        {
            if (root.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, root);
            if (mid.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, mid);
            if (tip.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, tip);
        }

        static Quaternion V4ToQuat(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);
    }

    public class BasisFullIKConstraintJobBinder : AnimationJobBinder<BasisFullIKConstraintJob, BasisFullIKConstraintData>
    {
        private static ReadWriteTransformHandle SafeBindHandle(Animator animator, Transform t) =>
            t != null ? ReadWriteTransformHandle.Bind(animator, t) : default;

        public override BasisFullIKConstraintJob Create(Animator animator, ref BasisFullIKConstraintData data, Component component)
        {
            var job = new BasisFullIKConstraintJob
            {
                // Hips
                hipsHandle = SafeBindHandle(animator, data.hips),
                targetPositionHips = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHips),
                targetRotationHips = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHips),
                offsetRotationHips = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyHips),
                enabledHips = BoolProperty.Bind(animator, component, data.EnabledPropertyHips),

                // Head
                rootHead = SafeBindHandle(animator, data.rootHead),
                midHead = SafeBindHandle(animator, data.midHead),
                tipHead = SafeBindHandle(animator, data.tipHead),

                targetPositionHead = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHead),
                targetRotationHead = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHead),
                hintPositionHead = Vector3Property.Bind(animator, component, data.HintPositionPropertyHead),
                hintRotationHead = Vector4Property.Bind(animator, component, data.HintRotationPropertyHead),
                bendNormalHead = Vector3Property.Bind(animator, component, data.HintDirectionProperty),

                hintWeightHead = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyHead),
                enabledHead = BoolProperty.Bind(animator, component, data.EnabledPropertyHead),

                targetOffsetHead = new AffineTransform(data.m_CalibratedOffsetHead, data.m_CalibratedRotationHead),

                // Left Lower Leg
                rootLeftLowerLeg = SafeBindHandle(animator, data.rootLeftLowerLeg),
                midLeftLowerLeg = SafeBindHandle(animator, data.midLeftLowerLeg),
                tipLeftLowerLeg = SafeBindHandle(animator, data.tipLeftLowerLeg),

                targetPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyLeftLowerLeg),
                targetRotationLeftLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyLeftLowerLeg),
                hintPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyLeftLowerLeg),
                hintRotationLeftLowerLeg = Vector4Property.Bind(animator, component, data.HintRotationPropertyLeftLowerLeg),

                hintWeightLeftLowerLeg = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyLeftLowerLeg),
                enabledLeftLowerLeg = BoolProperty.Bind(animator, component, data.EnabledPropertyLeftLowerLeg),

                targetOffsetLeftLowerLeg = new AffineTransform(data.m_CalibratedOffsetLeftLowerLeg, data.m_CalibratedRotationLeftLowerLeg),

                // Right Lower Leg
                rootRightLowerLeg = SafeBindHandle(animator, data.rootRightLowerLeg),
                midRightLowerLeg = SafeBindHandle(animator, data.midRightLowerLeg),
                tipRightLowerLeg = SafeBindHandle(animator, data.tipRightLowerLeg),

                targetPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyRightLowerLeg),
                targetRotationRightLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyRightLowerLeg),
                hintPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyRightLowerLeg),
                hintRotationRightLowerLeg = Vector4Property.Bind(animator, component, data.HintRotationPropertyRightLowerLeg),

                hintWeightRightLowerLeg = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyRightLowerLeg),
                enabledRightLowerLeg = BoolProperty.Bind(animator, component, data.EnabledPropertyRightLowerLeg),

                targetOffsetRightLowerLeg = new AffineTransform(data.m_CalibratedOffsetRightLowerLeg, data.m_CalibratedRotationRightLowerLeg),

                // Integrated Dual "Driven TR"
                leftDrivenHandle = SafeBindHandle(animator, data.leftDriven),
                rightDrivenHandle = SafeBindHandle(animator, data.rightDriven),

                leftDrivenTargetPos = Vector3Property.Bind(animator, component, data.LeftDrivenTargetPosProperty),
                leftDrivenTargetRot = Vector4Property.Bind(animator, component, data.LeftDrivenTargetRotProperty),
                rightDrivenTargetPos = Vector3Property.Bind(animator, component, data.RightDrivenTargetPosProperty),
                rightDrivenTargetRot = Vector4Property.Bind(animator, component, data.RightDrivenTargetRotProperty),

                LeftToggle = BoolProperty.Bind(animator, component, data.LeftDrivenEnabledProperty),
                RightToggle = BoolProperty.Bind(animator, component, data.RightDrivenEnabledProperty),
            };

            return job;
        }

        public override void Destroy(BasisFullIKConstraintJob job) { }
    }
}
