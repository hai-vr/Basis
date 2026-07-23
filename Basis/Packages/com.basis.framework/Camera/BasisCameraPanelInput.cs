using Basis.Scripts.Device_Management;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Self-contained input controller for ONE UI Toolkit camera panel. Drop it on every panel — it
/// discovers whichever named controls its own panel happens to contain, wires those to the camera
/// API, and refreshes only the readouts it owns. No central binder: panels never talk to each
/// other, they read and write the camera, so a change made on one panel shows up on another purely
/// because both read the same live camera state.
/// </summary>
[RequireComponent(typeof(Basis.Scripts.UI.BasisUIToolkitPanel))]
public class BasisCameraPanelInput : MonoBehaviour
{
    public BasisHandHeldCamera HandHeldCamera;
    public Basis.Scripts.UI.BasisUIToolkitPanel Panel;

    private const string OnClass = "icon-btn--on";
    private const int RefreshIntervalMs = 200;

    private VisualElement Root;
    private readonly List<Action> Refreshers = new List<Action>();

    private BasisHandHeldCameraUI UI => HandHeldCamera != null ? HandHeldCamera.HandHeld : null;

    private void OnEnable()
    {
        if (Panel == null)
        {
            TryGetComponent(out Panel);
        }

        if (HandHeldCamera == null)
        {
            HandHeldCamera = GetComponentInParent<BasisHandHeldCamera>();
        }

        if (Panel == null || UI == null)
        {
            BasisDebug.LogWarning($"{nameof(BasisCameraPanelInput)} on {name}: missing panel or camera; not bound.", BasisDebug.LogTag.Input);
            return;
        }

        if (Panel.Root != null)
        {
            Bind(Panel.Root);
        }
        else
        {
            Panel.RootResolved += Bind;
        }
    }

    private void OnDisable()
    {
        if (Panel != null)
        {
            Panel.RootResolved -= Bind;
        }
        Refreshers.Clear();
        Root = null;
    }

