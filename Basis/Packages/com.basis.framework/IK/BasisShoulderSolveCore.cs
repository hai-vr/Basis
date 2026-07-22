using UnityEngine;
namespace Basis.IK
{
    public struct BasisShoulderSolveInput
    {
        public Vector3 ShoulderPos;
        public Vector3 HandTargetPos;
        public Vector3 ElbowPos;            // lower-arm hint / elbow tracker world position
        public bool HasElbow;              // elbow tracker present -> drive from the true upper-arm direction
        public bool HasShoulderTracker;    // real shoulder tracker present -> TrackerFinal is the base
        public Quaternion ChestRot;        // live chest world rotation (the girdle frame)
        public Quaternion TposeChestRot;   // rest chest world rotation
        // Bind rotation of the SAME bone ChestRot is read from, so the girdle frame can be built on
        // anatomical axes instead of that bone's authored ones. Zero (the default) declines and leaves
        // the caller bit-identical to the pre-bind behaviour. See the frame block in Solve.
        public Quaternion ChestBind;
        public Quaternion TposeShoulderRot;// rest shoulder world rotation
        public Vector3 TposeArmDirWorld;   // rest upper-arm direction (shoulder->upperArm) at bind, world space
        public float TposeArmLength;       // rest shoulder->hand distance (reach normaliser)
        public float TposeClavicleLength;  // rest shoulder->upperArm distance; feeds the shrug's expected-reach geometry
        public float TposeElbowLength;     // rest shoulder->lowerArm distance; 0 (the default) disables the elbow-driven shrug
        public bool ShrugEnabled;          // the user's body-tracking toggle; false (the default) disables the shrug outright
        public bool RetractEnabled;        // the user's body-tracking toggle; false (the default) disables the posterior retraction outright
        public bool RhythmEnabled;         // the user's body-tracking toggle; false (the default) disables the scapulohumeral rhythm outright
        public float ElevationFactor;      // per-axis trim on the lift component of the coupled swing
        public float ProtractionFactor;    // per-axis trim on the horizontal (protraction / cross-body) component
        public float CoupleRatio;          // scapulohumeral coupling: girdle share of the humeral swing
        public float MaxShoulderDeg;       // clamp on the applied girdle rotation
        public Quaternion TrackerFinal;    // real shoulder tracker rotation
        public bool IsLeft;
    }

    public struct BasisShoulderSolveResult
    {
        public bool Apply;
        public Quaternion ShoulderRotation;

        public float RawReachRatio;
        public float ReachRatio;
        public float Elevation;
        public float Protraction;
        public float CrossBodyContrib;
        public float ComputedWeight;

        // Anatomical diagnostics (chest-frame).
        public float SwingAngleDeg;     // total humeral swing from rest
        public float AppliedAngleDeg;   // girdle rotation actually applied
        public float TwistLeakDeg;      // girdle rotation about the arm axis (must stay ~0)
        public bool DriverIsElbow;
        public float ShrugDeg;          // girdle elevation mined from the hanging hand's reach deficit (0 = not engaged)
        public float RetractDeg;        // girdle retraction supplied to a posterior reach (0 = not engaged)

        // Scapulohumeral rhythm (0 = not engaged).
        public float HumeralElevationDeg;// CLINICAL humeral elevation the rhythm reads: 0 = arm at the side,
                                         // 180 = straight overhead. NOT the swing-from-bind SwingAngleDeg.
        public float RhythmElevDeg;      // clavicular elevation supplied by the rhythm
        public float RhythmRetractDeg;   // clavicular retraction supplied by the rhythm
    }

    // Scapulohumeral-rhythm shoulder pre-solve. The shoulder girdle (clavicle/scapula bone) follows a
    // coupled fraction of the upper-arm SWING away from rest, measured in the chest frame, with the
    // humeral axial twist removed -- a twist-following clavicle was tried and reverted. The upper-arm
    // direction comes from the elbow tracker when present (shoulder->elbow, flexion-independent), else
    // from the hand target. Stream-free so the offline sweep + NUnit gates exercise the exact math.
    //
    // Four terms reach the girdle, all of them summed as chest-anatomical rotation vectors before a single
    // shared twist-strip and a single shared MaxShoulderDeg clamp: the coupled swing (scaled by `engage`),
    // the shrug, the posterior retraction, and the scapulohumeral rhythm (the last three skip `engage` by
    // design, each for a documented reason). Every term past the coupled swing is off unless its caller
    // opts in, so an input built with `= default` reproduces the original coupled swing bit for bit.
    public static class BasisShoulderSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_ReachEngageThreshold = 0.7f;   // hand-fallback: shoulder stays at rest below this reach
        const float k_SetStartDeg = 8f;              // setting phase: girdle barely moves below this swing
        const float k_SetFullDeg = 95f;
        const float k_TrackerRefine = 0.35f;         // with a shoulder tracker, only a gentle anatomical nudge (tracker stays dominant)
        const float k_DepressionShare = 0.25f;       // lowering below bind moves the girdle far less than raising
        const float k_DepressionBand = 0.12f;        // smooth raise/lower crossover, in chest-up units

