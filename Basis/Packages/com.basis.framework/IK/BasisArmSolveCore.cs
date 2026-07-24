using UnityEngine;
namespace Basis.IK
{
    public struct BasisArmSolveInput
    {
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;
        public Quaternion RootRotation;
        public Quaternion MidRotation;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 HintPosition;
        public bool HintWeight;
        public Quaternion TargetOffset;
        public Vector3 PlayerUp;
        /// <summary>The TORSO's up (chest->neck, i.e. BasisSwivelHintCore.BuildFrame(...).Up), which is the
        /// frame BasisElbowAnatomyCore's law was measured in and the ONLY frame it is true in. PlayerUp is the
        /// player ROOT's up: it stays vertical while the chest does not, so feeding the guard that one makes
        /// "above the shoulder" meaningless the moment the user bends at the waist -- and the guard then fires
        /// on ordinary bent-over poses (measured: 0.3475% of the posture tier, with the measurement's SIGN
        /// FLIPPED). Zero -- the struct default -- falls back to PlayerUp, which is the pre-fix behaviour, so a
        /// caller that cannot build a body frame still gets a guarded arm rather than none.</summary>
        public Vector3 TorsoUp;
        public float HintMaxStepDeg;   // max elbow-swivel change this solve; float.MaxValue = unclamped (offline)
        public bool HintIsTracker;     // hint is a REAL elbow tracker (trust it further before the down-stabilizer overrides); false = lookup-derived
        public Quaternion TipRotation; // ANIMATED hand world rotation (pre-IK), like RootRotation/MidRotation. Zero (the default) disables wrist-roll relief.
        /// <summary>The lower-arm tracker's measured FOREARM BONE world rotation (its raw rotation already
        /// mapped through the calibration reference -- BasisLimbRollStore, applied by the rig driver; an elbow
        /// strap's clock angle is arbitrary and would otherwise land as a constant forearm twist). Zero (the
        /// struct default) disables the tracker forearm roll.</summary>
        public Quaternion HintRotation;
        /// <summary>Last frame's well-conditioned pole DIRECTION (world, unit, perpendicular to the then
        /// shoulder->hand axis), from BasisArmSolveResult.PoleDirUsed.</summary>
        public Vector3 PrevPoleDir;
        /// <summary>HintRotation as it was when PrevPoleDir was stored. The pole is carried through the
        /// tracker's rotation SINCE then, so only a relative rotation about a world axis is ever read and no
        /// assumption is made about which local axis the forearm bone points down.</summary>
        public Quaternion PrevHintRotation;
        /// <summary>False (the struct default) restores the pre-anchor behaviour exactly, so every existing
        /// caller, test and offline sweep is bit-identical.</summary>
        public bool HasPrevPole;
        /// <summary>LIVE world rotation of the clavicle (shoulder) bone, read after SolveShoulder.</summary>
        public Quaternion ClavicleRotation;
        /// <summary>Bind/T-pose world rotation of that same clavicle bone.</summary>
        public Quaternion BindClavicleRotation;
        /// <summary>Bind/T-pose world rotation of the UPPER ARM bone.</summary>
        public Quaternion BindHumerusRotation;
        /// <summary>Bind world direction upperArm->lowerArm, normalized. Zero (the default) declines the
        /// humeral twist guard, so every existing caller, test and offline sweep stays bit-identical.</summary>
        public Vector3 BindHumerusDir;
        /// <summary>A bone-LOCAL axis perpendicular to the humerus, baked per rig. It is the reference the
        /// twist is measured against; a hardcoded world axis would be parallel to the bone on some rigs and
        /// silently decline. Zero declines.</summary>
        public Vector3 BindHumerusRefAxis;
        /// <summary>Bind/T-pose world rotation of the HAND bone. With BindLowerArmRotation it fixes the
        /// hand's ZERO-AXIAL-ROLL relationship to the forearm, which is what the wrist bound at the end of
        /// Solve measures against.
        ///
        /// Zero -- the struct default -- takes that relationship to be the IDENTITY, i.e. "the rig's bind
        /// hand is axially aligned with its bind forearm". That is not a new assumption: the no-tracker
        /// forearm roll above already reads the hand's pronation demand as its twist against the ZERO
        /// PRONATION FOREARM, which is the same statement. A rig that bakes this field gets the exact
        /// relation instead, and a rig whose bind hand carries an axial offset needs it -- without it the
        /// wrist bound is centred that offset away from the rig's true neutral.</summary>
        public Quaternion BindHandRotation;
        /// <summary>Lets the wrist bound at the end of Solve write the hand. False -- the struct default --
        /// still MEASURES the breach into WristAxialDeg but leaves TipRotation exactly on the target, because
        /// a hand that has left the controller's rotation is the worse of the two artifacts in a headset.</summary>
        public bool ApplyWristAxialBound;
        /// <summary>Bind/T-pose world rotation of the LOWER ARM bone. With BindHumerusRotation it fixes the
        /// forearm's ZERO-PRONATION relationship to the humerus, which is the only thing that makes the
        /// no-tracker forearm roll a DEFINED quantity instead of one inherited from the animation clip.
        ///
        /// It is a RELATIVE rotation that gets used (Inverse(BindHumerusRotation) * BindLowerArmRotation), so
        /// it carries no world-frame assumption and no conditioning hazard: a straight-armed T-pose, which
        /// breaks any construction that needs a hinge AXIS (upper arm and forearm are collinear there, so
        /// their cross product is undefined), is a complete non-event for a relative quaternion.
        ///
        /// Zero -- the struct default -- declines, so every existing caller, test and offline sweep is
        /// bit-identical until a rig actually bakes it.</summary>
        public Quaternion BindLowerArmRotation;
        /// <summary>The body-lateral axis pointing AWAY from the torso for THIS arm -- the shoulder line
        /// (rightUpperArm - leftUpperArm), negated for the left arm. It seeds the elbow anatomy guard's
        /// branch on the first frame the guard fires with the elbow in its tie band, where the measured pole
        /// carries no side information and sign(s) is a coin flip. Zero declines.</summary>
        public Vector3 ElbowLateralOut;
        /// <summary>The side (-1 / +1) BasisElbowAnatomyCore chose for this arm last frame, from
        /// BasisArmSolveResult.GuardSideUsed. 0 -- the struct default -- means no history and declines the
        /// hysteresis, which is bit-identical to the pre-band guard.</summary>
        public int PrevGuardSide;
    }

    public struct BasisArmSolveResult
    {
        // Apply through the AnimationStream in this order; identity steps are exact no-ops:
        //   mid.SetRotation(MidDelta * mid.GetRotation), root.SetRotation(RootDelta * ...),
        //   root.SetRotation(HintDelta * ...), mid.SetRotation(MidPostRoll * mid.GetRotation),
        //   tip.SetRotation(TipRotation).
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion MidPostRoll;   // WORLD premultiply on the forearm, applied AFTER the root deltas
                                         // have propagated: a pure roll about its own long axis, so it can
                                         // move no joint. Always a valid quaternion (identity when unused).
        public Quaternion TipRotation;
        public bool HintApplied;

        public Vector3 ElbowSolved;
        public Vector3 HandSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float ElbowAngleDeg;
        public float HintFade;       // 0..1 tracker/hint influence actually used (0 at full extension = tracker ignored)
        public float HintProjMag;    // |hint projected onto swing plane|; small = unstable, tiny tracker error swings the elbow
        public float ArmProjMag;     // |elbow projected onto swing plane|; small = elbow near-straight (pole ill-defined)
        public byte AxisSource;     // 0 bend-plane, 1 hint, 2 shoulder->target, 3 playerUp
        public float HandError;
        public float WristTwistDeg;   // signed hand-target roll vs the carried animated wrist, about the forearm axis
        public float WristReliefDeg;  // signed swivel actually spent relieving it (0 = relief not engaged)
        public float ForearmRollDeg;  // signed forearm roll applied via MidPostRoll (0 = not engaged). Tracker
                                      // path: toward the tracker's measured pronation. No-tracker path: toward
                                      // the BIND pronation plus the hand's in-band demand, so the forearm's
                                      // axial roll stops being inherited from the animation clip.
        /// <summary>TRACKER path only (0 on every other path): the roll this stage was ASKED for, before the
        /// +/-180 principal-branch wrap, before the soft/hard saturation and before the seam cap.
        ///
        /// It is published for the same reason <see cref="HumeralTwistDeg"/> is: the envelope the APPLIED roll
        /// has to satisfy is a function of the demand, not a constant. The seam cap deliberately relaxes the
        /// hard bound approaching +/-180 -- that is the price of continuity there, stated in the core -- so a
        /// gate on ForearmRollDeg has to read
        ///
        ///     max(TrackerForearmRollMaxDeg, 2*|Mathf.DeltaAngle(0, ForearmRollDemandDeg)| - 180)
        ///
        /// exactly as the humeral guard's gate reads max(HumeralTwistHardDeg, 2*|HumeralTwistDeg| - 180). A
        /// FLAT bound on the applied roll is only correct for a stage that has no seam cap, and this one now
        /// has. Reported BEFORE the wrap so a test can also see when the hand blend has carried the demand off
        /// its principal branch, which is a separate hole the wrap closes.</summary>
        public float ForearmRollDemandDeg;
        public Vector3 PoleDirUsed;   // pole direction to carry into next frame's PrevPoleDir
        public Quaternion PoleRotUsed;// HintRotation to carry into next frame's PrevHintRotation
        public bool PoleAnchorValid;  // false = nothing worth storing this frame
        public float PoleConditioning;// 1 = the measured pole's lever arm is healthy, 0 = fully anchored
        public float HumeralTwistDeg;      // signed humeral axial rotation vs the clavicle, before guarding
        public float HumeralTwistGuardDeg; // signed correction the twist guard applied (0 = inside the envelope)
        /// <summary>The hand's axial roll relative to the SOLVED forearm -- as the stream will actually
        /// render it, not as the solver hoped -- measured BEFORE the wrist bound. This is the quantity the
        /// radiocarpal joint has no range for, and it is published because the bound's envelope is a
        /// function of the demand (the seam cap relaxes nothing here, but a gate still has to be able to
        /// see what was asked for in order to tell a bound that engaged from one that never had to).</summary>
        public float WristAxialDeg;
        /// <summary>Signed roll the wrist bound took OFF TipRotation, about the solved forearm's own long
        /// axis (0 = the hand was already inside the envelope). This is the hand roll the controller asked
        /// for and did not get; it is the ONLY thing the bound changes.</summary>
        public float WristAxialGuardDeg;
        /// <summary>The side of its circle the elbow anatomy guard put the elbow on (-1 / +1), to carry into
        /// next frame's BasisArmSolveInput.PrevGuardSide. 0 means the guard declined or was inside its
        /// envelope this frame, so the caller stores 0 and the next firing frame re-seeds rather than
        /// holding a side chosen under conditions that no longer apply.</summary>
        public int GuardSideUsed;
    }

    // Stream-free geometry shared by BasisFullIKConstraintJob.SolveTwoBoneIKArms and the
    // offline sweep harness. Change the elbow math HERE so both stay in lock-step.
    public static class BasisArmSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        // ⚠️ THERE IS NO ELBOW-LEVER FADE WINDOW ANY MORE, AND THE CONSTANTS THAT DEFINED ONE ARE GONE.
        // A truncated sentence describing them survived their deletion and sat here reading like live
        // documentation ("Start = sqrt(0.001), the exact threshold the old boolean gate cliffed at, so the
        // fade only"). Both fades -- projNorm on |ahProj| and bendNorm on |abProj| -- were removed for the
        // reasons set out at the hintFade site below, and the sqrt(0.001) threshold went with them: the
        // surviving admission test is k_SqrEpsilon, a genuine singularity check three orders of magnitude
        // smaller. Nothing in this file fades on a lever arm now. The tracker pole is CONDITIONED instead,
        // by TrackerPoleAnchorFrac / TrackerPoleTrustFrac, which is a different mechanism with a different
        // justification -- see the pole-anchor note below.

        // Anatomical elbow flexion range, as the angle at the elbow between the upper arm and the forearm.
        // 180 deg = arm straight; small = forearm folded toward the upper arm. A human elbow cannot
        // hyperextend past straight, nor fold the forearm fully into the upper arm (~25-30 deg is the limit).
        public const float MinElbowAngleDeg = 23f;

