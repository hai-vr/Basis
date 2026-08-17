using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// What happens to a dolly point in the moment after somebody lets go of it.
    ///
    /// <para>All of these are about arrival order rather than about a wrong number. The author's
    /// roster is a snapshot, so one captured before a drag can land after it; taking that at face
    /// value is what makes a point spring back to where it started a moment after it was moved,
    /// which reads as "grabbing a point does not sync" even when every byte on the wire is right.</para>
    /// </summary>
    public class BasisCameraDollySettleGateTests
    {
        private static readonly Vector3 Dropped = new Vector3(3f, 1.5f, -2f);
        private static readonly Vector3 WhereItStarted = new Vector3(-4f, 0.25f, 8f);
        private static readonly Quaternion DroppedRotation = new Quaternion(0f, 0.342020f, 0f, 0.939693f);

        [Test]
        public void AStaleRosterDoesNotPullAReleasedPointBack()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 0f);

            Assert.That(gate.Blocks(0, WhereItStarted, Quaternion.identity), Is.True,
                "A roster from before the drag must not be allowed to undo it.");
        }

        [Test]
        public void TheAuthorAgreeingEndsTheWait()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 0f);

            Assert.That(gate.Blocks(0, Dropped, DroppedRotation), Is.False,
                "The position that was asked for coming back is the author confirming the move.");
            Assert.That(gate.IsSettling(0), Is.False, "A confirmed point is done waiting.");
        }

        [Test]
        public void AConfirmationSurvivesTheRoundTripsRounding()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 0f);

            Vector3 nudged = Dropped + new Vector3(1e-5f, -1e-5f, 1e-5f);
            Assert.That(gate.Blocks(0, nudged, DroppedRotation), Is.False,
                "A pose makes a float trip through the packet and a transform; that must still read as the same place.");
        }

        [Test]
        public void APointOnlyDefendsItsOwnSlot()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(2, Dropped, DroppedRotation, 0f);

            Assert.That(gate.Blocks(1, WhereItStarted, Quaternion.identity), Is.False,
                "Holding one point must not freeze the rest of the track.");
        }

        [Test]
        public void TheWaitRunsOutSoARefusedMoveIsNotHeldForever()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 100f);

            gate.Expire(100f + BasisCameraDollySettleGate.SettleSeconds - 0.01f);
            Assert.That(gate.Blocks(0, WhereItStarted, Quaternion.identity), Is.True,
                "The wait must cover the author's keyframe interval, or a slow confirmation loses.");

            gate.Expire(100f + BasisCameraDollySettleGate.SettleSeconds);
            Assert.That(gate.Blocks(0, WhereItStarted, Quaternion.identity), Is.False,
                "A locked track never confirms, and a point held forever is this client authoring somebody else's move.");
        }

        [Test]
        public void TheWaitOutlastsTheAuthorsKeyframe()
        {
            // The author resends unprompted every two seconds. A wait shorter than that could time
            // out in the gap between the move landing and the roster that carries it back.
            Assert.That(BasisCameraDollySettleGate.SettleSeconds, Is.GreaterThan(2f));
        }

        [Test]
        public void PickingThePointBackUpGivesUpOnTheOldRelease()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 0f);
            gate.Release(0);

            Assert.That(gate.Blocks(0, WhereItStarted, Quaternion.identity), Is.False,
                "A fresh grab is the live answer; the place it was last put down no longer means anything.");
        }

        [Test]
        public void ShorteningTheTrackForgetsThePointsItNoLongerHas()
        {
            var gate = new BasisCameraDollySettleGate();
            gate.Hold(0, Dropped, DroppedRotation, 0f);
            gate.Hold(3, Dropped, DroppedRotation, 0f);

            gate.DropAtOrAbove(2);

            Assert.That(gate.IsSettling(3), Is.False, "A slot the track no longer has cannot be waiting on anything.");
            Assert.That(gate.IsSettling(0), Is.True, "Trimming the tail must not disturb the points that remain.");
        }

        [Test]
        public void NothingIsHeldByDefault()
        {
            var gate = new BasisCameraDollySettleGate();

            Assert.That(gate.Count, Is.Zero);
            Assert.That(gate.Blocks(0, WhereItStarted, Quaternion.identity), Is.False,
                "A track nobody has touched takes the author's word for everything.");
        }
    }
}
