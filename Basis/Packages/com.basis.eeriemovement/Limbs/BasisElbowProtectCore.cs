using UnityEngine;
namespace Basis.IK
{
    public struct BasisElbowProtectInput
    {
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;

        public Vector3 HipsPos;
        public Vector3 SpinePos;
        public Vector3 ChestPos;
        public Vector3 NeckPos;
        public bool HasHips;
        public bool HasSpine;

        public float ChestRadiusBase;
        public float CollisionSkin;
        public float HandRadius;
        public float HandSkin;

        public Vector3 PlayerUp;

        public Vector3 BodyRight;
    }

    public struct BasisElbowProtectResult
    {
        public bool Engaged;
        public int CollisionState;
        public Vector3 DesiredElbow;
        public float WorstPenetration;
        public float SideDot;
        public float BlendUsed;
        public float SwingAngleDeg;
        public float ElbowRadius;
        public Vector3 ElbowCenter;
        public float ResidualClearance;
    }

    public static class BasisElbowProtectCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_ClearMargin = 0.003f;
        const int k_SwivelSteps = 48;

        const int k_ClearRefineSteps = 12;

        const int k_MaxRefineSteps = 12;

        const float k_SwingPreferenceRatio = 0.0125f;

        const float k_ContactMarginRatio = 0.25f;

        const float k_ChestDepthRatio = 0.68f;

        const float k_AuthorityFadeStart = 0.95f;
        const float k_AuthorityFadeEnd = 0.995f;

