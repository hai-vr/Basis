using Basis.Scripts.Device_Management;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit front end for the handheld camera. Drives the same
/// <see cref="BasisHandHeldCameraUI"/> API the uGUI canvas does, so both surfaces stay in
/// agreement — this component owns presentation only, never camera state.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class BasisHandHeldCameraToolkitUI : MonoBehaviour
{
    public BasisHandHeldCamera HandHeldCamera;
    public UIDocument Document;

    private const string OnClass = "icon-btn--on";
    private const int RefreshIntervalMs = 250;

    private VisualElement Root;
    private VisualElement Viewfinder;

    // Snapshot of camera state taken at bind time. The camera keeps no persistent settings
    // object — CreateCurrentCameraSettings() builds one on demand — so indices the user changes
    // are written back here rather than re-snapshotting (which would allocate every refresh).
    private BasisHandHeldCameraUI.CameraSettings Snapshot;

    private Button SelfieButton;
    private Button AutoLevelButton;
    private Button SmoothingButton;
    private Button FollowButton;
    private Button FlyButton;
    private Button NameplatesButton;
    private Button DesktopButton;
    private Button VideoButton;

    private Label ResolutionPill;
    private Label FormatPill;
    private Label Capture360Pill;
    private Label LivePill;

    private Label FovReadout;
    private Label ExposureReadout;
    private Label ApertureReadout;
    private Label ShutterReadout;
    private Label IsoReadout;

    private Toggle DepthToggle;
    private VisualElement DepthFocusRow;
    private VisualElement DepthApertureRow;
    private Button DepthAutoButton;
    private Button DepthManualButton;
    private VisualElement VideoCard;

    private void OnEnable()
    {
        if (Document == null)
        {
            TryGetComponent(out Document);
        }

        if (HandHeldCamera == null)
        {
            HandHeldCamera = GetComponentInParent<BasisHandHeldCamera>();
        }

        Root = Document != null ? Document.rootVisualElement : null;
        if (Root == null || HandHeldCamera == null || HandHeldCamera.HandHeld == null)
        {
            BasisDebug.LogWarning($"{nameof(BasisHandHeldCameraToolkitUI)} could not resolve its document or camera; UI not bound.", BasisDebug.LogTag.Input);
            return;
        }

        Snapshot = HandHeldCamera.HandHeld.CreateCurrentCameraSettings();
        Bind();
        RefreshAll();
        Root.schedule.Execute(RefreshAll).Every(RefreshIntervalMs);
    }

    private void Bind()
    {
        BasisHandHeldCameraUI ui = HandHeldCamera.HandHeld;

        Viewfinder = Root.Q<VisualElement>("viewfinder");
        RefreshViewfinder();

        SelfieButton = BindButton("btn-selfie", ui.SelfieToggle);
        AutoLevelButton = BindButton("btn-autolevel", ui.ToggleAutoLevel);
        SmoothingButton = BindButton("btn-smoothing", ui.ToggleVRHandheldSmoothing);
        FollowButton = BindButton("btn-follow", ui.ToggleFollowPlayer);
        FlyButton = BindButton("btn-fly", ui.ToggleFlyMode);
        NameplatesButton = BindButton("btn-nameplates", HandHeldCamera.Nameplates);
        DesktopButton = BindButton("btn-desktop", HandHeldCamera.OnOverrideDesktopOutputButtonPress);
        VideoButton = BindButton("btn-video", ui.ToggleVideoOutput);

        BindButton("btn-close", ui.CloseUI);
        BindButton("btn-shutter", HandHeldCamera.CapturePhoto);
        BindButton("btn-timer", HandHeldCamera.Timer);
        BindButton("btn-reset", () =>
        {
            ui.ResetSettings();
            Snapshot = ui.CreateCurrentCameraSettings();
            RefreshViewfinder();
        });

        BindButton("btn-resolution", () =>
        {
            ui.CycleResolutionPreset();
            Snapshot.resolutionIndex = (Snapshot.resolutionIndex + 1) % Mathf.Max(1, HandHeldCamera.MetaData.resolutions.Length);
            RefreshViewfinder();
        });
        BindButton("btn-format", () => ui.OnFormatToggleChanged(HandHeldCamera.captureFormat != "EXR"));
        BindButton("btn-360", () => ui.SetCapture360State(!HandHeldCamera.capture360Enabled));

        ResolutionPill = Root.Q<Label>("pill-res");
        FormatPill = Root.Q<Label>("pill-format");
        Capture360Pill = Root.Q<Label>("pill-360");
        LivePill = Root.Q<Label>("pill-live");

        FovReadout = Root.Q<Label>("read-fov");
        ExposureReadout = Root.Q<Label>("read-ev");
        ApertureReadout = Root.Q<Label>("read-aperture");
        ShutterReadout = Root.Q<Label>("read-shutter");
        IsoReadout = Root.Q<Label>("read-iso");

        BindSlider("sld-fov", "val-fov", HandHeldCamera.captureCamera.fieldOfView,
            v => ui.ChangeFOV(v), v => $"{v:0}°");

        BindSlider("sld-ev", "val-ev", Snapshot.exposureIndex,
            v => { ui.ChangeExposureCompensation(v); Snapshot.exposureIndex = Mathf.RoundToInt(v); }, FormatExposure);

        BindSlider("sld-bloom-i", "val-bloom-i", Snapshot.bloomIntensity,
            v => ui.ChangeBloomIntensity(v), v => $"{v:0.00}");

        BindSlider("sld-bloom-t", "val-bloom-t", Snapshot.bloomThreshold,
            v => ui.ChangeBloomThreshold(v), v => $"{v:0.00}");

        BindSlider("sld-contrast", "val-contrast", Snapshot.contrast,
            v => ui.ChangeContrast(v), v => $"{v:0}");

        BindSlider("sld-saturation", "val-saturation", Snapshot.saturation,
            v => ui.ChangeSaturation(v), v => $"{v:0}");

        BindSlider("sld-fog", "val-fog", Snapshot.VolumetricFogVolumedensity,
            v => ui.ChangeVolumetricDensity(v), v => $"{v * 100f:0}%");

        BindSlider("sld-dof-focus", "val-dof-focus", Snapshot.depthFocusDistance,
            v => ui.DepthChangeFocusDistance(v), v => $"{v:0.0} m");

        BindSlider("sld-dof-aperture", "val-dof-aperture", Snapshot.depthAperture,
            v => ui.ChangeAperture(v), v => $"{v:0.0}");

        BindSlider("sld-follow-h", "val-follow-h", HandHeldCamera.followHeightOffset,
            v => ui.ChangeFollowHeight(v), v => $"{v:0.00} m");

        BindSlider("sld-follow-s", "val-follow-s", HandHeldCamera.followHorizontalOffset,
            v => ui.ChangeFollowHorizontal(v), v => $"{v:0.00} m");

        BindSlider("sld-vid-res", "val-vid-res", FindVideoResolutionIndex(),
            v => ui.ChangeVideoResolution(v), FormatVideoResolution);

        BindSlider("sld-vid-fps", "val-vid-fps", Snapshot.videoOutputFrameRate,
            v => ui.ChangeVideoFrameRate(v), v => $"{v:0} fps");

        BindDropdown("dd-aperture", HandHeldCamera.MetaData.apertures, Snapshot.apertureIndex,
            i => { ui.ChangeAperture(i); Snapshot.apertureIndex = i; });

        BindDropdown("dd-shutter", HandHeldCamera.MetaData.shutterSpeeds, Snapshot.shutterSpeedIndex,
            i => { ui.ChangeShutterSpeed(i); Snapshot.shutterSpeedIndex = i; });

        BindDropdown("dd-iso", HandHeldCamera.MetaData.isoValues, Snapshot.isoIndex,
            i => { ui.ChangeISO(i); Snapshot.isoIndex = i; });

        DepthToggle = Root.Q<Toggle>("tgl-dof");
        DepthFocusRow = Root.Q<VisualElement>("row-dof-focus");
        DepthApertureRow = Root.Q<VisualElement>("row-dof-aperture");
        if (DepthToggle != null)
        {
            DepthToggle.RegisterValueChangedCallback(evt =>
            {
                SetDepthOfFieldActive(evt.newValue);
                RefreshAll();
            });
        }

        DepthAutoButton = BindButton("btn-dof-auto", () => ui.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Auto));
        DepthManualButton = BindButton("btn-dof-manual", () => ui.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Manual));

        VideoCard = Root.Q<VisualElement>("card-video");
        ApplyPlatformVisibility();
    }

    private Button BindButton(string elementName, Action action)
    {
        Button button = Root.Q<Button>(elementName);
        if (button == null)
        {
            return null;
        }

        button.clicked += () =>
        {
            action();
            RefreshAll();
        };
        return button;
    }

    private void BindSlider(string sliderName, string valueName, float initial, Action<float> apply, Func<float, string> format)
    {
        Slider slider = Root.Q<Slider>(sliderName);
        if (slider == null)
        {
            return;
        }

        Label readout = Root.Q<Label>(valueName);
        slider.SetValueWithoutNotify(Mathf.Clamp(initial, slider.lowValue, slider.highValue));
        if (readout != null)
        {
            readout.text = format(slider.value);
        }

        slider.RegisterValueChangedCallback(evt =>
        {
            apply(evt.newValue);
            if (readout != null)
            {
                readout.text = format(evt.newValue);
            }
        });
    }

    private void BindDropdown(string elementName, string[] choices, int initialIndex, Action<int> apply)
    {
        DropdownField dropdown = Root.Q<DropdownField>(elementName);
        if (dropdown == null || choices == null || choices.Length == 0)
        {
            return;
        }

        dropdown.choices = new List<string>(choices);
        dropdown.index = Mathf.Clamp(initialIndex, 0, choices.Length - 1);
        dropdown.RegisterValueChangedCallback(_ =>
        {
            apply(dropdown.index);
            RefreshAll();
        });
    }

    /// <summary>
    /// The preview render target is recreated whenever the resolution changes, so the
    /// background has to be re-pointed rather than bound once.
    /// </summary>
    public void RefreshViewfinder()
    {
        if (Viewfinder == null || HandHeldCamera.captureCamera == null)
        {
            return;
        }

        RenderTexture target = HandHeldCamera.captureCamera.targetTexture;
        if (target != null)
        {
            Viewfinder.style.backgroundImage = Background.FromRenderTexture(target);
        }
    }

    public void RefreshAll()
    {
        if (Root == null || HandHeldCamera == null || Snapshot == null)
        {
            return;
        }

        SetOn(SelfieButton, HandHeldCamera.HandHeld.IsSelfieMode);
        SetOn(AutoLevelButton, HandHeldCamera.useAutoLeveling);
        SetOn(SmoothingButton, HandHeldCamera.useVRHandheldSmoothing);
        SetOn(FollowButton, HandHeldCamera.IsFollowingPlayer);
        SetOn(FlyButton, HandHeldCamera.IsFlying);
        SetOn(NameplatesButton, HandHeldCamera.ShowUIInCapture);
        SetOn(DesktopButton, HandHeldCamera.enableRecordingView);
        SetOn(VideoButton, HandHeldCamera.IsVideoOutputActive);

        int resolutionIndex = Mathf.Clamp(Snapshot.resolutionIndex, 0, HandHeldCamera.MetaData.resolutions.Length - 1);
        SetText(ResolutionPill, $"{HandHeldCamera.MetaData.resolutions[resolutionIndex].height}P");
        SetText(FormatPill, HandHeldCamera.captureFormat);

        Capture360Pill?.EnableInClassList("pill--accent", HandHeldCamera.capture360Enabled);
        SetDisplayed(LivePill, HandHeldCamera.IsVideoOutputActive);
        Viewfinder?.EnableInClassList("viewfinder--recording", HandHeldCamera.IsVideoOutputActive);

        SetText(FovReadout, $"{HandHeldCamera.captureCamera.fieldOfView:0}");
        SetText(ExposureReadout, FormatExposure(Snapshot.exposureIndex));
        SetText(ApertureReadout, Choice(HandHeldCamera.MetaData.apertures, Snapshot.apertureIndex));
        SetText(ShutterReadout, Choice(HandHeldCamera.MetaData.shutterSpeeds, Snapshot.shutterSpeedIndex));
        SetText(IsoReadout, Choice(HandHeldCamera.MetaData.isoValues, Snapshot.isoIndex));

        bool depthActive = IsDepthOfFieldActive();
        bool manual = HandHeldCamera.HandHeld.currentDepthMode == BasisHandHeldCameraUI.DepthMode.Manual;
        DepthToggle?.SetValueWithoutNotify(depthActive);
        SetOn(DepthAutoButton, depthActive && !manual);
        SetOn(DepthManualButton, depthActive && manual);
        SetDisplayed(DepthApertureRow, depthActive);
        SetDisplayed(DepthFocusRow, depthActive && manual);
    }

    private void ApplyPlatformVisibility()
    {
        SetDisplayed(VideoCard, BasisHandHeldCamera.IsVideoOutputSupported);
        SetDisplayed(VideoButton, BasisHandHeldCamera.IsVideoOutputSupported);
        SetDisplayed(DesktopButton, !BasisDeviceManagement.IsUserInDesktop());
    }

    private bool IsDepthOfFieldActive()
    {
        return HandHeldCamera.MetaData != null
            && HandHeldCamera.MetaData.depthOfField != null
            && HandHeldCamera.MetaData.depthOfField.active;
    }

    private void SetDepthOfFieldActive(bool active)
    {
        if (HandHeldCamera.MetaData == null || HandHeldCamera.MetaData.depthOfField == null)
        {
            return;
        }

        HandHeldCamera.MetaData.depthOfField.active = active;
        HandHeldCamera.HandHeld.SetDepthMode(HandHeldCamera.HandHeld.currentDepthMode);
    }

    private int FindVideoResolutionIndex()
    {
        int width = Snapshot.videoOutputWidth;
        for (int i = 0; i < BasisHandHeldCameraUI.VideoResolutionPresets.Length; i++)
        {
            if (BasisHandHeldCameraUI.VideoResolutionPresets[i].width == width)
            {
                return i;
            }
        }
        return 1;
    }

    private static string FormatVideoResolution(float index)
    {
        int clamped = Mathf.Clamp(Mathf.RoundToInt(index), 0, BasisHandHeldCameraUI.VideoResolutionPresets.Length - 1);
        (int width, int height) preset = BasisHandHeldCameraUI.VideoResolutionPresets[clamped];
        return $"{preset.width}x{preset.height}";
    }

    private static string FormatExposure(float index)
    {
        int clamped = Mathf.Clamp(Mathf.RoundToInt(index), 0, BasisHandHeldCameraUI.ExposureStops.Length - 1);
        float stop = BasisHandHeldCameraUI.ExposureStops[clamped];
        return stop > 0f ? $"+{stop:0.0} EV" : $"{stop:0.0} EV";
    }

    private static string Choice(string[] values, int index)
    {
        if (values == null || values.Length == 0)
        {
            return "--";
        }
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }

    private static void SetOn(VisualElement element, bool on)
    {
        element?.EnableInClassList(OnClass, on);
    }

    private static void SetText(Label label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    private static void SetDisplayed(VisualElement element, bool displayed)
    {
        if (element != null)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
