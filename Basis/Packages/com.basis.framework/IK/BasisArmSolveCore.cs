namespace UnityEngine.Animations.Rigging
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
        public float HintMaxStepDeg;   // max elbow-swivel change this solve; float.MaxValue = unclamped (offline)
    }

    public struct BasisArmSolveResult
    {
        // Apply through the AnimationStream in this order; identity steps are exact no-ops:
        //   mid.SetRotation(MidDelta * mid.GetRotation), root.SetRotation(RootDelta * ...),
        //   root.SetRotation(HintDelta * ...), tip.SetRotation(TipRotation).
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
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
    }

    // Stream-free geometry shared by BasisFullIKConstraintJob.SolveTwoBoneIKArms and the
    // offline sweep harness. Change the elbow math HERE so both stay in lock-step.
    public static class BasisArmSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;

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

            // Prefer current bend plane; fall back to hint / target / player-up if collinear.
            byte axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = i.HintWeight ? Vector3.Cross(i.HintPosition - aPosition, bc) : Vector3.zero;
                axisSource = 1;
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

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);

            // mid.SetRotation(deltaR * midRot): tip rotates about the elbow pivot.
            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            // --- rotate root toward the corrected target direction ---
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = QuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                // Propagate root rotation to its children (mid + tip), pivoting about A.
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            float hintFade = 0f;
            float hintProjMag = 0f;
            float armProjMag = 0f;
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
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;

                    // Fade only when the pole genuinely collapses onto the shoulder->hand axis,
                    // keyed on the projection magnitude itself, not raw extension (which discarded
                    // tracker follow on 21% of the workspace).
                    float projNorm = (totalLen > k_Epsilon) ? ahProj.magnitude / totalLen : 0f;
                    hintFade = Mathf.Clamp01((projNorm - 0.06f) / 0.12f);
                    if (hintFade > 0f && abProj.sqrMagnitude > (totalLen * totalLen * 0.001f) && ahProj.sqrMagnitude > (totalLen * totalLen * 0.001f))
                    {
                        hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        if (hintFade < 1f)
                        {
                            hintR = Quaternion.Slerp(Quaternion.identity, hintR, hintFade);
                        }
                        hintR = QuaternionExt.NormalizeSafe(hintR);

                        // Rate-limit the swivel so the elbow eases toward the pole instead of
                        // snapping ~180 deg when the hint crosses to the opposite side of the
                        // current elbow (the long-standing pole flip). Reach is unaffected; this
                        // only bounds the swivel rotation. Offline callers pass MaxValue (no clamp).
                        float hintAngle = 2f * Mathf.Acos(Mathf.Clamp(Mathf.Abs(hintR.w), 0f, 1f)) * Mathf.Rad2Deg;
                        if (hintAngle > i.HintMaxStepDeg && hintAngle > k_Epsilon)
                        {
                            hintR = Quaternion.Slerp(Quaternion.identity, hintR, i.HintMaxStepDeg / hintAngle);
                        }

                        rootRot = hintR * rootRot;
                        bPosition = aPosition + hintR * (bPosition - aPosition);
                        cPosition = aPosition + hintR * (cPosition - aPosition);
                        midRot = hintR * midRot;
                        hintApplied = true;
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
            r.AxisSource = axisSource;
            r.HandError = (cPosition - tPosition).magnitude;
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
