using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>What a dolly packet is carrying.</summary>
    public enum BasisCameraDollyPacketType : byte
    {
        /// <summary>The whole roster, from the camera's owner: the mode and every point.</summary>
        Roster = 1,
        /// <summary>One point moving, from whoever is holding it.</summary>
        PointMove = 2,
        /// <summary>One point being picked up or put down.</summary>
        Claim = 3,
    }

    /// <summary>
    /// The wire format for a networked dolly track.
    ///
    /// <para>Kept as a pure static codec with no <c>MonoBehaviour</c> and no transport, so the
    /// format can be asserted on in full — a byte laid out wrongly here is a class of bug that
    /// only shows up as points landing in the wrong place on somebody else's screen, which is
    /// exactly the sort of thing that is miserable to debug live.</para>
    ///
    /// <para>Positions are full floats. A dolly point is authored once and then sits still, so the
    /// traffic is tiny and bounded by <see cref="MaxPoints"/>; spending bits on quantisation would
    /// buy nothing and would put a rounding error into a camera move whose whole purpose is to be
    /// smooth.</para>
    /// </summary>
    public static class BasisCameraDollyPacket
    {
        /// <summary>Matches the panel's waypoint cap, so a roster can never outgrow one packet.</summary>
        public const int MaxPoints = 32;

        private const int HeaderSize = 1;
        private const int PointSize = 28;      // position (12) + rotation (16)
        private const int RosterHeader = HeaderSize + 1 + 1;   // type, mode, count
        private const int MoveSize = HeaderSize + 1 + PointSize;
        private const int ClaimSize = HeaderSize + 1 + 1;      // type, slot, claimed

        public static int RosterSize(int count) => RosterHeader + Mathf.Clamp(count, 0, MaxPoints) * PointSize;

        /// <summary>One authored point on the wire.</summary>
        public struct Point
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // ---- Writing ---------------------------------------------------------------------

        /// <summary>
        /// Writes the whole roster. Returns the byte count, or 0 when the buffer is too small —
        /// callers size with <see cref="RosterSize"/>, so a 0 means a caller bug rather than a
        /// runtime condition to handle.
        /// </summary>
        public static int WriteRoster(byte[] buffer, BasisCameraDollySync mode, Point[] points, int count)
        {
            count = Mathf.Clamp(count, 0, MaxPoints);
            if (points == null) count = 0;

            int size = RosterSize(count);
            if (buffer == null || buffer.Length < size) return 0;

            int offset = 0;
            buffer[offset++] = (byte)BasisCameraDollyPacketType.Roster;
            buffer[offset++] = (byte)mode;
            buffer[offset++] = (byte)count;

            for (int Index = 0; Index < count; Index++)
            {
                WritePoint(buffer, ref offset, points[Index]);
            }
            return offset;
        }

        public static int WritePointMove(byte[] buffer, int slot, Vector3 position, Quaternion rotation)
        {
            if (buffer == null || buffer.Length < MoveSize) return 0;
            if (slot < 0 || slot >= MaxPoints) return 0;

            int offset = 0;
            buffer[offset++] = (byte)BasisCameraDollyPacketType.PointMove;
            buffer[offset++] = (byte)slot;
            WritePoint(buffer, ref offset, new Point { Position = position, Rotation = rotation });
            return offset;
        }

        public static int WriteClaim(byte[] buffer, int slot, bool claimed)
        {
            if (buffer == null || buffer.Length < ClaimSize) return 0;
            if (slot < 0 || slot >= MaxPoints) return 0;

            buffer[0] = (byte)BasisCameraDollyPacketType.Claim;
            buffer[1] = (byte)slot;
            buffer[2] = (byte)(claimed ? 1 : 0);
            return ClaimSize;
        }

        // ---- Reading ---------------------------------------------------------------------

        /// <summary>
        /// The kind of packet this is, or false for anything this build does not understand — a
        /// short buffer, a type from a newer client, or noise. Every read goes through here first
        /// so an unknown packet is dropped rather than being read as whatever it resembles.
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
        /// Reads a roster into <paramref name="points"/>, which must hold <see cref="MaxPoints"/>.
        /// Returns false when the packet is truncated or names a mode this build does not have.
        /// </summary>
        public static bool TryReadRoster(byte[] buffer, int length, Point[] points,
            out BasisCameraDollySync mode, out int count)
        {
            mode = BasisCameraDollySync.LocalOnly;
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
            count = declared;

            int offset = RosterHeader;
            for (int Index = 0; Index < declared; Index++)
            {
                points[Index] = ReadPoint(buffer, ref offset);
            }
            return true;
        }

        public static bool TryReadPointMove(byte[] buffer, int length, out int slot,
            out Vector3 position, out Quaternion rotation)
        {
            slot = -1;
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!TryReadType(buffer, length, out BasisCameraDollyPacketType type) ||
                type != BasisCameraDollyPacketType.PointMove)
            {
                return false;
            }
            if (length < MoveSize) return false;
            if (buffer[1] >= MaxPoints) return false;

            slot = buffer[1];
            int offset = 2;
            Point point = ReadPoint(buffer, ref offset);
            position = point.Position;
            rotation = point.Rotation;
            return true;
        }

        public static bool TryReadClaim(byte[] buffer, int length, out int slot, out bool claimed)
        {
            slot = -1;
            claimed = false;

            if (!TryReadType(buffer, length, out BasisCameraDollyPacketType type) ||
                type != BasisCameraDollyPacketType.Claim)
            {
                return false;
            }
            if (length < ClaimSize) return false;
            if (buffer[1] >= MaxPoints) return false;

            slot = buffer[1];
            claimed = buffer[2] != 0;
            return true;
        }

        // ---- Primitives ------------------------------------------------------------------

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

            // A quaternion that arrived as all zeros — from a truncated or hand-made packet — is
            // not a rotation, and handing one to a transform silently produces NaNs downstream.
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
