using Unity.Burst;
using Unity.Mathematics;

using UnityEngine;
namespace Basis.IK
{
    /// <summary>
    /// The stateful half of the reach-behind fix: keep the ACCURATE elbow field (which poses the elbow
    /// correctly out-and-back reaching behind) and just CAP how fast its bend may rotate.
    ///
    /// ================================================================================================
    /// WHY THIS EXISTS.
    ///
    /// The no-tracker elbow bend is a unit tangent field on the sphere of hand directions, so by
    /// Poincare-Hopf it carries topologically-required zeros ("cores"). Sweeping the hand through a core
    /// flips the bend ~180 degrees in a fraction of a degree of hand motion -- the reach-behind snap.
    /// (BasisElbowFieldModel's down-and-back core, azimuth ~130, ~20 below level.) A STATELESS field
    /// cannot delete the core without mis-posing the elbow elsewhere: moving it into the torso
    /// (BasisElbowStereoModel) combed the elbow ACROSS THE BODY reaching behind the back -- "not human".
    ///
    /// So do not fight the topology in the field. Keep the field's pose, and bound the RATE the bend may
    /// turn RELATIVE TO THE HAND. Measured over 199,528 real frames, a human elbow tracks its hand at a
    /// bend-rotation / hand-rotation gain of ~1x (median), 4.6x at p99. Anything faster than that is a
    /// core flip, not a human motion. Cap the gain at MaxGain and the flip becomes a fast-but-bounded
    /// sweep at the human's own ceiling instead of a teleport, while everything away from a core -- where
    /// the field already turns slower than the cap -- passes through BIT-IDENTICAL.
    ///
    /// ================================================================================================
    /// WHY A GAIN CAP, AND WHY IT IS SAFE WHERE THE REVERTED "COAST" WAS NOT.
    ///
    ///   * It is a GAIN cap (bend rotation per unit HAND rotation), not a velocity cap. So it is
    ///     FRAMERATE-INDEPENDENT (per hand-step, no dt -- the velocity/rate blends this project tried
    ///     drifted with fps), and it SELF-SCALES: a fast hand earns a proportionally fast elbow, so there
    ///     is NO lag on ordinary reaching, only at a core where the field demands superhuman gain.
    ///
    ///   * It ALWAYS CHASES the field -- it never HOLDS a pole. So a carried bend that is stale or wrong
    ///     (a tracker just dropped, the avatar teleported, the frame hitched) re-acquires the field within
    ///     a few frames on its own. The reverted BasisElbowPoleCoastCore HELD the pole through the core,
    ///     and in a headset that frozen pole degraded everyday arm motion. This cannot: where the cap does
    ///     not bind it returns the field unchanged, and where it does it still moves toward the field.
    ///
    /// ================================================================================================
    /// THE HAND'S MOTION IS NOT ONLY ITS ROTATION. (2026-07-22 -- the radial hole.)
    ///
    /// The paragraph above used to say the cap "always chases the field ... where it does bind it still
    /// moves toward the field". THAT WAS FALSE IN ONE WHOLE DIRECTION OF MOTION, and the hole was exactly
    /// the size of a punch.
    ///
    /// The budget was `MaxGain * angle(prevAxis, curAxis)` -- the rotation of the shoulder->hand axis. A
    /// hand travelling straight ALONG that axis rotates it by EXACTLY ZERO. Reach out, punch, push, point
    /// and retract: the axis is fixed, so cap == 0, so `clamp(ang, 0, 0) == 0`, and the bend is not slowed,
    /// it is FROZEN at the transported previous bend. Held, not chased -- the coast's failure mode, arrived
    /// at by arithmetic.
    ///
    /// And the field genuinely moves there. BasisElbowFieldModel.Elbow is affine in `tipLocal`, so at fixed
    /// direction `d` the predicted elbow is `c0 + r*(M d)`: its perpendicular component is `a + r*b`, whose
    /// DIRECTION rotates with r whenever a and b are not parallel. BasisSwivelHintCore's elbow-down term
    /// then ramps over reach 0.90->0.99 on top, which is pure radial rotation by construction.
    ///
    /// MEASURED (live ArmHint + live BasisArmSolveCore, reach 0.55->1.00 over 40 frames, direction fixed;
    /// "wander" is degrees of honest direction drift added so the claim does not rest on a perfect line):
    ///
    ///                        wander 0 deg          wander 2 deg        wander 8 deg
    ///     forward punch      22.9 deg / 5.33 cm    9.3 / 2.46 cm       0.0 / 0.26 cm
    ///     down-forward reach 29.8 deg / 7.03 cm   13.7 / 3.45 cm       0.0 / 0.46 cm
    ///     cross-body push    21.5 deg / 5.00 cm   12.7 / 3.26 cm       0.0 / 0.98 cm
    ///
    /// (pole error / worst solved-elbow error vs the uncapped field. The whole pole error also shows up as
    /// humeral ROLL, degree for degree.) For scale, the field model's own published error is 2.07 cm and a
    /// real elbow tracker's is 0.64 cm. By 8 degrees of wander the cap is already the advertised bit-exact
    /// no-op; the damage lives in 0-2 degrees, i.e. in straight-line reaching.
    ///
    /// ================================================================================================
    /// THE FIX: BUDGET THE RADIAL MOTION TOO, AND GATE IT ON THE FIELD'S OWN CONDITIONING.
    ///
    ///     cap = MaxGain * ( angle(prevAxis, curAxis)  +  ReachGain * trust(conditioning) * |dReach| )
    ///
    /// STILL NO dt, and that is not an oversight -- it is the property being protected. The added term is
    /// homogeneous of degree one in the hand step, exactly like the rotation term: split a frame in two and
    /// each half earns half the budget, so the total over any wall-clock interval is identical at 72, 90 or
    /// 120 Hz. A RATE floor (`minRateDeg * dt`) would have been the obvious fix and is the wrong one twice
    /// over: it licenses elbow motion for a hand that is not moving at all, and a hitched 100 ms frame would
    /// hand a core enough budget to flip through in one step. Budget what the hand DID, never what the
    /// clock did.
    ///
    /// WHY THE CONDITIONING GATE. Radial budget is only safe where the field's radial answer is meaningful.
    /// `conditioning` is BendDirection's own output -- the predicted elbow's lever arm off the arm axis, in
    /// arm lengths, a purely GEOMETRIC number -- and it collapses to zero AT the topological cores, which is
    /// precisely where the field's radial sensitivity blows up (both go as 1/|perp|). Measured:
    ///
    ///     at the four cores (within +/-2 deg) ..... conditioning 0.0001 - 0.0185
    ///     along the punch battery above ........... conditioning 0.2069 - 0.4300
    ///     55,140 real corpus frames ............... conditioning p0.1 = 0.1047  (never lower)
    ///
    /// An 11x gap with nothing in it. So the gate is free: full radial budget on every pose a human has ever
    /// actually reached to, and IDENTICALLY ZERO at a core, where the cap keeps its old behaviour to the bit.
    ///
    /// ⚠️ THIS IS NOT THE REVERTED `conf > 0.20` HINT GATE (see BasisSwivelHintCore.ArmHint's header). That
    /// one switched the POLE ITSELF on and off, so it swapped between two unrelated poles and the swap was
    /// the pop. This gates a RATE BUDGET, it is a smoothstep and not a cliff, and at trust = 0 the output is
    /// the shipped cap's output exactly -- there is no second pole to fall back to and nothing to pop
    /// between. It can only ever ADD budget, so no pose that is unclipped today can start being clipped.
    ///
    /// MEASURED COST AT THE CORES (worst single-frame bend step, sweeping through each core; "extending"
    /// means simultaneously reaching out at 0.033 arm lengths/frame, a 0.15 s punch at 90 Hz):
    ///
    ///                          raw field    shipped cap    with radial budget, gated
    ///     core az 131 ........  105.4 deg       0.2 deg              0.2 deg
    ///     core az 264 ........  170.2 deg       0.2 deg              0.2 deg
    ///
    /// UNGATED (ReachTrustLo/Hi both 0) the same sweep leaks 28.6 deg/frame. The gate is what makes this
    /// free rather than a trade, so do not "simplify" it away.
    ///
    /// ⚠️ NOT VERIFIED IN A HEADSET. The offline harness drives the field maths, not the animation job,
    /// and the live-vs-offline gap (frame timing, tracker<->model handoff, state staleness) is exactly
    /// what sank the coast. Raise MaxGain toward infinity to disable the whole cap and A/B; set ReachGain to
    /// 0 to disable ONLY the radial budget and get the pre-2026-07-22 behaviour bit-for-bit. See the
    /// elbow-field memory.
    /// </summary>
    [BurstCompile]
    public static class BasisElbowSwingCapCore
    {
        /// <summary>Max bend-rotation / hand-rotation gain. Just above the human p99 (4.6x) so ordinary
        /// reaching is never clipped, far below a core flip (100x+). Raise toward infinity to disable.</summary>
        public const float MaxGain = 5f;

