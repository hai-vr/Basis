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
        /// block and the shot list with the modifier stack.
        /// </summary>
        public const int CurrentVersion = 9;
        public int settingsVersion = CurrentVersion;

        public CameraSettings()
        {
            settingsVersion = CurrentVersion;

            cameraMode = (int)BasisCameraMode.Photo;

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

            // Off by default (a still photo of a moving world is not usually what is wanted), but
            // with the shape of the effect already sane for the moment it is switched on.
            motionBlurIntensity = 0f;
            motionBlurClamp = 0.05f;
            motionBlurQuality = 1;   // Medium
            motionBlurMode = 0;      // Camera only — no motion vector pass

            VolumetricFogVolumedensity = 0.01f;
            VolumetricFogenableAPVContribution = true;
            VolumetricFogenableMainLightContribution = true;

            msaaSamples = 2;

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

        // Capture-mode toggles.
        public bool capture360;
        public bool useAutoLeveling;
        public bool useVRHandheldSmoothing;

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
