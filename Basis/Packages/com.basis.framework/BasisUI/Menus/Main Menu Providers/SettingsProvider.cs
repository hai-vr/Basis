using Basis.Scripts.Device_Management;
using Basis.Scripts.UI.UI_Panels;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    public class SettingsProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new SettingsProvider());
        }

        public override string Title => "Settings";
        public override string IconAddress => AddressableAssets.Sprites.Settings;
        public override int Order => 0;

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title) return;

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);

            TextMeshProUGUI TitleLabel = panel.Descriptor.TitleLabel;
            BasisFrameRateVisualization FRV = TitleLabel.gameObject.AddComponent<BasisFrameRateVisualization>();
            FRV.Title = Title;
            FRV.fpsText = TitleLabel;

            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            PanelTabGroup tabGroup = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Vertical);

            tabGroup.AddTab("General", null, GeneralTab(tabGroup));
            tabGroup.AddTab("Audio", null, AudioTab(tabGroup));
            tabGroup.AddTab("Graphics", null, GraphicsTab(tabGroup));
            tabGroup.AddTab("Developer", null, DeveloperTab(tabGroup));
            tabGroup.AddTab("Avatar", null, AvatarTab(tabGroup));
            tabGroup.AddTab("Calibration", null, IKTab(tabGroup));

            tabGroup.AddExtraAction("Admin", OpenAdminPanel);
            tabGroup.AddExtraAction("Console", OpenConsoleLogger);
            tabGroup.AddExtraAction("Bindings", OpenControllerConfig);

            tabGroup.AddExtraAction("Switch To OpenVR", SwitchToOpenVR);
            tabGroup.AddExtraAction("Switch To OpenXR", SwitchToOpenXR);
            tabGroup.AddExtraAction("Switch To Desktop", SwitchToDesktop);

            // tabGroup.AddExtraAction("Statistics", OpenControllerConfig);

            tabGroup.AssignBinding(new BasisSettingsBinding<int>("BasisVR/SettingsTabs"));

            panel.Descriptor.ForceRebuild();
        }

        public void SwitchToOpenVR()
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To OpenVR",
                "Are you sure you want to swap to OpenVR?",
                "Switch To OpenVR",
                "Cancel",
                async value =>
                {
                    if (!value) return;

                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenVRLoader);
                });
        }

        public void SwitchToOpenXR()
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To OpenXR",
                "Are you sure you want to swap to OpenXR?",
                "Switch To OpenXR",
                "Cancel",
                async value =>
                {
                    if (!value) return;

                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenXRLoader);
                });
        }

        public void SwitchToDesktop()
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To Desktop",
                "Are you sure you want to swap to Desktop?",
                "Switch To Desktop",
                "Cancel",
                async value =>
                {
                    if (!value) return;

                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.Desktop);
                });
        }

        public static void OpenAdminPanel()
        {
            BasisMainMenu.Close();
            BasisUIAdminPanel.OpenThisMenu(BasisUIAdminPanel.Path);
        }

        public static void OpenConsoleLogger()
        {
            BasisMainMenu.Close();
            BasisUIBase.OpenMenuNow("BasisConsoleLogger");
        }

        public static void OpenControllerConfig()
        {
            BasisMainMenu.Close();
            BasisUIActionBindingsPanel.OpenMenuNow("Packages/com.basis.sdk/Prefabs/UI/ControllerConfig.prefab");
        }

        // ------------------
        // GENERAL TAB
        // ------------------
        public static PanelTabPage GeneralTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle("General Settings");

            RectTransform container = descriptor.ContentParent;

            // GENERAL INPUT / GAMEPLAY GROUP
            PanelElementDescriptor generalGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            generalGroup.SetIcon(AddressableAssets.Sprites.Settings);
            generalGroup.SetTitle("Gameplay & Input");
            generalGroup.SetDescription("General controls and comfort settings.");

            // Invert Mouse
            PanelToggle toggleInvertMouse = PanelToggle.CreateNewEntry(generalGroup);
            toggleInvertMouse.Descriptor.SetTitle("Invert Mouse");
            toggleInvertMouse.AssignBinding(BasisSettingsDefaults.InvertMouse);

            // Controller Dead Zone
            PanelSlider sliderControllerDeadZone = PanelSlider.CreateEntryAndBind(
                generalGroup,
                PanelSlider.SliderSettings.Advanced("Controller Dead Zone", 0, 1, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.ControllerDeadZone);

            // Snap Turn Angle
            PanelSlider sliderSnapTurnAngle = PanelSlider.CreateEntryAndBind(
                generalGroup,
                PanelSlider.SliderSettings.Advanced("Snap Turn Angle", -1, 120, true, 0, ValueDisplayMode.Degrees),
                BasisSettingsDefaults.SnapTurnAngle);

            // RANGE SETTINGS GROUP
            PanelElementDescriptor rangeGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            rangeGroup.SetTitle("Ranges");
            rangeGroup.SetDescription("Visibility and hearing ranges.");

            // Avatar Visibility Range
            PanelSlider sliderAvatarRange = PanelSlider.CreateEntryAndBind(
                rangeGroup,
                PanelSlider.SliderSettings.Distance("Avatar Visibility Range", 100),
                BasisSettingsDefaults.AvatarRange);

            // Hearing Range
            PanelSlider sliderHearingRange = PanelSlider.CreateEntryAndBind(
                rangeGroup,
                PanelSlider.SliderSettings.Distance("Hearing Range", 25),
                BasisSettingsDefaults.HearingRange);

            // Microphone Range
            PanelSlider sliderMicrophoneRange = PanelSlider.CreateEntryAndBind(
                rangeGroup,
                PanelSlider.SliderSettings.Distance("Microphone Range", 25),
                BasisSettingsDefaults.MicrophoneRange);

            descriptor.ForceRebuild();
            return tab;
        }

        // ------------------
        // AUDIO TAB
        // ------------------
        public static PanelTabPage AudioTab(PanelTabGroup tabGroup)
        {
            SMDMicrophone.LoadInMicrophoneData(BasisDeviceManagement.StaticCurrentMode);

            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetTitle("Audio Settings");
            RectTransform container = descriptor.ContentParent;

            PanelSlider sliderMainVolume = PanelSlider.CreateAndBind(
                container,
                PanelSlider.SliderSettings.Percentage("Main Volume"),
                BasisSettingsDefaults.MainVolume);
            sliderMainVolume.Descriptor.SetTitle("Master Volume");
            sliderMainVolume.Descriptor.SetDescription("Overall game volume.");

            // MIXER GROUP
            PanelElementDescriptor mixerGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            mixerGroup.SetTitle("Volume Mixer");
            mixerGroup.SetDescription("Control individual channel volumes.");

            PanelSlider sliderMenuVolume = PanelSlider.CreateEntryAndBind(
                mixerGroup,
                PanelSlider.SliderSettings.Percentage("Menu Volume"),
                BasisSettingsDefaults.MenuVolume);

            PanelSlider sliderWorldVolume = PanelSlider.CreateEntryAndBind(
                mixerGroup,
                PanelSlider.SliderSettings.Percentage("World Volume"),
                BasisSettingsDefaults.WorldVolume);

            PanelSlider sliderPlayerVolume = PanelSlider.CreateEntryAndBind(
                mixerGroup,
                PanelSlider.SliderSettings.Percentage("Player Volume"),
                BasisSettingsDefaults.PlayerVolume);

            // MICROPHONE GROUP
            PanelElementDescriptor microphoneGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            microphoneGroup.SetTitle("Microphone");
            microphoneGroup.SetDescription("Microphone Related Settings");

            PanelSlider sliderMicrophoneVolume = PanelSlider.CreateEntryAndBind(
                microphoneGroup,
                PanelSlider.SliderSettings.Advanced("Microphone Volume", 0, 1, false, 4, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.MicrophoneVolume);
            sliderMicrophoneVolume.SetValueWithoutNotify(SMDMicrophone.SelectedVolumeMicrophone);

            void MicrophoneVolumeChanged(float value)
            {
                SMDMicrophone.SaveVolumeSettings(BasisDeviceManagement.StaticCurrentMode, value);
            }

            sliderMicrophoneVolume.SliderComponent.onValueChanged.AddListener(MicrophoneVolumeChanged);


            BasisLocalVolumeMeterUIDescriptor rangeGroup = BasisLocalVolumeMeterUIDescriptor.CreateNew(BasisLocalVolumeMeterUIDescriptor.ElementStyles.Horizontal, microphoneGroup.ContentParent);


            // Microphone Mode
            PanelDropdown dropdownMicrophoneSelection = PanelDropdown.CreateNewEntry(microphoneGroup);
            dropdownMicrophoneSelection.Descriptor.SetTitle("Microphone Selection");
            // Options inferred from default naming – adjust to your actual system values if needed
            dropdownMicrophoneSelection.AssignEntries(SMDMicrophone.MicrophoneDevices.ToList());
            // dropdownMicrophoneSelection.DropdownComponent.value = dropdownMicrophoneSelection.StringValueToIndex(SMDMicrophone.SelectedMicrophone);
            dropdownMicrophoneSelection.SetValueWithoutNotify(SMDMicrophone.SelectedMicrophone);

            void MicrophoneSelectionChanged(string Name)
            {
                SMDMicrophone.SaveMicrophoneData(BasisDeviceManagement.StaticCurrentMode, Name);
            }

            dropdownMicrophoneSelection.OnValueChanged += MicrophoneSelectionChanged;



            // Microphone Denoiser
            PanelToggle toggleMicrophoneDenoiser = PanelToggle.CreateNewEntry(microphoneGroup);
            toggleMicrophoneDenoiser.Descriptor.SetTitle("Microphone Denoiser");
            toggleMicrophoneDenoiser.AssignBinding(BasisSettingsDefaults.MicrophoneDenoiser);

            // Automatic Gain Control
            PanelToggle toggleAGC = PanelToggle.CreateNewEntry(microphoneGroup);
            toggleAGC.Descriptor.SetTitle("Automatic Gain (AGC)");
            toggleAGC.AssignBinding(BasisSettingsDefaults.UseAutomaticGain);

            // Microphone Mode
            PanelDropdown dropdownMicrophoneMode = PanelDropdown.CreateNewEntry(microphoneGroup);
            dropdownMicrophoneMode.Descriptor.SetTitle("Microphone Mode");

            // Options inferred from default naming – adjust to your actual system values if needed
            dropdownMicrophoneMode.AssignEntries(new List<string>
            {
                "On Activation",
                "Push To Talk"
            });
            dropdownMicrophoneMode.AssignBinding(BasisSettingsDefaults.MicrophoneMode);

            // Microphone Icon
            PanelDropdown dropdownMicrophoneIcon = PanelDropdown.CreateNewEntry(microphoneGroup);
            dropdownMicrophoneIcon.Descriptor.SetTitle("Microphone Icon");
            dropdownMicrophoneIcon.AssignEntries(new List<string>
            {
                "AlwaysVisible",
                "ActivityDetection",
                "Hidden"
            });
            dropdownMicrophoneIcon.AssignBinding(BasisSettingsDefaults.MicrophoneIcon);

            descriptor.ForceRebuild();
            return tab;
        }

        // ------------------
        // GRAPHICS TAB
        // ------------------
        public static PanelTabPage GraphicsTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            descriptor.SetTitle("Graphics Settings");

            RectTransform container = descriptor.ContentParent;

            // QUALITY GROUP
            PanelElementDescriptor qualityGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            qualityGroup.SetTitle("Quality");
            qualityGroup.SetDescription("Overall render quality and post-processing.");

            // Quality Level
            PanelDropdown dropdownQualityLevel = PanelDropdown.CreateNewEntry(qualityGroup.ContentParent);
            dropdownQualityLevel.Descriptor.SetTitle("Quality Level");
            dropdownQualityLevel.AssignEntries(new List<string>
            {
                "Very Low", "Low", "Medium", "High", "Ultra"
            });
            dropdownQualityLevel.AssignBinding(BasisSettingsDefaults.QualityLevel);

            // Shadow Quality
            PanelDropdown dropdownShadowQuality = PanelDropdown.CreateNewEntry(qualityGroup.ContentParent);
            dropdownShadowQuality.Descriptor.SetTitle("Shadow Quality");
            dropdownShadowQuality.AssignEntries(new List<string>
            {
                "Very Low", "Low", "Medium", "High", "Ultra"
            });
            dropdownShadowQuality.AssignBinding(BasisSettingsDefaults.ShadowQuality);

            // Antialiasing
            PanelDropdown dropdownAntialiasing = PanelDropdown.CreateNewEntry(qualityGroup.ContentParent);
            dropdownAntialiasing.Descriptor.SetTitle("Antialiasing");
            dropdownAntialiasing.AssignEntries(new List<string>
            {
                "Off",
                "MSAA 2X",
                "MSAA 4X",
                "MSAA 8X",
                "Linear",
                "Point",
                "FSR",
                "STP"
            });
            dropdownAntialiasing.AssignBinding(BasisSettingsDefaults.Antialiasing);

            // VSync
            PanelDropdown dropdownVSync = PanelDropdown.CreateNewEntry(qualityGroup.ContentParent);
            dropdownVSync.Descriptor.SetTitle("Vertical Sync");
            dropdownVSync.AssignEntries(new List<string>
            {
                "On",
                "Capped",
                "Off",
                "Half",
            });
            dropdownVSync.AssignBinding(BasisSettingsDefaults.VSync);

            // RENDERING GROUP
            PanelElementDescriptor renderingGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            renderingGroup.SetTitle("Rendering");
            renderingGroup.SetDescription("Resolution, HDR and performance-related options.");

            // HDR Support
            PanelDropdown dropdownHDR = PanelDropdown.CreateNewEntry(renderingGroup.ContentParent);
            dropdownHDR.Descriptor.SetTitle("HDR Support");
            dropdownHDR.AssignEntries(new List<string>
            {
                "off",
                "32bit",
                "64bit"
            });
            dropdownHDR.AssignBinding(BasisSettingsDefaults.HDRSupport);

            // Memory Allocation
            PanelDropdown dropdownMemoryAllocation = PanelDropdown.CreateNewEntry(renderingGroup.ContentParent);
            dropdownMemoryAllocation.Descriptor.SetTitle("Memory Allocation");
            dropdownMemoryAllocation.AssignEntries(new List<string>
            {
                "Dynamic",
                "256",
                "512",
                "1024",
                "2048",
                "4096",
                "8192",
            });
            dropdownMemoryAllocation.AssignBinding(BasisSettingsDefaults.MemoryAllocation);

            // Render Scale
            PanelSlider sliderRenderResolution = PanelSlider.CreateEntryAndBind(
                renderingGroup.ContentParent,
                new PanelSlider.SliderSettings("Render Scale", "", 0, 1.5f, false, 3, ValueDisplayMode.percentageFromZero),
                BasisSettingsDefaults.RenderResolution);

            // Resolution (logical / display resolution)
            dropdownResolution = PanelDropdown.CreateNewEntry(renderingGroup.ContentParent);
            dropdownResolution.Descriptor.SetTitle("Resolution");
            uniqueResolutions = new List<Vector2Int>();
            resolutionOptions = new List<string>();

            foreach (Resolution res in Screen.resolutions)
            {
                Vector2Int size = new Vector2Int(res.width, res.height);

                // Only add if not already in the list (removes duplicates with different refresh rates)
                if (!uniqueResolutions.Contains(size))
                {
                    uniqueResolutions.Add(size);
                    resolutionOptions.Add(size.x + " x " + size.y);
                }
            }

            // NOTE: in many systems this will be populated by platform code – tweak/remove entries as needed
            dropdownResolution.AssignEntries(resolutionOptions);
            dropdownResolution.DropdownComponent.onValueChanged.AddListener(ResolutionChanged);

            // Monitor
            dropdownScreenMode = PanelDropdown.CreateNewEntry(renderingGroup.ContentParent);
            List<string> screenModeOptions = new List<string>
            {
                "Fullscreen",
                "Borderless Window",
                "Windowed"
            };

            dropdownScreenMode.Descriptor.SetTitle("ScreenMode");
            dropdownScreenMode.AssignEntries(screenModeOptions);
            dropdownScreenMode.DropdownComponent.onValueChanged.AddListener(ScreenMode);
            ;

            // ADVANCED / FOVEATION GROUP
            PanelElementDescriptor advancedGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            advancedGroup.SetTitle("Advanced Rendering");
            advancedGroup.SetDescription("Foveation, FOV and LOD controls.");

            // Foveated Rendering
            PanelSlider sliderFoveatedRendering = PanelSlider.CreateEntryAndBind(
                advancedGroup.ContentParent,
                PanelSlider.SliderSettings.Advanced("Foveated Rendering", 0, 1, false, 1, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.FoveatedRendering);

            // Field Of View
            PanelSlider sliderFieldOfView = PanelSlider.CreateEntryAndBind(
                advancedGroup.ContentParent,
                PanelSlider.SliderSettings.Degrees("Field Of View", BasisSettingsDefaults.FOV_MIN, BasisSettingsDefaults.FOV_MAX, true, 0),
                BasisSettingsDefaults.FieldOfView);

            // Mesh LOD
            PanelSlider sliderMeshLOD = PanelSlider.CreateEntryAndBind(
                advancedGroup.ContentParent,
                new PanelSlider.SliderSettings("Avatar LOD Multiplier", "", 0, 1, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.AvatarMeshLOD);

            // Global Mesh LOD
            PanelSlider sliderGlobalMeshLOD = PanelSlider.CreateEntryAndBind(
                advancedGroup.ContentParent,
                PanelSlider.SliderSettings.Percentage("World LOD Multiplier"),
                BasisSettingsDefaults.GlobalMeshLOD);

            descriptor.ForceRebuild();
            return tab;
        }
        public static PanelDropdown dropdownResolution;
        public static List<Vector2Int> uniqueResolutions;
        private static List<string> resolutionOptions;
        public static PanelDropdown dropdownScreenMode;

        private static void ScreenMode(int screenModeIndex)
        {
            FullScreenMode mode = GetScreenModeFromIndex(screenModeIndex);
            Vector2Int currentResolution = uniqueResolutions[dropdownResolution.DropdownComponent.value];

            Screen.SetResolution(currentResolution.x, currentResolution.y, mode);
            BasisDebug.Log("Changed Screen Mode: " + mode);
        }
        private static FullScreenMode GetScreenModeFromIndex(int index)
        {
            switch (index)
            {
                case 0: return FullScreenMode.ExclusiveFullScreen;
                case 1: return FullScreenMode.FullScreenWindow;
                case 2: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }
        private static void ResolutionChanged(int resolutionIndex)
        {
            Vector2Int selectedResolution = uniqueResolutions[resolutionIndex];
            FullScreenMode mode = GetScreenModeFromIndex(dropdownScreenMode.DropdownComponent.value);

            Screen.SetResolution(selectedResolution.x, selectedResolution.y, mode);
            BasisDebug.Log("Changed Resolution: " + selectedResolution.x + "x" + selectedResolution.y);
        }

        // ------------------
        // DEVELOPER TAB
        // ------------------
        public static PanelTabPage DeveloperTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetTitle("Developer & Debug");
            RectTransform container = descriptor.ContentParent;

            // DEBUG VISUALS GROUP
            PanelElementDescriptor debugGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            debugGroup.SetTitle("Debug Visuals");
            debugGroup.SetDescription("Debug Systems running through visuals in 3D space");

            // Debug Visuals Toggle
            PanelToggle toggleDebugVisuals = PanelToggle.CreateNewEntry(debugGroup.ContentParent);
            toggleDebugVisuals.Descriptor.SetTitle("Debug Visuals Enabled");
            toggleDebugVisuals.AssignBinding(BasisSettingsDefaults.DebugVisuals);

            // Visual State Mode
            PanelDropdown dropdownVisualState = PanelDropdown.CreateNewEntry(debugGroup.ContentParent);
            dropdownVisualState.Descriptor.SetTitle("Visual Helpers");
            dropdownVisualState.AssignEntries(new List<string>
            {
                "Off",
                "all visuals",
                "only avatar distance"
            });
            dropdownVisualState.AssignBinding(BasisSettingsDefaults.VisualState);

            descriptor.ForceRebuild();
            return tab;
        }

        // ------------------
        // AVATAR TAB
        // ------------------
        public static PanelTabPage AvatarTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetTitle("Avatar Settings");
            RectTransform container = descriptor.ContentParent;

            PanelElementDescriptor debugGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            debugGroup.SetTitle("Avatar Settings");
            debugGroup.SetDescription("Configuration settings for avatars.");

            // Avatar Download Size.
            PanelSlider AvatarDownloadSize = PanelSlider.CreateEntryAndBind(
                debugGroup.ContentParent,
                PanelSlider.SliderSettings.Advanced("Avatar Download Size", 5, 1024, false, 0, ValueDisplayMode.MemorySize),
                BasisSettingsDefaults.AvatarDownloadSize);

            descriptor.ForceRebuild();
            return tab;
        }
        // Put these as fields on the class (so RevaluteInteractableStatus can touch them)
        private static PanelDropdown dropdownVisualState;
        private static PanelDropdown dropdownSeatedMode;
        //   private static PanelSlider sliderCalibrationHeightRange;

        //  private const string VisualMode_CalibEyeHeight = "Calibration Eye Height";
        private const string SeatedMode_Seated = "Seated Mode";
        // ------------------
        // IK & Input
        // ------------------
        public static PanelTabPage IKTab(PanelTabGroup tabGroup)
        {
            var tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            var descriptor = tab.Descriptor;
            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle("IK Tab");

            RectTransform container = descriptor.ContentParent;

            // IK GROUP
            var IkGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            IkGroup.SetIcon(AddressableAssets.Sprites.Settings);
            IkGroup.SetTitle("Calibration & IK");
            IkGroup.SetDescription("Settings for Fine Tuning Calibration and IK");

            // Seated Mode
            dropdownSeatedMode = PanelDropdown.CreateNewEntry(IkGroup.ContentParent);
            dropdownSeatedMode.Descriptor.SetTitle("Seated Mode");
            dropdownSeatedMode.AssignEntries(new List<string>
    {
        "Standing Mode",
        SeatedMode_Seated
    });
            dropdownSeatedMode.AssignBinding(BasisSettingsDefaults.SeatedMode);

            // Visual State Mode
            dropdownVisualState = PanelDropdown.CreateNewEntry(IkGroup.ContentParent);
            dropdownVisualState.Descriptor.SetTitle("Full Body IK Mode");
            dropdownVisualState.AssignEntries(new List<string>
    {
        "Eye Height",
        "Arm Distance",
     //   VisualMode_CalibEyeHeight
    });
            dropdownVisualState.AssignBinding(BasisSettingsDefaults.IKMode);

            // Custom Scale toggle (your label says Custom Scale but variable name says DebugVisuals; up to you)
            var toggleCustomScale = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            toggleCustomScale.Descriptor.SetTitle("Custom Scale");
            toggleCustomScale.AssignBinding(BasisSettingsDefaults.CustomScale);

            // Avatar Scale slider
            var sliderScaleRange = PanelSlider.CreateEntryAndBind(
                IkGroup.ContentParent,
                PanelSlider.SliderSettings.Advanced("Avatar height Scale", 0.1f, 5f, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.SelectedScale);

            // Hook both dropdowns to the same evaluator
            dropdownVisualState.OnValueChanged += EvaluateInteractables;
            dropdownSeatedMode.OnValueChanged += EvaluateInteractables;

            // Run once on build so initial state is correct
            EvaluateInteractables();



            // ---------------- GLOBAL ONE EURO PARAMS ----------------
            var FBIKMinCutoff = PanelSlider.CreateEntryAndBind(
                IkGroup,
                PanelSlider.SliderSettings.Advanced("MinCutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKMinCutoff);

            var FBIKBeta = PanelSlider.CreateEntryAndBind(
                IkGroup,
                PanelSlider.SliderSettings.Advanced("Beta", 0f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKBeta);

            var FBIKDerivativeCutoff = PanelSlider.CreateEntryAndBind(
                IkGroup,
                PanelSlider.SliderSettings.Advanced("DerivativeCutoff", 0.1f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKDerivativeCutoff);

            var FBIKPositionSmoothingHz = PanelSlider.CreateEntryAndBind(
                IkGroup,
                PanelSlider.SliderSettings.Advanced("Position Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKPositionSmoothingHz);

            var FBIKRotationSmoothingHz = PanelSlider.CreateEntryAndBind(
                IkGroup,
                PanelSlider.SliderSettings.Advanced("Rotation Smoothing (Hz)", 0.01f, 60f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.FBIKRotationSmoothingHz);


            // ---------------- HIPS ----------------
            var tFBIKHipsSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHipsSmoothPos.Descriptor.SetTitle("Hips Smooth Pos");
            tFBIKHipsSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKHipsSmoothPos);

            var tFBIKHipsSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHipsSmoothRot.Descriptor.SetTitle("Hips Smooth Rot");
            tFBIKHipsSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKHipsSmoothRot);

            var tFBIKHipsEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHipsEuroPos.Descriptor.SetTitle("Hips Euro Pos");
            tFBIKHipsEuroPos.AssignBinding(BasisSettingsDefaults.FBIKHipsEuroPos);

            var tFBIKHipsEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHipsEuroRot.Descriptor.SetTitle("Hips Euro Rot");
            tFBIKHipsEuroRot.AssignBinding(BasisSettingsDefaults.FBIKHipsEuroRot);


            // ---------------- HEAD ----------------
            var tFBIKHeadSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHeadSmoothPos.Descriptor.SetTitle("Head Smooth Pos");
            tFBIKHeadSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKHeadSmoothPos);

            var tFBIKHeadSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHeadSmoothRot.Descriptor.SetTitle("Head Smooth Rot");
            tFBIKHeadSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKHeadSmoothRot);

            var tFBIKHeadEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHeadEuroPos.Descriptor.SetTitle("Head Euro Pos");
            tFBIKHeadEuroPos.AssignBinding(BasisSettingsDefaults.FBIKHeadEuroPos);

            var tFBIKHeadEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKHeadEuroRot.Descriptor.SetTitle("Head Euro Rot");
            tFBIKHeadEuroRot.AssignBinding(BasisSettingsDefaults.FBIKHeadEuroRot);


            // ---------------- LEFT FOOT ----------------
            var tFBIKLeftFootSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftFootSmoothPos.Descriptor.SetTitle("Left Foot Smooth Pos");
            tFBIKLeftFootSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftFootSmoothPos);

            var tFBIKLeftFootSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftFootSmoothRot.Descriptor.SetTitle("Left Foot Smooth Rot");
            tFBIKLeftFootSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftFootSmoothRot);

            var tFBIKLeftFootEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftFootEuroPos.Descriptor.SetTitle("Left Foot Euro Pos");
            tFBIKLeftFootEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftFootEuroPos);

            var tFBIKLeftFootEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftFootEuroRot.Descriptor.SetTitle("Left Foot Euro Rot");
            tFBIKLeftFootEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftFootEuroRot);


            // ---------------- RIGHT FOOT ----------------
            var tFBIKRightFootSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightFootSmoothPos.Descriptor.SetTitle("Right Foot Smooth Pos");
            tFBIKRightFootSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightFootSmoothPos);

            var tFBIKRightFootSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightFootSmoothRot.Descriptor.SetTitle("Right Foot Smooth Rot");
            tFBIKRightFootSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightFootSmoothRot);

            var tFBIKRightFootEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightFootEuroPos.Descriptor.SetTitle("Right Foot Euro Pos");
            tFBIKRightFootEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightFootEuroPos);

            var tFBIKRightFootEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightFootEuroRot.Descriptor.SetTitle("Right Foot Euro Rot");
            tFBIKRightFootEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightFootEuroRot);


            // ---------------- CHEST ----------------
            var tFBIKChestSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKChestSmoothPos.Descriptor.SetTitle("Chest Smooth Pos");
            tFBIKChestSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKChestSmoothPos);

            var tFBIKChestSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKChestSmoothRot.Descriptor.SetTitle("Chest Smooth Rot");
            tFBIKChestSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKChestSmoothRot);

            var tFBIKChestEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKChestEuroPos.Descriptor.SetTitle("Chest Euro Pos");
            tFBIKChestEuroPos.AssignBinding(BasisSettingsDefaults.FBIKChestEuroPos);

            var tFBIKChestEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKChestEuroRot.Descriptor.SetTitle("Chest Euro Rot");
            tFBIKChestEuroRot.AssignBinding(BasisSettingsDefaults.FBIKChestEuroRot);


            // ---------------- LEFT LOWER LEG ----------------
            var tFBIKLeftLowerLegSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerLegSmoothPos.Descriptor.SetTitle("Left Lower Leg Smooth Pos");
            tFBIKLeftLowerLegSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerLegSmoothPos);

            var tFBIKLeftLowerLegSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerLegSmoothRot.Descriptor.SetTitle("Left Lower Leg Smooth Rot");
            tFBIKLeftLowerLegSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerLegSmoothRot);

            var tFBIKLeftLowerLegEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerLegEuroPos.Descriptor.SetTitle("Left Lower Leg Euro Pos");
            tFBIKLeftLowerLegEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerLegEuroPos);

            var tFBIKLeftLowerLegEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerLegEuroRot.Descriptor.SetTitle("Left Lower Leg Euro Rot");
            tFBIKLeftLowerLegEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerLegEuroRot);


            // ---------------- RIGHT LOWER LEG ----------------
            var tFBIKRightLowerLegSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerLegSmoothPos.Descriptor.SetTitle("Right Lower Leg Smooth Pos");
            tFBIKRightLowerLegSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightLowerLegSmoothPos);

            var tFBIKRightLowerLegSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerLegSmoothRot.Descriptor.SetTitle("Right Lower Leg Smooth Rot");
            tFBIKRightLowerLegSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightLowerLegSmoothRot);

            var tFBIKRightLowerLegEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerLegEuroPos.Descriptor.SetTitle("Right Lower Leg Euro Pos");
            tFBIKRightLowerLegEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightLowerLegEuroPos);

            var tFBIKRightLowerLegEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerLegEuroRot.Descriptor.SetTitle("Right Lower Leg Euro Rot");
            tFBIKRightLowerLegEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightLowerLegEuroRot);


            // ---------------- LEFT HAND ----------------
            var tFBIKLeftHandSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftHandSmoothPos.Descriptor.SetTitle("Left Hand Smooth Pos");
            tFBIKLeftHandSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftHandSmoothPos);

            var tFBIKLeftHandSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftHandSmoothRot.Descriptor.SetTitle("Left Hand Smooth Rot");
            tFBIKLeftHandSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftHandSmoothRot);

            var tFBIKLeftHandEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftHandEuroPos.Descriptor.SetTitle("Left Hand Euro Pos");
            tFBIKLeftHandEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftHandEuroPos);

            var tFBIKLeftHandEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftHandEuroRot.Descriptor.SetTitle("Left Hand Euro Rot");
            tFBIKLeftHandEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftHandEuroRot);


            // ---------------- RIGHT HAND ----------------
            var tFBIKRightHandSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightHandSmoothPos.Descriptor.SetTitle("Right Hand Smooth Pos");
            tFBIKRightHandSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightHandSmoothPos);

            var tFBIKRightHandSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightHandSmoothRot.Descriptor.SetTitle("Right Hand Smooth Rot");
            tFBIKRightHandSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightHandSmoothRot);

            var tFBIKRightHandEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightHandEuroPos.Descriptor.SetTitle("Right Hand Euro Pos");
            tFBIKRightHandEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightHandEuroPos);

            var tFBIKRightHandEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightHandEuroRot.Descriptor.SetTitle("Right Hand Euro Rot");
            tFBIKRightHandEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightHandEuroRot);


            // ---------------- LEFT LOWER ARM ----------------
            var tFBIKLeftLowerArmSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerArmSmoothPos.Descriptor.SetTitle("Left Lower Arm Smooth Pos");
            tFBIKLeftLowerArmSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerArmSmoothPos);

            var tFBIKLeftLowerArmSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerArmSmoothRot.Descriptor.SetTitle("Left Lower Arm Smooth Rot");
            tFBIKLeftLowerArmSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerArmSmoothRot);

            var tFBIKLeftLowerArmEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerArmEuroPos.Descriptor.SetTitle("Left Lower Arm Euro Pos");
            tFBIKLeftLowerArmEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerArmEuroPos);

            var tFBIKLeftLowerArmEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftLowerArmEuroRot.Descriptor.SetTitle("Left Lower Arm Euro Rot");
            tFBIKLeftLowerArmEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftLowerArmEuroRot);


            // ---------------- RIGHT LOWER ARM ----------------
            var tFBIKRightLowerArmSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerArmSmoothPos.Descriptor.SetTitle("Right Lower Arm Smooth Pos");
            tFBIKRightLowerArmSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightLowerArmSmoothPos);

            var tFBIKRightLowerArmSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerArmSmoothRot.Descriptor.SetTitle("Right Lower Arm Smooth Rot");
            tFBIKRightLowerArmSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightLowerArmSmoothRot);

            var tFBIKRightLowerArmEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerArmEuroPos.Descriptor.SetTitle("Right Lower Arm Euro Pos");
            tFBIKRightLowerArmEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightLowerArmEuroPos);

            var tFBIKRightLowerArmEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightLowerArmEuroRot.Descriptor.SetTitle("Right Lower Arm Euro Rot");
            tFBIKRightLowerArmEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightLowerArmEuroRot);


            // ---------------- LEFT TOE ----------------
            var tFBIKLeftToeSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftToeSmoothPos.Descriptor.SetTitle("Left Toe Smooth Pos");
            tFBIKLeftToeSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftToeSmoothPos);

            var tFBIKLeftToeSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftToeSmoothRot.Descriptor.SetTitle("Left Toe Smooth Rot");
            tFBIKLeftToeSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftToeSmoothRot);

            var tFBIKLeftToeEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftToeEuroPos.Descriptor.SetTitle("Left Toe Euro Pos");
            tFBIKLeftToeEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftToeEuroPos);

            var tFBIKLeftToeEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftToeEuroRot.Descriptor.SetTitle("Left Toe Euro Rot");
            tFBIKLeftToeEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftToeEuroRot);


            // ---------------- RIGHT TOE ----------------
            var tFBIKRightToeSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightToeSmoothPos.Descriptor.SetTitle("Right Toe Smooth Pos");
            tFBIKRightToeSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightToeSmoothPos);

            var tFBIKRightToeSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightToeSmoothRot.Descriptor.SetTitle("Right Toe Smooth Rot");
            tFBIKRightToeSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightToeSmoothRot);

            var tFBIKRightToeEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightToeEuroPos.Descriptor.SetTitle("Right Toe Euro Pos");
            tFBIKRightToeEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightToeEuroPos);

            var tFBIKRightToeEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightToeEuroRot.Descriptor.SetTitle("Right Toe Euro Rot");
            tFBIKRightToeEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightToeEuroRot);


            // ---------------- LEFT SHOULDER ----------------
            var tFBIKLeftShoulderSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftShoulderSmoothPos.Descriptor.SetTitle("Left Shoulder Smooth Pos");
            tFBIKLeftShoulderSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKLeftShoulderSmoothPos);

            var tFBIKLeftShoulderSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftShoulderSmoothRot.Descriptor.SetTitle("Left Shoulder Smooth Rot");
            tFBIKLeftShoulderSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKLeftShoulderSmoothRot);

            var tFBIKLeftShoulderEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftShoulderEuroPos.Descriptor.SetTitle("Left Shoulder Euro Pos");
            tFBIKLeftShoulderEuroPos.AssignBinding(BasisSettingsDefaults.FBIKLeftShoulderEuroPos);

            var tFBIKLeftShoulderEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKLeftShoulderEuroRot.Descriptor.SetTitle("Left Shoulder Euro Rot");
            tFBIKLeftShoulderEuroRot.AssignBinding(BasisSettingsDefaults.FBIKLeftShoulderEuroRot);


            // ---------------- RIGHT SHOULDER ----------------
            var tFBIKRightShoulderSmoothPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightShoulderSmoothPos.Descriptor.SetTitle("Right Shoulder Smooth Pos");
            tFBIKRightShoulderSmoothPos.AssignBinding(BasisSettingsDefaults.FBIKRightShoulderSmoothPos);

            var tFBIKRightShoulderSmoothRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightShoulderSmoothRot.Descriptor.SetTitle("Right Shoulder Smooth Rot");
            tFBIKRightShoulderSmoothRot.AssignBinding(BasisSettingsDefaults.FBIKRightShoulderSmoothRot);

            var tFBIKRightShoulderEuroPos = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightShoulderEuroPos.Descriptor.SetTitle("Right Shoulder Euro Pos");
            tFBIKRightShoulderEuroPos.AssignBinding(BasisSettingsDefaults.FBIKRightShoulderEuroPos);

            var tFBIKRightShoulderEuroRot = PanelToggle.CreateNewEntry(IkGroup.ContentParent);
            tFBIKRightShoulderEuroRot.Descriptor.SetTitle("Right Shoulder Euro Rot");
            tFBIKRightShoulderEuroRot.AssignBinding(BasisSettingsDefaults.FBIKRightShoulderEuroRot);

            descriptor.ForceRebuild();
            return tab;
        }
        private static void EvaluateInteractables(string obj)
        {
            EvaluateInteractables();
        }
        private static void EvaluateInteractables()
        {
            string seatedValue = GetCurrentText(dropdownSeatedMode);
            string visualValue = GetCurrentText(dropdownVisualState);
            bool isSeated = seatedValue == SeatedMode_Seated;
            SetDropdownInteractable(dropdownVisualState, !isSeated);
            // bool isCalibrationMode = (!isSeated) && (visualValue == VisualMode_CalibEyeHeight);
            //   sliderCalibrationHeightRange.SliderComponent.interactable = isCalibrationMode || !isSeated;
        }
        private static string GetCurrentText(PanelDropdown dd)
        {
            return dd.DropdownComponent.options[dd.DropdownComponent.value].text;
        }
        private static void SetDropdownInteractable(PanelDropdown dd, bool interactable)
        {
            dd.DropdownComponent.interactable = interactable;
        }
    }
}
