using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The dolly track's wire format. Every one of these is about a packet that arrives wrong
    /// rather than one that arrives right: a byte read at the wrong offset, a count a sender
    /// claimed but did not send, a mode from a newer build. None of that is visible locally — it
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
        public void ARosterSurvivesTheRoundTrip()
        {
            BasisCameraDollyPacket.Point[] sent = Points(4);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(4)];

            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, sent, 4);
            Assert.That(written, Is.EqualTo(buffer.Length), "The writer and the size helper disagree.");

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read,
                out BasisCameraDollySync mode, out int count), Is.True);

            Assert.That(mode, Is.EqualTo(BasisCameraDollySync.Networked));
            Assert.That(count, Is.EqualTo(4));
            for (int Index = 0; Index < 4; Index++)
            {
                Assert.That(read[Index].Position, Is.EqualTo(sent[Index].Position));
                Assert.That(read[Index].Rotation.x, Is.EqualTo(sent[Index].Rotation.x).Within(1e-6f));
                Assert.That(read[Index].Rotation.w, Is.EqualTo(sent[Index].Rotation.w).Within(1e-6f));
            }
        }

        [Test]
        public void AnEmptyRosterIsAValidPacket()
        {
            // Deleting the last waypoint has to be sendable, or a cleared track never clears on
            // anyone else's screen.
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(0)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, Points(0), 0);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read, out _, out int count), Is.True);
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void APointMoveSurvivesTheRoundTrip()
        {
            byte[] buffer = new byte[64];
            Vector3 position = new Vector3(-12.5f, 3.25f, 88.125f);
            Quaternion rotation = new Quaternion(0.5f, -0.5f, 0.5f, 0.5f);

            int written = BasisCameraDollyPacket.WritePointMove(buffer, 7, position, rotation);
            Assert.That(written, Is.GreaterThan(0));

            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out int slot,
                out Vector3 readPosition, out Quaternion readRotation), Is.True);

            Assert.That(slot, Is.EqualTo(7));
            Assert.That(readPosition, Is.EqualTo(position));
            Assert.That(readRotation.y, Is.EqualTo(rotation.y).Within(1e-6f));
        }

        [Test]
        public void AClaimSurvivesTheRoundTrip()
        {
            byte[] buffer = new byte[8];

            int written = BasisCameraDollyPacket.WriteClaim(buffer, 3, true);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(buffer, written, out int slot, out bool claimed), Is.True);
            Assert.That(slot, Is.EqualTo(3));
            Assert.That(claimed, Is.True);

            written = BasisCameraDollyPacket.WriteClaim(buffer, 3, false);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(buffer, written, out _, out claimed), Is.True);
            Assert.That(claimed, Is.False);
        }

        [Test]
        public void EachReaderRefusesTheOtherKindsOfPacket()
        {
            // Every read starts by checking the type, so a move can never be read as a roster and
            // land 28 bytes of someone's rotation in the point count.
            byte[] move = new byte[64];
            int written = BasisCameraDollyPacket.WritePointMove(move, 1, Vector3.one, Quaternion.identity);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(move, written, read, out _, out _), Is.False);
            Assert.That(BasisCameraDollyPacket.TryReadClaim(move, written, out _, out _), Is.False);
            Assert.That(BasisCameraDollyPacket.TryReadPointMove(move, written, out _, out _, out _), Is.True);
        }

        [Test]
        public void ATruncatedRosterIsRefusedRatherThanReadPastTheEnd()
        {
            BasisCameraDollyPacket.Point[] sent = Points(6);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(6)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, sent, 6);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];

            // The header still claims six points; the bytes for them are not all there.
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written - 1, read, out _, out _), Is.False,
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
            BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, Points(1), 1);
            buffer[1] = 99;

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, buffer.Length, read, out _, out _), Is.False);
        }

        [Test]
        public void ASlotOutsideTheTrackIsRefusedBothWays()
        {
            byte[] buffer = new byte[64];

            Assert.That(BasisCameraDollyPacket.WritePointMove(buffer, BasisCameraDollyPacket.MaxPoints,
                Vector3.zero, Quaternion.identity), Is.EqualTo(0));
            Assert.That(BasisCameraDollyPacket.WriteClaim(buffer, -1, true), Is.EqualTo(0));

            int written = BasisCameraDollyPacket.WritePointMove(buffer, 5, Vector3.zero, Quaternion.identity);
            buffer[1] = BasisCameraDollyPacket.MaxPoints;
            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out _, out _, out _), Is.False);
        }

        [Test]
        public void ARosterLongerThanTheTrackIsRefused()
        {
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(1)];
            BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, Points(1), 1);
            buffer[2] = BasisCameraDollyPacket.MaxPoints + 1;

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, buffer.Length, read, out _, out _), Is.False,
                "A count past the cap would run the reader off the end of its own array.");
        }

        [Test]
        public void AnAllZeroRotationArrivesAsIdentityRatherThanNaN()
        {
            // Not a rotation, and handing one to a transform produces NaNs several frames later
            // somewhere else entirely.
            byte[] buffer = new byte[64];
            int written = BasisCameraDollyPacket.WritePointMove(buffer, 0, Vector3.zero, Quaternion.identity);
            for (int Index = written - 16; Index < written; Index++)
            {
                buffer[Index] = 0;
            }

            Assert.That(BasisCameraDollyPacket.TryReadPointMove(buffer, written, out _, out _,
                out Quaternion rotation), Is.True);
            Assert.That(rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void AFullTrackStillFitsOnePacket()
        {
            BasisCameraDollyPacket.Point[] sent = Points(BasisCameraDollyPacket.MaxPoints);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(BasisCameraDollyPacket.MaxPoints)];

            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.NetworkedLocked,
                sent, BasisCameraDollyPacket.MaxPoints);

            Assert.That(written, Is.EqualTo(buffer.Length));
            Assert.That(written, Is.LessThan(1200), "A roster has to stay inside one datagram.");
        }
    }
}
