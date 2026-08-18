using Basis.Network.Core.Compression;
using Xunit;
using Xunit.Abstractions;

namespace BasisServerTests;

/// <summary>
/// Characterization of the RENDERED velocity ripple a strafe produces, as a function of how finely
/// hips world position is quantized on the wire.
///
/// v48 cut High-quality position from 3 x float32 to 3 x signed int24 MILLIMETRES. A constant-velocity
/// strafe does not move a whole number of millimetres per snapshot, so the snapshot sequence beats
/// between two integer step sizes — and the receiver's Catmull-Rom spline takes its tangent at p1 from
/// (p2 - p0)/2, so a one-step error on a NEIGHBOUR perturbs the velocity at the current point. This
/// measures how much of that survives to the rendered frame.
///
/// The spline here is a faithful copy of BasisRemoteInterpolationCore.Position — that type lives in
/// com.basis.framework and depends on Unity.Mathematics, so it cannot be referenced from these tests.
/// Kept to four lines and checked against the original.
/// </summary>
public class PositionQuantizationExperiment
{
    private readonly ITestOutputHelper _out;
    public PositionQuantizationExperiment(ITestOutputHelper output) => _out = output;

    /// <summary>Catmull-Rom position of the p1→p2 segment at t in [0,1]. Mirrors the receiver exactly.</summary>
    static double Spline(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * ((2.0 * p1)
            + (-p0 + p2) * t
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    sealed class Result
    {
        public double MeanVel, MinVel, MaxVel, RipplePct, PeakDevMmPerSec;
    }

    /// <param name="quantumMm">Wire quantum in millimetres; 0 = unquantized (the pre-v48 float32 case).</param>
    static Result Measure(double metresPerSecond, double quantumMm, double snapHz = 11.0, double renderHz = 90.0)
    {
        const int snaps = 400;
        var snap = new double[snaps];
        for (int k = 0; k < snaps; k++)
        {
            double trueX = metresPerSecond * (k / snapHz) * 1000.0;               // mm
            snap[k] = quantumMm <= 0 ? trueX : Math.Round(trueX / quantumMm) * quantumMm;
        }

        // Render the middle of the sequence so p0/p3 always exist.
        double dt = 1.0 / renderHz;
        int firstRender = (int)(2 * renderHz / snapHz) + 1;
        int lastRender = (int)((snaps - 3) * renderHz / snapHz);

        double prev = 0; bool have = false;
        var r = new Result { MinVel = double.MaxValue, MaxVel = double.MinValue };
        double sum = 0; int n = 0;

        for (int i = firstRender; i < lastRender; i++)
        {
            double tSec = i * dt;
            double u = tSec * snapHz;
            int k = (int)Math.Floor(u);
            double t = u - k;
            double pos = Spline(snap[k - 1], snap[k], snap[k + 1], snap[k + 2], t);

            if (have)
            {
                double vel = (pos - prev) / dt;      // mm/s
                r.MinVel = Math.Min(r.MinVel, vel);
                r.MaxVel = Math.Max(r.MaxVel, vel);
                sum += vel; n++;
            }
            prev = pos; have = true;
        }
        r.MeanVel = sum / n;
        double trueVel = metresPerSecond * 1000.0;
        r.PeakDevMmPerSec = Math.Max(Math.Abs(r.MaxVel - trueVel), Math.Abs(r.MinVel - trueVel));
        r.RipplePct = (r.MaxVel - r.MinVel) / trueVel * 100.0;
        return r;
    }

    [Fact]
    public void MeasureRenderedVelocityRippleVsPositionPrecision()
    {
        double[] speeds = { 0.3, 0.8, 1.5, 3.0 };
        (string name, double mm)[] quanta =
        {
            ("float32 (pre-v48)", 0),
            ("1.00 mm (v48, current)", 1.0),
            ("0.50 mm", 0.5),
            ("0.25 mm", 0.25),
            ("0.125 mm", 0.125),
            ("0.10 mm", 0.1),
        };

        _out.WriteLine("Rendered velocity ripple for a constant-velocity strafe.");
        _out.WriteLine("11 Hz snapshots -> Catmull-Rom -> 90 Hz render. True acceleration is zero,");
        _out.WriteLine("so every mm/s of spread below is introduced by position quantization.");
        _out.WriteLine("");

        foreach (var (name, mm) in quanta)
        {
            _out.WriteLine($"== {name} ==");
            _out.WriteLine("   m/s | true mm/s |  min mm/s |  max mm/s | peak dev | ripple");
            foreach (double sp in speeds)
            {
                var r = Measure(sp, mm);
                _out.WriteLine($"  {sp,4:F1} | {sp * 1000,9:F1} | {r.MinVel,9:F1} | {r.MaxVel,9:F1} | " +
                               $"{r.PeakDevMmPerSec,8:F1} | {r.RipplePct,5:F1}%");
            }
            _out.WriteLine("");
        }

        _out.WriteLine("For scale: project_basis_remote_jitter_quantization measured the bone-quantization");
        _out.WriteLine("shimmer that motivated the 10->12 bit change at 17.75 mm/s, and considered");
        _out.WriteLine("4.5 mm/s an acceptable result.");
    }

    /// <summary>
    /// Isolates the mechanism: with the spline's tangent taken from (p2 - p0)/2, a one-quantum error on
    /// a NEIGHBOURING snapshot moves the velocity at the current point by quantum * snapHz / 2 — so the
    /// ripple scales with the snapshot rate, not just the quantum.
    /// </summary>
    [Fact]
    public void RippleScalesWithQuantumTimesSnapshotRate()
    {
        // Deliberately incommensurate: at a speed whose per-snapshot step IS a whole number of
        // quanta the quantizer is exact and the ripple is zero, which measures nothing. 0.83 m/s
        // gives a fractional step at both rates.
        foreach (double snapHz in new[] { 11.0, 20.0 })
        {
            var oneMm = Measure(0.83, 1.0, snapHz);
            var quarterMm = Measure(0.83, 0.25, snapHz);
            _out.WriteLine($"snapHz {snapHz}: 1.00 mm peak dev {oneMm.PeakDevMmPerSec:F1} mm/s, " +
                           $"0.25 mm peak dev {quarterMm.PeakDevMmPerSec:F1} mm/s " +
                           $"(ratio {oneMm.PeakDevMmPerSec / Math.Max(quarterMm.PeakDevMmPerSec, 1e-9):F2})");
            // Finer quantization must reduce the ripple roughly proportionally.
            Assert.True(quarterMm.PeakDevMmPerSec < oneMm.PeakDevMmPerSec * 0.6,
                $"snapHz {snapHz}: quartering the quantum should cut the ripple substantially");
        }
    }
}