        public static void Solve(in BasisElbowProtectInput i, out BasisElbowProtectResult r)
        {
            r = default;
            r.DesiredElbow = i.Elbow;
            r.SideDot = float.NaN;

            Vector3 shoulderPos = i.Shoulder;
            Vector3 elbowPos = i.Elbow;
            Vector3 handPos = i.Hand;

            Vector3 acAxis = handPos - shoulderPos;
            float acSqr = Vector3.Dot(acAxis, acAxis);
            if (acSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }

            Vector3 acDir = acAxis / Mathf.Sqrt(acSqr);
            Vector3 toElbow = elbowPos - shoulderPos;
            Vector3 elbowCenter = shoulderPos + acDir * Vector3.Dot(toElbow, acDir);
            float elbowRadius = (elbowPos - elbowCenter).magnitude;
            r.ElbowCenter = elbowCenter;
            r.ElbowRadius = elbowRadius;
            if (elbowRadius <= k_Epsilon)
            {
                return;
            }

            float upperArmR = Mathf.Max(0f, (i.HandRadius + i.HandSkin) * 1.2f);
            float chestRBase = i.ChestRadiusBase;
            float skin = i.CollisionSkin;

            float chestR = Mathf.Max(0f, chestRBase + skin);
            float spineR = Mathf.Max(0f, chestRBase * 0.8f + skin);
            float hipsR = Mathf.Max(0f, chestRBase * 1.4f + skin);

            Vector3 upN = i.PlayerUp.sqrMagnitude > k_Epsilon * k_Epsilon ? i.PlayerUp.normalized : Vector3.up;
            Vector3 bodyLat = i.BodyRight - upN * Vector3.Dot(i.BodyRight, upN);
            if (bodyLat.sqrMagnitude <= k_Epsilon * k_Epsilon)
            {
                Vector3 chestClosest = BasisEerieMovement.ClosestPointOnSegment(shoulderPos, i.ChestPos, i.NeckPos);
                Vector3 off = shoulderPos - chestClosest;
                bodyLat = off - upN * Vector3.Dot(off, upN);
            }
            Vector3 bodyFwd = Vector3.zero;
            float bodyLatLen = bodyLat.magnitude;
            if (bodyLatLen > k_Epsilon)
            {
                bodyLat /= bodyLatLen;
                Vector3 fwd = Vector3.Cross(bodyLat, upN);
                float fLen = fwd.magnitude;
                bodyFwd = fLen > k_Epsilon ? fwd / fLen : Vector3.zero;
            }
            else
            {
                bodyLat = Vector3.zero;
            }

            float contactMargin = chestR * k_ContactMarginRatio;
            float swingPreference = chestR * k_SwingPreferenceRatio;

            float natClear = MinTorsoClearance(i, shoulderPos, elbowPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
            float worstPen = natClear < 0f ? -natClear : 0f;
            r.WorstPenetration = worstPen;
            if (natClear >= contactMargin)
            {
                return;
            }

            Vector3 shoulderClosest = BasisEerieMovement.ClosestPointOnSegment(shoulderPos, i.ChestPos, i.NeckPos);
            Vector3 shoulderOut = shoulderPos - shoulderClosest;
            Vector3 shoulderPerp = shoulderOut - acDir * Vector3.Dot(shoulderOut, acDir);
            float shoulderPerpSqr = shoulderPerp.sqrMagnitude;
            if (shoulderPerpSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }
            Vector3 outDir = shoulderPerp / Mathf.Sqrt(shoulderPerpSqr);

            Vector3 currentDir = (elbowPos - elbowCenter) / elbowRadius;
            r.SideDot = Vector3.Dot(currentDir, outDir);

            float thetaOut = Mathf.Atan2(Vector3.Dot(Vector3.Cross(currentDir, outDir), acDir),
                Vector3.Dot(currentDir, outDir)) * Mathf.Rad2Deg;
            float firstClearT = -1f;
            float lastBlockedT = 0f;
            float bestClear = float.NegativeInfinity;
            int bestClearK = 0;
            for (int k = 0; k <= k_SwivelSteps; k++)
            {
                float t = (float)k / k_SwivelSteps;
                float c = SwivelClearance(i, t, thetaOut, acDir, currentDir, elbowCenter, elbowRadius,
                    shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
                if (firstClearT < 0f)
                {
                    if (c >= k_ClearMargin)
                    {
                        firstClearT = t;
                    }
                    else
                    {
                        lastBlockedT = t;
                    }
                }
                float s = c - swingPreference * t;
                if (s > bestClear)
                {
                    bestClear = s;
                    bestClearK = k;
                }
            }
            float bestClearT = (float)bestClearK / k_SwivelSteps;

            bool cleared = firstClearT >= 0f;
            if (cleared)
            {
                if (firstClearT > 0f)
                {
                    float lo = lastBlockedT, hi = firstClearT;
                    for (int b = 0; b < k_ClearRefineSteps; b++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float c = SwivelClearance(i, mid, thetaOut, acDir, currentDir, elbowCenter, elbowRadius,
                            shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
                        if (c >= k_ClearMargin)
                        {
                            hi = mid;
                        }
                        else
                        {
                            lo = mid;
                        }
                    }
                    firstClearT = hi;
                }
            }
            else
            {
                float lo = (float)Mathf.Max(0, bestClearK - 1) / k_SwivelSteps;
                float hi = (float)Mathf.Min(k_SwivelSteps, bestClearK + 1) / k_SwivelSteps;
                bestClearT = RefineClearanceMax(i, lo, hi, thetaOut, acDir, currentDir, elbowCenter,
                    elbowRadius, shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd,
                    swingPreference);
            }

            float chosenT = cleared ? firstClearT : bestClearT;

            float flipCommit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(thetaOut) - 100f) / 80f));
            chosenT += (1f - chosenT) * flipCommit;

            float totalLen = (elbowPos - shoulderPos).magnitude + (handPos - elbowPos).magnitude;
            float reach = totalLen > k_Epsilon ? Mathf.Sqrt(acSqr) / totalLen : 1f;
            float authority = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((reach - k_AuthorityFadeStart) / (k_AuthorityFadeEnd - k_AuthorityFadeStart)));
            chosenT *= authority;

