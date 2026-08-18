namespace Basis.Benchmark.Tuning;

/// <summary>Which XML file declares a setting.</summary>
public enum SettingFile
{
    /// <summary>config/config.xml</summary>
    Server,

    /// <summary>config/transports/litenetlib.xml</summary>
    Transport,
}

/// <summary>
/// How much a single-box loopback run can be trusted about a given setting.
///
/// <para>This exists because loopback lies, and it lies selectively rather than uniformly. The
/// kernel performs the receive-side work inline inside the sender's <c>sendto</c>, and that cost
/// scales with bytes rather than with datagrams — so on loopback a change that removes 45% of the
/// datagrams while moving the same bytes measures as approximately zero, and the same change is a
/// real win over a NIC. Meanwhile the CPU-side findings — parallel widths, codec cost, allocation
/// behaviour — measure honestly, because none of them route through that path.</para>
///
/// <para>Tagging each setting is what lets one tool serve both topologies without quietly
/// converting a topology artifact into a baked default. Untrusted settings are still measured and
/// still reported; they are just never written.</para>
/// </summary>
public enum LoopbackConfidence
{
    /// <summary>Measures the same on loopback as over a NIC. Safe to bake from a single-box run.</summary>
    Honest,

    /// <summary>Directionally right on loopback, magnitude understated. Bake only large effects.</summary>
    Degraded,

    /// <summary>Loopback cannot measure this at all. Never bake from a single-box run.</summary>
    Untrusted,
}

/// <summary>One tunable setting, its candidate values, and what it takes to measure it.</summary>
public sealed record Knob
{
    public required string Name { get; init; }
    public required SettingFile File { get; init; }
    public required string Summary { get; init; }
    public required LoopbackConfidence Confidence { get; init; }

    /// <summary>Values to try, in the order they should be tried. The first is the shipped default.</summary>
    public required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>
    /// Whether changing this needs a server restart. Almost everything here does, and the ones
    /// that do not are still restarted between arms so every arm starts from the same cold state.
    /// </summary>
    public bool RestartRequired { get; init; } = true;

    /// <summary>
    /// A setting that must already hold a particular value before this one does anything at all,
    /// with the reason. Sweeping a knob whose precondition is unmet produces a clean, repeatable,
    /// meaningless null result.
    /// </summary>
    public (string Setting, Func<string?, bool> Holds, string Reason)? Requires { get; init; }

    /// <summary>Candidates derived from this machine rather than fixed, e.g. multiples of the core count.</summary>
    public Func<int, long, IReadOnlyList<string>>? CandidatesFor { get; init; }

    public IReadOnlyList<string> ResolveCandidates(int cores, long memoryBytes) =>
        CandidatesFor?.Invoke(cores, memoryBytes) ?? Candidates;
}

/// <summary>
/// Everything this tool is willing to change, and why each one is on the list.
///
/// <para>The list is short on purpose. The server already resolves most of its own numbers at
/// runtime — core shares through a lease allocator that measures its own ceilings, queue and pool
/// bounds through a population-and-memory scaler, send sockets through a growth loop that has to
/// earn each one. Re-deriving those offline would replace a value that adapts with one that does
/// not, which is worse even when the offline number is better on the day it was measured.</para>
///
/// <para>So what remains is exactly the settings that <em>cannot</em> self-tune: values read once
/// at boot before any load exists to learn from, constants fitted on one machine and shipped to
/// every other, and trade-offs with no in-process feedback signal to close the loop on.</para>
/// </summary>
public static class KnobCatalog
{
    public static bool IsTransportSetting(string name) =>
        All.FirstOrDefault(k => k.Name == name)?.File == SettingFile.Transport;

    public static Knob? Find(string name) => All.FirstOrDefault(k => k.Name == name);

