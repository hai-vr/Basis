using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync.Testing
{
    /// <summary>
    /// Measures END-TO-END LATENCY of the generic value-sync path — the wall-clock gap between the
    /// owner writing a value and a remote rendering it. This is the "why does it feel laggy" axis the
    /// convergence matrix (<see cref="BasisSyncSim"/>) deliberately leaves out: that harness freezes the
    /// signal and asks "does the remote eventually reach the final value"; this one keeps the signal
    /// moving and asks "how far behind is the remote at every frame".
    ///
    /// Method: a single continuous field is driven by a LINEAR RAMP (value = slope·t), so the remote's
    /// interpolated output inverts cleanly back to the owner time that produced it, and
    /// latency = now − value/slope. It drives the REAL <see cref="BasisSyncReceiver"/> through the REAL
    /// <see cref="SimSender"/> cadence and <see cref="SimNetwork"/> wire (same primitives the convergence
    /// harness uses), so the number reflects shipping code, not a re-implementation.
    ///
    /// Distance reduction (default ON for synced objects) is modeled here because it is a first-order
    /// latency contributor in practice: the owner throttles its send rate by squared distance to the
    /// nearest observer, and the jitter buffer then holds 2+ of those now-larger intervals — so latency
    /// grows roughly quadratically with viewer distance.
    /// </summary>
    public static class BasisSyncLatency
    {
        // Server distance-reduction curve. Mirrors BasisServerConfiguration defaults (BSRBaseMultiplier /
        // BSRSIncreaseRate / BSRSlowestSendRate) and the scaling in BasisSyncedObject.TransmitIfDue.
        public const float DistBaseMultiplier = 1.0f;
        public const float DistIncreaseRate = 0.005f;
        public const float DistSlowestSendRate = 2.55f;

        /// <summary>
        /// The owner's effective send interval after distance reduction, for a nearest-observer distance in
        /// meters. interval = clamp(base·(BaseMultiplier + dist²·IncreaseRate), base, SlowestSendRate).
        /// </summary>
        public static double EffectiveSendInterval(double baseInterval, double distanceMeters)
        {
            double d2 = distanceMeters * distanceMeters;
            double scaled = baseInterval * (DistBaseMultiplier + d2 * DistIncreaseRate);
            return Mathf.Clamp((float)scaled, (float)baseInterval, DistSlowestSendRate);
        }

        public static BasisSyncLatencyResult Run(BasisSyncLatencyScenario s)
        {
            var result = new BasisSyncLatencyResult
            {
                Name = s.Name,
                SendHz = s.SendHz,
                DistanceMeters = s.DistanceMeters,
                DistanceReduction = s.DistanceReduction,
                BaseLatencyMs = s.BaseLatencyMs,
                JitterMs = s.JitterMs,
                Extrapolate = s.Extrapolate,
                RenderHz = s.RenderHz,
                JitterBufferDepth = s.JitterBufferDepth,
                SuppressReduction = s.SuppressDistanceReduction,
            };

            double baseInterval = 1.0 / Math.Max(1.0, s.SendHz);
            // SuppressDistanceReduction models the "full rate while held" path (BasisSyncedObject
            // .ShouldSuppressDistanceReduction): the owner ignores the distance throttle for this object.
            bool reduce = s.DistanceReduction && !s.SuppressDistanceReduction;
            double effInterval = reduce ? EffectiveSendInterval(baseInterval, s.DistanceMeters) : baseInterval;
            result.EffSendIntervalMs = (float)(effInterval * 1000.0);

            // One continuous Float field — the codec/interp path is identical for Position; a scalar keeps
            // the value↔time inversion unambiguous.
            var schema = new BasisSyncSchema();
            int contOffset = schema.GetField(schema.AddField(BasisSyncFieldType.Float, s.Interpolate, s.Quantize)).Offset;
            schema.Lock();

            var sender = new SimSender(schema)
            {
                SendInterval = (float)effInterval,
                KeyframeInterval = (float)s.KeyframeInterval,
                ContinuousEpsilon = 1e-4f,
                UseChecksum = s.UseChecksum,
            };

            var receiver = new BasisSyncReceiver(schema);
            receiver.Configure(s.Extrapolate, s.MaxExtrapolation, false, 0f, 0, 0, s.UseChecksum, (float)s.JitterBufferDepth);

            var profile = new NetworkProfile
            {
                Name = s.Name,
                DeltaLoss = (float)s.DeltaLoss,
                DuplicateProb = (float)s.DuplicateProb,
                BaseLatencySec = (float)(s.BaseLatencyMs / 1000.0),
                JitterSec = (float)(s.JitterMs / 1000.0),
            };
            var net = new SimNetwork(profile, new System.Random(s.Seed));
            var netStats = new BasisSyncSimResult();

            var outVals = new BasisSyncValues();
            outVals.Allocate(schema);

            double dt = 1.0 / Math.Max(1.0, s.RenderHz);
            int frames = Mathf.CeilToInt((float)(s.DurationSeconds / dt));
            double t = 0;

            var latencies = new List<double>(frames);
            double sumBufferIntervals = 0, sumBufferMs = 0, sumDynDepth = 0, sumStaged = 0;
            int settled = 0;

            for (int f = 0; f < frames; f++)
            {
                t += dt;
                double ownerValue = s.Slope * t;
                sender.Local.Cont[contOffset] = (float)ownerValue;

                // BasisEventDriver order: TransmitOwned, then ScheduleRemote (which advances the receiver).
                SimPacket pkt = sender.Tick(t);
                if (pkt != null) { net.Send(pkt, t, netStats); result.Sends++; }

                net.DeliverDue(t, receiver.OnPacket, netStats);
                receiver.Advance(dt);

                if (!receiver.HasData) continue;
                BasisSyncSim.Interpolate(schema, receiver.CurrentValues, receiver.NextValues, receiver.InterpTime, outVals);

                if (t < s.WarmupSeconds) continue;

                double sampled = outVals.Cont[contOffset];
                double latency = t - sampled / s.Slope;
                if (latency < 0 || latency > 5.0) continue; // discard fill-phase / nonsense

                latencies.Add(latency * 1000.0);

                // Structural buffer occupancy: frames held behind the freshest staged one, in send intervals.
                double bufferIntervals = 1.0 + receiver.BufferedFrameCount - receiver.InterpTime;
                if (bufferIntervals < 0) bufferIntervals = 0;
                sumBufferIntervals += bufferIntervals;
                sumBufferMs += bufferIntervals * effInterval * 1000.0;
                sumDynDepth += receiver.DynamicDepth;
                sumStaged += receiver.BufferedFrameCount;
                settled++;
            }

            result.Delivered = netStats.PacketsDelivered;
            result.Samples = latencies.Count;
            if (latencies.Count > 0)
            {
                latencies.Sort();
                double sum = 0; for (int i = 0; i < latencies.Count; i++) sum += latencies[i];
                double mean = sum / latencies.Count;
                double var = 0; for (int i = 0; i < latencies.Count; i++) { double d = latencies[i] - mean; var += d * d; }
                result.MeanLatencyMs = (float)mean;
                result.P50Ms = (float)Pct(latencies, 0.50);
                result.P95Ms = (float)Pct(latencies, 0.95);
                result.MaxLatencyMs = (float)latencies[latencies.Count - 1];
                result.LatencyStdMs = (float)Math.Sqrt(var / latencies.Count);
            }
            if (settled > 0)
            {
                result.MeanBufferIntervals = (float)(sumBufferIntervals / settled);
                result.MeanBufferMs = (float)(sumBufferMs / settled);
                result.MeanDynamicDepth = (float)(sumDynDepth / settled);
                result.MeanStagedDepth = (float)(sumStaged / settled);
            }
            return result;
        }

        static double Pct(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int idx = (int)(p * sorted.Count);
            if (idx >= sorted.Count) idx = sorted.Count - 1;
            return sorted[idx];
        }
    }

    /// <summary>One latency scenario: a send rate, a viewer distance, and a wire profile.</summary>
    public sealed class BasisSyncLatencyScenario
    {
        public string Name = "latency";
        public double SendHz = 20;             // BasisSyncedObject.SendIntervalSeconds default = 0.05 (20 Hz)
        public double KeyframeInterval = 0.5;
        public double DistanceMeters = 0;      // nearest-observer distance -> distance reduction
        public bool DistanceReduction = true;  // default ON for synced objects
        public double BaseLatencyMs = 10;      // one-way transit
        public double JitterMs = 0;
        public double DeltaLoss = 0;
        public double DuplicateProb = 0;
        public bool Extrapolate = false;       // default OFF
        public double MaxExtrapolation = 0.2;
        public double JitterBufferDepth = 2;   // BasisSyncedObject.JitterBufferDepth; 1 = low latency
        public bool SuppressDistanceReduction = false; // models "full rate while held"
        public bool Interpolate = true;
        public bool Quantize = false;
        public bool UseChecksum = true;        // default ON

        public double RenderHz = 72;
        public double Slope = 1.0;             // units/sec ramp; cancels out of the latency math
        public double DurationSeconds = 12.0;
        public double WarmupSeconds = 2.0;
        public int Seed = 12345;
    }

    /// <summary>One latency result — one CSV row.</summary>
    public sealed class BasisSyncLatencyResult
    {
        public string Name;
        public double SendHz;
        public double DistanceMeters;
        public bool DistanceReduction;
        public double BaseLatencyMs;
        public double JitterMs;
        public bool Extrapolate;
        public double RenderHz;
        public double JitterBufferDepth;
        public bool SuppressReduction;

        public float EffSendIntervalMs;
        public float MeanLatencyMs;
        public float P50Ms;
        public float P95Ms;
        public float MaxLatencyMs;
        public float LatencyStdMs;       // wobble of the latency itself (perceived jitter)

        public float MeanBufferIntervals; // structural jitter-buffer occupancy, in send intervals
        public float MeanBufferMs;        // ... in milliseconds (the buffer's share of the latency)
        public float MeanDynamicDepth;
        public float MeanStagedDepth;

        public int Sends;
        public int Delivered;
        public int Samples;

        public static string CsvHeader =>
            "scenario,sendHz,distanceM,distanceReduction,baseLatencyMs,jitterMs,extrapolate,renderHz," +
            "bufferDepth,suppressReduction," +
            "effSendIntervalMs,meanLatencyMs,p50Ms,p95Ms,maxLatencyMs,latencyStdMs," +
            "meanBufferIntervals,meanBufferMs,meanDynamicDepth,meanStagedDepth,sends,delivered,samples";

        public string ToCsvRow()
        {
            return string.Join(",",
                Csv(Name), F(SendHz), F(DistanceMeters), DistanceReduction ? "1" : "0",
                F(BaseLatencyMs), F(JitterMs), Extrapolate ? "1" : "0", F(RenderHz),
                F(JitterBufferDepth), SuppressReduction ? "1" : "0",
                F1(EffSendIntervalMs), F1(MeanLatencyMs), F1(P50Ms), F1(P95Ms), F1(MaxLatencyMs), F1(LatencyStdMs),
                F2(MeanBufferIntervals), F1(MeanBufferMs), F2(MeanDynamicDepth), F2(MeanStagedDepth),
                Sends, Delivered, Samples);
        }

        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        static string F1(double v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        static string F2(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        static string Csv(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0)
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
