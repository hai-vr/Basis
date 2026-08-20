using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Basis.IK;

namespace Basis.IK.Debugging
{
    // Offline sweep of BasisTwistSolveCore (swing-twist decomposition used by the arm/forearm twist
    // distribution). Pure math, edit mode. Asserts: a pure twist about the bone axis is recovered
    // exactly, the Fraction blends it linearly, the extracted twist stays on the bone axis even with
    // a perpendicular swing present, and the singular cases (no fraction / zero bone vector) no-op.

    public struct BasisTwistSweepConfig
    {
        public int Cases;
        public static BasisTwistSweepConfig Default() => new BasisTwistSweepConfig { Cases = 5000 };
    }

    public struct BasisTwistSummary
    {
        public bool Ok;
        public string Path;
        public string Error;
        public int Cases;
        public float MaxPureTwistErrDeg;   // recovered twist angle vs the applied pure-twist angle
        public float MaxFractionErrDeg;    // partial-twist angle vs Fraction*angle
        public float MaxAxisMisalignDeg;   // extracted twist axis vs the bone axis, with a perpendicular swing present
        public int SingularityFailures;    // Fraction<=0 or zero bone vector must return Apply=false
        public float MaxConcentrationRatio; // worst per-span twist rate / the even ideal (1 = perfectly even, >1 = roll piling up at one joint)
        public float MaxEvennessErrDeg;     // worst deviation of the twist bone from the linear (even) gradient
        public int DistributionCases;       // position×roll combos checked for even distribution
    }

    public static class BasisTwistSweep
    {
        public const string DefaultFileName = "BasisTwistSweep.csv";
        public static string DefaultPath() => Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisTwistSummary Run(BasisTwistSweepConfig cfg, string path)
        {
            var s = new BasisTwistSummary { Ok = false, Path = path };
            int cases = Mathf.Max(1, cfg.Cases);

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisTwistSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("case,pure_err,frac_err,axis_misalign,singular_ok");
                    var sb = new StringBuilder(128);

                    for (int i = 0; i < cases; i++)
                    {
                        var rng = new System.Random(7000 + i);
                        Quaternion parentRot = RandRot(rng);
                        Vector3 localAxis = RandUnit(rng);
                        Vector3 parentToChild = parentRot * localAxis * Mathf.Lerp(0.1f, 0.6f, (float)rng.NextDouble());
                        float theta = Mathf.Lerp(5f, 150f, (float)rng.NextDouble());
                        float frac = Mathf.Lerp(0.1f, 1f, (float)rng.NextDouble());

                        // Pure twist about the bone axis → recovered angle must equal theta.
                        Quaternion pureChild = parentRot * Quaternion.AngleAxis(theta, localAxis);
                        bool apply = BasisTwistSolveCore.Solve(parentRot, pureChild, parentToChild, 1f, default, default,
                            out _, out _, out float twistAngleDeg);
                        float pureErr = apply ? Mathf.Abs(twistAngleDeg - theta) : 999f;

                        // Partial twist: the applied world rotation sits Fraction*theta off the parent.
                        bool apply2 = BasisTwistSolveCore.Solve(parentRot, pureChild, parentToChild, frac, default, default,
                            out Quaternion twistWorld2, out _, out _);
                        float fracAngle = apply2 ? Quaternion.Angle(parentRot, twistWorld2) : 999f;
                        float fracErr = Mathf.Abs(fracAngle - frac * theta);

                        // Swing rejection: a perpendicular swing must not tilt the extracted twist axis.
                        Vector3 perp = Vector3.Cross(localAxis, RandUnit(rng));
                        float axisMis = 0f;
                        if (perp.sqrMagnitude > 1e-6f)
                        {
                            perp.Normalize();
                            float swingAng = Mathf.Lerp(5f, 60f, (float)rng.NextDouble());
                            Quaternion swungChild = parentRot * Quaternion.AngleAxis(swingAng, perp) * Quaternion.AngleAxis(theta, localAxis);
                            bool apply3 = BasisTwistSolveCore.Solve(parentRot, swungChild, parentToChild, 1f, default, default,
                                out _, out Quaternion twistOnly3, out float twistAngle3);
                            if (apply3 && twistAngle3 > 5f)
                            {
                                twistOnly3.ToAngleAxis(out _, out Vector3 twAxis);
                                if (twAxis.sqrMagnitude > 1e-6f)
                                {
                                    float a = Vector3.Angle(twAxis.normalized, localAxis);
                                    axisMis = Mathf.Min(a, 180f - a); // axis sign is arbitrary
                                }
                            }
                        }

                        // Singular cases must no-op.
                        bool singApply = BasisTwistSolveCore.Solve(parentRot, pureChild, parentToChild, 0f, default, default,
                            out _, out _, out _);
                        bool sing2Apply = BasisTwistSolveCore.Solve(parentRot, pureChild, Vector3.zero, 1f, default, default,
                            out _, out _, out _);
                        bool singularOk = !singApply && !sing2Apply;

                        s.MaxPureTwistErrDeg = Mathf.Max(s.MaxPureTwistErrDeg, pureErr);
                        s.MaxFractionErrDeg = Mathf.Max(s.MaxFractionErrDeg, fracErr);
                        s.MaxAxisMisalignDeg = Mathf.Max(s.MaxAxisMisalignDeg, axisMis);
                        if (!singularOk) s.SingularityFailures++;
                        s.Cases++;

                        bool fail = pureErr > 0.5f || fracErr > 0.5f || axisMis > 1f || !singularOk;
                        if (fail || i < 3)
                        {
                            sb.Clear();
                            sb.Append(i).Append(',').Append(F(pureErr)).Append(',').Append(F(fracErr)).Append(',').Append(F(axisMis)).Append(',').Append(singularOk ? 1 : 0);
                            w.WriteLine(sb.ToString());
                        }
                    }
                }

