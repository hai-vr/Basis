using System.Text;
using Basis.BasisUI;
using UnityEngine;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

/// <summary>
/// Writes a settings file out as something you can read.
///
/// <para>The panel spreads these values over seven tabs and sixteen collapsible sections, which is
/// the right shape for <em>changing</em> one and the wrong shape for answering "what is this camera
/// actually set to" — or the question a saved mode raises, "what am I about to get back". This is
/// the whole file on one page, in the panel's own words, using the same localized labels the
/// controls carry so the readout and the control that sets a value never disagree about its name.
/// </para>
///
/// <para>Values that reach the camera as an index into a preset table are shown as the preset —
/// "f/2.8", not "1". The tables live on <see cref="BasisHandHeldCameraMetaData"/>, which is a plain
/// serializable class with readonly defaults, so passing null is only a fallback for a camera that
/// has not built one yet rather than the normal case.</para>
/// </summary>
public static class BasisCameraSettingsReadout
{
    private static readonly StringBuilder Builder = new StringBuilder(2048);

    /// <summary>
    /// The whole file as text. <paramref name="pinSpace"/> is passed alongside because it is
    /// placement rather than
    /// settings — <see cref="CameraSettings"/> deliberately does not carry them — and leaving them
    /// out would make the readout silent about the two things that decide where the camera goes.
    /// </summary>
    public static string Build(
        CameraSettings settings,
        int pinSpace,
        BasisHandHeldCameraMetaData metaData)
    {
        if (settings == null) return string.Empty;

        Builder.Clear();

        Section("camera.placement");
        Row("camera.anchor", PinSpaceLabel(pinSpace));
        Row("camera.anchorFollowsBody", OnOff(settings.anchorFollowsBody));

        Section("camera.lens");
        Row("camera.fieldOfView", Number(settings.fov));
        Row("camera.sensorSize", $"{Number(settings.sensorSizeX)} x {Number(settings.sensorSizeY)} mm");
        Row("camera.aperture", Preset(metaData?.apertures, settings.apertureIndex));
        Row("camera.shutterSpeed", Preset(metaData?.shutterSpeeds, settings.shutterSpeedIndex));
        Row("camera.iso", Preset(metaData?.isoValues, settings.isoIndex));

        Section("camera.depthOfField");
        Row("camera.depthOfField", OnOff(settings.depthIsActive));
        Row("camera.mode", DepthModeLabel(settings.dofMode));
        Row("camera.aperture", "f/" + Number(settings.depthAperture));
        Row("camera.focalLength", Number(settings.dofFocalLength) + " mm");
        Row("camera.bokehBlades", settings.dofBladeCount.ToString());
        Row("camera.focusMode", BasisLocalization.Get(settings.useManualFocus ? "camera.focusManual" : "camera.focusAuto"));
        Row("camera.focusDistance", Number(settings.depthFocusDistance));

        Section("camera.body");
        Row("camera.body.kind", BasisLocalization.Get(
            BasisCameraBodies.TitleKey(BasisCameraBodies.Sanitize(settings.cameraBody))));

        BasisCameraBodyTraits body = BasisCameraBodies.Get(BasisCameraBodies.Sanitize(settings.cameraBody));
        if (body.HasFlash) Row("camera.body.flash", OnOff(settings.flashEnabled));

        Section("camera.exposureColour");
        Row("camera.exposure", ExposureLabel(settings.exposureIndex));
        Row("camera.exposureOnCamera", OnOff(settings.showExposureOnCamera));
        Row("camera.contrast", Number(settings.contrast));
        Row("camera.saturation", Number(settings.saturation));
        Row("camera.hueShift", Number(settings.hueShift));
        Row("camera.whiteBalanceTemp", Number(settings.whiteBalanceTemperature));
        Row("camera.whiteBalanceTint", Number(settings.whiteBalanceTint));
        Row("camera.splitToning.shadows", Swatch(settings.splitToningShadows));
        Row("camera.splitToning.highlights", Swatch(settings.splitToningHighlights));
        Row("camera.splitToning.balance", Number(settings.splitToningBalance));
        Row("camera.filmLift", Number(settings.filmLift));
        Row("camera.tonemapping", TonemappingLabel(settings.captureTonemapping));

        Section("camera.effects");
        Row("camera.bloomIntensity", Number(settings.bloomIntensity));
        Row("camera.bloomThreshold", Number(settings.bloomThreshold));
        Row("camera.bloomScatter", Number(settings.bloomScatter));
        Row("camera.vignette", Number(settings.vignette));
        Row("camera.vignetteSmoothness", Number(settings.vignetteSmoothness));
        Row("camera.chromaticAberration", Number(settings.chromaticAberration));
        Row("camera.filmGrain", Number(settings.filmGrain));
        Row("camera.filmGrain.type", FilmGrainTypeLabel(settings.filmGrainType));
        Row("camera.filmGrain.response", Number(settings.filmGrainResponse));
        Row("camera.bloomTint", Swatch(settings.bloomTint));
        Row("camera.vignetteColour", Swatch(settings.vignetteColour));
        Row("camera.vignetteRounded", OnOff(settings.vignetteRounded));
        Row("camera.lensDistortion", Number(settings.lensDistortion));
        Row("camera.lensDistortionScale", Number(settings.lensDistortionScale));
        Row("camera.panini", Number(settings.paniniDistance));
        Row("camera.paniniCrop", Number(settings.paniniCropToFit));
        Row("camera.motionBlur", Number(settings.motionBlurIntensity));
        Row("camera.motionBlurClamp", Number(settings.motionBlurClamp));
        Row("camera.motionBlurQuality", MotionBlurQualityLabel(settings.motionBlurQuality));
        Row("camera.motionBlurMode", MotionBlurModeLabel(settings.motionBlurMode));
        Row("settings.graphics.fog.override", OnOff(settings.overrideVolumetricFog));
        if (settings.overrideVolumetricFog)
            Row("settings.graphics.fog.density", Number(settings.VolumetricFogVolumedensity));

        Section("camera.output");
        Row("camera.photoResolution", ResolutionLabel(metaData, settings.resolutionIndex));
        Row("camera.photoFormat", Preset(metaData?.formats, settings.formatIndex));
        Row("camera.msaa", settings.msaaSamples + "x");
        Row("camera.n360Capture", OnOff(settings.capture360));
        Row("camera.printPhoto", OnOff(settings.printPhoto));
        Row("camera.autoLevel", OnOff(settings.useAutoLeveling));
        Row("camera.vrStabilization", OnOff(settings.useVRHandheldSmoothing));
        Row("camera.smoothDrag", OnOff(settings.useSmoothDrag));
        Row("camera.smoothDrag.position", Number(settings.smoothDragPositionDamping) + " s");
        Row("camera.smoothDrag.rotation", Number(settings.smoothDragRotationDamping) + " s");
        Row("camera.smoothDrag.leash", Number(settings.smoothDragMaxDistance) + " m");

        Section("camera.gif");
        Row("camera.gif.length", Number(settings.gifDurationSeconds) + " s");
        Row("camera.gif.frameRate", settings.gifFrameRate.ToString());
        Row("camera.gif.size", settings.gifWidth + " px");
        Row("camera.gif.loop", OnOff(settings.gifLoop));
        Row("camera.gif.dither", OnOff(settings.gifDither));

        Section("camera.video");
        Row("camera.video.timeLimit", OnOff(settings.videoTimeLimit));
        Row("camera.video.autoNewClip", OnOff(settings.videoContinuousClips));
        Row("camera.video.length", Number(settings.videoDurationSeconds) + " s");
        Row("camera.video.frameRate", settings.videoFrameRate.ToString());
        Row("camera.video.size", settings.videoWidth + " px");
        Row("camera.video.quality", settings.videoQuality.ToString());

        Basis.Cinematics.BasisCameraModifierStack stack =
            settings.modifiers ?? new Basis.Cinematics.BasisCameraModifierStack();

        Section("camera.subject");
        Row("camera.modifier.subject",
            BasisLocalization.Get(Basis.Cinematics.BasisCameraModifiers.NameKey(stack.subject.modifier)));
        Row("camera.followPlayspace", OnOff(stack.subject.anchorToBody));
        Row("camera.lookAtHeightY", Number(stack.subject.aimHeightOffset));
        Row("camera.subjectRadius", Number(stack.subject.framingRadius));
        Row("camera.groupIncludesMe", OnOff(stack.subject.groupIncludesLocal));
        Row("camera.fixedPoint", Vector(stack.subject.fixedPoint));

        Section("camera.modifiers");
        Row("camera.modifier.position",
            BasisLocalization.Get(Basis.Cinematics.BasisCameraModifiers.NameKey(stack.positionModifier)));
        Row("camera.modifier.rotation",
            BasisLocalization.Get(Basis.Cinematics.BasisCameraModifiers.NameKey(stack.rotationModifier)));
        Row("camera.modifier.effects", EffectList(stack));
        Row("camera.followOffset", Vector(stack.follow.positionOffset));
        Row("camera.lateralTrackingX", Number(stack.follow.lateralTracking));
        Row("camera.followRotationOffset", Vector(stack.lookAt.rotationOffset));
        Row("camera.autoFocusSubject", OnOff(settings.autoFocusFollowSubject));
        Row("camera.detachedMarker", DetachedMarkerLabel(settings.detachedMarker));

        Section("camera.background");
        Row("camera.backgroundMode", BackgroundModeLabel(settings.backgroundMode));
        Row("camera.backgroundKeepWorld", OnOff(settings.backgroundKeepsWorld));
        Row("camera.backgroundRed", "#" + ColorUtility.ToHtmlStringRGB(settings.backgroundCustomColor));

        // A count rather than a list: the shots are a track someone authored, and naming each one
        // here would bury every setting above it under a mode with a dozen of them.

        return Builder.ToString();
    }

