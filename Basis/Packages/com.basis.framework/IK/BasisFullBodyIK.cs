using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    /// <summary>
    /// Full-body pass: Head + Legs + Hips + Dual Driven TR + Dual TwoBoneIK Hands (with chest/hand capsule & elbow protection).
    /// All driven via a single job.
    /// </summary>
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

        public Vector3 targetPositionHead, TargetChestPosition, TargetChestPositionRaw, playerUp, KneeBendPrefLeft, KneeBendPrefRight, KneeAnteriorRef,
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
hintRotationLeftLowerLeg, hintRotationRightLowerLeg,
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
elbowDragEnabled,
wristAxialBound,
collisionsEnabled;

        /// <summary>Corner frequency of the no-elbow-tracker pole drag, Hz. Lower = heavier drag (tau =
        /// 1/(2*pi*hz)). Only consulted on the model path — a real elbow tracker is the user's own input and
        /// is never lagged. See BasisElbowDragCore.</summary>
        public float elbowDragHz;

        /// <summary>Procedural toe articulation from BasisLocalFootDriver's surface probes. Degrees, positive =
        /// dorsiflexion; the axis is the world medio-lateral. Only consulted when the matching toe TRACKER is
        /// absent, so a tracked toe is never overridden. Zero = inert, which is the state on every path that does
        /// not run the foot driver (remote players, foot trackers, foot IK disabled).</summary>
        public float leftToeBendDeg, rightToeBendDeg;
        public Vector3 leftToeBendAxis, rightToeBendAxis;

        // Per-bone override slots, indexed identically to BasisFullIKConstraintJob.
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
        /// <summary>
        /// The avatar size ratio in force when the Tpose* scalars below were measured. They are baked from
        /// live bone positions ONCE per avatar load, but ApplyAvatarScale rescales the root without
        /// rebuilding the rig — so without this they carry the previous scale forever.
        /// </summary>
        public float TposeBakeScale;
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
        // True crouch depth (how far the head target sits below the avatar's standing head height) and the
        // standing head height itself, both world metres, packed per frame by BasisLocalRigDriver. The
        // sit-back cannot be derived from the head-hips separation inside the job: the lock-mode stage
        // restores that separation to rest length before this job's crouch stage would read it, which is
        // exactly how the old separation-driven signal died to a permanent zero.
        public float crouchDepth;
        public float standingHeadHeight;
        // Postural counterbalance gain: the fraction of the neck's forward travel the pelvis answers with as
        // the trunk folds. 0 disables it. See BasisTrunkCounterbalanceCore.
        public float trunkCounterbalance;
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

        /// <summary>Per-arm swivel the elbow protect chose last frame, degrees from the natural pole. Feeds
        /// BasisElbowProtectInput.PrevSwivelDeg, which is what lets the protect search the whole swivel
        /// circle without hopping between disconnected feasible arcs.</summary>
        public NativeArray<float> swingSwivelDeg;
        /// <summary>Which side of its circle the elbow anatomy guard chose last frame, per arm. The guard's
        /// branch cut is IRREDUCIBLE -- identity-on-legal plus enforcement forces either a jump or unbounded
        /// gain, whatever the inputs -- but the BUZZ is not: `s` at the top of the circle is noise, so the
        /// branch re-decided 92-110 times per 200 frames and dragged the elbow through 4-38 METRES of path
        /// for an input standing still. Hysteresis makes the flip points differ by direction of travel, so
        /// no azimuth re-decides: 0-1 flips. 0 = no history, which declines to the shipped behaviour.</summary>
        public NativeArray<int> swingGuardSide;
        // Limiter latch per slot: -1 while a collision pop is still easing in, else the last settled tag.
        public NativeArray<int> swingSmoothState;
        // Per-arm gain-cap state (BasisElbowSwingCapCore): last frame's capped bend + shoulder->hand axis,
        // and an init flag reset whenever the no-tracker model did not drive the elbow (so it re-seeds).
        public NativeArray<Vector3> swingHintBend;
        public NativeArray<Vector3> swingHintAxis;
        /// <summary>Last frame's reach (|hand - shoulder| / armLength) per arm -- the RADIAL half of the hand's
        /// motion, which swingHintAxis throws away when it normalises. Without it the swing cap's budget is
        /// structurally zero for a straight-line reach: a punch rotates the axis by exactly 0, so the cap
        /// froze the bend while the field genuinely moved. See BasisElbowSwingCapCore.</summary>
        public NativeArray<float> swingHintReach;
        /// <summary>Last DRAGGED pole per arm — the drag's own state, deliberately not the cap's. See SolveHand.</summary>
        public NativeArray<Vector3> swingHintDrag;
        /// <summary>Body (hips) rotation when swingHintDrag was stored, so a pure turn can be carried out of
        /// the drag instead of damped. See BasisElbowDragCore.</summary>
        public NativeArray<Quaternion> swingHintBodyRot;
        public NativeArray<int> swingHintInit;
        /// <summary>Last well-conditioned ELBOW TRACKER pole direction per arm, and the tracker rotation it
        /// was stored at. A measured pole collapses onto the shoulder->hand axis at full extension where a
        /// model pole does not, so past that point the swivel is carried from here through the tracker's own
        /// rotation instead of read off a noise-length vector. See BasisArmSolveCore's pole-anchor note.</summary>
        public NativeArray<Vector3> swingPoleAnchor;
        public NativeArray<Quaternion> swingPoleAnchorRot;
        public NativeArray<int> swingPoleAnchorInit;
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
        public NativeArray<BasisLegDiagnostics> legDiagnostics;
        /// <summary>Per-arm solved angular state, captured from the STREAM composition rather than the result
        /// struct. The solver publishes five twist diagnostics and recorded none of them, which is why three
        /// separate investigations this week aimed at the wrong joint before a test caught it.</summary>
        public NativeArray<BasisArmDiagnostics> armDiagnostics;
        public bool armDiagnosticsEnabled;
        public float ikLockMode;
        public bool shoulderSolveEnabled;
        public bool shoulderShrugEnabled;
        public bool shoulderRetractionEnabled;
        /// <summary>Scapulohumeral rhythm: clavicular elevation + retraction as a function of humeral
        /// elevation and plane of elevation. Ships FALSE, unlike Shrug and Retraction which ship on --
        /// CMU carries no clavicle motion at all (RightShoulder channel range is 0/0/0 in every clip) so
        /// the corpus cannot validate it, it is headset-unverified, and it perturbs the humeral twist
        /// guard's segmented firing-rate calibration by up to ~15.8 deg at the extremes. Re-run that audit
        /// with this ON before flipping the default. Inert and bit-identical while false.</summary>
        public bool shoulderRhythmEnabled;
        // T-pose baked reference data for shoulder solve
        /// <summary>Bind data for BasisArmSolveCore's humeral twist guard. RefAxis is baked per rig rather
        /// than hardcoded: a fixed world axis is parallel to the bone on some rigs, which would silently
        /// decline the guard instead of failing loudly.</summary>
        public Quaternion TposeLeftUpperArmRot, TposeRightUpperArmRot;
        /// <summary>Bind world rotation of the LOWER ARM. Defines zero pronation as the rig's own
        /// forearm-vs-humerus relationship, so the forearm's axial roll stops being inherited 1:1 from
        /// whichever idle clip happens to be playing. See BasisArmSolveCore's forearm-roll note.</summary>
        public Quaternion TposeLeftLowerArmRot, TposeRightLowerArmRot;
        public Quaternion TposeLeftHandRot, TposeRightHandRot;
        public Vector3 TposeLeftHumerusDir, TposeRightHumerusDir;
        public Vector3 TposeLeftHumerusRefAxis, TposeRightHumerusRefAxis;
        public Vector3 TposeLeftShoulderLocalDir, TposeRightShoulderLocalDir;
        public Quaternion TposeLeftShoulderRot, TposeRightShoulderRot;
        public Quaternion TposeChestRot;
        /// <summary>ROOT-RELATIVE bind of the same bone TposeChestRot is baked from, so BasisShoulderSolveCore
        /// can build its girdle frame anatomically instead of reading the chest BONE's local axes as
        /// lateral/up/forward. Derived from TposeChestRot so it can never drift to a different bone than the
        /// live rotation. Root-relative and not the raw world bind: a world bind leaves the anatomical axes
        /// tilted by whatever yaw the AnimatorRoot happened to have at capture -- the same trap
        /// BasisCalibrationDebugCsvWindow already warns about for the head offset. Zero declines.</summary>
        public Quaternion TposeChestBind;
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
            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, KneeBendPrefLeft, hintIsTrackerLeftLowerLeg, footIsTrackerLeftLeg, 0);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, KneeBendPrefRight, hintIsTrackerRightLowerLeg, footIsTrackerRightLeg, 1);

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
                ApplySwingContinuity(stream, k_SwingLeftElbow, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, swingRate, swingDt, bodyRight);
            }

            if (enabledRightHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingRightElbow, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, swingRate, swingDt, bodyRight);
            }

            // 4b) Arm twist distribution: spread wrist/elbow roll along the optional twist bones
            // so the mesh doesn't pinch at the wrist when the hand rotates.
            float lowerTwist = lowerArmTwistFraction;
            float upperTwist = upperArmTwistFraction;
            SolveArmTwist(stream, HandleLeftLowerArm, HandleLeftHand, HandleLeftLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleRightLowerArm, HandleRightHand, HandleRightLowerArmTwist, lowerTwist);
            SolveArmTwist(stream, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftUpperArmTwist, upperTwist);
            SolveArmTwist(stream, HandleRightUpperArm, HandleRightLowerArm, HandleRightUpperArmTwist, upperTwist);

            // 5) Toes. A toe TRACKER wins outright; otherwise the procedural surface bend from the foot driver
            // articulates the toe over stair noses, kerbs and ramps.
            if (leftToeEnabled) ApplyRotation(stream, true, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            else ApplyToeSurfaceBend(stream, HandleLeftToe, leftToeBendDeg, leftToeBendAxis);

            if (RightToeEnabled) ApplyRotation(stream, true, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);
            else ApplyToeSurfaceBend(stream, HandleRightToe, rightToeBendDeg, rightToeBendAxis);

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
                            Vector3 spineDir = spineLen > k_Epsilon ? headToHips / spineLen : hipsTargetRot * Vector3.down;
                            hipsTargetPos = headTargetPos + spineDir * restDist;
                        }
                    }
                    break;

                default: // LockBoth (2) - original behavior: clamp hips relative to head
                    hipsTargetPos = AntiContortionist(headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, restDist);
                    hipsTargetPos = MitigateSpineBuckling(headTargetPos, hipsTargetRot, hipsTargetPos, restDist, up);
                    float MaxBendDeg = maxBendDeg;
                    hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                    hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor, maxFactor, up);
                    break;
            }

            // The gaze-invariant trunk cue, shared by everything below that reads torso POSTURE. The HMD sits
            // forward of the neck pivot, so a pure look-down swings headTargetPos forward and any consumer
            // that mistakes it for the torso reads a lean that never happened. DistributeSpineBend was fixed
            // to use this cue; the pelvis stages below were still on the raw head.
            Vector3 neckCue = ComputeNeckCue(headTargetPos);

            // Postural counterbalance: the pelvis travels BACK as the trunk folds forward, so the fold happens
            // at the hip instead of driving the torso down into itself. Runs before the crouch sit-back and
            // reports how much of the pose is a forward fold, so the crouch term -- which is driven by head
            // HEIGHT and therefore cannot tell a squat from a waist-bend -- is faded out by the complement
            // rather than stacking on top. Gated on the HIPS tracker alone (deliberately narrower than the
            // crouch gate): a chest tracker measures lean, but the pelvis POSITION is still synthesised here.
            float crouchFade = 1f;
            if (!hasHipsTracker)
            {
                hipsTargetPos = ApplyTrunkCounterbalance(neckCue, hipsTargetPos, up, out float flexionFrac);
                crouchFade = 1f - flexionFrac;
            }
            hipsTargetPos = ApplyCrouchBodyOffset(stream, headTargetPos, hipsTargetPos, hipDesired, up, crouchFade);
            targetPositionHips = hipsTargetPos;

            // The hinge SYNTHESISES an anterior pelvis pitch on a deep lean so the spine does not swallow the
            // whole reach -- but only when there is no hip tracker. With one, the pelvis rotation is the
            // user's OWN, measured, and must feed straight to IK "how we used to" (the hip-tilt-stabilization
            // that reshaped a tracked pelvis was built and deliberately removed for exactly this reason). The
            // hip-bob/sway synthesis in BasisLocalRigDriver is gated on the same flag, for the same reason:
            // do not invent pelvis motion on top of a tracker.
            if (!hasHipsTracker)
            {
                hipDesired = ApplyHipHinge(stream, neckCue, hipsTargetPos, hipDesired, up);
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

            // ==========================================================================================
            // THE TAUT BAND. Standing upright, the virtual spine places the hips a full chain length
            // below the head, so the CCD runs AT the chain's full-extension singularity — and the
            // mm-scale distance between target and full extension flickers across zero with tracker
            // noise. BOTH sides of that point stall the loop for all 20 iterations, chasing a point the
            // chain cannot land on, and every futile sweep re-aims into the frame's noise:
            //   • target INSIDE reach: a straight chain cannot shorten by aiming; only a bow can, the
            //     required bow angle goes as sqrt(compression), and nothing constrains its plane — the
            //     noise picks it, a different plane every frame.
            //   • target BEYOND reach: the chain pulls straight and the tolerance can never be met, so
            //     the sweeps churn on, tilting the whole chain into the noise azimuth of the frame.
            // Measured: a sustained 0.25-0.39 deg/frame neck/chest buzz on either side of the band,
            // 0.000-0.003 outside it with identical noise — the "head jitters when I stand looking
            // almost straight ahead" report. Regularize the commanded DISTANCE, keep the direction:
            // compression is softened through a C1 hinge, so noise-scale compressions leave the chain
            // taut (where the shipped solve already stalled — measured tip error unchanged to 0.1 mm)
            // while real compressions pass through with an error that decays as band^2/compression; a
            // beyond-reach target is brought onto the reach sphere, which is exactly the pose the stall
            // was already converging to (tip error unchanged), minus the churn. The band scales with
            // the avatar's own chain. Gated by BasisSpineTautBandTests.
            // ==========================================================================================
            {
                Vector3 rootPos = ChainHeadToSpine[chainLen - 1].GetPosition(stream);
                float chainReach = 0f;
                for (int i = 0; i < chainLen - 1; i++)
                {
                    chainReach += (ChainHeadToSpine[i].GetPosition(stream) - ChainHeadToSpine[i + 1].GetPosition(stream)).magnitude;
                }
                Vector3 rootToTarget = headTargetPos - rootPos;
                float targetDist = rootToTarget.magnitude;
                if (targetDist > k_Epsilon && chainReach > k_Epsilon)
                {
                    float compression = chainReach - targetDist;
                    float commandedDist;
                    if (compression > 0f)
                    {
                        float band = k_SpineTautBandFrac * chainReach;
                        commandedDist = chainReach - compression * compression * compression / (compression * compression + band * band);
                    }
                    else
                    {
                        commandedDist = chainReach;
                    }
                    headTargetPos = rootPos + rootToTarget * (commandedDist / targetDist);
                }
            }

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

            ReassertTrackedChest(stream, headTargetPos, firstJoint, chainLen, jointSpan,
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
        // ==============================================================================================
        // ⭐ THE TRACKED CHEST IS A MEASUREMENT, AND THE HEAD CCD ABOVE JUST OVERWROTE IT.
        //
        // SolveSpine writes the tracker's chest rotation ONCE, before the solve. Everything after it --
        // DistributeSpineBend (which writes the Spine, the chest's PARENT), BiasSpineTowardChest (Spine
        // again) and above all the CCD (which rotates the chest DIRECTLY at chainLen-3, and the Spine
        // under it) -- is chasing the HEAD and has no term for the chest at all. ClampChestCone bounds
        // the chest against its PARENT, never against the tracker, so nothing pulls it back.
        //
        // Measured with a chest tracker and the hips pinned, moving ONLY the head: the chest follows the
        // gaze at 0.402 deg per deg, so a 45 deg look-down swings a chest that has not moved by 17.55 deg
        // mean / 29.86 p95, and the CCD contributes 15.5 of that 17.55. A real human's chest pitches
        // -0.05 deg/deg, i.e. not at all. That is "the head drags the chest around".
        //
        // ⚠️ IT IS NOT A CUMULATIVE-OVERWRITE PROBLEM, WHICH IS WHY THE OBVIOUS FIXES DO NOTHING.
        // Deleting BiasSpineTowardChest changes the final chest error by 0.01 deg -- the CCD re-converges
        // to the same place regardless. Turning chestIkTarget ON fixes chest POSITION (3.23 -> 0.29 cm)
        // and not ROTATION (11.38 -> 11.51), because it is a position pull and this is a rotation
        // complaint. Turning MaxChestDeltaProperty DOWN goes backwards (11.38 -> 16.90 deg): it
        // constrains the chest against its parent, so the Spine simply moves instead and carries the
        // chest with it. Only re-asserting the measurement after the solve addresses it.
        //
        // Re-clamped against the POST-solve neck and spine, not the pre-solve ones, because that is the
        // pose the bound is actually protecting against. Then the head is restored with the joints ABOVE
        // the chest only -- upperChest and neck, never the chest itself or the Spine beneath it -- the
        // same redundancy trick SolveChestTarget uses, so the head returns to the HMD without disturbing
        // the chest that was just pinned.
        //
        // ⭐⭐ THE CHEST GETS EXACTLY THE AUTHORITY THE HEAD CAN AFFORD, AND NOT ONE DEGREE MORE.
        //
        // The first version of this pinned the chest to the tracker OUTRIGHT. It measured beautifully --
        // chest error 11.68 -> 1.45 deg, gaze drag 0.402 -> 0.008 -- and it was WRONG IN A HEADSET:
        // "chest is now able to be rotated in a way that pulls it off the head". Neither number could
        // see that, because both ask "is the chest where the tracker says" and neither asks "is the body
        // still attached to the head". The MaxChestDeltaProperty pair is no protection either: it ships
        // at 90 deg, so on a drifting or mis-calibrated tracker it is not a bound at all.
        //
        // The rule instead: walk the chest toward the tracker only as far as the joints above it can
        // still put the head back on the HMD. Bisect the blend, keep the largest weight whose head
        // residual is inside spineTolerance, and if even a tiny weight loses the head, keep the pose the
        // CCD already produced. The head is never traded -- the chest spends whatever is left over. The
        // barrier is not a tuned angle; it is wherever the neck and upperChest actually run out, which
        // moves with the pose, the avatar and the ROM envelope, exactly as it should.
        //
        // ⭐ The full-authority case costs nothing extra: probe 0 tries weight 1 and returns immediately
        // when the head survives it, so a well-calibrated tracker in an ordinary pose pays for one pass.
        // Only a chest the head cannot afford pays for the bisection.
        //
        // ⚠️ GuardSpineJoint IS applied here, and the earlier reasoning for skipping it was wrong. The
        // guard's contract says "the head and the hips: commanded, not solved. Never guarded", and a
        // tracked chest reads like that category -- but the chest is not an END of the chain, it is in
        // the middle of it, and an unguarded middle joint is precisely what lets the torso leave the
        // head. Skipping it measured better (1.45 vs 5.17 deg) and felt worse, which is the whole
        // lesson of this block.
        // ==============================================================================================
        const int k_ChestReassertHeadRestoreSweeps = 2;
        const int k_ChestReassertBarrierProbes = 5;
        const float k_ChestReassertMaxHeadErr = 0.010f;
        void ReassertTrackedChest(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint,
            int chainLen, float jointSpan, float cervicalTwistKeep, float lumbarTwistKeep, Vector3 ccdUp,
            float ccdRelax, float neckCone, float chestCone)
        {
            if (!HasChestTracker || !HandleChest.IsValid(stream))
                return;

            int chestBoneIdx = chainLen - 3;
            if (chestBoneIdx <= firstJoint || chestBoneIdx >= chainLen)
                return;

            Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;
            Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;
            float maxDelta = MaxChestDeltaProperty;

            Quaternion solvedChestRot = HandleChest.GetRotation(stream);
            Quaternion chestDesired = targetChestRotation * targetOffsetChest;
            Quaternion clampedChestRot = ClampRotation(chestDesired, neckRot, maxDelta);
            clampedChestRot = ClampRotation(clampedChestRot, spineRot, maxDelta);

            // ⚠️⚠️ NO PER-JOINT SNAPSHOT, AND THAT IS DELIBERATE. The obvious way to bisect is to save the
            // chest and the joints above it and restore them between probes -- but the only place to put
            // that buffer is a NativeArray allocated next to ChainHeadToSpine, and EVERY test and probe
            // in this repo assigns ChainHeadToSpine through an object initialiser instead
            // (BasisTrackerConfigMatrixTests, BasisSpineCorpusAccuracyTests, BasisSpineTautBandTests).
            // A guard on IsCreated therefore makes the whole stage a SILENT NO-OP under test while
            // reading as a fix -- the exact trap GuardSpineJoint fell into with ChainSpineRestFrames.
            // Measured: it returned bit-identical-to-shipped numbers at every head budget.
            //
            // It is not needed. The chest is re-set ABSOLUTELY from solvedChestRot each probe, so it
            // cannot accumulate; and the joints above it do not need restoring because ReachHeadJoint is
            // contractive toward the head -- whatever pose a rejected probe left them in, the next
            // probe's sweeps re-aim them at the same target.
            //
            // ⚠️ THE BUDGET IS ABSOLUTE, AND BOTH RELATIVE FORMULATIONS FAIL. Against spineTolerance
            // (1 mm) it rejects everything, because the CCD itself only reaches ~6.7 mm. Against "no
            // worse than the CCD" it also rejects everything, because the CCD has just converged and its
            // pose is a local optimum for the head, so ANY chest perturbation degrades it monotonically.
            // So: a distance the head may end up from the HMD -- or the CCD's own residual, whichever is
            // larger, so a pose the CCD could not solve is never made the chest's fault.
            float baseHeadErrSqr = (headTargetPos - ChainHeadToSpine[0].GetPosition(stream)).sqrMagnitude;
            float headTolSqr = Mathf.Max(k_ChestReassertMaxHeadErr * k_ChestReassertMaxHeadErr, baseHeadErrSqr);
            float accepted = 0f;
            float lo = 0f, hi = 1f;

            for (int probe = 0; probe < k_ChestReassertBarrierProbes; probe++)
            {
                float t = probe == 0 ? 1f : 0.5f * (lo + hi);

                HandleChest.SetRotation(stream, Quaternion.Slerp(solvedChestRot, clampedChestRot, t));
                for (int sweep = 0; sweep < k_ChestReassertHeadRestoreSweeps; sweep++)
                {
                    for (int i = chestBoneIdx - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }

                bool headHeld = (headTargetPos - ChainHeadToSpine[0].GetPosition(stream)).sqrMagnitude <= headTolSqr;
                if (headHeld)
                {
                    accepted = t;
                    lo = t;
                    if (probe == 0)
                        return;   // the tracker cost the head nothing: the pose already standing is the answer
                }
                else
                {
                    hi = t;
                }
            }

            {
                HandleChest.SetRotation(stream, Quaternion.Slerp(solvedChestRot, clampedChestRot, accepted));
                for (int sweep = 0; sweep < k_ChestReassertHeadRestoreSweeps; sweep++)
                {
                    for (int i = chestBoneIdx - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chainLen, jointSpan,
                            cervicalTwistKeep, lumbarTwistKeep, ccdUp, ccdRelax, neckCone, chestCone);
                    }
                }
            }
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
            //
            // Also gated on a REAL chest tracker. Without one, TargetChestPositionRaw is NOT a measurement --
            // it is the virtual spine's OWN chest (lerp(neck, hips), routed back out through the rig driver),
            // so pinning the bone to it adds motion without adding truth: in half-body the chest tracked the
            // head instead of sitting as the stable FK consequence of the head solve, and moved far too much.
            // The measured-chest win is real and stays on for tracker users; a synthesized "target" is not a
            // target. (The head CCD's drag on a TRACKED chest is a separate concern, owned by ReassertTrackedChest.)
            if (!chestIkTarget || !HasChestTracker)
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
        // Width of the spine CCD's taut band as a fraction of the hips->head chain length (~11 mm on a
        // 1.7 m avatar). Must comfortably exceed the compressions an upright head commands through the
        // neck-pivot lever (quadratic in pitch: ~1.4 mm at 8 deg, ~5.6 mm at 20 deg) — those are the
        // noise-scale demands that sat the solver on its full-extension singularity. See SolveSequentialSpineIK.
        const float k_SpineTautBandFrac = 0.015f;
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
        // The neck, estimated RIGIDLY off the head target, and therefore EXACTLY invariant to a gaze: if the
        // head orbits the neck by Q then Q's two lever arms cancel algebraically (written out in full inside
        // DistributeSpineBend). Every consumer that wants to know where the TORSO is must read this and not
        // headTargetPos -- the HMD sits forward of the neck pivot, so the raw head target reports a lean the
        // moment you look down. Shared by the spine bend, the postural counterbalance and the hip hinge so
        // the three cannot drift apart.
        Vector3 ComputeNeckCue(Vector3 headTargetPos)
        {
            return headTargetPos + (targetRotationHead * targetOffsetHead) * TposeHeadToNeckLocal;
        }
        // Wrapper for BasisTrunkCounterbalanceCore: the pelvis travels back as the trunk folds forward, so the
        // bend happens at the hip instead of the torso folding down into itself. The cap scales with the
        // avatar's own spine (MinHeadSpineHeight is the T-pose hips->head chain), so it is avatar-relative
        // rather than a fixed number of metres. Gating (no hip tracker) is the caller's, as with ApplyHipHinge.
        Vector3 ApplyTrunkCounterbalance(Vector3 neckCue, Vector3 hipsPos, Vector3 playerUp, out float flexionFrac)
        {
            BasisTrunkCounterbalanceInput input;
            input.HipsPos = hipsPos;
            input.NeckCue = neckCue;
            input.PlayerUp = playerUp;
            input.Gain = trunkCounterbalance;
            input.MaxShift = k_TrunkCounterbalanceMaxSpineFrac * MinHeadSpineHeight;
            BasisTrunkCounterbalanceCore.Solve(input, out BasisTrunkCounterbalanceResult result);
            flexionFrac = result.FlexionFrac;
            return result.HipsPos;
        }
        // Ceiling on the posterior pelvic shift, as a fraction of T-pose spine length: ~25 cm on a 0.55 m
        // spine, the top of the measured range for a real full forward bend. Eased into, never a step.
        const float k_TrunkCounterbalanceMaxSpineFrac = 0.45f;
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
            Vector3 neckCue = ComputeNeckCue(headTargetPos);

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
        // `fade` is 1 - sin(trunk flexion) from the postural counterbalance. This term reads head HEIGHT, so
        // it cannot tell a squat from a waist-fold and would double-count the pelvis travel the counterbalance
        // has already applied; fading it out as the trunk folds lets each own the posture it describes -- the
        // crouch sit-back for a squat with an upright trunk, the counterbalance for a bend.
        Vector3 ApplyCrouchBodyOffset(BasisPoseStream stream, Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir, float fade)
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
            input.CrouchDepth = crouchDepth;
            input.StandingHeadHeight = standingHeadHeight;
            input.Fade = fade;
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        // Extra forward neck curve at FULL look-down when NeckGazeFollow = 1 (it scales this by the setting
        // and by how far down you look). Modest: the head is re-pinned so this only arcs the neck, but too
        // much cocks the head relative to the neck. The user dials the setting; this is the ceiling.
        const float k_NeckGazeFollowMaxDeg = 18f;
        public void ApplyCervicalLordosis(BasisPoseStream stream)
        {
            if (!HandleNeck.IsValid(stream))
            {
                return;
            }

            Vector3 referenceUp;
            if (HandleChest.IsValid(stream))
            {
                Vector3 chestToNeck = HandleNeck.GetPosition(stream) - HandleChest.GetPosition(stream);
                referenceUp = chestToNeck.sqrMagnitude > k_SqrEpsilon
                    ? chestToNeck.normalized
                    : HandleChest.GetRotation(stream) * Vector3.up;
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
                // The head pin must not depend on WHICH side of the early-out threshold this frame
                // landed on. lordosisDeg crosses the 0.01 cutoff constantly at level gaze when BaseDeg
                // is ~0, and gating the pin on the pitch clamp meant the head POSITION toggled between
                // "pinned to the target" and "CCD FK" with it -- a sub-mm head pop exactly at the most
                // common head pose. Pin unconditionally: on a no-op frame HeadRotClamped IS the raw gaze
                // (same value the spine solve pinned), so the rotation write changes nothing.
                if (HandleHead.IsValid(stream))
                {
                    HandleHead.SetPosition(stream, targetPositionHead);
                    HandleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
                }
                return;
            }

            Vector3 shoulderRight = (HandleLeftUpperArm.IsValid(stream) && HandleRightUpperArm.IsValid(stream))
                ? HandleRightUpperArm.GetPosition(stream) - HandleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            bool hasShoulderRight = shoulderRight.sqrMagnitude > k_SqrEpsilon;
            if (hasShoulderRight)
            {
                shoulderRight.Normalize();
            }

            BasisBoneHandle bendHandle = input.HasUpperChest ? HandleUpperChest : HandleChest;
            if (bendHandle.IsValid(stream) && result.BhDeg != 0f)
            {
                Quaternion bhRot = bendHandle.GetRotation(stream);
                Vector3 bhAxis = hasShoulderRight ? shoulderRight : bhRot * Vector3.right;
                bendHandle.SetRotation(stream, Quaternion.AngleAxis(result.BhDeg, bhAxis) * bhRot);
            }

            if (result.HasExtreme)
            {
                Quaternion refRot = HandleHips.IsValid(stream)
                    ? HandleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips)
                    : (HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream) : Quaternion.identity);
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
                Vector3 neckAxis = hasShoulderRight ? shoulderRight : neckRotCurrent * Vector3.right;
                HandleNeck.SetRotation(stream, Quaternion.AngleAxis(totalNeckDeg, neckAxis) * neckRotCurrent);
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

            // ==========================================================================================
            // ⚠️ BIND-CANCELLED HIPS FRAME (hipsRot * inv(bind)), NOT THE RAW HIPS BONE. This was the last
            // stage in this file still measuring and applying about the bone's own axes -- DistributeSpineBend,
            // ApplyArmSwingChestFollow and ApplyCervicalLordosis were all fixed for exactly this and it was
            // missed. Measured against the live BasisTwistSolveCore on an X-90 (Blender bone-Y-up) hips bind:
            // a real 60 deg chest TWIST reported 0.0 deg and a real 60 deg lateral LEAN reported -60.0 deg,
            // with the counter-yaw then applied about the body's fore-aft axis -- i.e. as a shoulder ROLL,
            // one shoulder up and one down, on a user who merely leaned. No-op at an identity bind.
            // ==========================================================================================
            Quaternion hipsRot = HandleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips);
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
            // ==========================================================================================
            // ⭐ THE CLAVICLE'S PARENT IS THE UPPERCHEST, NOT THE CHEST -- AND THIS WRITES A WORLD ROTATION.
            //
            // BasisShoulderSolveCore builds `ChestRot * girdle * shoulderRestLocal` and the result is
            // applied with SetRotation, i.e. SetWorldRotation: the parent chain is DISCARDED and the
            // clavicle is pinned outright to whatever frame is handed in here. So handing it the Chest
            // while the bone actually hangs off the UpperChest leaves an error equal to the
            // UpperChest-vs-Chest delta from bind -- and by the time this runs, SolveSpine has already
            // written the UpperChest TWICE, independently of the Chest: DistributeSpineBend (pitch 0.25 /
            // roll 0.20, plus 0.75x of the routed axial twist when anatPelvicTwistRouting is on, which is
            // the default) and ApplyArmSwingChestFollow (0.4 of a delta capped at 15 deg/axis).
            //
            // Measured from the shipped constants: ~11 deg on a 45 deg forward fold, +6 deg from the arm
            // swing, and up to 0.75x the torso twist -- about 30 deg on a 40 deg rotation.
            //
            // ⭐⭐ AND IT PUT THE SHOULDER SOLVE AND THE ELBOW MODEL IN DIFFERENT FRAMES. BuildArmFrame,
            // which feeds BasisElbowFieldModel and the anatomy guard's TorsoUp, is built from POSITIONS
            // including the neck, so it follows the UpperChest correctly. The two disagreed about where
            // the torso was on every frame the user twisted, which is exactly when the arm root is most
            // wrong. TposeChestRot is baked from the same bone (see the bake site) so the delta stays a
            // pure since-bind rotation; taking the live rotation from one bone and the bind from another
            // would be worse than either choice alone.
            // ==========================================================================================
            input.ChestRot = HandleUpperChest.IsValid(stream) ? HandleUpperChest.GetRotation(stream)
                           : HandleChest.IsValid(stream) ? HandleChest.GetRotation(stream)
                           : Quaternion.identity;
            input.TposeChestRot = tposeChestRot;
            input.ChestBind = TposeChestBind;
            input.TposeShoulderRot = tposeShoulderRot;
            input.TposeArmDirWorld = tposeArmDir;
            input.TposeArmLength = tposeArmLength;
            input.TposeClavicleLength = tposeClavicleLen;
            input.TposeElbowLength = tposeElbowLen;
            input.ShrugEnabled = shoulderShrugEnabled;
            input.RetractEnabled = shoulderRetractionEnabled;
            input.RhythmEnabled = shoulderRhythmEnabled;
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
        /// <summary>
        /// Bind inputs for the humeral twist guard. The reference axis is chosen PER RIG, perpendicular to
        /// the bone in its own local frame: a hardcoded world axis lands parallel to the humerus on rigs
        /// whose arm points down its local -Y, and the guard would then decline silently rather than fail
        /// loudly. Zero outputs mean decline, which keeps every existing caller bit-identical.
        /// </summary>
        static void BakeHumerusTwistBind(Transform upperArm, Transform lowerArm,
            out Quaternion bindRot, out Vector3 bindDir, out Vector3 refAxis)
        {
            bindRot = Quaternion.identity;
            bindDir = Vector3.zero;
            refAxis = Vector3.zero;
            if (upperArm == null || lowerArm == null)
            {
                return;
            }

            Vector3 dir = lowerArm.position - upperArm.position;
            if (dir.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            bindRot = upperArm.rotation;
            bindDir = dir.normalized;

            Vector3 localBone = Quaternion.Inverse(bindRot) * bindDir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            refAxis = perp.sqrMagnitude > k_SqrEpsilon ? perp.normalized : Vector3.zero;
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
        /// <summary>
        /// Bend the toe about a world medio-lateral axis, on top of wherever the pose already has it.
        ///
        /// Deliberately RELATIVE, not an absolute target. Reading the toe's current world rotation and adding a
        /// delta is identity at zero bend BY CONSTRUCTION, so this cannot come out toes-up the way an absolute
        /// LookRotation-style target did -- that bug cost a whole investigation, and the foot's footAlign
        /// rest-basis map exists solely to undo it. It also needs no calibration offset (offsetRotationLeftToe is
        /// only meaningful when a toe CONTROL was actually calibrated, which it is not on the procedural path)
        /// and no assumption about the rig's toe bone axes.
        ///
        /// Runs after SolveLegs, so the toe's composed world rotation already carries the solved foot.
        /// Sign: positive bendDeg is DORSIFLEXION (toes up). A positive AngleAxis about world-right pitches
        /// forward toward down in Unity's left-handed frame, hence the negation.
        /// </summary>
        public void ApplyToeSurfaceBend(BasisPoseStream stream, BasisBoneHandle handle, float bendDeg, Vector3 axis)
        {
            if (!handle.IsValid(stream)) return;
            if (Mathf.Abs(bendDeg) < 0.01f || axis.sqrMagnitude < 1e-6f) return;

            Quaternion current = handle.GetRotation(stream);
            handle.SetRotation(stream, Quaternion.AngleAxis(-bendDeg, axis.normalized) * current);
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
        public void SolveTwoBoneIKArms(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, BasisAffineTransform hint, bool hintWeight, bool hintIsTracker, Quaternion targetOffset, int swingSlot = -1, Vector3 bodyRight = default)
        {
            // Geometry lives in BasisArmSolveCore so the offline sweep harness solves the
            // exact same elbow math. The core returns incremental deltas; apply them through
            // the stream in the original order (identity steps are exact no-ops).
            BasisArmSolveInput input = default;
            root.GetPositionAndRotation(stream, out Vector3 shoulderPos, out Quaternion shoulderRot);
            mid.GetPositionAndRotation(stream, out Vector3 elbowPos, out Quaternion elbowRot);
            tip.GetPositionAndRotation(stream, out Vector3 handPos, out Quaternion handRot);
            input.Shoulder = shoulderPos;
            input.Elbow = elbowPos;
            input.Hand = handPos;
            input.RootRotation = shoulderRot;
            input.MidRotation = elbowRot;
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint.translation;
            input.HintWeight = hintWeight;
            input.TargetOffset = targetOffset;
            input.PlayerUp = playerUp;
            // The anatomy guard's ceiling is TORSO-relative (see BasisElbowAnatomyCore's frame note), so it
            // needs the chest->neck up, not the root's. Same BuildFrame the elbow model already runs on --
            // the house body frame, from bone POSITIONS -- so the guard and the hint cannot disagree about
            // which way is up. Left at zero on a degenerate rig; BasisArmSolveCore then falls back to PlayerUp.
            BasisSwivelFrame torsoFrame = BuildArmFrame(stream);
            if (torsoFrame.Valid)
            {
                input.TorsoUp = torsoFrame.Up;
            }
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
            input.TipRotation = handRot;
            // A real tracker's measured lower-arm rotation feeds the forearm roll; zero keeps it off for
            // the model path, whose hint rotation is just the stale property value.
            input.HintRotation = hintIsTracker ? hint.rotation : default;

            // Humeral twist guard bind + live clavicle. Handedness from the swing slot, exactly as SolveHand
            // derives it. Anything unavailable is left at zero, which declines the guard.
            if (swingSlot == k_SwingLeftElbow || swingSlot == k_SwingRightElbow)
            {
                bool twistIsLeft = swingSlot == k_SwingLeftElbow;
                // Lateral OUT seeds the cold start; the previous frame's side is what actually kills the buzz.
                input.ElbowLateralOut = twistIsLeft ? -bodyRight : bodyRight;
                if (swingGuardSide.IsCreated) input.PrevGuardSide = swingGuardSide[swingSlot];
                input.BindLowerArmRotation = twistIsLeft ? TposeLeftLowerArmRot : TposeRightLowerArmRot;
                input.BindHandRotation = twistIsLeft ? TposeLeftHandRot : TposeRightHandRot;
                input.ApplyWristAxialBound = wristAxialBound;
                BasisBoneHandle clavicle = twistIsLeft ? HandleLeftShoulder : HandleRightShoulder;
                if (clavicle.IsValid(stream))
                {
                    input.ClavicleRotation = clavicle.GetRotation(stream);
                    input.BindClavicleRotation = twistIsLeft ? TposeLeftShoulderRot : TposeRightShoulderRot;
                    input.BindHumerusRotation = twistIsLeft ? TposeLeftUpperArmRot : TposeRightUpperArmRot;
                    input.BindHumerusDir = twistIsLeft ? TposeLeftHumerusDir : TposeRightHumerusDir;
                    input.BindHumerusRefAxis = twistIsLeft ? TposeLeftHumerusRefAxis : TposeRightHumerusRefAxis;
                }
            }

            bool anchorSlot = hintIsTracker && (uint)swingSlot < (uint)k_SwingCount
                              && swingPoleAnchor.IsCreated && swingPoleAnchorRot.IsCreated && swingPoleAnchorInit.IsCreated;
            if (anchorSlot && swingPoleAnchorInit[swingSlot] != 0)
            {
                input.PrevPoleDir = swingPoleAnchor[swingSlot];
                input.PrevHintRotation = swingPoleAnchorRot[swingSlot];
                input.HasPrevPole = true;
            }

            BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

            if (swingGuardSide.IsCreated && (uint)swingSlot < (uint)k_SwingCount)
            {
                swingGuardSide[swingSlot] = result.GuardSideUsed;
            }

            if (anchorSlot)
            {
                if (result.PoleAnchorValid)
                {
                    swingPoleAnchor[swingSlot] = result.PoleDirUsed;
                    swingPoleAnchorRot[swingSlot] = result.PoleRotUsed;
                    swingPoleAnchorInit[swingSlot] = 1;
                }
            }
            else if ((uint)swingSlot < (uint)k_SwingCount && swingPoleAnchorInit.IsCreated)
            {
                swingPoleAnchorInit[swingSlot] = 0;
            }

            if (armDiagnosticsEnabled && armDiagnostics.IsCreated
                && (swingSlot == k_SwingLeftElbow || swingSlot == k_SwingRightElbow))
            {
                BasisArmDiagnosticsCore.Capture(input, result,
                    swingSlot == k_SwingLeftElbow ? -1f : 1f,
                    out BasisArmDiagnostics diag);
                armDiagnostics[swingSlot] = diag;
            }

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
        // ==============================================================================================
        // ⚠️ BasisArmSolveCore's anatomy guard says "there is no path by which the arm can end a frame
        // outside the envelope, because this is the end of the frame." IT IS NOT THE END OF THE FRAME.
        // The elbow protect and the swing limiter both re-swivel the elbow about the SAME shoulder->hand
        // axis afterwards, and the protect's objective is clearance minus a swing preference minus a
        // temporal anchor -- there is NO anatomy term in it at all, and its flip-commit can drive the
        // elbow to outDir outright.
        //
        // Measured as the illegal fraction of the elbow's full circle, using the live GuardSwivelRad:
        // cross-body at chest height 150.5 deg of 360 (41.8%); hand at the opposite shoulder 133.5 deg
        // (37.1%), where a representative outDir is ITSELF illegal; hand at chin 98.5 deg (27.4%). Those
        // are exactly the poses the torso collider is active for, so "the elbow points at the sky"
        // survived precisely where the guard was supposed to own it.
        //
        // Re-running the guard costs nothing when the pose is already legal: GuardSwivelRad returns an
        // exact 0f inside the envelope and this early-outs. It is a swivel about shoulder->hand, and the
        // hand LIES on that axis, so it cannot move the hand the protect just preserved.
        // ==============================================================================================
        // ⚠️ THIS MUST THREAD THE SAME HYSTERESIS STATE AS THE MAIN SOLVE. It is a SECOND call into the
        // anatomy guard, after the elbow protect has moved the elbow, so it decides the branch again --
        // and on the declining 5-arg overload it decides it from `sign(s)`, which at the top of the elbow's
        // circle is NOISE. That is the buzz: measured 92-110 re-decisions per 200 frames, dragging the
        // elbow through 4-38 METRES of path for an input standing still, and a full 180 deg flip when it
        // crosses. Sharing swingGuardSide with the main solve means both calls agree on a side and neither
        // re-decides on noise.
        void ReGuardElbowAnatomy(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int swingSlot, Vector3 bodyRight)
        {
            if (!root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            Vector3 a = root.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);
            float totalLen = (b - a).magnitude + (c - b).magnitude;
            if (totalLen <= k_Epsilon)
            {
                return;
            }

            BasisSwivelFrame torsoFrame = BuildArmFrame(stream);
            Vector3 guardUp = torsoFrame.Valid ? torsoFrame.Up : playerUp;
            bool sideSlot = (uint)swingSlot < (uint)k_SwingCount && swingGuardSide.IsCreated;
            Vector3 lateralOut = swingSlot == k_SwingLeftElbow ? -bodyRight : bodyRight;
            int prevSide = sideSlot ? swingGuardSide[swingSlot] : 0;
            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(a, b, c, guardUp, totalLen,
                lateralOut, prevSide, out int sideUsed);
            if (sideSlot && sideUsed != 0)
            {
                swingGuardSide[swingSlot] = sideUsed;
            }
            if (guardSwivel == 0f)
            {
                return;
            }

            Vector3 ac = c - a;
            if (ac.sqrMagnitude <= k_SqrEpsilon)
            {
                return;
            }

            Quaternion guard = Quaternion.AngleAxis(guardSwivel * Mathf.Rad2Deg, ac.normalized);
            root.SetRotation(stream, guard * root.GetRotation(stream));
            mid.SetRotation(stream, guard * mid.GetRotation(stream));
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
        void ApplySwingContinuity(BasisPoseStream stream, int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos, float rateDegPerSec, float dt, Vector3 bodyRight)
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
                // ⚠️ THE SWING LIMITER RUNS LAST -- AFTER the elbow protect AND after its re-guard -- and it
                // had NO anatomy term. Vector3.Slerp takes the SHORTEST arc on the elbow's circle, but the
                // legal set is the complement of a forbidden arc around the top, so it is NOT geodesically
                // convex: two perfectly LEGAL endpoints on opposite sides interpolate straight THROUGH THE
                // SKY. At the shipped 720 deg/s that is ~0.25 s -- 18-22 frames -- with the elbow above the
                // anatomical ceiling, which is what "the elbow caves in and rotates 180 like crazy" is.
                ReGuardElbowAnatomy(stream, root, mid, tip, slot, bodyRight);
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
        /// <param name="hint">The world-space hint (pole) position.</param>
        /// <param name="HasHint">The weight for which hint transform has an effect on IK calculations. This is a value in between 0 and 1.</param>
        /// <param name="targetOffset">The offset applied to the target transform.</param>
        /// <summary>Returns the shin roll applied to the mid bone, so a preserved (untracked) foot can be carried
        /// by it. Identity whenever no shin roll ran.</summary>
        public Quaternion SolveTwoBone(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, Vector3 hint, float hintWeight, Quaternion targetOffset, Vector3 BendNormal, float hintDistrust = 0f, int diagSlot = -1, Quaternion hintRotation = default, bool hintIsTracker = false, Vector3 anteriorNormal = default)
        {
            BasisLegSolveInput input = default;
            root.GetPositionAndRotation(stream, out Vector3 rootPos, out Quaternion rootRot);
            mid.GetPositionAndRotation(stream, out Vector3 midPos, out Quaternion midRot);
            input.Root = rootPos;
            input.Mid = midPos;
            input.Tip = tip.GetPosition(stream);
            input.RootRotation = rootRot;
            input.MidRotation = midRot;
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint;
            input.HintWeight = hintWeight;
            input.HintDistrust = hintDistrust;
            input.TargetOffset = targetOffset;
            input.BendNormal = BendNormal;
            // ANTERIOR stays body-frame even when BendNormal rides a lower-leg tracker: otherwise tibial
            // rotation spins the guard's reference and drags a legal knee into its compression band.
            input.AnteriorNormal = anteriorNormal;
            input.HintRotation = hintRotation;
            input.HintIsTracker = hintIsTracker;

            BasisLegSolveCore.Solve(input, out BasisLegSolveResult result);

            if (diagSlot >= 0 && legDiagnostics.IsCreated && diagSlot < legDiagnostics.Length)
            {
                BasisLegDiagnostics d = legDiagnostics[diagSlot];
                d.ReachRatio = result.ReachRatio;
                d.KneeAngleDeg = result.KneeAngleDeg;
                d.AxisSource = result.AxisSource;
                d.HintApplied = result.HintApplied ? 1f : 0f;
                d.HintDistrust = hintDistrust;
                d.ShinRollDeg = result.ShinRollDeg;
                legDiagnostics[diagSlot] = d;
            }

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
            return result.MidPostRoll;
        }
        public void SolveLegs(BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, Quaternion hintRotProp, float hintWeightProp, Quaternion targetOffset, Vector3 bendNormalProp, bool hintIsTrackerProp, bool footIsTrackerProp, int legSlot)
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
            Vector3 hint = hintPosProp;
            Vector3 bendNormal = bendNormalProp;

            float hintDistrust = 0f;
            bool usedModelHint = false;
            bool fabricatedLeg = !hintIsTrackerProp && !footIsTrackerProp;
            if (!(hintW > 0f) || fabricatedLeg)
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
                    hint = modelHint;
                    hintW = 1f;
                    usedModelHint = true;
                    if (legDiagnostics.IsCreated && legSlot < legDiagnostics.Length)
                    {
                        BasisLegDiagnostics d = legDiagnostics[legSlot];
                        d.ModelHintUsed = 1f;
                        d.ModelConfidence = conf;
                        legDiagnostics[legSlot] = d;
                    }
                    hintDistrust = 1f - BasisSwivelHintCore.LegModelTrust(conf);
                }
            }

            // hintRotation is the tracker-implied shin BONE rotation (rig driver maps the raw tracker through
            // the calibration reference). Only a real lower-leg tracker carries one; every other path passes
            // default, which the solve reads as off.
            Quaternion shinRoll = SolveTwoBone(stream, root, mid, tip, target, hint, hintW, targetOffset, bendNormal, hintDistrust, legSlot,
                                               hintIsTrackerProp ? hintRotProp : default, hintIsTrackerProp, KneeAnteriorRef);
            // Rotation-only fade: the solve produces rotations, so blending positions here would
            // translate bones off the FK chain (dislocated foot) mid-fade.
            if (posWeight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), posWeight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), posWeight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), posWeight));
            }
            // Position-only foot: keep the animation rotation, but CARRIED BY THE SHIN ROLL. A shin tracker with
            // no foot tracker still rolls the shin, and a real foot rides its shin -- restoring the raw animation
            // rotation would leave the ankle counter-twisted by exactly the roll, which is the artifact this
            // whole change exists to remove, just with the sign flipped.
            if (preserveTip)
            {
                Quaternion carriedTip = shinRoll * origTipRot;
                tip.SetRotation(stream, posWeight < 1f ? Quaternion.Slerp(origTipRot, carriedTip, posWeight) : carriedTip);
            }

            RecordHipDiagnostics(stream, root, mid, legSlot);

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
                    // ⭐ A FOOT-DERIVED POLE IS A MEASUREMENT TOO. With foot trackers and NO knee tracker
                    // (canonical 6-point FBT) the pole is not invented: BasisKneeForwardCore builds it from the
                    // foot tracker's own ROTATION (toe azimuth) and BasisButterflyKneeCore from its instep roll.
                    // Both were written after the flags below, and the flags still assumed the only thing driving
                    // a foot-tracked leg was BasisLegSwivelModel -- which reads foot POSITION and never rotation.
                    //
                    // That assumption is what "the legs are not using the feet for direction" is. Two separate
                    // gates were suppressing the foot signal:
                    //   ConditionOnPole multiplies beta by the conditioning (~0.035 standing), so the designed
                    //     0.20 responsiveness became ~0.007 -- the same strangle the knee-tracker path was fixed
                    //     for on 2026-07-17, for the same wrong reason.
                    //   HoldWhenSingular FREEZES the swivel outright below HoldCondLo, and standing IS below it
                    //     -- so turning a foot in place moved the knee not at all. The hold exists to reject a
                    //     slow postural sway that the measurement cannot distinguish from signal; a deliberate
                    //     foot rotation IS signal, and it arrives on a channel the driver has ALREADY damped
                    //     (smoothedBendDir, KneeForwardSmoothRate = 10), so holding it again is redundant.
                    //
                    // So both now key on whether the pole is MEASURED, not on which tracker happens to exist.
                    // A model pole on a foot-tracked leg (butterfly and knee-follow both disabled or gated off)
                    // is still invented and still gets both guards. The knee-TRACKER path is untouched: it keeps
                    // the hold that 2026-07-18 verified against the slow back-and-forth roll.
                    bool footDerivedPole = !hintIsTrackerProp && footIsTrackerProp && !usedModelHint;
                    SmoothKneeSwivel(stream, root, mid, tip, legSlot, stream.deltaTime,
                        k_TrackedKneeSwivelMinCutoffHz, k_TrackedKneeSwivelBeta, k_TrackedKneeSwivelDerivCutoffHz,
                        conditionOnPole: !hintIsTrackerProp && !footDerivedPole,
                        holdWhenSingular: !footDerivedPole);
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
                        conditionOnPole: true, holdWhenSingular: true);
                }
            }
        }
        // Femur pose in the PELVIS frame. Diagnostic only -- nothing in the solve constrains the femur against
        // the pelvis, so this reports whether a hip complaint is genuinely out of anatomical range. Flexion and
        // abduction are read off the femur DIRECTION (positions only, no bind convention); the twist is taken
        // about the femur's own axis and is meaningful as a relative signal, not an absolute angle.
        void RecordHipDiagnostics(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, int slot)
        {
            if (!legDiagnostics.IsCreated || slot < 0 || slot >= legDiagnostics.Length || !HandleHips.IsValid(stream))
            {
                return;
            }

            Vector3 femur = mid.GetPosition(stream) - root.GetPosition(stream);
            if (!(femur.sqrMagnitude > 1e-8f))
            {
                return;
            }

            Quaternion hipsRot = HandleHips.GetRotation(stream);
            Quaternion hipsInv = Quaternion.Inverse(hipsRot);
            Vector3 femurLocal = (hipsInv * femur).normalized;

            BasisLegDiagnostics d = legDiagnostics[slot];
            // Pelvis frame: -Y is straight down the leg, +Z forward, +X the player's right.
            d.HipFlexionDeg = Mathf.Atan2(femurLocal.z, -femurLocal.y) * Mathf.Rad2Deg;
            d.HipAbductionDeg = Mathf.Atan2(femurLocal.x, -femurLocal.y) * Mathf.Rad2Deg;
            d.FemurTwistDeg = TwistDeg(hipsInv * root.GetRotation(stream), femurLocal);
            legDiagnostics[slot] = d;
        }

        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > 1e-8f))
            {
                return 0f;
            }

            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
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
        void SmoothKneeSwivel(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int slot, float dt, float minCutoffHz, float beta, float derivCutoffHz, bool conditionOnPole, bool holdWhenSingular)
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
            // ⭐ Transport `forward` onto the leg's swing plane from body-DOWN rather than PROJECTING it there.
            // The projection REVERSES as hip->ankle sweeps through body-forward -- legs straight out in front,
            // i.e. sitting on the floor, a front kick, lying supine -- flipping the measured swivel a full 180
            // deg and clicking the knee. Body-down is the direction a leg hangs, so the transport is a no-op for
            // every sagittal pose and its own singularity (thigh straight up out of the pelvis) is unreachable.
            // Leg only: the arm's reference IS body-down, so it needs a different home and its own change.
            input.TransportHomeLocal = Vector3.down;
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
            input.HoldWhenSingular = holdWhenSingular;
            input.HoldCondLo = BasisSwivelSmootherCore.DefaultHoldCondLo;
            input.HoldCondHi = BasisSwivelSmootherCore.DefaultHoldCondHi;
            input.State = new BasisSwivelFilterState { Raw = legSwivelRaw[slot].x, Vel = legSwivelRaw[slot].y, Smooth = legSwivelSmooth[slot].x };
            input.Seeded = legSwivelInit[slot] != 0;

            BasisSwivelSmootherCore.Solve(input, out BasisSwivelSmootherResult result);
            if (legDiagnostics.IsCreated && slot < legDiagnostics.Length)
            {
                BasisLegDiagnostics d = legDiagnostics[slot];
                d.RawSwivelDeg = result.RawSwivelDeg;
                d.SmoothSwivelDeg = result.SmoothSwivelDeg;
                d.Conditioning = result.Conditioning;
                d.HoldGate = result.HoldGate;
                d.AnteriorGuardApplied = result.AnteriorGuardApplied ? 1f : 0f;
                d.Seeded = result.Seeded ? 1f : 0f;
                legDiagnostics[slot] = d;
            }
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
                                                out Vector3 modelHint, out float poleConditioning, useNeuralPole))
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
                        bool seeded = swingHintInit[swingSlot] != 0;
                        // The cap budgets the hand's ROTATION and its RADIAL travel separately. A straight punch
                        // rotates the axis by exactly zero, so on the rotation term alone the budget is zero and
                        // the bend freezes while the field -- which is a function of the whole of tipLocal, not
                        // just its direction -- genuinely moves. Measured cost of the missing term on
                        // punch/push/point: 21.5-29.8 deg of pole error and 5.0-7.0 cm of elbow error, against a
                        // field model whose entire budget is 2.07 cm. The conditioning gate is load-bearing:
                        // radial budget is only safe where the field's radial answer means anything, and both
                        // conditioning and radial sensitivity go as 1/|perp|, so they collapse together at a core.
                        float curReach = axLen / armLen;
                        Vector3 cappedBend = seeded
                            ? (Vector3)BasisElbowSwingCapCore.Apply(swingHintBend[swingSlot], swingHintAxis[swingSlot],
                                                                    curAxis, rawBend, BasisElbowSwingCapCore.MaxGain,
                                                                    curReach - swingHintReach[swingSlot], poleConditioning)
                            : rawBend;
                        swingHintBend[swingSlot] = cappedBend;
                        swingHintAxis[swingSlot] = curAxis;
                        swingHintReach[swingSlot] = curReach;

                        // DRAG — no-tracker path only, and it keeps its OWN state rather than feeding back into
                        // the cap's. That separation is load-bearing, not tidiness:
                        //
                        // The cap's budget is `MaxGain * (hand rotation this frame)`, so a STILL hand licenses
                        // ZERO elbow motion. Today that is harmless -- with no lag the bend already sits on the
                        // field, the requested angle is 0, and a cap of 0 clamps nothing. Chain the drag through
                        // the same state and it stops being harmless: the elbow now trails the field, so when the
                        // hand stops there IS a residual angle, and a zero budget FORBIDS THE CATCH-UP. The elbow
                        // parks wherever the lag left it, permanently, at a pose that depends on how you got
                        // there. Measured: it never came within 5 mm of the correct pose in 1.1 s of holding
                        // still. (Real tracker noise would mask this by keeping dHand off zero -- which is worse,
                        // because it makes correctness depend on jitter.)
                        //
                        // So the cap chases the FIELD from its own last output, exactly as before and
                        // bit-identically whether or not drag is on, and the drag is a pure post-filter on top.
                        // Nothing gates the drag's convergence, so a stopped hand settles onto the field.
                        // ==================================================================================
                        // ⚠️ THE DRAG MUST CANCEL THE FRAME THE POLE LIVES IN, AND THAT IS THE ARM FRAME --
                        // NOT THE HIPS. This read HandleHips, copied from SmoothKneeSwivel, which is correct
                        // for the LEG because BuildLegFrame's frame IS the pelvis. The arm's pole comes from
                        // BasisSwivelHintCore.ArmHint on BuildArmFrame -- the shoulder line and chest->neck --
                        // so cancelling the hips left the CHEST-RELATIVE-TO-HIPS rotation entirely uncancelled,
                        // and the drag read every torso twist as swivel error to be damped. The drag's own
                        // header gives the cost of exactly that failure: 0.86 cm at 90 deg/s and 2.5 Hz,
                        // 1.81 cm at 1.25 Hz (the shipped default), 3.57 cm at 180 deg/s -- against a field
                        // model whose entire error budget is ~2.1 cm. Reads as the elbow "swimming" when you
                        // turn your upper body with your feet planted.
                        //
                        // Falls back to the hips, then to identity, so a degenerate arm frame degrades to the
                        // previous behaviour rather than fabricating a frame.
                        // ==================================================================================
                        Quaternion bodyRot = frame.Valid
                            ? Quaternion.LookRotation(frame.Forward, frame.Up)
                            : HandleHips.IsValid(stream) ? HandleHips.GetRotation(stream) : Quaternion.identity;

                        Vector3 outBend = cappedBend;
                        if (elbowDragEnabled && seeded)
                        {
                            Quaternion bodyDelta = bodyRot * Quaternion.Inverse(swingHintBodyRot[swingSlot]);
                            outBend = (Vector3)BasisElbowDragCore.Apply(swingHintDrag[swingSlot], bodyDelta, curAxis, cappedBend,
                                                                       BasisElbowDragCore.Alpha(elbowDragHz, stream.deltaTime));
                        }
                        swingHintDrag[swingSlot] = outBend;
                        swingHintBodyRot[swingSlot] = bodyRot;
                        swingHintInit[swingSlot] = 1;
                        modelHint = shoulderPos + 0.5f * armLen * outBend;
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
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hasHint, hasHint && !usedModel, targetOffset, swingSlot, bodyRight);
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
            float elbowSwivelDeg = float.NaN;   // NaN == no established choice to anchor on next frame
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
                // Last frame's swivel for this arm, which turns the protect's search from a one-sided arc
                // into the whole circle. The domain widening is where the cleared fraction comes from; the
                // anchor is what stops a wider domain hopping between disconnected feasible arcs. See the
                // block above SearchFullCircle in BasisElbowProtectCore -- they only work as a pair.
                // ⚠️ OFF ON PURPOSE -- the measured trade does not pay for itself yet.
                // BasisElbowProtectSweep over 354 375 points, production collider + arm solve:
                //   legacy      clearedFrac 0.3606  couldNotClear 57740  meanSwing 12.68  sens 3.126
                //   fullCircle  clearedFrac 0.3707  couldNotClear 55952  meanSwing 20.99  sens 3.525
                // +1.0 point of clearing for +66% elbow swing and +13% twitch. An offline harness on a
                // slimmer collider predicted +12.9 points; the production rig does not reproduce it, and
                // the swing/sensitivity cost is a FEEL regression that no gate here can see.
                // Flip to true and re-run BasisIKSweepBatch.ElbowProtectFullCircle before trusting either.
                epi.FullCircle = false;
                if (swingSwivelDeg.IsCreated)
                {
                    // NaN == no established choice (first engage, or the protect was off last frame). The
                    // anchor MUST stay off in that case -- see HasPrevSwivel's doc comment.
                    float prev = swingSwivelDeg[swingSlot];
                    if (!float.IsNaN(prev))
                    {
                        epi.PrevSwivelDeg = prev;
                        epi.HasPrevSwivel = true;
                    }
                }

                BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
                if (epr.Engaged)
                {
                    tip.GetPositionAndRotation(stream, out Vector3 preservedHandPos, out Quaternion preservedHandRot);
                    SwingElbowAroundAC(stream, root, mid, tip, epr.DesiredElbow);
                    tip.SetPosition(stream, preservedHandPos);
                    tip.SetRotation(stream, preservedHandRot);
                    ReGuardElbowAnatomy(stream, root, mid, tip, swingSlot, bodyRight);
                }
                collisionState = epr.CollisionState;
                elbowSwivelDeg = epr.Engaged ? epr.ChosenSwivelDeg : float.NaN;
            }

            if (swingCollided.IsCreated)
            {
                swingCollided[swingSlot] = collisionState;
            }

            // Carry the chosen swivel to next frame. Written on EVERY path, including the one where the
            // protect did not engage -- that writes 0, which re-anchors on the natural pole, so re-engaging
            // later starts from where the elbow actually is instead of from a stale arc.
            if (swingSwivelDeg.IsCreated)
            {
                swingSwivelDeg[swingSlot] = elbowSwivelDeg;
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

        public const int Count = 22;


        // Slots are HumanBodyBones values: 0..RightToes map directly, UpperChest (54) maps to the last slot.
        public const int UpperChestSlot = Count - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Slot(int humanBodyBone)
        {
            if (humanBodyBone >= 0 && humanBodyBone <= (int)HumanBodyBones.RightToes)
            {
                return humanBodyBone;
            }
            return humanBodyBone == (int)HumanBodyBones.UpperChest ? UpperChestSlot : -1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetPosition(int idx, in Vector3 v)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotPositions.Length)
            {
                slotPositions[s] = v;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotRotations.Length)
            {
                slotRotations[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOffsetRotation(int idx, in Quaternion q)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotOffsets.Length)
            {
                slotOffsets[s] = q;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWeight(int idx, bool State)
        {
            int s = Slot(idx);
            if (s >= 0 && s < slotWeights.Length)
            {
                slotWeights[s] = State;
            }
        }
        public void SetDefaultValues()
        {



            HasChestTracker = true;
            hintWeightLeftLowerLeg = hintWeightRightLowerLeg = 1f;
            enabledSpineIK = true;
            hasHipsTracker = false;
            footIsTrackerLeftLeg = footIsTrackerRightLeg = false;
            enabledLeftLowerLeg = enabledRightLowerLeg = 1f;
            hintIsTrackerLeftLowerLeg = hintIsTrackerRightLowerLeg = false;
            ikLockMode = (float)BasisIKLockMode.LockHead;

            hintWeightLeftHand = hintWeightRightHand = true;
            enabledLeftHand = enabledRightHand = 1f;
            offsetRotationHead = offsetRotationLeftFoot = offsetRotationRightFoot = Quaternion.identity;
            offsetRotationLeftHand = offsetRotationRightHand = Quaternion.identity;

            playerUp = Vector3.up;

            targetPositionHips = Vector3.zero;
            targetRotationHips = Quaternion.identity;
            offsetRotationHips = Quaternion.identity;

            // Integrated driven TR defaults

            leftDrivenTargetRot = rightDrivenTargetRot = Quaternion.identity;
            leftToeEnabled = false;
            RightToeEnabled = false;

            // Chest/hand capsule defaults — read from persisted settings
            chestRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKChestRadius.RawValue;
            collisionSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionSkin.RawValue;
            collisionsEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKCollisionsEnabled.RawValue;
            handRadius = Basis.BasisUI.BasisSettingsDefaults.FBIKHandRadius.RawValue;
            handSkin = Basis.BasisUI.BasisSettingsDefaults.FBIKHandSkin.RawValue;
            protectElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKProtectElbow.RawValue;
            elbowDragEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDrag.RawValue;
            elbowDragHz = Basis.BasisUI.BasisSettingsDefaults.FBIKElbowDragHz.RawValue;
            collideTrackedElbow = Basis.BasisUI.BasisSettingsDefaults.FBIKCollideTrackedElbow.RawValue;
            wristAxialBound = Basis.BasisUI.BasisSettingsDefaults.FBIKWristAxialBound.RawValue;

            shoulderSolveEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderSolveEnabled.RawValue;
            shoulderShrugEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderShrug.RawValue;
            shoulderRetractionEnabled = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderRetraction.RawValue;
            shoulderRhythmEnabled = false;
            shoulderElevationFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderElevation.RawValue;
            shoulderProtractionFactor = Basis.BasisUI.BasisSettingsDefaults.FBIKShoulderProtraction.RawValue;

            spineBendPitch = 0.45f;
            spineBendYaw = 0.10f;
            spineBendRoll = 0.35f;
            upperChestBendPitch = 0.25f;
            upperChestBendYaw = 0.30f;
            upperChestBendRoll = 0.20f;
            hipHingeStartDeg = 40f;
            hipHingeMaxAddDeg = 52f;
            chestSpringHz = 12f;
            chestSpringDamping = 1f;
            spineMaxForwardDeg = 60f;
            spineMaxBackwardDeg = 25f;
            spineMaxLateralDeg = 25f;
            spineSquishBoost = 0.5f;
            spineGazeFollow = 0.25f;
            neckGazeFollow = 0.3f;
            moveBodyBackWhenCrouching = 1f;
            crouchDepth = 0f;
            standingHeadHeight = 0f; // 0 = sit-back inert until the rig driver packs the real height
            trunkCounterbalance = BasisTrunkCounterbalanceCore.DerivedGain;
            swingSmoothRateDeg = 720f;
            chestArmSwingFactor = 0.3f;
            chestArmSwingMaxDeg = 15f;
            lowerArmTwistFraction = 0.5f;
            upperArmTwistFraction = 0.3f;

            anatDifferentialStiffness = true;
            anatShoulderSlide = true;
            anatCervicalLordosis = true;
            anatPelvicTwistRouting = true;
            spineAnatomicalRom = true;
            chestIkTarget = true;
            legSwivelSmoothing = true;
            lordosisPitchGainDeg = 8f;
            lordosisBaseDeg = 5f;
            lordosisNeckShare = 0.65f;
            lordosisMaxHeadPitchDeg = 80f;
            lordosisExtremeStartDeg = 50f;
            lordosisExtremeFullDeg = 80f;
            lordosisExtremeRollForwardMaxDeg = 10f;
            lordosisExtremeRollBackwardMaxDeg = 4f;
            lordosisExtremeHipsHorizontalMax = 0.025f;
            lordosisExtremeChestHorizontalMax = 0.04f;
            lordosisExtremeHipsDownMax = 0.015f;
            lordosisExtremeChestDownMax = 0.025f;
            lordosisExtremeHipsDownLookUp = 0.0005f;
            lordosisExtremeChestDownLookUp = 0.001f;
            // 1.0 (was 0.8), retuned against the mocap corpus: full relax is strictly better measured —
            // closer to the human spine AND a quieter standing noise floor. See FBIKSpineCCDRelax.
            spineCCDRelax = 1.0f;
            neckMaxConeDeg = 45f;
            spineTwistKeep = 0.25f;
            spineNeckTwistKeep = 0.9f;

            // Slots: identity rotations, zero positions, weights disabled.
            slotPositions.Length = Count;
            slotRotations.Length = Count;
            slotOffsets.Length = Count;
            slotWeights.Length = Count;
            for (int i = 0; i < Count; i++)
            {
                slotPositions[i] = Vector3.zero;
                slotRotations[i] = Quaternion.identity;
                slotOffsets[i] = Quaternion.identity;
                slotWeights[i] = false;
            }
        }

        public void Create(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            HandleHips = BindHandle(skeleton, Mapping.Hips);
            HandleChest = BindHandle(skeleton, Mapping.chest);
            HandleNeck = BindHandle(skeleton, Mapping.neck);
            HandleHead = BindHandle(skeleton, Mapping.head);
            HandleLeftUpperLeg = BindHandle(skeleton, Mapping.LeftUpperLeg);
            HandleLeftLowerLeg = BindHandle(skeleton, Mapping.LeftLowerLeg);
            HandleLeftFoot = BindHandle(skeleton, Mapping.leftFoot);
            HandleRightUpperLeg = BindHandle(skeleton, Mapping.RightUpperLeg);
            HandleRightLowerLeg = BindHandle(skeleton, Mapping.RightLowerLeg);
            HandleRightFoot = BindHandle(skeleton, Mapping.rightFoot);
            HandleLeftToe = BindHandle(skeleton, Mapping.leftToe);
            HandleRightToe = BindHandle(skeleton, Mapping.rightToe);
            HandleLeftUpperArm = BindHandle(skeleton, Mapping.leftUpperArm);
            HandleLeftLowerArm = BindHandle(skeleton, Mapping.leftLowerArm);
            HandleLeftHand = BindHandle(skeleton, Mapping.leftHand);
            HandleRightUpperArm = BindHandle(skeleton, Mapping.RightUpperArm);
            HandleRightLowerArm = BindHandle(skeleton, Mapping.RightLowerArm);
            HandleRightHand = BindHandle(skeleton, Mapping.rightHand);
            HandleLeftUpperArmTwist = BindHandle(skeleton, Mapping.leftUpperArmTwist);
            HandleLeftLowerArmTwist = BindHandle(skeleton, Mapping.leftLowerArmTwist);
            HandleRightUpperArmTwist = BindHandle(skeleton, Mapping.RightUpperArmTwist);
            HandleRightLowerArmTwist = BindHandle(skeleton, Mapping.RightLowerArmTwist);
            HandleSpine = BindHandle(skeleton, Mapping.spine);
            HandleUpperChest = BindHandle(skeleton, Mapping.Upperchest);
            HandleLeftShoulder = BindHandle(skeleton, Mapping.leftShoulder);
            HandleRightShoulder = BindHandle(skeleton, Mapping.RightShoulder);

            // Baked T-pose data for shoulder solve
            TposeLeftShoulderRot = Mapping.leftShoulder != null ? Mapping.leftShoulder.rotation : Quaternion.identity;
            TposeRightShoulderRot = Mapping.RightShoulder != null ? Mapping.RightShoulder.rotation : Quaternion.identity;
            BakeHumerusTwistBind(Mapping.leftUpperArm, Mapping.leftLowerArm,
                out TposeLeftUpperArmRot, out TposeLeftHumerusDir, out TposeLeftHumerusRefAxis);
            BakeHumerusTwistBind(Mapping.RightUpperArm, Mapping.RightLowerArm,
                out TposeRightUpperArmRot, out TposeRightHumerusDir, out TposeRightHumerusRefAxis);
            // ⚠️ The wrist axial bound centres on the bind hand-vs-forearm relationship. Without these it
            // centres on "bind hand is axially aligned with bind forearm", and any rig with a real bind
            // offset gets its hand roll clipped in ONE direction on EVERY frame, permanently.
            // ⚠️ `default` (the ZERO quaternion), NOT identity. Identity passes the bound's `> 0.5f` liveness
            // test, so it would DEFEAT the decline path and hand the wrist a reference off by the whole bind
            // forearm rotation -- a permanent one-sided clip. Zero means decline, matching BakeHumerusTwistBind.
            TposeLeftHandRot = Mapping.leftHand != null ? Mapping.leftHand.rotation : default;
            TposeRightHandRot = Mapping.rightHand != null ? Mapping.rightHand.rotation : default;
            TposeLeftLowerArmRot = Mapping.leftLowerArm != null ? Mapping.leftLowerArm.rotation : Quaternion.identity;
            TposeRightLowerArmRot = Mapping.RightLowerArm != null ? Mapping.RightLowerArm.rotation : Quaternion.identity;

            // Must be the SAME bone SolveShoulder reads live -- the clavicle's actual parent, which is the
            // UpperChest when the rig has one. Baking the bind from one bone and reading the live rotation
            // from another turns the girdle frame into a since-bind delta plus a constant offset.
            TposeChestRot = Mapping.Upperchest != null ? Mapping.Upperchest.rotation
                          : Mapping.chest != null ? Mapping.chest.rotation
                          : Quaternion.identity;
            TposeChestBind = (Mapping.HasAnimatorRoot && Mapping.AnimatorRoot != null
                ? Quaternion.Inverse(Mapping.AnimatorRoot.rotation)
                : Quaternion.identity) * TposeChestRot;
            TposeLeftShoulderLocalDir = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? (Mapping.leftUpperArm.position - Mapping.leftShoulder.position).normalized : Vector3.left;
            TposeRightShoulderLocalDir = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? (Mapping.RightUpperArm.position - Mapping.RightShoulder.position).normalized : Vector3.right;
            // 0.6 m is an adult arm; on a small avatar it is the same shoulder-inert / shrug-latched failure
            // a stale bake produces, so the fallback tracks avatar size too.
            float fallbackArmLength = 0.6f * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
            TposeShoulderToHandLeft = (Mapping.leftShoulder != null && Mapping.leftHand != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftHand.position) : fallbackArmLength;
            TposeShoulderToHandRight = (Mapping.RightShoulder != null && Mapping.rightHand != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.rightHand.position) : fallbackArmLength;
            TposeClavicleLenLeft = (Mapping.leftShoulder != null && Mapping.leftUpperArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftUpperArm.position) : 0f;
            TposeClavicleLenRight = (Mapping.RightShoulder != null && Mapping.RightUpperArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightUpperArm.position) : 0f;
            TposeShoulderToElbowLeft = (Mapping.leftShoulder != null && Mapping.leftLowerArm != null)
                ? Vector3.Distance(Mapping.leftShoulder.position, Mapping.leftLowerArm.position) : 0f;
            TposeShoulderToElbowRight = (Mapping.RightShoulder != null && Mapping.RightLowerArm != null)
                ? Vector3.Distance(Mapping.RightShoulder.position, Mapping.RightLowerArm.position) : 0f;

            // Pair each slot with its bone handle, in HumanBodyBones order.
            slotHandles.Length = Count;
            slotHandles[0] = HandleHips;
            slotHandles[1] = HandleLeftUpperLeg;
            slotHandles[2] = HandleRightUpperLeg;
            slotHandles[3] = HandleLeftLowerLeg;
            slotHandles[4] = HandleRightLowerLeg;
            slotHandles[5] = HandleLeftFoot;
            slotHandles[6] = HandleRightFoot;
            slotHandles[7] = HandleSpine;
            slotHandles[8] = HandleChest;
            slotHandles[9] = HandleNeck;
            slotHandles[10] = HandleHead;
            slotHandles[11] = HandleLeftShoulder;
            slotHandles[12] = HandleRightShoulder;
            slotHandles[13] = HandleLeftUpperArm;
            slotHandles[14] = HandleRightUpperArm;
            slotHandles[15] = HandleLeftLowerArm;
            slotHandles[16] = HandleRightLowerArm;
            slotHandles[17] = HandleLeftHand;
            slotHandles[18] = HandleRightHand;
            slotHandles[19] = HandleLeftToe;
            slotHandles[20] = HandleRightToe;
            slotHandles[UpperChestSlot] = HandleUpperChest;

            GenerateHeadToSpine(skeleton, Mapping);
            spineMaxIterations = 20;
            spineTolerance = 0.001f;
            chestSpringState = new NativeArray<Vector3>(2, Allocator.Persistent);
            chestSpringInit = new NativeArray<int>(1, Allocator.Persistent);

            swingLastDir = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingLastAxis = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingLastTarget = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingContinuityInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingCollided = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingSwivelDeg = new NativeArray<float>(k_SwingCount, Allocator.Persistent);
            swingGuardSide = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            // NativeArray zero-inits, and 0 would read as "anchored on the natural pole" rather than
            // "no history" -- which is the exact conflation that measured backwards on the sweep.
            for (int s = 0; s < k_SwingCount; s++) swingSwivelDeg[s] = float.NaN;
            swingSmoothState = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingHintBend = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintAxis = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintReach = new NativeArray<float>(k_SwingCount, Allocator.Persistent);
            swingHintDrag = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingHintBodyRot = new NativeArray<Quaternion>(k_SwingCount, Allocator.Persistent);
            swingHintInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchor = new NativeArray<Vector3>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchorRot = new NativeArray<Quaternion>(k_SwingCount, Allocator.Persistent);
            swingPoleAnchorInit = new NativeArray<int>(k_SwingCount, Allocator.Persistent);
            legSwivelRaw = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelSmooth = new NativeArray<Vector3>(2, Allocator.Persistent);
            legSwivelInit = new NativeArray<int>(2, Allocator.Persistent);
            legDiagnostics = new NativeArray<BasisLegDiagnostics>(2, Allocator.Persistent);
            armDiagnostics = new NativeArray<BasisArmDiagnostics>(2, Allocator.Persistent);
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
        void BuildSpineAnatomy(Transform[] chain, BasisTransformMapping Mapping)
        {
            int n = chain.Length;
            ChainSpineRestFrames = new NativeArray<BasisSpineRestFrame>(n, Allocator.Persistent);
            ChainSpineRoms = new NativeArray<BasisSpineRom>(n, Allocator.Persistent);

            // The subject's RIGHT, from the shoulders. A body-wide fact -- NOT a bone's local axis, which is
            // a rig convention and does not transfer between avatars. This project has been bitten by that
            // repeatedly; it is why the arm swivel model is position-only.
            if (Mapping.leftUpperArm == null || Mapping.RightUpperArm == null)
            {
                return;   // every frame stays Valid=false, so the guard is a no-op. Decline, never guess.
            }
            Vector3 hipsRight = Mapping.RightUpperArm.position - Mapping.leftUpperArm.position;

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
                if (bone == Mapping.spine)
                {
                    segment = BasisSpineSegment.Lumbar;
                }
                else if (bone == Mapping.chest)
                {
                    segment = BasisSpineSegment.LowerThoracic;
                }
                else if (bone == Mapping.Upperchest)
                {
                    segment = BasisSpineSegment.UpperThoracic;
                }
                else if (bone == Mapping.neck)
                {
                    segment = BasisSpineSegment.Cervical;
                }
                else
                {
                    continue;
                }

                ChainSpineRestFrames[i] = BasisSpineAnatomy.BuildRestFrame(
                    bone.position, child.position, bone.rotation, parent.rotation, hipsRight);
                ChainSpineRoms[i] = BasisSpineAnatomy.Rom(segment);
            }
        }
        public void GenerateHeadToSpine(BasisPoseSkeleton skeleton, BasisTransformMapping Mapping)
        {
            var HeadToSpine = Mapping.Upperchest != null
                ? new Transform[] { Mapping.head, Mapping.neck, Mapping.Upperchest, Mapping.chest, Mapping.spine, Mapping.Hips }
                : new Transform[] { Mapping.head, Mapping.neck, Mapping.chest, Mapping.spine, Mapping.Hips };
            int SpineToHeadLength = HeadToSpine.Length;
            ChainHeadToSpine = new NativeArray<BasisBoneHandle>(SpineToHeadLength, Allocator.Persistent);
            BuildSpineAnatomy(HeadToSpine, Mapping);

            for (int i = 0; i < SpineToHeadLength; i++)
            {
                ChainHeadToSpine[i] = skeleton.Bind(HeadToSpine[i]);
            }
            if (Mapping.Hips != null && Mapping.head != null)
            {
                TposeLengthHeadToHips = (Mapping.head.position - Mapping.Hips.position);
            }
            else
            {
                TposeLengthHeadToHips = Vector3.zero;
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
            if (Mapping.head != null && Mapping.neck != null)
            {
                TposeHeadToNeckLocal = Quaternion.Inverse(Mapping.head.rotation) * (Mapping.neck.position - Mapping.head.position);
            }
            else
            {
                TposeHeadToNeckLocal = Vector3.zero;
            }

            if (Mapping.Hips != null && Mapping.neck != null)
            {
                TposeLengthNeckToHips = (Mapping.neck.position - Mapping.Hips.position);
            }
            else
            {
                TposeLengthNeckToHips = TposeLengthHeadToHips;
            }

            // Record the size these were measured at, so a later rescale can carry them along.
            TposeBakeScale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        }

        /// <summary>
        /// Carries the baked Tpose* scalars to a new avatar size. They are DENOMINATORS of ratio tests whose
        /// numerators are read live, so a stale value does not degrade the test — it saturates it: the
        /// shoulder solve goes inert (rawReach never reaches ReachEngage), the shrug latches at maximum on
        /// the elbow-tracker path, squishMult pins at 1+boost, and ComputeNeckCue lands at the wrong distance
        /// from the head, which mis-cues DistributeSpineBend, ApplyTrunkCounterbalance and ApplyHipHinge.
        /// All of it inverts above 1x. No-ops before the first bake and when the size has not moved.
        /// </summary>
        public void RescaleTposeScalars(float newScale)
        {
            if (float.IsNaN(newScale) || float.IsInfinity(newScale) || newScale <= 0f)
            {
                return;
            }
            if (TposeBakeScale <= 0f)
            {
                return;
            }
            float k = newScale / TposeBakeScale;
            if (Mathf.Abs(k - 1f) < 1e-6f)
            {
                return;
            }

            TposeShoulderToHandLeft *= k;
            TposeShoulderToHandRight *= k;
            TposeClavicleLenLeft *= k;
            TposeClavicleLenRight *= k;
            TposeShoulderToElbowLeft *= k;
            TposeShoulderToElbowRight *= k;
            TposeLengthHeadToHips *= k;
            TposeHeadToNeckLocal *= k;
            TposeLengthNeckToHips *= k;

            TposeBakeScale = newScale;
        }
        static BasisBoneHandle BindHandle(BasisPoseSkeleton skeleton, Transform t) => (t != null) ? skeleton.Bind(t) : default;
        public void Destroy()
        {
            if (ChainHeadToSpine.IsCreated) ChainHeadToSpine.Dispose();
            if (ChainSpineRestFrames.IsCreated) ChainSpineRestFrames.Dispose();
            if (ChainSpineRoms.IsCreated) ChainSpineRoms.Dispose();

            if (chestSpringState.IsCreated) chestSpringState.Dispose();
            if (chestSpringInit.IsCreated) chestSpringInit.Dispose();

            if (swingLastDir.IsCreated) swingLastDir.Dispose();
            if (swingLastAxis.IsCreated) swingLastAxis.Dispose();
            if (swingLastTarget.IsCreated) swingLastTarget.Dispose();
            if (swingContinuityInit.IsCreated) swingContinuityInit.Dispose();
            if (swingCollided.IsCreated) swingCollided.Dispose();
            if (swingSwivelDeg.IsCreated) swingSwivelDeg.Dispose();
            if (swingGuardSide.IsCreated) swingGuardSide.Dispose();
            if (swingSmoothState.IsCreated) swingSmoothState.Dispose();
            if (swingHintBend.IsCreated) swingHintBend.Dispose();
            if (swingHintAxis.IsCreated) swingHintAxis.Dispose();
            if (swingHintReach.IsCreated) swingHintReach.Dispose();
            if (swingHintDrag.IsCreated) swingHintDrag.Dispose();
            if (swingHintBodyRot.IsCreated) swingHintBodyRot.Dispose();
            if (swingHintInit.IsCreated) swingHintInit.Dispose();
            if (swingPoleAnchor.IsCreated) swingPoleAnchor.Dispose();
            if (swingPoleAnchorRot.IsCreated) swingPoleAnchorRot.Dispose();
            if (swingPoleAnchorInit.IsCreated) swingPoleAnchorInit.Dispose();
            if (legDiagnostics.IsCreated) legDiagnostics.Dispose();
            if (armDiagnostics.IsCreated) armDiagnostics.Dispose();
            if (legSwivelRaw.IsCreated) legSwivelRaw.Dispose();
            if (legSwivelSmooth.IsCreated) legSwivelSmooth.Dispose();
            if (legSwivelInit.IsCreated) legSwivelInit.Dispose();
        }
    }
}