            float approach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(natClear / contactMargin));
            chosenT *= approach;

            Vector3 dir = Quaternion.AngleAxis(thetaOut * chosenT, acDir) * currentDir;

            r.DesiredElbow = elbowCenter + dir * elbowRadius;
            r.SwingAngleDeg = Mathf.Abs(thetaOut * chosenT);
            r.BlendUsed = chosenT;

            r.CollisionState = worstPen <= k_Epsilon ? 0 : (cleared ? 1 : 2);
            r.ResidualClearance = MinTorsoClearance(i, shoulderPos, r.DesiredElbow, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
            r.Engaged = true;
        }

        static float RefineClearanceMax(in BasisElbowProtectInput i, float lo, float hi, float thetaOut,
            Vector3 acDir, Vector3 currentDir, Vector3 elbowCenter, float elbowRadius, Vector3 shoulderPos,
            float upperArmR, float chestR, float spineR, float hipsR, Vector3 bodyLat, Vector3 bodyFwd,
            float swingPreference)
        {
            const float invPhi = 0.6180339887f;
            float a = lo, b = hi;
            float t1 = b - invPhi * (b - a);
            float t2 = a + invPhi * (b - a);
            float f1 = SwivelScore(i, t1, thetaOut, acDir, currentDir, elbowCenter, elbowRadius, shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd, swingPreference);
            float f2 = SwivelScore(i, t2, thetaOut, acDir, currentDir, elbowCenter, elbowRadius, shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd, swingPreference);
            for (int n = 0; n < k_MaxRefineSteps; n++)
            {
                if (f1 < f2)
                {
                    a = t1; t1 = t2; f1 = f2;
                    t2 = a + invPhi * (b - a);
                    f2 = SwivelScore(i, t2, thetaOut, acDir, currentDir, elbowCenter, elbowRadius, shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd, swingPreference);
                }
                else
                {
                    b = t2; t2 = t1; f2 = f1;
                    t1 = b - invPhi * (b - a);
                    f1 = SwivelScore(i, t1, thetaOut, acDir, currentDir, elbowCenter, elbowRadius, shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd, swingPreference);
                }
            }
            return 0.5f * (a + b);
        }

        static float SwivelScore(in BasisElbowProtectInput i, float t, float thetaOut, Vector3 acDir,
            Vector3 currentDir, Vector3 elbowCenter, float elbowRadius, Vector3 shoulderPos,
            float upperArmR, float chestR, float spineR, float hipsR, Vector3 bodyLat, Vector3 bodyFwd,
            float swingPreference)
        {
            return SwivelClearance(i, t, thetaOut, acDir, currentDir, elbowCenter, elbowRadius,
                shoulderPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd) - swingPreference * t;
        }

        static float SwivelClearance(in BasisElbowProtectInput i, float t, float thetaOut, Vector3 acDir,
            Vector3 currentDir, Vector3 elbowCenter, float elbowRadius, Vector3 shoulderPos,
            float upperArmR, float chestR, float spineR, float hipsR, Vector3 bodyLat, Vector3 bodyFwd)
        {
            Vector3 d = Quaternion.AngleAxis(thetaOut * t, acDir) * currentDir;
            return MinTorsoClearance(i, shoulderPos, elbowCenter + d * elbowRadius, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
        }

        static float MinTorsoClearance(in BasisElbowProtectInput i, Vector3 shoulderPos, Vector3 elbowPos,
            float upperArmR, float chestLatR, float spineLatR, float hipsLatR, Vector3 bodyLat, Vector3 bodyFwd)
        {
            float worst = float.PositiveInfinity;
            if (i.HasHips && i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.HipsPos, i.SpinePos, hipsLatR, spineLatR, bodyLat, bodyFwd));
            }
            if (i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.SpinePos, i.ChestPos, spineLatR, chestLatR, bodyLat, bodyFwd));
            }
            worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.ChestPos, i.NeckPos, chestLatR, chestLatR, bodyLat, bodyFwd));
            return worst;
        }

        static float SegmentClearance(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2,
            float latR0, float latR1, Vector3 bodyLat, Vector3 bodyFwd)
        {
            BasisEerieMovement.SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out float segT, out Vector3 c1, out Vector3 c2);
            Vector3 sep = c1 - c2;
            float sepLen = sep.magnitude;

            float latR = latR0 + (latR1 - latR0) * segT;
            float rEff = latR;
            float apR = latR * k_ChestDepthRatio;
            Vector3 axis = q2 - p2;
            float axisSqr = axis.sqrMagnitude;
            if (apR > k_Epsilon && sepLen > k_Epsilon && axisSqr > k_Epsilon * k_Epsilon
                && bodyLat.sqrMagnitude > k_Epsilon * k_Epsilon && bodyFwd.sqrMagnitude > k_Epsilon * k_Epsilon)
            {
                Vector3 axisN = axis / Mathf.Sqrt(axisSqr);
                Vector3 sepPerp = sep - axisN * Vector3.Dot(sep, axisN);
                float sepPerpLen = sepPerp.magnitude;
                if (sepPerpLen > k_Epsilon)
                {
                    Vector3 sepDir = sepPerp / sepPerpLen;
                    float cu = Vector3.Dot(sepDir, bodyLat);
                    float cw = Vector3.Dot(sepDir, bodyFwd);
                    float denom = (cu * cu) / (latR * latR) + (cw * cw) / (apR * apR);
                    if (denom > k_Epsilon)
                    {
                        rEff = 1f / Mathf.Sqrt(denom);
                    }
                }
            }
            return sepLen - (r1 + rEff);
        }
    }
}
