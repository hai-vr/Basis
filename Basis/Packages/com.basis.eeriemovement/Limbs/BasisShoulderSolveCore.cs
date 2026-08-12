using UnityEngine;
namespace Basis.IK
{
    public struct BasisShoulderSolveInput
    {
        public Vector3 ShoulderPos;
        public Vector3 HandTargetPos;
        public Vector3 ElbowPos;
        public bool HasElbow;
        public bool HasShoulderTracker;
        public Quaternion ChestRot;
        public Quaternion TposeChestRot;
        public Quaternion TposeShoulderRot;
        public Vector3 TposeArmDirWorld;
        public float TposeArmLength;
        public float TposeClavicleLength;
        public float TposeElbowLength;
        public bool ShrugEnabled;
        public float ElevationFactor;
        public float ProtractionFactor;
        public float CoupleRatio;
        public float MaxShoulderDeg;
        public Quaternion TrackerFinal;
        public bool IsLeft;
    }

    public struct BasisShoulderSolveResult
    {
        public bool Apply;
        public Quaternion ShoulderRotation;

        public float RawReachRatio;
        public float ReachRatio;
        public float Elevation;
        public float Protraction;
        public float CrossBodyContrib;
        public float ComputedWeight;

        public float SwingAngleDeg;
        public float AppliedAngleDeg;
        public float TwistLeakDeg;
        public bool DriverIsElbow;
        public float ShrugDeg;
    }

    public static class BasisShoulderSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-10f;
        const float k_ReachEngageThreshold = 0.7f;
        const float k_SetStartDeg = 8f;
        const float k_SetFullDeg = 95f;
        const float k_TrackerRefine = 0.35f;
        const float k_DepressionShare = 0.25f;
        const float k_DepressionBand = 0.12f;

        const float k_ShrugHangStart = 0.75f;
        const float k_ShrugHangFull = 0.92f;
        const float k_ShrugRiseStartFrac = 0.02f;
        const float k_ShrugRiseFullFrac = 0.065f;
        const float k_ShrugBendFadeStartFrac = 0.09f;
        const float k_ShrugBendFadeEndFrac = 0.14f;
        const float k_ShrugMaxDeg = 44f;

