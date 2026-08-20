using System;
using Basis.Cinematics;
using UnityEngine;

public partial class BasisHandHeldCameraUI
{
    [Serializable]
    public class CameraSettings
    {
        /// <summary>
        /// Bumped whenever fields are added whose zero-fill value (JsonUtility leaves absent fields
        /// at 0/false) differs from their intended default. LoadSettings migrates older files.
        /// v2 added the auto-follow config, capture toggles and MSAA. v9 replaced the auto-follow
        /// block and the shot list with the modifier stack. v10 added the camera body. v11 added
        /// the film grading — grain shape, halation tint, vignette colour, split toning and lift.
        /// </summary>
        public const int CurrentVersion = 11;
        public int settingsVersion = CurrentVersion;

        public CameraSettings()
        {
            settingsVersion = CurrentVersion;

            cameraMode = (int)BasisCameraMode.Photo;

            // A digital camera with a full load and its flash armed. The load matters: JsonUtility
            // builds the object through this constructor before filling it, so a file written
            // before bodies existed arrives with a fresh roll rather than an empty one — which is
            // the difference between an older camera loading normally and one that will not fire.
            cameraBody = (int)BasisCameraBodyKind.Digital;
            exposuresRemaining = BasisHandHeldCamera.FullRoll;
            flashEnabled = true;

            // Empty rather than null, and never allowed to become null again: JsonUtility writes a
            // null string as "" and reads it back as "", so a null here would be a field that
            // provably cannot survive its own file.
            userMode = string.Empty;

            backgroundMode = 0;
            backgroundCustomColor = BasisHandHeldCamera.ChromaGreen;
            backgroundKeepsWorld = false;

            modifiers = new BasisCameraModifierStack();
            detachedMarker = (int)BasisCameraDetachedMarker.Puck;

            dofMode = 2;          // Bokeh, matching the authored profile
            dofFocalLength = 50f;
            dofBladeCount = 5;

            resolutionIndex = 1;
            formatIndex = 0;
            apertureIndex = 0;
            shutterSpeedIndex = 0;
            isoIndex = 0;
            fov = 40;
            focusDistance = 10f;
            sensorSizeX = 36f;
            sensorSizeY = 24f;
            bloomIntensity = 0.5f;
            bloomThreshold = 0.5f;

            // URP's own defaults for the parts of an effect that shape it rather than switch it on.
            // Set here rather than left to the zero fill, so a file written before they existed
            // loads the look it was saved with instead of a hard-edged bloom and a flat vignette.
            bloomScatter = 0.7f;
            vignetteSmoothness = 0.2f;

            // The film grading, at the values that mean "leave the picture alone". None of these is
            // its own zero: grain falls back to the thinnest texture rather than to none, bloom and
            // the vignette tint to white and black rather than to nothing, and split toning is
            // neutral at GREY at both ends — a black shadow colour is the strongest shift there is,
            // not the absence of one, which is the trap in defaulting a colour to default(Color).
            filmGrainType = 0;                      // Thin1
            filmGrainResponse = 0.8f;               // URP's own default: grain backs off in highlights
            bloomTint = Color.white;
            vignetteColour = Color.black;
            vignetteRounded = false;
            splitToningShadows = Color.grey;
            splitToningHighlights = Color.grey;
            splitToningBalance = 0f;
            filmLift = 0f;
            lensDistortionScale = 1f;
            paniniCropToFit = 1f;
            captureTonemapping = (int)UnityEngine.Rendering.Universal.TonemappingMode.ACES;
            contrast = 1f;
            saturation = 1f;
            depthAperture = 2.8f;
            depthFocusDistance = 10f;
            depthIsActive = false;
            useManualFocus = true;
            showExposureOnCamera = false;

            // Off, but with a usable sensitivity already set, so the file a camera loads on the day
            // the feature arrives is not one that switches it on at the least sensitive end.
            focusPeaking = false;
            focusPeakingSensitivity = BasisHandHeldCamera.DefaultFocusPeakingSensitivity;
            focusPeakingColour = 0;
            focusPeakingGreyPicture = false;

            // Same shape again: off, but already holding an opacity that reads, so the file a
            // camera loads on the day the grid arrives is not one that switches it on at the
            // faintest setting there is.
            viewfinderGrid = false;
            viewfinderGridPattern = (int)BasisCameraGridPattern.Thirds;
            viewfinderGridOpacity = BasisHandHeldCamera.DefaultGridOpacity;

            // Same again for the meter: off, but already set up to behave the moment it is on.
            autoBrightness = false;
            autoBrightnessTarget = BasisHandHeldCamera.DefaultBrightnessTarget;
            autoBrightnessSpeed = BasisHandHeldCamera.DefaultBrightnessSpeed;
            autoBrightnessMetering = (int)BasisCameraMeteringMode.CentreWeighted;
            autoBrightnessRange = BasisHandHeldCamera.DefaultBrightnessRange;

            // Off by default (a still photo of a moving world is not usually what is wanted), but
            // with the shape of the effect already sane for the moment it is switched on.
            motionBlurIntensity = 0f;
            motionBlurClamp = 0.05f;
            motionBlurQuality = 1;   // Medium
            motionBlurMode = 0;      // Camera only — no motion vector pass

            overrideVolumetricFog = false;
            VolumetricFogVolumedensity = 0.01f;
            VolumetricFogenableAPVContribution = true;
            VolumetricFogenableMainLightContribution = true;

            msaaSamples = 2;

            smoothDragPositionDamping = 0.4f;
            smoothDragRotationDamping = 0.5f;
            smoothDragMaxDistance = 0.25f;

            gifDurationSeconds = 5f;
            gifFrameRate = 15;
            gifWidth = 480;
            gifLoop = true;
            gifDither = true;

            videoDurationSeconds = 30f;
            videoFrameRate = 30;
            videoWidth = 1920;
            videoQuality = 80;
            videoTimeLimit = true;
            videoContinuousClips = false;
        }

