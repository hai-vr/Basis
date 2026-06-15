namespace UnityEngine.Animations.Rigging
{
    public struct BasisHipHingeInput
    {
        public Vector3 HeadPos;
        public Vector3 HipsPos;
        public Quaternion HipsRot;
        public Vector3 PlayerUp;
        public float StartDeg;
        public float MaxAddDeg;
    }

    public struct BasisHipHingeResult
    {
        public Quaternion HipsRot;   // input rotation, pitched forward by AddDeg when Applied
        public bool Applied;
        public float LeanDeg;        // forward lean of head over hips (NaN when not computed)
        public float AddDeg;         // pelvis pitch added this solve
    }

    // Stream-free port of BasisFullIKConstraintJob.ApplyHipHinge. When the forward lean past StartDeg
    // grows, the pelvis pitches forward by half the excess (capped at MaxAddDeg) so the spine doesn't
    // swallow the whole reach. Change the hinge math HERE so the job and the offline sweep stay in lock-step.
    public static class BasisHipHingeCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisHipHingeInput i, out BasisHipHingeResult r)
        {
            r = default;
            r.HipsRot = i.HipsRot;
            r.LeanDeg = float.NaN;

            if (i.MaxAddDeg <= 0f)
            {
                return;
            }

            Vector3 hipsToHead = i.HeadPos - i.HipsPos;
            float upDot = Vector3.Dot(hipsToHead, i.PlayerUp);
            Vector3 horizontal = hipsToHead - i.PlayerUp * upDot;
            float horizMag = horizontal.magnitude;
            if (horizMag < k_Epsilon || upDot <= 0f)
            {
                return;
            }

            float leanDeg = Mathf.Atan2(horizMag, upDot) * Mathf.Rad2Deg;
            r.LeanDeg = leanDeg;
            if (leanDeg <= i.StartDeg)
            {
                return;
            }

            float excess = leanDeg - i.StartDeg;
            float addDeg = Mathf.Min(excess * 0.5f, i.MaxAddDeg);

            Vector3 hingeAxis = Vector3.Cross(i.PlayerUp, horizontal / horizMag);
            if (hingeAxis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            hingeAxis.Normalize();
            r.AddDeg = addDeg;
            r.Applied = true;
            r.HipsRot = Quaternion.AngleAxis(addDeg, hingeAxis) * i.HipsRot;
        }
    }
}
