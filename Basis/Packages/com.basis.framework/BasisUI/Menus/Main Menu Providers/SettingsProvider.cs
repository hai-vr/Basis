using Basis.Scripts.Device_Management;
using Basis.Scripts.UI.UI_Panels;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class SettingsProvider : BasisMenuActionProvider<BasisMainMenu>
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
            tabGroup.AddTab("Avatar", null, AvatarTab(tabGroup));
            tabGroup.AddTab("Calibration", null, SettingsProviderIK.IKTab(tabGroup));
            tabGroup.AddTab("Bindings", null, SettingsProviderControllerConfig.OpenControllerConfig(tabGroup));
            tabGroup.AddTab("Console", null, SettingsProviderConsoleTab.ConsoleTab(tabGroup));
            tabGroup.AddTab("Admin", null, SettingsProviderAdminTab.AdminTab(tabGroup));
            tabGroup.AddTab("Developer", null, DeveloperTab(tabGroup));

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

            // =======================
            // GENERAL
            // =======================
            PanelElementDescriptor generalGroupDeadZone =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);

            generalGroupDeadZone.SetTitle("General");
            generalGroupDeadZone.SetDescription("Basic filtering applied to the whole stick. (excluding look)");

            PanelSlider controllerDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                generalGroupDeadZone,
                PanelSlider.SliderSettings.Advanced(
                    "Radial Dead Zone",
                    0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.ControllerDeadZone);


            // =======================
            // HORIZONTAL (YAW) COMFORT
            // =======================
            PanelElementDescriptor horizontalGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);

            horizontalGroup.SetTitle("Horizontal (Yaw) Comfort");
            horizontalGroup.SetDescription("Prevents forward/back stick pressure from causing accidental left/right drift (\"butterfly wings\").");

            PanelSlider minHorizontalDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                horizontalGroup,
                PanelSlider.SliderSettings.Advanced(
                    "X Dead Zone (Min)",
                    0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Basexdeadzone);

            PanelSlider horizontalGateStrengthSlider = PanelSlider.CreateEntryAndBind(
                horizontalGroup,
                PanelSlider.SliderSettings.Advanced(
                    "X Gate (At Full Y)",
                    0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Extraxdeadzoneatfully);

            PanelSlider wingCurveSlider = PanelSlider.CreateEntryAndBind(
                horizontalGroup,
                PanelSlider.SliderSettings.Advanced(
                    "Gate Curve",
                    0f, 3f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Wingexponent);


            // =======================
            // VERTICAL (PITCH / OTHER)
            // =======================
            PanelElementDescriptor verticalGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);

            verticalGroup.SetTitle("Vertical (Pitch / Other)");
            verticalGroup.SetDescription("look joystick Y Dead Zone");

            PanelSlider verticalDeadZoneSlider = PanelSlider.CreateEntryAndBind(
                verticalGroup,
                PanelSlider.SliderSettings.Advanced(
                    "Look Y Dead Zone",
                    0f, 1f, false, 3, ValueDisplayMode.Percentage),
                BasisSettingsDefaults.Ydeadzone);

            controllerDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
            minHorizontalDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
            horizontalGateStrengthSlider.OnValueChanged += _ => UpdatePreview();
            verticalDeadZoneSlider.OnValueChanged += _ => UpdatePreview();
            wingCurveSlider.OnValueChanged += _ => UpdatePreview();

            descriptor.ForceRebuild();
            return tab;
        }
        private static void UpdatePreview()
        {
            //wire up to butterflygatepreview one day
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

            // After building uniqueResolutions + AssignEntries
            int currentIndex = Mathf.Max(0, uniqueResolutions.FindIndex(r => r.x == Screen.width && r.y == Screen.height));
            dropdownResolution.DropdownComponent.SetValueWithoutNotify(currentIndex);

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
            dropdownScreenMode.DropdownComponent.SetValueWithoutNotify(GetIndexFromScreenMode(Screen.fullScreenMode));
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
        private static int GetIndexFromScreenMode(FullScreenMode FullScreenMode)
        {
            switch (FullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen: return 0;
                case FullScreenMode.FullScreenWindow: return 1;
                case FullScreenMode.Windowed: return 2;
                default: return 2;
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
            PanelDropdown Visual = PanelDropdown.CreateNewEntry(debugGroup.ContentParent);
            Visual.Descriptor.SetTitle("Visual Helpers");
            Visual.AssignEntries(new List<string>
            {
                "Off",
                "all visuals",
                "only avatar distance" 
            });
            Visual.AssignBinding(BasisSettingsDefaults.VisualState);

            // ---- Header / info group ----
            PanelElementDescriptor infoGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            infoGroup.SetTitle("Build & Environment");
            infoGroup.SetDescription("Useful identifiers for debugging builds.");

            CreateBuildInfoSection(infoGroup.ContentParent);


            descriptor.ForceRebuild();
            return tab;
        }

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
        private static void CreateBuildInfoSection(RectTransform parent)
        {
            // Add a "Copy All" action button at the top (optional but handy)
            PanelButton copyAll = PanelButton.CreateNew(parent);
            copyAll.Descriptor.SetTitle("Copy Build Info");
            copyAll.Descriptor.SetDescription("Copies all fields to clipboard.");
            copyAll.OnClicked += () =>
            {
                GUIUtility.systemCopyBuffer = BuildInfoString();
                BasisDebug.Log("Copied build info to clipboard.");
            };

            // Individual rows (selectable + copyable)
            AddInfoRow(parent, "Version", Application.version);
            AddInfoRow(parent, "Unity", Application.unityVersion);
            AddInfoRow(parent, "Platform", Application.platform.ToString());

            // Your own runtime value (keep as-is)
            AddInfoRow(parent, "Mode", Basis.Scripts.Device_Management.BasisDeviceManagement.StaticCurrentMode.ToString());

            AddInfoRow(parent, "Build GUID", Application.buildGUID);
            AddInfoRow(parent, "Log Path", Application.consoleLogPath);
            AddInfoRow(parent, "Data Path", Application.dataPath);
        }

        private static PanelTextField AddInfoRow(RectTransform parent, string title, string value)
        {
            // Uses your existing prefab + styling
            PanelTextField field = PanelTextField.CreateNewEntry(parent);
            field.Descriptor.SetTitle(title);
            field.Descriptor.SetDescription(string.Empty);

            field.SetValueWithoutNotify(value ?? string.Empty);

            // Make it behave like a read-only “info label” but still selectable for copy
            TMP_InputField input = field.GetComponentInChildren<TMP_InputField>(true);
            if (input)
            {
                input.readOnly = true;
                input.interactable = true; // keep selectable
                input.contentType = TMP_InputField.ContentType.Standard;

                // For long paths/GUIDs, multiline reads nicer
                input.lineType = TMP_InputField.LineType.MultiLineNewline;
                input.scrollSensitivity = 2f;
            }
            return field;
        }

        private static string BuildInfoString()
        {
            return
                $"Version: {Application.version}\n" +
                $"Unity: {Application.unityVersion}\n" +
                $"Platform: {Application.platform}\n" +
                $"Mode: {Basis.Scripts.Device_Management.BasisDeviceManagement.StaticCurrentMode}\n" +
                $"Build GUID: {Application.buildGUID}\n" +
                $"Log Path: {Application.consoleLogPath}\n" +
                $"Data Path: {Application.dataPath}";
        }
    }
}