        /// <summary>
        /// The <see cref="BasisCameraMode"/> the camera was last in. Restored on load and then
        /// immediately re-derived from the values that loaded alongside it, so a file that no
        /// longer matches the mode it names settles on Custom instead of mislabelling itself.
        /// </summary>
        public int cameraMode;

        /// <summary>
        /// The saved mode the camera was last wearing, by name, or empty for none. Looked up in
        /// <see cref="BasisCameraUserModes"/> on load and dropped if that mode has since been
        /// deleted or edited into something this file no longer matches.
        ///
        /// <para>Only the name is stored, never the mode's values: the values are already in this
        /// file. A copy here would be a second version of the same settings, free to disagree with
        /// both the file around it and the mode it names.</para>
        /// </summary>
        public string userMode;

        /// <summary>
        /// The physical camera, as <see cref="BasisCameraBodyKind"/>. Saved separately from
        /// <see cref="cameraMode"/> because it outlives it: touch one slider on a disposable and
        /// the mode is Custom, and you are still holding a disposable.
        /// </summary>
        public int cameraBody;

        /// <summary>
        /// Frames left on the load, or <see cref="BasisHandHeldCamera.FullRoll"/> for a fresh one.
        /// Saved because a disposable that refilled itself every session would never run out at
        /// all, which is most of what makes it a disposable.
        /// </summary>
        public int exposuresRemaining;

        /// <summary>Whether the flash is armed. Ignored on a body with nothing on the front.</summary>
        public bool flashEnabled;

        public int resolutionIndex = 1;
        public int formatIndex = 0;
        public int msaaSamples = 2;

        public int apertureIndex;
        public int shutterSpeedIndex;
        public int isoIndex;

        public int exposureIndex = 6;

        /// <summary>Whether the exposure slider is shown on the camera's own interface. Off unless turned on from the camera panel.</summary>
        public bool showExposureOnCamera = false;


        public float fov;
        public float focusDistance;
        public float sensorSizeX;
        public float sensorSizeY;