        // =============================================================================================
        // ⭐ WHY THIS IS 170 AND NOT 180, AND WHY IT IS THE MOST IMPORTANT CONSTANT IN THIS FILE.
        //
        // AT 180 DEGREES THE ELBOW HAS NO LEVER ARM. Its distance from the shoulder->hand axis is
        //
        //     rho = sqrt(upper^2 - (d/2)^2),   d = 2*L*sin(theta/2)
        //
        // and at theta = 180 that is EXACTLY ZERO. The elbow lies ON the axis. A pole can then no longer
        // POSITION it -- every direction on its circle is the same point -- so everything the hint, the
        // swivel, the anatomy guard and the elbow protect ask for is paid out as ROLL of the upper arm
        // and forearm about their own long axis. A dead-straight arm is a FREE-SPINNING STICK, and that
        // is what "the elbows are messing up" looks like.
        //
        // AND THE ARM LIVES THERE. A relaxed human arm hangs at ~170-175 deg, which is already 99.6% of
        // full reach. Any target at or past the avatar's reach is clamped by TriangleAngle to a flat 180.
        //
        // ⚠️ AND YOU CANNOT CALIBRATE YOUR WAY OUT OF IT. Reach is STATIONARY in the elbow angle near
        // straight (dr/dtheta -> 0), so the mapping from a length error to an angle error EXPLODES:
        //
        //         1 mm of arm-length error  ->  the elbow is already 6.6 deg away from where it should be
        //         2 mm                      ->  9.4 deg
        //         0 mm (a PERFECT calibration) -> the target sits exactly AT reach, theta = 180, rho = 0
        //
        // A person T-poses with ~10 deg of residual elbow flexion, so a straight-line span under-measures
        // their true arm by ~2.3 mm on its own. The singularity sits exactly where the user wants to
        // reach, so no calibration -- however good -- keeps the arm off it.
        //
        // ⭐ BUT THE SAME STATIONARITY MAKES THE FIX ALMOST FREE, IN REVERSE:
        //
        //         180 -> 170 deg  costs  2.3 MILLIMETRES of reach
        //                         buys   2.61 CENTIMETRES of guaranteed lever arm      (a 12x trade)
        //
        // rho can now never be zero, so the pole always has something to push on, and the roll
        // singularities this codebase has chased through three separate files become UNREACHABLE:
        // they all live below rho ~ 1.3 cm (extension >= 0.999), and 170 deg floors rho at 2.61 cm.
        //
        // It is also the more ANATOMICAL number. A human elbow does not lock out at 180 under load, and
        // an avatar whose arm snaps dead straight reads as robotic. 10 deg of flex is what a real arm
        // does at full stretch.
        // =============================================================================================
        public const float MaxElbowAngleDeg = 170f;

        // Hand roll the forearm+wrist render entirely on their own. Anatomical pronation/supination is
        // ~80 deg each way from the thumbs-up neutral, so palm-flat either way sits right at the edge and
        // any roll past it belongs to the upper arm.
        public const float WristRollComfortDeg = 80f;

        // Where humeral recruitment BEGINS. Zero relief below this; a quadratic ramp from here reaches
        // slope 1 exactly at the comfort band, so the handover is C1 and the elbow cannot buzz on wrist
        // jitter. A flat in-band share was tried first and the corpus refuted it: 0.2x of the wrist's roll
        // fed the elbow high-frequency motion the human never made (09_01 buzzed 1.52%L above 8 Hz, gate
        // 0.5), and the humans' own elbows demonstrably do not follow in-band roll at a constant gain. They
        // recruit NEAR THE LIMIT -- which is what this ramp is.
        public const float WristRollRampStartDeg = 55f;

        // Hard bound on the relief swivel, so a pathological target (avatar/controller mismatch) is a bounded
        // wrong answer instead of an unbounded one. The anatomy guard still owns the outcome.
        public const float WristRollMaxReliefDeg = 70f;

        // =============================================================================================
        // ⭐ THE WRIST HAS NO ACTIVE AXIAL DEGREE OF FREEDOM. 5 / 15 IS PASSIVE COMPLIANCE UNDER GRIP.
        //
        // The radiocarpal joint does flexion/extension and radial/ulnar deviation. It has NO active axial
        // rotation at all -- what feels like "twisting your wrist" is the RADIUS CROSSING THE ULNA, in the
        // forearm, which is why every absorber above this one is a forearm or a humerus and none of them is
        // a wrist. What the carpus does have is PASSIVE compliance: ~17 +/- 8 deg of supination and
        // ~17 +/- 10 deg of pronation (n = 20, CT), reaching ~42 deg fully relaxed -- and COLLAPSING TOWARD
        // ZERO AS GRIP FORCE RISES. A user holding a controller is at the gripped end of that range, so the
        // envelope is the gripped one and not the relaxed one: soft +/-5, hard +/-15.
        //
        // ⭐ AND THE SMALLNESS OF THE BOUND IS WHY THIS STAGE CAN BE BOTH HARD-BOUNDED AND CONTINUOUS,
        // WHERE ITS THREE SIBLINGS ABOVE COULD NOT. All four stages face the same principal-angle seam, but
        // the humeral guard (150), the tracker forearm roll (120) and the no-tracker forearm clamp (80) all
        // have to travel from their bound back to the seam at 180 in 30-100 deg, which is why each of them
        // buys continuity by RELAXING its bound near +/-180 and says so. This one has 165 deg of room to make
        // the same trip, so `min(bound, PI - mag)` costs it a slope of 15/165 = 0.09 and the hard bound is
        // enforced EVERYWHERE, seam included. Measured worst applied roll over the 7,884-pose envelope sweep:
        // 13.76 deg (no-tracker) and 14.41 deg (tracker), both strictly inside 15.
        // =============================================================================================
        public const float WristAxialSoftDeg = 5f;
        public const float WristAxialHardDeg = 15f;

        // Tracker-path forearm roll: how much say the HAND's roll demand gets against the tracker's own
        // measured roll. The tracker is strapped to the forearm, so its roll IS pronation, measured; the
        // hand is where the demand actually ends. Half way keeps a slipped strap and a mis-gripped
        // controller from each owning the forearm outright.
        public const float TrackerRollHandBlend = 0.5f;

        // Bound on the applied forearm roll vs the animated pose. Full pronation from a mid-range animated
        // wrist is ~90-100 deg; past 120 the signal is a slipped strap or a calibration wreck, and a bounded
        // wrong forearm beats an unbounded one.
        public const float TrackerForearmRollMaxDeg = 120f;

        public const float TrackerForearmRollSoftDeg = 90f;

        // The measured twist is a principal angle, so +180 and -180 are the SAME hand pose with opposite
        // relief signs. Fade the relief out approaching the seam so both sides meet at zero -- the exact
        // wrap pop BasisElbowFlareCore documents. Real wrists cannot reach here; only mismatch can.
        const float k_WristWrapFadeStartDeg = 155f;
        const float k_WristWrapFadeEndDeg = 178f;

        // =============================================================================================
        // ⭐ A MEASURED POLE COLLAPSES; A COMMANDED ONE DOES NOT. THAT IS THE WHOLE DIFFERENCE.
        //
        // The block below asserts that the pole is commanded and so never needs conditioning. That is
        // true of BasisArmSwivelModel's pole, which is built perpendicular to shoulder->hand BY
        // CONSTRUCTION and measures a flat 180 mm at every extension from 0.90 to 1.05. It is FALSE of a
        // real elbow tracker: that pole is the user's own elbow, and when they straighten their arm their
        // elbow lies ON the shoulder->hand axis, so |ahProj| genuinely goes to zero. Measured on the live
        // core at 99.9% extension with 1 mm of puck noise: |ahProj| 13.36 mm, and the swivel flips 179°
        // on 72 of 400 frames. Identical pole with HintIsTracker false: 0.13°, zero flips.
        //
        // The gain is exact and it is not a gate crossing -- d(swivel) = d(pole) / |ahProj|, verified to
        // two decimals against prediction. So the epsilon at the admission test never fires (the failure
        // regime sits at ~1.25 mm, twelve times above it) and tightening it would only restore the old
        // drop-cliff. MaxElbowAngleDeg floors the OTHER lever arm: |abProj| never falls below 26.15 mm in
        // the same frames that |ahProj| reaches 1.25 mm.
        //
        // ⚠ AND THE ONSET IS SET BY THE USER'S ARM, NOT THE AVATAR'S. At a user/avatar arm ratio of 0.98
        // -- an ordinary proportion mismatch -- the flip starts at 98% of avatar extension, not 99.9%.
        //
        // The fix cannot be a fade toward the animated bend (that is the old hintFade, which parked the
        // elbow on an idle pose the user is not performing) nor toward world-down (that is the
        // stabilizer below, which measures perfectly clean here precisely BECAUSE it stops the elbow
        // following the tracker at all -- swivel drops to 0.00°). It has to keep the measurement. So the
        // pole is ANCHORED: the last well-conditioned pole direction is carried forward through the
        // tracker's OWN rotation since it was stored. A puck strapped to the forearm still knows which
        // way the elbow points when the arm is straight -- its position degenerates, its orientation
        // does not. With no usable HintRotation the carry is identity, which is a plain hold.
        // =============================================================================================
        public const float TrackerPoleAnchorFrac = 0.03f;
        public const float TrackerPoleTrustFrac = 0.12f;

        // =============================================================================================
        // ⭐ NOTHING IN THIS CODEBASE BOUNDED HUMERAL AXIAL ROTATION, AND IT SHOWED.
        //
        // The clavicle and the upper arm were never read together: no code computed the humerus relative
        // to its own parent. Measured in Unity with an elbow tracker on an extended arm, rolling the
        // tracker rolls the humerus 1:1 THROUGH A FULL 360 DEGREES inside a clavicle that never moves --
        // BasisShoulderSolveCore is direction-driven, and a pure roll changes no direction, so the girdle
        // contributes exactly 0.0 deg and never engages. Bounded elsewhere: elbow height, elbow flexion,
        // forearm roll, wrist relief, clavicle-vs-chest. Not this.
        //
        // ⚠️ THE VALUE IS 120/150 AND IT CAME FROM A SEGMENTED VETO, NOT A POOLED ONE. 90 deg looked
        // defensible on pooled corpus data and breached the house 3% firing precedent in TWO elevation
        // bands once split: 4.2% at 60-90 deg and 4.8% at 90-120 deg. Pooling hid it. At 120/150 the guard
        // fires <=2.5% in every band at every extension. It is a WHOLE-COMPLEX bound, deliberately looser
        // than a true glenohumeral one (clinical GH arc is ~48/71/40 deg at 45/90/135 abduction) because
        // this rig's girdle does not move: with a frozen clavicle the humerus is absorbing the girdle's
        // share, so clamping to true GH values would tear the hand off the controller.
        //
        // ⭐⭐ IT IS MEASURED AND APPLIED ABOUT SHOULDER->ELBOW, THE HUMERUS'S OWN AXIS -- NOT ABOUT
        // SHOULDER->HAND. Those coincide only at full extension and are ORTHOGONAL at 90 deg of elbow
        // flexion, where a shoulder->hand swivel is almost pure elbow repositioning and almost zero twist.
        // The correct axis is also strictly cheaper: the elbow LIES ON it, so the correction moves the
        // elbow 0.000 mm (a shoulder->hand swivel moved it 1.6 cm) and cannot fight a measured elbow
        // tracker. An earlier draft widened the limit with the elbow's lever arm to avoid exactly that
        // fight; that term is gone, and removing it closed a measured leak at 160-165 deg where the
        // residual reached 152.4 deg.
        // =============================================================================================
        public const float HumeralTwistSoftDeg = 120f;
        public const float HumeralTwistHardDeg = 150f;

        // The reference is built with FromToRotation, which returns an arbitrary WORLD axis when its
        // inputs are antiparallel. Fade the guard out approaching that. Corpus worst real
        // clavicle->humerus angle is 143.5 deg, so this costs nothing real.
        public const float TwistSwingFadeStartDeg = 150f;
        public const float TwistSwingFadeEndDeg = 170f;

        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;
            // default(Quaternion) is the ZERO quaternion, and the runtime multiplies MidPostRoll into the
            // forearm unconditionally -- it must be a rotation on every path out of this method.
            r.MidPostRoll = Quaternion.identity;

