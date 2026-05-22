using Basis.Scripts.Settings;

namespace Basis.MediaPipe
{
    /// <summary>Persistent settings for webcam tracking, surfaced in the Settings "Webcam Tracking" tab.</summary>
    public static class BasisMediaPipeSettings
    {
        public static readonly BasisSettingsBinding<bool> Enable =
            new BasisSettingsBinding<bool>("mediapipe_enable", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<string> Camera =
            new BasisSettingsBinding<string>("mediapipe_camera", new BasisPlatformDefault<string>(string.Empty));

        public static readonly BasisSettingsBinding<bool> EnableFace =
            new BasisSettingsBinding<bool>("mediapipe_face", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> EnableHands =
            new BasisSettingsBinding<bool>("mediapipe_hands", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> Mirror =
            new BasisSettingsBinding<bool>("mediapipe_mirror", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> EnableHead =
            new BasisSettingsBinding<bool>("mediapipe_head", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> EnableHandTracking =
            new BasisSettingsBinding<bool>("mediapipe_handtracking", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> SwapHands =
            new BasisSettingsBinding<bool>("mediapipe_swaphands", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertBlink =
            new BasisSettingsBinding<bool>("mediapipe_invertblink", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertHeadYaw =
            new BasisSettingsBinding<bool>("mediapipe_invertheadyaw", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertHeadPitch =
            new BasisSettingsBinding<bool>("mediapipe_invertheadpitch", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<float> HeadSmoothing =
            new BasisSettingsBinding<float>("mediapipe_headsmoothing", new BasisPlatformDefault<float>(0.5f));

        public static readonly BasisSettingsBinding<float> FaceSmoothing =
            new BasisSettingsBinding<float>("mediapipe_facesmoothing", new BasisPlatformDefault<float>(0.5f));

        public static readonly BasisSettingsBinding<float> HandSmoothing =
            new BasisSettingsBinding<float>("mediapipe_handsmoothing", new BasisPlatformDefault<float>(0.5f));

        public static readonly BasisSettingsBinding<float> FingerSmoothing =
            new BasisSettingsBinding<float>("mediapipe_fingersmoothing", new BasisPlatformDefault<float>(0.5f));
    }
}
