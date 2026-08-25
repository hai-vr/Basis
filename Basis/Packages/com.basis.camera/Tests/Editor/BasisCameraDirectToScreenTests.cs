using Basis.BasisUI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityCamera = UnityEngine.Camera;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Direct To Screen: the feed on the monitor in place of the headset mirror. The rules it lives
    /// by — VR only, one camera at a time, never throttled, no socket on a film body, and the
    /// window handed back and taken again as the device swaps — are decisions that can be made
    /// without a headset, so they are pinned here without one.
    /// </summary>
    public class BasisCameraDirectToScreenTests
    {
        private BasisCameraSettingsRig _rig;
        private bool _limitRate;
        private float _rateHz;

        [SetUp]
        public void SetUp()
        {
            _rig = new BasisCameraSettingsRig();

            _limitRate = BasisSettingsDefaults.LimitHandHeldCameraRate.RawValue;
            _rateHz = BasisSettingsDefaults.HandHeldCameraRenderHz.RawValue;
            BasisSettingsDefaults.LimitHandHeldCameraRate.SetValueWithoutNotify(false);

            // Every test below stands in the headset unless it says otherwise.
            BasisHandHeldCamera.VRModeOverrideForTest = true;
        }

        [TearDown]
        public void TearDown()
        {
            BasisHandHeldCamera.VRModeOverrideForTest = null;
            BasisSettingsDefaults.LimitHandHeldCameraRate.SetValueWithoutNotify(_limitRate);
            BasisSettingsDefaults.HandHeldCameraRenderHz.SetValueWithoutNotify(_rateHz);
            _rig?.Dispose();
        }

        [Test]
        public void Off_ByDefault()
        {
            Assert.IsFalse(_rig.Camera.DirectToScreen);
            Assert.IsFalse(_rig.Camera.IsDirectToScreenPresenting);
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.Off));
            Assert.IsNull(BasisCameraDirectToScreenOutput.Presenting);
        }

        [Test]
        public void TheDecision_NeedsEveryConditionAtOnce()
        {
            Assert.IsTrue(BasisHandHeldCamera.ShouldPresentDirectToScreen(true, true, true, true));
            Assert.IsFalse(BasisHandHeldCamera.ShouldPresentDirectToScreen(false, true, true, true), "Off is off.");
            Assert.IsFalse(BasisHandHeldCamera.ShouldPresentDirectToScreen(true, false, true, true),
                "In desktop mode the window is already the operator's own view.");
            Assert.IsFalse(BasisHandHeldCamera.ShouldPresentDirectToScreen(true, true, false, true), "A film body has no socket.");
            Assert.IsFalse(BasisHandHeldCamera.ShouldPresentDirectToScreen(true, true, true, false), "No window, nothing to draw on.");
        }

        [Test]
        public void SwitchedOn_InVR_TakesTheWindow()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            _rig.Camera.SetDirectToScreen(true);

            Assert.IsTrue(_rig.Camera.IsDirectToScreenPresenting);
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.Presenting));

            BasisCameraDirectToScreenOutput output = BasisCameraDirectToScreenOutput.Presenting;
            Assert.IsNotNull(output);
            Assert.IsTrue(output.transform.IsChildOf(_rig.Camera.transform), "The screen camera goes with the camera it belongs to.");

            UnityCamera screen = output.ScreenCamera;
            Assert.IsNotNull(screen);
            Assert.IsTrue(screen.enabled);
            Assert.IsTrue(output.IsScreenCamera(screen));
            Assert.That(screen.depth, Is.GreaterThan(_rig.CaptureCamera.depth), "It has to render after the feed it shows.");
            Assert.That(screen.cullingMask, Is.Zero, "It draws nothing of its own.");
            Assert.IsNull(screen.targetTexture, "It has to land on the window.");
            Assert.IsTrue(screen.allowHDR, "A float feed keeps its range, and an HDR display gets URP's own encoding.");
            Assert.IsFalse(screen.allowMSAA, "The target only ever receives a full-screen blit; samples would be waste.");

            UniversalAdditionalCameraData data = screen.GetUniversalAdditionalCameraData();
            Assert.IsFalse(data.allowXRRendering, "XR rendering would send it to the headset instead of the window.");
            Assert.IsTrue(data.allowHDROutput, "URP's final blit does the display's HDR encoding only when the camera allows it.");
            Assert.That(data.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.IsFalse(data.renderPostProcessing);
        }

        [Test]
        public void SwitchingToDesktop_HandsTheWindowBack_AndVRTakesItAgain()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            _rig.Camera.SetDirectToScreen(true);
            Assert.IsTrue(_rig.Camera.IsDirectToScreenPresenting);
            UnityCamera screen = BasisCameraDirectToScreenOutput.Presenting.ScreenCamera;

            // The hot-swap to desktop: the mode announces itself and the camera re-decides.
            BasisHandHeldCamera.VRModeOverrideForTest = false;
            _rig.Camera.RefreshDirectToScreen();

            Assert.IsFalse(_rig.Camera.IsDirectToScreenPresenting, "In desktop mode the main camera is already on the window.");
            Assert.IsTrue(_rig.Camera.DirectToScreen, "The setting is kept; only the window is handed back.");
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.WaitingForVR));
            Assert.IsFalse(screen.enabled, "A disabled screen camera costs nothing and draws nothing.");
            Assert.IsNull(BasisCameraDirectToScreenOutput.Presenting);

            // And back into VR: nothing was touched, so it takes the window over again.
            BasisHandHeldCamera.VRModeOverrideForTest = true;
            _rig.Camera.RefreshDirectToScreen();

            Assert.IsTrue(_rig.Camera.IsDirectToScreenPresenting);
            Assert.IsTrue(screen.enabled);
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.Presenting));
        }

        [Test]
        public void SwitchingItOff_HandsTheWindowBack()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            _rig.Camera.SetDirectToScreen(true);
            _rig.Camera.SetDirectToScreen(false);

            Assert.IsFalse(_rig.Camera.IsDirectToScreenPresenting);
            Assert.IsNull(BasisCameraDirectToScreenOutput.Presenting);
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.Off));
        }

        [Test]
        public void AFilmBody_HasNoSocketForIt()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            BasisHandHeldCameraUI.CameraSettings film = new BasisHandHeldCameraUI.CameraSettings
            {
                cameraBody = (int)BasisCameraBodyKind.Disposable,
                directToScreen = true,
            };
            _rig.UI.ApplySettingsForTest(film);

            Assert.IsTrue(_rig.Camera.DirectToScreen, "The setting survives; the body overrules it.");
            Assert.IsFalse(_rig.Camera.IsDirectToScreenPresenting);
            Assert.That(_rig.Camera.DirectToScreenState, Is.EqualTo(BasisCameraDirectToScreenState.NoOutputSocket));

            // Fitting a digital body gives the window back without the setting having moved.
            BasisHandHeldCameraUI.CameraSettings digital = new BasisHandHeldCameraUI.CameraSettings
            {
                cameraBody = (int)BasisCameraBodyKind.Digital,
                directToScreen = true,
            };
            _rig.UI.ApplySettingsForTest(digital);

            Assert.IsTrue(_rig.Camera.IsDirectToScreenPresenting);
        }

        [Test]
        public void TheMonitor_KeepsAnOffScreenCameraRendering()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            _rig.Camera.SetDirectToScreen(true);
            _rig.Camera.SetRendererVisibleForTest(false);

            Assert.IsTrue(_rig.CaptureCamera.enabled,
                "The monitor is showing the feed, so the prop being out of view says nothing about whether anyone is watching.");
        }

        [Test]
        public void TheMonitor_IsNeverThrottled()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            // A cap of one frame a second, which holds the camera off for nearly every gate
            // evaluation — and does, for the viewfinder alone.
            BasisSettingsDefaults.HandHeldCameraRenderHz.SetValueWithoutNotify(1f);
            BasisSettingsDefaults.LimitHandHeldCameraRate.SetValueWithoutNotify(true);

            _rig.Camera.SetDirectToScreen(true);
            for (int Frame = 0; Frame < 5; Frame++)
            {
                _rig.Camera.SetRendererVisibleForTest(true);
                Assert.IsTrue(_rig.CaptureCamera.enabled,
                    $"Throttled on gate evaluation {Frame}: a picture that stutters where the headset mirror was smooth reads as broken.");
            }
        }

        [Test]
        public void OneCameraHasTheWindowAtATime()
        {
            Assume.That(BasisHandHeldCamera.IsDirectToScreenSupported, "This test needs a platform with a desktop window.");

            BasisCameraSettingsRig other = new BasisCameraSettingsRig();
            BasisHandHeldCameraRegistry.Add(_rig.Camera);
            BasisHandHeldCameraRegistry.Add(other.Camera);
            try
            {
                _rig.Camera.SetDirectToScreen(true);
                Assert.IsTrue(_rig.Camera.IsDirectToScreenPresenting);

                other.Camera.SetDirectToScreen(true);

                Assert.IsFalse(_rig.Camera.DirectToScreen,
                    "Switching it on for one camera switches it off for the rest, so the panel can read the setting back.");
                Assert.IsFalse(_rig.Camera.IsDirectToScreenPresenting);
                Assert.IsTrue(other.Camera.IsDirectToScreenPresenting);
                Assert.IsTrue(BasisCameraDirectToScreenOutput.Presenting.transform.IsChildOf(other.Camera.transform));
            }
            finally
            {
                BasisHandHeldCameraRegistry.Remove(other.Camera);
                BasisHandHeldCameraRegistry.Remove(_rig.Camera);
                other.Dispose();
            }
        }

        [Test]
        public void TheFeedHandle_FollowsTheTextureWithoutOwningIt()
        {
            // An output with no owner, so the handle is driven by hand rather than re-synced from
            // the camera's own render texture on every read.
            GameObject go = new GameObject("OutputUnderTest");
            BasisCameraDirectToScreenOutput output = go.AddComponent<BasisCameraDirectToScreenOutput>();

            // With depth buffers, like the capture camera's target: the case the render graph
            // refuses to describe on its own, which is why the handle wraps an identifier.
            RenderTexture first = new RenderTexture(8, 8, 24);
            RenderTexture second = new RenderTexture(16, 16, 24);
            try
            {
                first.Create();
                second.Create();

                Assert.IsFalse(output.TryGetFeed(out _, out _), "Nothing to draw yet.");

                output.SetFeed(first);
                Assert.IsTrue(output.TryGetFeed(out RTHandle handle, out RenderTexture texture));
                Assert.That(texture, Is.SameAs(first));
                Assert.That(handle.nameID, Is.EqualTo(new RenderTargetIdentifier(first)));
                Assert.IsNull(handle.rt, "Wrapped by identifier, so the graph is told the texture's description rather than deriving one that includes the depth buffer.");

                // The camera rebuilds its texture on every resize: the handle has to follow it, and
                // letting go of the old one must not destroy a texture the camera still owns.
                output.SetFeed(second);
                Assert.IsTrue(output.TryGetFeed(out handle, out texture));
                Assert.That(texture, Is.SameAs(second));
                Assert.That(handle.nameID, Is.EqualTo(new RenderTargetIdentifier(second)));
                Assert.IsTrue(first.IsCreated(), "The handle never owned the texture, so releasing it leaves the texture alone.");

                output.SetFeed(null);
                Assert.IsFalse(output.TryGetFeed(out _, out _));
            }
            finally
            {
                Object.DestroyImmediate(go);
                first.Release();
                second.Release();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void FitViewport_KeepsTheShotsAspect()
        {
            Rect window = new Rect(0f, 0f, 1920f, 1080f);

            // Same aspect: the whole window.
            Assert.That(BasisCameraDirectToScreenPass.FitViewport(1920, 1080, window), Is.EqualTo(new Rect(0f, 0f, 1920f, 1080f)));

            // A portrait shot on a landscape monitor: pillarboxed and centred.
            Rect portrait = BasisCameraDirectToScreenPass.FitViewport(1080, 1920, window);
            Assert.That(portrait.height, Is.EqualTo(1080f));
            Assert.That(portrait.width, Is.EqualTo(608f).Within(1f));
            Assert.That(portrait.x, Is.EqualTo(656f).Within(1f));
            Assert.That(portrait.y, Is.EqualTo(0f));

            // A wide shot on a squarer monitor: letterboxed and centred, with the window's own offset kept.
            Rect letterbox = BasisCameraDirectToScreenPass.FitViewport(1920, 1080, new Rect(10f, 20f, 1600f, 1200f));
            Assert.That(letterbox.width, Is.EqualTo(1600f));
            Assert.That(letterbox.height, Is.EqualTo(900f));
            Assert.That(letterbox.x, Is.EqualTo(10f));
            Assert.That(letterbox.y, Is.EqualTo(170f));
        }

        [Test]
        public void FitViewport_DrawsNothingForNothing()
        {
            Rect window = new Rect(0f, 0f, 1920f, 1080f);
            Assert.That(BasisCameraDirectToScreenPass.FitViewport(0, 1080, window), Is.EqualTo(Rect.zero));
            Assert.That(BasisCameraDirectToScreenPass.FitViewport(1920, 0, window), Is.EqualTo(Rect.zero));
            Assert.That(BasisCameraDirectToScreenPass.FitViewport(1920, 1080, Rect.zero), Is.EqualTo(Rect.zero));
        }
    }
}
