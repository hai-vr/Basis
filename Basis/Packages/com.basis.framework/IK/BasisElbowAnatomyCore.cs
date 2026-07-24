using UnityEngine;
namespace Basis.IK
{
    /// <summary>
    /// AN ELBOW CANNOT POINT AT THE SKY. This makes that unreachable rather than merely discouraged.
    ///
    /// ================================================================================================
    /// THE LAW, MEASURED, NOT INVENTED:
    ///
    ///     THE ELBOW NEVER RISES ABOVE THE SHOULDER, NOR ABOVE THE HAND -- WHICHEVER IS HIGHER.
    ///
    /// Across 55,140 frames of real human arm motion (the CMU corpus, both arms, every clip), the worst
    /// violation of that statement is +0.015 of an arm length -- nine millimetres. It is violated on 0.0000%
    /// of frames at a margin of 0.05, and a margin of 0.10 would clamp ZERO poses a human actually made.
    /// Broken out by where the hand is:
    ///
    ///     hand well below the shoulder .. 43,556 frames ... elbow above the shoulder on 0.000%
    ///     hand below .................... 4,232 frames .... 0.000%
    ///     hand just below ............... 2,186 frames .... 0.183%
    ///     hand at or above .............. 2,636 frames .... 15.3%
    ///     hand high ..................... 2,530 frames .... 71.9%
    ///
    /// You cannot lift your elbow above your shoulder while your hand hangs low. The humerus will not do it.
    /// So this is a HARD ANATOMICAL CONSTRAINT of exactly the same status as BasisLegSolveCore's anterior
    /// half-space guard ("a knee behind that axis is not unnatural, it is anatomically unrepresentable"), and
    /// it is not a tuning knob. The KNEE has had that guard for a long time. THE ARM HAD NOTHING -- nothing at
    /// all stopped the solver placing the elbow anywhere on its circle, including straight up, which is the
    /// "arms get into rotations that are not possible / the elbow points at the sky" report.
    /// ================================================================================================
    ///
    /// IT GUARDS THE ELBOW, NOT THE HINT, AND THAT IS DELIBERATE. A guard on the hint protects you from a bad
    /// hint; a guard on the OUTCOME protects you from everything -- a bad hint, a mis-strapped elbow tracker, a
    /// torso-collision push, the animation the solve started from. There is no path by which the arm can end a
    /// frame outside the envelope, because the last thing that happens is this.
    ///
    /// IT COSTS NO REACH, STRUCTURALLY. The correction is a swivel ABOUT THE SHOULDER->HAND AXIS, and the hand
    /// LIES on that axis -- a rotation about a line cannot move a point on that line. Reach preservation is not
    /// a tolerance here, it is geometry.
    ///
    /// AND IT IS THE IDENTITY WHERE IT SHOULD BE. Below the soft margin it returns exactly 0f, so a legal pose
    /// is not perturbed by a guard that has no business firing -- bit for bit the unguarded solver.
    ///
    /// ================================================================================================
    /// ⚠ "ABOVE" MEANS ABOVE THE TORSO, NOT ABOVE THE WORLD. The law was MEASURED in a torso frame -- the
    /// chest->neck axis -- and it is only true there. It is a statement about the humerus and the shoulder
    /// joint, and those are bolted to the ribcage; they have never heard of gravity.
    ///
    /// Bend at the waist and the two frames come apart. Re-measured over 266,156 arm-samples of the same
    /// corpus, per tier, as a fraction of arm length:
    ///
    ///     tier       world-up max   torso-up max    %>0.05 world   %>0.05 torso
    ///     root         0.0525         0.0150          0.0036%        0.0000%
    ///     posture      0.3723         0.0242          0.3475%        0.0000%
    ///     dynamic      0.2555         0.2155          0.7707%        0.2574%
    ///     slow        -0.0785        -0.0637          0              0
    ///
    /// Only the torso frame reproduces the published numbers above (+0.015 worst, 0.0000% past 0.05, 0.10
    /// clamping nothing). And on the 148 posture-tier frames that "violate" in world up, every one has the
    /// trunk 73.7-119.7 deg off vertical -- bent double -- where the measurement reads +0.3723 world against
    /// -0.3713 torso: THE SIGN FLIPS. Those are people touching their toes with their arms hanging normally.
    /// Hand this guard a world/root up and it fires on them, hard, to "fix" an elbow that was never wrong.
    ///
    /// So the caller owes this function a TORSO up. BasisArmSolveCore takes it from
    /// BasisSwivelHintCore.BuildFrame (chest->neck), the same body frame the swivel models are fitted in.
    /// ================================================================================================
    /// ⚠ WHAT THE CORPUS CAN AND CANNOT REFEREE, because the two are not the same question and this guard
    /// straddles the line.
    ///
    /// CAN: "is the elbow above the ceiling", at EVERY extension. That is a comparison of two HEIGHTS, both
    /// read straight off marker-derived joint centres. It stays well-conditioned as the arm straightens.
    ///
    /// CANNOT: "which way round its circle does the elbow point", above ~0.98 extension. There the circle's
    /// radius has shrunk to a few centimetres, so the mocap solver's own elbow reconstruction hits the SAME
    /// singularity ours does and the pole direction goes bimodal. It is measurable: c = dot(poleDir, upN)
    /// puts 0.0% of samples in its top two bins (c > 0.6) below 0.50 extension, 4.6% at 0.95-0.98, and 20.4%
    /// at 0.99+ -- a distribution flying apart, not a population of people raising their elbows.
    ///
    /// So SoftMarginMaxFracRadius is sized off the HEIGHT question in the bands the corpus can referee, and
    /// continued into the top band by continuity. It is not fitted to the top band's pole statistics, and a
    /// future revision must not fit it there either: that band cannot judge this quantity.
    /// ================================================================================================
    /// </summary>
    public static class BasisElbowAnatomyCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        /// <summary>
        /// Everything below this above the ceiling passes through UNTOUCHED. The corpus's worst real human
        /// violation is 0.015, so 0.05 (3 cm on a 0.6 m arm) clears every frame a human has ever produced with
        /// room to spare -- and leaves headroom for the poses a VR user strikes that a CMU subject walking
        /// around a lab never did.
        /// </summary>
        public const float SoftMarginFracLimb = 0.05f;

