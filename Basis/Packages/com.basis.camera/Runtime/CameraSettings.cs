using System;
using System.Collections.Generic;
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
        /// v2 added the auto-follow config, capture toggles and MSAA.
        /// </summary>
        public const int CurrentVersion = 8;
        public int settingsVersion = CurrentVersion;

        public CameraSettings()
        {
            settingsVersion = CurrentVersion;

            cameraMode = (int)BasisCameraMode.Photo;

            backgroundMode = 0;
            backgroundCustomColor = BasisHandHeldCamera.ChromaGreen;
            backgroundKeepsWorld = false;
            subjectFramingRadius = 0.45f;

            autoFollowPositionOffset = new Vector3(0.5f, 0f, 1.4f);
            autoFollowRotationOffset = Vector3.zero;
            autoFollowPlayspace = true;
            autoFollowLookAtPlayer = true;
            autoFollowLookAtHeightOffset = 0f;
            autoFollowLateralTracking = 0.5f;
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
            contrast = 1f;
            saturation = 1f;
            depthAperture = 2.8f;
            depthFocusDistance = 10f;
            depthIsActive = false;
            useManualFocus = true;
            showExposureOnCamera = false;

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
        }

        /// <summary>
        /// The <see cref="BasisCameraMode"/> the camera was last in. Restored on load and then
        /// immediately re-derived from the values that loaded alongside it, so a file that no
        /// longer matches the mode it names settles on Custom instead of mislabelling itself.
        /// </summary>
        public int cameraMode;

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

        // Auto-follow configuration (the follow target itself is per-session and not persisted).
        public Vector3 autoFollowPositionOffset;
        public Vector3 autoFollowRotationOffset;
        public bool autoFollowPlayspace;
        public bool autoFollowLookAtPlayer;
        public float autoFollowLookAtHeightOffset;
        public float autoFollowLateralTracking;

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

        // Background. Mode 0 is World, so a zero-filled old file keeps the world background.
        public int backgroundMode;
        public Color backgroundCustomColor;
        public bool backgroundKeepsWorld;

        /// <summary>
        /// The authored shot rig. Whether the rig is switched on is deliberately not saved — the
        /// same reasoning as auto follow, which would otherwise fly the camera off on every spawn.
        /// </summary>
        public List<BasisCameraShot> cinematicShots = new List<BasisCameraShot>();

        public float subjectFramingRadius;
    }
}