    private static void Section(string key)
    {
        if (Builder.Length > 0) Builder.Append('\n');
        Builder.Append(BasisLocalization.Get(key)).Append('\n');
    }

    // Two spaces rather than a tab: TextMeshPro's tab stops are set by the style asset, so an
    // indent made of tabs is a different width in every panel it is read in.
    private static void Row(string key, string value) =>
        Builder.Append("  ").Append(BasisLocalization.Get(key)).Append(": ").Append(value).Append('\n');

    /// <summary>Trailing zeros are noise on a readout — 40 and 2.8 rather than 40.00 and 2.80.</summary>
    private static string Number(float value) => value.ToString("0.###");

    /// <summary>A colour as the hex the panel's own colour rows show, which is what can be typed back in.</summary>
    private static string Swatch(Color value) => "#" + ColorUtility.ToHtmlStringRGB(value);

    /// <summary>
    /// The grain texture by name. Written out rather than looked up in the language table: these are
    /// URP's own identifiers and a translated "Large01" would name nothing.
    /// </summary>
    private static string FilmGrainTypeLabel(int type)
    {
        int highest = (int)UnityEngine.Rendering.Universal.FilmGrainLookup.Large02;
        return type >= 0 && type <= highest
            ? ((UnityEngine.Rendering.Universal.FilmGrainLookup)type).ToString()
            : type.ToString();
    }

