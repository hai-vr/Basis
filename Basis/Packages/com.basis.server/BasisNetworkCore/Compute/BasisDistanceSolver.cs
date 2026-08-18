using System;

namespace Basis.Network.Core.Compute
{
    /// <summary>
    /// The parameters the distance sweep turns into a per-pair send interval and quality tier.
    /// </summary>
    public struct BasisDistanceSolveParameters
    {
        public float HighDistanceSq;
        public float MediumDistanceSq;
        public float LowDistanceSq;
        public float BaseMultiplier;
        public float IncreaseRate;
        public int BaseIntervalMs;
    }

    /// <summary>
    /// One slice of the sweep: receivers <c>[SliceStart, SliceEnd)</c> of a roster of
    /// <see cref="PlayerCount"/>, measured against every player in that roster.
    ///
    /// <para>Positions are dense and in roster order rather than indexed by peer id. The server's
    /// own arrays are keyed by peer id and therefore full of holes; compacting once on the way in
    /// costs one pass and removes the holes from the transfer and the kernel both.</para>
    /// </summary>
    public struct BasisDistanceSolveRequest
    {
        public float[] PosX;
        public float[] PosY;
        public float[] PosZ;
        public int PlayerCount;
        public int SliceStart;
        public int SliceEnd;
        public BasisDistanceSolveParameters Parameters;

        public int SliceLength => SliceEnd - SliceStart;
        public long ResultLength => (long)SliceLength * PlayerCount;
    }

    /// <summary>
    /// A backend that can produce the distance cache.
    ///
    /// <para>Declared here, in the assembly both sides already reference, so the server can hold a
    /// solver without referencing the assembly that implements one. That indirection is the whole
    /// point: the GPU backend carries ILGPU, and this assembly is compiled by Unity.</para>
    ///
    /// <para>Results are two bytes per pair at <c>(sliceIndex * PlayerCount) + j</c>. The third
    /// field the cache carries, <c>CachedIntervalTicks</c>, is a pure function of the interval byte
    /// and is recovered by the caller from a 256-entry table — sending it would triple the bytes
    /// crossing the bus to carry no information.</para>
    /// </summary>
    public interface IBasisDistanceSolver : IDisposable
    {
        string Backend { get; }

        string DeviceName { get; }

        void Solve(ref BasisDistanceSolveRequest request, byte[] intervalByte, byte[] quality);
    }
}
