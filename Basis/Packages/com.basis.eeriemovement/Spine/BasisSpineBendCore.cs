using UnityEngine;

namespace Basis.IK
{
    public struct BasisSpineBendInput
    {
        public Quaternion HipsRot;
        public Vector3 HipsPos;
        public Vector3 ChestPos;
        public Vector3 SmoothedHead;
        public Quaternion HipsBind;
        public Quaternion HeadTargetRot;

        public float SpineMaxForwardDeg;
        public float SpineMaxBackwardDeg;
        public float SpineMaxLateralDeg;

        public float SpineBendPitch, SpineBendYaw, SpineBendRoll;
        public float UpperBendPitch, UpperBendYaw, UpperBendRoll;

        public bool AnatDifferentialStiffness;
        public bool AnatPelvicTwistRouting;
        public float BendTwistCoupling;

        public float SquishBoost;
        public float RestLen;

        public bool HasSpine;
        public bool HasUpper;
    }

    public struct BasisSpineBendResult
    {
        public bool EarlyOut;
        public bool WriteSpine; public Vector3 SpineEuler;
        public bool WriteUpper; public Vector3 UpperEuler;

        public float BendPitchDeg;
        public float BendRollDeg;
        public float TwistY;
        public float SquishMult;
        public float BendGate;
        public float SpineYawEff;
        public float UpperYawEff;
    }

    public static class BasisSpineBendCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;
        const float k_BendDeadbandDeg = 3f;
        const float k_BendDeadbandWidthDeg = 7f;

        const float k_TwistFadeFullHoriz = 0.342f;
        const float k_TwistFadeZeroHoriz = 0.174f;