        // ── ELBOW TRUST ─────────────────────────────────────────────────────────────────────────────
        // An elbow tracker is only worth believing if it sits about where an elbow can: compare its
        // distance from the shoulder against the baked upper-arm length and fade the reach gate back in
        // as that mismatch grows, so a badly placed or drifting tracker stops driving the girdle.
        // Fraction by which the elbow tracker overshoots the straight-arm reach for the direction it is
        // ALREADY pointing. Only the FAR side is gated: a shrug can only bring the elbow CLOSER, so the
        // near side stays untouched and the elbow path still never fades. The band clears the ~19% that
        // live girdle rotation can legitimately add, since `expected` is cast from the REST clavicle.
        const float k_ElbowOvershootStartFrac = 0.25f;
        const float k_ElbowOvershootEndFrac = 0.60f;

        // ── SHRUG, mined from the hands (or better, the elbows) ─────────────────────────────────────
        // Hanging at the side, a real shrug lifts the whole girdle: the controller RISES while the arm's
        // DIRECTION barely changes -- zero humeral swing, which is the one quantity everything above is
        // driven by, so the shoulder never moved. What survives is the REACH DEFICIT: the driver (hand, or
        // the elbow tracker when present) ends up measurably closer to the chest-anchored clavicle than a
        // straight hanging arm can reach. That deficit is the shrug, in metres.
        //
        // The expected reach is exact, not the T-pose scalar: clav->driver for a straight arm at the
        // current direction is sqrt(c^2 + seg^2 + 2*c*seg*cos), cos = dot(rest clavicle dir, arm dir).
        // Using the T-pose length raw would read a permanent ~18% deficit on every hanging arm, because
        // T-pose is the one pose where clavicle and arm are colinear.
        //
        // All windows are FRACTIONS OF TposeArmLength, so the gesture is avatar-scale-free and the hand
        // and elbow paths agree about centimetres. The HAND path fades back out at large deficits -- a
        // hand on a hip or a lap is an elbow BEND, not a shrug. The ELBOW path does not fade: an upper
        // arm cannot bend itself, so with a tracker the deficit can only BE girdle motion.
        const float k_ShrugHangStart = 0.75f;        // smoothstep on how DOWN the arm points (chest frame)
        const float k_ShrugHangFull = 0.92f;
        const float k_ShrugRiseStartFrac = 0.02f;    // deficit where the shrug starts; relaxed micro-flex stays silent
        const float k_ShrugRiseFullFrac = 0.065f;    // a committed shrug (~4.5 cm on a 0.7 m reach)
        const float k_ShrugBendFadeStartFrac = 0.09f;// hand path only: past here it is an elbow bend...
        const float k_ShrugBendFadeEndFrac = 0.14f;  // ...and the shrug lets go entirely
        const float k_ShrugMaxDeg = 44f;             // full-shrug girdle elevation, before sliders and the max clamp.
                                                     // 22 read at half strength in-headset (the Elevation slider
                                                     // defaults to 0.4); doubled on that verdict, 2026-07-16.
                                                     // NOTE: past an Elevation slider of ~0.57 the live 25 deg
                                                     // girdle clamp becomes the ceiling, not this.

        // ── POSTERIOR REACH, i.e. scapular RETRACTION ───────────────────────────────────────────────
        // Horizontal shoulder motion is strongly ASYMMETRIC and the coupled swing above is not: one
        // ProtractionFactor answers both a reach across the chest and a reach behind the back. Real
        // glenohumeral horizontal EXTENSION runs out at ~45-60 deg posterior -- the figure this repo
        // already carries on BasisBodyPlausibility's ShoulderPosteriorDeg channel -- while horizontal
        // FLEXION reaches ~130. So the forward half is comfortably inside range at the shipped
        // coefficient and the rear half is not: measured against this core with a T-pose bind, a 90 deg
        // posterior hand target put 86.8 deg on the humerus and 3.2 deg on the girdle.
        //
        // Worse, the rear poses that need the girdle most are the ones `reachFade` switches it OFF for.
        // A hand at the lower back sits at ~0.55 of reach, under k_ReachEngageThreshold, so the girdle
        // contributes exactly zero while the humerus swings 55 deg posterior. The reach gate is right
        // about a folded arm IN FRONT -- the hand is near the body because the elbow is bent, and the
        // shoulder should stay put -- and wrong about a folded arm BEHIND, because no amount of
        // glenohumeral range puts a hand back there without the scapula. Direction separates the two
        // cases where reach cannot, so this term is gated on direction alone and, like the shrug,
        // deliberately skips `engage`.
        //
        // Driven by the raw posterior COMPONENT of the arm direction, not its horizontal azimuth:
        // azimuth divides elevation out, so it would read a nearly-hanging arm whose hand drifts a
        // little behind the hip as a deep posterior reach and retract the girdle in the resting stance.
        const float k_RetractStartDot = 0.50f;       // ~30 deg posterior; the glenohumeral joint owns everything up to here
        const float k_RetractFullDot = 0.95f;        // ~72 deg posterior; scapula fully retracted
        const float k_RetractMaxDeg = 60f;           // full retraction BEFORE the Protraction slider, so ~18 deg at its
                                                     // 0.3 default -- real scapular retraction range. Sized for the
                                                     // default exactly like k_ShrugMaxDeg: past a slider of ~0.42 the
                                                     // 25 deg girdle clamp becomes the ceiling, not this.

