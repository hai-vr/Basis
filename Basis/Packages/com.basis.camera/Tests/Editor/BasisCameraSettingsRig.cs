using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityCamera = UnityEngine.Camera;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// A handheld camera assembled far enough to exercise its settings: a capture camera, the full
    /// set of post-processing overrides the UI writes to, and the prop's own sliders with the ranges
    /// <c>SetupSliderRanges</c> gives them.
    ///
    /// <para>
    /// The prefab's sliders are part of the rig on purpose. <c>CreateCurrentCameraSettings</c> reads
    /// several values back off those widgets rather than off the camera, so a rig without them would
    /// quietly test a code path the shipping prefab never takes.
    /// </para>
    ///
    /// <para>
    /// Awake never runs here — outside play mode Unity does not call it for a plain MonoBehaviour —
    /// so the component arrives with its field initializers applied and no scene dependencies.
    /// Anything <c>Initialize</c> would have wired is wired here instead, explicitly.
    /// </para>
    /// </summary>
    internal sealed class BasisCameraSettingsRig : IDisposable
    {
        public readonly BasisHandHeldCamera Camera;
        public readonly BasisHandHeldCameraUI UI;
        public readonly UnityCamera CaptureCamera;

        public readonly DepthOfField DepthOfField;
        public readonly Bloom Bloom;
        public readonly ColorAdjustments ColorAdjustments;
        public readonly Vignette Vignette;
        public readonly ChromaticAberration ChromaticAberration;
        public readonly FilmGrain FilmGrain;
        public readonly WhiteBalance WhiteBalance;
        public readonly LensDistortion LensDistortion;
        public readonly MotionBlur MotionBlur;

        public readonly Slider FovSlider;
        public readonly Slider ExposureSlider;
        public readonly Slider ApertureSlider;
        public readonly Slider FocusSlider;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        public BasisCameraSettingsRig()
        {
            GameObject root = NewObject("HandHeldCameraUnderTest");
            Camera = root.AddComponent<BasisHandHeldCamera>();
            // The interactable half of the camera reaches the camera half through a serialized
            // back-reference that the prefab supplies. Several settings — the field of view among
            // them — go nowhere without it, so the rig has to wire it the way the prefab does.
            Camera.HHC = Camera;

            GameObject captureGo = NewObject("CaptureCamera");
            CaptureCamera = captureGo.AddComponent<UnityCamera>();
            captureGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Camera.captureCamera = CaptureCamera;

            DepthOfField = NewOverride<DepthOfField>();
            Bloom = NewOverride<Bloom>();
            ColorAdjustments = NewOverride<ColorAdjustments>();
            Vignette = NewOverride<Vignette>();
            ChromaticAberration = NewOverride<ChromaticAberration>();
            FilmGrain = NewOverride<FilmGrain>();
            WhiteBalance = NewOverride<WhiteBalance>();
            LensDistortion = NewOverride<LensDistortion>();
            MotionBlur = NewOverride<MotionBlur>();

            BasisHandHeldCameraMetaData metaData = Camera.MetaData;
            metaData.depthOfField = DepthOfField;
            metaData.bloom = Bloom;
            metaData.colorAdjustments = ColorAdjustments;
            metaData.vignette = Vignette;
            metaData.chromaticAberration = ChromaticAberration;
            metaData.filmGrain = FilmGrain;
            metaData.whiteBalance = WhiteBalance;
            metaData.lensDistortion = LensDistortion;
            metaData.motionBlur = MotionBlur;

            // Mirrors CachePostProcessingReferences: colour grading is always live, the added
            // effects start switched off so an unconfigured one never alters the shot.
            ColorAdjustments.active = true;
            Vignette.active = false;
            ChromaticAberration.active = false;
            FilmGrain.active = false;
            WhiteBalance.active = false;
            LensDistortion.active = false;
            MotionBlur.active = false;

            // Ranges copied from SetupSliderRanges. A Slider defaults to 0..1, so without these
            // every value the settings carry would be clamped away on the way in.
            FovSlider = NewSlider("FOV", 20f, 120f);
            ExposureSlider = NewSlider("Exposure", 0f, BasisHandHeldCameraUI.ExposureStopCount - 1);
            ApertureSlider = NewSlider("Aperture", BasisHandHeldCameraUI.MinAperture, BasisHandHeldCameraUI.MaxAperture);
            FocusSlider = NewSlider("Focus", 0.1f, 100f);

            UI = new BasisHandHeldCameraUI
            {
                HHC = Camera,
                FOVSlider = FovSlider,
                ExposureSlider = ExposureSlider,
                DepthApertureSlider = ApertureSlider,
                DepthFocusDistanceSlider = FocusSlider,
            };

            // The back-link the prefab supplies, and the other half of the pair wired above. Without
            // it the camera holds a second, empty UI with no camera and no sliders, so anything that
            // reaches back through the camera to refresh the prop's HUD silently does nothing — and
            // saving harvests the field of view and aperture from those very sliders, so the miss
            // shows up as settings that quietly fail to round-trip.
            Camera.HandHeld = UI;
        }

        /// <summary>
        /// A settings object with every field moved off its default, so a round trip that drops a
        /// field shows up as a mismatch rather than coincidentally matching the default.
        /// </summary>
        public static BasisHandHeldCameraUI.CameraSettings DistinctiveSettings()
        {
            return new BasisHandHeldCameraUI.CameraSettings
            {
                resolutionIndex = 2,
                formatIndex = BasisHandHeldCameraUI.FORMAT_EXR,
                msaaSamples = 8,
                apertureIndex = 3,
                shutterSpeedIndex = 4,
                isoIndex = 5,
                exposureIndex = 9,
                showExposureOnCamera = true,
                fov = 77f,
                focusDistance = 12.5f,
                sensorSizeX = 23.6f,
                sensorSizeY = 15.7f,
                bloomIntensity = 1.75f,
                bloomThreshold = 1.1f,
                contrast = 22f,
                saturation = -14f,
                hueShift = 35f,
                depthAperture = 5.6f,
                depthFocusDistance = 4.25f,
                depthIsActive = true,
                dofMode = 1,
                dofFocalLength = 85f,
                dofBladeCount = 7,
                useManualFocus = false,
                VolumetricFogVolumedensity = 0.42f,
                VolumetricFogenableAPVContribution = false,
                VolumetricFogenableMainLightContribution = false,
                vignette = 0.35f,
                chromaticAberration = 0.2f,
                filmGrain = 0.15f,
                whiteBalanceTemperature = 18f,
                whiteBalanceTint = -12f,
                lensDistortion = 0.4f,
                motionBlurIntensity = 0.6f,
                motionBlurClamp = 0.12f,
                motionBlurQuality = 2,
                motionBlurMode = 1,
                autoFocusFollowSubject = true,
                autoFollowPositionOffset = new Vector3(1.25f, 0.6f, 2.4f),
                autoFollowRotationOffset = new Vector3(11f, -23f, 0f),
                autoFollowPlayspace = false,
                autoFollowLookAtPlayer = false,
                autoFollowLookAtHeightOffset = -0.35f,
                autoFollowLateralTracking = 0.8f,
                detachedMarker = (int)BasisCameraDetachedMarker.Gizmo,
                capture360 = true,
                useAutoLeveling = true,
                useVRHandheldSmoothing = true,
                backgroundMode = (int)BasisCameraBackgroundMode.BlueScreen,
                backgroundCustomColor = new Color(0.1f, 0.2f, 0.3f, 1f),
                backgroundKeepsWorld = true,
                subjectFramingRadius = 0.8f,
            };
        }

        private Slider NewSlider(string name, float min, float max)
        {
            GameObject go = NewObject("Slider_" + name, typeof(RectTransform));
            Slider slider = go.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            return slider;
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            GameObject go = new GameObject(name, components);
            _objects.Add(go);
            return go;
        }

        private T NewOverride<T>() where T : UnityEngine.Rendering.VolumeComponent
        {
            T component = ScriptableObject.CreateInstance<T>();
            _assets.Add(component);
            return component;
        }

        public void Dispose()
        {
            for (int Index = 0; Index < _objects.Count; Index++)
            {
                if (_objects[Index] != null) UnityEngine.Object.DestroyImmediate(_objects[Index]);
            }
            for (int Index = 0; Index < _assets.Count; Index++)
            {
                if (_assets[Index] != null) UnityEngine.Object.DestroyImmediate(_assets[Index]);
            }
            _objects.Clear();
            _assets.Clear();
        }
    }
}