        public static void Solve(in BasisShoulderSolveInput i, out BasisShoulderSolveResult r)
        {
            r = default;

            float armLen = Mathf.Max(i.TposeArmLength, k_Epsilon);
            Vector3 driver = i.HasElbow ? i.ElbowPos : i.HandTargetPos;
            Vector3 armVec = driver - i.ShoulderPos;
            Vector3 handVec = i.HandTargetPos - i.ShoulderPos;
            if (armVec.sqrMagnitude < k_SqrEpsilon || i.TposeArmDirWorld.sqrMagnitude < k_SqrEpsilon)
            {
                r.Apply = false;
                return;
            }

            Quaternion invChest = Quaternion.Inverse(i.ChestRot);
            Vector3 restDirL = (Quaternion.Inverse(i.TposeChestRot) * i.TposeArmDirWorld).normalized;
            Vector3 armDirL = (invChest * armVec).normalized;

            Quaternion swing = BasisQuaternionExt.FromToRotation(restDirL, armDirL);
            Vector3 rv = QuatToRotationVector(swing);
            float swingDeg = rv.magnitude * Mathf.Rad2Deg;

            float rawReach = handVec.magnitude / (armLen * 1.1f);
            float reachFade = i.HasElbow ? 1f : ReachEngage(Mathf.Clamp01(rawReach));
            float setting = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_SetStartDeg, k_SetFullDeg, swingDeg));
            float engage = setting * reachFade;

            float couple = i.CoupleRatio * engage;

            float raise = armDirL.y - restDirL.y;
            float elevGain = Mathf.Lerp(k_DepressionShare, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-k_DepressionBand, k_DepressionBand, raise)));
            Vector3 girdleRv = new Vector3(rv.x * i.ElevationFactor * elevGain, rv.y * i.ProtractionFactor, rv.z * i.ElevationFactor * elevGain) * couple;

            float shrugRad = 0f;
            float bindLen = i.HasElbow ? i.TposeElbowLength : i.TposeArmLength;
            if (i.ShrugEnabled && bindLen > k_Epsilon)
            {
                float clav = Mathf.Clamp(i.TposeClavicleLength, 0f, bindLen * 0.45f);
                float seg = Mathf.Max(bindLen - clav, k_Epsilon);

                Vector3 segVecL = armDirL * armVec.magnitude - restDirL * clav;
                float hangDot = segVecL.sqrMagnitude > k_SqrEpsilon ? -segVecL.normalized.y : 0f;
                if (hangDot > k_ShrugHangStart)
                {
                    float hang = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugHangStart, k_ShrugHangFull, hangDot));

                    float cr = Vector3.Dot(restDirL, armDirL);
                    float expected = clav * cr + Mathf.Sqrt(clav * clav * (cr * cr - 1f) + seg * seg);
                    float deficitFrac = (expected - armVec.magnitude) / armLen;

                    float riseIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugRiseStartFrac, k_ShrugRiseFullFrac, deficitFrac));
                    float bendOut = i.HasElbow ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_ShrugBendFadeStartFrac, k_ShrugBendFadeEndFrac, deficitFrac));
                    shrugRad = k_ShrugMaxDeg * Mathf.Deg2Rad * hang * riseIn * (1f - bendOut) * Mathf.Max(i.ElevationFactor, 0f);
                    girdleRv.z += i.IsLeft ? -shrugRad : shrugRad;
                }
            }

            girdleRv -= Vector3.Dot(girdleRv, armDirL) * armDirL;

            float maxRad = Mathf.Max(i.MaxShoulderDeg, 0f) * Mathf.Deg2Rad;
            float mag = girdleRv.magnitude;
            if (maxRad > 0f && mag > maxRad && mag > k_Epsilon)
            {
                girdleRv *= maxRad / mag;
            }

            Quaternion girdleL = BasisQuaternionExt.NormalizeSafe(RotationVectorToQuat(girdleRv));

            Quaternion shoulderRestLocal = Quaternion.Inverse(i.TposeChestRot) * i.TposeShoulderRot;
            Quaternion anatomical = i.ChestRot * girdleL * shoulderRestLocal;

            Quaternion result;
            if (i.HasShoulderTracker)
            {
                Quaternion deltaWorld = i.ChestRot * girdleL * invChest;
                result = Quaternion.Slerp(i.TrackerFinal, deltaWorld * i.TrackerFinal, k_TrackerRefine * engage);
            }
            else
            {
                result = anatomical;
            }

            float elevRad = Mathf.Sqrt(girdleRv.x * girdleRv.x + girdleRv.z * girdleRv.z);
            float twistLeak = girdleRv.sqrMagnitude > k_Epsilon ? Vector3.Dot(girdleRv, armDirL) : 0f;

            r.Apply = true;
            r.ShoulderRotation = result;
            r.RawReachRatio = rawReach;
            r.ReachRatio = reachFade;
            r.Elevation = elevRad * Mathf.Rad2Deg;
            r.Protraction = Mathf.Abs(girdleRv.y) * Mathf.Rad2Deg;
            r.CrossBodyContrib = CrossBody(armDirL, restDirL, i.IsLeft) * Mathf.Abs(girdleRv.y) * Mathf.Rad2Deg;
            r.ComputedWeight = engage;
            r.SwingAngleDeg = swingDeg;
            r.AppliedAngleDeg = Quaternion.Angle(Quaternion.identity, girdleL);
            r.TwistLeakDeg = Mathf.Abs(twistLeak) * Mathf.Rad2Deg;
            r.DriverIsElbow = i.HasElbow;
            r.ShrugDeg = shrugRad * Mathf.Rad2Deg;
        }

        static float ReachEngage(float reach)
        {
            if (reach < k_ReachEngageThreshold)
            {
                return 0f;
            }
            float t = (reach - k_ReachEngageThreshold) / (1f - k_ReachEngageThreshold);
            return t * t;
        }

        static float CrossBody(Vector3 armDirL, Vector3 restDirL, bool isLeft)
        {
            float side = isLeft ? -armDirL.x : armDirL.x;
            return Mathf.Clamp01(-side);
        }

        static Quaternion RotationVectorToQuat(Vector3 rv)
        {
            float ang = rv.magnitude;
            if (ang < k_Epsilon)
            {
                return Quaternion.identity;
            }
            return Quaternion.AngleAxis(ang * Mathf.Rad2Deg, rv / ang);
        }

        static Vector3 QuatToRotationVector(Quaternion q)
        {
            q.ToAngleAxis(out float deg, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || axis.sqrMagnitude < k_Epsilon)
            {
                return Vector3.zero;
            }
            if (deg > 180f)
            {
                deg -= 360f;
            }
            return axis.normalized * (deg * Mathf.Deg2Rad);
        }
    }
}
