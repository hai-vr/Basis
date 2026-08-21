using Basis.BasisUI;
using Basis.Scripts.Avatar;
using NUnit.Framework;

namespace Basis.Tests.Calibration
{
    /// <summary>
    /// The hand-tracking exclusion list. These devices are published by hand-tracking software as
    /// plain roleless SteamVR trackers, so nothing but their identity strings tells them apart from a
    /// real body tracker — and left in the constellation they steal the LowerArm/Shoulder roles.
    /// Because a name is the only evidence, the exclusion ships off; these tests pin both halves.
    /// </summary>
    public sealed class BasisIgnoredCalibrationTrackerTests
    {
        static readonly string[] KnownHandTrackers = { "HANDL", "HANDR", "VRLINKQ_Hand_Left", "VRLINKQ_Hand_Right" };

        /// <summary>
        /// The whole behaviour hangs off this shipping false. Edit-mode tests never run
        /// BasisSettingsDefaults.LoadAll(), so RawValue here is still the static-init default —
        /// which is exactly the value a fresh install gets.
        /// </summary>
        [Test]
        public void Exclusion_IsOffByDefault()
        {
            Assert.IsFalse(BasisSettingsDefaults.IgnoreHandTrackingDevices.RawValue, "hand-tracking exclusion must ship off");
        }

        [Test]
        public void NullInput_IsNeverIgnored()
        {
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.ShouldIgnore(null));
        }

        [Test]
        public void KnownHandTrackers_Match_BySerial()
        {
            foreach (string name in KnownHandTrackers)
            {
                Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(name, string.Empty, string.Empty), $"serial '{name}' should match");
            }
        }

        [Test]
        public void KnownHandTrackers_Match_ByRenderModelName()
        {
            foreach (string name in KnownHandTrackers)
            {
                Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(string.Empty, name, string.Empty), $"common identifier '{name}' should match");
            }
        }

        /// <summary>OpenVR's unique id carries a session-volatile device index: "{index}|{name}".</summary>
        [Test]
        public void KnownHandTrackers_Match_ByUniqueIdentifierWithDeviceIndex()
        {
            foreach (string name in KnownHandTrackers)
            {
                Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(string.Empty, string.Empty, $"7|{name}"), $"unique identifier '7|{name}' should match");
            }
        }

        [Test]
        public void Matching_IsCaseInsensitive_AndTrimsWhitespace()
        {
            Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("handl", string.Empty, string.Empty));
            Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("vrlinkq_hand_right", string.Empty, string.Empty));
            Assert.IsTrue(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(" HANDR ", string.Empty, string.Empty));
        }

        [Test]
        public void RealTrackers_DoNotMatch()
        {
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("LHR-1A2B3C4D", "vr_tracker_vive_3_0", "4|vr_tracker_vive_3_0"));
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("human://WAIST", string.Empty, "9|human://WAIST"));
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("HANDLE", string.Empty, string.Empty), "partial matches must not count");
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName("VRLINKQ_Hand_Left_Extra", string.Empty, string.Empty), "partial matches must not count");
        }

        [Test]
        public void MissingIdentifiers_DoNotMatch()
        {
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(null, null, null));
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(string.Empty, string.Empty, string.Empty));
            Assert.IsFalse(BasisIgnoredCalibrationTrackers.MatchesIgnoredName(null, null, "3|"));
        }
    }
}
