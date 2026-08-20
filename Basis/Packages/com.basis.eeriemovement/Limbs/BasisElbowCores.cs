using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
namespace Basis.IK
{
    public static class BasisElbowAnatomyCore
    {
        const float epsilon = 1e-5f, sqrEpsilon = 1e-8f;
        public const float SoftMarginFracLimb = 0.05f, HardMarginFracLimb = 0.15f, SoftMarginMaxFracRadius = 0.5f;
        public const float TieBandFracRadius = 0.10f, ConditioningFadeLo = 0.04f, ConditioningFadeHi = 0.10f;
        public static float ConditioningFade(float radius, float totalLen)
        {
            if (!(totalLen > epsilon))
            {
                return 0f;
            }
            float t = Mathf.Clamp01((radius / totalLen - ConditioningFadeLo) / (ConditioningFadeHi - ConditioningFadeLo));
            return t * t * (3f - 2f * t);
        }
        public static float GuardSwivelRad(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 playerUp, float totalLen)
        {
            return GuardSwivelRad(shoulder, elbow, hand, playerUp, totalLen, Vector3.zero, 0, out _);
        }
        public static float GuardSwivelRad(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 playerUp, float totalLen, Vector3 lateralOut, int prevSide, out int sideUsed)
        {
            sideUsed = prevSide;
            Vector3 ac = hand - shoulder;
            float acSqr = ac.sqrMagnitude;

            if (!(acSqr > sqrEpsilon) || !(totalLen > epsilon))
            {
                return 0f;
            }

            Vector3 up = playerUp;
            float upSqr = up.sqrMagnitude;
            if (!(upSqr > sqrEpsilon))
            {
                return 0f;
            }
            up /= Mathf.Sqrt(upSqr);

            Vector3 acN = ac / Mathf.Sqrt(acSqr), ae = elbow - shoulder, aeProj = ae - acN * Vector3.Dot(ae, acN);
            float radius = aeProj.magnitude;

            if (!(radius > epsilon))
            {
                return 0f;
            }

            Vector3 upProj = up - acN * Vector3.Dot(up, acN);
            float upLen = upProj.magnitude;
            if (!(upLen > epsilon))
            {
                return 0f;
            }

            Vector3 upN = upProj / upLen, w = Vector3.Cross(acN, upN);
            float handUp = Vector3.Dot(ac, up), ceiling = handUp > 0f ? handUp : 0f;
            float softRise = SoftMarginFracLimb * totalLen, hardRise = HardMarginFracLimb * totalLen;
            float riseCap = SoftMarginMaxFracRadius * radius;
            if (softRise > riseCap)
            {
                hardRise *= riseCap / softRise;
                softRise = riseCap;
            }

            float hSoft = ceiling + softRise, hHard = ceiling + hardRise, h = Vector3.Dot(ae, up);

            if (!(h > hSoft))
            {
                return 0f;
            }

            float M = hHard - hSoft;
            if (!(M > epsilon))
            {
                return 0f;
            }

            float e = h - hSoft, hGuarded = hSoft + M * e / (M + e);
            float along = Vector3.Dot(ae, acN) * Vector3.Dot(acN, up), denom = radius * upLen;
            float cG = (hGuarded - along) / denom;
            cG = cG > 1f ? 1f : (cG > -1f ? cG : -1f);

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
                    if (latSqr > sqrEpsilon)
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
            return BasisIKMath.SignedAngleRad(poleDir, poleGuarded, acN) * ConditioningFade(radius, totalLen);
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
        const float capEngageEnd = 0.3f, rollProjFadeStart = 0.10f, rollProjFadeFull = 0.25f, basisFadeStart = 0.20f;
        const float basisFadeFull = 0.50f, bendProjFadeStart = 0.05f, bendProjFadeFull = 0.20f, rollWrapFadeDeg = 40f;
        public static Vector3 ApplyFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp, float engage01, float maxFlareDeg)
        {
            float r = Mathf.Clamp01(engage01);
            if (r <= 0f) return bend;
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole, out Vector3 outPole, out float basisConfidence))
                return bend;

            r *= basisConfidence;
            if (r <= 0f) return bend;

