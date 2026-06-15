using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations.Rigging;

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
                        var input = new BasisTwistSolveInput
                        {
                            ParentRotation = parentRot,
                            ChildRotation = parentRot * Quaternion.AngleAxis(theta, localAxis),
                            ParentToChild = parentToChild,
                            Fraction = 1f,
                        };
                        BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r);
                        float pureErr = r.Apply ? Mathf.Abs(r.TwistAngleDeg - theta) : 999f;

                        // Partial twist: the applied world rotation sits Fraction*theta off the parent.
                        input.Fraction = frac;
                        BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r2);
                        float fracAngle = r2.Apply ? Quaternion.Angle(parentRot, r2.TwistWorldRotation) : 999f;
                        float fracErr = Mathf.Abs(fracAngle - frac * theta);

                        // Swing rejection: a perpendicular swing must not tilt the extracted twist axis.
                        Vector3 perp = Vector3.Cross(localAxis, RandUnit(rng));
                        float axisMis = 0f;
                        if (perp.sqrMagnitude > 1e-6f)
                        {
                            perp.Normalize();
                            float swingAng = Mathf.Lerp(5f, 60f, (float)rng.NextDouble());
                            input.ChildRotation = parentRot * Quaternion.AngleAxis(swingAng, perp) * Quaternion.AngleAxis(theta, localAxis);
                            input.Fraction = 1f;
                            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r3);
                            if (r3.Apply && r3.TwistAngleDeg > 5f)
                            {
                                r3.TwistOnly.ToAngleAxis(out _, out Vector3 twAxis);
                                if (twAxis.sqrMagnitude > 1e-6f)
                                {
                                    float a = Vector3.Angle(twAxis.normalized, localAxis);
                                    axisMis = Mathf.Min(a, 180f - a); // axis sign is arbitrary
                                }
                            }
                        }

                        // Singular cases must no-op.
                        var sing = input; sing.Fraction = 0f; sing.ChildRotation = parentRot * Quaternion.AngleAxis(theta, localAxis);
                        BasisTwistSolveCore.Solve(sing, out BasisTwistSolveResult rs);
                        var sing2 = sing; sing2.Fraction = 1f; sing2.ParentToChild = Vector3.zero;
                        BasisTwistSolveCore.Solve(sing2, out BasisTwistSolveResult rs2);
                        bool singularOk = !rs.Apply && !rs2.Apply;

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
