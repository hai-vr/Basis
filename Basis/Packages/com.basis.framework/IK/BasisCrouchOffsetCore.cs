namespace UnityEngine.Animations.Rigging
{
    public struct BasisCrouchOffsetInput
    {
        public Vector3 HeadTargetPos;
        public Vector3 HipsPos;
        public Quaternion HipsRot;
        public Vector3 PlayerUp;
        public float Factor;
        public float RestDist;
    }

    public struct BasisCrouchOffsetResult
    {
        public Vector3 HipsPos;  // input hips offset backward when crouching (== input when not)
        public bool Applied;
        public float Crouch;     // clamped crouch depth [0, RestDist]
    }

    // Stream-free port of BasisFullIKConstraintJob.ApplyCrouchBodyOffset's math. As the head drops below
    // standing height the hips slide backward (hips-forward, horizontal) by crouch*Factor so a squat reads
    // as sitting back rather than knees-forward. The caller still gates on the chest/hips trackers (rig
    // state); this is the pure geometry, shared so the live job and the sweep stay in lock-step.
    public static class BasisCrouchOffsetCore
    {
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisCrouchOffsetInput i, out BasisCrouchOffsetResult r)
        {
            r = default;
            r.HipsPos = i.HipsPos;

            if (i.Factor <= 0f)
            {
                return;
            }

            Vector3 up = i.PlayerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : i.PlayerUp.normalized;

            float crouch = i.RestDist - (Vector3.Dot(i.HeadTargetPos, up) - Vector3.Dot(i.HipsPos, up));
            crouch = Mathf.Clamp(crouch, 0f, i.RestDist);
            r.Crouch = crouch;
            if (crouch <= 0f)
            {
                return;
            }

            Vector3 forward = i.HipsRot * Vector3.forward;
            forward -= up * Vector3.Dot(forward, up);
            if (forward.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            r.Applied = true;
            r.HipsPos = i.HipsPos - forward.normalized * (crouch * i.Factor);
        }
    }
}
