using System.Threading.Tasks;
using Basis.Network.Core.Compute;

namespace Basis.Network.Compute;

/// <summary>
/// The reference backend, and the one that runs when there is no usable device. Shaped like the
/// server's own sweep — a parallel loop over receivers, a scalar loop over the roster — so the two
/// backends can be compared on equal terms.
/// </summary>
public sealed class CpuDistanceSolver : IBasisDistanceSolver
{
    private readonly ParallelOptions _options;

    public CpuDistanceSolver(int maxDegreeOfParallelism)
    {
        _options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : 1,
        };
    }

    public string Backend => "cpu";

    public string DeviceName => "managed parallel loop";

    public void Solve(ref BasisDistanceSolveRequest request, byte[] intervalByte, byte[] quality)
    {
        float[] posX = request.PosX;
        float[] posY = request.PosY;
        float[] posZ = request.PosZ;
        int playerCount = request.PlayerCount;
        int sliceStart = request.SliceStart;
        BasisDistanceSolveParameters p = request.Parameters;

        Parallel.For(0, request.SliceLength, _options, s =>
        {
            int i = sliceStart + s;
            float iX = posX[i];
            float iY = posY[i];
            float iZ = posZ[i];
            long baseOffset = (long)s * playerCount;

            for (int j = 0; j < playerCount; j++)
            {
                float dx = iX - posX[j];
                float dy = iY - posY[j];
                float dz = iZ - posZ[j];
                float distSq = dx * dx + dy * dy + dz * dz;

                int raw = DistanceMath.RawInterval(distSq, p.BaseMultiplier, p.IncreaseRate, p.BaseIntervalMs);
                intervalByte[baseOffset + j] = DistanceMath.Encode(raw, p.BaseIntervalMs);
                quality[baseOffset + j] = DistanceMath.Quality(distSq, p.HighDistanceSq, p.MediumDistanceSq, p.LowDistanceSq);
            }
        });
    }

    public void Dispose() { }
}