                // Even-distribution check: a single twist bone must take a share equal to its POSITION along
                // the bone (BasisTwistSolveCore.SegmentPositionFraction), so the roll spreads as a LINEAR
                // gradient. Concentration ratio = worst per-span twist rate / the uniform ideal (1 = perfectly
                // even; >1 = the roll piling up at one joint -- the "candy-wrapper" the position-proportional
                // share fixes). With distribution strength 1, the bone's share == its position fraction.
                float worstConc = 1f, worstEvenErr = 0f;
                int distCases = 0;
                Vector3 axis = Vector3.forward;
                Vector3 parentPos = Vector3.zero;
                Vector3 childPos = axis * 0.26f;
                for (int pi = 1; pi <= 17; pi++)
                {
                    float p = pi / 18f;
                    Vector3 twistPos = axis * (p * 0.26f);
                    float pMeas = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, twistPos);
                    foreach (float roll in new[] { 30f, 75f, 120f, 160f })
                    {
                        // strength 1 -> even share == position fraction
                        bool drApply = BasisTwistSolveCore.Solve(Quaternion.identity, Quaternion.AngleAxis(roll, axis),
                            childPos - parentPos, pMeas, default, default, out Quaternion drTwistWorld, out _, out _);
                        float boneRoll = drApply ? Quaternion.Angle(Quaternion.identity, drTwistWorld) : 0f;
                        float effMeas = roll > 1e-4f ? boneRoll / roll : 0f;
                        float rate1 = p > 1e-4f ? effMeas / p : 0f;
                        float rate2 = (1f - p) > 1e-4f ? (1f - effMeas) / (1f - p) : 0f;
                        worstConc = Mathf.Max(worstConc, Mathf.Max(rate1, rate2));
                        worstEvenErr = Mathf.Max(worstEvenErr, Mathf.Abs(boneRoll - p * roll));
                        distCases++;
                    }
                }
                s.MaxConcentrationRatio = worstConc;
                s.MaxEvennessErrDeg = worstEvenErr;
                s.DistributionCases = distCases;

                s.Ok = true;
            }
            catch (System.Exception e)
            {
                s.Ok = false;
                s.Error = e.Message;
            }
            return s;
        }

        private static Quaternion RandRot(System.Random r) =>
            Quaternion.Euler((float)(r.NextDouble() * 360.0 - 180.0), (float)(r.NextDouble() * 360.0 - 180.0), (float)(r.NextDouble() * 360.0 - 180.0));

        private static Vector3 RandUnit(System.Random r)
        {
            Vector3 v = new Vector3((float)(r.NextDouble() * 2.0 - 1.0), (float)(r.NextDouble() * 2.0 - 1.0), (float)(r.NextDouble() * 2.0 - 1.0));
            if (v.sqrMagnitude < 1e-6f) v = Vector3.up;
            return v.normalized;
        }

        private static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
