namespace UnityEngine.Animations.Rigging
{
    // Stream-free swivel smoother shared by BasisFullIKConstraintJob.SmoothElbowSwivel and SmoothKneeSwivel.
    // Measures the mid joint's roll about the root->tip axis, One-Euro low-passes it (BasisSwivelFilterCore),
    // and rebuilds the mid position on its circle at the smoothed angle. The caller swings the limb onto
    // DesiredMid and restores the tip, so the hand/foot stays exactly on target.
    //
    // The reference direction is expressed in the BODY frame (BodyRotation, the solved hips), never in world.
    // A swivel measured against a world axis is not invariant under a body turn: the pole co-rotates with the
    // player but a world reference does not, so a yaw registers as swivel CHANGE, the One-Euro lags it, and
    // the smoother then drags the limb toward a stale pole. Co-rotating the reference cancels bulk body
    // motion out of the measurement, which is what makes "damp jitter, don't lag a turn" actually hold. It
    // also cancels hips jitter, which the pole would otherwise inherit through the parent chain.
    //
    // No valid body frame => no smoothing. Fabricating one from a bare up-vector would put an arbitrary yaw
    // back into the reference, which is the defect this core exists to remove.
    public struct BasisSwivelSmootherInput
    {
        public Vector3 Root;             // shoulder / hip
        public Vector3 Mid;              // elbow / knee
        public Vector3 Tip;              // hand / foot
        public Quaternion BodyRotation;  // solved hips rotation; the frame the swivel is measured in
        public Vector3 ReferenceLocal;   // body-local reference dir (arm: down, leg: forward)
        public Vector3 FallbackLocal;    // body-local fallback when ReferenceLocal is colinear with the axis; zero = none
        public float Dt;
        public float MinCutoffHz;
        public float Beta;
        public float DerivCutoffHz;
        public BasisSwivelFilterState State;
        public bool Seeded;

        // Scale the One-Euro's responsiveness by how much the swivel measurement is actually WORTH this frame
        // (see Conditioning below). Opt-in: false reproduces the legacy filter exactly, so the elbow path is
        // untouched. The knee turns it on -- a standing leg lives ON the pole singularity, which is where the
        // legacy behaviour is not merely useless but actively harmful.
        public bool ConditionOnPole;

        // The min-cutoff to fall back to at ZERO conditioning, i.e. when the limb is dead straight and the
        // swivel carries no information at all. Lerped toward MinCutoffHz as conditioning recovers. Ignored
        // unless ConditionOnPole. Pass BasisSwivelFilterCore.MinCutoffHz for the heavy standing floor.
        public float SingularMinCutoffHz;
    }

    public struct BasisSwivelSmootherResult
    {
        public bool Valid;        // false => degenerate this frame, caller must not move the bone
        public bool WriteState;   // true => store State back (also true on the seed frame, which does not move the bone)
        public bool Seeded;
        public BasisSwivelFilterState State;
        public Vector3 DesiredMid;
        public float RawSwivelDeg;
        public float SmoothSwivelDeg;

        // sin(angle between the upper bone and the root->tip axis), 0..1. How much the swivel angle is worth:
        // 0 = the mid joint sits ON the axis (pole singularity, bend plane undefined), 1 = limb fully folded.
        // Always reported, whether or not ConditionOnPole is set, so it can be measured and gated in tests.
        public float Conditioning;
    }

