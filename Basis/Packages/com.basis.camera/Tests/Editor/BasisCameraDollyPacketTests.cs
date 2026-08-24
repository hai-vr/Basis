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

        private static BasisCameraDollyPacket.Motion Motion() => new BasisCameraDollyPacket.Motion
        {
            Speed = -2.75f,
            EaseIn = BasisCameraEase.Back,
            EaseInPortion = 0.35f,
            EaseOut = BasisCameraEase.Bounce,
            EaseOutPortion = 0.2f,
            Scale = 1.6f,
        };

        [Test]
        public void TheMoveRidesWithTheTrack_SoAMirrorCanPaintTheSpeed()
        {
            // The colours along the path are drawn from the move, not the points: the same
            // waypoints read cool under a slow move and hot under a fast one. Left off the wire,
            // every mirror painted the resting colour while the author's own track was coloured.
            BasisCameraDollyPacket.Point[] sent = Points(3);
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(3)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, sent, 3, Motion(), true);
            Assert.That(written, Is.EqualTo(buffer.Length), "The writer and the size helper disagree.");

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read, out _, out _, out int count,
                out BasisCameraDollyPacket.Motion motion, out bool speedColors), Is.True);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(speedColors, Is.True);
            Assert.That(motion.Speed, Is.EqualTo(-2.75f), "The sign is the direction of travel, and the run-up sits at the end it starts from.");
            Assert.That(motion.EaseIn, Is.EqualTo(BasisCameraEase.Back));
            Assert.That(motion.EaseInPortion, Is.EqualTo(0.35f).Within(1e-6f));
            Assert.That(motion.EaseOut, Is.EqualTo(BasisCameraEase.Bounce));
            Assert.That(motion.EaseOutPortion, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(motion.Scale, Is.EqualTo(1.6f).Within(1e-6f), "The author's scale is what the ramp was drawn against on their screen.");
            Assert.That(read[2].Position, Is.EqualTo(sent[2].Position), "The move must not shift the points.");
        }

        [Test]
        public void AnAuthorWhoIsNotPainting_SendsThatToo()
        {
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(2)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(2), 2, Motion(), false);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read, out _, out _, out _,
                out BasisCameraDollyPacket.Motion motion, out bool speedColors), Is.True);

            Assert.That(speedColors, Is.False, "A flat track on the author's screen is a flat track on everyone's.");
            Assert.That(motion.Speed, Is.EqualTo(-2.75f), "The move travels either way; the flag is only whether to paint with it.");
        }

        [Test]
        public void ATrackFromABuildThatSentNoMove_StillArrives()
        {
            // The move is a trailing block behind a flag. A roster from before it existed has
            // neither, and has to read exactly as it always did, points and all, rather than be
            // refused for being short.
            BasisCameraDollyPacket.Point[] sent = Points(2);
            byte[] modern = new byte[BasisCameraDollyPacket.RosterSize(2)];
            int written = BasisCameraDollyPacket.WriteRoster(modern, BasisCameraDollySync.NetworkedLocked, true, sent, 2, Motion(), true);

            int legacyLength = written - BasisCameraDollyPacket.MotionSize;
            byte[] legacy = new byte[legacyLength];
            System.Array.Copy(modern, legacy, legacyLength);
            legacy[3] = 1;   // looped, and nothing else: the only flag a build without the move knew

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(legacy, legacyLength, read,
                out BasisCameraDollySync mode, out bool looped, out int count,
                out BasisCameraDollyPacket.Motion motion, out bool speedColors), Is.True);

            Assert.That(mode, Is.EqualTo(BasisCameraDollySync.NetworkedLocked));
            Assert.That(looped, Is.True);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(read[1].Position, Is.EqualTo(sent[1].Position));
            Assert.That(speedColors, Is.False, "No move on the wire means nothing to paint with.");
            Assert.That(motion.Scale, Is.EqualTo(0f), "No scale either, so the track falls back to its own.");
        }

        [Test]
        public void ThePointsSitWhereTheyAlwaysDid_SoAnOlderReaderStillFindsThem()
        {
            // The block trails the points rather than leading them: a build that reads only as far
            // as its own idea of the size takes every point from the same offset it always has, and
            // simply never looks at what follows.
            BasisCameraDollyPacket.Point[] sent = Points(2);
            byte[] modern = new byte[BasisCameraDollyPacket.RosterSize(2)];
            int written = BasisCameraDollyPacket.WriteRoster(modern, BasisCameraDollySync.Networked, false, sent, 2, Motion(), true);

            byte[] withoutMove = new byte[BasisCameraDollyPacket.RosterSize(2)];
            BasisCameraDollyPacket.WriteRoster(withoutMove, BasisCameraDollySync.Networked, false, sent, 2);

            int pointsEnd = written - BasisCameraDollyPacket.MotionSize;
            for (int Index = 4; Index < pointsEnd; Index++)
            {
                Assert.That(modern[Index], Is.EqualTo(withoutMove[Index]), $"byte {Index} moved");
            }
        }

        [Test]
        public void AMoveCutShortIsCorruptRatherThanOld()
        {
            // The flag says a block follows. A packet that ends inside it is damaged, and is refused
            // the way a track short of its points is, rather than read as a roster without a move.
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(1)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(1), 1, Motion(), true);

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written - 1, read, out _, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void AnEaseCurveThisBuildDoesNotHave_ArrivesAsLinear()
        {
            // A newer build's curve is not a reason to lose the whole track: the ramp is drawn
            // with a straight run-up instead, which is at worst a little less colourful.
            byte[] buffer = new byte[BasisCameraDollyPacket.RosterSize(1)];
            int written = BasisCameraDollyPacket.WriteRoster(buffer, BasisCameraDollySync.Networked, false, Points(1), 1, Motion(), true);
            buffer[written - BasisCameraDollyPacket.MotionSize + 4] = 200;

            var read = new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];
            Assert.That(BasisCameraDollyPacket.TryReadRoster(buffer, written, read, out _, out _, out _,
                out BasisCameraDollyPacket.Motion motion, out _), Is.True);

            Assert.That(motion.EaseIn, Is.EqualTo(BasisCameraEase.Linear));
            Assert.That(motion.EaseOut, Is.EqualTo(BasisCameraEase.Bounce), "Only the unknown curve is replaced.");
        }

        [Test]
        public void TheMoveRoundTripsThroughTheSettingsItWasTakenFrom()
        {
            BasisCameraDollySettings dolly = BasisCameraDollySettings.Default;
            dolly.speed = 3.5f;
            dolly.easeIn = BasisCameraEase.Expo;
            dolly.easeInPortion = 0.4f;
            dolly.easeOut = BasisCameraEase.Circ;
            dolly.easeOutPortion = 0.1f;
            dolly.damping = 0.9f;

            BasisCameraDollyPacket.Motion motion = BasisCameraDollyPacket.Motion.From(dolly, 2f);
            BasisCameraDollySettings restored = BasisCameraDollySettings.Default;
            motion.ApplyTo(ref restored);

            Assert.That(restored.speed, Is.EqualTo(3.5f));
            Assert.That(restored.easeIn, Is.EqualTo(BasisCameraEase.Expo));
            Assert.That(restored.easeInPortion, Is.EqualTo(0.4f));
            Assert.That(restored.easeOut, Is.EqualTo(BasisCameraEase.Circ));
            Assert.That(restored.easeOutPortion, Is.EqualTo(0.1f));
            Assert.That(restored.damping, Is.EqualTo(BasisCameraDollySettings.Default.damping),
                "The shape of the move travels; the playhead and the damping are the author's own.");

            Assert.That(motion.SameAs(BasisCameraDollyPacket.Motion.From(dolly, 2f)), Is.True);
            dolly.speed += 0.5f;
            Assert.That(motion.SameAs(BasisCameraDollyPacket.Motion.From(dolly, 2f)), Is.False,
                "A speed change has to count as a change, or it only leaves the author at the keyframe rate.");
        }

        [Test]
        public void PaintingNeedsAPlayMoveFittedAndTheColoursSwitchedOn()
        {
            // The one rule the author's own line and the wire flag both read, so what leaves the
            // author is exactly what their screen is showing.
            var track = new BasisCameraDollyTrack { ColorBySpeed = true, MotionActive = true };
            BasisCameraDollySettings move = BasisCameraDollySettings.Default;
            move.mode = BasisCameraDollyMode.Play;
            track.Motion = move;
            Assert.That(track.PaintsBySpeed, Is.True);

            track.ColorBySpeed = false;
            Assert.That(track.PaintsBySpeed, Is.False, "the toggle is off");
            track.ColorBySpeed = true;

            track.MotionActive = false;
            Assert.That(track.PaintsBySpeed, Is.False, "nothing is fitted");
            track.MotionActive = true;

            move.mode = BasisCameraDollyMode.Manual;
            track.Motion = move;
            Assert.That(track.PaintsBySpeed, Is.False, "a hand-placed playhead has no speed to show");
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
