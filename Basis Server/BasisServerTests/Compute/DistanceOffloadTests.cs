using Basis.Network.Core;
using Basis.Network.Core.Compute;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;
using Xunit.Abstractions;

namespace BasisServerTests;

/// <summary>
/// The compute offload, exercised through the same runtime lookup the server uses.
///
/// <para>These tests deliberately go through <see cref="BasisComputeBackend"/> rather than
/// constructing a solver directly. The server never names the backend's types either, so a
/// direct reference here would test a path that does not exist in production and would keep
/// passing after a rename that breaks the real one.</para>
///
/// <para>Every test is skippable rather than failing when there is no device, because most
/// machines that run this suite have none and that is the supported configuration.</para>
/// </summary>
public sealed class DistanceOffloadTests
{
    private const int BaseIntervalMs = 50;
    private const float HighDistanceSq = 100f;
    private const float MediumDistanceSq = 900f;
    private const float LowDistanceSq = 2500f;
    private const float BaseMultiplier = 1.0f;
    private const float IncreaseRate = 0.01f;

    private readonly ITestOutputHelper _output;

    public DistanceOffloadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Backend_LoadsOrExplainsWhyNot()
    {
        using IBasisDistanceSolver? solver = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        _output.WriteLine($"status: {BasisComputeBackend.Status}");

        Assert.False(string.IsNullOrWhiteSpace(BasisComputeBackend.Status));
        if (solver != null)
        {
            Assert.False(string.IsNullOrWhiteSpace(solver.Backend));
            Assert.False(string.IsNullOrWhiteSpace(solver.DeviceName));
        }
    }

    /// <summary>
    /// The tier a pair is served at must be identical on both backends. A disagreement here is a
    /// receiver getting the wrong avatar detail, which is the one difference that is visible.
    /// </summary>
    [Fact]
    public void QualityTiersMatchTheCpuExactly()
    {
        using IBasisDistanceSolver? solver = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        if (solver == null)
        {
            _output.WriteLine($"skipped: {BasisComputeBackend.Status}");
            return;
        }

        const int players = 512;
        BuildRoster(players, out float[] x, out float[] y, out float[] z);

        var request = new BasisDistanceSolveRequest
        {
            PosX = x,
            PosY = y,
            PosZ = z,
            PlayerCount = players,
            SliceStart = 0,
            SliceEnd = players,
            Parameters = Parameters(),
        };

        var interval = new byte[players * players];
        var quality = new byte[players * players];
        solver.Solve(ref request, interval, quality);

        long tierMismatches = 0;
        long intervalBeyondOneStep = 0;
        long intervalDiffering = 0;

        for (int i = 0; i < players; i++)
        {
            for (int j = 0; j < players; j++)
            {
                if (i == j) continue;

                float dx = x[i] - x[j];
                float dy = y[i] - y[j];
                float dz = z[i] - z[j];
                float distSq = dx * dx + dy * dy + dz * dz;

                byte expectedQuality = distSq <= HighDistanceSq ? (byte)3
                                     : distSq <= MediumDistanceSq ? (byte)2
                                     : distSq <= LowDistanceSq ? (byte)1 : (byte)0;

                int raw = (int)(BaseIntervalMs * (BaseMultiplier + (distSq * IncreaseRate)));
                byte expectedInterval = BasisNetworkCommons.EncodeAvatarIntervalByte(raw, BaseIntervalMs);

                long o = (long)i * players + j;
                if (quality[o] != expectedQuality) tierMismatches++;

                int difference = interval[o] - expectedInterval;
                if (difference != 0) intervalDiffering++;
                if (difference < -1 || difference > 1) intervalBeyondOneStep++;
            }
        }

        long pairs = (long)players * players - players;
        _output.WriteLine($"{solver.Backend} ({solver.DeviceName}) over {pairs:N0} pairs: " +
                          $"tier mismatches {tierMismatches}, interval differing {intervalDiffering}, " +
                          $"interval beyond one step {intervalBeyondOneStep}");

        Assert.Equal(0, tierMismatches);
        Assert.Equal(0, intervalBeyondOneStep);
    }

    /// <summary>
    /// A slice must produce the same answers as the full sweep for the receivers it covers, since
    /// the server only ever asks for slices and stitches a refresh out of them.
    /// </summary>
    [Fact]
    public void SliceAgreesWithTheFullSweep()
    {
        using IBasisDistanceSolver? solver = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        if (solver == null)
        {
            _output.WriteLine($"skipped: {BasisComputeBackend.Status}");
            return;
        }

        const int players = 256;
        const int sliceStart = 64;
        const int sliceEnd = 192;
        BuildRoster(players, out float[] x, out float[] y, out float[] z);

        var full = new BasisDistanceSolveRequest
        {
            PosX = x, PosY = y, PosZ = z,
            PlayerCount = players, SliceStart = 0, SliceEnd = players,
            Parameters = Parameters(),
        };
        var fullInterval = new byte[players * players];
        var fullQuality = new byte[players * players];
        solver.Solve(ref full, fullInterval, fullQuality);

        var slice = new BasisDistanceSolveRequest
        {
            PosX = x, PosY = y, PosZ = z,
            PlayerCount = players, SliceStart = sliceStart, SliceEnd = sliceEnd,
            Parameters = Parameters(),
        };
        var sliceInterval = new byte[(sliceEnd - sliceStart) * players];
        var sliceQuality = new byte[(sliceEnd - sliceStart) * players];
        solver.Solve(ref slice, sliceInterval, sliceQuality);

        for (int s = 0; s < sliceEnd - sliceStart; s++)
        {
            for (int j = 0; j < players; j++)
            {
                long fromFull = (long)(sliceStart + s) * players + j;
                long fromSlice = (long)s * players + j;
                Assert.Equal(fullInterval[fromFull], sliceInterval[fromSlice]);
                Assert.Equal(fullQuality[fromFull], sliceQuality[fromSlice]);
            }
        }
    }

