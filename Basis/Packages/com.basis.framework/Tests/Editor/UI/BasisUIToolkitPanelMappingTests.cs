using Basis.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Pins the local-space to panel-space convention used by BOTH the ray pointer and the
    /// fingertip poke. A mirrored axis here is invisible in rendering (the panel still reads
    /// correctly) and only shows up as clicks landing on the wrong control in a headset, so the
    /// corner mapping is asserted explicitly rather than trusted.
    ///
    /// Geometry under test: a 2m x 1m panel presenting a 400x200px document.
    /// Local space is centre-origin, +X right, +Y up. Panel space is top-left origin, +Y DOWN.
    /// </summary>
    public class BasisUIToolkitPanelMappingTests
    {
        private static readonly Vector2 WorldSize = new Vector2(2f, 1f);
        private static readonly Vector2 PanelSize = new Vector2(400f, 200f);
        private const float Tolerance = 1e-3f;

        private static Vector2 Map(Vector3 localPoint)
        {
            bool inside = BasisUIToolkitPanel.TryConvertLocalPointToPanel(
                localPoint, WorldSize, PanelSize, true, out Vector2 panelPosition);

            Assert.That(inside, Is.True, "Corner should be inside the panel bounds.");
            return panelPosition;
        }

        [Test]
        public void TopLeftCorner_MapsToPanelOrigin()
        {
            Vector2 mapped = Map(new Vector3(-1f, 0.5f, 0f));

            Assert.That(mapped.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(mapped.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void TopRightCorner_MapsToPanelWidthAtZeroY()
        {
            Vector2 mapped = Map(new Vector3(1f, 0.5f, 0f));

            Assert.That(mapped.x, Is.EqualTo(PanelSize.x).Within(Tolerance));
            Assert.That(mapped.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void BottomLeftCorner_MapsToPanelHeightAtZeroX()
        {
            Vector2 mapped = Map(new Vector3(-1f, -0.5f, 0f));

            Assert.That(mapped.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(mapped.y, Is.EqualTo(PanelSize.y).Within(Tolerance));
        }

        [Test]
        public void BottomRightCorner_MapsToPanelExtent()
        {
            Vector2 mapped = Map(new Vector3(1f, -0.5f, 0f));

            Assert.That(mapped.x, Is.EqualTo(PanelSize.x).Within(Tolerance));
            Assert.That(mapped.y, Is.EqualTo(PanelSize.y).Within(Tolerance));
        }

        [Test]
        public void Centre_MapsToPanelCentre()
        {
            Vector2 mapped = Map(Vector3.zero);

            Assert.That(mapped.x, Is.EqualTo(PanelSize.x * 0.5f).Within(Tolerance));
            Assert.That(mapped.y, Is.EqualTo(PanelSize.y * 0.5f).Within(Tolerance));
        }

        /// <summary>
        /// Horizontal must NOT be mirrored: moving right in local space moves right in panel space.
        /// </summary>
        [Test]
        public void MovingRightInLocalSpace_IncreasesPanelX()
        {
            Vector2 left = Map(new Vector3(-0.5f, 0f, 0f));
            Vector2 right = Map(new Vector3(0.5f, 0f, 0f));

            Assert.That(right.x, Is.GreaterThan(left.x));
        }

        /// <summary>
        /// Vertical MUST invert: panel space grows downward while local space grows upward.
        /// </summary>
        [Test]
        public void MovingUpInLocalSpace_DecreasesPanelY()
        {
            Vector2 low = Map(new Vector3(0f, -0.25f, 0f));
            Vector2 high = Map(new Vector3(0f, 0.25f, 0f));

            Assert.That(high.y, Is.LessThan(low.y));
        }

        /// <summary>
        /// Depth off the panel plane is irrelevant — the caller has already projected onto it.
        /// </summary>
        [Test]
        public void DepthAlongLocalZ_DoesNotAffectMapping()
        {
            Vector2 onPlane = Map(new Vector3(0.25f, 0.1f, 0f));
            Vector2 offPlane = Map(new Vector3(0.25f, 0.1f, 0.37f));

            Assert.That(offPlane.x, Is.EqualTo(onPlane.x).Within(Tolerance));
            Assert.That(offPlane.y, Is.EqualTo(onPlane.y).Within(Tolerance));
        }

        [Test]
        public void PointOutsidePanel_RejectedWhenBoundsRequired()
        {
            bool inside = BasisUIToolkitPanel.TryConvertLocalPointToPanel(
                new Vector3(1.4f, 0f, 0f), WorldSize, PanelSize, true, out _);

            Assert.That(inside, Is.False);
        }

        /// <summary>
        /// Guards the captured-drag path: while a press is held the pointer must keep producing
        /// coordinates past the panel edge (UI Toolkit clamps sliders itself). Requiring bounds
        /// here would freeze a slider the moment the ray slipped off.
        /// </summary>
        [Test]
        public void PointOutsidePanel_AcceptedAndExtrapolatedWhenCaptured()
        {
            bool ok = BasisUIToolkitPanel.TryConvertLocalPointToPanel(
                new Vector3(1.5f, 0f, 0f), WorldSize, PanelSize, false, out Vector2 mapped);

            Assert.That(ok, Is.True);
            Assert.That(mapped.x, Is.GreaterThan(PanelSize.x), "Off-panel drag should extrapolate past the right edge.");
        }

        [Test]
        public void DegenerateWorldSize_IsRejected()
        {
            Assert.That(BasisUIToolkitPanel.TryConvertLocalPointToPanel(
                Vector3.zero, Vector2.zero, PanelSize, false, out _), Is.False);

            Assert.That(BasisUIToolkitPanel.TryConvertLocalPointToPanel(
                Vector3.zero, new Vector2(-1f, 1f), PanelSize, false, out _), Is.False);
        }
    }
}
