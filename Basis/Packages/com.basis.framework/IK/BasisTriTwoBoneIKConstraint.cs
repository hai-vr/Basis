namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
    [System.Serializable]
    public struct BasisFullIKConstraintData : IAnimationJobData
    {
        [SerializeField] Transform m_Hips;
        [SerializeField] Transform m_RootHead;
        [SerializeField] Transform m_MidHead;
        [SerializeField] Transform m_TipHead;

        [SerializeField] Transform m_RootLeftLowerLeg;
        [SerializeField] Transform m_MidLeftLowerLeg;
        [SerializeField] Transform m_TipLeftLowerLeg;
        [SerializeField] Transform m_RootRightLowerLeg;
        [SerializeField] Transform m_MidRightLowerLeg;
        [SerializeField] Transform m_TipRightLowerLeg;

        [SerializeField] Transform m_LeftDriven;
        [SerializeField] Transform m_RightDriven;

        [SerializeField] Transform m_RootLeftHand;
        [SerializeField] Transform m_MidLeftHand;
        [SerializeField] Transform m_TipLeftHand;

        [SerializeField] Transform m_RootRightHand;
        [SerializeField] Transform m_MidRightHand;
        [SerializeField] Transform m_TipRightHand;

        // Chest capsule for RIGHT hand
        [SyncSceneToStream, SerializeField] Transform m_ChestCapsuleStart;
        [SyncSceneToStream, SerializeField] Transform m_ChestCapsuleEnd;


        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionHead;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationHead;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionHead;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationHead;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetHead;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationHead;

        [SyncSceneToStream, SerializeField] public Vector3 m_HintDirection;
        [SyncSceneToStream, SerializeField] public Vector3 LeftFootTargetPosition;
        [SyncSceneToStream, SerializeField] public Quaternion LeftFootTargetRotation;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationLeftLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Vector3 RightFootTargetPosition;
        [SyncSceneToStream, SerializeField] public Quaternion RightFootTargetRotation;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationRightLowerLeg;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionHips;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationEulerHips; // (compat name)
        [SyncSceneToStream, SerializeField] public Quaternion OffsetRotationHips;

        [SyncSceneToStream, SerializeField] public Vector3 OutGoingLeftToePosition;
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingLeftToeRotation;

        [SyncSceneToStream, SerializeField] public Vector3 OutGoingRightToePosition;
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingRightToeRotation;
        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionLeftHand;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationLeftHand;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionLeftHand;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationLeftHand;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetLeftHand;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftHand;

        // Hand capsule (tip local)
        [SyncSceneToStream, SerializeField] Vector3 m_HandLocalStart;
        [SyncSceneToStream, SerializeField] Vector3 m_HandLocalEnd;
        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetLeftHandHint; // optional spare if desired
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftHandHint;

        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationRightHand;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationRightHand;

        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightHand;


        [SyncSceneToStream, SerializeField] bool m_LeftDrivenEnabled;
        [SyncSceneToStream, SerializeField] bool m_RightDrivenEnabled;
        [SyncSceneToStream, SerializeField] float m_HandSkin;
        [SyncSceneToStream, SerializeField] bool m_UseHandCapsule;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_HandRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_ChestRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_CollisionSkin;
        [SyncSceneToStream, SerializeField] bool m_CollisionsEnabled;
        [SyncSceneToStream, SerializeField] bool m_ProtectElbow;

        [SyncSceneToStream, SerializeField] bool m_HintWeightRightHand;
        [SyncSceneToStream, SerializeField] bool m_EnabledRightHand;
        [SyncSceneToStream, SerializeField] bool m_HintWeightLeftHand;
        [SyncSceneToStream, SerializeField] bool m_EnabledLeftHand;
        [SyncSceneToStream, SerializeField] bool m_EnabledHips;
        [SyncSceneToStream, SerializeField] bool m_HintWeightHead;
        [SyncSceneToStream, SerializeField] bool m_EnabledHead;
        [SyncSceneToStream, SerializeField] bool m_HintWeightLeftLowerLeg;
        [SyncSceneToStream, SerializeField] bool m_EnabledLeftLowerLeg;
        [SyncSceneToStream, SerializeField] bool m_HintWeightRightLowerLeg;
        [SyncSceneToStream, SerializeField] bool m_EnabledRightLowerLeg;

        public Transform rootHead { get => m_RootHead; set => m_RootHead = value; }
        public Transform midHead { get => m_MidHead; set => m_MidHead = value; }
        public Transform tipHead { get => m_TipHead; set => m_TipHead = value; }
        public Transform rootLeftLowerLeg { get => m_RootLeftLowerLeg; set => m_RootLeftLowerLeg = value; }
        public Transform midLeftLowerLeg { get => m_MidLeftLowerLeg; set => m_MidLeftLowerLeg = value; }
        public Transform tipLeftLowerLeg { get => m_TipLeftLowerLeg; set => m_TipLeftLowerLeg = value; }
        public Transform rootRightLowerLeg { get => m_RootRightLowerLeg; set => m_RootRightLowerLeg = value; }
        public Transform midRightLowerLeg { get => m_MidRightLowerLeg; set => m_MidRightLowerLeg = value; }
        public Transform tipRightLowerLeg { get => m_TipRightLowerLeg; set => m_TipRightLowerLeg = value; }
        public Transform hips { get => m_Hips; set => m_Hips = value; }
        public Transform leftDriven { get => m_LeftDriven; set => m_LeftDriven = value; }
        public Transform rightDriven { get => m_RightDriven; set => m_RightDriven = value; }
        public Transform rootLeftHand { get => m_RootLeftHand; set => m_RootLeftHand = value; }
        public Transform midLeftHand { get => m_MidLeftHand; set => m_MidLeftHand = value; }
        public Transform tipLeftHand { get => m_TipLeftHand; set => m_TipLeftHand = value; }
        public Transform chestCapsuleStart { get => m_ChestCapsuleStart; set => m_ChestCapsuleStart = value; }
        public Transform chestCapsuleEnd { get => m_ChestCapsuleEnd; set => m_ChestCapsuleEnd = value; }
        public Transform rootRightHand { get => m_RootRightHand; set => m_RootRightHand = value; }
        public Transform midRightHand { get => m_MidRightHand; set => m_MidRightHand = value; }
        public Transform tipRightHand { get => m_TipRightHand; set => m_TipRightHand = value; }

        public string EnabledPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledHead));
        public string HintWeightBoolPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightHead));
        public string TargetPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHead));
        public string TargetRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationHead));
        public string HintPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionHead));
        public string HintRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationHead));
        public string HintDirectionProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintDirection));
        public string EnabledPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledLeftLowerLeg));
        public string HintWeightBoolPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightLeftLowerLeg));
        public string TargetPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootTargetPosition));
        public string TargetRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootTargetRotation));
        public string HintPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionLeftLowerLeg));
        public string HintRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationLeftLowerLeg));
        public string EnabledPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledRightLowerLeg));
        public string HintWeightBoolPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightRightLowerLeg));
        public string TargetPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootTargetPosition));
        public string TargetRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootTargetRotation));
        public string HintPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionRightLowerLeg));
        public string HintRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationRightLowerLeg));
        public string TargetPositionPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHips));
        public string TargetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationEulerHips));
        public string OffsetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotationHips));
        public string EnabledPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledHips));
        public string LeftDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftDrivenEnabled));
        public string RightDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightDrivenEnabled));
        public string LeftDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToePosition));
        public string LeftDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToeRotation));
        public string RightDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToePosition));
        public string RightDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToeRotation));
        public string EnabledPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledLeftHand));
        public string HintWeightBoolPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightLeftHand));
        public string TargetPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionLeftHand));
        public string TargetRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationLeftHand));
        public string HintPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionLeftHand));
        public string HintRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationLeftHand));
        public string ChestRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestRadius));
        public string CollisionSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionSkin));
        public string CollisionsEnabledBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionsEnabled));
        public string HandLocalStartVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandLocalStart));
        public string HandLocalEndVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandLocalEnd));
        public string HandRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandRadius));
        public string HandSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandSkin));
        public string UseHandCapsuleBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UseHandCapsule));
        public string ProtectElbowBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ProtectElbow));
        public string EnabledPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledRightHand));
        public string HintWeightBoolPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintWeightRightHand));
        public string TargetPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionRightHand));
        public string TargetRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationRightHand));
        public string HintPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionRightHand));
        public string HintRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationRightHand));

        public bool hintWeightHead { get => m_HintWeightHead; set => m_HintWeightHead = value; }
        public bool enabledHead { get => m_EnabledHead; set => m_EnabledHead = value; }
        public bool hintWeightLeftLowerLeg { get => m_HintWeightLeftLowerLeg; set => m_HintWeightLeftLowerLeg = value; }
        public bool EnableLeftLeg { get => m_EnabledLeftLowerLeg; set => m_EnabledLeftLowerLeg = value; }
        public bool hintWeightRightLowerLeg { get => m_HintWeightRightLowerLeg; set => m_HintWeightRightLowerLeg = value; }
        public bool EnableRightLeg { get => m_EnabledRightLowerLeg; set => m_EnabledRightLowerLeg = value; }
        public bool enabledHips { get => m_EnabledHips; set => m_EnabledHips = value; }
        public bool LeftToggleEnabled { get => m_LeftDrivenEnabled; set => m_LeftDrivenEnabled = value; }
        public bool RightToggleEnabled { get => m_RightDrivenEnabled; set => m_RightDrivenEnabled = value; }
        public bool hintWeightLeftHand { get => m_HintWeightLeftHand; set => m_HintWeightLeftHand = value; }
        public bool enabledLeftHand { get => m_EnabledLeftHand; set => m_EnabledLeftHand = value; }
        public bool protectElbow { get => m_ProtectElbow; set => m_ProtectElbow = value; }
        public bool hintWeightRightHand { get => m_HintWeightRightHand; set => m_HintWeightRightHand = value; }
        public bool enabledRightHand { get => m_EnabledRightHand; set => m_EnabledRightHand = value; }
        public float handRadius { get => m_HandRadius; set => m_HandRadius = value; }
        public float handSkin { get => m_HandSkin; set => m_HandSkin = value; }
        public bool useHandCapsule { get => m_UseHandCapsule; set => m_UseHandCapsule = value; }
        public float chestRadius { get => m_ChestRadius; set => m_ChestRadius = value; }
        public float collisionSkin { get => m_CollisionSkin; set => m_CollisionSkin = value; }
        public bool collisionsEnabled { get => m_CollisionsEnabled; set => m_CollisionsEnabled = value; }

        public Vector3 handLocalStartLeft { get => m_HandLocalStart; set => m_HandLocalStart = value; }
        public Vector3 handLocalEndLeft { get => m_HandLocalEnd; set => m_HandLocalEnd = value; }
        public Vector3 handLocalStartRight { get => m_HandLocalStart; set => m_HandLocalStart = value; }
        public Vector3 handLocalEndRight { get => m_HandLocalEnd; set => m_HandLocalEnd = value; }

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

            bool lHand = (m_TipLeftHand && m_MidLeftHand && m_RootLeftHand &&
                          m_TipLeftHand.IsChildOf(m_MidLeftHand) && m_MidLeftHand.IsChildOf(m_RootLeftHand));

            bool rHand = (m_TipRightHand && m_MidRightHand && m_RootRightHand &&
                          m_TipRightHand.IsChildOf(m_MidRightHand) && m_MidRightHand.IsChildOf(m_RootRightHand));

            // Any of these being valid is enough to run.
            return head || lLeg || rLeg || lHand || rHand || hipsValid || (m_LeftDriven != null) || (m_RightDriven != null);
        }

        void IAnimationJobData.SetDefaultValues()
        {
            m_RootHead = m_MidHead = m_TipHead = null;
            m_RootLeftLowerLeg = m_MidLeftLowerLeg = m_TipLeftLowerLeg = null;
            m_RootRightLowerLeg = m_MidRightLowerLeg = m_TipRightLowerLeg = null;

            m_RootLeftHand = m_MidLeftHand = m_TipLeftHand = null;
            m_RootRightHand = m_MidRightHand = m_TipRightHand = null;

            m_Hips = null;

            m_HintWeightHead = m_HintWeightLeftLowerLeg = m_HintWeightRightLowerLeg = true;
            m_EnabledHead = m_EnabledLeftLowerLeg = m_EnabledRightLowerLeg = true;
            m_EnabledHips = true;

            m_HintWeightLeftHand = m_HintWeightRightHand = true;
            m_EnabledLeftHand = m_EnabledRightHand = true;

            m_CalibratedOffsetHead = m_CalibratedOffsetLeftLowerLeg = m_CalibratedOffsetRightLowerLeg = Vector3.zero;
            m_CalibratedRotationHead = m_CalibratedRotationLeftLowerLeg = m_CalibratedRotationRightLowerLeg = Quaternion.identity;

            m_CalibratedOffsetLeftHand = m_CalibratedOffsetRightHand = Vector3.zero;
            m_CalibratedRotationLeftHand = m_CalibratedRotationRightHand = Quaternion.identity;

            m_HintDirection = Vector3.up;

            TargetPositionHips = Vector3.zero;
            TargetRotationEulerHips = Quaternion.identity;
            OffsetRotationHips = Quaternion.identity;

            // Integrated driven TR defaults
            m_LeftDriven = m_RightDriven = null;
            OutGoingLeftToePosition = OutGoingRightToePosition = Vector3.zero;
            OutGoingLeftToeRotation = OutGoingRightToeRotation = Quaternion.identity;
            m_LeftDrivenEnabled = m_RightDrivenEnabled = false;

            // Chest/hand capsule defaults (left)
            m_ChestCapsuleStart = m_ChestCapsuleEnd = null;
            m_ChestRadius = 0.18f; m_CollisionSkin = 0.02f; m_CollisionsEnabled = true;
            m_HandLocalStart = new Vector3(0f, 0f, -0.05f);
            m_HandLocalEnd = new Vector3(0f, 0f, 0.08f);
            m_HandRadius = 0.05f; m_HandSkin = 0.01f; m_UseHandCapsule = true;
            m_ProtectElbow = true;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Animation Rigging/Basis Full IK Constraint (Head + Legs + Hips + Driven TR + Hands)")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/index.html")]
    public class BasisFullIKConstraint : RigConstraint<BasisFullIKConstraintJob, BasisFullIKConstraintData, BasisFullIKConstraintJobBinder>
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

            // hands toggles
            m_Data.hintWeightLeftHand = m_Data.hintWeightLeftHand;
            m_Data.hintWeightRightHand = m_Data.hintWeightRightHand;
            m_Data.enabledLeftHand = m_Data.enabledLeftHand;
            m_Data.enabledRightHand = m_Data.enabledRightHand;
            m_Data.protectElbow = m_Data.protectElbow;
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

        // ----- Left Hand (TwoBone + collisions) -----
        public ReadWriteTransformHandle rootLeftHand, midLeftHand, tipLeftHand;
        public Vector3Property targetPositionLeftHand, hintPositionLeftHand;
        public Vector4Property targetRotationLeftHand, hintRotationLeftHand;
        public AffineTransform targetOffsetLeftHand;
        public BoolProperty hintWeightLeftHand, enabledLeftHand;

        public ReadOnlyTransformHandle chestStart, chestEnd;

        public Vector3Property handLocalStart, handLocalEnd;

        // ----- Right Hand (TwoBone + collisions) -----
        public ReadWriteTransformHandle rootRightHand, midRightHand, tipRightHand;
        public Vector3Property targetPositionRightHand, hintPositionRightHand;
        public Vector4Property targetRotationRightHand, hintRotationRightHand;
        public AffineTransform targetOffsetRightHand;
        public BoolProperty hintWeightRightHand, enabledRightHand;

        public FloatProperty handRadius, handSkin;
        public BoolProperty useHandCapsule, protectElbow;

        public FloatProperty chestRadius, collisionSkin;
        public BoolProperty collisionsEnabled;
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

                Pass(stream, rootLeftHand, midLeftHand, tipLeftHand);
                Pass(stream, rootRightHand, midRightHand, tipRightHand);

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

            // --- Head + Legs (classic TwoBone) ---
            SolveOne(stream, enabledHead, rootHead, midHead, tipHead,
                targetPositionHead, targetRotationHead, hintPositionHead, hintRotationHead,
                hintWeightHead, targetOffsetHead, bendNormalHead);

            SolveOne(stream, enabledLeftLowerLeg, rootLeftLowerLeg, midLeftLowerLeg, tipLeftLowerLeg,
                targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg,
                hintWeightLeftLowerLeg, targetOffsetLeftLowerLeg, bendNormalHead);

            SolveOne(stream, enabledRightLowerLeg, rootRightLowerLeg, midRightLowerLeg, tipRightLowerLeg,
                targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg,
                hintWeightRightLowerLeg, targetOffsetRightLowerLeg, bendNormalHead);

            // --- Hands (TwoBone with capsules + elbow protection) ---
            SolveHand(stream,
                enabledLeftHand, rootLeftHand, midLeftHand, tipLeftHand,
                targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand,
                hintWeightLeftHand, targetOffsetLeftHand,
                chestStart, chestEnd, chestRadius, collisionSkin, collisionsEnabled,
                handLocalStart, handLocalEnd, handRadius, handSkin, useHandCapsule,
                protectElbow);

            SolveHand(stream,
                enabledRightHand, rootRightHand, midRightHand, tipRightHand,
                targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand,
                hintWeightRightHand, targetOffsetRightHand,
                chestStart, chestEnd, chestRadius, collisionSkin, collisionsEnabled,
                handLocalStart, handLocalEnd, handRadius, handSkin, useHandCapsule,
                protectElbow);

            // --- Integrated "damped TR" application (world-space) ---
            ApplyDrivenTR(stream, LeftToggle, leftDrivenHandle, leftDrivenTargetPos, leftDrivenTargetRot);
            ApplyDrivenTR(stream, RightToggle, rightDrivenHandle, rightDrivenTargetPos, rightDrivenTargetRot);
        }

        // === Helpers ===
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

            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
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

        static void SolveHand(
            AnimationStream stream,
            BoolProperty enabledProp,
            ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip,
            Vector3Property targetPosProp, Vector4Property targetRotProp,
            Vector3Property hintPosProp, Vector4Property hintRotProp,
            BoolProperty hintWeightProp, AffineTransform targetOffset,
            ReadOnlyTransformHandle chestStart, ReadOnlyTransformHandle chestEnd,
            FloatProperty chestRadius, FloatProperty collisionSkin, BoolProperty collisionsEnabled,
            Vector3Property handLocalStart, Vector3Property handLocalEnd, FloatProperty handRadius, FloatProperty handSkin, BoolProperty useHandCapsule,
            BoolProperty protectElbow)
        {
            if (!enabledProp.Get(stream))
            {
                Pass(stream, root, mid, tip);
                return;
            }
            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                Pass(stream, root, mid, tip);
                return;
            }

            // Read inputs
            Vector3 tgtPos = targetPosProp.Get(stream);
            Quaternion tgtRot = V4ToQuat(targetRotProp.Get(stream));
            Vector3 hintPos = hintPosProp.Get(stream);
            Quaternion hintRot = V4ToQuat(hintRotProp.Get(stream));

            bool doCollisions = collisionsEnabled.Get(stream) && chestStart.IsValid(stream) && chestEnd.IsValid(stream);

            if (doCollisions)
            {
                Vector3 a = chestStart.GetPosition(stream);
                Vector3 b = chestEnd.GetPosition(stream);
                float chestR = Mathf.Max(0f, chestRadius.Get(stream) + collisionSkin.Get(stream));

                if (useHandCapsule.Get(stream))
                {
                    Vector3 hsLocal = handLocalStart.Get(stream);
                    Vector3 heLocal = handLocalEnd.Get(stream);
                    float hRad = Mathf.Max(0f, handRadius.Get(stream) + handSkin.Get(stream));

                    Vector3 handA = tgtPos + (tgtRot * hsLocal);
                    Vector3 handB = tgtPos + (tgtRot * heLocal);

                    Vector3 correction = BasisAnimationRuntimeUtils.CapsuleCapsuleResolve(handA, handB, hRad, a, b, chestR);
                    if (correction.sqrMagnitude > 0f)
                    {
                        tgtPos += correction;
                        hintPos += correction * 0.25f; // steer elbow slightly
                    }
                }
                else
                {
                    tgtPos = BasisAnimationRuntimeUtils.PushOutFromCapsule(tgtPos, a, b, chestR);
                    Vector3 nudgedHint = BasisAnimationRuntimeUtils.PushOutFromCapsule(hintPos, a, b, chestR * 0.9f);
                    hintPos = Vector3.Lerp(hintPos, nudgedHint, 0.6f);
                }
            }

            var target = new AffineTransform(tgtPos, tgtRot);
            var hint = new AffineTransform(hintPos, hintRot);

            // First solve (arms variant to preserve wrist)
            BasisAnimationRuntimeUtils.SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset);

            // Optional elbow protection pass
            if (protectElbow.Get(stream) && doCollisions)
            {
                Vector3 a = chestStart.GetPosition(stream);
                Vector3 b = chestEnd.GetPosition(stream);
                float chestR = Mathf.Max(0f, chestRadius.Get(stream) + collisionSkin.Get(stream));

                Vector3 B = mid.GetPosition(stream);
                Vector3 pushedB = BasisAnimationRuntimeUtils.PushOutFromCapsule(B, a, b, chestR);
                if ((pushedB - B).sqrMagnitude > 1e-10f)
                {
                    BasisAnimationRuntimeUtils.SwingElbowAroundAC(stream, root, mid, tip, pushedB);
                    // Re-lock wrist to target after elbow swing
                    BasisAnimationRuntimeUtils.SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset);
                }
            }
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
        private static ReadOnlyTransformHandle SafeBindRO(Animator animator, Transform t) =>
            t != null ? ReadOnlyTransformHandle.Bind(animator, t) : default;

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

                // Left Hand
                rootLeftHand = SafeBindHandle(animator, data.rootLeftHand),
                midLeftHand = SafeBindHandle(animator, data.midLeftHand),
                tipLeftHand = SafeBindHandle(animator, data.tipLeftHand),

                targetPositionLeftHand = Vector3Property.Bind(animator, component, data.TargetPositionPropertyLeftHand),
                targetRotationLeftHand = Vector4Property.Bind(animator, component, data.TargetRotationPropertyLeftHand),
                hintPositionLeftHand = Vector3Property.Bind(animator, component, data.HintPositionPropertyLeftHand),
                hintRotationLeftHand = Vector4Property.Bind(animator, component, data.HintRotationPropertyLeftHand),

                hintWeightLeftHand = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyLeftHand),
                enabledLeftHand = BoolProperty.Bind(animator, component, data.EnabledPropertyLeftHand),

                targetOffsetLeftHand = new AffineTransform(data.m_CalibratedOffsetLeftHand, data.m_CalibratedRotationLeftHand),

                chestStart = SafeBindRO(animator, data.chestCapsuleStart),
                chestEnd = SafeBindRO(animator, data.chestCapsuleEnd),

                protectElbow = BoolProperty.Bind(animator, component, data.ProtectElbowBoolProperty),

                // Right Hand
                rootRightHand = SafeBindHandle(animator, data.rootRightHand),
                midRightHand = SafeBindHandle(animator, data.midRightHand),
                tipRightHand = SafeBindHandle(animator, data.tipRightHand),

                targetPositionRightHand = Vector3Property.Bind(animator, component, data.TargetPositionPropertyRightHand),
                targetRotationRightHand = Vector4Property.Bind(animator, component, data.TargetRotationPropertyRightHand),
                hintPositionRightHand = Vector3Property.Bind(animator, component, data.HintPositionPropertyRightHand),
                hintRotationRightHand = Vector4Property.Bind(animator, component, data.HintRotationPropertyRightHand),

                hintWeightRightHand = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyRightHand),
                enabledRightHand = BoolProperty.Bind(animator, component, data.EnabledPropertyRightHand),

                targetOffsetRightHand = new AffineTransform(data.m_CalibratedOffsetRightHand, data.m_CalibratedRotationRightHand),

                handLocalStart = Vector3Property.Bind(animator, component, data.HandLocalStartVector3Property),
                handLocalEnd = Vector3Property.Bind(animator, component, data.HandLocalEndVector3Property),

                chestRadius = FloatProperty.Bind(animator, component, data.ChestRadiusFloatProperty),
                collisionSkin = FloatProperty.Bind(animator, component, data.CollisionSkinFloatProperty),
                collisionsEnabled = BoolProperty.Bind(animator, component, data.CollisionsEnabledBoolProperty),

                handRadius = FloatProperty.Bind(animator, component, data.HandRadiusFloatProperty),
                handSkin = FloatProperty.Bind(animator, component, data.HandSkinFloatProperty),
                useHandCapsule = BoolProperty.Bind(animator, component, data.UseHandCapsuleBoolProperty),
            };

            return job;
        }

        public override void Destroy(BasisFullIKConstraintJob job) { }
    }
}