        /// <summary>
        /// Radians of extra bend budget per arm-length of REACH change, before MaxGain. Set to 0 to disable
        /// the radial budget entirely and get the pre-2026-07-22 cap bit-for-bit.
        ///
        /// 3 because MaxGain * 3 = 15 rad/arm-length covers the field's own worst measured radial
        /// sensitivity anywhere the gate grants FULL trust (max 14.9 over conditioning >= ReachTrustHi),
        /// so a straight reach is never clipped where the term is allowed to act at all. It is a coverage
        /// bound on the field, not a fitted human number -- the 4.6x that sets MaxGain was measured against
        /// hand ROTATION and has no radial counterpart in the corpus (a real hand almost never moves purely
        /// radially: 1.34% of frames, where the mocap's own noise floor dominates the ratio).
        /// </summary>
        public const float ReachGain = 3f;

        /// <summary>
        /// Conditioning (BendDirection's lever arm, arm lengths) below which the radial budget is identically
        /// zero, and at or above which it acts in full. Both ends are measured, not chosen:
        ///
        ///   * BELOW: the four topological cores sit at 0.0001-0.0185, and the radial sensitivity that makes
        ///     radial budget dangerous is still 22.4 rad/arm-length at 0.06-0.08 against a 15 budget. So the
        ///     term must be fully off well before a core, not just at one.
        ///   * ABOVE: binned over 55,140 real corpus frames, 0.00% sit below 0.10 -- the whole distribution
        ///     is 0.10-0.50, 71% of it in 0.16-0.20. Full trust at 0.10 therefore costs real reaching nothing,
        ///     and 0.10 is also where the field's worst sensitivity (14.9) drops under the budget.
        /// </summary>
        public const float ReachTrustLo = 0.06f;
        public const float ReachTrustHi = 0.10f;