            Vector3 aPosition = i.Shoulder;
            Vector3 bPosition = i.Elbow;
            Vector3 cPosition = i.Hand;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;

            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            // Segment vectors (rest pose)
            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            Vector3 atCorrected = tPosition - aPosition;
            float acLen = ac.magnitude;

            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            float atCorrectedLen = atCorrected.magnitude;
            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);

            // Clamp to the anatomical elbow flexion range. The triangle solve already caps extension at
            // straight (180 deg); the lower clamp stops the forearm folding impossibly far into the upper
            // arm for a too-close target -- the joint holds at min flex and the hand falls short (pushed
            // out along the target direction) instead of bending the elbow past human range.
            newAbcAngle = Mathf.Clamp(newAbcAngle, MinElbowAngleDeg * Mathf.Deg2Rad, MaxElbowAngleDeg * Mathf.Deg2Rad);

            // Bend in the ARM plane. Cross(ab,bc) is the shoulder-elbow-hand plane normal, which the
            // triangle solve REQUIRES so deltaR changes |ac| to exactly the target distance. Seeding this
            // axis from the hint (as the leg does) tilts it off the arm plane, so deltaR rotates the hand
            // out of plane and it over/undershoots the target. The hint instead follows via the swivel
            // (hintR) below, which rotates about the shoulder->hand axis and so preserves reach exactly.
            // Stable-down-pole / hint / shoulder->target / player-up are collinear-only fallbacks (arm near-straight).
            byte axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                // Arm dead straight: the bend plane is undefined. Bend toward a STABLE pole (the elbow hangs
                // down, perpendicular to the shoulder->hand axis) FIRST, so a fully-stretched arm settles
                // instead of thrashing between the collinear fallbacks below -- which, reaching BACKWARD, are
                // themselves near-parallel to the backward forearm and so flip the bend plane frame-to-frame.
                Vector3 straightArm = ac.sqrMagnitude > k_SqrEpsilon ? ac : bc;
                if (straightArm.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 saN = straightArm.normalized;
                    Vector3 downPole = -i.PlayerUp - saN * Vector3.Dot(-i.PlayerUp, saN);
                    axis = Vector3.Cross(downPole, bc);
                    axisSource = 4;
                }

                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = i.HintWeight ? Vector3.Cross(i.HintPosition - aPosition, bc) : Vector3.zero;
                    axisSource = 1;
                }
                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = Vector3.Cross(atCorrected, bc);
                    axisSource = 2;
                }

                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = i.PlayerUp;
                    axisSource = 3;
                }
            }
            axis = axis.normalized;

            // =============================================================================================
            // ⚠️ Vector3.normalized RETURNS THE ZERO VECTOR, NOT NaN, WHEN ITS INPUT IS DEGENERATE -- and the
            // quaternion below is built COMPONENTWISE from whatever it returns. With every joint coincident
            // AND PlayerUp zero, the whole fallback chain above is degenerate too, so `axis` is exactly zero
            // and deltaR came out as
            //
            //      (0, 0, 0, cos)  =  (0, 0, 0, 0.97992)   -- a SCALED identity, NOT a rotation
            //
            // (cos is not 1 because the elbow angle was clamped up to MinElbowAngleDeg, so `a` is not zero).
            // The runtime multiplies MidDelta into the forearm unconditionally, and a non-unit quaternion
            // there does not merely rotate the bone wrongly: `q * v` scales v by |q|^2, so the arm SHRINKS.
            // This is the one place in this solver where a zero axis reached a quaternion.
            //
            // Written reject-unless-good, so a NaN in the geometry -- which makes `magnitude` NaN, which
            // fails normalized's own `m > 1E-05f` test and so also returns zero -- takes the decline branch
            // rather than a bone. Identity is the correct decline: there is no bend plane, so there is no
            // bend, and MidDelta being the exact identity makes the stream's mid.SetRotation an exact no-op.
            // =============================================================================================
            Quaternion deltaR = Quaternion.identity;
            if (axis.sqrMagnitude > 0.5f)   // normalized is either a unit vector or exactly zero
            {
                float a = 0.5f * (oldAbcAngle - newAbcAngle);
                float sin = Mathf.Sin(a);
                float cos = Mathf.Cos(a);
                deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            }

            // mid.SetRotation(deltaR * midRot): tip rotates about the elbow pivot.
            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            // --- rotate root toward the corrected target direction ---
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = BasisQuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                // Propagate root rotation to its children (mid + tip), pivoting about A.
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            float hintFade = 0f;
            float swivelUsedRad = 0f;   // how much of the per-frame swivel budget the hint has already spent
            float hintProjMag = 0f;
            float armProjMag = 0f;
            float poleCondW = 1f;
            if (i.HintWeight)
            {
                // Original keeps the pre-root |ac|^2 here; rootDelta is a pure rotation so the
                // magnitude is unchanged and acNorm below stays correctly normalized.
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    Vector3 elbowDir = Vector3.Cross(acNorm, rootDelta * axis);
                    elbowDir -= acNorm * Vector3.Dot(elbowDir, acNorm);
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;

                    // Last frame's anchor, carried onto this frame's tracker rotation. Raw = as carried;
                    // `anchorCarried` = the same vector projected onto this frame's swing plane, which is the
                    // form the swivel reads. Filled in by the anchor block below and consumed by the swivel.
                    Vector3 anchorCarriedRaw = Vector3.zero;
                    Vector3 anchorCarried = Vector3.zero;
                    bool hasAnchorCarried = false;

                    if (i.HintIsTracker && totalLen > k_Epsilon)
                    {
                        poleCondW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                            (hintProjMag / totalLen - TrackerPoleAnchorFrac) / (TrackerPoleTrustFrac - TrackerPoleAnchorFrac)));

                        bool poleMeasurable = ahProj.sqrMagnitude > k_SqrEpsilon;

                        // The anchor is a PAIR: a pole direction and the tracker rotation it was measured at.
                        // Read together they are a calibration -- "which way the elbow points, in the puck's
                        // own frame" -- so carrying it onto this frame's tracker rotation is exact, and a
                        // rigid puck that the user rolls carries the pole with it perfectly. Computed here,
                        // once, and handed to the swivel below so the two can never disagree about it.
                        if (i.HasPrevPole)
                        {
                            Quaternion carryRot = Quaternion.identity;
                            float prevRotSqr = i.PrevHintRotation.x * i.PrevHintRotation.x + i.PrevHintRotation.y * i.PrevHintRotation.y
                                             + i.PrevHintRotation.z * i.PrevHintRotation.z + i.PrevHintRotation.w * i.PrevHintRotation.w;
                            float curRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                                            + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
                            if (prevRotSqr > 0.5f && curRotSqr > 0.5f)
                            {
                                carryRot = i.HintRotation * Quaternion.Inverse(i.PrevHintRotation);
                            }

                            anchorCarriedRaw = carryRot * i.PrevPoleDir;
                            anchorCarried = anchorCarriedRaw - acNorm * Vector3.Dot(anchorCarriedRaw, acNorm);
                            hasAnchorCarried = anchorCarried.sqrMagnitude > k_SqrEpsilon;
                        }

                        if (poleMeasurable && (poleCondW >= 1f || !i.HasPrevPole))
                        {
                            r.PoleDirUsed = ahProj / hintProjMag;
                            r.PoleRotUsed = i.HintRotation;
                            r.PoleAnchorValid = true;
                        }
                        else if (i.HasPrevPole)
                        {
                            r.PoleDirUsed = i.PrevPoleDir;
                            r.PoleRotUsed = i.PrevHintRotation;
                            r.PoleAnchorValid = true;

                            // ==============================================================================
                            // ⭐ A STALE ANCHOR MUST STILL BE ABLE TO BE WRONG, AND THEN GET BETTER.
                            //
                            // THE DEFECT THIS CLOSES, MEASURED. The refresh above fires only at poleCondW >= 1.
                            // For the hint THE LIVE RIG ACTUALLY SENDS -- the calibrated lower-arm BONE, i.e.
                            // the joint centre, stand-off ~= 0 (BasisLocalRigDriver, ApplyHintBias deliberately
                            // not applied) -- |ahProj| IS the elbow's own lever arm, rho = 0.5*L*sqrt(1-reach^2),
                            // so poleCondW reaches 1 only at an elbow angle of 152.2 deg (reach 0.971) and is
                            // 0.06 at 170 deg. ABOVE THAT THE ANCHOR WAS NEVER REFRESHED AT ALL, for as long as
                            // the user kept their arm near-straight -- which is where a VR user lives.
                            //
                            // That alone would be survivable, because the carry above is exact. What is not is
                            // the SEED: BasisFullBodyIK zeroes swingPoleAnchorInit on any non-tracker frame, so
                            // a dropout forces the !HasPrevPole branch, and that branch takes ahProj/|ahProj|
                            // REGARDLESS of conditioning. Re-acquire while the arm is straight and the anchor is
                            // seeded from a noise-length vector -- a uniformly random direction -- and then held.
                            // MEASURED, 12 noise seeds, dropout at full extension then a relaxed 164 deg arm
                            // held for 300 frames: the elbow ends 1.9 to 97.4 deg off the tracker's actual pole
                            // (median ~60) AND STAYS THERE. That is "the elbow parks in an arbitrary roll".
                            //
                            // ⚠️ THE SEED CANNOT SIMPLY BE GATED ON CONDITIONING. At poleCondW = 0 there is no
                            // other azimuth signal in the frame, and refusing to seed leaves HasPrevPole false,
                            // which switches the anchor off entirely and restores the 179 deg flips
                            // BasisArmPoleAnchorTests gates (its ext 1.000 case seeds from rho = 0 and NEEDS to).
                            // Seeding from the animated bend instead was tried on paper and fails
                            // AtFullExtension_TheElbowStillFollowsTheTracker: the carry would then track the
                            // animation's swivel, not the user's. A stable-but-arbitrary anchor really is the
                            // least-bad thing to hold at zero conditioning.
                            //
                            // So the seed stays, and the anchor is made CORRECTABLE instead: ease it toward the
                            // measurement at the measurement's OWN trust weight. Weight poleCondW makes the
                            // stored anchor a first-order filter whose azimuth is exactly the swivel this frame
                            // pays out (the blend below is anchorSwivel + poleCondW * dSwivel, and dSwivel is
                            // this same angle), so the anchor is literally "where the elbow now is" rather than
                            // a second, drifting quantity.
                            //
                            // ⭐ WHY THIS CANNOT REINTRODUCE THE FLIPS. The gain is poleCondW, so:
                            //   * at poleCondW = 0 (reach > 0.998 -- the hand within 1.2 mm of full stretch, and
                            //     every pose BasisArmPoleAnchorTests measures) this is EXACTLY the old hold,
                            //     bit for bit: the branch does not execute.
                            //   * at poleCondW = 1 the first branch above already owned the frame, unchanged.
                            //   * in between it is an EMA with time constant 1/poleCondW frames, whose noise
                            //     transfer is sqrt(w/(2-w)) -- 0.52 at w = 0.43, 0.18 at w = 0.06. It ATTENUATES
                            //     the measurement rather than following it, which is the whole point of the
                            //     anchor; refreshing fully at poleCondW > 0 instead would hand the raw
                            //     measurement straight back and is what the flips were.
                            // The measurement is independent of the anchor, so there is no feedback loop here,
                            // just a stable low-pass.
                            //
                            // The ease is applied as a rotation ABOUT acNorm of the UNPROJECTED carried vector,
                            // not a lerp of two directions: it moves only the azimuth, leaves the axial
                            // component alone, and so re-expressing the pair at this frame's tracker rotation
                            // is an exact identity rather than a slow erosion of the stored calibration.
                            // ==============================================================================
                            if (poleMeasurable && poleCondW > 0f && hasAnchorCarried)
                            {
                                float ease = SignedAngleRad(anchorCarried, ahProj, acNorm) * poleCondW;
                                r.PoleDirUsed = (AngleAxisRad(ease, acNorm) * anchorCarriedRaw).normalized;
                                r.PoleRotUsed = i.HintRotation;
                            }
                        }
                    }

                    // ==========================================================================================
                    // ⭐ THE POLE IS COMMANDED. IT IS OBEYED. THERE IS NOTHING HERE TO FADE.
                    //
                    // This block used to hold TWO fades, and between them they were the reason a real elbow
                    // tracker inverted:
                    //
                    //   projNorm  faded on |ahProj| -- how far the POLE stands off the shoulder->hand axis.
                    //   bendNorm  faded on |abProj| -- how far the CURRENT ELBOW stands off that same axis.
                    //
                    // BOTH of those collapse to zero as the arm STRAIGHTENS, and a VR user holds their arms
                    // mostly straight: 57% of the corpus sits above 95% extension. Measured on a 0.6 m arm with
                    // a real tracker, the authority the solver granted it was
                    //
                    //      94% extension -> 1.00     98.5% -> 0.62     99.5% -> 0.21     99.9% -> 0.00
                    //
                    // At partial authority the elbow does not land on the tracker and it does not land on the
                    // animation -- it lands BETWEEN them, at a point that depends on the noisy, collapsing
                    // abProj. Cross the threshold and it snaps from one to the other. THAT is "the elbow
                    // inverts with a real tracker attached", and "it is not very human movement": the elbow was
                    // being blended toward an idle animation the user is not performing.
                    //
                    // THE FADES WERE GUARDING THE WRONG QUANTITY. What goes to noise as the limb straightens is
                    // abProj -- the MEASURED direction of the current elbow, whose lever arm is vanishing. The
                    // POLE does not: it is COMMANDED, by a tracker or by the swivel model, and it stays
                    // perfectly well-defined at every extension. And because this swivel rotates the elbow ONTO
                    // the pole, THE ELBOW'S FINAL DIRECTION DOES NOT DEPEND ON WHERE IT STARTED.
                    //
                    // BasisLegSolveCore worked this out and deleted its copy long ago, and its comment says so
                    // in as many words -- "the POLE is commanded, BY A TRACKER OR BY BendNormal". The leg has
                    // shipped without these fades ever since. The arm's survived, and it is what has been
                    // breaking the elbow.
                    //
                    // What the deletion left behind was the SECOND half of the same defect, and it is why the
                    // angle below is measured from elbowDir and not from abProj. Deleting the fade correctly
                    // stopped abProj's collapse from stealing the pole's AUTHORITY -- but the swivel ANGLE was
                    // still being read off abProj's DIRECTION, and that direction is gone at full extension.
                    // The resulting rotation is about acNorm, which at full extension IS the upper arm's own
                    // long axis, so a noisy angle is not a harmless "wrong twist for one frame" -- it is the arm
                    // ROLLING, at full magnitude, on a limb whose elbow has no lever arm left to show it.
                    // Measured on a 0.6 m arm: a 1 mm nudge of the hand target rolled the upper arm and the
                    // forearm 180 deg, with the hand still on target. The median roll at 99.95% extension was
                    // 20 deg. A VR user reaching or pointing lives there -- 57% of the corpus is past 95%.
                    //
                    // elbowDir is that same direction RECONSTRUCTED from the plane the bend just used, via the
                    // identity Cross(ac, Cross(ab, bc)) == |ac|^2 * abProj. The bend axis is perpendicular to ac
                    // by construction, so the cross product is a UNIT vector at every extension: identical to
                    // abProj wherever abProj exists, and still there when it does not. See BasisLegSolveCore,
                    // which carries the same fix and the same identity.
                    //
                    // And note WHAT the surviving epsilon below guards: ahProj, the POLE. It was never a guard on
                    // the ELBOW, which is why the elbow's collapse ran unnoticed for so long.
                    // ==========================================================================================
                    hintFade = 1f;

                    // ==========================================================================================
                    // ⚠ A NUMERICAL EPSILON, NOT A BEHAVIOURAL GATE. This test used to read
                    //
                    //      ahProj.sqrMagnitude > totalLen * totalLen * 0.001f
                    //
                    // which on a 0.6 m arm is |ahProj| > 1.9 CENTIMETRES -- and it is a BOOLEAN. Below it the
                    // hint was not faded, it was DROPPED, in a single frame, leaving the elbow wherever the base
                    // animation happened to have left it. And |ahProj| is the pole's stand-off from the
                    // shoulder->hand axis, which COLLAPSES as the arm straightens -- so an ordinary extended
                    // reach walked straight through the cliff. Measured by BasisArmTrackerHintAuthorityTests:
                    // the elbow travelled 2582x the hand's own distance in one step as it crossed.
                    //
                    // Geometry alone gets you to about 12x near full extension. Hundreds is not geometry -- it
                    // is a pole being switched off between two frames, which is the definition of a pop.
                    //
                    // The pole's DIRECTION is perfectly well-defined at 1.9 cm; it is well-defined at 1.9
                    // millimetres. It only becomes noise at true numerical zero. So the guard belongs where
                    // BasisLegSolveCore has always had it -- at 1e-8, a genuine singularity check -- and nowhere
                    // else. (And below it, SignedAngleRad returns 0 anyway, so the elbow simply is not swivelled:
                    // a straight arm whose circle has collapsed has nowhere to put its elbow regardless.)
                    // ==========================================================================================
                    if (ahProj.sqrMagnitude > k_SqrEpsilon && elbowDir.sqrMagnitude > k_SqrEpsilon)
                    {
                        // ==========================================================================================
                        // ⛔ UNREACHABLE, AND KEPT ONLY AS THE RECORD OF WHY IT IS NO LONGER NEEDED.
                        //
                        // A near-180 deg bend->hint swivel is direction-ambiguous when applied PARTIALLY: as the
                        // geometry crosses anti-parallel the signed angle flips +179 -> -179, and at half weight
                        // that is a 180 deg elbow swing (the pole flip). At FULL weight the very same flip is a
                        // 0.2 deg no-op, because rotations are periodic. So this committed toward the hint as
                        // the bend neared anti-parallel, where the ambiguity stops being able to hurt.
                        //
                        // ⚠️ IT CANNOT RUN. `hintFade` is assigned the literal 1f a few lines above and nothing
                        // between there and here modifies it, so the entry test is `1f < 1f`. It has been dead
                        // since the two fades were deleted -- which is exactly the change that made it
                        // unnecessary, because the ambiguity it guards against only exists at PARTIAL weight and
                        // there is no longer any partial weight to be at. Deleting it would be behaviourally
                        // identical; it is commented out rather than removed so that anyone who reintroduces a
                        // fade on this path finds the reason this block existed instead of rediscovering the
                        // flip. THE CONSEQUENCE WORTH KNOWING: r.HintFade is therefore a CONSTANT 1.0, and any
                        // test or diagnostic reading it is reading a constant, not a measurement.
                        //
                        // float effFade = hintFade;
                        // if (effFade < 1f)
                        // {
                        //     float denom = Mathf.Sqrt(elbowDir.sqrMagnitude * ahProj.sqrMagnitude);
                        //     float cosBA = denom > k_Epsilon ? Mathf.Clamp(Vector3.Dot(elbowDir, ahProj) / denom, -1f, 1f) : 1f;
                        //     float flipDeg = Mathf.Acos(cosBA) * Mathf.Rad2Deg;
                        //     float commit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((flipDeg - 90f) / 80f));
                        //     // Only commit when the hint is already strong. Committing as it merely emerges
                        //     // (low fade) snaps the elbow off the stable rest bend -- the folded-arm case.
                        //     commit *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hintFade - 0.3f) / 0.25f));
                        //     effFade = hintFade + (1f - hintFade) * commit;
                        // }
                        //
                        // BIT-IDENTICAL, PROVEN RATHER THAN ASSERTED: with the block gone effFade is hintFade,
                        // which is exactly 1f, and `swivelUsedRad = poleSwivel * 1f` is the identity on every
                        // float including NaN and the signed zeroes. Nothing else read effFade.
                        // ==========================================================================================
                        float effFade = hintFade;

                        // Swivel about the shoulder->hand axis BY NAME. The hand LIES on that axis, so a
                        // rotation about it cannot move the hand: reach preservation is structural, holds at
                        // every weight, and the promise made in the bend comment above is finally kept.
                        //
                        // BasisQuaternionExt.FromToRotation(abProj, ahProj) used to build this. It takes its axis
                        // from Cross(from, to), which DOES lie along acNorm in the general case -- but when the
                        // two go anti-parallel it abandons the plane and returns 180 deg about
                        // Cross(from, Vector3.right), an arbitrary WORLD axis, and swinging the arm about that
                        // throws the hand clean off its target. A 12-iteration bisection used to sit right here,
                        // walking the hint back toward identity until the hand came home. Naming the axis
                        // deletes the failure and the search for it together.
                        float poleSwivel = SignedAngleRad(elbowDir, ahProj, acNorm);
                        if (i.HintIsTracker && i.HasPrevPole && poleCondW < 1f && hasAnchorCarried)
                        {
                            // anchorCarried is computed once, in the anchor block above, precisely so the
                            // swivel and the stored anchor can never be carried through different rotations.
                            float anchorSwivel = SignedAngleRad(elbowDir, anchorCarried, acNorm);
                            float dSwivel = poleSwivel - anchorSwivel;
                            if (dSwivel > Mathf.PI) dSwivel -= 2f * Mathf.PI;
                            else if (dSwivel < -Mathf.PI) dSwivel += 2f * Mathf.PI;
                            poleSwivel = anchorSwivel + poleCondW * dSwivel;
                        }

                        swivelUsedRad = poleSwivel * effFade;
                        float swivel = swivelUsedRad;

                        // Rate-limit so the elbow eases toward the pole rather than swinging ~180 deg the frame
                        // the hint crosses sides. Reach is unaffected either way; this only bounds the swivel.
                        // Offline callers pass MaxValue (no clamp).
                        float maxStep = i.HintMaxStepDeg * Mathf.Deg2Rad;
                        if (swivel > maxStep) swivel = maxStep;
                        else if (swivel < -maxStep) swivel = -maxStep;
                        swivelUsedRad = swivel;

                        hintR = AngleAxisRad(swivel, acNorm);

                        rootRot = hintR * rootRot;
                        bPosition = aPosition + hintR * (bPosition - aPosition);
                        cPosition = aPosition + hintR * (cPosition - aPosition);
                        midRot = hintR * midRot;
                        hintApplied = true;
                    }
                }
            }

            // Pole-collapse stabilizer (the BACKWARD full-stretch rapid flip). The live arm solve is
            // stateless and unclamped, so when the hint pole goes near-collinear with the shoulder->hand axis
            // -- as the arm stretches out, worst BEHIND the body where the lookup bend itself points backward
            // along the arm -- the swivel is hypersensitive and the elbow flips rapidly on small hand motion.
            // Ease the elbow toward a STABLE pole (world-down projected onto the swing plane, where it
            // naturally hangs) by a reach-preserving swivel about the shoulder->hand axis, weighted by how
            // collapsed the hint pole is (collapse 1 at projNorm<=0.15, off by 0.30 -- the tuning knob; raise
            // it if a backward flip survives, lower it if forward/up reaches drift). Folded into HintDelta so
            // the runtime applies it; the hand stays exactly on target -- only the ill-conditioned swivel DOF
            // is replaced by a stable attractor. Perpendicular hint poles (forward / up reaches) are untouched.
            // ⚠ NOT WHEN THE ELBOW IS TRACKED. This stabilizer eases the elbow toward WORLD-DOWN, and it was
            // written for the old invented lookup pole, which really could point backward along the arm and
            // flip. A TRACKER is the user's actual elbow. Dragging it toward world-down is the solver
            // overruling a measurement with a guess -- and because it engages on |ahProj|, which collapses as
            // the arm straightens, it engaged hardest exactly where the user spends most of their time. It is
            // half of "the elbow is not doing very human movement": the other half was the fades above.
            //
            // The swivel model does not need it either (its pole is perpendicular by construction, so
            // poleCond == 0.5 and collapse == 0 -- this block has been dead code on that path since the model
            // landed). So it now guards only what it was built for, and nothing else.
            //
            // ==============================================================================================
            // ⛔ AND "WHAT IT WAS BUILT FOR" NO LONGER EXISTS ON THE LIVE RIG. THIS BLOCK GUARDS NOTHING THERE.
            //
            // Read the two conditions together. The enclosing branch is `HintWeight && !HintIsTracker`, so in
            // the shipping rig this is EXACTLY the model path -- there is no third hint source; the lookup pole
            // this stabilizer was written for is deleted. And BasisSwivelHintCore.ArmHint ends
            //
            //      hintPos = shoulder + 0.5f * armLen * bendWorld
            //
            // with bendWorld a UNIT vector built perpendicular to shoulder->hand by construction (its own
            // comment: "bend is perpendicular to tipLocal in the body frame, therefore bendWorld is
            // perpendicular to shoulder->hand in world, exactly"). So |ahProj| = 0.5 * armLen identically,
            // poleCond = 0.5 identically, and collapse = 1 - SmoothStep((0.5-0.15)/0.15) = 0 identically -- at
            // EVERY reach. Measured across 0.20 to 1.20 of extension: poleCond 0.500000, collapse 0.000000,
            // with no exceptions. The `collapse > 0f` test is never true and the body never executes.
            //
            // ⚠️ IT IS NOT DELETED, AND THAT IS DELIBERATE. It is dead on the LIVE path, not unreachable in
            // general: BasisMocapAccuracy's Lookup / LookupNoFlare rows build their hint the same way but from
            // ComputeArmBendFromLookup, whose bend carries no perpendicularity guarantee, so those offline
            // sweeps can and do enter this block. Removing it would silently change published offline numbers
            // while changing nothing a user can see. Leaving the code exactly as it is keeps the live path bit
            // for bit unchanged -- the strongest form of "prove it" available, since nothing was touched.
            // ==============================================================================================
            if (i.HintWeight && !i.HintIsTracker)
            {
                float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;
                // A tracker stand-off floor used to sit here, nested in `if (i.HintIsTracker)`. The enclosing
                // branch is `HintWeight && !HintIsTracker`, so it was unreachable by construction and has been
                // removed rather than left to read as live tracker handling. The tracker path does its own
                // conditioning at TrackerPoleAnchorFrac/TrackerPoleTrustFrac above.
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
                Vector3 acStab = cPosition - aPosition;
                if (collapse > 0f && acStab.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acStabN = acStab.normalized;
                    Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
                    Vector3 elbowPole = (bPosition - aPosition) - acStabN * Vector3.Dot(bPosition - aPosition, acStabN);
                    if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
                    {
                        // Named-axis swivel, exactly as the hint above and for exactly the same reason. This block
                        // advertises itself as "a reach-preserving swivel about the shoulder->hand axis", and with
                        // FromToRotation choosing the axis it was no such thing: an elbow pole opposite world-down
                        // IS the anti-parallel case, and it comes up on precisely the backward full-stretch reaches
                        // this stabilizer exists to rescue. It was throwing the hand off the target it was hired to
                        // protect. Offline temporal callers pass a per-solve cap; the live stateless rig passes
                        // MaxValue (down is a fixed target, so a full stateless swivel onto it settles, not snaps).
                        float stabSwivel = SignedAngleRad(elbowPole, downPole, acStabN) * collapse;

                        // ONE budget, because the elbow swivel is ONE degree of freedom. The hint above and this
                        // stabilizer both spin the elbow about the shoulder->hand axis -- and now that the hint
                        // swivel genuinely preserves reach, the hand does not move between them, so acStabN IS
                        // acNorm and the two angles simply ADD. Giving each its own full HintMaxStepDeg therefore
                        // let the elbow travel at TWICE the rate limit whenever they pulled the same way, which
                        // is a pop by the rate limiter's own definition. Spend what the hint left.
                        float budget = i.HintMaxStepDeg * Mathf.Deg2Rad - Mathf.Abs(swivelUsedRad);
                        if (!(budget > 0f)) budget = 0f;   // NaN-safe
                        if (stabSwivel > budget) stabSwivel = budget;
                        else if (stabSwivel < -budget) stabSwivel = -budget;

                        Quaternion stab = AngleAxisRad(stabSwivel, acStabN);
                        rootRot = stab * rootRot;
                        bPosition = aPosition + stab * (bPosition - aPosition);
                        cPosition = aPosition + stab * (cPosition - aPosition);
                        midRot = stab * midRot;
                        hintR = stab * hintR; // fold into the hint delta the runtime applies
                    }
                }
            }

            // =============================================================================================
            // ⭐ WRIST-ROLL RELIEF: THE HUMERUS ANSWERS FOR WHAT THE FOREARM CANNOT.
            //
            // The tip is written straight to the target rotation, so every degree of controller roll lands
            // in the wrist joint; the twist bones can only redistribute it along the forearm MESH. Nothing
            // upstream reads the hand's roll at all -- the field model is position-only, and a twisted-in-
            // place hand cannot move a position-only elbow. That is "the upper arm stays rigid when I twist
            // my hand". A real arm pronates ~80 deg each way and then ROTATES THE HUMERUS, which with the
            // hand pinned is exactly the swivel DOF: the elbow swings around its circle.
            //
            // The roll is measured against the ANIMATED wrist carried onto the solved forearm
            // (midRot * animMid^-1 * animTip), so whatever swivel the hint has already applied is already
            // relieved before it is measured.
            //
            // ⚠ NOT WHEN THE ELBOW IS TRACKED, and the corpus is why. A tracker is the user's real elbow,
            // and the real humerus already answered: twist your hand and the tracker MOVES -- the avatar's
            // compensation arrives through the measurement. Running the relief on top was measured on the
            // 20-clip corpus in TruthJoint mode: real clips drift their wrist past the band against the
            // carried neutral routinely, so the relief fired constantly and dragged the elbow 3.6-10.1 cm
            // (MEAN, per clip) off an elbow it had been HANDED. A measured pole is not to be second-guessed
            // -- the same law the pole-collapse stabilizer above already obeys.
            //
            // THE SIGN IS AN IDENTITY, NOT A CONVENTION: swivelling the whole arm about shoulder->hand by
            // theta rolls the carried neutral by theta about ~the forearm axis, so the residual twist drops
            // by theta * dot(forearm, ac) -- relief therefore swivels in the SAME signed direction as the
            // measured twist, for either hand, with no handedness flag. (Anatomy agrees: pronating past
            // palm-down flares the elbow up-and-out, supinating past palm-up tucks it in-and-down.) The
            // hand lies on the swivel axis, so reach preservation is structural, and the anatomy guard
            // below still has the last word on where the elbow may actually sit.
            // =============================================================================================
            float tipRotSqr = i.TipRotation.x * i.TipRotation.x + i.TipRotation.y * i.TipRotation.y
                            + i.TipRotation.z * i.TipRotation.z + i.TipRotation.w * i.TipRotation.w;
            if (tipRotSqr > 0.5f && !i.HintIsTracker)
            {
                Vector3 fore = cPosition - bPosition;
                Vector3 acRelief = cPosition - aPosition;
                if (fore.sqrMagnitude > k_SqrEpsilon && acRelief.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                    float twistRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), fore.normalized);
                    r.WristTwistDeg = twistRad * Mathf.Rad2Deg;

                    float rollAbs = Mathf.Abs(twistRad);
                    float rampStart = WristRollRampStartDeg * Mathf.Deg2Rad;
                    float band = WristRollComfortDeg * Mathf.Deg2Rad;

                    // Zero below the ramp; quadratic easing that reaches slope 1 exactly at the band; the
                    // excess past the band 1:1. C1 everywhere, so wrist jitter cannot become elbow buzz.
                    float relief;
                    if (rollAbs <= rampStart)
                    {
                        relief = 0f;
                    }
                    else if (rollAbs <= band)
                    {
                        float t = rollAbs - rampStart;
                        relief = t * t / (2f * (band - rampStart));
                    }
                    else
                    {
                        relief = 0.5f * (band - rampStart) + (rollAbs - band);
                    }

                    float reliefCap = WristRollMaxReliefDeg * Mathf.Deg2Rad;
                    if (relief > reliefCap) relief = reliefCap;

                    // =====================================================================================
                    // ⭐ THE SEAM ENVELOPE. SLOPE 1 BY CONSTRUCTION -- IT REPLACES A 4.6x AMPLIFIER.
                    //
                    // The measured twist is a PRINCIPAL angle, so +180 and -180 are the same hand pose with
                    // opposite relief signs. Something has to bring the relief to zero at the seam or the
                    // elbow jumps 140 deg across it. The old device was a smoothstep fade over the last
                    // 23 deg (155 -> 178) applied to an already-CAPPED 70 deg of relief -- and crushing 70
                    // to 0 in 23 deg is a slope of 3.0 on average and 4.6 at the smoothstep's inflection.
                    //
                    // MEASURED, humeral roll per degree of controller roll, which for a near-straight arm
                    // is what a swivel becomes (the humerus sits 2.6-14 deg off the swivel axis, so ~99% of
                    // it is bone ROLL): 4.56 at a lateral reach, 3.5-4.3 overhead. A smooth input producing
                    // 4x amplified bone roll is wrong whatever the correct pose is -- and it is REACHABLE:
                    // the seam's amplified zone sits at ~85-90 deg of controller roll when the arm is
                    // overhead (vs ~125-135 at a lateral reach, where a real wrist cannot go), while the
                    // CMU corpus shows real wrist pronation reaching a 59 deg median in one elevation bin.
                    //
                    // THE FIX IS NOT TO MOVE THE FADE. Widening or shifting it only relocates the steep
                    // zone, and relocating a discontinuity is this repo's documented way of not fixing
                    // something. Instead the relief is clamped under an envelope that is ALREADY slope 1:
                    //
                    //     seam = PI - |twist|          zero exactly at the seam, gradient -1 in the twist
                    //     relief = min(relief, seam)
                    //
                    // min() of two functions of slope +1 and -1 has slope magnitude 1 EVERYWHERE, so the
                    // amplification is bounded by construction rather than by choosing constants. Below the
                    // crossing (|twist| = 123.75 deg, relief = 56.25) this is the EXACT identity, so the
                    // ramp, the comfort band, the C1 handover at 80 deg and every in-band pose are bit for
                    // bit unchanged -- the corpus-refuted flat share is not being reintroduced. Above it the
                    // relief eases out along the envelope and meets the opposite sign at zero, so the two
                    // sides of one hand pose still agree, which is all the old fade was for.
                    //
                    // WristRollMaxReliefDeg (70) is now unreachable -- the envelope peaks at 56.25 -- and is
                    // kept as a belt-and-braces bound rather than a live tuning knob.
                    // =====================================================================================
                    float seam = Mathf.PI - rollAbs;
                    if (relief > seam) relief = seam;
                    if (!(relief > 0f)) relief = 0f;   // reject-unless-good: NaN lands here, not in a bone

                    if (relief > 0f)
                    {
                        float reliefSigned = twistRad < 0f ? -relief : relief;
                        Quaternion reliefR = AngleAxisRad(reliefSigned, acRelief.normalized);

                        rootRot = reliefR * rootRot;
                        bPosition = aPosition + reliefR * (bPosition - aPosition);
                        cPosition = aPosition + reliefR * (cPosition - aPosition);
                        midRot = reliefR * midRot;
                        hintR = reliefR * hintR;   // fold into the hint delta the runtime applies
                        r.WristReliefDeg = reliefSigned * Mathf.Rad2Deg;
                    }
                }
            }

            // =============================================================================================
            // ⭐ THE ANATOMY GUARD, AND IT IS THE LAST THING THAT HAPPENS.
            //
            // An elbow cannot point at the sky. Measured on 55,140 frames of real human arm motion: THE ELBOW
            // NEVER RISES ABOVE THE SHOULDER, NOR ABOVE THE HAND, WHICHEVER IS HIGHER -- worst violation in the
            // entire corpus, nine millimetres. You cannot lift your elbow over your shoulder while your hand
            // hangs low; the humerus will not do it. See BasisElbowAnatomyCore.
            //
            // The KNEE has had a guard of exactly this status for a long time (BasisLegSolveCore's anterior
            // half-space: "a knee behind that axis is not unnatural, it is anatomically unrepresentable").
            // THE ARM HAD NONE. Nothing whatsoever stopped this solver placing the elbow anywhere on its
            // circle, and "the arms get into rotations that are not possible, the elbows point at the sky" is
            // what that costs.
            //
            // IT GUARDS THE OUTCOME, NOT THE HINT, and that is the whole point of putting it HERE, after
            // everything else has had its say. A guard on the hint would protect the arm from a bad hint. A
            // guard on the RESULT protects it from a bad hint, a mis-strapped elbow tracker, the pole-collapse
            // stabilizer above, and the animated pose the solve started from -- so there is no path by which
            // the arm can end a frame outside the envelope, because this is the end of the frame.
            //
            // It costs no reach, structurally: the correction is a swivel about the shoulder->hand axis, and
            // the hand LIES on that axis. A rotation about a line cannot move a point on that line.
            //
            // ⚠ AND IT IS EVALUATED IN THE TORSO'S FRAME, NOT THE ROOT'S. The law is a statement about the
            // humerus against the shoulder joint, and that joint is bolted to the ribcage -- so "above" means
            // above the CHEST. This used to pass i.PlayerUp, the player ROOT up, which stays vertical when the
            // chest does not: bend at the waist and the guard's ceiling stopped meaning anything. Measured
            // over the 109-clip corpus, the posture tier's worst apparent rise went from 0.0242 (torso) to
            // 0.3723 (root) -- 7.4x the soft margin, 2.5x past the hard asymptote -- and on all 148 of those
            // frames the trunk is 73.7-119.7 deg off vertical and the measurement's SIGN IS FLIPPED. The guard
            // was firing hardest on people bent over with their arms hanging perfectly normally. See
            // BasisElbowAnatomyCore's frame note. Falls back to PlayerUp when no body frame is available
            // (degenerate rig): the pre-fix behaviour, which for an upright torso is the same vector anyway.
            // =============================================================================================
            Vector3 guardUp = i.TorsoUp.sqrMagnitude > k_SqrEpsilon ? i.TorsoUp : i.PlayerUp;
            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(aPosition, bPosition, cPosition, guardUp, totalLen,
                i.ElbowLateralOut, i.PrevGuardSide, out int guardSideUsed);
            r.GuardSideUsed = guardSideUsed;
            if (guardSwivel != 0f)   // exact 0 inside the envelope: legal poses take the untouched path
            {
                Vector3 acGuard = cPosition - aPosition;
                if (acGuard.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acGuardN = acGuard.normalized;
                    Quaternion guard = AngleAxisRad(guardSwivel, acGuardN);

                    rootRot = guard * rootRot;
                    bPosition = aPosition + guard * (bPosition - aPosition);
                    cPosition = aPosition + guard * (cPosition - aPosition);
                    midRot = guard * midRot;
                    hintR = guard * hintR;   // fold into the hint delta the runtime applies
                }
            }

            // =============================================================================================
            // TRACKER FOREARM ROLL: THE MEASUREMENT THE POLE THROWS AWAY.
            //
            // The wrist-roll relief above stands down for a tracker -- the corpus showed second-guessing a
            // measured pole costs centimetres -- but standing down entirely put every degree of hand roll
            // back into the wrist joint, and the wrist PINCHES. The answer was on the arm the whole time:
            // an elbow tracker is STRAPPED TO THE FOREARM, so its rotation carries real pronation, and the
            // solver was using only its position. Roll the forearm to the blend of the tracker's measured
            // roll and the hand's demand, and the wrist keeps only the residual a real wrist would.
            //
            // A PURE ROLL CAN MOVE NO JOINT. The rotation axis is the forearm's own long axis through the
            // elbow: the elbow pivots on it and the hand LIES on it, so the elbow stays exactly on the
            // tracker's pole -- the corpus gates that killed the relief here structurally cannot see this.
            // It runs AFTER the anatomy guard because the guard reads positions, which this does not touch,
            // and the runtime applies MidPostRoll after the root deltas for the same reason: the composition
            // order here and in the stream must be the same, and a roll about the final axis commutes with
            // nothing that comes later.
            // =============================================================================================
            // =============================================================================================
            // THE HUMERAL TWIST GUARD. Runs AFTER the elbow anatomy guard so it reads the final elbow, and
            // touches no positions, so it commutes with everything after it. See the constants above.
            //
            // ⚠️ THE TWIST IS A SIGNED ANGLE BETWEEN VECTORS, NOT A QUATERNION SWING-TWIST. A swing-twist
            // forms the twist as (proj.xyz, q.w) and normalises; at 180 deg q.w -> 0 and that normalisation
            // is ill-conditioned. Measured: at a 180 deg roll the quaternion form read +152.4, computed
            // -16.8, and drove the twist UP to 169.3 -- broken at precisely the pose this guard exists for.
            // The vector form is well-conditioned to 180; only its SIGN is ambiguous there, and both signs
            // give the same magnitude and the same corrective direction, so either branch reduces it.
            // =============================================================================================
            // Set by the humeral twist guard below to the inverse of the roll it put on the ROOT, so the
            // forearm can be taken back off it at the end. Identity means the guard declined or was inside
            // the envelope, and an identity multiply into MidPostRoll is an exact no-op.
            Quaternion humeralTwistUndo = Quaternion.identity;

            float bindClavSqr = i.BindClavicleRotation.x * i.BindClavicleRotation.x + i.BindClavicleRotation.y * i.BindClavicleRotation.y
                              + i.BindClavicleRotation.z * i.BindClavicleRotation.z + i.BindClavicleRotation.w * i.BindClavicleRotation.w;
            float clavSqr = i.ClavicleRotation.x * i.ClavicleRotation.x + i.ClavicleRotation.y * i.ClavicleRotation.y
                          + i.ClavicleRotation.z * i.ClavicleRotation.z + i.ClavicleRotation.w * i.ClavicleRotation.w;
            float bindHumSqr = i.BindHumerusRotation.x * i.BindHumerusRotation.x + i.BindHumerusRotation.y * i.BindHumerusRotation.y
                             + i.BindHumerusRotation.z * i.BindHumerusRotation.z + i.BindHumerusRotation.w * i.BindHumerusRotation.w;
            if (bindClavSqr > 0.5f && clavSqr > 0.5f && bindHumSqr > 0.5f
                && i.BindHumerusDir.sqrMagnitude > k_SqrEpsilon
                && i.BindHumerusRefAxis.sqrMagnitude > k_SqrEpsilon)
            {
                Vector3 liveDir = bPosition - aPosition;
                if (liveDir.sqrMagnitude > k_SqrEpsilon)
                {
                    liveDir = liveDir.normalized;
                    Quaternion carry = i.ClavicleRotation * Quaternion.Inverse(i.BindClavicleRotation);
                    Quaternion restHumerusRot = carry * i.BindHumerusRotation;
                    Vector3 restDir = (carry * i.BindHumerusDir).normalized;

                    float swingDeg = AngleDeg(restDir, liveDir);
                    float swingFade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(TwistSwingFadeStartDeg, TwistSwingFadeEndDeg, swingDeg));
                    if (swingFade > 0f)
                    {
                        Quaternion swingMin = BasisQuaternionExt.FromToRotation(restDir, liveDir);
                        Vector3 refPerp = (swingMin * restHumerusRot) * i.BindHumerusRefAxis;
                        Vector3 livePerp = rootRot * i.BindHumerusRefAxis;
                        refPerp -= liveDir * Vector3.Dot(refPerp, liveDir);
                        livePerp -= liveDir * Vector3.Dot(livePerp, liveDir);
                        if (refPerp.sqrMagnitude > k_SqrEpsilon && livePerp.sqrMagnitude > k_SqrEpsilon)
                        {
                            float twistRad = SignedAngleRad(refPerp, livePerp, liveDir);
                            r.HumeralTwistDeg = twistRad * Mathf.Rad2Deg;

                            float mag = Mathf.Abs(twistRad);
                            if (mag > HumeralTwistSoftDeg * Mathf.Deg2Rad)
                            {
                                float guarded = BasisJointLimitCore.Saturate(mag,
                                    HumeralTwistSoftDeg * Mathf.Deg2Rad,
                                    HumeralTwistHardDeg * Mathf.Deg2Rad);

                                // ======================================================================
                                // ⭐ THE +/-180 SEAM, AND IT WAS AN 80 DEGREE SNAP. MEASURED, NOT ARGUED.
                                //
                                // twistRad is a PRINCIPAL angle, so +179.5 and -179.5 are the same humerus
                                // one degree apart. The correction used to be
                                //
                                //      need = sign(twist) * guarded - twist
                                //
                                // whose OUTPUT is sign(twist) * guarded -- and that maps those two
                                // neighbouring inputs to +140.0 and -140.0, which as poses are 80 degrees
                                // apart. Measured on the live core, sweeping the elbow tracker's roll in
                                // 0.05 deg steps: the upper arm's pose jumped 80.05 deg in ONE step at
                                // every flexion tested (170/160/135/90), while the UNGUARDED control moved
                                // 0.056 deg -- an amplification of ~1400x. The user's report was "if i move
                                // my hint role around im able to get it to flip", and this is a flip that
                                // moving the hint round its circle reaches by construction.
                                //
                                // ⚠️ THE DEFENCE ON RECORD WAS THAT BOTH SIGNS "GIVE THE SAME MAGNITUDE AND
                                // THE SAME CORRECTIVE DIRECTION, SO EITHER BRANCH REDUCES IT". That is true
                                // of the correction's MAGNITUDE and false of the resulting POSE, which is
                                // the only thing the user sees.
                                //
                                // ⭐ AND THE OBSTRUCTION IS TOPOLOGICAL, SO THERE IS NO CLEAN WIN HERE.
                                // The twist lives on a circle; the envelope is an arc. Any map from the
                                // circle onto the arc that is the IDENTITY inside the arc must either be
                                // discontinuous, or traverse the arc's whole 300 deg complement over the
                                // 60 deg illegal wedge -- a 5x amplifier that at 180 deg of twist drives
                                // the humerus to ZERO twist, i.e. 180 deg from the pose being guarded. That
                                // is a worse artifact than the snap, so it is not what this does.
                                //
                                // What it does instead is this file's OWN established answer to a principal
                                // -angle seam, the one the wrist-roll relief already uses: cap the
                                // correction by the distance to the seam.
                                //
                                //      pull  = mag - guarded        the correction, slope +1 in mag
                                //      seam  = PI - mag             zero AT the seam, slope -1 in mag
                                //      pull  = min(pull, seam)
                                //
                                // min() of a +1 and a -1 slope has slope magnitude 1 everywhere, so the
                                // resulting POSE has gain at most 2 -- bounded by construction rather than
                                // by choosing constants, exactly as the wrist seam is. The correction goes
                                // continuously to zero at 180 deg, where the branch genuinely is ambiguous,
                                // so both sides of the seam meet at the same pose and the snap is GONE
                                // rather than relocated. Below the crossing (mag ~ 158.3 deg) the cap does
                                // not bind and this is the EXACT identity, so the soft limit, the
                                // saturation curve and every pose inside the envelope are bit for bit
                                // unchanged.
                                //
                                // ⚠️ THE PRICE, STATED PLAINLY: the hard bound is no longer enforced in the
                                // last ~15 deg before the seam. Guarded twist now runs to 150 deg at
                                // mag = 165 and rises linearly to 180 deg at mag = 180. That is the cost of
                                // continuity and it is not hideable -- the alternative is the 80 deg snap.
                                // ======================================================================
                                float pull = mag - guarded;
                                float seam = Mathf.PI - mag;
                                if (pull > seam) pull = seam;
                                if (!(pull > 0f)) pull = 0f;   // reject-unless-good: NaN lands here, not in a bone

                                float need = (twistRad < 0f ? pull : -pull) * swingFade;
                                Quaternion twistR = AngleAxisRad(need, liveDir);

                                rootRot = twistR * rootRot;
                                hintR = twistR * hintR;

                                // ======================================================================
                                // ⭐ AND THE CORRECTION HAS TO BE TAKEN BACK OFF THE FOREARM, OR IT THROWS
                                // THE HAND UP TO 20 CENTIMETRES OFF THE CONTROLLER.
                                //
                                // twistR is folded into hintR, and BasisFullBodyIK applies HintDelta to the
                                // ROOT: `root.SetRotation(stream, result.HintDelta * root.GetRotation)`.
                                // Setting a parent's rotation in an AnimationStream carries its children
                                // RIGIDLY. Every other swivel folded into hintR is about shoulder->HAND, and
                                // the hand lies on that axis, so carrying the children costs nothing. THIS
                                // one is about shoulder->ELBOW. The elbow lies on it; THE HAND DOES NOT.
                                //
                                // MEASURED by replaying the exact stream composition (MidDelta, RootDelta,
                                // HintDelta, MidPostRoll in the runtime's order; the replay reproduces the
                                // unguarded solve to 1.5e-5 mm, so it is the stream and not a model of it):
                                //
                                //     elbow flexion   170     160     150     135     120      90
                                //     hand moved     35.6    70.2   102.6   145.1   177.7   205.2  mm
                                //
                                // while the solver's own bookkeeping reported 0.000 mm at every one of them,
                                // because the guard updated rootRot and hintR but not cPosition or midRot.
                                // HumeralTwistGuard_MovesNoJoint_AndActsAboutShoulderToElbow asserts the
                                // hand does not move by comparing two numbers the guard never writes, so it
                                // is structurally incapable of failing and reported safety throughout.
                                //
                                // The fix is exact and costs nothing. Relative to the ELBOW the stream turns
                                // the forearm by twistR (the elbow is on the axis, so the axis-parallel part
                                // of the offset is untouched and the perpendicular part rotates), and
                                // MidPostRoll is applied to the forearm AFTER the root deltas and pivots the
                                // tip about that same elbow. So right-multiplying MidPostRoll by
                                // inverse(twistR) restores the forearm's world rotation and the hand's
                                // position to what the unguarded solve had, EXACTLY, leaving a pure humeral
                                // roll -- which is what this guard has always claimed to be. The solver's
                                // untouched cPosition/midRot then become correct rather than merely unwritten.
                                // ======================================================================
                                humeralTwistUndo = Quaternion.Inverse(twistR);

                                r.HumeralTwistGuardDeg = need * Mathf.Rad2Deg;
                            }
                        }
                    }
                }
            }

            float hintRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                             + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
            if (i.HintIsTracker && hintRotSqr > 0.5f)
            {
                Vector3 foreRoll = cPosition - bPosition;
                if (foreRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreRollN = foreRoll.normalized;
                    float trackerRoll = TwistAngleRad(i.HintRotation * Quaternion.Inverse(midRot), foreRollN);

                    float roll = trackerRoll;
                    if (tipRotSqr > 0.5f)
                    {
                        Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                        float handRoll = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), foreRollN);
                        r.WristTwistDeg = handRoll * Mathf.Rad2Deg;

                        // The two signals are principal angles, so their disagreement wraps; and a wrist
                        // 155+ deg from the measured forearm is not anatomy, it is garbage in one of the
                        // two. Fade the hand's say to zero approaching the seam so both sides meet there.
                        float d = handRoll - trackerRoll;
                        if (d > Mathf.PI) d -= 2f * Mathf.PI;
                        else if (d < -Mathf.PI) d += 2f * Mathf.PI;
                        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                            (Mathf.Abs(d) * Mathf.Rad2Deg - k_WristWrapFadeStartDeg) / (k_WristWrapFadeEndDeg - k_WristWrapFadeStartDeg)));
                        roll = trackerRoll + TrackerRollHandBlend * d * fade;
                    }

                    // =====================================================================================
                    // ⭐ THE +/-180 SEAM, FOR THE THIRD TIME IN THIS FILE -- AND THIS IS THE STAGE THAT
                    // NEVER GOT AN ENVELOPE, THOUGH BOTH ITS SIBLINGS CARRY ONE. MEASURED, NOT ARGUED.
                    //
                    // `roll` is a PRINCIPAL angle: it lives on a circle and wraps at +/-180. The applied
                    // roll used to be
                    //
                    //      rollSigned = sign(roll) * Saturate(|roll|, 90, 120)
                    //
                    // and at the wrap the SIGN flips while the MAGNITUDE stays ~180, so the applied roll
                    // jumped 2 * Saturate(180, 90, 120) = 2 * 112.5 = 225 deg -- a forearm pose change of
                    // 360 - 225 = 135 deg. MEASURED on the live core, elbow tracker rolled in 0.1 deg
                    // steps, at tracker roll 81.7:
                    //
                    //     ForearmRollDeg  +109.4  ->  -114.2   (136.39 deg of forearm, ONE step)
                    //     humerus                             0.000 deg   (continuous)
                    //     elbow / hand position               0.0000 mm   (unmoved)
                    //
                    // ⚠️ AND IT BREAKS THE ARM IN TWO PLACES AT ONCE, WHICH IS HOW IT WAS REPORTED. The
                    // jump is a PURE ROLL about the forearm's own long axis, so it moves no joint: only
                    // the bone BETWEEN the elbow and the wrist turns, so the mesh tears at BOTH of its
                    // ends and nothing else in the arm moves. "Straight arm, rotate the arm, it breaks in
                    // two places" IS this, and an elbow tracker is what puts a user on this path.
                    //
                    // Reproduced at reach 0.80 / 0.90 / 0.96 and at controller roll 0 and 90 -- the same
                    // 135-136 deg every time, so it is the algebra and not the geometry.
                    //
                    // ── FIRST, WRAP. `roll` is trackerRoll + TrackerRollHandBlend * d * fade, and the
                    // blend can carry it PAST the seam: |roll| measured up to 214.9 deg on the sweep
                    // above. Past 180 the magnitude and its own principal representative saturate to two
                    // different numbers with opposite signs -- the same defect reached from the hand blend
                    // instead of from the tracker. Both terms are principal angles; their sum is not, so
                    // it is put back on the branch before anything reads its sign.
                    //
                    // ── THEN CAP, exactly as the wrist-roll relief's `seam = PI - rollAbs` and the
                    // humeral twist guard's `seam = PI - mag` already do, ten and thirty lines from here:
                    //
                    //      pull = mag - saturated      the bound's own correction, slope +1 in mag
                    //      seam = PI - mag             zero AT the seam, slope -1 in mag
                    //      pull = min(pull, seam)
                    //
                    // min() of a +1 and a -1 slope has slope magnitude 1 everywhere, so the forearm's gain
                    // is at most 2, bounded by construction rather than by choosing a constant. The
                    // correction goes continuously to zero at 180 deg, where the branch genuinely is
                    // ambiguous, so the applied roll meets +/-180 from both sides -- and +180 and -180 are
                    // the SAME rotation (quaternion double cover), so the two sides of one forearm pose
                    // agree and the snap is GONE rather than relocated.
                    //
                    // ⭐ WHAT IT COSTS AND WHAT IT BUYS, BOTH EXACTLY:
                    //   * |roll| <= 90 (the soft limit): Saturate is the identity and pull is 0. UNTOUCHED.
                    //   * 90 < |roll| <= 144.69 (the crossing, where 2*mag - 180 meets the saturation
                    //     curve): pull < seam, so the cap does not bind and this is the EXACT identity
                    //     with the old saturation -- every ordinary pronation is bit for bit unchanged.
                    //   * |roll| > 144.69: the applied roll eases out along 2*|roll| - 180 and reaches
                    //     180 at the seam.
                    //
                    // ⚠️ THE PRICE, STATED PLAINLY, AND IT IS THE SAME PRICE THE HUMERAL GUARD PAYS: the
                    // 120 deg hard bound is no longer enforced in the last ~35 deg before the seam. That
                    // is the cost of continuity and it is not hideable -- the alternative is the 135 deg
                    // snap. It is BOUNDED: the applied roll can now reach 180 and no further.
                    //
                    // ⚠️ AND IT DOES NOT PUSH THE RESIDUAL INTO THE WRIST -- it takes residual OUT. The
                    // wrist keeps whatever the forearm does not absorb, and at the seam the forearm now
                    // absorbs 180 deg where it used to absorb 112.5. MEASURED over 11,528 poses (a full
                    // turn of the tracker at four reaches and two controller rolls), hand-vs-forearm axial
                    // roll: MEAN 74.6 -> 56.0 deg, forearm 113.9 -> 180.0 deg. Pronation belongs in the
                    // forearm; the radiocarpal joint has no axial degree of freedom at all. And nothing
                    // downstream inherited the jump either -- both twist bones' worst single-step across
                    // the seam is 0.55 / 1.10 deg, because BasisTwistSolveCore carries its own seam cap,
                    // which is the trap the no-tracker sibling fell into (54.10 deg = 360 * 0.15).
                    // Fading the roll to ZERO near the seam
                    // would ALSO be continuous (0 and 180 are the only two continuous choices at the
                    // branch point) and it is rejected for exactly that reason: it puts the whole residual
                    // in a joint that has no axial degree of freedom at all, which is the "5x amplifier
                    // traversing the arc's complement" the humeral guard's comment rejects by name and the
                    // device the no-tracker sibling's comment rejects by name.
                    // =====================================================================================
                    r.ForearmRollDemandDeg = roll * Mathf.Rad2Deg;   // pre-wrap, pre-bound: see the field's note

                    if (roll > Mathf.PI) roll -= 2f * Mathf.PI;
                    else if (roll < -Mathf.PI) roll += 2f * Mathf.PI;

                    // Bound, then apply -- in the reject-unless-good shape, because Mathf.Clamp waves NaN
                    // through and a NaN written to a bone persists.
                    float rollAbs = Mathf.Abs(roll);
                    float rollSat = BasisJointLimitCore.Saturate(
                        rollAbs,
                        TrackerForearmRollSoftDeg * Mathf.Deg2Rad,
                        TrackerForearmRollMaxDeg * Mathf.Deg2Rad);
                    float rollPull = rollAbs - rollSat;
                    float rollSeam = Mathf.PI - rollAbs;
                    if (rollPull > rollSeam) rollPull = rollSeam;
                    if (!(rollPull > 0f)) rollPull = 0f;   // reject-unless-good: NaN lands here, not in a bone
                    rollAbs -= rollPull;
                    if (rollAbs > 1e-6f)
                    {
                        float rollSigned = roll < 0f ? -rollAbs : rollAbs;
                        r.MidPostRoll = AngleAxisRad(rollSigned, foreRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ForearmRollDeg = rollSigned * Mathf.Rad2Deg;
                    }
                }
            }

            // =============================================================================================
            // ⭐ NO-TRACKER FOREARM ROLL: THE ONE DEGREE OF FREEDOM NOTHING WAS SETTING.
            //
            // THE DEFECT, MEASURED IN UNITY AND REFERENCE-FREE. Twist the ANIMATED forearm about its own long
            // axis and NOTHING MOVES: the elbow travels 0.00000 mm, the hand travels 0.00000 mm, and the
            // humerus's solved roll changes 0.000 deg (it is pinned by where the elbow was put). But the
            // SOLVED forearm's roll tracks that animated twist 1:1, to 0.000 deg. The forearm's axial roll is
            // therefore a FREE DOF that this solver never set -- it was inherited verbatim from whatever the
            // animation clip happened to be doing.
            //
            // So the avatar's forearm twist depended on WHICH IDLE CLIP WAS PLAYING. With an elbow tracker
            // MidPostRoll above gives it a measured value; with no tracker nothing did, and the same user
            // pose rendered a different forearm depending on the animator's state. That is half of "unnatural
            // twist, with and without the tracker" and it needs no human-motion reference to call wrong: a
            // pose the user is holding still must not depend on an animation they are not performing.
            //
            // THE NEUTRAL IS THE BIND RELATIONSHIP, AND THAT IS WHY IT IS DEFENSIBLE WITHOUT A CORPUS. Zero
            // pronation is defined as "the forearm sits relative to the humerus exactly as it does in the
            // rig's own bind pose", carried onto the SOLVED humerus. It is a property of the rig, not of a
            // clip and not of a fitted model, so it cannot rot the way a mocap-derived reference can -- and
            // notably it does NOT claim to know what a human's forearm does at any elevation, which is
            // exactly the claim the corpus refused to support (zero frames above 70 deg of hand elevation).
            //
            // Then the wrist is given back what it actually asks for, BOUNDED TO THE COMFORT BAND: real
            // pronation is ~80 deg either way, so anything inside that is the forearm's own business. The
            // excess is already the wrist-roll relief's job further up, and the two compose without
            // double-counting because the relief runs BEFORE this and rotates the whole arm, while this is a
            // pure roll about the forearm's own long axis and so moves no joint -- the elbow stays exactly
            // where the guards left it.
            //
            // Declines to the exact identity unless the rig bakes both bind rotations.
            // =============================================================================================
            float bindLowerSqr = i.BindLowerArmRotation.x * i.BindLowerArmRotation.x + i.BindLowerArmRotation.y * i.BindLowerArmRotation.y
                               + i.BindLowerArmRotation.z * i.BindLowerArmRotation.z + i.BindLowerArmRotation.w * i.BindLowerArmRotation.w;
            if (!i.HintIsTracker && bindLowerSqr > 0.5f && bindHumSqr > 0.5f)
            {
                Vector3 foreRoll = cPosition - bPosition;
                if (foreRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreRollN = foreRoll.normalized;

                    // The forearm at ZERO pronation: the bind forearm-vs-humerus relation on the solved humerus.
                    Quaternion neutralFore = rootRot * (Quaternion.Inverse(i.BindHumerusRotation) * i.BindLowerArmRotation);

                    // What the animation left in the forearm, about its own long axis. This is the arbitrary part.
                    float inherited = TwistAngleRad(midRot * Quaternion.Inverse(neutralFore), foreRollN);

                    float want = 0f;   // no hand feed: pronation is simply defined to be zero
                    if (tipRotSqr > 0.5f)
                    {
                        float demand = TwistAngleRad(tRotation * Quaternion.Inverse(neutralFore), foreRollN);
                        float band = WristRollComfortDeg * Mathf.Deg2Rad;

                        // =====================================================================
                        // ⭐ THE +/-180 SEAM AGAIN, AND HERE IT WAS A 160 DEGREE SNAP OF THE
                        // FOREARM -- THE WORST ONE THIS FILE HAS HAD. MEASURED, NOT ARGUED.
                        //
                        // `demand` comes out of TwistAngleRad, which is a PRINCIPAL angle: it
                        // lives on a CIRCLE and wraps at +/-180. A hard min/max onto the arc
                        // [-band, +band] therefore maps demand = +179.99 to +80 and
                        // demand = -179.99 to -80 -- two HAND POSES 0.02 DEGREES APART sent to
                        // two FOREARMS 160 DEGREES APART. This is the identical topological
                        // defect the wrist-roll relief's seam envelope and the humeral twist
                        // guard's seam cap already fix, twice, above; this block is the newest
                        // of the three and shipped without one.
                        //
                        // ⚠️ AND IT BREAKS THE ARM IN TWO PLACES AT ONCE, WHICH IS EXACTLY HOW
                        // IT WAS REPORTED. The snap is a PURE ROLL about the forearm's own long
                        // axis, so it moves no joint: measured across the step, the elbow and
                        // the hand are identical to 5 decimals and the HUMERUS moves 0.07 deg.
                        // Only the bone BETWEEN them turns -- so the mesh tears at BOTH of its
                        // ends, the elbow AND the wrist, and nothing else in the arm moves.
                        //
                        // MEASURED on the live core, straight arm (170 deg), controller roll
                        // swept in 0.02 deg steps, at roll -167.49:
                        //
                        //     forearm roll   +55.00  ->  -105.00   (160.05 deg, one step)
                        //     elbow junction  +80.00  ->   -80.00
                        //     wrist junction  +99.98  ->   -99.98
                        //     humerus            12.51  ->    12.49   (continuous)
                        //     elbow / hand position        UNCHANGED
                        //
                        // Direction-independent: the same 160.05 deg at the same roll for a
                        // lateral reach and an overhead one, so it is the algebra and not the
                        // geometry. Amplification 3201x at a 0.05 deg step -- for scale, the
                        // humeral guard's snap fixed earlier measured 1429.9x.
                        //
                        // AND A FULL TURN OF THE CONTROLLER CANNOT MISS IT. demand is the hand's
                        // roll measured against a BIND reference that does not follow the hand,
                        // so it sweeps a full 360 as the user does; crossing +/-180 exactly once
                        // per turn is forced by topology. "Straight arm, rotate it" IS the
                        // reproduction.
                        //
                        // THE FIX IS THIS FILE'S OWN ESTABLISHED ANSWER: cap the correction by
                        // the distance to the seam, so it goes continuously to zero where the
                        // branch is genuinely ambiguous.
                        //
                        //     pull = mag - band       the clamp, slope +1 in mag
                        //     seam = PI - mag         zero AT the seam, slope -1 in mag
                        //     pull = min(pull, seam)
                        //
                        // ⭐ WHAT THIS COSTS, AND WHAT IT BUYS, BOTH EXACTLY:
                        //   * |demand| <= band: the branch is not entered. EXACT identity.
                        //   * band < |demand| <= 130 (the crossing, (180+band)/2): pull < seam,
                        //     so the cap does not bind and this is the EXACT identity with the
                        //     old hard clamp -- every in-band and ordinary pose is bit for bit
                        //     unchanged, and the corpus-refuted saturation is not reintroduced.
                        //   * |demand| > 130: want eases out along 2*|demand| - 180, reaching
                        //     180 at the seam -- where +180 and -180 are the SAME rotation
                        //     (quaternion double cover), so both sides meet and the snap is
                        //     GONE rather than relocated.
                        //   * min() of a +1 and a -1 slope has slope magnitude 1 everywhere, so
                        //     the forearm's gain is at most 2x, bounded by construction rather
                        //     than by choosing a constant -- the same argument the humeral
                        //     guard's cap rests on.
                        //
                        // ⭐ AND IT BOUNDS THE WRIST FOR FREE, WHICH NOTHING DID BEFORE. The
                        // wrist keeps the residual demand - want. Under the old hard clamp that
                        // grew without limit and MEASURED 100.0 deg on every no-tracker reach;
                        // a real wrist has ~0 deg of independent axial range, the forearm is
                        // what rotates. Under the cap the residual peaks at 50 deg (at
                        // |demand| = 130) and returns to ZERO at the seam, because want meets
                        // demand there. So the same ten lines that remove the snap also cap the
                        // one junction in this solver that had no bound at all.
                        //
                        // ⚠️ WHY NOT THE ALTERNATIVES. Widening `band` only moves the arc, so
                        // the snap survives at 2*band -- relocating a discontinuity is this
                        // repo's documented way of not fixing one. Fading want to ZERO near the
                        // seam (the tracker path's device below) puts the whole 180 into the
                        // WRIST, which is the worse of the two impossible poses and is the "5x
                        // amplifier traversing the arc's complement" the humeral guard's comment
                        // rejects by name.
                        //
                        // ⚠️ THIS BLOCK USED TO END "clamping the HAND instead would take it off
                        // the controller, which this solver may never do", and the wrist bound at
                        // the end of Solve now does exactly that -- so the claim is corrected here
                        // rather than left to contradict shipping code. What that sentence is right
                        // about is POSITION: the hand's target position is never negotiable, and
                        // the wrist bound moves it 0.000 mm because TipRotation carries no position.
                        // What it is wrong about is treating the hand's axial ROLL as equally
                        // untouchable, because the alternative to bounding it is not "the hand on
                        // the controller" -- it is a hand rotated up to 179.8 deg against its own
                        // forearm, in a joint with no axial range, which is a wrist that has come
                        // off. The residual has to land somewhere and the measured answer is that
                        // by the time it reaches the wrist the forearm and humerus are already past
                        // their own human ceilings (93.1% of breaching tracker poses, by a mean of
                        // 60.9 deg), so there is nowhere left to put it but the controller's roll.
                        // Which is why THIS clamp still matters: every degree the forearm legally
                        // takes here is a degree the wrist bound does not have to drop.
                        // =====================================================================
                        float mag = demand < 0f ? -demand : demand;
                        if (mag > band)
                        {
                            float pull = mag - band;
                            float seam = Mathf.PI - mag;
                            if (pull > seam) pull = seam;
                            if (!(pull > 0f)) pull = 0f;   // reject-unless-good: NaN lands here, not in a bone
                            mag -= pull;
                        }
                        want = demand < 0f ? -mag : mag;
                    }

                    // ⚠ NOT SATURATED, and that is the difference between this path and the tracker's. The
                    // tracker saturates because its INPUT can be garbage -- a slipped strap or a wrecked
                    // calibration -- so a bounded wrong forearm beats an unbounded one. Here the OUTCOME is
                    // already bounded analytically: the forearm lands on `want`, which is clamped to the
                    // comfort band, so no saturation is needed and applying one is a category error. It
                    // saturates the DELTA (want - inherited), and the delta carries the animation's arbitrary
                    // twist -- so saturating it feeds that arbitrariness straight back into the result.
                    // Measured with the tracker's 90/120 saturation wrongly applied here: a 120 deg sweep of
                    // the animated forearm still moved the solved forearm 29.3 deg at overhead, i.e. most of
                    // the defect survived the fix. Without it the residual is under a degree.
                    // `want` is clamped to the comfort band and `inherited` is a principal angle, so a sane
                    // roll is at most band + PI ~ 4.6 rad; 2*PI is a generous sanity bound whose real job is
                    // to be written reject-unless-good so a NaN takes the decline branch instead of a bone.
                    float roll = want - inherited;
                    if (Mathf.Abs(roll) > 1e-6f && roll > -2f * Mathf.PI && roll < 2f * Mathf.PI)
                    {
                        r.MidPostRoll = AngleAxisRad(roll, foreRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ForearmRollDeg = roll * Mathf.Rad2Deg;
                    }
                }
            }

            // The humeral twist guard's counter-rotation on the forearm. RIGHT-multiplied, and the side
            // matters: the stream reaches this point with the forearm at twistR * (whatever the roll blocks
            // above intended), so MidPostRoll must undo twistR FIRST and then apply the intended roll --
            // which is exactly `intended * inverse(twistR)` acting on the left of that product. Identity
            // when the guard did not fire, so every existing caller is bit-identical.
            r.MidPostRoll = r.MidPostRoll * humeralTwistUndo;

            // =============================================================================================
            // ⭐ THE WRIST BOUND, AND IT IS THE LAST THING THAT HAPPENS TO THE HAND.
            //
            // THE DEFECT, MEASURED. This solver ended with `r.TipRotation = TargetRotation * TargetOffset`
            // and the runtime does `tip.SetRotation(TipRotation)` -- the controller's rotation, verbatim,
            // with nothing relating it to the forearm it is attached to. Every degree of roll the forearm
            // and the humerus did not absorb therefore landed in the one joint in the arm that has no axial
            // range at all. Measured on the 7,884-pose envelope sweep, hand-vs-forearm axial roll:
            //
            //      NO-TRACKER path   p50 15.4   p90 43.0   WORST 75.7 deg
            //      ELBOW-TRACKER     p50 88.5   p90 163.1  WORST 179.8 deg   (49.5% of poses past 90 deg)
            //
            // against a gripped-carpus envelope of 15. This is the same shape as the humeral defect fixed
            // earlier in this file -- a DOF driven 1:1 from a device with nothing bounding it, sitting next
            // to several carefully-bounded neighbours -- and it hid the same way: every neighbour asserted
            // its own bound and nothing asserted this one.
            //
            // ⭐⭐ WHY THE RESIDUAL IS DROPPED AND NOT RECRUITED, WHICH IS THE REAL DESIGN QUESTION.
            // The obvious alternative is to make the forearm and the humerus take more before the wrist has
            // to. That was measured on the same sweep, over every pose whose wrist breaches 15 deg, against
            // the researched flexion-dependent forearm ceiling (sup_max = clamp(50 + 0.35*flex, 50, 85),
            // pro_max = clamp(85 - 0.20*flex, 55, 85)) and this file's own 150 deg humeral bound:
            //
            //      ELBOW-TRACKER   the forearm is ALREADY past its human ceiling on 93.1% of them, by a
            //                      MEAN of 60.9 deg and a worst of 105.3
            //      NO-TRACKER      already past it on 40.6% (59.4% against the tighter sign), and the
            //                      humerus is already past its own soft limit on 33.0%
            //
            // There is nothing to recruit INTO. The absorbers are not idle when the wrist breaches, they are
            // over-committed -- so "recruit first" would buy a smaller wrist error by DEEPENING a forearm
            // breach, which is relocating the defect rather than fixing it. (And the humeral half of that
            // recruitment already exists as the wrist-roll relief above; 75.7 deg is what is left AFTER it,
            // and widening it was refuted on the 20-clip corpus for dragging a measured elbow 3.6-10.1 cm.)
            //
            // ⭐ WHAT DROPPING COSTS, MEASURED RATHER THAN ASSERTED. The correction is a pure roll about the
            // forearm's own long axis, so the hand's POSITION is untouched (TipRotation carries none) and
            // its swing -- where the fingers point -- moves only as that roll carries it:
            //
            //                        hand pose err        finger direction        palm facing
            //      NO-TRACKER        p50  5.3  max 61.9   p50 2.06  max 42.8     p50  4.6  max 58.2
            //      TRACKER (real)    p50 15.9  max 84.7   p50 6.05  max 57.1     p50 14.9  max 81.2
            //
            // where "TRACKER (real)" is the tracker rolled WITH the controller, i.e. a puck actually strapped
            // to the arm holding it. What the user loses is palm ROLL; where their hand is and where their
            // fingers point are near enough untouched. Against that, the pose being bought back is a hand
            // rotated up to 179.8 deg against its own forearm, which is a wrist that has come off.
            //
            // ⚠️ AND THE RESIDUAL CANNOT MOVE ANYWHERE ELSE, STRUCTURALLY RATHER THAN BY MEASUREMENT. This
            // block writes r.TipRotation and nothing else. MidDelta, RootDelta, HintDelta, MidPostRoll,
            // every joint position and both solved bone rotations are bit-identical with it and without it,
            // so there is no channel through which a relocated discontinuity could reach the forearm, the
            // twist bones, the elbow or the humerus. That is the one thing the three seams above this could
            // not say about themselves, and it is why this one is measured against `midRot` -- the core's
            // own forearm, which the stream composition reproduces EXACTLY (MidPostRoll carries
            // inverse(twistR), so HintDelta's humeral roll cancels out of the forearm and the two agree to
            // the float).
            //
            // ⚠️ THE SEAM, FOR THE FOURTH TIME IN THIS FILE, AND HERE IT IS CHEAP. `wristRad` is a PRINCIPAL
            // angle, so a bare clamp onto [-hard, hard] sends two hand poses 0.02 deg apart to two hands
            // 2*hard apart. The device is this file's own, the one the wrist-roll relief uses -- cap the
            // BOUND itself by the distance to the seam, not the correction:
            //
            //      bound = min( Saturate(mag, soft, hard),  PI - mag )
            //
            // min() of a curve of slope <= 1 and a line of slope -1 has slope magnitude <= 1 everywhere, so
            // the hand's gain is at most 2 by construction. Note this is the OPPOSITE choice from the three
            // stages above, which cap the PULL and so relax their bound at the seam: capping the pull here
            // would leave the wrist at 180 deg, which is the entire defect. Capping the bound instead keeps
            // 15 deg enforced everywhere and pays for it in hand roll, which is the trade this stage is for.
            // MEASURED, controller roll swept in 0.02 deg steps at three reaches on both paths: worst single
            // -step change of the written hand rotation 0.1101 deg, amplification 1.2-1.7x the sweep's own
            // 95th-percentile step. For scale, the seams this file has already fixed measured 1430x and
            // 3201x.
            //
            // DECLINES to the exact identity unless the rig bakes BindLowerArmRotation -- the same gate, and
            // the same reason, as the no-tracker forearm roll above: without the rig's own bind there is no
            // rig-defined zero for this angle and the solver would be bounding it against one it invented.
            //
            // ⚠️ AND IT DECLINES AGAIN UNLESS ApplyWristAxialBound, WHICH IS DEFAULT OFF (FBIKWristAxialBound).
            // The trade above was measured correctly and rejected in a headset: p50 15.9 deg of hand roll on
            // the tracker path is a hand that visibly does not match the controller the user is holding, and
            // the user is looking straight at both. The MEASUREMENT is kept unconditional -- WristAxialDeg is
            // published either way, so the recorder can still show what the wrist is being asked to do.
            // =============================================================================================
            if (bindLowerSqr > 0.5f)
            {
                Vector3 foreWrist = cPosition - bPosition;
                if (foreWrist.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreWristN = foreWrist.normalized;

                    float bindHandSqr = i.BindHandRotation.x * i.BindHandRotation.x + i.BindHandRotation.y * i.BindHandRotation.y
                                      + i.BindHandRotation.z * i.BindHandRotation.z + i.BindHandRotation.w * i.BindHandRotation.w;
                    Quaternion neutralHand = bindHandSqr > 0.5f
                        ? midRot * (Quaternion.Inverse(i.BindLowerArmRotation) * i.BindHandRotation)
                        : midRot;

                    float wristRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutralHand), foreWristN);
                    r.WristAxialDeg = wristRad * Mathf.Rad2Deg;

                    float mag = wristRad < 0f ? -wristRad : wristRad;
                    float bound = BasisJointLimitCore.Saturate(mag,
                        WristAxialSoftDeg * Mathf.Deg2Rad,
                        WristAxialHardDeg * Mathf.Deg2Rad);
                    float seam = Mathf.PI - mag;
                    if (bound > seam) bound = seam;
                    if (!(bound > 0f)) bound = 0f;   // reject-unless-good: NaN lands here, not in a bone

                    float pull = mag - bound;
                    if (i.ApplyWristAxialBound && pull > 1e-6f)
                    {
                        float need = wristRad < 0f ? pull : -pull;
                        tRotation = AngleAxisRad(need, foreWristN) * tRotation;
                        r.WristAxialGuardDeg = need * Mathf.Rad2Deg;
                    }
                }
            }

            r.MidDelta = deltaR;
            r.RootDelta = rootDelta;
            r.HintDelta = hintR;
            r.TipRotation = tRotation;
            r.HintApplied = hintApplied;

            r.ElbowSolved = bPosition;
            r.HandSolved = cPosition;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (totalLen > k_Epsilon) ? atCorrectedLen / totalLen : 0f;
            r.ElbowAngleDeg = AngleDeg(aPosition - bPosition, cPosition - bPosition);
            r.HintFade = hintFade;
            r.HintProjMag = hintProjMag;
            r.ArmProjMag = armProjMag;
            r.PoleConditioning = poleCondW;
            r.AxisSource = axisSource;
            r.HandError = (cPosition - tPosition).magnitude;
        }

        // Signed angle from `from` to `to`, measured about `axis` (normalized). Both vectors already lie in the
        // plane perpendicular to `axis`, so this is exact. Written the long way rather than through
        // Vector3.SignedAngle / Quaternion.AngleAxis because this runs inside a Burst job.
        //
        // NaN-safe by shape: `!(denom > k_Epsilon)` takes the reject branch on NaN, where `denom < k_Epsilon`
        // would have waved it through -- NaN fails every ordered comparison, so a guard has to be written as
        // "reject unless good", never "reject if bad".
        static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }

            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);   // Mathf.Clamp does NOT clamp NaN; this shape sends it to -1
            float angle = Mathf.Acos(c);
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }

        static Quaternion AngleAxisRad(float radians, Vector3 axis)
        {
            float h = 0.5f * radians;
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        // Signed rotation of q about `axis` (unit), i.e. the swing-twist decomposition's twist angle,
        // wrapped to (-180, 180]. Projecting the vector part onto the axis removes the swing; atan2 needs
        // no normalization because a common positive scale cancels; forcing w >= 0 first picks the principal
        // branch of the double cover. NaN-safe by shape: `!(x > eps)` rejects NaN.
        static float TwistAngleRad(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > k_SqrEpsilon))
            {
                return 0f;
            }
            return 2f * Mathf.Atan2(s, c);
        }

        static float AngleDeg(Vector3 from, Vector3 to)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denom < k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp(Vector3.Dot(from, to) / denom, -1f, 1f);
            return Mathf.Acos(c) * Mathf.Rad2Deg;
        }

        static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_Epsilon || aLen2 <= k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2.0f * aLen1 * aLen2), -1.0f, 1.0f);
            return Mathf.Acos(c);
        }
    }
}
