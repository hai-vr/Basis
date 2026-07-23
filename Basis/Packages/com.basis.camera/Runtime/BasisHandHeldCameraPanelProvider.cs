using System.Collections.Generic;
using Basis.BasisUI.Styling;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI.HandHeldCamera
{
    public class BasisHandHeldCameraPanelProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        public const string StaticTitle = "Camera Settings";

        private static readonly int[] VideoResolutionWidths = { 1280, 1920, 2560, 3840 };
        private static readonly int[] VideoResolutionHeights = { 720, 1080, 1440, 2160 };

        /// <summary>Preview height used until the row has a laid-out width to derive one from.</summary>
        private const float PreviewFallbackHeight = 320f;

        private static BasisHandHeldCameraPanelProvider _instance;

        public override string Title => StaticTitle;
        public override string IconAddress => AddressableAssets.Sprites.CameraSettings;
        public override int Order => 9;
        public override bool Hidden => BasisHandHeldCameraRegistry.Count == 0;

        private BasisMenuPanel _panel;
        private RectTransform _scrollContent;
        private PanelDropdown _selector;
        private PanelElementDescriptor _emptyState;
        private PanelElementDescriptor _previewGroup;
        private PanelElementDescriptor _lookGroup;
        private PanelElementDescriptor _outputGroup;
        private PanelElementDescriptor _actionGroup;
        private PanelSectionToggle _previewSection;
        private PanelSectionToggle _lookSection;
        private PanelSectionToggle _outputSection;
        private PanelSectionToggle _followSection;
        private PanelSectionToggle _actionSection;
        private PanelButton _resetPageButton;
        private PanelButton _timerButton;
        private int _lastCountdownShown = -1;
        private const string TimerIdleLabel = "Timer";
        private RectTransform _topActions;
        private RawImage _previewImage;

        private PanelSlider _fovSlider;
        private PanelSlider _exposureSlider;
        private PanelToggle _exposureOnCameraToggle;
        private PanelSlider _bloomIntensitySlider;
        private PanelSlider _bloomThresholdSlider;
        private PanelSlider _contrastSlider;
        private PanelSlider _saturationSlider;
        private PanelSlider _apertureSlider;
        private PanelSlider _focusSlider;
#if Basis_VOLUMETRIC_SUPPORTED
        private PanelSlider _fogSlider;
#endif

        private PanelElementDescriptor _followGroup;
        private PanelToggle _autoFollowToggle;
        private PanelToggle _followLookAtToggle;
        private PanelSlider _followLookAtHeightSlider;
        private PanelSlider _followSideSlider;
        private PanelSlider _followHeightSlider;
        private PanelSlider _followDistanceSlider;
        private PanelSlider _followYawSlider;
        private PanelSlider _followPitchSlider;

        private PanelDropdown _resolutionDropdown;
        private PanelDropdown _formatDropdown;
        private PanelToggle _recordToggle;
        private PanelToggle _nameplateToggle;
        private PanelToggle _capture360Toggle;
        private PanelToggle _previewScreenToggle;
        private PanelToggle _videoOutputToggle;
        private PanelDropdown _videoResolutionDropdown;
        private PanelSlider _videoFrameRateSlider;
        private PanelTextField _videoSenderNameField;

        private BasisHandHeldCamera _activeCamera;
        private readonly List<BasisHandHeldCamera> _entries = new List<BasisHandHeldCamera>();
        private bool _panelTickSubscribed;
        private bool? _lastVideoOutputActive;
        private bool? _lastRecordingView;
        private bool? _lastPreviewScreenVisible;
        private bool? _lastAutoFollow;
        private bool? _lastExposureOnCamera;

        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            _instance = new BasisHandHeldCameraPanelProvider();
            BasisMenuBase<BasisMainMenu>.AddProvider(_instance);
            BasisHandHeldCameraRegistry.OnChanged += RefreshMainMenu;
        }

        private static void RefreshMainMenu()
        {
            if (BasisMenuBase<BasisMainMenu>.Instance != null)
            {
                BasisMenuBase<BasisMainMenu>.Instance.BindProvidersToButtons();
            }
            if (BasisMainMenu.ActiveMenuTitle != StaticTitle || _instance == null) return;

            // The last camera just closed. Its menu button is already hidden, so a panel left
            // open would strand the user on a page with nothing to drive and no way back to it.
            if (BasisHandHeldCameraRegistry.Count == 0)
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            _instance.RebuildSelector();
        }

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            _panel = panel;

            panel.OnInstanceReleased += OnPanelClosed;

            RectTransform container = panel.Descriptor.ContentParent;
            PanelElementDescriptor scroll = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            _scrollContent = scroll.ContentParent;

            // The shared scroll-view prefab ships a bare, zero-anchored viewport
            // with no mask, so content taller than the panel draws straight past
            // its bounds. Bound the viewport to the scroll rect and mask it.
            if (scroll.TryGetComponent(out ScrollRect scrollRect) && scrollRect.viewport != null)
            {
                RectTransform viewport = scrollRect.viewport;
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = new Vector2(-25f, 0f);
                if (!viewport.TryGetComponent(out RectMask2D _))
                {
                    viewport.gameObject.AddComponent<RectMask2D>();
                }
            }

            BuildTopActions(_scrollContent);

            _selector = PanelDropdown.CreateNewEntry(_scrollContent);
            _selector.Descriptor.SetTitle("Camera");
            _selector.OnValueChanged = _ => OnSelectionChanged();

            _emptyState = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, _scrollContent);
            _emptyState.SetTitle("No Cameras Open");
            _emptyState.SetDescription("Spawn a handheld camera to adjust it here.");

            BuildPreviewGroup(_scrollContent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_previewSection, _previewGroup, true);

            BuildLookGroup(_scrollContent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_lookSection, _lookGroup, false);

            BuildOutputGroup(_scrollContent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_outputSection, _outputGroup, false);

            BuildFollowGroup(_scrollContent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_followSection, _followGroup, false);

            BuildActionsGroup(_scrollContent);
            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(_actionSection, _actionGroup, true);

            BuildResetButton(_scrollContent);

            RebuildSelector();

            SetPanelTickSubscription(true);
        }

        /// <summary>
        /// Page-level reset at the very bottom, mirroring the settings pages: confirm first,
        /// then reset and reopen the panel so every control rebuilds from the new values.
        /// Reuses the settings pages' localization keys so the wording stays in step.
        /// </summary>
        private void BuildResetButton(RectTransform parent)
        {
            _resetPageButton = PanelButton.CreateNew(parent);
            _resetPageButton.Descriptor.SetTitle(BasisLocalization.Get("ui.resetPage.title", StaticTitle));
            _resetPageButton.OnClicked += () =>
            {
                // Hold the camera across the dialogue: closing the panel clears _activeCamera.
                BasisHandHeldCamera camera = _activeCamera;
                if (camera == null) return;

                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("ui.resetPage.title", StaticTitle),
                    BasisLocalization.Get("ui.resetPage.confirm", StaticTitle),
                    BasisLocalization.Get("ui.reset"),
                    BasisLocalization.Get("ui.cancel"),
                    confirmed =>
                    {
                        if (!confirmed || camera == null) return;

                        camera.HandHeld.ResetSettings();
                        BasisMainMenu.Close();
                        BasisMainMenu.OpenWithProvider(StaticTitle);
                    });
            };
        }

        /// <summary>
        /// On desktop this panel and the prop's own HUD compete for the same flat screen, so
        /// the prop HUD steps aside while the panel drives the camera. In VR they sit in
        /// different places and the HUD stays put.
        /// </summary>
        private static void ApplyOnPropUIVisibility(bool panelOpen)
        {
            // Restore unconditionally: the user can leave desktop for VR while the panel is
            // open, and a HUD hidden under the old mode must still come back.
            bool hide = panelOpen && BasisDeviceManagement.IsUserInDesktop();
            IReadOnlyList<BasisHandHeldCamera> cameras = BasisHandHeldCameraRegistry.Cameras;
            for (int Index = 0; Index < cameras.Count; Index++)
            {
                cameras[Index]?.SetOnPropUIHidden(hide);
            }
        }

        private void OnPanelClosed()
        {
            ApplyOnPropUIVisibility(false);
            SetPanelTickSubscription(false);
            _panel = null;
            _scrollContent = null;
            _selector = null;
            _emptyState = null;
            _previewGroup = null;
            _lookGroup = null;
            _outputGroup = null;
            _actionGroup = null;
            _previewImage = null;
            _fovSlider = null;
            _exposureSlider = null;
            _exposureOnCameraToggle = null;
            _bloomIntensitySlider = null;
            _bloomThresholdSlider = null;
            _contrastSlider = null;
            _saturationSlider = null;
            _apertureSlider = null;
            _focusSlider = null;
#if Basis_VOLUMETRIC_SUPPORTED
            _fogSlider = null;
#endif
            _followGroup = null;
            _resetPageButton = null;
            _topActions = null;
            _timerButton = null;
            _lastCountdownShown = -1;
            _previewSection = null;
            _lookSection = null;
            _outputSection = null;
            _followSection = null;
            _actionSection = null;
            _autoFollowToggle = null;
            _followLookAtToggle = null;
            _followLookAtHeightSlider = null;
            _followSideSlider = null;
            _followHeightSlider = null;
            _followDistanceSlider = null;
            _followYawSlider = null;
            _followPitchSlider = null;
            _lastAutoFollow = null;
            _resolutionDropdown = null;
            _formatDropdown = null;
            _recordToggle = null;
            _nameplateToggle = null;
            _capture360Toggle = null;
            _previewScreenToggle = null;
            _videoOutputToggle = null;
            _videoResolutionDropdown = null;
            _videoFrameRateSlider = null;
            _videoSenderNameField = null;
            _activeCamera = null;
            _lastVideoOutputActive = null;
            _lastRecordingView = null;
            _lastPreviewScreenVisible = null;
            _lastExposureOnCamera = null;
            _entries.Clear();
        }

        public override void OnReleaseEvent() => OnPanelClosed();

        private void BuildPreviewGroup(RectTransform parent)
        {
            _previewSection = PanelSectionToggle.CreateNewEntry(parent);
            _previewGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup( _previewSection, parent, "Preview", false);

            // A live RenderTexture can only be drawn by a RawImage, and the element's base
            // image is a UnityEngine.UI.Image, which draws sprites only. Graphic is
            // DisallowMultipleComponent, so the Image has to come off before the RawImage can
            // take its place on the card — and UiStyleImage goes first, since it requires the
            // Image and Unity refuses the removal while it's attached. DestroyImmediate
            // rather than Destroy: a deferred destroy would still hold the slot when
            // AddComponent runs later in this frame.
            RectTransform card = (RectTransform)_previewGroup.transform;
            if (card.TryGetComponent(out UiStyleImage styleImage)) Object.DestroyImmediate(styleImage);

            // Take the card's material with us: it's the overlay-variant UI material the rest
            // of the menu draws with, so the preview keeps sorting on top the way the card
            // did. Read it before the destroy, and don't take the colour — the card is tinted
            // and the feed wants a plain white pass-through.
            Material cardMaterial = null;
            if (card.TryGetComponent(out Graphic baseGraphic))
            {
                cardMaterial = baseGraphic.material;
                Object.DestroyImmediate(baseGraphic);
            }

            _previewImage = card.gameObject.AddComponent<RawImage>();
            _previewImage.raycastTarget = false;
            if (cardMaterial != null) _previewImage.material = cardMaterial;

            // The card shrink-wraps its rows; the header is hidden and Content is empty for
            // this group, so its fitter would collapse the preview flat. Own the height and
            // let ApplyPreviewAspect drive it from the feed.
            if (card.TryGetComponent(out ContentSizeFitter fitter)) fitter.enabled = false;
            card.sizeDelta = new Vector2(card.sizeDelta.x, PreviewFallbackHeight);
        }

        private void BuildLookGroup(RectTransform parent)
        {
            _lookSection = PanelSectionToggle.CreateNewEntry(parent);
            _lookGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _lookSection, parent, "Lens & Grading", false);
            RectTransform content = _lookGroup.ContentParent;

            _fovSlider = PanelSlider.CreateNew(content);
            _fovSlider.SetSliderSettings(PanelSlider.SliderSettings.Degrees("Field Of View", 20f, 120f, false, 1));
            _fovSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeFOV(v);

            _exposureSlider = PanelSlider.CreateNew(content);
            _exposureSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Exposure", 0f, BasisHandHeldCameraUI.ExposureStopCount - 1, true, 0, ValueDisplayMode.Raw));
            _exposureSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeExposureCompensation(v);

            _exposureOnCameraToggle = PanelToggle.CreateNewEntry(content);
            _exposureOnCameraToggle.Descriptor.SetTitle("Exposure On Camera");
            _exposureOnCameraToggle.Descriptor.SetDescription("Also show the exposure slider on the camera's own interface.");
            _exposureOnCameraToggle.OnValueChanged = v =>
            {
                _activeCamera?.HandHeld.SetExposureOnCameraVisible(v);
                // Keep the cache tracking what the widget shows, or switching to a camera
                // that already holds this value would skip the push and strand the knob.
                _lastExposureOnCamera = v;
            };

            _bloomIntensitySlider = PanelSlider.CreateNew(content);
            _bloomIntensitySlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Bloom Intensity", 0f, 5f, false, 2, ValueDisplayMode.Raw));
            _bloomIntensitySlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeBloomIntensity(v);

            _bloomThresholdSlider = PanelSlider.CreateNew(content);
            _bloomThresholdSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Bloom Threshold", 0.1f, 2f, false, 2, ValueDisplayMode.Raw));
            _bloomThresholdSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeBloomThreshold(v);

            _contrastSlider = PanelSlider.CreateNew(content);
            _contrastSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Contrast", -100f, 100f, false, 1, ValueDisplayMode.Raw));
            _contrastSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeContrast(v);

            _saturationSlider = PanelSlider.CreateNew(content);
            _saturationSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Saturation", -100f, 100f, false, 1, ValueDisplayMode.Raw));
            _saturationSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeSaturation(v);

            _apertureSlider = PanelSlider.CreateNew(content);
            _apertureSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Depth Of Field Aperture", 0f, 32f, false, 1, ValueDisplayMode.Raw));
            _apertureSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeAperture(v);

            _focusSlider = PanelSlider.CreateNew(content);
            _focusSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Focus Distance", 0.1f, 100f, false, 1, ValueDisplayMode.Meters));
            _focusSlider.OnValueChanged = v => _activeCamera?.HandHeld.DepthChangeFocusDistance(v);

