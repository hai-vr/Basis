using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK
{
    public static class BasisElbowAnatomyCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public const float SoftMarginFracLimb = 0.05f;

        public const float HardMarginFracLimb = 0.15f;

        public static float GuardSwivelRad(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 playerUp, float totalLen)
        {
            Vector3 ac = hand - shoulder;
            float acSqr = ac.sqrMagnitude;

            if (!(acSqr > k_SqrEpsilon) || !(totalLen > k_Epsilon))
            {
                return 0f;
            }

            Vector3 up = playerUp;
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

            if (!(radius > k_Epsilon))
            {
                return 0f;
            }

            Vector3 upProj = up - acN * Vector3.Dot(up, acN);
            float upLen = upProj.magnitude;
            if (!(upLen > k_Epsilon))
            {
                return 0f;
            }

            Vector3 upN = upProj / upLen;
            Vector3 w = Vector3.Cross(acN, upN);

            float handUp = Vector3.Dot(ac, up);
            float ceiling = handUp > 0f ? handUp : 0f;

            float hSoft = ceiling + SoftMarginFracLimb * totalLen;
            float hHard = ceiling + HardMarginFracLimb * totalLen;

            float h = Vector3.Dot(ae, up);

            if (!(h > hSoft))
            {
                return 0f;
            }

            float M = hHard - hSoft;
            if (!(M > k_Epsilon))
            {
                return 0f;
            }

            float e = h - hSoft;
            float hGuarded = hSoft + M * e / (M + e);

            float along = Vector3.Dot(ae, acN) * Vector3.Dot(acN, up);
            float denom = radius * upLen;
            float cG = (hGuarded - along) / denom;
            cG = cG > 1f ? 1f : (cG > -1f ? cG : -1f);

            Vector3 poleDir = aeProj / radius;
            float s = Vector3.Dot(poleDir, w);

            float sG = (s < 0f ? -1f : 1f) * Mathf.Sqrt(Mathf.Max(1f - cG * cG, 0f));

            Vector3 poleGuarded = upN * cG + w * sG;
            return SignedAngleRad(poleDir, poleGuarded, acN);
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
    }

    [BurstCompile]
    public static class BasisElbowDragCore
    {
        public static float Alpha(float hz, float dt)
        {
            if (!(hz > 0f) || !(dt > 0f))
            {
                return 1f;
            }
            return 1f - math.exp(-2f * math.PI * hz * dt);
        }

        public static float3 Apply(float3 prevBend, quaternion bodyDelta, float3 curAxis, float3 targetBend, float alpha)
        {
            if (alpha >= 1f)
            {
                return targetBend;
            }
            if (alpha <= 0f)
            {
                alpha = 0f;
            }

            prevBend = math.rotate(bodyDelta, prevBend);

            float3 tp = prevBend - curAxis * math.dot(prevBend, curAxis);
            float tpLen = math.length(tp);
            if (tpLen < 1e-4f)
            {
                return targetBend;
            }
            tp /= tpLen;

            float3 cross = math.cross(curAxis, tp);
            float ang = math.atan2(math.dot(targetBend, cross), math.dot(targetBend, tp));

            ang *= alpha;

            float3 outb = tp * math.cos(ang) + cross * math.sin(ang);
            outb = outb - curAxis * math.dot(outb, curAxis);
            return math.normalizesafe(outb, targetBend);
        }
    }

    public static class BasisElbowFlareCore
    {
        const float k_CapEngageEnd = 0.3f;

        const float k_RollProjFadeStart = 0.10f;
        const float k_RollProjFadeFull = 0.25f;

        const float k_BasisFadeStart = 0.20f;
        const float k_BasisFadeFull = 0.50f;

        const float k_BendProjFadeStart = 0.05f;
        const float k_BendProjFadeFull = 0.20f;

        const float k_RollWrapFadeDeg = 40f;

        public static Vector3 ApplyFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            float engage01, float maxFlareDeg)
        {
            float r = Mathf.Clamp01(engage01);
            if (r <= 0f) return bend;
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole,
                                 out Vector3 outPole, out float basisConfidence))
                return bend;

            r *= basisConfidence;
            if (r <= 0f) return bend;

            float cap = Mathf.Max(0f, maxFlareDeg);

            Vector3 bendProj = Vector3.ProjectOnPlane(bend, axis);
            float bendMag = bendProj.magnitude;
            r *= Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((bendMag - k_BendProjFadeStart) / (k_BendProjFadeFull - k_BendProjFadeStart)));
            if (r <= 0f) return bend;

            float s0 = Mathf.Atan2(Vector3.Dot(bendProj, outPole), Vector3.Dot(bendProj, downPole)) * Mathf.Rad2Deg;

            float s = Mathf.Lerp(s0, cap, r);

            float capNow = Mathf.Lerp(180f, cap, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / k_CapEngageEnd)));
            s = Mathf.Clamp(s, -capNow, capNow);

            float rad = s * Mathf.Deg2Rad;
            Vector3 pole = downPole * Mathf.Cos(rad) + outPole * Mathf.Sin(rad);
            return pole.sqrMagnitude > 1e-12f ? pole.normalized : bend;
        }

        public static float RollEngagement01(Quaternion handRot, Vector3 shoulderToHand, Vector3 outwardDir,
            Vector3 playerUp, float inwardGain, float fullRollDeg)
        {
            if (Mathf.Abs(inwardGain) < 1e-6f) return 0f;
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole,
                                 out Vector3 outPole, out float basisConfidence))
                return 0f;
            if (basisConfidence <= 0f) return 0f;

            Vector3 hUp = Vector3.ProjectOnPlane(handRot * Vector3.up, axis);
            float proj = hUp.magnitude;
            if (proj < 1e-6f) return 0f;
            hUp /= proj;

            float aDeg = Mathf.Atan2(Vector3.Dot(hUp, outPole), Vector3.Dot(hUp, -downPole)) * Mathf.Rad2Deg;
            float engage = Mathf.Clamp01((aDeg / Mathf.Max(1f, fullRollDeg)) * inwardGain);

            float wrapFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((180f - Mathf.Abs(aDeg)) / k_RollWrapFadeDeg));

            float confidence = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((proj - k_RollProjFadeStart) / (k_RollProjFadeFull - k_RollProjFadeStart)));

            return engage * confidence * basisConfidence * wrapFade;
        }

        public static Vector3 ApplyChickenWingFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir,
            Vector3 playerUp, Quaternion handRot, float inwardGain, float fullRollDeg, float maxFlareDeg)
        {
            float r = RollEngagement01(handRot, shoulderToHand, outwardDir, playerUp, inwardGain, fullRollDeg);
            return ApplyFlare(bend, shoulderToHand, outwardDir, playerUp, r, maxFlareDeg);
        }

        static bool BuildSwingBasis(Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            out Vector3 axis, out Vector3 downPole, out Vector3 outPole, out float confidence)
        {
            axis = downPole = outPole = Vector3.zero;
            confidence = 0f;
            if (shoulderToHand.sqrMagnitude < 1e-10f) return false;
            axis = shoulderToHand.normalized;

            Vector3 dp = Vector3.ProjectOnPlane(-playerUp, axis);
            float dpMag = dp.magnitude;
            if (dpMag < 1e-6f) return false;
            downPole = dp / dpMag;

            Vector3 op = Vector3.ProjectOnPlane(outwardDir, axis);
            op -= downPole * Vector3.Dot(op, downPole);
            float opMag = op.magnitude;
            if (opMag < 1e-6f) return false;
            outPole = op / opMag;

            float weakest = Mathf.Min(dpMag, opMag);
            confidence = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((weakest - k_BasisFadeStart) / (k_BasisFadeFull - k_BasisFadeStart)));
            return true;
        }
    }

    [BurstCompile]
    public static class BasisElbowSwingCapCore
    {
        public const float MaxGain = 5f;

        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain)
        {
            float3 tp = prevBend - curAxis * math.dot(prevBend, curAxis);
            float tpLen = math.length(tp);
            if (tpLen < 1e-4f)
            {
                return rawBend;
            }
            tp /= tpLen;

            float3 cross = math.cross(curAxis, tp);
            float ang = math.atan2(math.dot(rawBend, cross), math.dot(rawBend, tp));

            float dHand = math.atan2(math.length(math.cross(prevAxis, curAxis)), math.dot(prevAxis, curAxis));
            float cap = maxGain * dHand;
            float capped = math.clamp(ang, -cap, cap);
            if (capped == ang)
            {
                return rawBend;
            }

            float3 outb = tp * math.cos(capped) + cross * math.sin(capped);
            outb = outb - curAxis * math.dot(outb, curAxis);
            return math.normalizesafe(outb, rawBend);
        }
    }
}
