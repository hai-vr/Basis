using Unity.Burst;

namespace UnityEngine.Animations.Rigging
{
    public struct BasisElbowPoleCoastState
    {
        public Vector3 Bend;   // last bend direction USED (world, unit, perpendicular to the arm axis)
        public Vector3 Axis;   // last shoulder->hand axis, so the stored bend can be parallel-transported
        public int Seeded;     // 0 until the first valid frame
    }

    public struct BasisElbowPoleCoastResult
    {
        public bool Valid;                     // false on a degenerate pose -> caller keeps the raw field bend
        public Vector3 Bend;                   // the bend direction to hand the solver this frame (unit, perp)
        public BasisElbowPoleCoastState State; // persist when Valid
    }

    /// <summary>
    /// The stateful half of the no-tracker elbow. BasisElbowFieldModel is a STATELESS tangent field on the
    /// sphere of hand directions, so Poincare-Hopf forces it two direction-zeros per reach shell (index 2):
    /// an ACROSS-BODY-UP core (~0.75 reach) and a DOWN-BACK core. A hand path THROUGH one winds the bend
    /// arbitrarily fast -- the "big up-down swing flips", and worst of all "T-pose, hands swept behind while
    /// stretched", which transits the down-back core AT FULL STRETCH and rolls the whole arm ~135 degrees in
    /// a single frame. No stateless formula deletes the cores; the fade that tried to (see the field model's
    /// header) only moved the teleport into healthy workspace.
    ///
    /// ================================================================================================
    /// COAST, DON'T CHASE -- AND ONLY WHERE COASTING IS FREE.
    ///
    /// The elbow, with shoulder and hand fixed, rides a CIRCLE of radius rho = sqrt(1/4 - (reach/2)^2) (in
    /// arm lengths). AT FULL STRETCH THAT CIRCLE COLLAPSES TO A POINT: rho -> 0, so the bend direction sets
    /// only the arm's axial ROLL and moves the elbow's POSITION by nothing. So when the field starts winding
    /// there, chasing it spins the arm for zero positional benefit. The fix is to HOLD the pole (carry the
    /// previous bend, parallel-transported by the axis change) instead of chasing -- but ONLY where holding
    /// costs nothing:
    ///
    ///   * near full stretch (rho small): holding changes roll, not elbow position, and
    ///   * at a core (conditioning small): the field's direction is noise anyway.
    ///
    /// The AND of the two. `track = 1 - rhoCoast*condCoast`. Where EITHER is healthy the pole TRACKS the
    /// field outright, so there is no lag off the cores and no frozen-wrong roll at full stretch away from
    /// one. Measured (harness, the real field chain): the T-pose-hands-back transit falls from 144 deg/frame
    /// to 0 at the core center with 0.0 cm of elbow-position change; a fast free-air front swing tracks with
    /// 0.0 deg of deviation from the raw field (zero lag); and reach 0.90 -- healthy rho -- is left EXACTLY
    /// as the stateless field had it (no phantom 19 cm the conditioning gate alone would have introduced).
    ///
    /// A generous RATE CAP (RateDegPerSec) rides on top so the far side of a core re-acquires the field over
    /// a few frames rather than snapping; it only bites inside the cores (a real elbow never swivels this
    /// fast) and it is what bounds the MID-REACH cores too, where rho is healthy so `track` stays 1.
    /// ================================================================================================
    ///
    /// This is the arm's answer; the leg keeps its own (BasisSwivelHintCore.LegHint), whose reference is body
    /// OUTWARD and whose zero therefore sits outside the reachable set -- it has no core to coast through.
    /// </summary>
    [BurstCompile]
    public static class BasisElbowPoleCoastCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-4f;

        /// <summary>Lever arm (arm lengths) below which the field's bend DIRECTION is noise -> coast, and above
        /// which it is trusted -> track. Matches BasisElbowFieldModel's own conditioning scale.</summary>
        public const float CondLo = 0.02f;
        public const float CondHi = 0.09f;

        /// <summary>Elbow-circle radius rho (arm lengths) below which the bend is nearly pure ROLL (coast is
        /// free) and above which it moves the elbow enough to matter (track). rho = 0.05 is ~reach 0.986;
        /// rho = 0.18 is ~reach 0.93.</summary>
        public const float RhoLo = 0.05f;
        public const float RhoHi = 0.18f;