#if Basis_VOLUMETRIC_SUPPORTED
            _fogSlider = PanelSlider.CreateNew(content);
            _fogSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Volumetric Fog", 0f, 1f, false, 2, ValueDisplayMode.Raw));
            _fogSlider.OnValueChanged = v => _activeCamera?.HandHeld.ChangeVolumetricDensity(v);
#endif
        }

        private void BuildOutputGroup(RectTransform parent)
        {
            _outputSection = PanelSectionToggle.CreateNewEntry(parent);
            _outputGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _outputSection, parent, "Output", false);
            RectTransform content = _outputGroup.ContentParent;

            _recordToggle = PanelToggle.CreateNewEntry(content);
            _recordToggle.Descriptor.SetTitle("Direct To Screen");
            _recordToggle.Descriptor.SetDescription("Render this camera to the main screen in place of your own view. Nothing is written to disk.");
            _recordToggle.OnValueChanged = v =>
            {
                if (_activeCamera == null) return;
                if (_activeCamera.enableRecordingView != v) _activeCamera.OnOverrideDesktopOutputButtonPress();
            };

            _previewScreenToggle = PanelToggle.CreateNewEntry(content);
            _previewScreenToggle.Descriptor.SetTitle("Preview Screen");
            _previewScreenToggle.Descriptor.SetDescription("Spawn a grabbable, resizable screen showing this camera's feed.");
            _previewScreenToggle.OnValueChanged = v => _activeCamera?.SetPreviewScreenVisible(v);

            if (!BasisHandHeldCamera.IsVideoOutputSupported) return;

            _videoOutputToggle = PanelToggle.CreateNewEntry(content);
            _videoOutputToggle.Descriptor.SetTitle($"{BasisHandHeldCamera.VideoOutputBackendName} Output");
            _videoOutputToggle.Descriptor.SetDescription(
                $"Publish this camera as a live video source. {BasisHandHeldCamera.VideoOutputRequirement}");
            _videoOutputToggle.OnValueChanged = v =>
            {
                if (_activeCamera == null) return;
                if (v) _activeCamera.StartVideoOutput();
                else _activeCamera.StopVideoOutput();
                // The click already moved the widget, so the cached value no longer describes
                // it — clear it or a failed start would leave the toggle stuck on.
                _lastVideoOutputActive = null;
                RefreshVideoOutputState();
            };

            _videoResolutionDropdown = PanelDropdown.CreateNewEntry(content);
            _videoResolutionDropdown.Descriptor.SetTitle("Stream Resolution");
            List<string> resolutionLabels = new List<string>();
            for (int Index = 0; Index < VideoResolutionWidths.Length; Index++)
            {
                resolutionLabels.Add($"{VideoResolutionWidths[Index]} x {VideoResolutionHeights[Index]}");
            }
            _videoResolutionDropdown.AssignEntries(resolutionLabels);
            _videoResolutionDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _videoResolutionDropdown == null) return;
                int index = _videoResolutionDropdown.Index;
                if (index < 0 || index >= VideoResolutionWidths.Length) return;
                _activeCamera.SetVideoOutputResolution(VideoResolutionWidths[index], VideoResolutionHeights[index]);
            };

            _videoFrameRateSlider = PanelSlider.CreateNew(content);
            _videoFrameRateSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Stream Frame Rate", 15f, 120f, true, 0, ValueDisplayMode.Hz));
            _videoFrameRateSlider.OnValueChanged = v => _activeCamera?.SetVideoOutputFrameRate(v);

            _videoSenderNameField = PanelTextField.CreateNewEntry(content);
            _videoSenderNameField.Descriptor.SetTitle("Sender Name");
            _videoSenderNameField.OnValueChanged = v =>
            {
                if (_activeCamera == null || string.IsNullOrWhiteSpace(v)) return;
                _activeCamera.VideoOutputSettings.SenderName = v;
            };
        }

        private void BuildFollowGroup(RectTransform parent)
        {
            _followSection = PanelSectionToggle.CreateNewEntry(parent);
            _followGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _followSection, parent, "Follow", false);
            RectTransform content = _followGroup.ContentParent;

            _autoFollowToggle = PanelToggle.CreateNewEntry(content);
            _autoFollowToggle.Descriptor.SetTitle("Auto Follow");
            _autoFollowToggle.OnValueChanged = v => _activeCamera?.SetAutoFollowEnabled(v);

            _followLookAtToggle = PanelToggle.CreateNewEntry(content);
            _followLookAtToggle.Descriptor.SetTitle("Look At Me");
            _followLookAtToggle.Descriptor.SetDescription("Aim at you instead of matching the direction you face.");
            _followLookAtToggle.OnValueChanged = v =>
            {
                if (_activeCamera != null) _activeCamera.autoFollowLookAtPlayer = v;
            };

            _followLookAtHeightSlider = PanelSlider.CreateNew(content);
            _followLookAtHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Look At Height", -1f, 1f, false, 2, ValueDisplayMode.Meters));
            _followLookAtHeightSlider.OnValueChanged = v =>
            {
                if (_activeCamera != null) _activeCamera.autoFollowLookAtHeightOffset = v;
            };

            _followSideSlider = PanelSlider.CreateNew(content);
            _followSideSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Side Offset", -3f, 3f, false, 2, ValueDisplayMode.Meters));
            _followSideSlider.OnValueChanged = v => SetFollowPositionAxis(0, v);

            _followHeightSlider = PanelSlider.CreateNew(content);
            _followHeightSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Height Offset", -2f, 2f, false, 2, ValueDisplayMode.Meters));
            _followHeightSlider.Descriptor.SetDescription("Relative to your calibrated eye level. 0 films you at eyeline.");
            _followHeightSlider.OnValueChanged = v => SetFollowPositionAxis(1, v);

            _followDistanceSlider = PanelSlider.CreateNew(content);
            _followDistanceSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                "Distance", 0.3f, 6f, false, 2, ValueDisplayMode.Meters));
            _followDistanceSlider.OnValueChanged = v => SetFollowPositionAxis(2, v);

            _followYawSlider = PanelSlider.CreateNew(content);
            _followYawSlider.SetSliderSettings(PanelSlider.SliderSettings.Degrees("Yaw Offset", -180f, 180f, false, 1));
            _followYawSlider.OnValueChanged = v => SetFollowRotationAxis(1, v);

            _followPitchSlider = PanelSlider.CreateNew(content);
            _followPitchSlider.SetSliderSettings(PanelSlider.SliderSettings.Degrees("Pitch Offset", -90f, 90f, false, 1));
            _followPitchSlider.OnValueChanged = v => SetFollowRotationAxis(0, v);
        }

        private void SetFollowPositionAxis(int axis, float value)
        {
            if (_activeCamera == null) return;
            Vector3 offset = _activeCamera.autoFollowPositionOffset;
            offset[axis] = value;
            _activeCamera.autoFollowPositionOffset = offset;
        }

        private void SetFollowRotationAxis(int axis, float value)
        {
            if (_activeCamera == null) return;
            Vector3 rotation = _activeCamera.autoFollowRotationOffset;
            rotation[axis] = value;
            _activeCamera.autoFollowRotationOffset = rotation;
        }

        private void BuildActionsGroup(RectTransform parent)
        {
            _actionSection = PanelSectionToggle.CreateNewEntry(parent);
            _actionGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _actionSection, parent, "Capture", false);
            RectTransform content = _actionGroup.ContentParent;

            _resolutionDropdown = PanelDropdown.CreateNewEntry(content);
            _resolutionDropdown.Descriptor.SetTitle("Photo Resolution");
            _resolutionDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _resolutionDropdown == null) return;
                int index = _resolutionDropdown.Index;
                if (index >= 0) _activeCamera.ChangeResolution(index);
            };

            _formatDropdown = PanelDropdown.CreateNewEntry(content);
            _formatDropdown.Descriptor.SetTitle("Photo Format");
            _formatDropdown.AssignEntries(new List<string> { "PNG", "EXR" });
            _formatDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _formatDropdown == null) return;
                int index = _formatDropdown.Index;
                if (index >= 0) _activeCamera.HandHeld.SetFormat(index);
            };

            _nameplateToggle = PanelToggle.CreateNewEntry(content);
            _nameplateToggle.Descriptor.SetTitle("Show Nameplates");
            _nameplateToggle.OnValueChanged = v =>
            {
                if (_activeCamera == null) return;
                if (_activeCamera.ShowUIInCapture != v) _activeCamera.Nameplates();
            };

            _capture360Toggle = PanelToggle.CreateNewEntry(content);
            _capture360Toggle.Descriptor.SetTitle("360 Capture");
            _capture360Toggle.OnValueChanged = v => _activeCamera?.HandHeld.SetCapture360State(v);

        }

        private void BuildTopActions(RectTransform parent)
        {
            _topActions = PanelElementDescriptor.BuildActionRow(parent, "CameraTopActions");

            PanelButton photoButton = PanelButton.CreateNew(_topActions);
            photoButton.Descriptor.SetTitle("Take Photo");
            photoButton.OnClicked += () => _activeCamera?.CapturePhoto();

            _timerButton = PanelButton.CreateNew(_topActions);
            _timerButton.Descriptor.SetTitle(TimerIdleLabel);
            _timerButton.OnClicked += () =>
            {
                if (_activeCamera == null || _activeCamera.IsCountingDown) return;
                _activeCamera.Timer();
                RefreshTimerLabel();
            };

        }

        private void RebuildSelector()
        {
            if (_selector == null) return;

            // Also covers cameras spawned while the panel is already open.
            ApplyOnPropUIVisibility(_panel != null);

            _entries.Clear();
            List<string> labels = new List<string>();
            IReadOnlyList<BasisHandHeldCamera> cameras = BasisHandHeldCameraRegistry.Cameras;
            for (int Index = 0; Index < cameras.Count; Index++)
            {
                BasisHandHeldCamera camera = cameras[Index];
                if (camera == null) continue;
                _entries.Add(camera);
                labels.Add($"{_entries.Count}. {camera.gameObject.name}");
            }

            _selector.AssignEntries(labels);

            if (_entries.Count == 0)
            {
                _selector.gameObject.SetActive(false);
                _emptyState?.SetActive(true);
                SetGroupsActive(false);
                _activeCamera = null;
                return;
            }

            _selector.gameObject.SetActive(_entries.Count > 1);
            _emptyState?.SetActive(false);
            SetGroupsActive(true);

            int selected = _activeCamera != null ? _entries.IndexOf(_activeCamera) : 0;
            if (selected < 0) selected = 0;
            _activeCamera = _entries[selected];
            _selector.SetValueWithoutNotify(labels[selected]);

            ApplyActiveCameraToControls();
        }

        private void OnSelectionChanged()
        {
            if (_selector == null) return;
            int index = _selector.Index;
            if (index < 0 || index >= _entries.Count) return;
            _activeCamera = _entries[index];
            ApplyActiveCameraToControls();
        }

        private void SetGroupsActive(bool active)
        {
            if (_topActions != null) _topActions.gameObject.SetActive(active);
            SetSectionActive(_previewSection, _previewGroup, active);
            SetSectionActive(_lookSection, _lookGroup, active);
            SetSectionActive(_outputSection, _outputGroup, active);
            SetSectionActive(_followSection, _followGroup, active);
            SetSectionActive(_actionSection, _actionGroup, active);
            if (_resetPageButton != null) _resetPageButton.gameObject.SetActive(active);
        }

        private static void SetSectionActive(PanelSectionToggle section, PanelElementDescriptor group, bool active)
        {
            if (section != null) section.gameObject.SetActive(active);
            if (group != null) group.gameObject.SetActive(active && (section == null || section.Expanded));
        }

        private void ApplyActiveCameraToControls()
        {
            if (_activeCamera == null) return;

            BasisHandHeldCameraMetaData metaData = _activeCamera.MetaData;

            if (_activeCamera.captureCamera != null)
            {
                _fovSlider?.SetValueWithoutNotify(_activeCamera.captureCamera.fieldOfView);
            }

            _exposureSlider?.SetValueWithoutNotify(_activeCamera.HandHeld.ExposureIndex);
            SyncToggle(_exposureOnCameraToggle, _activeCamera.HandHeld.ShowExposureOnCamera, ref _lastExposureOnCamera);

            if (metaData.bloom != null)
            {
                _bloomIntensitySlider?.SetValueWithoutNotify(metaData.bloom.intensity.value);
                _bloomThresholdSlider?.SetValueWithoutNotify(metaData.bloom.threshold.value);
            }

            if (metaData.colorAdjustments != null)
            {
                _contrastSlider?.SetValueWithoutNotify(metaData.colorAdjustments.contrast.value);
                _saturationSlider?.SetValueWithoutNotify(metaData.colorAdjustments.saturation.value);
            }

            if (metaData.depthOfField != null)
            {
                _apertureSlider?.SetValueWithoutNotify(metaData.depthOfField.aperture.value);
                _focusSlider?.SetValueWithoutNotify(metaData.depthOfField.focusDistance.value);
            }

#if Basis_VOLUMETRIC_SUPPORTED
            if (metaData.VolumetricFogVolume != null)
            {
                _fogSlider?.SetValueWithoutNotify(metaData.VolumetricFogVolume.density.value);
            }
#endif

            if (_resolutionDropdown != null)
            {
                List<string> labels = new List<string>();
                for (int Index = 0; Index < metaData.resolutions.Length; Index++)
                {
                    labels.Add($"{metaData.resolutions[Index].width} x {metaData.resolutions[Index].height}");
                }
                _resolutionDropdown.AssignEntries(labels);
                int current = FindResolutionIndex(metaData, _activeCamera.captureWidth, _activeCamera.captureHeight);
                if (current >= 0 && current < labels.Count)
                {
                    _resolutionDropdown.SetValueWithoutNotify(labels[current]);
                }
            }

            SyncToggle(_recordToggle, _activeCamera.enableRecordingView, ref _lastRecordingView);
            SyncToggle(_previewScreenToggle, _activeCamera.IsPreviewScreenVisible, ref _lastPreviewScreenVisible);
            _nameplateToggle?.SetValueWithoutNotify(_activeCamera.ShowUIInCapture);
            _capture360Toggle?.SetValueWithoutNotify(_activeCamera.capture360Enabled);
            _formatDropdown?.SetValueWithoutNotify(
                _activeCamera.HandHeld.FormatIndex == BasisHandHeldCameraUI.FORMAT_EXR ? "EXR" : "PNG");

            SyncToggle(_autoFollowToggle, _activeCamera.IsAutoFollowing, ref _lastAutoFollow);
            _followLookAtToggle?.SetValueWithoutNotify(_activeCamera.autoFollowLookAtPlayer);
            _followLookAtHeightSlider?.SetValueWithoutNotify(_activeCamera.autoFollowLookAtHeightOffset);
            _followSideSlider?.SetValueWithoutNotify(_activeCamera.autoFollowPositionOffset.x);
            _followHeightSlider?.SetValueWithoutNotify(_activeCamera.autoFollowPositionOffset.y);
            _followDistanceSlider?.SetValueWithoutNotify(_activeCamera.autoFollowPositionOffset.z);
            _followYawSlider?.SetValueWithoutNotify(_activeCamera.autoFollowRotationOffset.y);
            _followPitchSlider?.SetValueWithoutNotify(_activeCamera.autoFollowRotationOffset.x);

            RefreshVideoOutputState();
            RefreshPreviewTexture();
        }

        private static int FindResolutionIndex(BasisHandHeldCameraMetaData metaData, int width, int height)
        {
            for (int Index = 0; Index < metaData.resolutions.Length; Index++)
            {
                if (metaData.resolutions[Index].width == width && metaData.resolutions[Index].height == height)
                {
                    return Index;
                }
            }
            return -1;
        }

        private void RefreshVideoOutputState()
        {
            if (_activeCamera == null) return;

            SyncToggle(_videoOutputToggle, _activeCamera.IsVideoOutputActive, ref _lastVideoOutputActive);
            _videoFrameRateSlider?.SetValueWithoutNotify(_activeCamera.VideoOutputSettings.FrameRate);
            _videoSenderNameField?.SetValueWithoutNotify(_activeCamera.VideoOutputSettings.SenderName);

            if (_videoResolutionDropdown != null)
            {
                for (int Index = 0; Index < VideoResolutionWidths.Length; Index++)
                {
                    if (VideoResolutionWidths[Index] != _activeCamera.VideoOutputSettings.Width) continue;
                    _videoResolutionDropdown.SetValueWithoutNotify(
                        $"{VideoResolutionWidths[Index]} x {VideoResolutionHeights[Index]}");
                    break;
                }
            }
        }

        private void RefreshPreviewTexture()
        {
            if (_previewImage == null) return;
            Texture feed = _activeCamera != null ? _activeCamera.PreviewTexture : null;
            if (_previewImage.texture != feed) _previewImage.texture = feed;
            _previewImage.enabled = feed != null;
            ApplyPreviewAspect(feed);
        }

        /// <summary>
        /// The layout drives the preview's width but not its height, so height is ours to set.
        /// Derive it from the laid-out width and the feed's aspect instead of stretching a
        /// fixed row, change-gated so it isn't dirtying the layout every frame.
        /// </summary>
        private void ApplyPreviewAspect(Texture feed)
        {
            RectTransform rect = _previewImage.rectTransform;
            float width = rect.rect.width;
            // Width is still zero on the frame the row is built, before the first layout pass.
            if (width <= 1f) return;

            float aspect = (feed != null && feed.height > 0) ? (float)feed.width / feed.height : 16f / 9f;
            float height = width / aspect;
            if (Mathf.Abs(rect.sizeDelta.y - height) < 0.5f) return;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        private void SetPanelTickSubscription(bool subscribe)
        {
            if (subscribe == _panelTickSubscribed) return;
            if (subscribe)
            {
                BasisFrameClock.AddRequest();
                BasisFrameClock.OnTick += OnPanelTick;
            }
            else
            {
                BasisFrameClock.OnTick -= OnPanelTick;
                BasisFrameClock.RemoveRequest();
            }
            _panelTickSubscribed = subscribe;
        }

        private void OnPanelTick()
        {
            if (_activeCamera == null) return;

            RefreshPreviewTexture();

            if (_activeCamera.IsVideoOutputActive != _lastVideoOutputActive)
            {
                RefreshVideoOutputState();
            }

            SyncToggle(_recordToggle, _activeCamera.enableRecordingView, ref _lastRecordingView);
            SyncToggle(_previewScreenToggle, _activeCamera.IsPreviewScreenVisible, ref _lastPreviewScreenVisible);
            SyncToggle(_autoFollowToggle, _activeCamera.IsAutoFollowing, ref _lastAutoFollow);
            RefreshTimerLabel();
        }

        private void RefreshTimerLabel()
        {
            if (_timerButton == null) return;

            int remaining = _activeCamera != null ? _activeCamera.CountdownRemaining : 0;
            if (remaining == _lastCountdownShown) return;
            _lastCountdownShown = remaining;

            _timerButton.Descriptor.SetTitle(remaining > 0 ? $"{TimerIdleLabel} ({remaining})" : TimerIdleLabel);
            _timerButton.SetInteractable(remaining <= 0);
        }

        private static void SyncToggle(PanelToggle toggle, bool value, ref bool? cached)
        {
            if (toggle == null || cached == value) return;
            cached = value;
            toggle.SetValueWithoutNotify(value);
        }
    }
}
