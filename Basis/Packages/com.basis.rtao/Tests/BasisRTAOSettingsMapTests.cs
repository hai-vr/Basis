using NUnit.Framework;

namespace Basis.Rendering.RTAO.Tests
{
    public sealed class BasisRTAOSettingsMapTests
    {
        [Test]
        public void ModeReadsTheDropdownEntries()
        {
            Assert.AreEqual(BasisRTAOTracingMode.Auto, BasisRTAOSettingsMap.ReadMode("Auto"));
            Assert.AreEqual(BasisRTAOTracingMode.RayTracedOnly, BasisRTAOSettingsMap.ReadMode("Ray Traced"));
            Assert.AreEqual(BasisRTAOTracingMode.ScreenSpace, BasisRTAOSettingsMap.ReadMode("Screen Space"));
        }

        [Test]
        public void ModeSurvivesTheLowercasingTheSettingsSystemApplies()
        {
            Assert.AreEqual(BasisRTAOTracingMode.RayTracedOnly, BasisRTAOSettingsMap.ReadMode("ray traced"),
                "Values reach the modules already lowercased, so the parser has to accept that form.");
            Assert.AreEqual(BasisRTAOTracingMode.ScreenSpace, BasisRTAOSettingsMap.ReadMode("screen space"));
            Assert.AreEqual(BasisRTAOTracingMode.ScreenSpace, BasisRTAOSettingsMap.ReadMode("  SCREEN SPACE  "));
        }

        [Test]
        public void UnknownModeFallsBackToAuto()
        {
            Assert.AreEqual(BasisRTAOTracingMode.Auto, BasisRTAOSettingsMap.ReadMode(null));
            Assert.AreEqual(BasisRTAOTracingMode.Auto, BasisRTAOSettingsMap.ReadMode(string.Empty));
            Assert.AreEqual(BasisRTAOTracingMode.Auto, BasisRTAOSettingsMap.ReadMode("nonsense"),
                "A stale settings file must not turn the effect off, it must land on the safe default.");
        }

        [Test]
        public void QualityReadsEveryTier()
        {
            Assert.AreEqual(BasisRTAOQuality.Low, BasisRTAOSettingsMap.ReadQuality("Low"));
            Assert.AreEqual(BasisRTAOQuality.Medium, BasisRTAOSettingsMap.ReadQuality("Medium"));
            Assert.AreEqual(BasisRTAOQuality.High, BasisRTAOSettingsMap.ReadQuality("high"));
            Assert.AreEqual(BasisRTAOQuality.Ultra, BasisRTAOSettingsMap.ReadQuality("ULTRA"));
            Assert.AreEqual(BasisRTAOQuality.Medium, BasisRTAOSettingsMap.ReadQuality("who knows"));
        }

        [Test]
        public void SkinnedModeReadsEveryEntry()
        {
            Assert.AreEqual(BasisRTAOSkinnedMode.Off, BasisRTAOSettingsMap.ReadSkinnedMode("Off"));
            Assert.AreEqual(BasisRTAOSkinnedMode.Static, BasisRTAOSettingsMap.ReadSkinnedMode("static"));
            Assert.AreEqual(BasisRTAOSkinnedMode.Dynamic, BasisRTAOSettingsMap.ReadSkinnedMode("Dynamic"));
        }

        [Test]
        public void UnknownSkinnedModeStaysOff()
        {
            Assert.AreEqual(BasisRTAOSkinnedMode.Off, BasisRTAOSettingsMap.ReadSkinnedMode("everything"),
                "Baking skinned meshes costs CPU every frame, so an unreadable value must not opt the player in.");
            Assert.AreEqual(BasisRTAOSkinnedMode.Off, BasisRTAOSettingsMap.ReadSkinnedMode(null));
        }

        [Test]
        public void EveryModeRoundTripsThroughTheDropdownString()
        {
            foreach (BasisRTAOTracingMode mode in new[] { BasisRTAOTracingMode.Auto, BasisRTAOTracingMode.RayTracedOnly, BasisRTAOTracingMode.ScreenSpace })
                Assert.AreEqual(mode, BasisRTAOSettingsMap.ReadMode(BasisRTAOSettingsMap.WriteMode(mode)));
        }

        [Test]
        public void EveryQualityRoundTripsThroughTheDropdownString()
        {
            foreach (BasisRTAOQuality quality in System.Enum.GetValues(typeof(BasisRTAOQuality)))
                Assert.AreEqual(quality, BasisRTAOSettingsMap.ReadQuality(BasisRTAOSettingsMap.WriteQuality(quality)));
        }