        public float bloomIntensity;
        public float bloomThreshold;

        public float contrast;
        public float saturation;
        public float hueShift;

        public float depthAperture;
        public float depthFocusDistance;
        public bool depthIsActive;
        public int dofMode;
        public float dofFocalLength;
        public int dofBladeCount;

        public bool useManualFocus = true;

        /// <summary>
        /// The viewfinder focus aid. A view preference rather than part of the shot — the overlay
        /// is produced into a texture of its own that no capture path reads — but it is saved for
        /// the same reason the detached marker is: it is a control with nowhere else to be
        /// remembered, and one that resets every session is one nobody leaves on.
        /// </summary>
        public bool focusPeaking;
        public float focusPeakingSensitivity;
        /// <summary>Index into <see cref="BasisHandHeldCamera.FocusPeakingColours"/>; 0 is red.</summary>
        public int focusPeakingColour;
        public bool focusPeakingGreyPicture;

        /// <summary>
        /// The viewfinder alignment grid, saved for the reason focus peaking is: a view preference
        /// that no capture path can reach, but one with nowhere else to be remembered.
        /// </summary>
        public bool viewfinderGrid;
        /// <summary>Which grid, as <see cref="BasisCameraGridPattern"/>; 0 is the rule of thirds.</summary>
        public int viewfinderGridPattern;
        public float viewfinderGridOpacity;

        /// <summary>
        /// Auto brightness. The stops the meter is currently adding are deliberately not saved:
        /// they describe the room the camera was last in, and a file that restored them would open
        /// every session mis-exposed until the loop had walked it back.
        /// </summary>
        public bool autoBrightness;
        public float autoBrightnessTarget;
        public float autoBrightnessSpeed;
        /// <summary>Which part of the frame is metered, as <see cref="BasisCameraMeteringMode"/>.</summary>
        public int autoBrightnessMetering;
        public float autoBrightnessRange;

        /// <summary>Uses this camera's volumetric-fog profile instead of the world's fog.</summary>
        public bool overrideVolumetricFog;
        public float VolumetricFogVolumedensity;
        public bool VolumetricFogenableAPVContribution;
        public bool VolumetricFogenableMainLightContribution;

        // Extra post-processing (0 = effect off, so a fresh install adds nothing to the shot).
        public float vignette;
        public float chromaticAberration;
        public float filmGrain;
        public float whiteBalanceTemperature;
        public float whiteBalanceTint;
        public float lensDistortion;
        public float paniniDistance;

        /// <summary>
        /// Which grain texture is used, as <c>FilmGrainLookup</c>. Size rather than strength — the
        /// difference between the grain of a fast negative and digital noise is how big it is.
        /// </summary>
        public int filmGrainType;

        /// <summary>How far the grain backs off in the highlights. 0 lays it evenly; 1 confines it to the shadows.</summary>
        public float filmGrainResponse;

        /// <summary>
        /// The colour of the glow around a highlight. Orange on film, where it is halation rather
        /// than bloom; white on anything with a sensor.
        /// </summary>
        public Color bloomTint;

        /// <summary>What the corners darken toward, and whether they do it in a circle or with the frame.</summary>
        public Color vignetteColour;
        public bool vignetteRounded;

        /// <summary>
        /// The two ends of the colour split, and where the split falls. Neutral is grey at both
        /// ends; this is the control that makes a stock rather than a filter.
        /// </summary>
        public Color splitToningShadows;
        public Color splitToningHighlights;
        public float splitToningBalance;

        /// <summary>
        /// The black point, raised. Stored as the flat offset rather than as the whole lift trackball
        /// — the colour half is held at neutral so this cannot fight the split toning above it.
        /// </summary>
        public float filmLift;

        /// <summary>
        /// Shape, as opposed to strength. Each belongs to an effect whose own slider is the on/off,
        /// so these carry usable values even while the effect they shape is switched off.
        /// </summary>
        public float bloomScatter;
        public float vignetteSmoothness;
        public float lensDistortionScale;
        public float paniniCropToFit;

