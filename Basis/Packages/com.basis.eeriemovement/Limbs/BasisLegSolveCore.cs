using UnityEngine;

namespace Basis.IK
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

        public float HintDistrust;
        public Quaternion TargetOffset;
        public Vector3 BendNormal;

        public Vector3 AnteriorNormal;

        public Quaternion HintRotation;

        public bool HintIsTracker;
    }

    public struct BasisLegSolveResult
    {
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion MidPostRoll;
        public Quaternion TipRotation;
        public bool HintApplied;
        public float ShinRollDeg;

        public Vector3 KneeSolved;
        public Vector3 FootSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float KneeAngleDeg;
        public byte AxisSource;
        public float FootError;
    }

    public static class BasisLegSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;
        const float k_PoleColinearSin = 0.5f;

        public const float MinKneeInteriorDeg = 20f;

        public const float MaxKneeInteriorDeg = 176f;

        public const float KneeAnteriorSoftDeg = 85f;
        public const float KneeAnteriorHardDeg = 89.5f;

        public const float KneeAnteriorTaperStartDeg = 160f;

        public const float TrackerShinRollMaxDeg = 45f;

        public static float ClampKneeSwivelDeg(float swivelDeg, float softDeg, float hardDeg)
        {
            float wrapped = swivelDeg - 360f * Mathf.Floor((swivelDeg + 180f) / 360f);

            float mag = wrapped < 0f ? -wrapped : wrapped;

            if (!(mag >= 0f))
            {
                return 0f;
            }

            if (!(mag > softDeg))
            {
                return wrapped;
            }

            float maxExcess = hardDeg - softDeg;
            if (!(maxExcess > 0f))
            {
                return wrapped < 0f ? -softDeg : softDeg;
            }

            float excess = mag - softDeg;
            float compressed = softDeg + maxExcess * excess / (maxExcess + excess);

            float taperSpan = 180f - KneeAnteriorTaperStartDeg;
            if (mag > KneeAnteriorTaperStartDeg && taperSpan > 0f)
            {
                float u = (mag - KneeAnteriorTaperStartDeg) / taperSpan;
                if (u > 1f) u = 1f;
                compressed *= 1f - u * u * (3f - 2f * u);
            }

            return wrapped < 0f ? -compressed : compressed;
        }

        public static void Solve(in BasisLegSolveInput i, out BasisLegSolveResult r)
        {
            r = default;

            r.MidPostRoll = Quaternion.identity;

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

            float minFlexReach = MinFlexionReach(abLen, bcLen);
            if (atCorrectedLen < minFlexReach) atCorrectedLen = minFlexReach;

            float maxExtReach = MaxExtensionReach(abLen, bcLen);
            if (atCorrectedLen > maxExtReach) atCorrectedLen = maxExtReach;

            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);

            byte axisSource = 0;
            Vector3 bendAxis = Vector3.Cross(ab, bc);
            if (bendAxis.sqrMagnitude < k_SqrEpsilon)
            {
                if (hasHint)
                {
                    bendAxis = Vector3.Cross(i.HintPosition - aPosition, bc);
                    axisSource = 1;
                }

                if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                {
                    bendAxis = Vector3.Cross(atCorrected, bc);
                    axisSource = 2;
                }

                if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                {
                    Vector3 bcN = bcLen > k_Epsilon ? bc / bcLen : Vector3.zero;
                    bendAxis = i.BendNormal - bcN * Vector3.Dot(i.BendNormal, bcN);
                    axisSource = 3;

                    if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                    {
                        bendAxis = i.BendNormal;
                    }
                }
            }

            bendAxis = Vector3.Normalize(bendAxis);

            float half = 0.5f * (oldAbcAngle - newAbcAngle);
            float sinHalf = Mathf.Sin(half);
            float cosHalf = Mathf.Cos(half);
            Quaternion deltaR = new Quaternion(bendAxis.x * sinHalf, bendAxis.y * sinHalf, bendAxis.z * sinHalf, cosHalf);

            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = BasisQuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;

            Vector3 acFinal = cPosition - aPosition;
            float acFinalSqr = acFinal.sqrMagnitude;
            if (acFinalSqr > k_SqrEpsilon)
            {
                Vector3 acNorm = acFinal / Mathf.Sqrt(acFinalSqr);
                Vector3 kneeDir = Vector3.Cross(acNorm, rootDelta * bendAxis);
                kneeDir -= acNorm * Vector3.Dot(kneeDir, acNorm);

                Vector3 bendPole = Vector3.Cross(acNorm, i.BendNormal);
                bendPole -= acNorm * Vector3.Dot(bendPole, acNorm);
                bool hasBendPole = bendPole.sqrMagnitude > k_SqrEpsilon;

                Vector3 anteriorPole = bendPole;
                bool hasAnteriorPole = hasBendPole;
                if (i.AnteriorNormal.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 ap = Vector3.Cross(acNorm, i.AnteriorNormal);
                    ap -= acNorm * Vector3.Dot(ap, acNorm);
                    if (ap.sqrMagnitude > k_SqrEpsilon)
                    {
                        anteriorPole = ap;
                        hasAnteriorPole = true;
                    }
                }

                Vector3 pole = bendPole;
                if (hasHint)
                {
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    float ahLen = ah.magnitude;

                    if (ahProj.sqrMagnitude > k_SqrEpsilon)
                    {
                        pole = ahProj;
                    }

                    pole = GuardPoleAnterior(pole, anteriorPole, acNorm, hasAnteriorPole, ref axisSource);

                    float poleSin = ahLen > k_Epsilon ? ahProj.magnitude / ahLen : 0f;
                    if (poleSin < k_PoleColinearSin && hasBendPole && pole.sqrMagnitude > k_SqrEpsilon)
                    {
                        float blend = 1f - poleSin / k_PoleColinearSin;
                        pole = Vector3.Slerp(pole.normalized, bendPole.normalized, blend);
                        axisSource = 4;
                    }

                    if (i.HintDistrust > 0f && hasBendPole && pole.sqrMagnitude > k_SqrEpsilon)
                    {
                        pole = Vector3.Slerp(pole.normalized, bendPole.normalized, Mathf.Clamp01(i.HintDistrust));
                        axisSource = 5;
                    }
                }

                pole -= acNorm * Vector3.Dot(pole, acNorm);

                pole = GuardPoleAnterior(pole, anteriorPole, acNorm, hasAnteriorPole, ref axisSource);

                float weight = hasHint ? hintWeight : 1f;

                if (weight > 0f && kneeDir.sqrMagnitude > k_SqrEpsilon && pole.sqrMagnitude > k_SqrEpsilon)
                {
                    float swivel = ScaleSwivel(SignedAngleRad(kneeDir, pole, acNorm), weight);
                    hintR = AngleAxisRad(swivel, acNorm);

                    rootRot = hintR * rootRot;
                    bPosition = aPosition + hintR * (bPosition - aPosition);
                    cPosition = aPosition + hintR * (cPosition - aPosition);
                    midRot = hintR * midRot;
                    hintApplied = hasHint;
                }
            }

            float hintRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                             + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
            if (i.HintIsTracker && hintRotSqr > 0.5f)
            {
                Vector3 shinRoll = cPosition - bPosition;
                if (shinRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 shinRollN = shinRoll.normalized;
                    float roll = TwistAngleRad(i.HintRotation * Quaternion.Inverse(midRot), shinRollN);

                    float rollAbs = Mathf.Abs(roll);
                    float rollCap = TrackerShinRollMaxDeg * Mathf.Deg2Rad;
                    if (rollAbs > rollCap) rollAbs = rollCap;
                    if (rollAbs > 1e-6f)
                    {
                        float rollSigned = roll < 0f ? -rollAbs : rollAbs;
                        r.MidPostRoll = AngleAxisRad(rollSigned, shinRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ShinRollDeg = rollSigned * Mathf.Rad2Deg;
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

        static Vector3 GuardPoleAnterior(Vector3 pole, Vector3 anteriorPole, Vector3 acNorm, bool hasAnteriorPole, ref byte axisSource)
        {
            if (!hasAnteriorPole || !(pole.sqrMagnitude > k_SqrEpsilon))
            {
                return pole;
            }

            Vector3 anterior = anteriorPole.normalized;
            float poleDeg = SignedAngleRad(anterior, pole, acNorm) * Mathf.Rad2Deg;
            float guardedDeg = ClampKneeSwivelDeg(poleDeg, KneeAnteriorSoftDeg, KneeAnteriorHardDeg);

            if (guardedDeg != poleDeg)
            {
                pole = AngleAxisRad(guardedDeg * Mathf.Deg2Rad, acNorm) * anterior;
                axisSource = 5;
            }

            return pole;
        }

        static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }

            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);
            float angle = Mathf.Acos(c);
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }

        static Quaternion AngleAxisRad(float radians, Vector3 axis)
        {
            float h = 0.5f * radians;
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

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

        static float ScaleSwivel(float radians, float weight)
        {
            if (weight >= 1f)
            {
                return radians;
            }

            if (!(weight > 0f))
            {
                return 0f;
            }

            return 2f * Mathf.Atan(weight * Mathf.Tan(0.5f * radians));
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

        static float MinFlexionReach(float upper, float lower)
        {
            float c = Mathf.Cos(MinKneeInteriorDeg * Mathf.Deg2Rad);
            float d2 = upper * upper + lower * lower - 2f * upper * lower * c;
            return d2 > 0f ? Mathf.Sqrt(d2) : 0f;
        }

        static float MaxExtensionReach(float upper, float lower)
        {
            float c = Mathf.Cos(MaxKneeInteriorDeg * Mathf.Deg2Rad);
            float d2 = upper * upper + lower * lower - 2f * upper * lower * c;
            return d2 > 0f ? Mathf.Sqrt(d2) : 0f;
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

    public struct BasisKneeForwardInput
    {
        public Vector3 HipPosition;
        public Vector3 FootPosition;
        public Vector3 FootForwardDir;
        public Vector3 BodyForwardDir;
        public Vector3 PlayerUp;
        public float UpperLength;
        public float Coupling;
        public float Strength;
    }

    public struct BasisKneeForwardResult
    {
        public Vector3 KneeHint;
        public Vector3 BendDir;
        public float HintWeight;
        public float Upright01;
        public float FollowDeg;
    }

    public static class BasisKneeForwardCore
    {
        public const float DefaultUprightCoupling = 1.0f;

        public const float FollowFadeStartDeg = 120f;

        public const float MaxFollowDeg = 60f;

        public const float LegUprightFadeStartDot = 0.25f;
        public const float LegUprightFadeFullDot = 0.55f;

        public const float RefCondSinFadeStart = 0.15f;
        public const float RefCondSinFadeFull = 0.35f;

        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-10f;

        public static void Solve(in BasisKneeForwardInput i, out BasisKneeForwardResult r)
        {
            r = default;

            Vector3 hipToFoot = i.FootPosition - i.HipPosition;
            float axisSqr = hipToFoot.sqrMagnitude;
            float radius = i.UpperLength > k_Epsilon ? i.UpperLength : 0.4f;
            Vector3 mid = (i.HipPosition + i.FootPosition) * 0.5f;

            if (axisSqr < k_SqrEpsilon)
            {
                r.BendDir = i.BodyForwardDir.sqrMagnitude > k_SqrEpsilon ? i.BodyForwardDir.normalized : Vector3.forward;
                r.KneeHint = mid + r.BendDir * radius;
                r.HintWeight = 0f;
                return;
            }
            Vector3 axis = hipToFoot / Mathf.Sqrt(axisSqr);
            Vector3 up = i.PlayerUp.sqrMagnitude > k_SqrEpsilon ? i.PlayerUp.normalized : Vector3.up;

            Vector3 bodyPerp = Vector3.ProjectOnPlane(i.BodyForwardDir, axis);
            if (bodyPerp.sqrMagnitude < k_SqrEpsilon)
            {
                Vector3 fallback = Vector3.ProjectOnPlane(up, axis);
                if (fallback.sqrMagnitude < k_SqrEpsilon)
                {
                    fallback = Vector3.Cross(axis, Vector3.right);
                }
                if (fallback.sqrMagnitude < k_SqrEpsilon)
                {
                    fallback = Vector3.Cross(axis, Vector3.up);
                }
                r.BendDir = fallback.normalized;
                r.KneeHint = mid + r.BendDir * radius;
                r.HintWeight = 0f;
                return;
            }
            Vector3 bodyPerpN = bodyPerp.normalized;
            float fwdMag = i.BodyForwardDir.magnitude;
            float refConditioning = Smoothstep(RefCondSinFadeStart, RefCondSinFadeFull,
                fwdMag > k_Epsilon ? Mathf.Sqrt(bodyPerp.sqrMagnitude) / fwdMag : 0f);

            float legVertical01 = Smoothstep(LegUprightFadeStartDot, LegUprightFadeFullDot, Mathf.Abs(Vector3.Dot(axis, up)));
            r.Upright01 = legVertical01;

            float strength = Saturate(i.Strength);

            Vector3 footPerp = Vector3.ProjectOnPlane(i.FootForwardDir, axis);
            Vector3 bendDir;
            float followDeg;
            if (footPerp.sqrMagnitude < k_SqrEpsilon || legVertical01 <= 0f)
            {
                bendDir = bodyPerpN;
                followDeg = 0f;
            }
            else
            {
                Vector3 footPerpN = footPerp.normalized;

                float signedDeg = Vector3.SignedAngle(bodyPerpN, footPerpN, axis);
                float rawAngle = signedDeg < 0f ? -signedDeg : signedDeg;

                followDeg = Mathf.Min(Saturate(i.Coupling) * legVertical01 * refConditioning * rawAngle, MaxFollowDeg);

                if (rawAngle > FollowFadeStartDeg)
                {
                    float u = (rawAngle - FollowFadeStartDeg) / (180f - FollowFadeStartDeg);
                    if (u > 1f) u = 1f;
                    followDeg *= 1f - u * u * (3f - 2f * u);
                }

                bendDir = Quaternion.AngleAxis(signedDeg < 0f ? -followDeg : followDeg, axis) * bodyPerpN;
                bendDir = Vector3.ProjectOnPlane(bendDir, axis);
                bendDir = bendDir.sqrMagnitude > k_SqrEpsilon ? bendDir.normalized : bodyPerpN;
            }

            r.BendDir = bendDir;
            r.FollowDeg = followDeg;
            r.KneeHint = mid + bendDir * radius;
            r.HintWeight = strength * refConditioning;
        }

        static float Saturate(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float Smoothstep(float a, float b, float v)
        {
            float t = Mathf.Approximately(a, b) ? (v >= b ? 1f : 0f) : Saturate((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public struct BasisLegDiagnostics
    {
        public float ReachRatio;
        public float KneeAngleDeg;
        public float AxisSource;
        public float HintApplied;
        public float ModelHintUsed;
        public float ModelConfidence;
        public float HintDistrust;
        public float RawSwivelDeg;
        public float SmoothSwivelDeg;
        public float Conditioning;
        public float HoldGate;
        public float AnteriorGuardApplied;
        public float Seeded;
        public float ShinRollDeg;

        public float HipFlexionDeg;
        public float HipAbductionDeg;
        public float FemurTwistDeg;

        public static string Header =>
            "leg,reach,kneeDeg,axisSrc,hintApplied,modelUsed,modelConf,distrust,rawSwivel,smoothSwivel,cond,holdGate,antGuard,seeded,shinRoll,hipFlex,hipAbd,femurTwist";

        public string ToRow(string leg) =>
            $"{leg},{ReachRatio:F4},{KneeAngleDeg:F2},{AxisSource:F0},{HintApplied:F0},{ModelHintUsed:F0}," +
            $"{ModelConfidence:F3},{HintDistrust:F3},{RawSwivelDeg:F2},{SmoothSwivelDeg:F2}," +
            $"{Conditioning:F4},{HoldGate:F3},{AnteriorGuardApplied:F0},{Seeded:F0},{ShinRollDeg:F2}," +
            $"{HipFlexionDeg:F2},{HipAbductionDeg:F2},{FemurTwistDeg:F2}";
    }
}
