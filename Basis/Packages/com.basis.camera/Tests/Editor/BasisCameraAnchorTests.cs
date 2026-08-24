using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;
using CameraAnchorKind = BasisHandHeldCameraInteractable.CameraAnchorKind;
using CameraPinSpace = BasisHandHeldCameraInteractable.CameraPinSpace;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The anchor: what carries the camera, and the rigid move that carries it.
    ///
    /// <para>The property every test here comes back to is that the camera's pose <em>relative to
    /// its anchor</em> is preserved exactly across a move of that anchor. That is what "bolted to
    /// the boat" means, and it is the one thing a camera on a moving thing has to get right — a
    /// shot lined up on the deck must still be that shot after the deck has turned.</para>
    ///
    /// <para>Awake never runs in edit mode for a plain MonoBehaviour, so AddComponent yields a
    /// camera with its field initializers applied and no scene dependencies, the way the mode
    /// tests use it.</para>
    /// </summary>
    public class BasisCameraAnchorTests
    {
        private const float Tolerance = 1e-4f;

        private GameObject _go;
        private BasisHandHeldCamera _camera;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("HandHeldCameraUnderTest");
            _camera = _go.AddComponent<BasisHandHeldCamera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ---- The transport itself ---------------------------------------------------------------

        [Test]
        public void APointOnTheAnchorTravelsWithIt()
        {
            Vector3 from = new Vector3(1f, 2f, 3f);
            Vector3 to = new Vector3(-4f, 0.5f, 9f);

            Vector3 moved = BasisCameraAnchorMath.TransportPoint(
                from, from, Quaternion.identity, to, Quaternion.identity);

            Assert.That(Vector3.Distance(moved, to), Is.LessThan(Tolerance),
                "A point sitting exactly on the anchor has no offset to preserve, so it lands on it.");
        }

        [Test]
        public void APureTranslationMovesEverythingByTheSameAmount()
        {
            Vector3 offset = new Vector3(0f, 1.5f, -2f);
            Vector3 point = new Vector3(3f, 1f, 4f);

            Vector3 moved = BasisCameraAnchorMath.TransportPoint(
                point, Vector3.zero, Quaternion.identity, offset, Quaternion.identity);

            Assert.That(Vector3.Distance(moved, point + offset), Is.LessThan(Tolerance));
        }

        [Test]
        public void ARotatingAnchorSwingsThePoseAroundItself()
        {
            // Two metres in front of an anchor that turns a quarter turn to the right: the camera
            // must end up two metres to the anchor's right, not still in front of where it was.
            Vector3 anchor = Vector3.zero;
            Vector3 camera = new Vector3(0f, 0f, 2f);

            Vector3 moved = BasisCameraAnchorMath.TransportPoint(
                camera, anchor, Quaternion.identity, anchor, Quaternion.Euler(0f, 90f, 0f));

            Assert.That(Vector3.Distance(moved, new Vector3(2f, 0f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void TheOffsetToTheAnchorIsPreservedExactly()
        {
            Vector3 fromPos = new Vector3(3f, 1f, -2f);
            Quaternion fromRot = Quaternion.Euler(12f, 47f, -8f);
            Vector3 toPos = new Vector3(-9f, 4.5f, 30f);
            Quaternion toRot = Quaternion.Euler(-3f, 215f, 20f);

            Vector3 cameraPos = new Vector3(3.9f, 2.2f, -0.4f);
            Quaternion cameraRot = Quaternion.Euler(5f, 130f, 2f);

            Vector3 before = Quaternion.Inverse(fromRot) * (cameraPos - fromPos);
            Quaternion beforeRot = Quaternion.Inverse(fromRot) * cameraRot;

            Vector3 movedPos = BasisCameraAnchorMath.TransportPoint(cameraPos, fromPos, fromRot, toPos, toRot);
            Quaternion movedRot = BasisCameraAnchorMath.TransportRotation(cameraRot, fromRot, toRot);

            Vector3 after = Quaternion.Inverse(toRot) * (movedPos - toPos);
            Quaternion afterRot = Quaternion.Inverse(toRot) * movedRot;

            Assert.That(Vector3.Distance(before, after), Is.LessThan(Tolerance),
                "The whole point of an anchor is that the shot keeps its place on the thing it rides.");
            Assert.That(Quaternion.Angle(beforeRot, afterRot), Is.LessThan(0.01f));
        }

        [Test]
        public void ADirectionTurnsWithTheAnchorButDoesNotTravel()
        {
            Vector3 velocity = new Vector3(0f, 0f, 5f);

            Vector3 moved = BasisCameraAnchorMath.TransportDirection(
                velocity, Quaternion.identity, Quaternion.Euler(0f, 90f, 0f));

            Assert.That(Vector3.Distance(moved, new Vector3(5f, 0f, 0f)), Is.LessThan(Tolerance),
                "Momentum is a direction: a turning boat turns it, and does not add its own position to it.");
        }

        [Test]
        public void AThousandStepsAroundACircleComeBackToTheStart()
        {
            // Error accumulates once per frame rather than once per move, so the question is not
            // whether one step is exact but whether an hour on a turntable walks the shot away.
            Vector3 camera = new Vector3(0f, 1f, 2f);
            Quaternion previous = Quaternion.identity;

            for (int Index = 1; Index <= 1000; Index++)
            {
                Quaternion current = Quaternion.Euler(0f, Index * 0.36f, 0f);
                camera = BasisCameraAnchorMath.TransportPoint(camera, Vector3.zero, previous, Vector3.zero, current);
                previous = current;
            }

            Assert.That(Vector3.Distance(camera, new Vector3(0f, 1f, 2f)), Is.LessThan(0.001f),
                "A full revolution in a thousand steps must land back where it started.");
        }

        [Test]
        public void JitterUnderAMicrometreIsNotAMove()
        {
            Assert.That(BasisCameraAnchorMath.HasMoved(
                Vector3.zero, Quaternion.identity, new Vector3(1e-7f, 0f, 0f), Quaternion.identity), Is.False,
                "An anchor standing still still reports solver noise; transporting on it folds that into the pose.");

            Assert.That(BasisCameraAnchorMath.HasMoved(
                Vector3.zero, Quaternion.identity, new Vector3(0.001f, 0f, 0f), Quaternion.identity), Is.True);
        }

        // ---- Heading, which the desktop fly rig keeps as a float --------------------------------

        [Test]
        public void HeadingFollowsTheAnchorsYaw()
        {
            float moved = BasisCameraAnchorMath.TransportHeading(
                30f, Quaternion.identity, Quaternion.Euler(0f, 90f, 0f));

            Assert.That(Mathf.DeltaAngle(moved, 120f), Is.LessThan(0.01f));
        }

        [Test]
        public void HeadingIgnoresPitchAndRoll()
        {
            float moved = BasisCameraAnchorMath.TransportHeading(
                30f, Quaternion.identity, Quaternion.Euler(45f, 0f, 20f));

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(moved, 30f)), Is.LessThan(0.5f),
                "A boat pitching over a wave has not swung the shot around the person on it.");
        }

        [Test]
        public void APureYawIsReadBackExactly()
        {
            foreach (float yaw in new[] { -170f, -90f, -12f, 0f, 37f, 90f, 179f })
            {
                float read = BasisCameraAnchorMath.YawDegrees(Quaternion.Euler(0f, yaw, 0f));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(read, yaw)), Is.LessThan(0.01f), $"yaw {yaw}");
            }
        }

        [Test]
        public void RollAtEveryPitchAddsNoYaw()
        {
            // The bug this pins: taking whichever of forward and up projects longer swaps to the up
            // axis as soon as roll makes it the longer one, which is nowhere near vertical. A
            // rolling deck then reported yaw it had not turned through.
            foreach (float pitch in new[] { 0f, 20f, 45f, 60f, 75f })
            {
                foreach (float roll in new[] { -40f, -20f, 0f, 20f, 40f })
                {
                    float read = BasisCameraAnchorMath.YawDegrees(Quaternion.Euler(pitch, 0f, roll));
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(read, 0f)), Is.LessThan(1f),
                        $"pitch {pitch} roll {roll} reported {read} degrees of yaw.");
                }
            }
        }

        [Test]
        public void YawDoesNotFlipWhenTheAnchorTipsPastVertical()
        {
            // A plain projection of forward reverses the moment its source passes vertical, which
            // a lift, a barrel roll or a ragdolled player all reach.
            float below = BasisCameraAnchorMath.YawDegrees(Quaternion.Euler(89f, 0f, 0f));
            float above = BasisCameraAnchorMath.YawDegrees(Quaternion.Euler(91f, 0f, 0f));

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(below, above)), Is.LessThan(5f),
                $"Yaw jumped from {below} to {above} across vertical.");
        }

        // ---- The solver's memory -----------------------------------------------------------------

        [Test]
        public void TheSolverStateRidesTheAnchorToo()
        {
            BasisCameraModifierState state = new BasisCameraModifierState();
            state.Seed(new Vector3(0f, 1f, 2f), Quaternion.identity, 40f);

            state.Transport(Vector3.zero, Quaternion.identity, new Vector3(10f, 0f, 0f), Quaternion.identity);

            Assert.That(Vector3.Distance(state.Position, new Vector3(10f, 1f, 2f)), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(state.PreviousPosition, new Vector3(10f, 1f, 2f)), Is.LessThan(Tolerance),
                "The collision sweep travels from here, so leaving it behind sweeps the whole anchor move.");
        }

        [Test]
        public void SubjectHistoryIsLeftWhereItIs()
        {
            // The subject is resolved fresh in world space every frame, and one standing on the
            // same moving thing has already travelled with it. Moving its history too counts the
            // move twice and lurches the shot sideways.
            BasisCameraModifierState state = new BasisCameraModifierState();
            state.Seed(Vector3.zero, Quaternion.identity, 40f);
            state.LastAnchor = new Vector3(1f, 0f, 0f);
            state.HasLastAnchor = true;
            state.SteadyAnchor = new Vector3(2f, 0f, 0f);
            state.HasSteadyAnchor = true;

            state.Transport(Vector3.zero, Quaternion.identity, new Vector3(50f, 0f, 0f), Quaternion.identity);

            Assert.That(state.LastAnchor, Is.EqualTo(new Vector3(1f, 0f, 0f)));
            Assert.That(state.SteadyAnchor, Is.EqualTo(new Vector3(2f, 0f, 0f)));
        }

        [Test]
        public void AnUnseededStateIsNotTransported()
        {
            BasisCameraModifierState state = new BasisCameraModifierState();

            state.Transport(Vector3.zero, Quaternion.identity, new Vector3(10f, 0f, 0f), Quaternion.identity);

            Assert.That(state.Position, Is.EqualTo(Vector3.zero),
                "Nothing has been solved yet, so there is no pose that belongs to the old frame.");
        }

        // ---- Picking and losing a target ---------------------------------------------------------

        [Test]
        public void AnchoringToAnObjectTakesTheAttachedSpace()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");

                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.Attached));
                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Object));
                Assert.That(_camera.AnchorLabel, Is.EqualTo("Boat"));
            }
            finally
            {
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void AnObjectAnchorResolvesToThatObjectsPose()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                boat.transform.SetPositionAndRotation(new Vector3(5f, 0f, 7f), Quaternion.Euler(0f, 45f, 0f));
                _camera.SetAnchorToObject(boat.transform, "Boat");

                Assert.That(_camera.TryResolveAnchorPose(out Vector3 position, out Quaternion rotation), Is.True);
                Assert.That(Vector3.Distance(position, new Vector3(5f, 0f, 7f)), Is.LessThan(Tolerance));
                Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(0f, 45f, 0f)), Is.LessThan(0.01f));
            }
            finally
            {
                if (boat != null) Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void ADestroyedAnchorResolvesToNothingRatherThanTheOrigin()
        {
            // The failure this rules out is the camera snapping to 0,0,0 when the thing it was
            // riding is unloaded — which is what a resolve that answered "true, identity" would do.
            GameObject boat = new GameObject("Boat");
            _camera.SetAnchorToObject(boat.transform, "Boat");
            Object.DestroyImmediate(boat);

            Assert.That(_camera.TryResolveAnchorPose(out _, out _), Is.False);
        }

        [Test]
        public void ResolvingAnAnchorDoesNotChangeIt()
        {
            // The gizmos resolve this every frame to draw the line back to the anchor. A read that
            // repaired state would make what the camera does depend on whether anyone was looking.
            GameObject boat = new GameObject("Boat");
            _camera.SetAnchorToObject(boat.transform, "Boat");
            Object.DestroyImmediate(boat);

            _camera.TryResolveAnchorPose(out _, out _);

            Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Object));
            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.Attached));
        }

        [Test]
        public void ClearingTheTargetLeavesTheCameraOnTheWorld()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");
                _camera.ClearAnchorTarget();

                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.None));
                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace),
                    "An attached anchor with nothing to attach to is a world anchor by another name.");
            }
            finally
            {
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void ClearingTheTargetLeavesAHandHeldCameraInTheHand()
        {
            _camera.PinSpace = CameraPinSpace.HandHeld;

            _camera.ClearAnchorTarget();

            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.HandHeld),
                "Dropping a target the camera was not using must not take it out of your hand.");
        }

        [Test]
        public void NetIdZeroIsARealPlayerToAnchorTo()
        {
            // Peer ids start at zero, so the binding cannot be carried by a reserved id value.
            _camera.SetAnchorToPlayer(0, true);

            Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Player));
            Assert.That(_camera.AnchorPlayerId, Is.EqualTo(0));
            Assert.That(_camera.AnchorPlayerIsRemote, Is.True);
        }

        [Test]
        public void SwitchingAnchorKindDropsTheOtherKindsBinding()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");
                _camera.SetAnchorToPlayer(4, true);

                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Player));
                Assert.That(_camera.AnchorLabel, Is.Empty);

                _camera.SetAnchorToObject(boat.transform, "Boat");

                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Object));
                Assert.That(_camera.AnchorPlayerIsRemote, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void AnchoringToANullTransformClearsRatherThanAttachingToNothing()
        {
            _camera.SetAnchorToObject(null, "Gone");

            Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.None));
            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.HandHeld));
        }

        [Test]
        public void OnlyTheAnchorsThatCanMoveReportThemselvesAsMoving()
        {
            _camera.PinSpace = CameraPinSpace.HandHeld;
            Assert.That(_camera.IsAnchorMoving, Is.False);

            _camera.PinSpace = CameraPinSpace.WorldSpace;
            Assert.That(_camera.IsAnchorMoving, Is.False,
                "The world does not move, which is what makes a world anchor a tripod.");

            _camera.SetAnchorSpace(CameraPinSpace.PlaySpace);
            Assert.That(_camera.IsAnchorMoving, Is.True);

            _camera.SetAnchorSpace(CameraPinSpace.Attached);
            Assert.That(_camera.IsAnchorMoving, Is.False,
                "Attached with nothing picked holds still, so it has nothing to be carried by.");

            _camera.SetAnchorToPlayer(0, false);
            Assert.That(_camera.IsAnchorMoving, Is.True);
        }

        [Test]
        public void SettingTheSameAnchorSpaceTwiceIsANoOp()
        {
            _camera.SetAnchorSpace(CameraPinSpace.WorldSpace);
            _camera.SetAnchorSpace(CameraPinSpace.WorldSpace);

            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace));
        }

        [Test]
        public void APickWithNoCaptureCameraFailsRatherThanThrowing()
        {
            // The panel's buttons are reachable before the prop has finished building itself.
            Assert.That(_camera.TryAnchorToSurfaceBelow(), Is.False);
            Assert.That(_camera.TryAnchorToViewTarget(), Is.False);
            Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.None));
        }

        // ---- Where the anchor sits in the rest of the camera --------------------------------------

        [Test]
        public void ArmingFlightKeepsAnAnchorTheUserChose()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");
                _camera.SetFlyModeEnabled(true);

                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.Attached),
                    "Flying a camera around a moving deck is the case this exists for.");
                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Object));
            }
            finally
            {
                _camera.SetFlyModeEnabled(false);
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void ArmingFlightFromTheHandStillTakesTheWorld()
        {
            _camera.SetFlyModeEnabled(true);
            try
            {
                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace));
            }
            finally
            {
                _camera.SetFlyModeEnabled(false);
            }
        }

        [Test]
        public void PlacingTheCameraByHandDropsWhateverItWasRiding()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");
                _camera.PlaceWorldPinned(new Vector3(1f, 2f, 3f), Quaternion.identity);

                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace));
                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.None),
                    "Placing the camera at an explicit world pose says to stop chasing anything.");
            }
            finally
            {
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void ASavedModeCannotRestoreAnAnchorWithNothingToRideOn()
        {
            // The target is a live reference to something in the world the mode was saved in, so it
            // is never written to disk. Coming back Attached to nothing would present an anchor the
            // camera has no way to actually be on.
            _camera.ApplyPlacementForTest((int)CameraPinSpace.Attached);

            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace));
        }

        [Test]
        public void ASavedModeKeepsAnAnchorThatIsStillLive()
        {
            GameObject boat = new GameObject("Boat");
            try
            {
                _camera.SetAnchorToObject(boat.transform, "Boat");
                _camera.ApplyPlacementForTest((int)CameraPinSpace.Attached);

                Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.Attached));
                Assert.That(_camera.AnchorKind, Is.EqualTo(CameraAnchorKind.Object));
            }
            finally
            {
                Object.DestroyImmediate(boat);
            }
        }

        [Test]
        public void AnAnchorThisBuildDoesNotHaveFallsBackToTheHand()
        {
            // A settings file written by a newer build names an anchor by number, and the cast that
            // reads it back cannot fail on its own.
            _camera.ApplyPlacementForTest(99);

            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.HandHeld));
        }

        [Test]
        public void EveryAnchorHasARowInTheDropdown()
        {
            Assert.That(
                Basis.BasisUI.HandHeldCamera.BasisHandHeldCameraPanelProvider.AnchorSpaceKeysForTest.Length,
                Is.EqualTo(System.Enum.GetValues(typeof(CameraPinSpace)).Length),
                "The dropdown resolves its selection as an index into that table, so a missing row " +
                "silently selects the wrong anchor.");
        }
    }
}
