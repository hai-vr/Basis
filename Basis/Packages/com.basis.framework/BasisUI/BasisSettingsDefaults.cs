namespace Basis.BasisUI
{
    public static class BasisSettingsDefaults
    {
        public static readonly BasisSettingsBinding<float> MainVolume = new("main volume", new BasisPlatformDefault<float>(75));

        public static readonly BasisSettingsBinding<float> MenuVolume = new("menuvolume", new BasisPlatformDefault<float>(75));
        public static readonly BasisSettingsBinding<float> MediaVolume = new("mediavolume", new BasisPlatformDefault<float>(75));
        public static readonly BasisSettingsBinding<float> WorldVolume = new("worldvolume", new BasisPlatformDefault<float>(75));

        public static readonly BasisSettingsBinding<float> VoiceVolume = new("voicevolume", new BasisPlatformDefault<float>(75));
        public static readonly BasisSettingsBinding<float> AvatarVolume = new("avatarvolume", new BasisPlatformDefault<float>(75));
        public static readonly BasisSettingsBinding<float> PropVolume = new("propvolume", new BasisPlatformDefault<float>(75));
        public static readonly BasisSettingsBinding<float> MicrophoneVolume = new("microphonevolume", new BasisPlatformDefault<float>(1));

        public static readonly BasisSettingsBinding<float> ControllerDeadZone = new("joystickdeadzone", new BasisPlatformDefault<float>(0.01f));

        public static readonly BasisSettingsBinding<float> Basexdeadzone = new("basexdeadzone", new BasisPlatformDefault<float>(0.08f));

        public static readonly BasisSettingsBinding<float> Extraxdeadzoneatfully = new("extraxdeadzoneatfully", new BasisPlatformDefault<float>(0.35f));

        public static readonly BasisSettingsBinding<float> Ydeadzone = new("ydeadzone", new BasisPlatformDefault<float>(0.10f));

        public static readonly BasisSettingsBinding<float> Wingexponent = new("wingexponent", new BasisPlatformDefault<float>(1.6f));

        public static readonly BasisSettingsBinding<float> MicrophoneRange = new("microphonerange", new BasisPlatformDefault<float>(25));

        public static readonly BasisSettingsBinding<float> HearingRange = new("hearingrange", new BasisPlatformDefault<float>(25));

        public static readonly BasisSettingsBinding<float> SelectedHeight = new("selectedheight", new BasisPlatformDefault<float>(1.6f));

        public static readonly BasisSettingsBinding<float> SelectedScale = new("selectedscale", new BasisPlatformDefault<float>(1.6f));

        public static readonly BasisSettingsBinding<float> realworldeyeheight = new("realworldeyeheight", new BasisPlatformDefault<float>(1.61f));

        public static readonly BasisSettingsBinding<bool> CustomScale = new("customscale", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<float> AvatarRange = new("avatarrange", new BasisPlatformDefault<float>(25));

        /// <summary>
        /// Maximum number of remote players allowed to show their real avatar at once.
        /// 0 = unlimited (all in-range players show real avatars).
        /// Players beyond this limit fall back to the default avatar.
        /// Closest players get priority; currently-visible avatars are sticky to prevent pulsing.
        /// </summary>
        public static readonly BasisSettingsBinding<float> MaxVisibleAvatars = new("maxvisibleavatars", new BasisPlatformDefault<float>(0));

        public static readonly BasisSettingsBinding<float> SnapTurnAngle = new("snapturnangle", new BasisPlatformDefault<float>(25f));

        public static readonly BasisSettingsBinding<float> mousesensitivty = new("mousesensitivty", new BasisPlatformDefault<float>(1));

        public static readonly BasisSettingsBinding<bool> InvertMouse = new("invertmouse", new BasisPlatformDefault<bool>(false));

        /// <summary>
        /// Dominant hand preference. "right" or "left". Affects placement raycast and pickup priority.
        /// </summary>
        public static readonly BasisSettingsBinding<string> DominantHand = new("dominanthand", new BasisPlatformDefault<string>("right"));

        public static readonly BasisSettingsBinding<bool> usesnapturn = new("usesnapturn", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<string> QualityLevel = new("qualitylevel", new BasisPlatformDefault<string>
        {
            windows = "Ultra",
            android = "Very Low",
            linux = "Ultra",
            other = "Ultra"
        });

        public static readonly BasisSettingsBinding<string> ShadowQuality = new("shadowquality", new BasisPlatformDefault<string>
        {
            windows = "Ultra",
            android = "Very Low",
            linux = "Ultra",
            other = "Ultra"
        });

        public static readonly BasisSettingsBinding<string> HDRSupport = new("hdrsupport", new BasisPlatformDefault<string>
        {
            windows = "64bit",
            android = "Off",
            linux = "64bit",
            other = "64bit"
        });

        public static readonly BasisSettingsBinding<bool> MicrophoneDenoiser = new("voicedenoiser", new BasisPlatformDefault<bool>
        {
            windows = true,
            android = false,
            linux = false,
            other = false
        });

        public static readonly BasisSettingsBinding<string> Antialiasing = new("antialiasing", new BasisPlatformDefault<string>("msaa 2x"));

        public static readonly BasisSettingsBinding<bool> DebugVisuals = new("debugvisuals", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<string> MemoryAllocation = new("memoryallocation", new BasisPlatformDefault<string>
        {
            windows = "Dynamic",
            android = "Dnamic",
            linux = "Dynamic",
            other = "Dynamic"
        });

        public static readonly BasisSettingsBinding<string> MicrophoneIcon = new("microphoneicon", new BasisPlatformDefault<string>("alwaysvisible"));

        public static readonly BasisSettingsBinding<float> MicrophoneIconOffsetX = new("microphoneiconoffsetx", new BasisPlatformDefault<float>(0f));
        public static readonly BasisSettingsBinding<float> MicrophoneIconOffsetY = new("microphoneiconoffsety", new BasisPlatformDefault<float>(0f));

        public static readonly BasisSettingsBinding<string> VisualState = new("visualstate", new BasisPlatformDefault<string>("off"));

        public static readonly BasisSettingsBinding<string> IKMode = new("ikmode", new BasisPlatformDefault<string>("eye height"));

        public static readonly BasisSettingsBinding<string> IKLockMode = new("iklockmode", new BasisPlatformDefault<string>("lock hips"));

        public static readonly BasisSettingsBinding<bool> PitchCalibration = new("pitchcalibration", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<string> SelectedBone = new("selectedbone", new BasisPlatformDefault<string>("selectedbone"));

        public static readonly BasisSettingsBinding<float> FoveatedRendering = new("foveatedrendering", new BasisPlatformDefault<float>
        {
            windows = 0,
            android = 1,
            linux = 0,
            other = 0,
            ios = 0
        });

        public static readonly BasisSettingsBinding<float> FieldOfView = new("fieldofview", new BasisPlatformDefault<float>(65));

        public const float FOV_MIN = 50;
        public const float FOV_MAX = 120;

        public static readonly BasisSettingsBinding<float> AvatarDownloadSize = new("avatardownloadsize", new BasisPlatformDefault<float>(256));

        public static readonly BasisSettingsBinding<float> CacheMaxSizeGB = new("cachemaxsizegb", new BasisPlatformDefault<float>(128));

        public static readonly BasisSettingsBinding<float> AvatarMeshLOD = new("avatarmeshlod", new BasisPlatformDefault<float>
        {
            windows = 0.05f,
            android = 0.1f,
            linux = 0.05f,
            other = 0.05f
        });

        public static readonly BasisSettingsBinding<float> GlobalMeshLOD = new("globalmeshlod", new BasisPlatformDefault<float>
        {
            windows = 0,
            android = 30,
            linux = 0,
            other = 0
        });

        public static readonly BasisSettingsBinding<string> SitStand = new("seatedmode", new BasisPlatformDefault<string>(SettingsProviderIK.SeatedMode_Standing));

        public static readonly BasisSettingsBinding<string> VSync = new("verticalsync", new BasisPlatformDefault<string>
        {
            windows = "On",
            android = "On",
            linux = "Capped",
            other = "On"
        });

        public static readonly BasisSettingsBinding<float> RenderResolution = new("render resolution", new BasisPlatformDefault<float>(1));

        public static readonly BasisSettingsBinding<string> MicrophoneMode = new("microphonemode", new BasisPlatformDefault<string>("onactivation"));

        public static readonly BasisSettingsBinding<string> MicStartBehavior = new("micstartbehavior", new BasisPlatformDefault<string>(BasisLocalMicrophoneDriver.SettingStartOff));

        public static readonly BasisSettingsBinding<bool> UseAutomaticGain = new("automaticgainenabled", new BasisPlatformDefault<bool>
        {
            windows = true,
            android = true,
            linux = true,
            other = true
        });

        // ---------------- NOTIFICATIONS ----------------
        public static readonly BasisSettingsBinding<bool> JoinNotifications = new("joinnotifications", new BasisPlatformDefault<bool>(false));
        public static readonly BasisSettingsBinding<bool> LeaveNotifications = new("leavenotifications", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FalseBinding = new("falsebinding", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> TrueBinding = new("truebinding", new BasisPlatformDefault<bool>(false));

        // ---------------- GLOBAL ONE EURO PARAMS ----------------
        public static readonly BasisSettingsBinding<float> FBIKMinCutoff = new("fbikmincutoff", new BasisPlatformDefault<float>(5.5f));

        public static readonly BasisSettingsBinding<float> FBIKBeta = new("fbikbeta", new BasisPlatformDefault<float>(3.25f));

        public static readonly BasisSettingsBinding<float> FBIKDerivativeCutoff = new("fbikderivativecutoff", new BasisPlatformDefault<float>(3f));

        public static readonly BasisSettingsBinding<float> FBIKPositionSmoothingHz =>
            new("fbikpositionsmoothinghz", new BasisPlatformDefault<float>(20f));

        public static readonly BasisSettingsBinding<float> FBIKRotationSmoothingHz =>
            new("fbikrotationsmoothinghz", new BasisPlatformDefault<float>(25f));

        public static readonly BasisSettingsBinding<float> FBIKSmoothingStrength =>
            new("fbiksmoothingstrength", new BasisPlatformDefault<float>(1f));

        // ---------------- HIPS ----------------
        public static readonly BasisSettingsBinding<bool> FBIKHipsSmoothPos =>
            new("fbikhipssmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKHipsSmoothRot =>
            new("fbikhipssmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKHipsEuroPos =>
            new("fbikhipseuropos", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> FBIKHipsEuroRot =>
            new("fbikhipseurorot", new BasisPlatformDefault<bool>(true));

        // ---------------- HEAD ----------------
        public static readonly BasisSettingsBinding<bool> FBIKHeadSmoothPos =>
            new("fbikheadsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKHeadSmoothRot =>
            new("fbikheadsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKHeadEuroPos =>
            new("fbikheadeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKHeadEuroRot =>
            new("fbikheadeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT FOOT ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftFootSmoothPos =>
            new("fbikleftfootsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftFootSmoothRot =>
            new("fbikleftfootsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftFootEuroPos =>
            new("fbikleftfooteuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftFootEuroRot =>
            new("fbikleftfooteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT FOOT ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightFootSmoothPos =>
            new("fbikrightfootsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightFootSmoothRot =>
            new("fbikrightfootsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightFootEuroPos =>
            new("fbikrightfooteuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightFootEuroRot =>
            new("fbikrightfooteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- CHEST ----------------
        public static readonly BasisSettingsBinding<bool> FBIKChestSmoothPos =>
            new("fbikchestsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKChestSmoothRot =>
            new("fbikchestsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKChestEuroPos =>
            new("fbikchesteuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKChestEuroRot =>
            new("fbikchesteurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT LOWER LEG ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothPos =>
            new("fbikleftlowerlegsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerLegSmoothRot =>
            new("fbikleftlowerlegsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerLegEuroPos =>
            new("fbikleftlowerlegeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerLegEuroRot =>
            new("fbikleftlowerlegeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT LOWER LEG ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightLowerLegSmoothPos =>
            new("fbikrightlowerlegsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerLegSmoothRot =>
            new("fbikrightlowerlegsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerLegEuroPos =>
            new("fbikrightlowerlegeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerLegEuroRot =>
            new("fbikrightlowerlegeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT HAND ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftHandSmoothPos =>
            new("fbiklefthandsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftHandSmoothRot =>
            new("fbiklefthandsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftHandEuroPos =>
            new("fbikleftehandeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftHandEuroRot =>
            new("fbikleftehandeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT HAND ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightHandSmoothPos =>
            new("fbikrighthandsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightHandSmoothRot =>
            new("fbikrighthandsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightHandEuroPos =>
            new("fbikrighthandeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightHandEuroRot =>
            new("fbikrighthandeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT LOWER ARM ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothPos =>
            new("fbikleftlowerarmsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerArmSmoothRot =>
            new("fbikleftlowerarmsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerArmEuroPos =>
            new("fbikleftlowerarmeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftLowerArmEuroRot =>
            new("fbikleftlowerarmeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT LOWER ARM ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightLowerArmSmoothPos =>
            new("fbikrightlowerarmsmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerArmSmoothRot =>
            new("fbikrightlowerarmsmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerArmEuroPos =>
            new("fbikrightlowerarmeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightLowerArmEuroRot =>
            new("fbikrightlowerarmeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT TOE ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftToeSmoothPos =>
            new("fbiklefttoesmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftToeSmoothRot =>
            new("fbiklefttoesmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftToeEuroPos =>
            new("fbiklefttoeeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftToeEuroRot =>
            new("fbiklefttoeeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT TOE ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightToeSmoothPos =>
            new("fbikrighttoesmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightToeSmoothRot =>
            new("fbikrighttoesmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightToeEuroPos =>
            new("fbikrighttoeeuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightToeEuroRot = new("fbikrighttoeeurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- LEFT SHOULDER ----------------
        public static readonly BasisSettingsBinding<bool> FBIKLeftShoulderSmoothPos = new("fbikleftshouldersmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftShoulderSmoothRot = new("fbikleftshouldersmoothrot", new BasisPlatformDefault<bool>(true));

        public static readonly BasisSettingsBinding<bool> FBIKLeftShoulderEuroPos = new("fbikleftshouldereuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKLeftShoulderEuroRot = new("fbikleftshouldereurorot", new BasisPlatformDefault<bool>(false));

        // ---------------- RIGHT SHOULDER ----------------
        public static readonly BasisSettingsBinding<bool> FBIKRightShoulderSmoothPos = new("fbikrightshouldersmoothpos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightShoulderSmoothRot = new("fbikrightshouldersmoothrot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightShoulderEuroPos = new("fbikrightshouldereuropos", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKRightShoulderEuroRot = new("fbikrightshouldereurorot", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<string> VSyncCapFps = new("vsynccappedset", new BasisPlatformDefault<string>
        {
            windows = "120",
            android = "60",
            linux = "120",
            other = "120"
        });

        // ---------------- REMOTE PLAYER AUDIO ----------------
        // AudioSource
        public static readonly BasisSettingsBinding<float> RAMinDistance = new("ra_mindistance", new BasisPlatformDefault<float>(0.5f));
        public static readonly BasisSettingsBinding<float> RASpread = new("ra_spread", new BasisPlatformDefault<float>(70f));
        public static readonly BasisSettingsBinding<float> RADopplerLevel = new("ra_dopplerlevel", new BasisPlatformDefault<float>(0f));
        public static readonly BasisSettingsBinding<float> RASpatialBlend = new("ra_spatialblend", new BasisPlatformDefault<float>(1f));

        // Steam Audio - HRTF
        public static readonly BasisSettingsBinding<bool> RADirectBinaural = new("ra_directbinaural", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<bool> RAPerspectiveCorrection = new("ra_perspectivecorrection", new BasisPlatformDefault<bool>(false));
        public static readonly BasisSettingsBinding<string> RAInterpolation = new("ra_interpolation", new BasisPlatformDefault<string>("nearest"));

        // Steam Audio - Propagation
        public static readonly BasisSettingsBinding<bool> RADistanceAttenuation = new("ra_distanceattenuation", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<bool> RAAirAbsorption = new("ra_airabsorption", new BasisPlatformDefault<bool>(true));

        // Steam Audio - Directivity
        public static readonly BasisSettingsBinding<bool> RADirectivity = new("ra_directivity", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<float> RADipoleWeight = new("ra_dipoleweight", new BasisPlatformDefault<float>(0.25f));
        public static readonly BasisSettingsBinding<float> RADipolePower = new("ra_dipolepower", new BasisPlatformDefault<float>(1f));

        // Steam Audio - Occlusion
        public static readonly BasisSettingsBinding<bool> RAOcclusion = new("ra_occlusion", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<string> RAOcclusionType = new("ra_occlusiontype", new BasisPlatformDefault<string>("volumetric"));
        public static readonly BasisSettingsBinding<float> RAOcclusionRadius = new("ra_occlusionradius", new BasisPlatformDefault<float>(0.15f));
        public static readonly BasisSettingsBinding<float> RAOcclusionSamples = new("ra_occlusionsamples", new BasisPlatformDefault<float>(16f));

        // Steam Audio - Transmission
        public static readonly BasisSettingsBinding<bool> RATransmission = new("ra_transmission", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<string> RATransmissionType = new("ra_transmissiontype", new BasisPlatformDefault<string>("frequency dependent"));
        public static readonly BasisSettingsBinding<float> RAMaxTransmissionSurfaces = new("ra_maxtransmissionsurfaces", new BasisPlatformDefault<float>(4f));

        // AudioSource - Rolloff
        public static readonly BasisSettingsBinding<string> RARolloffMode = new("ra_rolloffmode", new BasisPlatformDefault<string>("custom"));
        public static readonly BasisSettingsBinding<string> RARolloffCurvePreset = new("ra_rolloffcurvepreset", new BasisPlatformDefault<string>("default"));
        public static readonly BasisSettingsBinding<float> RACurvePoint25 = new("ra_curvepoint25", new BasisPlatformDefault<float>(0.6f));
        public static readonly BasisSettingsBinding<float> RACurvePoint50 = new("ra_curvepoint50", new BasisPlatformDefault<float>(0.3f));
        public static readonly BasisSettingsBinding<float> RACurvePoint75 = new("ra_curvepoint75", new BasisPlatformDefault<float>(0.1f));
        public static readonly BasisSettingsBinding<float> RAPriority = new("ra_priority", new BasisPlatformDefault<float>(128f));

        // Listener Directional Dampening
        public static readonly BasisSettingsBinding<float> RAListenerConeAngle = new("ra_listenerconeangle", new BasisPlatformDefault<float>(150f));
        public static readonly BasisSettingsBinding<float> RAListenerDampenAmount = new("ra_listenerdampenamount", new BasisPlatformDefault<float>(75f));

        // Steam Audio - Attenuation Input
        public static readonly BasisSettingsBinding<string> RADistanceAttenuationInput = new("ra_distanceattenuationinput", new BasisPlatformDefault<string>("curve driven"));

        // Steam Audio - Air Absorption Bands
        public static readonly BasisSettingsBinding<string> RAAirAbsorptionInput = new("ra_airabsorptioninput", new BasisPlatformDefault<string>("simulation defined"));
        public static readonly BasisSettingsBinding<float> RAAirAbsorptionLow = new("ra_airabsorptionlow", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> RAAirAbsorptionMid = new("ra_airabsorptionmid", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> RAAirAbsorptionHigh = new("ra_airabsorptionhigh", new BasisPlatformDefault<float>(1f));

        // Steam Audio - Mix
        public static readonly BasisSettingsBinding<float> RADirectMixLevel = new("ra_directmixlevel", new BasisPlatformDefault<float>(1f));

        // Steam Audio - Reflections
        public static readonly BasisSettingsBinding<bool> RAReflections = new("ra_reflections", new BasisPlatformDefault<bool>(false));
        public static readonly BasisSettingsBinding<float> RAReflectionsMixLevel = new("ra_reflectionsmixlevel", new BasisPlatformDefault<float>(0.1f));
        public static readonly BasisSettingsBinding<bool> RAApplyHRTFToReflections = new("ra_applyhrtftoreflections", new BasisPlatformDefault<bool>(false));

        public static readonly BasisSettingsBinding<bool> FBIKEuroAll = new("euroall");

        // ---------------- CALIBRATION SPHERE SCALE (per bone) ----------------
        public static readonly BasisSettingsBinding<float> CalibSphereScaleHips = new("calibspherescalehips", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleChest = new("calibspherescalechest", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftFoot = new("calibspherescaleleftfoot", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightFoot = new("calibspherescalerightfoot", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftLowerLeg = new("calibspherescaleleftlowerleg", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightLowerLeg = new("calibspherescalerightlowerleg", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftLowerArm = new("calibspherescaleleftlowerarm", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightLowerArm = new("calibspherescalerightlowerarm", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftHand = new("calibspherescalelefthand", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightHand = new("calibspherescalerighthand", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftToes = new("calibspherescalelefttoes", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightToes = new("calibspherescalerighttoes", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleLeftShoulder = new("calibspherescaleleftshoulder", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> CalibSphereScaleRightShoulder = new("calibspherescalerightshoulder", new BasisPlatformDefault<float>(1f));

        // ---------------- IK COLLIDER & TUNING ----------------
        public static readonly BasisSettingsBinding<bool> FBIKAdvancedVisible = new("fbikadvancedvisible", new BasisPlatformDefault<bool>(false));
        public static readonly BasisSettingsBinding<bool> FBIKCollisionsEnabled = new("fbikcollisionsenabled", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<bool> FBIKProtectElbow = new("fbikprotectelbow", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<bool> FBIKUseHandCapsule = new("fbikusehandcapsule", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<float> FBIKChestRadius = new("fbikchestradius", new BasisPlatformDefault<float>(0.18f));
        public static readonly BasisSettingsBinding<float> FBIKCollisionSkin = new("fbikcollisionskin", new BasisPlatformDefault<float>(0.02f));
        public static readonly BasisSettingsBinding<float> FBIKHandRadius = new("fbikhandradius", new BasisPlatformDefault<float>(0.05f));
        public static readonly BasisSettingsBinding<float> FBIKHandSkin = new("fbikhandskin", new BasisPlatformDefault<float>(0.01f));
        public static readonly BasisSettingsBinding<bool> FBIKShoulderSolveEnabled = new("fbikshouldersolveenabled", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<float> FBIKShoulderElevation = new("fbikshoulderelevation", new BasisPlatformDefault<float>(0.4f));
        public static readonly BasisSettingsBinding<float> FBIKShoulderProtraction = new("fbikshoulderprotraction", new BasisPlatformDefault<float>(0.3f));
        public static readonly BasisSettingsBinding<float> FBIKMaxBendDeg = new("fbikmaxbenddeg", new BasisPlatformDefault<float>(90f));
        public static readonly BasisSettingsBinding<float> FBIKStruggleStart = new("fbikstrugglestart", new BasisPlatformDefault<float>(0.9f));
        public static readonly BasisSettingsBinding<float> FBIKStruggleEnd = new("fbikstruggleend", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> FBIKMaxChestDelta = new("fbikmaxchestdelta", new BasisPlatformDefault<float>(90f));
        public static readonly BasisSettingsBinding<float> FBIKMaxHipDelta = new("fbikmaxhipdelta", new BasisPlatformDefault<float>(90f));

        // ---------------- REMOTE NAMEPLATE ----------------
        public static readonly BasisSettingsBinding<bool> NPEnabled = new("np_enabled", new BasisPlatformDefault<bool>(true));
        public static readonly BasisSettingsBinding<bool> NPMenuOnly = new("np_menuonly", new BasisPlatformDefault<bool>(false));
        public static readonly BasisSettingsBinding<float> NPWidth = new("np_width", new BasisPlatformDefault<float>(30f));
        public static readonly BasisSettingsBinding<float> NPSize = new("np_size", new BasisPlatformDefault<float>(1f));
        public static readonly BasisSettingsBinding<float> NPTransparency = new("np_transparency", new BasisPlatformDefault<float>(0.45f));

        // Limiter
        public static readonly BasisSettingsBinding<float> LimitThreshold = new("limitthreshold", new BasisPlatformDefault<float>(0.95f)); // pre-clip

        public static readonly BasisSettingsBinding<float> LimitKnee = new("limitknee", new BasisPlatformDefault<float>(0.05f)); // soft knee width

        // Denoise extra params (post gain + wet/dry)
        public static readonly BasisSettingsBinding<float> DenoiseMakeupDb = new("denoisemakeupdb", new BasisPlatformDefault<float>(3f));

        public static readonly BasisSettingsBinding<float> DenoiseWet = new("denoisewet", new BasisPlatformDefault<float>(1f)); // 0..1


        public static readonly BasisSettingsBinding<float> AgcTargetRms = new("agctargetrms", new BasisPlatformDefault<float>(0.1f)); // ~ -24 dBFS

        public static readonly BasisSettingsBinding<float> AgcMaxGainDb = new("agcdbgainmax", new BasisPlatformDefault<float>(8f));

        public static readonly BasisSettingsBinding<float> AgcAttack = new("agcattack", new BasisPlatformDefault<float>(0.10f)); // 0..1

        public static readonly BasisSettingsBinding<float> AgcRelease = new("agcrelease", new BasisPlatformDefault<float>(0.01f)); // 0..1

        public static void LoadAll()
        {
            // Audio
            MainVolume.LoadBindingValue();
            MenuVolume.LoadBindingValue();
            WorldVolume.LoadBindingValue();
            AvatarVolume.LoadBindingValue();
            PropVolume.LoadBindingValue();
            MediaVolume.LoadBindingValue();

            MicrophoneVolume.LoadBindingValue();
            MicrophoneRange.LoadBindingValue();
            HearingRange.LoadBindingValue();
            MicrophoneDenoiser.LoadBindingValue();
            MicrophoneMode.LoadBindingValue();
            MicStartBehavior.LoadBindingValue();
            UseAutomaticGain.LoadBindingValue();
            DenoiseMakeupDb.LoadBindingValue();
            DenoiseWet.LoadBindingValue();
            AgcTargetRms.LoadBindingValue();
            AgcMaxGainDb.LoadBindingValue();
            AgcAttack.LoadBindingValue();
            AgcRelease.LoadBindingValue();

            // Input / Movement
            ControllerDeadZone.LoadBindingValue();
            Basexdeadzone.LoadBindingValue();
            Extraxdeadzoneatfully.LoadBindingValue();
            Ydeadzone.LoadBindingValue();
            Wingexponent.LoadBindingValue();
            SnapTurnAngle.LoadBindingValue();
            mousesensitivty.LoadBindingValue();
            InvertMouse.LoadBindingValue();
            DominantHand.LoadBindingValue();
            usesnapturn.LoadBindingValue();

            // Avatar / IK / Body
            SelectedHeight.LoadBindingValue();
            SelectedScale.LoadBindingValue();
            realworldeyeheight.LoadBindingValue();
            CustomScale.LoadBindingValue();
            AvatarRange.LoadBindingValue();
            MaxVisibleAvatars.LoadBindingValue();
            SelectedBone.LoadBindingValue();
            IKMode.LoadBindingValue();
            IKLockMode.LoadBindingValue();
            PitchCalibration.LoadBindingValue();
            SitStand.LoadBindingValue();

            // Rendering / Graphics
            QualityLevel.LoadBindingValue();
            ShadowQuality.LoadBindingValue();
            HDRSupport.LoadBindingValue();
            Antialiasing.LoadBindingValue();
            DebugVisuals.LoadBindingValue();
            MemoryAllocation.LoadBindingValue();
            VisualState.LoadBindingValue();
            FoveatedRendering.LoadBindingValue();
            FieldOfView.LoadBindingValue();
            RenderResolution.LoadBindingValue();
            VSync.LoadBindingValue();
            VSyncCapFps.LoadBindingValue();

            // LOD / Download limits
            AvatarDownloadSize.LoadBindingValue();
            CacheMaxSizeGB.LoadBindingValue();
            AvatarMeshLOD.LoadBindingValue();
            GlobalMeshLOD.LoadBindingValue();

            // Notifications
            JoinNotifications.LoadBindingValue();
            LeaveNotifications.LoadBindingValue();

            // UI
            MicrophoneIcon.LoadBindingValue();
            MicrophoneIconOffsetX.LoadBindingValue();
            MicrophoneIconOffsetY.LoadBindingValue();

            // Misc
            FalseBinding.LoadBindingValue();
            TrueBinding.LoadBindingValue();
            LimitThreshold.LoadBindingValue();
            LimitKnee.LoadBindingValue();

            // Global FBIK parameters
            FBIKMinCutoff.LoadBindingValue();
            FBIKBeta.LoadBindingValue();
            FBIKDerivativeCutoff.LoadBindingValue();
            FBIKPositionSmoothingHz.LoadBindingValue();
            FBIKRotationSmoothingHz.LoadBindingValue();
            FBIKSmoothingStrength.LoadBindingValue();

            // Hips
            FBIKHipsSmoothPos.LoadBindingValue();
            FBIKHipsSmoothRot.LoadBindingValue();
            FBIKHipsEuroPos.LoadBindingValue();
            FBIKHipsEuroRot.LoadBindingValue();

            // Head
            FBIKHeadSmoothPos.LoadBindingValue();
            FBIKHeadSmoothRot.LoadBindingValue();
            FBIKHeadEuroPos.LoadBindingValue();
            FBIKHeadEuroRot.LoadBindingValue();

            // Left Foot
            FBIKLeftFootSmoothPos.LoadBindingValue();
            FBIKLeftFootSmoothRot.LoadBindingValue();
            FBIKLeftFootEuroPos.LoadBindingValue();
            FBIKLeftFootEuroRot.LoadBindingValue();

            // Right Foot
            FBIKRightFootSmoothPos.LoadBindingValue();
            FBIKRightFootSmoothRot.LoadBindingValue();
            FBIKRightFootEuroPos.LoadBindingValue();
            FBIKRightFootEuroRot.LoadBindingValue();

            // Chest
            FBIKChestSmoothPos.LoadBindingValue();
            FBIKChestSmoothRot.LoadBindingValue();
            FBIKChestEuroPos.LoadBindingValue();
            FBIKChestEuroRot.LoadBindingValue();

            // Left Lower Leg
            FBIKLeftLowerLegSmoothPos.LoadBindingValue();
            FBIKLeftLowerLegSmoothRot.LoadBindingValue();
            FBIKLeftLowerLegEuroPos.LoadBindingValue();
            FBIKLeftLowerLegEuroRot.LoadBindingValue();

            // Right Lower Leg
            FBIKRightLowerLegSmoothPos.LoadBindingValue();
            FBIKRightLowerLegSmoothRot.LoadBindingValue();
            FBIKRightLowerLegEuroPos.LoadBindingValue();
            FBIKRightLowerLegEuroRot.LoadBindingValue();

            // Left Hand
            FBIKLeftHandSmoothPos.LoadBindingValue();
            FBIKLeftHandSmoothRot.LoadBindingValue();
            FBIKLeftHandEuroPos.LoadBindingValue();
            FBIKLeftHandEuroRot.LoadBindingValue();

            // Right Hand
            FBIKRightHandSmoothPos.LoadBindingValue();
            FBIKRightHandSmoothRot.LoadBindingValue();
            FBIKRightHandEuroPos.LoadBindingValue();
            FBIKRightHandEuroRot.LoadBindingValue();

            // Left Lower Arm
            FBIKLeftLowerArmSmoothPos.LoadBindingValue();
            FBIKLeftLowerArmSmoothRot.LoadBindingValue();
            FBIKLeftLowerArmEuroPos.LoadBindingValue();
            FBIKLeftLowerArmEuroRot.LoadBindingValue();

            // Right Lower Arm
            FBIKRightLowerArmSmoothPos.LoadBindingValue();
            FBIKRightLowerArmSmoothRot.LoadBindingValue();
            FBIKRightLowerArmEuroPos.LoadBindingValue();
            FBIKRightLowerArmEuroRot.LoadBindingValue();

            // Left Toe
            FBIKLeftToeSmoothPos.LoadBindingValue();
            FBIKLeftToeSmoothRot.LoadBindingValue();
            FBIKLeftToeEuroPos.LoadBindingValue();
            FBIKLeftToeEuroRot.LoadBindingValue();

            // Right Toe
            FBIKRightToeSmoothPos.LoadBindingValue();
            FBIKRightToeSmoothRot.LoadBindingValue();
            FBIKRightToeEuroPos.LoadBindingValue();
            FBIKRightToeEuroRot.LoadBindingValue();

            // Shoulders
            FBIKLeftShoulderSmoothPos.LoadBindingValue();
            FBIKLeftShoulderSmoothRot.LoadBindingValue();
            FBIKLeftShoulderEuroPos.LoadBindingValue();
            FBIKLeftShoulderEuroRot.LoadBindingValue();

            FBIKRightShoulderSmoothPos.LoadBindingValue();
            FBIKRightShoulderSmoothRot.LoadBindingValue();
            FBIKRightShoulderEuroPos.LoadBindingValue();
            FBIKRightShoulderEuroRot.LoadBindingValue();

            // Global toggle
            FBIKEuroAll.LoadBindingValue();

            // Calibration sphere scale (per bone)
            CalibSphereScaleHips.LoadBindingValue();
            CalibSphereScaleChest.LoadBindingValue();
            CalibSphereScaleLeftFoot.LoadBindingValue();
            CalibSphereScaleRightFoot.LoadBindingValue();
            CalibSphereScaleLeftLowerLeg.LoadBindingValue();
            CalibSphereScaleRightLowerLeg.LoadBindingValue();
            CalibSphereScaleLeftLowerArm.LoadBindingValue();
            CalibSphereScaleRightLowerArm.LoadBindingValue();
            CalibSphereScaleLeftHand.LoadBindingValue();
            CalibSphereScaleRightHand.LoadBindingValue();
            CalibSphereScaleLeftToes.LoadBindingValue();
            CalibSphereScaleRightToes.LoadBindingValue();
            CalibSphereScaleLeftShoulder.LoadBindingValue();
            CalibSphereScaleRightShoulder.LoadBindingValue();

            // IK Collider & Tuning
            FBIKAdvancedVisible.LoadBindingValue();
            FBIKCollisionsEnabled.LoadBindingValue();
            FBIKProtectElbow.LoadBindingValue();
            FBIKUseHandCapsule.LoadBindingValue();
            FBIKChestRadius.LoadBindingValue();
            FBIKCollisionSkin.LoadBindingValue();
            FBIKHandRadius.LoadBindingValue();
            FBIKHandSkin.LoadBindingValue();
            FBIKShoulderSolveEnabled.LoadBindingValue();
            FBIKShoulderElevation.LoadBindingValue();
            FBIKShoulderProtraction.LoadBindingValue();
            FBIKMaxBendDeg.LoadBindingValue();
            FBIKStruggleStart.LoadBindingValue();
            FBIKStruggleEnd.LoadBindingValue();
            FBIKMaxChestDelta.LoadBindingValue();
            FBIKMaxHipDelta.LoadBindingValue();

            // Remote Nameplate
            NPEnabled.LoadBindingValue();
            NPMenuOnly.LoadBindingValue();
            NPWidth.LoadBindingValue();
            NPSize.LoadBindingValue();
            NPTransparency.LoadBindingValue();

            // Remote Player Audio
            RAMinDistance.LoadBindingValue();
            RASpread.LoadBindingValue();
            RADopplerLevel.LoadBindingValue();
            RASpatialBlend.LoadBindingValue();
            RADirectBinaural.LoadBindingValue();
            RAPerspectiveCorrection.LoadBindingValue();
            RAInterpolation.LoadBindingValue();
            RADistanceAttenuation.LoadBindingValue();
            RAAirAbsorption.LoadBindingValue();
            RADirectivity.LoadBindingValue();
            RADipoleWeight.LoadBindingValue();
            RADipolePower.LoadBindingValue();
            RAOcclusion.LoadBindingValue();
            RAOcclusionType.LoadBindingValue();
            RAOcclusionRadius.LoadBindingValue();
            RAOcclusionSamples.LoadBindingValue();
            RATransmission.LoadBindingValue();
            RATransmissionType.LoadBindingValue();
            RAMaxTransmissionSurfaces.LoadBindingValue();
            RADirectMixLevel.LoadBindingValue();
            RAListenerConeAngle.LoadBindingValue();
            RAListenerDampenAmount.LoadBindingValue();
            RARolloffMode.LoadBindingValue();
            RARolloffCurvePreset.LoadBindingValue();
            RACurvePoint25.LoadBindingValue();
            RACurvePoint50.LoadBindingValue();
            RACurvePoint75.LoadBindingValue();
            RAPriority.LoadBindingValue();
            RADistanceAttenuationInput.LoadBindingValue();
            RAAirAbsorptionInput.LoadBindingValue();
            RAAirAbsorptionLow.LoadBindingValue();
            RAAirAbsorptionMid.LoadBindingValue();
            RAAirAbsorptionHigh.LoadBindingValue();
            RAReflections.LoadBindingValue();
            RAReflectionsMixLevel.LoadBindingValue();
            RAApplyHRTFToReflections.LoadBindingValue();
        }
    }
}