        /// <summary>
        /// The asymptote. The elbow eases toward this and never reaches it, so the envelope is closed by
        /// construction rather than by a tolerance -- the same rational saturation, with the same slope-1
        /// handover, that BasisLegSolveCore.ClampKneeSwivelDeg uses on the knee. Slope-1 means NO derivative
        /// step: the elbow eases into the limit instead of hitting a wall, which is the difference between a
        /// guard and a pop.
        /// </summary>
        public const float HardMarginFracLimb = 0.15f;

        // =============================================================================================
        // ⭐⭐ THE MARGIN MAY NEVER EXCEED WHAT A SWIVEL CAN REACH. THIS IS WHY, AND IT IS A THEOREM.
        //
        // This function's ONLY authority is a swivel about shoulder->hand, so the elbow it is asked to move
        // can only travel on a circle of radius `radius` about that axis. Write kappa = dot(acN, up); then
        //
        //     h - ceiling  =  (p - d)*kappa + radius*sqrt(1-kappa^2)*c        (kappa >= 0, ceiling = d*kappa)
        //     h - ceiling  =   p    *kappa + radius*sqrt(1-kappa^2)*c        (kappa <  0, ceiling = 0)
        //
        // with c = dot(poleDir, upN) in [-1,1] and p the elbow's projection onto the arm axis. p <= d for any
        // elbow flexion past ~30 deg and p >= 0 always, so BOTH branches are maximised at kappa = 0, where
        // both collapse to radius*c. So over the ENTIRE circle and EVERY hand direction:
        //
        //         max(h - ceiling)  =  radius,  EXACTLY.
        //
        // ⇒ A MARGIN OF `radius` OR MORE IS A GUARD THAT CAN NEVER FIRE. Not "rarely" -- never, for any pose.
        //
        // ⚠️⚠️ AND THAT IS NOT HYPOTHETICAL: IT HAD ALREADY HAPPENED, AT THE ONE EXTENSION VR LIVES AT.
        // For equal segments radius = (L/2)*cos(theta/2), so SoftMarginFracLimb = 0.05 could fire at all only
        // while 0.5*cos(theta/2) > 0.05, i.e. theta < 2*acos(0.1) = 168.522 deg. BasisArmSolveCore clamps the
        // elbow to MaxElbowAngleDeg = 170, where radius/L is 0.0436 -- BELOW the margin. Measured on the live
        // core over a 612-direction x 36-swivel grid (22,032 poses per row):
        //
        //     theta      reach     radius/L    fired / 22032
        //      90        0.7071     0.3536       4384   (19.90%)
        //     120        0.8660     0.2500       2596   (11.78%)
        //     150        0.9659     0.1294        849   ( 3.85%)
        //     168        0.9945     0.0523          6   ( 0.03%)
        //     168.522    0.9950     0.0500          0            <- the closed form, to the digit
        //     169        0.9954     0.0479          0
        //     170        0.9962     0.0436          0            <- MaxElbowAngleDeg. THE CAP. ALWAYS ZERO.
        //
        // Driven through the shipping BasisArmSolveCore.Solve rather than this core alone, a hint swept round
        // the full circle at reach 0.995/0.999/1.000/1.050 parks the elbow at c = 1.000 -- the very TOP of its
        // circle, the elbow pointing at the sky -- with the guard returning 0f on every one. 41.1% of the CMU
        // corpus is past 0.95 extension, and a user whose real arms are longer than their avatar's is past the
        // cap on EVERY FRAME, so this was not a corner: it was the common case, unguarded.
        //
        // ⚠️ THE HUMERAL TWIST GUARD DOES NOT COVER IT EITHER, though at full extension it is measured about
        // an axis only (180-theta)/2 = 5.00 deg away from this one -- they are near the SAME degree of freedom
        // there, and orthogonal (45 deg) at 90 deg of flexion, so neither can be reasoned about as a stand-in
        // for the other. At the c = 1.000 pose above it reads HumeralTwistDeg 90.0 against a 120 deg soft
        // limit: HumeralTwistGuardDeg 0.0. Both guards silent, elbow at the sky.
        //
        // ⭐ SO THE MARGIN IS CAPPED AT A FRACTION OF THE GUARD'S OWN AUTHORITY, and the two constants can
        // never silently decouple again: this bound holds for ANY MaxElbowAngleDeg, any segment ratio, any
        // avatar scale, because it is stated in terms of the circle the swivel actually moves the elbow on.
        // A fraction of `radius` is reachable BY CONSTRUCTION.
        //
        // 0.5 IS NOT A GUESS -- IT IS WHERE THE OLD BEHAVIOUR WAS ALREADY SITTING WHEN THE CAP TAKES OVER.
        // The cap binds only where 0.5*radius < 0.05*L, i.e. radius/L < 0.1, i.e. reach > ~0.98. At exactly
        // that crossover the UNCAPPED soft margin was 0.05*L/radius = 0.5025 of radius. Freezing the ratio at
        // 0.5 therefore continues the curve the guard was already on instead of bending it: the elbow height
        // the guard PERMITS, in units of its own circle, runs 0.405 / 0.517 / 0.650 / 0.835 at reach 0.80 /
        // 0.90 / 0.95 / 0.98 and then held 1.000 (i.e. nothing) for ever. It now holds 0.833 instead -- the
        // 0.835 it had just below the cliff, continued flat. No step, no new shape, just no cliff.
        //
        // ⚠️ CORPUS VETO, SEGMENTED BY EXTENSION BAND, because pooling hides breaches (a 90 deg humeral twist
        // limit passed pooled and breached 3% in two elevation bands once split). Over the 109-clip CMU corpus,
        // 265,939 arm-frames, the house 3% precedent (BasisSpineAnatomyCorpusTests.k_MaxFireFraction):
        //
        //     ext band      [.85,.92) [.92,.95) [.95,.98) [.98,.99) [.99,1.01)   worst clip
        //     fires         0.010%    0.000%    0.000%    0.000%    0.409%       see the gate
        //
        // The band this acts in is the one the corpus can LEAST referee -- see the frame note below -- so the
        // number is a veto that was not exercised, not a fit. It is sized off continuity with 0.98, which the
        // corpus CAN referee, not off the tail it cannot.
        // =============================================================================================
        public const float SoftMarginMaxFracRadius = 0.5f;

