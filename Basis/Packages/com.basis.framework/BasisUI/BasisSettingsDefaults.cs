namespace Basis.BasisUI
{
    public static class BasisSettingsDefaults
    {

        public static BasisSettingsBinding<float> MainVolume =>
            new("main volume", new BasisPlatformDefault<float>(75));

        public static BasisSettingsBinding<float> MenuVolume =>
            new("menu volume", new BasisPlatformDefault<float>(75));

        public static BasisSettingsBinding<float> WorldVolume =>
            new("world volume", new BasisPlatformDefault<float>(75));

        public static BasisSettingsBinding<float> PlayerVolume =>
            new("player volume", new BasisPlatformDefault<float>(75));

        public static BasisSettingsBinding<float> MicrophoneVolume =>
            new("Microphone Volume", new BasisPlatformDefault<float>(1));

        public static BasisSettingsBinding<float> ControllerDeadZone =>
            new("joystickdeadzone", new BasisPlatformDefault<float>(0.01f));
        public static BasisSettingsBinding<float> Basexdeadzone =>
    new("basexdeadzone", new BasisPlatformDefault<float>(0.08f));

        public static BasisSettingsBinding<float> Extraxdeadzoneatfully =>
    new("extraxdeadzoneatfully", new BasisPlatformDefault<float>(0.35f));

        public static BasisSettingsBinding<float> Ydeadzone =>
    new("ydeadzone", new BasisPlatformDefault<float>(0.10f));

        public static BasisSettingsBinding<float> Wingexponent =>
new("wingexponent", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> MicrophoneRange =>
            new("MicrophoneRange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> HearingRange =>
            new("HearingRange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> SelectedHeight =>
    new("SelectedHeight", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> SelectedScale =>
new("selected scale", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> realworldeyeheight =>
new("real world eye height", new BasisPlatformDefault<float>(1.61f));
        public static BasisSettingsBinding<bool> CustomScale =>
new("custom scale", new BasisPlatformDefault<bool>(false));
        public static BasisSettingsBinding<float> AvatarRange =>
            new("AvatarRange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> SnapTurnAngle =>
            new("snapturnangle", new BasisPlatformDefault<float>(25f));

        public static BasisSettingsBinding<float> mousesensitivty =>
    new("mousesensitivty", new BasisPlatformDefault<float>(1));

        public static BasisSettingsBinding<bool> InvertMouse =>
            new("invertmouse", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> usesnapturn =>
    new("usesnapturn", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<string> QualityLevel =>
            new("Quality Level", new BasisPlatformDefault<string>
            {
                windows = "Ultra",
                android = "Very Low",
                linux = "Ultra",
                other = "Ultra"
            });

        public static BasisSettingsBinding<string> ShadowQuality =>
            new("Shadow Quality", new BasisPlatformDefault<string>
            {
                windows = "Ultra",
                android = "Very Low",
                linux = "Ultra",
                other = "Ultra"
            });

        public static BasisSettingsBinding<string> HDRSupport =>
            new("HDR Support", new BasisPlatformDefault<string>
            {
                windows = "64bit",
                android = "off",
                linux = "64bit",
                other = "64bit"
            });

        public static BasisSettingsBinding<bool> MicrophoneDenoiser =>
            new("voicedenoiser", new BasisPlatformDefault<bool>
            {
                windows = true,
                android = false,
                linux = false,
                other = false
            });

        public static BasisSettingsBinding<string> Antialiasing =>
            new("Antialiasing", new BasisPlatformDefault<string>("MSAA 2X"));

        public static BasisSettingsBinding<bool> DebugVisuals =>
            new("Debug Visuals", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<string> MemoryAllocation =>
            new("Memory Allocation", new BasisPlatformDefault<string>
            {
                windows = "Dynamic",
                android = "Dynamic",
                linux = "Dynamic",
                other = "Dynamic"
            });

        public static BasisSettingsBinding<string> MicrophoneIcon =>
            new("Microphone Icon", new BasisPlatformDefault<string>("AlwaysVisible"));

        public static BasisSettingsBinding<string> VisualState =>
            new("Visual State", new BasisPlatformDefault<string>("Off"));

        public static BasisSettingsBinding<string> IKMode =>
    new("Ik Mode", new BasisPlatformDefault<string>("Eye Height"));

        public static BasisSettingsBinding<string> SelectedBone =>
new("SelectedBone", new BasisPlatformDefault<string>("SelectedBone"));

        public static BasisSettingsBinding<float> FoveatedRendering =>
            new("Foveated Rendering", new BasisPlatformDefault<float>
            {
                windows = 0,
                android = 1,
                linux = 0,
                other = 0,
                ios = 0,
            });

        public static BasisSettingsBinding<float> FieldOfView =>
            new("Field Of View", new BasisPlatformDefault<float>(65));

        public const float FOV_MIN = 50;
        public const float FOV_MAX = 120;

        public static BasisSettingsBinding<float> AvatarScale =>
    new("Scale Of Avatar", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> AvatarDownloadSize =>
new("Avatar Download Size", new BasisPlatformDefault<float>(256));

        public static BasisSettingsBinding<float> AvatarMeshLOD =>
            new("avatarmeshlod", new BasisPlatformDefault<float>
            {
                windows = 0.05f,
                android = 0.1f,
                linux = 0.05f,
                other = 0.05f
            });

        public static BasisSettingsBinding<float> GlobalMeshLOD =>
            new("global meshlod", new BasisPlatformDefault<float>
            {
                windows = 0,
                android = 30,
                linux = 0,
                other = 0
            });

        public static BasisSettingsBinding<string> SeatedMode =>
            new("Seated Mode", new BasisPlatformDefault<string>("Standing Mode"));

        public static BasisSettingsBinding<string> VSync =>
            new("Vertical Sync", new BasisPlatformDefault<string>
            {
                windows = "On",
                android = "On",
                linux = "Capped",
                other = "On"
            });


        public static BasisSettingsBinding<float> RenderResolution => new("Render Resolution", new BasisPlatformDefault<float>(1));

        public static BasisSettingsBinding<string> MicrophoneMode =>
            new("microphonemode", new BasisPlatformDefault<string>("OnActivation"));

        public static BasisSettingsBinding<bool> UseAutomaticGain =>
            new("AGC", new BasisPlatformDefault<bool>
            {
                windows = true,
                android = false,
                linux = false,
                other = false
            });
        public static BasisSettingsBinding<bool> FalseBinding =>
new("FalseBinding", new BasisPlatformDefault<bool>(false));
        public static BasisSettingsBinding<bool> TrueBinding =>
new("TrueBinding", new BasisPlatformDefault<bool>(false));
        // ---------------- GLOBAL ONE EURO PARAMS ----------------
        public static BasisSettingsBinding<float> FBIKMinCutoff =>
            new("FBIKMinCutoff", new BasisPlatformDefault<float>(5.5f));

        public static BasisSettingsBinding<float> FBIKBeta =>
            new("FBIKBeta", new BasisPlatformDefault<float>(3.25f));

        public static BasisSettingsBinding<float> FBIKDerivativeCutoff =>
            new("FBIKDerivativeCutoff", new BasisPlatformDefault<float>(3f));

        // Fallback smoothing (when Smooth* is true but Euro* is false)
        public static BasisSettingsBinding<float> FBIKPositionSmoothingHz =>
            new("FBIKPositionSmoothingHz", new BasisPlatformDefault<float>(20f));

        public static BasisSettingsBinding<float> FBIKRotationSmoothingHz =>
            new("FBIKRotationSmoothingHz", new BasisPlatformDefault<float>(25f));


        // ---------------- HIPS ----------------
        public static BasisSettingsBinding<bool> FBIKHipsSmoothPos =>
            new("FBIKHipsSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHipsSmoothRot =>
            new("FBIKHipsSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHipsEuroPos =>
            new("FBIKHipsEuroPos", new BasisPlatformDefault<bool>(true));

        public static BasisSettingsBinding<bool> FBIKHipsEuroRot =>
            new("FBIKHipsEuroRot", new BasisPlatformDefault<bool>(true));


        // ---------------- HEAD ----------------
        public static BasisSettingsBinding<bool> FBIKHeadSmoothPos =>
            new("FBIKHeadSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadSmoothRot =>
            new("FBIKHeadSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadEuroPos =>
            new("FBIKHeadEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadEuroRot =>
            new("FBIKHeadEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT FOOT ----------------
        public static BasisSettingsBinding<bool> FBIKLeftFootSmoothPos =>
            new("FBIKLeftFootSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootSmoothRot =>
            new("FBIKLeftFootSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootEuroPos =>
            new("FBIKLeftFootEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootEuroRot =>
            new("FBIKLeftFootEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT FOOT ----------------
        public static BasisSettingsBinding<bool> FBIKRightFootSmoothPos =>
            new("FBIKRightFootSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootSmoothRot =>
            new("FBIKRightFootSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootEuroPos =>
            new("FBIKRightFootEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootEuroRot =>
            new("FBIKRightFootEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- CHEST ----------------
        public static BasisSettingsBinding<bool> FBIKChestSmoothPos =>
            new("FBIKChestSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestSmoothRot =>
            new("FBIKChestSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestEuroPos =>
            new("FBIKChestEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestEuroRot =>
            new("FBIKChestEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT LOWER LEG (hint) ----------------
        public static BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothPos =>
            new("FBIKLeftLowerLegSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothRot =>
            new("FBIKLeftLowerLegSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegEuroPos =>
            new("FBIKLeftLowerLegEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegEuroRot =>
            new("FBIKLeftLowerLegEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT LOWER LEG (hint) ----------------
        public static BasisSettingsBinding<bool> FBIKRightLowerLegSmoothPos =>
            new("FBIKRightLowerLegSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegSmoothRot =>
            new("FBIKRightLowerLegSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegEuroPos =>
            new("FBIKRightLowerLegEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegEuroRot =>
            new("FBIKRightLowerLegEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT HAND ----------------
        // Default choice: smooth rotation, but leave position raw by default (hands often feel "laggy" when position-smoothed).
        public static BasisSettingsBinding<bool> FBIKLeftHandSmoothPos =>
            new("FBIKLeftHandSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandSmoothRot =>
            new("FBIKLeftHandSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandEuroPos =>
            new("FBIKLeftHandEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandEuroRot =>
            new("FBIKLeftHandEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT HAND ----------------
        public static BasisSettingsBinding<bool> FBIKRightHandSmoothPos =>
            new("FBIKRightHandSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandSmoothRot =>
            new("FBIKRightHandSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandEuroPos =>
            new("FBIKRightHandEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandEuroRot =>
            new("FBIKRightHandEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT LOWER ARM (hand hint) ----------------
        public static BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothPos =>
            new("FBIKLeftLowerArmSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothRot =>
            new("FBIKLeftLowerArmSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmEuroPos =>
            new("FBIKLeftLowerArmEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmEuroRot =>
            new("FBIKLeftLowerArmEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT LOWER ARM (hand hint) ----------------
        public static BasisSettingsBinding<bool> FBIKRightLowerArmSmoothPos =>
            new("FBIKRightLowerArmSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmSmoothRot =>
            new("FBIKRightLowerArmSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmEuroPos =>
            new("FBIKRightLowerArmEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmEuroRot =>
            new("FBIKRightLowerArmEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT TOE ----------------
        // Toes: usually noisy / low value; keep off by default unless you have solid toe trackers.
        public static BasisSettingsBinding<bool> FBIKLeftToeSmoothPos =>
            new("FBIKLeftToeSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeSmoothRot =>
            new("FBIKLeftToeSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeEuroPos =>
            new("FBIKLeftToeEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeEuroRot =>
            new("FBIKLeftToeEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT TOE ----------------
        public static BasisSettingsBinding<bool> FBIKRightToeSmoothPos =>
            new("FBIKRightToeSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeSmoothRot =>
            new("FBIKRightToeSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeEuroPos =>
            new("FBIKRightToeEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeEuroRot =>
            new("FBIKRightToeEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- LEFT SHOULDER ----------------
        // Your current solve uses shoulders as rotation-only targets; defaults reflect that.
        public static BasisSettingsBinding<bool> FBIKLeftShoulderSmoothPos =>
            new("FBIKLeftShoulderSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderSmoothRot =>
            new("FBIKLeftShoulderSmoothRot", new BasisPlatformDefault<bool>(true));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderEuroPos =>
            new("FBIKLeftShoulderEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderEuroRot =>
            new("FBIKLeftShoulderEuroRot", new BasisPlatformDefault<bool>(false));


        // ---------------- RIGHT SHOULDER ----------------
        public static BasisSettingsBinding<bool> FBIKRightShoulderSmoothPos =>
            new("FBIKRightShoulderSmoothPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderSmoothRot =>
            new("FBIKRightShoulderSmoothRot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderEuroPos =>
            new("FBIKRightShoulderEuroPos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderEuroRot =>
            new("FBIKRightShoulderEuroRot", new BasisPlatformDefault<bool>(false));

        // Optional: a real persistent setting for the master toggle.
        public static BasisSettingsBinding<string> VSyncCapFps =>
            new("vsynccappedset", new BasisPlatformDefault<string>
            {
                windows = "120",
                android = "60",
                linux = "120",
                other = "120"
            });

        // Optional: a real persistent setting for the master toggle.
        public static readonly BasisSettingsBinding<bool> FBIKEuroAll =
            new BasisSettingsBinding<bool>("EuroAll");
    }
}
