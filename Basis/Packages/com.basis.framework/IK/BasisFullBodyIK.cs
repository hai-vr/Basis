using System.Runtime.CompilerServices;
using Unity.Collections;
namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
    [System.Serializable]
    public struct BasisFullBodyData
    {
        public const int Count = 22;


        // Slots are HumanBodyBones values: 0..RightToes map directly, UpperChest (54) maps to the last slot.
        public const int UpperChestSlot = Count - 1;

        // Live target positions pushed every frame from the manager.
        public FixedList512Bytes<Vector3> TargetPositions;

        // Live target rotations.
        public FixedList512Bytes<Quaternion> TargetRotations;

        // Calibration offsets (applied on top of target each frame) — final = target * offset
        public FixedList512Bytes<Quaternion> OffsetRotations;

        // Per-slot enables. Allows toggling bones independently within a single job.
        public FixedList64Bytes<bool> Weights;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Slot(int humanBodyBone)
        {
            if (humanBodyBone >= 0 && humanBodyBone <= (int)HumanBodyBones.RightToes)
            {
                return humanBodyBone;
            }
            return humanBodyBone == (int)HumanBodyBones.UpperChest ? UpperChestSlot : -1;
        }

        // Property name helpers for binding



        Transform m_Hips;
        Transform m_chest;
        Transform m_neck;
        Transform m_head;

        Transform m_LeftUpperLeg;
        Transform m_LeftLowerLeg;
        Transform m_leftFoot;
        Transform m_RightUpperLeg;
        Transform m_RightLowerLeg;
        Transform m_RightFoot;

        Transform m_LeftToe;
        Transform m_RightToe;

        Transform m_leftUpperArm;
        Transform m_leftLowerArm;
        Transform m_leftHand;

        Transform m_RightUpperArm;
        Transform m_RightLowerArm;
        Transform m_rightHand;

        Transform m_Spine;
        Transform m_UpperChest;
        Transform m_LeftShoulder;
        Transform m_RightShoulder;

        // Twist bones — derived bones that absorb a fraction of wrist/elbow roll for natural
        // forearm/upper-arm deformation. Optional per rig; when null, the side is skipped.
        Transform m_LeftUpperArmTwist;
        Transform m_LeftLowerArmTwist;
        Transform m_RightUpperArmTwist;
        Transform m_RightLowerArmTwist;

        // Head
        public Vector3 PositionHead;
        public Quaternion RotationHead;
        public Vector3 ChestPosition;
        // The chest bone's ACTUAL position, WITHOUT the chest-as-head-hint bias that ChestPosition carries
        // (that bias pushes ~8cm 'up in chest frame' to steer the head solve -- see BasisAvatarIKStageCalibration).
        // The chest IK target must pin to the real chest, not the hinted one, or it hauls the torso up = a lean.
        public Vector3 ChestPositionRaw;
        public Quaternion ChestRotation;
        public Quaternion m_CalibratedRotationHead;

        public Quaternion m_CalibratedRotationRightToe;
        public Quaternion m_CalibratedRotationLeftToe;
        public Quaternion m_CalibratedRotationChest;

        public Quaternion LeftShoulderRotation;
        public Quaternion RightShoulderRotation;

        // Hips
        public Vector3 PositionHips;
        public Quaternion RotationHips;
        public Quaternion OffsetRotationHips;

        // Left Leg
        public Vector3 LeftFootPosition;
        public Quaternion LeftFootRotation;
        public Vector3 PositionLeftLowerLeg;
        public Quaternion M_CalibrationLeftFootRotation;

        // Right Leg
        public Vector3 RightFootPosition;
        public Quaternion RightFootRotation;
        public Vector3 PositionRightLowerLeg;
        public Quaternion M_CalibrationRightFootRotation;

        // Toes
        public Quaternion OutGoingLeftToeRotation;
        public Quaternion OutGoingRightToeRotation;

        // Left Hand
        public Vector3 PositionLeftHand;
        public Quaternion RotationLeftHand;
        public Vector3 LeftLowerArmPosition;
        public Quaternion LeftLowerArmRotation;
        public Quaternion m_CalibratedRotationLeftHand;

        // Right Hand
        public Vector3 PositionRightHand;
        public Quaternion RotationRightHand;
        public Vector3 RightLowerArmPosition;
        public Quaternion RightLowerArmRotation;
        public Quaternion m_CalibratedRotationRightHand;

        // Misc
        public Vector3 PlayerUp;

        public Vector3 KneeBendPrefLeft;
        public Vector3 KneeBendPrefRight;

        public float m_HandSkin;
        [Min(0f)] public float m_HandRadius;
        [Min(0f)] public float m_ChestRadius;
        [Min(0f)] public float m_CollisionSkin;
        bool m_CollisionsEnabled;
        bool m_ProtectElbow;
        bool m_UseNeuralPole;
        bool m_CollideTrackedElbow;

        bool m_HintHeadEnabled;
        bool m_SpineIKEnabled;
        bool m_HasHipsTracker;

        // IK Lock Mode: 0 = LockHips, 1 = LockHead, 2 = LockBoth (see BasisIKLockMode enum)
        float m_IKLockMode;

        public bool m_LeftToeEnabled;
        public bool m_RightToeEnabled;

        bool m_LeftFootIsTracker;
        bool m_RightFootIsTracker;
        float m_LeftLowerLegEnabled;
        float m_RightLowerLegEnabled;

        float m_HintLeftLowerLegEnabled;
        float m_HintRightLowerLegEnabled;

        // True when the knee/lower-leg hint is a physical tracker (jittery, and pole-amplified by the leg
        // solve) rather than a computed hint (foot driver / butterfly). Gates the tracked-knee output-swivel
        // smoothing in SolveLegs -- see SmoothKneeSwivel.
        bool m_LeftLowerLegHintIsTracker;
        bool m_RightLowerLegHintIsTracker;

        // Hand IK weight (0..1), not a toggle: the webcam fades the hands in and out as tracking comes and
        // goes, and a hard on/off pops the arm. Mirrors the legs, which have been fractional all along.
        float m_EnabledLeftHand;
        float m_EnabledRightHand;

        bool m_HintRightHandEnabled;
        bool m_HintLeftHandEnabled;

        float m_MinHeadSpineHeight;
        public bool m_enabledLeftShoulder;
        public bool m_enabledRightShoulder;
        public Quaternion m_CalibratedRotationRightShoulder;
        public Quaternion m_CalibratedRotationLeftShoulder;

        public float m_MaxBendDeg;
        public float m_MinFactor;
        public float m_MaxFactor;
        public float m_MaxChestDeltaDeg;

        // Shoulder pre-solve: raises/protracts shoulders based on hand target
        bool m_ShoulderSolveEnabled;
        bool m_ShoulderShrugEnabled;
        [Range(0f, 1f)] float m_ShoulderElevationFactor;
        [Range(0f, 1f)] float m_ShoulderProtractionFactor;

        // Spine bend distribution: per-axis fractions of the hips→head bend pre-applied to lumbar
        // and thoracic joints before the chest→neck→head two-bone solve. Splitting by axis lets
        // forward bend, side bend, and twist be tuned independently — humans are very anisotropic.
        [Range(0f, 1f)] float m_SpineBendPitch;
        [Range(0f, 1f)] float m_SpineBendYaw;
        [Range(0f, 1f)] float m_SpineBendRoll;
        [Range(0f, 1f)] float m_UpperChestBendPitch;
        [Range(0f, 1f)] float m_UpperChestBendYaw;
        [Range(0f, 1f)] float m_UpperChestBendRoll;
        // Hip hinge: when forward lean exceeds the start angle, the pelvis pitches forward by a
        // capped fraction of the excess so the spine doesn't have to swallow the whole reach.
        [Min(0f)] float m_HipHingeStartDeg;
        [Min(0f)] float m_HipHingeMaxAddDeg;
        // Chest follow spring: critically-damped second-order spring on the head target before it
        // is consumed by DistributeSpineBend, so quick head turns leave the body momentarily behind.
        [Min(0f)] float m_ChestSpringHz;
        [Min(0f)] float m_ChestSpringDamping;
        // Asymmetric flexion clamps: humans flex forward much further than they extend backward.
        // Applied to the per-axis spine + upperChest contributions after distribution.
        [Min(0f)] float m_SpineMaxForwardDeg;
        [Min(0f)] float m_SpineMaxBackwardDeg;
        [Min(0f)] float m_SpineMaxLateralDeg;
        // Squish coupling: scales per-axis bend weights by the head-to-hips compression ratio so
        // the spine folds more when crouched and straightens when reaching up. 0 disables.
        [Range(0f, 2f)] float m_SpineSquishBoost;
        // How much the chest FOLLOWS the gaze (no chest tracker). 0 = rigid (the look-down-stability fix,
        // chest never folds on a pure look-down); 1 = full follow (the old phantom-lean). A small value is
        // 'a little real spine': the chest folds a touch when you look down, which reads better on desktop.
        [Range(0f, 1f)] float m_SpineGazeFollow;
        // How much EXTRA forward neck curve to add on a look-down (no chest tracker). Same idea as the
        // chest gaze-follow, but the neck's lordosis runs AFTER the head-placing CCD, so this is a
        // cosmetic post-solve curve -- it nudges the head BONE a touch (the camera rides the HMD target).
        [Range(0f, 1f)] float m_NeckGazeFollow;
        [Range(0f, 2f)] float m_MoveBodyBackWhenCrouching;
        // Elbow/knee swing smoothing: max swing speed (deg/s) around the root→tip axis. Lower =
        // smoother (more lag) so a torso-collision change eases in; 0 disables. See ApplySwingContinuity.
        [Min(0f)] float m_SwingSmoothRateDeg;
        // Arm-swing chest follow: when hand targets shift laterally, the chest yaws to follow so
        // gestures and walking arm-swing don't read as a stiff torso. Only used without a chest
        // tracker — when one is present, it owns chest rotation directly.
        [Range(0f, 1f)] float m_ChestArmSwingFactor;
        [Min(0f)] float m_ChestArmSwingMaxDeg;
        // Arm twist distribution: fractions of the wrist/elbow roll absorbed by the optional
        // forearm/upper-arm twist bones. Without these, the wrist eats 100% of the roll and the
        // mesh pinches around the elbow ("candy-wrap" deformation).
        [Range(0f, 1f)] float m_LowerArmTwistFraction;
        [Range(0f, 1f)] float m_UpperArmTwistFraction;

        // Anatomy: IK refinements modeled on real biomechanics. Each toggle gates its own
        // solver pass; all on by default.
        bool m_AnatDifferentialStiffness;
        bool m_AnatShoulderSlide;
        bool m_AnatCervicalLordosis;
        bool m_AnatPelvicTwistRouting;
        // The anatomical range-of-motion envelope on every solved vertebra. Default ON: what it replaces
        // is not a safe fallback, it is a measured error (BasisSpineAnatomy).
        bool m_SpineAnatomicalRom;
        // The chest as a secondary IK target (SolveChestTarget). Default ON.
        bool m_ChestIKTarget;
        // Low-pass the knee swivel (leg roll about the hip->foot axis) on the no-foot-tracker path so a
        // near-straight standing leg doesn't twist with hips-yaw jitter. Off => identical to before.
        bool m_LegSwivelSmoothing;
        // Cervical lordosis pitch coupling: extra forward bend per unit of head pitch-down (0..1
        // where 1 = looking straight down). Multiplied by the gain in degrees. Only used when
        // AnatCervicalLordosis is on.
        [Min(0f)] float m_LordosisPitchGainDeg;
        // Cervical lordosis shaping (previously hardcoded consts in ApplyCervicalLordosis). Base
        // bend held in a neutral pose and how it splits between neck and upperChest; the head pitch
        // clamp; and the "extreme look" onset/full window that drives extra spine roll plus
        // hips/chest counter-translation when looking far up or down. Down* are meters of vertical
        // shift at full look-down; *LookUp are the much smaller shift when looking up. Only used
        // when AnatCervicalLordosis is on.
        [Min(0f)] float m_LordosisBaseDeg;
        [Range(0f, 1f)] float m_LordosisNeckShare;
        [Range(0f, 90f)] float m_LordosisMaxHeadPitchDeg;
        [Range(0f, 90f)] float m_LordosisExtremeStartDeg;
        [Range(0f, 90f)] float m_LordosisExtremeFullDeg;
        [Min(0f)] float m_LordosisExtremeRollForwardMaxDeg;
        [Min(0f)] float m_LordosisExtremeRollBackwardMaxDeg;
        [Min(0f)] float m_LordosisExtremeHipsHorizontalMax;
        [Min(0f)] float m_LordosisExtremeChestHorizontalMax;
        [Min(0f)] float m_LordosisExtremeHipsDownMax;
        [Min(0f)] float m_LordosisExtremeChestDownMax;
        [Min(0f)] float m_LordosisExtremeHipsDownLookUp;
        [Min(0f)] float m_LordosisExtremeChestDownLookUp;

        // Spine CCD solve: per-iteration under-relaxation (1 = full step) and the neck's max bend
        // cone vs the chest→neck direction, which stops the short neck bone overbending.
        [Range(0.1f, 1f)] float m_SpineCCDRelax;
        [Min(0f)] float m_NeckMaxConeDeg;
        // Axial twist the spine CCD reach may use, about the body's hips-up axis, graded down the chain:
        // m_SpineTwistKeep is the lumbar (lower-back) end -- near-rigid in reality -- and m_SpineNeckTwistKeep
        // the cervical (neck) end, which rotates freely; the joints between interpolate. Lower = a sideways
        // head reach bends instead of corkscrewing (the corkscrew flips sign across center). Hips-up, not
        // world-up, so it stays correct lying down.
        [Range(0f, 1f)] float m_SpineTwistKeep;
        [Range(0f, 1f)] float m_SpineNeckTwistKeep;
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
        public bool WeightChest { get => m_HintHeadEnabled; set => m_HintHeadEnabled = value; }
        public bool EnabledSpineIK { get => m_SpineIKEnabled; set => m_SpineIKEnabled = value; }
        public bool HasHipsTracker { get => m_HasHipsTracker; set => m_HasHipsTracker = value; }
        public float IKLockMode { get => m_IKLockMode; set => m_IKLockMode = value; }
        public float EnableLeftLowerLeg { get => m_HintLeftLowerLegEnabled; set => m_HintLeftLowerLegEnabled = value; }
        public bool LeftFootIsTracker { get => m_LeftFootIsTracker; set => m_LeftFootIsTracker = value; }
        public bool RightFootIsTracker { get => m_RightFootIsTracker; set => m_RightFootIsTracker = value; }
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
        public bool UseNeuralPole { get => m_UseNeuralPole; set => m_UseNeuralPole = value; }
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
        public float LordosisPitchGainDeg { get => m_LordosisPitchGainDeg; set => m_LordosisPitchGainDeg = value; }
        public float LordosisBaseDeg { get => m_LordosisBaseDeg; set => m_LordosisBaseDeg = value; }
        public float LordosisNeckShare { get => m_LordosisNeckShare; set => m_LordosisNeckShare = value; }
        public float LordosisMaxHeadPitchDeg { get => m_LordosisMaxHeadPitchDeg; set => m_LordosisMaxHeadPitchDeg = value; }
        public float LordosisExtremeStartDeg { get => m_LordosisExtremeStartDeg; set => m_LordosisExtremeStartDeg = value; }
        public float LordosisExtremeFullDeg { get => m_LordosisExtremeFullDeg; set => m_LordosisExtremeFullDeg = value; }
        public float LordosisExtremeRollForwardMaxDeg { get => m_LordosisExtremeRollForwardMaxDeg; set => m_LordosisExtremeRollForwardMaxDeg = value; }
        public float LordosisExtremeRollBackwardMaxDeg { get => m_LordosisExtremeRollBackwardMaxDeg; set => m_LordosisExtremeRollBackwardMaxDeg = value; }
        public float LordosisExtremeHipsHorizontalMax { get => m_LordosisExtremeHipsHorizontalMax; set => m_LordosisExtremeHipsHorizontalMax = value; }
        public float LordosisExtremeChestHorizontalMax { get => m_LordosisExtremeChestHorizontalMax; set => m_LordosisExtremeChestHorizontalMax = value; }
        public float LordosisExtremeHipsDownMax { get => m_LordosisExtremeHipsDownMax; set => m_LordosisExtremeHipsDownMax = value; }
        public float LordosisExtremeChestDownMax { get => m_LordosisExtremeChestDownMax; set => m_LordosisExtremeChestDownMax = value; }
        public float LordosisExtremeHipsDownLookUp { get => m_LordosisExtremeHipsDownLookUp; set => m_LordosisExtremeHipsDownLookUp = value; }
        public float LordosisExtremeChestDownLookUp { get => m_LordosisExtremeChestDownLookUp; set => m_LordosisExtremeChestDownLookUp = value; }
        public float SpineCCDRelax { get => m_SpineCCDRelax; set => m_SpineCCDRelax = value; }
        public float SpineTwistKeep { get => m_SpineTwistKeep; set => m_SpineTwistKeep = value; }
        public float SpineNeckTwistKeep { get => m_SpineNeckTwistKeep; set => m_SpineNeckTwistKeep = value; }
        public float NeckMaxConeDeg { get => m_NeckMaxConeDeg; set => m_NeckMaxConeDeg = value; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetPosition(int idx, in Vector3 v)
        {
            int s = Slot(idx);
            if (s >= 0 && s < TargetPositions.Length)
            {
                TargetPositions[s] = v;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < TargetRotations.Length)
            {
                TargetRotations[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOffsetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < OffsetRotations.Length)
            {
                OffsetRotations[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWeight(int idx, bool State)
        {
            int s = Slot(idx);
            if (s >= 0 && s < Weights.Length)
            {
                Weights[s] = State;
            }
        }

        public void SetDefaultValues()
        {
            TargetPositions.Length = Count;
            TargetRotations.Length = Count;
            OffsetRotations.Length = Count;
            Weights.Length = Count;
            for (int i = 0; i < Count; i++)
            {
                TargetPositions[i] = Vector3.zero;
                TargetRotations[i] = Quaternion.identity;
                OffsetRotations[i] = Quaternion.identity;
                Weights[i] = false;
            }
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
            m_LeftFootIsTracker = m_RightFootIsTracker = false;
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

            // Slots: identity rotations, zero positions, weights disabled.
            TargetPositions.Length = Count;
            TargetRotations.Length = Count;
            OffsetRotations.Length = Count;
            Weights.Length = Count;
            for (int i = 0; i < Count; i++)
            {
                TargetPositions[i] = Vector3.zero;
                TargetRotations[i] = Quaternion.identity;
                OffsetRotations[i] = Quaternion.identity;
                Weights[i] = false;
            }
        }
    }
    [Unity.Burst.BurstCompile]
    public struct BasisFullIKConstraintJob : Unity.Jobs.IJob
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

        public BasisBoneHandle HandleChest, HandleNeck, HandleHead,
  HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot,
  HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot,
  HandleHips, HandleSpine, HandleUpperChest,
            HandleLeftShoulder, HandleRightShoulder,

  HandleLeftToe, HandleRightToe,
  HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand,
  HandleRightUpperArm, HandleRightLowerArm, HandleRightHand,
  HandleLeftUpperArmTwist, HandleLeftLowerArmTwist,
  HandleRightUpperArmTwist, HandleRightLowerArmTwist;

        public Vector3 targetPositionHead, TargetChestPosition, TargetChestPositionRaw, playerUp, KneeBendPrefLeft, KneeBendPrefRight,
targetPositionLeftLowerLeg, hintPositionLeftLowerLeg,
targetPositionRightLowerLeg, hintPositionRightLowerLeg,
targetPositionHips,
targetPositionLeftHand, hintPositionLeftHand,
targetPositionRightHand, hintPositionRightHand;

        public Quaternion targetRotationHead, targetChestRotation,
targetRotationLeftLowerLeg,
targetRotationRightLowerLeg,
targetRotationHips, offsetRotationHips,
offsetRotationHead, offsetRotationChest, offsetRotationLeftFoot, offsetRotationRightFoot,
offsetRotationLeftToe, offsetRotationRightToe, offsetRotationLeftShoulder, offsetRotationRightShoulder,
offsetRotationLeftHand, offsetRotationRightHand,
leftDrivenTargetRot, rightDrivenTargetRot,
targetRotationLeftHand, hintRotationLeftHand,
targetRotationRightHand, hintRotationRightHand,
TargetRotationLeftShoulder, TargetRotationRightShoulder;

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

        public float
enabledLeftLowerLeg, enabledRightLowerLeg,
hintWeightLeftLowerLeg, hintWeightRightLowerLeg,
enabledLeftHand, enabledRightHand;

        public bool
HasChestTracker, hasHipsTracker, enabledSpineIK,
            enabledLeftShoulder, enabledRightShoulder,

leftToeEnabled, RightToeEnabled,
hintWeightLeftHand,
hintWeightRightHand,
protectElbow, collideTrackedElbow, useNeuralPole,
collisionsEnabled;

        // Per-bone override slots, indexed identically to BasisFullBodyData.
        public FixedList512Bytes<Vector3> slotPositions;
        public FixedList512Bytes<Quaternion> slotRotations;
        public FixedList512Bytes<Quaternion> slotOffsets;
        public FixedList64Bytes<bool> slotWeights;
        public FixedList128Bytes<BasisBoneHandle> slotHandles;
        public NativeArray<BasisBoneHandle> ChainHeadToSpine;
        // The anatomical envelope, PARALLEL TO ChainHeadToSpine so a chain index guards itself. The head
        // (index 0) and the hips (the last) carry Valid=false frames -- the head is welded to the HMD and
        // the hips are the anchor, so neither is a DOF the solver invents, and neither is guarded. Every
        // other entry is a real vertebral segment with its own ROM. See BasisSpineAnatomy.
        public NativeArray<BasisSpineRestFrame> ChainSpineRestFrames;
        public NativeArray<BasisSpineRom> ChainSpineRoms;
        // optional tuning (can be constants or properties)
        public int spineMaxIterations;
        public float spineTolerance;
        public Vector3 TposeLengthHeadToHips;
        // The spine's bend cue. `TposeHeadToNeckLocal` is the neck's offset from the head, IN THE HEAD'S OWN
        // FRAME, so re-attaching it to a rotated head reconstructs where the neck must be -- and cancels the
        // nod exactly (see DistributeSpineBend). `TposeLengthNeckToHips` is the matching rest span for the
        // squish coupling, which now measures the SPINE's compression instead of the head's.
        public Vector3 TposeHeadToNeckLocal;
        public Vector3 TposeLengthNeckToHips;
        public float handRadius, handSkin, chestRadius, collisionSkin, MinHeadSpineHeight, maxBendDeg, minFactor, maxFactor, MaxChestDeltaProperty;
        public float shoulderElevationFactor, shoulderProtractionFactor;
        public float spineBendPitch, spineBendYaw, spineBendRoll;
        public float upperChestBendPitch, upperChestBendYaw, upperChestBendRoll;
        public float hipHingeStartDeg, hipHingeMaxAddDeg;
        public float chestSpringHz, chestSpringDamping;
        public float spineMaxForwardDeg, spineMaxBackwardDeg, spineMaxLateralDeg;
        public float spineSquishBoost;
        public float spineGazeFollow;
        public float neckGazeFollow;
        public float moveBodyBackWhenCrouching;
        public float swingSmoothRateDeg;
        public float chestArmSwingFactor, chestArmSwingMaxDeg;
        public float lowerArmTwistFraction, upperArmTwistFraction;
        public bool anatDifferentialStiffness, anatShoulderSlide, anatCervicalLordosis, anatPelvicTwistRouting, legSwivelSmoothing;
        public bool spineAnatomicalRom;
        public bool chestIkTarget;
        public bool hintIsTrackerLeftLowerLeg, hintIsTrackerRightLowerLeg;
        public bool footIsTrackerLeftLeg, footIsTrackerRightLeg;
        public float lordosisPitchGainDeg;
        public float lordosisBaseDeg, lordosisNeckShare, lordosisMaxHeadPitchDeg;
        public float lordosisExtremeStartDeg, lordosisExtremeFullDeg;
        public float lordosisExtremeRollForwardMaxDeg, lordosisExtremeRollBackwardMaxDeg;
        public float lordosisExtremeHipsHorizontalMax, lordosisExtremeChestHorizontalMax;
        public float lordosisExtremeHipsDownMax, lordosisExtremeChestDownMax;
        public float lordosisExtremeHipsDownLookUp, lordosisExtremeChestDownLookUp;
        public float spineCCDRelax, neckMaxConeDeg, spineTwistKeep, spineNeckTwistKeep;
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
        // Per-arm gain-cap state (BasisElbowSwingCapCore): last frame's capped bend + shoulder->hand axis,
        // and an init flag reset whenever the no-tracker model did not drive the elbow (so it re-seeds).
        public NativeArray<Vector3> swingHintBend;
        public NativeArray<Vector3> swingHintAxis;
        public NativeArray<int> swingHintInit;
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
        public float ikLockMode;
        public bool shoulderSolveEnabled;
        public bool shoulderShrugEnabled;
        // T-pose baked reference data for shoulder solve
        public Vector3 TposeLeftShoulderLocalDir, TposeRightShoulderLocalDir;
        public Quaternion TposeLeftShoulderRot, TposeRightShoulderRot;
        public Quaternion TposeChestRot;
        public float TposeShoulderToHandLeft, TposeShoulderToHandRight;
        public float TposeClavicleLenLeft, TposeClavicleLenRight;
        public float TposeShoulderToElbowLeft, TposeShoulderToElbowRight;
        public BasisPoseStream Stream;

        public void Execute() => ProcessAnimation(Stream);

        public void ProcessAnimation(BasisPoseStream stream)
        {

            // Per-frame reads so FBT recalibration (which updates these on the constraint data)
            // reaches the running job; the originals were copied once at job build (issue #531).
            targetOffsetHead = offsetRotationHead;
            targetOffsetChest = offsetRotationChest;
            targetOffsetLeftFoot = offsetRotationLeftFoot;
            targetOffsetRightFoot = offsetRotationRightFoot;
            targetOffsetLeftToe = offsetRotationLeftToe;
            targetOffsetRightToe = offsetRotationRightToe;
            targetOffsetLeftShoulder = offsetRotationLeftShoulder;
            targetOffsetRightShoulder = offsetRotationRightShoulder;
            targetOffsetLeftHand = offsetRotationLeftHand;
            targetOffsetRightHand = offsetRotationRightHand;

            // 1) Spine: hips + chest/neck/head chain
            SolveSpine(stream);

            // 1b) Anatomy modifiers that act on the spine after the main solve.
            if (anatCervicalLordosis)
            {
                ApplyCervicalLordosis(stream);
            }

            // 2) Shoulder pre-solve: elevate/protract based on hand targets before arm IK
            if (shoulderSolveEnabled)
            {
                SolveShoulder(stream, HandleLeftShoulder, enabledLeftShoulder, targetPositionLeftHand, hintPositionLeftHand, hintWeightLeftHand, TposeLeftShoulderLocalDir, TposeLeftShoulderRot, TposeChestRot, TposeShoulderToHandLeft, TposeClavicleLenLeft, TposeShoulderToElbowLeft, true);
                SolveShoulder(stream, HandleRightShoulder, enabledRightShoulder, targetPositionRightHand, hintPositionRightHand, hintWeightRightHand, TposeRightShoulderLocalDir, TposeRightShoulderRot, TposeChestRot, TposeShoulderToHandRight, TposeClavicleLenRight, TposeShoulderToElbowRight, false);
            }
            else
            {
                ApplyRotation(stream, enabledLeftShoulder, HandleLeftShoulder, TargetRotationLeftShoulder, targetOffsetLeftShoulder);
                ApplyRotation(stream, enabledRightShoulder, HandleRightShoulder, TargetRotationRightShoulder, targetOffsetRightShoulder);
            }
            if (anatShoulderSlide)
            {
                ApplyShoulderSlide(stream);
            }

            // 3) Legs: two-bone IK with bend normal preference
            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, KneeBendPrefLeft, hintIsTrackerLeftLowerLeg, footIsTrackerLeftLeg, 0);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, KneeBendPrefRight, hintIsTrackerRightLowerLeg, footIsTrackerRightLeg, 1);

            // 4) Hands: two-bone IK with collision + elbow protection. bodyRight (shoulder->shoulder) orients
            // the torso's elliptical collision cross-section; shared by both arms so it is computed once here.
            Vector3 bodyRight = (HandleLeftUpperArm.IsValid(stream) && HandleRightUpperArm.IsValid(stream))
                ? HandleRightUpperArm.GetPosition(stream) - HandleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            SolveHand(stream, enabledLeftHand, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand, hintWeightLeftHand, targetOffsetLeftHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingLeftElbow);
            SolveHand(stream, enabledRightHand, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand, hintWeightRightHand, targetOffsetRightHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, protectElbow, collideTrackedElbow, bodyRight, k_SwingRightElbow);

            // Arm pop continuity: rate-limit the elbow swing so a torso-collision change eases in
            // instead of popping in one frame. Runs before arm twist (which reads the arm pose).
            float swingRate = swingSmoothRateDeg;
            float swingDt = stream.deltaTime;
            if (enabledLeftHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingLeftElbow, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, swingRate, swingDt);
            }

            if (enabledRightHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingRightElbow, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, swingRate, swingDt);
            }

            // 4b) Arm twist distribution: spread wrist/elbow roll along the optional twist bones
            // so the mesh doesn't pinch at the wrist when the hand rotates.
            float lowerTwist = lowerArmTwistFraction;
            float upperTwist = upperArmTwistFraction;
            SolveArmTwist(stream, HandleLeftLowerArm, HandleLeftHand, HandleLeftLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleRightLowerArm, HandleRightHand, HandleRightLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftUpperArmTwist, upperTwist);
            SolveArmTwist(stream, HandleRightUpperArm, HandleRightLowerArm, HandleRightUpperArmTwist, upperTwist);

            // 5) Toes
            ApplyRotation(stream, leftToeEnabled, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            ApplyRotation(stream, RightToeEnabled, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);

            // 6) Generic per-bone overrides (direct tracker control)
            for (int i = 0; i < slotHandles.Length; i++)
            {
                Apply(stream, slotHandles[i], slotPositions[i], slotRotations[i], slotOffsets[i], slotWeights[i]);
            }
        }
        public void SolveSpine(BasisPoseStream stream)
        {
            if (!enabledSpineIK)
            {
                return;
            }
            // ---- Read targets ----
            Vector3 headTargetPos = targetPositionHead;
            Vector3 hipsTargetPos = targetPositionHips;

            Quaternion headTargetRot = targetRotationHead;
            Quaternion hipsTargetRot = targetRotationHips;
            Quaternion offsetHips = offsetRotationHips;
            Quaternion chestTargetRot = targetChestRotation;

            Quaternion hipDesired = hipsTargetRot * offsetHips;
            Quaternion chestDesired = chestTargetRot * targetOffsetChest;

            float restDist = MinHeadSpineHeight;
            int lockMode = (int)ikLockMode;
            Vector3 up = playerUp;

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
                    float MaxBendDeg = maxBendDeg;
                    hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                    hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor, maxFactor, up);
                    break;
            }

            hipsTargetPos = ApplyCrouchBodyOffset(stream, headTargetPos, hipsTargetPos, hipDesired, up);
            targetPositionHips = hipsTargetPos;

            // The hinge SYNTHESISES an anterior pelvis pitch on a deep lean so the spine does not swallow the
            // whole reach -- but only when there is no hip tracker. With one, the pelvis rotation is the
            // user's OWN, measured, and must feed straight to IK "how we used to" (the hip-tilt-stabilization
            // that reshaped a tracked pelvis was built and deliberately removed for exactly this reason). The
            // hip-bob/sway synthesis in BasisLocalRigDriver is gated on the same flag, for the same reason:
            // do not invent pelvis motion on top of a tracker.
            if (!hasHipsTracker)
            {
                hipDesired = ApplyHipHinge(stream, headTargetPos, hipsTargetPos, hipDesired, up);
            }

            // Apply hips driver if valid
            if (HandleHips.IsValid(stream))
            {
                HandleHips.SetPosition(stream, hipsTargetPos);
                HandleHips.SetRotation(stream, hipDesired);
            }
            if (HasChestTracker && HandleChest.IsValid(stream))
            {
                // Neck rotation produced by your spine IK pass – we keep this
                Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;

                // Spine as an extra reference if available (nice stabiliser)
                Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;

                float Value = MaxChestDeltaProperty;
                // Clamp relative to neck and spine
                Quaternion clampedChestRot = ClampRotation(chestDesired, neckRot, Value);
                clampedChestRot = ClampRotation(clampedChestRot, spineRot, Value);

                HandleChest.SetRotation(stream, clampedChestRot);

                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

                DistributeSpineBend(stream, headPos);
                BiasSpineTowardChest(stream);
                GuardSpineChain(stream);
                SolveSequentialSpineIK(stream, headPos, headRot);
            }
            else if (HandleChest.IsValid(stream) && HandleNeck.IsValid(stream) && HandleHead.IsValid(stream))
            {
                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

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
        public void SolveSequentialSpineIK(BasisPoseStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
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

            int maxIters = Mathf.Max(1, spineMaxIterations);
            float tolerance = Mathf.Max(0f, spineTolerance);
            float tolSqr = tolerance * tolerance;

            float ccdRelax = spineCCDRelax;
            float lumbarTwistKeep = spineTwistKeep;
            float cervicalTwistKeep = spineNeckTwistKeep;
            // Body-relative twist axis (hips-up), NOT world-up: vertical standing, horizontal lying down, so
            // the relax strips the same anatomical axial-twist DOF in any orientation. Falls back to playerUp.
            Quaternion hipsTwistRot = HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : Quaternion.identity;
            Vector3 ccdUp = hipsTwistRot * Vector3.up;
            if (ccdUp.sqrMagnitude < k_SqrEpsilon) ccdUp = playerUp;
            float jointSpan = Mathf.Max(1, lastJoint - firstJoint);
            float neckCone = neckMaxConeDeg;
            float chestCone = MaxChestDeltaProperty;
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
        void ReachHeadJoint(BasisPoseStream stream, int i, Vector3 headTargetPos, int firstJoint, int chainLen,
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

            Quaternion delta = BasisQuaternionExt.FromToRotation(cur, tgt);
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
        void SolveChestTarget(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint, int lastJoint,
            int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp,
            float ccdRelax, float neckCone, float chestCone)
        {
            // Off (toggle false -> weight 0): return before touching a single bone, so the head-only solve
            // above is the whole story, bit for bit. This is the "same usability" guarantee.
            if (!chestIkTarget)
                return;

            int chestBoneIdx = chainLen - 3;   // the Chest bone
            // Need a real Spine joint below the chest to move it, and real upper joints to restore the head.
            if (chestBoneIdx < firstJoint || lastJoint <= firstJoint || lastJoint <= chestBoneIdx)
                return;

            // THE RAW chest, not the head-hint-biased TargetChestPosition -- pinning to the biased one dragged
            // the torso ~8cm up and leaned the body in desktop / no-tracker mode.
            Vector3 chestTargetPos = TargetChestPositionRaw;
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
                    Quaternion cDelta = BasisQuaternionExt.FromToRotation(cCur, cTgt);
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
        void GuardSpineJoint(BasisPoseStream stream, int i)
        {
            if (!spineAnatomicalRom)
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
        void GuardSpineChain(BasisPoseStream stream)
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
        void ClampNeckCone(BasisPoseStream stream, int neckIdx, float maxConeDeg)
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
        void ClampChestCone(BasisPoseStream stream, int chestIdx, float maxConeDeg)
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
        void BiasSpineTowardChest(BasisPoseStream stream)
        {
            if (!HandleSpine.IsValid(stream) || !HandleChest.IsValid(stream))
                return;

            Vector3 chestTargetPos = TargetChestPosition;
            Vector3 spinePos = HandleSpine.GetPosition(stream);
            Vector3 chestPos = HandleChest.GetPosition(stream);

            if ((chestTargetPos - chestPos).sqrMagnitude > k_ChestPullMaxDistSqr)
                return;

            Vector3 cur = chestPos - spinePos;
            Vector3 tgt = chestTargetPos - spinePos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion pull = ClampRotation(BasisQuaternionExt.FromToRotation(cur, tgt), Quaternion.identity, k_ChestPosPullMaxDeg);
            HandleSpine.SetRotation(stream, pull * HandleSpine.GetRotation(stream));
        }
        // Pre-distributes the hips→head bend onto spine and upperChest in hips-local space, split
        // into independent pitch / yaw / roll contributions so anisotropic human ranges of motion
        // can be respected (lumbar twists very little, cervical twists a lot, forward bend ≫ back).
        // Pipeline: (chest spring smooths target) → (decompose bend into pitch/roll, twist into yaw)
        //   → (per-axis weight) → (asymmetric clamp) → (apply as hips-local delta).
        // The chest→neck→head two-bone solve afterwards handles whatever residual reach remains.
        public void DistributeSpineBend(BasisPoseStream stream, Vector3 headTargetPos)
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
            Quaternion headWorldRot = targetRotationHead * targetOffsetHead;

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
            Vector3 spineCue = Vector3.Lerp(neckCue, headTargetPos, Mathf.Clamp01(spineGazeFollow));

            Quaternion hipsBind = offsetRotationHips;

            BasisSpineBendInput input;
            input.HipsRot = hipsRot;
            input.HipsPos = HandleHips.GetPosition(stream);
            input.ChestPos = HandleChest.GetPosition(stream);
            input.SmoothedHead = ApplyChestSpring(stream, spineCue);
            input.HipsBind = hipsBind;
            input.HeadTargetRot = targetRotationHead;
            input.SpineMaxForwardDeg = spineMaxForwardDeg;
            input.SpineMaxBackwardDeg = spineMaxBackwardDeg;
            input.SpineMaxLateralDeg = spineMaxLateralDeg;
            input.SpineBendPitch = spineBendPitch;
            input.SpineBendYaw = spineBendYaw;
            input.SpineBendRoll = spineBendRoll;
            input.UpperBendPitch = upperChestBendPitch;
            input.UpperBendYaw = upperChestBendYaw;
            input.UpperBendRoll = upperChestBendRoll;
            input.AnatDifferentialStiffness = anatDifferentialStiffness;
            input.AnatPelvicTwistRouting = anatPelvicTwistRouting;
            input.SquishBoost = spineSquishBoost;
            input.RestLen = TposeLengthNeckToHips.magnitude;   // the spine spans hips->NECK; the head was never part of it
            input.BendTwistCoupling = k_BendTwistCoupling;
            input.HasSpine = hasSpine;
            input.HasUpper = hasUpper;

            // A tracked chest already measures torso lean, so the head-position-derived forward/lateral
            // pre-bend is redundant -- and looking down swings the HMD forward of the neck, which it
            // misreads as a lean and hunches the chest forward (the squish boost compounds it). Drop the
            // lean (pitch/roll) and let the tracked chest + the spine chain own it; keep the facing twist.
            if (HasChestTracker)
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
        Vector3 ApplyChestSpring(BasisPoseStream stream, Vector3 headTargetPos)
        {
            if (!chestSpringState.IsCreated || !chestSpringInit.IsCreated)
            {
                return headTargetPos;
            }

            float hz = chestSpringHz;
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
                chestSpringDamping, out Vector3 newPos, out Vector3 newVel);

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
        Quaternion ApplyHipHinge(BasisPoseStream stream, Vector3 headPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUp)
        {
            BasisHipHingeInput input;
            input.HeadPos = headPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.PlayerUp = playerUp;
            input.StartDeg = hipHingeStartDeg;
            input.MaxAddDeg = hipHingeMaxAddDeg;
            BasisHipHingeCore.Solve(input, out BasisHipHingeResult result);
            return result.HipsRot;
        }
        Vector3 ApplyCrouchBodyOffset(BasisPoseStream stream, Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir)
        {
            if (HasChestTracker || hasHipsTracker)
            {
                return hipsPos;
            }

            BasisCrouchOffsetInput input;
            input.HeadTargetPos = headTargetPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.Bind = offsetRotationHips;
            input.PlayerUp = playerUpDir;
            input.Factor = moveBodyBackWhenCrouching;
            input.RestDist = MinHeadSpineHeight;
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        // Extra forward neck curve at FULL look-down when NeckGazeFollow = 1 (it scales this by the setting
        // and by how far down you look). Modest: the head is re-pinned so this only arcs the neck, but too
        // much cocks the head relative to the neck. The user dials the setting; this is the ceiling.
        const float k_NeckGazeFollowMaxDeg = 18f;
        void ApplyCervicalLordosis(BasisPoseStream stream)
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
                Vector3 up = playerUp;
                referenceUp = up.sqrMagnitude < k_SqrEpsilon ? Vector3.up : up.normalized;
            }

            BasisCervicalInput input;
            input.BaseDeg = lordosisBaseDeg;
            input.NeckShare = Mathf.Clamp01(lordosisNeckShare);
            input.MaxHeadPitchDeg = lordosisMaxHeadPitchDeg;
            input.ExtremeStartDeg = lordosisExtremeStartDeg;
            input.ExtremeFullDeg = lordosisExtremeFullDeg;
            input.ExtremeRollForwardMaxDeg = lordosisExtremeRollForwardMaxDeg;
            input.ExtremeRollBackwardMaxDeg = lordosisExtremeRollBackwardMaxDeg;
            input.ExtremeHipsHorizontalMax = lordosisExtremeHipsHorizontalMax;
            input.ExtremeChestHorizontalMax = lordosisExtremeChestHorizontalMax;
            input.ExtremeHipsDownMax = lordosisExtremeHipsDownMax;
            input.ExtremeChestDownMax = lordosisExtremeChestDownMax;
            input.ExtremeHipsDownLookUp = lordosisExtremeHipsDownLookUp;
            input.ExtremeChestDownLookUp = lordosisExtremeChestDownLookUp;
            input.PitchGainDeg = Mathf.Max(0f, lordosisPitchGainDeg);
            input.ReferenceUp = referenceUp;
            input.HeadTargetRot = targetRotationHead;
            input.HasUpperChest = HandleUpperChest.IsValid(stream);

            BasisCervicalSolveCore.Solve(input, out BasisCervicalResult result);
            if (result.EarlyOut)
            {
                return;
            }

            BasisBoneHandle bendHandle = input.HasUpperChest ? HandleUpperChest : HandleChest;
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
            float extraNeckDeg = Mathf.Clamp01(neckGazeFollow) * k_NeckGazeFollowMaxDeg * result.LookDownFrac;
            float totalNeckDeg = result.NeckDeg + extraNeckDeg;
            if (totalNeckDeg != 0f)
            {
                Quaternion neckRotCurrent = HandleNeck.GetRotation(stream);
                HandleNeck.SetRotation(stream, Quaternion.AngleAxis(totalNeckDeg, neckRotCurrent * Vector3.right) * neckRotCurrent);
            }

            if (HandleHead.IsValid(stream))
            {
                HandleHead.SetPosition(stream, targetPositionHead);
                HandleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
            }
        }
        // Anatomy: shoulder slide. Shoulders don't fully follow chest twist past ~30° because the
        // scapula slides on the rib cage. Counter-yaw both shoulders by a fraction of the chest's
        // twist relative to hips, capped at 15°.
        void ApplyShoulderSlide(BasisPoseStream stream)
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
        void ApplyShoulderYaw(BasisPoseStream stream, BasisBoneHandle shoulder, Quaternion hipsRot, float yawDeg)
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
        void ApplyArmSwingChestFollow(BasisPoseStream stream)
        {
            float factor = chestArmSwingFactor;
            if (factor <= 0f)
            {
                return;
            }

            if (!HandleHips.IsValid(stream) || !HandleChest.IsValid(stream))
            {
                return;
            }

            bool leftEnabled = enabledLeftHand > 0f;
            bool rightEnabled = enabledRightHand > 0f;
            if (!leftEnabled && !rightEnabled)
            {
                return;
            }

            Vector3 leftPos = leftEnabled ? targetPositionLeftHand : Vector3.zero;
            Vector3 rightPos = rightEnabled ? targetPositionRightHand : Vector3.zero;
            Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
            Vector3 hipsPos = HandleHips.GetPosition(stream);
            // Bind-cancelled hips frame (hipsRot * inv(bind)): the hand-midpoint is decomposed into yaw/pitch
            // in the body's ANATOMICAL right/forward, and the delta re-applied about the same axes. In the raw
            // hips-bone frame a rolled bind turned the forward-follow into a chest roll. No-op at identity bind.
            Quaternion hipsAnat = HandleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            Vector3 localMid = invHipsAnat * (handMid - hipsPos);

            float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
            float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;

            Vector3 localMidChest = invHipsAnat * (handMid - HandleChest.GetPosition(stream));
            float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;

            float maxDeg = chestArmSwingMaxDeg;
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
        void SolveArmTwist(BasisPoseStream stream, BasisBoneHandle parent, BasisBoneHandle child, BasisBoneHandle twist, float fraction)
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
        public void SolveShoulder(BasisPoseStream stream, BasisBoneHandle shoulderHandle, bool hasShoulderTrackerProp, Vector3 handTargetPosProp, Vector3 hintPosProp, bool hintWeightProp, Vector3 tposeArmDir, Quaternion tposeShoulderRot, Quaternion tposeChestRot, float tposeArmLength, float tposeClavicleLen, float tposeElbowLen, bool isLeft)
        {
            if (!shoulderHandle.IsValid(stream))
            {
                return;
            }

            Quaternion trackerRot = isLeft ? TargetRotationLeftShoulder : TargetRotationRightShoulder;

            BasisShoulderSolveInput input;
            input.ShoulderPos = shoulderHandle.GetPosition(stream);
            input.HandTargetPos = handTargetPosProp;
            input.ElbowPos = hintPosProp;
            input.HasElbow = hintWeightProp;
            input.HasShoulderTracker = hasShoulderTrackerProp;
            input.ChestRot = HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity;
            input.TposeChestRot = tposeChestRot;
            input.TposeShoulderRot = tposeShoulderRot;
            input.TposeArmDirWorld = tposeArmDir;
            input.TposeArmLength = tposeArmLength;
            input.TposeClavicleLength = tposeClavicleLen;
            input.TposeElbowLength = tposeElbowLen;
            input.ShrugEnabled = shoulderShrugEnabled;
            input.ElevationFactor = shoulderElevationFactor;
            input.ProtractionFactor = shoulderProtractionFactor;
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
        public void ApplyRotation(BasisPoseStream stream, bool enabledProp, BasisBoneHandle handle, Quaternion targetRotProp, Quaternion RotationOffset)
        {
            if (!handle.IsValid(stream))
            {
                return;
            }

            if (enabledProp)
            {
                handle.SetRotation(stream, targetRotProp * RotationOffset);
            }
        }
        public void SolveTwoBoneIKArms(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, BasisAffineTransform hint, bool hintWeight, bool hintIsTracker, Quaternion targetOffset)
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
            input.PlayerUp = playerUp;
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
        BasisSwivelFrame BuildArmFrame(BasisPoseStream stream)
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
        BasisSwivelFrame BuildLegFrame(BasisPoseStream stream)
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
        public static void SwingElbowAroundAC(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 desiredB)
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
        void ApplySwingContinuity(BasisPoseStream stream, int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos, float rateDegPerSec, float dt)
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
        public void SolveTwoBone(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, BasisAffineTransform hint, float hintWeight, Quaternion targetOffset, Vector3 BendNormal, float hintDistrust = 0f)
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
        public void SolveLegs(BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, float hintWeightProp, Quaternion targetOffset, Vector3 bendNormalProp, bool hintIsTrackerProp, bool footIsTrackerProp, int legSlot)
        {
            float posWeight = enabledProp;
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
            Quaternion tRot = targetRotProp;
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
            float hintW = hintWeightProp;

            BasisAffineTransform target = new BasisAffineTransform(targetPosProp, tRot);
            // Hint rotation is unused by the leg solve (BasisLegSolveInput has no rotation field).
            BasisAffineTransform hint = new BasisAffineTransform(hintPosProp, Quaternion.identity);
            Vector3 bendNormal = bendNormalProp;

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
                                                out Vector3 modelHint, out float conf, useNeuralPole))
                {
                    hint = new BasisAffineTransform(modelHint, Quaternion.identity);
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
            if (legSwivelSmoothing)
            {
                // A REAL foot tracker -- not merely a non-sentinel target rotation. FootRotationFromDriver
                // makes the procedural driver emit a real quaternion, so !preserveTip stopped meaning
                // "tracked foot" and a desktop leg was taking the responsive branch, losing the heavy
                // standing floor that exists to stop hips-yaw jitter rolling a near-straight leg.
                if (hintIsTrackerProp || footIsTrackerProp)
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
                        conditionOnPole: !hintIsTrackerProp);
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
        public void Apply(BasisPoseStream stream, BasisBoneHandle h, Vector3 p, Quaternion r, Quaternion o, bool sw)
        {
            if (h.IsValid(stream))
            {
                if (sw)
                {

                    Vector3 targetPos = p;
                    Quaternion targetRot = r;
                    Quaternion offsetRot = o;
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
        void SmoothKneeSwivel(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int slot, float dt, float minCutoffHz, float beta, float derivCutoffHz, bool conditionOnPole)
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
            // ⭐ SINGULARITY HOLD (knee only). A standing leg is pinned at the 176 cap on the pole singularity,
            // where the swivel angle carries no information and a slow body-frame sway (postural, pivoting over a
            // planted foot) rolls the whole leg -- "the knee slowly rotates back and forth while all the trackers
            // are still". This is exactly the case the tracked path (conditionOnPole=false, the 07-17 "6x faster"
            // responsiveness fix) stopped damping: a low-pass can't remove a ~0.3 Hz oscillation, only a HOLD can.
            // Freeze the swivel in the near-straight band; release the instant the knee bends (HoldCondHi), so
            // deliberate shin motion is byte-for-byte untouched. See BasisSwivelSmootherCore. Applies to BOTH the
            // tracked and invented-pole knee paths -- both live on the same standing singularity.
            input.HoldWhenSingular = true;
            input.HoldCondLo = BasisSwivelSmootherCore.DefaultHoldCondLo;
            input.HoldCondHi = BasisSwivelSmootherCore.DefaultHoldCondHi;
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
        public void SolveHand(BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, Quaternion hintRotProp, bool hintWeightProp, Quaternion targetOffset, BasisBoneHandle chestStart, BasisBoneHandle chestEnd, float chestRadius, float collisionSkin, bool collisionsEnabled, float handRadius, float handSkin, bool protectElbow, bool collideTrackedElbow, Vector3 bodyRight, int swingSlot)
        {
            // Written `!(w > 0)` so a NaN weight takes the reject branch rather than solving on garbage.
            float weight = enabledProp;
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
            Vector3 tgtPos = targetPosProp;
            Quaternion tgtRot = targetRotProp;
            Vector3 hintPos = hintPosProp;
            Quaternion hintRot = hintRotProp;

            var target = new BasisAffineTransform(tgtPos, tgtRot);
            var hint = new BasisAffineTransform(hintPos, hintRot);
            bool hasHint = hintWeightProp;
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
                                                out Vector3 modelHint, out _, useNeuralPole))
                {
                    // GAIN-CAP the model bend against the hand's own rotation. The bend field has
                    // topologically-required cores (BasisElbowFieldModel's down-and-back one is the
                    // reach-behind snap); sweeping the hand through a core flips the bend faster than any
                    // human elbow tracks. The cap bounds bend rotation to MaxGain x hand rotation -- a
                    // no-op everywhere the field is already slower (bit-identical), a bounded fast sweep at
                    // the human ceiling through a core. State is per swing slot; it always chases the field,
                    // so a stale carried pole self-corrects (unlike the reverted hold-the-pole coast).
                    Vector3 curAxisV = tgtPos - shoulderPos;
                    Vector3 rawBendV = modelHint - shoulderPos;
                    float axLen = curAxisV.magnitude;
                    float rbLen = rawBendV.magnitude;
                    if (axLen > 1e-5f && rbLen > 1e-5f)
                    {
                        // Vector3 throughout (the file's convention); the Apply boundary converts to/from
                        // Unity.Mathematics.float3 implicitly.
                        Vector3 curAxis = curAxisV / axLen;
                        Vector3 rawBend = rawBendV / rbLen;
                        Vector3 cappedBend = swingHintInit[swingSlot] == 0
                            ? rawBend
                            : (Vector3)BasisElbowSwingCapCore.Apply(swingHintBend[swingSlot], swingHintAxis[swingSlot],
                                                                    curAxis, rawBend, BasisElbowSwingCapCore.MaxGain);
                        swingHintBend[swingSlot] = cappedBend;
                        swingHintAxis[swingSlot] = curAxis;
                        swingHintInit[swingSlot] = 1;
                        modelHint = shoulderPos + 0.5f * armLen * cappedBend;
                    }

                    hint = new BasisAffineTransform(modelHint, hintRot);
                    hasHint = true;
                    usedModel = true;
                }
            }
            // Reset the gain-cap state whenever the no-tracker model did NOT drive the elbow this frame (a
            // real elbow tracker, or a degenerate frame), so the model re-seeds on its next frame rather
            // than transporting a stale, unrelated pole.
            if (!usedModel)
            {
                swingHintInit[swingSlot] = 0;
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
            bool doCollisions = collisionsEnabled && chestStart.IsValid(stream) && chestEnd.IsValid(stream);
            bool elbowTrackerForced = hasHint && !usedModel;
            if (doCollisions && protectElbow && (!elbowTrackerForced || collideTrackedElbow))
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
                epi.ChestRadiusBase = chestRadius;
                epi.CollisionSkin = collisionSkin;
                epi.HandRadius = handRadius;
                epi.HandSkin = handSkin;
                epi.PlayerUp = playerUp;
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
    public static class BasisFullBodyJobBinder
    {

        public static void Sync(ref BasisFullIKConstraintJob job, ref BasisFullBodyData data)
        {
            job.targetPositionHips = data.PositionHips;
            job.targetPositionHead = data.PositionHead;
            job.TargetChestPosition = data.ChestPosition;
            job.TargetChestPositionRaw = data.ChestPositionRaw;
            job.playerUp = data.PlayerUp;
            job.KneeBendPrefLeft = data.KneeBendPrefLeft;
            job.KneeBendPrefRight = data.KneeBendPrefRight;
            job.targetPositionLeftLowerLeg = data.LeftFootPosition;
            job.hintPositionLeftLowerLeg = data.PositionLeftLowerLeg;
            job.targetPositionRightLowerLeg = data.RightFootPosition;
            job.hintPositionRightLowerLeg = data.PositionRightLowerLeg;
            job.targetPositionLeftHand = data.PositionLeftHand;
            job.hintPositionLeftHand = data.LeftLowerArmPosition;
            job.targetPositionRightHand = data.PositionRightHand;
            job.hintPositionRightHand = data.RightLowerArmPosition;
            job.targetRotationHips = data.RotationHips;
            job.offsetRotationHips = data.OffsetRotationHips;
            job.targetRotationHead = data.RotationHead;
            job.targetChestRotation = data.ChestRotation;
            job.TargetRotationLeftShoulder = data.LeftShoulderRotation;
            job.TargetRotationRightShoulder = data.RightShoulderRotation;
            job.targetRotationLeftLowerLeg = data.LeftFootRotation;
            job.targetRotationRightLowerLeg = data.RightFootRotation;
            job.leftDrivenTargetRot = data.OutGoingLeftToeRotation;
            job.rightDrivenTargetRot = data.OutGoingRightToeRotation;
            job.targetRotationLeftHand = data.RotationLeftHand;
            job.hintRotationLeftHand = data.LeftLowerArmRotation;
            job.targetRotationRightHand = data.RotationRightHand;
            job.hintRotationRightHand = data.RightLowerArmRotation;
            job.enabledSpineIK = data.EnabledSpineIK;
            job.HasChestTracker = data.WeightChest;
            job.hasHipsTracker = data.HasHipsTracker;
            job.footIsTrackerLeftLeg = data.LeftFootIsTracker;
            job.footIsTrackerRightLeg = data.RightFootIsTracker;
            job.enabledLeftLowerLeg = data.EnableLeftLeg;
            job.hintWeightLeftLowerLeg = data.EnableLeftLowerLeg;
            job.enabledRightLowerLeg = data.EnableRightLeg;
            job.hintWeightRightLowerLeg = data.EnableRightLowerLeg;
            job.leftToeEnabled = data.LeftToeEnabled;
            job.RightToeEnabled = data.RightToeEnabled;
            job.enabledLeftHand = data.EnabledLeftHand;
            job.hintWeightLeftHand = data.HintWeightLeftHand;
            job.enabledRightHand = data.EnabledRightHand;
            job.hintWeightRightHand = data.HintWeightRightHand;
            job.protectElbow = data.ProtectElbow;
            job.useNeuralPole = data.UseNeuralPole;
            job.collideTrackedElbow = data.CollideTrackedElbow;
            job.collisionsEnabled = data.CollisionsEnabled;
            job.chestRadius = data.ChestRadius;
            job.collisionSkin = data.CollisionSkin;
            job.handRadius = data.HandRadius;
            job.handSkin = data.HandSkin;
            job.maxBendDeg = data.MaxBendDeg;
            job.minFactor = data.MinFactor;
            job.maxFactor = data.MaxFactor;
            job.MaxChestDeltaProperty = data.MaxChestDelta;
            job.enabledLeftShoulder = data.EnabledLeftShoulder;
            job.enabledRightShoulder = data.EnabledRightShoulder;
            job.offsetRotationLeftShoulder = data.m_CalibratedRotationLeftShoulder;
            job.offsetRotationRightShoulder = data.m_CalibratedRotationRightShoulder;
            job.offsetRotationHead = data.m_CalibratedRotationHead;
            job.offsetRotationChest = data.m_CalibratedRotationChest;
            job.offsetRotationLeftToe = data.m_CalibratedRotationLeftToe;
            job.offsetRotationRightToe = data.m_CalibratedRotationRightToe;
            job.offsetRotationLeftFoot = data.M_CalibrationLeftFootRotation;
            job.offsetRotationRightFoot = data.M_CalibrationRightFootRotation;
            job.offsetRotationLeftHand = data.m_CalibratedRotationLeftHand;
            job.offsetRotationRightHand = data.m_CalibratedRotationRightHand;
            job.MinHeadSpineHeight = data.minHeadSpineHeight;
            job.shoulderSolveEnabled = data.ShoulderSolveEnabled;
            job.shoulderShrugEnabled = data.ShoulderShrugEnabled;
            job.shoulderElevationFactor = data.ShoulderElevationFactor;
            job.shoulderProtractionFactor = data.ShoulderProtractionFactor;
            job.spineBendPitch = data.SpineBendPitch;
            job.spineBendYaw = data.SpineBendYaw;
            job.spineBendRoll = data.SpineBendRoll;
            job.upperChestBendPitch = data.UpperChestBendPitch;
            job.upperChestBendYaw = data.UpperChestBendYaw;
            job.upperChestBendRoll = data.UpperChestBendRoll;
            job.hipHingeStartDeg = data.HipHingeStartDeg;
            job.hipHingeMaxAddDeg = data.HipHingeMaxAddDeg;
            job.chestSpringHz = data.ChestSpringHz;
            job.chestSpringDamping = data.ChestSpringDamping;
            job.spineMaxForwardDeg = data.SpineMaxForwardDeg;
            job.spineMaxBackwardDeg = data.SpineMaxBackwardDeg;
            job.spineMaxLateralDeg = data.SpineMaxLateralDeg;
            job.spineSquishBoost = data.SpineSquishBoost;
            job.spineGazeFollow = data.SpineGazeFollow;
            job.neckGazeFollow = data.NeckGazeFollow;
            job.moveBodyBackWhenCrouching = data.MoveBodyBackWhenCrouching;
            job.swingSmoothRateDeg = data.SwingSmoothRateDeg;
            job.chestArmSwingFactor = data.ChestArmSwingFactor;
            job.chestArmSwingMaxDeg = data.ChestArmSwingMaxDeg;
            job.lowerArmTwistFraction = data.LowerArmTwistFraction;
            job.upperArmTwistFraction = data.UpperArmTwistFraction;
            job.anatDifferentialStiffness = data.AnatDifferentialStiffness;
            job.anatShoulderSlide = data.AnatShoulderSlide;
            job.anatCervicalLordosis = data.AnatCervicalLordosis;
            job.anatPelvicTwistRouting = data.AnatPelvicTwistRouting;
            job.spineAnatomicalRom = data.SpineAnatomicalRom;
            job.chestIkTarget = data.ChestIKTarget;
            job.legSwivelSmoothing = data.LegSwivelSmoothing;
            job.hintIsTrackerLeftLowerLeg = data.LeftLowerLegHintIsTracker;
            job.hintIsTrackerRightLowerLeg = data.RightLowerLegHintIsTracker;
            job.lordosisPitchGainDeg = data.LordosisPitchGainDeg;
            job.lordosisBaseDeg = data.LordosisBaseDeg;
            job.lordosisNeckShare = data.LordosisNeckShare;
            job.lordosisMaxHeadPitchDeg = data.LordosisMaxHeadPitchDeg;
            job.lordosisExtremeStartDeg = data.LordosisExtremeStartDeg;
            job.lordosisExtremeFullDeg = data.LordosisExtremeFullDeg;
            job.lordosisExtremeRollForwardMaxDeg = data.LordosisExtremeRollForwardMaxDeg;
            job.lordosisExtremeRollBackwardMaxDeg = data.LordosisExtremeRollBackwardMaxDeg;
            job.lordosisExtremeHipsHorizontalMax = data.LordosisExtremeHipsHorizontalMax;
            job.lordosisExtremeChestHorizontalMax = data.LordosisExtremeChestHorizontalMax;
            job.lordosisExtremeHipsDownMax = data.LordosisExtremeHipsDownMax;
            job.lordosisExtremeChestDownMax = data.LordosisExtremeChestDownMax;
            job.lordosisExtremeHipsDownLookUp = data.LordosisExtremeHipsDownLookUp;
            job.lordosisExtremeChestDownLookUp = data.LordosisExtremeChestDownLookUp;
            job.spineCCDRelax = data.SpineCCDRelax;
            job.neckMaxConeDeg = data.NeckMaxConeDeg;
            job.spineTwistKeep = data.SpineTwistKeep;
            job.spineNeckTwistKeep = data.SpineNeckTwistKeep;
            job.ikLockMode = data.IKLockMode;
            job.slotPositions = data.TargetPositions;
            job.slotRotations = data.TargetRotations;
            job.slotOffsets = data.OffsetRotations;
            job.slotWeights = data.Weights;
        }

        public static BasisFullIKConstraintJob Create(BasisPoseSkeleton skeleton, ref BasisFullBodyData data)
        {
            var job = new BasisFullIKConstraintJob
            {
                HandleHips = BindHandle(skeleton, data.hips),
                HandleChest = BindHandle(skeleton, data.chest),
                HandleNeck = BindHandle(skeleton, data.neck),
                HandleHead = BindHandle(skeleton, data.head),
                HandleLeftUpperLeg = BindHandle(skeleton, data.LeftUpperLeg),
                HandleLeftLowerLeg = BindHandle(skeleton, data.LeftLowerLeg),
                HandleLeftFoot = BindHandle(skeleton, data.leftFoot),
                HandleRightUpperLeg = BindHandle(skeleton, data.RightUpperLeg),
                HandleRightLowerLeg = BindHandle(skeleton, data.RightLowerLeg),
                HandleRightFoot = BindHandle(skeleton, data.RightFoot),
                HandleLeftToe = BindHandle(skeleton, data.LeftToe),
                HandleRightToe = BindHandle(skeleton, data.RightToe),
                HandleLeftUpperArm = BindHandle(skeleton, data.leftUpperArm),
                HandleLeftLowerArm = BindHandle(skeleton, data.leftLowerArm),
                HandleLeftHand = BindHandle(skeleton, data.LeftHand),
                HandleRightUpperArm = BindHandle(skeleton, data.RightUpperArm),
                HandleRightLowerArm = BindHandle(skeleton, data.RightLowerArm),
                HandleRightHand = BindHandle(skeleton, data.RightHand),
                HandleLeftUpperArmTwist = BindHandle(skeleton, data.LeftUpperArmTwist),
                HandleLeftLowerArmTwist = BindHandle(skeleton, data.LeftLowerArmTwist),
                HandleRightUpperArmTwist = BindHandle(skeleton, data.RightUpperArmTwist),
                HandleRightLowerArmTwist = BindHandle(skeleton, data.RightLowerArmTwist),
                HandleSpine = BindHandle(skeleton, data.spine),
                HandleUpperChest = BindHandle(skeleton, data.upperChest),
                HandleLeftShoulder = BindHandle(skeleton, data.LeftShoulder),
                HandleRightShoulder = BindHandle(skeleton, data.RightShoulder),
                targetPositionHips = data.PositionHips,
                targetPositionHead = data.PositionHead,
                TargetChestPosition = data.ChestPosition,
                TargetChestPositionRaw = data.ChestPositionRaw,
                playerUp = data.PlayerUp,

                KneeBendPrefLeft = data.KneeBendPrefLeft,
                KneeBendPrefRight = data.KneeBendPrefRight,

                targetPositionLeftLowerLeg = data.LeftFootPosition,
                hintPositionLeftLowerLeg = data.PositionLeftLowerLeg,
                targetPositionRightLowerLeg = data.RightFootPosition,
                hintPositionRightLowerLeg = data.PositionRightLowerLeg,
                targetPositionLeftHand = data.PositionLeftHand,
                hintPositionLeftHand = data.LeftLowerArmPosition,
                targetPositionRightHand = data.PositionRightHand,
                hintPositionRightHand = data.RightLowerArmPosition,
                targetRotationHips = data.RotationHips,
                offsetRotationHips = data.OffsetRotationHips,
                targetRotationHead = data.RotationHead,
                targetChestRotation = data.ChestRotation,
                TargetRotationLeftShoulder = data.LeftShoulderRotation,
                TargetRotationRightShoulder = data.RightShoulderRotation,
                targetRotationLeftLowerLeg = data.LeftFootRotation,
                targetRotationRightLowerLeg = data.RightFootRotation,
                leftDrivenTargetRot = data.OutGoingLeftToeRotation,
                rightDrivenTargetRot = data.OutGoingRightToeRotation,
                targetRotationLeftHand = data.RotationLeftHand,
                hintRotationLeftHand = data.LeftLowerArmRotation,
                targetRotationRightHand = data.RotationRightHand,
                hintRotationRightHand = data.RightLowerArmRotation,
                enabledSpineIK = data.EnabledSpineIK,
                HasChestTracker = data.WeightChest,
                hasHipsTracker = data.HasHipsTracker,
                enabledLeftLowerLeg = data.EnableLeftLeg,
                hintWeightLeftLowerLeg = data.EnableLeftLowerLeg,
                enabledRightLowerLeg = data.EnableRightLeg,
                hintWeightRightLowerLeg = data.EnableRightLowerLeg,
                leftToeEnabled = data.LeftToeEnabled,
                RightToeEnabled = data.RightToeEnabled,
                enabledLeftHand = data.EnabledLeftHand,
                hintWeightLeftHand = data.HintWeightLeftHand,
                enabledRightHand = data.EnabledRightHand,
                hintWeightRightHand = data.HintWeightRightHand,
                protectElbow = data.ProtectElbow,
                useNeuralPole = data.UseNeuralPole,
                collideTrackedElbow = data.CollideTrackedElbow,
                collisionsEnabled = data.CollisionsEnabled,
                chestRadius = data.ChestRadius,
                collisionSkin = data.CollisionSkin,
                handRadius = data.HandRadius,
                handSkin = data.HandSkin,
                maxBendDeg = data.MaxBendDeg,
                minFactor = data.MinFactor,
                maxFactor = data.MaxFactor,
                MaxChestDeltaProperty = data.MaxChestDelta,
                enabledLeftShoulder = data.EnabledLeftShoulder,
                enabledRightShoulder = data.EnabledRightShoulder,
                offsetRotationLeftShoulder = data.m_CalibratedRotationLeftShoulder,
                offsetRotationRightShoulder = data.m_CalibratedRotationRightShoulder,
                offsetRotationHead = data.m_CalibratedRotationHead,
                offsetRotationChest = data.m_CalibratedRotationChest,
                offsetRotationLeftToe = data.m_CalibratedRotationLeftToe,
                offsetRotationRightToe = data.m_CalibratedRotationRightToe,
                offsetRotationLeftFoot = data.M_CalibrationLeftFootRotation,
                offsetRotationRightFoot = data.M_CalibrationRightFootRotation,
                offsetRotationLeftHand = data.m_CalibratedRotationLeftHand,
                offsetRotationRightHand = data.m_CalibratedRotationRightHand,
                MinHeadSpineHeight = data.minHeadSpineHeight,

                // Shoulder solve bindings
                shoulderSolveEnabled = data.ShoulderSolveEnabled,
                shoulderShrugEnabled = data.ShoulderShrugEnabled,
                shoulderElevationFactor = data.ShoulderElevationFactor,
                shoulderProtractionFactor = data.ShoulderProtractionFactor,

                // Spine bend distribution bindings (per-axis pitch/yaw/roll)
                spineBendPitch = data.SpineBendPitch,
                spineBendYaw = data.SpineBendYaw,
                spineBendRoll = data.SpineBendRoll,
                upperChestBendPitch = data.UpperChestBendPitch,
                upperChestBendYaw = data.UpperChestBendYaw,
                upperChestBendRoll = data.UpperChestBendRoll,
                hipHingeStartDeg = data.HipHingeStartDeg,
                hipHingeMaxAddDeg = data.HipHingeMaxAddDeg,
                chestSpringHz = data.ChestSpringHz,
                chestSpringDamping = data.ChestSpringDamping,
                spineMaxForwardDeg = data.SpineMaxForwardDeg,
                spineMaxBackwardDeg = data.SpineMaxBackwardDeg,
                spineMaxLateralDeg = data.SpineMaxLateralDeg,
                spineSquishBoost = data.SpineSquishBoost,
                spineGazeFollow = data.SpineGazeFollow,
                neckGazeFollow = data.NeckGazeFollow,
                moveBodyBackWhenCrouching = data.MoveBodyBackWhenCrouching,
                swingSmoothRateDeg = data.SwingSmoothRateDeg,
                chestArmSwingFactor = data.ChestArmSwingFactor,
                chestArmSwingMaxDeg = data.ChestArmSwingMaxDeg,
                lowerArmTwistFraction = data.LowerArmTwistFraction,
                upperArmTwistFraction = data.UpperArmTwistFraction,

                anatDifferentialStiffness = data.AnatDifferentialStiffness,
                anatShoulderSlide = data.AnatShoulderSlide,
                anatCervicalLordosis = data.AnatCervicalLordosis,
                anatPelvicTwistRouting = data.AnatPelvicTwistRouting,
                spineAnatomicalRom = data.SpineAnatomicalRom,
                chestIkTarget = data.ChestIKTarget,
                legSwivelSmoothing = data.LegSwivelSmoothing,
                hintIsTrackerLeftLowerLeg = data.LeftLowerLegHintIsTracker,
                hintIsTrackerRightLowerLeg = data.RightLowerLegHintIsTracker,
                lordosisPitchGainDeg = data.LordosisPitchGainDeg,
                lordosisBaseDeg = data.LordosisBaseDeg,
                lordosisNeckShare = data.LordosisNeckShare,
                lordosisMaxHeadPitchDeg = data.LordosisMaxHeadPitchDeg,
                lordosisExtremeStartDeg = data.LordosisExtremeStartDeg,
                lordosisExtremeFullDeg = data.LordosisExtremeFullDeg,
                lordosisExtremeRollForwardMaxDeg = data.LordosisExtremeRollForwardMaxDeg,
                lordosisExtremeRollBackwardMaxDeg = data.LordosisExtremeRollBackwardMaxDeg,
                lordosisExtremeHipsHorizontalMax = data.LordosisExtremeHipsHorizontalMax,
                lordosisExtremeChestHorizontalMax = data.LordosisExtremeChestHorizontalMax,
                lordosisExtremeHipsDownMax = data.LordosisExtremeHipsDownMax,
                lordosisExtremeChestDownMax = data.LordosisExtremeChestDownMax,
                lordosisExtremeHipsDownLookUp = data.LordosisExtremeHipsDownLookUp,
                lordosisExtremeChestDownLookUp = data.LordosisExtremeChestDownLookUp,
                spineCCDRelax = data.SpineCCDRelax,
                neckMaxConeDeg = data.NeckMaxConeDeg,
                spineTwistKeep = data.SpineTwistKeep,
                spineNeckTwistKeep = data.SpineNeckTwistKeep,

                // IK Lock Mode binding
                ikLockMode = data.IKLockMode,

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

            // Bind slot data
            job.slotPositions = data.TargetPositions;
            job.slotRotations = data.TargetRotations;
            job.slotOffsets = data.OffsetRotations;
            job.slotWeights = data.Weights;

            // Pair each slot with its bone handle, in HumanBodyBones order.
            job.slotHandles.Length = BasisFullBodyData.Count;
            job.slotHandles[0] = job.HandleHips;
            job.slotHandles[1] = job.HandleLeftUpperLeg;
            job.slotHandles[2] = job.HandleRightUpperLeg;
            job.slotHandles[3] = job.HandleLeftLowerLeg;
            job.slotHandles[4] = job.HandleRightLowerLeg;
            job.slotHandles[5] = job.HandleLeftFoot;
            job.slotHandles[6] = job.HandleRightFoot;
            job.slotHandles[7] = job.HandleSpine;
            job.slotHandles[8] = job.HandleChest;
            job.slotHandles[9] = job.HandleNeck;
            job.slotHandles[10] = job.HandleHead;
            job.slotHandles[11] = job.HandleLeftShoulder;
            job.slotHandles[12] = job.HandleRightShoulder;
            job.slotHandles[13] = job.HandleLeftUpperArm;
            job.slotHandles[14] = job.HandleRightUpperArm;
            job.slotHandles[15] = job.HandleLeftLowerArm;
            job.slotHandles[16] = job.HandleRightLowerArm;
            job.slotHandles[17] = job.HandleLeftHand;
            job.slotHandles[18] = job.HandleRightHand;
            job.slotHandles[19] = job.HandleLeftToe;
            job.slotHandles[20] = job.HandleRightToe;
            job.slotHandles[BasisFullBodyData.UpperChestSlot] = job.HandleUpperChest;


            GenerateHeadToSpine(skeleton, ref job, ref data);
            job.spineMaxIterations = 20;
            job.spineTolerance = 0.001f;
            job.chestSpringState = new NativeArray<Vector3>(2, Allocator.Persistent);
            job.chestSpringInit = new NativeArray<int>(1, Allocator.Persistent);

            job.swingLastDir = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingLastAxis = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingLastTarget = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingContinuityInit = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingCollided = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingSmoothState = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingHintBend = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingHintAxis = new NativeArray<Vector3>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
            job.swingHintInit = new NativeArray<int>(BasisFullIKConstraintJob.k_SwingCount, Allocator.Persistent);
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
        public static void GenerateHeadToSpine(BasisPoseSkeleton skeleton, ref BasisFullIKConstraintJob job, ref BasisFullBodyData data)
        {
            var HeadToSpine = data.upperChest != null
                ? new Transform[] { data.head, data.neck, data.upperChest, data.chest, data.spine, data.hips }
                : new Transform[] { data.head, data.neck, data.chest, data.spine, data.hips };
            int SpineToHeadLength = HeadToSpine.Length;
            job.ChainHeadToSpine = new NativeArray<BasisBoneHandle>(SpineToHeadLength, Allocator.Persistent);
            BuildSpineAnatomy(HeadToSpine, ref job, ref data);

            for (int i = 0; i < SpineToHeadLength; i++)
            {
                job.ChainHeadToSpine[i] = skeleton.Bind(HeadToSpine[i]);
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
        static BasisBoneHandle BindHandle(BasisPoseSkeleton skeleton, Transform t) => (t != null) ? skeleton.Bind(t) : default;
        public static void Destroy(BasisFullIKConstraintJob job)
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
            if (job.swingHintBend.IsCreated) job.swingHintBend.Dispose();
            if (job.swingHintAxis.IsCreated) job.swingHintAxis.Dispose();
            if (job.swingHintInit.IsCreated) job.swingHintInit.Dispose();
            if (job.legSwivelRaw.IsCreated) job.legSwivelRaw.Dispose();
            if (job.legSwivelSmooth.IsCreated) job.legSwivelSmooth.Dispose();
            if (job.legSwivelInit.IsCreated) job.legSwivelInit.Dispose();
        }
    }
}
