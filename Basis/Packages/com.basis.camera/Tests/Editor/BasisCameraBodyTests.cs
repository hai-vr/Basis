using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The camera body: the film in it, the wheel between shots, and the date it burns into the
    /// corner of the picture.
    ///
    /// <para>All of it is reachable without a scene, which is the point of where it lives — the
    /// counter and the lockouts are plain state on the camera, and the stamp is geometry rather
    /// than pixels, so what a disposable actually does can be pinned without a render.</para>
    ///
    /// <para>Awake never runs outside play mode, so these cameras arrive with field initializers
    /// and no capture camera and no volume profile. Every method under test null-guards its scene
    /// state for that reason, and the body is deliberately the one half of a camera kind that
    /// survives having neither.</para>
    /// </summary>
    public class BasisCameraBodyTests
    {
        private GameObject _go;
        private BasisHandHeldCamera _camera;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BodyCameraUnderTest");
            _camera = _go.AddComponent<BasisHandHeldCamera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        // ---- The table ----------------------------------------------------------------------

        [Test]
        public void AFreshCameraIsDigitalAndUnconstrained()
        {
            Assert.That(_camera.Body, Is.EqualTo(BasisCameraBodyKind.Digital));
            Assert.That(_camera.BodyTraits.Constrains, Is.False,
                "The camera as it has always been must not have gained a way to refuse a photograph.");
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Ready));
        }

        [Test]
        public void AnUnknownBodyOffDiskReadsAsDigital()
        {
            // A settings file is text, and a build that has never heard of body 97 must not strand
            // its owner holding a camera with no traits to look up.
            Assert.That(BasisCameraBodies.Sanitize(97), Is.EqualTo(BasisCameraBodyKind.Digital));
            Assert.That(BasisCameraBodies.Sanitize(-3), Is.EqualTo(BasisCameraBodyKind.Digital));
            Assert.That(BasisCameraBodies.Get((BasisCameraBodyKind)97), Is.Not.Null);
        }

        [Test]
        public void EveryModeThatIsAKindHandsOutItsOwnBody()
        {
            // Two kinds sharing a body would make them indistinguishable on a camera with no volume
            // profile, which is exactly the fixture the mode round-trip test runs on.
            var seen = new HashSet<BasisCameraBodyKind>();

            foreach (BasisCameraMode mode in new[]
            {
                BasisCameraMode.Disposable,
                BasisCameraMode.Instant,
                BasisCameraMode.Camcorder,
                BasisCameraMode.Security,
            })
            {
                _camera.ApplyCameraMode(mode);

                Assert.That(_camera.Body, Is.Not.EqualTo(BasisCameraBodyKind.Digital),
                    $"{mode} is a camera kind, so it has to hand out a body of its own.");
                Assert.That(seen.Add(_camera.Body), Is.True,
                    $"{mode} shares a body with a kind before it.");
            }
        }

        [Test]
        public void EveryModeThatIsAJobLeavesTheBodyDigital()
        {
            foreach (BasisCameraMode mode in new[]
            {
                BasisCameraMode.Photo,
                BasisCameraMode.FlyingPuck,
                BasisCameraMode.FollowMe,
                BasisCameraMode.Cinematic,
            })
            {
                _camera.ApplyCameraMode(BasisCameraMode.Disposable);
                _camera.ApplyCameraMode(mode);

                Assert.That(_camera.Body, Is.EqualTo(BasisCameraBodyKind.Digital),
                    $"{mode} is about where the camera goes, so it must hand back a plain camera.");
            }
        }

        // ---- Film ---------------------------------------------------------------------------

        [Test]
        public void PickingDisposable_HandsOutAFullRoll()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);

            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(_camera.BodyTraits.Exposures));
            Assert.That(_camera.BodyOutOfFilm, Is.False);
        }

        [Test]
        public void PickingTheSameKindAgain_IsANewCamera()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            _camera.TryTakeFrameForTest();

            _camera.ApplyCameraMode(BasisCameraMode.Disposable);

            // Choosing a disposable from the picker is being handed one, not picking up the one you
            // were already holding — otherwise an empty camera could never be replaced, only reloaded.
            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(_camera.BodyTraits.Exposures));
        }

        [Test]
        public void TakingAFrame_SpendsItAndLocksTheShutter()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            int before = _camera.ExposuresRemaining;

            Assert.That(_camera.TryTakeFrameForTest(), Is.True);

            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(before - 1));
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.WindingOn));
            Assert.That(_camera.TryTakeFrameForTest(), Is.False,
                "A disposable that could be fired twice in a frame is not a disposable.");
        }

        [Test]
        public void WindingOn_ReleasesTheShutterAgain()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            _camera.TryTakeFrameForTest();

            _camera.AdvanceBodyForTest(_camera.BodyTraits.WindOnSeconds + 0.01f);

            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Ready));
            Assert.That(_camera.TryTakeFrameForTest(), Is.True);
        }

        [Test]
        public void ARollRunsOut_AndOnlyAReloadBringsItBack()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            int roll = _camera.BodyTraits.Exposures;

            for (int Frame = 0; Frame < roll; Frame++)
            {
                Assert.That(_camera.TryTakeFrameForTest(), Is.True, $"Frame {Frame + 1} of {roll} was refused.");
                _camera.AdvanceBodyForTest(10f);
            }

            Assert.That(_camera.BodyOutOfFilm, Is.True);
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.OutOfFilm));
            Assert.That(_camera.TryTakeFrameForTest(), Is.False);

            // Out of film is the one refusal waiting does not lift.
            _camera.AdvanceBodyForTest(600f);
            Assert.That(_camera.TryTakeFrameForTest(), Is.False);

            _camera.ReloadFilm();
            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(roll));
            Assert.That(_camera.TryTakeFrameForTest(), Is.True);
        }

        [Test]
        public void InstantFilm_HoldsTheShutterWhileAPrintComesUp()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Instant);
            BasisCameraBodyTraits body = _camera.BodyTraits;

            Assert.That(body.DevelopSeconds, Is.GreaterThan(body.WindOnSeconds),
                "The develop wait is the whole character of instant film, so it has to outlast the eject.");

            _camera.TryTakeFrameForTest();
            _camera.AdvanceBodyForTest(body.WindOnSeconds + 0.01f);

            // Wound on but not yet developed: the refusal has to name the one still in the way.
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Developing));

            _camera.AdvanceBodyForTest(body.DevelopSeconds);
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Ready));
        }

        [Test]
        public void TapeBodies_NeverRunOutAndNeverWait()
        {
            foreach (BasisCameraMode mode in new[] { BasisCameraMode.Camcorder, BasisCameraMode.Security })
            {
                _camera.ApplyCameraMode(mode);

                for (int Frame = 0; Frame < 5; Frame++)
                {
                    Assert.That(_camera.TryTakeFrameForTest(), Is.True, $"{mode} refused frame {Frame + 1}.");
                }

                Assert.That(_camera.BodyOutOfFilm, Is.False, $"{mode} has nothing to run out of.");
                Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Ready));
            }
        }

        [Test]
        public void ReloadingACameraWithNoFilm_DoesNothing()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Camcorder);
            _camera.ReloadFilm();

            Assert.That(_camera.ExposuresRemaining, Is.Zero,
                "A tape body has no load, so the counter must stay the zero that means 'not counting'.");
        }

        // ---- The flash ----------------------------------------------------------------------

        [Test]
        public void TheFlashArmsItselfOnABodyThatHasOne()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            Assert.That(_camera.FlashEnabled, Is.True);
            Assert.That(_camera.FlashReady, Is.True);

            _camera.ApplyCameraMode(BasisCameraMode.Camcorder);
            Assert.That(_camera.FlashEnabled, Is.False, "A camcorder has nothing on the front to arm.");
            Assert.That(_camera.FlashReady, Is.False);

            _camera.SetFlashEnabled(true);
            Assert.That(_camera.FlashEnabled, Is.False,
                "Switching on a flash that is not fitted must not leave the panel claiming one.");
        }

        [Test]
        public void FiringTheFlash_PutsItOnChargeUntilItRecycles()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            _camera.TryTakeFrameForTest();

            Assert.That(_camera.FlashReady, Is.False, "A flash that recharged instantly would not be one.");

            // Long enough to wind on, but not to recycle: the two clocks are separate on purpose,
            // and a disposable spends most of a roll wound on with the flash still whining.
            _camera.AdvanceBodyForTest(_camera.BodyTraits.WindOnSeconds + 0.01f);
            Assert.That(_camera.EvaluateShutter(), Is.EqualTo(BasisCameraShutterState.Ready));
            Assert.That(_camera.FlashReady, Is.False);

            _camera.AdvanceBodyForTest(_camera.BodyTraits.FlashRecycleSeconds);
            Assert.That(_camera.FlashReady, Is.True);
        }

        [Test]
        public void AFlashThatIsSwitchedOff_NeverGoesOnCharge()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            _camera.SetFlashEnabled(false);

            _camera.TryTakeFrameForTest();

            Assert.That(_camera.FlashRecycleRemaining, Is.Zero,
                "A flash nobody fired has nothing to recover from.");
        }

        // ---- Loading a saved camera ---------------------------------------------------------

        [Test]
        public void RestoringABody_KeepsWhatWasLeftOnTheLoad()
        {
            _camera.RestoreBodyForTest((int)BasisCameraBodyKind.Disposable, 5, true);

            Assert.That(_camera.Body, Is.EqualTo(BasisCameraBodyKind.Disposable));
            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(5),
                "A disposable that refilled itself on every load would never run out at all.");
        }

        [Test]
        public void RestoringAFileThatPredatesBodies_LoadsAFullRoll()
        {
            // JsonUtility fills an absent field from the constructor, which writes FullRoll — so an
            // older file has to arrive as a fresh camera rather than one that will not fire.
            _camera.RestoreBodyForTest((int)BasisCameraBodyKind.Disposable, BasisHandHeldCamera.FullRoll, true);

            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(_camera.BodyTraits.Exposures));
        }

        [Test]
        public void RestoringACountBiggerThanTheRoll_IsClampedToIt()
        {
            _camera.RestoreBodyForTest((int)BasisCameraBodyKind.Instant, 900, true);

            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(_camera.BodyTraits.Exposures));
        }

        [Test]
        public void RestoringAnArmedFlashOntoABodyWithoutOne_LeavesItOff()
        {
            _camera.RestoreBodyForTest((int)BasisCameraBodyKind.Security, BasisHandHeldCamera.FullRoll, true);

            Assert.That(_camera.FlashEnabled, Is.False);
        }

        [Test]
        public void ABodyOutlivesTheModeItCameFrom()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Disposable);
            _camera.TryTakeFrameForTest();

            // Touch something the mode drives. The label goes; the camera in your hand does not.
            _camera.useAutoLeveling = !_camera.useAutoLeveling;
            _camera.RefreshCameraMode();

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom));
            Assert.That(_camera.Body, Is.EqualTo(BasisCameraBodyKind.Disposable));
            Assert.That(_camera.ExposuresRemaining, Is.EqualTo(_camera.BodyTraits.Exposures - 1));
        }

        [Test]
        public void SavingAModeOffAPartlyUsedCamera_KeepsTheBodyAndForgetsTheCount()
        {
            using (var rig = new BasisCameraSettingsRig())
            {
                rig.Camera.ApplyCameraMode(BasisCameraMode.Disposable);
                rig.Camera.TryTakeFrameForTest();

                BasisCameraUserMode saved = rig.Camera.CaptureUserMode("half a roll", Color.white);

                Assert.That(saved.settings.cameraBody, Is.EqualTo((int)BasisCameraBodyKind.Disposable),
                    "A mode saved off a disposable is a disposable.");
                Assert.That(saved.settings.exposuresRemaining, Is.EqualTo(BasisHandHeldCamera.FullRoll),
                    "A mode is a configuration; one that handed back the frames its owner happened " +
                    "to have left would be a snapshot of an afternoon instead.");
            }
        }

        [Test]
        public void ACameraWithEveryOverrideFitted_StillMatchesTheKindItWasGiven()
        {
            // The bare fixture above skips every optical compare for want of a volume profile, so
            // this is the only place the look half of a kind is actually exercised: applied through
            // the UI's own setters and then read back off the live overrides.
            using (var rig = new BasisCameraSettingsRig())
            {
                foreach (BasisCameraMode mode in new[]
                {
                    BasisCameraMode.Disposable,
                    BasisCameraMode.Instant,
                    BasisCameraMode.Camcorder,
                    BasisCameraMode.Security,
                })
                {
                    rig.Camera.ApplyCameraMode(mode);

                    Assert.That(rig.Camera.MatchesCameraMode(mode), Is.True,
                        $"Applying {mode} on a camera that has the effects fitted left it not matching {mode}.");
                    Assert.That(rig.Camera.RefreshCameraMode(), Is.False,
                        $"A freshly applied {mode} re-derived to something else.");
                }
            }
        }

        [Test]
        public void EditingTheGrainOfAKind_DropsToCustomButKeepsTheCamera()
        {
            using (var rig = new BasisCameraSettingsRig())
            {
                rig.Camera.ApplyCameraMode(BasisCameraMode.Disposable);

                rig.UI.ChangeFilmGrain(0f);

                Assert.That(rig.Camera.RefreshCameraMode(), Is.True, "The drift should have been noticed.");
                Assert.That(rig.Camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom));
                Assert.That(rig.Camera.Body, Is.EqualTo(BasisCameraBodyKind.Disposable),
                    "Turning the grain off does not turn a disposable into a digital camera.");
            }
        }

        // ---- The stamp ----------------------------------------------------------------------

        [Test]
        public void OnlyABodyThatStampsComposesText()
        {
            DateTime when = new DateTime(2026, 8, 21, 14, 32, 9);

            Assert.That(BasisCameraStampPainter.Compose(BasisCameraStamp.None, when), Is.Null);
            Assert.That(BasisCameraStampPainter.Compose(BasisCameraStamp.Date, when), Is.EqualTo("'26 08 21"));
            Assert.That(BasisCameraStampPainter.Compose(BasisCameraStamp.Timecode, when),
                Does.Contain("14:32:09"));
        }

        [Test]
        public void EveryGlyphLandsInsideThePicture()
        {
            var rects = new List<RectInt>();
            const int width = 1296;
            const int height = 864;

            Assert.That(BasisCameraStampPainter.BuildGlyphs("'26 08 21", width, height, rects), Is.True);
            Assert.That(rects, Is.Not.Empty);

            foreach (RectInt rect in rects)
            {
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(height));
                Assert.That(rect.width, Is.GreaterThan(0));
                Assert.That(rect.height, Is.GreaterThan(0));
            }
        }

        [Test]
        public void TheStampSitsInTheBottomRightCorner()
        {
            var rects = new List<RectInt>();
            const int width = 1296;
            const int height = 864;
            BasisCameraStampPainter.BuildGlyphs("'26 08 21", width, height, rects);

            int right = 0;
            int top = 0;
            foreach (RectInt rect in rects)
            {
                right = Mathf.Max(right, rect.xMax);
                top = Mathf.Max(top, rect.yMax);
            }

            // Bottom-left origin, matching raw texture rows — so "low y" is the bottom of the shot.
            Assert.That(right, Is.GreaterThan(width / 2), "The stamp belongs on the right.");
            Assert.That(top, Is.LessThan(height / 4), "The stamp belongs at the bottom.");
        }

        [Test]
        public void APictureTooSmallToStampIsLeftAlone()
        {
            var rects = new List<RectInt>();

            // A stamp nobody can read is worse than none: it is a row of orange smudges over
            // somebody's photograph.
            Assert.That(BasisCameraStampPainter.BuildGlyphs("'26 08 21", 64, 48, rects), Is.False);
            Assert.That(rects, Is.Empty);
        }

        [Test]
        public void NothingToStampProducesNothing()
        {
            var rects = new List<RectInt>();

            Assert.That(BasisCameraStampPainter.BuildGlyphs(null, 1280, 720, rects), Is.False);
            Assert.That(BasisCameraStampPainter.BuildGlyphs("", 1280, 720, rects), Is.False);
            Assert.That(rects, Is.Empty);
        }

        [Test]
        public void ASpaceDrawsNothingButStillTakesItsPlace()
        {
            var one = new List<RectInt>();
            var spaced = new List<RectInt>();

            BasisCameraStampPainter.BuildGlyphs("11", 1280, 720, one);
            BasisCameraStampPainter.BuildGlyphs("1 1", 1280, 720, spaced);

            Assert.That(spaced.Count, Is.EqualTo(one.Count), "A space is not a glyph.");

            // Same glyph count, but pushed left by the cell the space took, so the stamp still ends
            // at the same margin.
            int oneLeft = int.MaxValue;
            int spacedLeft = int.MaxValue;
            foreach (RectInt rect in one) oneLeft = Mathf.Min(oneLeft, rect.xMin);
            foreach (RectInt rect in spaced) spacedLeft = Mathf.Min(spacedLeft, rect.xMin);

            Assert.That(spacedLeft, Is.LessThan(oneLeft));
        }

        [Test]
        public void AOneIsTwoSegmentsAndAnEightIsSeven()
        {
            var one = new List<RectInt>();
            var eight = new List<RectInt>();

            BasisCameraStampPainter.BuildGlyphs("1", 1280, 720, one);
            BasisCameraStampPainter.BuildGlyphs("8", 1280, 720, eight);

            Assert.That(one.Count, Is.EqualTo(2));
            Assert.That(eight.Count, Is.EqualTo(7));
        }

        [Test]
        public void ALongStampOnANarrowFrameIsShrunkRatherThanCropped()
        {
            var rects = new List<RectInt>();
            const int width = 640;

            // The tape and security bodies shoot 640 wide, and a clock that ran off the edge would
            // lose its seconds first — which is the half anyone reading a timecode wants.
            string timecode = BasisCameraStampPainter.Compose(
                BasisCameraStamp.Timecode, new DateTime(2026, 8, 21, 14, 32, 9));

            Assert.That(BasisCameraStampPainter.BuildGlyphs(timecode, width, 480, rects), Is.True);

            foreach (RectInt rect in rects)
            {
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
            }
        }
    }
}
