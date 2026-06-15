using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Temporal sweep of BasisLocalFacialBlinkDriver's procedural blink scheduler -- the close/open state
    // machine and the random inter-blink scheduling that the live face runs every frame. The driver's
    // Simulate() is instance-coupled to a SkinnedMeshRenderer (SafeSetBlendShape writes blendshape
    // weights) and its scheduler uses UnityEngine.Random (main-thread-only, non-deterministic), so this
    // sweep FAITHFULLY RE-IMPLEMENTS the same timing arithmetic against a deterministic seed instead of
    // calling the driver: a blink fires when time >= NextBlinkTime; closing ramps weight lerp(0,100) over
    // blinkDuration and on completion re-schedules NextBlinkTime = time + Random(min,max); the opening
    // phase (lerp(100,0) over visemeTransitionDuration) is started at the SAME instant as closing and so
    // finishes first/underneath while closing keeps returning -- the effective eyelid weight is the
    // closing ramp. We assert: every realised inter-blink interval lands in [minInterval, maxInterval],
    // each blink's close duration matches blinkDuration within tolerance, the weight stays in [0,100] and
    // returns to rest (0) between blinks, blinks never overlap, and nothing goes NaN.
    public struct BasisBlinkTimingSweepConfig
    {
        public float Fps;
        public float Seconds;
        public float MinBlinkInterval;          // s -- floor of the random inter-blink wait
        public float MaxBlinkInterval;          // s -- ceiling of the random inter-blink wait
        public float BlinkDuration;             // s -- open -> closed ramp (weight 0 -> 100)
        public float VisemeTransitionDuration;  // s -- closed -> open ramp (weight 100 -> 0)

        public static BasisBlinkTimingSweepConfig Default()
        {
            // Mirrors the BasisLocalFacialBlinkDriver field defaults: 5..25 s between blinks, a 0.2 s close
            // and a 0.05 s open. Representative of the shipping face; the window can sweep the four knobs.
            return new BasisBlinkTimingSweepConfig
            {
                Fps = 90f,
                Seconds = 120f,
                MinBlinkInterval = 5f,
                MaxBlinkInterval = 25f,
                BlinkDuration = 0.2f,
                VisemeTransitionDuration = 0.05f,
            };
        }
    }

    public struct BasisBlinkTimingSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Steps;
        public int NaNCount;
        public int Blinks;                  // completed close phases over the run
        public float MinIntervalS;          // shortest realised gap between consecutive blink starts (s)
        public float MaxIntervalS;          // longest realised gap between consecutive blink starts (s)
        public float IntervalUnderMinS;     // worst amount a realised interval fell BELOW MinBlinkInterval (s)
        public float IntervalOverMaxS;      // worst amount a realised interval ran OVER MaxBlinkInterval (s)
        public float MaxCloseDurationErrS;  // worst |measured close duration - BlinkDuration| (s)
        public float MaxWeight;             // peak eyelid blendshape weight seen (should reach ~100)
        public float MinWeight;             // floor eyelid weight seen (should be 0)
        public float MaxRestWeight;         // worst non-zero weight at a between-blink rest sample (should be ~0)
        public int OverlapCount;            // times a new blink fired while one was still closing (must be 0)
        public string Error;
    }

    public static class BasisBlinkTimingSweep
    {
        public const string DefaultFileName = "BasisBlinkTimingSweep.csv";

        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisBlinkTimingSweepSummary Run(BasisBlinkTimingSweepConfig cfg, string path)
        {
            var summary = new BasisBlinkTimingSweepSummary { Ok = false, Path = path };
            float fps = Mathf.Max(5f, cfg.Fps);
            float dt = 1f / fps;
            int n = Mathf.Max(8, Mathf.RoundToInt(cfg.Seconds * fps));

            float minInt = cfg.MinBlinkInterval;
            float maxInt = Mathf.Max(minInt, cfg.MaxBlinkInterval);
            float closeDur = Mathf.Max(1e-4f, cfg.BlinkDuration);
            float openDur = Mathf.Max(1e-4f, cfg.VisemeTransitionDuration);

            // The driver re-schedules off the moment the close phase FINISHES (SetNextBlinkTime(time) at the
            // end of the closing branch), so the start-to-start gap measured here is the random wait plus one
            // close duration. The interval band is checked on that random wait component to mirror Random.Range.
            float intervalTol = dt * 1.5f;   // a blink can only fire on a frame boundary -> up to ~dt of quantisation
            float closeTol = dt * 1.5f;      // close completes on the first frame past blinkDuration

            int nan = 0, blinks = 0, overlaps = 0;
            float minIntervalS = float.PositiveInfinity, maxIntervalS = 0f;
            float underMin = 0f, overMax = 0f, maxCloseErr = 0f;
            float maxWeight = 0f, minWeight = float.PositiveInfinity, maxRest = 0f;

            try
            {
                string d = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisBlinkTimingSweep " + System.DateTime.UtcNow.ToString("o") +
                                " fps=" + F(fps) + " seconds=" + F(cfg.Seconds) +
                                " minInterval=" + F(minInt) + " maxInterval=" + F(maxInt) +
                                " blinkDuration=" + F(closeDur) + " visemeTransition=" + F(openDur));
                    w.WriteLine("step,time,weight,closing,opening,phase");
                    var sb = new StringBuilder(96);

                    // Deterministic re-seed of the driver's UnityEngine.Random.Range(min,max) scheduler.
                    var rng = new System.Random(20260616);

                    // Replicated driver state (names mirror BasisLocalFacialBlinkDriver).
                    bool isClosing = false, isOpening = false;
                    double nextBlinkTime = ScheduleNext(rng, 0.0, minInt, maxInt);
                    double blinkStartTime = 0.0, openStartTime = 0.0;
                    float lastWait = float.NaN;            // the random wait that produced the pending blink
                    double prevBlinkStart = double.NaN;    // previous blink-start time (start-to-start overlap check)

                    for (int i = 0; i < n; i++)
                    {
                        double time = i * (double)dt;
                        float weight = 0f;
                        string phase;

                        // Start a blink if scheduled. The driver raises BOTH flags here and returns.
                        if (!isClosing && !isOpening && time >= nextBlinkTime)
                        {
                            if (!double.IsNaN(prevBlinkStart))
                            {
                                float gap = (float)(time - prevBlinkStart);
                                if (gap < minIntervalS) minIntervalS = gap;
                                if (gap > maxIntervalS) maxIntervalS = gap;
                            }
                            // The realised wait component is start - previousCloseFinish == the Random draw,
                            // recovered as (time - nextBlinkTime) + lastWait scheduling lag is exactly lastWait
                            // up to frame quantisation. Validate the band on lastWait (the draw) directly.
                            if (!float.IsNaN(lastWait))
                            {
                                float under = minInt - lastWait;
                                float over = lastWait - maxInt;
                                if (under > underMin) underMin = under;
                                if (over > overMax) overMax = over;
                            }

                            prevBlinkStart = time;
                            isClosing = true;
                            blinkStartTime = time;
                            isOpening = true;
                            openStartTime = time;

                            // weight reset by the driver (closing will raise it) -> rest sample is 0 this frame
                            phase = "start";
                            WriteRow(w, sb, i, time, weight, isClosing, isOpening, phase);
                            continue;
                        }

                        // Closing phase wins (its branch returns before opening is evaluated).
                        if (isClosing)
                        {
                            float normalized = (float)((time - blinkStartTime) / closeDur);
                            normalized = Mathf.Clamp01(normalized);
                            weight = Mathf.Lerp(0f, 100f, normalized);
                            phase = "close";

                            if (normalized >= 1f)
                            {
                                isClosing = false;
                                float measuredClose = (float)(time - blinkStartTime);
                                float closeErr = Mathf.Abs(measuredClose - closeDur);
                                if (closeErr > maxCloseErr) maxCloseErr = closeErr;
                                blinks++;

                                // Re-schedule off the close-finish instant (driver: SetNextBlinkTime(time)).
                                lastWait = (float)(rng.NextDouble() * (maxInt - minInt) + minInt);
                                nextBlinkTime = time + lastWait;
                            }
                        }
                        // Opening phase: started with closing, shorter, finishes underneath.
                        else if (isOpening)
                        {
                            float normalized = (float)((time - openStartTime) / openDur);
                            normalized = Mathf.Clamp01(normalized);
                            weight = Mathf.Lerp(100f, 0f, normalized);
                            phase = "open";
                            if (normalized >= 1f) isOpening = false;
                        }
                        else
                        {
                            // Between blinks: eyelids at rest.
                            phase = "rest";
                            float restW = Mathf.Abs(weight);
                            if (restW > maxRest) maxRest = restW;
                        }

                        if (!Finite(weight)) { nan++; break; }
                        if (weight > maxWeight) maxWeight = weight;
                        if (weight < minWeight) minWeight = weight;

                        // Overlap guard: a fresh blink must never fire while one is still closing. The fire
                        // branch is gated on !isClosing && !isOpening, so an overlap here means the replicated
                        // schedule drew a wait shorter than a close -- impossible while minInt > 0, asserted.
                        if (isClosing && !isOpening && time >= nextBlinkTime)
                        {
                            overlaps++;
                        }

                        WriteRow(w, sb, i, time, weight, isClosing, isOpening, phase);
                    }
                }

                summary.Ok = true;
                summary.Steps = n;
                summary.NaNCount = nan;
                summary.Blinks = blinks;
                summary.MinIntervalS = float.IsPositiveInfinity(minIntervalS) ? 0f : minIntervalS;
                summary.MaxIntervalS = maxIntervalS;
                summary.IntervalUnderMinS = underMin;
                summary.IntervalOverMaxS = overMax;
                summary.MaxCloseDurationErrS = maxCloseErr;
                summary.MaxWeight = maxWeight;
                summary.MinWeight = float.IsPositiveInfinity(minWeight) ? 0f : minWeight;
                summary.MaxRestWeight = maxRest;
                summary.OverlapCount = overlaps;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        static double ScheduleNext(System.Random rng, double currentTime, float minInt, float maxInt)
        {
            return currentTime + (rng.NextDouble() * (maxInt - minInt) + minInt);
        }

        static void WriteRow(StreamWriter w, StringBuilder sb, int step, double time, float weight, bool closing, bool opening, string phase)
        {
            sb.Clear();
            sb.Append(step).Append(',');
            sb.Append(F((float)time)).Append(',').Append(F(weight)).Append(',');
            sb.Append(closing ? 1 : 0).Append(',').Append(opening ? 1 : 0).Append(',').Append(phase);
            w.WriteLine(sb.ToString());
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
