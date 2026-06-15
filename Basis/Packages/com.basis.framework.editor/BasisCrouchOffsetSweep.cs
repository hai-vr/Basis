using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.IK.Debugging
{
    // Offline sweep of BasisCrouchOffsetCore over crouch depth x facing x factor x rest height. Verifies
    // the hips slide back by exactly crouch*Factor, purely horizontally along hips-back, grow monotonically
    // with crouch, clamp at the rest height, and never move while standing or disabled. Same math as the
    // live ApplyCrouchBodyOffset.
    public struct BasisCrouchOffsetSweepConfig
    {
        public int DepthSteps;     // crouch samples from above-standing to past-full
        public int YawSteps;       // hips facing directions

        public static BasisCrouchOffsetSweepConfig Default()
        {
            return new BasisCrouchOffsetSweepConfig { DepthSteps = 41, YawSteps = 8 };
        }
    }

    public struct BasisCrouchOffsetSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Cases;
        public int AppliedCases;
        public int NaNCount;
        public float MaxMagErrM;          // |offset| vs crouch*Factor
        public float MaxUpComponentM;     // vertical leak (should be ~0)
        public float MaxDirErrDeg;        // offset direction vs hips-back horizontal
        public int StandingMoves;         // moved while crouch==0 / disabled
        public int MonotonicViolations;   // offset shrank as crouch grew
        public int Failures;
        public string Error;
    }

    public static class BasisCrouchOffsetSweep
    {
        public const string DefaultFileName = "BasisCrouchOffsetSweep.csv";
        static readonly float[] k_Rest = { 0.4f, 0.6f, 0.8f };
        static readonly float[] k_Factor = { 0f, 0.2f, 0.5f, 1f };

        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisCrouchOffsetSweepSummary Run(BasisCrouchOffsetSweepConfig cfg, string path)
        {
            var summary = new BasisCrouchOffsetSweepSummary { Ok = false, Path = path };

            Vector3 hips = Vector3.zero;
            Vector3 up = Vector3.up;
            int ds = Mathf.Max(2, cfg.DepthSteps);
            int ys = Mathf.Max(1, cfg.YawSteps);

            int cases = 0, applied = 0, nan = 0, standingMoves = 0, monoViol = 0, fails = 0;
            float mMag = 0f, mUp = 0f, mDir = 0f;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisCrouchOffsetSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("rest,factor,yaw,crouch,applied,offset_mag,mag_err,up_comp,dir_err,mono_viol,fail");
                    var sb = new StringBuilder(160);

                    foreach (float rest in k_Rest)
                    {
                        foreach (float factor in k_Factor)
                        {
                            for (int yi = 0; yi < ys; yi++)
                            {
                                float yaw = ys <= 1 ? 0f : (yi / (float)ys) * 360f;
                                Quaternion hipsRot = Quaternion.Euler(0f, yaw, 0f);
                                Vector3 fwd = hipsRot * Vector3.forward;
                                fwd -= up * Vector3.Dot(fwd, up);
                                Vector3 expectDir = fwd.sqrMagnitude > 1e-8f ? -fwd.normalized : Vector3.zero;

                                float prevMag = 0f; bool havePrev = false;
                                for (int di = 0; di < ds; di++)
                                {
                                    float d = Mathf.Lerp(-0.1f, rest + 0.2f, di / (float)(ds - 1));
                                    Vector3 head = hips + up * (rest - d); // (headUp - hipsUp) = rest - d -> crouch = clamp(d)

                                    BasisCrouchOffsetInput input;
                                    input.HeadTargetPos = head;
                                    input.HipsPos = hips;
                                    input.HipsRot = hipsRot;
                                    input.PlayerUp = up;
                                    input.Factor = factor;
                                    input.RestDist = rest;
                                    BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult res);

                                    bool hadNaN = !Finite(res.HipsPos) || float.IsNaN(res.Crouch);
                                    Vector3 offset = res.HipsPos - hips;
                                    float offMag = offset.magnitude;
                                    float expectCrouch = Mathf.Clamp(d, 0f, rest);
                                    float expectMag = expectCrouch * factor;

                                    float magErr = Mathf.Abs(offMag - expectMag);
                                    float upComp = Mathf.Abs(Vector3.Dot(offset, up));
                                    float dirErr = (offMag > 1e-5f && expectDir.sqrMagnitude > 0f)
                                        ? Vector3.Angle(offset.normalized, expectDir) : 0f;
                                    bool standingMove = expectMag <= 1e-6f && offMag > 1e-6f;
                                    bool monoBad = havePrev && offMag < prevMag - 1e-5f;
                                    prevMag = offMag; havePrev = true;

                                    cases++;
                                    if (hadNaN) nan++;
                                    if (res.Applied) applied++;
                                    if (standingMove) standingMoves++;
                                    if (monoBad) monoViol++;
                                    if (magErr > mMag) mMag = magErr;
                                    if (upComp > mUp) mUp = upComp;
                                    if (dirErr > mDir) mDir = dirErr;

                                    bool fail = hadNaN || magErr > 1e-5f || upComp > 1e-5f || dirErr > 0.1f || standingMove || monoBad;
                                    if (fail) fails++;

                                    sb.Clear();
                                    Append(sb, rest); Append(sb, factor); Append(sb, yaw); Append(sb, expectCrouch);
                                    sb.Append(res.Applied ? '1' : '0').Append(',');
                                    Append(sb, offMag); Append(sb, magErr); Append(sb, upComp); Append(sb, dirErr);
                                    sb.Append(monoBad ? '1' : '0').Append(',');
                                    sb.Append(fail ? '1' : '0');
                                    w.WriteLine(sb.ToString());
                                }
                            }
                        }
                    }
                }

                summary.Ok = true;
                summary.Cases = cases;
                summary.AppliedCases = applied;
                summary.NaNCount = nan;
                summary.MaxMagErrM = mMag;
                summary.MaxUpComponentM = mUp;
                summary.MaxDirErrDeg = mDir;
                summary.StandingMoves = standingMoves;
                summary.MonotonicViolations = monoViol;
                summary.Failures = fails;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