    private static string Vector(Vector3 value) =>
        $"{Number(value.x)}, {Number(value.y)}, {Number(value.z)}";

    private static string OnOff(bool value) =>
        BasisLocalization.Get(value ? "ui.option.on" : "ui.option.off");

    /// <summary>An index with no table behind it is still worth showing — as an index, honestly.</summary>
    private static string Preset(string[] table, int index) =>
        table != null && index >= 0 && index < table.Length ? table[index] : index.ToString();

    private static string ResolutionLabel(BasisHandHeldCameraMetaData metaData, int index)
    {
        if (metaData == null || index < 0 || index >= metaData.resolutions.Length) return index.ToString();

        var resolution = metaData.resolutions[index];
        return $"{resolution.width} x {resolution.height}";
    }

    private static string ExposureLabel(int index)
    {
        // The stop table is the UI's, and it is what the index means. Showing the stop rather than
        // the index is the difference between "+1" and "8".
        float stop = BasisHandHeldCameraUI.ExposureStopAt(index);
        return (stop > 0f ? "+" : string.Empty) + Number(stop);
    }

    /// <summary>The fitted effects by name, or a dash when the stack carries none.</summary>
    private static string EffectList(Basis.Cinematics.BasisCameraModifierStack stack)
    {
        if (stack == null || stack.EffectCount == 0)
        {
            return "-";
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int Index = 0; Index < stack.EffectCount; Index++)
        {
            if (!stack.TryGetEffectAt(Index, out Basis.Cinematics.BasisCameraEffectModifier effect)) continue;
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(BasisLocalization.Get(Basis.Cinematics.BasisCameraModifiers.NameKey(effect)));
        }
        return builder.Length > 0 ? builder.ToString() : "-";
    }

