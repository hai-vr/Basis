using Basis.Network.Core.Compute;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace Basis.Network.Compute;

/// <summary>
/// The distance sweep on a compute device.
///
/// <para>Device buffers are kept and grown rather than allocated per sweep: the output is two bytes
/// per pair, so a full sweep at four thousand players is thirty megabytes, and allocating and
/// freeing that on every sweep would cost more than the sweep does.</para>
/// </summary>
public sealed class GpuDistanceSolver : IBasisDistanceSolver
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly Action<Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<byte>, ArrayView<byte>, int, int, float, float, float, float, float, int> _kernel;
    private MemoryBuffer1D<float, Stride1D.Dense>? _posX;
    private MemoryBuffer1D<float, Stride1D.Dense>? _posY;
    private MemoryBuffer1D<float, Stride1D.Dense>? _posZ;
    private MemoryBuffer1D<byte, Stride1D.Dense>? _intervalByte;
    private MemoryBuffer1D<byte, Stride1D.Dense>? _quality;
    private bool _disposed;
    private GpuDistanceSolver(Context context, Accelerator accelerator)
    {
        _context = context;
        _accelerator = accelerator;
        _kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>,
            ArrayView<byte>, ArrayView<byte>, int, int, float, float, float, float, float, int>(Kernel);
    }
    public string Backend { get; private set; } = "gpu";
    public string DeviceName => _accelerator.Name;

    /// <summary>
    /// Builds a solver on the best device present, or returns null when there is none. Never
    /// throws: a missing driver, a device that refuses to initialise and a kernel that will not
    /// compile are all ordinary outcomes on a host that was never meant to have a GPU, and every
    /// one of them means the same thing to the caller.
    /// </summary>
    public static GpuDistanceSolver? TryCreate(string? deviceSelector, out string? failure)
    {
        failure = null;
        Context? context = null;
        Accelerator? accelerator = null;
        try
        {
            context = Context.Create(b => b.Default());

            Device[] candidates = Devices(context);
            if (candidates.Length == 0)
            {
                failure = "no non-CPU compute device";
                context.Dispose();
                return null;
            }

            Device? device = Select(candidates, deviceSelector, out string? selectionFailure);
            if (device == null)
            {
                failure = selectionFailure;
                context.Dispose();
                return null;
            }

            // ScheduleBlockingSync, not the default.
            //
            // CUDA's default waits for a kernel by spinning, which is right for a process whose
            // only job is the device and exactly wrong here: the point of the offload is to hand
            // cores back to the send phase and the transport's per-peer pass, and a spinning wait
            // hands back nothing. Measured on one sweep at 1000 players: 1.04 ms of CPU burned
            // waiting with the default, 0.00 ms blocking, for the same work. Costs a few hundred
            // microseconds of wakeup latency on a pass that runs at most a few times a second.
            accelerator = device is CudaDevice cuda
                ? cuda.CreateCudaAccelerator(context, CudaAcceleratorFlags.ScheduleBlockingSync)
                : device.CreateAccelerator(context);
            var solver = new GpuDistanceSolver(context, accelerator);
            solver.Backend = device.AcceleratorType == AcceleratorType.Cuda ? "cuda" : "opencl";
            return solver;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            accelerator?.Dispose();
            context?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// The devices worth offering, best first. The CPU accelerator ILGPU always exposes is left
    /// out: it is the fallback the server already has, and listing it would let an operator
    /// "select a GPU" that is not one.
    /// </summary>
    public static Device[] Devices(Context context) => context.Devices
        .Where(d => d.AcceleratorType != AcceleratorType.CPU)
        .OrderByDescending(d => d.AcceleratorType == AcceleratorType.Cuda ? 1 : 0)
        .ThenByDescending(d => d.MemorySize)
        .ToArray();

    /// <summary>
    /// One line per device, indexed the way <see cref="Select"/> reads an index, so what an
    /// operator is shown and what they may type are the same list.
    /// </summary>
    public static string DescribeDevices()
    {
        try
        {
            using var context = Context.Create(b => b.Default());
            Device[] devices = Devices(context);
            if (devices.Length == 0) return "  (no compute device)";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < devices.Length; i++)
            {
                Device d = devices[i];
                sb.Append("  [").Append(i).Append("] ").Append(d.Name)
                  .Append("  ").Append(d.AcceleratorType)
                  .Append("  ").Append((d.MemorySize / (1024.0 * 1024 * 1024)).ToString("F1")).Append(" GB");
                if (i == 0) sb.Append("   <-- chosen when the selector is empty");
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"  (could not enumerate: {ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// Resolves an operator's selector to a device. Empty picks the best one; an integer picks by
    /// the index <see cref="DescribeDevices"/> prints; anything else matches the name.
    ///
    /// <para>A selector that matches nothing is an error rather than a fall back to the default
    /// device. Someone who named a card meant that card, and quietly running on a different one
    /// would be indistinguishable from it having worked.</para>
    /// </summary>
    public static Device? Select(Device[] devices, string? selector, out string? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(selector) || selector.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return devices[0];
        }

        string trimmed = selector.Trim();
        if (int.TryParse(trimmed, out int index))
        {
            if (index >= 0 && index < devices.Length) return devices[index];
            failure = $"compute device index {index} is out of range; this host has {devices.Length}";
            return null;
        }

        foreach (Device d in devices)
        {
            if (d.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) return d;
        }

        failure = $"no compute device matching '{trimmed}'; this host has " +
                  string.Join(", ", devices.Select(d => d.Name));
        return null;
    }
    private static void Kernel(
        Index2D idx,
        ArrayView<float> posX, ArrayView<float> posY, ArrayView<float> posZ,
        ArrayView<byte> outIntervalByte, ArrayView<byte> outQuality,
        int playerCount, int sliceStart,
        float highSq, float mediumSq, float lowSq,
        float baseMultiplier, float increaseRate, int baseIntervalMs)
    {
        int i = idx.X + sliceStart;
        int j = idx.Y;
        if (i >= playerCount || j >= playerCount) return;

        float dx = posX[i] - posX[j];
        float dy = posY[i] - posY[j];
        float dz = posZ[i] - posZ[j];
        float distSq = dx * dx + dy * dy + dz * dz;

        int raw = DistanceMath.RawInterval(distSq, baseMultiplier, increaseRate, baseIntervalMs);

        long o = (long)idx.X * playerCount + j;
        outIntervalByte[o] = DistanceMath.Encode(raw, baseIntervalMs);
        outQuality[o] = DistanceMath.Quality(distSq, highSq, mediumSq, lowSq);
    }

    public void Solve(ref BasisDistanceSolveRequest request, byte[] intervalByte, byte[] quality)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int playerCount = request.PlayerCount;
        int sliceLength = request.SliceLength;
        if (sliceLength <= 0 || playerCount <= 0) return;

        long resultLength = request.ResultLength;
        EnsurePositions(playerCount);
        EnsureResults(resultLength);

        ArrayView<float> viewX = _posX!.View.BaseView;
        ArrayView<float> viewY = _posY!.View.BaseView;
        ArrayView<float> viewZ = _posZ!.View.BaseView;
        ArrayView<byte> viewInterval = _intervalByte!.View.BaseView;
        ArrayView<byte> viewQuality = _quality!.View.BaseView;

        viewX.SubView(0, playerCount).CopyFromCPU(request.PosX.AsSpan(0, playerCount));
        viewY.SubView(0, playerCount).CopyFromCPU(request.PosY.AsSpan(0, playerCount));
        viewZ.SubView(0, playerCount).CopyFromCPU(request.PosZ.AsSpan(0, playerCount));

        BasisDistanceSolveParameters p = request.Parameters;
        _kernel(new Index2D(sliceLength, playerCount),
            viewX, viewY, viewZ,
            viewInterval, viewQuality,
            playerCount, request.SliceStart,
            p.HighDistanceSq, p.MediumDistanceSq, p.LowDistanceSq,
            p.BaseMultiplier, p.IncreaseRate, p.BaseIntervalMs);

        _accelerator.Synchronize();

        viewInterval.SubView(0, resultLength).CopyToCPU(intervalByte.AsSpan(0, (int)resultLength));
        viewQuality.SubView(0, resultLength).CopyToCPU(quality.AsSpan(0, (int)resultLength));
    }

    private void EnsurePositions(int playerCount)
    {
        if (_posX != null && _posX.Length >= playerCount) return;

        _posX?.Dispose();
        _posY?.Dispose();
        _posZ?.Dispose();
        _posX = _accelerator.Allocate1D<float>(playerCount);
        _posY = _accelerator.Allocate1D<float>(playerCount);
        _posZ = _accelerator.Allocate1D<float>(playerCount);
    }

    private void EnsureResults(long length)
    {
        if (_intervalByte != null && _intervalByte.Length >= length) return;

        _intervalByte?.Dispose();
        _quality?.Dispose();
        _intervalByte = _accelerator.Allocate1D<byte>(length);
        _quality = _accelerator.Allocate1D<byte>(length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _posX?.Dispose();
        _posY?.Dispose();
        _posZ?.Dispose();
        _intervalByte?.Dispose();
        _quality?.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