        // ── SCAPULOHUMERAL RHYTHM, i.e. the girdle's share of ARM ELEVATION ─────────────────────────
        // Everything above is driven by the SWING AWAY FROM THE AUTHORED BIND. That is the wrong
        // independent variable for the rhythm, and measurably so. On a T-pose bind the bind direction IS
        // ~90 deg of clinical elevation, so `swingDeg` is V-shaped about it: measured on this core at the
        // shipped constants, the girdle's contribution to the clavicle tip is
        //
        //      arm elevation   0     30    60    90    120   150   180   (deg from the side)
        //      tip lift    -1.06  -0.46 -0.06  0.00   0.23  1.84  4.24  (deg)
        //
        // -- exactly 0.00 at the bind pose, never past 4.24 anywhere, and DECREASING over the first 90 deg
        // of a raise. In the sagittal (flexion) plane it is worse than small, it is blind: every elevation
        // there is a 90 deg swing from a T-pose bind, so `swingDeg` is pinned at 90.00 for the entire arc
        // and the coupled term cannot see the raise at all. Against that, the clinical clavicle ELEVATES
        // ~30 deg. The girdle is not subtly under-driven, it is reading the wrong quantity.
        //
        // So this term is driven by CLINICAL HUMERAL ELEVATION -- the angle from the arm-at-side, which is
        // bind-independent -- and by the PLANE OF ELEVATION. Nothing else. Grewal & Dickerson (n=28) found
        // humeral AXIAL ROTATION does not influence scapular retraction/protraction (p>0.05), so the model
        // stays a clean 2-input one and gains nothing from a third.
        //
        // ⚠️ AND THEREFORE IT CONTRIBUTES EXACTLY ZERO TO A PURE AXIAL-ROLL GESTURE, by construction:
        // rolling the humerus holds both inputs constant. This is a posture/appearance term. It is not a
        // fix for anything in the roll family and must not be sold as one.
        //
        // NUMBERS (Ludewig et al., transcortical BONE PINS, n=12 -- the gold standard; the ratio spread is
        // cross-checked against the wider literature):
        //   * overall rhythm GH:ST 2.1:1 abduction, 2.4:1 flexion, 2.2:1 scapular plane;
        //   * at full elevation: clavicular elevation ~30 deg PEAKING NEAR 130 deg of arm elevation,
        //     clavicular retraction ~16 deg, clavicular posterior axial rotation ~31 deg,
        //     scapular posterior tilt ~19 deg;
        //   * the ratio is strongly NON-CONSTANT -- the first ~30 deg (the setting phase) runs 4:1 to 7:1
        //     and 90-150 deg runs about 1:1. A single constant ratio is wrong at both ends.
        // Published regressions on this reach RMSE ~6.6-11.1 deg (Xu et al., n=38): that is the accuracy
        // ceiling for ANY model of this shape, including this one. It is a posture prior, not a measurement.
        //
        // ⭐ WHAT THIS RIG CAN AND CANNOT CARRY. The bone written here is the CLAVICLE (Unity humanoid has
        // no scapula), so only the clavicular numbers are representable:
        //   * elevation (30 deg) -> lifts the arm root. Representable, and the visible half of the defect.
        //   * retraction (16 deg) -> swings the arm root back. Representable.
        //   * clavicular posterior AXIAL rotation (31 deg) -> a roll about the clavicle's own long axis.
        //     The upper arm sits essentially ON that axis, so in a 3-bone chain it moves almost nothing and
        //     what it would move is arm-root ROLL -- the exact thing the twist-strip below exists to
        //     forbid. DROPPED deliberately.
        //   * scapular posterior tilt (19 deg) -> no scapula bone. DROPPED.
        // So this recovers the CLAVICULAR share, not the whole ~48 deg girdle share the 2.1:1 ratio implies.
        // The remainder keeps being absorbed by the humerus, exactly as it is today. That is a ceiling on
        // realism no coefficient can lift; it needs a scapula bone.
        //
        // THE SHAPE comes from the ratio structure, not from taste. Let the girdle's instantaneous share of
        // elevation be s = 1/(1+r). The cited r goes 5:1 (setting) -> 1:1 (late), i.e. s goes 1/6 -> 1/2:
        // the rate TRIPLES, which is k_RhythmLateRateMul and the only free number in the curve. Integrating
        // a rate that is flat to 30 deg then ramps linearly to 3x at 130 deg gives a cumulative girdle
        // contribution of 48.3 deg over a 150 deg arc == an overall GH:ST of 2.10:1, the cited abduction
        // figure, to three digits. The knot at 130 deg is independently the cited peak of clavicular
        // elevation. Two separate citations land on the same number, so it is not tuned.
        const float k_RhythmSetEndDeg = 30f;         // end of the setting phase (rate is flat below this)
        const float k_RhythmPeakDeg = 130f;          // rate saturates AND clavicular elevation peaks here
        const float k_RhythmLateRateMul = 3f;        // late girdle rate / setting-phase rate, from 5:1 -> 1:1
        // ⚠️ SIZED AT SLIDER 1.0, NOT AT THE DEFAULT -- deliberately HALF the convention k_ShrugMaxDeg and
        // k_RetractMaxDeg use. Those are pre-multiplied so the DEFAULT slider lands on the anatomical value;
        // doing that here would need 75 deg pre-slider, and the clinical 30 deg ALREADY EXCEEDS the 25 deg
        // shared girdle clamp before the shrug or the retraction get any share at all. These two constants
        // are therefore the literal clinical numbers, reached only at a slider of 1.0; at the shipped
        // Elevation 0.4 / Protraction 0.3 defaults the term delivers ~12 deg and ~4.8 deg. Under-delivering
        // against the clinical figure is the deliberate choice -- see the clamp note in Solve.
        //
        // ⚠️ THE SHARED 25 deg BUDGET, MEASURED. Sweeping the direction sphere with every girdle term on,
        // the clamp binds in 0.0% of poses today and 1.9% with this term at the shipped Elevation 0.4 --
        // and in 12.0% at 0.6, 33.6% at 0.8, 38.9% at 1.0. So the DEFAULT is comfortable and the slider is
        // no longer a linear dial past about 0.6; above that the clamp, not these constants, sets the
        // outcome. The terms are summed BEFORE the clamp, so a saturating clamp scales them together --
        // verified NOT to happen at the default: the strongest shrug pose keeps 17.60 deg and the
        // strongest posterior reach keeps 18.00 deg, unchanged to 0.01 deg with this term on.
        // ⚠️ AND THE 25 IS NOT THE LIVE CEILING ANYWAY: BasisFullBodyIK.ApplyShoulderSlide adds up to
        // 15 deg AFTER this clamp, so the girdle already reaches 40 deg against a documented 25. This term
        // does not change that -- it stays inside the 25 like everything else here -- but it does spend
        // more of the budget more often, which makes the pre-existing overrun easier to notice. Restructuring
        // that shared budget is a BasisFullBodyIK change and was deliberately left alone.
        const float k_RhythmElevMaxDeg = 30f;        // clavicular elevation at full arm elevation, slider 1.0
        const float k_RhythmRetractMaxDeg = 16f;     // clavicular retraction at full arm elevation, slider 1.0
        // Plane of elevation. Flexion 2.4:1 vs abduction 2.1:1 => the girdle's share in the sagittal plane
        // is (1/3.4)/(1/3.1) = 0.912 of its share in the frontal plane. Small, cited, and it is the whole
        // of the plane dependence on the elevation channel.
        const float k_RhythmFlexionShare = 0.912f;
        // ── ⚠️ DOES THE DRIVER ACTUALLY CARRY THE HUMERUS DIRECTION? ────────────────────────────────
        // The rhythm is a function of HUMERAL elevation, and only an elbow tracker measures the humerus.
        // shoulder->HAND is humerus + forearm, and with the elbow bent the two differ by up to ~36 deg.
        // The failure is not a static offset, it is a PHANTOM GESTURE: a pure humeral axial roll with a
        // bent elbow sweeps the hand around a cone of half-angle err while the humerus does not move at
        // all, so a hand-driven rhythm reads up to 2*err of elevation change out of a gesture that has
        // none. Measured, controller-only, 90 deg of elbow flexion, arm out to the side:
        //
        //      apparent elevation swept   59.4 -> 120.6 deg   (61.2 deg of pure phantom)
        //      girdle, before this term    0.00 ->   0.00 deg
        //      girdle, unguarded rhythm    3.73 ->  11.24 deg  (7.51 deg of clavicle swing)
        //
        // With an elbow tracker the same sweep moves the girdle 0.0000 deg. That is the whole defect, and
        // it is the user-visible "in regular VR with controllers I can rotate my hand and break it".
        //
        // The bound is EXACT and closed form, so the driver's trustworthiness is measured per frame rather
        // than assumed. With upper arm a, forearm f and d = |shoulder->hand|, the angle at the shoulder
        // between the true humerus and the hand vector is the law of cosines:
        //      err = acos((a^2 + d^2 - f^2) / (2ad))
        // zero at full extension (where the hand direction IS the humerus direction) and largest with the
        // elbow folded. Fade the rhythm out across it. 8 deg costs nothing real; by 25 deg the hand has
        // stopped being evidence about the humerus, and the 90 deg-flexion phantom lands past it at 30.6.
        const float k_RhythmDriverErrStartDeg = 8f;
        const float k_RhythmDriverErrEndDeg = 25f;

