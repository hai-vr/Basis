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
            new("microphone volume", new BasisPlatformDefault<float>(1));

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
            new("microphonerange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> HearingRange =>
            new("hearingrange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> SelectedHeight =>
            new("selectedheight", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> SelectedScale =>
            new("selected scale", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> realworldeyeheight =>
            new("real world eye height", new BasisPlatformDefault<float>(1.61f));

        public static BasisSettingsBinding<bool> CustomScale =>
            new("custom scale", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<float> AvatarRange =>
            new("avatarrange", new BasisPlatformDefault<float>(25));

        public static BasisSettingsBinding<float> SnapTurnAngle =>
            new("snapturnangle", new BasisPlatformDefault<float>(25f));

        public static BasisSettingsBinding<float> mousesensitivty =>
            new("mousesensitivty", new BasisPlatformDefault<float>(1));

        public static BasisSettingsBinding<bool> InvertMouse =>
            new("invertmouse", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> usesnapturn =>
            new("usesnapturn", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<string> QualityLevel =>
            new("quality level", new BasisPlatformDefault<string>
            {
                windows = "ultra",
                android = "very low",
                linux = "ultra",
                other = "ultra"
            });

        public static BasisSettingsBinding<string> ShadowQuality =>
            new("shadow quality", new BasisPlatformDefault<string>
            {
                windows = "ultra",
                android = "very low",
                linux = "ultra",
                other = "ultra"
            });

        public static BasisSettingsBinding<string> HDRSupport =>
            new("hdr support", new BasisPlatformDefault<string>
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
            new("antialiasing", new BasisPlatformDefault<string>("msaa 2x"));

        public static BasisSettingsBinding<bool> DebugVisuals =>
            new("debug visuals", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<string> MemoryAllocation =>
            new("memory allocation", new BasisPlatformDefault<string>
            {
                windows = "dynamic",
                android = "dynamic",
                linux = "dynamic",
                other = "dynamic"
            });

        public static BasisSettingsBinding<string> MicrophoneIcon =>
            new("microphone icon", new BasisPlatformDefault<string>("alwaysvisible"));

        public static BasisSettingsBinding<string> VisualState =>
            new("visual state", new BasisPlatformDefault<string>("off"));

        public static BasisSettingsBinding<string> IKMode =>
            new("ik mode", new BasisPlatformDefault<string>("eye height"));

        public static BasisSettingsBinding<string> SelectedBone =>
            new("selectedbone", new BasisPlatformDefault<string>("selectedbone"));

        public static BasisSettingsBinding<float> FoveatedRendering =>
            new("foveated rendering", new BasisPlatformDefault<float>
            {
                windows = 0,
                android = 1,
                linux = 0,
                other = 0,
                ios = 0
            });

        public static BasisSettingsBinding<float> FieldOfView =>
            new("field of view", new BasisPlatformDefault<float>(65));

        public const float FOV_MIN = 50;
        public const float FOV_MAX = 120;

        public static BasisSettingsBinding<float> AvatarScale =>
            new("scale of avatar", new BasisPlatformDefault<float>(1.6f));

        public static BasisSettingsBinding<float> AvatarDownloadSize =>
            new("avatar download size", new BasisPlatformDefault<float>(256));

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

        public static BasisSettingsBinding<string> SeatedMode => new("Seated Mode", new BasisPlatformDefault<string>("standing mode"));

        public static BasisSettingsBinding<string> VSync =>
            new("vertical sync", new BasisPlatformDefault<string>
            {
                windows = "on",
                android = "on",
                linux = "capped",
                other = "on"
            });

        public static BasisSettingsBinding<float> RenderResolution =>
            new("render resolution", new BasisPlatformDefault<float>(1));

        public static BasisSettingsBinding<string> MicrophoneMode =>
            new("microphonemode", new BasisPlatformDefault<string>("onactivation"));

        public static BasisSettingsBinding<bool> UseAutomaticGain =>
            new("agc", new BasisPlatformDefault<bool>
            {
                windows = true,
                android = false,
                linux = false,
                other = false
            });

        public static BasisSettingsBinding<bool> FalseBinding =>
            new("falsebinding", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> TrueBinding =>
            new("truebinding", new BasisPlatformDefault<bool>(false));

        // ---------------- GLOBAL ONE EURO PARAMS ----------------
        public static BasisSettingsBinding<float> FBIKMinCutoff =>
            new("fbikmincutoff", new BasisPlatformDefault<float>(5.5f));

        public static BasisSettingsBinding<float> FBIKBeta =>
            new("fbikbeta", new BasisPlatformDefault<float>(3.25f));

        public static BasisSettingsBinding<float> FBIKDerivativeCutoff =>
            new("fbikderivativecutoff", new BasisPlatformDefault<float>(3f));

        public static BasisSettingsBinding<float> FBIKPositionSmoothingHz =>
            new("fbikpositionsmoothinghz", new BasisPlatformDefault<float>(20f));

        public static BasisSettingsBinding<float> FBIKRotationSmoothingHz =>
            new("fbikrotationsmoothinghz", new BasisPlatformDefault<float>(25f));

        // ---------------- HIPS ----------------
        public static BasisSettingsBinding<bool> FBIKHipsSmoothPos =>
            new("fbikhipssmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHipsSmoothRot =>
            new("fbikhipssmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHipsEuroPos =>
            new("fbikhipseuropos", new BasisPlatformDefault<bool>(true));

        public static BasisSettingsBinding<bool> FBIKHipsEuroRot =>
            new("fbikhipseurorot", new BasisPlatformDefault<bool>(true));

        // ---------------- HEAD ----------------
        public static BasisSettingsBinding<bool> FBIKHeadSmoothPos =>
            new("fbikheadsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadSmoothRot =>
            new("fbikheadsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadEuroPos =>
            new("fbikheadeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKHeadEuroRot =>
            new("fbikheadeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT FOOT ----------------
        public static BasisSettingsBinding<bool> FBIKLeftFootSmoothPos =>
            new("fbikleftfootsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootSmoothRot =>
            new("fbikleftfootsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootEuroPos =>
            new("fbikleftfooteuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftFootEuroRot =>
            new("fbikleftfooteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT FOOT ----------------
        public static BasisSettingsBinding<bool> FBIKRightFootSmoothPos =>
            new("fbikrightfootsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootSmoothRot =>
            new("fbikrightfootsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootEuroPos =>
            new("fbikrightfooteuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightFootEuroRot =>
            new("fbikrightfooteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- CHEST ----------------
        public static BasisSettingsBinding<bool> FBIKChestSmoothPos =>
            new("fbikchestsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestSmoothRot =>
            new("fbikchestsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestEuroPos =>
            new("fbikchesteuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKChestEuroRot =>
            new("fbikchesteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT LOWER LEG ----------------
        public static BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothPos =>
            new("fbikleftlowerlegsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothRot =>
            new("fbikleftlowerlegsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegEuroPos =>
            new("fbikleftlowerlegeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerLegEuroRot =>
            new("fbikleftlowerlegeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT LOWER LEG ----------------
        public static BasisSettingsBinding<bool> FBIKRightLowerLegSmoothPos =>
            new("fbikrightlowerlegsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegSmoothRot =>
            new("fbikrightlowerlegsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegEuroPos =>
            new("fbikrightlowerlegeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerLegEuroRot =>
            new("fbikrightlowerlegeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT HAND ----------------
        public static BasisSettingsBinding<bool> FBIKLeftHandSmoothPos =>
            new("fbiklefthandsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandSmoothRot =>
            new("fbiklefthandsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandEuroPos =>
            new("fbikleftehandeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftHandEuroRot =>
            new("fbikleftehandeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT HAND ----------------
        public static BasisSettingsBinding<bool> FBIKRightHandSmoothPos =>
            new("fbikrighthandsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandSmoothRot =>
            new("fbikrighthandsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandEuroPos =>
            new("fbikrighthandeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightHandEuroRot =>
            new("fbikrighthandeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT LOWER ARM ----------------
        public static BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothPos =>
            new("fbikleftlowerarmsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothRot =>
            new("fbikleftlowerarmsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmEuroPos =>
            new("fbikleftlowerarmeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftLowerArmEuroRot =>
            new("fbikleftlowerarmeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT LOWER ARM ----------------
        public static BasisSettingsBinding<bool> FBIKRightLowerArmSmoothPos =>
            new("fbikrightlowerarmsmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmSmoothRot =>
            new("fbikrightlowerarmsmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmEuroPos =>
            new("fbikrightlowerarmeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightLowerArmEuroRot =>
            new("fbikrightlowerarmeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT TOE ----------------
        public static BasisSettingsBinding<bool> FBIKLeftToeSmoothPos =>
            new("fbiklefttoesmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeSmoothRot =>
            new("fbiklefttoesmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeEuroPos =>
            new("fbiklefttoeeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftToeEuroRot =>
            new("fbiklefttoeeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT TOE ----------------
        public static BasisSettingsBinding<bool> FBIKRightToeSmoothPos =>
            new("fbikrighttoesmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeSmoothRot =>
            new("fbikrighttoesmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeEuroPos =>
            new("fbikrighttoeeuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightToeEuroRot =>
            new("fbikrighttoeeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT SHOULDER ----------------
        public static BasisSettingsBinding<bool> FBIKLeftShoulderSmoothPos =>
            new("fbikleftshouldersmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderSmoothRot =>
            new("fbikleftshouldersmoothrot", new BasisPlatformDefault<bool>(true));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderEuroPos =>
            new("fbikleftshouldereuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKLeftShoulderEuroRot =>
            new("fbikleftshouldereurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT SHOULDER ----------------
        public static BasisSettingsBinding<bool> FBIKRightShoulderSmoothPos =>
            new("fbikrightshouldersmoothpos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderSmoothRot =>
            new("fbikrightshouldersmoothrot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderEuroPos =>
            new("fbikrightshouldereuropos", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<bool> FBIKRightShoulderEuroRot =>
            new("fbikrightshouldereurorot", new BasisPlatformDefault<bool>(false));

        public static BasisSettingsBinding<string> VSyncCapFps =>
            new("vsynccappedset", new BasisPlatformDefault<string>
            {
                windows = "120",
                android = "60",
                linux = "120",
                other = "120"
            });

        public static readonly BasisSettingsBinding<bool> FBIKEuroAll =
            new("euroall");

        // Limiter
        public static BasisSettingsBinding<float> LimitThreshold =>
            new("limitthreshold", new BasisPlatformDefault<float>(0.95f)); // pre-clip

        public static BasisSettingsBinding<float> LimitKnee =>
            new("limitknee", new BasisPlatformDefault<float>(0.05f)); // soft knee width

        // Denoise extra params (post gain + wet/dry)
        public static BasisSettingsBinding<float> DenoiseMakeupDb =>
            new("denoisemakeupdb", new BasisPlatformDefault<float>(3f));

        public static BasisSettingsBinding<float> DenoiseWet =>
            new("denoisewet", new BasisPlatformDefault<float>(1f)); // 0..1


        public static BasisSettingsBinding<float> AgcTargetRms =>
            new("agctargetrms", new BasisPlatformDefault<float>(0.06f)); // ~ -24 dBFS

        public static BasisSettingsBinding<float> AgcMaxGainDb =>
            new("agcmaxgaindb", new BasisPlatformDefault<float>(18f));

        public static BasisSettingsBinding<float> AgcAttack =>
            new("agcattack", new BasisPlatformDefault<float>(0.10f)); // 0..1

        public static BasisSettingsBinding<float> AgcRelease =>
            new("agcrelease", new BasisPlatformDefault<float>(0.01f)); // 0..1
    }
}
