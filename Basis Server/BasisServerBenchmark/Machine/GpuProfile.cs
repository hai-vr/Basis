using System.Text;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace Basis.Benchmark.Machine;

/// <summary>Whether this host has a compute device the server could offload to.</summary>
public enum GpuAvailability
{
    None,
    Present,
    Unusable,
}

/// <summary>One compute device as the runtime sees it.</summary>
public sealed class GpuDevice
{
    public required string Name { get; init; }
    public required AcceleratorType Backend { get; init; }
    public required long MemoryBytes { get; init; }
    public string? Architecture { get; init; }
    public int MultiProcessors { get; init; }
    public int ClockRateKhz { get; init; }

    public double MemoryGb => MemoryBytes / (1024.0 * 1024 * 1024);

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(Name).Append("  [").Append(Backend).Append(']');
        if (MemoryBytes > 0) sb.Append("  ").Append(MemoryGb.ToString("F1")).Append(" GB");
        if (Architecture != null) sb.Append("  ").Append(Architecture);
        if (MultiProcessors > 0) sb.Append("  ").Append(MultiProcessors).Append(" SMs");
        return sb.ToString();
    }
}

/// <summary>
/// What compute hardware the box has, read once before anything is measured.
///
/// Reported rather than acted on. Whether a device is worth offloading to is decided by
/// <see cref="Micro.GpuBench"/> against the CPU it would be replacing, not by its presence — a
/// CPU-backed OpenCL runtime enumerates here exactly like a discrete card and loses the
/// measurement, which is the outcome that should decide it.
/// </summary>
public sealed class GpuProfile
{
    public required GpuAvailability Availability { get; init; }
    public required IReadOnlyList<GpuDevice> Devices { get; init; }
    public GpuDevice? Preferred { get; init; }
    public string? Failure { get; init; }

    public static GpuProfile Collect()
    {
        try
        {
            using var context = Context.Create(b => b.Default());

            var found = new List<GpuDevice>();
            foreach (Device device in context.Devices)
            {
                if (device.AcceleratorType == AcceleratorType.CPU) continue;

                string? architecture = null;
                int multiProcessors = 0;
                int clock = 0;
                if (device is CudaDevice cudaDevice)
                {
                    architecture = cudaDevice.Architecture.ToString();
                    multiProcessors = cudaDevice.NumMultiprocessors;
                    clock = cudaDevice.ClockRate;
                }

                found.Add(new GpuDevice
                {
                    Name = device.Name,
                    Backend = device.AcceleratorType,
                    MemoryBytes = device.MemorySize,
                    Architecture = architecture,
                    MultiProcessors = multiProcessors,
                    ClockRateKhz = clock,
                });
            }

            if (found.Count == 0)
            {
                return new GpuProfile { Availability = GpuAvailability.None, Devices = Array.Empty<GpuDevice>() };
            }

            GpuDevice preferred = found
                .OrderByDescending(d => d.Backend == AcceleratorType.Cuda ? 1 : 0)
                .ThenByDescending(d => d.MemoryBytes)
                .First();

            return new GpuProfile
            {
                Availability = GpuAvailability.Present,
                Devices = found,
                Preferred = preferred,
            };
        }
        catch (Exception ex)
        {
            return new GpuProfile
            {
                Availability = GpuAvailability.Unusable,
                Devices = Array.Empty<GpuDevice>(),
                Failure = $"{ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        switch (Availability)
        {
            case GpuAvailability.None:
                sb.AppendLine("  GPU            none - the server runs every pass on the CPU, which is the supported default");
                break;
            case GpuAvailability.Unusable:
                sb.AppendLine($"  GPU            present but unusable ({Failure})");
                break;
            default:
                for (int i = 0; i < Devices.Count; i++)
                {
                    GpuDevice device = Devices[i];
                    string label = i == 0 ? "  GPU           " : "                ";
                    string index = Devices.Count > 1 ? $"[{i}] " : "";
                    string chosen = Devices.Count > 1 && ReferenceEquals(device, Preferred)
                        ? "   <-- used unless ComputeDevice says otherwise"
                        : "";
                    sb.AppendLine($"{label} {index}{device.Summary()}{chosen}");
                }
                break;
        }
        return sb.ToString();
    }
}
