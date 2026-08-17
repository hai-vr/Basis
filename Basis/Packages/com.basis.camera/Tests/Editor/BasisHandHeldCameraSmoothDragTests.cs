using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The trail a held camera leaves behind the hand dragging it. Everything here is the pure step,
    /// so the lag, the settle and the leash can be asserted without a hand, a device mode or a frame
    /// — which is the only way any of it gets checked, since the behaviour only exists while
    /// somebody is holding the prop.
    /// </summary>
    public class BasisHandHeldCameraSmoothDragTests
    {
        private const float PositionDamping = 0.4f;
        private const float RotationDamping = 0.5f;
        private const float Leash = 0.25f;
        private const float Frame = 1f / 90f;

        private static void Step(ref Vector3 position, ref Quaternion rotation,
            Vector3 targetPosition, Quaternion targetRotation, float deltaTime = Frame,
            float leash = Leash, float positionDamping = PositionDamping, float rotationDamping = RotationDamping)
            => BasisHandHeldCameraInteractable.SolveSmoothDragForTest(
                ref position, ref rotation, targetPosition, targetRotation,
                positionDamping, rotationDamping, leash, deltaTime);

        [Test]
        public void TheCameraTrailsBehindAHandThatIsMovingAway()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Vector3 hand = Vector3.zero;

            for (int frame = 0; frame < 30; frame++)
            {
                hand += new Vector3(0f, 0f, 0.01f);
                Step(ref position, ref rotation, hand, Quaternion.identity);

                Assert.That(position.z, Is.LessThan(hand.z),
                    "the camera reached the hand it is supposed to be trailing");
            }

            Assert.That(position.z, Is.GreaterThan(0f), "the camera never set off after the hand at all");
        }

        [Test]
        public void ItSettlesOnAHandThatHasStopped()
        {
            Vector3 position = new Vector3(0f, 0f, -0.2f);
            Quaternion rotation = Quaternion.identity;
            Vector3 hand = Vector3.zero;
            Quaternion handRotation = Quaternion.Euler(0f, 40f, 0f);

            for (int frame = 0; frame < 360; frame++)
            {
                Step(ref position, ref rotation, hand, handRotation);
            }

            Assert.That(Vector3.Distance(position, hand), Is.LessThan(0.001f),
                "a camera let go of by a still hand has to arrive, not hover short of it");
            Assert.That(Quaternion.Angle(rotation, handRotation), Is.LessThan(0.1f));
        }

        /// <summary>
        /// Without the leash a fast drag opens an unbounded gap, and the camera swings through the
        /// player and the room on its way back. The clamp is what makes this a soft mount rather
        /// than a tether.
        /// </summary>
        [Test]
        public void TheLeashCapsHowFarBehindTheCameraCanEverFall()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Vector3 hand = Vector3.zero;

            for (int frame = 0; frame < 200; frame++)
            {
                hand += new Vector3(0.3f, 0f, 0f);
                Step(ref position, ref rotation, hand, Quaternion.identity);

                Assert.That(Vector3.Distance(position, hand), Is.LessThanOrEqualTo(Leash + 1e-4f),
                    $"the camera was {Vector3.Distance(position, hand):0.000}m behind the hand on frame {frame}");
            }
        }

        [Test]
        public void ATeleportingHandIsCaughtUpWithRatherThanChasedAcrossTheMap()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Vector3 hand = new Vector3(0f, 0f, 500f);

            Step(ref position, ref rotation, hand, Quaternion.identity);

            Assert.That(Vector3.Distance(position, hand), Is.LessThanOrEqualTo(Leash + 1e-4f),
                "one frame of a hand arriving somewhere else must not leave the camera a map away from it");
        }

        /// <summary>
        /// The damper is exponential, so the same damp time has to read the same at any framerate.
        /// The lerp this replaced did not, and a 144Hz headset trailed less than a 60Hz one from
        /// the same slider.
        /// </summary>
        [Test]
        public void TheTrailIsTheSameAtAnyFramerate()
        {
            Vector3 hand = new Vector3(0f, 0f, 1f);

            Vector3 slowPosition = Vector3.zero;
            Quaternion slowRotation = Quaternion.identity;
            Step(ref slowPosition, ref slowRotation, hand, Quaternion.identity, 0.1f, leash: 10f);

            Vector3 fastPosition = Vector3.zero;
            Quaternion fastRotation = Quaternion.identity;
            for (int frame = 0; frame < 20; frame++)
            {
                Step(ref fastPosition, ref fastRotation, hand, Quaternion.identity, 0.005f, leash: 10f);
            }

            Assert.That(fastPosition.z, Is.EqualTo(slowPosition.z).Within(1e-3f),
                "one long frame and twenty short ones covering the same time landed somewhere different");
        }

        [Test]
        public void AZeroDampTimeIsAnInstantSnap()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Vector3 hand = new Vector3(0.1f, 0.05f, 0.2f);
            Quaternion handRotation = Quaternion.Euler(10f, 25f, 5f);

            Step(ref position, ref rotation, hand, handRotation, positionDamping: 0f, rotationDamping: 0f);

            Assert.That(Vector3.Distance(position, hand), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(rotation, handRotation), Is.LessThan(1e-3f));
        }

        [Test]
        public void AStoppedClockMovesNothing()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;

            Step(ref position, ref rotation, new Vector3(0f, 0f, 0.1f), Quaternion.Euler(0f, 90f, 0f), 0f);

            Assert.That(position, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(rotation, Quaternion.identity), Is.LessThan(1e-4f));
        }

        /// <summary>
        /// The leash is a limit on where the camera may be, not a rate — so it holds on a frame the
        /// damper does nothing on. That is what pulls the camera in the moment the slider is
        /// shortened under a hold, rather than leaving it parked outside its own limit until the
        /// hand next moves; it is the same clause that catches a hand which teleports.
        /// </summary>
        [Test]
        public void ShorteningTheLeashPullsTheCameraInOnTheSpot()
        {
            Vector3 position = new Vector3(0f, 0f, -0.5f);
            Quaternion rotation = Quaternion.identity;

            Step(ref position, ref rotation, Vector3.zero, Quaternion.identity, 0f, leash: 0.1f);

            Assert.That(position.magnitude, Is.EqualTo(0.1f).Within(1e-4f));
        }

        [Test]
        public void RotationTrailsTooRatherThanSnappingWhileThePositionLags()
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Quaternion handRotation = Quaternion.Euler(0f, 90f, 0f);

            Step(ref position, ref rotation, Vector3.zero, handRotation);

            float turned = Quaternion.Angle(Quaternion.identity, rotation);
            Assert.That(turned, Is.GreaterThan(0f), "the camera did not begin to follow the hand's aim");
            Assert.That(turned, Is.LessThan(90f), "the camera matched the hand's aim in a single frame");
        }
    }

    /// <summary>
    /// A settings file is text on disk and can name any number at all, so the numbers reach the
    /// camera through setters that clamp them back into the range the panel promises.
    /// </summary>
    public class BasisHandHeldCameraSmoothDragRangeTests
    {
        private BasisCameraSettingsRig _rig;

        [SetUp]
        public void SetUp() => _rig = new BasisCameraSettingsRig();

        [TearDown]
        public void TearDown() => _rig?.Dispose();

        [Test]
        public void TheDampTimesAreClampedIntoTheRangeTheSlidersOffer()
        {
            _rig.Camera.SetSmoothDragPositionDamping(-40f);
            Assert.That(_rig.Camera.smoothDragPositionDamping,
                Is.EqualTo(BasisHandHeldCameraInteractable.MinSmoothDragDamping).Within(1e-5f));

            _rig.Camera.SetSmoothDragRotationDamping(9000f);
            Assert.That(_rig.Camera.smoothDragRotationDamping,
                Is.EqualTo(BasisHandHeldCameraInteractable.MaxSmoothDragDamping).Within(1e-5f));
        }

        [Test]
        public void TheLeashIsClampedAndNeverReachesZero()
        {
            _rig.Camera.SetSmoothDragMaxDistance(0f);

            Assert.That(_rig.Camera.smoothDragMaxDistance,
                Is.EqualTo(BasisHandHeldCameraInteractable.MinSmoothDragDistance).Within(1e-5f));
            Assert.That(_rig.Camera.smoothDragMaxDistance, Is.GreaterThan(0f),
                "a zero leash pins the camera to the hand and makes the whole feature a no-op");
        }

        [Test]
        public void TheDragIsOffOnAFreshCamera()
        {
            Assert.That(_rig.Camera.useSmoothDrag, Is.False,
                "a camera that trails out of the box changes how every existing shot is held");
        }

        [Test]
        public void ASavedFileBringsTheDragBack()
        {
            BasisHandHeldCameraUI.CameraSettings settings = BasisCameraSettingsRig.DistinctiveSettings();

            _rig.UI.ApplySettingsForTest(settings);

            Assert.That(_rig.Camera.useSmoothDrag, Is.EqualTo(settings.useSmoothDrag));
            Assert.That(_rig.Camera.smoothDragPositionDamping,
                Is.EqualTo(settings.smoothDragPositionDamping).Within(1e-4f));
            Assert.That(_rig.Camera.smoothDragRotationDamping,
                Is.EqualTo(settings.smoothDragRotationDamping).Within(1e-4f));
            Assert.That(_rig.Camera.smoothDragMaxDistance,
                Is.EqualTo(settings.smoothDragMaxDistance).Within(1e-4f));
        }

        /// <summary>
        /// An older file has none of these fields, and JsonUtility runs the constructor before it
        /// fills what the file does carry — so the numbers have to be defaulted there rather than
        /// migrated, and a v9 file must not load with a zero damp time and a zero leash.
        /// </summary>
        [Test]
        public void AFileWrittenBeforeTheFeatureLoadsWithUsableNumbers()
        {
            BasisHandHeldCameraUI.CameraSettings loaded =
                JsonUtility.FromJson<BasisHandHeldCameraUI.CameraSettings>("{\"settingsVersion\":9,\"fov\":55.0}");

            Assert.That(loaded.fov, Is.EqualTo(55f).Within(1e-4f),
                "the file this is standing in for has to actually have been read");

            Assert.That(loaded.useSmoothDrag, Is.False, "an older file has to load as the rigid hold it was written as");
            Assert.That(loaded.smoothDragPositionDamping,
                Is.InRange(BasisHandHeldCameraInteractable.MinSmoothDragDamping,
                    BasisHandHeldCameraInteractable.MaxSmoothDragDamping));
            Assert.That(loaded.smoothDragRotationDamping,
                Is.InRange(BasisHandHeldCameraInteractable.MinSmoothDragDamping,
                    BasisHandHeldCameraInteractable.MaxSmoothDragDamping));
            Assert.That(loaded.smoothDragMaxDistance,
                Is.InRange(BasisHandHeldCameraInteractable.MinSmoothDragDistance,
                    BasisHandHeldCameraInteractable.MaxSmoothDragDistance));
        }
    }
}
