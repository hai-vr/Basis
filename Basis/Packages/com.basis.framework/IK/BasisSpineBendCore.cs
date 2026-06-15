namespace UnityEngine.Animations.Rigging
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

        public float SquishBoost;
        public float RestLen;            // TposeLengthHeadToHips.magnitude

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
    // asymmetric flexion clamp, returning the spine/upperChest euler deltas. The wrapper still owns the
    // chest spring and the handle reads/writes (delta = hipsRot * Euler(e) * invHips, pre-multiplied onto
    // the bone). Change the distribution math HERE so the job and the sweep stay in lock-step.
    public static class BasisSpineBendCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;
        const float k_BendDeadbandDeg = 3f;
        const float k_BendDeadbandWidthDeg = 7f;

        public static void Solve(in BasisSpineBendInput i, out BasisSpineBendResult r)
        {
            r = default;

            Quaternion invHips = Quaternion.Inverse(i.HipsRot);

            Vector3 localChestDir = invHips * (i.ChestPos - i.HipsPos);
            Vector3 localTargetDir = invHips * (i.SmoothedHead - i.HipsPos);
            if (localChestDir.sqrMagnitude < k_SqrEpsilon || localTargetDir.sqrMagnitude < k_SqrEpsilon)
            {
                r.EarlyOut = true;
                return;
            }

            Vector3 chestDirN = localChestDir.normalized;
            Vector3 targetDirN = localTargetDir.normalized;
            float bendPitchDeg = (Mathf.Atan2(targetDirN.z, targetDirN.y) - Mathf.Atan2(chestDirN.z, chestDirN.y)) * Mathf.Rad2Deg;
            float bendRollDeg = (Mathf.Atan2(-targetDirN.x, targetDirN.y) - Mathf.Atan2(-chestDirN.x, chestDirN.y)) * Mathf.Rad2Deg;
            Vector3 bendEuler = new Vector3(bendPitchDeg, 0f, bendRollDeg);

            Quaternion headRotLocal = (i.HipsBind * invHips) * i.HeadTargetRot;
            Vector3 headFwdLocal = headRotLocal * Vector3.forward;
            float horizMagSq = headFwdLocal.x * headFwdLocal.x + headFwdLocal.z * headFwdLocal.z;
            float twistY = (horizMagSq < k_SqrEpsilon) ? 0f : Mathf.Atan2(headFwdLocal.x, headFwdLocal.z) * Mathf.Rad2Deg;

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
