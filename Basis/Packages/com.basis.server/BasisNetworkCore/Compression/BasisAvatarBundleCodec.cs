using System;
using System.Buffers.Binary;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Reader for the compressed-avatar-bundle body carried on
    /// <see cref="BasisNetworkCommons.CompressedAvatarBundleChannel"/>.
    ///
    /// The server writes the body grouped by channel (BasisServerReductionSystemEvents.BuildRawForRange):
    /// <code>
    ///   group*  where group := [origChannel:1][n:1][msgLen:2-LE] x n [bodies]
    /// </code>
    /// with the DeltaAvatarChannel group's bodies column-transposed — byte j of every body, then
    /// byte j+1 of every body — because delta bodies are short and field-aligned, so interleaving
    /// puts the same field from different players adjacent and gives LZ4 something to match.
    ///
    /// Three places consume this: the Unity client handler, and the console load-tester's sniffer
    /// in each server tree. Rather than triplicate the un-transpose, <see cref="TryFlatten"/>
    /// rewrites a grouped body into the flat, ungrouped
    /// <c>[origChannel:1][msgLen:2-LE][body]*</c> stream those callers already know how to walk.
    /// </summary>
    public static class BasisAvatarBundleCodec
    {
        /// <summary>
        /// Buffer size <see cref="TryFlatten"/> can require for a grouped body of
        /// <paramref name="groupedLength"/> bytes.
        ///
        /// Flattening costs 3 bytes of framing per entry against the grouped form's 2, so it can
        /// only grow. Every entry occupies at least 3 bytes when grouped (2 length + 1 body), so
        /// there are at most groupedLength/3 entries and the flat form is at most 2x the input.
        /// </summary>
        public static int MaxFlatSize(int groupedLength) => groupedLength * 2 + 8;

        /// <summary>
        /// Rewrites a grouped bundle body into the flat <c>[channel:1][len:2-LE][body]*</c> form.
        /// Returns false on any inconsistency — a truncated group, a zero length, or a body run
        /// that overruns the buffer — in which case the caller should drop the datagram.
        /// </summary>
        public static bool TryFlatten(ReadOnlySpan<byte> grouped, Span<byte> flat, out int flatLength)
        {
            flatLength = 0;
            int read = 0;
            int write = 0;

            while (read + 2 <= grouped.Length)
            {
                byte channel = grouped[read];
                int n = grouped[read + 1];
                read += 2;
                if (n == 0) return false;
                if (read + n * 2 > grouped.Length) return false;

                int lengthsAt = read;
                read += n * 2;

                int bodyTotal = 0;
                int maxLen = 0;
                for (int i = 0; i < n; i++)
                {
                    int len = BinaryPrimitives.ReadUInt16LittleEndian(grouped.Slice(lengthsAt + i * 2, 2));
                    if (len == 0) return false;
                    bodyTotal += len;
                    if (len > maxLen) maxLen = len;
                }
                if (read + bodyTotal > grouped.Length) return false;
                if (write + n * 3 + bodyTotal > flat.Length) return false;

                // Reserve the flat frames first so the un-transpose can scatter straight into
                // their body regions. Frame i is [channel][len:2] followed by len body bytes.
                int frameAt = write;
                for (int i = 0; i < n; i++)
                {
                    int len = BinaryPrimitives.ReadUInt16LittleEndian(grouped.Slice(lengthsAt + i * 2, 2));
                    flat[write] = channel;
                    BinaryPrimitives.WriteUInt16LittleEndian(flat.Slice(write + 1, 2), (ushort)len);
                    write += 3 + len;
                }

                if (channel != BasisNetworkCommons.DeltaAvatarChannel)
                {
                    // Bodies are stored back to back in entry order.
                    int src = read;
                    int at = frameAt;
                    for (int i = 0; i < n; i++)
                    {
                        int len = BinaryPrimitives.ReadUInt16LittleEndian(grouped.Slice(lengthsAt + i * 2, 2));
                        grouped.Slice(src, len).CopyTo(flat.Slice(at + 3, len));
                        src += len;
                        at += 3 + len;
                    }
                }
                else
                {
                    // Column-major. Walk the columns in exactly the order the writer emitted them:
                    // for each byte index j, every entry still long enough to have a byte j.
                    int src = read;
                    for (int j = 0; j < maxLen; j++)
                    {
                        int at = frameAt;
                        for (int i = 0; i < n; i++)
                        {
                            int len = BinaryPrimitives.ReadUInt16LittleEndian(grouped.Slice(lengthsAt + i * 2, 2));
                            if (j < len) flat[at + 3 + j] = grouped[src++];
                            at += 3 + len;
                        }
                    }
                    if (src != read + bodyTotal) return false;
                }

                read += bodyTotal;
            }

            if (read != grouped.Length) return false;
            flatLength = write;
            return true;
        }
    }
}
