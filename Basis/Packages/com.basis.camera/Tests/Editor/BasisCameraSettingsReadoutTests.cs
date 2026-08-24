using NUnit.Framework;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

namespace Basis.Tests.Camera
{
    /// <summary>The settings readout: every value the camera holds, as text on the Preset tab.</summary>
    public class BasisCameraSettingsReadoutTests
    {
        [Test]
        public void TheReadoutShowsEverySectionAndResolvesItsIndexes()
        {
            CameraSettings settings = BasisCameraSettingsRig.DistinctiveSettings();
            BasisHandHeldCameraMetaData metaData = new BasisHandHeldCameraMetaData();

            string text = BasisCameraSettingsReadout.Build(
                settings,
                (int)BasisHandHeldCameraInteractable.CameraPinSpace.WorldSpace,
                metaData);

            Assert.That(text, Is.Not.Null.And.Not.Empty);

            // The values that reach the camera as an index into a preset table are the ones a raw
            // readout would render meaningless.
            Assert.That(text, Does.Contain(metaData.apertures[settings.apertureIndex]));
            Assert.That(text, Does.Contain(metaData.shutterSpeeds[settings.shutterSpeedIndex]));
            Assert.That(text, Does.Contain(metaData.isoValues[settings.isoIndex]));
            Assert.That(text, Does.Contain(
                $"{metaData.resolutions[settings.resolutionIndex].width} x {metaData.resolutions[settings.resolutionIndex].height}"));

            // A value from each of the settings groups, so a group dropped from the readout fails
            // here rather than going unnoticed until someone reads the page.
            Assert.That(text, Does.Contain("77"), "the field of view is missing");
            Assert.That(text, Does.Contain("0.35"), "the vignette is missing");
            Assert.That(text, Does.Contain("0.8"), "the framing radius is missing");
            Assert.That(text.Split('\n').Length, Is.GreaterThan(40),
                "the readout is meant to be every setting, not a summary");
        }

        [Test]
        public void TheReadoutSurvivesACameraWithNoPresetTables()
        {
            Assert.That(
                BasisCameraSettingsReadout.Build(BasisCameraSettingsRig.DistinctiveSettings(), 0, null),
                Is.Not.Null.And.Not.Empty);

            Assert.That(
                BasisCameraSettingsReadout.Build(null, 0, null),
                Is.Empty);
        }
    }
}
