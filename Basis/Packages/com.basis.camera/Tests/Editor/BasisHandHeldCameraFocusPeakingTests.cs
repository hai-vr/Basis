using NUnit.Framework;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Focus peaking is a viewfinder overlay, and the one thing it must never be is part of a shot.
    /// The design that guarantees that — a texture of its own, which only the viewfinder surfaces
    /// are pointed at — is what these pin, along with the two tables the panel indexes into.
    ///
    /// Awake never runs here: outside play mode Unity does not invoke it for a plain MonoBehaviour,
    /// so AddComponent yields a camera with field initializers applied and no scene dependencies.
    /// </summary>
    public class BasisHandHeldCameraFocusPeakingTests
    {
        private GameObject _go;
        private GameObject _captureGo;
        private BasisHandHeldCamera _camera;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("HandHeldCameraUnderTest");
            _camera = _go.AddComponent<BasisHandHeldCamera>();

            _captureGo = new GameObject("CaptureCamera");
            _camera.captureCamera = _captureGo.AddComponent<UnityCamera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_captureGo != null) Object.DestroyImmediate(_captureGo);
        }

        [Test]
        public void ThePaletteAndItsLabelsAreTheSameLength()
        {
            // The dropdown resolves a selection by its position in the key table and hands that
            // index straight to the palette, so a table that has drifted paints the wrong colour
            // or reads off the end of the array.
            Assert.That(BasisHandHeldCamera.FocusPeakingColourKeys.Length,
                Is.EqualTo(BasisHandHeldCamera.FocusPeakingColours.Length));
        }

        [Test]
        public void RedIsTheFirstColourAndTheOneAFreshCameraUses()
        {
            Assert.That(BasisHandHeldCamera.FocusPeakingColourKeys[0], Is.EqualTo("camera.focusPeaking.red"));

            Color red = BasisHandHeldCamera.FocusPeakingColours[0];
            Assert.That(red.r, Is.GreaterThan(0.8f));
            Assert.That(red.g, Is.LessThan(0.3f));
            Assert.That(red.b, Is.LessThan(0.3f));

            Assert.That(new CameraSettings().focusPeakingColour, Is.Zero);
            Assert.That(_camera.focusPeakingColour, Is.Zero);
        }

        [Test]
        public void AColourIndexFromAStaleFileCannotReadOffThePalette()
        {
            Assert.That(BasisHandHeldCamera.FocusPeakingColour(-4),
                Is.EqualTo(BasisHandHeldCamera.FocusPeakingColours[0]));
            Assert.That(BasisHandHeldCamera.FocusPeakingColour(9999),
                Is.EqualTo(BasisHandHeldCamera.FocusPeakingColours[BasisHandHeldCamera.FocusPeakingColours.Length - 1]));

            _camera.SetFocusPeakingColour(9999);
            Assert.That(_camera.focusPeakingColour,
                Is.EqualTo(BasisHandHeldCamera.FocusPeakingColours.Length - 1));
        }

        [Test]
        public void MoreSensitiveMeansALowerThreshold()
        {
            float least = BasisHandHeldCamera.FocusPeakingThreshold(0f);
            float middle = BasisHandHeldCamera.FocusPeakingThreshold(0.5f);
            float most = BasisHandHeldCamera.FocusPeakingThreshold(1f);

            Assert.That(least, Is.GreaterThan(middle));
            Assert.That(middle, Is.GreaterThan(most));
        }

        [Test]
        public void NeitherEndOfTheSliderIsAnOffSwitch()
        {
            // The toggle owns whether peaking runs. A threshold of zero at one end would paint the
            // whole frame and an unreachable one at the other would paint none of it, either of
            // which is a slider position that reads as broken.
            Assert.That(BasisHandHeldCamera.FocusPeakingThreshold(1f), Is.GreaterThan(0f));
            Assert.That(BasisHandHeldCamera.FocusPeakingThreshold(0f), Is.LessThan(1f));
        }

        [Test]
        public void TheSensitivityIsClampedToTheSliderItComesFrom()
        {
            _camera.SetFocusPeakingSensitivity(4f);
            Assert.That(_camera.focusPeakingSensitivity, Is.EqualTo(1f));

            _camera.SetFocusPeakingSensitivity(-4f);
            Assert.That(_camera.focusPeakingSensitivity, Is.Zero);
        }

        [Test]
        public void SwitchingItOnDoesNotByItselfPutTheViewfinderOnTheOverlay()
        {
            // The overlay only becomes the viewfinder's feed once a frame has been drawn into it.
            // Claiming it earlier would show one frame of black on the prop, the preview screen and
            // the desktop output at once.
            _camera.SetFocusPeakingEnabled(true);

            Assert.That(_camera.IsFocusPeaking, Is.False);
            Assert.That(_camera.ViewfinderTexture, Is.EqualTo(_camera.PreviewTexture));
        }

        [Test]
        public void TheViewfinderFallsBackToTheFeedWhenPeakingIsOff()
        {
            _camera.SetFocusPeakingEnabled(false);
            Assert.That(_camera.ViewfinderTexture, Is.EqualTo(_camera.PreviewTexture));
        }

        [Test]
        public void EverySettingOfItSurvivesApplyThenCapture()
        {
            using (var rig = new BasisCameraSettingsRig())
            {
                CameraSettings original = BasisCameraSettingsRig.DistinctiveSettings();
                rig.UI.ApplySettingsForTest(original);

                Assert.That(rig.Camera.focusPeakingEnabled, Is.EqualTo(original.focusPeaking));
                Assert.That(rig.Camera.focusPeakingSensitivity, Is.EqualTo(original.focusPeakingSensitivity).Within(1e-4f));
                Assert.That(rig.Camera.focusPeakingColour, Is.EqualTo(original.focusPeakingColour));
                Assert.That(rig.Camera.focusPeakingGreyPicture, Is.EqualTo(original.focusPeakingGreyPicture));

                CameraSettings captured = rig.UI.CreateCurrentCameraSettingsForTest();

                Assert.That(captured.focusPeaking, Is.EqualTo(original.focusPeaking));
                Assert.That(captured.focusPeakingSensitivity, Is.EqualTo(original.focusPeakingSensitivity).Within(1e-4f));
                Assert.That(captured.focusPeakingColour, Is.EqualTo(original.focusPeakingColour));
                Assert.That(captured.focusPeakingGreyPicture, Is.EqualTo(original.focusPeakingGreyPicture));
            }
        }

        [Test]
        public void AFreshCameraDoesNotStartPeaking()
        {
            // It is an aid rather than a look, so a camera nobody has asked for it on renders the
            // picture it always did — and pays nothing for the overlay.
            CameraSettings defaults = new CameraSettings();

            Assert.That(defaults.focusPeaking, Is.False);
            Assert.That(defaults.focusPeakingGreyPicture, Is.False);
            Assert.That(defaults.focusPeakingSensitivity,
                Is.EqualTo(BasisHandHeldCamera.DefaultFocusPeakingSensitivity).Within(1e-4f));
            Assert.That(defaults.focusPeakingSensitivity, Is.GreaterThan(0f),
                "A saved sensitivity of zero would come back as the least sensitive setting rather than a usable one.");
        }

        [Test]
        public void AModeNoticesEachPartOfTheOverlayChanging()
        {
            CameraSettings stored = BasisCameraSettingsRig.DistinctiveSettings();

            CameraSettings peakingOff = BasisCameraSettingsRig.DistinctiveSettings();
            peakingOff.focusPeaking = !stored.focusPeaking;
            Assert.That(BasisCameraUserMode.SettingsMatch(stored, peakingOff), Is.False);

            CameraSettings otherColour = BasisCameraSettingsRig.DistinctiveSettings();
            otherColour.focusPeakingColour = stored.focusPeakingColour + 1;
            Assert.That(BasisCameraUserMode.SettingsMatch(stored, otherColour), Is.False);

            CameraSettings otherSensitivity = BasisCameraSettingsRig.DistinctiveSettings();
            otherSensitivity.focusPeakingSensitivity = stored.focusPeakingSensitivity + 0.2f;
            Assert.That(BasisCameraUserMode.SettingsMatch(stored, otherSensitivity), Is.False);

            CameraSettings otherGrey = BasisCameraSettingsRig.DistinctiveSettings();
            otherGrey.focusPeakingGreyPicture = !stored.focusPeakingGreyPicture;
            Assert.That(BasisCameraUserMode.SettingsMatch(stored, otherGrey), Is.False);
        }
    }
}
