using UnityEngine;
namespace Basis.IK
{
    public struct BasisCrouchOffsetInput
    {
        public Vector3 HeadTargetPos;
        public Vector3 HipsPos;
        public Quaternion HipsRot;
        // The hips calibration bind (offsetRotationHips). HipsRot carries it, so HipsRot * forward is the
        // bone's local +Z, not the body's facing -- on a Blender-bound rig that +Z is world-up, so the crouch
        // slid the hips vertically (or, once the up-component was stripped, collapsed and never fired). Cancel
        // it to get the anatomical forward. A degenerate/zero value (uncalibrated, or a caller that leaves it
        // default) means "HipsRot is already anatomical" -- the pre-bind behaviour, bit for bit.
        public Quaternion Bind;
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

            // Cancel the hips bind (a unit quaternion, so its inverse is its conjugate -- written out to keep
            // the core free of the native Quaternion.Inverse ECall, same discipline as BasisSpineAnatomyCore).
            // A degenerate bind falls back to HipsRot, which is the exact pre-bind behaviour.
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

            r.Applied = true;
            r.HipsPos = i.HipsPos - forward.normalized * (crouch * i.Factor);
        }
    }
}