        public static void Solve(in BasisSpineBendInput i, out BasisSpineBendResult r)
        {
            r = default;

            Quaternion invHips = Quaternion.Inverse(i.HipsRot);

            Quaternion hipsSpace = i.HipsBind * invHips;

            Vector3 localChestDir = hipsSpace * (i.ChestPos - i.HipsPos);
            Vector3 localTargetDir = hipsSpace * (i.SmoothedHead - i.HipsPos);
            if (localChestDir.sqrMagnitude < k_SqrEpsilon || localTargetDir.sqrMagnitude < k_SqrEpsilon)
            {
                r.EarlyOut = true;
                return;
            }

            Vector3 chestDirN = localChestDir.normalized;
            Vector3 targetDirN = localTargetDir.normalized;

            Vector3 bendCross = Vector3.Cross(chestDirN, targetDirN);
            float bendDot = Mathf.Clamp(Vector3.Dot(chestDirN, targetDirN), -1f, 1f);
            float bendAngleDeg = Mathf.Atan2(bendCross.magnitude, bendDot) * Mathf.Rad2Deg;
            Vector3 bendAxisScaled = bendCross.sqrMagnitude > k_SqrEpsilon
                ? bendCross.normalized * bendAngleDeg
                : Vector3.zero;
            float bendPitchDeg = bendAxisScaled.x;
            float bendRollDeg = bendAxisScaled.z;
            Vector3 bendEuler = new Vector3(bendPitchDeg, 0f, bendRollDeg);

            Quaternion headRotLocal = hipsSpace * i.HeadTargetRot;
            Vector3 headFwdLocal = headRotLocal * Vector3.forward;
            float horizMagSq = headFwdLocal.x * headFwdLocal.x + headFwdLocal.z * headFwdLocal.z;
            float twistY = (horizMagSq < k_SqrEpsilon) ? 0f : Mathf.Atan2(headFwdLocal.x, headFwdLocal.z) * Mathf.Rad2Deg;

            float twistFadeT = Mathf.Clamp01((Mathf.Sqrt(horizMagSq) - k_TwistFadeZeroHoriz) / (k_TwistFadeFullHoriz - k_TwistFadeZeroHoriz));
            twistY *= Mathf.SmoothStep(0f, 1f, twistFadeT);

            float maxFwd = Mathf.Max(0f, i.SpineMaxForwardDeg);
            float maxBack = Mathf.Max(0f, i.SpineMaxBackwardDeg);
            float maxLat = Mathf.Max(0f, i.SpineMaxLateralDeg);

            float squishMult = ComputeSquishMultiplier(i.SmoothedHead - i.HipsPos, i.RestLen, i.SquishBoost);

            float bendMag = Mathf.Sqrt(bendEuler.x * bendEuler.x + bendEuler.z * bendEuler.z);
            float bendT = Mathf.Clamp01((bendMag - k_BendDeadbandDeg) / k_BendDeadbandWidthDeg);
            float bendGate = Mathf.SmoothStep(0f, 1f, bendT);

            float spinePitchEff = Mathf.Clamp01(i.SpineBendPitch);
            float spineYawEff = Mathf.Clamp01(i.SpineBendYaw);
            float spineRollEff = Mathf.Clamp01(i.SpineBendRoll);
            float upperPitchEff = Mathf.Clamp01(i.UpperBendPitch);
            float upperYawEff = Mathf.Clamp01(i.UpperBendYaw);
            float upperRollEff = Mathf.Clamp01(i.UpperBendRoll);
            if (i.AnatDifferentialStiffness)
            {
                spineYawEff *= 0.4f;
                upperYawEff = Mathf.Clamp01(upperYawEff * 1.5f);
            }
            if (i.AnatPelvicTwistRouting)
            {
                float total = spineYawEff + upperYawEff;
                spineYawEff = total * 0.25f;
                upperYawEff = total * 0.75f;
            }

            if (i.HasSpine)
            {
                Vector3 e = new Vector3(
                    bendEuler.x * spinePitchEff * squishMult * bendGate,
                    twistY * spineYawEff * squishMult,
                    bendEuler.z * spineRollEff * squishMult * bendGate
                );
                e.y += i.BendTwistCoupling * e.z;
                r.SpineEuler = ClampAsymmetric(e, maxFwd, maxBack, maxLat);
                r.WriteSpine = true;
            }
            if (i.HasUpper)
            {
                Vector3 e = new Vector3(
                    bendEuler.x * upperPitchEff * squishMult * bendGate,
                    twistY * upperYawEff * squishMult,
                    bendEuler.z * upperRollEff * squishMult * bendGate
                );
                e.y += i.BendTwistCoupling * e.z;
                r.UpperEuler = ClampAsymmetric(e, maxFwd, maxBack, maxLat);
                r.WriteUpper = true;
            }

            r.BendPitchDeg = bendPitchDeg;
            r.BendRollDeg = bendRollDeg;
            r.TwistY = twistY;
            r.SquishMult = squishMult;
            r.BendGate = bendGate;
            r.SpineYawEff = spineYawEff;
            r.UpperYawEff = upperYawEff;
        }

        public static Quaternion Compose(Vector3 e)
        {
            Quaternion yaw = BasisSpineAnatomyCore.AxisAngle(e.y, Vector3.up);
            float swingDeg = Mathf.Sqrt(e.x * e.x + e.z * e.z);
            if (swingDeg <= k_Epsilon)
            {
                return yaw;
            }
            Quaternion swing = BasisSpineAnatomyCore.AxisAngle(swingDeg, new Vector3(e.x / swingDeg, 0f, e.z / swingDeg));
            return yaw * swing;
        }

        public static float ComputeSquishMultiplier(Vector3 hipsToHead, float restLen, float squishBoost)
        {
            float boost = Mathf.Clamp(squishBoost, 0f, 2f);
            if (boost <= 0f)
            {
                return 1f;
            }
            if (restLen < k_Epsilon)
            {
                return 1f;
            }
            float currentMag = hipsToHead.magnitude;
            float squish = currentMag / restLen;
            float t = Mathf.Clamp01((squish - 0.7f) / 0.6f);
            return Mathf.Lerp(1f + boost, Mathf.Max(0f, 1f - boost), t);
        }

