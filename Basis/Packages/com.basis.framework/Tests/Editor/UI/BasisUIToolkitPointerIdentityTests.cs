using Basis.Scripts.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Guards the one genuinely fragile part of the UI Toolkit integration.
    ///
    /// UI Toolkit models XR pointers but exposes no public way to construct one — every public
    /// GetPooled overload derives its id from a mouse, touch or pen. BasisUIToolkitPointerIdentity
    /// binds the non-public setters once per event type. That binding is the thing a Unity upgrade
    /// can silently break, and the failure is quiet: pointers collapse onto one id and two hands
    /// start fighting over a single hover, with nothing logged.
    ///
    /// If <see cref="Supported"/> fails after an editor upgrade, the integration still works —
    /// it has fallen back to a single pen pointer. Re-point the binding at the renamed member.
    /// </summary>
    public class BasisUIToolkitPointerIdentityTests
    {
        private static PenData NeutralPen => new PenData
        {
            position = Vector2.zero,
            deltaPos = Vector2.zero,
            pressure = 0f,
            penStatus = PenStatus.None,
            contactType = PenEventType.NoContact,
        };

        [Test]
        public void Supported_IsTrue_SoPointersStayIndependent()
        {
            Assert.That(BasisUIToolkitPointerIdentity.Supported, Is.True,
                "Tracked pointer identity failed to bind. Every Basis pointer has collapsed onto a " +
                "single pen id: hover and capture are now shared between hands and fingertips.");
        }

        [Test]
        public void Apply_AssignsRequestedIdAndTrackedType_ToPointerDown()
        {
            int expected = PointerId.trackedPointerIdBase + 2;

            using (PointerDownEvent pointerEvent = PointerDownEvent.GetPooled(NeutralPen, EventModifiers.None))
            {
                BasisUIToolkitPointerIdentity.Apply(pointerEvent, expected);

                Assert.That(pointerEvent.pointerId, Is.EqualTo(expected));
                Assert.That(pointerEvent.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.tracked));
            }
        }

        [Test]
        public void Apply_AssignsRequestedIdAndTrackedType_ToPointerMove()
        {
            int expected = PointerId.trackedPointerIdBase + 3;

            using (PointerMoveEvent pointerEvent = PointerMoveEvent.GetPooled(NeutralPen, EventModifiers.None))
            {
                BasisUIToolkitPointerIdentity.Apply(pointerEvent, expected);

                Assert.That(pointerEvent.pointerId, Is.EqualTo(expected));
                Assert.That(pointerEvent.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.tracked));
            }
        }

        [Test]
        public void Apply_AssignsRequestedIdAndTrackedType_ToPointerUp()
        {
            int expected = PointerId.trackedPointerIdBase + 4;

            using (PointerUpEvent pointerEvent = PointerUpEvent.GetPooled(NeutralPen, EventModifiers.None))
            {
                BasisUIToolkitPointerIdentity.Apply(pointerEvent, expected);

                Assert.That(pointerEvent.pointerId, Is.EqualTo(expected));
                Assert.That(pointerEvent.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.tracked));
            }
        }

        /// <summary>
        /// Distinct ids must survive dispatch preparation — two pointers written in sequence must
        /// not alias, which is what a setter bound to the wrong member would produce.
        /// </summary>
        [Test]
        public void Apply_KeepsSeparateIdsDistinct()
        {
            int first = PointerId.trackedPointerIdBase;
            int second = PointerId.trackedPointerIdBase + 1;

            using (PointerDownEvent a = PointerDownEvent.GetPooled(NeutralPen, EventModifiers.None))
            using (PointerDownEvent b = PointerDownEvent.GetPooled(NeutralPen, EventModifiers.None))
            {
                BasisUIToolkitPointerIdentity.Apply(a, first);
                BasisUIToolkitPointerIdentity.Apply(b, second);

                Assert.That(a.pointerId, Is.Not.EqualTo(b.pointerId));
            }
        }

        /// <summary>
        /// The pen shape is deliberate: a pen reports hover without contact. Touch pointers have no
        /// hover state, and sourcing from touch would make every un-pressed move read as a held
        /// button, leaving panels permanently pressed.
        /// </summary>
        [Test]
        public void PenSourceWithoutContact_ReportsNoPressedButtons()
        {
            using (PointerMoveEvent pointerEvent = PointerMoveEvent.GetPooled(NeutralPen, EventModifiers.None))
            {
                Assert.That(pointerEvent.pressedButtons, Is.Zero,
                    "An un-pressed hover must not report a held button, or panels look permanently pressed.");
            }
        }
    }
}
