// Screen space global illumination is optional: the define comes from the
// com.jiaozi158.unityssgiurp package being present (asmdef versionDefines), and the effect is
// not viable on mobile GPUs, so the whole integration compiles out on Android.
#if BASIS_HAS_SSGI && !UNITY_ANDROID
using System;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using QualityMode = ScreenSpaceGlobalIlluminationVolume.QualityMode;

namespace Basis.Tests.Graphics
{
    public class BasisScreenSpaceGlobalIlluminationSettingsTests
    {
        private GameObject host;
        private Func<UnityEngine.Camera, bool> previousFilter;
        private UnityEngine.Camera previousCameraInstance;

        [SetUp]
        public void SetUp()
        {
            previousFilter = ScreenSpaceGlobalIlluminationURP.CameraFilter;
            previousCameraInstance = BasisLocalCameraDriver.CameraInstance;
        }

        [TearDown]
        public void TearDown()
        {
            ScreenSpaceGlobalIlluminationURP.CameraFilter = previousFilter;
            BasisLocalCameraDriver.CameraInstance = previousCameraInstance;
            if (host != null)
            {
                SMModuleScreenSpaceGlobalIlluminationURP module = host.GetComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
                if (module != null && module.Volume != null && module.Volume.sharedProfile != null)
                {
                    UnityEngine.Object.DestroyImmediate(module.Volume.sharedProfile);
                }
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }
        }

        [Test]
        public void ScreenSpaceGlobalIlluminationIsOffByDefault()
        {
            Assert.IsFalse(BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.DefaultValue.GetDefault());
            Assert.AreEqual("Medium", BasisSettingsDefaults.ScreenSpaceGlobalIlluminationQuality.DefaultValue.GetDefault());
            Assert.IsFalse(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFullResolution.DefaultValue.GetDefault());
            Assert.AreEqual(1f, BasisSettingsDefaults.ScreenSpaceGlobalIlluminationIntensity.DefaultValue.GetDefault());
        }

        [Test]
        public void IntensityRangeDoesNotUseZeroAsADefault()
        {
            Assert.Greater(BasisSettingsDefaults.SSGI_INTENSITY_MIN, 0f);
            Assert.Greater(BasisSettingsDefaults.SSGI_INTENSITY_MAX, BasisSettingsDefaults.SSGI_INTENSITY_MIN);
            float defaultIntensity = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationIntensity.DefaultValue.GetDefault();
            Assert.GreaterOrEqual(defaultIntensity, BasisSettingsDefaults.SSGI_INTENSITY_MIN);
            Assert.LessOrEqual(defaultIntensity, BasisSettingsDefaults.SSGI_INTENSITY_MAX);
        }

        [TestCase("low", QualityMode.Low)]
        [TestCase("Medium", QualityMode.Medium)]
        [TestCase("HIGH", QualityMode.High)]
        [TestCase("garbage", QualityMode.Medium)]
        [TestCase(null, QualityMode.Medium)]
        public void QualityDropdownValuesParseCaseInsensitively(string option, QualityMode expected)
        {
            Assert.AreEqual(expected, SMModuleScreenSpaceGlobalIlluminationURP.ReadQuality(option));
        }

        [TestCase("Off", ScreenSpaceGlobalIlluminationURP.DebugViewMode.None)]
        [TestCase("indirect light", ScreenSpaceGlobalIlluminationURP.DebugViewMode.IndirectLight)]
        [TestCase("GI Contribution", ScreenSpaceGlobalIlluminationURP.DebugViewMode.GlobalIlluminationContribution)]
        [TestCase("gbuffer albedo", ScreenSpaceGlobalIlluminationURP.DebugViewMode.GBufferAlbedo)]
        [TestCase("GBUFFER NORMALS", ScreenSpaceGlobalIlluminationURP.DebugViewMode.GBufferNormals)]
        [TestCase(null, ScreenSpaceGlobalIlluminationURP.DebugViewMode.None)]
        public void DebugViewDropdownValuesParse(string option, ScreenSpaceGlobalIlluminationURP.DebugViewMode expected)
        {
            Assert.AreEqual(expected, SMModuleScreenSpaceGlobalIlluminationURP.ReadDebugView(option));
        }

