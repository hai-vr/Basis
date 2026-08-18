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
        Func<string, string?> readCurrent,
        CapacityResult? capacity = null)
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
                    : cores.IsRange
                        ? $"Efficiency decays smoothly, so the machine singles out no width: anything from " +
                          $"{cores.KneeWorkers} to {cores.WidestUsefulWorkers} workers is defensible and this takes " +
                          "the conservative end. PROVISIONAL - expect it to move between runs, since a busy box " +
                          "costs a wide pool more than a narrow one. A load sweep supersedes it. "
                        : $"Efficiency decays smoothly here with no width the machine singles out; {cores.KneeWorkers} " +
                          "is the widest still holding most of the narrow-pool efficiency, which is a stated " +
                          "trade-off rather than a discovery. ") +
                $"Running the full {machine.LogicalCores} costs {cores.WidthPenalty:F2}x the CPU for the same " +
                $"output. At the {designPopulation:N0}-player design point, {peersPerWorker} peers per worker lands " +
                $"the pass on {cores.KneeWorkers} workers. The shipped default of 128 caps workers by population " +
                "rather than by the machine, which is why it leaves a large host underused and overloads a small one.",
        });

        // ── Send-phase budget share ─────────────────────────────────────────────────────
        // Fitted, not swept. The server publishes what its send pass costs and what its whole tick
        // costs, and the difference is what the phases sharing that tick cost - which is precisely
        // the quantity this setting has to leave room for. Sweeping four candidates would spend
        // twenty minutes arriving at a noisier version of a subtraction the ladder has already
        // paid for.
        Recommendation? budget = RecommendSendBudget(capacity, designPopulation, readCurrent);
        if (budget != null) results.Add(budget);

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
                    "configuration say what is actually happening. The zstd rows above are dictionary-less " +
                    "and are not evidence either way about the configuration that would actually run - train " +
                    "a dictionary from a real capture (see BundleDictionaryTrainer) and re-run before reading " +
                    "them as a verdict on the codec.",
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
    /// Headroom left unclaimed by either the send pass or the phases beside it, as a fraction of
    /// the period.
    ///
    /// <para>Not a safety margin in the vague sense - it is what the tick has to absorb a GC pause,
    /// a join burst or a slice change in without missing its period, and missing the period is what
    /// starts the load controller shedding. Ten points was chosen against the measured oscillation
    /// of the slicing controller, which moves the non-send cost by several points on its own.</para>
    /// </summary>
    private const double TickHeadroomShare = 0.10;

    /// <summary>Narrowest and widest share worth writing. Matches the server's own clamp.</summary>
    private const int MinBudgetPercent = 20;
    private const int MaxBudgetPercent = 85;

    /// <summary>
    /// Fits the send pass's share of the tick from what the rest of the tick was measured to cost
    /// at the design population.
    ///
    /// <para>The arithmetic is one subtraction, and the care is all in which two numbers go into
    /// it. The obvious reading - how full the send pass's own budget looked - is the wrong one and
    /// is unstable in the direction that hides it: widening the budget widens the pool, the pass
    /// gets shorter, its duty falls, and the next run reads the new value as roomy and widens
    /// again. The non-send phases do not respond to the send pool's width at all, so a share
    /// derived by subtracting them stays put once written, which is the only way an offline fit for
    /// a runtime-adaptive system can be honest.</para>
    ///
    /// <para>Both inputs are the server's own 0.9/0.1 EMAs on the same per-tick cadence, so they
    /// are comparably smoothed - but tick time is heavy-tailed and an EMA of it reads high (the
    /// reduction system's own comments record 18 ms against a 13.2 ms real average at 2000
    /// players). The tail lands in the tick total and only partly in the send phase, so the
    /// subtraction overstates the non-send cost slightly and the fitted share comes out slightly
    /// narrow. That is the safe direction - a send budget erring small overruns nothing - and it
    /// is why this is a fit rather than an exact split.</para>
    ///
    /// <para>Returns null rather than a guess whenever the ladder cannot support the subtraction:
    /// a server predating the health fields, a design point too idle to have a meaningful tick
    /// cost, or a reading outside what a tick can physically contain.</para>
    /// </summary>
    private static Recommendation? RecommendSendBudget(
        CapacityResult? capacity,
        int designPopulation,
        Func<string, string?> readCurrent)
    {
        LadderRung? rung = capacity?.Rungs.FirstOrDefault(r => r.Players == designPopulation)
                           ?? capacity?.Rungs.LastOrDefault();
        if (rung == null) return null;

        double reportedShare = rung.Result.Median(w => w.SendBudgetPercent);
        if (reportedShare <= 0) return null;   // server build predates the field

        double nonSend = rung.Result.Median(w => w.NonSendShareOfPeriod);
        double tickDuty = rung.Result.Median(w => w.TickMs) / Math.Max(1.0, rung.Result.Median(w => (double)w.IntervalMs));

        // A tick that spends nothing outside the send pass is not a discovery about this machine,
        // it is a reading taken while nothing was happening - and the value it implies (85, the
        // ceiling) would be written onto a box that has never been loaded.
        if (nonSend <= 0.02 || nonSend >= 1.0 || tickDuty <= 0) return null;

        int fitted = (int)Math.Round((1.0 - nonSend - TickHeadroomShare) * 100 / 5.0) * 5;
        fitted = Math.Clamp(fitted, MinBudgetPercent, MaxBudgetPercent);

        string current = readCurrent("BSRSendPhaseBudgetPercent") ?? "0";
        double sendShare = tickDuty - nonSend;

        return new Recommendation
        {
            Setting = "BSRSendPhaseBudgetPercent",
            File = SettingFile.Server,
            Evidence = Evidence.Derived,
            CurrentValue = current,
            ProposedValue = fitted.ToString(CultureInfo.InvariantCulture),
            Rationale =
                $"At {rung.Players:N0} players the tick spent {tickDuty:P0} of its period working, of which the send " +
                $"pass took {sendShare:P0} and everything else - the queue drain, message processing, the distance " +
                $"slice, the transport kick - took {nonSend:P0}. Those other phases are what a send budget is a share " +
                $"of, and unlike the send pass they do not get cheaper when the pool widens, so {nonSend:P0} plus " +
                $"{TickHeadroomShare:P0} of headroom is what has to stay out of this number: {fitted}. The shipped 60 " +
                (nonSend > 0.30
                    ? $"assumes they cost about 30%, and here they cost {nonSend:P0} - so the send pool was being sized " +
                      "for a slice of the tick this box does not have, and the overrun that produces is answered by " +
                      "shedding players rather than by anything that names the cause."
                    : $"assumes they cost about 30%, and here they cost only {nonSend:P0} - so there is period going " +
                      "unused that the send pool could be sized into.") +
                $" Measured with the server running at {reportedShare:F0}%; the figure it is derived from does not " +
                "move when that changes, which is why it is derived this way round.",
        };
    }

    /// <summary>
    /// The player cap, from what the machine was measured to actually serve.
    ///
    /// <para><b>The most consequential thing this tool can write, and the easiest to leave out.</b>
    /// <c>PeerLimit</c> ships at 65535 — effectively uncapped — so a server admits everyone who
    /// knocks and then discovers it cannot serve them. That failure is quiet and collective: past
    /// capacity the reduction system sheds avatar updates across the whole roster, so a room of
    /// 2,000 does not fail for the last 500 to arrive, it degrades for all 2,000 at once. Capping
    /// at what the box was measured to deliver converts that into the honest failure instead, where
    /// the players who get in have a working session and the rest are told the server is full.</para>
    ///
    /// <para>Set to the full-quality ceiling, not to the largest population that stayed up. Those
    /// are different numbers and the gap between them is exactly the region where the server is
    /// still running and no longer delivering — which is a state to keep out of, not to sell.</para>
    /// </summary>
    public static Recommendation? RecommendPeerLimit(CapabilityModel model, Func<string, string?> readCurrent)
    {
        if (!model.HasData || model.FullQualityPlayers <= 0) return null;

        int limit = model.FullQualityPlayers;
        Ceiling binding = model.Binding();

        // A physical ceiling below the measured quality ceiling wins: the box may deliver well at
        // this population and still be unable to hold it once memory or the link is accounted for.
        if (binding.Constraint != BindingConstraint.Quality && binding.Players > 0 && binding.Players < limit)
            limit = binding.Players;

        string current = readCurrent("PeerLimit") ?? "65535";
        if (int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int existing)
            && existing > 0 && existing < limit)
        {
            // An operator who already capped lower than the measurement meant it - they may be
            // sharing the box, or selling a quality level above what the hardware merely survives.
            return new Recommendation
            {
                Setting = "PeerLimit",
                File = SettingFile.Server,
                CurrentValue = current,
                ProposedValue = current,
                Evidence = Evidence.NoChange,
                Rationale =
                    $"Already capped at {existing:N0}, below the {limit:N0} this machine was measured to serve well. " +
                    "Left alone - a cap tighter than the hardware is a deliberate choice, not a mistake to correct.",
            };
        }

        return new Recommendation
        {
            Setting = "PeerLimit",
            File = SettingFile.Server,
            CurrentValue = current,
            ProposedValue = limit.ToString(CultureInfo.InvariantCulture),
            Evidence = Evidence.Measured,
            Rationale =
                $"This machine delivered full quality to {model.FullQualityPlayers:N0} players" +
                (model.KneeFound ? "" : " and the ladder stopped there rather than finding a limit, so that figure is a floor") +
                (limit < model.FullQualityPlayers
                    ? $", but {Describe(binding.Constraint)} is fitted to run out at {binding.Players:N0}, so that is the cap. " +
                      "Those two do not contradict each other: the load clients shared this machine, so the traffic " +
                      "that served those players never crossed the resource the fitted limit is measured against. The " +
                      "bytes were real, the path was not, and a real deployment is held to the lower figure."
                    : ".") +
                $" The shipped default is 65,535, which is no cap at all: the server admits everyone and then sheds " +
                "avatar updates across the entire roster, so an overfull room degrades for every player in it rather " +
                "than turning the excess away. Capping at the measured figure keeps the sessions that do get in " +
                "working." +
                (binding.Extrapolated && limit == binding.Players
                    ? " Note this ceiling is extrapolated beyond the populations actually run."
                    : ""),
        };
    }

    /// <summary>
    /// The auth window, from a measured join burst.
    ///
    /// <para>This is the setting the ladder can never fit, because admission and steady state are
    /// different subsystems under different pressure — a box comfortable at 2,000 players can still
    /// be unable to get 2,000 players in. The failure is a race that only exists during a burst:
    /// every client in the queue is holding a half-open handshake while the server works through
    /// the ones ahead of it, and the timeout is running the whole time. It has bitten here before,
    /// with 596 of 4,000 clients unable to finish inside the window after a restart, and the only
    /// log line said they were not in the authenticated set — the symptom, not the cause.</para>
    /// </summary>
    public static Recommendation? RecommendAuthTimeout(
        Harness.AdmissionResult? admission, Func<string, string?> readCurrent)
    {
        if (admission is not { Completed: true } || admission.Requested <= 0) return null;
        if (admission.SecondsToFull <= 0) return null;

        int required = Harness.AdmissionBurst.RequiredBaseTimeoutMs(
            admission.Requested, admission.WorstCaseWaitSeconds);

        string current = readCurrent("AuthValidationTimeOutMiliseconds") ?? "9000";
        bool everyoneIn = admission.EveryoneGotIn;

        if (int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int existing)
            && existing >= required)
        {
            return new Recommendation
            {
                Setting = "AuthValidationTimeOutMiliseconds",
                File = SettingFile.Server,
                CurrentValue = current,
                ProposedValue = current,
                Evidence = Evidence.NoChange,
                Rationale =
                    $"{admission.Admitted:N0} of {admission.Requested:N0} clients were admitted in " +
                    $"{admission.SecondsToFull:F1}s ({admission.AverageRatePerSecond:N0}/s). The window already at " +
                    $"{existing:N0} ms covers the {required:N0} ms that implies, so it is left alone.",
            };
        }

        return new Recommendation
        {
            Setting = "AuthValidationTimeOutMiliseconds",
            File = SettingFile.Server,
            CurrentValue = current,
            ProposedValue = required.ToString(CultureInfo.InvariantCulture),
            Evidence = Evidence.Measured,
            Rationale =
                $"A join burst admitted {admission.Admitted:N0} of {admission.Requested:N0} clients in " +
                $"{admission.SecondsToFull:F1}s, averaging {admission.AverageRatePerSecond:N0}/s" +
                (everyoneIn ? "" : $" - {admission.Requested - admission.Admitted:N0} never got in at all") +
                $". The last client in that queue waits the whole {admission.SecondsToFull:F1}s while its handshake is " +
                "being timed, so the window has to cover it. Doubled for headroom, because this burst was the good " +
                "case: every client was on this machine over loopback with no loss and no retransmits, and a real " +
                "herd arrives over a network with all three. The server's own per-peer widening is subtracted out so " +
                $"this is not double-counted, which leaves {required:N0} ms. A longer window does mean more half-open " +
                "auth state held at once, which is the cost being accepted here.",
        };
    }

    private static string Describe(BindingConstraint constraint) => constraint switch
    {
        BindingConstraint.Cpu => "CPU",
        BindingConstraint.Memory => "memory",
        BindingConstraint.Bandwidth => "link bandwidth",
        _ => "capacity",
    };

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
