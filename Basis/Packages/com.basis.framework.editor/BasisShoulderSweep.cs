using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.IK.Debugging
{
    // Offline sweep of BasisShoulderSolveCore over a grid of hand targets in chest-local space.
    // Records the reach/elevation/protraction engagement curve and the applied shoulder angle.
    public struct BasisShoulderSweepConfig
    {
        public float TposeArmLength;
        public float ElevationFactor;
        public float ProtractionFactor;
        public bool IsLeft;

        public Vector3 MinFrac;     // hand target around shoulder, fractions of arm length
        public Vector3 MaxFrac;
        public Vector3Int Steps;

        public static BasisShoulderSweepConfig Default()
        {
            return new BasisShoulderSweepConfig
            {
                TposeArmLength = 0.54f,
                ElevationFactor = 1.0f,
                ProtractionFactor = 1.0f,
                IsLeft = false,
                MinFrac = new Vector3(-1.1f, -1.1f, -1.1f),
                MaxFrac = new Vector3(1.1f, 1.1f, 1.1f),
                Steps = new Vector3Int(11, 11, 11),
            };
        }
    }

    public struct BasisShoulderSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Rows;
        public int Engaged;     // points where reachRatio > 0
        public float MaxShoulderAngleDeg;
        public string Error;
    }

    public static class BasisShoulderSweep
    {
        public const string DefaultFileName = "BasisShoulderSweep.csv";

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public static BasisShoulderSweepSummary Run(BasisShoulderSweepConfig cfg, string path)
        {
            var summary = new BasisShoulderSweepSummary { Ok = false, Path = path };

            float mirror = cfg.IsLeft ? -1f : 1f;
            float armLen = Mathf.Max(1e-4f, cfg.TposeArmLength);
            Vector3 shoulder = Vector3.zero;

            int sx = Mathf.Max(1, cfg.Steps.x);
            int sy = Mathf.Max(1, cfg.Steps.y);
            int sz = Mathf.Max(1, cfg.Steps.z);

            int rows = 0;
            int engaged = 0;
            float maxAngle = 0f;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisShoulderSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# side=" + (cfg.IsLeft ? "left" : "right") + " armLen=" + F(armLen) +
                                " elevFactor=" + F(cfg.ElevationFactor) + " protFactor=" + F(cfg.ProtractionFactor));
                    w.WriteLine("side,ti,tj,tk,target_x,target_y,target_z,reach_len," +
                                "raw_reach_ratio,reach_ratio,elevation,protraction,cross_body,computed_weight,shoulder_angle_deg");

                    var sb = new StringBuilder(192);
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
                                Vector3 target = shoulder + new Vector3(fx, fy, fz) * armLen;

                                BasisShoulderSolveInput input;
                                input.ShoulderPos = shoulder;
                                input.HandTargetPos = target;
                                input.ChestRot = Quaternion.identity;
                                input.TposeShoulderRot = Quaternion.identity;
                                input.TposeArmLength = armLen;
                                input.ElevationFactor = cfg.ElevationFactor;
                                input.ProtractionFactor = cfg.ProtractionFactor;
                                input.TrackerFinal = Quaternion.identity;
                                input.IsLeft = cfg.IsLeft;
                                BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult res);

                                float angle = res.Apply ? Quaternion.Angle(Quaternion.identity, res.ShoulderRotation) : 0f;
                                if (res.ReachRatio > 0f) engaged++;
                                if (angle > maxAngle) maxAngle = angle;

                                sb.Clear();
                                sb.Append(side).Append(',');
                                sb.Append(i).Append(',').Append(j).Append(',').Append(k).Append(',');
                                Append(sb, target.x); Append(sb, target.y); Append(sb, target.z);
                                Append(sb, (target - shoulder).magnitude);
                                Append(sb, res.RawReachRatio); Append(sb, res.ReachRatio);
                                Append(sb, res.Elevation); Append(sb, res.Protraction);
                                Append(sb, res.CrossBodyContrib); Append(sb, res.ComputedWeight);
                                sb.Append(F(angle));
                                w.WriteLine(sb.ToString());
                                rows++;
                            }
                        }
                    }
                }

                summary.Ok = true;
                summary.Rows = rows;
                summary.Engaged = engaged;
                summary.MaxShoulderAngleDeg = maxAngle;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static float Lerp01(float min, float max, int steps, int idx)
        {
            if (steps <= 1) return 0.5f * (min + max);
            return Mathf.Lerp(min, max, idx / (float)(steps - 1));
        }
    }
}