        // =============================================================================================
        // ⭐ THE TIE BAND. `s` DOES NOT MERELY VANISH AT THE TOP OF THE CIRCLE -- IT IS NOISE THERE, AND
        // THE BRANCH BELOW WAS RINGING ON IT EVERY FRAME.
        //
        // Inside |s| < this, the elbow's lateral offset from the mirror plane is under a tenth of its own
        // circle, which is millimetres: the measured pole is no longer telling us which side the elbow is
        // on, it is telling us what the tracker's jitter did this frame. The band is a FRACTION OF THE
        // RADIUS for the same reason SoftMarginMaxFracRadius is -- it is stated in units of the circle the
        // swivel actually moves the elbow on, so it cannot silently decouple from any avatar scale, segment
        // ratio or extension.
        //
        // SIZED OFF THE MEASURED NOISE FLOOR, NOT CHOSEN. Elbow parked at the top of its circle, 200 frames
        // of gaussian jitter on the elbow position, counting how many times the branch flips side:
        //
        //     band k     flips @0.5mm    flips @2mm     flips @5mm     worst-jump cost
        //     0.00          304             283            306              +0.000 mm      <- shipped
        //     0.02            0             168            281              +0.181 mm
        //     0.05            0               6            151              +1.074 mm
        //     0.10            0               0             34              +4.772 mm      <- this
        //     0.15            0               0              0              +8.900 mm
        //     0.30            0               0              0             +29.943 mm
        //
        // 0.10 is the smallest band that reaches ZERO flips across the realistic tracker band (0.5-2 mm of
        // elbow position noise); 0.05 still leaks at 2 mm. The cost column is the growth in the worst
        // single-step elbow jump on the pose it is worst on, and it is linear in the band, so moving to
        // 0.15 to cover a 5 mm-noise tracker is a one-line change whose price is already stated.
        // =============================================================================================
        public const float TieBandFracRadius = 0.10f;

