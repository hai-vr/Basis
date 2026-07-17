using System.Runtime.CompilerServices;
using Unity.Collections;
namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
    [System.Serializable]
    public struct BasisFullBodyData : IAnimationJobData, IBasisFullBodyData
    {
        public const int Count = 22;

        // Live target positions (Vector3) pushed every frame from the manager.
        [SyncSceneToStream, SerializeField]
        public Vector3
            TargetPosition0, TargetPosition1, TargetPosition2, TargetPosition3, TargetPosition4,
            TargetPosition5, TargetPosition6, TargetPosition7, TargetPosition8, TargetPosition9,
            TargetPosition10, TargetPosition11, TargetPosition12, TargetPosition13, TargetPosition14,
            TargetPosition15, TargetPosition16, TargetPosition17, TargetPosition18, TargetPosition19,
            TargetPosition20, TargetPosition54;

        // Live target rotations (Quaternion) — stored as Quaternion on the component; bound as Vector4 by the job.
        [SyncSceneToStream, SerializeField]
        public Quaternion
            TargetRotation0, TargetRotation1, TargetRotation2, TargetRotation3, TargetRotation4,
            TargetRotation5, TargetRotation6, TargetRotation7, TargetRotation8, TargetRotation9,
            TargetRotation10, TargetRotation11, TargetRotation12, TargetRotation13, TargetRotation14,
            TargetRotation15, TargetRotation16, TargetRotation17, TargetRotation18, TargetRotation19,
            TargetRotation20, TargetRotation54;

        // Calibration offsets (applied on top of target each frame) — final = target * offset
        [SyncSceneToStream, SerializeField]
        public Quaternion
            OffsetRotation0, OffsetRotation1, OffsetRotation2, OffsetRotation3, OffsetRotation4,
            OffsetRotation5, OffsetRotation6, OffsetRotation7, OffsetRotation8, OffsetRotation9,
            OffsetRotation10, OffsetRotation11, OffsetRotation12, OffsetRotation13, OffsetRotation14,
            OffsetRotation15, OffsetRotation16, OffsetRotation17, OffsetRotation18, OffsetRotation19,
            OffsetRotation20, OffsetRotation54;

        // Per-slot enable/weights (0..1). Allows toggling bones independently within a single job.
        [SyncSceneToStream, SerializeField]
        public bool
            Weight0, Weight1, Weight2, Weight3, Weight4,
            Weight5, Weight6, Weight7, Weight8, Weight9,
            Weight10, Weight11, Weight12, Weight13, Weight14,
            Weight15, Weight16, Weight17, Weight18, Weight19,
            Weight20, Weight54;

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
            54 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetPosition54)),
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
            54 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(TargetRotation54)),
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
            54 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotation54)),
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
            54 => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(Weight54)),
            _ => string.Empty
        };
        [SerializeField] Transform m_Hips;
        [SyncSceneToStream, SerializeField] Transform m_chest;
        [SyncSceneToStream, SerializeField] Transform m_neck;
        [SerializeField] Transform m_head;

        [SerializeField] Transform m_LeftUpperLeg;
        [SerializeField] Transform m_LeftLowerLeg;
        [SerializeField] Transform m_leftFoot;
        [SerializeField] Transform m_RightUpperLeg;
        [SerializeField] Transform m_RightLowerLeg;
        [SerializeField] Transform m_RightFoot;

        [SerializeField] Transform m_LeftToe;
        [SerializeField] Transform m_RightToe;

        [SerializeField] Transform m_leftUpperArm;
        [SerializeField] Transform m_leftLowerArm;
        [SerializeField] Transform m_leftHand;

        [SerializeField] Transform m_RightUpperArm;
        [SerializeField] Transform m_RightLowerArm;
        [SerializeField] Transform m_rightHand;

        [SerializeField] Transform m_Spine;
        [SerializeField] Transform m_UpperChest;
        [SerializeField] Transform m_LeftShoulder;
        [SerializeField] Transform m_RightShoulder;

        // Twist bones — derived bones that absorb a fraction of wrist/elbow roll for natural
        // forearm/upper-arm deformation. Optional per rig; when null, the side is skipped.
        [SerializeField] Transform m_LeftUpperArmTwist;
        [SerializeField] Transform m_LeftLowerArmTwist;
        [SerializeField] Transform m_RightUpperArmTwist;
        [SerializeField] Transform m_RightLowerArmTwist;

        // Head
        [SyncSceneToStream, SerializeField] public Vector3 PositionHead;
        [SyncSceneToStream, SerializeField] public Quaternion RotationHead;
        [SyncSceneToStream, SerializeField] public Vector3 ChestPosition;
        // The chest bone's ACTUAL position, WITHOUT the chest-as-head-hint bias that ChestPosition carries
        // (that bias pushes ~8cm 'up in chest frame' to steer the head solve -- see BasisAvatarIKStageCalibration).
        // The chest IK target must pin to the real chest, not the hinted one, or it hauls the torso up = a lean.
        [SyncSceneToStream, SerializeField] public Vector3 ChestPositionRaw;
        [SyncSceneToStream, SerializeField] public Quaternion ChestRotation;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationHead;

        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightToe;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftToe;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationChest;

        [SyncSceneToStream, SerializeField] public Quaternion LeftShoulderRotation;
        [SyncSceneToStream, SerializeField] public Quaternion RightShoulderRotation;

        // Hips
        [SyncSceneToStream, SerializeField] public Vector3 PositionHips;
        [SyncSceneToStream, SerializeField] public Quaternion RotationHips;
        [SyncSceneToStream, SerializeField] public Quaternion OffsetRotationHips;

        // Left Leg
        [SyncSceneToStream, SerializeField] public Vector3 LeftFootPosition;
        [SyncSceneToStream, SerializeField] public Quaternion LeftFootRotation;
        [SyncSceneToStream, SerializeField] public Vector3 PositionLeftLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion M_CalibrationLeftFootRotation;

        // Right Leg
        [SyncSceneToStream, SerializeField] public Vector3 RightFootPosition;
        [SyncSceneToStream, SerializeField] public Quaternion RightFootRotation;
        [SyncSceneToStream, SerializeField] public Vector3 PositionRightLowerLeg;
        [SyncSceneToStream, SerializeField] public Quaternion M_CalibrationRightFootRotation;

        // Toes
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingLeftToeRotation;
        [SyncSceneToStream, SerializeField] public Quaternion OutGoingRightToeRotation;

        // Left Hand
        [SyncSceneToStream, SerializeField] public Vector3 PositionLeftHand;
        [SyncSceneToStream, SerializeField] public Quaternion RotationLeftHand;
        [SyncSceneToStream, SerializeField] public Vector3 LeftLowerArmPosition;
        [SyncSceneToStream, SerializeField] public Quaternion LeftLowerArmRotation;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftHand;

        // Right Hand
        [SyncSceneToStream, SerializeField] public Vector3 PositionRightHand;
        [SyncSceneToStream, SerializeField] public Quaternion RotationRightHand;
        [SyncSceneToStream, SerializeField] public Vector3 RightLowerArmPosition;
        [SyncSceneToStream, SerializeField] public Quaternion RightLowerArmRotation;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightHand;

        // Misc
        [SyncSceneToStream, SerializeField] public Vector3 PlayerUp;

        [SyncSceneToStream, SerializeField] public Vector3 KneeBendPrefLeft;
        [SyncSceneToStream, SerializeField] public Vector3 KneeBendPrefRight;

        [SyncSceneToStream, SerializeField] public float m_HandSkin;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_HandRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_ChestRadius;
        [SyncSceneToStream, SerializeField, Min(0f)] public float m_CollisionSkin;
        [SyncSceneToStream, SerializeField] bool m_CollisionsEnabled;
        [SyncSceneToStream, SerializeField] bool m_ProtectElbow;
        [SyncSceneToStream, SerializeField] bool m_CollideTrackedElbow;

        [SyncSceneToStream, SerializeField] bool m_HintHeadEnabled;
        [SyncSceneToStream, SerializeField] bool m_SpineIKEnabled;
        [SyncSceneToStream, SerializeField] bool m_HasHipsTracker;

        // IK Lock Mode: 0 = LockHips, 1 = LockHead, 2 = LockBoth (see BasisIKLockMode enum)
        [SyncSceneToStream, SerializeField] float m_IKLockMode;

        [SyncSceneToStream, SerializeField] public bool m_LeftToeEnabled;
        [SyncSceneToStream, SerializeField] public bool m_RightToeEnabled;

        [SyncSceneToStream, SerializeField] float m_LeftLowerLegEnabled;
        [SyncSceneToStream, SerializeField] float m_RightLowerLegEnabled;

        [SyncSceneToStream, SerializeField] float m_HintLeftLowerLegEnabled;
        [SyncSceneToStream, SerializeField] float m_HintRightLowerLegEnabled;

        // True when the knee/lower-leg hint is a physical tracker (jittery, and pole-amplified by the leg
        // solve) rather than a computed hint (foot driver / butterfly). Gates the tracked-knee output-swivel
        // smoothing in SolveLegs -- see SmoothKneeSwivel.
        [SyncSceneToStream, SerializeField] bool m_LeftLowerLegHintIsTracker;
        [SyncSceneToStream, SerializeField] bool m_RightLowerLegHintIsTracker;

        // Hand IK weight (0..1), not a toggle: the webcam fades the hands in and out as tracking comes and
        // goes, and a hard on/off pops the arm. Mirrors the legs, which have been fractional all along.
        [SyncSceneToStream, SerializeField] float m_EnabledLeftHand;
        [SyncSceneToStream, SerializeField] float m_EnabledRightHand;

        [SyncSceneToStream, SerializeField] bool m_HintRightHandEnabled;
        [SyncSceneToStream, SerializeField] bool m_HintLeftHandEnabled;

        [SyncSceneToStream, SerializeField] float m_MinHeadSpineHeight;
        [SyncSceneToStream, SerializeField] public bool m_enabledLeftShoulder;
        [SyncSceneToStream, SerializeField] public bool m_enabledRightShoulder;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationRightShoulder;
        [SyncSceneToStream, SerializeField] public Quaternion m_CalibratedRotationLeftShoulder;

        [SyncSceneToStream, SerializeField] public float m_MaxBendDeg;
        [SyncSceneToStream, SerializeField] public float m_MinFactor;
        [SyncSceneToStream, SerializeField] public float m_MaxFactor;
        [SyncSceneToStream, SerializeField] public float m_MaxChestDeltaDeg;

        // Shoulder pre-solve: raises/protracts shoulders based on hand target
        [SyncSceneToStream, SerializeField] bool m_ShoulderSolveEnabled;
        [SyncSceneToStream, SerializeField] bool m_ShoulderShrugEnabled;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_ShoulderElevationFactor;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_ShoulderProtractionFactor;

        // Spine bend distribution: per-axis fractions of the hips→head bend pre-applied to lumbar
        // and thoracic joints before the chest→neck→head two-bone solve. Splitting by axis lets
        // forward bend, side bend, and twist be tuned independently — humans are very anisotropic.
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineBendPitch;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineBendYaw;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineBendRoll;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_UpperChestBendPitch;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_UpperChestBendYaw;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_UpperChestBendRoll;
        // Hip hinge: when forward lean exceeds the start angle, the pelvis pitches forward by a
        // capped fraction of the excess so the spine doesn't have to swallow the whole reach.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_HipHingeStartDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_HipHingeMaxAddDeg;
        // Chest follow spring: critically-damped second-order spring on the head target before it
        // is consumed by DistributeSpineBend, so quick head turns leave the body momentarily behind.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_ChestSpringHz;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_ChestSpringDamping;
        // Asymmetric flexion clamps: humans flex forward much further than they extend backward.
        // Applied to the per-axis spine + upperChest contributions after distribution.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_SpineMaxForwardDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_SpineMaxBackwardDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_SpineMaxLateralDeg;
        // Squish coupling: scales per-axis bend weights by the head-to-hips compression ratio so
        // the spine folds more when crouched and straightens when reaching up. 0 disables.
        [SyncSceneToStream, SerializeField, Range(0f, 2f)] float m_SpineSquishBoost;
        // How much the chest FOLLOWS the gaze (no chest tracker). 0 = rigid (the look-down-stability fix,
        // chest never folds on a pure look-down); 1 = full follow (the old phantom-lean). A small value is
        // 'a little real spine': the chest folds a touch when you look down, which reads better on desktop.
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineGazeFollow;
        // How much EXTRA forward neck curve to add on a look-down (no chest tracker). Same idea as the
        // chest gaze-follow, but the neck's lordosis runs AFTER the head-placing CCD, so this is a
        // cosmetic post-solve curve -- it nudges the head BONE a touch (the camera rides the HMD target).
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_NeckGazeFollow;
        [SyncSceneToStream, SerializeField, Range(0f, 2f)] float m_MoveBodyBackWhenCrouching;
        // Elbow/knee swing smoothing: max swing speed (deg/s) around the root→tip axis. Lower =
        // smoother (more lag) so a torso-collision change eases in; 0 disables. See ApplySwingContinuity.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_SwingSmoothRateDeg;
        // Arm-swing chest follow: when hand targets shift laterally, the chest yaws to follow so
        // gestures and walking arm-swing don't read as a stiff torso. Only used without a chest
        // tracker — when one is present, it owns chest rotation directly.
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_ChestArmSwingFactor;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_ChestArmSwingMaxDeg;
        // Arm twist distribution: fractions of the wrist/elbow roll absorbed by the optional
        // forearm/upper-arm twist bones. Without these, the wrist eats 100% of the roll and the
        // mesh pinches around the elbow ("candy-wrap" deformation).
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_LowerArmTwistFraction;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_UpperArmTwistFraction;

        // Anatomy: IK refinements modeled on real biomechanics. Each toggle gates its own
        // solver pass; all on by default.
        [SyncSceneToStream, SerializeField] bool m_AnatDifferentialStiffness;
        [SyncSceneToStream, SerializeField] bool m_AnatShoulderSlide;
        [SyncSceneToStream, SerializeField] bool m_AnatCervicalLordosis;
        [SyncSceneToStream, SerializeField] bool m_AnatPelvicTwistRouting;
        // The anatomical range-of-motion envelope on every solved vertebra. Default ON: what it replaces
        // is not a safe fallback, it is a measured error (BasisSpineAnatomy).
        [SyncSceneToStream, SerializeField] bool m_SpineAnatomicalRom;
        // The chest as a secondary IK target (SolveChestTarget). Default ON.
        [SyncSceneToStream, SerializeField] bool m_ChestIKTarget;
        // Low-pass the knee swivel (leg roll about the hip->foot axis) on the no-foot-tracker path so a
        // near-straight standing leg doesn't twist with hips-yaw jitter. Off => identical to before.
        [SyncSceneToStream, SerializeField] bool m_LegSwivelSmoothing;
        // Cervical lordosis pitch coupling: extra forward bend per unit of head pitch-down (0..1
        // where 1 = looking straight down). Multiplied by the gain in degrees. Only used when
        // AnatCervicalLordosis is on.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisPitchGainDeg;
        // Cervical lordosis shaping (previously hardcoded consts in ApplyCervicalLordosis). Base
        // bend held in a neutral pose and how it splits between neck and upperChest; the head pitch
        // clamp; and the "extreme look" onset/full window that drives extra spine roll plus
        // hips/chest counter-translation when looking far up or down. Down* are meters of vertical
        // shift at full look-down; *LookUp are the much smaller shift when looking up. Only used
        // when AnatCervicalLordosis is on.
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisBaseDeg;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_LordosisNeckShare;
        [SyncSceneToStream, SerializeField, Range(0f, 90f)] float m_LordosisMaxHeadPitchDeg;
        [SyncSceneToStream, SerializeField, Range(0f, 90f)] float m_LordosisExtremeStartDeg;
        [SyncSceneToStream, SerializeField, Range(0f, 90f)] float m_LordosisExtremeFullDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeRollForwardMaxDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeRollBackwardMaxDeg;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeHipsHorizontalMax;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeChestHorizontalMax;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeHipsDownMax;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeChestDownMax;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeHipsDownLookUp;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_LordosisExtremeChestDownLookUp;

        // Spine CCD solve: per-iteration under-relaxation (1 = full step) and the neck's max bend
        // cone vs the chest→neck direction, which stops the short neck bone overbending.
        [SyncSceneToStream, SerializeField, Range(0.1f, 1f)] float m_SpineCCDRelax;
        [SyncSceneToStream, SerializeField, Min(0f)] float m_NeckMaxConeDeg;
        // Axial twist the spine CCD reach may use, about the body's hips-up axis, graded down the chain:
        // m_SpineTwistKeep is the lumbar (lower-back) end -- near-rigid in reality -- and m_SpineNeckTwistKeep
        // the cervical (neck) end, which rotates freely; the joints between interpolate. Lower = a sideways
        // head reach bends instead of corkscrewing (the corkscrew flips sign across center). Hips-up, not
        // world-up, so it stays correct lying down.
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineTwistKeep;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] float m_SpineNeckTwistKeep;
        public float minHeadSpineHeight{  get => m_MinHeadSpineHeight; set => m_MinHeadSpineHeight = value; }
        public Transform chest { get => m_chest; set => m_chest = value; }
        public Transform neck { get => m_neck; set => m_neck = value; }
        public Transform head { get => m_head; set => m_head = value; }
        public Transform LeftUpperLeg { get => m_LeftUpperLeg; set => m_LeftUpperLeg = value; }
        public Transform LeftLowerLeg { get => m_LeftLowerLeg; set => m_LeftLowerLeg = value; }
        public Transform leftFoot { get => m_leftFoot; set => m_leftFoot = value; }
        public Transform RightUpperLeg { get => m_RightUpperLeg; set => m_RightUpperLeg = value; }
        public Transform RightLowerLeg { get => m_RightLowerLeg; set => m_RightLowerLeg = value; }
        public Transform RightFoot { get => m_RightFoot; set => m_RightFoot = value; }
        public Transform hips { get => m_Hips; set => m_Hips = value; }
        public Transform LeftToe { get => m_LeftToe; set => m_LeftToe = value; }
        public Transform RightToe { get => m_RightToe; set => m_RightToe = value; }
        public Transform leftUpperArm { get => m_leftUpperArm; set => m_leftUpperArm = value; }
        public Transform leftLowerArm { get => m_leftLowerArm; set => m_leftLowerArm = value; }
        public Transform LeftHand { get => m_leftHand; set => m_leftHand = value; }
        public Transform RightUpperArm { get => m_RightUpperArm; set => m_RightUpperArm = value; }
        public Transform RightLowerArm { get => m_RightLowerArm; set => m_RightLowerArm = value; }
        public Transform RightHand { get => m_rightHand; set => m_rightHand = value; }
        public Transform spine { get => m_Spine; set => m_Spine = value; }
        public Transform upperChest { get => m_UpperChest; set => m_UpperChest = value; }
        public Transform LeftShoulder { get => m_LeftShoulder; set => m_LeftShoulder = value; }
        public Transform RightShoulder { get => m_RightShoulder; set => m_RightShoulder = value; }
        public Transform LeftUpperArmTwist { get => m_LeftUpperArmTwist; set => m_LeftUpperArmTwist = value; }
        public Transform LeftLowerArmTwist { get => m_LeftLowerArmTwist; set => m_LeftLowerArmTwist = value; }
        public Transform RightUpperArmTwist { get => m_RightUpperArmTwist; set => m_RightUpperArmTwist = value; }
        public Transform RightLowerArmTwist { get => m_RightLowerArmTwist; set => m_RightLowerArmTwist = value; }
        public string EnabledPropertySpineIK => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineIKEnabled));
        public string HintWeightBoolPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintHeadEnabled));
        public string HasHipsTrackerBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HasHipsTracker));
        public string TargetPositionPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionHead));
        public string TargetRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RotationHead));
        public string PropertyChestPosition => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(ChestPosition));
        public string PropertyChestPositionRaw => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(ChestPositionRaw));
        public string PropertyChestRotation => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(ChestRotation));
        public string PlayerUpProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PlayerUp));
        public string KneeBendPrefLeftProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(KneeBendPrefLeft));
        public string KneeBendPrefRightProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(KneeBendPrefRight));
        public string EnabledPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftLowerLegEnabled));
        public string HintWeightBoolPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintLeftLowerLegEnabled));
        public string TargetPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootPosition));
        public string TargetRotationPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftFootRotation));
        public string HintPositionPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionLeftLowerLeg));
        public string EnabledPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightLowerLegEnabled));
        public string HintWeightBoolPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintRightLowerLegEnabled));
        public string TargetPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootPosition));
        public string TargetRotationPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightFootRotation));
        public string HintPositionPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionRightLowerLeg));
        public string HintIsTrackerBoolPropertyLeftLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftLowerLegHintIsTracker));
        public string HintIsTrackerBoolPropertyRightLowerLeg => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightLowerLegHintIsTracker));
        public string TargetPositionPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionHips));
        public string TargetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RotationHips));
        public string OffsetRotationPropertyHips => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OffsetRotationHips));
        public string OffsetRotationPropertyHead => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationHead));
        public string OffsetRotationPropertyChest => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationChest));
        public string OffsetRotationPropertyLeftFoot => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(M_CalibrationLeftFootRotation));
        public string OffsetRotationPropertyRightFoot => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(M_CalibrationRightFootRotation));
        public string OffsetRotationPropertyLeftToe => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationLeftToe));
        public string OffsetRotationPropertyRightToe => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationRightToe));
        public string OffsetRotationPropertyLeftShoulder => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationLeftShoulder));
        public string OffsetRotationPropertyRightShoulder => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationRightShoulder));
        public string OffsetRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationLeftHand));
        public string OffsetRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CalibratedRotationRightHand));
        public string LeftToeEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LeftToeEnabled));
        public string RightToeEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_RightToeEnabled));
        public string LeftDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingLeftToeRotation));
        public string RightDrivenTargetRotProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(OutGoingRightToeRotation));
        public string HintWeightBoolPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintLeftHandEnabled));
        public string TargetPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionLeftHand));
        public string TargetRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RotationLeftHand));
        public string HintPositionPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftLowerArmPosition));
        public string HintRotationPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftLowerArmRotation));
        public string EnabledPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledRightHand));
        public string EnabledPropertyLeftHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_EnabledLeftHand));
        public string HintWeightBoolPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HintRightHandEnabled));
        public string TargetPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(PositionRightHand));
        public string TargetRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RotationRightHand));
        public string HintPositionPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightLowerArmPosition));
        public string HintRotationPropertyRightHand => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightLowerArmRotation));
        public string ChestRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestRadius));
        public string CollisionSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionSkin));
        public string CollisionsEnabledBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollisionsEnabled));
        public string HandRadiusFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandRadius));
        public string HandSkinFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HandSkin));
        public string ProtectElbowBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ProtectElbow));
        public string CollideTrackedElbowBoolProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_CollideTrackedElbow));
        public string EnabledLeftShoulderProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_enabledLeftShoulder));
        public string EnabledRightShoulderProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_enabledRightShoulder));
        public string MinHeadSpineHeightFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MinHeadSpineHeight));
        public string TargetRotationLeftShoulderProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(LeftShoulderRotation));
        public string TargetRotationRightShoulderProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(RightShoulderRotation));
        public string MaxBendDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MaxBendDeg));
        public string MinFactorFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MinFactor));
        public string MaxFactorFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MaxFactor));
        public string MaxChestDeltaPropertyDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MaxChestDeltaDeg));
        public bool WeightChest { get => m_HintHeadEnabled; set => m_HintHeadEnabled = value; }
        public bool EnabledSpineIK { get => m_SpineIKEnabled; set => m_SpineIKEnabled = value; }
        public bool HasHipsTracker { get => m_HasHipsTracker; set => m_HasHipsTracker = value; }
        public float IKLockMode { get => m_IKLockMode; set => m_IKLockMode = value; }
        public string IKLockModeFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_IKLockMode));
        public float EnableLeftLowerLeg { get => m_HintLeftLowerLegEnabled; set => m_HintLeftLowerLegEnabled = value; }
        public float EnableLeftLeg { get => m_LeftLowerLegEnabled; set => m_LeftLowerLegEnabled = value; }
        public float EnableRightLowerLeg { get => m_HintRightLowerLegEnabled; set => m_HintRightLowerLegEnabled = value; }
        public float EnableRightLeg { get => m_RightLowerLegEnabled; set => m_RightLowerLegEnabled = value; }
        public bool LeftLowerLegHintIsTracker { get => m_LeftLowerLegHintIsTracker; set => m_LeftLowerLegHintIsTracker = value; }
        public bool RightLowerLegHintIsTracker { get => m_RightLowerLegHintIsTracker; set => m_RightLowerLegHintIsTracker = value; }
        public bool LeftToeEnabled { get => m_LeftToeEnabled; set => m_LeftToeEnabled = value; }
        public bool RightToeEnabled { get => m_RightToeEnabled; set => m_RightToeEnabled = value; }
        public bool HintWeightLeftHand { get => m_HintLeftHandEnabled; set => m_HintLeftHandEnabled = value; }
        public float EnabledLeftHand { get => m_EnabledLeftHand; set => m_EnabledLeftHand = value; }
        public float EnabledRightHand { get => m_EnabledRightHand; set => m_EnabledRightHand = value; }
        public bool ProtectElbow { get => m_ProtectElbow; set => m_ProtectElbow = value; }
        public bool CollideTrackedElbow { get => m_CollideTrackedElbow; set => m_CollideTrackedElbow = value; }
        public bool HintWeightRightHand { get => m_HintRightHandEnabled; set => m_HintRightHandEnabled = value; }
        public float HandRadius { get => m_HandRadius; set => m_HandRadius = value; }
        public float HandSkin { get => m_HandSkin; set => m_HandSkin = value; }
        public float ChestRadius { get => m_ChestRadius; set => m_ChestRadius = value; }
        public float CollisionSkin { get => m_CollisionSkin; set => m_CollisionSkin = value; }
        public bool CollisionsEnabled { get => m_CollisionsEnabled; set => m_CollisionsEnabled = value; }
        public bool EnabledRightShoulder { get => m_enabledRightShoulder; set => m_enabledRightShoulder = value; }
        public bool EnabledLeftShoulder { get => m_enabledLeftShoulder; set => m_enabledLeftShoulder = value; }
        public float MaxBendDeg { get => m_MaxBendDeg; set => m_MaxBendDeg = value; }
        public float MinFactor { get => m_MinFactor; set => m_MinFactor = value; }
        public float MaxFactor { get => m_MaxFactor; set => m_MaxFactor = value; }
        public float MaxChestDelta { get => m_MaxChestDeltaDeg; set => m_MaxChestDeltaDeg = value; }
        public bool ShoulderSolveEnabled { get => m_ShoulderSolveEnabled; set => m_ShoulderSolveEnabled = value; }
        public bool ShoulderShrugEnabled { get => m_ShoulderShrugEnabled; set => m_ShoulderShrugEnabled = value; }
        public float ShoulderElevationFactor { get => m_ShoulderElevationFactor; set => m_ShoulderElevationFactor = value; }
        public float ShoulderProtractionFactor { get => m_ShoulderProtractionFactor; set => m_ShoulderProtractionFactor = value; }
        public string ShoulderSolveEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ShoulderSolveEnabled));
        public string ShoulderShrugEnabledProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ShoulderShrugEnabled));
        public string ShoulderElevationFactorProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ShoulderElevationFactor));
        public string ShoulderProtractionFactorProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ShoulderProtractionFactor));
        public float SpineBendPitch { get => m_SpineBendPitch; set => m_SpineBendPitch = value; }
        public float SpineBendYaw { get => m_SpineBendYaw; set => m_SpineBendYaw = value; }
        public float SpineBendRoll { get => m_SpineBendRoll; set => m_SpineBendRoll = value; }
        public float UpperChestBendPitch { get => m_UpperChestBendPitch; set => m_UpperChestBendPitch = value; }
        public float UpperChestBendYaw { get => m_UpperChestBendYaw; set => m_UpperChestBendYaw = value; }
        public float UpperChestBendRoll { get => m_UpperChestBendRoll; set => m_UpperChestBendRoll = value; }
        public float HipHingeStartDeg { get => m_HipHingeStartDeg; set => m_HipHingeStartDeg = value; }
        public float HipHingeMaxAddDeg { get => m_HipHingeMaxAddDeg; set => m_HipHingeMaxAddDeg = value; }
        public float ChestSpringHz { get => m_ChestSpringHz; set => m_ChestSpringHz = value; }
        public float ChestSpringDamping { get => m_ChestSpringDamping; set => m_ChestSpringDamping = value; }
        public float SpineMaxForwardDeg { get => m_SpineMaxForwardDeg; set => m_SpineMaxForwardDeg = value; }
        public float SpineMaxBackwardDeg { get => m_SpineMaxBackwardDeg; set => m_SpineMaxBackwardDeg = value; }
        public float SpineMaxLateralDeg { get => m_SpineMaxLateralDeg; set => m_SpineMaxLateralDeg = value; }
        public float SpineSquishBoost { get => m_SpineSquishBoost; set => m_SpineSquishBoost = value; }
        public float SpineGazeFollow { get => m_SpineGazeFollow; set => m_SpineGazeFollow = value; }
        public float NeckGazeFollow { get => m_NeckGazeFollow; set => m_NeckGazeFollow = value; }
        public float MoveBodyBackWhenCrouching { get => m_MoveBodyBackWhenCrouching; set => m_MoveBodyBackWhenCrouching = value; }
        public float SwingSmoothRateDeg { get => m_SwingSmoothRateDeg; set => m_SwingSmoothRateDeg = value; }
        public float ChestArmSwingFactor { get => m_ChestArmSwingFactor; set => m_ChestArmSwingFactor = value; }
        public float ChestArmSwingMaxDeg { get => m_ChestArmSwingMaxDeg; set => m_ChestArmSwingMaxDeg = value; }
        public float LowerArmTwistFraction { get => m_LowerArmTwistFraction; set => m_LowerArmTwistFraction = value; }
        public float UpperArmTwistFraction { get => m_UpperArmTwistFraction; set => m_UpperArmTwistFraction = value; }
        public bool AnatDifferentialStiffness { get => m_AnatDifferentialStiffness; set => m_AnatDifferentialStiffness = value; }
        public bool AnatShoulderSlide { get => m_AnatShoulderSlide; set => m_AnatShoulderSlide = value; }
        public bool AnatCervicalLordosis { get => m_AnatCervicalLordosis; set => m_AnatCervicalLordosis = value; }
        public bool AnatPelvicTwistRouting { get => m_AnatPelvicTwistRouting; set => m_AnatPelvicTwistRouting = value; }
        public bool SpineAnatomicalRom { get => m_SpineAnatomicalRom; set => m_SpineAnatomicalRom = value; }
        public bool ChestIKTarget { get => m_ChestIKTarget; set => m_ChestIKTarget = value; }
        public bool LegSwivelSmoothing { get => m_LegSwivelSmoothing; set => m_LegSwivelSmoothing = value; }
        public string SpineBendPitchFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineBendPitch));
        public string SpineBendYawFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineBendYaw));
        public string SpineBendRollFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineBendRoll));
        public string UpperChestBendPitchFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UpperChestBendPitch));
        public string UpperChestBendYawFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UpperChestBendYaw));
        public string UpperChestBendRollFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UpperChestBendRoll));
        public string HipHingeStartDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HipHingeStartDeg));
        public string HipHingeMaxAddDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_HipHingeMaxAddDeg));
        public string ChestSpringHzFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestSpringHz));
        public string ChestSpringDampingFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestSpringDamping));
        public string SpineMaxForwardDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineMaxForwardDeg));
        public string SpineMaxBackwardDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineMaxBackwardDeg));
        public string SpineMaxLateralDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineMaxLateralDeg));
        public string SpineSquishBoostFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineSquishBoost));
        public string SpineGazeFollowFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineGazeFollow));
        public string NeckGazeFollowFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_NeckGazeFollow));
        public string MoveBodyBackWhenCrouchingFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_MoveBodyBackWhenCrouching));
        public string SwingSmoothRateDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SwingSmoothRateDeg));
        public string ChestArmSwingFactorFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestArmSwingFactor));
        public string ChestArmSwingMaxDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestArmSwingMaxDeg));
        public string LowerArmTwistFractionFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LowerArmTwistFraction));
        public string UpperArmTwistFractionFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_UpperArmTwistFraction));
        public string AnatDifferentialStiffnessProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_AnatDifferentialStiffness));
        public string AnatShoulderSlideProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_AnatShoulderSlide));
        public string AnatCervicalLordosisProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_AnatCervicalLordosis));
        public string AnatPelvicTwistRoutingProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_AnatPelvicTwistRouting));
        public string SpineAnatomicalRomProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineAnatomicalRom));
        public string ChestIKTargetProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_ChestIKTarget));
        public string LegSwivelSmoothingProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LegSwivelSmoothing));
        public float LordosisPitchGainDeg { get => m_LordosisPitchGainDeg; set => m_LordosisPitchGainDeg = value; }
        public string LordosisPitchGainDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisPitchGainDeg));
        public float LordosisBaseDeg { get => m_LordosisBaseDeg; set => m_LordosisBaseDeg = value; }
        public string LordosisBaseDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisBaseDeg));
        public float LordosisNeckShare { get => m_LordosisNeckShare; set => m_LordosisNeckShare = value; }
        public string LordosisNeckShareFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisNeckShare));
        public float LordosisMaxHeadPitchDeg { get => m_LordosisMaxHeadPitchDeg; set => m_LordosisMaxHeadPitchDeg = value; }
        public string LordosisMaxHeadPitchDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisMaxHeadPitchDeg));
        public float LordosisExtremeStartDeg { get => m_LordosisExtremeStartDeg; set => m_LordosisExtremeStartDeg = value; }
        public string LordosisExtremeStartDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeStartDeg));
        public float LordosisExtremeFullDeg { get => m_LordosisExtremeFullDeg; set => m_LordosisExtremeFullDeg = value; }
        public string LordosisExtremeFullDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeFullDeg));
        public float LordosisExtremeRollForwardMaxDeg { get => m_LordosisExtremeRollForwardMaxDeg; set => m_LordosisExtremeRollForwardMaxDeg = value; }
        public string LordosisExtremeRollForwardMaxDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeRollForwardMaxDeg));
        public float LordosisExtremeRollBackwardMaxDeg { get => m_LordosisExtremeRollBackwardMaxDeg; set => m_LordosisExtremeRollBackwardMaxDeg = value; }
        public string LordosisExtremeRollBackwardMaxDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeRollBackwardMaxDeg));
        public float LordosisExtremeHipsHorizontalMax { get => m_LordosisExtremeHipsHorizontalMax; set => m_LordosisExtremeHipsHorizontalMax = value; }
        public string LordosisExtremeHipsHorizontalMaxFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeHipsHorizontalMax));
        public float LordosisExtremeChestHorizontalMax { get => m_LordosisExtremeChestHorizontalMax; set => m_LordosisExtremeChestHorizontalMax = value; }
        public string LordosisExtremeChestHorizontalMaxFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeChestHorizontalMax));
        public float LordosisExtremeHipsDownMax { get => m_LordosisExtremeHipsDownMax; set => m_LordosisExtremeHipsDownMax = value; }
        public string LordosisExtremeHipsDownMaxFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeHipsDownMax));
        public float LordosisExtremeChestDownMax { get => m_LordosisExtremeChestDownMax; set => m_LordosisExtremeChestDownMax = value; }
        public string LordosisExtremeChestDownMaxFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeChestDownMax));
        public float LordosisExtremeHipsDownLookUp { get => m_LordosisExtremeHipsDownLookUp; set => m_LordosisExtremeHipsDownLookUp = value; }
        public string LordosisExtremeHipsDownLookUpFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeHipsDownLookUp));
        public float LordosisExtremeChestDownLookUp { get => m_LordosisExtremeChestDownLookUp; set => m_LordosisExtremeChestDownLookUp = value; }
        public string LordosisExtremeChestDownLookUpFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_LordosisExtremeChestDownLookUp));
        public float SpineCCDRelax { get => m_SpineCCDRelax; set => m_SpineCCDRelax = value; }
        public string SpineCCDRelaxFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineCCDRelax));
        public float SpineTwistKeep { get => m_SpineTwistKeep; set => m_SpineTwistKeep = value; }
        public string SpineTwistKeepFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineTwistKeep));
        public float SpineNeckTwistKeep { get => m_SpineNeckTwistKeep; set => m_SpineNeckTwistKeep = value; }
        public string SpineNeckTwistKeepFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_SpineNeckTwistKeep));
        public float NeckMaxConeDeg { get => m_NeckMaxConeDeg; set => m_NeckMaxConeDeg = value; }
        public string NeckMaxConeDegFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(m_NeckMaxConeDeg));
        bool IAnimationJobData.IsValid()
        {
            bool hipsValid = m_Hips != null;

            bool head = (m_head && m_neck && m_chest &&
                         m_head.IsChildOf(m_neck) && m_neck.IsChildOf(m_chest));

            bool lLeg = (m_leftFoot && m_LeftLowerLeg && m_LeftUpperLeg &&
                         m_leftFoot.IsChildOf(m_LeftLowerLeg) && m_LeftLowerLeg.IsChildOf(m_LeftUpperLeg));

            bool rLeg = (m_RightFoot && m_RightLowerLeg && m_RightUpperLeg &&
                         m_RightFoot.IsChildOf(m_RightLowerLeg) && m_RightLowerLeg.IsChildOf(m_RightUpperLeg));

            bool lHand = (m_leftHand && m_leftLowerArm && m_leftUpperArm &&
                          m_leftHand.IsChildOf(m_leftLowerArm) && m_leftLowerArm.IsChildOf(m_leftUpperArm));

            bool rHand = (m_rightHand && m_RightLowerArm && m_RightUpperArm &&
                          m_rightHand.IsChildOf(m_RightLowerArm) && m_RightLowerArm.IsChildOf(m_RightUpperArm));

            // Any of these being valid is enough to run.
            return head || lLeg || rLeg || lHand || rHand || hipsValid || (m_LeftToe != null) || (m_RightToe != null);
        }
        void IAnimationJobData.SetDefaultValues()
        {
            m_chest = m_neck = m_head = null;
            m_LeftUpperLeg = m_LeftLowerLeg = m_leftFoot = null;
            m_RightUpperLeg = m_RightLowerLeg = m_RightFoot = null;

            m_leftUpperArm = m_leftLowerArm = m_leftHand = null;
            m_RightUpperArm = m_RightLowerArm = m_rightHand = null;

            m_Hips = null;

            m_HintHeadEnabled = true;
            m_HintLeftLowerLegEnabled = m_HintRightLowerLegEnabled = 1f;
            m_SpineIKEnabled = true;
            m_HasHipsTracker = false;
            m_LeftLowerLegEnabled = m_RightLowerLegEnabled = 1f;
            m_LeftLowerLegHintIsTracker = m_RightLowerLegHintIsTracker = false;
            m_IKLockMode = (float)BasisIKLockMode.LockHead;

            m_HintLeftHandEnabled = m_HintRightHandEnabled = true;
            m_EnabledLeftHand = m_EnabledRightHand = 1f;
            m_CalibratedRotationHead = M_CalibrationLeftFootRotation = M_CalibrationRightFootRotation = Quaternion.identity;
            m_CalibratedRotationLeftHand = m_CalibratedRotationRightHand = Quaternion.identity;

            PlayerUp = Vector3.up;

            PositionHips = Vector3.zero;
            RotationHips = Quaternion.identity;
            OffsetRotationHips = Quaternion.identity;

            // Integrated driven TR defaults
            m_LeftToe = null;
            m_RightToe = null;

            OutGoingLeftToeRotation = OutGoingRightToeRotation = Quaternion.identity;
            m_LeftToeEnabled = false;
            m_RightToeEnabled = false;

            // Chest/hand capsule defaults — read from persisted settings
            m_chest = m_neck = null;
            m_ChestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue;
            m_CollisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue;
            m_CollisionsEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionsEnabled.RawValue;
            m_HandRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue;
            m_HandSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue;
            m_ProtectElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKProtectElbow.RawValue;
            m_CollideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;

            m_ShoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            m_ShoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
            m_ShoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            m_ShoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;

            m_SpineBendPitch = 0.45f;
            m_SpineBendYaw = 0.10f;
            m_SpineBendRoll = 0.35f;
            m_UpperChestBendPitch = 0.25f;
            m_UpperChestBendYaw = 0.30f;
            m_UpperChestBendRoll = 0.20f;
            m_HipHingeStartDeg = 40f;
            m_HipHingeMaxAddDeg = 52f;
            m_ChestSpringHz = 12f;
            m_ChestSpringDamping = 1f;
            m_SpineMaxForwardDeg = 60f;
            m_SpineMaxBackwardDeg = 25f;
            m_SpineMaxLateralDeg = 25f;
            m_SpineSquishBoost = 0.5f;
            m_SpineGazeFollow = 0.25f;
            m_NeckGazeFollow = 0.3f;
            m_MoveBodyBackWhenCrouching = 1f;
            m_SwingSmoothRateDeg = 720f;
            m_ChestArmSwingFactor = 0.3f;
            m_ChestArmSwingMaxDeg = 15f;
            m_LowerArmTwistFraction = 0.5f;
            m_UpperArmTwistFraction = 0.3f;

            m_AnatDifferentialStiffness = false;
            m_AnatShoulderSlide = false;
            m_AnatCervicalLordosis = false;
            m_AnatPelvicTwistRouting = false;
            m_SpineAnatomicalRom = false;
            m_ChestIKTarget = false;
            m_LegSwivelSmoothing = true;
            m_LordosisPitchGainDeg = 8f;
            m_LordosisBaseDeg = 5f;
            m_LordosisNeckShare = 0.65f;
            m_LordosisMaxHeadPitchDeg = 80f;
            m_LordosisExtremeStartDeg = 50f;
            m_LordosisExtremeFullDeg = 80f;
            m_LordosisExtremeRollForwardMaxDeg = 10f;
            m_LordosisExtremeRollBackwardMaxDeg = 4f;
            m_LordosisExtremeHipsHorizontalMax = 0.025f;
            m_LordosisExtremeChestHorizontalMax = 0.04f;
            m_LordosisExtremeHipsDownMax = 0.015f;
            m_LordosisExtremeChestDownMax = 0.025f;
            m_LordosisExtremeHipsDownLookUp = 0.0005f;
            m_LordosisExtremeChestDownLookUp = 0.001f;
            m_SpineCCDRelax = 0.8f;
            m_NeckMaxConeDeg = 45f;
            m_SpineTwistKeep = 0.25f;
            m_SpineNeckTwistKeep = 0.9f;

            // Positions
            TargetPosition0 = TargetPosition1 = TargetPosition2 = TargetPosition3 = TargetPosition4 =
            TargetPosition5 = TargetPosition6 = TargetPosition7 = TargetPosition8 = TargetPosition9 =
            TargetPosition10 = TargetPosition11 = TargetPosition12 = TargetPosition13 = TargetPosition14 =
            TargetPosition15 = TargetPosition16 = TargetPosition17 = TargetPosition18 = TargetPosition19 =
            TargetPosition20 = TargetPosition54 = Vector3.zero;

            // Rotations
            TargetRotation0 = TargetRotation1 = TargetRotation2 = TargetRotation3 = TargetRotation4 =
            TargetRotation5 = TargetRotation6 = TargetRotation7 = TargetRotation8 = TargetRotation9 =
            TargetRotation10 = TargetRotation11 = TargetRotation12 = TargetRotation13 = TargetRotation14 =
            TargetRotation15 = TargetRotation16 = TargetRotation17 = TargetRotation18 = TargetRotation19 =
            TargetRotation20 = TargetRotation54 = Quaternion.identity;

            // Offsets
            OffsetRotation0 = OffsetRotation1 = OffsetRotation2 = OffsetRotation3 = OffsetRotation4 =
            OffsetRotation5 = OffsetRotation6 = OffsetRotation7 = OffsetRotation8 = OffsetRotation9 =
            OffsetRotation10 = OffsetRotation11 = OffsetRotation12 = OffsetRotation13 = OffsetRotation14 =
            OffsetRotation15 = OffsetRotation16 = OffsetRotation17 = OffsetRotation18 = OffsetRotation19 =
            OffsetRotation20 = OffsetRotation54 = Quaternion.identity;

            // Weights default to disabled
            Weight0 = Weight1 = Weight2 = Weight3 = Weight4 =
            Weight5 = Weight6 = Weight7 = Weight8 = Weight9 =
            Weight10 = Weight11 = Weight12 = Weight13 = Weight14 =
            Weight15 = Weight16 = Weight17 = Weight18 = Weight19 =
            Weight20 = Weight54 = false;
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
                case 54: TargetPosition54 = v; break;
                default:
                    break;
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
                case 54: TargetRotation54 = q; break;
                default:
                    break;
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
                case 54: OffsetRotation54 = q; break;
                default:
                    break;
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
                case 54: Weight54 = State; break;
                default:
                    break;
            }
        }
    }
    public interface IBasisFullBodyData
    {
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
            m_Data.WeightChest = m_Data.WeightChest;
            m_Data.EnableLeftLowerLeg = m_Data.EnableLeftLowerLeg;
            m_Data.EnableRightLowerLeg = m_Data.EnableRightLowerLeg;
            m_Data.EnabledSpineIK = m_Data.EnabledSpineIK;
            // new toggles
            m_Data.LeftToeEnabled = m_Data.LeftToeEnabled;
            m_Data.RightToeEnabled = m_Data.RightToeEnabled;
            // hands toggles
            m_Data.HintWeightLeftHand = m_Data.HintWeightLeftHand;
            m_Data.HintWeightRightHand = m_Data.HintWeightRightHand;
            m_Data.EnabledLeftHand = m_Data.EnabledLeftHand;
            m_Data.EnabledRightHand = m_Data.EnabledRightHand;
            m_Data.ProtectElbow = m_Data.ProtectElbow;
            m_Data.CollideTrackedElbow = m_Data.CollideTrackedElbow;
            m_Data.ShoulderSolveEnabled = m_Data.ShoulderSolveEnabled;
            m_Data.ShoulderShrugEnabled = m_Data.ShoulderShrugEnabled;
            m_Data.IKLockMode = m_Data.IKLockMode;
        }
    }

    [Unity.Burst.BurstCompile]
    public struct BasisFullIKConstraintJob : IWeightedAnimationJob
    {
        const float k_Epsilon = 1e-5f; // or 0.00001f
        const float k_MinMag = 1e-6f;// or 0.000001f
        const float k_SqrEpsilon = 1e-8f;// or 0.00000001f
        // Scapulohumeral coupling: the shoulder girdle follows this share of the humeral swing
        // (real scapula contributes ~1/3 of total elevation); the per-axis Elevation/Protraction
        // settings trim it. Clamp the applied girdle rotation below the GateShoulder ceiling.
        // Kept conservative because the elbow rides the girdle root: with no shoulder tracker a high
        // coupling swings the arm root on a ramped curve the hand has already left, reading as a
        // floaty / trailing elbow. ~0.4 keeps the anatomical girdle motion without the lag.
        const float k_ShoulderCoupleRatio = 0.4f;
        const float k_ShoulderMaxDeg = 25f;

        public ReadWriteTransformHandle HandleChest, HandleNeck, HandleHead,
  HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot,
  HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot,
  HandleHips, HandleSpine, HandleUpperChest,
            HandleLeftShoulder, HandleRightShoulder,

  HandleLeftToe, HandleRightToe,
  HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand,
  HandleRightUpperArm, HandleRightLowerArm, HandleRightHand,
  HandleLeftUpperArmTwist, HandleLeftLowerArmTwist,
  HandleRightUpperArmTwist, HandleRightLowerArmTwist;

        public Vector3Property targetPositionHead, TargetChestPosition, TargetChestPositionRaw, playerUp, KneeBendPrefLeft, KneeBendPrefRight,
targetPositionLeftLowerLeg, hintPositionLeftLowerLeg,
targetPositionRightLowerLeg, hintPositionRightLowerLeg,
targetPositionHips,
targetPositionLeftHand, hintPositionLeftHand,
targetPositionRightHand, hintPositionRightHand,
p0, p1, p2, p3, p4, p5, p6, p7, p8, p9,
p10, p11, p12, p13, p14, p15, p16, p17, p18, p19,
p20, p54;

        public Vector4Property targetRotationHead, targetChestRotation,
targetRotationLeftLowerLeg,
targetRotationRightLowerLeg,
targetRotationHips, offsetRotationHips,
offsetRotationHead, offsetRotationChest, offsetRotationLeftFoot, offsetRotationRightFoot,
offsetRotationLeftToe, offsetRotationRightToe, offsetRotationLeftShoulder, offsetRotationRightShoulder,
offsetRotationLeftHand, offsetRotationRightHand,
leftDrivenTargetRot, rightDrivenTargetRot,
targetRotationLeftHand, hintRotationLeftHand,
targetRotationRightHand, hintRotationRightHand,
TargetRotationLeftShoulder, TargetRotationRightShoulder,
r0, r1, r2, r3, r4, r5, r6, r7, r8, r9,
r10, r11, r12, r13, r14, r15, r16, r17, r18, r19,
r20, r54,
o0, o1, o2, o3, o4, o5, o6, o7, o8, o9,
o10, o11, o12, o13, o14, o15, o16, o17, o18, o19,
o20, o54;

        // Swivel models: where the elbow/knee go for a user with no elbow/knee tracker.
        //
        // WHAT THIS REPLACED. An 11^3 trilinear lookup of bend VECTORS (BasisArmBendLookup), filled by six
        // hand-authored lerps over invented factors and never fitted to anything, plus a "chicken-wing flare"
        // (BasisElbowFlareCore) bolted on top. Measured against 20 CMU clips the table put the elbow 6.62% of an
        // arm length from where the human's actually was, with 34 pops -- a single CONSTANT swivel angle that
        // ignores the hand entirely scores 6.41%, so the table was worse than not looking. The leg had no model
        // at all: a FIXED hips-right bend normal, which collapses precisely when the leg straightens, and
        // standing IS a straight leg.
        //
        // ⚠ NO T-POSE IS BAKED HERE ANY MORE, AND THAT IS THE SCAR FROM SHIPPING ONE. The models briefly read
        // the hand's/foot's ROTATION relative to a T-pose captured at job build. But BasisLocalAvatarDriver
        // calls ResetAvatarAnimator() -- "Exit T-Pose" -- BEFORE BuildBuilder(), so that rest pose was not
        // reliably a rest pose; in a headset the elbows sat up by the ears on almost every frame while the whole
        // suite stayed green. The models now read POSITIONS ONLY. A limb's geometry is anatomy and it transfers;
        // a bone's rotation is a modelling convention and it does not. See BasisArmSwivelModel.
        public Quaternion targetOffsetHead, targetOffsetChest, targetOffsetLeftToe,
            targetOffsetRightToe, targetOffsetLeftShoulder, targetOffsetRightShoulder, targetOffsetLeftFoot,
            targetOffsetRightFoot, targetOffsetLeftHand, targetOffsetRightHand;

        public FloatProperty
enabledLeftLowerLeg, enabledRightLowerLeg,
hintWeightLeftLowerLeg, hintWeightRightLowerLeg,
enabledLeftHand, enabledRightHand;

        public BoolProperty
HasChestTracker, hasHipsTracker, enabledSpineIK,
            enabledLeftShoulder, enabledRightShoulder,

leftToeEnabled, RightToeEnabled,
hintWeightLeftHand,
hintWeightRightHand,
protectElbow, collideTrackedElbow,
collisionsEnabled,
w0, w1, w2, w3, w4, w5, w6, w7, w8, w9,
w10, w11, w12, w13, w14, w15, w16, w17, w18, w19,
w20, w54;
        public NativeArray<ReadWriteTransformHandle> ChainHeadToSpine;
        // The anatomical envelope, PARALLEL TO ChainHeadToSpine so a chain index guards itself. The head
        // (index 0) and the hips (the last) carry Valid=false frames -- the head is welded to the HMD and
        // the hips are the anchor, so neither is a DOF the solver invents, and neither is guarded. Every
        // other entry is a real vertebral segment with its own ROM. See BasisSpineAnatomy.
        public NativeArray<BasisSpineRestFrame> ChainSpineRestFrames;
        public NativeArray<BasisSpineRom> ChainSpineRoms;
        // optional tuning (can be constants or properties)
        public CacheIndex spineToleranceIdx;
        public CacheIndex spineMaxIterationsIdx;
        public AnimationJobCache spineCache;
        public Vector3 TposeLengthHeadToHips;
        // The spine's bend cue. `TposeHeadToNeckLocal` is the neck's offset from the head, IN THE HEAD'S OWN
        // FRAME, so re-attaching it to a rotated head reconstructs where the neck must be -- and cancels the
        // nod exactly (see DistributeSpineBend). `TposeLengthNeckToHips` is the matching rest span for the
        // squish coupling, which now measures the SPINE's compression instead of the head's.
        public Vector3 TposeHeadToNeckLocal;
        public Vector3 TposeLengthNeckToHips;
        public FloatProperty handRadius, handSkin, chestRadius, collisionSkin, MinHeadSpineHeight, maxBendDeg, minFactor, maxFactor, MaxChestDeltaProperty;
        public FloatProperty shoulderElevationFactor, shoulderProtractionFactor;
        public FloatProperty spineBendPitch, spineBendYaw, spineBendRoll;
        public FloatProperty upperChestBendPitch, upperChestBendYaw, upperChestBendRoll;
        public FloatProperty hipHingeStartDeg, hipHingeMaxAddDeg;
        public FloatProperty chestSpringHz, chestSpringDamping;
        public FloatProperty spineMaxForwardDeg, spineMaxBackwardDeg, spineMaxLateralDeg;
        public FloatProperty spineSquishBoost;
        public FloatProperty spineGazeFollow;
        public FloatProperty neckGazeFollow;
        public FloatProperty moveBodyBackWhenCrouching;
        public FloatProperty swingSmoothRateDeg;
        public FloatProperty chestArmSwingFactor, chestArmSwingMaxDeg;
        public FloatProperty lowerArmTwistFraction, upperArmTwistFraction;
        public BoolProperty anatDifferentialStiffness, anatShoulderSlide, anatCervicalLordosis, anatPelvicTwistRouting, legSwivelSmoothing;
        public BoolProperty spineAnatomicalRom;
        public BoolProperty chestIkTarget;
        public BoolProperty hintIsTrackerLeftLowerLeg, hintIsTrackerRightLowerLeg;
        public FloatProperty lordosisPitchGainDeg;
        public FloatProperty lordosisBaseDeg, lordosisNeckShare, lordosisMaxHeadPitchDeg;
        public FloatProperty lordosisExtremeStartDeg, lordosisExtremeFullDeg;
        public FloatProperty lordosisExtremeRollForwardMaxDeg, lordosisExtremeRollBackwardMaxDeg;
        public FloatProperty lordosisExtremeHipsHorizontalMax, lordosisExtremeChestHorizontalMax;
        public FloatProperty lordosisExtremeHipsDownMax, lordosisExtremeChestDownMax;
        public FloatProperty lordosisExtremeHipsDownLookUp, lordosisExtremeChestDownLookUp;
        public FloatProperty spineCCDRelax, neckMaxConeDeg, spineTwistKeep, spineNeckTwistKeep;
        // Persistent state for the chest follow spring. [0]=smoothed pos, [1]=velocity. Allocated
        // in CreateJob, disposed in Destroy. Initialised lazily on first frame to avoid spring kick.
        public NativeArray<Vector3> chestSpringState;
        public NativeArray<int> chestSpringInit;
        // Swing continuity: persistent per-DOF state to rate-limit the mid-joint (elbow/knee) swing
        // around the root→tip axis, so a torso-collision change eases in instead of popping.
        // Slots: 0/1 = left/right elbow; 2/3 reserved for left/right knee.
        public const int k_SwingLeftElbow = 0, k_SwingRightElbow = 1, k_SwingLeftKnee = 2, k_SwingRightKnee = 3, k_SwingCount = 4;
        public NativeArray<Vector3> swingLastDir;
        public NativeArray<Vector3> swingLastAxis;
        public NativeArray<Vector3> swingLastTarget;
        public NativeArray<int> swingContinuityInit;
        // Per-arm torso-collision tag written by SolveHand each frame: 0 = no push, 1 = pushed to the
        // natural side, 2 = wrong-side full snap. The swing limiter only engages when this changes.
        public NativeArray<int> swingCollided;
        // Limiter latch per slot: -1 while a collision pop is still easing in, else the last settled tag.
        public NativeArray<int> swingSmoothState;
        // Per-leg OneEuro state (0=left, 1=right) for knee-swivel OUTPUT smoothing.
        //
        // The ARM had one of these too, and it is GONE. It was damping the jitter the old bend LOOKUP fed the
        // solve (0.126); the fitted swivel model that replaced the lookup is a polynomial -- smooth by
        // construction -- and measures 0.042 jitter, LOWER than a real elbow tracker's 0.046, with zero pops.
        // Filtering it was measured and it made every metric worse: err 2.12 -> 2.55, jitter 0.042 -> 0.060,
        // pops 0 -> 1. See BasisMocapMotionQualityTests, hint source SwivelModelSmoothed, which exists purely
        // to keep that answer honest if anyone is tempted to add the filter back.
        public NativeArray<Vector3> legSwivelRaw;
        public NativeArray<Vector3> legSwivelSmooth;
        public NativeArray<int> legSwivelInit;
        public FloatProperty ikLockMode;
        public BoolProperty shoulderSolveEnabled;
        public BoolProperty shoulderShrugEnabled;
        // T-pose baked reference data for shoulder solve
        public Vector3 TposeLeftShoulderLocalDir, TposeRightShoulderLocalDir;
        public Quaternion TposeLeftShoulderRot, TposeRightShoulderRot;
        public Quaternion TposeChestRot;
        public float TposeShoulderToHandLeft, TposeShoulderToHandRight;
        public float TposeClavicleLenLeft, TposeClavicleLenRight;
        public float TposeShoulderToElbowLeft, TposeShoulderToElbowRight;
        public FloatProperty jobWeight { get; set; }
        public void ProcessRootMotion(AnimationStream stream) { }
        public void ProcessAnimation(AnimationStream stream)
        {
            float w = jobWeight.Get(stream);
            if (w <= 0f)
            {
                return;
            }

            // Per-frame reads so FBT recalibration (which updates these on the constraint data)
            // reaches the running job; the originals were copied once at job build (issue #531).
            targetOffsetHead = V4ToQuat(offsetRotationHead.Get(stream));
            targetOffsetChest = V4ToQuat(offsetRotationChest.Get(stream));
            targetOffsetLeftFoot = V4ToQuat(offsetRotationLeftFoot.Get(stream));
            targetOffsetRightFoot = V4ToQuat(offsetRotationRightFoot.Get(stream));
            targetOffsetLeftToe = V4ToQuat(offsetRotationLeftToe.Get(stream));
            targetOffsetRightToe = V4ToQuat(offsetRotationRightToe.Get(stream));
            targetOffsetLeftShoulder = V4ToQuat(offsetRotationLeftShoulder.Get(stream));
            targetOffsetRightShoulder = V4ToQuat(offsetRotationRightShoulder.Get(stream));
            targetOffsetLeftHand = V4ToQuat(offsetRotationLeftHand.Get(stream));
            targetOffsetRightHand = V4ToQuat(offsetRotationRightHand.Get(stream));

            // 1) Spine: hips + chest/neck/head chain
            SolveSpine(stream);

            // 1b) Anatomy modifiers that act on the spine after the main solve.
            if (anatCervicalLordosis.Get(stream))
            {
                ApplyCervicalLordosis(stream);
            }

            // 2) Shoulder pre-solve: elevate/protract based on hand targets before arm IK
            if (shoulderSolveEnabled.Get(stream))
            {
                SolveShoulder(stream, HandleLeftShoulder, enabledLeftShoulder, targetPositionLeftHand, hintPositionLeftHand, hintWeightLeftHand, TposeLeftShoulderLocalDir, TposeLeftShoulderRot, TposeChestRot, TposeShoulderToHandLeft, TposeClavicleLenLeft, TposeShoulderToElbowLeft, true);
                SolveShoulder(stream, HandleRightShoulder, enabledRightShoulder, targetPositionRightHand, hintPositionRightHand, hintWeightRightHand, TposeRightShoulderLocalDir, TposeRightShoulderRot, TposeChestRot, TposeShoulderToHandRight, TposeClavicleLenRight, TposeShoulderToElbowRight, false);
            }
            else
            {
                ApplyRotation(stream, enabledLeftShoulder, HandleLeftShoulder, TargetRotationLeftShoulder, targetOffsetLeftShoulder);
                ApplyRotation(stream, enabledRightShoulder, HandleRightShoulder, TargetRotationRightShoulder, targetOffsetRightShoulder);
            }
            if (anatShoulderSlide.Get(stream))
            {
                ApplyShoulderSlide(stream);
            }

            // 3) Legs: two-bone IK with bend normal preference
            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, KneeBendPrefLeft, hintIsTrackerLeftLowerLeg, 0);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, KneeBendPrefRight, hintIsTrackerRightLowerLeg, 1);

            // 4) Hands: two-bone IK with collision + elbow protection. bodyRight (shoulder->shoulder) orients
            // the torso's elliptical collision cross-section; shared by both arms so it is computed once here.
            Vector3 bodyRight = (HandleLeftUpperArm.IsValid(stream) && HandleRightUpperArm.IsValid(stream))
                ? HandleRightUpperArm.GetPosition(stream) - HandleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            SolveHand(stream, enabledLeftHand, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand, hintWeightLeftHand, targetOffsetLeftHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingLeftElbow);
            SolveHand(stream, enabledRightHand, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand, hintWeightRightHand, targetOffsetRightHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingRightElbow);

            // Arm pop continuity: rate-limit the elbow swing so a torso-collision change eases in
            // instead of popping in one frame. Runs before arm twist (which reads the arm pose).
            float swingRate = swingSmoothRateDeg.Get(stream);
            float swingDt = stream.deltaTime;
            if (enabledLeftHand.Get(stream) > 0f)
            {
                ApplySwingContinuity(stream, k_SwingLeftElbow, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand.Get(stream), swingRate, swingDt);
            }

            if (enabledRightHand.Get(stream) > 0f)
            {
                ApplySwingContinuity(stream, k_SwingRightElbow, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand.Get(stream), swingRate, swingDt);
            }

            // 4b) Arm twist distribution: spread wrist/elbow roll along the optional twist bones
            // so the mesh doesn't pinch at the wrist when the hand rotates.
            float lowerTwist = lowerArmTwistFraction.Get(stream);
            float upperTwist = upperArmTwistFraction.Get(stream);
            SolveArmTwist(stream, HandleLeftLowerArm, HandleLeftHand, HandleLeftLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleRightLowerArm, HandleRightHand, HandleRightLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftUpperArmTwist, upperTwist);
            SolveArmTwist(stream, HandleRightUpperArm, HandleRightLowerArm, HandleRightUpperArmTwist, upperTwist);

            // 5) Toes
            ApplyRotation(stream, leftToeEnabled, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            ApplyRotation(stream, RightToeEnabled, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);

            // 6) Generic per-bone overrides (direct tracker control)
            Apply(stream, HandleHips, p0, r0, o0, w0);
            Apply(stream, HandleLeftUpperLeg, p1, r1, o1, w1);
            Apply(stream, HandleRightUpperLeg, p2, r2, o2, w2);
            Apply(stream, HandleLeftLowerLeg, p3, r3, o3, w3);
            Apply(stream, HandleRightLowerLeg, p4, r4, o4, w4);
            Apply(stream, HandleLeftFoot, p5, r5, o5, w5);
            Apply(stream, HandleRightFoot, p6, r6, o6, w6);
            Apply(stream, HandleSpine, p7, r7, o7, w7);
            Apply(stream, HandleChest, p8, r8, o8, w8);
            Apply(stream, HandleNeck, p9, r9, o9, w9);
            Apply(stream, HandleHead, p10, r10, o10, w10);
            Apply(stream, HandleLeftShoulder, p11, r11, o11, w11);
            Apply(stream, HandleRightShoulder, p12, r12, o12, w12);
            Apply(stream, HandleLeftUpperArm, p13, r13, o13, w13);
            Apply(stream, HandleRightUpperArm, p14, r14, o14, w14);
            Apply(stream, HandleLeftLowerArm, p15, r15, o15, w15);
            Apply(stream, HandleRightLowerArm, p16, r16, o16, w16);
            Apply(stream, HandleLeftHand, p17, r17, o17, w17);
            Apply(stream, HandleRightHand, p18, r18, o18, w18);
            Apply(stream, HandleLeftToe, p19, r19, o19, w19);
            Apply(stream, HandleRightToe, p20, r20, o20, w20);
            Apply(stream, HandleUpperChest, p54, r54, o54, w54);
        }
        public void SolveSpine(AnimationStream stream)
        {
            if (!enabledSpineIK.Get(stream))
            {
                return;
            }
            // ---- Read targets ----
            Vector3 headTargetPos = targetPositionHead.Get(stream);
            Vector3 hipsTargetPos = targetPositionHips.Get(stream);

            Quaternion headTargetRot = V4ToQuat(targetRotationHead.Get(stream));
            Quaternion hipsTargetRot = V4ToQuat(targetRotationHips.Get(stream));
            Quaternion offsetHips = V4ToQuat(offsetRotationHips.Get(stream));
            Quaternion chestTargetRot = V4ToQuat(targetChestRotation.Get(stream));

            Quaternion hipDesired = hipsTargetRot * offsetHips;
            Quaternion chestDesired = chestTargetRot * targetOffsetChest;

            float restDist = MinHeadSpineHeight.Get(stream);
            int lockMode = (int)ikLockMode.Get(stream);
            Vector3 up = playerUp.Get(stream);

            // Lock mode determines how hips position relates to head position:
            // 0 = LockHips:  Hips are the anchor; apply hips directly, no head-relative clamping.
            // 1 = LockHead:  Head is the anchor; hips ride at rest spine length along the spine's own axis.
            // 2 = LockBoth:  Both independently positioned; spine must accommodate (original behavior).
            switch (lockMode)
            {
                case 0: // LockHips - hips are authoritative, skip head-relative clamping
                    break;

                case 1: // LockHead - head is the anchor; the spine may not compress below its rest length, allow stretching further
                    {
                        Vector3 headToHips = hipsTargetPos - headTargetPos;
                        float spineLen = headToHips.magnitude;
                        if (spineLen < restDist)
                        {
                            Vector3 spineDir = spineLen > k_Epsilon ? headToHips / spineLen : hipDesired * Vector3.down;
                            hipsTargetPos = headTargetPos + spineDir * restDist;
                        }
                    }
                    break;

                default: // LockBoth (2) - original behavior: clamp hips relative to head
                    hipsTargetPos = AntiContortionist(headTargetPos, headTargetRot, hipsTargetPos, hipDesired, restDist);
                    hipsTargetPos = MitigateSpineBuckling(headTargetPos, hipDesired, hipsTargetPos, restDist, up);
                    float MaxBendDeg = maxBendDeg.Get(stream);
                    hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                    hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor.Get(stream), maxFactor.Get(stream), up);
                    break;
            }

            hipsTargetPos = ApplyCrouchBodyOffset(stream, headTargetPos, hipsTargetPos, hipDesired, up);
            targetPositionHips.Set(stream, hipsTargetPos);

            // The hinge SYNTHESISES an anterior pelvis pitch on a deep lean so the spine does not swallow the
            // whole reach -- but only when there is no hip tracker. With one, the pelvis rotation is the
            // user's OWN, measured, and must feed straight to IK "how we used to" (the hip-tilt-stabilization
            // that reshaped a tracked pelvis was built and deliberately removed for exactly this reason). The
            // hip-bob/sway synthesis in BasisLocalRigDriver is gated on the same flag, for the same reason:
            // do not invent pelvis motion on top of a tracker.
            if (!hasHipsTracker.Get(stream))
            {
                hipDesired = ApplyHipHinge(stream, headTargetPos, hipsTargetPos, hipDesired, up);
            }

            // Apply hips driver if valid
            if (HandleHips.IsValid(stream))
            {
                HandleHips.SetPosition(stream, hipsTargetPos);
                HandleHips.SetRotation(stream, hipDesired);
            }
            if (HasChestTracker.Get(stream) && HandleChest.IsValid(stream))
            {
                // Neck rotation produced by your spine IK pass – we keep this
                Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;

                // Spine as an extra reference if available (nice stabiliser)
                Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;

                float Value = MaxChestDeltaProperty.Get(stream);
                // Clamp relative to neck and spine
                Quaternion clampedChestRot = ClampRotation(chestDesired, neckRot, Value);
                clampedChestRot = ClampRotation(clampedChestRot, spineRot, Value);

                HandleChest.SetRotation(stream, clampedChestRot);

                Vector3 headPos = targetPositionHead.Get(stream);
                Quaternion headRot = V4ToQuat(targetRotationHead.Get(stream));

                DistributeSpineBend(stream, headPos);
                BiasSpineTowardChest(stream);
                GuardSpineChain(stream);
                SolveSequentialSpineIK(stream, headPos, headRot);
            }
            else if (HandleChest.IsValid(stream) && HandleNeck.IsValid(stream) && HandleHead.IsValid(stream))
            {
                Vector3 headPos = targetPositionHead.Get(stream);
                Quaternion headRot = V4ToQuat(targetRotationHead.Get(stream));

                DistributeSpineBend(stream, headPos);
                ApplyArmSwingChestFollow(stream);
                GuardSpineChain(stream);
                SolveSequentialSpineIK(stream, headPos, headRot);
            }
        }
        // CCD root→tip aim across the hips→head chain. Hips is the fixed anchor (the hip pre-pass
        // already placed it); we rotate spine, chest, neck so the head bone slides onto its target,
        // then pin the head's rotation to the tracker. Rotation-only — bone lengths are preserved
        // implicitly because each joint is rotated in place. Convergence parameters live in
        // spineCache (iterations + squared-position tolerance).
        public void SolveSequentialSpineIK(AnimationStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
        {
            if (!ChainHeadToSpine.IsCreated || ChainHeadToSpine.Length < 3)
                return;

            int chainLen = ChainHeadToSpine.Length;
            const int tipIdx = 0;
            const int firstJoint = 1;
            int lastJoint = chainLen - 2;

            for (int i = 0; i < chainLen; i++)
            {
                if (!ChainHeadToSpine[i].IsValid(stream))
                    return;
            }

            int maxIters = Mathf.Max(1, (int)spineCache.GetRaw(spineMaxIterationsIdx));
            float tolerance = Mathf.Max(0f, spineCache.GetRaw(spineToleranceIdx));
            float tolSqr = tolerance * tolerance;

            float ccdRelax = spineCCDRelax.Get(stream);
            float lumbarTwistKeep = spineTwistKeep.Get(stream);
            float cervicalTwistKeep = spineNeckTwistKeep.Get(stream);
            // Body-relative twist axis (hips-up), NOT world-up: vertical standing, horizontal lying down, so
            // the relax strips the same anatomical axial-twist DOF in any orientation. Falls back to playerUp.
            Quaternion hipsTwistRot = HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : Quaternion.identity;
            Vector3 ccdUp = hipsTwistRot * Vector3.up;
            if (ccdUp.sqrMagnitude < k_SqrEpsilon) ccdUp = playerUp.Get(stream);
            float jointSpan = Mathf.Max(1, lastJoint - firstJoint);
            float neckCone = neckMaxConeDeg.Get(stream);
            float chestCone = MaxChestDeltaProperty.Get(stream);
            Quaternion finalHeadRot = headTargetRot * targetOffsetHead;

            for (int iter = 0; iter < maxIters; iter++)
            {
                Vector3 tipPos = ChainHeadToSpine[tipIdx].GetPosition(stream);
                if ((headTargetPos - tipPos).sqrMagnitude < tolSqr)
                    break;

                // Walk from root-side (spine) toward tip-side (neck) so the longer-lever joints
                // take the bigger swing first; later passes through the loop fine-tune with the
                // shorter levers.
                for (int i = lastJoint; i >= firstJoint; i--)
                {
                    ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                        cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                }
            }

            // ==========================================================================================
            // PHASE B -- THE CHEST AS A SECONDARY IK TARGET. The loop above placed the HEAD (primary,
            // welded to the HMD); the chest position fell out of it as a free FK consequence. Now pull the
            // chest bone onto its own target and RESTORE the head with the joints above the chest, which
            // have spare DOF. The head is never traded for the chest. Bit-identical to head-only above when
            // the chest target is off (weight 0). See SolveChestTarget.
            // ==========================================================================================
            SolveChestTarget(stream, headTargetPos, firstJoint, lastJoint, chainLen, jointSpan,
                cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);

            ChainHeadToSpine[tipIdx].SetRotation(stream, finalHeadRot);
        }
        // One CCD step aiming the head tip from joint `i` -- the exact body of the Phase A loop, extracted so
        // Phase B's head-restore reuses it verbatim (a copy would drift). Shapes the reach (twist graded root
        // -> tip, mid-thoracic stiffened), relaxes, applies the cones, then the anatomy guard LAST.
        void ReachHeadJoint(AnimationStream stream, int i, Vector3 headTargetPos, int firstJoint, int chainLen,
            float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp, float ccdRelax,
            float neckCone, float chestCone)
        {
            const int tipIdx = 0;
            Vector3 jointPos = ChainHeadToSpine[i].GetPosition(stream);
            Vector3 curTipPos = ChainHeadToSpine[tipIdx].GetPosition(stream);

            Vector3 cur = curTipPos - jointPos;
            Vector3 tgt = headTargetPos - jointPos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion delta = QuaternionExt.FromToRotation(cur, tgt);
            float t = (i - firstJoint) / jointSpan;
            float jointTwistKeep = Mathf.Lerp(cervicalTwistKeep, lumbarTwistKeep, t);
            float jointSwingScale = 1f - k_ThoracicBendStiffen * (1f - Mathf.Abs(2f * t - 1f));
            delta = BasisTwistSolveCore.ShapeReachStep(delta, ccdUp, jointTwistKeep, jointSwingScale);
            delta = Quaternion.Slerp(Quaternion.identity, delta, ccdRelax);
            ChainHeadToSpine[i].SetRotation(stream, delta * ChainHeadToSpine[i].GetRotation(stream));

            if (i == firstJoint)
            {
                ClampNeckCone(stream, i, neckCone);
            }
            else if (chainLen >= 5 && i == chainLen - 3)
            {
                ClampChestCone(stream, i, chestCone);
            }

            // LAST, so it sees the outcome of every other constraint on this joint, not just the
            // CCD's own step. The cones above are reach heuristics; this is anatomy.
            GuardSpineJoint(stream, i);
        }
        // The Chest bone in the chain sits at chainLen-3 (the index ClampChestCone uses); the one joint below
        // it -- the Spine (lastJoint) -- is what moves it. Weight 0.5 was the corpus sweet spot: at it, BOTH
        // the chest AND the head placement improved over head-only (the restore sweeps tighten the head).
        // Full weight (1.0) placed the chest slightly better but loosened the head, so it is deliberately not
        // used. Iteration budget (8 x 2 restore) captures ~all of the gain a full 20 does, for a fraction of
        // the cost -- measured, not guessed.
        const float k_ChestIkWeight = 0.5f;
        const int k_ChestIkIters = 8;
        const int k_ChestIkHeadRestoreSweeps = 2;
        void SolveChestTarget(AnimationStream stream, Vector3 headTargetPos, int firstJoint, int lastJoint,
            int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp,
            float ccdRelax, float neckCone, float chestCone)
        {
            // Off (toggle false -> weight 0): return before touching a single bone, so the head-only solve
            // above is the whole story, bit for bit. This is the "same usability" guarantee.
            if (!chestIkTarget.Get(stream))
                return;

            int chestBoneIdx = chainLen - 3;   // the Chest bone
            // Need a real Spine joint below the chest to move it, and real upper joints to restore the head.
            if (chestBoneIdx < firstJoint || lastJoint <= firstJoint || lastJoint <= chestBoneIdx)
                return;

            // THE RAW chest, not the head-hint-biased TargetChestPosition -- pinning to the biased one dragged
            // the torso ~8cm up and leaned the body in desktop / no-tracker mode.
            Vector3 chestTargetPos = TargetChestPositionRaw.Get(stream);
            Vector3 chestBonePos = ChainHeadToSpine[chestBoneIdx].GetPosition(stream);
            // A chest target that is wildly far from the FK chest is a glitching tracker or an unset target;
            // chasing it would wreck the torso. Fall back to the head-only chest. Same guard the old
            // BiasSpineTowardChest used, and the anatomy guard below bounds whatever does get through.
            if ((chestTargetPos - chestBonePos).sqrMagnitude > k_ChestPullMaxDistSqr)
                return;

            // The Spine is the root end of the chain, so its shaping params are those of index lastJoint.
            float spineT = (lastJoint - firstJoint) / jointSpan;
            float spineTwistKeep = Mathf.Lerp(cervicalTwistKeep, lumbarTwistKeep, spineT);
            float spineSwingScale = 1f - k_ThoracicBendStiffen * (1f - Mathf.Abs(2f * spineT - 1f));

            for (int citer = 0; citer < k_ChestIkIters; citer++)
            {
                // 1) rotate the Spine so the Chest bone slides toward its target.
                Vector3 spinePos = ChainHeadToSpine[lastJoint].GetPosition(stream);
                Vector3 cCur = ChainHeadToSpine[chestBoneIdx].GetPosition(stream) - spinePos;
                Vector3 cTgt = chestTargetPos - spinePos;
                if (cCur.sqrMagnitude > k_SqrEpsilon && cTgt.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion cDelta = QuaternionExt.FromToRotation(cCur, cTgt);
                    cDelta = BasisTwistSolveCore.ShapeReachStep(cDelta, ccdUp, spineTwistKeep, spineSwingScale);
                    // Relax x weight: a gentler chest pull lets the head-restore keep pace, which is exactly
                    // why the moderate weight preserves the head where a full pull loosened it.
                    cDelta = Quaternion.Slerp(Quaternion.identity, cDelta, ccdRelax * k_ChestIkWeight);
                    ChainHeadToSpine[lastJoint].SetRotation(stream, cDelta * ChainHeadToSpine[lastJoint].GetRotation(stream));
                    GuardSpineJoint(stream, lastJoint);
                }

                // 2) restore the head with the UPPER joints only (chest and above -- never the Spine, which
                // now owns the chest). They have far more DOF than the head needs, so the head returns to
                // target without disturbing the chest the Spine just placed.
                for (int sweep = 0; sweep < k_ChestIkHeadRestoreSweeps; sweep++)
                {
                    for (int i = lastJoint - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }
            }
        }
        // ==============================================================================================
        // THE ANATOMICAL ENVELOPE. Pulls one spine joint back inside the range of motion its real vertebrae
        // have. See BasisSpineAnatomyCore for the measurements and BasisSpineAnatomy for the table.
        //
        // WHY IT LIVES INSIDE THE CCD LOOP. The CCD is what actually places the head, and before this it
        // rotated the spine, chest and upperChest with NO per-joint limit whatsoever -- its only constraints
        // were a cone on the neck and a cone on the chest. So a limit applied BEFORE the CCD is a suggestion
        // the CCD is free to ignore, which is exactly what happened to BasisSpineBendCore.ClampAsymmetric.
        // And a limit applied AFTER the CCD would drag the head off the HMD, which is not negotiable.
        //
        // Applied per-joint INSIDE the loop, the residual simply redistributes onto the other vertebrae on
        // the next sweep -- which is what a real spine does when you ask one segment for more than it has.
        // The head still converges, because the CCD still gets the last word on it.
        //
        // The chain runs head -> hips, so joint `i`'s PARENT is `i + 1`.
        // ==============================================================================================
        void GuardSpineJoint(AnimationStream stream, int i)
        {
            if (!spineAnatomicalRom.Get(stream))
            {
                return;
            }
            if (!ChainSpineRestFrames.IsCreated || i < 0 || i >= ChainSpineRestFrames.Length)
            {
                return;
            }

            BasisSpineRestFrame frame = ChainSpineRestFrames[i];
            if (!frame.Valid)
            {
                return;   // the head and the hips: commanded, not solved. Never guarded.
            }

            int parent = i + 1;
            if (parent >= ChainHeadToSpine.Length || !ChainHeadToSpine[parent].IsValid(stream) || !ChainHeadToSpine[i].IsValid(stream))
            {
                return;
            }

            Quaternion parentRot = ChainHeadToSpine[parent].GetRotation(stream);
            Quaternion boneRot = ChainHeadToSpine[i].GetRotation(stream);
            Quaternion local = BasisSpineAnatomyCore.Conj(parentRot) * boneRot;

            Quaternion clamped = BasisSpineAnatomyCore.Clamp(local, frame, ChainSpineRoms[i], out BasisSpineClampInfo info);
            if (!info.Touched)
            {
                return;   // legal pose: the bone is not written at all, so it cannot be perturbed.
            }

            ChainHeadToSpine[i].SetRotation(stream, parentRot * clamped);
        }

        // A full sweep of the envelope over every solved vertebra. Run right after DistributeSpineBend so
        // the CCD starts from a legal spine -- the CCD breaks out early when the head is already on target,
        // and on those frames it would otherwise never look at the pre-bend's output at all.
        void GuardSpineChain(AnimationStream stream)
        {
            if (!ChainHeadToSpine.IsCreated || ChainHeadToSpine.Length < 3)
            {
                return;
            }
            for (int i = 1; i <= ChainHeadToSpine.Length - 2; i++)
            {
                GuardSpineJoint(stream, i);
            }
        }
        // Constrains the neck (chain index neckIdx) to within maxConeDeg of the chest→neck
        // direction. Enforced in-loop so chest/spine take the slack on the next CCD sweep.
        void ClampNeckCone(AnimationStream stream, int neckIdx, float maxConeDeg)
        {
            Vector3 chestPos = ChainHeadToSpine[neckIdx + 1].GetPosition(stream);
            Vector3 neckPos = ChainHeadToSpine[neckIdx].GetPosition(stream);
            Vector3 headPos = ChainHeadToSpine[0].GetPosition(stream);

            Vector3 parentDir = neckPos - chestPos;
            Vector3 boneDir = headPos - neckPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
            {
                return;
            }

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            ChainHeadToSpine[neckIdx].SetRotation(stream, correction * ChainHeadToSpine[neckIdx].GetRotation(stream));
        }
        // Mid-thoracic bend stiffness for the spine CCD: the swing of the mid joints is scaled down by this
        // (ends unaffected) so a lean curves at the flexible lumbar + cervical and stays firm through the
        // ribcage, distributing the bend instead of kinking at one joint. 0 = uniform (off).
        const float k_ThoracicBendStiffen = 0.3f;
        // Lateral bend -> a little same-side axial rotation in the pre-bend, so a sustained lean reads as an
        // organic spinal coupling rather than a pure hinge. Small; clamped by the lateral limit downstream.
        const float k_BendTwistCoupling = 0.15f;
        const float k_ChestPosPullMaxDeg = 20f;
        const float k_ChestPullMaxDistSqr = 0.25f;
        const float k_ChestFollowChestShare = 0.6f;
        void ClampChestCone(AnimationStream stream, int chestIdx, float maxConeDeg)
        {
            Vector3 spinePos = ChainHeadToSpine[chestIdx + 1].GetPosition(stream);
            Vector3 chestPos = ChainHeadToSpine[chestIdx].GetPosition(stream);
            Vector3 childPos = ChainHeadToSpine[chestIdx - 1].GetPosition(stream);

            Vector3 parentDir = chestPos - spinePos;
            Vector3 boneDir = childPos - chestPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
                return;

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
                return;

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
                return;

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            ChainHeadToSpine[chestIdx].SetRotation(stream, correction * ChainHeadToSpine[chestIdx].GetRotation(stream));
        }
        void BiasSpineTowardChest(AnimationStream stream)
        {
            if (!HandleSpine.IsValid(stream) || !HandleChest.IsValid(stream))
                return;

            Vector3 chestTargetPos = TargetChestPosition.Get(stream);
            Vector3 spinePos = HandleSpine.GetPosition(stream);
            Vector3 chestPos = HandleChest.GetPosition(stream);

            if ((chestTargetPos - chestPos).sqrMagnitude > k_ChestPullMaxDistSqr)
                return;

            Vector3 cur = chestPos - spinePos;
            Vector3 tgt = chestTargetPos - spinePos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion pull = ClampRotation(QuaternionExt.FromToRotation(cur, tgt), Quaternion.identity, k_ChestPosPullMaxDeg);
            HandleSpine.SetRotation(stream, pull * HandleSpine.GetRotation(stream));
        }
        // Pre-distributes the hips→head bend onto spine and upperChest in hips-local space, split
        // into independent pitch / yaw / roll contributions so anisotropic human ranges of motion
        // can be respected (lumbar twists very little, cervical twists a lot, forward bend ≫ back).
        // Pipeline: (chest spring smooths target) → (decompose bend into pitch/roll, twist into yaw)
        //   → (per-axis weight) → (asymmetric clamp) → (apply as hips-local delta).
        // The chest→neck→head two-bone solve afterwards handles whatever residual reach remains.
        public void DistributeSpineBend(AnimationStream stream, Vector3 headTargetPos)
        {
            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            bool hasSpine = HandleSpine.IsValid(stream);
            bool hasUpper = HandleUpperChest.IsValid(stream);
            if (!hasSpine && !hasUpper)
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Quaternion headWorldRot = V4ToQuat(targetRotationHead.Get(stream)) * targetOffsetHead;

            // ==========================================================================================
            // THE SPINE IS CUED OFF THE NECK, NOT THE HEAD. This is the fix for "looking down forces chest
            // to rotate".
            //
            // BasisSpineBendCore bends the spine by the angle between hips->chest and hips->CUE. Hand it the
            // HEAD and you have handed it a point that is not on the spine at all -- the head sits on the END
            // of the neck and ORBITS it when you nod. So a user who gazes down without moving their torso by
            // one millimetre still swings the head target forward and down, the hips->head vector tips over,
            // and the solver bends the spine to a lean that never happened. Measured on a T-posed adult with
            // the torso held byte-identical: a 45 deg glance down invents 4.4 deg of chest pitch, 60 deg
            // invents 8.4 deg, 75 deg invents 10.4 deg. (BasisSpineGazeContaminationTests.)
            //
            // The neck, estimated RIGIDLY off the head, is exactly invariant to that nod. Write it out: if
            // the head orbits the neck by Q, then
            //     estimatedNeck = (neck + Q*(head-neck)) + (Q*headRot) * inv(headRot)*(neck-head)
            //                   = neck + Q*(head-neck) + Q*(neck-head)
            //                   = neck
            // -- the two lever arms cancel, algebraically, for ANY Q. Not damped, not faded, not clamped:
            // CANCELLED. A gaze cannot move this cue, so it cannot bend the spine, so there is nothing left
            // to tune. BasisSpineGazeContaminationTests pins it at exactly zero.
            //
            // A real human's chest pitches -0.05 deg per degree of gaze -- i.e. not at all -- so zero is not
            // an approximation of the right answer here, it IS the right answer.
            //
            // It also disarms a SECOND bug for free. ComputeSquishMultiplier amplifies the spine's rotation
            // as hips->cue COMPRESSES (x1.42 at 25% compression), and gazing down was shortening hips->HEAD
            // -- so the phantom bend was being multiplied by a phantom squish. The neck does not move on a
            // gaze, so neither does the squish. RestLen moves to hips->NECK to match: the spine spans the
            // spine, and the head was never part of it.
            // ==========================================================================================
            Vector3 neckCue = headTargetPos + headWorldRot * TposeHeadToNeckLocal;

            // A LITTLE REAL SPINE. neckCue is invariant to a pure gaze (the head orbits the neck by Q, the
            // rigid re-attachment un-orbits it -- that is the look-down-stability fix, chest pitch 0.000 deg
            // on any gaze). But that reads as a rigid mannequin under a swiveling head on desktop. Blend the
            // cue a fraction back toward the ACTUAL head: on a look-down the head has orbited forward+down, so
            // the cue tips that way and the chest folds a touch. 0 = rigid, 1 = the full (phantom) follow. A
            // real chest does NOT fold on gaze (corpus: -0.05 deg/deg), so this is a deliberate desktop-feel
            // knob, small by default, and it costs nothing with a chest tracker (the pitch weight is zeroed).
            Vector3 spineCue = Vector3.Lerp(neckCue, headTargetPos, Mathf.Clamp01(spineGazeFollow.Get(stream)));

            Quaternion hipsBind = V4ToQuat(offsetRotationHips.Get(stream));

            BasisSpineBendInput input;
            input.HipsRot = hipsRot;
            input.HipsPos = HandleHips.GetPosition(stream);
            input.ChestPos = HandleChest.GetPosition(stream);
            input.SmoothedHead = ApplyChestSpring(stream, spineCue);
            input.HipsBind = hipsBind;
            input.HeadTargetRot = V4ToQuat(targetRotationHead.Get(stream));
            input.SpineMaxForwardDeg = spineMaxForwardDeg.Get(stream);
            input.SpineMaxBackwardDeg = spineMaxBackwardDeg.Get(stream);
            input.SpineMaxLateralDeg = spineMaxLateralDeg.Get(stream);
            input.SpineBendPitch = spineBendPitch.Get(stream);
            input.SpineBendYaw = spineBendYaw.Get(stream);
            input.SpineBendRoll = spineBendRoll.Get(stream);
            input.UpperBendPitch = upperChestBendPitch.Get(stream);
            input.UpperBendYaw = upperChestBendYaw.Get(stream);
            input.UpperBendRoll = upperChestBendRoll.Get(stream);
            input.AnatDifferentialStiffness = anatDifferentialStiffness.Get(stream);
            input.AnatPelvicTwistRouting = anatPelvicTwistRouting.Get(stream);
            input.SquishBoost = spineSquishBoost.Get(stream);
            input.RestLen = TposeLengthNeckToHips.magnitude;   // the spine spans hips->NECK; the head was never part of it
            input.BendTwistCoupling = k_BendTwistCoupling;
            input.HasSpine = hasSpine;
            input.HasUpper = hasUpper;

            // A tracked chest already measures torso lean, so the head-position-derived forward/lateral
            // pre-bend is redundant -- and looking down swings the HMD forward of the neck, which it
            // misreads as a lean and hunches the chest forward (the squish boost compounds it). Drop the
            // lean (pitch/roll) and let the tracked chest + the spine chain own it; keep the facing twist.
            if (HasChestTracker.Get(stream))
            {
                input.SpineBendPitch = 0f;
                input.SpineBendRoll = 0f;
                input.UpperBendPitch = 0f;
                input.UpperBendRoll = 0f;
            }

            BasisSpineBendCore.Solve(input, out BasisSpineBendResult r);
            if (r.EarlyOut)
            {
                return;
            }

            // Apply the delta in the SAME bind-cancelled frame the core measured it in (hipsRot * inv(bind)),
            // not the raw hips-bone frame. On an identity bind this is hipsRot exactly, so it is bit-identical
            // for the usual rigs; on a rig bound rolled/axis-swapped it stops the anatomically-framed bend from
            // being re-applied about the bone's rolled axes (which leaned the chest sideways by 10-14 deg).
            Quaternion hipsAnat = hipsRot * Quaternion.Inverse(hipsBind);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            if (r.WriteSpine)
            {
                Quaternion deltaWorld = hipsAnat * Quaternion.Euler(r.SpineEuler) * invHipsAnat;
                HandleSpine.SetRotation(stream, deltaWorld * HandleSpine.GetRotation(stream));
            }
            if (r.WriteUpper)
            {
                Quaternion deltaWorld = hipsAnat * Quaternion.Euler(r.UpperEuler) * invHipsAnat;
                HandleUpperChest.SetRotation(stream, deltaWorld * HandleUpperChest.GetRotation(stream));
            }
        }
        // Critically-damped spring on the head target consumed by DistributeSpineBend. Lets the
        // body lag slightly behind quick head moves without affecting the head bone itself.
        // Uses implicit Euler so it stays stable at high Hz / low fps where explicit Euler blows
        // up (omega * dt > 1 → divergent oscillation → NaN → corrupted quaternions downstream).
        Vector3 ApplyChestSpring(AnimationStream stream, Vector3 headTargetPos)
        {
            if (!chestSpringState.IsCreated || !chestSpringInit.IsCreated)
            {
                return headTargetPos;
            }

            float hz = chestSpringHz.Get(stream);
            if (hz <= 0f)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }
            if (chestSpringInit[0] == 0)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }

            float dt = stream.deltaTime;
            if (dt <= 0f)
                return chestSpringState[0];

            BasisChestSpringCore.Step(chestSpringState[0], chestSpringState[1], headTargetPos, dt, hz,
                chestSpringDamping.Get(stream), out Vector3 newPos, out Vector3 newVel);

            // Defensive: if upstream input has produced a NaN, re-seed instead of poisoning the rig.
            if (!IsFinite(newPos) || !IsFinite(newVel))
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                return headTargetPos;
            }

            chestSpringState[0] = newPos;
            chestSpringState[1] = newVel;
            return newPos;
        }
        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        static bool IsFinite(Quaternion q) => !float.IsNaN(q.x) && !float.IsInfinity(q.x) && !float.IsNaN(q.y) && !float.IsInfinity(q.y) && !float.IsNaN(q.z) && !float.IsInfinity(q.z) && !float.IsNaN(q.w) && !float.IsInfinity(q.w);
        // Pelvis tilts forward to share the lean past the threshold. Without this, a deep forward
        // reach makes the spine swallow the entire bend and everything above the hips folds.
        Quaternion ApplyHipHinge(AnimationStream stream, Vector3 headPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUp)
        {
            BasisHipHingeInput input;
            input.HeadPos = headPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.PlayerUp = playerUp;
            input.StartDeg = hipHingeStartDeg.Get(stream);
            input.MaxAddDeg = hipHingeMaxAddDeg.Get(stream);
            BasisHipHingeCore.Solve(input, out BasisHipHingeResult result);
            return result.HipsRot;
        }
        Vector3 ApplyCrouchBodyOffset(AnimationStream stream, Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir)
        {
            if (HasChestTracker.Get(stream) || hasHipsTracker.Get(stream))
            {
                return hipsPos;
            }

            BasisCrouchOffsetInput input;
            input.HeadTargetPos = headTargetPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.Bind = V4ToQuat(offsetRotationHips.Get(stream));
            input.PlayerUp = playerUpDir;
            input.Factor = moveBodyBackWhenCrouching.Get(stream);
            input.RestDist = MinHeadSpineHeight.Get(stream);
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        // Extra forward neck curve at FULL look-down when NeckGazeFollow = 1 (it scales this by the setting
        // and by how far down you look). Modest: the head is re-pinned so this only arcs the neck, but too
        // much cocks the head relative to the neck. The user dials the setting; this is the ceiling.
        const float k_NeckGazeFollowMaxDeg = 18f;
        void ApplyCervicalLordosis(AnimationStream stream)
        {
            if (!HandleNeck.IsValid(stream))
            {
                return;
            }

            Vector3 referenceUp;
            if (HandleChest.IsValid(stream))
            {
                referenceUp = HandleChest.GetRotation(stream) * Vector3.up;
            }
            else
            {
                Vector3 up = playerUp.Get(stream);
                referenceUp = up.sqrMagnitude < k_SqrEpsilon ? Vector3.up : up.normalized;
            }

            BasisCervicalInput input;
            input.BaseDeg = lordosisBaseDeg.Get(stream);
            input.NeckShare = Mathf.Clamp01(lordosisNeckShare.Get(stream));
            input.MaxHeadPitchDeg = lordosisMaxHeadPitchDeg.Get(stream);
            input.ExtremeStartDeg = lordosisExtremeStartDeg.Get(stream);
            input.ExtremeFullDeg = lordosisExtremeFullDeg.Get(stream);
            input.ExtremeRollForwardMaxDeg = lordosisExtremeRollForwardMaxDeg.Get(stream);
            input.ExtremeRollBackwardMaxDeg = lordosisExtremeRollBackwardMaxDeg.Get(stream);
            input.ExtremeHipsHorizontalMax = lordosisExtremeHipsHorizontalMax.Get(stream);
            input.ExtremeChestHorizontalMax = lordosisExtremeChestHorizontalMax.Get(stream);
            input.ExtremeHipsDownMax = lordosisExtremeHipsDownMax.Get(stream);
            input.ExtremeChestDownMax = lordosisExtremeChestDownMax.Get(stream);
            input.ExtremeHipsDownLookUp = lordosisExtremeHipsDownLookUp.Get(stream);
            input.ExtremeChestDownLookUp = lordosisExtremeChestDownLookUp.Get(stream);
            input.PitchGainDeg = Mathf.Max(0f, lordosisPitchGainDeg.Get(stream));
            input.ReferenceUp = referenceUp;
            input.HeadTargetRot = V4ToQuat(targetRotationHead.Get(stream));
            input.HasUpperChest = HandleUpperChest.IsValid(stream);

            BasisCervicalSolveCore.Solve(input, out BasisCervicalResult result);
            if (result.EarlyOut)
            {
                return;
            }

            ReadWriteTransformHandle bendHandle = input.HasUpperChest ? HandleUpperChest : HandleChest;
            if (bendHandle.IsValid(stream) && result.BhDeg != 0f)
            {
                Quaternion bhRot = bendHandle.GetRotation(stream);
                bendHandle.SetRotation(stream, Quaternion.AngleAxis(result.BhDeg, bhRot * Vector3.right) * bhRot);
            }

            if (result.HasExtreme)
            {
                Quaternion refRot = HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : (HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity);
                Vector3 refForward = refRot * Vector3.forward;
                Vector3 refDown = -(refRot * Vector3.up);

                if (HandleHips.IsValid(stream))
                {
                    Vector3 hipsOffset = refForward * result.HipsForwardAmount + refDown * result.HipsDownAmount;
                    HandleHips.SetPosition(stream, HandleHips.GetPosition(stream) + hipsOffset);
                }

                if (HandleChest.IsValid(stream))
                {
                    Vector3 chestOffset = refForward * result.ChestForwardAmount + refDown * result.ChestDownAmount;
                    HandleChest.SetPosition(stream, HandleChest.GetPosition(stream) + chestOffset);
                }
            }

            // A LITTLE REAL SPINE, for the neck: extra forward curve on a look-down, on top of the lordosis.
            // The head is re-pinned to the HMD just below (SetPosition/SetRotation), so this arcs the neck
            // WITHOUT moving the head -- the neck curves, the head stays exactly on target. Look-down only
            // (LookDownFrac); a real cervical spine flexes forward as you look down. 0 = lordosis only.
            float extraNeckDeg = Mathf.Clamp01(neckGazeFollow.Get(stream)) * k_NeckGazeFollowMaxDeg * result.LookDownFrac;
            float totalNeckDeg = result.NeckDeg + extraNeckDeg;
            if (totalNeckDeg != 0f)
            {
                Quaternion neckRotCurrent = HandleNeck.GetRotation(stream);
                HandleNeck.SetRotation(stream, Quaternion.AngleAxis(totalNeckDeg, neckRotCurrent * Vector3.right) * neckRotCurrent);
            }

            if (HandleHead.IsValid(stream))
            {
                HandleHead.SetPosition(stream, targetPositionHead.Get(stream));
                HandleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
            }
        }
        // Anatomy: shoulder slide. Shoulders don't fully follow chest twist past ~30° because the
        // scapula slides on the rib cage. Counter-yaw both shoulders by a fraction of the chest's
        // twist relative to hips, capped at 15°.
        void ApplyShoulderSlide(AnimationStream stream)
        {
            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Quaternion chestRot = HandleChest.GetRotation(stream);
            Quaternion chestLocal = Quaternion.Inverse(hipsRot) * chestRot;
            // The chest's AXIAL twist about the spine (hips-up), by swing-twist -- NOT eulerAngles.y, which
            // gimbal-locks the instant the chest pitches ~90 deg off the hips (a deep forward bend on any rig,
            // or a chest bound pitched near vertical) and threw a phantom counter-yaw into the shoulders. The
            // yaw is applied about this same hips-up axis below, so measuring about it keeps the two in step.
            float chestYaw = BasisTwistSolveCore.SignedTwistAngleDeg(chestLocal, Vector3.up);

            const float threshold = 30f;
            const float maxCounter = 15f;
            const float fraction = 0.4f;
            float excess = Mathf.Abs(chestYaw) - threshold;
            if (excess <= 0f)
                return;

            float counterYaw = -Mathf.Sign(chestYaw) * Mathf.Min(excess * fraction, maxCounter);
            ApplyShoulderYaw(stream, HandleLeftShoulder, hipsRot, counterYaw);
            ApplyShoulderYaw(stream, HandleRightShoulder, hipsRot, counterYaw);
        }
        void ApplyShoulderYaw(AnimationStream stream, ReadWriteTransformHandle shoulder, Quaternion hipsRot, float yawDeg)
        {
            if (!shoulder.IsValid(stream))
                return;
            Quaternion delta = hipsRot * Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.Inverse(hipsRot);
            shoulder.SetRotation(stream, delta * shoulder.GetRotation(stream));
        }
        // Yaw the chest toward the hand-target midpoint relative to hips. Applied around the
        // hips-local Y axis, which is approximately the spine "twist" axis in normal stances —
        // close to orthogonal to the head-reach direction, so SolveSequentialSpineIK's aim
        // corrections don't undo it. Skipped when a chest tracker is active; that case owns
        // chest rotation directly.
        void ApplyArmSwingChestFollow(AnimationStream stream)
        {
            float factor = chestArmSwingFactor.Get(stream);
            if (factor <= 0f)
            {
                return;
            }

            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            bool leftEnabled = enabledLeftHand.Get(stream) > 0f;
            bool rightEnabled = enabledRightHand.Get(stream) > 0f;
            if (!leftEnabled && !rightEnabled)
            {
                return;
            }

            Vector3 leftPos = leftEnabled ? targetPositionLeftHand.Get(stream) : Vector3.zero;
            Vector3 rightPos = rightEnabled ? targetPositionRightHand.Get(stream) : Vector3.zero;
            Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
            Vector3 hipsPos = HandleHips.GetPosition(stream);
            // Bind-cancelled hips frame (hipsRot * inv(bind)): the hand-midpoint is decomposed into yaw/pitch
            // in the body's ANATOMICAL right/forward, and the delta re-applied about the same axes. In the raw
            // hips-bone frame a rolled bind turned the forward-follow into a chest roll. No-op at identity bind.
            Quaternion hipsAnat = HandleHips.GetRotation(stream) * Quaternion.Inverse(V4ToQuat(offsetRotationHips.Get(stream)));
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            Vector3 localMid = invHipsAnat * (handMid - hipsPos);

            float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
            float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;

            Vector3 localMidChest = invHipsAnat * (handMid - HandleChest.GetPosition(stream));
            float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;

            float maxDeg = chestArmSwingMaxDeg.Get(stream);
            if (maxDeg > 0f)
            {
                yawDeg = Mathf.Clamp(yawDeg, -maxDeg, maxDeg);
                pitchDeg = Mathf.Clamp(pitchDeg, -maxDeg, maxDeg);
            }

            Quaternion local = Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, Vector3.right);
            Quaternion deltaWorld = hipsAnat * local * invHipsAnat;

            if (HandleUpperChest.IsValid(stream))
            {
                Quaternion chestPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, k_ChestFollowChestShare);
                Quaternion upperPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, 1f - k_ChestFollowChestShare);
                HandleChest.SetRotation(stream, chestPart * HandleChest.GetRotation(stream));
                HandleUpperChest.SetRotation(stream, upperPart * HandleUpperChest.GetRotation(stream));
            }
            else
            {
                HandleChest.SetRotation(stream, deltaWorld * HandleChest.GetRotation(stream));
            }
        }
        // Distributes a fraction of the child bone's roll (around the parent bone's longitudinal
        // axis) onto a twist bone that sits as a child of the parent. Uses swing-twist quaternion
        // decomposition: the child's local rotation is split into a "swing" (axis perpendicular to
        // the bone) and a "twist" (axis along the bone). We apply only the twist component, scaled
        // by `fraction`, to the twist bone — the original child bone's rotation is not changed.
        // No-op when the twist handle isn't bound (rig has no twist bone) or fraction is zero.
        void SolveArmTwist(AnimationStream stream, ReadWriteTransformHandle parent, ReadWriteTransformHandle child, ReadWriteTransformHandle twist, float fraction)
        {
            if (!twist.IsValid(stream) || fraction <= 0f)
                return;
            if (!parent.IsValid(stream) || !child.IsValid(stream))
                return;

            Vector3 parentPos = parent.GetPosition(stream);
            Vector3 childPos = child.GetPosition(stream);
            // Even distribution: the twist bone absorbs a share equal to its POSITION along the segment, so the
            // roll spreads as a linear gradient instead of piling up between a wrist-end twist bone and the hand
            // (the candy-wrapper). 'fraction' is the distribution strength (1 = fully even, 0 = no twist bone).
            float positionFraction = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, twist.GetPosition(stream));

            BasisTwistSolveInput input;
            input.ParentRotation = parent.GetRotation(stream);
            input.ChildRotation = child.GetRotation(stream);
            input.ParentToChild = childPos - parentPos;
            input.Fraction = positionFraction * fraction;

            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult result);
            if (result.Apply)
            {
                twist.SetRotation(stream, result.TwistWorldRotation);
            }
        }
        static Quaternion ExtractTwist(Quaternion q, Vector3 axis) => BasisTwistSolveCore.ExtractTwist(q, axis);
        // Shoulder pre-solve. Runs whenever the shoulder bone exists and the global toggle is on — a
        // dedicated shoulder tracker is no longer required. hasShoulderTrackerProp (the shoulder rig
        // layer) selects the base: the tracker when present, else the chest-anchored rest. The elbow
        // hint drives the upper-arm direction when an elbow tracker is present, hand target otherwise.
        public void SolveShoulder(AnimationStream stream, ReadWriteTransformHandle shoulderHandle, BoolProperty hasShoulderTrackerProp, Vector3Property handTargetPosProp, Vector3Property hintPosProp, BoolProperty hintWeightProp, Vector3 tposeArmDir, Quaternion tposeShoulderRot, Quaternion tposeChestRot, float tposeArmLength, float tposeClavicleLen, float tposeElbowLen, bool isLeft)
        {
            if (!shoulderHandle.IsValid(stream))
            {
                return;
            }

            Quaternion trackerRot = V4ToQuat(isLeft ? TargetRotationLeftShoulder.Get(stream) : TargetRotationRightShoulder.Get(stream));

            BasisShoulderSolveInput input;
            input.ShoulderPos = shoulderHandle.GetPosition(stream);
            input.HandTargetPos = handTargetPosProp.Get(stream);
            input.ElbowPos = hintPosProp.Get(stream);
            input.HasElbow = hintWeightProp.Get(stream);
            input.HasShoulderTracker = hasShoulderTrackerProp.Get(stream);
            input.ChestRot = HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity;
            input.TposeChestRot = tposeChestRot;
            input.TposeShoulderRot = tposeShoulderRot;
            input.TposeArmDirWorld = tposeArmDir;
            input.TposeArmLength = tposeArmLength;
            input.TposeClavicleLength = tposeClavicleLen;
            input.TposeElbowLength = tposeElbowLen;
            input.ShrugEnabled = shoulderShrugEnabled.Get(stream);
            input.ElevationFactor = shoulderElevationFactor.Get(stream);
            input.ProtractionFactor = shoulderProtractionFactor.Get(stream);
            input.CoupleRatio = k_ShoulderCoupleRatio;
            input.MaxShoulderDeg = k_ShoulderMaxDeg;
            input.TrackerFinal = trackerRot * (isLeft ? targetOffsetLeftShoulder : targetOffsetRightShoulder);
            input.IsLeft = isLeft;

            BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult result);
            if (result.Apply)
            {
                shoulderHandle.SetRotation(stream, result.ShoulderRotation);
            }
        }
        public static Vector3 ClampHipsAroundHead(Vector3 headPos, Vector3 hipsPos, float restDistance, float minFactor, float maxFactor, Vector3 playerUp)
        {
            Vector3 headToHips = hipsPos - headPos;
            float dist = headToHips.magnitude;
            float minD = restDistance * minFactor;
            float maxD = restDistance * maxFactor;
            if (dist < k_Epsilon)
            {
                return headPos - minD * playerUp; // degenerate: place the hips straight below the head
            }

            Vector3 dir = headToHips / dist;
            // The hips must never rise above the head -- that inversion is the deep-crouch flip (hips fly up).
            // If the head→hips ray points upward, drop it to head height (a full forward fold) keeping its
            // heading; if that heading is degenerate too, fall straight down. Below-head poses are untouched,
            // so normal posture/lean is unchanged -- only the inversion is clamped.
            float upDot = Vector3.Dot(dir, playerUp);
            if (upDot > 0f)
            {
                Vector3 horiz = dir - playerUp * upDot;
                dir = horiz.sqrMagnitude > k_SqrEpsilon ? horiz.normalized : -playerUp;
            }

            return headPos + dir * Mathf.Clamp(dist, minD, maxD);
        }
        public static Vector3 EnforceSpineBendLimit(Vector3 headPos, Vector3 hipsPos, float maxBendDeg, Vector3 playerUp)
        {
            if (maxBendDeg <= 0f)
            {
                return hipsPos;
            }

            Vector3 diff = hipsPos - headPos;
            if (diff.sqrMagnitude < k_MinMag)
            {
                return hipsPos;
            }

            Vector3 up = playerUp;

            // Decompose head→hips into a downward drop (along -up) and a horizontal lean.
            float down = Vector3.Dot(diff, -up);  // signed: hips are below the head when > 0
            Vector3 lateral = diff + up * down;   // diff minus the (-up * down) vertical part
            float lateralLen = lateral.magnitude;

            // The hips sit at most maxBendDeg off straight-down from the head -- and NEVER above it. The
            // downward drop that puts them exactly on that cone is lateral / tan(maxBend); if the current
            // drop is less (over-bent, or inverted with down <= 0) pull it down onto the cone, below the head.
            // Without this, a deep crouch drives the hips up/sideways here as the head passes hip height.
            // Already within the cone (and below the head) => unchanged, so normal posture is untouched.
            // Clamp the cone angle below 90deg so tan stays finite and positive (>=90 would blow up / go
            // negative): the hips can fold to nearly horizontal but never above the head.
            float coneTan = Mathf.Tan(Mathf.Min(maxBendDeg, 89.9f) * Mathf.Deg2Rad);
            float minDown = lateralLen / Mathf.Max(coneTan, k_MinMag);
            if (down >= minDown)
            {
                return hipsPos;
            }

            return headPos - up * minDown + lateral;
        }
        /// <summary>
        /// Anti-contortionist: enforces minimum hip-to-head distance based on angular similarity
        /// between head and hip facing directions. When facing same direction, min distance is near
        /// full rest length; facing opposite, it can compress more. From HVR-IK's HIKSpineSolver.
        /// </summary>
        public static Vector3 AntiContortionist(Vector3 headPos, Quaternion headRot, Vector3 hipsPos, Quaternion hipsRot, float restDistance)
        {
            Vector3 headFwd = headRot * Vector3.forward;
            Vector3 hipsFwd = hipsRot * Vector3.forward;
            float facingSimilarity = Vector3.Dot(headFwd, hipsFwd);

            float minDistFactor = Mathf.Lerp(0.2f, 0.85f, Mathf.Clamp01((facingSimilarity + 1f) * 0.5f));
            float minDist = restDistance * minDistFactor;

            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist < minDist && currentDist > k_Epsilon)
            {
                return headPos + diff * (minDist / currentDist);
            }
            return hipsPos;
        }
        /// <summary>
        /// Spine buckling fix: when the body is upright but the hip-to-head distance is shorter
        /// than rest pose, the FABRIK chain can buckle into unnatural S-curves. This pushes the
        /// hips downward to prevent oscillation. From HVR-IK's HIKSpineSolver.
        /// </summary>
        public static Vector3 MitigateSpineBuckling(Vector3 headPos, Quaternion hipsRot, Vector3 hipsPos, float restDistance, Vector3 playerUp)
        {
            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist >= restDistance || currentDist < k_Epsilon)
                return hipsPos;

            Vector3 hipsUp = hipsRot * Vector3.up;
            Vector3 spineDir = (headPos - hipsPos).normalized;

            float tension = Mathf.Clamp01(Vector3.Dot(hipsUp, spineDir));
            float compression = 1f - (currentDist / restDistance);

            float pushAmount = compression * tension * restDistance * 0.5f;
            return hipsPos - playerUp * pushAmount;
        }
        public static Quaternion ClampRotation(Quaternion current, Quaternion reference, float maxAngleDeg)
        {
            // Angle between the two orientations
            float angle = Quaternion.Angle(reference, current);
            if (angle <= maxAngleDeg)
            {
                return current;
            }

            // Scale back toward the reference so the final difference is exactly maxAngleDeg
            float t = maxAngleDeg / Mathf.Max(angle, k_Epsilon);
            return Quaternion.Slerp(reference, current, t);
        }
        public void ApplyRotation(AnimationStream stream, BoolProperty enabledProp, ReadWriteTransformHandle handle, Vector4Property targetRotProp, Quaternion RotationOffset)
        {
            if (!handle.IsValid(stream))
            {
                return;
            }

            if (enabledProp.Get(stream))
            {
                handle.SetRotation(stream, V4ToQuat(targetRotProp.Get(stream)) * RotationOffset);
            }
        }
        public void SolveTwoBoneIKArms(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool hintWeight, bool hintIsTracker, Quaternion targetOffset)
        {
            // Geometry lives in BasisArmSolveCore so the offline sweep harness solves the
            // exact same elbow math. The core returns incremental deltas; apply them through
            // the stream in the original order (identity steps are exact no-ops).
            BasisArmSolveInput input = default;
            input.Shoulder = root.GetPosition(stream);
            input.Elbow = mid.GetPosition(stream);
            input.Hand = tip.GetPosition(stream);
            input.RootRotation = root.GetRotation(stream);
            input.MidRotation = mid.GetRotation(stream);
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint.translation;
            input.HintWeight = hintWeight;
            input.TargetOffset = targetOffset;
            input.PlayerUp = playerUp.Get(stream);
            // No per-frame swivel clamp. The rig runs after the animator resets the bones, so the solve is
            // stateless: a per-frame cap can't "ease in" over frames, it just permanently pins the elbow that
            // many degrees from the animated bend -- which is why an assigned elbow tracker did almost nothing
            // (6deg/frame). Offline always ran unclamped (MaxValue) and its tests pass, so full swivel is the
            // proven-safe path. The anti-parallel flip is held off by the commit + hand-reach reduction in
            // BasisArmSolveCore (reach stays exact), not by clamping the swivel.
            input.HintIsTracker = hintIsTracker;
            input.HintMaxStepDeg = float.MaxValue;
            // The ANIMATED hand rotation (nothing has written the tip yet this frame): the neutral the
            // wrist-roll relief measures the controller's roll against.
            input.TipRotation = tip.GetRotation(stream);
            // A real tracker's measured lower-arm rotation feeds the forearm roll; zero keeps it off for
            // the model path, whose hint rotation is just the stale property value.
            input.HintRotation = hintIsTracker ? hint.rotation : default;

            BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
        }
        /// <summary>
        /// The ARM's body frame, live, from BONE POSITIONS: shoulder line for right, chest->neck for up.
        ///
        /// From POSITIONS, not from the chest bone's ROTATION, and that is the whole reason it transfers. A
        /// bone's local axes are a rig convention, so a frame taken from rotations is fitted to one skeleton and
        /// no other. It also deletes the old frame's entire problem: ArmBendFrame had to strip the chest's YAW
        /// (or head-gaze chest twist swept the lookup and flipped the elbow pole) and then spring-smooth the hips
        /// to stop hip sway wobbling the derived elbow. A position frame has no yaw to strip -- the shoulder line
        /// IS the yaw -- so both the twist-extraction and the hip-frame spring go away.
        /// </summary>
        BasisSwivelFrame BuildArmFrame(AnimationStream stream)
        {
            if (!HandleLeftUpperArm.IsValid(stream) || !HandleRightUpperArm.IsValid(stream)
                || !HandleChest.IsValid(stream) || !HandleNeck.IsValid(stream))
            {
                return default;   // Valid = false; the caller leaves the arm on the solver's own fallback pole
            }

            return BasisSwivelHintCore.BuildFrame(
                HandleLeftUpperArm.GetPosition(stream), HandleRightUpperArm.GetPosition(stream),
                HandleChest.GetPosition(stream), HandleNeck.GetPosition(stream));
        }
        /// <summary>
        /// The LEG's body frame hangs off the PELVIS, not the chest: hip line for right, hips->chest for up.
        /// Same positions-only construction, same reason.
        /// </summary>
        BasisSwivelFrame BuildLegFrame(AnimationStream stream)
        {
            if (!HandleLeftUpperLeg.IsValid(stream) || !HandleRightUpperLeg.IsValid(stream)
                || !HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return default;
            }

            return BasisSwivelHintCore.BuildFrame(
                HandleLeftUpperLeg.GetPosition(stream), HandleRightUpperLeg.GetPosition(stream),
                HandleHips.GetPosition(stream), HandleChest.GetPosition(stream));
        }
        public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float abSqr = Vector3.Dot(ab, ab);
            if (abSqr <= k_SqrEpsilon)
            {
                return a;
            }

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
            return a + ab * t;
        }
        public static void SegmentSegmentClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a <= k_SqrEpsilon && e <= k_SqrEpsilon)
            {
                s = t = 0.0f; c1 = p1; c2 = p2; return;
            }
            if (a <= k_SqrEpsilon)
            {
                s = 0.0f; t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= k_SqrEpsilon)
                {
                    t = 0.0f; s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    if (denom != 0.0f) s = Mathf.Clamp01((b * f - c * e) / denom);
                    else s = 0.0f;

                    t = (b * s + f) / e;
                    if (t < 0.0f) { t = 0.0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1.0f) { t = 1.0f; s = Mathf.Clamp01((b - c) / a); }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
        public static Vector3 CapsuleCapsuleResolve(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float r2, Vector3 playerUp)
        {
            SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
            Vector3 n = c1 - c2;
            float dSqr = Vector3.Dot(n, n);
            float rSum = r1 + r2;

            if (dSqr >= rSum * rSum) return Vector3.zero;

            Vector3 normal;
            if (dSqr > k_SqrEpsilon) normal = n / Mathf.Sqrt(dSqr);
            else
            {
                Vector3 axis = (q2 - p2);
                normal = Vector3.Normalize(Vector3.Cross(axis, playerUp));
                if (normal.sqrMagnitude < k_MinMag)
                {
                    normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
                }

                if (normal.sqrMagnitude < k_MinMag)
                {
                    normal = playerUp;
                }
            }

            float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
            float penetration = (rSum - d);
            return normal * penetration;
        }
        public static void SwingElbowAroundAC(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3 desiredB)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);

            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= k_SqrEpsilon) return;

            Vector3 n = AC / Mathf.Sqrt(acSqr);
            Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Sqr = Vector3.Dot(v1, v1);
            float v2Sqr = Vector3.Dot(v2, v2);
            if (v1Sqr <= k_SqrEpsilon || v2Sqr <= k_SqrEpsilon) return;

            v1 /= Mathf.Sqrt(v1Sqr);
            v2 /= Mathf.Sqrt(v2Sqr);

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot);
            Vector3 cross = Vector3.Cross(v1, v2);
            float dir = Mathf.Sign(Vector3.Dot(cross, n));
            Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

            root.SetRotation(stream, swing * root.GetRotation(stream));
        }
        // Temporal continuity for a 3-bone chain's mid-joint swing around the root→tip axis.
        // Engages ONLY when SolveHand's torso-collision tag changes (the push starts, ends, or flips
        // side) and rate-limits the elbow/knee swing until that pop has eased in; free-air reaching
        // and pole flips are accepted instantly. Carries the stored swing with root→tip motion and
        // re-seeds when the tip target teleports. Keys off persistent state + the target — never the
        // bone it overwrites, which would oscillate.
        void ApplySwingContinuity(AnimationStream stream, int slot, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3 targetPos, float rateDegPerSec, float dt)
        {
            if (!swingContinuityInit.IsCreated || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            Vector3 a = root.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);

            BasisSwingContinuityState state;
            state.LastDir = swingLastDir[slot];
            state.LastAxis = swingLastAxis[slot];
            state.LastTarget = swingLastTarget[slot];
            state.SmoothState = swingSmoothState[slot];
            state.Seeded = swingContinuityInit[slot] != 0;
            int collided = swingCollided.IsCreated ? swingCollided[slot] : 0;

            BasisSwingContinuityCore.Step(state, a, b, c, targetPos, collided, rateDegPerSec, dt, out BasisSwingContinuityResult r);
            if (!r.Valid)
            {
                return;
            }

            if (r.ApplySwing)
            {
                Quaternion preservedHandRot = tip.GetRotation(stream);
                SwingElbowAroundAC(stream, root, mid, tip, a + r.NewDir);
                tip.SetPosition(stream, c);
                tip.SetRotation(stream, preservedHandRot);
            }

            swingLastDir[slot] = r.State.LastDir;
            swingLastAxis[slot] = r.State.LastAxis;
            swingLastTarget[slot] = r.State.LastTarget;
            swingSmoothState[slot] = r.State.SmoothState;
            swingContinuityInit[slot] = 1;
        }
        public static Vector3 PushOutFromCapsule(Vector3 p, Vector3 a, Vector3 b, float radiusWithSkin, Vector3 playerUp)
        {
            Vector3 q = ClosestPointOnSegment(p, a, b);
            Vector3 qp = p - q;
            float dSqr = Vector3.Dot(qp, qp);
            if (dSqr >= radiusWithSkin * radiusWithSkin) return p;
            float d = Mathf.Sqrt(Mathf.Max(dSqr, k_SqrEpsilon));
            Vector3 n = (d > 0f) ? (qp / d) : playerUp;
            return q + n * radiusWithSkin;
        }

        // Capsule-vs-capsule penetration check for one torso segment. Keeps the deepest
        // penetration depth across all checked segments. Direction comes from the
        // shoulder offset (in SolveHand), not from per-segment normals — the shoulder
        // is anatomically attached to its arm's side of the body, while the elbow may
        // have been pushed through to the wrong side.
        public static void AccumulateWorstTorsoSegment(
            Vector3 shoulderPos, Vector3 elbowPos, float upperArmR,
            Vector3 segA, Vector3 segB, float segR, Vector3 playerUp,
            ref float worstPenetration)
        {
            Vector3 c = CapsuleCapsuleResolve(shoulderPos, elbowPos, upperArmR, segA, segB, segR, playerUp);
            float pen = c.magnitude;
            if (pen > worstPenetration)
            {
                worstPenetration = pen;
            }
        }
        /// <summary>
        /// Evaluates the Two-Bone IK algorithm.
        /// </summary>
        /// <param name="stream">The animation stream to work on.</param>
        /// <param name="root">The transform handle for the root transform.</param>
        /// <param name="mid">The transform handle for the mid transform.</param>
        /// <param name="tip">The transform handle for the tip transform.</param>
        /// <param name="target">The transform handle for the target transform.</param>
        /// <param name="hint">The transform handle for the hint transform.</param>
        /// <param name="HasHint">The weight for which hint transform has an effect on IK calculations. This is a value in between 0 and 1.</param>
        /// <param name="targetOffset">The offset applied to the target transform.</param>
        public void SolveTwoBone(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, float hintWeight, Quaternion targetOffset, Vector3 BendNormal, float hintDistrust = 0f)
        {
            BasisLegSolveInput input = default;
            input.Root = root.GetPosition(stream);
            input.Mid = mid.GetPosition(stream);
            input.Tip = tip.GetPosition(stream);
            input.RootRotation = root.GetRotation(stream);
            input.MidRotation = mid.GetRotation(stream);
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint.translation;
            input.HintWeight = hintWeight;
            input.HintDistrust = hintDistrust;
            input.TargetOffset = targetOffset;
            input.BendNormal = BendNormal;

            BasisLegSolveCore.Solve(input, out BasisLegSolveResult result);

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
        }
        public Quaternion V4ToQuat(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);
        public void SolveLegs(AnimationStream stream, FloatProperty enabledProp, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3Property targetPosProp, Vector4Property targetRotProp, Vector3Property hintPosProp, FloatProperty hintWeightProp, Quaternion targetOffset, Vector3Property bendNormalProp, BoolProperty hintIsTrackerProp, int legSlot)
        {
            float posWeight = enabledProp.Get(stream);
            if (posWeight <= 0f)
            {
                return;
            }

            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                return;
            }
            Quaternion origRootRot = root.GetRotation(stream);
            Quaternion origMidRot = mid.GetRotation(stream);
            Quaternion origTipRot = tip.GetRotation(stream);

            // Solve at full strength toward the IK target
            Quaternion tRot = V4ToQuat(targetRotProp.Get(stream));
            // Zero-quaternion target = position-only foot IK: keep the foot's pre-solve (animation) rotation,
            // which is already correct, instead of applying target*offset. Sidesteps the foot offset entirely.
            //
            // Written as !(x > 0.5f), NOT (x < 0.5f). Those are the same for every finite number and OPPOSITE for
            // NaN: `NaN < 0.5f` is FALSE, so the old shape declared a NaN target "valid" and fed it straight into
            // SolveTwoBone -- and a NaN'd bone transform PERSISTS in Unity, so the leg dies and never recovers,
            // not even once good data returns. `!(NaN > 0.5f)` is TRUE, so a NaN now lands in the SAFE branch and
            // the foot simply keeps the animation's rotation. A validity check must be "reject unless good", never
            // "reject if bad", or it fails open on exactly the input that hurts most.
            float tRotSqrLen = tRot.x * tRot.x + tRot.y * tRot.y + tRot.z * tRot.z + tRot.w * tRot.w;
            bool preserveTip = !(tRotSqrLen > 0.5f);
            if (preserveTip) tRot = origTipRot;
            float hintW = hintWeightProp.Get(stream);

            AffineTransform target = new AffineTransform(targetPosProp.Get(stream), tRot);
            // Hint rotation is unused by the leg solve (BasisLegSolveInput has no rotation field).
            AffineTransform hint = new AffineTransform(hintPosProp.Get(stream), Quaternion.identity);
            Vector3 bendNormal = bendNormalProp.Get(stream);

            float hintDistrust = 0f;
            if (!(hintW > 0f))
            {
                // NO KNEE TRACKER. The leg used to have no hint model AT ALL here -- it fell through to
                // BendNormal = hips-right, a FIXED body axis. A fixed pole collapses precisely when the leg
                // straightens, and standing IS a straight leg, so the knee sat on the pole singularity nearly all
                // the time: that is why it snapped past ~95% extension and why it never tracked where a real
                // knee was. Predict the swivel angle instead; see BasisLegSwivelModel.
                //
                // Fed as a HINT, deliberately, and NOT by overwriting BendNormal. BendNormal does double duty in
                // BasisLegSolveCore: it is the no-hint fallback pole AND it is the ANTERIOR REFERENCE for the
                // half-space guard that stops a knee bending backwards through the joint. Overwrite it and the
                // guard starts measuring "anterior" from the model's own answer, which makes it unfalsifiable.
                // As a hint the model steers the knee and the hips-right anterior reference still guards it.
                BasisSwivelFrame frame = BuildLegFrame(stream);

                Vector3 hipPos = root.GetPosition(stream);
                float upperLen = (mid.GetPosition(stream) - hipPos).magnitude;
                float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
                float legLen = upperLen + lowerLen;
                bool isLeft = legSlot == 0;

                // The confidence is used as POLE distrust, never as a fade of hintW -- hintW is discontinuous
                // at zero, and that jump is the pop the earlier weight-fade attempt measured (70 -> 65) and
                // wrongly blamed on the idea rather than the mechanism. See BasisSwivelHintCore.LegModelTrust.
                if (BasisSwivelHintCore.LegHint(frame, hipPos, target.translation, legLen, isLeft,
                                                out Vector3 modelHint, out float conf))
                {
                    hint = new AffineTransform(modelHint, Quaternion.identity);
                    hintW = 1f;
                    hintDistrust = 1f - BasisSwivelHintCore.LegModelTrust(conf);
                }
            }

            SolveTwoBone(stream, root, mid, tip, target, hint, hintW, targetOffset, bendNormal, hintDistrust);
            // Rotation-only fade: the solve produces rotations, so blending positions here would
            // translate bones off the FK chain (dislocated foot) mid-fade.
            if (posWeight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), posWeight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), posWeight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), posWeight));
            }
            if (preserveTip) tip.SetRotation(stream, origTipRot);

            // Body-relative One-Euro on the OUTPUT knee swivel (leg roll about the hip->foot axis): damps
            // swivel jitter without lagging bulk locomotion (translation/turn move the whole leg, so the
            // swivel angle barely changes). Two entry points, different cutoffs:
            //  - tracked knee hint: the pole is a physical tracker whose few-mm jitter is amplified into
            //    degrees of knee swivel by the leg solve's short pole lever arm -> shave that jitter, but
            //    stay responsive so deliberate shin motion isn't lagged.
            //  - no foot tracker (preserveTip): the near-full-extension standing leg rolls on hips-yaw
            //    jitter via the bend normal -> heavy 1 Hz floor (the original leg-twist fix).
            if (legSwivelSmoothing.Get(stream))
            {
                if (hintIsTrackerProp.Get(stream) || !preserveTip)
                {
                    // Something REAL drives this leg -- a knee/lower-leg tracker, or (no knee tracker but) a FOOT
                    // tracker. Track it responsively.
                    //
                    // The foot-tracker case must NOT get the heavy standing floor below. That floor is justified by
                    // "a turn moves the whole leg, so the swivel angle is ~unchanged" -- which only holds when the
                    // foot moves WITH the body. A tracked foot is welded to the user's REAL foot, so a
                    // character-controller turn rotates the hips while the foot stays put in the world: the leg's
                    // body-frame geometry genuinely swings, the swivel angle really does change, and a 1 Hz
                    // low-pass drags the knee visibly behind the turn. The pole is still invented and still needs
                    // damping -- just at the responsive rate, not the fabricated-leg rate.
                    //
                    // ⭐ A REAL KNEE TRACKER DOES NOT GET THE POLE-CONDITIONING. The conditioning multiplies beta
                    // by sin(thigh-off-axis) -- ~0.04 on a standing leg -- which strangled the "opens fast so real
                    // shin motion isn't lagged" beta below (0.20) down to ~0.007 exactly where a leg LIVES. That
                    // is "the knee trackers are way too slow to update": the designed responsiveness was being
                    // multiplied away. The conditioning models the swivel as NOISE near straight, which is right
                    // for an INVENTED pole -- but a strapped-on tracker's pole is a MEASUREMENT with a physical
                    // stand-off (the same doctrine the arm's stabilizer and wrist relief already follow: a
                    // measured pole is not second-guessed), and the One-Euro's own derivative cutoff is what
                    // separates sustained shin motion from mm jitter. That unconditioned model is EXACTLY what
                    // BasisLegTwistSmoothingTests.TrackedFilter_RejectsAmplifiedHintJitter gates -- the live path
                    // now matches its own test. Foot-only keeps the conditioning: its pole is still invented.
                    SmoothKneeSwivel(stream, root, mid, tip, legSlot, stream.deltaTime,
                        k_TrackedKneeSwivelMinCutoffHz, k_TrackedKneeSwivelBeta, k_TrackedKneeSwivelDerivCutoffHz,
                        conditionOnPole: !hintIsTrackerProp.Get(stream));
                }
                else
                {
                    // Nothing real drives this leg: no knee tracker AND no foot tracker, so the pole is invented
                    // (BendNormal = hipsRot * right) and the foot rides the body. A near-full-extension standing
                    // leg sits on the pole singularity, where hips-yaw jitter is amplified hardest into knee
                    // swivel -> heavy 1 Hz floor (the original leg-twist fix). Safe here precisely BECAUSE the
                    // foot moves with the body: a turn carries the whole leg, so the body-frame swivel angle
                    // barely changes and there is nothing real for the filter to lag.
                    SmoothKneeSwivel(stream, root, mid, tip, legSlot, stream.deltaTime,
                        BasisSwivelFilterCore.MinCutoffHz, BasisSwivelFilterCore.Beta, BasisSwivelFilterCore.DerivCutoffHz,
                        conditionOnPole: true);
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(AnimationStream stream, ReadWriteTransformHandle h, Vector3Property p, Vector4Property r, Vector4Property o, BoolProperty sw)
        {
            if (h.IsValid(stream))
            {
                if (sw.Get(stream))
                {

                    Vector3 targetPos = p.Get(stream);
                    Quaternion targetRot = V4ToQuat(r.Get(stream));
                    Quaternion offsetRot = V4ToQuat(o.Get(stream));
                    Quaternion finalRot = targetRot * offsetRot;

                    h.SetPosition(stream, targetPos);
                    h.SetRotation(stream, finalRot);
                }
            }
        }
        // Tracked-knee swivel cutoffs. A One-Euro rejects rest jitter at its FLOOR, so the floor stays low
        // (near the 1 Hz standing floor) to actually kill the pole-amplified tracker jitter -- a high floor
        // would pass it straight through. The difference from the standing path is a much larger BETA: a knee
        // tracker is a real user-driven signal, so the cutoff must open aggressively on deliberate shin motion
        // and not lag it. Starting points -- tune in-headset; BasisLegTwistSmoothingTests guards the balance.
        const float k_TrackedKneeSwivelMinCutoffHz = 1.5f;  // held-still smoothing floor (vs 1.0 standing)
        const float k_TrackedKneeSwivelBeta = 0.20f;        // 4x standing: opens fast so real shin motion isn't lagged
        const float k_TrackedKneeSwivelDerivCutoffHz = 1.0f;

        // OneEuro low-pass of the knee swivel (leg roll about the
        // hip->foot axis), foot kept exactly on target. Damps swivel jitter without lagging a real turn or
        // locomotion (both move the whole leg, leaving the swivel angle ~unchanged). Called on the no-foot-
        // tracker path (standing twist) and the tracked-knee path (pole-amplified tracker jitter); the
        // caller passes the appropriate One-Euro cutoffs. Per-leg slot.
        void SmoothKneeSwivel(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, int slot, float dt, float minCutoffHz, float beta, float derivCutoffHz, bool conditionOnPole)
        {
            if (!legSwivelInit.IsCreated || slot < 0 || slot >= legSwivelInit.Length || !HandleHips.IsValid(stream))
            {
                return;
            }
            BasisSwivelSmootherInput input = default;
            input.Root = root.GetPosition(stream);
            input.Mid = mid.GetPosition(stream);
            input.Tip = tip.GetPosition(stream);
            input.BodyRotation = HandleHips.GetRotation(stream);
            // A standing leg hangs along the AC axis, so Vector3.down (the arm's ref) is colinear and
            // degenerate here. Reference off body forward (the knee bulges forward); body right as the fallback.
            input.ReferenceLocal = Vector3.forward;
            input.FallbackLocal = Vector3.right;
            input.Dt = dt;
            input.MinCutoffHz = minCutoffHz;
            input.Beta = beta;
            input.DerivCutoffHz = derivCutoffHz;
            // A standing leg sits ON the pole singularity -- footHeightOffset is deliberately clamped so the legs
            // fully extend, which parks hip->foot distance at ~= thigh+shin, leaving the knee on the hip->foot axis
            // with no meaningful bend plane. There the raw swivel is noise, and a speed-adaptive filter reads that
            // noise as intent and opens right up (see BasisSwivelSmootherCore). Condition the filter on the pole's
            // lever arm so it damps hard while straight and recovers full responsiveness once the knee is bent.
            // Only the LEG opts in; the arm keeps the legacy path. The caller decides: an INVENTED pole conditions
            // (its near-straight swivel really is noise); a REAL knee tracker's pole is a measurement and does NOT
            // -- strangling it was "the knee trackers are way too slow to update".
            input.ConditionOnPole = conditionOnPole;
            input.SingularMinCutoffHz = BasisSwivelFilterCore.MinCutoffHz;
            // A knee is a hinge: it cannot bend backwards. The solve already refuses to PLACE the knee posterior
            // (BasisLegSolveCore's pole guard), but this smoother MOVES it afterwards, so without the same bound
            // here a lagging filter could still drag it through the joint. Same limits, one shared clamp.
            input.GuardAnteriorHalfSpace = true;
            input.AnteriorSoftDeg = BasisLegSolveCore.KneeAnteriorSoftDeg;
            input.AnteriorHardDeg = BasisLegSolveCore.KneeAnteriorHardDeg;
            input.State = new BasisSwivelFilterState { Raw = legSwivelRaw[slot].x, Vel = legSwivelRaw[slot].y, Smooth = legSwivelSmooth[slot].x };
            input.Seeded = legSwivelInit[slot] != 0;

            BasisSwivelSmootherCore.Solve(input, out BasisSwivelSmootherResult result);
            if (result.WriteState)
            {
                legSwivelRaw[slot] = new Vector3(result.State.Raw, result.State.Vel, 0f);
                legSwivelSmooth[slot] = new Vector3(result.State.Smooth, 0f, 0f);
                legSwivelInit[slot] = 1;
            }
            if (!result.Valid)
            {
                return;
            }

            Vector3 preFoot = input.Tip;
            Quaternion preFootRot = tip.GetRotation(stream);
            SwingElbowAroundAC(stream, root, mid, tip, result.DesiredMid);
            tip.SetPosition(stream, preFoot);
            tip.SetRotation(stream, preFootRot);
        }
        public void SolveHand(AnimationStream stream, FloatProperty enabledProp, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3Property targetPosProp, Vector4Property targetRotProp, Vector3Property hintPosProp, Vector4Property hintRotProp, BoolProperty hintWeightProp, Quaternion targetOffset, ReadWriteTransformHandle chestStart, ReadWriteTransformHandle chestEnd, FloatProperty chestRadius, FloatProperty collisionSkin, BoolProperty collisionsEnabled, FloatProperty handRadius, FloatProperty handSkin, BoolProperty protectElbow, BoolProperty collideTrackedElbow, Vector3 bodyRight, int swingSlot)
        {
            // Written `!(w > 0)` so a NaN weight takes the reject branch rather than solving on garbage.
            float weight = enabledProp.Get(stream);
            if (!(weight > 0f))
            {
                return;
            }
            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                return;
            }

            // Rotation-only fade, exactly as SolveLegs does it: the solve produces ROTATIONS, so blending
            // positions mid-fade would translate bones off the FK chain and dislocate the hand.
            Quaternion origRootRot = root.GetRotation(stream);
            Quaternion origMidRot = mid.GetRotation(stream);
            Quaternion origTipRot = tip.GetRotation(stream);

            // Read inputs
            Vector3 tgtPos = targetPosProp.Get(stream);
            Quaternion tgtRot = V4ToQuat(targetRotProp.Get(stream));
            Vector3 hintPos = hintPosProp.Get(stream);
            Quaternion hintRot = V4ToQuat(hintRotProp.Get(stream));

            var target = new AffineTransform(tgtPos, tgtRot);
            var hint = new AffineTransform(hintPos, hintRot);
            bool hasHint = hintWeightProp.Get(stream);
            bool usedModel = false;

            if (!hasHint)
            {
                // NO ELBOW TRACKER: predict the elbow's SWIVEL ANGLE about the shoulder->hand axis.
                //
                // With the shoulder and the hand both fixed the elbow is confined to a CIRCLE, so its entire
                // redundancy is ONE SCALAR. Predicting that angle lands the elbow ON the reachable circle by
                // construction -- which is exactly why the snap past ~95% extension cannot happen here. The old
                // lookup predicted a 3-VECTOR, which does not lie on the circle, so the solver needed fades and
                // pole guards to drag it back; and as the arm straightens the circle collapses, the fades
                // switched the hint off, and the pole was handed to a fallback pointing somewhere else. THAT
                // HANDOFF WAS THE SNAP. An angle stays defined and continuous at every extension, and the
                // resulting POSITION change goes to zero on its own as the circle shrinks.
                BasisSwivelFrame frame = BuildArmFrame(stream);

                Vector3 shoulderPos = root.GetPosition(stream);
                float upperLen = (mid.GetPosition(stream) - shoulderPos).magnitude;
                float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
                float armLen = upperLen + lowerLen;
                // Handedness is structural — derive it from the swing slot the binding assigned,
                // not from live chest geometry (a heavy chest roll, e.g. lying on your side, can
                // flip a geometric test and mirror the model mid-session).
                bool isLeft = swingSlot == k_SwingLeftElbow;

                // NO CONFIDENCE GATE. There used to be one -- `conf > 0.20` -- and it was a boolean cliff:
                // below it the hint was dropped ENTIRELY and the elbow was handed back to whatever the
                // animation clip was doing. Switching between two unrelated poles IS the pop, and the LEG
                // worked this out long ago and deleted its copy (see BasisSwivelHintCore.LegHint's comment,
                // which says exactly this). The arm's survived. BasisElbowFieldModel has nothing to be
                // unconfident about anyway: its only degeneracy is geometric, measure-zero, and handled
                // internally by a fallback at the exact cores (its old fade BAND is gone -- the fade's
                // antipodal lerp was the "big swings flip drastically" teleport; see the model's header).
                if (BasisSwivelHintCore.ArmHint(frame, shoulderPos, tgtPos, armLen, isLeft,
                                                out Vector3 modelHint, out _))
                {
                    hint = new AffineTransform(modelHint, hintRot);
                    hasHint = true;
                    usedModel = true;
                }
            }
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hasHint, hasHint && !usedModel, targetOffset);
            // NO OUTPUT FILTER ON THE MODEL PATH, and that is a measured choice, not an oversight.
            //
            // SmoothElbowSwivel is a One-Euro on the elbow swivel. It existed to fight the LOOKUP's jitter
            // (0.126) -- a table sampled by a moving hand is not smooth, so its output had to be filtered. The
            // model is a POLYNOMIAL: C-infinity, smooth by construction, and it measures JITTER 0.042, which is
            // lower than a real elbow TRACKER's (0.046), with zero pops. Filtering something already smoother
            // than the hardware buys nothing and costs lag on every deliberate reach.
            //
            // A real elbow tracker was never filtered either (the old code gated on `usedLookup`), for the same
            // reason it should not be: it is the user's own input, and damping it just mutes the hint they are
            // moving. So the filter now has no caller, and the arm's One-Euro state is gone with it.
            int collisionState = 0;
            bool doCollisions = collisionsEnabled.Get(stream) && chestStart.IsValid(stream) && chestEnd.IsValid(stream);
            bool elbowTrackerForced = hasHint && !usedModel;
            if (doCollisions && protectElbow.Get(stream) && (!elbowTrackerForced || collideTrackedElbow.Get(stream)))
            {
                // Geometry lives in BasisElbowProtectCore so the offline sweep harness runs the
                // exact same penetration test and elbow push. Apply the result through the stream.
                BasisElbowProtectInput epi = default;
                epi.Shoulder = root.GetPosition(stream);
                epi.Elbow = mid.GetPosition(stream);
                epi.Hand = tip.GetPosition(stream);
                epi.HasHips = HandleHips.IsValid(stream);
                epi.HasSpine = HandleSpine.IsValid(stream);
                epi.HipsPos = epi.HasHips ? HandleHips.GetPosition(stream) : Vector3.zero;
                epi.SpinePos = epi.HasSpine ? HandleSpine.GetPosition(stream) : Vector3.zero;
                epi.ChestPos = chestStart.GetPosition(stream);
                epi.NeckPos = chestEnd.GetPosition(stream);
                epi.ChestRadiusBase = chestRadius.Get(stream);
                epi.CollisionSkin = collisionSkin.Get(stream);
                epi.HandRadius = handRadius.Get(stream);
                epi.HandSkin = handSkin.Get(stream);
                epi.PlayerUp = playerUp.Get(stream);
                epi.BodyRight = bodyRight;

                BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
                if (epr.Engaged)
                {
                    Vector3 preservedHandPos = tip.GetPosition(stream);
                    Quaternion preservedHandRot = tip.GetRotation(stream);
                    SwingElbowAroundAC(stream, root, mid, tip, epr.DesiredElbow);
                    tip.SetPosition(stream, preservedHandPos);
                    tip.SetRotation(stream, preservedHandRot);
                }
                collisionState = epr.CollisionState;
            }

            if (swingCollided.IsCreated)
            {
                swingCollided[swingSlot] = collisionState;
            }

            if (weight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), weight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), weight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), weight));
            }
        }
        public float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_Epsilon || aLen2 <= k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2.0f * aLen1 * aLen2), -1.0f, 1.0f);
            return Mathf.Acos(c);
        }
    }
    public class BasisFullBodyJobBinder : AnimationJobBinder<BasisFullIKConstraintJob, BasisFullBodyData>
    {
        public override BasisFullIKConstraintJob Create(Animator animator, ref BasisFullBodyData data, Component component)
        {
            var job = new BasisFullIKConstraintJob
            {
                HandleHips = BindHandle(animator, data.hips),
                HandleChest = BindHandle(animator, data.chest),
                HandleNeck = BindHandle(animator, data.neck),
                HandleHead = BindHandle(animator, data.head),
                HandleLeftUpperLeg = BindHandle(animator, data.LeftUpperLeg),
                HandleLeftLowerLeg = BindHandle(animator, data.LeftLowerLeg),
                HandleLeftFoot = BindHandle(animator, data.leftFoot),
                HandleRightUpperLeg = BindHandle(animator, data.RightUpperLeg),
                HandleRightLowerLeg = BindHandle(animator, data.RightLowerLeg),
                HandleRightFoot = BindHandle(animator, data.RightFoot),
                HandleLeftToe = BindHandle(animator, data.LeftToe),
                HandleRightToe = BindHandle(animator, data.RightToe),
                HandleLeftUpperArm = BindHandle(animator, data.leftUpperArm),
                HandleLeftLowerArm = BindHandle(animator, data.leftLowerArm),
                HandleLeftHand = BindHandle(animator, data.LeftHand),
                HandleRightUpperArm = BindHandle(animator, data.RightUpperArm),
                HandleRightLowerArm = BindHandle(animator, data.RightLowerArm),
                HandleRightHand = BindHandle(animator, data.RightHand),
                HandleLeftUpperArmTwist = BindHandle(animator, data.LeftUpperArmTwist),
                HandleLeftLowerArmTwist = BindHandle(animator, data.LeftLowerArmTwist),
                HandleRightUpperArmTwist = BindHandle(animator, data.RightUpperArmTwist),
                HandleRightLowerArmTwist = BindHandle(animator, data.RightLowerArmTwist),
                HandleSpine = BindHandle(animator, data.spine),
                HandleUpperChest = BindHandle(animator, data.upperChest),
                HandleLeftShoulder = BindHandle(animator, data.LeftShoulder),
                HandleRightShoulder = BindHandle(animator, data.RightShoulder),
                targetPositionHips = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHips),
                targetPositionHead = Vector3Property.Bind(animator, component, data.TargetPositionPropertyHead),
                TargetChestPosition = Vector3Property.Bind(animator, component, data.PropertyChestPosition),
                TargetChestPositionRaw = Vector3Property.Bind(animator, component, data.PropertyChestPositionRaw),
                playerUp = Vector3Property.Bind(animator, component, data.PlayerUpProperty),

                KneeBendPrefLeft = Vector3Property.Bind(animator, component, data.KneeBendPrefLeftProperty),
                KneeBendPrefRight = Vector3Property.Bind(animator, component, data.KneeBendPrefRightProperty),

                targetPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyLeftLowerLeg),
                hintPositionLeftLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyLeftLowerLeg),
                targetPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.TargetPositionPropertyRightLowerLeg),
                hintPositionRightLowerLeg = Vector3Property.Bind(animator, component, data.HintPositionPropertyRightLowerLeg),
                targetPositionLeftHand = Vector3Property.Bind(animator, component, data.TargetPositionPropertyLeftHand),
                hintPositionLeftHand = Vector3Property.Bind(animator, component, data.HintPositionPropertyLeftHand),
                targetPositionRightHand = Vector3Property.Bind(animator, component, data.TargetPositionPropertyRightHand),
                hintPositionRightHand = Vector3Property.Bind(animator, component, data.HintPositionPropertyRightHand),
                targetRotationHips = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHips),
                offsetRotationHips = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyHips),
                targetRotationHead = Vector4Property.Bind(animator, component, data.TargetRotationPropertyHead),
                targetChestRotation = Vector4Property.Bind(animator, component, data.PropertyChestRotation),
                TargetRotationLeftShoulder = Vector4Property.Bind(animator, component, data.TargetRotationLeftShoulderProperty),
                TargetRotationRightShoulder = Vector4Property.Bind(animator, component, data.TargetRotationRightShoulderProperty),
                targetRotationLeftLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyLeftLowerLeg),
                targetRotationRightLowerLeg = Vector4Property.Bind(animator, component, data.TargetRotationPropertyRightLowerLeg),
                leftDrivenTargetRot = Vector4Property.Bind(animator, component, data.LeftDrivenTargetRotProperty),
                rightDrivenTargetRot = Vector4Property.Bind(animator, component, data.RightDrivenTargetRotProperty),
                targetRotationLeftHand = Vector4Property.Bind(animator, component, data.TargetRotationPropertyLeftHand),
                hintRotationLeftHand = Vector4Property.Bind(animator, component, data.HintRotationPropertyLeftHand),
                targetRotationRightHand = Vector4Property.Bind(animator, component, data.TargetRotationPropertyRightHand),
                hintRotationRightHand = Vector4Property.Bind(animator, component, data.HintRotationPropertyRightHand),
                enabledSpineIK = BoolProperty.Bind(animator, component, data.EnabledPropertySpineIK),
                HasChestTracker = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyHead),
                hasHipsTracker = BoolProperty.Bind(animator, component, data.HasHipsTrackerBoolProperty),
                enabledLeftLowerLeg = FloatProperty.Bind(animator, component, data.EnabledPropertyLeftLowerLeg),
                hintWeightLeftLowerLeg = FloatProperty.Bind(animator, component, data.HintWeightBoolPropertyLeftLowerLeg),
                enabledRightLowerLeg = FloatProperty.Bind(animator, component, data.EnabledPropertyRightLowerLeg),
                hintWeightRightLowerLeg = FloatProperty.Bind(animator, component, data.HintWeightBoolPropertyRightLowerLeg),
                leftToeEnabled = BoolProperty.Bind(animator, component, data.LeftToeEnabledProperty),
                RightToeEnabled = BoolProperty.Bind(animator, component, data.RightToeEnabledProperty),
                enabledLeftHand = FloatProperty.Bind(animator, component, data.EnabledPropertyLeftHand),
                hintWeightLeftHand = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyLeftHand),
                enabledRightHand = FloatProperty.Bind(animator, component, data.EnabledPropertyRightHand),
                hintWeightRightHand = BoolProperty.Bind(animator, component, data.HintWeightBoolPropertyRightHand),
                protectElbow = BoolProperty.Bind(animator, component, data.ProtectElbowBoolProperty),
                collideTrackedElbow = BoolProperty.Bind(animator, component, data.CollideTrackedElbowBoolProperty),
                collisionsEnabled = BoolProperty.Bind(animator, component, data.CollisionsEnabledBoolProperty),
                chestRadius = FloatProperty.Bind(animator, component, data.ChestRadiusFloatProperty),
                collisionSkin = FloatProperty.Bind(animator, component, data.CollisionSkinFloatProperty),
                handRadius = FloatProperty.Bind(animator, component, data.HandRadiusFloatProperty),
                handSkin = FloatProperty.Bind(animator, component, data.HandSkinFloatProperty),
                maxBendDeg = FloatProperty.Bind(animator, component, data.MaxBendDegFloatProperty),
                minFactor = FloatProperty.Bind(animator, component, data.MinFactorFloatProperty),
                maxFactor = FloatProperty.Bind(animator, component, data.MaxFactorFloatProperty),
                MaxChestDeltaProperty = FloatProperty.Bind(animator, component, data.MaxChestDeltaPropertyDegFloatProperty),
                enabledLeftShoulder = BoolProperty.Bind(animator, component, data.EnabledLeftShoulderProperty),
                enabledRightShoulder = BoolProperty.Bind(animator, component, data.EnabledRightShoulderProperty),
                offsetRotationLeftShoulder = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyLeftShoulder),
                offsetRotationRightShoulder = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyRightShoulder),
                offsetRotationHead = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyHead),
                offsetRotationChest = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyChest),
                offsetRotationLeftToe = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyLeftToe),
                offsetRotationRightToe = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyRightToe),
                offsetRotationLeftFoot = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyLeftFoot),
                offsetRotationRightFoot = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyRightFoot),
                offsetRotationLeftHand = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyLeftHand),
                offsetRotationRightHand = Vector4Property.Bind(animator, component, data.OffsetRotationPropertyRightHand),
                MinHeadSpineHeight = FloatProperty.Bind(animator, component, data.MinHeadSpineHeightFloatProperty),

                // Shoulder solve bindings
                shoulderSolveEnabled = BoolProperty.Bind(animator, component, data.ShoulderSolveEnabledProperty),
                shoulderShrugEnabled = BoolProperty.Bind(animator, component, data.ShoulderShrugEnabledProperty),
                shoulderElevationFactor = FloatProperty.Bind(animator, component, data.ShoulderElevationFactorProperty),
                shoulderProtractionFactor = FloatProperty.Bind(animator, component, data.ShoulderProtractionFactorProperty),

                // Spine bend distribution bindings (per-axis pitch/yaw/roll)
                spineBendPitch = FloatProperty.Bind(animator, component, data.SpineBendPitchFloatProperty),
                spineBendYaw = FloatProperty.Bind(animator, component, data.SpineBendYawFloatProperty),
                spineBendRoll = FloatProperty.Bind(animator, component, data.SpineBendRollFloatProperty),
                upperChestBendPitch = FloatProperty.Bind(animator, component, data.UpperChestBendPitchFloatProperty),
                upperChestBendYaw = FloatProperty.Bind(animator, component, data.UpperChestBendYawFloatProperty),
                upperChestBendRoll = FloatProperty.Bind(animator, component, data.UpperChestBendRollFloatProperty),
                hipHingeStartDeg = FloatProperty.Bind(animator, component, data.HipHingeStartDegFloatProperty),
                hipHingeMaxAddDeg = FloatProperty.Bind(animator, component, data.HipHingeMaxAddDegFloatProperty),
                chestSpringHz = FloatProperty.Bind(animator, component, data.ChestSpringHzFloatProperty),
                chestSpringDamping = FloatProperty.Bind(animator, component, data.ChestSpringDampingFloatProperty),
                spineMaxForwardDeg = FloatProperty.Bind(animator, component, data.SpineMaxForwardDegFloatProperty),
                spineMaxBackwardDeg = FloatProperty.Bind(animator, component, data.SpineMaxBackwardDegFloatProperty),
                spineMaxLateralDeg = FloatProperty.Bind(animator, component, data.SpineMaxLateralDegFloatProperty),
                spineSquishBoost = FloatProperty.Bind(animator, component, data.SpineSquishBoostFloatProperty),
                spineGazeFollow = FloatProperty.Bind(animator, component, data.SpineGazeFollowFloatProperty),
                neckGazeFollow = FloatProperty.Bind(animator, component, data.NeckGazeFollowFloatProperty),
                moveBodyBackWhenCrouching = FloatProperty.Bind(animator, component, data.MoveBodyBackWhenCrouchingFloatProperty),
                swingSmoothRateDeg = FloatProperty.Bind(animator, component, data.SwingSmoothRateDegFloatProperty),
                chestArmSwingFactor = FloatProperty.Bind(animator, component, data.ChestArmSwingFactorFloatProperty),
                chestArmSwingMaxDeg = FloatProperty.Bind(animator, component, data.ChestArmSwingMaxDegFloatProperty),
                lowerArmTwistFraction = FloatProperty.Bind(animator, component, data.LowerArmTwistFractionFloatProperty),
                upperArmTwistFraction = FloatProperty.Bind(animator, component, data.UpperArmTwistFractionFloatProperty),

                anatDifferentialStiffness = BoolProperty.Bind(animator, component, data.AnatDifferentialStiffnessProperty),
                anatShoulderSlide = BoolProperty.Bind(animator, component, data.AnatShoulderSlideProperty),
                anatCervicalLordosis = BoolProperty.Bind(animator, component, data.AnatCervicalLordosisProperty),
                anatPelvicTwistRouting = BoolProperty.Bind(animator, component, data.AnatPelvicTwistRoutingProperty),
                spineAnatomicalRom = BoolProperty.Bind(animator, component, data.SpineAnatomicalRomProperty),
                chestIkTarget = BoolProperty.Bind(animator, component, data.ChestIKTargetProperty),
                legSwivelSmoothing = BoolProperty.Bind(animator, component, data.LegSwivelSmoothingProperty),
                hintIsTrackerLeftLowerLeg = BoolProperty.Bind(animator, component, data.HintIsTrackerBoolPropertyLeftLowerLeg),
                hintIsTrackerRightLowerLeg = BoolProperty.Bind(animator, component, data.HintIsTrackerBoolPropertyRightLowerLeg),
                lordosisPitchGainDeg = FloatProperty.Bind(animator, component, data.LordosisPitchGainDegFloatProperty),
                lordosisBaseDeg = FloatProperty.Bind(animator, component, data.LordosisBaseDegFloatProperty),
                lordosisNeckShare = FloatProperty.Bind(animator, component, data.LordosisNeckShareFloatProperty),
                lordosisMaxHeadPitchDeg = FloatProperty.Bind(animator, component, data.LordosisMaxHeadPitchDegFloatProperty),
                lordosisExtremeStartDeg = FloatProperty.Bind(animator, component, data.LordosisExtremeStartDegFloatProperty),
                lordosisExtremeFullDeg = FloatProperty.Bind(animator, component, data.LordosisExtremeFullDegFloatProperty),
                lordosisExtremeRollForwardMaxDeg = FloatProperty.Bind(animator, component, data.LordosisExtremeRollForwardMaxDegFloatProperty),
                lordosisExtremeRollBackwardMaxDeg = FloatProperty.Bind(animator, component, data.LordosisExtremeRollBackwardMaxDegFloatProperty),
                lordosisExtremeHipsHorizontalMax = FloatProperty.Bind(animator, component, data.LordosisExtremeHipsHorizontalMaxFloatProperty),
                lordosisExtremeChestHorizontalMax = FloatProperty.Bind(animator, component, data.LordosisExtremeChestHorizontalMaxFloatProperty),
                lordosisExtremeHipsDownMax = FloatProperty.Bind(animator, component, data.LordosisExtremeHipsDownMaxFloatProperty),
                lordosisExtremeChestDownMax = FloatProperty.Bind(animator, component, data.LordosisExtremeChestDownMaxFloatProperty),
                lordosisExtremeHipsDownLookUp = FloatProperty.Bind(animator, component, data.LordosisExtremeHipsDownLookUpFloatProperty),
                lordosisExtremeChestDownLookUp = FloatProperty.Bind(animator, component, data.LordosisExtremeChestDownLookUpFloatProperty),
                spineCCDRelax = FloatProperty.Bind(animator, component, data.SpineCCDRelaxFloatProperty),
                neckMaxConeDeg = FloatProperty.Bind(animator, component, data.NeckMaxConeDegFloatProperty),
                spineTwistKeep = FloatProperty.Bind(animator, component, data.SpineTwistKeepFloatProperty),
                spineNeckTwistKeep = FloatProperty.Bind(animator, component, data.SpineNeckTwistKeepFloatProperty),

                // IK Lock Mode binding
                ikLockMode = FloatProperty.Bind(animator, component, data.IKLockModeFloatProperty),

                // Baked T-pose data for shoulder solve
                TposeLeftShoulderRot = data.LeftShoulder != null ? data.LeftShoulder.rotation : Quaternion.identity,
                TposeRightShoulderRot = data.RightShoulder != null ? data.RightShoulder.rotation : Quaternion.identity,
                TposeChestRot = data.chest != null ? data.chest.rotation : Quaternion.identity,
                TposeLeftShoulderLocalDir = (data.LeftShoulder != null && data.leftUpperArm != null)
                    ? (data.leftUpperArm.position - data.LeftShoulder.position).normalized : Vector3.left,
                TposeRightShoulderLocalDir = (data.RightShoulder != null && data.RightUpperArm != null)
                    ? (data.RightUpperArm.position - data.RightShoulder.position).normalized : Vector3.right,
                TposeShoulderToHandLeft = (data.LeftShoulder != null && data.LeftHand != null)
                    ? Vector3.Distance(data.LeftShoulder.position, data.LeftHand.position) : 0.6f,
                TposeShoulderToHandRight = (data.RightShoulder != null && data.RightHand != null)
                    ? Vector3.Distance(data.RightShoulder.position, data.RightHand.position) : 0.6f,
                TposeClavicleLenLeft = (data.LeftShoulder != null && data.leftUpperArm != null)
                    ? Vector3.Distance(data.LeftShoulder.position, data.leftUpperArm.position) : 0f,
                TposeClavicleLenRight = (data.RightShoulder != null && data.RightUpperArm != null)
                    ? Vector3.Distance(data.RightShoulder.position, data.RightUpperArm.position) : 0f,
                TposeShoulderToElbowLeft = (data.LeftShoulder != null && data.leftLowerArm != null)
                    ? Vector3.Distance(data.LeftShoulder.position, data.leftLowerArm.position) : 0f,
                TposeShoulderToElbowRight = (data.RightShoulder != null && data.RightLowerArm != null)
                    ? Vector3.Distance(data.RightShoulder.position, data.RightLowerArm.position) : 0f,

            };

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
            job.p54 = Vector3Property.Bind(animator, component, data.GetTargetPositionVector3Property(54));
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
            job.r54 = Vector4Property.Bind(animator, component, data.GetTargetRotationVector4Property(54));
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
            job.o54 = Vector4Property.Bind(animator, component, data.GetOffsetRotationVector4Property(54));
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
            job.w54 = BoolProperty.Bind(animator, component, data.GetWeightFloatProperty(54));


            GenerateHeadToSpine(animator, ref job, ref data);

            var cacheBuilder = new AnimationJobCacheBuilder();

            job.spineMaxIterationsIdx = cacheBuilder.Add(20);
            job.spineToleranceIdx = cacheBuilder.Add(0.001f);
            job.spineCache = cacheBuilder.Build();

            job.chestSpringState = new NativeArray<Vector3>(2, Allocator.Persistent);
            job.chestSpringInit = new NativeArray<int>(1, Allocator.Persistent);

            job.swingLastDir = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingLastAxis = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingLastTarget = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingContinuityInit = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingCollided = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingSmoothState = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.legSwivelRaw = new NativeArray<Vector3>(2, Allocator.Persistent);
            job.legSwivelSmooth = new NativeArray<Vector3>(2, Allocator.Persistent);
            job.legSwivelInit = new NativeArray<int>(2, Allocator.Persistent);



            return job;
        }
        // Bakes each vertebra's anatomical rest frame + ROM, PARALLEL TO THE CHAIN, so the guard can be
        // applied by chain index alone. Runs in the same T-pose window as TposeHeadToNeckLocal below.
        //
        // The chain is [head, neck, (upperChest,) chest, spine, hips]. The head and the hips get an INVALID
        // frame on purpose -- the head is welded to the HMD and the hips are the anchor, so neither is a DOF
        // the solver invents. Guarding a commanded bone would fight the tracker. Same doctrine as the arm:
        // guard the elbow, never the hand.
        //
        // The segment a bone stands for depends on whether the avatar HAS an upperChest. With one, chest is
        // the lower thorax and upperChest the upper. Without one, the single `chest` bone spans the whole
        // thorax, so it inherits the LOWER thoracic ROM -- the more permissive of the two, because it is now
        // doing both jobs and clamping it to the stiffer upper-thoracic envelope would rob the avatar of
        // bend it genuinely has.
        static void BuildSpineAnatomy(Transform[] chain, ref BasisFullIKConstraintJob job, ref BasisFullBodyData data)
        {
            int n = chain.Length;
            job.ChainSpineRestFrames = new NativeArray<BasisSpineRestFrame>(n, Allocator.Persistent);
            job.ChainSpineRoms = new NativeArray<BasisSpineRom>(n, Allocator.Persistent);

            // The subject's RIGHT, from the shoulders. A body-wide fact -- NOT a bone's local axis, which is
            // a rig convention and does not transfer between avatars. This project has been bitten by that
            // repeatedly; it is why the arm swivel model is position-only.
            if (data.leftUpperArm == null || data.RightUpperArm == null)
            {
                return;   // every frame stays Valid=false, so the guard is a no-op. Decline, never guess.
            }
            Vector3 hipsRight = data.RightUpperArm.position - data.leftUpperArm.position;

            for (int i = 1; i <= n - 2; i++)   // skip the head (0) and the hips (n-1)
            {
                Transform bone = chain[i];
                Transform child = chain[i - 1];    // the chain runs tip -> root, so the CHILD is i-1
                Transform parent = chain[i + 1];
                if (bone == null || child == null || parent == null)
                {
                    continue;
                }

                BasisSpineSegment segment;
                if (bone == data.spine)
                {
                    segment = BasisSpineSegment.Lumbar;
                }
                else if (bone == data.chest)
                {
                    segment = BasisSpineSegment.LowerThoracic;
                }
                else if (bone == data.upperChest)
                {
                    segment = BasisSpineSegment.UpperThoracic;
                }
                else if (bone == data.neck)
                {
                    segment = BasisSpineSegment.Cervical;
                }
                else
                {
                    continue;
                }

                job.ChainSpineRestFrames[i] = BasisSpineAnatomy.BuildRestFrame(
                    bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                job.ChainSpineRoms[i] = BasisSpineAnatomy.Rom(segment);
            }
        }
        public void GenerateHeadToSpine(Animator animator, ref BasisFullIKConstraintJob job, ref BasisFullBodyData data)
        {
            var HeadToSpine = data.upperChest != null
                ? new Transform[] { data.head, data.neck, data.upperChest, data.chest, data.spine, data.hips }
                : new Transform[] { data.head, data.neck, data.chest, data.spine, data.hips };
            int SpineToHeadLength = HeadToSpine.Length;
            job.ChainHeadToSpine = new NativeArray<ReadWriteTransformHandle>(SpineToHeadLength, Allocator.Persistent);
            BuildSpineAnatomy(HeadToSpine, ref job, ref data);

            for (int i = 0; i < SpineToHeadLength; i++)
            {
                job.ChainHeadToSpine[i] = ReadWriteTransformHandle.Bind(animator, HeadToSpine[i]);
            }
            if (data.hips != null && data.head != null)
            {
                job.TposeLengthHeadToHips = (data.head.position - data.hips.position);
            }
            else
            {
                job.TposeLengthHeadToHips = Vector3.zero;
            }

            // The spine's bend cue, baked while the avatar is still physically T-posed (the same window
            // TposeChestRot and the swivel models' T-poses are captured in).
            //
            // TposeHeadToNeckLocal is the neck's position RELATIVE TO THE HEAD, expressed in the HEAD'S OWN
            // rest frame. That is what makes it a rigid re-attachment rather than a fudge: rotate the head by
            // anything at all, carry this offset along with it, and you land back on the neck. Dividing out the
            // head's rest rotation is what makes it rig-independent -- a bone's local axes are a convention.
            //
            // No head or no neck => zero, and the cue degrades exactly to the old hips->head behaviour rather
            // than to something novel and untested.
            if (data.head != null && data.neck != null)
            {
                job.TposeHeadToNeckLocal = Quaternion.Inverse(data.head.rotation) * (data.neck.position - data.head.position);
            }
            else
            {
                job.TposeHeadToNeckLocal = Vector3.zero;
            }

            if (data.hips != null && data.neck != null)
            {
                job.TposeLengthNeckToHips = (data.neck.position - data.hips.position);
            }
            else
            {
                job.TposeLengthNeckToHips = job.TposeLengthHeadToHips;
            }
        }
        static ReadWriteTransformHandle BindHandle(Animator animator, Transform t) => (t != null) ? ReadWriteTransformHandle.Bind(animator, t) : default;
        public override void Destroy(BasisFullIKConstraintJob job)
        {
            if (job.ChainHeadToSpine.IsCreated) job.ChainHeadToSpine.Dispose();
            if (job.ChainSpineRestFrames.IsCreated) job.ChainSpineRestFrames.Dispose();
            if (job.ChainSpineRoms.IsCreated) job.ChainSpineRoms.Dispose();

            if (job.chestSpringState.IsCreated) job.chestSpringState.Dispose();
            if (job.chestSpringInit.IsCreated) job.chestSpringInit.Dispose();

            if (job.swingLastDir.IsCreated) job.swingLastDir.Dispose();
            if (job.swingLastAxis.IsCreated) job.swingLastAxis.Dispose();
            if (job.swingLastTarget.IsCreated) job.swingLastTarget.Dispose();
            if (job.swingContinuityInit.IsCreated) job.swingContinuityInit.Dispose();
            if (job.swingCollided.IsCreated) job.swingCollided.Dispose();
            if (job.swingSmoothState.IsCreated) job.swingSmoothState.Dispose();
            if (job.legSwivelRaw.IsCreated) job.legSwivelRaw.Dispose();
            if (job.legSwivelSmooth.IsCreated) job.legSwivelSmooth.Dispose();
            if (job.legSwivelInit.IsCreated) job.legSwivelInit.Dispose();

            job.spineCache.Dispose();
        }
    }
}
