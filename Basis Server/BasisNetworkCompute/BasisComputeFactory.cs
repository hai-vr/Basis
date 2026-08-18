using Basis.Network.Core.Compute;

namespace Basis.Network.Compute;

/// <summary>
/// The entry point the server looks for by name.
///
/// <para>The server resolves this assembly at runtime rather than referencing it, so this type's
/// full name and this method's signature are the contract between them and cannot change without
/// changing the loader. Everything else here is free to move.</para>
/// </summary>
public static class BasisComputeFactory
{
    public const string FactoryTypeName = "Basis.Network.Compute.BasisComputeFactory";
    public const string FactoryMethodName = nameof(TryCreateDistanceSolver);

    /// <summary>The selectable devices, one per line, for an operator choosing between them.</summary>
    public static string DescribeDevices() => GpuDistanceSolver.DescribeDevices();

    /// <summary>
    /// A device-backed distance solver, or null with a reason. Verifies the kernel's interval
    /// encoder against the protocol's before returning one: a solver that encodes the wire byte
    /// differently from the rest of the server is worse than no solver, and this is the last point
    /// at which that can be caught cheaply.
    /// </summary>
    public static IBasisDistanceSolver? TryCreateDistanceSolver(int baseIntervalMs, string? deviceSelector, out string? failure)
    {
        if (DistanceMath.VerifyAgainstProtocol(baseIntervalMs) is { } driftAt)
        {
            failure = $"the compute backend's interval encoder disagrees with the protocol's at {driftAt} ms";
            return null;
        }

        return GpuDistanceSolver.TryCreate(deviceSelector, out failure);
    }
}