        static Vector3 ClampAsymmetric(Vector3 e, float maxFwd, float maxBack, float maxLat)
        {
            if (e.x > 0f) e.x = Mathf.Min(e.x, maxFwd);
            else e.x = Mathf.Max(e.x, -maxBack);
            e.y = Mathf.Clamp(e.y, -maxLat, maxLat);
            e.z = Mathf.Clamp(e.z, -maxLat, maxLat);
            return e;
        }
    }

    public struct BasisCrouchOffsetInput
    {
        public Vector3 HeadTargetPos;
        public Vector3 HipsPos;
        public Quaternion HipsRot;

        public Quaternion Bind;
        public Vector3 PlayerUp;

        public float Factor;

        public float RestDist;

        public float CrouchDepth;

        public float StandingHeadHeight;

        public float Fade;
    }

    public struct BasisCrouchOffsetResult
    {
        public Vector3 HipsPos;
        public bool Applied;
        public float SetbackMeters;
        public float LeanDeg;
    }

    public static class BasisCrouchOffsetCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;

        public const float k_DepthDeadzone = 0.03f;
        public const float k_SetbackSlope = 2.57f;
        public const float k_SetbackSat = 8.1f;

        public const float k_MaxLeanSin = 0.88f;

        public const float k_VerticalEngageFrac = 0.04f;

        public static float EvaluateSetback(float crouchDepth, float standingHeadHeight, float factor, float fade, float restDist)
        {
            if (factor <= 0f || fade <= 0f || crouchDepth <= 0f || standingHeadHeight <= k_Epsilon || restDist <= k_Epsilon)
            {
                return 0f;
            }
            float x = crouchDepth / standingHeadHeight - k_DepthDeadzone;
            if (x <= 0f)
            {
                return 0f;
            }
            float setback = k_SetbackSlope * x / (1f + k_SetbackSat * x) * standingHeadHeight;
            setback *= factor * Mathf.Clamp01(fade);
            return BasisTrunkCounterbalanceCore.Saturate(setback, k_MaxLeanSin * restDist);
        }

        public static void Solve(in BasisCrouchOffsetInput i, out BasisCrouchOffsetResult r)
        {
            r = default;
            r.HipsPos = i.HipsPos;

            float setback = EvaluateSetback(i.CrouchDepth, i.StandingHeadHeight, i.Factor, i.Fade, i.RestDist);
            if (setback <= k_Epsilon)
            {
                return;
            }

            Vector3 up = i.PlayerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : i.PlayerUp.normalized;

            Quaternion hipsAnat = i.HipsRot;
            float bindSq = i.Bind.x * i.Bind.x + i.Bind.y * i.Bind.y + i.Bind.z * i.Bind.z + i.Bind.w * i.Bind.w;
            if (bindSq > 0.5f)
            {
                Quaternion invBind = new Quaternion(-i.Bind.x, -i.Bind.y, -i.Bind.z, i.Bind.w);
                hipsAnat = i.HipsRot * invBind;
            }

            Vector3 forward = hipsAnat * Vector3.forward;
            forward -= up * Vector3.Dot(forward, up);
            if (forward.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            Vector3 horizontal = i.HipsPos - i.HeadTargetPos;
            horizontal -= up * Vector3.Dot(horizontal, up);
            horizontal -= forward.normalized * setback;
            float horizMag = horizontal.magnitude;
            float horizCap = k_MaxLeanSin * i.RestDist;
            if (horizMag > horizCap)
            {
                horizontal *= horizCap / horizMag;
                horizMag = horizCap;
            }

            float sphereDrop = Mathf.Sqrt(Mathf.Max(i.RestDist * i.RestDist - horizMag * horizMag, 0f));
            float currentDrop = Vector3.Dot(i.HeadTargetPos - i.HipsPos, up);
            float w = Mathf.Clamp01(setback / (k_VerticalEngageFrac * i.RestDist));
            float drop = Mathf.Lerp(currentDrop, sphereDrop, w);

            r.Applied = true;
            r.SetbackMeters = setback;
            r.LeanDeg = Mathf.Atan2(horizMag, drop) * Mathf.Rad2Deg;
            r.HipsPos = i.HeadTargetPos + horizontal - up * drop;
        }
    }
}
