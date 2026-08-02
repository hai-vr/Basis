using NUnit.Framework;
using UnityEngine;
using CameraPinSpace = BasisHandHeldCameraInteractable.CameraPinSpace;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Pins camera modes: that applying one leaves the camera in it, that editing a setting the
    /// mode chose drops to Custom while editing anything else does not, and that swapping between
    /// two modes that both claim world space hands over cleanly.
    ///
    /// Awake never runs outside play mode, so these cameras have field initializers and no scene:
    /// no capture camera and no volume profile. That confines the assertions to the behaviour half
    /// of each preset — which is the half with a state machine in it, and so the half that can
    /// actually break. The lens and post-processing halves are skipped by the same null guards in
    /// both the apply and the match, which is why a profile-less camera still round-trips.
    /// </summary>
    public class BasisCameraModePresetTests
    {
        private GameObject _go;
        private BasisHandHeldCamera _camera;

        private static readonly BasisCameraMode[] Presets =
        {
            BasisCameraMode.Photo,
            BasisCameraMode.FlyingPuck,
            BasisCameraMode.FollowMe,
            BasisCameraMode.Cinematic,
        };

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ModePresetCameraUnderTest");
            _camera = _go.AddComponent<BasisHandHeldCamera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ---- The contract that keeps apply and match from drifting apart --------------------

        [Test]
        public void ApplyingAnyMode_LeavesTheCameraMatchingIt()
        {
            // The single most load-bearing test here. Apply writes a preset and Match decides
            // whether the camera is still in it; if a value is ever added to one and not the
            // other, the mode would flip to Custom the instant it was selected.
            foreach (BasisCameraMode mode in Presets)
            {
                _camera.ApplyCameraMode(mode);

                Assert.That(_camera.CameraMode, Is.EqualTo(mode), $"{mode} did not take.");
                Assert.That(_camera.MatchesCameraMode(mode), Is.True,
                    $"Applying {mode} left the camera not matching {mode} — apply and match disagree.");
                Assert.That(_camera.RefreshCameraMode(), Is.False,
                    $"A freshly applied {mode} must not immediately re-derive to something else.");
            }
        }

        [Test]
        public void EveryPresetIsDistinguishableFromEveryOther()
        {
            // Two modes that match the same camera state would make the label a coin flip.
            foreach (BasisCameraMode applied in Presets)
            {
                _camera.ApplyCameraMode(applied);

                foreach (BasisCameraMode other in Presets)
                {
                    if (other == applied) continue;
                    Assert.That(_camera.MatchesCameraMode(other), Is.False,
                        $"A camera in {applied} also reports as {other}.");
                }
            }
        }

        [Test]
        public void DefaultsToPhoto()
        {
            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo));
        }

        // ---- Drifting off a mode ------------------------------------------------------------

        [Test]
        public void ChangingASettingTheModeChose_DropsToCustom()
        {
            _camera.ApplyCameraMode(BasisCameraMode.FollowMe);

            _camera.autoFollowPlayspace = !_camera.autoFollowPlayspace;

            Assert.That(_camera.RefreshCameraMode(), Is.True, "The drift should have been noticed.");
            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom));
        }

        [Test]
        public void DisarmingFollow_LeavesFollowMe()
        {
            _camera.ApplyCameraMode(BasisCameraMode.FollowMe);

            _camera.SetAutoFollowEnabled(false);
            _camera.RefreshCameraMode();

            // Where it lands is deliberately not asserted. Switching follow off also hands the
            // camera back to the hand, and on a camera that has a lens and a volume profile the
            // leftover Follow Me optics put it on Custom — but this fixture has neither, so by
            // every measure that remains it is genuinely a Photo camera and says so. Both are
            // right; what matters is that it stops claiming to be following you when it is not.
            Assert.That(_camera.autoFollowEnabled, Is.False);
            Assert.That(_camera.CameraMode, Is.Not.EqualTo(BasisCameraMode.FollowMe),
                "Follow being armed is what Follow Me is, so switching it off cannot still be Follow Me.");
        }

        [Test]
        public void ChangingASettingTheModeDoesNotOwn_KeepsTheMode()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Photo);

            // The follow aim height is not in any preset — the modes leave it to the user.
            _camera.autoFollowLookAtHeightOffset = 0.42f;

            Assert.That(_camera.RefreshCameraMode(), Is.False);
            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo),
                "A setting no preset writes must not be able to knock the camera out of its mode.");
        }

        [Test]
        public void AModeThatGreysOutFollow_LeavesTheUsersFramingAlone()
        {
            // Photo, Flying Puck and Cinematic all colour the Follow section as doing nothing.
            // A mode that greys a section out must not quietly reset the values inside it, or the
            // user's framing is gone the next time they come back to Follow Me.
            _camera.ApplyCameraMode(BasisCameraMode.FollowMe);
            Vector3 framing = new Vector3(1.25f, 0.6f, 2.4f);
            _camera.autoFollowPositionOffset = framing;
            _camera.autoFollowLookAtHeightOffset = -0.3f;

            foreach (BasisCameraMode mode in new[]
                     { BasisCameraMode.Photo, BasisCameraMode.FlyingPuck, BasisCameraMode.Cinematic })
            {
                _camera.ApplyCameraMode(mode);

                Assert.That(_camera.autoFollowPositionOffset, Is.EqualTo(framing),
                    $"{mode} greys out Follow but reset the follow offset.");
                Assert.That(_camera.autoFollowLookAtHeightOffset, Is.EqualTo(-0.3f).Within(1e-4f),
                    $"{mode} greys out Follow but reset the aim height.");
            }
        }

        [Test]
        public void EditingFollowSettings_DoesNotKnockAModeThatIgnoresThemToCustom()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Photo);

            _camera.autoFollowPlayspace = !_camera.autoFollowPlayspace;
            _camera.autoFollowPositionOffset = new Vector3(3f, 2f, 1f);

            Assert.That(_camera.RefreshCameraMode(), Is.False);
            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo),
                "Photo does not run follow, so follow's settings cannot take it out of Photo.");
        }

        [Test]
        public void FlyingAPhotoCamera_KeepsItInPhoto()
        {
            // Where the camera is sitting is not how it is configured. Letting go of a handheld
            // camera, or grabbing a flying one back, must not read as leaving the mode.
            _camera.ApplyCameraMode(BasisCameraMode.Photo);

            _camera.PinSpace = CameraPinSpace.WorldSpace;
            _camera.RefreshCameraMode();

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo));
        }

        [Test]
        public void TuningBackToAPresetExactly_ReturnsToThatMode()
        {
            // The label is derived, not sticky: a camera that has been hand-tuned all the way onto
            // a preset is in that preset, and saying Custom would be a lie the user can see.
            _camera.ApplyCameraMode(BasisCameraMode.Photo);
            _camera.useAutoLeveling = true;
            _camera.RefreshCameraMode();
            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom), "Precondition.");

            _camera.useAutoLeveling = false;
            _camera.RefreshCameraMode();

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo));
        }

        // ---- Mode swapping ------------------------------------------------------------------

        [Test]
        public void SwappingBetweenEveryPairOfModes_LandsCleanly()
        {
            // Follow and the shot rig both claim world space on the way in and both hand it back
            // on the way out, so a careless order lets the loser's hand-back fire last and drag
            // the camera out of the pin the winner just took.
            foreach (BasisCameraMode from in Presets)
            {
                foreach (BasisCameraMode to in Presets)
                {
                    _camera.ApplyCameraMode(from);
                    _camera.ApplyCameraMode(to);

                    Assert.That(_camera.CameraMode, Is.EqualTo(to), $"{from} -> {to} did not land.");
                    Assert.That(_camera.MatchesCameraMode(to), Is.True, $"{from} -> {to} left a mismatch.");
                }
            }
        }

        [Test]
        public void FollowMeToCinematic_DisarmsFollowAndKeepsWorldSpace()
        {
            _camera.ApplyCameraMode(BasisCameraMode.FollowMe);
            _camera.ApplyCameraMode(BasisCameraMode.Cinematic);

            Assert.That(_camera.autoFollowEnabled, Is.False, "The rig cannot share the camera with follow.");
            Assert.That(_camera.cinematicEnabled, Is.True);
            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace),
                "Disarming follow must not drag the camera back to the hand the rig just took it from.");
        }

        [Test]
        public void CinematicToPhoto_StowsTheRigAndReturnsToTheHand()
        {
            _camera.ApplyCameraMode(BasisCameraMode.Cinematic);
            _camera.ApplyCameraMode(BasisCameraMode.Photo);

            Assert.That(_camera.cinematicEnabled, Is.False);
            Assert.That(_camera.autoFollowEnabled, Is.False);
            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.HandHeld));
        }

        [Test]
        public void RepeatedSwapping_Converges()
        {
            for (int Index = 0; Index < 10; Index++)
            {
                _camera.ApplyCameraMode(BasisCameraMode.FollowMe);
                Assert.That(_camera.MatchesCameraMode(BasisCameraMode.FollowMe), Is.True);

                _camera.ApplyCameraMode(BasisCameraMode.Photo);
                Assert.That(_camera.MatchesCameraMode(BasisCameraMode.Photo), Is.True);
            }
        }

        // ---- Custom -------------------------------------------------------------------------

        [Test]
        public void ApplyingCustom_ChangesNothingButTheLabel()
        {
            _camera.ApplyCameraMode(BasisCameraMode.FollowMe);
            bool followBefore = _camera.autoFollowEnabled;
            Vector3 offsetBefore = _camera.autoFollowPositionOffset;

            _camera.ApplyCameraMode(BasisCameraMode.Custom);

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom));
            Assert.That(_camera.autoFollowEnabled, Is.EqualTo(followBefore),
                "Custom has no preset to apply, so it must not disturb the camera.");
            Assert.That(_camera.autoFollowPositionOffset, Is.EqualTo(offsetBefore));
        }

        [Test]
        public void MatchesCameraMode_IsAlwaysFalseForCustom()
        {
            // There is nothing to match against, and answering true would let the resolver settle
            // on Custom before it had tried any real mode.
            Assert.That(_camera.MatchesCameraMode(BasisCameraMode.Custom), Is.False);
        }

        // ---- Restoring a saved mode ---------------------------------------------------------

        [Test]
        public void RestoringAMode_ReArmsFlightWithoutOverwritingLoadedSettings()
        {
            // A settings file carries every value a preset writes EXCEPT whether follow and the
            // rig are armed and where the camera is pinned. Restore therefore has to re-arm those
            // three and touch nothing else — re-applying the whole preset would overwrite the
            // values the load had just finished restoring with the preset's own.
            _camera.autoFollowPositionOffset = new Vector3(1.25f, 0.6f, 2.4f);
            _camera.autoFocusFollowSubject = true;
            _camera.useAutoLeveling = true;
            _camera.capture360Enabled = true;

            _camera.RestoreCameraModeForTest(BasisCameraMode.FollowMe);

            Assert.That(_camera.autoFollowEnabled, Is.True, "Flight must come back, per the saved mode.");
            Assert.That(_camera.PinSpace, Is.EqualTo(CameraPinSpace.WorldSpace));

            Assert.That(_camera.autoFollowPositionOffset, Is.EqualTo(new Vector3(1.25f, 0.6f, 2.4f)),
                "The loaded follow offset was overwritten by the preset's.");
            Assert.That(_camera.useAutoLeveling, Is.True, "The loaded auto-level was overwritten.");
            Assert.That(_camera.capture360Enabled, Is.True, "The loaded 360 toggle was overwritten.");
        }

        [Test]
        public void RestoringAModeTheSettingsNoLongerMatch_SettlesOnCustom()
        {
            // The file says Follow Me but its values have been edited since. The label has to
            // follow the values, not the other way round.
            _camera.useAutoLeveling = true;   // Follow Me wants this off.

            _camera.RestoreCameraModeForTest(BasisCameraMode.FollowMe);

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.Custom));
        }

        [Test]
        public void RestoringCustom_PromotesToAPresetWhenTheValuesMatchOne()
        {
            _camera.ApplyCameraMode(BasisCameraMode.FlyingPuck);

            _camera.RestoreCameraModeForTest(BasisCameraMode.Custom);

            Assert.That(_camera.CameraMode, Is.EqualTo(BasisCameraMode.FlyingPuck),
                "A hand-tuned file that lands exactly on a preset is in that preset.");
        }

        // ---- The mode table -----------------------------------------------------------------

        [Test]
        public void OrderedListsEveryModeExactlyOnce()
        {
            System.Array all = System.Enum.GetValues(typeof(BasisCameraMode));
            Assert.That(BasisCameraModes.Ordered.Length, Is.EqualTo(all.Length),
                "A mode missing from Ordered would never appear in the panel's dropdown.");

            foreach (BasisCameraMode mode in all)
            {
                Assert.That(System.Array.IndexOf(BasisCameraModes.Ordered, mode), Is.GreaterThanOrEqualTo(0),
                    $"{mode} is missing from the presentation order.");
            }
        }

        [Test]
        public void EveryModeDescribesEverySection()
        {
            foreach (BasisCameraMode mode in System.Enum.GetValues(typeof(BasisCameraMode)))
            {
                BasisCameraModeDescriptor descriptor = BasisCameraModes.Get(mode);
                Assert.That(descriptor.Mode, Is.EqualTo(mode), $"Get({mode}) returned the wrong descriptor.");
                Assert.That(descriptor.TitleKey, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.DescriptionKey, Is.Not.Null.And.Not.Empty);

                foreach (BasisCameraPanelSection section in System.Enum.GetValues(typeof(BasisCameraPanelSection)))
                {
                    // Reading a role must never throw or fall off the end of the table — a section
                    // added later has to arrive as Available rather than as an exception.
                    Assert.That(System.Enum.IsDefined(typeof(BasisCameraSectionRole), descriptor.RoleOf(section)),
                        Is.True, $"{mode}/{section} has no valid role.");
                }
            }
        }

        [Test]
        public void CustomClaimsNothing()
        {
            BasisCameraModeDescriptor custom = BasisCameraModes.Get(BasisCameraMode.Custom);

            foreach (BasisCameraPanelSection section in System.Enum.GetValues(typeof(BasisCameraPanelSection)))
            {
                Assert.That(custom.RoleOf(section), Is.EqualTo(BasisCameraSectionRole.Available),
                    $"Custom drives nothing and disables nothing, but claims {section}.");
            }
        }

        [Test]
        public void EveryPresetDrivesAndDisablesSomething()
        {
            // A mode that colours nothing tells the user nothing, which defeats the point.
            foreach (BasisCameraMode mode in Presets)
            {
                BasisCameraModeDescriptor descriptor = BasisCameraModes.Get(mode);
                int driven = 0;
                int inactive = 0;

                foreach (BasisCameraPanelSection section in System.Enum.GetValues(typeof(BasisCameraPanelSection)))
                {
                    if (descriptor.RoleOf(section) == BasisCameraSectionRole.Driven) driven++;
                    if (descriptor.RoleOf(section) == BasisCameraSectionRole.Inactive) inactive++;
                }

                Assert.That(driven, Is.GreaterThan(0), $"{mode} drives no section.");
                Assert.That(inactive, Is.GreaterThan(0), $"{mode} switches nothing off.");
            }
        }

        // ---- Tinting ------------------------------------------------------------------------

        [Test]
        public void TintsForTheThreeRolesAreVisiblyDifferent()
        {
            Color baseline = new Color(0.15f, 0.15f, 0.18f, 0.85f);
            const BasisCameraMode mode = BasisCameraMode.Cinematic;

            // Cinematic drives the shot rig, leaves colour alone, and switches follow off.
            Color driven = BasisCameraModes.TintFor(mode, BasisCameraPanelSection.Cinematic, baseline);
            Color available = BasisCameraModes.TintFor(mode, BasisCameraPanelSection.Colour, baseline);
            Color inactive = BasisCameraModes.TintFor(mode, BasisCameraPanelSection.Follow, baseline);

            Assert.That(Distance(driven, available), Is.GreaterThan(0.05f), "Driven is not distinct from available.");
            Assert.That(Distance(driven, inactive), Is.GreaterThan(0.05f), "Driven is not distinct from inactive.");
            Assert.That(Distance(available, inactive), Is.GreaterThan(0.02f), "Available is not distinct from inactive.");
        }

        [Test]
        public void TintingPreservesTheCardsOwnTranslucency()
        {
            // The alpha is the palette's, not the mode's: overwriting it would make a tinted panel
            // sit differently against the menu behind it than an untinted one.
            Color baseline = new Color(0.15f, 0.15f, 0.18f, 0.85f);

            foreach (BasisCameraMode mode in System.Enum.GetValues(typeof(BasisCameraMode)))
            {
                foreach (BasisCameraPanelSection section in System.Enum.GetValues(typeof(BasisCameraPanelSection)))
                {
                    Color tinted = BasisCameraModes.TintFor(mode, section, baseline);
                    bool inactive = BasisCameraModes.RoleOf(mode, section) == BasisCameraSectionRole.Inactive;

                    // Inactive deliberately fades as well as darkens; everything else keeps alpha.
                    if (inactive)
                    {
                        Assert.That(tinted.a, Is.LessThan(baseline.a), $"{mode}/{section} should fade.");
                        Assert.That(tinted.a, Is.GreaterThan(0f), $"{mode}/{section} faded to nothing.");
                    }
                    else
                    {
                        Assert.That(tinted.a, Is.EqualTo(baseline.a).Within(0.0001f),
                            $"{mode}/{section} changed the card's alpha.");
                    }
                }
            }
        }

        [Test]
        public void InactiveSectionsGoDarkerRatherThanMoreColourful()
        {
            Color baseline = new Color(0.30f, 0.30f, 0.34f, 0.85f);
            Color inactive = BasisCameraModes.TintFor(
                BasisCameraMode.FlyingPuck, BasisCameraPanelSection.Dolly, baseline);

            Assert.That(Luminance(inactive), Is.LessThan(Luminance(baseline)),
                "'Does nothing here' has to read as switched off, which more colour cannot say.");
        }

        [Test]
        public void TintingIsStableUnderRepetition()
        {
            // The panel re-asserts tints every tick against the palette's colour, so the same
            // input must always produce the same output — otherwise a section would creep.
            Color baseline = new Color(0.15f, 0.15f, 0.18f, 0.85f);
            Color first = BasisCameraModes.TintFor(BasisCameraMode.Photo, BasisCameraPanelSection.Lens, baseline);

            for (int Index = 0; Index < 5; Index++)
            {
                Color again = BasisCameraModes.TintFor(BasisCameraMode.Photo, BasisCameraPanelSection.Lens, baseline);
                Assert.That(Distance(again, first), Is.LessThan(0.0001f));
            }
        }

        [Test]
        public void EveryModeHasItsOwnColour()
        {
            BasisCameraMode[] all = BasisCameraModes.Ordered;
            for (int a = 0; a < all.Length; a++)
            {
                for (int b = a + 1; b < all.Length; b++)
                {
                    Color first = BasisCameraModes.Get(all[a]).Tint;
                    Color second = BasisCameraModes.Get(all[b]).Tint;
                    Assert.That(Distance(first, second), Is.GreaterThan(0.1f),
                        $"{all[a]} and {all[b]} are too close to tell apart.");
                }
            }
        }

        private static float Distance(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        private static float Luminance(Color c) => (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);
    }
}
