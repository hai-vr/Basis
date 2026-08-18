using System.Globalization;
using System.Text;

namespace Basis.Benchmark.Harness;

/// <summary>One reading of the population during a join burst.</summary>
public readonly record struct AdmissionSample(double Seconds, int Connected);

/// <summary>What a mass rejoin looked like.</summary>
public sealed class AdmissionResult
{
    public required int Requested { get; init; }
    public required int Admitted { get; init; }
    public required double SecondsToFull { get; init; }
    public required IReadOnlyList<AdmissionSample> Curve { get; init; }
    public required bool Completed { get; init; }
    public required string? Failure { get; init; }

    /// <summary>Fastest sustained admission rate seen, players per second.</summary>
    public double PeakRatePerSecond { get; init; }

    /// <summary>Rate across the whole burst — what the last client in the queue actually experiences.</summary>
    public double AverageRatePerSecond => SecondsToFull <= 0 ? 0 : Admitted / SecondsToFull;

    public bool EveryoneGotIn => Requested > 0 && Admitted >= Requested;

    /// <summary>
    /// How long the last client in the queue waits before the server reaches it.
    ///
    /// This is the number the auth window has to cover. A client is not idle while it waits — it is
    /// holding a half-open handshake that the server is timing, so the wait and the timeout are
    /// racing each other.
    /// </summary>
    public double WorstCaseWaitSeconds => SecondsToFull;

    public string Describe()
    {
        var sb = new StringBuilder();
        if (!Completed)
        {
            sb.AppendLine($"  Burst failed: {Failure}");
            return sb.ToString();
        }

        sb.AppendLine($"  {Admitted:N0} of {Requested:N0} clients admitted in {SecondsToFull:F1}s");
        sb.AppendLine($"    average {AverageRatePerSecond:N0}/s, peak {PeakRatePerSecond:N0}/s");

        if (!EveryoneGotIn)
            sb.AppendLine($"    {Requested - Admitted:N0} never got in - the server rejected or timed out their handshake.");

        // A few points off the curve, so the shape is visible without printing every sample. A
        // burst that admits fast then stalls looks nothing like one that admits steadily, and the
        // difference is what distinguishes a saturated auth path from a saturated socket.
        if (Curve.Count > 3)
        {
            sb.Append("    ramp:");
            foreach (double fraction in new[] { 0.25, 0.5, 0.75, 1.0 })
            {
                int target = (int)(Requested * fraction);
                AdmissionSample point = Curve.FirstOrDefault(c => c.Connected >= target);
                if (point.Connected > 0) sb.Append($"  {fraction:P0} at {point.Seconds:F1}s");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// Measures a thundering herd: everyone reconnecting at once, the way they do after a restart.
///
/// <para><b>Why this needs its own test rather than falling out of the capacity ladder.</b> The
/// ladder connects its crowd gradually and then measures the steady state, which is the right
/// shape for asking how many players a box can serve — and it is the wrong shape entirely for
/// asking how many it can <em>admit</em>. Those are different subsystems under different pressure.
/// Admission is a handshake with several round trips and a signature verification per client, all
/// racing a per-client timeout; steady state is a send loop. A server can be comfortable at 2000
/// players and still be unable to get 2000 players in.</para>
///
/// <para>It is also a failure mode that has actually happened here: a 4000-client join burst left
/// 596 unable to finish the handshake inside the auth window, and the only trace was a log line
/// saying they were not in the authenticated set, which names the symptom and not the cause. The
/// operator sees a restart that never recovers.</para>
///
/// <para><b>The measurement is the ramp, not the endpoint.</b> "Everyone got in eventually" hides
/// the thing worth knowing, because the failure is a race: the last client in the queue waits for
/// the whole burst to drain, and the auth window is running the entire time. Sampling the curve
/// gives the worst-case wait directly, which is what the timeout has to cover.</para>
/// </summary>
public static class AdmissionBurst
{
    /// <summary>How often the population is sampled during the ramp.</summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Stop once the population has not moved for this long. A burst that has stalled is finished,
    /// whatever it reached, and waiting out the full timeout on every stalled run wastes minutes.
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    public static AdmissionResult Run(LoadRunner runner, RunOptions options, Action<string> log, CancellationToken cancel)
    {
        log($"  Burst: {options.Players:N0} clients connecting at once (this is what a restart looks like).");

        var curve = new List<AdmissionSample>();
        double peakRate = 0;

        AdmissionResult result = runner.RunBurst(options, (seconds, connected) =>
        {
            if (curve.Count > 0)
            {
                AdmissionSample previous = curve[^1];
                double elapsed = seconds - previous.Seconds;
                if (elapsed > 0)
                {
                    double rate = (connected - previous.Connected) / elapsed;
                    if (rate > peakRate) peakRate = rate;
                }
            }
            curve.Add(new AdmissionSample(seconds, connected));
        }, cancel);

        result = new AdmissionResult
        {
            Requested = result.Requested,
            Admitted = result.Admitted,
            SecondsToFull = result.SecondsToFull,
            Curve = curve,
            Completed = result.Completed,
            Failure = result.Failure,
            PeakRatePerSecond = peakRate,
        };

        log(result.Describe());
        return result;
    }

    /// <summary>
    /// The base auth window this machine needs, from the measured worst-case wait.
    ///
    /// <para>The server already widens the window with population — it adds a fixed amount per
    /// connected peer up to a cap — so what is fitted here is the base it adds to, and the fit has
    /// to account for what the population term already contributes or it would double-count.</para>
    /// </summary>
    /// <param name="players">Population the burst was run at.</param>
    /// <param name="worstCaseWaitSeconds">How long the last client waited.</param>
    /// <param name="perPeerMs">The server's per-peer addition (12 ms at time of writing).</param>
    /// <param name="maxExtraMs">Cap on that addition (45 s at time of writing).</param>
    public static int RequiredBaseTimeoutMs(int players, double worstCaseWaitSeconds, int perPeerMs = 12, int maxExtraMs = 45000)
    {
        if (players <= 0 || worstCaseWaitSeconds <= 0) return 0;

        // Doubled, because the burst measured here is the good case: every client is on this
        // machine, on loopback, with no packet loss and no retransmits. A real herd arrives over a
        // network with all three, and a window fitted exactly to the ideal case fails on the first
        // real restart it meets.
        double needed = worstCaseWaitSeconds * 2.0 * 1000.0;

        double populationTerm = Math.Min((double)players * perPeerMs, maxExtraMs);
        double baseNeeded = needed - populationTerm;

        // Rounded up to the nearest second, and never below the shipped default: a shorter window
        // than the server ships with is not something this measurement can justify, since a smaller
        // number only ever rejects more people.
        int rounded = (int)(Math.Ceiling(baseNeeded / 1000.0) * 1000);
        return Math.Max(9000, rounded);
    }

    public static string FormatRate(double perSecond) =>
        perSecond.ToString("N0", CultureInfo.InvariantCulture) + "/s";
}
