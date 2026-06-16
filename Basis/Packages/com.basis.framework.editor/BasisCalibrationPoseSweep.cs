using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Offline proof that calibration is POSE-TOLERANT. For each limb chain (arm/leg, both sides) it
    // models a player who calibrates at a spread of elbow/knee BEND angles, with a tracker rigidly on
    // the limb, and measures -- using the real BasisCalibrationMath offset round-trip -- whether the
    // pose-matched compensation (BasisCalibrationPoseCore) keeps the result the same as a straight
    // calibration, where the old T-pose reference drifts with the bend.
    public struct BasisCalibrationPoseSweepConfig
    {
        public int BendSteps;        // calibration bend samples from straight to folded
        public float MinBendDeg;     // most-folded interior elbow/knee angle tested
        public float MaxBendDeg;     // straight

        public static BasisCalibrationPoseSweepConfig Default()
        {
            return new BasisCalibrationPoseSweepConfig { BendSteps = 31, MinBendDeg = 30f, MaxBendDeg = 180f };
        }
    }

    public struct BasisCalibrationPoseSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Rows;

        public float ScaleMaxLimbDeviationMm;   // |bend-invariant length - true length| (must be ~0)
        public float ScaleWorstBentShortfallMm; // how far wrist-to-wrist underestimates at max bend (the bug)

        public float CalibMaxCompErrMm;         // avatar end placed at the calibration pose, compensated (~0)
        public float CalibMaxUncompErrMm;       // same, T-pose reference (grows with bend -- the bug)

        public float RuntimeMaxCompErrMm;       // calibrate bent, straighten at runtime: compensated tracking error
        public float RuntimeMaxUncompErrMm;     // same, uncompensated
        public float RuntimeImprovementFactor;  // min over poses of uncomp/comp (>1 = compensation helps)

        public float QualMidStraight, QualMidBent;     // quality with a mid tracker (stays high)
        public float QualNoMidStraight, QualNoMidBent; // quality without one (falls with bend)
        public string Error;
    }

    public static class BasisCalibrationPoseSweep
    {
        public const string DefaultFileName = "BasisCalibrationPoseSweep.csv";
        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        struct Chain
        {
            public string Name;
            public Vector3 AvatarRoot, PlayerRoot;
            public Vector3 TposeDir;          // straight-limb direction in T-pose (out to the side / straight down)
            public float AvatarUpper, AvatarLower, PlayerUpper, PlayerLower;
        }

        static Chain[] Chains(bool isLeft)
        {
            float m = isLeft ? -1f : 1f;
            return new[]
            {
                new Chain
                {
                    Name = isLeft ? "armL" : "armR",
                    AvatarRoot = new Vector3(0.16f * m, 1.40f, 0f), PlayerRoot = new Vector3(0.18f * m, 1.43f, 0.02f),
                    TposeDir = new Vector3(m, 0f, 0f),
                    AvatarUpper = 0.28f, AvatarLower = 0.26f, PlayerUpper = 0.30f, PlayerLower = 0.27f,
                },
                new Chain
                {
                    Name = isLeft ? "legL" : "legR",
                    AvatarRoot = new Vector3(0.10f * m, 0.92f, 0f), PlayerRoot = new Vector3(0.11f * m, 0.94f, 0.01f),
                    TposeDir = new Vector3(0f, -1f, 0f),
                    AvatarUpper = 0.42f, AvatarLower = 0.40f, PlayerUpper = 0.45f, PlayerLower = 0.43f,
                },
            };
        }

        public static BasisCalibrationPoseSweepSummary Run(BasisCalibrationPoseSweepConfig cfg, string path)
        {
            var s = new BasisCalibrationPoseSweepSummary { Ok = false, Path = path };
            int steps = Mathf.Max(2, cfg.BendSteps);

            float scaleDevMax = 0f, scaleShortfallMax = 0f;
            float calibComp = 0f, calibUncomp = 0f;
            float runComp = 0f, runUncomp = 0f, improveMin = float.PositiveInfinity;
            float qMidStraight = 0f, qMidBent = 1f, qNoMidStraight = 0f, qNoMidBent = 1f;
            int rows = 0;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisCalibrationPoseSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("chain,side,has_mid,bend_deg,true_len,bend_invariant_len,straight_line_len," +
                                "calib_err_comp_mm,calib_err_uncomp_mm,runtime_err_comp_mm,runtime_err_uncomp_mm,quality");

                    var sb = new StringBuilder(192);

                    foreach (bool isLeft in new[] { false, true })
                    foreach (var c in Chains(isLeft))
                    {
                        string side = isLeft ? "L" : "R";
                        Vector3 tposeDir = c.TposeDir.normalized;

                        // Avatar straight T-pose bone poses.
                        Vector3 aMidT = c.AvatarRoot + tposeDir * c.AvatarUpper;
                        Vector3 aEndT = aMidT + tposeDir * c.AvatarLower;
                        Quaternion aMidRotT = SafeLook(tposeDir);
                        Quaternion aEndRotT = SafeLook(tposeDir);

                        float trueLen = c.PlayerUpper + c.PlayerLower;
                        Vector3 bendAxis = BendAxis(tposeDir);

                        foreach (bool hasMid in new[] { true, false })
                        {
                            for (int b = 0; b < steps; b++)
                            {
                                float bendDeg = Mathf.Lerp(cfg.MaxBendDeg, cfg.MinBendDeg, b / (float)(steps - 1));

                                // Player calibration pose: upper arm along the intended T-pose direction,
                                // forearm folded by (180 - bend) about the bend axis.
                                Vector3 upperDir = tposeDir;
                                Vector3 lowerDir = Quaternion.AngleAxis(180f - bendDeg, bendAxis) * upperDir;
                                Vector3 pMid = c.PlayerRoot + upperDir * c.PlayerUpper;
                                Vector3 pEnd = pMid + lowerDir * c.PlayerLower;
                                Vector3 trackerPos = pEnd;
                                Quaternion trackerRot = SafeLook(lowerDir);

                                BasisLimbCalibrationInput input;
                                input.PlayerRoot = c.PlayerRoot; input.PlayerMid = pMid; input.PlayerEnd = pEnd;
                                input.HasMid = hasMid;
                                input.AvatarRoot = c.AvatarRoot; input.AvatarMidTpose = aMidT; input.AvatarEndTpose = aEndT;
                                input.AvatarMidRotTpose = aMidRotT; input.AvatarEndRotTpose = aEndRotT;
                                input.AvatarUpperLen = c.AvatarUpper; input.AvatarLowerLen = c.AvatarLower;
                                BasisCalibrationPoseCore.Solve(input, out BasisLimbCalibrationResult r);

                                // 1) Scale: the bend-invariant length must equal the true limb length at
                                //    every bend; the straight-line (wrist-to-wrist style) length shrinks.
                                float scaleDev = Mathf.Abs(r.PlayerLimbLength - trueLen) * 1000f;
                                float shortfall = (trueLen - r.StraightLineDist) * 1000f;
                                if (scaleDev > scaleDevMax) scaleDevMax = scaleDev;
                                if (shortfall > scaleShortfallMax) scaleShortfallMax = shortfall;

                                // 2) Offset at the calibration pose. The avatar end SHOULD mirror the
                                //    player (AvatarEndMatched). Compensated lands there; the T-pose
                                //    reference lands at the straight hand, off by the bend.
                                Vector3 idealEnd = r.AvatarEndMatched;
                                BasisCalibrationPoseCore.CaptureEndOffset(true, trackerPos, trackerRot, r, input, out var offCpos, out var offCrot);
                                BasisCalibrationPoseCore.CaptureEndOffset(false, trackerPos, trackerRot, r, input, out var offUpos, out var offUrot);
                                BasisCalibrationMath.ApplyInverseOffset(trackerPos, trackerRot, offCpos, offCrot, out var landCcal, out _);
                                BasisCalibrationMath.ApplyInverseOffset(trackerPos, trackerRot, offUpos, offUrot, out var landUcal, out _);
                                float calibErrC = (landCcal - idealEnd).magnitude * 1000f;
                                float calibErrU = (landUcal - idealEnd).magnitude * 1000f;
                                if (hasMid)
                                {
                                    if (calibErrC > calibComp) calibComp = calibErrC;
                                    if (calibErrU > calibUncomp) calibUncomp = calibErrU;
                                }

                                // 3) Runtime: keep the calibration offset, straighten the limb. The avatar
                                //    end should track to the straightened mirror; measure how far each lands.
                                Vector3 lowerRun = upperDir; // straightened
                                Vector3 pEndRun = pMid + lowerRun * c.PlayerLower;
                                Quaternion trackerRotRun = SafeLook(lowerRun);
                                Vector3 idealEndRun = c.AvatarRoot + tposeDir * (c.AvatarUpper + c.AvatarLower);
                                BasisCalibrationMath.ApplyInverseOffset(pEndRun, trackerRotRun, offCpos, offCrot, out var landCrun, out _);
                                BasisCalibrationMath.ApplyInverseOffset(pEndRun, trackerRotRun, offUpos, offUrot, out var landUrun, out _);
                                float runErrC = (landCrun - idealEndRun).magnitude * 1000f;
                                float runErrU = (landUrun - idealEndRun).magnitude * 1000f;
                                if (hasMid)
                                {
                                    if (runErrC > runComp) runComp = runErrC;
                                    if (runErrU > runUncomp) runUncomp = runErrU;
                                    // Improvement over realistic calibration bends. Near straight (>170)
                                    // both errors are ~0 (noisy ratio); past a right angle (<75) the
                                    // inverse-offset articulation limit dominates BOTH equally, so the
                                    // ratio is not the story there -- the absolute runtime error is.
                                    if (runErrC > 1f && bendDeg < 170f && bendDeg > 75f)
                                    {
                                        float improve = runErrU / Mathf.Max(runErrC, 1e-3f);
                                        if (improve < improveMin) improveMin = improve;
                                    }
                                }

                                // 4) Quality score vs bend, with and without the mid tracker.
                                bool straight = bendDeg > 175f;
                                bool bent = bendDeg < cfg.MinBendDeg + 5f;
                                if (hasMid)
                                {
                                    if (straight) qMidStraight = r.QualityScore;
                                    if (bent) qMidBent = r.QualityScore;
                                }
                                else
                                {
                                    if (straight) qNoMidStraight = r.QualityScore;
                                    if (bent) qNoMidBent = r.QualityScore;
                                }

                                sb.Clear();
                                sb.Append(c.Name).Append(',').Append(side).Append(',').Append(hasMid ? '1' : '0').Append(',');
                                Append(sb, bendDeg); Append(sb, trueLen); Append(sb, r.PlayerLimbLength); Append(sb, r.StraightLineDist);
                                Append(sb, calibErrC); Append(sb, calibErrU); Append(sb, runErrC); Append(sb, runErrU);
                                sb.Append(F(r.QualityScore));
                                w.WriteLine(sb.ToString());
                                rows++;
                            }
                        }
                    }
                }

                s.Rows = rows;
                s.ScaleMaxLimbDeviationMm = scaleDevMax;
                s.ScaleWorstBentShortfallMm = scaleShortfallMax;
                s.CalibMaxCompErrMm = calibComp;
                s.CalibMaxUncompErrMm = calibUncomp;
                s.RuntimeMaxCompErrMm = runComp;
                s.RuntimeMaxUncompErrMm = runUncomp;
                s.RuntimeImprovementFactor = float.IsPositiveInfinity(improveMin) ? 0f : improveMin;
                s.QualMidStraight = qMidStraight; s.QualMidBent = qMidBent;
                s.QualNoMidStraight = qNoMidStraight; s.QualNoMidBent = qNoMidBent;
                s.Ok = true;
            }
            catch (System.Exception e)
            {
                s.Ok = false;
                s.Error = e.Message;
            }

            return s;
        }

        static Vector3 BendAxis(Vector3 dir)
        {
            Vector3 axis = Vector3.Cross(dir, Vector3.forward);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dir, Vector3.up);
            return axis.normalized;
        }

        static Quaternion SafeLook(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir.normalized, up);
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
