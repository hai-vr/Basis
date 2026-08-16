using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The shared dolly track's wire format. Every one of these is about a packet that arrives
    /// wrong rather than one that arrives right: a byte read at the wrong offset, a count a sender
    /// claimed but did not send, a mode from a newer build. None of it is visible locally — it
    /// shows up as somebody else's camera move being subtly in the wrong place.
    /// </summary>
    public class BasisCameraDollyPacketTests
    {
        private static BasisCameraDollyPacket.Point[] Points(int count)
        {
            var points = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            for (int Index = 0; Index < count; Index++)
            {
                points[Index] = new BasisCameraDollyPacket.Point
                {
                    Position = new Vector3(Index + 0.25f, Index * 2f - 1.5f, Index * -3.75f),
                    Rotation = new Quaternion(0.1f * Index, 0.2f, -0.3f, 0.9f),
                };
            }
            return points;
        }

        [Test]
        public void ATrackSurvivesTheRoundTrip()
        {
            BasisCameraDollyPacket.Point[] sent = Points(4);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(4)];

            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, true, sent, 4);
            Assert.That(written, Is.EqualTo(buffer.Length), "The writer and the size helper disagree.");

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read,
                out BasisCameraDollySync mode, out bool looped, out int count), Is.True);

            Assert.That(mode, Is.EqualTo(BasisCameraDollySync.Networked));
            Assert.That(looped, Is.True);
            Assert.That(count, Is.EqualTo(4));
            for (int Index = 0; Index < 4; Index++)
            {
                Assert.That(read[Index].Position, Is.EqualTo(sent[Index].Position));
                Assert.That(read[Index].Rotation.x, Is.EqualTo(sent[Index].Rotation.x).Within(1e-6f));
                Assert.That(read[Index].Rotation.w, Is.EqualTo(sent[Index].Rotation.w).Within(1e-6f));
            }
        }

        [Test]
        public void AnEmptyTrackIsAValidPacket()
        {
            // Deleting the last waypoint has to be sendable, or a cleared track never clears on
            // anyone else's screen.
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(0)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(0), 0);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read, out _, out _, out int count), Is.True);
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void APointMoveCarriesTheTrackItActsOn()
        {
            // Without the owner, a remote reaching for somebody's point is indistinguishable from
            // them editing their own — and the move would land on the wrong track.
            byte[] buffer = new byte[64];
            Vector3 position = new Vector3(-12.5f, 3.25f, 88.125f);
            Quaternion rotation = new Quaternion(0.5f, -0.5f, 0.5f, 0.5f);

            int written = BasisCameraDollyPacket.WritePointMove(buffer, 4242, 7, position, rotation);
            Assert.That(written, Is.GreaterThan(0));

            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out ushort owner, out int slot,
                out Vector3 readPosition, out Quaternion readRotation), Is.True);

            Assert.That(owner, Is.EqualTo(4242));
            Assert.That(slot, Is.EqualTo(7));
            Assert.That(readPosition, Is.EqualTo(position));
            Assert.That(readRotation.y, Is.EqualTo(rotation.y).Within(1e-6f));
        }

        [Test]
        public void AClaimSurvivesTheRoundTrip()
        {
            byte[] buffer = new byte[16];

            int written = BasisCameraDollyPacket.WriteClaim(buffer, 9, 3, true);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(buffer, written, out ushort owner, out int slot,
                out bool claimed), Is.True);
            Assert.That(owner, Is.EqualTo(9));
            Assert.That(slot, Is.EqualTo(3));
            Assert.That(claimed, Is.True);

            written = BasisCameraDollyPacket.WriteClaim(buffer, 9, 3, false);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(buffer, written, out _, out _, out claimed), Is.True);
            Assert.That(claimed, Is.False);
        }

        [Test]
        public void OwnerZeroIsAReadableOwner()
        {
            // Peer ids are handed out from zero up, so the first player to join owns id 0. A format
            // that could not carry it would silently misroute their track.
            byte[] buffer = new byte[64];
            int written = BasisCameraDollyPacket.WritePointMove(buffer, 0, 1, Vector3.one, Quaternion.identity);

            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out ushort owner, out _, out _, out _), Is.True);
            Assert.That(owner, Is.EqualTo(0));
        }

        [Test]
        public void EachReaderRefusesTheOtherKindsOfPacket()
        {
            // Every read starts by checking the type, so a move can never be read as a roster and
            // land 28 bytes of somebody's rotation in the point count.
            byte[] move = new byte[64];
            int written = BasisCameraDollyPacket.WritePointMove(move, 1, 1, Vector3.one, Quaternion.identity);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(move, written, read, out _, out _, out _), Is.False);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(move, written, out _, out _, out _), Is.False);
            Assert.That(BasisCameraDollyPacket.TryReadPointMove(move, written, out _, out _, out _, out _), Is.True);
        }

        [Test]
        public void ATruncatedTrackIsRefusedRatherThanReadPastTheEnd()
        {
            BasisCameraDollyPacket.Point[] sent = Points(6);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(6)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, sent, 6);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written - 1, read, out _, out _, out _), Is.False,
                "A sender that claimed more points than it sent must not be believed.");
        }

        [Test]
        public void AnUnknownPacketTypeIsRefused()
        {
            byte[] buffer = new byte[16];
            buffer[0] = 200;

            Assert.That(BasisCameraDollyPacket.TryReadType(buffer, buffer.Length, out _), Is.False,
                "A payload from a newer build must be dropped, not guessed at.");
        }

        [Test]
        public void AnUnknownSharingModeIsRefused()
        {
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(1)];
            BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(1), 1);
            buffer[1] = 99;

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, buffer.Length, read, out _, out _, out _), Is.False);
        }

        [Test]
        public void ASlotOutsideTheTrackIsRefusedBothWays()
        {
            byte[] buffer = new byte[64];

            Assert.That(BasisCameraDollyPacket.WritePointMove(buffer, 1, BasisCameraDollyPacket.MaxPoints,
                Vector3.zero, Quaternion.identity), Is.EqualTo(0));
            Assert.That(BasisCameraDollyPacket.WriteClaim(buffer, 1, -1, true), Is.EqualTo(0));

            int written = BasisCameraDollyPacket.WritePointMove(buffer, 1, 5, Vector3.zero, Quaternion.identity);
            buffer[3] = BasisCameraDollyPacket.MaxPoints;
            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void ATrackLongerThanTheCapIsRefused()
        {
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(1)];
            BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(1), 1);
            buffer[2] = BasisCameraDollyPacket.MaxPoints + 1;

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, buffer.Length, read, out _, out _, out _), Is.False,
                "A count past the cap would run the reader off the end of its own array.");
        }

        [Test]
        public void AnAllZeroRotationArrivesAsIdentityRatherThanNaN()
        {
            // Not a rotation, and handing one to a transform produces NaNs several frames later
            // somewhere else entirely.
            byte[] buffer = new byte[64];
            int written = BasisCameraDollyPacket.WritePointMove(buffer, 1, 0, Vector3.zero, Quaternion.identity);
            for (int Index = written - 16; Index < written; Index++)
            {
                buffer[Index] = 0;
            }

            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out _, out _, out _,
                out Quaternion rotation), Is.True);
            Assert.That(rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void AFullTrackStillFitsOnePacket()
        {
            BasisCameraDollyPacket.Point[] sent = Points(BasisCameraDollyPacket.MaxPoints);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(BasisCameraDollyPacket.MaxPoints)];

            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.NetworkedLocked,
                true, sent, BasisCameraDollyPacket.MaxPoints);

            Assert.That(written, Is.EqualTo(buffer.Length));
            Assert.That(written, Is.LessThan(1200), "A track has to stay inside one datagram.");
        }

        [Test]
        public void ClosingTheTrackIsCarriedSeparatelyFromItsPoints()
        {
            // A loop is the same waypoints with one more span through them, so it changes nothing a
            // reader could infer from the points themselves. Left off the wire it is invisible to
            // the author, whose own track closes, and only wrong on everybody else's screen.
            byte[] open = new byte[BasisCameraDollyPacket.RosterSize(3)];
            byte[] closed = new byte[BasisCameraDollyPacket.RosterSize(3)];

            int openWritten = BasisCameraDollyPacket.WriteRoster(open, BasisCameraDollySync.Networked, false, Points(3), 3);
            int closedWritten = BasisCameraDollyPacket.WriteRoster(closed, BasisCameraDollySync.Networked, true, Points(3), 3);

            Assert.That(closedWritten, Is.EqualTo(openWritten), "Closing a track must not change its size.");

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];

            Assert.That(BasisCameraDollyPacket.TryReadRoster(open, openWritten, read,
                out BasisCameraDollySync openMode, out bool openLooped, out int openCount), Is.True);
            Assert.That(openLooped, Is.False);

            Assert.That(BasisCameraDollyPacket.TryReadRoster(closed, closedWritten, read,
                out BasisCameraDollySync closedMode, out bool closedLooped, out int closedCount), Is.True);
            Assert.That(closedLooped, Is.True);

            Assert.That(closedMode, Is.EqualTo(openMode), "The loop flag must not disturb the mode.");
            Assert.That(closedCount, Is.EqualTo(openCount), "The loop flag must not disturb the count.");
        }

        [Test]
        public void OnlyALockedTrackTakesTheMoveAwayFromEveryoneElse()
        {
            // The whole of what the three states mean, asserted where it is decided rather than
            // where it is drawn.
            var track = new BasisCameraDollyTrack { IsAuthor = false };

            track.SyncMode = BasisCameraDollySync.Networked;
            Assert.That(track.CanMovePoints, Is.True, "A shared track is one anyone can reshape.");

            track.SyncMode = BasisCameraDollySync.LocalOnly;
            Assert.That(track.CanMovePoints, Is.True, "A track nobody else sees is your own to move.");

            track.SyncMode = BasisCameraDollySync.NetworkedLocked;
            Assert.That(track.CanMovePoints, Is.False, "A locked track is readable, not writable.");

            track.IsAuthor = true;
            Assert.That(track.CanMovePoints, Is.True, "Locking it must not lock out the person who made it.");
        }
    }
}