        [Test]
        public void DebugViewIsOffByDefault()
        {
            Assert.AreEqual("Off", BasisSettingsDefaults.DevSsgiDebugView.DefaultValue.GetDefault());
            Assert.AreEqual(ScreenSpaceGlobalIlluminationURP.DebugViewMode.None, SMModuleScreenSpaceGlobalIlluminationURP.ReadDebugView(BasisSettingsDefaults.DevSsgiDebugView.DefaultValue.GetDefault()));
        }

        [Test]
        public void ApplyOverridesOnlyTheUsersControlsAndLeavesArtisticParametersToTheWorld()
        {
            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.High, true, 1.5f);

                Assert.IsTrue(ssgi.enable.overrideState);
                Assert.IsTrue(ssgi.enable.value);
                Assert.IsTrue(ssgi.quality.overrideState);
                Assert.AreEqual(QualityMode.High, ssgi.quality.value);
                Assert.IsTrue(ssgi.sampleCount.overrideState);
                Assert.AreEqual(4, ssgi.sampleCount.value);
                Assert.IsTrue(ssgi.maxRaySteps.overrideState);
                Assert.AreEqual(64, ssgi.maxRaySteps.value);
                Assert.IsTrue(ssgi.fullResolutionSS.overrideState);
                Assert.IsTrue(ssgi.fullResolutionSS.value);
                Assert.IsTrue(ssgi.indirectDiffuseLightingMultiplier.overrideState);
                Assert.AreEqual(1.5f, ssgi.indirectDiffuseLightingMultiplier.value);

                Assert.IsFalse(ssgi.thicknessMode.overrideState);
                Assert.IsFalse(ssgi.depthBufferThickness.overrideState);
                Assert.IsFalse(ssgi.rayMiss.overrideState);
                Assert.IsFalse(ssgi.denoiseSS.overrideState);
                Assert.IsFalse(ssgi.denoiserAlgorithmSS.overrideState);
                Assert.IsFalse(ssgi.resolutionScaleSS.overrideState);

                // The one denoiser parameter that is NOT left to the world: how much of last frame is
                // reused decides whether the bounce trails behind a moving player, which is comfort.
                Assert.IsTrue(ssgi.denoiseIntensitySS.overrideState);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void TemporalDenoiseDefaultsBelowTheVolumeMaximumSoTheBounceDoesNotTrail()
        {
            // The volume ships denoiseIntensitySS at 0.95, the top of its own range, which keeps ~91% of
            // last frame once accumulated. That is what smears the bounce behind anything that moves.
            Assert.Less(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.DefaultValue.GetDefault(),
                BasisSettingsDefaults.SSGI_DENOISE_MAX);

            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                Assert.AreEqual(BasisSettingsDefaults.SSGI_DENOISE_MAX, ssgi.denoiseIntensitySS.value, 0.0001f);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Medium, false, 1f, false, 0.8f);
                Assert.AreEqual(0.8f, ssgi.denoiseIntensitySS.value, 0.0001f);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Medium, false, 1f, false, 5f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_DENOISE_MAX, ssgi.denoiseIntensitySS.value, 0.0001f);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Medium, false, 1f, false, -1f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_DENOISE_MIN, ssgi.denoiseIntensitySS.value, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void ContentAlreadyBuiltIntoAssetBundlesReceivesLightByDefault()
        {
            // Shaders inside a published avatar, prop or world cannot gain a GBuffer pass after the fact,
            // so the fallback is the only path by which existing content receives bounce light.
            Assert.IsTrue(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationGBufferFallback.DefaultValue.GetDefault());
        }

        [Test]
        public void FallbackAlbedoRangeDoesNotUseZeroAsADefault()
        {
            Assert.Greater(BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MIN, 0f);
            Assert.GreaterOrEqual(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.DefaultValue.GetDefault(),
                BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MIN);
            Assert.LessOrEqual(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.DefaultValue.GetDefault(),
                BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MAX);
        }

