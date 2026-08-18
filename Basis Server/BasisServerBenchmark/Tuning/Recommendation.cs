using Basis.Benchmark.Measure;

namespace Basis.Benchmark.Tuning;

/// <summary>Why a recommendation is being made, which decides whether it may be written.</summary>
public enum Evidence
{
    /// <summary>Measured under load on this machine, and it beat the incumbent by more than the noise.</summary>
    Measured,

    /// <summary>Derived from a machine fact that cannot be wrong (cores, memory, kernel support).</summary>
    Derived,

    /// <summary>Measured offline, without load. Real, but narrower than a load run.</summary>
    Microbenchmark,

    /// <summary>
    /// Measured, but on a topology that cannot measure it honestly. Reported, never written.
    /// </summary>
    UntrustedTopology,

    /// <summary>Nothing distinguished the candidates. The incumbent stands.</summary>
    NoChange,
}

/// <summary>One setting, one proposed value, and the evidence behind it.</summary>
public sealed record Recommendation
{
    public required string Setting { get; init; }
    public required SettingFile File { get; init; }
    public required string CurrentValue { get; init; }
    public required string ProposedValue { get; init; }
    public required Evidence Evidence { get; init; }
    public required string Rationale { get; init; }
    public Comparison? Comparison { get; init; }

    public bool IsChange => !string.Equals(CurrentValue, ProposedValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this may be written into a config file.
    ///
    /// <para><see cref="Evidence.UntrustedTopology"/> is the case this gate exists for. A
    /// single-box run still produces a number for the socket and packet-rate settings, and that
    /// number is not noise — it is a real measurement of a machine talking to itself, where the
    /// kernel does the receive work inline in the sender and charges for bytes rather than
    /// datagrams. Writing it would bake a property of loopback into a config that will run over a
    /// NIC. So it is measured, reported with its caveat, and left out of the file.</para>
    /// </summary>
    public bool Writable => Evidence is Evidence.Measured or Evidence.Derived or Evidence.Microbenchmark;
}
