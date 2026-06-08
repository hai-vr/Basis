namespace UnityEngine.Animations.Rigging
{
    public struct BasisShoulderSolveInput
    {
        public Vector3 ShoulderPos;
        public Vector3 HandTargetPos;
        public Quaternion ChestRot;
        public Quaternion TposeShoulderRot;
        public float TposeArmLength;
        public float ElevationFactor;
        public float ProtractionFactor;
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
    }

    // Stream-free port of BasisFullIKConstraintJob.SolveShoulder.
    public static class BasisShoulderSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_ShoulderEngageThreshold = 0.7f;

        public static void Solve(in BasisShoulderSolveInput i, out BasisShoulderSolveResult r)
        {
            r = default;

            Vector3 shoulderToHand = i.HandTargetPos - i.ShoulderPos;
            float reachLen = shoulderToHand.magnitude;
            if (reachLen < k_Epsilon || i.TposeArmLength < k_Epsilon)
            {
                r.Apply = false;
                return;
            }

            float rawReachRatio = Mathf.Clamp01(reachLen / (i.TposeArmLength * 1.1f));

            float reachRatio;
            if (rawReachRatio < k_ShoulderEngageThreshold)
            {
                reachRatio = 0f;
            }
            else
            {
                float t = (rawReachRatio - k_ShoulderEngageThreshold) / (1f - k_ShoulderEngageThreshold);
                reachRatio = t * t;
            }

            Quaternion invChest = Quaternion.Inverse(i.ChestRot);
            Vector3 localHandDir = invChest * shoulderToHand.normalized;

            float elevation = Mathf.Clamp01(localHandDir.y) * reachRatio * i.ElevationFactor;
            float protraction = Mathf.Clamp01(localHandDir.z) * reachRatio * i.ProtractionFactor;

            float crossBody = i.IsLeft ? Mathf.Clamp01(-localHandDir.x) : Mathf.Clamp01(localHandDir.x);
            float crossBodyContrib = crossBody * reachRatio * i.ProtractionFactor * 0.5f;

            float elevAngle = elevation * 30f;
            float protAngle = (protraction + crossBodyContrib) * 20f;

            Quaternion elevRot = Quaternion.AngleAxis(i.IsLeft ? elevAngle : -elevAngle, Vector3.forward);
            Quaternion protRot = Quaternion.AngleAxis(i.IsLeft ? -protAngle : protAngle, Vector3.up);

            Quaternion baseRot = i.TposeShoulderRot;
            Quaternion adjustedRot = i.ChestRot * (invChest * baseRot) * elevRot * protRot;

            float computedWeight = Mathf.Clamp01(reachRatio * 1.5f);

            r.Apply = true;
            r.ShoulderRotation = Quaternion.Slerp(i.TrackerFinal, adjustedRot, computedWeight * (elevation + protraction + crossBodyContrib));
            r.RawReachRatio = rawReachRatio;
            r.ReachRatio = reachRatio;
            r.Elevation = elevation;
            r.Protraction = protraction;
            r.CrossBodyContrib = crossBodyContrib;
            r.ComputedWeight = computedWeight;
        }
    }
}