        /// <summary>
        /// Which tonemapper grades the saved photo, as <c>TonemappingMode</c>. The preview is always
        /// Neutral — the capture is rendered at a different resolution and exposure and has always
        /// been graded on its own — so this is the still's look, not the viewfinder's.
        /// </summary>
        public int captureTonemapping;

        /// <summary>
        /// Motion blur. The strength is the on/off — URP only runs the pass above zero — so the
        /// shape settings below carry usable values even in a file that has the effect switched
        /// off, and no migration is needed: JsonUtility leaves a field absent from an older file
        /// holding the constructor default rather than zeroing it.
        /// </summary>
        public float motionBlurIntensity;
        public float motionBlurClamp;
        /// <summary>0 = Low, 1 = Medium, 2 = High.</summary>
        public int motionBlurQuality;
        /// <summary>0 = camera movement only, 1 = camera and moving objects.</summary>
        public int motionBlurMode;

        public bool autoFocusFollowSubject;

        /// <summary>
        /// How the camera is driven, including who it films. Which modifiers are fitted is saved
        /// in full: unlike the auto follow flag it replaced, an empty stack is the resting state,
        /// so a restored file can only ever fly the camera off on spawn if that is what was
        /// actually saved. The follow target itself is per-session and not persisted.
        /// </summary>
        public BasisCameraModifierStack modifiers = new BasisCameraModifierStack();


        /// <summary>
        /// Which marker shows where the camera has gone while it is detached, as
        /// <see cref="BasisCameraDetachedMarker"/>. A view preference like the follow framing
        /// around it, not part of the shot — but it was the only control in the Follow section
        /// with nowhere to be saved, so it reset to Puck every session.
        /// </summary>
        public int detachedMarker;

        /// <summary>
        /// Whether a playspace anchor rides your body rather than your playspace origin. Off is the
        /// zero fill and is the steadier of the two, so an older file loads as the playspace anchor
        /// it was written as. Which anchor is selected is deliberately not saved, for the same
        /// reason a fitted follow was not: a camera that restored bolted to a vehicle from the last
        /// world has nothing to be bolted to in this one.
        /// </summary>
        public bool anchorFollowsBody;

        // Capture-mode toggles.
        public bool capture360;
        public bool useAutoLeveling;
        public bool useVRHandheldSmoothing;

        /// <summary>
        /// The held camera trails the hand instead of being locked to it. Off is the zero fill, so
        /// an older file loads as the rigid hold it was written as, and the three numbers below it
        /// are defaulted in the constructor rather than migrated.
        /// </summary>
        public bool useSmoothDrag;
        public float smoothDragPositionDamping;
        public float smoothDragRotationDamping;
        public float smoothDragMaxDistance;

        // GIF recording. Every default is set in the constructor, so an older file that lacks
        // them loads the intended values without a migration.
        public float gifDurationSeconds;
        public int gifFrameRate;
        public int gifWidth;
        public bool gifLoop;
        public bool gifDither;

        // Video recording (MJPEG AVI), defaulted the same way.
        public float videoDurationSeconds;
        public int videoFrameRate;
        public int videoWidth;
        public int videoQuality;

        /// <summary>On, a recording stops itself after <see cref="videoDurationSeconds"/>; off, it runs until stopped.</summary>
        public bool videoTimeLimit;

        /// <summary>
        /// With a time limit set, on: reaching it starts the next clip instead of ending the
        /// recording, which then runs until it is stopped by hand. Off is the zero fill, so an
        /// older file loads as the single-clip recording it was written as.
        /// </summary>
        public bool videoContinuousClips;

        /// <summary>
        /// Whether each saved photo is also printed into the world as a shared image pickup,
        /// exactly as if its file had been drag-and-dropped onto the window.
        /// </summary>
        public bool printPhoto;

        // Background. Mode 0 is World, so a zero-filled old file keeps the world background.
        public int backgroundMode;
        public Color backgroundCustomColor;
        public bool backgroundKeepsWorld;

    }
}
