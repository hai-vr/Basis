using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Offline proof that the calibration lock-in aid math (BasisCalibrationLockInCore) behaves the way
    // the visualizer relies on: the proximity weight is a clamped, monotonic 0..1 ramp (1 at the capture
    // radius, 0 at the falloff radius), the ball diameter shrinks max -> min as it locks, and the foot
    // alignment score is a worse-axis-wins 0..1 that reaches 0 exactly at the yaw/tilt limits. Sweeps the
    // same formulas the runtime and NUnit tests use and writes a CSV for eyeballing the curves.
    public struct BasisCalibrationLockInSweepConfig
    {
        public int ProximitySteps;   // distance samples from on-the-spot out past falloff
        public int AngleSteps;       // angle samples from aligned out past the limit
        public float CaptureRadius;  // meters: locked at/under this
        public float FalloffRadius;  // meters: faded out at/over this
        public float MinDiameter;    // meters: ball size when locked
        public float MaxDiameter;    // meters: ball size when far
        public float MaxYawDeg;
        public float MaxTiltDeg;

        public static BasisCalibrationLockInSweepConfig Default()
        {
            return new BasisCalibrationLockInSweepConfig
            {
                ProximitySteps = 41,
                AngleSteps = 41,
                CaptureRadius = 0.05f,
                FalloffRadius = 0.30f,
                MinDiameter = 0.02f,
                MaxDiameter = 0.09f,
                MaxYawDeg = BasisCalibrationLockInCore.DefaultMaxYawDeg,
                MaxTiltDeg = BasisCalibrationLockInCore.DefaultMaxTiltDeg,
            };
        }
    }

    public struct BasisCalibrationLockInSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Rows;

        public float WeightAtCapture;       // must be 1
        public float WeightAtFalloff;       // must be 0
        public float WeightBeyondFalloff;   // must be 0 (clamped, never negative)
        public float WeightMaxRise;         // largest frame-to-frame increase as distance grows (must be ~0)

        public float DiameterAtLocked;      // must equal MinDiameter
        public float DiameterAtFar;         // must equal MaxDiameter

        public float ScorePerfect;          // forward + flat, must be 1
        public float ScoreAtYawLimit;       // must be 0
        public float ScoreAtTiltLimit;      // must be 0
        public float ScoreMaxRiseYaw;       // largest increase as yaw grows (must be ~0)

        public string Error;
    }

    public static class BasisCalibrationLockInSweep
    {
        public const string DefaultFileName = "BasisCalibrationLockInSweep.csv";
        public static string DefaultPath() => Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisCalibrationLockInSweepSummary Run(BasisCalibrationLockInSweepConfig cfg, string path)
        {
            var s = new BasisCalibrationLockInSweepSummary { Ok = false, Path = path };
            int proxSteps = Mathf.Max(2, cfg.ProximitySteps);
            int angSteps = Mathf.Max(2, cfg.AngleSteps);

            float weightMaxRise = 0f, scoreMaxRiseYaw = 0f;
            int rows = 0;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisCalibrationLockInSweep " + System.DateTime.UtcNow.ToString("o"));

                    // --- proximity: distance -> lock weight -> ball diameter ---
                    w.WriteLine("# proximity");
                    w.WriteLine("distance_m,weight,diameter_m");
                    var sb = new StringBuilder(96);
                    float prevWeight = 2f;
                    float maxDist = cfg.FalloffRadius * 1.25f;
                    for (int i = 0; i < proxSteps; i++)
                    {
                        float dist = Mathf.Lerp(0f, maxDist, i / (float)(proxSteps - 1));
                        float weight = BasisCalibrationLockInCore.ProximityWeight(dist, cfg.CaptureRadius, cfg.FalloffRadius);
                        float diameter = BasisCalibrationLockInCore.ProximityRadius(weight, cfg.MinDiameter, cfg.MaxDiameter);
                        if (weight - prevWeight > weightMaxRise) weightMaxRise = weight - prevWeight;
                        prevWeight = weight;

                        sb.Clear();
                        Append(sb, dist); Append(sb, weight); sb.Append(F(diameter));
                        w.WriteLine(sb.ToString());
                        rows++;
                    }

                    // --- alignment: yaw / tilt deviation -> guide score (each axis isolated) ---
                    w.WriteLine("# alignment");
                    w.WriteLine("frac,yaw_deg,yaw_score,tilt_deg,tilt_score");
                    float prevYawScore = 2f;
                    for (int i = 0; i < angSteps; i++)
                    {
                        float frac = Mathf.Lerp(0f, 1.25f, i / (float)(angSteps - 1));
                        float yawDeg = frac * cfg.MaxYawDeg;
                        float tiltDeg = frac * cfg.MaxTiltDeg;
                        float yawScore = BasisCalibrationLockInCore.FootAlignmentScore(yawDeg, 0f, cfg.MaxYawDeg, cfg.MaxTiltDeg);
                        float tiltScore = BasisCalibrationLockInCore.FootAlignmentScore(0f, tiltDeg, cfg.MaxYawDeg, cfg.MaxTiltDeg);
                        if (yawScore - prevYawScore > scoreMaxRiseYaw) scoreMaxRiseYaw = yawScore - prevYawScore;
                        prevYawScore = yawScore;

                        sb.Clear();
                        Append(sb, frac); Append(sb, yawDeg); Append(sb, yawScore); Append(sb, tiltDeg); sb.Append(F(tiltScore));
                        w.WriteLine(sb.ToString());
                        rows++;
                    }
                }

                s.Rows = rows;
                s.WeightAtCapture = BasisCalibrationLockInCore.ProximityWeight(cfg.CaptureRadius, cfg.CaptureRadius, cfg.FalloffRadius);
                s.WeightAtFalloff = BasisCalibrationLockInCore.ProximityWeight(cfg.FalloffRadius, cfg.CaptureRadius, cfg.FalloffRadius);
                s.WeightBeyondFalloff = BasisCalibrationLockInCore.ProximityWeight(cfg.FalloffRadius * 1.25f, cfg.CaptureRadius, cfg.FalloffRadius);
                s.WeightMaxRise = weightMaxRise;
                s.DiameterAtLocked = BasisCalibrationLockInCore.ProximityRadius(1f, cfg.MinDiameter, cfg.MaxDiameter);
                s.DiameterAtFar = BasisCalibrationLockInCore.ProximityRadius(0f, cfg.MinDiameter, cfg.MaxDiameter);
                s.ScorePerfect = BasisCalibrationLockInCore.FootAlignmentScore(0f, 0f, cfg.MaxYawDeg, cfg.MaxTiltDeg);
                s.ScoreAtYawLimit = BasisCalibrationLockInCore.FootAlignmentScore(cfg.MaxYawDeg, 0f, cfg.MaxYawDeg, cfg.MaxTiltDeg);
                s.ScoreAtTiltLimit = BasisCalibrationLockInCore.FootAlignmentScore(0f, cfg.MaxTiltDeg, cfg.MaxYawDeg, cfg.MaxTiltDeg);
                s.ScoreMaxRiseYaw = scoreMaxRiseYaw;
                s.Ok = true;
            }
            catch (System.Exception e)
            {
                s.Ok = false;
                s.Error = e.Message;
            }

            return s;
        }

        // Pass/fail gate over a finished sweep. Mirrors the properties the NUnit tests assert so an
        // editor button and CI both fail loudly on the same regressions.
        public static bool Gate(BasisCalibrationLockInSweepSummary s, out string reason)
        {
            if (!s.Ok) { reason = "sweep did not run: " + s.Error; return false; }
            if (Mathf.Abs(s.WeightAtCapture - 1f) > 1e-3f) { reason = $"weight at capture {s.WeightAtCapture:0.000} != 1"; return false; }
            if (Mathf.Abs(s.WeightAtFalloff) > 1e-3f) { reason = $"weight at falloff {s.WeightAtFalloff:0.000} != 0"; return false; }
            if (Mathf.Abs(s.WeightBeyondFalloff) > 1e-3f) { reason = $"weight past falloff {s.WeightBeyondFalloff:0.000} != 0 (not clamped)"; return false; }
            if (s.WeightMaxRise > 1e-4f) { reason = $"proximity weight rose with distance (max rise {s.WeightMaxRise:0.0000})"; return false; }
            if (s.DiameterAtFar <= s.DiameterAtLocked) { reason = "far diameter is not larger than locked diameter"; return false; }
            if (Mathf.Abs(s.ScorePerfect - 1f) > 1e-3f) { reason = $"perfect alignment score {s.ScorePerfect:0.000} != 1"; return false; }
            if (s.ScoreAtYawLimit > 1e-3f) { reason = $"score at yaw limit {s.ScoreAtYawLimit:0.000} != 0"; return false; }
            if (s.ScoreAtTiltLimit > 1e-3f) { reason = $"score at tilt limit {s.ScoreAtTiltLimit:0.000} != 0"; return false; }
            if (s.ScoreMaxRiseYaw > 1e-4f) { reason = $"alignment score rose with yaw (max rise {s.ScoreMaxRiseYaw:0.0000})"; return false; }
            reason = "ok";
            return true;
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
