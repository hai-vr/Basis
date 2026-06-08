namespace UnityEngine.Animations.Rigging
{
    public struct BasisLegSolveInput
    {
        public Vector3 Root;
        public Vector3 Mid;
        public Vector3 Tip;
        public Quaternion RootRotation;
        public Quaternion MidRotation;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 HintPosition;
        public float HintWeight;
        public Quaternion TargetOffset;
        public Vector3 BendNormal;
    }

    public struct BasisLegSolveResult
    {
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion TipRotation;
        public bool HintApplied;

        public Vector3 KneeSolved;
        public Vector3 FootSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float KneeAngleDeg;
        public byte AxisSource;     // 0 hint, 1 shoulder->target, 2 bend-normal(hint path), 3 bend-normal(no hint)
        public float FootError;
    }

    // Stream-free port of BasisFullIKConstraintJob.SolveTwoBone (the leg path). Differs from
    // BasisArmSolveCore: hint-first axis, BendNormal fallback + near-extension blend, weight-scaled hint.
    public static class BasisLegSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisLegSolveInput i, out BasisLegSolveResult r)
        {
            r = default;

            Vector3 aPosition = i.Root;
            Vector3 bPosition = i.Mid;
            Vector3 cPosition = i.Tip;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;

            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            float hintWeight = i.HintWeight;
            bool hasHint = hintWeight > 0f;

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;

            float maxReach = abLen + bcLen;
            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            Vector3 atCorrected = tPosition - aPosition;
            float atCorrectedLen = atCorrected.magnitude;

            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);

            byte axisSource;
            Vector3 axis;
            if (hasHint)
            {
                axis = Vector3.Cross(i.HintPosition - aPosition, bc);
                axisSource = 0;
                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = Vector3.Cross(atCorrected, bc);
                    axisSource = 1;
                }

                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = i.BendNormal;
                    axisSource = 2;
                }
            }
            else
            {
                axis = i.BendNormal;
                axisSource = 3;
            }

            float extensionRatio = (maxReach > k_Epsilon) ? (atCorrectedLen / maxReach) : 0f;
            if (extensionRatio > 0.9f)
            {
                float blend = Mathf.Clamp01((extensionRatio - 0.9f) / 0.1f);
                axis = Vector3.Slerp(axis.normalized, i.BendNormal.normalized, blend);
            }

            axis = Vector3.Normalize(axis);

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);

            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = QuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            if (hasHint)
            {
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);

                    if (abProj.sqrMagnitude > (maxReach * maxReach * 0.001f) && ahProj.sqrMagnitude > 0f)
                    {
                        hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        hintR.x *= hintWeight;
                        hintR.y *= hintWeight;
                        hintR.z *= hintWeight;
                        hintR = QuaternionExt.NormalizeSafe(hintR);

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

            r.KneeSolved = bPosition;
            r.FootSolved = cPosition;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (maxReach > k_Epsilon) ? atCorrectedLen / maxReach : 0f;
            r.KneeAngleDeg = AngleDeg(aPosition - bPosition, cPosition - bPosition);
            r.AxisSource = axisSource;
            r.FootError = (cPosition - tPosition).magnitude;
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
