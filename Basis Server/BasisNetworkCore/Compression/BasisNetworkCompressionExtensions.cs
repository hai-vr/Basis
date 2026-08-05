using Basis.Scripts.Networking.Compression;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// The hips world-position field at the head of every avatar payload, in the int24-millimetre
    /// form <see cref="BasisAvatarBitPacking.WritePosition"/> describes. Every quality tier uses
    /// this encoding, so the server reads a sender's position the same way regardless of the tier
    /// the frame arrived on.
    /// </summary>
    public static class BasisNetworkCompressionExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePosition(Vector3 position, ref byte[] buffer, ref int offset)
        {
            BasisAvatarBitPacking.EncodePosition(position.x, position.y, position.z, buffer, offset);
            offset += BasisAvatarBitPacking.WritePosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ReadPosition(ref byte[] buffer)
        {
            BasisAvatarBitPacking.DecodePosition(buffer, 0, out float x, out float y, out float z);
            Vector3 result;
            result.x = x;
            result.y = y;
            result.z = z;
            return result;
        }
    }
}
