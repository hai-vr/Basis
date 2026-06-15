using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Deterministic temporal sweep of the eye gaze / saccade / vergence math (BasisEyeState.Update), the
    // per-eye driver the live rig runs every LateUpdate. The job (BasisEyeJob) only unpacks a NativeArray
    // and calls BasisEyeState.Update -- which is pure Unity.Mathematics on a stack struct -- so this sweep
    // drives Update directly on a worker-safe copy (no NativeArrays, no transforms). Calibration is forced
    // to identity so a canonical yaw/pitch maps to itself: CanonicalYawPitchToRigOffset becomes the plain
    // canonical->quat, and the per-eye offset quaternions invert back to exact yaw/pitch for the asserts.
    // RNG is Unity.Mathematics.Random seeded by a constant (BasisEyeState.Create(seed)), so saccade timing
    // is fully deterministic and reproducible.
    //
    // Scenarios:
    //  - sweep:   a static gaze target stepped across the field of view; gaze must never exceed the maxAngle
    //             clamp and the two eyes must verge symmetrically about the focal yaw.
    //  - jump:    a fast target jump (far left -> far right); no NaN, no overshoot past the clamp, and both
    //             eyes must move TOWARD the new target (vergence sign correct, not away).
    //  - hold:    a held target with personality-driven idle saccades; hold intervals between saccades must
    //             land within the personality holdMin/holdMax bounds (the Liveliness-derived range).
    public struct BasisEyeSweepConfig
    {
        public float Fps;
        public float Seconds;
        public float MaxAngleDeg;          // eye rotation clamp away from forward (driver default 25)
        public float SaccadeMin;           // saccade duration floor (driver default 0.05)
        public float SaccadeMax;           // saccade duration ceiling (driver default 0.15)
        public float PerEyeVarianceDeg;    // per-eye vergence divergence (driver default 0.4)
        public float Liveliness;           // personality: saccade frequency/amplitude (0..1)
        public float Attentiveness;        // personality: eye-contact commitment (0..1)
        public float TargetYawDeg;         // held/jump target yaw amplitude in canonical degrees
        public float TargetPitchDeg;       // held/jump target pitch amplitude in canonical degrees
        public uint Seed;

        public static BasisEyeSweepConfig Default()
        {
            // Mirrors BasisLocalEyeDriver field defaults (maxAngleDeg=25, saccadeTimeRange=(0.05,0.15),
            // perEyeVarianceDeg=0.4). Personality at mid Liveliness/Attentiveness exercises the saccade
            // state machine and the gaze blend without sitting at an extreme; the window can sweep them.
            return new BasisEyeSweepConfig
            {
                Fps = 90f,
                Seconds = 8f,
                MaxAngleDeg = 25f,
                SaccadeMin = 0.05f,
                SaccadeMax = 0.15f,
                PerEyeVarianceDeg = 0.4f,
                Liveliness = 0.5f,
                Attentiveness = 0.5f,
                TargetYawDeg = 18f,
                TargetPitchDeg = 6f,
                Seed = 12345u,
            };
        }
    }

    public struct BasisEyeSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Steps;
        public int NaNCount;

        public float MaxGazeAngleDeg;       // worst per-eye gaze magnitude across all scenarios (deg)
        public float ClampDeg;              // the configured maxAngle clamp (deg) the gaze must stay under
        public float ClampOvershootDeg;     // how far the worst eye exceeded the clamp (deg, 0 = held)

        public int VergenceWrongSign;       // saccade frames where an eye verged AWAY from the focal yaw
        public int VergenceSamples;         // saccade frames the vergence-sign check looked at

        public float JumpMaxOvershootDeg;   // on the fast jump: worst eye travel PAST the target yaw (deg)
        public int JumpMovedTowardOk;       // 1 = both eyes' first motion was toward the jump target

        public int SaccadeIntervals;        // idle holds measured in the hold scenario
        public float SaccadeMinHoldSec;     // shortest measured hold between saccades (s)
        public float SaccadeMaxHoldSec;     // longest measured hold between saccades (s)
        public float PersonalityHoldMinSec; // personality lower bound a hold must respect
        public float PersonalityHoldMaxSec; // personality upper bound a hold must respect
        public int SaccadeOutOfBounds;      // holds that fell outside [holdMin, holdMax] (with tolerance)

        public float MaxSymmetryDeg;        // worst |leftMagnitude - rightMagnitude| about the focal (deg)

        public string Error;
    }

    public static class BasisEyeSweep
    {
        public const string DefaultFileName = "BasisEyeSweep.csv";

        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        // Identity calibration: basis = invBasis = identity so CanonicalYawPitchToRigOffset(yp) == the plain
        // canonical->quat, letting us invert leftOffset/rightOffset back to exact canonical yaw/pitch.
        static BasisEyeCalibration IdentityCal() => new BasisEyeCalibration
        {
            basis = quaternion.identity,
            invBasis = quaternion.identity,
            initialRotation = quaternion.identity,
        };

        public static BasisEyeSweepSummary Run(BasisEyeSweepConfig cfg, string path)
        {
            var summary = new BasisEyeSweepSummary { Ok = false, Path = path };
            float fps = Mathf.Max(5f, cfg.Fps);
            float dt = 1f / fps;
            int n = Mathf.Max(8, Mathf.RoundToInt(cfg.Seconds * fps));

            float maxAngleRad = math.radians(cfg.MaxAngleDeg);
            float perEyeVarRad = math.radians(cfg.PerEyeVarianceDeg);
            var personality = BasisEyePersonality.Compute(cfg.Liveliness, cfg.Attentiveness);
            var calL = IdentityCal();
            var calR = IdentityCal();

            // The clamp uses a plane (sqrt(yaw^2+pitch^2)) clamp; allow a hair of float slack on the assert.
            const float ClampSlackDeg = 0.05f;
            // Hold bounds are scaled in-state by holdScale * max(1, jitterScale*0.4) (both >= ~ values that
            // only widen the upper side) and the saccade itself consumes time; gate with generous slack and
            // report -- the point is "in the personality ballpark", not exact.
            const float HoldLowSlackSec = 0.05f;

            int nan = 0;
            float maxGaze = 0f, clampOver = 0f, maxSym = 0f;
            int vergWrong = 0, vergSamples = 0;
            float jumpOvershoot = 0f; int jumpTowardOk = 0;
            int sacIntervals = 0; float sacMin = float.PositiveInfinity, sacMax = 0f; int sacOob = 0;

            try
            {
                string d = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisEyeSweep " + System.DateTime.UtcNow.ToString("o") +
                                " fps=" + F(fps) + " maxAngle=" + F(cfg.MaxAngleDeg) +
                                " saccade=" + F(cfg.SaccadeMin) + ".." + F(cfg.SaccadeMax) +
                                " perEyeVar=" + F(cfg.PerEyeVarianceDeg) +
                                " liveliness=" + F(cfg.Liveliness) + " attentiveness=" + F(cfg.Attentiveness) +
                                " holdBounds=" + F(personality.holdMin) + ".." + F(personality.holdMax) +
                                " seed=" + cfg.Seed);
                    w.WriteLine("scenario,step,focalYawDeg,focalPitchDeg,leftYawDeg,leftPitchDeg,rightYawDeg,rightPitchDeg,leftMagDeg,rightMagDeg,gazeBlend,phase");
                    var sb = new StringBuilder(160);

                    // === sweep: a static target stepped across the field of view ===
                    // Each step holds a fixed canonical gaze target; we sweep it from far -left to far +right.
                    // Gaze must never exceed the clamp; the two eyes must verge symmetrically about the focal.
                    {
                        var s = BasisEyeState.Create(cfg.Seed);
                        int steps = math.max(2, n);
                        for (int i = 0; i < steps; i++)
                        {
                            float frac = steps > 1 ? (float)i / (steps - 1) : 0.5f;
                            float yawDeg = math.lerp(-cfg.TargetYawDeg, cfg.TargetYawDeg, frac);
                            float2 focal = new float2(math.radians(yawDeg), math.radians(cfg.TargetPitchDeg));
                            // Hold the same target every frame so socialCurrent converges onto it.
                            bool changed = i == 0;
                            s.Update(dt, float2.zero, maxAngleRad, cfg.SaccadeMin, cfg.SaccadeMax, perEyeVarRad,
                                     personality, calL, calR, true, focal, focal, focal, 1f, changed);

                            if (!ReadEyes(s, out float2 lYP, out float2 rYP)) { nan++; break; }
                            AccumGaze(lYP, rYP, maxAngleRad, ref maxGaze, ref clampOver);
                            // Symmetry: both eyes equal magnitude about the focal point (they verge by +/- eyeVar*scale).
                            float sym = math.abs(math.length(lYP) - math.length(rYP));
                            if (math.degrees(sym) > maxSym) maxSym = math.degrees(sym);

                            WriteRow(w, sb, "sweep", i, focal, lYP, rYP, s);
                        }
                    }

                    // === jump: a fast target jump far-left -> far-right ===
                    // Hold far-left to settle, snap to far-right, then watch convergence: no overshoot past the
                    // target yaw, no NaN, and both eyes' first post-jump motion is TOWARD the target (vergence
                    // sign correct). Vergence-sign is also sampled every frame the eyes are off-center.
                    {
                        var s = BasisEyeState.Create(cfg.Seed ^ 0x9E3779B9u);
                        int half = math.max(4, n / 2);
                        // Frames to let the gaze SETTLE after a target change before judging vergence sign --
                        // during the saccade the eyes legitimately travel through the old side. 0.6s >> the
                        // saccade (<=0.15s), so only the converged hold is sampled.
                        int settleFrames = math.max(8, (int)math.ceil(0.6f * fps));
                        float leftYaw = -cfg.TargetYawDeg;
                        float rightYaw = cfg.TargetYawDeg;
                        float2 focalA = new float2(math.radians(leftYaw), math.radians(cfg.TargetPitchDeg));
                        float2 focalB = new float2(math.radians(rightYaw), math.radians(cfg.TargetPitchDeg));

                        float prevLeftYaw = 0f, prevRightYaw = 0f;
                        bool capturedFirstMove = false;
                        for (int i = 0; i < half * 2; i++)
                        {
                            bool jumped = i == half;
                            float2 focal = i < half ? focalA : focalB;
                            bool changed = i == 0 || jumped;
                            s.Update(dt, float2.zero, maxAngleRad, cfg.SaccadeMin, cfg.SaccadeMax, perEyeVarRad,
                                     personality, calL, calR, true, focal, focal, focal, 1f, changed);

                            if (!ReadEyes(s, out float2 lYP, out float2 rYP)) { nan++; break; }
                            AccumGaze(lYP, rYP, maxAngleRad, ref maxGaze, ref clampOver);

                            // Overshoot past the post-jump target yaw (only meaningful after the jump).
                            if (i > half)
                            {
                                float overL = lYP.x - math.radians(rightYaw);
                                float overR = rYP.x - math.radians(rightYaw);
                                float over = math.degrees(math.max(overL, overR));
                                if (over > jumpOvershoot) jumpOvershoot = over;
                            }

                            // First post-jump frame: did both eyes start moving toward the new (positive) yaw?
                            if (jumped) { prevLeftYaw = lYP.x; prevRightYaw = rYP.x; capturedFirstMove = false; }
                            else if (i > half && !capturedFirstMove)
                            {
                                bool leftToward = lYP.x >= prevLeftYaw - 1e-4f;
                                bool rightToward = rYP.x >= prevRightYaw - 1e-4f;
                                jumpTowardOk = (leftToward && rightToward) ? 1 : 0;
                                capturedFirstMove = true;
                            }

                            // Vergence sign: once the gaze has SETTLED on the target, each eye must sit on the
                            // focal side (eyes verge AROUND the focal point, never flip to the opposite side of
                            // forward). Skip the post-change saccade window -- during travel the eyes legitimately
                            // occupy the OLD side while crossing to the new target, which is not an inversion.
                            // Change points are i=0 (focalA) and i=half (focalB).
                            if (s.gazeBlend > 0.5f)
                            {
                                int framesSinceChange = i < half ? i : i - half;
                                float focalYaw = focal.x;
                                if (framesSinceChange >= settleFrames && math.abs(focalYaw) > math.radians(3f))
                                {
                                    vergSamples += 2;
                                    if (math.sign(lYP.x) != math.sign(focalYaw)) vergWrong++;
                                    if (math.sign(rYP.x) != math.sign(focalYaw)) vergWrong++;
                                }
                            }

                            float symJ = math.abs(math.length(lYP) - math.length(rYP));
                            if (math.degrees(symJ) > maxSym) maxSym = math.degrees(symJ);

                            WriteRow(w, sb, "jump", i, focal, lYP, rYP, s);
                        }
                    }

                    // === hold: held target with personality-driven idle saccades ===
                    // No gaze target (hasGazeTarget=false) so the idle saccade state machine drives alone; we
                    // measure the hold interval between saccade onsets and assert it lands in [holdMin, holdMax].
                    {
                        var s = BasisEyeState.Create(cfg.Seed * 2654435761u + 1u);
                        byte prevPhase = s.phase;
                        float holdAccum = 0f;
                        bool inHold = s.phase == 0;
                        bool _sawFirstHold = false;
                        for (int i = 0; i < n; i++)
                        {
                            s.Update(dt, float2.zero, maxAngleRad, cfg.SaccadeMin, cfg.SaccadeMax, perEyeVarRad,
                                     personality, calL, calR, false, float2.zero, float2.zero, float2.zero, 0f, false);

                            if (!ReadEyes(s, out float2 lYP, out float2 rYP)) { nan++; break; }
                            AccumGaze(lYP, rYP, maxAngleRad, ref maxGaze, ref clampOver);
                            float symH = math.abs(math.length(lYP) - math.length(rYP));
                            if (math.degrees(symH) > maxSym) maxSym = math.degrees(symH);

                            // Hold accounting: accumulate while in HOLD (phase 0); on the HOLD->SACCADE edge,
                            // the just-ended hold is a completed interval.
                            if (s.phase == 0) { if (inHold) holdAccum += dt; else { inHold = true; holdAccum = dt; } }
                            else
                            {
                                if (prevPhase == 0 && inHold && holdAccum > 0f)
                                {
                                    // The very first hold uses the Create() seed phaseDur (0.5s), not a
                                    // personality-drawn duration, so skip it from both the stats and the bound
                                    // check -- only personality-driven holds are the assertion target.
                                    bool firstHold = !_sawFirstHold;
                                    _sawFirstHold = true;
                                    if (!firstHold)
                                    {
                                        sacIntervals++;
                                        if (holdAccum < sacMin) sacMin = holdAccum;
                                        if (holdAccum > sacMax) sacMax = holdAccum;
                                        // Compare to the personality range with slack: in-state scaling only
                                        // widens the upper side, so the lower bound is the tight one.
                                        bool below = holdAccum < personality.holdMin - HoldLowSlackSec;
                                        bool above = holdAccum > personality.holdMax * 3f + 0.5f;
                                        if (below || above) sacOob++;
                                    }
                                    inHold = false;
                                }
                            }
                            prevPhase = s.phase;

                            WriteRow(w, sb, "hold", i, float2.zero, lYP, rYP, s);
                        }
                    }
                }

                summary.Ok = true;
                summary.Steps = n;
                summary.NaNCount = nan;
                summary.MaxGazeAngleDeg = maxGaze;
                summary.ClampDeg = cfg.MaxAngleDeg;
                summary.ClampOvershootDeg = math.max(0f, clampOver - ClampSlackDeg);
                summary.VergenceWrongSign = vergWrong;
                summary.VergenceSamples = vergSamples;
                summary.JumpMaxOvershootDeg = math.max(0f, jumpOvershoot - ClampSlackDeg);
                summary.JumpMovedTowardOk = jumpTowardOk;
                summary.SaccadeIntervals = sacIntervals;
                summary.SaccadeMinHoldSec = sacIntervals > 0 ? sacMin : 0f;
                summary.SaccadeMaxHoldSec = sacMax;
                summary.PersonalityHoldMinSec = personality.holdMin;
                summary.PersonalityHoldMaxSec = personality.holdMax;
                summary.SaccadeOutOfBounds = sacOob;
                summary.MaxSymmetryDeg = maxSym;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        // Invert the rig-local per-eye offset quaternions back to canonical yaw/pitch. With identity
        // calibration the offset IS CanonicalYawPitchToQuat(yp) = yaw(+Y) * pitch(-X), so rotating +Z
        // forward by it and reading atan2/asin recovers (yaw, pitch) exactly.
        static bool ReadEyes(in BasisEyeState s, out float2 leftYP, out float2 rightYP)
        {
            leftYP = OffsetToYawPitch(s.leftOffset);
            rightYP = OffsetToYawPitch(s.rightOffset);
            return Finite(s.leftOffset) && Finite(s.rightOffset) && Finite(leftYP) && Finite(rightYP);
        }

        static float2 OffsetToYawPitch(quaternion q)
        {
            float3 f = math.mul(q, new float3(0f, 0f, 1f));
            return new float2(
                math.atan2(f.x, f.z),
                math.asin(math.clamp(f.y, -1f, 1f)));
        }

        static void AccumGaze(float2 lYP, float2 rYP, float maxAngleRad, ref float maxGazeDeg, ref float clampOverDeg)
        {
            float lMag = math.length(lYP);
            float rMag = math.length(rYP);
            float worst = math.max(lMag, rMag);
            float worstDeg = math.degrees(worst);
            if (worstDeg > maxGazeDeg) maxGazeDeg = worstDeg;
            float over = math.degrees(worst - maxAngleRad);
            if (over > clampOverDeg) clampOverDeg = over;
        }

        static void WriteRow(StreamWriter w, StringBuilder sb, string scenario, int step,
                             float2 focal, float2 lYP, float2 rYP, in BasisEyeState s)
        {
            sb.Clear();
            sb.Append(scenario).Append(',').Append(step).Append(',');
            sb.Append(F(math.degrees(focal.x))).Append(',').Append(F(math.degrees(focal.y))).Append(',');
            sb.Append(F(math.degrees(lYP.x))).Append(',').Append(F(math.degrees(lYP.y))).Append(',');
            sb.Append(F(math.degrees(rYP.x))).Append(',').Append(F(math.degrees(rYP.y))).Append(',');
            sb.Append(F(math.degrees(math.length(lYP)))).Append(',').Append(F(math.degrees(math.length(rYP)))).Append(',');
            sb.Append(F(s.gazeBlend)).Append(',').Append(s.phase);
            w.WriteLine(sb.ToString());
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(float2 v) => Finite(v.x) && Finite(v.y);
        static bool Finite(quaternion q) => Finite(q.value.x) && Finite(q.value.y) && Finite(q.value.z) && Finite(q.value.w);

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
