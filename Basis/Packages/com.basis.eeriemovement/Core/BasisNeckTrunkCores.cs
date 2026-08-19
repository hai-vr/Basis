using UnityEngine;

namespace Basis.IK
{
    public struct BasisCervicalInput
    {
        public float BaseDeg;
        public float NeckShare;
        public float MaxHeadPitchDeg;
        public float ExtremeStartDeg;
        public float ExtremeFullDeg;
        public float ExtremeRollForwardMaxDeg;
        public float ExtremeRollBackwardMaxDeg;
        public float ExtremeHipsHorizontalMax;
        public float ExtremeChestHorizontalMax;

        public float ExtremeHipsHorizontalLookUp;
        public float ExtremeChestHorizontalLookUp;
        public float ExtremeHipsDownMax;
        public float ExtremeChestDownMax;
        public float ExtremeHipsDownLookUp;
        public float ExtremeChestDownLookUp;
        public float PitchGainDeg;

        public Vector3 ReferenceUp;
        public Quaternion HeadTargetRot;
        public bool HasUpperChest;
    }

    public struct BasisCervicalResult
    {
        public bool EarlyOut;
        public Quaternion HeadRotClamped;

        public float BhDeg;
        public float NeckDeg;
        public bool HasExtreme;
        public float HipsForwardAmount;
        public float HipsDownAmount;
        public float ChestForwardAmount;
        public float ChestDownAmount;

        public float HeadPitchInputDeg;
        public float HeadPitchClampedDeg;
        public float LordosisDeg;
        public float UpperChestLordosisDeg;
        public float ExtremeFrac;
        public float ExtremeRollDeg;
        public float PitchAbsDeg;
        public float SignedPitch;
        public float LookUpFrac;
        public float LookDownFrac;
    }

