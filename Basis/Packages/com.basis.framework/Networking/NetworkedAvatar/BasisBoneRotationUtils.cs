using Basis.Network.Core.Compression;
using Unity.Collections;
using Unity.Mathematics;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    /// <summary>
    /// Unity-side wrappers around BasisBoneRotationCompression (which is pure C#).
    /// Bridges quaternion/NativeArray types to the server-compatible encode/decode.
    ///
    /// Since v47 the rotation region is two regions: explicit smallest-three rotations for wire
    /// slots 0..20, then ten curl/splay finger channels. Slots 21..50 of the pose still exist —
    /// they are just reconstructed from the channels through the receiver's own pose grid rather
    /// than transmitted.
    /// </summary>
    public static class BasisBoneRotationUtils
    {
        /// <summary>
        /// Compresses the explicit bone deltas and the finger channels into a packed byte buffer.
        /// <paramref name="boneDeltas"/> is indexed by wire slot; only 0..WireBoneSlotCount are read.
        /// <paramref name="percentages"/> is ten curl/splay pairs.
        /// </summary>
        public static void CompressBoneRotations(
            NativeArray<quaternion> boneDeltas,
            NativeArray<float2> percentages,
            BasisAvatarBitPacking.BitQuality quality,
            byte[] dst,
            ref int byteOffset)
        {
            byte[] bpc = BasisBoneRotationCompression.GetBpcTable(quality);
            float[] ranges = BasisBoneRotationCompression.MAX_COMPONENT;
            int bitPos = byteOffset << 3;

            for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
            {
                int bitsPerComp = bpc[slot];
                int totalBits = 2 + 3 * bitsPerComp;

                quaternion q = boneDeltas[slot];
                ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(
                    q.value.x, q.value.y, q.value.z, q.value.w, bitsPerComp, ranges[slot]);

                BasisBoneRotationCompression.WriteBits(dst, bitPos, packed, totalBits);
                bitPos += totalBits;
            }

            int curlBits = BasisBoneRotationCompression.CurlBits(quality);
            int splayBits = BasisBoneRotationCompression.SplayBits(quality);
            for (int finger = 0; finger < BasisBoneRotationCompression.FingerChannelCount; finger++)
            {
                float2 pct = percentages[finger];
                ulong curl = BasisBoneRotationCompression.EncodeSignedUnit(pct.x, curlBits);
                ulong splay = BasisBoneRotationCompression.EncodeSignedUnit(pct.y, splayBits);

                BasisBoneRotationCompression.WriteBits(dst, bitPos, curl | (splay << curlBits), curlBits + splayBits);
                bitPos += curlBits + splayBits;
            }

            byteOffset = (bitPos + 7) >> 3;
        }

        /// <summary>
        /// Decompresses the explicit bone rotations and the finger channels.
        /// Slots 21..50 of <paramref name="boneDeltas"/> are left untouched — the caller fills them
        /// by sampling its own pose grid at <paramref name="percentages"/>.
        /// </summary>
        public static void DecompressBoneRotations(
            byte[] src,
            BasisAvatarBitPacking.BitQuality quality,
            ref NativeArray<quaternion> boneDeltas,
            ref NativeArray<float2> percentages,
            ref int byteOffset)
        {
            byte[] bpc = BasisBoneRotationCompression.GetBpcTable(quality);
            float[] ranges = BasisBoneRotationCompression.MAX_COMPONENT;
            int bitPos = byteOffset << 3;

            for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
            {
                int bitsPerComp = bpc[slot];
                int totalBits = 2 + 3 * bitsPerComp;

                ulong packed = BasisBoneRotationCompression.ReadBits(src, ref bitPos, totalBits);
                BasisBoneRotationCompression.DecodeSmallestThree(packed, bitsPerComp,
                    out float qx, out float qy, out float qz, out float qw, ranges[slot]);

                boneDeltas[slot] = new quaternion(qx, qy, qz, qw);
            }

            int curlBits = BasisBoneRotationCompression.CurlBits(quality);
            int splayBits = BasisBoneRotationCompression.SplayBits(quality);
            uint curlMask = (uint)((1 << curlBits) - 1);
            uint splayMask = (uint)((1 << splayBits) - 1);
            for (int finger = 0; finger < BasisBoneRotationCompression.FingerChannelCount; finger++)
            {
                ulong packed = BasisBoneRotationCompression.ReadBits(src, ref bitPos, curlBits + splayBits);
                percentages[finger] = new float2(
                    BasisBoneRotationCompression.DecodeSignedUnit((uint)packed & curlMask, curlBits),
                    BasisBoneRotationCompression.DecodeSignedUnit((uint)(packed >> curlBits) & splayMask, splayBits));
            }

            byteOffset = (bitPos + 7) >> 3;
        }
    }
}
