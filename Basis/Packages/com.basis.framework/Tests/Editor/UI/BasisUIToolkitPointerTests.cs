using Basis.Scripts.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Guards the press-edge state machine. Both bugs this pins were live during development:
    /// a hold sliding onto a freshly spawned panel pressed it, and a release could arrive
    /// unpaired after its panel went away.
    /// </summary>
    public class BasisUIToolkitPointerTests
    {
        /// <summary>
        /// Fresh trigger pull over a panel is the only thing that may press.
        /// </summary>
        [Test]
        public void RisingEdgeWhileNotPressed_Presses()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(true, true, false),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Press));
        }

        /// <summary>
        /// THE phantom-click guard. Press on panel A opens panel B under the pointer; the trigger
        /// is still held from that same press. B must receive hover, never a press — this is the
        /// spawned-menu false click the uGUI path already had to fix once (see the
        /// pressTarget == pointerEnter rule in BasisUIRaycastProcess).
        /// </summary>
        [Test]
        public void HeldTriggerArrivingOnNewPanel_DoesNotPress()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(false, true, false),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Move));
        }

        [Test]
        public void ReleaseAfterPress_Releases()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(false, false, true),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Release));
        }

        /// <summary>
        /// Holding through a press is a drag, not a re-press — UI Toolkit's own pointer capture
        /// routes these to the captured element.
        /// </summary>
        [Test]
        public void HeldWhilePressed_Moves()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(false, true, true),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Move));
        }

        [Test]
        public void NoInputAndNotPressed_Moves()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(false, false, false),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Move));
        }

        /// <summary>
        /// A rising edge cannot re-press something already pressed, or a click would double-fire.
        /// </summary>
        [Test]
        public void RisingEdgeWhileAlreadyPressed_DoesNotPressAgain()
        {
            Assert.That(BasisUIToolkitPointer.ResolveAction(true, true, true),
                Is.EqualTo(BasisUIToolkitPointer.PointerAction.Move));
        }

        /// <summary>
        /// The edge is measured against the input itself, not against whichever panel is under it,
        /// which is what makes the guard above possible.
        /// </summary>
        [Test]
        public void BeginFrame_ReportsEdgeOnlyOnTheDownTransition()
        {
            BasisUIToolkitPointer pointer = new BasisUIToolkitPointer();

            pointer.BeginFrame(false);
            Assert.That(pointer.RisingEdgeThisFrame, Is.False, "Idle frame is not an edge.");

            pointer.BeginFrame(true);
            Assert.That(pointer.RisingEdgeThisFrame, Is.True, "First held frame is the edge.");

            pointer.BeginFrame(true);
            Assert.That(pointer.RisingEdgeThisFrame, Is.False, "A sustained hold is not a new edge.");

            pointer.BeginFrame(false);
            Assert.That(pointer.RisingEdgeThisFrame, Is.False, "Release is not an edge.");

            pointer.BeginFrame(true);
            Assert.That(pointer.RisingEdgeThisFrame, Is.True, "A deliberate re-press is a new edge.");
        }

        [Test]
        public void FreshPointer_IsNotPressedAndDoesNotWantCapture()
        {
            BasisUIToolkitPointer pointer = new BasisUIToolkitPointer();

            Assert.That(pointer.IsPressed, Is.False);
            Assert.That(pointer.WantsCapture, Is.False);
            Assert.That(pointer.CapturedPanel, Is.Null);
        }

        /// <summary>
        /// Release must be safe to call unconditionally — the process loop calls it every frame on
        /// every branch that is not driving a panel, including before anything was ever touched.
        /// </summary>
        [Test]
        public void ReleaseOnUntouchedPointer_IsSafeAndClearsState()
        {
            BasisUIToolkitPointer pointer = new BasisUIToolkitPointer();

            Assert.DoesNotThrow(() => pointer.Release());
            Assert.That(pointer.IsPressed, Is.False);
            Assert.That(pointer.WantsCapture, Is.False);
        }

        /// <summary>
        /// Each pointer source — both hands' rays and both fingertips — needs its own id, or they
        /// share one hover and one capture.
        /// </summary>
        [Test]
        public void SeparatePointers_ReceiveDistinctIds()
        {
            BasisUIToolkitPointer first = new BasisUIToolkitPointer();
            BasisUIToolkitPointer second = new BasisUIToolkitPointer();

            Assert.That(first.PointerIdValue, Is.Not.EqualTo(second.PointerIdValue));
        }

        [Test]
        public void PointerIds_FallInTheTrackedRange()
        {
            if (!BasisUIToolkitPointerIdentity.Supported)
            {
                Assert.Ignore("Tracked identity unavailable; pointers fall back to a single pen id.");
            }

            BasisUIToolkitPointer pointer = new BasisUIToolkitPointer();

            Assert.That(pointer.PointerIdValue, Is.GreaterThanOrEqualTo(PointerId.trackedPointerIdBase));
            Assert.That(pointer.PointerIdValue, Is.LessThan(PointerId.trackedPointerIdBase + PointerId.trackedPointerCount));
        }
    }
}
