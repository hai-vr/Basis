using System;
using System.Collections.Generic;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Drivers
{
    /// <summary>
    /// Covers the geometry the play-space gizmos draw with. The visualiser's whole claim is that a
    /// point it draws lands where a tracked device at that tracking-space point would land, so the
    /// transform is pinned against the device pipeline's own formula (lift, then offset-rotate and
    /// scale) written out independently here rather than called through.
    /// </summary>
    public class BasisPlayspaceGizmoTests
    {
        private const float Tolerance = 1e-5f;

        /// <summary>Yaw quaternion built in managed code, so the suite needs no native Unity runtime.</summary>
        private static Quaternion Yaw(float degrees)
        {
            float half = degrees * 0.5f * Mathf.Deg2Rad;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }

        private static Vector3 ExpectedDeviceLocal(Vector3 trackingPoint, float lift, float deviceScale, Vector3 offsetPosition, Quaternion offsetRotation)
        {
            Vector3 lifted = new Vector3(trackingPoint.x, trackingPoint.y + lift, trackingPoint.z);
            return offsetPosition + (offsetRotation * (lifted * deviceScale));
        }

        [Test]
        public void TrackingLift_IsZeroOnDesktop()
        {
            Assert.That(BasisPlayspaceGizmoCore.TrackingLift(false, 0.6f, true, 0.4f, 0.1f), Is.EqualTo(0f));
        }

        [Test]
        public void TrackingLift_SumsSpaceDragAndGroundingWhileStanding()
        {
            float lift = BasisPlayspaceGizmoCore.TrackingLift(true, 0.6f, false, 0.4f, 0.1f);
            Assert.That(lift, Is.EqualTo(0.7f).Within(Tolerance));
        }

        [Test]
        public void TrackingLift_AddsSeatedDeltaOnlyWhileSeated()
        {
            float standing = BasisPlayspaceGizmoCore.TrackingLift(true, 0.0f, false, 0.35f, 0f);
            float seated = BasisPlayspaceGizmoCore.TrackingLift(true, 0.0f, true, 0.35f, 0f);
            Assert.That(standing, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(seated, Is.EqualTo(0.35f).Within(Tolerance));
        }

        [Test]
        public void TrackingToPlayerLocal_MatchesTheDevicePipelineFormula()
        {
            Vector3 point = new Vector3(1.25f, 0f, -0.75f);
            Quaternion offsetRotation = Yaw(37f);
            Vector3 offsetPosition = new Vector3(0.2f, 0.05f, -0.4f);

            Vector3 actual = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(point, 0.45f, 1.3f, offsetPosition, offsetRotation);
            Vector3 expected = ExpectedDeviceLocal(point, 0.45f, 1.3f, offsetPosition, offsetRotation);

            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance));
        }

        [Test]
        public void TrackingToPlayerLocal_PutsTheTrackingOriginOnTheLiftedFloor()
        {
            Vector3 origin = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(Vector3.zero, 0.5f, 1f, Vector3.zero, Quaternion.identity);
            Assert.That(origin.y, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(origin.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(origin.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void TrackingToPlayerLocal_ScalesTheOutlineWithDeviceScale()
        {
            Vector3 corner = new Vector3(2f, 0f, 0f);
            Vector3 half = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(corner, 0f, 0.5f, Vector3.zero, Quaternion.identity);
            Vector3 doubled = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(corner, 0f, 2f, Vector3.zero, Quaternion.identity);

            Assert.That(half.x, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(doubled.x, Is.EqualTo(4f).Within(Tolerance));
        }

        [Test]
        public void TrackingToPlayerLocal_SeatOffsetRotatesTheWholePlaySpace()
        {
            Quaternion yaw = Yaw(90f);
            Vector3 forward = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(new Vector3(0f, 0f, 1f), 0f, 1f, Vector3.zero, yaw);

            Assert.That(forward.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(forward.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void VerticalOffset_MovesTheFloorByExactlyTheScaledDrag()
        {
            const float drag = 0.42f;
            const float deviceScale = 1.4f;
            const float grounding = 0.08f;

            float lift = BasisPlayspaceGizmoCore.TrackingLift(true, drag, false, 0f, grounding);
            Vector3 lifted = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(Vector3.zero, lift, deviceScale, Vector3.zero, Quaternion.identity);
            Vector3 resting = BasisPlayspaceGizmoCore.TrackingToPlayerLocal(Vector3.zero, lift - drag, deviceScale, Vector3.zero, Quaternion.identity);

            Assert.That(lifted.y - resting.y, Is.EqualTo(drag * deviceScale).Within(Tolerance));
        }

        [Test]
        public void TryComputeBounds_MeasuresARectangle()
        {
            var rect = new List<Vector3>
            {
                new Vector3(-1f, 0f, -1.5f),
                new Vector3(1f, 0f, -1.5f),
                new Vector3(1f, 0f, 1.5f),
                new Vector3(-1f, 0f, 1.5f),
            };

            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(rect, out BasisPlayspaceBoundsMetrics metrics), Is.True);
            Assert.That(metrics.SizeX, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(metrics.SizeZ, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(metrics.Area, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(metrics.Perimeter, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(metrics.Center.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(metrics.Center.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void TryComputeBounds_IsIndependentOfWinding()
        {
            var clockwise = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 2f),
                new Vector3(2f, 0f, 2f),
                new Vector3(2f, 0f, 0f),
            };
            var counter = new List<Vector3>(clockwise);
            counter.Reverse();

            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(clockwise, out BasisPlayspaceBoundsMetrics a), Is.True);
            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(counter, out BasisPlayspaceBoundsMetrics b), Is.True);
            Assert.That(a.Area, Is.EqualTo(b.Area).Within(Tolerance));
            Assert.That(a.Area, Is.EqualTo(4f).Within(Tolerance));
        }

        [Test]
        public void TryComputeBounds_ReportsTheShapesOwnAreaNotTheBoundingBox()
        {
            var lShape = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(2f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 2f),
                new Vector3(0f, 0f, 2f),
            };

            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(lShape, out BasisPlayspaceBoundsMetrics metrics), Is.True);
            Assert.That(metrics.SizeX * metrics.SizeZ, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(metrics.Area, Is.EqualTo(3f).Within(Tolerance));
        }

        [Test]
        public void TryComputeBounds_RefusesAnythingThatIsNotAShape()
        {
            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(null, out BasisPlayspaceBoundsMetrics none), Is.False);
            Assert.That(none.Valid, Is.False);

            var line = new List<Vector3> { Vector3.zero, Vector3.forward };
            Assert.That(BasisPlayspaceGizmoCore.TryComputeBounds(line, out BasisPlayspaceBoundsMetrics tooFew), Is.False);
            Assert.That(tooFew.Valid, Is.False);
        }

        [Test]
        public void StateLabel_ReadsDifferentlyForEveryGate()
        {
            var seen = new HashSet<string>();
            foreach (BasisPlayspaceMoverState state in Enum.GetValues(typeof(BasisPlayspaceMoverState)))
            {
                string label = BasisPlayspaceGizmoCore.StateLabel(state);
                Assert.That(string.IsNullOrEmpty(label), Is.False, $"{state} has no readout label");
                Assert.That(seen.Add(label), Is.True, $"{state} shares its readout label with another state");
            }
        }
    }
}
