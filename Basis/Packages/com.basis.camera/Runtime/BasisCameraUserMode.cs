using System;
using UnityEngine;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

/// <summary>
/// A mode somebody made themselves: a name, a colour, and a whole camera settings file.
///
/// <para>The built-in modes in <see cref="BasisCameraMode"/> each write a hand-picked handful of
/// values, because each one is an opinion about how to do a job. A saved mode is the opposite —
/// it is not an opinion, it is a snapshot, so it carries <em>everything</em> the camera can be
/// asked to remember. That is deliberate: it means saving one is exactly the harvest that already
/// runs on every save, restoring one is exactly the apply that already runs on every load, and a
/// setting added to the camera later is carried by saved modes the day it is added rather than the
/// day someone remembers to list it here.</para>
///
/// <para>The three values <see cref="CameraSettings"/> leaves out sit alongside it instead. They
/// are left out of the settings file on purpose — a camera that restored "follow armed" from disk
/// would fly out of your hand the moment it spawned — but a mode is picked deliberately, and a
/// saved flying puck that comes back sitting inert in your hand has not been restored at all.</para>
/// </summary>
[Serializable]
public class BasisCameraUserMode
{
    /// <summary>Longest name that still fits the dropdown and the panel's section header.</summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// What a mode is painted before anyone picks a colour.
    ///
    /// <para>A rose, at the same strength and brightness as the four built-in tints and on a hue
    /// none of them uses — they sit at roughly orange, cyan, green and violet. The panel offers one
    /// slider for this, straight through the spectrum, which only moves the hue: so the richness
    /// baked in here is the richness every mode is painted at, and a muted starting colour would
    /// quietly mute the whole family.</para>
    ///
    /// <para>Never the zero <see cref="Color"/> — that is transparent black, and the section tint
    /// blends toward it, so an absent colour would darken the page rather than colour it.</para>
    /// </summary>
    public static readonly Color DefaultTint = new Color(0.97f, 0.37f, 0.57f);

    public string name;
    public Color tint;

    /// <summary>Where the camera sits, as <see cref="BasisHandHeldCameraInteractable.CameraPinSpace"/>.</summary>
    public int pinSpace;

    /// <summary>Everything else. Never null once stored — a mode with no settings has nothing to be.</summary>
    public CameraSettings settings;

    public BasisCameraUserMode()
    {
        tint = DefaultTint;
        settings = new CameraSettings();
    }

    /// <summary>
    /// Trims a name down to something that can be stored, shown and matched. Returns null for
    /// anything that is only whitespace, which is the one name a mode cannot have: the dropdown
    /// would show a blank row and nothing could ever be selected back off it.
    /// </summary>
    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Newlines and tabs would break both the dropdown row and the readout's own line layout,
        // and a name is a label rather than a paragraph, so they collapse to spaces rather than
        // being rejected — pasting a name out of a document should work.
        char[] cleaned = new char[raw.Length];
        int written = 0;
        bool lastWasSpace = true;
        for (int Index = 0; Index < raw.Length; Index++)
        {
            char character = raw[Index];
            bool isSpace = char.IsWhiteSpace(character) || char.IsControl(character);
            if (isSpace)
            {
                if (lastWasSpace) continue;
                cleaned[written++] = ' ';
                lastWasSpace = true;
                continue;
            }

            cleaned[written++] = character;
            lastWasSpace = false;
        }

        while (written > 0 && cleaned[written - 1] == ' ') written--;
        if (written == 0) return null;
        if (written > MaxNameLength) written = MaxNameLength;

