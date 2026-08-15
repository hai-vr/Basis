using System;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    /// <summary>
    /// Entry framing for <see cref="PacketProperty.CompactMerged"/> datagrams.
    ///
    /// <para>Entry layout: <c>[channel | LongLengthFlag][length][payload]</c>, where length is one
    /// byte, or two when <see cref="LongLengthFlag"/> is set on the channel byte. A
    /// <see cref="PacketProperty.Merged"/> entry is <c>[ushort size][nested packet]</c> instead,
    /// and the nested packet carries its own property and channel bytes.</para>
    ///
    /// <para>Only plain unreliable messages can be carried: the container property identifies the
    /// entry type, so the per-entry property byte is dropped, and the channel is limited to
    /// <see cref="ChannelMask"/> because the top bit selects the length width.</para>
    /// </summary>
    internal static class CompactMerge
    {
        internal const byte LongLengthFlag = 0x80;
        internal const byte ChannelMask = 0x7F;
        internal const int MaxShortLength = byte.MaxValue;
        internal const int ShortEntryOverhead = 2;
        internal const int LongEntryOverhead = 3;

        internal static int EntryOverhead(int payloadLength)
        {
            return payloadLength > MaxShortLength ? LongEntryOverhead : ShortEntryOverhead;
        }

        internal static int EntrySize(int payloadLength)
        {
            return payloadLength + EntryOverhead(payloadLength);
        }

        internal static bool CanCarryChannel(byte channel)
        {
            return channel <= ChannelMask;
        }

        /// <summary>Writes one entry at <paramref name="offset"/> and returns the bytes written.</summary>
        internal static int WriteEntry(byte[] destination, int offset, byte channel, byte[] source, int sourceOffset, int payloadLength)
        {
            int start = offset;
            if (payloadLength > MaxShortLength)
            {
                destination[offset++] = (byte)(channel | LongLengthFlag);
                FastBitConverter.GetBytes(destination, offset, (ushort)payloadLength);
                offset += 2;
            }
            else
            {
                destination[offset++] = channel;
                destination[offset++] = (byte)payloadLength;
            }
            Buffer.BlockCopy(source, sourceOffset, destination, offset, payloadLength);
            return offset + payloadLength - start;
        }

        /// <summary>
        /// Reads the entry header at <paramref name="offset"/>, leaving it on the first payload
        /// byte. Returns false if the entry is truncated, in which case <paramref name="offset"/>
        /// is left unusable and the caller must stop parsing.
        /// </summary>
        internal static bool TryReadEntry(byte[] source, int size, ref int offset, out byte channel, out int payloadLength)
        {
            channel = 0;
            payloadLength = 0;
            if (offset >= size)
                return false;

            byte tag = source[offset++];
            if ((tag & LongLengthFlag) != 0)
            {
                if (size - offset < 2)
                    return false;
                payloadLength = FastBitConverter.Read<ushort>(source, offset);
                offset += 2;
            }
            else
            {
                if (offset >= size)
                    return false;
                payloadLength = source[offset++];
            }

            if (size - offset < payloadLength)
                return false;

            channel = (byte)(tag & ChannelMask);
            return true;
        }
    }
}
