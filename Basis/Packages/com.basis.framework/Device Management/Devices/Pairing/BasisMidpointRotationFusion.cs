using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Pairing
{
    /// <summary>
    /// Pure rotation math for fusing two rigidly-linked tracker rotations into a single
    /// midpoint orientation. Lives apart from <see cref="BasisVirtualMidpointInput"/> (a
    /// MonoBehaviour wired to live devices and settings) so the offline multi-tracker
    /// rotation sweep can exercise the exact math the live pair runs, with no scene.
    /// </summary>
    public static class BasisMidpointRotationFusion
    {
        // Slerp the two tracker rotations after first projecting them into a
        // common predicted-midpoint frame using the rest-pose offset captured at
        // init. aRot * halfRest and bRot * halfRest^-1 both predict the same
        // midpoint orientation when the pair is rigid, so the slerp output is
        // independent of t in that case -- no flip when the blend ratio swings during rotation.
        public static Quaternion ProjectAndSlerp(Quaternion aRot, Quaternion bRot, float t, Quaternion halfRestOffset)
        {
            bool aUsable = IsUsable(aRot);
            bool bUsable = IsUsable(bRot);
            // A partner with no pose this frame (XR input hands back a zero quaternion on
            // tracking loss) drops OUT of the fusion -- it must not fold in as identity,
            // which reads as world-forward and swings the fused rotation toward forward
            // by however far the body is turned (the linked-pair "rotation flip").
            if (!aUsable && !bUsable) return Quaternion.identity;
            if (!bUsable) return aRot * halfRestOffset;
            if (!aUsable) return bRot * Quaternion.Inverse(halfRestOffset);
            Quaternion midA = aRot * halfRestOffset;
            Quaternion midB = bRot * Quaternion.Inverse(halfRestOffset);
            // Force shortest-arc explicitly. Unity's Slerp does this internally,
            // but by aligning the hemisphere here we also keep the output
            // quaternion sign in a consistent hemisphere relative to midA across
            // frames -- defensive against any downstream code that ever inspects
            // the raw quaternion components rather than the rotation it represents.
            if (Quaternion.Dot(midA, midB) < 0f)
            {
                midB = new Quaternion(-midB.x, -midB.y, -midB.z, -midB.w);
            }
            return Quaternion.Slerp(midA, midB, t);
        }

        // Half of the rest-pose relative rotation A->B, i.e. the quaternion
        // square root of (A^-1 * B). Slerp from identity at t=0.5 is exactly
        // that square root and is well-defined for the full range of relative
        // orientations the trackers can be mounted at.
        public static Quaternion ComputeHalfRestOffset(Quaternion aRot, Quaternion bRot)
        {
            // Priming off a tracking-lost (zero) pose would bake a garbage rest offset
            // into every later frame; the caller defers the prime until both are usable.
            if (!IsUsable(aRot) || !IsUsable(bRot)) return Quaternion.identity;
            Quaternion delta = Quaternion.Inverse(aRot) * bRot;
            return Quaternion.Slerp(Quaternion.identity, delta, 0.5f);
        }

        // A zero (or non-finite) quaternion is the "no pose this frame" sentinel the XR
        // input layer produces on tracking loss; every other unit quaternion is usable.
        public static bool IsUsable(Quaternion q)
        {
            float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return sq > 1e-8f && !float.IsNaN(sq);
        }
    }
}
