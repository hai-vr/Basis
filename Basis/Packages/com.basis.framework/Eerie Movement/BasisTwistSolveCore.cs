using UnityEngine;
namespace Basis.IK
{
    public struct BasisTwistSolveInput
    {
        public Quaternion ParentRotation;
        public Quaternion ChildRotation;
        public Vector3 ParentToChild;   // world: child position - parent position
        public float Fraction;
    }

    public struct BasisTwistSolveResult
    {
        public bool Apply;
        public Quaternion TwistWorldRotation;
        public Quaternion TwistOnly;
        public float TwistAngleDeg;
    }

    // Stream-free port of BasisFullIKConstraintJob.SolveArmTwist (swing-twist distribution).
    public static class BasisTwistSolveCore
    {
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisTwistSolveInput i, out BasisTwistSolveResult r)
        {
            r = default;
            if (i.Fraction <= 0f || i.ParentToChild.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            Vector3 axis = (Quaternion.Inverse(i.ParentRotation) * i.ParentToChild).normalized;
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            Quaternion childLocal = Quaternion.Inverse(i.ParentRotation) * i.ChildRotation;
            Quaternion twistOnly = ExtractTwist(childLocal, axis);
            float fraction = Mathf.Clamp01(i.Fraction);

            // =====================================================================================
            // ⭐ THE +/-180 SEAM, AND THIS IS WHERE IT LANDS LAST. A FRACTIONAL TWIST CANNOT BE
            // CONTINUOUS OVER THE FULL CIRCLE, SO IT HAS TO BE MADE TO MEET ITSELF AT THE SEAM.
            //
            // The child's twist theta lives on a CIRCLE: +180 and -180 are the SAME child pose.
            // This stage applies `fraction * theta` to the twist bone, and for any fraction f
            // strictly between 0 and 1 that map sends those two identical poses to +180f and
            // -180f -- bones 360f apart. It is the same topological obstruction as the wrist
            // relief's and the humeral guard's seams in BasisArmSolveCore, one layer further
            // down, and it is the LAST stage in the arm chain, so nothing catches it afterwards.
            //
            // ⚠️ IT IS REACHED BY THE FIX ABOVE IT, WHICH IS EXACTLY WHY IT IS FIXED HERE TOO.
            // BasisArmSolveCore's forearm-roll seam cap deliberately drives the forearm to 180 deg
            // relative to the humerus at the seam -- that is what makes the FOREARM continuous
            // there, and there is no alternative: continuity at the seam REQUIRES the bone to pass
            // through 180 (any odd map with want(+180) - want(-180) != 360 leaves a jump). But that
            // hands this stage a 180 deg child twist, and MEASURED on the composed stream, the
            // upper-arm twist bone then jumped
            //
            //     54.10 deg in a single 0.25 deg step of controller roll   (f = 0.5 * 0.3 = 0.15)
            //
            // i.e. exactly 360 * f. Fixing the forearm without fixing this would have MOVED the
            // discontinuity from the elbow into the middle of the upper arm rather than removed it,
            // which is this repo's documented way of not fixing something.
            //
            // THE SAME DEVICE AS EVERY OTHER SEAM IN THIS CHAIN: cap the amount the bone DECLINES
            // to take by the distance to the seam.
            //
            //     pull = (1 - f) * |theta|     what the bone declines, slope (1-f) in |theta|
            //     seam = 180 - |theta|         zero AT the seam, slope -1
            //     pull = min(pull, seam)
            //
            // ⭐ WHAT IT COSTS, EXACTLY:
            //   * |theta| <= 180/(2-f) -- the crossing, 97.3 deg at f = 0.15 and 102.9 deg at
            //     f = 0.25, the two fractions the live rig actually uses -- the cap does not bind
            //     and the ORIGINAL Slerp runs, untouched, on the original operands. Not "equivalent
            //     to": the same call. So every ordinary twist is bit for bit what it was.
            //   * above the crossing the bone eases to 2*|theta| - 180, reaching 180 at the seam,
            //     where +180 and -180 are the same rotation and the two sides meet.
            //   * min() of a +(1-f) and a -1 slope bounds the gain by construction.
            //
            // The live arm never reaches the crossing on the LOWER twist bone (hand-vs-forearm is
            // bounded at ~50 deg by the forearm seam cap upstream), so in practice this changes
            // only the upper-arm bone, and only inside the seam window.
            // =====================================================================================
            float twistDeg = SignedTwistAngleDeg(twistOnly, axis);
            float mag = twistDeg < 0f ? -twistDeg : twistDeg;
            float pull = (1f - fraction) * mag;
            float seam = 180f - mag;

            Quaternion partialTwist;
            if (pull > seam)
            {
                float capped = mag - seam;                       // = 2*mag - 180
                if (!(capped > 0f)) capped = 0f;                 // reject-unless-good: NaN lands here
                partialTwist = AxisAngleDeg(twistDeg < 0f ? -capped : capped, axis);
            }
            else
            {
                // UNCHANGED. Below the crossing this is the original call on the original operands.
                partialTwist = Quaternion.Slerp(Quaternion.identity, twistOnly, fraction);
            }

            r.Apply = true;
            r.TwistWorldRotation = i.ParentRotation * partialTwist;
            r.TwistOnly = twistOnly;
            r.TwistAngleDeg = Quaternion.Angle(Quaternion.identity, twistOnly);
        }

        // Axis-angle about a unit axis, written the long way rather than through Quaternion.AngleAxis
        // because this runs inside a Burst job -- the same reason BasisArmSolveCore builds its own.
        static Quaternion AxisAngleDeg(float deg, Vector3 axis)
        {
            float h = deg * (0.5f * Mathf.Deg2Rad);
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        // Signed rotation angle (deg) of q about `axis` (unit) -- the twist half of the swing-twist split,
        // read out as an angle. Continuous through a 90 deg swing off the axis, where eulerAngles gimbal-locks
        // and flips ~180 deg. Used to measure the chest's axial twist for the shoulder slide.
        public static float SignedTwistAngleDeg(Quaternion q, Vector3 axis)
        {
            Quaternion t = ExtractTwist(q, axis);
            float s = t.x * axis.x + t.y * axis.y + t.z * axis.z;   // sin(theta/2) along axis
            float w = t.w;                                          // cos(theta/2)
            if (w < 0f) { w = -w; s = -s; }                        // shortest arc (quaternion double-cover)
            return 2f * Mathf.Atan2(s, w) * Mathf.Rad2Deg;
        }

        // Swing-twist decomposition: extracts the rotation of q around axis (unit vector).
        public static Quaternion ExtractTwist(Quaternion q, Vector3 axis)
        {
            Vector3 ra = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(ra, axis);
            Quaternion twist = new Quaternion(p.x, p.y, p.z, q.w);
            float magSq = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (magSq < k_SqrEpsilon)
            {
                return Quaternion.identity;
            }

            float invMag = 1f / Mathf.Sqrt(magSq);
            return new Quaternion(twist.x * invMag, twist.y * invMag, twist.z * invMag, twist.w * invMag);
        }

        // Fractional position of the twist bone along its parent->child segment (projected onto the segment),
        // clamped to [0,1]. A single twist bone that absorbs THIS fraction of the child's roll yields a LINEAR
        // (even) twist gradient from parent (0) to child (full): the parent->bone and bone->child spans then
        // twist at the same rate, instead of the roll piling up in the short span between a wrist-end twist
        // bone and the hand (the "candy-wrapper" concentration).
        public static float SegmentPositionFraction(Vector3 parentPos, Vector3 childPos, Vector3 twistPos)
        {
            Vector3 seg = childPos - parentPos;
            float segLen2 = seg.sqrMagnitude;
            if (segLen2 < k_SqrEpsilon) return 0f;
            return Mathf.Clamp01(Vector3.Dot(twistPos - parentPos, seg) / segLen2);
        }

        // Scales the component of 'delta' that rotates about 'axis' (unit) by 'keep' (1 = unchanged,
        // 0 = removed), leaving the swing about every perpendicular axis intact. delta = swing * twist,
        // so keep=1 reconstructs delta exactly and keep=0 returns the pure swing. The spine CCD uses it
        // to stop a sideways head reach from being solved as axial twist about player-up (it bends instead).
        public static Quaternion RelaxAroundAxis(Quaternion delta, Vector3 axis, float keep) => ShapeReachStep(delta, axis, keep, 1f);

        // Splits 'delta' into swing (rotation about every axis perpendicular to 'axis') and twist (about
        // 'axis'), and scales each: twist by 'twistKeep', swing by 'swingScale'. delta == swing * twist, so
        // twistKeep=1, swingScale=1 reconstructs delta exactly. The spine CCD uses it to keep a sideways head
        // reach a BEND -- twistKeep grades the axial twist (rigid lumbar -> free cervical), swingScale grades
        // bend mobility (stiffer mid-thoracic) so the curve distributes -- instead of an axial corkscrew or
        // a single-joint kink. 'axis' is the body's hips-up, so it is orientation-independent (works prone).
        public static Quaternion ShapeReachStep(Quaternion delta, Vector3 axis, float twistKeep, float swingScale)
        {
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(swingScale));
            }
            axis = axis.normalized;
            Quaternion twist = ExtractTwist(delta, axis);
            Quaternion swing = delta * Quaternion.Inverse(twist);
            Quaternion scaledSwing = Quaternion.Slerp(Quaternion.identity, swing, Mathf.Clamp01(swingScale));
            Quaternion scaledTwist = Quaternion.Slerp(Quaternion.identity, twist, Mathf.Clamp01(twistKeep));
            return scaledSwing * scaledTwist;
        }
    }
}
