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
            Vector3 pole = Vector3.ProjectOnPlane(i.Mid - i.Root, axis);
            if (refDir.sqrMagnitude < k_SqrEpsilon || pole.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }
            refDir.Normalize();

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

            BasisSwivelFilterState state = BasisSwivelFilterCore.Step(i.State, curSwivel, i.Dt, i.MinCutoffHz, i.Beta, i.DerivCutoffHz);
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
