using Basis.Network.Core;

namespace Basis.Network.Compute;

/// <summary>
/// The per-pair math, in one place so the CPU loop and the GPU kernel cannot drift apart.
///
/// <see cref="Encode"/> is a transcription of <c>BasisNetworkCommons.EncodeAvatarIntervalByte</c>
/// rather than a call to it, because the GPU backend compiles this method's IL into a kernel and
/// will not follow a call into an assembly Unity also compiles. <see cref="VerifyAgainstProtocol"/>
/// checks the transcription against the original across its whole input domain; anything that
/// changes one and not the other fails there rather than silently shipping two encodings of the
/// same wire byte.
/// </summary>
public static class DistanceMath
{
    public const byte ExtendedStart = BasisNetworkCommons.AvatarIntervalExtendedStart;
    public const int ExtendedStepMs = BasisNetworkCommons.AvatarIntervalExtendedStepMs;

    public static byte Encode(int intervalMs, int baseIntervalMs)
    {
        int rel = intervalMs - baseIntervalMs;
        if (rel <= 0) return 0;
        if (rel < ExtendedStart) return (byte)rel;
        int steps = (rel - ExtendedStart + (ExtendedStepMs >> 1)) / ExtendedStepMs;
        int maxSteps = byte.MaxValue - ExtendedStart;
        if (steps > maxSteps) steps = maxSteps;
        return (byte)(ExtendedStart + steps);
    }

    public static byte Quality(float distSq, float highSq, float mediumSq, float lowSq)
    {
        if (distSq <= highSq) return 3;
        if (distSq <= mediumSq) return 2;
        if (distSq <= lowSq) return 1;
        return 0;
    }

    public static int RawInterval(float distSq, float baseMultiplier, float increaseRate, int baseIntervalMs)
        => (int)(baseIntervalMs * (baseMultiplier + (distSq * increaseRate)));

    /// <summary>
    /// <c>CachedIntervalTicks</c> for every possible interval byte. The value is a pure function of
    /// the byte, so a solver only ever has to produce the byte.
    /// </summary>
    public static int[] BuildIntervalTickTable(int baseIntervalMs, double msToTick)
    {
        var table = new int[256];
        for (int b = 0; b < 256; b++)
        {
            table[b] = (int)(BasisNetworkCommons.DecodeAvatarIntervalMs((byte)b, baseIntervalMs) * msToTick);
        }
        return table;
    }

    /// <summary>
    /// Returns the first interval where <see cref="Encode"/> and the protocol encoder disagree, or
    /// null when they agree everywhere. Exhaustive: the encoder saturates well inside this range.
    /// </summary>
    public static int? VerifyAgainstProtocol(int baseIntervalMs)
    {
        int limit = baseIntervalMs + ExtendedStart + (byte.MaxValue - ExtendedStart) * ExtendedStepMs + ExtendedStepMs;
        for (int ms = 0; ms <= limit; ms++)
        {
            if (Encode(ms, baseIntervalMs) != BasisNetworkCommons.EncodeAvatarIntervalByte(ms, baseIntervalMs))
            {
                return ms;
            }
        }
        return null;
    }
}
