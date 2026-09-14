using Basis.Scripts.Device_Management;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Devices
{
    public class BasisDeviceOffsetMathTests
    {
        private const float PositionTolerance = 1e-4f;
        private const float RotationTolerance = 1e-6f;

        private static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            Vector3 unit = axis.normalized;
            float half = degrees * 0.5f * Mathf.Deg2Rad;
            float sin = Mathf.Sin(half);
            return new Quaternion(unit.x * sin, unit.y * sin, unit.z * sin, Mathf.Cos(half));
        }

        private static void AssertPosition(Vector3 expected, Vector3 actual)
        {
            float distance = (expected - actual).magnitude;
            Assert.That(distance, Is.LessThan(PositionTolerance), $"expected ({expected.x}, {expected.y}, {expected.z}) but was ({actual.x}, {actual.y}, {actual.z})");
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual)
        {
            float dot = Mathf.Abs((expected.x * actual.x) + (expected.y * actual.y) + (expected.z * actual.z) + (expected.w * actual.w));
            Assert.That(dot, Is.GreaterThan(1f - RotationTolerance), $"expected ({expected.x}, {expected.y}, {expected.z}, {expected.w}) but was ({actual.x}, {actual.y}, {actual.z}, {actual.w})");
        }

        [Test]
        public void Inverse_UndoesTheRotation()
        {
            Quaternion rotation = AxisAngle(new Vector3(0.3f, 1f, -0.4f), 73f);
            AssertRotation(Quaternion.identity, rotation * BasisDeviceOffsetMath.Inverse(rotation));
            AssertRotation(Quaternion.identity, BasisDeviceOffsetMath.Inverse(rotation) * rotation);
        }

        [Test]
        public void Compose_ThenRelative_ReturnsTheLocalPose()
        {
            Vector3 parentPosition = new Vector3(1.2f, 0.8f, -0.5f);
            Quaternion parentRotation = AxisAngle(new Vector3(0.2f, 1f, 0.1f), 120f);
            Vector3 localPosition = new Vector3(0.05f, -0.02f, 0.11f);
            Quaternion localRotation = AxisAngle(Vector3.right, -35f);
            BasisDeviceOffsetMath.Compose(parentPosition, parentRotation, localPosition, localRotation, out Vector3 position, out Quaternion rotation);
            BasisDeviceOffsetMath.Relative(parentPosition, parentRotation, position, rotation, out Vector3 roundTripPosition, out Quaternion roundTripRotation);
            AssertPosition(localPosition, roundTripPosition);
            AssertRotation(localRotation, roundTripRotation);
        }

        [Test]
        public void ApplyScaled_LandsWhereScalingTheOffsetDeviceWould()
        {
            Vector3 unscaledPosition = new Vector3(0.3f, 1.4f, 0.2f);
            Quaternion unscaledRotation = AxisAngle(new Vector3(1f, 0.5f, 0.2f), 64f);
            Vector3 offsetPosition = new Vector3(0.04f, -0.08f, 0.12f);
            Quaternion offsetRotation = AxisAngle(Vector3.up, 25f);
            Vector3 rigPosition = new Vector3(0.1f, 0.2f, -0.3f);
            Quaternion rigRotation = AxisAngle(Vector3.up, 90f);
            float deviceScale = 1.35f;

            BasisDeviceOffsetMath.Compose(unscaledPosition, unscaledRotation, offsetPosition, offsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
            Vector3 expectedPosition = rigPosition + (rigRotation * (virtualPosition * deviceScale));
            Quaternion expectedRotation = rigRotation * virtualRotation;

            Vector3 position = rigPosition + (rigRotation * (unscaledPosition * deviceScale));
            Quaternion rotation = rigRotation * unscaledRotation;
            BasisDeviceOffsetMath.ApplyScaled(ref position, ref rotation, offsetPosition, offsetRotation, deviceScale);

            AssertPosition(expectedPosition, position);
            AssertRotation(expectedRotation, rotation);
        }

        [Test]
        public void Retarget_CarriesARigidlyAttachedPoseOntoTheOffsetDevice()
        {
            Vector3 devicePosition = new Vector3(-0.2f, 1.1f, 0.4f);
            Quaternion deviceRotation = AxisAngle(new Vector3(0.4f, 1f, -0.3f), 140f);
            Vector3 attachedPosition = new Vector3(0.01f, -0.03f, -0.09f);
            Quaternion attachedRotation = AxisAngle(Vector3.forward, 90f);
            Vector3 offsetPosition = new Vector3(0f, 0.02f, -0.05f);
            Quaternion offsetRotation = AxisAngle(Vector3.right, 20f);

            BasisDeviceOffsetMath.Compose(devicePosition, deviceRotation, attachedPosition, attachedRotation, out Vector3 position, out Quaternion rotation);
            BasisDeviceOffsetMath.Retarget(devicePosition, deviceRotation, offsetPosition, offsetRotation, ref position, ref rotation);

            BasisDeviceOffsetMath.Compose(devicePosition, deviceRotation, offsetPosition, offsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
            BasisDeviceOffsetMath.Compose(virtualPosition, virtualRotation, attachedPosition, attachedRotation, out Vector3 expectedPosition, out Quaternion expectedRotation);
            AssertPosition(expectedPosition, position);
            AssertRotation(expectedRotation, rotation);
        }

        [Test]
        public void Retarget_WithoutAnOffset_LeavesThePoseAlone()
        {
            Vector3 devicePosition = new Vector3(0.5f, 1.3f, -0.1f);
            Quaternion deviceRotation = AxisAngle(new Vector3(1f, 0.2f, 0.7f), -60f);
            Vector3 position = new Vector3(0.45f, 1.25f, -0.05f);
            Quaternion rotation = AxisAngle(Vector3.up, 33f);
            Vector3 expectedPosition = position;
            Quaternion expectedRotation = rotation;
            BasisDeviceOffsetMath.Retarget(devicePosition, deviceRotation, Vector3.zero, Quaternion.identity, ref position, ref rotation);
            AssertPosition(expectedPosition, position);
            AssertRotation(expectedRotation, rotation);
        }

        [Test]
        public void Retarget_GivesTheSameAnswerWithOrWithoutAPlayspaceLift()
        {
            Vector3 devicePosition = new Vector3(0.1f, 0.9f, 0.3f);
            Quaternion deviceRotation = AxisAngle(new Vector3(0.1f, 1f, 0.5f), 210f);
            Vector3 offsetPosition = new Vector3(0.03f, 0.01f, -0.07f);
            Quaternion offsetRotation = AxisAngle(new Vector3(1f, 1f, 0f), 15f);
            Vector3 lift = new Vector3(0f, 0.37f, 0f);

            Vector3 unliftedPosition = new Vector3(0.12f, 0.85f, 0.25f);
            Quaternion unliftedRotation = AxisAngle(Vector3.right, 45f);
            BasisDeviceOffsetMath.Retarget(devicePosition, deviceRotation, offsetPosition, offsetRotation, ref unliftedPosition, ref unliftedRotation);

            Vector3 liftedPosition = new Vector3(0.12f, 0.85f, 0.25f) + lift;
            Quaternion liftedRotation = AxisAngle(Vector3.right, 45f);
            BasisDeviceOffsetMath.Retarget(devicePosition + lift, deviceRotation, offsetPosition, offsetRotation, ref liftedPosition, ref liftedRotation);

            AssertPosition(unliftedPosition + lift, liftedPosition);
            AssertRotation(unliftedRotation, liftedRotation);
        }

        [Test]
        public void SolveOffset_RebuildsTheHeldPose()
        {
            Vector3 physicalPosition = new Vector3(0.2f, 1.1f, 0.35f);
            Quaternion physicalRotation = AxisAngle(new Vector3(0.3f, 0.8f, -0.2f), 95f);
            Vector3 heldPosition = physicalPosition + new Vector3(0.06f, -0.04f, 0.1f);
            Quaternion heldRotation = AxisAngle(new Vector3(-0.5f, 1f, 0.25f), 130f);
            BasisDeviceOffsetMath.SolveOffset(physicalPosition, physicalRotation, heldPosition, heldRotation, out Vector3 offsetPosition, out Quaternion offsetRotation);
            BasisDeviceOffsetMath.Compose(physicalPosition, physicalRotation, offsetPosition, offsetRotation, out Vector3 position, out Quaternion rotation);
            AssertPosition(heldPosition, position);
            AssertRotation(heldRotation, rotation);
        }

        [Test]
        public void SolveOffset_ClampsEachAxisToTheLimit()
        {
            BasisDeviceOffsetMath.SolveOffset(Vector3.zero, Quaternion.identity, new Vector3(2f, -0.2f, -3f), Quaternion.identity, out Vector3 offsetPosition, out _);
            AssertPosition(new Vector3(BasisDeviceOffsetMath.PositionLimit, -0.2f, -BasisDeviceOffsetMath.PositionLimit), offsetPosition);
        }

        [Test]
        public void GrabbedDevice_FollowsTheHandWhileThePhysicalDeviceAlsoMoves()
        {
            Vector3 devicePosition = new Vector3(0f, 1f, 0.3f);
            Quaternion deviceRotation = AxisAngle(Vector3.up, 10f);
            Vector3 offsetPosition = new Vector3(0.02f, 0f, 0f);
            Quaternion offsetRotation = AxisAngle(Vector3.right, 12f);
            Vector3 handPosition = new Vector3(0.1f, 1.05f, 0.35f);
            Quaternion handRotation = AxisAngle(Vector3.forward, -30f);

            BasisDeviceOffsetMath.Compose(devicePosition, deviceRotation, offsetPosition, offsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
            BasisDeviceOffsetMath.Relative(handPosition, handRotation, virtualPosition, virtualRotation, out Vector3 anchorPosition, out Quaternion anchorRotation);

            Vector3 movedHandPosition = handPosition + new Vector3(0.03f, -0.01f, 0.02f);
            Quaternion movedHandRotation = AxisAngle(Vector3.up, 15f) * handRotation;
            Vector3 movedDevicePosition = devicePosition + new Vector3(-0.01f, 0.02f, 0f);
            Quaternion movedDeviceRotation = AxisAngle(Vector3.right, 5f) * deviceRotation;

            BasisDeviceOffsetMath.Compose(movedHandPosition, movedHandRotation, anchorPosition, anchorRotation, out Vector3 heldPosition, out Quaternion heldRotation);
            BasisDeviceOffsetMath.SolveOffset(movedDevicePosition, movedDeviceRotation, heldPosition, heldRotation, out Vector3 solvedPosition, out Quaternion solvedRotation);
            BasisDeviceOffsetMath.Compose(movedDevicePosition, movedDeviceRotation, solvedPosition, solvedRotation, out Vector3 position, out Quaternion rotation);

            AssertPosition(heldPosition, position);
            AssertRotation(heldRotation, rotation);
        }

        [Test]
        public void FromBasis_MapsEveryAxis()
        {
            Quaternion[] rotations =
            {
                Quaternion.identity,
                AxisAngle(Vector3.up, 90f),
                AxisAngle(Vector3.up, 180f),
                AxisAngle(Vector3.right, 180f),
                AxisAngle(Vector3.forward, 180f),
                AxisAngle(new Vector3(1f, 1f, 0f), 170f),
                AxisAngle(new Vector3(0.2f, -0.7f, 0.4f), 250f),
            };
            for (int index = 0; index < rotations.Length; index++)
            {
                Quaternion expected = rotations[index];
                Quaternion actual = BasisDeviceOffsetMath.FromBasis(expected * Vector3.right, expected * Vector3.up, expected * Vector3.forward);
                AssertRotation(expected, actual);
            }
        }

        [Test]
        public void TwoHandFrame_SitsBetweenTheHandsFacingForward()
        {
            Vector3 left = new Vector3(-0.2f, 1f, 0.3f);
            Vector3 right = new Vector3(0.2f, 1f, 0.3f);
            bool built = BasisDeviceOffsetMath.TryBuildTwoHandFrame(left, Quaternion.identity, right, Quaternion.identity, Vector3.up, out Vector3 position, out Quaternion rotation);
            Assert.That(built, Is.True);
            AssertPosition(new Vector3(0f, 1f, 0.3f), position);
            AssertRotation(Quaternion.identity, rotation);
        }

        [Test]
        public void TwoHandFrame_TurningBothHandsTogether_TurnsTheFrameTheSameWay()
        {
            Vector3 center = new Vector3(0.1f, 1.2f, 0.4f);
            Vector3 left = center + new Vector3(-0.15f, 0.02f, 0f);
            Vector3 right = center + new Vector3(0.15f, -0.02f, 0f);
            Quaternion leftRotation = AxisAngle(Vector3.forward, 10f);
            Quaternion rightRotation = AxisAngle(Vector3.forward, -5f);
            Assert.That(BasisDeviceOffsetMath.TryBuildTwoHandFrame(left, leftRotation, right, rightRotation, Vector3.up, out Vector3 startPosition, out Quaternion startRotation), Is.True);

            Quaternion turn = AxisAngle(new Vector3(0.3f, 1f, 0.2f), 50f);
            Assert.That(BasisDeviceOffsetMath.TryBuildTwoHandFrame(center + (turn * (left - center)), turn * leftRotation, center + (turn * (right - center)), turn * rightRotation, Vector3.up, out Vector3 endPosition, out Quaternion endRotation), Is.True);

            AssertPosition(startPosition, endPosition);
            AssertRotation(turn * startRotation, endRotation);
        }

        [Test]
        public void TwoHandFrame_HandsTogether_HasNoFrame()
        {
            Vector3 point = new Vector3(0f, 1f, 0f);
            Assert.That(BasisDeviceOffsetMath.TryBuildTwoHandFrame(point, Quaternion.identity, point + new Vector3(0.001f, 0f, 0f), Quaternion.identity, Vector3.up, out _, out _), Is.False);
        }

        [Test]
        public void TwoHandFrame_HandsPointingUpTheSpan_UsesTheFallbackUp()
        {
            Quaternion alongSpan = AxisAngle(Vector3.forward, -90f);
            bool built = BasisDeviceOffsetMath.TryBuildTwoHandFrame(new Vector3(-0.2f, 1f, 0f), alongSpan, new Vector3(0.2f, 1f, 0f), alongSpan, Vector3.up, out _, out Quaternion rotation);
            Assert.That(built, Is.True);
            AssertRotation(Quaternion.identity, rotation);
        }

        [Test]
        public void Format_ThenTryParse_RoundTrips()
        {
            Vector3 position = new Vector3(0.0123456f, -0.4999f, 0.25f);
            Quaternion rotation = BasisDeviceOffsetMath.Normalize(new Quaternion(0.1f, -0.2f, 0.3f, 0.9f));
            Assert.That(BasisDeviceOffsetMath.TryParse(BasisDeviceOffsetMath.Format(position, rotation), out Vector3 parsedPosition, out Quaternion parsedRotation), Is.True);
            Assert.That(parsedPosition.x, Is.EqualTo(position.x));
            Assert.That(parsedPosition.y, Is.EqualTo(position.y));
            Assert.That(parsedPosition.z, Is.EqualTo(position.z));
            AssertRotation(rotation, parsedRotation);
        }

        [TestCase("")]
        [TestCase("0,0,0")]
        [TestCase("0,0,0,0,0,0,1,0")]
        [TestCase("x,0,0,0,0,0,1")]
        [TestCase("NaN,0,0,0,0,0,1")]
        [TestCase("0,0,0,0,0,Infinity,1")]
        public void TryParse_RejectsMalformedText(string text)
        {
            Assert.That(BasisDeviceOffsetMath.TryParse(text, out _, out _), Is.False);
        }

        [Test]
        public void TryParse_ClampsPositionAndNormalizesRotation()
        {
            Assert.That(BasisDeviceOffsetMath.TryParse("2,-3,0.1,0,0,0,2", out Vector3 position, out Quaternion rotation), Is.True);
            AssertPosition(new Vector3(BasisDeviceOffsetMath.PositionLimit, -BasisDeviceOffsetMath.PositionLimit, 0.1f), position);
            Assert.That(rotation.w, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void IsIdentity_OnlyForANegligibleOffset()
        {
            Assert.That(BasisDeviceOffsetMath.IsIdentity(Vector3.zero, Quaternion.identity), Is.True);
            Assert.That(BasisDeviceOffsetMath.IsIdentity(Vector3.zero, new Quaternion(0f, 0f, 0f, -1f)), Is.True);
            Assert.That(BasisDeviceOffsetMath.IsIdentity(new Vector3(0f, 0.001f, 0f), Quaternion.identity), Is.False);
            Assert.That(BasisDeviceOffsetMath.IsIdentity(Vector3.zero, AxisAngle(Vector3.up, 0.5f)), Is.False);
        }

        [TestCase(0f, 0f)]
        [TestCase(90f, 90f)]
        [TestCase(270f, -90f)]
        [TestCase(359.5f, -0.5f)]
        [TestCase(180f, 180f)]
        [TestCase(-190f, 170f)]
        [TestCase(725f, 5f)]
        public void WrapDegrees_KeepsAnglesInTheSliderRange(float degrees, float expected)
        {
            Assert.That(BasisDeviceOffsetMath.WrapDegrees(degrees), Is.EqualTo(expected).Within(1e-3f));
        }

        private static void AssertAngle(float expected, float actual)
        {
            Assert.That(Mathf.Abs(BasisDeviceOffsetMath.WrapDegrees(actual - expected)), Is.LessThan(1e-2f), $"expected {expected} but was {actual}");
        }

        [Test]
        public void FromEuler_FollowsUnityAxisConventions()
        {
            AssertPosition(Vector3.right, BasisDeviceOffsetMath.FromEuler(new Vector3(0f, 90f, 0f)) * Vector3.forward);
            AssertPosition(Vector3.down, BasisDeviceOffsetMath.FromEuler(new Vector3(90f, 0f, 0f)) * Vector3.forward);
            AssertPosition(Vector3.up, BasisDeviceOffsetMath.FromEuler(new Vector3(0f, 0f, 90f)) * Vector3.right);
            AssertPosition(Vector3.back, BasisDeviceOffsetMath.FromEuler(new Vector3(90f, 90f, 0f)) * Vector3.right);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(30f, 45f, 60f)]
        [TestCase(-45f, 170f, -120f)]
        [TestCase(89f, -30f, 10f)]
        [TestCase(-60f, -179f, 179f)]
        public void ToEuler_UndoesFromEuler(float x, float y, float z)
        {
            Vector3 euler = BasisDeviceOffsetMath.ToEuler(BasisDeviceOffsetMath.FromEuler(new Vector3(x, y, z)));
            AssertAngle(x, euler.x);
            AssertAngle(y, euler.y);
            AssertAngle(z, euler.z);
        }

        [Test]
        public void ToEuler_AtGimbalLock_RebuildsTheSameRotation()
        {
            Quaternion rotation = BasisDeviceOffsetMath.FromEuler(new Vector3(90f, 30f, 20f));
            Vector3 euler = BasisDeviceOffsetMath.ToEuler(rotation);
            AssertAngle(90f, euler.x);
            AssertRotation(rotation, BasisDeviceOffsetMath.FromEuler(euler));
        }

        [Test]
        public void NearestEuler_StaysContinuousAcrossTheYawWrap()
        {
            Vector3 euler = BasisDeviceOffsetMath.NearestEuler(BasisDeviceOffsetMath.FromEuler(new Vector3(0f, -179f, 0f)), new Vector3(0f, 179f, 0f));
            Assert.That(euler.y, Is.EqualTo(181f).Within(1e-2f));
        }

        [Test]
        public void NearestEuler_KeepsPitchingPastNinetyInTheSameFamily()
        {
            Vector3 euler = BasisDeviceOffsetMath.NearestEuler(BasisDeviceOffsetMath.FromEuler(new Vector3(95f, 10f, 20f)), new Vector3(85f, 10f, 20f));
            Assert.That(euler.x, Is.EqualTo(95f).Within(1e-2f));
            Assert.That(euler.y, Is.EqualTo(10f).Within(1e-2f));
            Assert.That(euler.z, Is.EqualTo(20f).Within(1e-2f));
        }

        [Test]
        public void ConstrainOffset_MovesOnlyTheChosenPositionAxes()
        {
            Vector3 baseEuler = new Vector3(10f, 20f, 30f);
            Vector3 reference = baseEuler;
            BasisDeviceOffsetMath.ConstrainOffset(BasisDeviceOffsetAxes.PositionY, new Vector3(0.01f, 0.02f, 0.03f), baseEuler, new Vector3(0.1f, 0.2f, 0.3f), BasisDeviceOffsetMath.FromEuler(new Vector3(40f, 50f, 60f)), ref reference, out Vector3 position, out Quaternion rotation);
            AssertPosition(new Vector3(0.01f, 0.2f, 0.03f), position);
            AssertRotation(BasisDeviceOffsetMath.FromEuler(baseEuler), rotation);
        }

        [Test]
        public void ConstrainOffset_TurnsOnlyTheChosenRotationAxes()
        {
            Vector3 baseEuler = new Vector3(10f, 20f, 30f);
            Vector3 reference = baseEuler;
            BasisDeviceOffsetMath.ConstrainOffset(BasisDeviceOffsetAxes.RotationY, new Vector3(0.01f, 0.02f, 0.03f), baseEuler, new Vector3(0.1f, 0.2f, 0.3f), BasisDeviceOffsetMath.FromEuler(new Vector3(15f, 60f, 40f)), ref reference, out Vector3 position, out Quaternion rotation);
            AssertPosition(new Vector3(0.01f, 0.02f, 0.03f), position);
            Vector3 euler = BasisDeviceOffsetMath.ToEuler(rotation);
            AssertAngle(10f, euler.x);
            AssertAngle(60f, euler.y);
            AssertAngle(30f, euler.z);
        }

        [Test]
        public void ConstrainOffset_StacksPositionAndRotationAxes()
        {
            Vector3 baseEuler = new Vector3(10f, 20f, 30f);
            Vector3 reference = baseEuler;
            BasisDeviceOffsetAxes axes = BasisDeviceOffsetAxes.PositionX | BasisDeviceOffsetAxes.PositionZ | BasisDeviceOffsetAxes.RotationX | BasisDeviceOffsetAxes.RotationZ;
            BasisDeviceOffsetMath.ConstrainOffset(axes, new Vector3(0.01f, 0.02f, 0.03f), baseEuler, new Vector3(0.1f, 0.2f, 0.3f), BasisDeviceOffsetMath.FromEuler(new Vector3(15f, 60f, 40f)), ref reference, out Vector3 position, out Quaternion rotation);
            AssertPosition(new Vector3(0.1f, 0.02f, 0.3f), position);
            Vector3 euler = BasisDeviceOffsetMath.ToEuler(rotation);
            AssertAngle(15f, euler.x);
            AssertAngle(20f, euler.y);
            AssertAngle(40f, euler.z);
        }

        [Test]
        public void ConstrainOffset_AllRotationAxes_KeepTheGrabbedRotation()
        {
            Vector3 reference = Vector3.zero;
            Quaternion grabbed = BasisDeviceOffsetMath.FromEuler(new Vector3(60f, 120f, -45f));
            BasisDeviceOffsetMath.ConstrainOffset(BasisDeviceOffsetAxes.Rotation, Vector3.zero, Vector3.zero, Vector3.zero, grabbed, ref reference, out _, out Quaternion rotation);
            AssertRotation(grabbed, rotation);
        }

        [Test]
        public void ConstrainOffset_PitchStopsAtTheLimit()
        {
            Vector3 baseEuler = new Vector3(80f, 0f, 0f);
            Vector3 reference = baseEuler;
            BasisDeviceOffsetMath.ConstrainOffset(BasisDeviceOffsetAxes.RotationX, Vector3.zero, baseEuler, Vector3.zero, BasisDeviceOffsetMath.FromEuler(new Vector3(100f, 0f, 0f)), ref reference, out _, out Quaternion rotation);
            AssertRotation(BasisDeviceOffsetMath.FromEuler(new Vector3(90f, 0f, 0f)), rotation);
        }

        [Test]
        public void FollowFrame_TurningInPlace_DoesNotSwingTheDevice()
        {
            Vector3 framePosition = new Vector3(0.2f, 1.1f, 0.3f);
            Quaternion frameRotation = AxisAngle(new Vector3(0.2f, 1f, 0f), 30f);
            Vector3 anchorPosition = new Vector3(0.3f, 1f, 0.45f);
            Quaternion anchorRotation = AxisAngle(Vector3.right, 15f);
            Quaternion turn = AxisAngle(Vector3.up, 70f);
            BasisDeviceOffsetMath.FollowFrame(framePosition, frameRotation, anchorPosition, anchorRotation, framePosition, turn * frameRotation, out Vector3 position, out Quaternion rotation);
            AssertPosition(anchorPosition, position);
            AssertRotation(turn * anchorRotation, rotation);
        }

        [Test]
        public void FollowFrame_MovingTheHand_SlidesTheDeviceByTheSameAmount()
        {
            Vector3 framePosition = new Vector3(0.2f, 1.1f, 0.3f);
            Quaternion frameRotation = AxisAngle(new Vector3(0.2f, 1f, 0f), 30f);
            Vector3 anchorPosition = new Vector3(0.3f, 1f, 0.45f);
            Quaternion anchorRotation = AxisAngle(Vector3.right, 15f);
            Vector3 move = new Vector3(0.05f, -0.02f, 0.1f);
            BasisDeviceOffsetMath.FollowFrame(framePosition, frameRotation, anchorPosition, anchorRotation, framePosition + move, frameRotation, out Vector3 position, out Quaternion rotation);
            AssertPosition(anchorPosition + move, position);
            AssertRotation(anchorRotation, rotation);
        }
    }
}
