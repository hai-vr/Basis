using System.Globalization;
using System.IO;
using System.Text;
using Basis.Scripts.Device_Management.Devices.Pairing;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Offline sweep for the MULTI-TRACKER ("double hip") rotation fusion -- two physical trackers strapped
    // to the pelvis fused into one virtual hip by BasisVirtualMidpointInput. After the FBIK rotation
    // calibration moved from a fresh head-driven offset to a captured AnimatorRoot-anchored reference, the
    // fused hip rotation reads wrong. The algebra says why the sweep is shaped the way it is: on one avatar
    // the calibrated hip is
    //     driven(now) = deviceRot(now) * Inverse(deviceRot(cal)) * C
    // where C (the root/bind term) is IDENTICAL for the fused pair and for a single tracker, so it cancels
    // out of the pair-vs-single error. What's left is purely a property of the fusion:
    //     err = Angle( Fuse(now) * Inverse(Fuse(cal)) ,  R * Inverse(R_cal) )
    // i.e. "does the fused rotation, relative to its value at calibration, equal the body's rotation relative
    // to calibration." A single rigid tracker satisfies this exactly (its mounting constant cancels); a clean
    // rigid fusion does too. It breaks when the half-rest offset the fusion captured at calibration is not the
    // half-rest it uses at runtime -- which is exactly what a device-list-churn recreate does: the pairing
    // service tears down and re-Initializes the virtual (re-priming _halfRestOffset) at whatever flexed pose
    // the user is in, AFTER calibration already captured Fuse(cal). The calibration change didn't move the
    // math; it removed the per-frame fresh recompute that used to hide the re-prime.
    //
    // The sweep enumerates body orientations x blend ratios and scores that err for several fusion
    // CONVENTIONS, so the data names the robust one. Pure quaternion math on the real
    // BasisMidpointRotationFusion -- the exact code the live pair runs -- no scene, no avatar.
    public enum BasisMidpointFusionConvention
    {
        ProjectedReprime = 0,  // shipping math, half-rest re-primed at a flexed pose after calibration (the failure)
        ProjectedStable = 1,   // shipping math, half-rest captured once at calibration and never re-primed
        NaiveSlerp = 2,        // Slerp(a,b,t) with no rest-offset projection (regression baseline)
        PartnerAOnly = 3,      // drive rotation from one tracker; the other only contributes position
        AveragedHalfRest = 4,  // half-rest from a flex-averaged delta, captured once
    }

    public struct BasisMultiTrackerRotationConfig
    {
        public int YawSteps;        // body yaw samples over [-180,180)
        public int PitchSteps;      // body pitch samples over [-PitchMaxDeg, PitchMaxDeg]
        public int RollSteps;       // body roll samples over [-RollMaxDeg, RollMaxDeg]
        public float PitchMaxDeg;
        public float RollMaxDeg;

        public Vector3 CalEuler;    // body orientation at the moment calibration captured Fuse(cal)
        public Vector3 RePrimeEuler;// body orientation at the moment a device-churn recreate re-primed the half-rest
        public float MountYawDeg;   // A canted +half / B canted -half about up (trackers on opposite hip sides)
        public float MountRollDeg;  // A canted +half / B canted -half about forward
        public float FlexDeg;       // relative slop between the two straps (per-frame, and at the re-prime instant)

        public int TSteps;          // confidence-blend ratios sampled over [0,1]
        public float TCap;          // blend ratio in effect when calibration captured Fuse(cal)
        public float OnsetThresholdDeg; // err above this counts as "visibly wrong" for the onset metric
        public int AvgPrimeSamples; // flexed draws averaged for the AveragedHalfRest convention

        public static BasisMultiTrackerRotationConfig Default()
        {
            return new BasisMultiTrackerRotationConfig
            {
                YawSteps = 36,
                PitchSteps = 5,
                RollSteps = 5,
                PitchMaxDeg = 50f,
                RollMaxDeg = 40f,
                CalEuler = Vector3.zero,
                RePrimeEuler = new Vector3(10f, 35f, 8f),
                MountYawDeg = 24f,
                MountRollDeg = 16f,
                FlexDeg = 6f,
                TSteps = 5,
                TCap = 0.5f,
                OnsetThresholdDeg = 5f,
                AvgPrimeSamples = 8,
            };
        }
    }

    public struct BasisMultiTrackerRotationSummary
    {
        public bool Ok;
        public string Path;
        public string Error;
        public int Rows;

        public string[] ConvNames;
        public float[] RigidWorstErrDeg;   // worst err with rigid runtime trackers (isolates a mis-primed half-rest)
        public float[] FlexWorstErrDeg;    // worst err with per-frame strap flex added on top
        public float[] MeanErrDeg;         // mean err across the flexed sweep
        public float[] TSpreadDeg;         // worst spread of err across the blend ratios at a fixed pose (t-variance)
        public float[] OnsetYawDeg;        // smallest body yaw from calibration where err crosses the threshold (NaN = never)

        public int BestConvIndex;          // lowest worst err overall -- the recommended convention
        public int CurrentConvIndex;       // the shipping behavior under device churn (ProjectedReprime)
        public int StableConvIndex;        // ProjectedStable -- its rigid worst is the projection-math regression guard
    }

    // Result of the TEMPORAL sim: synthetic body-motion trajectories driven frame-by-frame through the REAL
    // stateful fusion (BasisMidpointFusionCore). This is what the static convention sweep above cannot see --
    // the lag, rubber-band, and blend-snap the per-frame EMAs / smoothed weights / rotation low-pass produce.
    public struct BasisMultiTrackerRotationTemporalSummary
    {
        public bool Ok;
        public string Path;
        public string Error;
        public int Frames;

        public string[] TrajNames;
        public float[] MaxTrackErrDeg;      // fused rotation vs ideal rigid body tracking (a single tracker is ~0 here)
        public float[] MaxLagDeg;           // worst yaw the fused output trails the body (low-pass lag)
        public float[] MaxOvershootDeg;     // worst yaw the fused output leads the body (rubber-band / overshoot)
        public float[] Max2ndDiffDeg;       // worst frame-to-frame change in step size (a snap / roughness)
        public float[] MaxBlendDevFromHalf; // worst |t - 0.5| -- how far the confidence blend swings during motion
        public float[] SettleFrames;        // frames after motion stops for the error to fall under 1 deg (rubber-band tail)

        public float WorstTrackErrDeg;
        public float WorstSnapDeg;
        public float WorstOvershootDeg;
    }

    public static class BasisMultiTrackerRotationSweep
    {
        public const string DefaultFileName = "BasisMultiTrackerRotationSweep.csv";

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public const string DefaultTemporalFileName = "BasisMultiTrackerRotationTemporal.csv";

        public static string DefaultTemporalPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, DefaultTemporalFileName);
        }

        // Drive synthetic body-motion trajectories frame-by-frame through the REAL stateful fusion (the same
        // BasisMidpointFusionCore.Step the live pair runs) -- no scene, no play mode. Two trackers mounted on
        // opposite hip sides swing through an arc as the body turns (so the surprise/confidence system sees
        // real positional velocity), with optional per-frame strap flex. Each trajectory ends in a hold so the
        // settle (rubber-band) tail is measurable. The fused output is compared to ideal rigid body tracking:
        // a single tracker would read ~0 error, so whatever shows here is the lag / snap the pairing adds.
        public static BasisMultiTrackerRotationTemporalSummary RunTemporal(
            BasisMultiTrackerRotationConfig cfg, BasisMidpointFusionTunables tun, float dt, float flexDeg, string path)
        {
            var summary = new BasisMultiTrackerRotationTemporalSummary { Ok = false, Path = path };
            dt = Mathf.Max(dt, 1e-4f);

            Quaternion Ma = Quaternion.Euler(0f, cfg.MountYawDeg * 0.5f, cfg.MountRollDeg * 0.5f);
            Quaternion Mb = Quaternion.Euler(0f, -cfg.MountYawDeg * 0.5f, -cfg.MountRollDeg * 0.5f);
            // Local mount points offset from the spin axis, so a turn swings them through an arc and the
            // surprise system sees the positional velocity a real turn produces.
            Vector3 offA = new Vector3(-0.12f, 0f, 0f);
            Vector3 offB = new Vector3(0.12f, 0f, 0f);

            string[] names = { "turn90-fast", "turn180-med", "oscillate-60", "snap-there-back" };
            const int hold = 90;

            var trackErr = new float[names.Length];
            var lagDeg = new float[names.Length];
            var overDeg = new float[names.Length];
            var d2Deg = new float[names.Length];
            var blendDev = new float[names.Length];
            var settle = new float[names.Length];
            int frames = 0;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisMultiTrackerRotationTemporal " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# rotHalfLife=" + F(tun.RotationHalfLife) + " flex=" + F(flexDeg) + " dt=" + F(dt) +
                                " mountYaw=" + F(cfg.MountYawDeg) + " mountRoll=" + F(cfg.MountRollDeg));
                    w.WriteLine("traj,frame,yaw,body_yaw,fused_yaw,track_err,lag,step_jump,t,wA,wB");
                    var sb = new StringBuilder(160);

                    for (int ti = 0; ti < names.Length; ti++)
                    {
                        var yawAt = MakeYaw(names[ti], out int motion);
                        int total = motion + hold;

                        var state = BasisMidpointFusionState.Fresh();
                        var rng = new System.Random(5000 + ti);
                        Quaternion fusedCalInv = Quaternion.identity, RcalInv = Quaternion.identity, prevErrQ = Quaternion.identity;
                        bool haveCal = false;
                        int settleFrame = -1;

                        for (int f = 0; f < total; f++)
                        {
                            float yaw = yawAt(Mathf.Min(f, motion)); // hold the final yaw through the tail
                            Quaternion R = Quaternion.Euler(0f, yaw, 0f);
                            Quaternion flexA = RandomFlex(rng, flexDeg);
                            Quaternion flexB = RandomFlex(rng, flexDeg);

                            Vector3 aPos = R * offA;
                            Vector3 bPos = R * offB;
                            Quaternion aRot = R * Ma * flexA;
                            Quaternion bRot = R * Mb * flexB;

                            BasisMidpointFusionCore.Step(ref state, aPos, bPos, aRot, bRot, tun, dt,
                                out _, out Quaternion fused, out float wA, out float wB, out float t);

                            if (!haveCal)
                            {
                                fusedCalInv = Quaternion.Inverse(fused);
                                RcalInv = Quaternion.Inverse(R);
                                haveCal = true;
                            }

                            Quaternion fusedDelta = fused * fusedCalInv;
                            Quaternion bodyDelta = R * RcalInv;
                            // The fusion's deviation from ideal rigid tracking. A clean tracker holds this at
                            // identity through the body's own accelerations; only the fusion moves it.
                            Quaternion errQ = fusedDelta * Quaternion.Inverse(bodyDelta);
                            float err = Quaternion.Angle(errQ, Quaternion.identity);
                            float fusedYaw = YawOf(fusedDelta);
                            float bodyYaw = YawOf(bodyDelta);
                            float lag = Mathf.DeltaAngle(fusedYaw, bodyYaw); // + = fused behind body (lag); - = fused ahead (overshoot)

                            // Snap = how much that deviation moved this frame. Invariant to the body's own motion
                            // profile (a perfect tracker reads 0 even on a hard start/stop), so it flags only
                            // fusion-introduced discontinuities -- a tB-swing pop, a hemisphere flip, etc.
                            float jump = f >= 2 ? Quaternion.Angle(prevErrQ, errQ) : 0f;
                            prevErrQ = errQ;

                            trackErr[ti] = Mathf.Max(trackErr[ti], err);
                            if (lag > lagDeg[ti]) lagDeg[ti] = lag;
                            if (-lag > overDeg[ti]) overDeg[ti] = -lag;
                            d2Deg[ti] = Mathf.Max(d2Deg[ti], jump);
                            blendDev[ti] = Mathf.Max(blendDev[ti], Mathf.Abs(t - 0.5f));
                            if (f >= motion && settleFrame < 0 && err < 1f) settleFrame = f - motion;

                            sb.Clear();
                            sb.Append(names[ti]).Append(',').Append(f).Append(',');
                            Append(sb, yaw); Append(sb, bodyYaw); Append(sb, fusedYaw);
                            Append(sb, err); Append(sb, lag); Append(sb, jump); Append(sb, t); Append(sb, wA);
                            sb.Append(F(wB));
                            w.WriteLine(sb.ToString());
                            frames++;
                        }
                        settle[ti] = settleFrame < 0 ? hold : settleFrame;
                    }
                }

                float worstErr = 0f, worstD2 = 0f, worstOver = 0f;
                for (int i = 0; i < names.Length; i++)
                {
                    worstErr = Mathf.Max(worstErr, trackErr[i]);
                    worstD2 = Mathf.Max(worstD2, d2Deg[i]);
                    worstOver = Mathf.Max(worstOver, overDeg[i]);
                }

                summary.Ok = true;
                summary.Frames = frames;
                summary.TrajNames = names;
                summary.MaxTrackErrDeg = trackErr;
                summary.MaxLagDeg = lagDeg;
                summary.MaxOvershootDeg = overDeg;
                summary.Max2ndDiffDeg = d2Deg;
                summary.MaxBlendDevFromHalf = blendDev;
                summary.SettleFrames = settle;
                summary.WorstTrackErrDeg = worstErr;
                summary.WorstSnapDeg = worstD2;
                summary.WorstOvershootDeg = worstOver;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        // yaw(frame) in degrees for each named trajectory, plus how many frames the motion lasts (the rest is hold).
        static System.Func<int, float> MakeYaw(string name, out int motionFrames)
        {
            switch (name)
            {
                case "turn90-fast":
                    motionFrames = 23; // ~0.25s @ 90fps -- the fast turn that rubber-bands a low-pass
                    return f => Mathf.Lerp(0f, 90f, Mathf.Clamp01(f / 23f));
                case "turn180-med":
                    motionFrames = 90; // ~1s
                    return f => Mathf.Lerp(0f, 180f, Mathf.Clamp01(f / 90f));
                case "oscillate-60":
                    motionFrames = 270; // ~3s, 2 cycles of +/-60 deg
                    return f => 60f * Mathf.Sin(2f * Mathf.PI * f / 135f);
                default: // snap-there-back: fast out to 90, hold, fast back
                    motionFrames = 60;
                    return f =>
                    {
                        if (f < 15) return Mathf.Lerp(0f, 90f, f / 15f);
                        if (f < 30) return 90f;
                        if (f < 45) return Mathf.Lerp(90f, 0f, (f - 30) / 15f);
                        return 0f;
                    };
            }
        }

        static float YawOf(Quaternion q)
        {
            Vector3 fwd = q * Vector3.forward;
            if (fwd.x * fwd.x + fwd.z * fwd.z < 1e-8f) return 0f;
            return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        static readonly string[] k_ConvNames =
        {
            "projected (current, re-prime)",
            "projected (stable prime)",
            "naive slerp (no projection)",
            "partner A only",
            "averaged half-rest (stable)",
        };

        public static BasisMultiTrackerRotationSummary Run(BasisMultiTrackerRotationConfig cfg, string path)
        {
            var summary = new BasisMultiTrackerRotationSummary { Ok = false, Path = path };

            int convN = k_ConvNames.Length;
            int yawN = Mathf.Max(1, cfg.YawSteps);
            int pitchN = Mathf.Max(1, cfg.PitchSteps);
            int rollN = Mathf.Max(1, cfg.RollSteps);
            int tN = Mathf.Max(1, cfg.TSteps);

            // Mounting: the two trackers sit canted to opposite hip sides. M_mid (the geodesic midpoint a
            // single tracker would report) cancels from the error, so it never appears below -- only Ma/Mb,
            // which set what the fusion has to reconcile.
            Quaternion Ma = Quaternion.Euler(0f, cfg.MountYawDeg * 0.5f, cfg.MountRollDeg * 0.5f);
            Quaternion Mb = Quaternion.Euler(0f, -cfg.MountYawDeg * 0.5f, -cfg.MountRollDeg * 0.5f);

            Quaternion Rcal = Quaternion.Euler(cfg.CalEuler);
            Quaternion RcalInv = Quaternion.Inverse(Rcal);
            Quaternion Rreprime = Quaternion.Euler(cfg.RePrimeEuler);

            // The half-rest each convention uses at runtime (hrRun) and the one in effect when calibration
            // captured the pose (hrCap). Only the projected family reads a half-rest; naive / A-only ignore it.
            var hrCap = new Quaternion[convN];
            var hrRun = new Quaternion[convN];
            for (int c = 0; c < convN; c++) { hrCap[c] = Quaternion.identity; hrRun[c] = Quaternion.identity; }

            Quaternion aCalClean = Rcal * Ma;
            Quaternion bCalClean = Rcal * Mb;
            Quaternion cleanHalfRest = BasisMidpointRotationFusion.ComputeHalfRestOffset(aCalClean, bCalClean);

            // ProjectedStable: primed once at calibration, clean.
            hrCap[(int)BasisMidpointFusionConvention.ProjectedStable] = cleanHalfRest;
            hrRun[(int)BasisMidpointFusionConvention.ProjectedStable] = cleanHalfRest;

            // ProjectedReprime: calibration saw the clean half-rest, but a later churn recreate re-primed it at
            // a flexed pose -- that wrong half-rest is what every runtime frame then uses.
            hrCap[(int)BasisMidpointFusionConvention.ProjectedReprime] = cleanHalfRest;
            {
                var rng = new System.Random(777);
                Quaternion fa = RandomFlex(rng, cfg.FlexDeg);
                Quaternion fb = RandomFlex(rng, cfg.FlexDeg);
                hrRun[(int)BasisMidpointFusionConvention.ProjectedReprime] =
                    BasisMidpointRotationFusion.ComputeHalfRestOffset(Rreprime * Ma * fa, Rreprime * Mb * fb);
            }

            // AveragedHalfRest: averaged over several flexed draws at calibration, captured once.
            {
                var rng = new System.Random(4242);
                Quaternion acc = cleanHalfRest;
                int avgN = Mathf.Max(1, cfg.AvgPrimeSamples);
                for (int i = 0; i < avgN; i++)
                {
                    Quaternion fa = RandomFlex(rng, cfg.FlexDeg);
                    Quaternion fb = RandomFlex(rng, cfg.FlexDeg);
                    Quaternion s = BasisMidpointRotationFusion.ComputeHalfRestOffset(aCalClean * fa, bCalClean * fb);
                    if (Quaternion.Dot(acc, s) < 0f) s = new Quaternion(-s.x, -s.y, -s.z, -s.w);
                    acc = Quaternion.Slerp(acc, s, 1f / (i + 2));
                }
                hrCap[(int)BasisMidpointFusionConvention.AveragedHalfRest] = acc;
                hrRun[(int)BasisMidpointFusionConvention.AveragedHalfRest] = acc;
            }

            // Fuse(cal) per convention -- captured once at the calibration pose with the cap half-rest and the
            // cap blend ratio. Fixed for the whole sweep (independent of the runtime orientation R).
            var fuseCapInv = new Quaternion[convN];
            for (int c = 0; c < convN; c++)
            {
                Quaternion fc = Mid((BasisMidpointFusionConvention)c, aCalClean, bCalClean, cfg.TCap, hrCap[c]);
                fuseCapInv[c] = Quaternion.Inverse(fc);
            }

            var rigidWorst = new float[convN];
            var flexWorst = new float[convN];
            var meanAcc = new double[convN];
            var tSpread = new float[convN];
            var onsetYaw = new float[convN];
            for (int c = 0; c < convN; c++) { onsetYaw[c] = float.NaN; }
            long meanCount = 0;
            int rows = 0;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisMultiTrackerRotationSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# mountYaw=" + F(cfg.MountYawDeg) + " mountRoll=" + F(cfg.MountRollDeg) +
                                " flex=" + F(cfg.FlexDeg) + " cal=(" + F(cfg.CalEuler.x) + "," + F(cfg.CalEuler.y) + "," + F(cfg.CalEuler.z) + ")" +
                                " reprime=(" + F(cfg.RePrimeEuler.x) + "," + F(cfg.RePrimeEuler.y) + "," + F(cfg.RePrimeEuler.z) + ")" +
                                " tCap=" + F(cfg.TCap) + " onsetThresh=" + F(cfg.OnsetThresholdDeg));
                    w.WriteLine("conv,conv_idx,yaw,pitch,roll,t,err_rigid_deg,err_flex_deg,yaw_from_cal");

                    var sb = new StringBuilder(160);
                    int sampleIndex = 0;

                    for (int yi = 0; yi < yawN; yi++)
                    {
                        float yaw = (yawN <= 1) ? cfg.CalEuler.y : Mathf.Lerp(-180f, 180f, yi / (float)yawN); // [-180,180)
                        float yawFromCal = Mathf.Abs(Mathf.DeltaAngle(cfg.CalEuler.y, yaw));
                        for (int pi = 0; pi < pitchN; pi++)
                        {
                            float pitch = (pitchN <= 1) ? 0f : Mathf.Lerp(-cfg.PitchMaxDeg, cfg.PitchMaxDeg, pi / (float)(pitchN - 1));
                            for (int ri = 0; ri < rollN; ri++)
                            {
                                float roll = (rollN <= 1) ? 0f : Mathf.Lerp(-cfg.RollMaxDeg, cfg.RollMaxDeg, ri / (float)(rollN - 1));

                                Quaternion R = Quaternion.Euler(pitch, yaw, roll);
                                Quaternion bodyDelta = R * RcalInv; // the rotation the hip should reproduce

                                Quaternion aR = R * Ma;
                                Quaternion bR = R * Mb;

                                // One strap-flex draw per pose, reused across the blend ratios (flex is a
                                // physical tracker state, not a function of the confidence blend).
                                var rng = new System.Random(sampleIndex * 97 + 12345);
                                Quaternion flexA = RandomFlex(rng, cfg.FlexDeg);
                                Quaternion flexB = RandomFlex(rng, cfg.FlexDeg);
                                Quaternion aRf = aR * flexA;
                                Quaternion bRf = bR * flexB;

                                for (int c = 0; c < convN; c++)
                                {
                                    var conv = (BasisMidpointFusionConvention)c;
                                    float tMin = float.PositiveInfinity, tMax = float.NegativeInfinity;

                                    for (int ti = 0; ti < tN; ti++)
                                    {
                                        float t = (tN <= 1) ? 0.5f : ti / (float)(tN - 1);

                                        Quaternion midRigid = Mid(conv, aR, bR, t, hrRun[c]);
                                        Quaternion midFlex = Mid(conv, aRf, bRf, t, hrRun[c]);

                                        float errRigid = Quaternion.Angle(midRigid * fuseCapInv[c], bodyDelta);
                                        float errFlex = Quaternion.Angle(midFlex * fuseCapInv[c], bodyDelta);

                                        if (errRigid > rigidWorst[c]) rigidWorst[c] = errRigid;
                                        if (errFlex > flexWorst[c]) flexWorst[c] = errFlex;
                                        if (errRigid < tMin) tMin = errRigid;
                                        if (errRigid > tMax) tMax = errRigid;
                                        meanAcc[c] += errFlex; meanCount++;
                                        if (errFlex > cfg.OnsetThresholdDeg && (float.IsNaN(onsetYaw[c]) || yawFromCal < onsetYaw[c]))
                                            onsetYaw[c] = yawFromCal;

                                        sb.Clear();
                                        sb.Append(k_ConvNames[c]).Append(',').Append(c).Append(',');
                                        Append(sb, yaw); Append(sb, pitch); Append(sb, roll); Append(sb, t);
                                        Append(sb, errRigid); Append(sb, errFlex);
                                        sb.Append(F(yawFromCal));
                                        w.WriteLine(sb.ToString());
                                        rows++;
                                    }

                                    float spread = (tMax >= tMin) ? (tMax - tMin) : 0f;
                                    if (spread > tSpread[c]) tSpread[c] = spread;
                                }

                                sampleIndex++;
                            }
                        }
                    }
                }

                // Rank by SYSTEMATIC error (rigid worst) -- the persistent hip offset a wrong half-rest bakes
                // in -- then by transient flex passthrough as a tiebreak. Flex worst alone is bounded by the
                // strap slop for every convention, so it can't be the primary discriminator.
                int best = 0;
                for (int c = 1; c < convN; c++)
                {
                    if (rigidWorst[c] < rigidWorst[best] - 1e-4f ||
                        (Mathf.Abs(rigidWorst[c] - rigidWorst[best]) <= 1e-4f && flexWorst[c] < flexWorst[best]))
                        best = c;
                }

                summary.Ok = true;
                summary.Rows = rows;
                summary.ConvNames = k_ConvNames;
                summary.RigidWorstErrDeg = rigidWorst;
                summary.FlexWorstErrDeg = flexWorst;
                summary.TSpreadDeg = tSpread;
                summary.OnsetYawDeg = onsetYaw;
                summary.MeanErrDeg = new float[convN];
                for (int c = 0; c < convN; c++)
                    summary.MeanErrDeg[c] = meanCount > 0 ? (float)(meanAcc[c] / (meanCount / convN)) : 0f;
                summary.BestConvIndex = best;
                summary.CurrentConvIndex = (int)BasisMidpointFusionConvention.ProjectedReprime;
                summary.StableConvIndex = (int)BasisMidpointFusionConvention.ProjectedStable;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        // The published rotation for a convention. The projected family routes through the REAL fusion the
        // live pair runs (BasisMidpointRotationFusion); the others are candidate alternatives.
        static Quaternion Mid(BasisMidpointFusionConvention conv, Quaternion aRot, Quaternion bRot, float t, Quaternion halfRest)
        {
            switch (conv)
            {
                case BasisMidpointFusionConvention.NaiveSlerp:
                    return Quaternion.Slerp(aRot, bRot, t);
                case BasisMidpointFusionConvention.PartnerAOnly:
                    return aRot;
                default: // ProjectedReprime, ProjectedStable, AveragedHalfRest
                    return BasisMidpointRotationFusion.ProjectAndSlerp(aRot, bRot, t, halfRest);
            }
        }

        // A small random rotation of magnitude up to maxDeg about a random axis -- the per-strap slop that
        // makes the pair non-rigid, so a half-rest primed at one instant doesn't match another.
        static Quaternion RandomFlex(System.Random rng, float maxDeg)
        {
            if (maxDeg <= 0f) return Quaternion.identity;
            Vector3 axis = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0));
            if (axis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            float deg = (float)(rng.NextDouble()) * maxDeg;
            return Quaternion.AngleAxis(deg, axis.normalized);
        }

        static void Append(StringBuilder sb, float v) { sb.Append(F(v)).Append(','); }

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
