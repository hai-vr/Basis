using UnityEngine;
namespace Basis.IK
{
    public struct BasisSpineBendInput
    {
        public Quaternion HipsRot;
        public Vector3 HipsPos;
        public Vector3 ChestPos;
        public Vector3 SmoothedHead;     // head target after the chest-follow spring
        public Quaternion HipsBind;      // offsetRotationHips (captured bind; cancels the live hips bone bind)
        public Quaternion HeadTargetRot; // targetRotationHead

        public float SpineMaxForwardDeg;
        public float SpineMaxBackwardDeg;
        public float SpineMaxLateralDeg;

        public float SpineBendPitch, SpineBendYaw, SpineBendRoll;
        public float UpperBendPitch, UpperBendYaw, UpperBendRoll;

        public bool AnatDifferentialStiffness;
        public bool AnatPelvicTwistRouting;
        public float BendTwistCoupling;  // lateral bend -> a little same-side axial rotation (organic spinal coupling)

        public float SquishBoost;
        public float RestLen;            // tposeLengthHeadToHips.magnitude

        public bool HasSpine;
        public bool HasUpper;
    }

    public struct BasisSpineBendResult
    {
        public bool EarlyOut;
        public bool WriteSpine; public Vector3 SpineEuler;
        public bool WriteUpper; public Vector3 UpperEuler;

        // diagnostics
        public float BendPitchDeg;
        public float BendRollDeg;
        public float TwistY;
        public float SquishMult;
        public float BendGate;
        public float SpineYawEff;
        public float UpperYawEff;
    }

    // Stream-free port of BasisFullIKConstraintJob.DistributeSpineBend's per-axis math. Computes the bend
    // (pitch/roll from chest->head), the twist (from head facing, with the hips bind cancelled so atan2
    // stays continuous across center), the squish coupling, the anatomy weight re-routing and the
    // asymmetric flexion clamp, returning the spine/upperChest per-axis deltas. The wrapper still owns the
    // chest spring and the handle reads/writes (delta = hipsAnat * Compose(e) * invHipsAnat, pre-multiplied
    // onto the bone). Change the distribution math HERE so the job and the sweep stay in lock-step.
    public static class BasisSpineBendCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;
        const float k_BendDeadbandDeg = 3f;
        const float k_BendDeadbandWidthDeg = 7f;
        // Head-facing twist fade, expressed in horizontal head-forward magnitude (= |cos| of the gaze
        // pitch off level). Full twist at/above ~70 deg of pitch (cos 70 = 0.342), faded to nothing by
        // ~80 deg (cos 80 = 0.174) -- before the vertical-gaze pole where the facing azimuth is undefined.
        const float k_TwistFadeFullHoriz = 0.342f;
        const float k_TwistFadeZeroHoriz = 0.174f;

        public static void Solve(in BasisSpineBendInput i, out BasisSpineBendResult r)
        {
            r = default;

            Quaternion invHips = Quaternion.Inverse(i.HipsRot);
            // Bind-cancelled hips space, same as the twist below: the raw hips-bone frame is a rig
            // convention, and on a rig whose hips bind is rolled the bend dirs land on the atan2(z,y)
            // pole -- the pre-bend then flips between the +forward and -backward clamps as the head
            // scans across center. Identity bind => identical products, bit for bit.
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
            // The bend as the ONE rotation carrying chest-dir onto target-dir, read out as rotation-vector
            // components on the anatomical axes -- not as two independent plane azimuths, whose difference
            // over-reports a diagonal bend and carries an atan2 pole per plane. Same sign convention:
            // +x tips forward (pitch), -z tips to the subject's right (roll). The axis' y component is
            // dropped, as before: a from-to rotation between directions carries no meaningful twist.
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
            // Fade the facing twist out as the gaze nears vertical. The azimuth flips ~180 deg across the
            // straight-down/up pole, which snapped the chest/upperChest sideways the instant the gaze
            // crossed vertical; horizMag collapses to 0 there, so smoothstep the twist off well before it.
            // Pure horizontal turning keeps horizMag == 1, so ordinary look-around is untouched.
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

        /// <summary>
        /// Quaternion for a per-axis anatomical delta (x = pitch, y = yaw, z = roll, degrees): the yaw
        /// outermost -- where Quaternion.Euler put it -- and the pitch/roll pair as ONE swing, the same
        /// swing-twist construction BasisSpineAnatomyCore.Recompose uses, instead of two ordered euler
        /// rotations. ECall-free, so it runs in the standalone harnesses.
        /// </summary>
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
}