            float cap = Mathf.Max(0f, maxFlareDeg);
            Vector3 bendProj = Vector3.ProjectOnPlane(bend, axis);
            float bendMag = bendProj.magnitude;
            r *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((bendMag - bendProjFadeStart) / (bendProjFadeFull - bendProjFadeStart)));
            if (r <= 0f) return bend;

            float s0 = Mathf.Atan2(Vector3.Dot(bendProj, outPole), Vector3.Dot(bendProj, downPole)) * Mathf.Rad2Deg;
            float s = Mathf.Lerp(s0, cap, r);
            float capNow = Mathf.Lerp(180f, cap, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / capEngageEnd)));
            s = Mathf.Clamp(s, -capNow, capNow);

            float rad = s * Mathf.Deg2Rad;
            Vector3 pole = downPole * Mathf.Cos(rad) + outPole * Mathf.Sin(rad);
            return pole.sqrMagnitude > 1e-12f ? pole.normalized : bend;
        }
        public static float RollEngagement01(Quaternion handRot, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp, float inwardGain, float fullRollDeg)
        {
            if (Mathf.Abs(inwardGain) < 1e-6f) return 0f;
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole, out Vector3 outPole, out float basisConfidence))
                return 0f;
            if (basisConfidence <= 0f) return 0f;

            Vector3 hUp = Vector3.ProjectOnPlane(handRot * Vector3.up, axis);
            float proj = hUp.magnitude;
            if (proj < 1e-6f) return 0f;
            hUp /= proj;

            float aDeg = Mathf.Atan2(Vector3.Dot(hUp, outPole), Vector3.Dot(hUp, -downPole)) * Mathf.Rad2Deg;
            float engage = Mathf.Clamp01((aDeg / Mathf.Max(1f, fullRollDeg)) * inwardGain);
            float wrapFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((180f - Mathf.Abs(aDeg)) / rollWrapFadeDeg));
            float confidence = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((proj - rollProjFadeStart) / (rollProjFadeFull - rollProjFadeStart)));

            return engage * confidence * basisConfidence * wrapFade;
        }
        public static Vector3 ApplyChickenWingFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp, Quaternion handRot, float inwardGain, float fullRollDeg, float maxFlareDeg)
        {
            float r = RollEngagement01(handRot, shoulderToHand, outwardDir, playerUp, inwardGain, fullRollDeg);
            return ApplyFlare(bend, shoulderToHand, outwardDir, playerUp, r, maxFlareDeg);
        }
        static bool BuildSwingBasis(Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp, out Vector3 axis, out Vector3 downPole, out Vector3 outPole, out float confidence)
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
            confidence = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((weakest - basisFadeStart) / (basisFadeFull - basisFadeStart)));
            return true;
        }
    }
    [BurstCompile]
    public static class BasisElbowSwingCapCore
    {
        public const float MaxGain = 5f, ReachGain = 3f, ReachTrustLo = 0.06f, ReachTrustHi = 0.10f;
        public static float ReachTrust(float conditioning)
        {
            if (!(conditioning > ReachTrustLo))
            {
                return 0f;
            }
            float t = math.saturate((conditioning - ReachTrustLo) / (ReachTrustHi - ReachTrustLo));
            return t * t * (3f - 2f * t);
        }
        public const float MaxSlewDegPerSec = 720f, MaxSlewBudgetDt = 1f / 30f;
        public static float SlewCapRad(float dt) => dt > 0f ? math.radians(MaxSlewDegPerSec) * math.min(dt, MaxSlewBudgetDt) : 0f;
        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain) => Apply(prevBend, prevAxis, curAxis, rawBend, maxGain, 0f, 0f, 0f);
        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain, float dReach, float conditioning) => Apply(prevBend, prevAxis, curAxis, rawBend, maxGain, dReach, conditioning, 0f);
        public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain, float dReach, float conditioning, float slewCapRad)
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
            float dRadial = 0f, absReach = math.abs(dReach);
            if (absReach > 0f && math.isfinite(absReach))
            {
                dRadial = ReachGain * ReachTrust(conditioning) * absReach;
            }

            float cap = maxGain * (dHand + dRadial);
            if (slewCapRad > 0f && slewCapRad < cap)
            {
                cap = slewCapRad;
            }
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
