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

        struct Frame
        {
            public Vector3 ShoulderPos, AcDir, CurrentDir, ElbowCenter, BodyLat, BodyFwd;
            public float ElbowRadius, UpperArmR, ChestR, SpineR, HipsR, ThetaOut, SwingPreference;
        }

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

            Frame f = default;
            f.ShoulderPos = shoulderPos;
            f.AcDir = acAxis / Mathf.Sqrt(acSqr);
            Vector3 toElbow = elbowPos - shoulderPos;
            f.ElbowCenter = shoulderPos + f.AcDir * Vector3.Dot(toElbow, f.AcDir);
            f.ElbowRadius = (elbowPos - f.ElbowCenter).magnitude;
            r.ElbowCenter = f.ElbowCenter;
            r.ElbowRadius = f.ElbowRadius;
            if (f.ElbowRadius <= k_Epsilon)
            {
                return;
            }

            f.UpperArmR = Mathf.Max(0f, (i.HandRadius + i.HandSkin) * 1.2f);
            float chestRBase = i.ChestRadiusBase;
            float skin = i.CollisionSkin;

            f.ChestR = Mathf.Max(0f, chestRBase + skin);
            f.SpineR = Mathf.Max(0f, chestRBase * 0.8f + skin);
            f.HipsR = Mathf.Max(0f, chestRBase * 1.4f + skin);

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
            f.BodyLat = bodyLat;
            f.BodyFwd = bodyFwd;

            float contactMargin = f.ChestR * k_ContactMarginRatio;
            f.SwingPreference = f.ChestR * k_SwingPreferenceRatio;

            float natClear = MinTorsoClearance(i, f, elbowPos);
            float worstPen = natClear < 0f ? -natClear : 0f;
            r.WorstPenetration = worstPen;
            if (natClear >= contactMargin)
            {
                return;
            }

            Vector3 shoulderClosest = BasisEerieMovement.ClosestPointOnSegment(shoulderPos, i.ChestPos, i.NeckPos);
            Vector3 shoulderOut = shoulderPos - shoulderClosest;
            Vector3 shoulderPerp = shoulderOut - f.AcDir * Vector3.Dot(shoulderOut, f.AcDir);
            float shoulderPerpSqr = shoulderPerp.sqrMagnitude;
            if (shoulderPerpSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }
            Vector3 outDir = shoulderPerp / Mathf.Sqrt(shoulderPerpSqr);

            f.CurrentDir = (elbowPos - f.ElbowCenter) / f.ElbowRadius;
            r.SideDot = Vector3.Dot(f.CurrentDir, outDir);

            f.ThetaOut = Mathf.Atan2(Vector3.Dot(Vector3.Cross(f.CurrentDir, outDir), f.AcDir),
                Vector3.Dot(f.CurrentDir, outDir)) * Mathf.Rad2Deg;
            float firstClearT = -1f;
            float lastBlockedT = 0f;
            float bestClear = float.NegativeInfinity;
            int bestClearK = 0;
            for (int k = 0; k <= k_SwivelSteps; k++)
            {
                float t = (float)k / k_SwivelSteps;
                float c = SwivelClearance(i, f, t);
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
                float s = c - f.SwingPreference * t;
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
                        float c = SwivelClearance(i, f, mid);
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
                bestClearT = RefineClearanceMax(i, f, lo, hi);
            }

            float chosenT = cleared ? firstClearT : bestClearT;

            float flipCommit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(f.ThetaOut) - 100f) / 80f));
            chosenT += (1f - chosenT) * flipCommit;

            float totalLen = (elbowPos - shoulderPos).magnitude + (handPos - elbowPos).magnitude;
            float reach = totalLen > k_Epsilon ? Mathf.Sqrt(acSqr) / totalLen : 1f;
            float authority = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((reach - k_AuthorityFadeStart) / (k_AuthorityFadeEnd - k_AuthorityFadeStart)));
            chosenT *= authority;

            float approach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(natClear / contactMargin));
            chosenT *= approach;

            Vector3 dir = Quaternion.AngleAxis(f.ThetaOut * chosenT, f.AcDir) * f.CurrentDir;

            r.DesiredElbow = f.ElbowCenter + dir * f.ElbowRadius;
            r.SwingAngleDeg = Mathf.Abs(f.ThetaOut * chosenT);
            r.BlendUsed = chosenT;

            r.CollisionState = worstPen <= k_Epsilon ? 0 : (cleared ? 1 : 2);
            r.ResidualClearance = MinTorsoClearance(i, f, r.DesiredElbow);
            r.Engaged = true;
        }

        static float RefineClearanceMax(in BasisElbowProtectInput i, in Frame f, float lo, float hi)
        {
            const float invPhi = 0.6180339887f;
            float a = lo, b = hi;
            float t1 = b - invPhi * (b - a);
            float t2 = a + invPhi * (b - a);
            float f1 = SwivelClearance(i, f, t1) - f.SwingPreference * t1;
            float f2 = SwivelClearance(i, f, t2) - f.SwingPreference * t2;
            for (int n = 0; n < k_MaxRefineSteps; n++)
            {
                if (f1 < f2)
                {
                    a = t1; t1 = t2; f1 = f2;
                    t2 = a + invPhi * (b - a);
                    f2 = SwivelClearance(i, f, t2) - f.SwingPreference * t2;
                }
                else
                {
                    b = t2; t2 = t1; f2 = f1;
                    t1 = b - invPhi * (b - a);
                    f1 = SwivelClearance(i, f, t1) - f.SwingPreference * t1;
                }
            }
            return 0.5f * (a + b);
        }

        static float SwivelClearance(in BasisElbowProtectInput i, in Frame f, float t)
        {
            Vector3 d = Quaternion.AngleAxis(f.ThetaOut * t, f.AcDir) * f.CurrentDir;
            return MinTorsoClearance(i, f, f.ElbowCenter + d * f.ElbowRadius);
        }

        static float MinTorsoClearance(in BasisElbowProtectInput i, in Frame f, Vector3 elbowPos)
        {
            float worst = float.PositiveInfinity;
            if (i.HasHips && i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(f.ShoulderPos, elbowPos, f.UpperArmR, i.HipsPos, i.SpinePos, f.HipsR, f.SpineR, f.BodyLat, f.BodyFwd));
            }
            if (i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(f.ShoulderPos, elbowPos, f.UpperArmR, i.SpinePos, i.ChestPos, f.SpineR, f.ChestR, f.BodyLat, f.BodyFwd));
            }
            worst = Mathf.Min(worst, SegmentClearance(f.ShoulderPos, elbowPos, f.UpperArmR, i.ChestPos, i.NeckPos, f.ChestR, f.ChestR, f.BodyLat, f.BodyFwd));
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