        /// <summary>
        /// How much of the radial budget the field's <paramref name="conditioning"/> has earned: 0 at a
        /// topological core, 1 anywhere a human actually reaches, smoothstep between. NaN or negative
        /// conditioning returns 0, so a degenerate frame declines to the rotation-only cap rather than
        /// inventing budget.
        /// </summary>
        public static float ReachTrust(float conditioning)
        {
            if (!(conditioning > ReachTrustLo))
            {
                return 0f;
            }
            float t = math.saturate((conditioning - ReachTrustLo) / (ReachTrustHi - ReachTrustLo));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Rotation-only overload: the pre-2026-07-22 behaviour, bit-for-bit. Every caller that cannot say
        /// how far the hand moved ALONG the arm keeps exactly what it had.
        /// </summary>
        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain)
            => Apply(prevBend, prevAxis, curAxis, rawBend, maxGain, 0f, 0f);

        /// <summary>
        /// Cap the bend's swivel about the shoulder->hand axis to <paramref name="maxGain"/> times the hand's
        /// own motion since last frame -- the axis's rotation PLUS its gated radial travel. All vectors WORLD
        /// space, unit; rawBend and the result are perpendicular to curAxis.
        ///
        /// prevBend / prevAxis are last frame's CAPPED bend and shoulder->hand axis (the caller's per-arm
        /// state). Away from a core the field turns slower than the cap and rawBend is returned unchanged,
        /// bit-for-bit -- so this is a true no-op on ordinary motion.
        ///
        /// <paramref name="dReach"/> is the change in |hand - shoulder| / armLength since prevAxis was
        /// stored -- the component of the hand's motion the axis rotation cannot see, and the one a punch is
        /// made of. Sign is ignored. Pass 0 and this is the rotation-only cap exactly.
        ///
        /// <paramref name="conditioning"/> is the field's own lever arm for this pose (ArmHint's out
        /// parameter, BendDirection's `conditioning`). It gates the radial term ONLY -- see ReachTrust and
        /// the file header. Pass 0 and the radial term is off whatever dReach says.
        /// </summary>
        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain,
                                   float dReach, float conditioning)
        {
            // Transport prevBend onto the plane perpendicular to curAxis: the axis itself rotates frame to
            // frame (body turns, hand moves) and the bend must follow that for free -- only the residual
            // SWIVEL about the axis is what a core spins, and what this caps.
            float3 tp = prevBend - curAxis * math.dot(prevBend, curAxis);
            float tpLen = math.length(tp);
            if (tpLen < 1e-4f)
            {
                return rawBend;   // degenerate transport (axis flipped ~180) -> just take the field
            }
            tp /= tpLen;

            float3 cross = math.cross(curAxis, tp);              // completes the tangent frame; rawBend = tp*cos+cross*sin
            float ang = math.atan2(math.dot(rawBend, cross), math.dot(rawBend, tp));

            // atan2(|cross|, dot), not acos(dot): acos is ill-conditioned near 1 (a barely-moved hand has
            // dot ~ 1, and float32 acos there loses most of its digits), which would make the cap jitter on
            // slow motion. This form is accurate for every angle.
            float dHand = math.atan2(math.length(math.cross(prevAxis, curAxis)), math.dot(prevAxis, curAxis));

            // The radial half of the hand's motion, which dHand is blind to by construction (see the header).
            // Fails CLOSED to the rotation-only budget on a degenerate input, and the finiteness test is
            // load-bearing in BOTH directions: NaN is rejected by `> 0`, but an INFINITE dReach would sail
            // through that and make cap infinite, which does not clamp harder -- it disables the cap
            // altogether and hands a core an unbounded frame. Reject it here rather than downstream.
            float dRadial = 0f;
            float absReach = math.abs(dReach);
            if (absReach > 0f && math.isfinite(absReach))
            {
                dRadial = ReachGain * ReachTrust(conditioning) * absReach;
            }

            float cap = maxGain * (dHand + dRadial);
            float capped = math.clamp(ang, -cap, cap);
            if (capped == ang)
            {
                return rawBend;   // cap not binding -> exact field, no drift on ordinary reaching
            }

            float3 outb = tp * math.cos(capped) + cross * math.sin(capped);
            outb = outb - curAxis * math.dot(outb, curAxis);
            return math.normalizesafe(outb, rawBend);
        }
    }
}
