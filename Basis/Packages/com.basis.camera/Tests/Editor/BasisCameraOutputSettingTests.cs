using Basis.BasisUI.HandHeldCamera;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The Output tab: where the shot goes once it has been taken. These settings are unusual in
    /// that several of them are not per-camera at all — the audio listener is a single scene-wide
    /// resource that whichever camera asked for it last holds — so the interesting failures are
    /// about two cameras rather than one control.
    ///
    /// <para>
    /// The streaming values are clamped in their setters rather than by the widget, because the
    /// panel's field lets you type. A port outside the bindable range or a quality outside 1-100
    /// fails at the socket or the encoder, a long way from the control that caused it.
    /// </para>
    /// </summary>
    public class BasisCameraOutputSettingTests
    {
        private BasisCameraSettingsRig _rig;

        [SetUp]
        public void SetUp() => _rig = new BasisCameraSettingsRig();

        [TearDown]
        public void TearDown()
        {
            _rig?.Camera?.SetAudioListener(false);
            _rig?.Dispose();
        }

        // ---------- Streaming ----------

        [Test]
        public void StreamPort_IsHeldWhereASocketCanActuallyBind()
        {
            _rig.Camera.SetWebStreamPort(80);
            Assert.That(_rig.Camera.VideoOutputSettings.WebPort, Is.GreaterThanOrEqualTo(1024),
                "Ports below 1024 need privileges the game does not have.");

            _rig.Camera.SetWebStreamPort(99999);
            Assert.That(_rig.Camera.VideoOutputSettings.WebPort, Is.LessThanOrEqualTo(65535));

            _rig.Camera.SetWebStreamPort(9000);
            Assert.That(_rig.Camera.VideoOutputSettings.WebPort, Is.EqualTo(9000));
        }

        [Test]
        public void ThePanelsPortValidatorAgreesWithTheClampBehindIt()
        {
            // The field rejects what it says it rejects, and accepts what the setter would keep.
            // Disagreement here means either a value refused that would have worked, or one
            // accepted that is silently changed the moment it lands.
            _rig.Camera.SetWebStreamPort(BasisHandHeldCameraPanelProvider.WebPortMinForTest);
            Assert.That(_rig.Camera.VideoOutputSettings.WebPort,
                Is.EqualTo(BasisHandHeldCameraPanelProvider.WebPortMinForTest));

            _rig.Camera.SetWebStreamPort(BasisHandHeldCameraPanelProvider.WebPortMaxForTest);
            Assert.That(_rig.Camera.VideoOutputSettings.WebPort,
                Is.EqualTo(BasisHandHeldCameraPanelProvider.WebPortMaxForTest));
        }

        [Test]
        public void StreamQuality_IsHeldWhereAJpegEncoderAcceptsIt()
        {
            _rig.Camera.SetWebStreamQuality(0);
            Assert.That(_rig.Camera.VideoOutputSettings.WebQuality, Is.InRange(1, 100));

            _rig.Camera.SetWebStreamQuality(500);
            Assert.That(_rig.Camera.VideoOutputSettings.WebQuality, Is.InRange(1, 100));

            _rig.Camera.SetWebStreamQuality(60);
            Assert.That(_rig.Camera.VideoOutputSettings.WebQuality, Is.EqualTo(60));
        }

        [Test]
        public void StreamResolutionAndFrameRate_ReachTheStreamSettings()
        {
            _rig.Camera.SetVideoOutputResolution(2560, 1440);
            _rig.Camera.SetVideoOutputFrameRate(60f);

            Assert.That(_rig.Camera.VideoOutputSettings.Width, Is.EqualTo(2560));
            Assert.That(_rig.Camera.VideoOutputSettings.Height, Is.EqualTo(1440));
            Assert.That(_rig.Camera.VideoOutputSettings.FrameRate, Is.EqualTo(60f).Within(1e-3f));
        }

        [Test]
        public void EveryStreamResolutionThePanelOffersIsOneTheCameraAccepts()
        {
            // The dropdown reads the two tables at one index and hands the pair straight over.
            int[] widths = BasisHandHeldCameraPanelProvider.VideoResolutionWidthsForTest;
            int[] heights = BasisHandHeldCameraPanelProvider.VideoResolutionHeightsForTest;

            for (int Index = 0; Index < widths.Length; Index++)
            {
                _rig.Camera.SetVideoOutputResolution(widths[Index], heights[Index]);

                Assert.That(_rig.Camera.VideoOutputSettings.Width, Is.EqualTo(widths[Index]));
                Assert.That(_rig.Camera.VideoOutputSettings.Height, Is.EqualTo(heights[Index]));
            }
        }

        [Test]
        public void ThereIsAlwaysATransportToChoose()
        {
            // The web stream is pure sockets, so even a machine with no shared-texture backend has
            // one. An empty list would leave the dropdown blank and live output unreachable.
            var transports = BasisHandHeldCamera.AvailableVideoTransports();

            Assert.That(transports, Is.Not.Empty);
            for (int Index = 0; Index < transports.Count; Index++)
            {
                Assert.That(BasisHandHeldCamera.GetVideoTransportName(transports[Index]), Is.Not.Null.And.Not.Empty,
                    "A transport with no name renders as an empty dropdown row.");
                Assert.That(BasisHandHeldCamera.GetVideoTransportRequirement(transports[Index]), Is.Not.Null,
                    "The requirement text is what tells the user what to install on the receiving side.");
            }
        }

        [Test]
        public void ChangingTransportWhileIdle_LeavesTheStreamIdle()
        {
            var transports = BasisHandHeldCamera.AvailableVideoTransports();
            if (transports.Count < 2) Assert.Ignore("Only one transport is compiled in on this platform.");

            _rig.Camera.SetVideoTransport(transports[1]);

            Assert.That(_rig.Camera.VideoTransport, Is.EqualTo(transports[1]));
            Assert.That(_rig.Camera.IsAnyVideoOutputActive, Is.False,
                "Picking a transport is not the same as switching the stream on.");
        }

        // ---------- The single audio listener ----------

        [Test]
        public void HearingFromTheCamera_MovesBetweenCamerasRatherThanDoubling()
        {
            // There is one audio listener in the scene. Two cameras both claiming to be it would
            // mean the toggle reads true on a camera that is not actually hearing anything.
            using (BasisCameraSettingsRig second = new BasisCameraSettingsRig())
            {
                _rig.Camera.SetAudioListener(true);
                Assert.That(_rig.Camera.IsAudioListener, Is.True);
                Assert.That(second.Camera.IsAudioListener, Is.False);

                second.Camera.SetAudioListener(true);

                Assert.That(second.Camera.IsAudioListener, Is.True);
                Assert.That(_rig.Camera.IsAudioListener, Is.False,
                    "Taking the listener has to take it from whoever held it.");

                second.Camera.SetAudioListener(false);
                Assert.That(second.Camera.IsAudioListener, Is.False);
            }
        }

        [Test]
        public void ReleasingTheListenerFromACameraThatDoesNotHoldIt_DoesNotStealItFromTheOneThatDoes()
        {
            using (BasisCameraSettingsRig second = new BasisCameraSettingsRig())
            {
                _rig.Camera.SetAudioListener(true);

                second.Camera.SetAudioListener(false);

                Assert.That(_rig.Camera.IsAudioListener, Is.True);
            }
        }

        // ---------- Closing ----------

        [Test]
        public void CloseHidesInstead_IsWhatSeparatesADismissedCameraFromAHiddenOne()
        {
            // Hiding the camera from the panel keeps its settings up so you can carry on adjusting
            // it. Closing it with hide-instead is the state that offers Bring Back. Conflating the
            // two either strands the settings page or hides it while you are using it.
            _rig.Camera.SetCameraHidden(true);

            Assert.That(_rig.Camera.IsCameraHidden, Is.True);
            Assert.That(_rig.Camera.IsClosedHidden, Is.False,
                "Merely hiding the visuals must not put the panel into its Bring Back state.");

            _rig.Camera.CloseToHidden();

            Assert.That(_rig.Camera.IsClosedHidden, Is.True);
        }

        [Test]
        public void CloseHidesCamera_DefaultsOffSoCloseStillMeansClose()
        {
            Assert.That(_rig.UI.CloseHidesCamera, Is.False);
        }

        [Test]
        public void HidingTheCameraDoesNotStopItCapturing()
        {
            // The point of hiding rather than closing is that the camera keeps running — a stream
            // stays up and the preview keeps feeding the panel.
            _rig.Camera.ChangeResolution(1);
            _rig.Camera.SetCameraHidden(true);

            Assert.That(_rig.Camera.captureWidth, Is.EqualTo(1920));
            Assert.That(_rig.Camera.captureHeight, Is.EqualTo(1080));
            Assert.That(_rig.CaptureCamera, Is.Not.Null);
        }

        // ---------- Photo destination ----------

        [Test]
        public void SavedPhotosLandUnderTheCamerasOwnPhotosFolder()
        {
            string path = _rig.Camera.GetSavePath("shot.png");

            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(path, Does.EndWith("shot.png"));
            Assert.That(path.Length, Is.GreaterThan("shot.png".Length),
                "A bare filename means the shot lands in the working directory rather than the photos folder.");
        }
    }
}