    [Fact]
    public void DeviceSelector_ByIndex_PicksTheFirstDevice()
    {
        using IBasisDistanceSolver? auto = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        if (auto == null)
        {
            _output.WriteLine($"skipped: {BasisComputeBackend.Status}");
            return;
        }
        string autoName = auto.DeviceName;

        using IBasisDistanceSolver? byIndex = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "0");
        Assert.NotNull(byIndex);
        Assert.Equal(autoName, byIndex!.DeviceName);
    }

    /// <summary>
    /// A selector naming a device this host does not have must refuse, not quietly run somewhere
    /// else. Falling back would be indistinguishable from the setting having worked.
    /// </summary>
    [Fact]
    public void DeviceSelector_Unknown_RefusesRatherThanFallingBack()
    {
        using IBasisDistanceSolver? probe = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        if (probe == null)
        {
            _output.WriteLine($"skipped: {BasisComputeBackend.Status}");
            return;
        }

        using IBasisDistanceSolver? bogus = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "no-such-device-xyz");
        _output.WriteLine($"status: {BasisComputeBackend.Status}");

        Assert.Null(bogus);
        Assert.Contains("no-such-device-xyz", BasisComputeBackend.Status);
    }

    [Fact]
    public void DeviceSelector_OutOfRangeIndex_Refuses()
    {
        using IBasisDistanceSolver? probe = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "");
        if (probe == null)
        {
            _output.WriteLine($"skipped: {BasisComputeBackend.Status}");
            return;
        }

        using IBasisDistanceSolver? bogus = BasisComputeBackend.TryLoadDistanceSolver(BaseIntervalMs, "99");
        _output.WriteLine($"status: {BasisComputeBackend.Status}");

        Assert.Null(bogus);
        Assert.Contains("out of range", BasisComputeBackend.Status);
    }

    /// <summary>
    /// The faster refresh is only taken while a device is actually carrying the sweep. This is the
    /// rule that keeps a CPU-only host off a schedule fitted to hardware it does not have, and it
    /// has to hold at the moment the backend is dropped, not just at startup - so it is keyed off
    /// the live solver rather than off configuration.
    /// </summary>
    [Fact]
    public void RefreshPeriod_TracksWhetherADeviceIsActuallyCarryingTheSweep()
    {
        var type = typeof(BasisServerReductionSystemEvents);
        var solverField = type.GetField("_distanceSolver",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var effective = type.GetProperty("EffectiveDistanceIntervalTicks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(solverField);
        Assert.NotNull(effective);

        object? saved = solverField!.GetValue(null);
        int savedCpu = BasisServerReductionSystemEvents.DistanceUpdateIntervalTicks;
        int savedGpu = BasisServerReductionSystemEvents.ComputeDistanceUpdateIntervalTicks;
        try
        {
            BasisServerReductionSystemEvents.DistanceUpdateIntervalTicks = 125;
            BasisServerReductionSystemEvents.ComputeDistanceUpdateIntervalTicks = 32;

            solverField.SetValue(null, null);
            Assert.Equal(125, (int)effective!.GetValue(null)!);

            using IBasisDistanceSolver? solver = BasisComputeBackend.TryLoadDistanceSolver(50, "");
            if (solver == null)
            {
                _output.WriteLine($"no device; only the CPU half of this rule is exercised: {BasisComputeBackend.Status}");
                return;
            }

            solverField.SetValue(null, solver);
            Assert.Equal(32, (int)effective!.GetValue(null)!);

            // Losing the backend must put the period back on the spot.
            solverField.SetValue(null, null);
            Assert.Equal(125, (int)effective!.GetValue(null)!);
        }
        finally
        {
            solverField.SetValue(null, saved);
            BasisServerReductionSystemEvents.DistanceUpdateIntervalTicks = savedCpu;
            BasisServerReductionSystemEvents.ComputeDistanceUpdateIntervalTicks = savedGpu;
        }
    }

    private static BasisDistanceSolveParameters Parameters() => new()
    {
        HighDistanceSq = HighDistanceSq,
        MediumDistanceSq = MediumDistanceSq,
        LowDistanceSq = LowDistanceSq,
        BaseMultiplier = BaseMultiplier,
        IncreaseRate = IncreaseRate,
        BaseIntervalMs = BaseIntervalMs,
    };

    /// <summary>
    /// A crowd spread across the tier boundaries rather than uniformly, so the thresholds the
    /// quality comparison cares about are actually crossed.
    /// </summary>
    private static void BuildRoster(int players, out float[] x, out float[] y, out float[] z)
    {
        var rng = new Random(9081);
        x = new float[players];
        y = new float[players];
        z = new float[players];
        for (int i = 0; i < players; i++)
        {
            x[i] = (float)(rng.NextDouble() * 120.0 - 60.0);
            y[i] = (float)(rng.NextDouble() * 4.0);
            z[i] = (float)(rng.NextDouble() * 120.0 - 60.0);
        }
    }
}
