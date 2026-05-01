using System.Linq;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using HVR.Vixxy;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public static class SettingsProviderFaceTracking
    {
        [RuntimeInitializeOnLoadMethod]
        static void Register()
        {
            SettingsProvider.MyAvatarTabOverride = MyAvatarTab;
        }

        public static PanelTabPage MyAvatarTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle("My Avatar");
            descriptor.SetDescription("Face tracking diagnostics and avatar statistics.");

            RectTransform container = descriptor.ContentParent;

            InitializeVixxyPanel(container);

            // The Face Tracking / Eye Tracking master toggles live on the
            // Body Tracking → Advanced page (SettingsProviderIK). This tab
            // shows the diagnostics for whatever the user has enabled there;
            // we subscribe to the bindings so the panels react to changes
            // made from the other tab without forcing a panel reopen.

            // ── Collapsible Face Tracking Status ──
            PanelElementDescriptor faceTrackingSection = null;
            void CreateFaceTrackingSection()
            {
                faceTrackingSection = PanelElementDescriptor.CreateNew(
                    PanelElementDescriptor.ElementStyles.Group, container);
                faceTrackingSection.SetTitle("Face Tracking");
                faceTrackingSection.SetDescription("Live state of the face tracking pipeline. Press Refresh to poll current values.");

                PanelButton refreshButton = PanelButton.CreateNew(faceTrackingSection.ContentParent);
                refreshButton.Descriptor.SetTitle("Refresh");
                refreshButton.Descriptor.SetDescription("Poll the current face tracking state from all components.");

                var fieldFTActive = CreateInfoField(faceTrackingSection.ContentParent, "Face Tracking Active", "...");
                var fieldOSC = CreateInfoField(faceTrackingSection.ContentParent, "OSC Acquisition", "...");
                var fieldBlendshapeActive = CreateInfoField(faceTrackingSection.ContentParent, "Blendshape Tracking", "...");
                var fieldActuatedAddresses = CreateInfoField(faceTrackingSection.ContentParent, "Actuated Addresses", "...");

                void Refresh() => RefreshFaceState(fieldFTActive, fieldOSC, fieldBlendshapeActive, fieldActuatedAddresses);
                refreshButton.OnClicked += Refresh;
                Refresh();
            }

            if (BasisSettingsDefaults.EnableFaceTracking.RawValue)
            {
                CreateFaceTrackingSection();
            }

            System.Action<bool> faceHandler = null;
            faceHandler = on =>
            {
                // Container destroyed (settings panel closed) — drop the leak so
                // we don't keep firing into stale closures.
                if (container == null)
                {
                    BasisSettingsDefaults.EnableFaceTracking.OnChanged -= faceHandler;
                    return;
                }

                if (faceTrackingSection != null)
                {
                    Object.Destroy(faceTrackingSection.gameObject);
                    faceTrackingSection = null;
                }
                if (on) CreateFaceTrackingSection();
            };
            BasisSettingsDefaults.EnableFaceTracking.OnChanged += faceHandler;

            // ── Collapsible Eye Tracking Status ──
            PanelElementDescriptor eyeTrackingSection = null;
            void CreateEyeTrackingSection()
            {
                eyeTrackingSection = PanelElementDescriptor.CreateNew(
                    PanelElementDescriptor.ElementStyles.Group, container);
                eyeTrackingSection.SetTitle("Eye Tracking");
                eyeTrackingSection.SetDescription("State of the eye tracking bone actuation and the natural eye driver.");

                PanelButton refreshButton = PanelButton.CreateNew(eyeTrackingSection.ContentParent);
                refreshButton.Descriptor.SetTitle("Refresh");
                refreshButton.Descriptor.SetDescription("Poll the current eye tracking state from all components.");

                var fieldEyeOverride = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Override", "...");
                var fieldEyeDriverEnabled = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Driver Enabled", "...");
                var fieldEyeParamsActive = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Params Active", "...");
                var fieldEyeLeftX = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Left X", "...");
                var fieldEyeRightX = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Right X", "...");
                var fieldEyeY = CreateInfoField(eyeTrackingSection.ContentParent, "Eye Y", "...");

                void Refresh() => RefreshEyeState(
                    fieldEyeOverride, fieldEyeDriverEnabled, fieldEyeParamsActive,
                    fieldEyeLeftX, fieldEyeRightX, fieldEyeY);
                refreshButton.OnClicked += Refresh;
                Refresh();
            }

            if (BasisSettingsDefaults.EnableEyeTracking.RawValue)
            {
                CreateEyeTrackingSection();
            }

            System.Action<bool> eyeHandler = null;
            eyeHandler = on =>
            {
                if (container == null)
                {
                    BasisSettingsDefaults.EnableEyeTracking.OnChanged -= eyeHandler;
                    return;
                }

                if (eyeTrackingSection != null)
                {
                    Object.Destroy(eyeTrackingSection.gameObject);
                    eyeTrackingSection = null;
                }
                if (on) CreateEyeTrackingSection();
            };
            BasisSettingsDefaults.EnableEyeTracking.OnChanged += eyeHandler;

            // ── Section Toggles ──
            PanelElementDescriptor sectionTogglesGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            sectionTogglesGroup.SetTitle("Sections");
            sectionTogglesGroup.SetDescription("Toggle additional avatar information panels.");

            PanelToggle toggleTextures = PanelToggle.CreateNewEntry(sectionTogglesGroup.ContentParent);
            toggleTextures.Descriptor.SetTitle("Texture Statistics");
            toggleTextures.Descriptor.SetDescription("Show texture, VRAM, and streaming mipmap statistics for the current avatar.");
            toggleTextures.AssignBinding(BasisSettingsDefaults.AvatarShowTextureStats);

            PanelToggle toggleTrackerRoles = PanelToggle.CreateNewEntry(sectionTogglesGroup.ContentParent);
            toggleTrackerRoles.Descriptor.SetTitle("Show Assigned Trackers");
            toggleTrackerRoles.Descriptor.SetDescription("List every input device that has been assigned a tracked bone role.");
            toggleTrackerRoles.AssignBinding(BasisSettingsDefaults.AvatarShowTrackerRoles);

            // ── Collapsible Texture Section (disabled by default) ──
            PanelElementDescriptor textureSection = null;
            void CreateTextureSection()
            {
                textureSection = PanelElementDescriptor.CreateNew(
                    PanelElementDescriptor.ElementStyles.Group, container);
                textureSection.SetTitle("Texture Statistics");
                textureSection.SetDescription("Texture and memory statistics for your current avatar.");
                BuildTextureStats(textureSection.ContentParent);
            }

            if (BasisSettingsDefaults.AvatarShowTextureStats.RawValue)
            {
                CreateTextureSection();
            }

            toggleTextures.OnValueChanged += on =>
            {
                if (textureSection != null)
                {
                    Object.Destroy(textureSection.gameObject);
                    textureSection = null;
                }
                if (on) CreateTextureSection();
            };

            // ── Collapsible Tracker Roles Section (disabled by default) ──
            PanelElementDescriptor trackerRolesSection = null;
            void CreateTrackerRolesSection()
            {
                trackerRolesSection = PanelElementDescriptor.CreateNew(
                    PanelElementDescriptor.ElementStyles.Group, container);
                trackerRolesSection.SetTitle("Assigned Trackers");
                SettingsProviderAvatarStats.PopulateTrackerRoles(trackerRolesSection);
            }

            if (BasisSettingsDefaults.AvatarShowTrackerRoles.RawValue)
            {
                CreateTrackerRolesSection();
            }

            toggleTrackerRoles.OnValueChanged += on =>
            {
                if (trackerRolesSection != null)
                {
                    Object.Destroy(trackerRolesSection.gameObject);
                    trackerRolesSection = null;
                }
                if (on) CreateTrackerRolesSection();
            };

            descriptor.ForceRebuild();
            return tab;
        }

        static void RefreshFaceState(
            PanelElementDescriptor ftActive,
            PanelElementDescriptor oscAcquisition,
            PanelElementDescriptor blendshapeActive,
            PanelElementDescriptor actuatedAddresses)
        {
            BasisLocalPlayer localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer != null ? localPlayer.BasisAvatar : null;

            FaceTrackingActivityRelay relay = avatar != null
                ? avatar.GetComponentInChildren<FaceTrackingActivityRelay>(true)
                : null;
            ftActive.SetDescription(relay != null ? relay.IsTrackingActive.ToString() : "No relay found");

            OSCAcquisition oscAcq = avatar != null
                ? avatar.GetComponentInChildren<OSCAcquisition>(true)
                : null;
            if (oscAcq != null)
                oscAcquisition.SetDescription(oscAcq.isActiveAndEnabled ? "Active" : "DISABLED");
            else
                oscAcquisition.SetDescription("No component");

            BlendshapeActuation blendshape = avatar != null
                ? avatar.GetComponentInChildren<BlendshapeActuation>(true)
                : null;
            if (blendshape != null)
            {
                blendshapeActive.SetDescription(blendshape.IsTrackingActive.ToString());
                int addressCount = blendshape.debugAddresses != null ? blendshape.debugAddresses.Length : 0;
                actuatedAddresses.SetDescription(addressCount.ToString());
            }
            else
            {
                blendshapeActive.SetDescription("No component");
                actuatedAddresses.SetDescription("--");
            }
        }

        static void RefreshEyeState(
            PanelElementDescriptor eyeOverride,
            PanelElementDescriptor eyeDriverEnabled,
            PanelElementDescriptor eyeParamsActive,
            PanelElementDescriptor eyeLeftX,
            PanelElementDescriptor eyeRightX,
            PanelElementDescriptor eyeY)
        {
            BasisLocalPlayer localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer != null ? localPlayer.BasisAvatar : null;

            eyeOverride.SetDescription(BasisLocalEyeDriver.Override.ToString());
            eyeDriverEnabled.SetDescription(BasisLocalEyeDriver.IsEnabled.ToString());

            EyeTrackingBoneActuation eyeActuation = avatar != null
                ? avatar.GetComponentInChildren<EyeTrackingBoneActuation>(true)
                : null;
            if (eyeActuation != null)
            {
                eyeParamsActive.SetDescription(eyeActuation.IsEyeTrackingParametersActive.ToString());
                eyeLeftX.SetDescription(eyeActuation._fEyeLeftX.ToString("F3"));
                eyeRightX.SetDescription(eyeActuation._fEyeRightX.ToString("F3"));
                eyeY.SetDescription(eyeActuation._fEyeY.ToString("F3"));
            }
            else
            {
                eyeParamsActive.SetDescription("No component");
                eyeLeftX.SetDescription("--");
                eyeRightX.SetDescription("--");
                eyeY.SetDescription("--");
            }
        }

        static void BuildTextureStats(RectTransform container)
        {
            PanelButton scanButton = PanelButton.CreateNew(container);
            scanButton.Descriptor.SetTitle("Scan Avatar");
            scanButton.Descriptor.SetDescription("Analyze your current avatar's textures, VRAM usage, and streaming mipmap status.");
            scanButton.OnClicked += () =>
            {
                Object.Destroy(scanButton.gameObject);
                SettingsProviderAvatarStats.PopulateStatsInto(container);
            };
        }

        static PanelElementDescriptor CreateInfoField(RectTransform parent, string title, string initialValue)
        {
            var field = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            field.SetTitle(title);
            field.SetDescription(initialValue);
            return field;
        }

        private static void InitializeVixxyPanel(RectTransform container)
        {
            var localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer?.BasisAvatar;
            if (avatar == null) return;

            var menuItems = avatar.GetComponentsInChildren<HVRVixxyMenuItem>(true);
            if (menuItems.Length <= 0) return;

            var menuGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            menuGroup.SetTitle("Vixxy");
            menuGroup.SetDescription("Trigger effects on this avatar.");

            foreach (var menuItem in menuItems)
            {
                var hasControl = menuItem.TryResolveActualControl(out var control);
                if (!hasControl) continue;

                if (menuItem.presentation == HVRVixxyControlPresentation.Slider)
                {
                    BuildSlider(menuGroup, control, menuItem);
                }
                else
                {
                    if (!control.HasThreeOrMoreChoices)
                    {
                        BuildToggle(menuGroup, menuItem, control);
                    }
                    else
                    {
                        BuildDropdown(control, menuGroup, menuItem);
                    }
                }
            }
        }

        private static void BuildToggle(PanelElementDescriptor menuGroup, HVRVixxyMenuItem menuItem, HVRVixxyControl control)
        {
            var toggle = PanelToggle.CreateNewEntry(menuGroup.ContentParent);
            toggle.Descriptor.SetTitle(menuItem.ResolveTitle());
            toggle.Descriptor.SetDescription(menuItem.ResolveDescription());
            toggle.OnValueChanged += value =>
            {
                menuItem.ApplyValue(value ? control.Max() : control.Min());
                toggle.Descriptor.SetTitle(menuItem.ResolveTitle());
                toggle.Descriptor.SetDescription(menuItem.ResolveDescription());
            };
            toggle.SetValueWithoutNotify(!Mathf.Approximately(menuItem.GetValue(), control.Min()));
        }

        private static void BuildSlider(PanelElementDescriptor menuGroup, HVRVixxyControl control, HVRVixxyMenuItem menuItem)
        {
            var slider = PanelSlider.CreateNew(menuGroup.ContentParent);
            slider.SetSliderSettings(new PanelSlider.SliderSettings
            {
                SliderMin = control.Min(),
                SliderMax = control.Max(),
                DecimalPlaces = 2,
                DisplayMode = ValueDisplayMode.Percentage,
            });
            slider.Descriptor.SetTitle(menuItem.ResolveTitle());
            slider.Descriptor.SetDescription(menuItem.ResolveDescription());
            void WhenValueChanged(float value)
            {
                menuItem.ApplyValue(value);
                slider.Descriptor.SetTitle(menuItem.ResolveTitle());
                slider.Descriptor.SetDescription(menuItem.ResolveDescription());
            }
            slider.SliderComponent.onValueChanged.AddListener(WhenValueChanged);
            slider.OnValueChanged += WhenValueChanged;
            slider.SetValueWithoutNotify(menuItem.GetValue());
        }

        private static void BuildDropdown(HVRVixxyControl control, PanelElementDescriptor menuGroup, HVRVixxyMenuItem menuItem)
        {
            var choiceStrings = control.choices.Select((choice, i) =>
            {
                if (string.IsNullOrWhiteSpace(choice.title)) return $"Option #{(i + 1)}";
                return $"{choice.title} (#{i + 1})";
            }).ToList();

            var dropdown = PanelDropdown.CreateNewEntry(menuGroup.ContentParent);
            dropdown.Descriptor.SetTitle(menuItem.ResolveTitle());
            dropdown.AssignEntries(choiceStrings);
            dropdown.OnValueChanged += choice =>
            {
                var valueForThatChoice = control.choices[choiceStrings.IndexOf(choice)].value;
                BasisDebug.Log($"Selected {choice}, value is {valueForThatChoice}");
                menuItem.ApplyValue(valueForThatChoice);
                dropdown.Descriptor.SetTitle(menuItem.ResolveTitle());
            };
            var currentValue = (int)menuItem.GetValue();
            var matchingChoice = control.choices.FirstOrDefault(choice => Mathf.Approximately(choice.value, currentValue));
            var currentChoice = matchingChoice != null ? control.choices.ToList().IndexOf(matchingChoice) : -1;
            BasisDebug.Log($"Current choice is {currentChoice}, choicestring count is {choiceStrings.Count}");
            if (currentChoice >= 0 && currentChoice < choiceStrings.Count)
            {
                dropdown.SetValueWithoutNotify(choiceStrings[currentChoice]);
            }
        }
    }
}