    private void Bind(VisualElement root)
    {
        Root = root;
        Refreshers.Clear();
        BasisHandHeldCameraUI ui = UI;

        // ---- mode toggles (each shows its own on/off state) ----
        WireToggleButton("btn-selfie", ui.SelfieToggle, () => ui.IsSelfieMode);
        WireToggleButton("btn-autolevel", ui.ToggleAutoLevel, () => HandHeldCamera.useAutoLeveling);
        WireToggleButton("btn-smoothing", ui.ToggleVRHandheldSmoothing, () => HandHeldCamera.useVRHandheldSmoothing);
        WireToggleButton("btn-follow", ui.ToggleFollowPlayer, () => HandHeldCamera.IsFollowingPlayer);
        WireToggleButton("btn-fly", ui.ToggleFlyMode, () => HandHeldCamera.IsFlying);
        WireToggleButton("btn-nameplates", HandHeldCamera.Nameplates, () => HandHeldCamera.ShowUIInCapture);
        WireToggleButton("btn-desktop", HandHeldCamera.OnOverrideDesktopOutputButtonPress, () => HandHeldCamera.enableRecordingView);
        WireToggleButton("btn-video", ui.ToggleVideoOutput, () => HandHeldCamera.IsVideoOutputActive);

        // ---- plain action buttons ----
        WireButton("btn-close", ui.CloseUI);
        WireButton("btn-shutter", HandHeldCamera.CapturePhoto);
        WireButton("btn-timer", HandHeldCamera.Timer);
        WireButton("btn-reset", ui.ResetSettings);
        WireButton("btn-resolution", ui.CycleResolutionPreset);
        WireButton("btn-format", () => ui.OnFormatToggleChanged(HandHeldCamera.captureFormat != "EXR"));
        WireButton("btn-360", () => ui.SetCapture360State(!HandHeldCamera.capture360Enabled));

        // ---- status pills ----
        WirePill("pill-res", () => $"{CurrentResolutionHeight()}P");
        WirePill("pill-format", () => HandHeldCamera.captureFormat);
        WirePillState("pill-360", "pill--accent", () => HandHeldCamera.capture360Enabled);
        WirePillVisible("pill-live", () => HandHeldCamera.IsVideoOutputActive);

        // ---- exposure-triangle readouts (all read live off the capture camera) ----
        WireReadout("read-fov", () => $"{CaptureCamera().fieldOfView:0}");
        WireReadout("read-ev", () => FormatEv(PostExposure()));
        WireReadout("read-aperture", () => $"f/{CaptureCamera().aperture:0.0}");
        WireReadout("read-shutter", () => FormatShutter(CaptureCamera().shutterSpeed));
        WireReadout("read-iso", () => $"{CaptureCamera().iso:0}");

        // ---- lens / image sliders ----
        WireSlider("sld-fov", "val-fov", CaptureCamera().fieldOfView, ui.ChangeFOV, v => $"{v:0}°");
        WireSlider("sld-ev", "val-ev", EvIndexFromPostExposure(), ui.ChangeExposureCompensation, FormatEvIndex);
        WireSlider("sld-bloom-i", "val-bloom-i", BloomIntensity(), ui.ChangeBloomIntensity, v => $"{v:0.00}");
        WireSlider("sld-bloom-t", "val-bloom-t", BloomThreshold(), ui.ChangeBloomThreshold, v => $"{v:0.00}");
        WireSlider("sld-contrast", "val-contrast", Contrast(), ui.ChangeContrast, v => $"{v:0}");
        WireSlider("sld-saturation", "val-saturation", Saturation(), ui.ChangeSaturation, v => $"{v:0}");
        WireSlider("sld-fog", "val-fog", VolumetricDensity(), ui.ChangeVolumetricDensity, v => $"{v * 100f:0}%");

        // ---- depth of field ----
        WireDepthOfField(ui);

        // ---- follow ----
        WireSlider("sld-follow-h", "val-follow-h", HandHeldCamera.followHeightOffset, ui.ChangeFollowHeight, v => $"{v:0.00} m");
        WireSlider("sld-follow-s", "val-follow-s", HandHeldCamera.followHorizontalOffset, ui.ChangeFollowHorizontal, v => $"{v:0.00} m");

        // ---- video output ----
        WireVideo(ui);

        // ---- exposure dropdowns ----
        WireDropdown("dd-aperture", HandHeldCamera.MetaData.apertures, NearestIndex(HandHeldCamera.MetaData.apertures, $"f/{CaptureCamera().aperture:0.0}"), ui.ChangeAperture);
        WireDropdown("dd-shutter", HandHeldCamera.MetaData.shutterSpeeds, 0, ui.ChangeShutterSpeed);
        WireDropdown("dd-iso", HandHeldCamera.MetaData.isoValues, NearestIndex(HandHeldCamera.MetaData.isoValues, $"{CaptureCamera().iso:0}"), ui.ChangeISO);

        // ---- click-to-focus on a preview element, if this panel carries one ----
        WireViewfinder();

        // First paint, then a lightweight polling refresh for the live readouts this panel owns.
        RunRefreshers();
        if (Refreshers.Count > 0)
        {
            Root.schedule.Execute(RunRefreshers).Every(RefreshIntervalMs);
        }
    }

    private void RunRefreshers()
    {
        for (int i = 0; i < Refreshers.Count; i++)
        {
            Refreshers[i]();
        }
    }

    // ---------------------------------------------------------------- wiring helpers

    private void WireButton(string elementName, Action action)
    {
        Button button = Root.Q<Button>(elementName);
        if (button != null)
        {
            button.clicked += action;
        }
    }

    private void WireToggleButton(string elementName, Action action, Func<bool> isOn)
    {
        Button button = Root.Q<Button>(elementName);
        if (button == null)
        {
            return;
        }

        button.clicked += () =>
        {
            action();
            button.EnableInClassList(OnClass, isOn());
        };
        Refreshers.Add(() => button.EnableInClassList(OnClass, isOn()));
    }

    private void WireSlider(string sliderName, string valueName, float initial, Action<float> apply, Func<float, string> format)
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

    private void WireDropdown(string elementName, string[] choices, int initialIndex, Action<int> apply)
    {
        DropdownField dropdown = Root.Q<DropdownField>(elementName);
        if (dropdown == null || choices == null || choices.Length == 0)
        {
            return;
        }

        dropdown.choices = new List<string>(choices);
        dropdown.index = Mathf.Clamp(initialIndex, 0, choices.Length - 1);
        dropdown.RegisterValueChangedCallback(_ => apply(dropdown.index));
    }

    private void WirePill(string elementName, Func<string> text)
    {
        Label pill = Root.Q<Label>(elementName);
        if (pill != null)
        {
            Refreshers.Add(() => pill.text = text());
        }
    }

    private void WirePillState(string elementName, string activeClass, Func<bool> active)
    {
        Label pill = Root.Q<Label>(elementName);
        if (pill != null)
        {
            Refreshers.Add(() => pill.EnableInClassList(activeClass, active()));
        }
    }