        [Test]
        public void EverySkinnedModeRoundTripsThroughTheDropdownString()
        {
            foreach (BasisRTAOSkinnedMode mode in System.Enum.GetValues(typeof(BasisRTAOSkinnedMode)))
                Assert.AreEqual(mode, BasisRTAOSettingsMap.ReadSkinnedMode(BasisRTAOSettingsMap.WriteSkinnedMode(mode)));
        }

        [Test]
        public void WrittenStringsMatchTheDropdownEntriesTheUiRegisters()
        {
            Assert.AreEqual("Auto", BasisRTAOSettingsMap.WriteMode(BasisRTAOTracingMode.Auto));
            Assert.AreEqual("Ray Traced", BasisRTAOSettingsMap.WriteMode(BasisRTAOTracingMode.RayTracedOnly));
            Assert.AreEqual("Screen Space", BasisRTAOSettingsMap.WriteMode(BasisRTAOTracingMode.ScreenSpace));
            Assert.AreEqual("Off", BasisRTAOSettingsMap.WriteSkinnedMode(BasisRTAOSkinnedMode.Off));
            Assert.AreEqual("Dynamic", BasisRTAOSettingsMap.WriteSkinnedMode(BasisRTAOSkinnedMode.Dynamic));
        }

        [Test]
        public void DenoiseDropdownMapsToPassCounts()
        {
            Assert.AreEqual(0, BasisRTAOSettingsMap.ReadDenoisePasses("Off"));
            Assert.AreEqual(1, BasisRTAOSettingsMap.ReadDenoisePasses("Standard"));
            Assert.AreEqual(2, BasisRTAOSettingsMap.ReadDenoisePasses("High"));
            Assert.AreEqual(3, BasisRTAOSettingsMap.ReadDenoisePasses("Maximum"));
        }

        [Test]
        public void DenoiseDropdownSurvivesLowercasingAndNonsense()
        {
            Assert.AreEqual(0, BasisRTAOSettingsMap.ReadDenoisePasses("off"));
            Assert.AreEqual(3, BasisRTAOSettingsMap.ReadDenoisePasses("  MAXIMUM "));
            Assert.AreEqual(2, BasisRTAOSettingsMap.ReadDenoisePasses("who knows"),
                "An unreadable value must land on the shipping default, not on Off.");
        }

        [Test]
        public void EveryDenoiseLevelRoundTrips()
        {
            for (int passes = 0; passes <= 3; passes++)
                Assert.AreEqual(passes, BasisRTAOSettingsMap.ReadDenoisePasses(BasisRTAOSettingsMap.WriteDenoisePasses(passes)));
        }

        [Test]
        public void ApplyModeMapsToTheDropdown()
        {
            Assert.AreEqual(BasisRTAOApplyMode.Lighting, BasisRTAOSettingsMap.ReadApplyMode("Lighting"));
            Assert.AreEqual(BasisRTAOApplyMode.AfterOpaque, BasisRTAOSettingsMap.ReadApplyMode("Final Image"));
            Assert.AreEqual(BasisRTAOApplyMode.AfterOpaque, BasisRTAOSettingsMap.ReadApplyMode("after opaque"),
                "URP calls this After Opaque, so a settings file written against that name must still read.");
            Assert.AreEqual(BasisRTAOApplyMode.Lighting, BasisRTAOSettingsMap.ReadApplyMode("nonsense"));
        }

        [Test]
        public void EveryApplyModeRoundTrips()
        {
            foreach (BasisRTAOApplyMode mode in System.Enum.GetValues(typeof(BasisRTAOApplyMode)))
                Assert.AreEqual(mode, BasisRTAOSettingsMap.ReadApplyMode(BasisRTAOSettingsMap.WriteApplyMode(mode)));
        }

        [Test]
        public void ScreenSpaceSettingResolvesToTheFallbackOnARayTracingGpu()
        {
            BasisRTAOTracingMode mode = BasisRTAOSettingsMap.ReadMode("Screen Space");
            Assert.AreEqual(BasisRTAOBackend.ScreenSpace, BasisRTAOTracing.Resolve(mode, true, true),
                "Choosing Screen Space in the settings must be honoured even when the GPU could trace.");
        }

        [Test]
        public void AutoSettingResolvesToTheFallbackWithoutRayTracing()
        {
            BasisRTAOTracingMode mode = BasisRTAOSettingsMap.ReadMode("Auto");
            Assert.AreEqual(BasisRTAOBackend.ScreenSpace, BasisRTAOTracing.Resolve(mode, false, true));
        }
    }
}
