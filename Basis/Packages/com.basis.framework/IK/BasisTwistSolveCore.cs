namespace UnityEngine.Animations.Rigging
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
            Quaternion partialTwist = Quaternion.Slerp(Quaternion.identity, twistOnly, Mathf.Clamp01(i.Fraction));

            r.Apply = true;
            r.TwistWorldRotation = i.ParentRotation * partialTwist;
            r.TwistOnly = twistOnly;
            r.TwistAngleDeg = Quaternion.Angle(Quaternion.identity, twistOnly);
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
    }
}
