using System;

namespace Basis.Scripts.Networking.Sync
{
    /// <summary>
    /// Frames several synced objects' update payloads into one scene-data packet so a client owning many
    /// objects amortizes the per-packet header + transport overhead. Each entry is [NetworkID u16][len u16][payload];
    /// the reader walks entries until the buffer is exhausted (the scene-data length bounds it). Because one packet
    /// carries a single delivery method and recipient set, only objects that match on both can share a batch.
    /// </summary>
    public struct BasisSyncBatchWriter
    {
        public const int EntryHeader = 4; // NetworkID (2) + payload length (2)

        private readonly byte[] _buf;
        private int _pos;
        private int _count;

        public BasisSyncBatchWriter(byte[] buffer)
        {
            _buf = buffer;
            _pos = 0;
            _count = 0;
        }

        public int Count => _count;
        public int Length => _pos;

        /// <summary>Appends one object's payload. Returns false (and writes nothing) if it wouldn't fit — flush, then retry.</summary>
        public bool TryAppend(ushort networkId, byte[] payload, int payloadLength)
        {
            if (payloadLength < 0 || payloadLength > ushort.MaxValue) return false;
            if (_pos + EntryHeader + payloadLength > _buf.Length) return false;

            _buf[_pos++] = (byte)networkId;
            _buf[_pos++] = (byte)(networkId >> 8);
            _buf[_pos++] = (byte)payloadLength;
            _buf[_pos++] = (byte)(payloadLength >> 8);
            if (payloadLength > 0) Array.Copy(payload, 0, _buf, _pos, payloadLength);
            _pos += payloadLength;
            _count++;
            return true;
        }

        public void Reset()
        {
            _pos = 0;
            _count = 0;
        }
    }

    /// <summary>Walks the entries written by <see cref="BasisSyncBatchWriter"/>. Payloads are returned as offsets into the source buffer (no copy).</summary>
    public struct BasisSyncBatchReader
    {
        private readonly byte[] _buf;
        private readonly int _len;
        private int _pos;

        public BasisSyncBatchReader(byte[] buffer, int length)
        {
            _buf = buffer;
            _len = length < buffer.Length ? length : buffer.Length;
            _pos = 0;
        }

        /// <summary>Reads the next entry. Returns false at the end, or on a truncated/corrupt tail (never throws).</summary>
        public bool TryRead(out ushort networkId, out int payloadOffset, out int payloadLength)
        {
            networkId = 0;
            payloadOffset = 0;
            payloadLength = 0;
            if (_pos + BasisSyncBatchWriter.EntryHeader > _len) return false;

            networkId = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
            int plen = _buf[_pos + 2] | (_buf[_pos + 3] << 8);
            _pos += BasisSyncBatchWriter.EntryHeader;
            if (_pos + plen > _len) return false;

            payloadOffset = _pos;
            payloadLength = plen;
            _pos += plen;
            return true;
        }
    }
}