    public static class BasisSwivelSmootherCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisSwivelSmootherInput i, out BasisSwivelSmootherResult r)
        {
            r = default;
            r.DesiredMid = i.Mid;
            r.State = i.State;
            r.Seeded = i.Seeded;

            if (i.Dt <= 1e-6f)
            {
                return;
            }

            Quaternion body = i.BodyRotation;
            if (body.x * body.x + body.y * body.y + body.z * body.z + body.w * body.w < 0.5f)
            {
                return;
            }

            Vector3 ac = i.Tip - i.Root;
            float acSqr = ac.sqrMagnitude;
            if (acSqr < k_SqrEpsilon)
            {
                return;
            }
            Vector3 axis = ac / Mathf.Sqrt(acSqr);

            Vector3 refDir = Vector3.ProjectOnPlane(body * i.ReferenceLocal, axis);
            if (refDir.sqrMagnitude < k_SqrEpsilon && i.FallbackLocal.sqrMagnitude > k_SqrEpsilon)
            {
                refDir = Vector3.ProjectOnPlane(body * i.FallbackLocal, axis);
            }
            Vector3 upper = i.Mid - i.Root;
            Vector3 pole = Vector3.ProjectOnPlane(upper, axis);
            if (refDir.sqrMagnitude < k_SqrEpsilon || pole.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }
            refDir.Normalize();

            // How much is this swivel measurement actually worth?
            //
            // `pole` is the mid joint's PERPENDICULAR LEVER ARM from the root->tip axis, so
            //     |pole| / |upper| == sin(angle between the upper bone and that axis)
            // which is exactly the conditioning of the swivel: it goes to ZERO when the mid joint sits on the
            // axis -- the pole singularity, where the bend plane is undefined and SignedAngle below is reading
            // the direction of a vanishing vector, i.e. pure noise.
            //
            // The existing guard (pole.sqrMagnitude < 1e-8, so |pole| < 0.1 mm) only rejects the EXACT
            // singularity. Everything in the ill-conditioned band -- a millimetre to a few centimetres of lever
            // arm, which is precisely where a standing leg lives, because footHeightOffset is clamped so the
            // legs fully extend -- sails through and gets measured as if it were signal.
            float upperLen = upper.magnitude;
            float conditioning = upperLen > k_Epsilon ? Mathf.Clamp01(pole.magnitude / upperLen) : 0f;
            r.Conditioning = conditioning;

            float curSwivel = Vector3.SignedAngle(refDir, pole, axis);
            r.RawSwivelDeg = curSwivel;

            if (!i.Seeded)
            {
                r.State = BasisSwivelFilterCore.Seed(curSwivel);
                r.Seeded = true;
                r.WriteState = true;
                r.SmoothSwivelDeg = curSwivel;
                return;
            }

            float minCutoffHz = i.MinCutoffHz;
            float beta = i.Beta;
            if (i.ConditionOnPole)
            {
                // A One-Euro is SPEED-ADAPTIVE: `cutoff = minCutoff + beta * |velocity|`. Its whole premise is
                // "a fast-moving signal is a signal the user MEANT, so stop filtering and track it". At the pole
                // singularity that premise inverts. The measured swivel is noise thrashing through large angles,
                // so |velocity| is huge -- and the filter reads that as intent, throws the cutoff wide open, and
                // passes the garbage straight through. With beta 0.20 and a 500 deg/s noise velocity the cutoff
                // lands at ~101 Hz, i.e. alpha ~= 1: effectively no filtering, exactly when there is nothing but
                // noise to filter. **A speed-adaptive filter is actively harmful at a singularity** -- it
                // amplifies the very thing it was added to suppress.
                //
                // So scale the adaptivity by the conditioning:
                //   beta -> 0 as the limb straightens, so the cutoff STOPS opening on noise velocity;
                //   minCutoff falls back to the heavy standing floor, since there is nothing worth tracking.
                // Away from the singularity both return to their full values, so deliberate shin motion at a
                // genuinely bent knee is NOT lagged. This is principled rather than a fudge: an ill-conditioned
                // measurement should be trusted less, in exact proportion to how ill-conditioned it is.
                //
                // The snap the user feels is the payoff of this: while the leg is straight the filter chases
                // noise, so the swivel state is wherever the jitter last flung it -- and the moment the leg bends
                // and the lever arm grows, that accumulated garbage becomes VISIBLE and the knee whips to it.
                // Holding the swivel steady while straight means it is already sane when the bend arrives.
                beta *= conditioning;
                minCutoffHz = Mathf.Lerp(i.SingularMinCutoffHz, i.MinCutoffHz, conditioning);
            }

            BasisSwivelFilterState state = BasisSwivelFilterCore.Step(i.State, curSwivel, i.Dt, minCutoffHz, beta, i.DerivCutoffHz);
            r.State = state;
            r.Seeded = true;
            r.WriteState = true;
            r.SmoothSwivelDeg = state.Smooth;

            Vector3 center = i.Root + axis * Vector3.Dot(i.Mid - i.Root, axis);
            float radius = (i.Mid - center).magnitude;
            if (radius < k_Epsilon)
            {
                return;
            }

            r.DesiredMid = center + (Quaternion.AngleAxis(state.Smooth, axis) * refDir) * radius;
            r.Valid = true;
        }
    }
}