    private void WirePillVisible(string elementName, Func<bool> visible)
    {
        Label pill = Root.Q<Label>(elementName);
        if (pill != null)
        {
            Refreshers.Add(() => pill.style.display = visible() ? DisplayStyle.Flex : DisplayStyle.None);
        }
    }

    private void WireReadout(string elementName, Func<string> text)
    {
        Label label = Root.Q<Label>(elementName);
        if (label != null)
        {
            Refreshers.Add(() => label.text = text());
        }
    }

    private void WireDepthOfField(BasisHandHeldCameraUI ui)
    {
        Toggle toggle = Root.Q<Toggle>("tgl-dof");
        VisualElement focusRow = Root.Q<VisualElement>("row-dof-focus");
        VisualElement apertureRow = Root.Q<VisualElement>("row-dof-aperture");
        Button autoButton = Root.Q<Button>("btn-dof-auto");
        Button manualButton = Root.Q<Button>("btn-dof-manual");

        if (toggle != null)
        {
            toggle.SetValueWithoutNotify(DepthActive());
            toggle.RegisterValueChangedCallback(evt =>
            {
                SetDepthActive(evt.newValue);
                ui.SetDepthMode(ui.currentDepthMode);
            });
        }

        if (autoButton != null)
        {
            autoButton.clicked += () => ui.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Auto);
        }
        if (manualButton != null)
        {
            manualButton.clicked += () => ui.SetDepthMode(BasisHandHeldCameraUI.DepthMode.Manual);
        }

        WireSlider("sld-dof-focus", "val-dof-focus", DepthFocus(), ui.DepthChangeFocusDistance, v => $"{v:0.0} m");
        WireSlider("sld-dof-aperture", "val-dof-aperture", DepthAperture(), ui.ChangeAperture, v => $"{v:0.0}");