        public static void Solve(in BasisShoulderSolveInput i, out BasisShoulderSolveResult r)
        {
            r = default;

            // ── A ZERO QUATERNION IS NOT A ROTATION, AND Apply == true PUTS IT ON THE BONE ──────────
            // ShoulderRotation is written straight through with SetRotation, so a zero quaternion
            // COLLAPSES the transform exactly as a NaN would. Four inputs reach the output un-normalised:
            // ChestRot and TposeShoulderRot multiply straight into `anatomical`, TposeChestRot reaches it
            // through Quaternion.Inverse -- which in Unity is a bare conjugate, so inverse(zero) is zero,
            // not identity and not NaN -- and TrackerFinal is the Slerp base on the tracker path. Any one
            // of them zero makes the whole product zero while Apply stays true. Each is a rig whose chest
            // or clavicle bind was never baked.
            //
            // Declined with the same `Quaternion.Dot(q, q) > eps` test the frame block below already
            // applies to ChestBind, written as reject-unless-good so a NaN takes the reject branch too.
            // Declining is the file's established answer to a degenerate input, and it leaves the bone
            // wherever the previous stage put it instead of collapsing it.
            //
            // TrackerFinal is checked ONLY under HasShoulderTracker: it is unused otherwise, and callers
            // that build the input with `= default` legitimately leave it zero.
            if (!(Quaternion.Dot(i.ChestRot, i.ChestRot) > k_Epsilon)
                || !(Quaternion.Dot(i.TposeChestRot, i.TposeChestRot) > k_Epsilon)
                || !(Quaternion.Dot(i.TposeShoulderRot, i.TposeShoulderRot) > k_Epsilon)
                || (i.HasShoulderTracker && !(Quaternion.Dot(i.TrackerFinal, i.TrackerFinal) > k_Epsilon)))
            {
                r.Apply = false;
                return;
            }

            float armLen = Mathf.Max(i.TposeArmLength, k_Epsilon);
            Vector3 driver = i.HasElbow ? i.ElbowPos : i.HandTargetPos;
            Vector3 armVec = driver - i.ShoulderPos;
            Vector3 handVec = i.HandTargetPos - i.ShoulderPos;
            if (armVec.sqrMagnitude < k_Epsilon || i.TposeArmDirWorld.sqrMagnitude < k_Epsilon)
            {
                r.Apply = false;
                return;
            }

            // ── THE GIRDLE FRAME IS ANATOMICAL, NOT THE CHEST BONE'S ────────────────────────────────
            // Everything downstream reads this frame as X=lateral, Y=up, Z=forward: the raise /
            // depression split, the shrug's hang gate, the retraction's posterior dot, the
            // elevation-vs-protraction axis split, CrossBody. Those are ANATOMY, and the chest BONE only
            // carries them on a rig that happens to be bound axis-aligned. On a chest bound
            // Y-up-along-the-bone (the ordinary Blender export, an X-90 bind) a plain hanging arm reads
            // as bone-local -Z -- dead ahead of the shoulder: the hang gate shuts, the posterior dot
            // saturates, and an IDLE arm takes a full 18 deg scapular retraction and saturates the
            // girdle clamp. That ships as "this avatar's shoulders are permanently hunched", i.e. it
            // reads as a broken avatar rather than as an IK bug; the genuine posterior reach meanwhile
            // loses its retraction entirely.
            //
            // So cancel the bind and measure in the frame the bind DEFINED as neutral -- the same move
            // SolveSpine already makes for the hips (BasisSpineBendCore: `hipsSpace = i.HipsBind * invHips`,
            // and BasisFullBodyIK's `hipsAnat` on the way back out). inv(ChestRot * inv(bind)) is
            // bind * inv(ChestRot), so the frame costs one extra product and no inverse.
            //
            // ChestBind zero -- the default -- DECLINES: every caller that does not bake one keeps the
            // pre-bind behaviour bit for bit, and an identity bind is bit-identical to declining.
            Quaternion invChest = Quaternion.Inverse(i.ChestRot);
            Quaternion invChestAnat = invChest;
            Quaternion invTposeChestAnat = Quaternion.Inverse(i.TposeChestRot);
            Quaternion girdleToBone = Quaternion.identity;
            bool hasBind = Quaternion.Dot(i.ChestBind, i.ChestBind) > k_Epsilon;
            if (hasBind)
            {
                invChestAnat = i.ChestBind * invChest;
                invTposeChestAnat = i.ChestBind * invTposeChestAnat;
                girdleToBone = Quaternion.Inverse(i.ChestBind);
            }

            Vector3 restDirL = (invTposeChestAnat * i.TposeArmDirWorld).normalized;
            Vector3 armDirL = (invChestAnat * armVec).normalized;

            // Twist-free by construction: FromToRotation's axis is perpendicular to the arm, so no roll.
            Quaternion swing = BasisQuaternionExt.FromToRotation(restDirL, armDirL);
            Vector3 rv = QuatToRotationVector(swing);   // chest-local rotation vector (rad), the humeral swing
            float swingDeg = rv.magnitude * Mathf.Rad2Deg;

            float rawReach = handVec.magnitude / (armLen * 1.1f);
            float reachEngage = ReachEngage(Mathf.Clamp01(rawReach));
            // Fails OPEN. TposeElbowLength == 0 means the caller did not bake one (its own doc: "0 (the
            // default) disables the elbow-driven shrug"), i.e. absent -- NOT evidence the tracker is
            // wrong. Starting from 0 would read "no data" as "maximum doubt" and silently collapse the
            // whole solve to the hand-path reach gate for every caller that omits it, which is the
            // offline sweeps and BasisShoulderDirectionTests: they drive the elbow path with
            // HandTargetPos == ShoulderPos, so rawReach is 0 and the girdle would never move at all.
            // Trusting by default reproduces the pre-trust `HasElbow ? 1f : ...` exactly, and the live
            // path is unchanged either way because BasisFullBodyIK always bakes the length.
            float elbowTrust = 1f;
            if (i.HasElbow && i.TposeElbowLength > k_Epsilon)
            {
                float trustClav = Mathf.Clamp(i.TposeClavicleLength, 0f, i.TposeElbowLength * 0.45f);
                float trustSeg = Mathf.Max(i.TposeElbowLength - trustClav, k_Epsilon);
                float trustCr = Vector3.Dot(restDirL, armDirL);
                float trustRoot = trustClav * trustClav * (trustCr * trustCr - 1f) + trustSeg * trustSeg;
                float trustExpected = trustClav * trustCr + Mathf.Sqrt(trustRoot > 0f ? trustRoot : 0f);
                if (trustExpected > k_Epsilon)
                {
                    float overshootFrac = (armVec.magnitude - trustExpected) / trustExpected;
                    if (overshootFrac > 0f)
                    {
                        elbowTrust = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ElbowOvershootStartFrac, k_ElbowOvershootEndFrac, overshootFrac));
                    }
                }
            }
            float reachFade = i.HasElbow ? Mathf.Lerp(reachEngage, 1f, elbowTrust) : reachEngage;
            float setting = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_SetStartDeg, k_SetFullDeg, swingDeg));
            float engage = setting * reachFade;

            float couple = i.CoupleRatio * engage;
            // Chest-local axes: X=lateral, Y=up, Z=forward. Raising the arm swings about X/Z (elevation);
            // moving it across the front swings about Y (protraction / cross-body). Lowering the arm below
            // bind moves the girdle far less than raising it (scapular depression has a small range), which
            // also keeps idle / arms-down poses near the authored bind shoulder.
            float raise = armDirL.y - restDirL.y;
            float elevGain = Mathf.Lerp(k_DepressionShare, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-k_DepressionBand, k_DepressionBand, raise)));
            Vector3 girdleRv = new Vector3(rv.x * i.ElevationFactor * elevGain, rv.y * i.ProtractionFactor, rv.z * i.ElevationFactor * elevGain) * couple;

            // The shrug (see the constants block). Deliberately NOT scaled by `engage`: a shrug has zero
            // humeral swing, so the swing-based setting gate would erase exactly the gesture this exists
            // to detect. It carries its own gates instead, and joins the girdle before the twist-strip and
            // the max clamp so those bounds still own the outcome. Chest-local +Z lifts a +X (right)
            // clavicle tip; the left clavicle points -X, so its lift is -Z.
            float shrugRad = 0f;
            float bindLen = i.HasElbow ? i.TposeElbowLength : i.TposeArmLength;
            if (i.ShrugEnabled && bindLen > k_Epsilon)
            {
                float clav = Mathf.Clamp(i.TposeClavicleLength, 0f, bindLen * 0.45f);
                float seg = Mathf.Max(bindLen - clav, k_Epsilon);

                // Gate on the ARM SEGMENT's direction, not the clav->driver composite: subtracting the rest
                // clavicle tip removes the lateral offset that tilts the composite as the driver rises --
                // worst on the short elbow segment, where a real 5 cm shrug would otherwise read half-closed.
                // A shrug keeps the segment dead vertical at any rise, so the gate stays open for exactly
                // the gesture and still closes on a genuine swing.
                Vector3 segVecL = armDirL * armVec.magnitude - restDirL * clav;
                float hangDot = segVecL.sqrMagnitude > k_Epsilon ? -segVecL.normalized.y : 0f;
                if (hangDot > k_ShrugHangStart)
                {
                    float hang = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugHangStart, k_ShrugHangFull, hangDot));

                    // The expected reach for the OBSERVED direction is a ray-sphere intersection, not the
                    // law of cosines: the observed direction already CONTAINS the clavicle offset, so
                    // treating it as the arm's own direction over-expects by ~5% of the reach on a hanging
                    // arm -- a permanent false shrug at idle. Cast clav->driver along what we saw, against
                    // the sphere of straight-arm reaches around the rest clavicle tip; clav < seg (the
                    // 0.45 clamp), so the root term is positive structurally.
                    float cr = Vector3.Dot(restDirL, armDirL);
                    float expected = clav * cr + Mathf.Sqrt(clav * clav * (cr * cr - 1f) + seg * seg);
                    float deficitFrac = (expected - armVec.magnitude) / armLen;

                    float riseIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugRiseStartFrac, k_ShrugRiseFullFrac, deficitFrac));
                    float bendOut = i.HasElbow ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugBendFadeStartFrac, k_ShrugBendFadeEndFrac, deficitFrac));
                    shrugRad = k_ShrugMaxDeg * Mathf.Deg2Rad * hang * riseIn * (1f - bendOut) * Mathf.Max(i.ElevationFactor, 0f);
                    girdleRv.z += i.IsLeft ? -shrugRad : shrugRad;
                }
            }

            // Scapular retraction on a posterior reach (see the constants block). Like the shrug it joins
            // girdleRv before the twist-strip and the max clamp so those bounds still own the outcome,
            // and it is deliberately NOT scaled by `engage`. Chest-local +Y swings a +X (right) arm root
            // toward -Z (backward); the left root points -X, so its retraction is -Y.
            float retractRad = 0f;
            float posterior = -armDirL.z;
            if (i.RetractEnabled && posterior > k_RetractStartDot)
            {
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_RetractStartDot, k_RetractFullDot, posterior));
                retractRad = k_RetractMaxDeg * Mathf.Deg2Rad * back * Mathf.Max(i.ProtractionFactor, 0f);
                girdleRv.y += i.IsLeft ? -retractRad : retractRad;
            }

            // Scapulohumeral rhythm (see the constants block). Driven by CLINICAL humeral elevation and the
            // plane of elevation ONLY. Like the shrug and the retraction it joins girdleRv before the
            // twist-strip and the max clamp, and it is deliberately NOT scaled by `engage`: `engage` is
            // built from swing-from-bind, which on a T-pose bind is 0.00 at exactly the pose the rhythm is
            // largest in, so scaling by it would erase the term at its own peak.
            //
            // Elevation is the angle from the arm-at-side (chest-local -Y). This is a rotation MAGNITUDE
            // about fixed anatomical axes, so unlike a direction-driven term it has no axis singularity
            // anywhere: at the arm-down pole the magnitude is 0 and grows at 0.13 deg per deg, and at the
            // overhead pole it is on its plateau. Nothing to guard.
            //
            // ⚠️ WHAT THE SHARED TWIST-STRIP TAKES BACK, MEASURED. The lift is about chest +Z, and the strip
            // below removes whatever component of girdleRv lies along armDirL, so this term survives at
            // sqrt(1 - armDirL.z^2) of its size: 100% in the frontal/scapular plane (the case this exists
            // for), 71% at 45 deg of elevation dead ahead, and 0% for an arm pointing exactly forward or
            // exactly back at shoulder height. 13.4% of the direction sphere by solid angle keeps under
            // half. That is not a bug in the strip -- with the arm along +Z, lifting the clavicle about +Z
            // IS rolling the humerus, and a rolling clavicle is the reverted defect the strip exists to
            // forbid. It IS a real ceiling on this term in the sagittal plane, and it is not fixable from
            // here: the strip is shared with the coupled swing, the shrug and the retraction, and it
            // measures against the arm rather than against the clavicle. Separating those is a change to
            // the shared architecture and was deliberately not attempted inside this term.
            float rhythmElevRad = 0f;
            float rhythmRetractRad = 0f;
            float humeralElevDeg = Mathf.Acos(Mathf.Clamp(-armDirL.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (i.RhythmEnabled)
            {
                // How much the DRIVER can be trusted to be the humerus (see the constants block). The elbow
                // tracker is the humerus by construction, so it inherits the elbow's own plausibility check;
                // the hand is only the humerus when the arm is straight, and how far it can be off is exact.
                float rhythmTrust;
                if (i.HasElbow)
                {
                    rhythmTrust = elbowTrust;
                }
                else
                {
                    float upperLen = i.TposeElbowLength;
                    float foreLen = i.TposeArmLength - upperLen;
                    float handDist = handVec.magnitude;
                    if (upperLen > k_Epsilon && foreLen > k_Epsilon && handDist > k_Epsilon)
                    {
                        float cosErr = (upperLen * upperLen + handDist * handDist - foreLen * foreLen)
                                     / (2f * upperLen * handDist);
                        float errDeg = Mathf.Acos(Mathf.Clamp(cosErr, -1f, 1f)) * Mathf.Rad2Deg;
                        rhythmTrust = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(k_RhythmDriverErrStartDeg, k_RhythmDriverErrEndDeg, errDeg));
                    }
                    else
                    {
                        // No baked segment lengths, so the exact bound is unavailable. Fall back to the
                        // reach gate, which measures the same thing more bluntly -- a hand near the body is
                        // a bent elbow. Deliberately NOT fail-open: on the hand path, trusting an unmeasured
                        // driver is precisely the defect above.
                        rhythmTrust = reachEngage;
                    }
                }

                float progress = RhythmProgress(humeralElevDeg) * rhythmTrust;
                // Plane of elevation from the RAW forward component, not the horizontal azimuth -- the same
                // call the retraction term makes above, for the same reason: azimuth divides elevation out
                // and is undefined at both poles, where the horizontal projection vanishes. The raw
                // component is continuous everywhere on the sphere and reads 0 at both poles, where the
                // frontal/sagittal distinction genuinely has no meaning.
                float forward = Mathf.Clamp01(armDirL.z);

                rhythmElevRad = k_RhythmElevMaxDeg * Mathf.Deg2Rad * progress
                              * Mathf.Lerp(1f, k_RhythmFlexionShare, forward)
                              * Mathf.Max(i.ElevationFactor, 0f);
                girdleRv.z += i.IsLeft ? -rhythmElevRad : rhythmElevRad;

                // Retraction fades out toward the sagittal plane. Reaching FORWARD the clavicle protracts,
                // and the coupled swing's ProtractionFactor already owns that half; adding a rhythm term
                // there would double-count it. What the coupled swing structurally cannot supply is
                // ELEVATION-driven retraction in the frontal/scapular plane, which is all this adds.
                rhythmRetractRad = k_RhythmRetractMaxDeg * Mathf.Deg2Rad * progress * (1f - forward)
                                 * Mathf.Max(i.ProtractionFactor, 0f);
                girdleRv.y += i.IsLeft ? -rhythmRetractRad : rhythmRetractRad;
            }

            // Enforce twist-free exactly: strip any girdle rotation about the arm axis the anisotropic
            // per-axis scaling introduced. The clavicle swings the arm root, it does not roll with it.
            girdleRv -= Vector3.Dot(girdleRv, armDirL) * armDirL;

            float maxRad = Mathf.Max(i.MaxShoulderDeg, 0f) * Mathf.Deg2Rad;
            float mag = girdleRv.magnitude;
            if (maxRad > 0f && mag > maxRad && mag > k_Epsilon)
            {
                girdleRv *= maxRad / mag;
            }

            // girdleRv was measured, stripped and clamped in the ANATOMICAL frame; both consumers below
            // (`anatomical` and the tracker's `deltaWorld`) conjugate by the raw chest BONE rotation, so
            // carry the axis back into the bone frame first. Conjugation moves only the axis --
            // inv(bind) * R(axis, t) * bind == R(inv(bind) * axis, t) -- so the clamp and every angle
            // reported above stand exactly as measured, and the rotation applied to the clavicle is the
            // one the anatomy asked for rather than that same rotation re-read about the bone's axes.
            Quaternion girdleL = BasisQuaternionExt.NormalizeSafe(RotationVectorToQuat(hasBind ? girdleToBone * girdleRv : girdleRv));

            // shoulder world = live chest  *  girdle swing  *  rest shoulder-in-chest.
            Quaternion shoulderRestLocal = Quaternion.Inverse(i.TposeChestRot) * i.TposeShoulderRot;
            Quaternion anatomical = i.ChestRot * girdleL * shoulderRestLocal;

            Quaternion result;
            if (i.HasShoulderTracker)
            {
                Quaternion deltaWorld = i.ChestRot * girdleL * invChest;
                result = Quaternion.Slerp(i.TrackerFinal, deltaWorld * i.TrackerFinal, k_TrackerRefine * engage);
            }
            else
            {
                result = anatomical;
            }

            float elevRad = Mathf.Sqrt(girdleRv.x * girdleRv.x + girdleRv.z * girdleRv.z);
            float twistLeak = girdleRv.sqrMagnitude > k_Epsilon ? Vector3.Dot(girdleRv, armDirL) : 0f;

            r.Apply = true;
            r.ShoulderRotation = result;
            r.RawReachRatio = rawReach;
            r.ReachRatio = reachFade;
            r.Elevation = elevRad * Mathf.Rad2Deg;
            r.Protraction = Mathf.Abs(girdleRv.y) * Mathf.Rad2Deg;
            r.CrossBodyContrib = CrossBody(armDirL, restDirL, i.IsLeft) * Mathf.Abs(girdleRv.y) * Mathf.Rad2Deg;
            r.ComputedWeight = engage;
            r.SwingAngleDeg = swingDeg;
            r.AppliedAngleDeg = Quaternion.Angle(Quaternion.identity, girdleL);
            r.TwistLeakDeg = Mathf.Abs(twistLeak) * Mathf.Rad2Deg;
            r.DriverIsElbow = i.HasElbow;
            r.ShrugDeg = shrugRad * Mathf.Rad2Deg;
            r.RetractDeg = retractRad * Mathf.Rad2Deg;
            r.HumeralElevationDeg = humeralElevDeg;
            r.RhythmElevDeg = rhythmElevRad * Mathf.Rad2Deg;
            r.RhythmRetractDeg = rhythmRetractRad * Mathf.Rad2Deg;
        }

        // Cumulative girdle progress in [0,1] against CLINICAL humeral elevation, from the cited
        // non-constant rhythm. The instantaneous girdle share of elevation is s = 1/(1+r); the cited r runs
        // 5:1 through the setting phase and about 1:1 late, so s runs 1/6 -> 1/2 and the RATE triples. This
        // integrates that rate -- flat to k_RhythmSetEndDeg, then linear to k_RhythmLateRateMul x at
        // k_RhythmPeakDeg -- and normalises by its value at the peak.
        //
        // Closed form, exactly: a piecewise-quadratic integral of a piecewise-linear rate. Monotone
        // non-decreasing by construction (the rate is positive everywhere), and C1 because the rate is
        // continuous at both knots -- so the girdle cannot pop or reverse as the arm sweeps.
        static float RhythmProgress(float elevDeg)
        {
            // Reject-unless-good: a NaN elevation lands on 0 (no contribution), never on a live rotation.
            if (!(elevDeg > 0f))
            {
                return 0f;
            }
            if (elevDeg >= k_RhythmPeakDeg)
            {
                return 1f;
            }

            float span = k_RhythmPeakDeg - k_RhythmSetEndDeg;
            // integral of the rate from 0 to the peak: the flat part plus the trapezoid under the ramp.
            float atPeak = k_RhythmSetEndDeg + span * (1f + k_RhythmLateRateMul) * 0.5f;
            if (!(atPeak > k_Epsilon))
            {
                return 0f;
            }

            float area;
            if (elevDeg <= k_RhythmSetEndDeg)
            {
                area = elevDeg;
            }
            else
            {
                float u = elevDeg - k_RhythmSetEndDeg;
                area = k_RhythmSetEndDeg + u + (k_RhythmLateRateMul - 1f) * u * u / (2f * span);
            }
            return area / atPeak;
        }

        // Hand-fallback reach gate: keep the shoulder at rest until the arm genuinely extends, so a
        // folded arm near the body (hand close, no elbow tracker) doesn't drag the shoulder around.
        static float ReachEngage(float reach)
        {
            if (reach < k_ReachEngageThreshold)
            {
                return 0f;
            }
            float t = (reach - k_ReachEngageThreshold) / (1f - k_ReachEngageThreshold);
            return t * t;
        }

        // Signed cross-body amount of the current arm direction past the rest direction, in [0,1].
        static float CrossBody(Vector3 armDirL, Vector3 restDirL, bool isLeft)
        {
            float side = isLeft ? -armDirL.x : armDirL.x;   // arm crossed to the far side of the chest
            return Mathf.Clamp01(-side);
        }

        static Quaternion RotationVectorToQuat(Vector3 rv)
        {
            float ang = rv.magnitude;
            if (ang < k_Epsilon)
            {
                return Quaternion.identity;
            }
            return Quaternion.AngleAxis(ang * Mathf.Rad2Deg, rv / ang);
        }

        static Vector3 QuatToRotationVector(Quaternion q)
        {
            q.ToAngleAxis(out float deg, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || axis.sqrMagnitude < k_Epsilon)
            {
                return Vector3.zero;
            }
            if (deg > 180f)
            {
                deg -= 360f;
            }
            return axis.normalized * (deg * Mathf.Deg2Rad);
        }
    }
}