        [Test]
        public void RendererOptionsAreAppliedToTheFeatureAndTheAlbedoIsClamped()
        {
            ScreenSpaceGlobalIlluminationURP feature = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationURP>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, true, 0.25f, false, true, false, true, 1f, 2f);

                Assert.IsTrue(feature.GBufferFallback);
                Assert.AreEqual(0.25f, feature.FallbackAlbedo, 0.0001f);
                Assert.IsFalse(feature.ReflectionProbes);
                Assert.IsTrue(feature.HighQualityUpscaling);
                Assert.IsFalse(feature.OverrideAmbientLighting);
                Assert.IsTrue(feature.BackfaceLighting);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, false, 9f, true, false, true, false, 1f, 2f);
                Assert.IsFalse(feature.GBufferFallback);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MAX, feature.FallbackAlbedo, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void EmissiveAndFallbackGainReachTheFeatureAndAreClamped()
        {
            ScreenSpaceGlobalIlluminationURP feature = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationURP>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, true, 0.5f, true, true, true, false, 3f, 4f);
                Assert.AreEqual(3f, feature.EmissiveMultiplier, 0.0001f);
                Assert.AreEqual(4f, feature.FallbackMaxGain, 0.0001f);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, true, 0.5f, true, true, true, false, 999f, 999f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_EMISSIVE_MAX, feature.EmissiveMultiplier, 0.0001f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_FALLBACK_MAX_GAIN_MAX, feature.FallbackMaxGain, 0.0001f);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, true, 0.5f, true, true, true, false, -5f, -5f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_EMISSIVE_MIN, feature.EmissiveMultiplier, 0.0001f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_FALLBACK_MAX_GAIN_MIN, feature.FallbackMaxGain, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void AnEmissiveMultiplierOfOneLeavesTheEffectAsItWasWithoutAnEmissionBuffer()
        {
            // The colour history a ray reads already carries emission, so the emission buffer only supplies what the
            // multiplier asks for beyond it. At 1 that is nothing, which is what makes this setting safe to ship on.
            Assert.AreEqual(1f, BasisSettingsDefaults.ScreenSpaceGlobalIlluminationEmissive.DefaultValue.GetDefault(), 0.0001f);
            Assert.AreEqual(1f, BasisSettingsDefaults.SSGI_EMISSIVE_MIN, 0.0001f);
        }

        [Test]
        public void TurningTheEffectOffDeactivatesTheRendererFeature()
        {
            // Regression: the master switch only wrote enable=false onto volume profiles, so a world volume
            // or a pipeline default profile the player's own volume never outranked kept SSGI rendering.
            // URP skips AddRenderPasses entirely for an inactive feature, so that is the switch that holds.
            ScreenSpaceGlobalIlluminationURP feature = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationURP>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, false, true, 0.5f, true, false, true, false, 1f, 2f);
                Assert.IsFalse(feature.isActive);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(feature, true, true, 0.5f, true, false, true, false, 1f, 2f);
                Assert.IsTrue(feature.isActive);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void TheShippedRendererAndPipelineProfilesCarryTheEffectSwitchedOff()
        {
            // The setting defaults to off, so everything the project ships has to agree with it: a renderer
            // feature left active or a pipeline default profile left enabled renders bounce light before the
            // settings module has run at all, and on every camera the module's own volume never reaches.
            ScreenSpaceGlobalIlluminationURP feature = SMModuleScreenSpaceGlobalIlluminationURP.FindFeature();
            if (feature == null)
            {
                Assert.Ignore("The active render pipeline carries no screen space global illumination feature.");
            }
            Assert.IsFalse(feature.isActive, "The renderer ships with the screen space global illumination feature active.");
            AssertProfileCarriesTheEffectOff(VolumeManager.instance.globalDefaultProfile);
            AssertProfileCarriesTheEffectOff(VolumeManager.instance.qualityDefaultProfile);
        }

        private static void AssertProfileCarriesTheEffectOff(VolumeProfile profile)
        {
            if (profile == null || !profile.TryGet(out ScreenSpaceGlobalIlluminationVolume ssgi))
            {
                return;
            }
            Assert.IsFalse(ssgi.enable.value, profile.name + " ships with screen space global illumination enabled.");
        }

        [Test]
        public void TheModuleDrivesTheFeatureActiveStateFromTheSetting()
        {
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
            ScreenSpaceGlobalIlluminationURP feature = SMModuleScreenSpaceGlobalIlluminationURP.FindFeature();
            if (feature == null)
            {
                Assert.Ignore("The active render pipeline carries no screen space global illumination feature.");
            }
            bool authored = feature.isActive;
            try
            {
                module.ValidSettingsChange(BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.BindingKey, "true");
                Assert.IsTrue(feature.isActive);

                module.ValidSettingsChange(BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.BindingKey, "false");
                Assert.IsFalse(feature.isActive);
            }
            finally
            {
                feature.SetActive(authored);
            }
        }

        [Test]
        public void MovingTheSmoothingSliderReachesTheOwnedVolume()
        {
            // Regression: the slider was persisted and its value threaded through Apply's signature, but
            // ApplyOverride still passed the binding default, so dragging it changed nothing on screen.
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
            module.ApplyOverride();

            module.ValidSettingsChange(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.BindingKey,
                BasisSettingsDefaults.SSGI_DENOISE_MIN.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Assert.AreEqual(BasisSettingsDefaults.SSGI_DENOISE_MIN, module.Ssgi.denoiseIntensitySS.value, 0.0001f);
        }

        [Test]
        public void MovingTheUnsupportedShaderSlidersAndTogglesReachesTheFeatureState()
        {
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
            module.ApplyOverride();

            // No feature exists on the test renderer, so this must stay quiet rather than throw.
            Assert.DoesNotThrow(() => module.ValidSettingsChange(
                BasisSettingsDefaults.ScreenSpaceGlobalIlluminationGBufferFallback.BindingKey, "false"));
            Assert.DoesNotThrow(() => module.ValidSettingsChange(
                BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.BindingKey, "0.25"));
        }

        [Test]
        public void TheTogglePropagatesToThePipelinesOwnDefaultVolumeProfile()
        {
            // Basis ships DefaultVolumeProfile with the effect overridden ON. URP seeds every camera's
            // stack from it before any scene volume, so unless the toggle reaches it the effect is pinned on.
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
            module.ApplyOverride();

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                ScreenSpaceGlobalIlluminationVolume seeded = profile.Add<ScreenSpaceGlobalIlluminationVolume>(false);
                seeded.enable.overrideState = true;
                seeded.enable.value = true;

                module.ValidSettingsChange(BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.BindingKey, "false");
                module.ApplyToProfile(profile);

                Assert.IsTrue(seeded.enable.overrideState);
                Assert.IsFalse(seeded.enable.value, "Turning the setting off must also clear the pipeline default profile.");

                module.ValidSettingsChange(BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.BindingKey, "true");
                module.ApplyToProfile(profile);
                Assert.IsTrue(seeded.enable.value);

                module.RestoreAuthoredProfiles();
                Assert.IsTrue(seeded.enable.value, "Teardown restores what the asset was authored with.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ApplyingRendererOptionsWithoutAFeatureIsHarmless()
        {
            // Android ships a renderer without the feature, so the lookup returns null every frame there.
            Assert.DoesNotThrow(() => SMModuleScreenSpaceGlobalIlluminationURP.Apply(null, true, true, 0.5f, true, true, true, true, 1f, 2f));
            Assert.IsNull(SMModuleScreenSpaceGlobalIlluminationURP.FindFeature(null));
        }

        [TestCase(QualityMode.Low, 1, 24)]
        [TestCase(QualityMode.Medium, 2, 32)]
        [TestCase(QualityMode.High, 4, 64)]
        public void QualityPresetsMatchThePackagePresets(QualityMode quality, int samples, int steps)
        {
            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, quality, false, 1f);
                Assert.AreEqual(samples, ssgi.sampleCount.value);
                Assert.AreEqual(steps, ssgi.maxRaySteps.value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void ACaptureRaisesSamplesAndResolutionOnlyWhileItLasts()
        {
            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Low, false, 1f, true);
                Assert.AreEqual(SMModuleScreenSpaceGlobalIlluminationURP.CaptureSampleCount, ssgi.sampleCount.value);
                Assert.AreEqual(ssgi.sampleCount.max, ssgi.sampleCount.value);
                Assert.AreEqual(SMModuleScreenSpaceGlobalIlluminationURP.CaptureMaxRaySteps, ssgi.maxRaySteps.value);
                Assert.IsTrue(ssgi.fullResolutionSS.value);
                Assert.AreEqual(QualityMode.Low, ssgi.quality.value);

                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Low, false, 1f, false);
                Assert.AreEqual(1, ssgi.sampleCount.value);
                Assert.AreEqual(24, ssgi.maxRaySteps.value);
                Assert.IsFalse(ssgi.fullResolutionSS.value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void DisablingKeepsTheOverrideSoAWorldCannotForceItBackOn()
        {
            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, false, QualityMode.Medium, false, 1f);
                Assert.IsTrue(ssgi.active);
                Assert.IsTrue(ssgi.enable.overrideState);
                Assert.IsFalse(ssgi.enable.value);
                Assert.IsFalse(ssgi.IsActive());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void IntensityIsClampedToTheSliderRange()
        {
            ScreenSpaceGlobalIlluminationVolume ssgi = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
            try
            {
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Medium, false, 99f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_INTENSITY_MAX, ssgi.indirectDiffuseLightingMultiplier.value);
                SMModuleScreenSpaceGlobalIlluminationURP.Apply(ssgi, true, QualityMode.Medium, false, -5f);
                Assert.AreEqual(BasisSettingsDefaults.SSGI_INTENSITY_MIN, ssgi.indirectDiffuseLightingMultiplier.value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void ModuleOwnsOneGlobalVolumeOnTheDefaultLayerThatStartsDisabled()
        {
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();

            module.ApplyOverride();

            Volume volume = module.Volume;
            Assert.IsNotNull(volume);
            Assert.IsTrue(volume.isGlobal);
            Assert.AreEqual(SMModuleScreenSpaceGlobalIlluminationURP.OverridePriority, volume.priority);
            Assert.AreEqual(0, volume.gameObject.layer);
            Assert.AreEqual(host.transform, volume.transform.parent);
            Assert.IsTrue(volume.gameObject.activeSelf);
            Assert.IsTrue(volume.sharedProfile.TryGet(out ScreenSpaceGlobalIlluminationVolume ssgi));
            Assert.AreSame(module.Ssgi, ssgi);
            Assert.IsTrue(ssgi.enable.overrideState);
            Assert.IsFalse(ssgi.enable.value);
            Assert.AreEqual(2, ssgi.sampleCount.value);
            Assert.IsFalse(module.Capturing);

            module.ApplyOverride();
            Assert.AreSame(volume, module.Volume);
            Assert.AreEqual(1, host.transform.childCount);
            Assert.AreEqual(0, module.CameraVolumes.Count);
        }

        [Test]
        public void CameraGateAllowsEverythingWithoutALocalPlayerAndOnlyTheMainCameraWithOne()
        {
            GameObject mainObject = new GameObject("main-camera");
            GameObject otherObject = new GameObject("mirror-camera");
            try
            {
                UnityEngine.Camera main = mainObject.AddComponent<UnityEngine.Camera>();
                UnityEngine.Camera other = otherObject.AddComponent<UnityEngine.Camera>();

                BasisLocalCameraDriver.CameraInstance = null;
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(main));
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(other));

                BasisLocalCameraDriver.CameraInstance = main;
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(main));
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(other));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mainObject);
                UnityEngine.Object.DestroyImmediate(otherObject);
            }
        }

        [Test]
        public void RegisteredCaptureCamerasPassTheGateUntilSuspendedOrUnregistered()
        {
            GameObject mainObject = new GameObject("main-camera");
            GameObject captureObject = new GameObject("capture-camera");
            UnityEngine.Camera capture = captureObject.AddComponent<UnityEngine.Camera>();
            try
            {
                BasisLocalCameraDriver.CameraInstance = mainObject.AddComponent<UnityEngine.Camera>();
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.IsCameraRegistered(capture));
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(capture));

                SMModuleScreenSpaceGlobalIlluminationURP.RegisterCamera(capture);
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.IsCameraRegistered(capture));
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(capture));

                SMModuleScreenSpaceGlobalIlluminationURP.SuspendCamera(capture, true);
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.IsCameraRegistered(capture));
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(capture));

                SMModuleScreenSpaceGlobalIlluminationURP.SuspendCamera(capture, false);
                Assert.IsTrue(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(capture));

                SMModuleScreenSpaceGlobalIlluminationURP.UnregisterCamera(capture);
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.IsCameraRegistered(capture));
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(capture));

                SMModuleScreenSpaceGlobalIlluminationURP.RegisterCamera(null);
                SMModuleScreenSpaceGlobalIlluminationURP.SuspendCamera(null, true);
                SMModuleScreenSpaceGlobalIlluminationURP.UnregisterCamera(null);
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.IsCameraRegistered(null));
                Assert.IsFalse(SMModuleScreenSpaceGlobalIlluminationURP.AcceptsCamera(null));
            }
            finally
            {
                SMModuleScreenSpaceGlobalIlluminationURP.UnregisterCamera(capture);
                UnityEngine.Object.DestroyImmediate(captureObject);
                UnityEngine.Object.DestroyImmediate(mainObject);
            }
        }

        [Test]
        public void UncoveredVolumeLayerIsTheLowestLayerOutsideTheDefaultVolumeMask()
        {
            GameObject captureObject = new GameObject("capture-camera");
            try
            {
                UnityEngine.Camera capture = captureObject.AddComponent<UnityEngine.Camera>();
                Assert.AreEqual(-1, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(null));
                Assert.AreEqual(-1, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));

                UniversalAdditionalCameraData cameraData = captureObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.volumeLayerMask = 1 << 11;
                Assert.AreEqual(11, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));
                cameraData.volumeLayerMask = (1 << 11) | (1 << 5);
                Assert.AreEqual(5, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));
                cameraData.volumeLayerMask = 1;
                Assert.AreEqual(-1, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));
                cameraData.volumeLayerMask = ~0;
                Assert.AreEqual(-1, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));
                cameraData.volumeLayerMask = 0;
                Assert.AreEqual(-1, SMModuleScreenSpaceGlobalIlluminationURP.UncoveredVolumeLayer(capture));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(captureObject);
            }
        }

        [Test]
        public void ModuleCoversARegisteredCamerasVolumeLayerWithTheSameProfile()
        {
            host = new GameObject("ssgi-settings-module");
            SMModuleScreenSpaceGlobalIlluminationURP module = host.AddComponent<SMModuleScreenSpaceGlobalIlluminationURP>();
            GameObject captureObject = new GameObject("capture-camera");
            UnityEngine.Camera capture = captureObject.AddComponent<UnityEngine.Camera>();
            try
            {
                captureObject.AddComponent<UniversalAdditionalCameraData>().volumeLayerMask = 1 << 11;
                SMModuleScreenSpaceGlobalIlluminationURP.RegisterCamera(capture);

                module.ApplyOverride();

                Assert.IsTrue(module.CameraVolumes.TryGetValue(11, out Volume layerVolume));
                Assert.AreEqual(11, layerVolume.gameObject.layer);
                Assert.IsTrue(layerVolume.isGlobal);
                Assert.AreEqual(SMModuleScreenSpaceGlobalIlluminationURP.OverridePriority, layerVolume.priority);
                Assert.AreEqual(host.transform, layerVolume.transform.parent);
                Assert.AreSame(module.Volume.sharedProfile, layerVolume.sharedProfile);
                Assert.AreEqual(2, host.transform.childCount);

                module.ApplyOverride();
                Assert.AreEqual(1, module.CameraVolumes.Count);
                Assert.AreEqual(2, host.transform.childCount);
            }
            finally
            {
                SMModuleScreenSpaceGlobalIlluminationURP.UnregisterCamera(capture);
                UnityEngine.Object.DestroyImmediate(captureObject);
            }
        }
    }
}
#endif
