using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.IK.Debugging
{
    // Offline sweep of BasisLegSolveCore over a 3D grid of foot targets, solved with and
    // without a knee hint. One CSV row per (target, mode). Pure math, no avatar.
    public struct BasisLegIKSweepConfig
    {
        public float UpperLength;
        public float LowerLength;
        public Vector3 RestKneeDir;     // root->knee at rest
        public Vector3 RestShinDir;     // knee->foot at rest
        public Vector3 BendNormal;      // KneeBendPref (hips right)
        public bool IsLeft;

        public Vector3 MinFrac;
        public Vector3 MaxFrac;
        public Vector3Int Steps;

        public Vector3 HintDir;
        public float HintDistanceFrac;

        public static BasisLegIKSweepConfig Default()
        {
            return new BasisLegIKSweepConfig
            {
                UpperLength = 0.42f,
                LowerLength = 0.42f,
                RestKneeDir = new Vector3(0.0f, -0.97f, 0.20f),
                RestShinDir = new Vector3(0.0f, -0.98f, -0.10f),
                BendNormal = new Vector3(1.0f, 0.0f, 0.0f),
                IsLeft = false,
                MinFrac = new Vector3(-0.5f, -1.15f, -0.8f),
                MaxFrac = new Vector3(0.7f, -0.2f, 0.9f),
                Steps = new Vector3Int(9, 9, 9),
                HintDir = new Vector3(0.0f, -0.3f, 1.0f),
                HintDistanceFrac = 0.5f,
            };
        }
    }

    public struct BasisLegIKSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Rows;
        public int Points;
        public int ReachablePoints;
        public float MeanSwivelShiftDeg;
        public float MaxSwivelShiftDeg;
        public string Error;
    }

    public static class BasisLegIKSweep
    {
        public const string DefaultFileName = "BasisLegIKSweep.csv";

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public static BasisLegIKSweepSummary Run(BasisLegIKSweepConfig cfg, string path)
        {
            var summary = new BasisLegIKSweepSummary { Ok = false, Path = path };

            float mirror = cfg.IsLeft ? -1f : 1f;
            float upper = Mathf.Max(1e-4f, cfg.UpperLength);
            float lower = Mathf.Max(1e-4f, cfg.LowerLength);
            float legLen = upper + lower;

            Vector3 hip = Vector3.zero;
            Vector3 kneeDir = Mirror(cfg.RestKneeDir, mirror).normalized;
            Vector3 shinDir = Mirror(cfg.RestShinDir, mirror).normalized;
            if (kneeDir.sqrMagnitude < 1e-8f) kneeDir = Vector3.down;
            if (shinDir.sqrMagnitude < 1e-8f) shinDir = Vector3.down;

            Vector3 restKnee = hip + kneeDir * upper;
            Vector3 restFoot = restKnee + shinDir * lower;

            Vector3 bendNormal = Mirror(cfg.BendNormal, mirror);
            if (bendNormal.sqrMagnitude < 1e-8f) bendNormal = Vector3.right;

            Vector3 hintDir = Mirror(cfg.HintDir, mirror).normalized;
            if (hintDir.sqrMagnitude < 1e-8f) hintDir = Vector3.forward;
            Vector3 hintPos = hip + hintDir * (cfg.HintDistanceFrac * legLen);

            int sx = Mathf.Max(1, cfg.Steps.x);
            int sy = Mathf.Max(1, cfg.Steps.y);
            int sz = Mathf.Max(1, cfg.Steps.z);

            int points = 0;
            int reachable = 0;
            double swivelShiftSum = 0.0;
            float swivelShiftMax = 0f;
            int rows = 0;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisLegIKSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# side=" + (cfg.IsLeft ? "left" : "right") +
                                " upper=" + F(upper) + " lower=" + F(lower) +
                                " bendNormal=(" + F(bendNormal.x) + "," + F(bendNormal.y) + "," + F(bendNormal.z) + ")");
                    w.WriteLine("side,mode,ti,tj,tk,target_x,target_y,target_z,target_dist,leg_len," +
                                "reach_ratio,reachable,hint_on,hint_x,hint_y,hint_z," +
                                "knee_x,knee_y,knee_z,foot_x,foot_y,foot_z,foot_error," +
                                "knee_flex_deg,knee_swivel_deg,axis_source");

                    var sb = new StringBuilder(256);
                    string side = cfg.IsLeft ? "left" : "right";

                    for (int i = 0; i < sx; i++)
                    {
                        float fx = Lerp01(cfg.MinFrac.x, cfg.MaxFrac.x, sx, i) * mirror;
                        for (int j = 0; j < sy; j++)
                        {
                            float fy = Lerp01(cfg.MinFrac.y, cfg.MaxFrac.y, sy, j);
                            for (int k = 0; k < sz; k++)
                            {
                                float fz = Lerp01(cfg.MinFrac.z, cfg.MaxFrac.z, sz, k);
                                Vector3 target = hip + new Vector3(fx, fy, fz) * legLen;
                                points++;

                                BasisLegSolveResult noHint = SolveOne(hip, restKnee, restFoot, target, hintPos, bendNormal, 0f);
                                BasisLegSolveResult hint = SolveOne(hip, restKnee, restFoot, target, hintPos, bendNormal, 1f);

                                bool isReachable = noHint.ReachRatio <= 1f;
                                if (isReachable) reachable++;

                                float swivelNo = Swivel(hip, noHint.FootSolved, noHint.KneeSolved);
                                float swivelHint = Swivel(hip, hint.FootSolved, hint.KneeSolved);
                                if (isReachable && !float.IsNaN(swivelNo) && !float.IsNaN(swivelHint))
                                {
                                    float shift = Mathf.Abs(Mathf.DeltaAngle(swivelNo, swivelHint));
                                    swivelShiftSum += shift;
                                    if (shift > swivelShiftMax) swivelShiftMax = shift;
                                }

                                rows += WriteRow(w, sb, side, "nohint", i, j, k, target, legLen, isReachable, false, Vector3.zero, noHint, swivelNo);
                                rows += WriteRow(w, sb, side, "hint", i, j, k, target, legLen, isReachable, true, hintPos, hint, swivelHint);
                            }
                        }
                    }
                }

                summary.Ok = true;
                summary.Rows = rows;
                summary.Points = points;
                summary.ReachablePoints = reachable;
                summary.MeanSwivelShiftDeg = reachable > 0 ? (float)(swivelShiftSum / reachable) : 0f;
                summary.MaxSwivelShiftDeg = swivelShiftMax;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        static BasisLegSolveResult SolveOne(Vector3 hip, Vector3 knee, Vector3 foot, Vector3 target, Vector3 hint, Vector3 bendNormal, float hintWeight)
        {
            BasisLegSolveInput input;
            input.Root = hip;
            input.Mid = knee;
            input.Tip = foot;
            input.RootRotation = Quaternion.identity;
            input.MidRotation = Quaternion.identity;
            input.TargetPosition = target;
            input.TargetRotation = Quaternion.identity;
            input.HintPosition = hint;
            input.HintWeight = hintWeight;
            input.TargetOffset = Quaternion.identity;
            input.BendNormal = bendNormal;
            BasisLegSolveCore.Solve(input, out BasisLegSolveResult r);
            return r;
        }

        // Signed knee swivel around the hip->foot axis, measured from straight-forward.
        static float Swivel(Vector3 hip, Vector3 foot, Vector3 knee)
        {
            Vector3 axis = foot - hip;
            if (axis.sqrMagnitude < 1e-8f) return float.NaN;
            axis.Normalize();
            Vector3 refVec = Vector3.ProjectOnPlane(Vector3.forward, axis);
            Vector3 poleVec = Vector3.ProjectOnPlane(knee - hip, axis);
            if (refVec.sqrMagnitude < 1e-8f || poleVec.sqrMagnitude < 1e-8f) return float.NaN;
            return Vector3.SignedAngle(refVec, poleVec, axis);
        }

        static int WriteRow(StreamWriter w, StringBuilder sb, string side, string mode, int i, int j, int k,
            Vector3 target, float legLen, bool reachable, bool hintOn, Vector3 hint, BasisLegSolveResult r, float swivel)
        {
            sb.Clear();
            sb.Append(side).Append(',').Append(mode).Append(',');
            sb.Append(i).Append(',').Append(j).Append(',').Append(k).Append(',');
            Append(sb, target.x); Append(sb, target.y); Append(sb, target.z);
            Append(sb, r.TargetDistance); Append(sb, legLen);
            Append(sb, r.ReachRatio);
            sb.Append(reachable ? '1' : '0').Append(',');
            sb.Append(hintOn ? '1' : '0').Append(',');
            Append(sb, hint.x); Append(sb, hint.y); Append(sb, hint.z);
            Append(sb, r.KneeSolved.x); Append(sb, r.KneeSolved.y); Append(sb, r.KneeSolved.z);
            Append(sb, r.FootSolved.x); Append(sb, r.FootSolved.y); Append(sb, r.FootSolved.z);
            Append(sb, r.FootError);
            Append(sb, r.KneeAngleDeg);
            Append(sb, swivel);
            sb.Append(r.AxisSource);
            w.WriteLine(sb.ToString());
            return 1;
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static Vector3 Mirror(Vector3 v, float mirror) { return new Vector3(v.x * mirror, v.y, v.z); }

        static float Lerp01(float min, float max, int steps, int idx)
        {
            if (steps <= 1) return 0.5f * (min + max);
            return Mathf.Lerp(min, max, idx / (float)(steps - 1));
        }
    }
}
