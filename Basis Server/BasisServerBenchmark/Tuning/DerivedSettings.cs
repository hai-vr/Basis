using System.Globalization;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Micro;

namespace Basis.Benchmark.Tuning;

/// <summary>
/// The settings that can be concluded rather than measured.
///
/// <para>Running a load test to discover a fact the machine will simply tell you is not rigour, it
/// is expense — and it is worse than that, because a load run has noise and a fact does not. So
/// anything that follows from core count, memory, kernel support, or an offline microbenchmark is
/// derived here, and the load sweep is spent only on the questions that genuinely need it.</para>
///
/// <para>Derivation is also the only route available for one of the most important settings.
/// Multi-socket cannot be measured on a single box at all: over loopback the kernel performs the
/// receive-side work inline inside the sender and charges for bytes rather than datagrams, so the
/// entire benefit is invisible, and a sweep would confidently report that sockets do not
/// matter.</para>
/// </summary>
public static class DerivedSettings
{
    public static IEnumerable<Recommendation> For(
        MachineProfile machine,
        CoreBenchResult cores,
        CompressionBenchResult compression,
        int designPopulation,
        Func<string, string?> readCurrent)
    {
        var results = new List<Recommendation>();

        // ── Sockets ─────────────────────────────────────────────────────────────────────
        string socketsNow = readCurrent("MultiSocketCount") ?? "1";
        if (!machine.SupportsReusePort)
        {
            results.Add(new Recommendation
            {
                Setting = "MultiSocketCount",
                File = SettingFile.Transport,
                CurrentValue = socketsNow,
                ProposedValue = "1",
                Evidence = Evidence.Derived,
                Rationale =
                    "This OS has no SO_REUSEPORT, so a second bind on the same port fails outright. " +
                    "Multi-socket is unavailable here - not merely less useful - and the runtime " +
                    "growth path is correspondingly inert. On Windows the receive path was measured " +
                    "at about 2% of server CPU at 500 players, so there is little to win even in " +
                    "principle.",
            });
        }
        else
        {
            int recommended = RecommendSocketCount(machine.LogicalCores);
            results.Add(new Recommendation
            {
                Setting = "MultiSocketCount",
                File = SettingFile.Transport,
                CurrentValue = socketsNow,
                ProposedValue = recommended.ToString(CultureInfo.InvariantCulture),
                Evidence = Evidence.Derived,
                Rationale =
                    $"{machine.LogicalCores} cores with SO_REUSEPORT available. Any value above 1 is what " +
                    "switches SO_REUSEPORT on for the primary socket, and that flag is the precondition for " +
                    "the entire runtime socket-growth path - at the default of 1 every rebalance declines to " +
                    "add a socket it is not permitted to add, silently, even while both of its triggers are " +
                    "firing. Each socket is an independent send path AND an extra receive thread, and one " +
                    "receive thread is one core's worth of syscall throughput before the kernel starts " +
                    $"discarding. {recommended} is a starting point sized to this core count; MaxSendSockets=0 " +
                    "will grow it further if the send path turns out to be what limits the box, and growth has " +
                    "to earn each socket. It is read once at socket bind, so this needs a full restart, not a " +
                    "config reload.",
            });
        }

        // ── Per-peer pass width ─────────────────────────────────────────────────────────
        // Derived from the measured knee rather than from single-core speed, which avoids needing a
        // reference machine to calibrate against: the knee says how many workers this box can
        // usefully run, and the setting is expressed as peers per worker, so the population it is
        // being fitted for closes the arithmetic.
        int peersPerWorker = RecommendPeersPerWorker(designPopulation, cores.KneeWorkers);
        results.Add(new Recommendation
        {
            Setting = "PeerUpdatePeersPerWorker",
            File = SettingFile.Transport,
            CurrentValue = readCurrent("PeerUpdatePeersPerWorker") ?? "0",
            ProposedValue = peersPerWorker.ToString(CultureInfo.InvariantCulture),
            Evidence = Evidence.Microbenchmark,
            Rationale =
                (cores.HasSharpKnee
                    ? $"Short parallel regions stop converting cores into work past {cores.KneeWorkers} workers on " +
                      "this machine - a measured boundary, with efficiency falling away sharply beyond it. "
                    : $"Efficiency decays smoothly here with no width the machine singles out; {cores.KneeWorkers} " +
                      "is the widest still holding most of the narrow-pool efficiency, which is a stated " +
                      "trade-off rather than a discovery. ") +
                $"Running the full {machine.LogicalCores} costs {cores.WidthPenalty:F2}x the CPU for the same " +
                $"output. At the {designPopulation:N0}-player design point, {peersPerWorker} peers per worker lands " +
                $"the pass on {cores.KneeWorkers} workers. The shipped default of 128 caps workers by population " +
                "rather than by the machine, which is why it leaves a large host underused and overloads a small one.",
        });

        // ── Compression ─────────────────────────────────────────────────────────────────
        if (!compression.ZstdDictionaryPresent)
        {
            results.Add(new Recommendation
            {
                Setting = "EnableAvatarBundleZstd",
                File = SettingFile.Server,
                CurrentValue = readCurrent("EnableAvatarBundleZstd") ?? "true",
                ProposedValue = "false",
                Evidence = Evidence.Microbenchmark,
                Rationale =
                    "This build embeds no zstd dictionary, so the codec is inert by design and every bundle " +
                    "falls through to LZ4 regardless of what this setting says. Turning it off makes the " +
                    "configuration say what is actually happening. Dictionary-less zstd is not a partial win " +
                    "here - the dictionary is where the ratio comes from on payloads this small.",
            });
        }
        else
        {
            var lz4 = compression.Lz4;
            var zstd = compression.BestZstd;
            results.Add(new Recommendation
            {
                Setting = "EnableAvatarBundleZstd",
                File = SettingFile.Server,
                CurrentValue = readCurrent("EnableAvatarBundleZstd") ?? "true",
                ProposedValue = compression.RecommendZstdEnabled ? "true" : "false",
                Evidence = Evidence.Microbenchmark,
                Rationale = compression.RecommendZstdEnabled
                    ? $"On this machine's cores zstd removes {zstd?.BytesSavedPerCoreMs:N0} bytes per core-millisecond " +
                      $"against LZ4's {lz4?.BytesSavedPerCoreMs:N0} - it is the better exchange rate, not merely the " +
                      "better ratio, which is the comparison that matters when the budget being spent is tick time."
                    : $"LZ4 gives the better exchange rate here ({lz4?.BytesSavedPerCoreMs:N0} bytes saved per " +
                      $"core-millisecond against zstd's {zstd?.BytesSavedPerCoreMs:N0}). Ratio alone would pick zstd; " +
                      "ratio alone is the wrong question when the cost is paid out of the tick budget.",
            });

            if (compression.RecommendZstdEnabled)
            {
                results.Add(new Recommendation
                {
                    Setting = "AvatarBundleZstdLevel",
                    File = SettingFile.Server,
                    CurrentValue = readCurrent("AvatarBundleZstdLevel") ?? "-2",
                    ProposedValue = compression.RecommendedZstdLevel.ToString(CultureInfo.InvariantCulture),
                    Evidence = Evidence.Microbenchmark,
                    Rationale =
                        $"Level {compression.RecommendedZstdLevel} maximises bytes removed per core-millisecond over " +
                        $"the corpus ({compression.Corpus.Label}). Note that the codec runs at " +
                        $"{zstd?.MegabytesPerSecond:N0} MB/s per core here, far above what the production " +
                        "milliseconds-per-tick figures imply - most of the time attributed to compression in a tick " +
                        "is buffer building, chunk selection and retries around the codec, not the codec call. So " +
                        "expect a level change to move the tick budget less than it moves this row.",
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Sockets to bind at startup, from the core count.
    ///
    /// <para>Bounded at both ends for different reasons. The floor is that anything above 1
    /// unlocks growth and 1 does not, so a machine that can use sockets at all should not start at
    /// the value that disables the mechanism. The ceiling is that each socket carries a receive
    /// thread, and threads that outnumber half the cores are competing with the work rather than
    /// serving it.</para>
    /// </summary>
    public static int RecommendSocketCount(int cores)
    {
        if (cores < 4) return 1;
        int ceiling = Math.Max(2, Math.Min(16, cores / 2));
        int wanted = Math.Max(4, cores / 8);
        return Math.Min(wanted, ceiling);
    }

    /// <summary>
    /// Peers per worker, so the pass lands on the measured knee at the design population.
    /// </summary>
    public static int RecommendPeersPerWorker(int designPopulation, int kneeWorkers)
    {
        if (kneeWorkers < 1) kneeWorkers = 1;
        int perWorker = Math.Max(1, designPopulation / kneeWorkers);

        // Held inside the range the setting was designed to span. Below 16 the pass is nearly one
        // worker per peer and pays more in coordination than it saves; above 512 the worker count
        // stops responding to population at all, which is the failure the setting exists to avoid.
        return Math.Clamp(perWorker, 16, 512);
    }
}
