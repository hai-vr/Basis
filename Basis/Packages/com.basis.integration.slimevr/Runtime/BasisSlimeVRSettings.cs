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

        /// <summary>
        /// SlimeVR trackers announce their body part in their serial and bind to it automatically
        /// (registered with the framework's BasisAnnouncedTrackerRoles scanner). Switch off to run
        /// them through the normal manual/geometric calibration instead.
        /// </summary>
        public static readonly BasisSettingsBinding<bool> AutoBindSlimeVRTrackers =
            new BasisSettingsBinding<bool>("slimevr_autobind", new BasisPlatformDefault<bool>(true));

        public const string TransportWebSocket = "websocket";
        public const string TransportPipe = "pipe";

        /// <summary>
        /// How to talk to the SlimeVR server: "websocket" (works on every released server today)
        /// or "pipe" (SlimeVR's native transport — Windows named pipe / unix socket — the one that
        /// stays once websockets are deprecated, but it needs a server whose pipe bridge works).
        /// </summary>
        public static readonly BasisSettingsBinding<string> Transport =
            new BasisSettingsBinding<string>("slimevr_transport", new BasisPlatformDefault<string>(TransportWebSocket));
    }
}
#endif
