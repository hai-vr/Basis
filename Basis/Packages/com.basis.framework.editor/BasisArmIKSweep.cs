using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.IK.Debugging
{
    // Offline sweep of BasisArmSolveCore over a 3D grid of hand targets, solved with and
    // without an elbow hint tracker. One CSV row per (target, mode). Pure math, no avatar.
    public struct BasisArmIKSweepConfig
    {
        public float UpperLength;
        public float LowerLength;
        public Vector3 RestElbowDir;    // seeds the bend plane
        public Vector3 RestForearmDir;
        public bool IsLeft;             // mirror X

        // Target grid in shoulder-local space, as fractions of total arm length.
        public Vector3 MinFrac;
        public Vector3 MaxFrac;
        public Vector3Int Steps;

        public Vector3 HintDir;
        public float HintDistanceFrac;

        public static BasisArmIKSweepConfig Default()
        {
            return new BasisArmIKSweepConfig
            {
                UpperLength = 0.28f,
                LowerLength = 0.26f,
                RestElbowDir = new Vector3(0.15f, -0.95f, 0.27f),
                RestForearmDir = new Vector3(0.0f, -0.30f, 0.95f),
                IsLeft = false,
                MinFrac = new Vector3(-0.7f, -1.1f, -0.6f),
                MaxFrac = new Vector3(1.2f, 0.7f, 1.15f),
                Steps = new Vector3Int(9, 9, 9),
                HintDir = new Vector3(0.2f, -1.0f, -0.15f),
                HintDistanceFrac = 0.5f,
            };
        }
    }

    public struct BasisArmIKSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Rows;            // total CSV data rows (2 per target point)
        public int Points;          // target points swept
        public int ReachablePoints; // points with reach ratio <= 1
        public float MeanSwivelShiftDeg; // mean |swivel_hint - swivel_nohint| over reachable points
        public float MaxSwivelShiftDeg;
        public float LookupMeanAbsSwivelDeg;
        public int LookupElbowUpCount;   // reachable lookup poses whose pole points up (chicken-wing)
        public float TrackerMeanSensDegPerCm; // elbow swivel deg per cm of tracker position error
        public float TrackerMaxSensDegPerCm;
        public int TrackerJitteryCount;  // reachable poses with sensitivity > 20 deg/cm
        public int TrackerFadedCount;    // reachable poses where the tracker hint is faded (reach>0.9)
        public float TrackerMeanAlignErrDeg; // mean angle between solved elbow and tracker pole (under-follow)
        public float TrackerMaxAlignErrDeg;
        public string Error;
    }

    public static class BasisArmIKSweep
    {
        public const string DefaultFileName = "BasisArmIKSweep.csv";

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public static BasisArmIKSweepSummary Run(BasisArmIKSweepConfig cfg, string path)
        {
            var summary = new BasisArmIKSweepSummary { Ok = false, Path = path };

            float mirror = cfg.IsLeft ? -1f : 1f;
            float upper = Mathf.Max(1e-4f, cfg.UpperLength);
            float lower = Mathf.Max(1e-4f, cfg.LowerLength);
            float armLen = upper + lower;

            Vector3 shoulder = Vector3.zero;
            Vector3 elbowDir = Mirror(cfg.RestElbowDir, mirror).normalized;
            Vector3 forearmDir = Mirror(cfg.RestForearmDir, mirror).normalized;
            if (elbowDir.sqrMagnitude < 1e-8f) elbowDir = Vector3.down;
            if (forearmDir.sqrMagnitude < 1e-8f) forearmDir = Vector3.forward;

            Vector3 restElbow = shoulder + elbowDir * upper;
            Vector3 restHand = restElbow + forearmDir * lower;

            Vector3 hintDir = Mirror(cfg.HintDir, mirror).normalized;
            if (hintDir.sqrMagnitude < 1e-8f) hintDir = Vector3.down;
            Vector3 hintPos = shoulder + hintDir * (cfg.HintDistanceFrac * armLen);

            int sx = Mathf.Max(1, cfg.Steps.x);
            int sy = Mathf.Max(1, cfg.Steps.y);
            int sz = Mathf.Max(1, cfg.Steps.z);

            int points = 0;
            int reachable = 0;
            double swivelShiftSum = 0.0;
            float swivelShiftMax = 0f;
            double lookupAbsSwivelSum = 0.0;
            int lookupElbowUp = 0;
            double trackerSensSum = 0.0;
            int trackerSensN = 0;
            float trackerSensMax = 0f;
            int trackerJittery = 0;   // tracker poses with sensitivity > 20 deg/cm
            int trackerFaded = 0;     // tracker poses where the hint is partially/fully faded out
            double trackerAlignSum = 0.0;
            int trackerAlignN = 0;
            float trackerAlignMax = 0f;
            int rows = 0;

            NativeArray<Vector3> table = default;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                table = new NativeArray<Vector3>(BasisArmBendLookup.GenerateDefaultTable(), Allocator.Temp);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisArmIKSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# side=" + (cfg.IsLeft ? "left" : "right") +
                                " upper=" + F(upper) + " lower=" + F(lower) +
                                " hint=(" + F(hintPos.x) + "," + F(hintPos.y) + "," + F(hintPos.z) + ")");
                    w.WriteLine("side,mode,ti,tj,tk,target_x,target_y,target_z,target_dist,arm_len," +
                                "reach_ratio,reachable,hint_on,hint_x,hint_y,hint_z," +
                                "elbow_x,elbow_y,elbow_z,hand_x,hand_y,hand_z,hand_error," +
                                "elbow_flex_deg,swivel_deg,axis_source,hint_fade,bend_x,bend_y,bend_z," +
                                "hint_proj,arm_proj,tracker_sens_deg_per_cm,tracker_align_err_deg");

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
                                Vector3 target = shoulder + new Vector3(fx, fy, fz) * armLen;
                                points++;

                                BasisArmSolveResult noHint = SolveOne(shoulder, restElbow, restHand, target, hintPos, false);
                                BasisArmSolveResult hint = SolveOne(shoulder, restElbow, restHand, target, hintPos, true);

                                Vector3 lookupBend = ComputeLookupBend(table, target - shoulder, armLen, cfg.IsLeft);
                                Vector3 lookupHintPos = shoulder + 0.5f * armLen * lookupBend;
                                BasisArmSolveResult lookup = SolveOne(shoulder, restElbow, restHand, target, lookupHintPos, true);

                                bool isReachable = noHint.ReachRatio <= 1f;
                                if (isReachable) reachable++;

                                float swivelNo = Swivel(shoulder, noHint.HandSolved, noHint.ElbowSolved);
                                float swivelHint = Swivel(shoulder, hint.HandSolved, hint.ElbowSolved);
                                float swivelLookup = Swivel(shoulder, lookup.HandSolved, lookup.ElbowSolved);

                                // Tracker jitter: how far the elbow swings per cm of tracker position error.
                                float sensHint = TrackerSensitivity(shoulder, restElbow, restHand, target, hintPos, swivelHint);
                                float sensLookup = TrackerSensitivity(shoulder, restElbow, restHand, target, lookupHintPos, swivelLookup);
                                // Tracker follow: angle between solved elbow pole and where the tracker says (0 = perfect follow).
                                float alignHint = TrackerAlignErr(shoulder, target, hint.ElbowSolved, hintPos);
                                float alignLookup = TrackerAlignErr(shoulder, target, lookup.ElbowSolved, lookupHintPos);

                                if (isReachable && !float.IsNaN(swivelNo) && !float.IsNaN(swivelHint))
                                {
                                    float shift = Mathf.Abs(Mathf.DeltaAngle(swivelNo, swivelHint));
                                    swivelShiftSum += shift;
                                    if (shift > swivelShiftMax) swivelShiftMax = shift;
                                }
                                if (isReachable)
                                {
                                    if (!float.IsNaN(swivelLookup)) lookupAbsSwivelSum += Mathf.Abs(swivelLookup);
                                    if (lookupBend.y > 0.2f) lookupElbowUp++;
                                    if (!float.IsNaN(sensHint))
                                    {
                                        trackerSensSum += sensHint;
                                        trackerSensN++;
                                        if (sensHint > trackerSensMax) trackerSensMax = sensHint;
                                        if (sensHint > 20f) trackerJittery++;
                                    }
                                    if (hint.HintFade < 0.999f) trackerFaded++;
                                    if (!float.IsNaN(alignHint))
                                    {
                                        trackerAlignSum += alignHint;
                                        trackerAlignN++;
                                        if (alignHint > trackerAlignMax) trackerAlignMax = alignHint;
                                    }
                                }

                                rows += WriteRow(w, sb, side, "nohint", i, j, k, target, armLen, isReachable, false, Vector3.zero, noHint, swivelNo, Vector3.zero, float.NaN, float.NaN);
                                rows += WriteRow(w, sb, side, "hint", i, j, k, target, armLen, isReachable, true, hintPos, hint, swivelHint, hintDir, sensHint, alignHint);
                                rows += WriteRow(w, sb, side, "lookup", i, j, k, target, armLen, isReachable, true, lookupHintPos, lookup, swivelLookup, lookupBend, sensLookup, alignLookup);
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
                summary.LookupMeanAbsSwivelDeg = reachable > 0 ? (float)(lookupAbsSwivelSum / reachable) : 0f;
                summary.LookupElbowUpCount = lookupElbowUp;
                summary.TrackerMeanSensDegPerCm = trackerSensN > 0 ? (float)(trackerSensSum / trackerSensN) : 0f;
                summary.TrackerMaxSensDegPerCm = trackerSensMax;
                summary.TrackerJitteryCount = trackerJittery;
                summary.TrackerFadedCount = trackerFaded;
                summary.TrackerMeanAlignErrDeg = trackerAlignN > 0 ? (float)(trackerAlignSum / trackerAlignN) : 0f;
                summary.TrackerMaxAlignErrDeg = trackerAlignMax;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }
            finally
            {
                if (table.IsCreated) table.Dispose();
            }

            return summary;
        }

        // Degrees of elbow swivel produced per 1 cm of lateral tracker position error at this pose.
        // High values = a tracker here will look jittery/unstable for tiny tracking noise.
        static float TrackerSensitivity(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 target, Vector3 hintPos, float baseSwivel)
        {
            if (float.IsNaN(baseSwivel)) return float.NaN;
            Vector3 ac = target - shoulder;
            if (ac.sqrMagnitude < 1e-8f) return float.NaN;
            Vector3 dir = Vector3.Cross(ac, Vector3.up);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.Cross(ac, Vector3.right);
            if (dir.sqrMagnitude < 1e-8f) return float.NaN;
            dir.Normalize();
            const float eps = 0.01f; // 1 cm
            BasisArmSolveResult p = SolveOne(shoulder, elbow, hand, target, hintPos + dir * eps, true);
            float s2 = Swivel(shoulder, p.HandSolved, p.ElbowSolved);
            if (float.IsNaN(s2)) return float.NaN;
            return Mathf.Abs(Mathf.DeltaAngle(baseSwivel, s2));
        }

        // Angle between where the elbow actually landed and where the tracker pole points,
        // both on the swing plane perpendicular to shoulder->hand. 0 = elbow follows the tracker.
        static float TrackerAlignErr(Vector3 shoulder, Vector3 target, Vector3 solvedElbow, Vector3 hintPos)
        {
            Vector3 ac = target - shoulder;
            if (ac.sqrMagnitude < 1e-8f) return float.NaN;
            ac.Normalize();
            Vector3 solvedPole = Vector3.ProjectOnPlane(solvedElbow - shoulder, ac);
            Vector3 hintPole = Vector3.ProjectOnPlane(hintPos - shoulder, ac);
            if (solvedPole.sqrMagnitude < 1e-8f || hintPole.sqrMagnitude < 1e-8f) return float.NaN;
            return Vector3.Angle(solvedPole, hintPole);
        }

        static Vector3 ComputeLookupBend(NativeArray<Vector3> table, Vector3 shoulderToHand, float armLen, bool isLeft)
        {
            Vector3 localPos = shoulderToHand / armLen;
            if (isLeft) localPos.x = -localPos.x;
            Vector3 localBend = BasisArmBendLookup.SampleTrilinear(table, localPos);
            if (isLeft) localBend.x = -localBend.x;
            return localBend.normalized;
        }

        static BasisArmSolveResult SolveOne(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 target, Vector3 hint, bool hintOn)
        {
            BasisArmSolveInput input;
            input.Shoulder = shoulder;
            input.Elbow = elbow;
            input.Hand = hand;
            input.RootRotation = Quaternion.identity; // does not affect solved positions
            input.MidRotation = Quaternion.identity;
            input.TargetPosition = target;
            input.TargetRotation = Quaternion.identity;
            input.HintPosition = hint;
            input.HintWeight = hintOn;
            input.TargetOffset = Quaternion.identity;
            input.PlayerUp = Vector3.up;
            input.HintMaxStepDeg = float.MaxValue;
            BasisArmSolveCore.Solve(input, out BasisArmSolveResult r);
            return r;
        }

        // Signed elbow swivel around the shoulder->hand axis, measured from straight-down.
        // This is the headline "is the elbow where a human's would be" metric.
        static float Swivel(Vector3 shoulder, Vector3 hand, Vector3 elbow)
        {
            Vector3 axis = hand - shoulder;
            if (axis.sqrMagnitude < 1e-8f) return float.NaN;
            axis.Normalize();
            Vector3 refVec = Vector3.ProjectOnPlane(Vector3.down, axis);
            Vector3 poleVec = Vector3.ProjectOnPlane(elbow - shoulder, axis);
            if (refVec.sqrMagnitude < 1e-8f || poleVec.sqrMagnitude < 1e-8f) return float.NaN;
            return Vector3.SignedAngle(refVec, poleVec, axis);
        }

        static int WriteRow(StreamWriter w, StringBuilder sb, string side, string mode, int i, int j, int k,
            Vector3 target, float armLen, bool reachable, bool hintOn, Vector3 hint, BasisArmSolveResult r, float swivel, Vector3 bendDir, float trackerSens, float trackerAlign)
        {
            sb.Clear();
            sb.Append(side).Append(',').Append(mode).Append(',');
            sb.Append(i).Append(',').Append(j).Append(',').Append(k).Append(',');
            Append(sb, target.x); Append(sb, target.y); Append(sb, target.z);
            Append(sb, r.TargetDistance); Append(sb, armLen);
            Append(sb, r.ReachRatio);
            sb.Append(reachable ? '1' : '0').Append(',');
            sb.Append(hintOn ? '1' : '0').Append(',');
            Append(sb, hint.x); Append(sb, hint.y); Append(sb, hint.z);
            Append(sb, r.ElbowSolved.x); Append(sb, r.ElbowSolved.y); Append(sb, r.ElbowSolved.z);
            Append(sb, r.HandSolved.x); Append(sb, r.HandSolved.y); Append(sb, r.HandSolved.z);
            Append(sb, r.HandError);
            Append(sb, r.ElbowAngleDeg);
            Append(sb, swivel);
            sb.Append(r.AxisSource).Append(',');
            Append(sb, r.HintFade);
            Append(sb, bendDir.x); Append(sb, bendDir.y); Append(sb, bendDir.z);
            Append(sb, r.HintProjMag); Append(sb, r.ArmProjMag); Append(sb, trackerSens); AppendLast(sb, trackerAlign);
            w.WriteLine(sb.ToString());
            return 1;
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }
        static void AppendLast(StringBuilder sb, float v) { sb.Append(F(v)); }

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
