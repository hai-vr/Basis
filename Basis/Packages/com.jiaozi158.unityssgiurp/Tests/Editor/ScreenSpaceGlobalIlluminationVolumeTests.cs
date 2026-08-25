using NUnit.Framework;
using UnityEngine;
using QualityMode = ScreenSpaceGlobalIlluminationVolume.QualityMode;
using RayMiss = ScreenSpaceGlobalIlluminationVolume.RayMarchingFallbackHierarchy;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationVolumeTests
    {
        private ScreenSpaceGlobalIlluminationVolume volume;

        [SetUp]
        public void SetUp()
        {
            volume = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationVolume>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(volume);
        }

        [Test]
        public void IsOffByDefault()
        {
            Assert.IsFalse(volume.enable.value);
            Assert.IsFalse(volume.IsActive());
        }

        [Test]
        public void EnableMakesItActive()
        {
            volume.enable.value = true;
            Assert.IsTrue(volume.IsActive());
        }

#if UNITY_2023_3_OR_NEWER
        [Test]
        public void NoRenderingLayersMeansInactiveEvenWhenEnabled()
        {
            volume.enable.value = true;
            volume.indirectDiffuseRenderingLayers.value = 0;
            Assert.IsFalse(volume.IsActive());
        }

        [Test]
        public void RenderingLayersDefaultToEverything()
        {
            Assert.AreEqual(uint.MaxValue, volume.indirectDiffuseRenderingLayers.value.value);
        }
#endif

        [TestCase(RayMiss.None, false, false)]
        [TestCase(RayMiss.Sky, true, false)]
        [TestCase(RayMiss.ReflectionProbes, false, true)]
        [TestCase(RayMiss.ReflectionProbesAndSky, true, true)]
        public void RayMissHierarchyFlags(RayMiss rayMiss, bool expectSky, bool expectProbes)
        {
            volume.rayMiss.value = rayMiss;
            Assert.AreEqual(expectSky, volume.IsFallbackSky());
            Assert.AreEqual(expectProbes, volume.IsFallbackReflectionProbes());
        }

        [Test]
        public void RayMissDefaultsToProbesThenSky()
        {
            Assert.AreEqual(RayMiss.ReflectionProbesAndSky, volume.rayMiss.value);
        }

        [TestCase(QualityMode.Low, 1, 24)]
        [TestCase(QualityMode.Medium, 2, 32)]
        [TestCase(QualityMode.High, 4, 64)]
        public void QualityPresetsSetSampleAndStepCounts(QualityMode mode, int expectedSamples, int expectedSteps)
        {
            volume.qualityMode = new ScreenSpaceGlobalIlluminationVolume.RayMarchingModeParameter(mode, true);
            Assert.AreEqual(expectedSamples, volume.sampleCount.value);
            Assert.AreEqual(expectedSteps, volume.maxRaySteps.value);
        }

        [Test]
        public void CustomQualityKeepsHandTunedCounts()
        {
            volume.sampleCount.value = 7;
            volume.maxRaySteps.value = 100;
            volume.qualityMode = new ScreenSpaceGlobalIlluminationVolume.RayMarchingModeParameter(QualityMode.Custom, true);
            Assert.AreEqual(7, volume.sampleCount.value);
            Assert.AreEqual(100, volume.maxRaySteps.value);
        }

        [Test]
        public void DefaultCountsMatchTheMediumPreset()
        {
            Assert.AreEqual(2, volume.sampleCount.value);
            Assert.AreEqual(32, volume.maxRaySteps.value);
        }

        [Test]
        public void SampleCountIsClampedToSixteen()
        {
            volume.sampleCount.value = 64;
            Assert.AreEqual(16, volume.sampleCount.value);
            volume.sampleCount.value = 0;
            Assert.AreEqual(1, volume.sampleCount.value);
        }

        [Test]
        public void MaxRayStepsHasAFloorOfSixteen()
        {
            volume.maxRaySteps.value = 4;
            Assert.AreEqual(16, volume.maxRaySteps.value);
        }

        [Test]
        public void ResolutionScaleIsClampedBetweenQuarterAndThreeQuarters()
        {
            volume.resolutionScaleSS.value = 1f;
            Assert.AreEqual(0.75f, volume.resolutionScaleSS.value);
            volume.resolutionScaleSS.value = 0f;
            Assert.AreEqual(0.25f, volume.resolutionScaleSS.value);
        }

        [Test]
        public void HalfResolutionIsTheDefault()
        {
            Assert.IsFalse(volume.fullResolutionSS.value);
            Assert.AreEqual(0.5f, volume.resolutionScaleSS.value);
        }

        [Test]
        public void DenoiserDefaults()
        {
            Assert.IsTrue(volume.denoiseSS.value);
            Assert.IsTrue(volume.secondDenoiserPassSS.value);
            Assert.AreEqual(ScreenSpaceGlobalIlluminationVolume.DenoiserAlgorithm.Aggressive, volume.denoiserAlgorithmSS.value);
            Assert.AreEqual(0.95f, volume.denoiseIntensitySS.value, 1e-6f);
            Assert.AreEqual(0.6f, volume.denoiserRadiusSS.value, 1e-6f);
        }

        [Test]
        public void IndirectMultiplierCannotGoNegative()
        {
            volume.indirectDiffuseLightingMultiplier.value = -3f;
            Assert.AreEqual(0f, volume.indirectDiffuseLightingMultiplier.value);
        }

        [Test]
        public void DepthBufferThicknessIsClamped()
        {
            volume.depthBufferThickness.value = 5f;
            Assert.AreEqual(0.5f, volume.depthBufferThickness.value);
        }
    }
}