        // Trimming to the length cap can strand a trailing space that was legal a character ago.
        while (written > 0 && cleaned[written - 1] == ' ') written--;
        return written == 0 ? null : new string(cleaned, 0, written);
    }

    /// <summary>Names are compared the way people read them, so "Night" and "night" are one mode.</summary>
    public static bool NamesMatch(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True while the live camera still holds everything this mode saved.
    ///
    /// <para>Drives the same "you have left the mode" behaviour the built-ins have, which is what
    /// stops the panel claiming a mode is in control of a shot it no longer describes.</para>
    /// </summary>
    public bool Matches(CameraSettings live) => SettingsMatch(settings, live);

    // Tolerances. Both sides of this comparison came out of the same harvest, so most values are
    // bit-identical and the epsilon is only there to absorb a value that made a round trip through
    // a rounded slider display. The four that carry a real tolerance are the ones the built-in
    // modes already needed one for, and they use the same numbers for the same reason.
    private const float Epsilon = 0.001f;
    private const float FovTolerance = 0.5f;
    private const float ApertureTolerance = 0.02f;
    private const float FocalLengthTolerance = 0.5f;
    private const float MotionBlurTolerance = 0.005f;
    private const float OffsetTolerance = 0.01f;

    // One 8-bit channel. A colour that survived a trip through a hex field cannot be closer.
    private const float ColorTolerance = 1f / 255f;

    /// <summary>
    /// Compares two settings files field for field.
    ///
    /// <para>Five fields are deliberately absent. <c>settingsVersion</c> is about the file format
    /// rather than the camera; <c>cameraMode</c> and <c>userMode</c> are labels <em>derived</em>
    /// from everything else, so comparing them would make the answer depend on itself. The shot
    /// list is left out because a shot is content the rig plays, not a setting the camera holds —
    /// moving a waypoint is authoring, and it should no more leave the mode than taking a photo
    /// does. <c>exposuresRemaining</c> is left out for the end of that same sentence: taking a
    /// photograph is what spends a frame, and a saved disposable is still that disposable after
    /// one.</para>
    ///
    /// <para>⚠️ Hand-written rather than reflective on purpose: this runs while the panel is open,
    /// and walking <see cref="CameraSettings"/> with <c>FieldInfo.GetValue</c> would box every
    /// float on every check. The cost of that choice is that a field added later is invisible here
    /// — which is what <c>EveryStoredSettingIsComparedByMatches</c> exists to catch, by perturbing
    /// each field in turn and demanding this method notice.</para>
    /// </summary>
    public static bool SettingsMatch(CameraSettings left, CameraSettings right)
    {
        if (left == null || right == null) return ReferenceEquals(left, right);

        if (left.resolutionIndex != right.resolutionIndex) return false;
        if (left.formatIndex != right.formatIndex) return false;
        if (left.msaaSamples != right.msaaSamples) return false;

        if (left.apertureIndex != right.apertureIndex) return false;
        if (left.shutterSpeedIndex != right.shutterSpeedIndex) return false;
        if (left.isoIndex != right.isoIndex) return false;
        if (left.exposureIndex != right.exposureIndex) return false;
        if (left.showExposureOnCamera != right.showExposureOnCamera) return false;

        // The body, but deliberately not what is left on the load: a saved disposable is still that
        // disposable after a frame has been taken off it, and comparing the counter would drop the
        // mode's name on the first photo somebody took with it.
        if (left.cameraBody != right.cameraBody) return false;
        if (left.flashEnabled != right.flashEnabled) return false;

        if (!Near(left.fov, right.fov, FovTolerance)) return false;
        if (!Near(left.focusDistance, right.focusDistance, OffsetTolerance)) return false;
        if (!Near(left.sensorSizeX, right.sensorSizeX, OffsetTolerance)) return false;
        if (!Near(left.sensorSizeY, right.sensorSizeY, OffsetTolerance)) return false;

        if (!Near(left.bloomIntensity, right.bloomIntensity, Epsilon)) return false;
        if (!Near(left.bloomThreshold, right.bloomThreshold, Epsilon)) return false;
        if (!Near(left.contrast, right.contrast, Epsilon)) return false;
        if (!Near(left.saturation, right.saturation, Epsilon)) return false;
        if (!Near(left.hueShift, right.hueShift, Epsilon)) return false;

        if (!Near(left.depthAperture, right.depthAperture, ApertureTolerance)) return false;
        if (!Near(left.depthFocusDistance, right.depthFocusDistance, OffsetTolerance)) return false;
        if (left.depthIsActive != right.depthIsActive) return false;
        if (left.dofMode != right.dofMode) return false;
        if (!Near(left.dofFocalLength, right.dofFocalLength, FocalLengthTolerance)) return false;
        if (left.dofBladeCount != right.dofBladeCount) return false;
        if (left.useManualFocus != right.useManualFocus) return false;

        if (left.focusPeaking != right.focusPeaking) return false;
        if (!Near(left.focusPeakingSensitivity, right.focusPeakingSensitivity, Epsilon)) return false;
        if (left.focusPeakingColour != right.focusPeakingColour) return false;
        if (left.focusPeakingGreyPicture != right.focusPeakingGreyPicture) return false;

        if (left.viewfinderGrid != right.viewfinderGrid) return false;
        if (left.viewfinderGridPattern != right.viewfinderGridPattern) return false;
        if (!Near(left.viewfinderGridOpacity, right.viewfinderGridOpacity, Epsilon)) return false;

        if (left.autoBrightness != right.autoBrightness) return false;
        if (!Near(left.autoBrightnessTarget, right.autoBrightnessTarget, Epsilon)) return false;
        if (!Near(left.autoBrightnessSpeed, right.autoBrightnessSpeed, Epsilon)) return false;
        if (left.autoBrightnessMetering != right.autoBrightnessMetering) return false;
        if (!Near(left.autoBrightnessRange, right.autoBrightnessRange, Epsilon)) return false;

        if (left.overrideVolumetricFog != right.overrideVolumetricFog) return false;
        if (left.overrideVolumetricFog)
        {
            if (!Near(left.VolumetricFogVolumedensity, right.VolumetricFogVolumedensity, Epsilon)) return false;
            if (left.VolumetricFogenableAPVContribution != right.VolumetricFogenableAPVContribution) return false;
            if (left.VolumetricFogenableMainLightContribution != right.VolumetricFogenableMainLightContribution) return false;
        }

        if (!Near(left.vignette, right.vignette, Epsilon)) return false;
        if (!Near(left.chromaticAberration, right.chromaticAberration, Epsilon)) return false;
        if (!Near(left.filmGrain, right.filmGrain, Epsilon)) return false;
        if (left.filmGrainType != right.filmGrainType) return false;
        if (!Near(left.filmGrainResponse, right.filmGrainResponse, Epsilon)) return false;
        if (!Near(left.bloomTint, right.bloomTint)) return false;
        if (!Near(left.vignetteColour, right.vignetteColour)) return false;
        if (left.vignetteRounded != right.vignetteRounded) return false;
        if (!Near(left.splitToningShadows, right.splitToningShadows)) return false;
        if (!Near(left.splitToningHighlights, right.splitToningHighlights)) return false;
        if (!Near(left.splitToningBalance, right.splitToningBalance, Epsilon)) return false;
        if (!Near(left.filmLift, right.filmLift, Epsilon)) return false;
        if (!Near(left.whiteBalanceTemperature, right.whiteBalanceTemperature, Epsilon)) return false;
        if (!Near(left.whiteBalanceTint, right.whiteBalanceTint, Epsilon)) return false;
        if (!Near(left.lensDistortion, right.lensDistortion, Epsilon)) return false;
        if (!Near(left.lensDistortionScale, right.lensDistortionScale, Epsilon)) return false;
        if (!Near(left.bloomScatter, right.bloomScatter, Epsilon)) return false;
        if (!Near(left.vignetteSmoothness, right.vignetteSmoothness, Epsilon)) return false;
        if (!Near(left.paniniDistance, right.paniniDistance, Epsilon)) return false;
        if (!Near(left.paniniCropToFit, right.paniniCropToFit, Epsilon)) return false;
        if (left.captureTonemapping != right.captureTonemapping) return false;

        if (!Near(left.motionBlurIntensity, right.motionBlurIntensity, MotionBlurTolerance)) return false;
        if (!Near(left.motionBlurClamp, right.motionBlurClamp, Epsilon)) return false;
        if (left.motionBlurQuality != right.motionBlurQuality) return false;
        if (left.motionBlurMode != right.motionBlurMode) return false;

        if (left.autoFocusFollowSubject != right.autoFocusFollowSubject) return false;
        if (!Basis.Cinematics.BasisCameraModifierStack.Matches(left.modifiers, right.modifiers)) return false;
        if (left.modifiers.subject.anchorToBody != right.modifiers.subject.anchorToBody) return false;
        if (!Near(left.modifiers.subject.aimHeightOffset, right.modifiers.subject.aimHeightOffset, OffsetTolerance)) return false;
        if (!Near(left.modifiers.subject.framingRadius, right.modifiers.subject.framingRadius, Epsilon)) return false;
        if (left.detachedMarker != right.detachedMarker) return false;
        if (left.anchorFollowsBody != right.anchorFollowsBody) return false;

        if (left.capture360 != right.capture360) return false;
        if (left.useAutoLeveling != right.useAutoLeveling) return false;
        if (left.useVRHandheldSmoothing != right.useVRHandheldSmoothing) return false;
        if (left.useSmoothDrag != right.useSmoothDrag) return false;
        if (!Near(left.smoothDragPositionDamping, right.smoothDragPositionDamping, Epsilon)) return false;
        if (!Near(left.smoothDragRotationDamping, right.smoothDragRotationDamping, Epsilon)) return false;
        if (!Near(left.smoothDragMaxDistance, right.smoothDragMaxDistance, Epsilon)) return false;
        if (left.printPhoto != right.printPhoto) return false;

        if (!Near(left.gifDurationSeconds, right.gifDurationSeconds, Epsilon)) return false;
        if (left.gifFrameRate != right.gifFrameRate) return false;
        if (left.gifWidth != right.gifWidth) return false;
        if (left.gifLoop != right.gifLoop) return false;
        if (left.gifDither != right.gifDither) return false;

        if (!Near(left.videoDurationSeconds, right.videoDurationSeconds, Epsilon)) return false;
        if (left.videoFrameRate != right.videoFrameRate) return false;
        if (left.videoWidth != right.videoWidth) return false;
        if (left.videoQuality != right.videoQuality) return false;
        if (left.videoTimeLimit != right.videoTimeLimit) return false;
        if (left.videoContinuousClips != right.videoContinuousClips) return false;

        if (left.backgroundMode != right.backgroundMode) return false;
        if (!Near(left.backgroundCustomColor, right.backgroundCustomColor)) return false;
        if (left.backgroundKeepsWorld != right.backgroundKeepsWorld) return false;

        return true;
    }

    private static bool Near(float left, float right, float tolerance) =>
        Mathf.Abs(left - right) <= tolerance;

    private static bool Near(Vector3 left, Vector3 right) =>
        Vector3.Distance(left, right) <= OffsetTolerance;

    private static bool Near(Color left, Color right) =>
        Near(left.r, right.r, ColorTolerance) &&
        Near(left.g, right.g, ColorTolerance) &&
        Near(left.b, right.b, ColorTolerance) &&
        Near(left.a, right.a, ColorTolerance);
}
