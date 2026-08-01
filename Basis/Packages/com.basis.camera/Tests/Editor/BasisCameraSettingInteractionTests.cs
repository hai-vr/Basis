using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Settings that share a destination. Each of these pairs writes to the same camera property from
    /// two different pages, so the bug they guard against is not a control that fails but a control
    /// that works right up until you touch the other one — the toggle that goes dead once a green
    /// screen is on, the focus slider that stops responding once follow-focus is picked, the mode
    /// that quietly switches depth of field back on over a saved off state.
    /// </summary>
    public class BasisCameraSettingInteractionTests
    {
        private BasisCameraSettingsRig _rig;

        [SetUp]
        public void SetUp() => _rig = new BasisCameraSettingsRig();

        [TearDown]
        public void TearDown() => _rig?.Dispose();

        // ---------- Render layers vs background ----------

        [Test]
        public void LayerToggles_KeepWorkingWhileAColourBackgroundOwnsTheCullingMask()
        {
            // A colour background narrows the live culling mask to the subject layers. A layer
            // toggle writing the camera directly would be thrown away by the next re-apply, and
            // read to the user as a toggle that simply does nothing.
            int layer = FirstTogglableLayer();
            if (layer < 0) Assert.Ignore("This project defines no user-togglable capture layer.");

            _rig.Camera.SetCaptureLayerEnabled(layer, true);
            _rig.Camera.SetBackgroundMode(BasisCameraBackgroundMode.GreenScreen);

            _rig.Camera.SetCaptureLayerEnabled(layer, false);
            Assert.That(_rig.Camera.IsCaptureLayerEnabled(layer), Is.False,
                "Turning a layer off during a green screen has to stick.");

            _rig.Camera.SetBackgroundMode(BasisCameraBackgroundMode.World);
            Assert.That(_rig.Camera.IsCaptureLayerEnabled(layer), Is.False,
                "...and still be off once the world comes back, rather than reappearing.");
        }

        [Test]
        public void LayerToggles_MadeUnderAColourBackgroundSurviveTheReturnToWorld()
        {
            int layer = FirstTogglableLayer();
            if (layer < 0) Assert.Ignore("This project defines no user-togglable capture layer.");

            _rig.Camera.SetCaptureLayerEnabled(layer, false);
            _rig.Camera.SetBackgroundMode(BasisCameraBackgroundMode.BlueScreen);
            _rig.Camera.SetCaptureLayerEnabled(layer, true);
            _rig.Camera.SetBackgroundMode(BasisCameraBackgroundMode.World);

            Assert.That(_rig.Camera.IsCaptureLayerEnabled(layer), Is.True);
        }

        [Test]
        public void ManagedLayers_AreRefusedSoTheCameraCannotUndermineItself()
        {
            // The camera drives the overlay and prop-HUD layers itself. Exposing them as toggles
            // would let the user leak the prop's own interface into the shot.
            for (int layer = 0; layer < 32; layer++)
            {
                if (BasisHandHeldCamera.IsCaptureLayerUserTogglable(layer)) continue;

                int before = _rig.CaptureCamera.cullingMask;
                _rig.Camera.SetCaptureLayerEnabled(layer, !_rig.Camera.IsCaptureLayerEnabled(layer));

                Assert.That(_rig.CaptureCamera.cullingMask, Is.EqualTo(before),
                    $"Layer {layer} is managed by the camera and must refuse the write.");
            }
        }

        [Test]
        public void KeyableBackground_StillOnlyShowsLayersTheOperatorLeftOn()
        {
            // The matte is the subject layers intersected with what was already rendering, so a
            // layer switched off before the green screen must not reappear on it.
            int subjectLayer = LayerMask.NameToLayer("Interactable");
            if (subjectLayer < 0) Assert.Ignore("This project has no Interactable layer.");
            if (!BasisHandHeldCamera.IsCaptureLayerUserTogglable(subjectLayer))
                Assert.Ignore("Interactable is camera-managed in this project.");

            _rig.Camera.SetCaptureLayerEnabled(subjectLayer, false);
            _rig.Camera.SetBackgroundMode(BasisCameraBackgroundMode.GreenScreen);

            Assert.That((_rig.CaptureCamera.cullingMask & (1 << subjectLayer)), Is.Zero);
        }

        [Test]
        public void ALayerTogglesTheSameWayUnderEveryBackgroundMode()
        {
            // The whole point of routing layer edits through WorldCullingMask is that the answer
            // does not depend on which background happens to be selected. Anything less and the
            // Render Layers list means something different on a green screen than off one.
            int layer = FirstTogglableLayer();
            if (layer < 0) Assert.Ignore("This project defines no user-togglable capture layer.");

            foreach (BasisCameraBackgroundMode mode in System.Enum.GetValues(typeof(BasisCameraBackgroundMode)))
            {
                _rig.Camera.SetBackgroundMode(mode);

                _rig.Camera.SetCaptureLayerEnabled(layer, true);
                Assert.That(_rig.Camera.IsCaptureLayerEnabled(layer), Is.True, $"turning on under {mode}");

                _rig.Camera.SetCaptureLayerEnabled(layer, false);
                Assert.That(_rig.Camera.IsCaptureLayerEnabled(layer), Is.False, $"turning off under {mode}");
            }
        }

        // ---------- Depth of field: style vs on/off vs focus mode ----------

        [Test]
        public void LoadingASavedStyle_DoesNotSwitchDepthOfFieldBackOn()
        {
            // dofMode carries the blur style, depthIsActive carries the on/off. Applying the style
            // must not decide the on/off, or a saved Bokeh style resurrects depth of field for
            // somebody who deliberately turned it off.
            var settings = new BasisHandHeldCameraUI.CameraSettings
            {
                dofMode = 2,
                depthIsActive = false,
            };

            _rig.UI.ApplySettingsForTest(settings);

            Assert.That(_rig.DepthOfField.mode.value, Is.EqualTo(DepthOfFieldMode.Bokeh));
            Assert.That(_rig.DepthOfField.active, Is.False);
        }

        [Test]
        public void LoadingAnEnabledFile_KeepsBothTheStyleAndTheOnState()
        {
            _rig.UI.ApplySettingsForTest(new BasisHandHeldCameraUI.CameraSettings
            {
                dofMode = 1,
                depthIsActive = true,
            });

            Assert.That(_rig.DepthOfField.mode.value, Is.EqualTo(DepthOfFieldMode.Gaussian));
            Assert.That(_rig.DepthOfField.active, Is.True);
        }

        [Test]
        public void FocalLength_AppliedBeforeFocus_SoTheClampUsesTheNewLens()
        {
            // The minimum focus distance is derived from the focal length. Applying focus first
            // clamps it against the old lens and the loaded focus lands somewhere else.
            _rig.UI.ApplySettingsForTest(new BasisHandHeldCameraUI.CameraSettings
            {
                dofMode = 2,
                depthIsActive = true,
                dofFocalLength = 300f,
                depthFocusDistance = 0.15f,
                depthAperture = 4f,
            });

            Assert.That(_rig.DepthOfField.focalLength.value, Is.EqualTo(300f).Within(1e-3f));
            Assert.That(_rig.DepthOfField.focusDistance.value, Is.EqualTo(_rig.Camera.MinimumFocusDistance).Within(1e-3f),
                "Focus inside the focal length inverts the circle of confusion and swaps near for far blur.");
        }

        [Test]
        public void FocusSlider_ShowsWhatTheEffectActuallyHolds()
        {
            // The slider travels to 0.1m but a long lens cannot focus that close, so pushing it
            // there has to leave the readout on the value the effect kept — not on 0.1, which
            // would read as focus being stuck.
            _rig.UI.ChangeDoFFocalLength(300f);
            _rig.UI.DepthChangeFocusDistance(0.1f);

            _rig.UI.SyncPropControlsFromState();

            Assert.That(_rig.FocusSlider.value, Is.EqualTo(_rig.DepthOfField.focusDistance.value).Within(1e-3f));
        }

        [Test]
        public void PickingFollowFocus_LeavesTheManualSliderReachableWhileNothingIsBeingFollowed()
        {
            // Follow Subject only drives focus while there is a subject to track. In hand, with no
            // follow target, the slider is the only way to focus and must stay usable.
            _rig.DepthOfField.active = true;
            _rig.Camera.autoFocusFollowSubject = true;

            _rig.UI.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Auto);

            Assert.That(_rig.UI.AutoFocusIsDriving, Is.False);
            Assert.That(_rig.FocusSlider.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void PickingFollowFocus_HidesTheManualSliderOnceSomethingIsActuallyBeingFollowed()
        {
            _rig.DepthOfField.active = true;
            _rig.Camera.autoFocusFollowSubject = true;
            _rig.Camera.SetAutoFollowEnabled(true);

            _rig.UI.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Auto);

            Assert.That(_rig.UI.AutoFocusIsDriving, Is.True);
            Assert.That(_rig.FocusSlider.gameObject.activeSelf, Is.False,
                "A slider that auto-focus overwrites every frame is a control that fights the user.");
        }

        [Test]
        public void TurningDepthOfFieldOff_HidesItsSlidersRatherThanLeavingThemInert()
        {
            _rig.UI.SetDoFMode(2);
            _rig.UI.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Manual);
            Assert.That(_rig.ApertureSlider.gameObject.activeSelf, Is.True);

            _rig.UI.SetDoFMode(0);

            Assert.That(_rig.ApertureSlider.gameObject.activeSelf, Is.False);
            Assert.That(_rig.FocusSlider.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void GaussianMode_HidesTheApertureItCannotUse()
        {
            // Aperture, focal length and blades are Bokeh-only. Left visible in Gaussian they are
            // three controls that do nothing.
            _rig.UI.SetDoFMode(1);
            _rig.UI.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Manual);

            Assert.That(_rig.ApertureSlider.gameObject.activeSelf, Is.False);
            Assert.That(_rig.FocusSlider.gameObject.activeSelf, Is.True,
                "Gaussian has no focus distance of its own, but the focus control is mapped onto its blur ramp.");
        }

        // ---------- Prop HUD and panel ----------

        [Test]
        public void ChangingASettingFromThePanel_ShowsUpOnThePropHud()
        {
            // The panel writes the camera; the prop's own HUD is re-seeded from the camera each
            // tick. Without that the two surfaces disagree the moment either is touched.
            _rig.UI.ChangeFOV(88f);
            _rig.UI.ChangeAperture(11f);
            _rig.UI.DepthChangeFocusDistance(6f);
            _rig.UI.ChangeExposureCompensation(2);

            _rig.UI.SyncPropControlsFromState();

            Assert.That(_rig.FovSlider.value, Is.EqualTo(88f).Within(1e-3f));
            Assert.That(_rig.ApertureSlider.value, Is.EqualTo(11f).Within(1e-3f));
            Assert.That(_rig.FocusSlider.value, Is.EqualTo(6f).Within(1e-3f));
            Assert.That(_rig.ExposureSlider.value, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void ReSeedingThePropHud_NeverDrivesTheCameraBack()
        {
            // The sync runs every frame while the panel is open. If it notified, the round trip
            // would re-apply a rounded slider value over the camera's own and settings would creep.
            _rig.UI.ChangeFOV(88.4f);
            float fovBefore = _rig.CaptureCamera.fieldOfView;

            for (int Index = 0; Index < 10; Index++) _rig.UI.SyncPropControlsFromState();

            Assert.That(_rig.CaptureCamera.fieldOfView, Is.EqualTo(fovBefore).Within(1e-4f));
        }

        [Test]
        public void ThePropSliderReachesBothEndsOfWhatThePanelCanSet()
        {
            // The prop's slider is what a save reads the field of view back off. If it were
            // narrower than the panel's range, the re-seed would clamp and the next save would
            // write the clamped value — the setting would visibly snap back on the next launch.
            foreach (float target in new[] { BasisHandHeldCameraUI.MinFov, BasisHandHeldCameraUI.MaxFov })
            {
                _rig.UI.ChangeFOV(target);
                _rig.UI.SyncPropControlsFromState();

                Assert.That(_rig.FovSlider.value, Is.EqualTo(target).Within(1e-3f),
                    $"The prop slider cannot reach {target} degrees, so saving there loses the difference.");
            }
        }

        // ---------- Follow, cinematic and pinning ----------

        [Test]
        public void CinematicRigAndAutoFollow_BothClaimWorldSpaceWithoutFightingOverIt()
        {
            _rig.Camera.SetAutoFollowEnabled(true);
            _rig.Camera.SetCinematicEnabled(true);

            Assert.That(_rig.Camera.PinSpace, Is.EqualTo(BasisHandHeldCameraInteractable.CameraPinSpace.WorldSpace));

            _rig.Camera.SetCinematicEnabled(false);
            _rig.Camera.SetAutoFollowEnabled(false);

            Assert.That(_rig.Camera.PinSpace, Is.EqualTo(BasisHandHeldCameraInteractable.CameraPinSpace.HandHeld),
                "Switching both off has to give the camera back to the hand, not strand it in the world.");
        }

        [Test]
        public void ChoosingAFollowTargetIsEnoughToLetFocusTrackThem()
        {
            // Following a remote does not fly the camera; it aims it. Focus has to be allowed to
            // track in that case too, or picking a subject leaves them soft.
            Assert.That(_rig.Camera.CanAutoFocusOnFollowSubject, Is.False);

            _rig.Camera.SetFollowTargetPlayer(4);

            Assert.That(_rig.Camera.CanAutoFocusOnFollowSubject, Is.True);
        }

        [Test]
        public void FollowSettingsAreIndependentOfWhetherFollowIsRunning()
        {
            // The offsets are edited with follow off as often as on — the panel is where a shot
            // gets set up before it is switched on.
            _rig.Camera.autoFollowPositionOffset = new Vector3(1f, 2f, 3f);
            _rig.Camera.autoFollowPlayspace = false;

            _rig.Camera.SetAutoFollowEnabled(true);
            _rig.Camera.SetAutoFollowEnabled(false);

            Assert.That(_rig.Camera.autoFollowPositionOffset, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(_rig.Camera.autoFollowPlayspace, Is.False);
        }

        // ---------- Output ----------

        [Test]
        public void MsaaSurvivesAResolutionChange()
        {
            // Changing resolution rebuilds the render target. A sample count applied afterwards
            // would be dropped on the floor by that rebuild, which is why load order matters.
            _rig.UI.ApplySettingsForTest(new BasisHandHeldCameraUI.CameraSettings
            {
                msaaSamples = 8,
                resolutionIndex = 2,
            });

            Assert.That(_rig.Camera.msaaSamples, Is.EqualTo(8));
            Assert.That(_rig.Camera.captureWidth, Is.EqualTo(3840));
        }

        [Test]
        public void SavedMsaa_IsAlwaysASampleCountTheGpuAccepts()
        {
            // The dropdown only offers 1/2/4/8 but a settings file is just text on disk, and a
            // render target built with a count the GPU rejects fails without a useful message.
            _rig.UI.ApplySettingsForTest(new BasisHandHeldCameraUI.CameraSettings { msaaSamples = 3 });

            Assert.That(new[] { 1, 2, 4, 8 }, Contains.Item(_rig.Camera.msaaSamples));
        }

        private static int FirstTogglableLayer()
        {
            for (int layer = 0; layer < 32; layer++)
            {
                if (BasisHandHeldCamera.IsCaptureLayerUserTogglable(layer)) return layer;
            }
            return -1;
        }
    }
}
