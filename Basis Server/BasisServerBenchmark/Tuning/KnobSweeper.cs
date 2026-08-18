using Basis.Benchmark.Harness;
using Basis.Benchmark.Measure;

namespace Basis.Benchmark.Tuning;

/// <summary>The outcome of sweeping every eligible knob, plus the run that checked the combination.</summary>
public sealed class SweepResult
{
    public required RunResult Baseline { get; init; }
    public required IReadOnlyList<Recommendation> Recommendations { get; init; }
    public required RunResult? Confirmation { get; init; }
    public required Comparison? ConfirmationComparison { get; init; }
    public required IReadOnlyList<string> Skipped { get; init; }

    /// <summary>
    /// Whether the combined settings actually beat the baseline when run together.
    ///
    /// Null when there was nothing to confirm. False is a real and important outcome: it means the
    /// individually-measured wins did not survive being combined, and the whole set must be
    /// discarded rather than partly kept.
    /// </summary>
    public bool? Confirmed => ConfirmationComparison?.Verdict switch
    {
        null => null,
        Verdict.Worse => false,
        Verdict.Inconclusive => null,
        _ => true,
    };
}

/// <summary>
/// Measures each setting against the incumbent, one at a time, then checks the winners together.
///
/// <para><b>One at a time, then confirm.</b> A full factorial would be honest about interactions
/// and would also take days: eight knobs at four values each is 65,536 runs at roughly five
/// minutes apiece. Greedy single-factor sweeping is the affordable approximation, and its failure
/// mode is specific and known — it is wrong exactly when settings interact. So the interactions
/// that are already known are encoded as preconditions rather than discovered, and everything else
/// is caught by the confirmation run at the end, which pits the whole proposed set against the
/// original baseline. If the combination does not win, the set is discarded entire. A tuner that
/// skips that step ships the sum of eight local wins and has never once measured the thing it is
/// about to write.</para>
///
/// <para><b>Preconditions are checked, not assumed.</b> Sweeping a setting whose enabling
/// condition is unmet produces a clean, reproducible, meaningless null result — which reads
/// exactly like "this setting does not matter". The clearest case is socket growth: it is gated on
/// SO_REUSEPORT being set on the primary socket, which only happens when the socket count is above
/// one at bind time, so on a default config every trigger fires and every one declines.</para>
/// </summary>
public sealed class KnobSweeper
{
    private readonly LoadRunner _runner;
    private readonly ConfigPatcher _configs;
    private readonly Action<string> _log;
    private readonly bool _loopback;
    private readonly int _cores;
    private readonly long _memoryBytes;

    public KnobSweeper(LoadRunner runner, ConfigPatcher configs, bool loopback, int cores, long memoryBytes, Action<string> log)
    {
        _runner = runner;
        _configs = configs;
        _loopback = loopback;
        _cores = cores;
        _memoryBytes = memoryBytes;
        _log = log;
    }