        if (toggle != null || focusRow != null || apertureRow != null || autoButton != null || manualButton != null)
        {
            Refreshers.Add(() =>
            {
                bool active = DepthActive();
                bool manual = ui.currentDepthMode == BasisHandHeldCameraUI.DepthMode.Manual;
                toggle?.SetValueWithoutNotify(active);
                autoButton?.EnableInClassList(OnClass, active && !manual);
                manualButton?.EnableInClassList(OnClass, active && manual);
                SetDisplayed(apertureRow, active);
                SetDisplayed(focusRow, active && manual);
            });
        }
    }

    private void WireVideo(BasisHandHeldCameraUI ui)
    {
        VisualElement videoCard = Root.Q<VisualElement>("card-video");
        if (videoCard != null)
        {
            SetDisplayed(videoCard, BasisHandHeldCamera.IsVideoOutputSupported);
        }

        WireSlider("sld-vid-res", "val-vid-res", VideoResolutionIndex(), ui.ChangeVideoResolution, FormatVideoResolution);
        WireSlider("sld-vid-fps", "val-vid-fps", HandHeldCamera.VideoOutputSettings.FrameRate, ui.ChangeVideoFrameRate, v => $"{v:0} fps");
    }

    // ---------------------------------------------------------------- click-to-focus

    private void WireViewfinder()
    {
        VisualElement viewfinder = Root.Q<VisualElement>("viewfinder");
        if (viewfinder == null)
        {
            return;
        }

        // Some panels ship a preview render target; point the element at it if the camera has one.
        RenderTexture target = CaptureCamera() != null ? CaptureCamera().targetTexture : null;
        if (target != null)
        {
            viewfinder.style.backgroundImage = Background.FromRenderTexture(target);
        }

        viewfinder.RegisterCallback<PointerDownEvent>(evt => FocusFromViewfinder(viewfinder, evt));
    }

    private void FocusFromViewfinder(VisualElement viewfinder, PointerDownEvent evt)
    {
        BasisDepthOfFieldInteractionHandler handler = HandHeldCamera.BasisDOFInteractionHandler;
        RenderTexture target = CaptureCamera() != null ? CaptureCamera().targetTexture : null;
        if (handler == null || !DepthActive() || target == null)
        {
            return;
        }

        Rect content = viewfinder.contentRect;
        if (content.width <= 0f || content.height <= 0f)
        {
            return;
        }

        Vector2 local = viewfinder.WorldToLocal(evt.position);
        // Panel space is top-left origin; the camera viewport is bottom-up, so v inverts.
        Vector2 uv = new Vector2(
            Mathf.Clamp01(local.x / content.width),
            Mathf.Clamp01(1f - (local.y / content.height)));

        Ray ray = CaptureCamera().ScreenPointToRay(new Vector2(uv.x * target.width, uv.y * target.height));
        handler.ApplyFocusFromRay(ray);
    }

    // ---------------------------------------------------------------- live camera reads

    private Camera CaptureCamera() => HandHeldCamera.captureCamera;
    private bool DepthActive() => HandHeldCamera.MetaData?.depthOfField != null && HandHeldCamera.MetaData.depthOfField.active;

    private void SetDepthActive(bool active)
    {
        if (HandHeldCamera.MetaData?.depthOfField != null)
        {
            HandHeldCamera.MetaData.depthOfField.active = active;
        }
    }

    private float PostExposure() => HandHeldCamera.MetaData?.colorAdjustments != null ? HandHeldCamera.MetaData.colorAdjustments.postExposure.value : 0f;
    private float BloomIntensity() => HandHeldCamera.MetaData?.bloom != null ? HandHeldCamera.MetaData.bloom.intensity.value : 0f;
    private float BloomThreshold() => HandHeldCamera.MetaData?.bloom != null ? HandHeldCamera.MetaData.bloom.threshold.value : 0f;
    private float Contrast() => HandHeldCamera.MetaData?.colorAdjustments != null ? HandHeldCamera.MetaData.colorAdjustments.contrast.value : 0f;
    private float Saturation() => HandHeldCamera.MetaData?.colorAdjustments != null ? HandHeldCamera.MetaData.colorAdjustments.saturation.value : 0f;
    private float DepthAperture() => HandHeldCamera.MetaData?.depthOfField != null ? HandHeldCamera.MetaData.depthOfField.aperture.value : 1f;
    private float DepthFocus() => HandHeldCamera.MetaData?.depthOfField != null ? HandHeldCamera.MetaData.depthOfField.focusDistance.value : 10f;
    private float VolumetricDensity() => HandHeldCamera.MetaData?.VolumetricFogVolume != null ? HandHeldCamera.MetaData.VolumetricFogVolume.density.value : 0f;

    private int CurrentResolutionHeight()
    {
        (int width, int height)[] resolutions = HandHeldCamera.MetaData.resolutions;
        return resolutions != null && resolutions.Length > 0 ? resolutions[0].height : 1080;
    }

    private int VideoResolutionIndex()
    {
        int width = HandHeldCamera.VideoOutputSettings.Width;
        for (int i = 0; i < BasisHandHeldCameraUI.VideoResolutionPresets.Length; i++)
        {
            if (BasisHandHeldCameraUI.VideoResolutionPresets[i].width == width)
            {
                return i;
            }
        }
        return 1;
    }

    private int EvIndexFromPostExposure()
    {
        float ev = PostExposure();
        int best = 0;
        float bestDelta = float.MaxValue;
        for (int i = 0; i < BasisHandHeldCameraUI.ExposureStops.Length; i++)
        {
            float delta = Mathf.Abs(BasisHandHeldCameraUI.ExposureStops[i] - ev);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- formatting

    private static int NearestIndex(string[] choices, string value)
    {
        if (choices == null)
        {
            return 0;
        }
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] == value)
            {
                return i;
            }
        }
        return 0;
    }

    private static string FormatEv(float ev)
    {
        return ev > 0f ? $"+{ev:0.0}" : $"{ev:0.0}";
    }

    private static string FormatEvIndex(float index)
    {
        int clamped = Mathf.Clamp(Mathf.RoundToInt(index), 0, BasisHandHeldCameraUI.ExposureStops.Length - 1);
        return FormatEv(BasisHandHeldCameraUI.ExposureStops[clamped]) + " EV";
    }

    private static string FormatShutter(float seconds)
    {
        if (seconds <= 0f)
        {
            return "--";
        }
        return seconds >= 1f ? $"{seconds:0.0}s" : $"1/{Mathf.RoundToInt(1f / seconds)}";
    }

    private static string FormatVideoResolution(float index)
    {
        int clamped = Mathf.Clamp(Mathf.RoundToInt(index), 0, BasisHandHeldCameraUI.VideoResolutionPresets.Length - 1);
        (int width, int height) preset = BasisHandHeldCameraUI.VideoResolutionPresets[clamped];
        return $"{preset.width}x{preset.height}";
    }

    private static void SetDisplayed(VisualElement element, bool displayed)
    {
        if (element != null)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
