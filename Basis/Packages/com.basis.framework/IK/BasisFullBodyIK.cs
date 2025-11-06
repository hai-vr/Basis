using System.Runtime.CompilerServices;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
    [System.Serializable]
    public struct BasisFullBodyData : IAnimationJobData, IBasisFullBodyData
    {
        public const int Count = 23;
        // Target transforms (one per slot). Null entries are simply skipped.
        [SerializeField]
        private Transform
            m_target0, m_target1, m_target2, m_target3, m_target4,
            m_target5, m_target6, m_target7, m_target8, m_target9,
            m_target10, m_target11, m_target12, m_target13, m_target14,
            m_target15, m_target16, m_target17, m_target18, m_target19,
            m_target20, m_target21, m_target22;

        // Live target positions (Vector3) pushed every frame from the manager.
        [SyncSceneToStream, SerializeField]
        public Vector3
            TargetPosition0, TargetPosition1, TargetPosition2, TargetPosition3, TargetPosition4,
            TargetPosition5, TargetPosition6, TargetPosition7, TargetPosition8, TargetPosition9,
            TargetPosition10, TargetPosition11, TargetPosition12, TargetPosition13, TargetPosition14,
            TargetPosition15, TargetPosition16, TargetPosition17, TargetPosition18, TargetPosition19,
            TargetPosition20, TargetPosition21, TargetPosition22;

        // Live target rotations (Quaternion) — stored as Quaternion on the component; bound as Vector4 by the job.
        [SyncSceneToStream, SerializeField]
        public Quaternion
            TargetRotation0, TargetRotation1, TargetRotation2, TargetRotation3, TargetRotation4,
            TargetRotation5, TargetRotation6, TargetRotation7, TargetRotation8, TargetRotation9,
            TargetRotation10, TargetRotation11, TargetRotation12, TargetRotation13, TargetRotation14,
            TargetRotation15, TargetRotation16, TargetRotation17, TargetRotation18, TargetRotation19,
            TargetRotation20, TargetRotation21, TargetRotation22;

        // Calibration offsets (applied on top of target each frame) — final = target * offset
        [SyncSceneToStream, SerializeField]
        public Quaternion
            OffsetRotation0, OffsetRotation1, OffsetRotation2, OffsetRotation3, OffsetRotation4,
            OffsetRotation5, OffsetRotation6, OffsetRotation7, OffsetRotation8, OffsetRotation9,
            OffsetRotation10, OffsetRotation11, OffsetRotation12, OffsetRotation13, OffsetRotation14,
            OffsetRotation15, OffsetRotation16, OffsetRotation17, OffsetRotation18, OffsetRotation19,
            OffsetRotation20, OffsetRotation21, OffsetRotation22;

        // Per-slot enable/weights (0..1). Allows toggling bones independently within a single job.
        [SyncSceneToStream, SerializeField]
        public bool
            Weight0, Weight1, Weight2, Weight3, Weight4,
            Weight5, Weight6, Weight7, Weight8, Weight9,
            Weight10, Weight11, Weight12, Weight13, Weight14,
            Weight15, Weight16, Weight17, Weight18, Weight19,
            Weight20, Weight21, Weight22;

        public int count => Count;

        public Transform GetTarget(int index) => index switch
        {
            0 => m_target0,
            1 => m_target1,
            2 => m_target2,
            3 => m_target3,
            4 => m_target4,
            5 => m_target5,
            6 => m_target6,
            7 => m_target7,
            8 => m_target8,
            9 => m_target9,
            10 => m_target10,
            11 => m_target11,
            12 => m_target12,
            13 => m_target13,
            14 => m_target14,
            15 => m_target15,
            16 => m_target16,
            17 => m_target17,
            18 => m_target18,
            19 => m_target19,
            20 => m_target20,
            21 => m_target21,
            22 => m_target22,
            _ => null
        };

        // Property name helpers for binding
        public string GetTargetPositionVector3Property(int index) => index switch
        {
            0 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition0)),
            1 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition1)),
            2 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition2)),
            3 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition3)),
            4 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition4)),
            5 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition5)),
            6 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition6)),
            7 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition7)),
            8 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition8)),
            9 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition9)),
            10 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition10)),
            11 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition11)),
            12 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition12)),
            13 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition13)),
            14 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition14)),
            15 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition15)),
            16 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition16)),
            17 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition17)),
            18 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition18)),
            19 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition19)),
            20 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition20)),
            21 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition21)),
            22 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition22)),
            _ => string.Empty
        };

        public string GetTargetRotationVector4Property(int index) => index switch
        {
            0 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation0)),
            1 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation1)),
            2 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation2)),
            3 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation3)),
            4 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation4)),
            5 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation5)),
            6 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation6)),
            7 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation7)),
            8 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation8)),
            9 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation9)),
            10 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation10)),
            11 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation11)),
            12 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation12)),
            13 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation13)),
            14 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation14)),
            15 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation15)),
            16 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation16)),
            17 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation17)),
            18 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation18)),
            19 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation19)),
            20 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation20)),
            21 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation21)),
            22 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation22)),
            _ => string.Empty
        };

        public string GetOffsetRotationVector4Property(int index) => index switch
        {
            0 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation0)),
            1 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation1)),
            2 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation2)),
            3 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation3)),
            4 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation4)),
            5 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation5)),
            6 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation6)),
            7 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation7)),
            8 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation8)),
            9 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation9)),
            10 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation10)),
            11 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation11)),
            12 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation12)),
            13 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation13)),
            14 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation14)),
            15 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation15)),
            16 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation16)),
            17 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation17)),
            18 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation18)),
            19 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation19)),
            20 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation20)),
            21 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation21)),
            22 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation22)),
            _ => string.Empty
        };

        public string GetWeightFloatProperty(int index) => index switch
        {
            0 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight0)),
            1 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight1)),
            2 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight2)),
            3 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight3)),
            4 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight4)),
            5 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight5)),
            6 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight6)),
            7 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight7)),
            8 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight8)),
            9 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight9)),
            10 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight10)),
            11 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight11)),
            12 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight12)),
            13 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight13)),
            14 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight14)),
            15 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight15)),
            16 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight16)),
            17 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight17)),
            18 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight18)),
            19 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight19)),
            20 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight20)),
            21 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight21)),
            22 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight22)),
            _ => string.Empty
        };
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

        [SerializeField] Transform m_LeftToe;
        [SerializeField] Transform m_RightToe;

        [SerializeField] Transform m_RootLeftHand;
        [SerializeField] Transform m_MidLeftHand;
        [SerializeField] Transform m_TipLeftHand;

        [SerializeField] Transform m_RootRightHand;
        [SerializeField] Transform m_MidRightHand;
        [SerializeField] Transform m_TipRightHand;

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
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationEulerHips;
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
        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetLeftHandHint;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftHand;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftHandHint;
        [SyncSceneToStream, SerializeField] public Vector3 TargetPositionRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion TargetRotationRightHand;
        [SyncSceneToStream, SerializeField] public Vector3 HintPositionRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion HintRotationRightHand;
        [SyncSceneToStream, SerializeField] public Vector3 m_CalibratedOffsetRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightHand;
        [SyncSceneToStream, SerializeField] public Vector3 m_HandLocalStart;
        [SyncSceneToStream, SerializeField] public Vector3 m_HandLocalEnd;

        [SyncSceneToStream, SerializeField] public float m_HandSkin;
        [SyncSceneToStream, SerializeField] public bool m_UseHandCapsule;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_HandRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_ChestRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_CollisionSkin;
        [SyncSceneToStream, SerializeField] bool m_CollisionsEnabled;
        [SyncSceneToStream, SerializeField] bool m_ProtectElbow;
        [SyncSceneToStream, SerializeField] bool m_HipsEnabled;

        [SyncSceneToStream, SerializeField] bool m_HintHeadEnabled;
        [SyncSceneToStream, SerializeField] bool m_HeadEnabled;

        [SyncSceneToStream, SerializeField] public bool m_LeftToeEnabled;
        [SyncSceneToStream, SerializeField] public bool m_RightToeEnabled;

        [SyncSceneToStream, SerializeField] bool m_LeftLowerLegEnabled;
        [SyncSceneToStream, SerializeField] bool m_RightLowerLegEnabled;

        [SyncSceneToStream, SerializeField] bool m_HintLeftLowerLegEnabled;
        [SyncSceneToStream, SerializeField] bool m_HintRightLowerLegEnabled;

        [SyncSceneToStream, SerializeField] bool m_EnabledLeftHand;
        [SyncSceneToStream, SerializeField] bool m_EnabledRightHand;

        [SyncSceneToStream, SerializeField] bool m_HintRightHandEnabled;
        [SyncSceneToStream, SerializeField] bool m_HintLeftHandEnabled;

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
        public Transform chestCapsuleStart { get => m_ChestCapsuleStart; set => m_ChestCapsuleStart = value; }
        public Transform chestCapsuleEnd { get => m_ChestCapsuleEnd; set => m_ChestCapsuleEnd = value; }
        public Transform LeftToe { get => m_LeftToe; set => m_LeftToe = value; }
        public Transform RightToe { get => m_RightToe; set => m_RightToe = value; }
        public Transform rootLeftHand { get => m_RootLeftHand; set => m_RootLeftHand = value; }
        public Transform midLeftHand { get => m_MidLeftHand; set => m_MidLeftHand = value; }
        public Transform tipLeftHand { get => m_TipLeftHand; set => m_TipLeftHand = value; }
        public Transform rootRightHand { get => m_RootRightHand; set => m_RootRightHand = value; }
        public Transform midRightHand { get => m_MidRightHand; set => m_MidRightHand = value; }
        public Transform tipRightHand { get => m_TipRightHand; set => m_TipRightHand = value; }

        public string EnabledPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HeadEnabled));
        public string HintWeightBoolPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintHeadEnabled));
        public string TargetPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHead));
        public string TargetRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationHead));
        public string HintPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionHead));
        public string HintRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationHead));
        public string HintDirectionProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintDirection));
        public string EnabledPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftLowerLegEnabled));
        public string HintWeightBoolPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintLeftLowerLegEnabled));
        public string TargetPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootTargetPosition));
        public string TargetRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootTargetRotation));
        public string HintPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionLeftLowerLeg));
        public string HintRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationLeftLowerLeg));
        public string EnabledPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightLowerLegEnabled));
        public string HintWeightBoolPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintRightLowerLegEnabled));
        public string TargetPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootTargetPosition));
        public string TargetRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootTargetRotation));
        public string HintPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionRightLowerLeg));
        public string HintRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationRightLowerLeg));
        public string TargetPositionPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionHips));
        public string TargetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationEulerHips));
        public string OffsetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotationHips));
        public string EnabledPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HipsEnabled));
        public string LeftDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftToeEnabled));
        public string RightDrivenEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightToeEnabled));
        public string LeftDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToePosition));
        public string LeftDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToeRotation));
        public string RightDrivenTargetPosProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToePosition));
        public string RightDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToeRotation));
        public string HintWeightBoolPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintLeftHandEnabled));
        public string TargetPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionLeftHand));
        public string TargetRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationLeftHand));
        public string HintPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionLeftHand));
        public string HintRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationLeftHand));
        public string EnabledPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledRightHand));
        public string EnabledPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledLeftHand));
        public string HintWeightBoolPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintRightHandEnabled));
        public string TargetPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPositionRightHand));
        public string TargetRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotationRightHand));
        public string HintPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintPositionRightHand));
        public string HintRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(HintRotationRightHand));
        public string ChestRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestRadius));
        public string CollisionSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionSkin));
        public string CollisionsEnabledBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionsEnabled));
        public string HandLocalStartVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandLocalStart));
        public string HandLocalEndVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandLocalEnd));
        public string HandRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandRadius));
        public string HandSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandSkin));
        public string UseHandCapsuleBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UseHandCapsule));
        public string ProtectElbowBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ProtectElbow));
        public bool hintWeightHead { get => m_HintHeadEnabled; set => m_HintHeadEnabled = value; }
        public bool enabledHead { get => m_HeadEnabled; set => m_HeadEnabled = value; }
        public bool HintWeightLeftLowerLeg { get => m_HintLeftLowerLegEnabled; set => m_HintLeftLowerLegEnabled = value; }
        public bool EnableLeftLeg { get => m_LeftLowerLegEnabled; set => m_LeftLowerLegEnabled = value; }
        public bool HintWeightRightLowerLeg { get => m_HintRightLowerLegEnabled; set => m_HintRightLowerLegEnabled = value; }
        public bool EnableRightLeg { get => m_RightLowerLegEnabled; set => m_RightLowerLegEnabled = value; }
        public bool enabledHips { get => m_HipsEnabled; set => m_HipsEnabled = value; }
        public bool LeftToeEnabled { get => m_LeftToeEnabled; set => m_LeftToeEnabled = value; }
        public bool RightToeEnabled { get => m_RightToeEnabled; set => m_RightToeEnabled = value; }
        public bool hintWeightLeftHand { get => m_HintLeftHandEnabled; set => m_HintLeftHandEnabled = value; }
        public bool enabledLeftHand { get => m_EnabledLeftHand; set => m_EnabledLeftHand = value; }

        public bool enabledRightHand { get => m_EnabledRightHand; set => m_EnabledRightHand = value; }
        public bool protectElbow { get => m_ProtectElbow; set => m_ProtectElbow = value; }
        public bool hintWeightRightHand { get => m_HintRightHandEnabled; set => m_HintRightHandEnabled = value; }
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
            return head || lLeg || rLeg || lHand || rHand || hipsValid || (m_LeftToe != null) || (m_RightToe != null);
        }

        void IAnimationJobData.SetDefaultValues()
        {
            m_RootHead = m_MidHead = m_TipHead = null;
            m_RootLeftLowerLeg = m_MidLeftLowerLeg = m_TipLeftLowerLeg = null;
            m_RootRightLowerLeg = m_MidRightLowerLeg = m_TipRightLowerLeg = null;

            m_RootLeftHand = m_MidLeftHand = m_TipLeftHand = null;
            m_RootRightHand = m_MidRightHand = m_TipRightHand = null;

            m_Hips = null;

            m_HintHeadEnabled = m_HintLeftLowerLegEnabled = m_HintRightLowerLegEnabled = true;
            m_HeadEnabled = m_LeftLowerLegEnabled = m_RightLowerLegEnabled = true;
            m_HipsEnabled = true;

            m_HintLeftHandEnabled = m_HintRightHandEnabled = true;
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
            m_LeftToe = null;
            m_RightToe = null;

            OutGoingLeftToePosition = OutGoingRightToePosition = Vector3.zero;
            OutGoingLeftToeRotation = OutGoingRightToeRotation = Quaternion.identity;
            m_LeftToeEnabled = false;
            m_RightToeEnabled = false;

            // Chest/hand capsule defaults (left)
            m_ChestCapsuleStart = m_ChestCapsuleEnd = null;
            m_ChestRadius = 0.18f; m_CollisionSkin = 0.02f; m_CollisionsEnabled = true;
            m_HandLocalStart = new Vector3(0f, 0f, -0.05f);
            m_HandLocalEnd = new Vector3(0f, 0f, 0.08f);
            m_HandRadius = 0.05f; m_HandSkin = 0.01f; m_UseHandCapsule = true;
            m_ProtectElbow = true;

            // Positions
            TargetPosition0 = TargetPosition1 = TargetPosition2 = TargetPosition3 = TargetPosition4 =
            TargetPosition5 = TargetPosition6 = TargetPosition7 = TargetPosition8 = TargetPosition9 =
            TargetPosition10 = TargetPosition11 = TargetPosition12 = TargetPosition13 = TargetPosition14 =
            TargetPosition15 = TargetPosition16 = TargetPosition17 = TargetPosition18 = TargetPosition19 =
            TargetPosition20 = TargetPosition21 = TargetPosition22 = Vector3.zero;

            // Rotations
            TargetRotation0 = TargetRotation1 = TargetRotation2 = TargetRotation3 = TargetRotation4 =
            TargetRotation5 = TargetRotation6 = TargetRotation7 = TargetRotation8 = TargetRotation9 =
            TargetRotation10 = TargetRotation11 = TargetRotation12 = TargetRotation13 = TargetRotation14 =
            TargetRotation15 = TargetRotation16 = TargetRotation17 = TargetRotation18 = TargetRotation19 =
            TargetRotation20 = TargetRotation21 = TargetRotation22 = Quaternion.identity;

            // Offsets
            OffsetRotation0 = OffsetRotation1 = OffsetRotation2 = OffsetRotation3 = OffsetRotation4 =
            OffsetRotation5 = OffsetRotation6 = OffsetRotation7 = OffsetRotation8 = OffsetRotation9 =
            OffsetRotation10 = OffsetRotation11 = OffsetRotation12 = OffsetRotation13 = OffsetRotation14 =
            OffsetRotation15 = OffsetRotation16 = OffsetRotation17 = OffsetRotation18 = OffsetRotation19 =
            OffsetRotation20 = OffsetRotation21 = OffsetRotation22 = Quaternion.identity;

            // Weights default to disabled
            Weight0 = Weight1 = Weight2 = Weight3 = Weight4 =
            Weight5 = Weight6 = Weight7 = Weight8 = Weight9 =
            Weight10 = Weight11 = Weight12 = Weight13 = Weight14 =
            Weight15 = Weight16 = Weight17 = Weight18 = Weight19 =
            Weight20 = Weight21 = Weight22 = false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetPosition(int idx, in Vector3 v)
        {
            switch (idx)
            {
                case 0: TargetPosition0 = v; break;
                case 1: TargetPosition1 = v; break;
                case 2: TargetPosition2 = v; break;
                case 3: TargetPosition3 = v; break;
                case 4: TargetPosition4 = v; break;
                case 5: TargetPosition5 = v; break;
                case 6: TargetPosition6 = v; break;
                case 7: TargetPosition7 = v; break;
                case 8: TargetPosition8 = v; break;
                case 9: TargetPosition9 = v; break;
                case 10: TargetPosition10 = v; break;
                case 11: TargetPosition11 = v; break;
                case 12: TargetPosition12 = v; break;
                case 13: TargetPosition13 = v; break;
                case 14: TargetPosition14 = v; break;
                case 15: TargetPosition15 = v; break;
                case 16: TargetPosition16 = v; break;
                case 17: TargetPosition17 = v; break;
                case 18: TargetPosition18 = v; break;
                case 19: TargetPosition19 = v; break;
                case 20: TargetPosition20 = v; break;
                case 21: TargetPosition21 = v; break;
                case 22: TargetPosition22 = v; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetRotation(int idx, in Quaternion q)
        {
            switch (idx)
            {
                case 0: TargetRotation0 = q; break;
                case 1: TargetRotation1 = q; break;
                case 2: TargetRotation2 = q; break;
                case 3: TargetRotation3 = q; break;
                case 4: TargetRotation4 = q; break;
                case 5: TargetRotation5 = q; break;
                case 6: TargetRotation6 = q; break;
                case 7: TargetRotation7 = q; break;
                case 8: TargetRotation8 = q; break;
                case 9: TargetRotation9 = q; break;
                case 10: TargetRotation10 = q; break;
                case 11: TargetRotation11 = q; break;
                case 12: TargetRotation12 = q; break;
                case 13: TargetRotation13 = q; break;
                case 14: TargetRotation14 = q; break;
                case 15: TargetRotation15 = q; break;
                case 16: TargetRotation16 = q; break;
                case 17: TargetRotation17 = q; break;
                case 18: TargetRotation18 = q; break;
                case 19: TargetRotation19 = q; break;
                case 20: TargetRotation20 = q; break;
                case 21: TargetRotation21 = q; break;
                case 22: TargetRotation22 = q; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOffsetRotation(int idx, in Quaternion q)
        {
            switch (idx)
            {
                case 0: OffsetRotation0 = q; break;
                case 1: OffsetRotation1 = q; break;
                case 2: OffsetRotation2 = q; break;
                case 3: OffsetRotation3 = q; break;
                case 4: OffsetRotation4 = q; break;
                case 5: OffsetRotation5 = q; break;
                case 6: OffsetRotation6 = q; break;
                case 7: OffsetRotation7 = q; break;
                case 8: OffsetRotation8 = q; break;
                case 9: OffsetRotation9 = q; break;
                case 10: OffsetRotation10 = q; break;
                case 11: OffsetRotation11 = q; break;
                case 12: OffsetRotation12 = q; break;
                case 13: OffsetRotation13 = q; break;
                case 14: OffsetRotation14 = q; break;
                case 15: OffsetRotation15 = q; break;
                case 16: OffsetRotation16 = q; break;
                case 17: OffsetRotation17 = q; break;
                case 18: OffsetRotation18 = q; break;
                case 19: OffsetRotation19 = q; break;
                case 20: OffsetRotation20 = q; break;
                case 21: OffsetRotation21 = q; break;
                case 22: OffsetRotation22 = q; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWeight(int idx, bool State)
        {
            switch (idx)
            {
                case 0: Weight0 = State; break;
                case 1: Weight1 = State; break;
                case 2: Weight2 = State; break;
                case 3: Weight3 = State; break;
                case 4: Weight4 = State; break;
                case 5: Weight5 = State; break;
                case 6: Weight6 = State; break;
                case 7: Weight7 = State; break;
                case 8: Weight8 = State; break;
                case 9: Weight9 = State; break;
                case 10: Weight10 = State; break;
                case 11: Weight11 = State; break;
                case 12: Weight12 = State; break;
                case 13: Weight13 = State; break;
                case 14: Weight14 = State; break;
                case 15: Weight15 = State; break;
                case 16: Weight16 = State; break;
                case 17: Weight17 = State; break;
                case 18: Weight18 = State; break;
                case 19: Weight19 = State; break;
                case 20: Weight20 = State; break;
                case 21: Weight21 = State; break;
                case 22: Weight22 = State; break;
            }
        }
    }
    public interface IBasisFullBodyData
    {
        int count { get; }
        Transform GetTarget(int index);

        string GetTargetPositionVector3Property(int index);
        string GetTargetRotationVector4Property(int index);
        string GetOffsetRotationVector4Property(int index);
        string GetWeightFloatProperty(int index);
    }
    [DisallowMultipleComponent]
    [AddComponentMenu("Animation Rigging/Basis FullBody IK")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/index.html")]
    public class BasisFullBodyIK : RigConstraint<BasisFullIKConstraintJob, BasisFullBodyData, BasisFullBodyJobBinder>
    {
        protected override void OnValidate()
        {
            base.OnValidate();
            // force serialize dirty for animated bools
            m_Data.hintWeightHead = m_Data.hintWeightHead;
            m_Data.HintWeightLeftLowerLeg = m_Data.HintWeightLeftLowerLeg;
            m_Data.HintWeightRightLowerLeg = m_Data.HintWeightRightLowerLeg;
            m_Data.enabledHips = m_Data.enabledHips;

            // new toggles
            m_Data.LeftToeEnabled = m_Data.LeftToeEnabled;
            m_Data.RightToeEnabled = m_Data.RightToeEnabled;

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

        public ReadOnlyTransformHandle chestStart, chestEnd;
        public Vector3Property handLocalStart, handLocalEnd;
        public FloatProperty jobWeight { get; set; }


        // 23 handles
        public ReadWriteTransformHandle
            h0, h1, h2, h3, h4, h5, h6, h7, h8, h9,
            h10, h11, h12, h13, h14, h15, h16, h17, h18, h19,
            h20, h21, h22;

        // 23 position properties
        public Vector3Property
            p0, p1, p2, p3, p4, p5, p6, p7, p8, p9,
            p10, p11, p12, p13, p14, p15, p16, p17, p18, p19,
            p20, p21, p22;

        // 23 rotation properties (Quaternion as Vector4)
        public Vector4Property
            r0, r1, r2, r3, r4, r5, r6, r7, r8, r9,
            r10, r11, r12, r13, r14, r15, r16, r17, r18, r19,
            r20, r21, r22;

        // 23 offset rotation properties
        public Vector4Property
            o0, o1, o2, o3, o4, o5, o6, o7, o8, o9,
            o10, o11, o12, o13, o14, o15, o16, o17, o18, o19,
            o20, o21, o22;

        // 23 per-slot weights
        public BoolProperty
            w0, w1, w2, w3, w4, w5, w6, w7, w8, w9,
            w10, w11, w12, w13, w14, w15, w16, w17, w18, w19,
            w20, w21, w22;


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
                {
                    BasisAnimationRuntimeUtils.PassThrough(stream, leftDrivenHandle);
                }

                if (rightDrivenHandle.IsValid(stream))
                {
                    BasisAnimationRuntimeUtils.PassThrough(stream, rightDrivenHandle);
                }

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
            ApplyToeRotation(stream, LeftToggle, leftDrivenHandle, leftDrivenTargetPos, leftDrivenTargetRot);
            ApplyToeRotation(stream, RightToggle, rightDrivenHandle, rightDrivenTargetPos, rightDrivenTargetRot);

            Apply(stream, h0, p0, r0, o0, w0);
            Apply(stream, h1, p1, r1, o1, w1);
            Apply(stream, h2, p2, r2, o2, w2);
            Apply(stream, h3, p3, r3, o3, w3);
            Apply(stream, h4, p4, r4, o4, w4);
            Apply(stream, h5, p5, r5, o5, w5);
            Apply(stream, h6, p6, r6, o6, w6);
            Apply(stream, h7, p7, r7, o7, w7);
            Apply(stream, h8, p8, r8, o8, w8);
            Apply(stream, h9, p9, r9, o9, w9);
            Apply(stream, h10, p10, r10, o10, w10);
            Apply(stream, h11, p11, r11, o11, w11);
            Apply(stream, h12, p12, r12, o12, w12);
            Apply(stream, h13, p13, r13, o13, w13);
            Apply(stream, h14, p14, r14, o14, w14);
            Apply(stream, h15, p15, r15, o15, w15);
            Apply(stream, h16, p16, r16, o16, w16);
            Apply(stream, h17, p17, r17, o17, w17);
            Apply(stream, h18, p18, r18, o18, w18);
            Apply(stream, h19, p19, r19, o19, w19);
            Apply(stream, h20, p20, r20, o20, w20);
            Apply(stream, h21, p21, r21, o21, w21);
            Apply(stream, h22, p22, r22, o22, w22);
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

        static void ApplyToeRotation(
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
        static void Pass(AnimationStream stream, ReadWriteTransformHandle h)
        {
            if (h.IsValid(stream))
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, h);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Apply(AnimationStream stream, ReadWriteTransformHandle h, Vector3Property p, Vector4Property r, Vector4Property o, BoolProperty sw)
        {
            if (h.IsValid(stream))
            {
                if (sw.Get(stream))
                {

                    Vector3 targetPos = p.Get(stream);
                    Vector4 rv4 = r.Get(stream);
                    Vector4 ov4 = o.Get(stream);

                    Quaternion targetRot = new Quaternion(rv4.x, rv4.y, rv4.z, rv4.w);
                    Quaternion offsetRot = new Quaternion(ov4.x, ov4.y, ov4.z, ov4.w);

                    Quaternion finalRot = targetRot * offsetRot;

                    h.SetPosition(stream, targetPos);
                    h.SetRotation(stream, finalRot);
                }
                else
                {
                    BasisAnimationRuntimeUtils.PassThrough(stream, h);
                }
            }
        }
    }

    public class BasisFullBodyJobBinder : AnimationJobBinder<BasisFullIKConstraintJob, BasisFullBodyData>
    {
        private static ReadOnlyTransformHandle SafeBindRO(Animator animator, Transform t) =>
            t != null ? ReadOnlyTransformHandle.Bind(animator, t) : default;

        public override BasisFullIKConstraintJob Create(Animator animator, ref BasisFullBodyData data, Component component)
        {
            var job = new BasisFullIKConstraintJob
            {
                // Hips
                hipsHandle = BindHandle(animator, data.hips),
                targetPositionHips = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHips),
                targetRotationHips = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHips),
                offsetRotationHips = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyHips),
                enabledHips = BoolProperty.Bind(animator, component, data.EnabledPropertyHips),

                // Head
                rootHead = BindHandle(animator, data.rootHead),
                midHead = BindHandle(animator, data.midHead),
                tipHead = BindHandle(animator, data.tipHead),

                targetPositionHead = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHead),
                targetRotationHead = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHead),
                hintPositionHead = Vector3Property.Bind(animator, component, data.HintPositionPropertyHead),
                hintRotationHead = Vector4Property.Bind(animator, component, data.HintRotationPropertyHead),
                bendNormalHead = Vector3Property.Bind(animator, component, data.HintDirectionProperty),

                hintWeightHead = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyHead),
                enabledHead = BoolProperty.Bind(animator, component, data.EnabledPropertyHead),

                targetOffsetHead = new AffineTransform(data.m_CalibratedOffsetHead, data.m_CalibratedRotationHead),

                // Left Lower Leg
                rootLeftLowerLeg = BindHandle(animator, data.rootLeftLowerLeg),
                midLeftLowerLeg = BindHandle(animator, data.midLeftLowerLeg),
                tipLeftLowerLeg = BindHandle(animator, data.tipLeftLowerLeg),

                targetPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyLeftLowerLeg),
                targetRotationLeftLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyLeftLowerLeg),
                hintPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyLeftLowerLeg),
                hintRotationLeftLowerLeg = Vector4Property.Bind(animator, component, data.HintRotationPropertyLeftLowerLeg),

                hintWeightLeftLowerLeg = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyLeftLowerLeg),
                enabledLeftLowerLeg = BoolProperty.Bind(animator, component, data.EnabledPropertyLeftLowerLeg),

                targetOffsetLeftLowerLeg = new AffineTransform(data.m_CalibratedOffsetLeftLowerLeg, data.m_CalibratedRotationLeftLowerLeg),

                // Right Lower Leg
                rootRightLowerLeg = BindHandle(animator, data.rootRightLowerLeg),
                midRightLowerLeg = BindHandle(animator, data.midRightLowerLeg),
                tipRightLowerLeg = BindHandle(animator, data.tipRightLowerLeg),

                targetPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyRightLowerLeg),
                targetRotationRightLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyRightLowerLeg),
                hintPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyRightLowerLeg),
                hintRotationRightLowerLeg = Vector4Property.Bind(animator, component, data.HintRotationPropertyRightLowerLeg),

                hintWeightRightLowerLeg = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyRightLowerLeg),
                enabledRightLowerLeg = BoolProperty.Bind(animator, component, data.EnabledPropertyRightLowerLeg),

                targetOffsetRightLowerLeg = new AffineTransform(data.m_CalibratedOffsetRightLowerLeg, data.m_CalibratedRotationRightLowerLeg),

                // Integrated Dual "Driven TR"
                leftDrivenHandle = BindHandle(animator, data.LeftToe),
                rightDrivenHandle = BindHandle(animator, data.RightToe),

                leftDrivenTargetPos = Vector3Property.Bind(animator, component, data.LeftDrivenTargetPosProperty),
                leftDrivenTargetRot = Vector4Property.Bind(animator, component, data.LeftDrivenTargetRotProperty),
                rightDrivenTargetPos = Vector3Property.Bind(animator, component, data.RightDrivenTargetPosProperty),
                rightDrivenTargetRot = Vector4Property.Bind(animator, component, data.RightDrivenTargetRotProperty),

                LeftToggle = BoolProperty.Bind(animator, component, data.LeftDrivenEnabledProperty),
                RightToggle = BoolProperty.Bind(animator, component, data.RightDrivenEnabledProperty),

                // Left Hand
                rootLeftHand = BindHandle(animator, data.rootLeftHand),
                midLeftHand = BindHandle(animator, data.midLeftHand),
                tipLeftHand = BindHandle(animator, data.tipLeftHand),

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
                rootRightHand = BindHandle(animator, data.rootRightHand),
                midRightHand = BindHandle(animator, data.midRightHand),
                tipRightHand = BindHandle(animator, data.tipRightHand),

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
            // Bind 23 handles
            job.h0 = BindHandle(animator, data.GetTarget(0));
            job.h1 = BindHandle(animator, data.GetTarget(1));
            job.h2 = BindHandle(animator, data.GetTarget(2));
            job.h3 = BindHandle(animator, data.GetTarget(3));
            job.h4 = BindHandle(animator, data.GetTarget(4));
            job.h5 = BindHandle(animator, data.GetTarget(5));
            job.h6 = BindHandle(animator, data.GetTarget(6));
            job.h7 = BindHandle(animator, data.GetTarget(7));
            job.h8 = BindHandle(animator, data.GetTarget(8));
            job.h9 = BindHandle(animator, data.GetTarget(9));
            job.h10 = BindHandle(animator, data.GetTarget(10));
            job.h11 = BindHandle(animator, data.GetTarget(11));
            job.h12 = BindHandle(animator, data.GetTarget(12));
            job.h13 = BindHandle(animator, data.GetTarget(13));
            job.h14 = BindHandle(animator, data.GetTarget(14));
            job.h15 = BindHandle(animator, data.GetTarget(15));
            job.h16 = BindHandle(animator, data.GetTarget(16));
            job.h17 = BindHandle(animator, data.GetTarget(17));
            job.h18 = BindHandle(animator, data.GetTarget(18));
            job.h19 = BindHandle(animator, data.GetTarget(19));
            job.h20 = BindHandle(animator, data.GetTarget(20));
            job.h21 = BindHandle(animator, data.GetTarget(21));
            job.h22 = BindHandle(animator, data.GetTarget(22));

            // Bind positions
            job.p0 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(0));
            job.p1 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(1));
            job.p2 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(2));
            job.p3 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(3));
            job.p4 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(4));
            job.p5 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(5));
            job.p6 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(6));
            job.p7 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(7));
            job.p8 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(8));
            job.p9 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(9));
            job.p10 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(10));
            job.p11 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(11));
            job.p12 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(12));
            job.p13 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(13));
            job.p14 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(14));
            job.p15 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(15));
            job.p16 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(16));
            job.p17 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(17));
            job.p18 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(18));
            job.p19 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(19));
            job.p20 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(20));
            job.p21 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(21));
            job.p22 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(22));

            // Bind rotations (as Vector4)
            job.r0 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(0));
            job.r1 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(1));
            job.r2 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(2));
            job.r3 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(3));
            job.r4 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(4));
            job.r5 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(5));
            job.r6 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(6));
            job.r7 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(7));
            job.r8 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(8));
            job.r9 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(9));
            job.r10 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(10));
            job.r11 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(11));
            job.r12 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(12));
            job.r13 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(13));
            job.r14 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(14));
            job.r15 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(15));
            job.r16 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(16));
            job.r17 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(17));
            job.r18 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(18));
            job.r19 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(19));
            job.r20 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(20));
            job.r21 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(21));
            job.r22 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(22));

            // Bind offsets
            job.o0 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(0));
            job.o1 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(1));
            job.o2 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(2));
            job.o3 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(3));
            job.o4 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(4));
            job.o5 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(5));
            job.o6 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(6));
            job.o7 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(7));
            job.o8 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(8));
            job.o9 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(9));
            job.o10 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(10));
            job.o11 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(11));
            job.o12 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(12));
            job.o13 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(13));
            job.o14 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(14));
            job.o15 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(15));
            job.o16 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(16));
            job.o17 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(17));
            job.o18 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(18));
            job.o19 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(19));
            job.o20 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(20));
            job.o21 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(21));
            job.o22 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(22));

            // Bind per-slot weights
            job.w0 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(0));
            job.w1 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(1));
            job.w2 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(2));
            job.w3 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(3));
            job.w4 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(4));
            job.w5 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(5));
            job.w6 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(6));
            job.w7 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(7));
            job.w8 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(8));
            job.w9 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(9));
            job.w10 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(10));
            job.w11 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(11));
            job.w12 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(12));
            job.w13 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(13));
            job.w14 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(14));
            job.w15 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(15));
            job.w16 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(16));
            job.w17 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(17));
            job.w18 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(18));
            job.w19 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(19));
            job.w20 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(20));
            job.w21 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(21));
            job.w22 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(22));
            return job;
        }
        static ReadWriteTransformHandle BindHandle(Animator animator, Transform t)
    => (t != null) ? ReadWriteTransformHandle.Bind(animator, t) : default;
        public override void Destroy(BasisFullIKConstraintJob job) { }
    }
}