    public SweepResult Run(RunOptions template, IReadOnlyList<Knob> knobs, CancellationToken cancel)
    {
        _log("\n  Baseline (shipped defaults at the design population)");
        RunResult baseline = _runner.Run(With(template, new Dictionary<string, string>(), "baseline"), cancel);

        if (!baseline.IsValid(out string? why))
        {
            _log($"  Baseline is not usable: {why}. Nothing can be compared against it.");
            return new SweepResult
            {
                Baseline = baseline,
                Recommendations = Array.Empty<Recommendation>(),
                Confirmation = null,
                ConfirmationComparison = null,
                Skipped = new[] { $"everything - baseline invalid ({why})" },
            };
        }

        IReadOnlyList<double> baselineSeries = baseline.Series(w => w.DeliveredPairHz);

        // The reference every candidate is judged against, and it MOVES as changes are accepted.
        // It has to: once knob A has been adopted, every later arm runs with A applied, so
        // comparing knob C against the original baseline would credit C with A's improvement and
        // adopt settings that do nothing. The baseline series is kept separately for the final
        // confirmation, which is the one comparison that must be against the untouched config.
        IReadOnlyList<double> referenceSeries = baselineSeries;

        var accepted = new Dictionary<string, string>();
        var recommendations = new List<Recommendation>();
        var skipped = new List<string>();

        foreach (Knob knob in knobs)
        {
            if (cancel.IsCancellationRequested) break;

            if (knob.Requires is { } requirement)
            {
                // Read the value this sweep will actually run with, which is the accepted one if an
                // earlier knob changed it, not whatever is on disc.
                string? effective = accepted.TryGetValue(requirement.Setting, out string? staged)
                    ? staged
                    : _configs.Read(requirement.Setting);

                if (!requirement.Holds(effective))
                {
                    skipped.Add($"{knob.Name}: {requirement.Reason} (currently {requirement.Setting}={effective ?? "unset"})");
                    _log($"  Skipping {knob.Name} - {requirement.Reason}.");
                    continue;
                }
            }

            IReadOnlyList<string> candidates = knob.ResolveCandidates(_cores, _memoryBytes);
            string incumbent = _configs.Read(knob.Name) ?? candidates.FirstOrDefault() ?? "";

            var alternatives = candidates.Where(c => !string.Equals(c, incumbent, StringComparison.OrdinalIgnoreCase)).ToList();
            if (alternatives.Count == 0)
            {
                skipped.Add($"{knob.Name}: no candidate values differ from the current {incumbent}");
                continue;
            }

            _log($"\n  Sweeping {knob.Name} (currently {incumbent}) across {string.Join(", ", alternatives)}");

            string bestValue = incumbent;
            IReadOnlyList<double> bestSeries = referenceSeries;
            Comparison? bestComparison = null;

            foreach (string candidate in alternatives)
            {
                if (cancel.IsCancellationRequested) break;

                var settings = new Dictionary<string, string>(accepted) { [knob.Name] = candidate };
                RunResult arm = _runner.Run(With(template, settings, $"{knob.Name}={candidate}"), cancel);

                if (!arm.IsValid(out string? armWhy))
                {
                    _log($"    {knob.Name}={candidate}: discarded - {armWhy}");
                    continue;
                }

                IReadOnlyList<double> series = arm.Series(w => w.DeliveredPairHz);
                Comparison comparison = Stats.Compare(bestSeries, series);
                _log($"    {knob.Name}={candidate}: {comparison.Describe(" Hz/pair")}");

                if (comparison.Verdict == Verdict.Better)
                {
                    bestValue = candidate;
                    bestSeries = series;
                    bestComparison = comparison;
                }
                else if (comparison.Verdict == Verdict.NoDifference && bestComparison == null)
                {
                    // Remember the null result so the report can say the setting was measured and
                    // found not to matter, which is more useful to the next person than silence.
                    bestComparison = comparison;
                }
            }

            bool changed = !string.Equals(bestValue, incumbent, StringComparison.OrdinalIgnoreCase);
            bool trustworthy = !_loopback || knob.Confidence == LoopbackConfidence.Honest;

            recommendations.Add(new Recommendation
            {
                Setting = knob.Name,
                File = knob.File,
                CurrentValue = incumbent,
                ProposedValue = bestValue,
                Evidence = !changed ? Evidence.NoChange
                    : trustworthy ? Evidence.Measured
                    : Evidence.UntrustedTopology,
                Rationale = !changed
                    ? $"No candidate beat {incumbent} by more than the run-to-run noise."
                    : trustworthy
                        ? $"{bestComparison?.Describe(" Hz/pair") ?? "measured better"} at {template.Players:N0} players."
                        : $"Measured better on loopback ({bestComparison?.Describe(" Hz/pair")}), but this setting " +
                          "cannot be measured honestly on a single box - the kernel does receive-side work inline " +
                          "in the sender and charges for bytes rather than datagrams. Re-run with clients off-box " +
                          "before adopting.",
                Comparison = bestComparison,
            });

            if (changed && trustworthy)
            {
                accepted[knob.Name] = bestValue;
                referenceSeries = bestSeries;
            }
        }

        // The step that makes the rest of it worth anything: run everything that was accepted,
        // together, against the same baseline the individual arms were judged against.
        RunResult? confirmation = null;
        Comparison? confirmationComparison = null;

        if (accepted.Count > 0 && !cancel.IsCancellationRequested)
        {
            _log($"\n  Confirming the combined set ({accepted.Count} change{(accepted.Count == 1 ? "" : "s")}) against the original baseline");
            confirmation = _runner.Run(With(template, accepted, "combined"), cancel);

            if (confirmation.IsValid(out string? confirmWhy))
            {
                confirmationComparison = Stats.Compare(baselineSeries, confirmation.Series(w => w.DeliveredPairHz));
                _log($"  Combined: {confirmationComparison.Describe(" Hz/pair")}");

                if (confirmationComparison.Verdict == Verdict.Worse)
                {
                    _log("  The combination is WORSE than the baseline. The individual wins did not survive being " +
                         "applied together, so the whole set is withdrawn.");
                    recommendations = recommendations
                        .Select(r => r.Evidence != Evidence.Measured ? r : r with
                        {
                            ProposedValue = r.CurrentValue,
                            Evidence = Evidence.NoChange,
                            Rationale = r.Rationale + " Withdrawn: the combined set measured worse than the baseline.",
                        })
                        .ToList();
                }
            }
            else
            {
                _log($"  Confirmation run unusable: {confirmWhy}");
            }
        }

        return new SweepResult
        {
            Baseline = baseline,
            Recommendations = recommendations,
            Confirmation = confirmation,
            ConfirmationComparison = confirmationComparison,
            Skipped = skipped,
        };
    }

    private static RunOptions With(RunOptions template, IReadOnlyDictionary<string, string> settings, string label) => new()
    {
        ServerDirectory = template.ServerDirectory,
        LoadClientDirectory = template.LoadClientDirectory,
        Players = template.Players,
        Warmup = template.Warmup,
        WindowLength = template.WindowLength,
        Windows = template.Windows,
        ConnectTimeout = template.ConnectTimeout,
        Settings = settings,
        HealthHost = template.HealthHost,
        HealthPort = template.HealthPort,
        HealthPath = template.HealthPath,
        Label = label,
    };
}