    public static readonly IReadOnlyList<Knob> All = new List<Knob>
    {
        new()
        {
            Name = "MultiSocketCount",
            File = SettingFile.Transport,
            Confidence = LoopbackConfidence.Untrusted,
            Summary =
                "Sockets bound at startup. THE highest-value setting here and the only one that is " +
                "structurally load-bearing: SO_REUSEPORT has to be set on the primary socket before " +
                "bind, so this is read once at Start() and can never be raised later. At the default " +
                "of 1 the entire MaxSendSockets growth path silently no-ops - every rebalance " +
                "declines to add a socket it is not allowed to add - so a server can sit at 15% CPU " +
                "with the reduction system maximally degraded and the kernel discarding hundreds of " +
                "thousands of datagrams per 10s, and nothing in the logs names the cause.",
            Candidates = new[] { "1" },
            CandidatesFor = (cores, _) =>
            {
                var values = new List<string> { "1" };
                foreach (int n in new[] { 4, 8, 16, 32, 64 })
                    if (n <= cores) values.Add(n.ToString());
                return values;
            },
        },

        new()
        {
            Name = "PeerUpdatePeersPerWorker",
            File = SettingFile.Transport,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Peers each worker in the transport's per-peer pass covers; lower means more workers " +
                "for the same crowd. This is what decides how much of a large host the server can " +
                "actually use, and the shipped 128 was fitted to a 32-thread machine with fast cores. " +
                "Because it caps workers by population rather than by the machine, at 4000 peers it " +
                "picks 31 workers however many cores exist - so a 128-core host sits near a quarter " +
                "utilisation. Slower cores want a lower value, since a worker gets through fewer peers.",
            Candidates = new[] { "0", "32", "64", "128", "256" },
        },

        new()
        {
            Name = "PeerUpdateParallelism",
            File = SettingFile.Transport,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Worker ceiling for the per-peer pass; 0 derives it from the core count. The pass runs " +
                "hundreds of times a second and does little per peer, so past a point the scheduler's " +
                "own machinery costs more than the work: a 500-player profile found 40 threads in this " +
                "pass and three quarters of all GC-poll time coming from the parallel plumbing rather " +
                "than the loop body.",
            Candidates = new[] { "0" },
            CandidatesFor = (cores, _) =>
            {
                var values = new List<string> { "0" };
                foreach (int n in new[] { 4, 8, 16, 32 })
                    if (n <= cores) values.Add(n.ToString());
                return values;
            },
        },

        new()
        {
            Name = "MergeHoldMs",
            File = SettingFile.Transport,
            Confidence = LoopbackConfidence.Untrusted,
            Summary =
                "How long a partly-filled merge buffer may wait for more data. A full buffer is always " +
                "sent immediately, so this only ever delays small sends and caps latency rather than " +
                "adding it. Measured at 500 players with identical bytes on the wire throughout: 0 ms " +
                "= 175K datagrams/s at under half the MTU, 3 ms = 147K, 8 ms = 96K at ~79% fill. " +
                "Loopback cannot see any of this - the cost the reduction is paid in scales with bytes " +
                "there, not datagrams - so it is measured over a NIC or not at all.",
            Candidates = new[] { "3", "0", "1", "5", "8" },
        },

        new()
        {
            Name = "DistanceUpdateIntervalTicks",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Ticks one full refresh of the per-pair distance cache is spread over. This is the " +
                "setting the compute-offload question actually turns on: moving the sweep to a device " +
                "saves almost no CPU, so the only thing a cheaper sweep can buy is a shorter period, " +
                "and this measures whether a shorter period is worth anything. At the shipped 125 a " +
                "player moving 5 m/s travels about 12 m between refreshes, which is more than the " +
                "whole High-quality radius, so pairs are served at a tier fitted to where they used to " +
                "be. Lower is fresher and quadratically more expensive; if delivered pair-Hz does not " +
                "move across these arms then the sweep is not what limits quality and no offload of it " +
                "is worth building.",
            Candidates = new[] { "125", "64", "32", "250" },
        },

        new()
        {
            Name = "AvatarBundleZstdLevel",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Compression level for the zstd half of the bundle codec. Negative levels are the fast " +
                "end and are where a real-time send path belongs. The right value is a property of the " +
                "host's cores, not of the codec, and the offline codec benchmark answers it far more " +
                "cheaply than a load run can.",
            Candidates = new[] { "-2", "-5", "-3", "-1" },
        },

        new()
        {
            Name = "EnableAvatarBundleZstd",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Whether the zstd half of the hybrid codec runs at all. Inert without an embedded " +
                "dictionary, so on a build that has none this must stay off however good the level " +
                "sweep looks.",
            Candidates = new[] { "true", "false" },
        },

        new()
        {
            Name = "AvatarBundleMinMessages",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Fewest messages a bundle must hold before it is worth compressing. Guards the case " +
                "where the codec is paid to look at data that cannot compress: bundling scales the " +
                "opposite way to intuition - 1.4% of messages bundle at 500 players and 99.3% at 4000 " +
                "- so this setting does almost nothing on a small instance and a great deal on a full " +
                "one.",
            Candidates = new[] { "2", "1", "4", "8" },
        },

        new()
        {
            Name = "BSRMaxDegreeOfParallelism",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Worker ceiling for the reduction system's send phase; 0 lets the core-budget allocator " +
                "own it. Pinning it is almost always wrong and is included mainly so the sweep can " +
                "confirm that: the send phase is throughput-bound and already rate-limited by the tick " +
                "budget, so extra workers cannot make it deliver sooner, and a pin also caps the pool " +
                "below what extra send sockets would otherwise unlock.",
            Candidates = new[] { "0" },
            CandidatesFor = (cores, _) =>
            {
                var values = new List<string> { "0" };
                foreach (int n in new[] { 4, 8, 16 })
                    if (n <= cores) values.Add(n.ToString());
                return values;
            },
        },

        new()
        {
            Name = "BSRSendPhaseBudgetPercent",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Honest,
            Summary =
                "Share of the reduction tick the send pass is sized against, as a percentage; 0 = the " +
                "shipped 60. The send pool's width is already measured - the server times its own pass " +
                "and knows how many pairs a worker gets through per millisecond - so this is the other " +
                "half of that sum, the milliseconds it is allowed to spend, and rate times milliseconds " +
                "is the worker count. What it has to leave behind is the drain, message processing, the " +
                "distance slice and the transport kick, and what those cost is a property of the box: 60 " +
                "was fitted where they came to about 30% of the period. Set too high the send pass fits " +
                "its budget while the tick overruns anyway and the load controller shows up as player " +
                "shedding with nothing pointing here. Normally derived rather than swept - the run " +
                "measures the non-send phases directly at the design population, which is both cheaper " +
                "and less noisy than four arms - so this list is the fallback for a profile-only run.",
            Candidates = new[] { "0", "45", "70", "80" },
        },

        new()
        {
            Name = "MaxSendSockets",
            File = SettingFile.Transport,
            Confidence = LoopbackConfidence.Untrusted,
            Summary =
                "Ceiling on sockets the server may add at runtime when the network path is what limits " +
                "it. 0 derives it from the core count, which is almost always right. Included so the " +
                "report can state whether growth was even available.",
            Candidates = new[] { "0" },
            Requires = ("MultiSocketCount",
                v => int.TryParse(v, out int n) && n > 1,
                "socket growth needs SO_REUSEPORT on the primary socket, which is only set when " +
                "MultiSocketCount is above 1 at bind time"),
        },

        new()
        {
            Name = "BSRSMillisecondDefaultInterval",
            File = SettingFile.Server,
            Confidence = LoopbackConfidence.Degraded,
            Summary =
                "Base period of the reduction tick. Sets the ceiling on how often any pair can be " +
                "updated, so it is the top end of the quality the instance can deliver - and the floor " +
                "under how much CPU it will spend trying.",
            Candidates = new[] { "50", "33", "40", "66" },
        },
    };
}