        /// <summary>Cap on how fast the pole may re-acquire the field across a core. 720 deg/s is far above any
        /// real elbow swivel, so it never lags a deliberate reach; it only bounds a core transit.</summary>
        public const float RateDegPerSec = 720f;

        static float Smooth01(float x)
        {
            x = x < 0f ? 0f : (x > 1f ? 1f : x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Radius of the elbow's reachable circle, in arm lengths, at a given reach ratio (clamped:
        /// beyond full stretch rho is 0, so coasting is free there too).</summary>
        static float RhoOfReach(float reachRatio)
        {
            float along = 0.5f * (reachRatio < 1f ? reachRatio : 1f);
            float r2 = 0.25f - along * along;
            return r2 > 0f ? Mathf.Sqrt(r2) : 0f;
        }

        /// <summary>
        /// `fieldBend` is BasisElbowFieldModel's raw prediction as a world vector off the shoulder (any
        /// magnitude; only its perpendicular direction is used). `cond` is that model's conditioning, `reach`
        /// the shoulder->target distance in arm lengths. Returns the bend the solver should use, and the state
        /// to persist. On a degenerate pose Valid is false and the caller must keep the raw field bend.
        /// </summary>
        public static void Step(BasisElbowPoleCoastState s, Vector3 shoulder, Vector3 hand, Vector3 fieldBend,
            float cond, float reach, float dt, out BasisElbowPoleCoastResult r)
        {
            r = default;
            r.State = s;

            Vector3 ac = hand - shoulder;
            float acSqr = ac.sqrMagnitude;
            if (acSqr < k_SqrEpsilon)
            {
                return;
            }
            Vector3 axis = ac / Mathf.Sqrt(acSqr);

            // Field bend, made a clean unit perpendicular to the current arm axis.
            Vector3 fb = fieldBend - axis * Vector3.Dot(fieldBend, axis);
            float fbSqr = fb.sqrMagnitude;
            if (fbSqr < k_SqrEpsilon)
            {
                return;
            }
            fb /= Mathf.Sqrt(fbSqr);
            r.Valid = true;

            if (s.Seeded == 0)
            {
                r.Bend = fb;
                r.State = new BasisElbowPoleCoastState { Bend = fb, Axis = axis, Seeded = 1 };
                return;
            }

            // Parallel-transport the stored bend to the current axis, so only the swing AROUND the axis is
            // measured -- an arm that merely re-aimed did not swivel.
            Vector3 carried = QuaternionExt.FromToRotation(s.Axis, axis) * s.Bend;
            carried -= axis * Vector3.Dot(carried, axis);
            float carriedSqr = carried.sqrMagnitude;
            if (carriedSqr < k_SqrEpsilon)
            {
                r.Bend = fb;
                r.State = new BasisElbowPoleCoastState { Bend = fb, Axis = axis, Seeded = 1 };
                return;
            }
            carried /= Mathf.Sqrt(carriedSqr);

            // Coast only where BOTH the swivel is positionally moot (rho small) and the field direction is
            // noise (cond small). Track otherwise -> no lag off the cores.
            float rhoCoast = Smooth01((RhoHi - RhoOfReach(reach)) / (RhoHi - RhoLo));
            float condCoast = Smooth01((CondHi - cond) / (CondHi - CondLo));
            float track = 1f - rhoCoast * condCoast;

            float angleDeg = Vector3.Angle(carried, fb);
            float maxStep = RateDegPerSec * dt;
            float want = track * angleDeg;
            float step = want < maxStep ? want : maxStep;

            Vector3 outBend = angleDeg > k_Epsilon ? Vector3.Slerp(carried, fb, step / angleDeg) : carried;
            outBend -= axis * Vector3.Dot(outBend, axis);
            float outSqr = outBend.sqrMagnitude;
            outBend = outSqr > k_SqrEpsilon ? outBend / Mathf.Sqrt(outSqr) : fb;

            r.Bend = outBend;
            r.State = new BasisElbowPoleCoastState { Bend = outBend, Axis = axis, Seeded = 1 };
        }
    }
}