        /// <summary>
        /// The corrective swivel, in radians, about the shoulder->hand axis, that brings the elbow back inside
        /// the anatomical envelope. Returns exactly 0f when the elbow is already legal.
        ///
        /// `totalLen` is the whole arm (upper + lower): the margins are fractions of it, so the guard is
        /// SCALE-FREE -- a child avatar and a giant get the same posture, not the same centimetres.
        ///
        /// `torsoUp` is the TORSO's up (chest->neck), NOT the player root's. The law is only true in that
        /// frame -- see the frame note in this class's summary, which is measured. A world/root up turns
        /// this guard into a hazard the moment the user bends over.
        ///
        /// This overload declines the tie band entirely and is BIT-IDENTICAL to the pre-band guard, so every
        /// caller, test and offline sweep that has not been taught to carry a side is unchanged.
        /// </summary>
        public static float GuardSwivelRad(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 torsoUp, float totalLen)
        {
            return GuardSwivelRad(shoulder, elbow, hand, torsoUp, totalLen, Vector3.zero, 0, out _);
        }

        /// <summary>
        /// As above, plus the two symmetry-breaking inputs the branch needs at the top of the circle.
        ///
        /// `prevSide` is the side this arm's guard chose last frame (-1 / +1), or 0 for "no history" -- a
        /// cold start, a dropout, or a frame on which the guard did not fire. `sideUsed` is what to carry
        /// into next frame's `prevSide`; it is 0 whenever the guard declined or was inside its envelope, so
        /// a stale side can never be held across a stretch where the guard was not the thing deciding.
        ///
        /// `lateralOut` is the body-lateral axis pointing AWAY from the torso for this arm (the shoulder
        /// line, signed by which arm this is). It is used ONLY to seed the very first frame inside the tie
        /// band, where there is no history and `sign(s)` is a coin flip that hysteresis would then hold.
        ///
        /// Zero/0 for either -- the struct defaults -- declines that half, and zero for BOTH is bit-identical
        /// to the pre-band guard.
        /// </summary>
        public static float GuardSwivelRad(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 torsoUp, float totalLen,
            Vector3 lateralOut, int prevSide, out int sideUsed)
        {
            sideUsed = 0;
            Vector3 ac = hand - shoulder;
            float acSqr = ac.sqrMagnitude;
            // Reject-unless-good throughout: NaN fails every ordered comparison, so `!(x > eps)` catches it
            // where `x < eps` would wave it straight through into the bone -- and a NaN transform PERSISTS in
            // Unity, so the arm would never recover even once good data returned.
            if (!(acSqr > k_SqrEpsilon) || !(totalLen > k_Epsilon))
            {
                return 0f;
            }

            Vector3 up = torsoUp;
            float upSqr = up.sqrMagnitude;
            if (!(upSqr > k_SqrEpsilon))
            {
                return 0f;
            }
            up /= Mathf.Sqrt(upSqr);

            Vector3 acN = ac / Mathf.Sqrt(acSqr);

            Vector3 ae = elbow - shoulder;
            Vector3 aeProj = ae - acN * Vector3.Dot(ae, acN);
            float radius = aeProj.magnitude;
            // The arm is straight: the elbow's circle has collapsed to a point, so there is no swivel that
            // could move it anywhere. Nothing to guard, and nothing that could go wrong.
            if (!(radius > k_Epsilon))
            {
                return 0f;
            }

            // The circle's plane is perpendicular to the arm axis. If the arm axis is VERTICAL, that plane is
            // horizontal: every point on the circle sits at the same height and the constraint cannot be
            // expressed as a swivel. (Physically: the arm is straight up or straight down, and swinging the
            // elbow around it does not raise or lower the elbow at all.)
            Vector3 upProj = up - acN * Vector3.Dot(up, acN);
            float upLen = upProj.magnitude;
            if (!(upLen > k_Epsilon))
            {
                return 0f;
            }

            Vector3 upN = upProj / upLen;
            Vector3 w = Vector3.Cross(acN, upN);   // the other in-plane basis vector; (upN, w) span the circle

            // THE CEILING: the higher of the shoulder (0, by construction -- everything is measured from it)
            // and the hand. This is the measured law.
            float handUp = Vector3.Dot(ac, up);
            float ceiling = handUp > 0f ? handUp : 0f;

            // The margins, capped at a fraction of the circle the swivel can actually move the elbow on --
            // above that cap the guard is arithmetically unable to fire, whatever the numbers say. See
            // SoftMarginMaxFracRadius. Untouched (bit for bit) wherever the cap does not bind, and the
            // soft:hard ratio is carried through unchanged so the saturation keeps its shape rather than
            // flattening into a near-identity against an asymptote the elbow could never have reached.
            float softRise = SoftMarginFracLimb * totalLen;
            float hardRise = HardMarginFracLimb * totalLen;
            float cap = SoftMarginMaxFracRadius * radius;
            if (softRise > cap)
            {
                hardRise *= cap / softRise;
                softRise = cap;
            }

            float hSoft = ceiling + softRise;
            float hHard = ceiling + hardRise;

            float h = Vector3.Dot(ae, up);   // the elbow's height above the shoulder, right now

            // Inside the envelope: the IDENTITY, exactly. No normalisation, no float drift, bit for bit the
            // unguarded solve. A guard that perturbs legal poses to fix illegal ones is the wrong trade.
            if (!(h > hSoft))
            {
                return 0f;
            }

            float M = hHard - hSoft;
            if (!(M > k_Epsilon))
            {
                return 0f;   // degenerate margins: decline rather than divide by ~0
            }

            // out = soft + M*e/(M + e).  Slope exactly 1 where it takes over, asymptotic to hard.
            float e = h - hSoft;
            float hGuarded = hSoft + M * e / (M + e);

            // Back-solve the pole. The elbow's height is AFFINE in the pole's up-component:
            //     h = dot(ae, acN)*dot(acN, up)  +  radius * upLen * c        where c = dot(poleDir, upN)
            float along = Vector3.Dot(ae, acN) * Vector3.Dot(acN, up);
            float denom = radius * upLen;
            float cG = (hGuarded - along) / denom;
            cG = cG > 1f ? 1f : (cG > -1f ? cG : -1f);   // this shape clamps NaN to -1 (Mathf.Clamp does not)

            // =========================================================================================
            // ⛔⭐ THIS SIGN BRANCH IS DISCONTINUOUS AT THE TOP OF THE CIRCLE, IT IS KNOWN, IT IS
            // MEASURED, AND IT IS KEPT. THE THREE ALTERNATIVES WERE BUILT AND RUN; ALL ARE WORSE.
            //
            // `s` is EXACTLY ZERO when the elbow sits at the top of its circle (poleDir == upN there), so
            // a FIRING guard whose elbow crosses the top throws it to the other side -- a jump of
            //
            //     2 * radius * sqrt(1 - cG^2)   metres
            //
            // MEASURED through BasisArmSolveCore's full stream, an elbow tracker orbited across the top in
            // 0.005 deg steps: 34.3 mm at reach 0.700, 216.5 mm at reach 0.700 / el -25, 303.4 mm at reach
            // 0.600, each within 30% of that closed form. It is reachable from EVERY input axis -- hand
            // azimuth, controller roll and tracker orbit all walk the elbow over the top -- which is why
            // BasisArmInvariantNet found it four separate ways. See
            // BasisArmInvariantNetKnownOpenDefectTests, which reproduces it in isolation.
            //
            // ⭐⭐ WHY IT CANNOT BE REMOVED, AND THE EARLIER ANSWER HERE NAMED THE WRONG OBSTRUCTION.
            //
            // This comment used to say the obstruction was a MIRROR SYMMETRY -- that at the top the whole
            // input is symmetric about the plane spanned by acN and upN, so any continuous guard built from
            // those inputs is reflection-equivariant and its output at the top must lie in that plane. The
            // symmetry is real. But it is NOT what binds, and believing it was is what made "add an input
            // that breaks the symmetry" look like a fix. It is not one. The obstruction is TOPOLOGICAL and
            // it survives ANY set of inputs whatsoever:
            //
            //     The guard is the identity on legal poses, so it is pinned to the identity at BOTH ends
            //     of the firing arc: f(-phi_soft) = -phi_soft and f(+phi_soft) = +phi_soft. It must also
            //     keep the elbow out of the forbidden arc (-phi_hard, +phi_hard), which is non-empty
            //     whenever the circle's top clears the hard limit. A continuous path on the circle from
            //     -phi_soft to +phi_soft either passes through 0 -- forbidden -- or goes the long way
            //     round through pi. There is no third route. Lifting to the reals: the winding number is
            //     0 (a jump) or non-zero (a traverse).
            //
            // Nothing in that argument mentions the inputs, so no input can defeat it -- not a body-lateral
            // axis, not handedness, not the previous frame. The traverse branch is shape C below, and its
            // gain is (pi - phi_soft)/phi_soft, which DIVERGES as the violation shrinks: a hair-illegal
            // elbow gets swung 180 degrees. So the honest statement is a trade, not an oversight:
            //
            //     IDENTITY-ON-LEGAL + ENFORCEMENT  =>  a jump, OR an unbounded gain. Never neither.
            //
            // The shipped branch takes the jump, and it takes the SMALLEST one available: the jump at a cut
            // placed at azimuth phi_c is the chord across the forbidden arc there, 2*radius*sin(phiG(phi_c)),
            // and phiG is increasing in |phi_c|, so phi_c = 0 minimises it. The same placement independently
            // minimises the CORRECTION at the cut and makes the two sides' corrections equal in magnitude.
            // Three separate optimality properties, one placement. BasisElbowAnatomyBranchTests proves each
            // by scanning every alternative placement rather than by asserting it here.
            //
            // Both continuous alternatives were built and run against the shipped suite:
            //
            //   B  cap the swivel by the distance to the branch point, exactly as BasisArmSolveCore's
            //      three roll stages cap theirs (pull = min(phiG - |phi|, |phi|)).  CONTINUOUS, gain 2.
            //      ⛔ IT DISABLES THE GUARD AT THE ONE POSE THE GUARD EXISTS FOR. Ten tests red, 696
            //      invariant breaches in one of them; the elbow ends 24.0 cm above the anatomical ceiling
            //      against a 9.0 cm hard limit with a mis-strapped tracker; 240.0 mm above the ceiling at
            //      reach 0.50 where the unguarded solve also leaves it 240.0 mm -- i.e. the guard has
            //      become the identity. AnElbowPointedAtTheSky_IsPulledBackUnderTheCeiling,
            //      NoPointOnTheCircle_CanEndUpAboveTheHardLimit and
            //      AnIllegalPoseInTheTiltedFrame_IsStillCaught all go red, and the last of those fails
            //      with the message it was written to print: "the guard went SILENT on an elbow at the top
            //      of its circle". That is the ORIGINAL reported bug, reopened.
            //
            //   C  traverse the complement: sweep the guarded elbow round to the BOTTOM as the raw elbow
            //      approaches the top, so the envelope is enforced everywhere AND the map is continuous
            //      in the azimuth.  ⛔ IT RELOCATES THE DISCONTINUITY INTO THE POSE CHANNEL, and makes it
            //      worse: at the top the correction is 180 deg for ANY firing pose, so it jumps 0 -> 180
            //      deg as a pose crosses the soft margin by a micron. Ten tests red, including
            //      HandElevationSweep_ProducesNoJump (up to 155.6x amplification on a smooth input --
            //      the net catching exactly the relocation) and 728 breaches of the guard's own saturation
            //      curve (the elbow lands 287 mm BELOW the ceiling where the curve gives +68 mm).
            //
            // ⚠️ SO THE TRADE IS: A BOUNDED POP AT ONE AZIMUTH, versus AN ELBOW THAT POINTS AT THE SKY.
            // The pop is bounded by the circle's own diameter, it shrinks continuously to ZERO as the pose
            // becomes only marginally illegal (phiG -> phi_soft -> 0), and it happens where the correct
            // answer is genuinely bimodal. The alternative is the guard not existing.
            //
            // ⭐⭐⭐ BUT THE POP WAS NEVER THE SEVERE HALF, AND THAT HALF *IS* FIXED -- BY HYSTERESIS.
            //
            // A cut costs one pop when the elbow CROSSES it. It costs a sustained BUZZ when the elbow SITS
            // on it, because `s` is then noise and the branch re-decides every frame. The elbow sitting at
            // the top of its circle is not a corner case: it is the exact pose this guard exists to catch,
            // so the failure mode that summons the guard is the same one that parks the elbow on the cut.
            // Measured, elbow parked at the top with 2 mm of jitter, 300 frames:
            //
            //     SHIPPED (sign(s) alone)   ~150 side flips, dragging the elbow through 29-48 METRES of
            //                               path -- a ~160 mm teleport on every other frame, for an input
            //                               that is standing still.
            //     WITH THE TIE BAND         0 flips at EVERY parking azimuth; the elbow's path collapses to
            //                               the honest jitter response, ~0.8 m, a 35-130x reduction.
            //
            // ⚠️ AND NOTHING DOWNSTREAM WAS CATCHING IT. BasisSwingContinuityCore rate-limits exactly this
            // kind of elbow swing, but it engages ONLY when the torso-collision tag changes -- its own
            // header says "free-air motion, POLE FLIPS and target teleports are accepted instantly". This
            // is a free-air pole flip. It reached the bone at full amplitude, every frame.
            //
            // ⭐ HYSTERESIS REMOVES THE BUZZ; A BODY-LATERAL AXIS ONLY MOVES IT. Both were built and swept
            // over every parking azimuth in the firing arc, 2 mm jitter, worst flips over 200 frames:
            //
            //     shipped            102 flips, at s = 0 (the top)
            //     lateral axis only   96 flips, at s = -0.100  <- the spike MOVED, at full amplitude
            //     hysteresis            0 flips, at every azimuth tried
            //
            // That is the difference between relocating a discontinuity and eliminating a failure mode, and
            // it is why the lateral axis is NOT the branch here. A dead band decided by anatomy still has a
            // cut at the band's edge, and an elbow can park on that edge just as happily. A dead band
            // decided by HISTORY has its flip points at DIFFERENT places depending on which way you came --
            // that is what hysteresis is -- so there is no azimuth at all that re-decides under noise.
            //
            // ⚠️ THE PRICE, STATED PLAINLY: THE CHOSEN SIDE IS NOW PATH-DEPENDENT. What is NOT path
            // dependent is the anatomy: cG -- the guarded HEIGHT -- is not a function of the branch, so the
            // envelope this class exists to enforce is bit-identical with the band on, off, or seeded
            // either way. Only the coin flip became sticky, which is the one quantity that should be.
            //
            // The lateral axis is kept for the one job it is actually good at: SEEDING that coin flip on
            // the first frame, so hysteresis holds an anatomically-outward elbow rather than whichever side
            // float noise happened to pick.
            //
            // BasisElbowAnatomyBranchTests pins every number above, and
            // BasisArmTrackerForearmSeamTests.TheElbowGuardsBranchCut_IsTheLeastBadOfThree pins the
            // three-shape comparison, so the argument is re-checked by machine rather than re-litigated
            // from this comment.
            // =========================================================================================
            Vector3 poleDir = aeProj / radius;
            float s = Vector3.Dot(poleDir, w);
            int side = s < 0f ? -1 : 1;
            if (Mathf.Abs(s) < TieBandFracRadius)
            {
                if (prevSide != 0)
                {
                    side = prevSide < 0 ? -1 : 1;
                }
                else
                {
                    float latSqr = lateralOut.sqrMagnitude;
                    if (latSqr > k_SqrEpsilon)
                    {
                        float lat = Vector3.Dot(lateralOut, w);
                        if (lat > 0f)
                        {
                            side = 1;
                        }
                        else if (lat < 0f)
                        {
                            side = -1;
                        }
                    }
                }
            }

            sideUsed = side;
            float sG = side * Mathf.Sqrt(Mathf.Max(1f - cG * cG, 0f));

            Vector3 poleGuarded = upN * cG + w * sG;
            return SignedAngleRad(poleDir, poleGuarded, acN);
        }

        /// <summary>Signed angle from `from` to `to` about `axis`. Both inputs lie in the plane perpendicular
        /// to `axis`, so this is exact. Written the long way because it runs inside a Burst job.</summary>
        static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }

            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);   // NaN -> -1, deliberately
            float angle = Mathf.Acos(c);
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }
    }
}
