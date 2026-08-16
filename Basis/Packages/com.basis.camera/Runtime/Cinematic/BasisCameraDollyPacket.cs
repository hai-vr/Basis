using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>What a dolly packet is carrying.</summary>
    public enum BasisCameraDollyPacketType : byte
    {
        /// <summary>The whole track, from the player who authored it: the mode and every point.</summary>
        Roster = 1,
        /// <summary>One point moving, from whoever is holding it.</summary>
        PointMove = 2,
        /// <summary>One point being picked up or put down.</summary>
        Claim = 3,
    }

    /// <summary>
    /// The wire format for a shared dolly track.
    ///
    /// <para>Kept as a pure static codec with no <c>MonoBehaviour</c> and no transport, so the
    /// format can be asserted on in full — a byte laid out wrongly here is a class of bug that only
    /// shows up as points landing in the wrong place on somebody else's screen, which is miserable
    /// to debug live.</para>
    ///
    /// <para>A roster's author is the sender, so it carries no owner field. A move or a claim can
    /// come from anyone, so those name the track they act on — without it, a remote reaching for
    /// somebody's point would be indistinguishable from them editing their own.</para>
    ///
    /// <para>Positions are full floats. A point is authored once and then sits still, so the
    /// traffic is tiny and bounded by <see cref="MaxPoints"/>; quantising would buy nothing and
    /// would put a rounding error into a camera move whose whole purpose is to be smooth.</para>
    /// </summary>
    public static class BasisCameraDollyPacket
    {
        /// <summary>Matches the panel's waypoint cap, so a track can never outgrow one packet.</summary>
        public const int MaxPoints = 32;

        private const int HeaderSize = 1;
        private const int OwnerSize = 2;
        private const int PointSize = 28;                           // position (12) + rotation (16)
        private const int RosterHeader = HeaderSize + 1 + 1 + 1;    // type, mode, count, flags

        /// <summary>
        /// The track closes back on itself. It belongs with the points rather than beside them: it
        /// decides whether the last waypoint joins the first, so a mirror without it draws a
        /// different path from the one the author is looking at.
        /// </summary>
        private const byte RosterFlagLooped = 1 << 0;
        private const int MoveSize = HeaderSize + OwnerSize + 1 + PointSize;
        private const int ClaimSize = HeaderSize + OwnerSize + 1 + 1;

        public static int RosterSize(int count) => RosterHeader + Mathf.Clamp(count, 0, MaxPoints) * PointSize;

        public static int PointMoveSize => MoveSize;

        public static int ClaimPacketSize => ClaimSize;

        /// <summary>One authored point on the wire.</summary>
        public struct Point
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // ---- Writing ---------------------------------------------------------------------

        /// <summary>
        /// Writes the whole track. Returns the byte count, or 0 when the buffer is too small —
        /// callers size with <see cref="RosterSize"/>, so 0 means a caller bug rather than a
        /// runtime condition to handle.
        /// </summary>
        public static int WriteRoster(byte[] buffer, BasisCameraDollySync mode, bool looped, Point[] points, int count)
        {
            count = Mathf.Clamp(count, 0, MaxPoints);
            if (points == null) count = 0;

            if (buffer == null || buffer.Length < RosterSize(count)) return 0;

            int offset = 0;
            buffer[offset++] = (byte)BasisCameraDollyPacketType.Roster;
            buffer[offset++] = (byte)mode;
            buffer[offset++] = (byte)count;
            buffer[offset++] = looped ? RosterFlagLooped : (byte)0;

            for (int Index = 0; Index < count; Index++)
            {
                WritePoint(buffer, ref offset, points[Index]);
            }
            return offset;
        }

        public static int WritePointMove(byte[] buffer, ushort owner, int slot, Vector3 position, Quaternion rotation)
        {
            if (buffer == null || buffer.Length < MoveSize) return 0;
            if (slot < 0 || slot >= MaxPoints) return 0;

            int offset = 0;
            buffer[offset++] = (byte)BasisCameraDollyPacketType.PointMove;
            WriteUShort(buffer, ref offset, owner);
            buffer[offset++] = (byte)slot;
            WritePoint(buffer, ref offset, new Point { Position = position, Rotation = rotation });
            return offset;
        }

        public static int WriteClaim(byte[] buffer, ushort owner, int slot, bool claimed)
        {
            if (buffer == null || buffer.Length < ClaimSize) return 0;
            if (slot < 0 || slot >= MaxPoints) return 0;

            int offset = 0;
            buffer[offset++] = (byte)BasisCameraDollyPacketType.Claim;
            WriteUShort(buffer, ref offset, owner);
            buffer[offset++] = (byte)slot;
            buffer[offset++] = (byte)(claimed ? 1 : 0);
            return offset;
        }

        // ---- Reading ---------------------------------------------------------------------

        /// <summary>
        /// The kind of packet this is, or false for anything this build does not understand — a
        /// short buffer, a type from a newer client, or noise. Every read goes through here first,
        /// so an unknown packet is dropped rather than read as whatever it happens to resemble.
        /// </summary>
        public static bool TryReadType(byte[] buffer, int length, out BasisCameraDollyPacketType type)
        {
            type = default;
            if (buffer == null || length < HeaderSize || length > buffer.Length) return false;
            if (!Enum.IsDefined(typeof(BasisCameraDollyPacketType), buffer[0])) return false;

            type = (BasisCameraDollyPacketType)buffer[0];
            return true;
        }

        /// <summary>
        /// Reads a track into <paramref name="points"/>, which must hold <see cref="MaxPoints"/>.
        /// False when the packet is truncated or names a mode this build does not have.
        /// </summary>
        public static bool TryReadRoster(byte[] buffer, int length, Point[] points,
            out BasisCameraDollySync mode, out bool looped, out int count)
        {
            mode = BasisCameraDollySync.LocalOnly;
            looped = false;
            count = 0;

            if (!TryReadType(buffer, length, out BasisCameraDollyPacketType type) ||
                type != BasisCameraDollyPacketType.Roster)
            {
                return false;
            }
            if (length < RosterHeader || points == null || points.Length < MaxPoints) return false;
            if (!Enum.IsDefined(typeof(BasisCameraDollySync), (int)buffer[1])) return false;

            int declared = buffer[2];
            if (declared > MaxPoints) return false;
            if (length < RosterSize(declared)) return false;

            mode = (BasisCameraDollySync)buffer[1];
            looped = (buffer[3] & RosterFlagLooped) != 0;
            count = declared;

            int offset = RosterHeader;
            for (int Index = 0; Index < declared; Index++)
            {
                points[Index] = ReadPoint(buffer, ref offset);
            }
            return true;
        }

        public static bool TryReadPointMove(byte[] buffer, int length, out ushort owner, out int slot,
            out Vector3 position, out Quaternion rotation)
        {
            owner = 0;
            slot = -1;
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!TryReadType(buffer, length, out BasisCameraDollyPacketType type) ||
                type != BasisCameraDollyPacketType.PointMove)
            {
                return false;
            }
            if (length < MoveSize) return false;

            int offset = HeaderSize;
            owner = ReadUShort(buffer, ref offset);
            if (buffer[offset] >= MaxPoints) return false;

            slot = buffer[offset++];
            Point point = ReadPoint(buffer, ref offset);
            position = point.Position;
            rotation = point.Rotation;
            return true;
        }

        public static bool TryReadClaim(byte[] buffer, int length, out ushort owner, out int slot, out bool claimed)
        {
            owner = 0;
            slot = -1;
            claimed = false;

            if (!TryReadType(buffer, length, out BasisCameraDollyPacketType type) ||
                type != BasisCameraDollyPacketType.Claim)
            {
                return false;
            }
            if (length < ClaimSize) return false;

            int offset = HeaderSize;
            owner = ReadUShort(buffer, ref offset);
            if (buffer[offset] >= MaxPoints) return false;

            slot = buffer[offset++];
            claimed = buffer[offset] != 0;
            return true;
        }

        // ---- Primitives ------------------------------------------------------------------

        private static void WriteUShort(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        private static ushort ReadUShort(byte[] buffer, ref int offset)
        {
            ushort value = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
            offset += 2;
            return value;
        }

        private static void WritePoint(byte[] buffer, ref int offset, Point point)
        {
            WriteFloat(buffer, ref offset, point.Position.x);
            WriteFloat(buffer, ref offset, point.Position.y);
            WriteFloat(buffer, ref offset, point.Position.z);
            WriteFloat(buffer, ref offset, point.Rotation.x);
            WriteFloat(buffer, ref offset, point.Rotation.y);
            WriteFloat(buffer, ref offset, point.Rotation.z);
            WriteFloat(buffer, ref offset, point.Rotation.w);
        }

        private static Point ReadPoint(byte[] buffer, ref int offset)
        {
            Point point = new Point
            {
                Position = new Vector3(
                    ReadFloat(buffer, ref offset),
                    ReadFloat(buffer, ref offset),
                    ReadFloat(buffer, ref offset)),
                Rotation = new Quaternion(
                    ReadFloat(buffer, ref offset),
                    ReadFloat(buffer, ref offset),
                    ReadFloat(buffer, ref offset),
                    ReadFloat(buffer, ref offset)),
            };

            // An all-zero quaternion is not a rotation, and handing one to a transform produces
            // NaNs several frames later somewhere else entirely.
            if (point.Rotation.x == 0f && point.Rotation.y == 0f &&
                point.Rotation.z == 0f && point.Rotation.w == 0f)
            {
                point.Rotation = Quaternion.identity;
            }
            return point;
        }

        private static void WriteFloat(byte[] buffer, ref int offset, float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            buffer[offset++] = (byte)bits;
            buffer[offset++] = (byte)(bits >> 8);
            buffer[offset++] = (byte)(bits >> 16);
            buffer[offset++] = (byte)(bits >> 24);
        }

        private static float ReadFloat(byte[] buffer, ref int offset)
        {
            int bits = buffer[offset] | (buffer[offset + 1] << 8) |
                       (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
            offset += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
