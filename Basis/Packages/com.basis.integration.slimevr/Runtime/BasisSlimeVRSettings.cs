#if BASIS_FRAMEWORK_EXISTS
using Basis.Scripts.Settings;

namespace Basis.Integration.SlimeVR
{
    /// <summary>
    /// Persistent settings for the SlimeVR integration. Bindings self-load on construction,
    /// so nothing needs registering in the framework's LoadAll.
    /// </summary>
    public static class BasisSlimeVRSettings
    {
        /// <summary>Run the SlimeVR client at all. Connection attempts are cheap while no server is running.</summary>
        public static readonly BasisSettingsBinding<bool> Enable =
            new BasisSettingsBinding<bool>("slimevr_enable", new BasisPlatformDefault<bool>(true));

        /// <summary>Automatically apply SlimeVR's body proportions (eye height + arm span) to the Basis height system.</summary>
        public static readonly BasisSettingsBinding<bool> ApplyBodyMeasurements =
            new BasisSettingsBinding<bool>("slimevr_applybodymeasurements", new BasisPlatformDefault<bool>(true));
    }
}
#endif
