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

        public static readonly BasisSettingsBinding<bool> EnableBody =
            new BasisSettingsBinding<bool>("mediapipe_body", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> SwapHands =
            new BasisSettingsBinding<bool>("mediapipe_swaphands", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertBlink =
            new BasisSettingsBinding<bool>("mediapipe_invertblink", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertHeadYaw =
            new BasisSettingsBinding<bool>("mediapipe_invertheadyaw", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> InvertHeadPitch =
            new BasisSettingsBinding<bool>("mediapipe_invertheadpitch", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> InvertHeadRoll =
            new BasisSettingsBinding<bool>("mediapipe_invertheadroll", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<float> HeadSmoothing =
            new BasisSettingsBinding<float>("mediapipe_headsmoothing_v2", new BasisPlatformDefault<float>(0.8f));

        public static readonly BasisSettingsBinding<float> FaceSmoothing =
            new BasisSettingsBinding<float>("mediapipe_facesmoothing_v2", new BasisPlatformDefault<float>(0.8f));

        public static readonly BasisSettingsBinding<float> HandSmoothing =
            new BasisSettingsBinding<float>("mediapipe_handsmoothing_v2", new BasisPlatformDefault<float>(0.8f));

        public static readonly BasisSettingsBinding<float> FingerSmoothing =
            new BasisSettingsBinding<float>("mediapipe_fingersmoothing_v2", new BasisPlatformDefault<float>(0.8f));

        public static readonly BasisSettingsBinding<bool> HandRotation =
            new BasisSettingsBinding<bool>("mediapipe_handrotation", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<float> HeadPositionStrength =
            new BasisSettingsBinding<float>("mediapipe_headpositionstrength_v2", new BasisPlatformDefault<float>(0.6f));

        public static readonly BasisSettingsBinding<float> HeadRotationStrength =
            new BasisSettingsBinding<float>("mediapipe_headrotationstrength_v2", new BasisPlatformDefault<float>(0.6f));

        public static readonly BasisSettingsBinding<int> ResolutionWidth =
            new BasisSettingsBinding<int>("mediapipe_reswidth", new BasisPlatformDefault<int>(640));

        public static readonly BasisSettingsBinding<int> ResolutionHeight =
            new BasisSettingsBinding<int>("mediapipe_resheight", new BasisPlatformDefault<int>(480));

        public static readonly BasisSettingsBinding<int> CameraFps =
            new BasisSettingsBinding<int>("mediapipe_camerafps", new BasisPlatformDefault<int>(30));

        /// <summary>
        /// Re-reads every binding from the loaded settings dictionary. Must run after
        /// BasisSettingsSystem has loaded from disk (it replaces the dictionary), mirroring
        /// BasisSettingsDefaults.LoadAll. Otherwise bindings keep their construction-time defaults.
        /// </summary>
        public static void LoadAll()
        {
            Enable.LoadBindingValue();
            Camera.LoadBindingValue();
            EnableFace.LoadBindingValue();
            EnableHands.LoadBindingValue();
            EnableHead.LoadBindingValue();
            EnableHandTracking.LoadBindingValue();
            EnableBody.LoadBindingValue();
            SwapHands.LoadBindingValue();
            Mirror.LoadBindingValue();
            InvertBlink.LoadBindingValue();
            InvertHeadYaw.LoadBindingValue();
            InvertHeadPitch.LoadBindingValue();
            InvertHeadRoll.LoadBindingValue();
            HeadSmoothing.LoadBindingValue();
            FaceSmoothing.LoadBindingValue();
            HandSmoothing.LoadBindingValue();
            FingerSmoothing.LoadBindingValue();
            HandRotation.LoadBindingValue();
            HeadPositionStrength.LoadBindingValue();
            HeadRotationStrength.LoadBindingValue();
            ResolutionWidth.LoadBindingValue();
            ResolutionHeight.LoadBindingValue();
            CameraFps.LoadBindingValue();
        }
    }
}