    private static string PinSpaceLabel(int pinSpace)
    {
        switch (pinSpace)
        {
            case (int)BasisHandHeldCameraInteractable.CameraPinSpace.HandHeld:
                return BasisLocalization.Get("camera.anchor.hand");
            case (int)BasisHandHeldCameraInteractable.CameraPinSpace.PlaySpace:
                return BasisLocalization.Get("camera.anchor.playspace");
            case (int)BasisHandHeldCameraInteractable.CameraPinSpace.Attached:
                return BasisLocalization.Get("camera.anchor.attached");
            default:
                return BasisLocalization.Get("camera.anchor.world");
        }
    }

    private static string DepthModeLabel(int dofMode) =>
        BasisLocalization.Get(dofMode == 1 ? "camera.mode.gaussian" : "camera.mode.bokeh");

    private static string MotionBlurQualityLabel(int quality)
    {
        switch (quality)
        {
            case 0: return BasisLocalization.Get("camera.motionBlurQuality.low");
            case 2: return BasisLocalization.Get("camera.motionBlurQuality.high");
            default: return BasisLocalization.Get("camera.motionBlurQuality.medium");
        }
    }

    private static string MotionBlurModeLabel(int mode) =>
        BasisLocalization.Get(mode == 1 ? "camera.motionBlurMode.cameraAndObjects" : "camera.motionBlurMode.cameraOnly");

    private static string TonemappingLabel(int mode)
    {
        switch (mode)
        {
            case 0: return BasisLocalization.Get("camera.tonemapping.none");
            case 1: return BasisLocalization.Get("camera.tonemapping.neutral");
            default: return BasisLocalization.Get("camera.tonemapping.aces");
        }
    }

    private static string DetachedMarkerLabel(int marker)
    {
        switch ((BasisCameraDetachedMarker)marker)
        {
            case BasisCameraDetachedMarker.Off: return BasisLocalization.Get("ui.option.off");
            case BasisCameraDetachedMarker.Gizmo: return BasisLocalization.Get("camera.detachedMarker.wireframe");
            default: return BasisLocalization.Get("camera.detachedMarker.puck");
        }
    }

    private static string BackgroundModeLabel(int mode)
    {
        switch ((BasisCameraBackgroundMode)mode)
        {
            case BasisCameraBackgroundMode.GreenScreen: return BasisLocalization.Get("camera.background.greenScreen");
            case BasisCameraBackgroundMode.BlueScreen: return BasisLocalization.Get("camera.background.blueScreen");
            case BasisCameraBackgroundMode.Black: return BasisLocalization.Get("camera.background.black");
            case BasisCameraBackgroundMode.White: return BasisLocalization.Get("camera.background.white");
            case BasisCameraBackgroundMode.Magenta: return BasisLocalization.Get("camera.background.magenta");
            case BasisCameraBackgroundMode.Custom: return BasisLocalization.Get("camera.background.custom");
            case BasisCameraBackgroundMode.Transparent: return BasisLocalization.Get("camera.background.transparent");
            default: return BasisLocalization.Get("camera.background.world");
        }
    }
}
