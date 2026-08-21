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

        // ---------- Stream pacing ----------
        //
        // What decides when a live frame goes out. Everything here fails the same quiet way: the
        // stream still runs, the average frame rate still reads correctly, and the picture arrives
        // unevenly — which no assertion about settings or sockets would ever catch.

        /// <summary>Runs the pacer for a number of ticks and counts what it published.</summary>
        private static int CountPublished(ref BasisStreamFramePacer pacer, int ticks, float deltaTime, float frameRate)
        {
            int published = 0;
            for (int Index = 0; Index < ticks; Index++)
            {
                if (pacer.AllowThisFrame(deltaTime, frameRate, true, true)) published++;
            }
            return published;
        }

        [Test]
        public void StreamPacing_PublishesEveryFrameASourceAtTheStreamRateDraws()
        {
            // The bug this replaced: the capture camera was floored at the stream rate and the
            // stream paced itself at the same rate off a second accumulator. Two clocks running at
            // one rate, sampled once a frame, drift in and out of phase — so roughly every other
            // fresh render was dropped and a 30fps stream ran at 15 with no setting to explain it.
            BasisStreamFramePacer pacer = default;

            int published = CountPublished(ref pacer, 60, 1f / 30f, 30f);

            Assert.That(published, Is.EqualTo(60),
                "A source drawing at exactly the stream rate has no frames to spare; every one of them should go out.");
        }

        [Test]
        public void StreamPacing_SurvivesAJitteryFrameTime()
        {
            // Real frame times are never the nominal interval, and a source a hair early must not
            // be held back for a whole one — that is the same halving by another route.
            BasisStreamFramePacer pacer = default;
            float[] deltas = { 0.0306f, 0.0361f, 0.0328f, 0.0341f, 0.0315f };
            int published = 0;

            for (int Index = 0; Index < 60; Index++)
            {
                if (pacer.AllowThisFrame(deltas[Index % deltas.Length], 30f, true, true)) published++;
            }

            Assert.That(published, Is.GreaterThanOrEqualTo(57),
                "Jitter around the interval should cost the odd frame at most, not one in two.");
        }

        [Test]
        public void StreamPacing_HoldsAFasterSourceToTheStreamRate()
        {
            // The floor under the capture camera is a floor, not a cap: an uncapped render limit or
            // the desktop override both leave it drawing far faster than the stream rate.
            BasisStreamFramePacer pacer = default;

            int published = CountPublished(ref pacer, 120, 1f / 120f, 30f);

            Assert.That(published, Is.EqualTo(30),
                "A 120fps source on a 30fps stream should publish exactly one frame in four.");
        }

        [Test]
        public void StreamPacing_DoesNotChargeForASlotTheSinkCouldNotTake()
        {
            // The sink takes one frame at a time through readback and encode. A tick that arrives
            // while it is busy used to spend the slot anyway, so the stream then waited out a whole
            // further interval — at 30fps that is a dropped frame for every collision.
            BasisStreamFramePacer pacer = default;
            const float Delta = 1f / 90f;

            // Three ticks of a 90fps source fill one 30fps interval.
            Assert.That(pacer.AllowThisFrame(Delta, 30f, true, true), Is.False);
            Assert.That(pacer.AllowThisFrame(Delta, 30f, true, true), Is.False);
            Assert.That(pacer.AllowThisFrame(Delta, 30f, true, false), Is.False,
                "The sink is busy, so nothing is published.");

            Assert.That(pacer.AllowThisFrame(Delta, 30f, true, true), Is.True,
                "The moment the sink is free the banked interval is still there, so the newest frame goes out now rather than a whole interval later.");
        }

        [Test]
        public void StreamPacing_NeverPublishesARenderTwice()
        {
            // Encoding and sending a picture the camera has not redrawn costs a readback, a JPEG
            // and a frame's bandwidth to tell the viewer nothing.
            BasisStreamFramePacer pacer = default;

            for (int Index = 0; Index < 60; Index++)
            {
                Assert.That(pacer.AllowThisFrame(1f / 60f, 30f, false, true), Is.False);
            }
        }

        [Test]
        public void StreamPacing_DoesNotPayAStallBackAsABurst()
        {
            // A hitch, a viewer reconnecting, a camera nobody was watching: without a cap on the
            // banked time, whatever paused the stream is repaid as a run of back-to-back frames the
            // moment it resumes, which is the opposite of what a live viewer wants.
            BasisStreamFramePacer pacer = default;
            const float Delta = 1f / 120f;

            for (int Index = 0; Index < 120; Index++) pacer.AllowThisFrame(Delta, 30f, false, true);

            int published = CountPublished(ref pacer, 8, Delta, 30f);

            Assert.That(published, Is.LessThanOrEqualTo(3),
                "A second of stalled stream should be worth a frame or two of catch-up, not thirty.");
        }

        [Test]
        public void StreamPacing_WithNoRateSetPublishesEveryFreshFrame()
        {
            // 0 means "as fast as the source draws", the same as everywhere else these rates are read.
            BasisStreamFramePacer pacer = default;

            int published = CountPublished(ref pacer, 20, 1f / 144f, 0f);

            Assert.That(published, Is.EqualTo(20));
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