    public static class BasisCervicalSolveCore
    {
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisCervicalInput i, out BasisCervicalResult r)
        {
            r = default;

            Vector3 up = i.ReferenceUp.sqrMagnitude > k_SqrEpsilon ? i.ReferenceUp.normalized : Vector3.up;

            Quaternion headRot = i.HeadTargetRot;
            Vector3 hf = headRot * Vector3.forward;
            float upComp = Vector3.Dot(hf, up);
            Vector3 horiz = hf - up * upComp;
            float horizMag = horiz.magnitude;
            float pitchDeg = (horizMag > 1e-6f) ? Mathf.Atan2(-upComp, horizMag) * Mathf.Rad2Deg : (upComp < 0f ? 90f : -90f);
            float clampedDeg = Mathf.Clamp(pitchDeg, -i.MaxHeadPitchDeg, i.MaxHeadPitchDeg);
            if (clampedDeg != pitchDeg)
            {
                Vector3 yawForward;
                if (horizMag > 1e-6f)
                {
                    yawForward = horiz / horizMag;
                }
                else
                {
                    Vector3 alt = headRot * Vector3.up;
                    Vector3 altH = alt - up * Vector3.Dot(alt, up);
                    yawForward = altH.sqrMagnitude > k_SqrEpsilon ? altH.normalized : Vector3.Cross(up, Vector3.right).normalized;
                }
                Vector3 yawRight = Vector3.Cross(up, yawForward);
                Quaternion correction = Quaternion.AngleAxis(-(pitchDeg - clampedDeg), yawRight);
                headRot = correction * headRot;
            }

            Vector3 headForward = headRot * Vector3.forward;
            float pitchSigned = Vector3.Dot(headForward, up);
            float lookUpFrac = Mathf.Clamp01(pitchSigned);
            float lookDownFrac = Mathf.Clamp01(-pitchSigned);

            float pitchAbsDeg = Mathf.Asin(Mathf.Min(Mathf.Abs(pitchSigned), 1f)) * Mathf.Rad2Deg;
            float extremeFrac = Mathf.Clamp01((pitchAbsDeg - i.ExtremeStartDeg) / Mathf.Max(1e-3f, i.ExtremeFullDeg - i.ExtremeStartDeg));

            float signedPitch = lookDownFrac - lookUpFrac;
            float signedBalance = -signedPitch;

            float smoothAbsPitch = Mathf.Sqrt(signedPitch * signedPitch + 0.0225f) - 0.15f;
            float lordosisDeg = i.BaseDeg * (1f - smoothAbsPitch) + i.PitchGainDeg * signedPitch;

            bool hasUpperChest = i.HasUpperChest;
            float neckDeg = hasUpperChest ? lordosisDeg * i.NeckShare : lordosisDeg;
            float upperChestLordosisDeg = hasUpperChest ? lordosisDeg * (1f - i.NeckShare) : 0f;
            float extremeRollMag = signedPitch >= 0f ? i.ExtremeRollForwardMaxDeg : i.ExtremeRollBackwardMaxDeg;
            float extremeRollDeg = extremeFrac * signedPitch * extremeRollMag;

            r.HeadRotClamped = headRot;
            r.HeadPitchInputDeg = pitchDeg;
            r.HeadPitchClampedDeg = clampedDeg;
            r.LordosisDeg = lordosisDeg;
            r.UpperChestLordosisDeg = upperChestLordosisDeg;
            r.NeckDeg = neckDeg;
            r.ExtremeFrac = extremeFrac;
            r.ExtremeRollDeg = extremeRollDeg;
            r.PitchAbsDeg = pitchAbsDeg;
            r.SignedPitch = signedPitch;
            r.LookUpFrac = lookUpFrac;
            r.LookDownFrac = lookDownFrac;

            if (Mathf.Abs(lordosisDeg) < 0.01f && extremeFrac <= 0f)
            {
                r.EarlyOut = true;
                return;
            }

            r.BhDeg = upperChestLordosisDeg + extremeRollDeg;

            if (extremeFrac > 0f)
            {
                r.HasExtreme = true;
                float horizCoeff = extremeFrac * signedBalance;
                float hipsDown = extremeFrac * (lookDownFrac * i.ExtremeHipsDownMax + lookUpFrac * i.ExtremeHipsDownLookUp);
                float chestDown = extremeFrac * (lookDownFrac * i.ExtremeChestDownMax + lookUpFrac * i.ExtremeChestDownLookUp);

                float hipsHoriz = signedPitch >= 0f ? i.ExtremeHipsHorizontalMax : i.ExtremeHipsHorizontalLookUp;
                float chestHoriz = signedPitch >= 0f ? i.ExtremeChestHorizontalMax : i.ExtremeChestHorizontalLookUp;
                r.HipsForwardAmount = horizCoeff * hipsHoriz;
                r.HipsDownAmount = hipsDown;
                r.ChestForwardAmount = horizCoeff * chestHoriz;
                r.ChestDownAmount = chestDown;
            }
        }
    }

    public static class BasisNeckCueCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;

        public const float DefaultExtensionDamp = 0.65f;

        public const float DefaultFlexionDamp = 0.65f;

        public static Vector3 Solve(Vector3 headTargetPos, Quaternion headWorldRot, Vector3 tposeHeadToNeckLocal,
            Vector3 playerUp, float extensionDamp)
        {
            return Solve(headTargetPos, headWorldRot, tposeHeadToNeckLocal, playerUp, extensionDamp, 0f);
        }

        /// <summary>
        /// Re-attaches the T-pose head-to-neck lever to the head target. Swinging the lever by the
        /// HEAD's rotation assumes the nod pivoted at the neck bone; a real nod pivots at the base of
        /// the skull and the neck bone barely moves, so the raw estimate walks the neck -- and, through
        /// <c>hipsBase = neckPos - up * lenTotal</c>, the whole pelvis -- several centimetres on a steep
        /// look. Both damps rotate the swung lever back about the gaze's horizontal axis; 0 is the old
        /// undamped behaviour on that side, so an unset field cannot silently change anything.
        /// </summary>
        public static Vector3 Solve(Vector3 headTargetPos, Quaternion headWorldRot, Vector3 tposeHeadToNeckLocal,
            Vector3 playerUp, float extensionDamp, float flexionDamp)
        {
            Vector3 lever = headWorldRot * tposeHeadToNeckLocal;

            if (lever.sqrMagnitude < k_SqrEpsilon)
            {
                return headTargetPos + lever;
            }

            Vector3 up = playerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : playerUp.normalized;
            Vector3 gaze = headWorldRot * Vector3.forward;
            float upComp = Vector3.Dot(gaze, up);

            // Signed: positive is extension (look up), negative is flexion (look down). Each side gets
            // its own damp so enabling one cannot perturb the other.
            bool isExtension = upComp > 0f;
            float damp = Mathf.Clamp01(isExtension ? extensionDamp : flexionDamp);
            if (damp <= 0f)
            {
                return headTargetPos + lever;
            }

            Vector3 horiz = gaze - up * upComp;
            float horizMag = horiz.magnitude;
            float pitchDeg = Mathf.Atan2(upComp, horizMag) * Mathf.Rad2Deg;

            Vector3 forwardAzimuth;
            if (horizMag > k_Epsilon)
            {
                forwardAzimuth = horiz / horizMag;
            }
            else
            {
                // At the pole the gaze carries no azimuth of its own, so take it from the head's up
                // axis: it lies opposite the heading looking straight up and along it looking straight
                // down.
                Vector3 alt = headWorldRot * Vector3.up;
                Vector3 altH = alt - up * Vector3.Dot(alt, up);
                if (altH.sqrMagnitude < k_SqrEpsilon)
                {
                    return headTargetPos + lever;
                }
                forwardAzimuth = isExtension ? -altH.normalized : altH.normalized;
            }

            Vector3 axis = Vector3.Cross(up, forwardAzimuth);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return headTargetPos + lever;
            }
            axis.Normalize();

            Quaternion undo = BasisSpineAnatomyCore.AxisAngle(damp * pitchDeg, axis);
            return headTargetPos + undo * lever;
        }
    }

    public struct BasisTrunkCounterbalanceInput
    {
        public Vector3 HipsPos;

        public Vector3 NeckCue;
        public Vector3 PlayerUp;

        public float Gain;

        public float MaxShift;
    }

    public struct BasisTrunkCounterbalanceResult
    {
        public Vector3 HipsPos;
        public bool Applied;

        public float FlexionFrac;
        public float ShiftMeters;
    }

    public static class BasisTrunkCounterbalanceCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public const float DerivedGain = 0.38f;

        public static void Solve(in BasisTrunkCounterbalanceInput i, out BasisTrunkCounterbalanceResult r)
        {
            r = default;
            r.HipsPos = i.HipsPos;

            if (i.Gain <= 0f || i.MaxShift <= 0f)
            {
                return;
            }

            Vector3 up = i.PlayerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : i.PlayerUp.normalized;
            Vector3 trunk = i.NeckCue - i.HipsPos;
            float len = trunk.magnitude;
            if (len < k_Epsilon)
            {
                return;
            }

            Vector3 horizontal = trunk - up * Vector3.Dot(trunk, up);
            float horizMag = horizontal.magnitude;

            r.FlexionFrac = Mathf.Clamp01(horizMag / len);
            if (horizMag < k_Epsilon)
            {
                return;
            }

            float shift = Saturate(i.Gain * horizMag, i.MaxShift);
            if (shift <= 0f)
            {
                return;
            }

            r.Applied = true;
            r.ShiftMeters = shift;
            r.HipsPos = i.HipsPos - (horizontal / horizMag) * shift;
        }

        public static float Saturate(float x, float cap)
        {
            if (!(cap > k_Epsilon))
            {
                return 0f;
            }
            float soft = cap * 0.8f;
            if (!(x > soft))
            {
                return x < 0f ? 0f : x;
            }
            float m = cap - soft;
            float e = x - soft;
            return soft + m * e / (m + e);
        }
    }
}
