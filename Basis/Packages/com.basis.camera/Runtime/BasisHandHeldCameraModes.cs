using UnityEngine;
using CameraPinSpace = BasisHandHeldCameraInteractable.CameraPinSpace;

/// <summary>
/// Camera modes: applying one, and noticing when the camera has drifted off it.
///
/// <para>The values a mode writes live in a single <see cref="BasisCameraModePreset"/> table that
/// both <see cref="ApplyCameraMode"/> and <see cref="MatchesCameraMode"/> read, so "what the mode
/// sets" and "what counts as still being in the mode" cannot drift apart. A round-trip test asserts
/// that applying any mode leaves the camera matching it.</para>
///
/// <para>Detection is by comparison rather than by hooking the ~60 setters the panel and the prop
/// HUD share. That costs a handful of float compares on the panel tick and in exchange it catches
/// changes made from either surface, including ones made while the panel was shut.</para>
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>
    /// The mode the camera is in. Written when a mode is applied and re-derived whenever the
    /// settings are polled, so it is never stale by more than a tick.
    /// </summary>
    public BasisCameraMode CameraMode { get; private set; } = BasisCameraMode.Photo;

    /// <summary>The values one mode writes. Read by both the apply and the match, never duplicated.</summary>
    private readonly struct BasisCameraModePreset
    {
        // Placement: the three values a settings file cannot carry, because saving them would have
        // a camera fly out of your hand the moment it spawned. These are what a restore re-arms.
        public readonly CameraPinSpace Pin;
        public readonly bool AutoFollow;
        public readonly bool Cinematic;

        // Everything below is persisted in its own right, so a restore must leave it alone and let
        // the file speak. Only an explicit mode selection writes these.
        public readonly bool AutoLevel;
        public readonly bool VrStabilisation;
        public readonly bool Capture360;
        public readonly bool AutoFocusSubject;
        public readonly bool FollowPlayspace;
        public readonly bool FollowLookAtPlayer;
        public readonly Vector3 FollowOffset;
        public readonly float Fov;

        /// <summary>
        /// Whether depth of field runs, kept separate from the blur style below it. The camera
        /// stores the two independently — a style of Bokeh with the effect switched off is the
        /// shipped default — and folding them into one value would make picking a mode either
        /// switch the effect on or forget which style the user had.
        /// </summary>
        public readonly bool DoFEnabled;

        /// <summary>1 = Gaussian, 2 = Bokeh.</summary>
        public readonly int DoFStyle;
        public readonly float Aperture;
        public readonly float FocalLength;
        public readonly float MotionBlur;

        public BasisCameraModePreset(
            CameraPinSpace pin,
            bool autoFollow,
            bool cinematic,
            bool autoLevel,
            bool vrStabilisation,
            bool capture360,
            bool autoFocusSubject,
            bool followPlayspace,
            bool followLookAtPlayer,
            Vector3 followOffset,
            float fov,
            bool dofEnabled,
            int dofStyle,
            float aperture,
            float focalLength,
            float motionBlur)
        {
            Pin = pin;
            AutoFollow = autoFollow;
            Cinematic = cinematic;
            AutoLevel = autoLevel;
            VrStabilisation = vrStabilisation;
            Capture360 = capture360;
            AutoFocusSubject = autoFocusSubject;
            FollowPlayspace = followPlayspace;
            FollowLookAtPlayer = followLookAtPlayer;
            FollowOffset = followOffset;
            Fov = fov;
            DoFEnabled = dofEnabled;
            DoFStyle = dofStyle;
            Aperture = aperture;
            FocalLength = focalLength;
            MotionBlur = motionBlur;
        }
    }

    /// <summary>
    /// Photo is the camera as it has always behaved, so every number here is the shipped default —
    /// including depth of field being off with Bokeh waiting behind it. Picking Photo after any
    /// other mode has to be a clean return, not a new look, and a fresh install has to already be
    /// in it rather than one edit away.
    /// </summary>
    private static readonly BasisCameraModePreset PhotoPreset = new BasisCameraModePreset(
        pin: CameraPinSpace.HandHeld,
        autoFollow: false,
        cinematic: false,
        autoLevel: false,
        vrStabilisation: false,
        capture360: false,
        autoFocusSubject: false,
        followPlayspace: true,
        followLookAtPlayer: true,
        followOffset: new Vector3(0.5f, 0f, 1.4f),
        fov: 40f,
        dofEnabled: false,
        dofStyle: 2,
        aperture: 2.8f,
        focalLength: 50f,
        motionBlur: 0f);

    /// <summary>
    /// A puck parked in the world and flown by hand. Wide enough to hold a room, levelled and
    /// stabilised because the shot is watched live, and deliberately deep-focus: a stream where
    /// half the room is a blur is a worse stream, however good the still would look.
    /// </summary>
    private static readonly BasisCameraModePreset FlyingPuckPreset = new BasisCameraModePreset(
        pin: CameraPinSpace.WorldSpace,
        autoFollow: false,
        cinematic: false,
        autoLevel: true,
        vrStabilisation: true,
        capture360: false,
        autoFocusSubject: false,
        followPlayspace: true,
        followLookAtPlayer: true,
        followOffset: new Vector3(0.5f, 0f, 1.4f),
        fov: 55f,
        dofEnabled: false,
        dofStyle: 2,
        aperture: 5.6f,
        focalLength: 35f,
        motionBlur: 0f);

    /// <summary>
    /// Flies itself and keeps you sharp. This is the first mode to switch depth of field on,
    /// because it is the first one that knows what the subject is: auto focus tracks you, so the
    /// aperture can stay open enough to lift you off the background without the focus hunting.
    /// </summary>
    private static readonly BasisCameraModePreset FollowMePreset = new BasisCameraModePreset(
        pin: CameraPinSpace.WorldSpace,
        autoFollow: true,
        cinematic: false,
        autoLevel: false,
        vrStabilisation: false,
        capture360: false,
        autoFocusSubject: true,
        followPlayspace: true,
        followLookAtPlayer: true,
        followOffset: new Vector3(0.5f, 0f, 1.4f),
        fov: 45f,
        dofEnabled: true,
        dofStyle: 2,
        aperture: 2.8f,
        focalLength: 50f,
        motionBlur: 0f);

    /// <summary>
    /// The shot rig drives the camera. A longer lens and a wider aperture give the shallow,
    /// compressed look the dolly and orbit moves are there to show off, and a little motion blur
    /// stops a slow push reading as a slideshow.
    /// </summary>
    private static readonly BasisCameraModePreset CinematicPreset = new BasisCameraModePreset(
        pin: CameraPinSpace.WorldSpace,
        autoFollow: false,
        cinematic: true,
        autoLevel: false,
        vrStabilisation: false,
        capture360: false,
        autoFocusSubject: false,
        followPlayspace: true,
        followLookAtPlayer: true,
        followOffset: new Vector3(0.5f, 0f, 1.4f),
        fov: 35f,
        dofEnabled: true,
        dofStyle: 2,
        aperture: 2.0f,
        focalLength: 85f,
        motionBlur: 0.35f);

    // Tolerances. Every one of these values reaches the camera through a slider whose display is
    // rounded, so an exact compare would report Custom for a value the user never touched.
    private const float FovTolerance = 0.5f;
    private const float ApertureTolerance = 0.02f;
    private const float FocalLengthTolerance = 0.5f;
    private const float MotionBlurTolerance = 0.005f;
    private const float OffsetTolerance = 0.01f;

    private static bool TryGetPreset(BasisCameraMode mode, out BasisCameraModePreset preset)
    {
        switch (mode)
        {
            case BasisCameraMode.Photo: preset = PhotoPreset; return true;
            case BasisCameraMode.FlyingPuck: preset = FlyingPuckPreset; return true;
            case BasisCameraMode.FollowMe: preset = FollowMePreset; return true;
            case BasisCameraMode.Cinematic: preset = CinematicPreset; return true;
            default: preset = default; return false;
        }
    }

    /// <summary>
    /// Puts the camera into a mode. Custom is a state, not a preset — selecting it changes nothing,
    /// which is the point: it means "keep what I have".
    /// </summary>
    public void ApplyCameraMode(BasisCameraMode mode)
    {
        if (!TryGetPreset(mode, out BasisCameraModePreset preset))
        {
            CameraMode = BasisCameraMode.Custom;
            return;
        }

        ApplyPresetPlacement(preset);

        useAutoLeveling = preset.AutoLevel;
        useVRHandheldSmoothing = preset.VrStabilisation;
        capture360Enabled = preset.Capture360;

        // Only a mode that actually runs follow owns follow's settings. The others mark the whole
        // Follow section as doing nothing, and a mode that greys a section out has no business
        // resetting the values in it — the user's framing is still theirs when they come back.
        if (preset.AutoFollow)
        {
            autoFocusFollowSubject = preset.AutoFocusSubject;
            autoFollowPlayspace = preset.FollowPlayspace;
            autoFollowLookAtPlayer = preset.FollowLookAtPlayer;
            autoFollowPositionOffset = preset.FollowOffset;
        }

        SetFieldOfView(preset.Fov);
        ApplyPresetOptics(preset);
        SyncPropUiAfterModeChange();

        CameraMode = mode;
    }

    /// <summary>
    /// Arms the mode's placement: whether follow and the shot rig are running, and where the camera
    /// is pinned. This is the half a settings file cannot carry, so it is also the whole of what a
    /// restore re-runs.
    ///
    /// <para>Follow and the shot rig both claim world space on the way in and both hand it back on
    /// the way out, so whichever is unwanted has to be switched off before the wanted one is armed
    /// — otherwise the loser's hand-back fires last and drags the camera out of the pin it was just
    /// given. The explicit pin write afterwards then settles the modes that arm neither.</para>
    /// </summary>
    private void ApplyPresetPlacement(BasisCameraModePreset preset)
    {
        if (!preset.AutoFollow) SetAutoFollowEnabled(false);
        if (!preset.Cinematic) SetCinematicEnabled(false);
        if (preset.AutoFollow) SetAutoFollowEnabled(true);
        if (preset.Cinematic) SetCinematicEnabled(true);
        PinSpace = preset.Pin;
    }

    /// <summary>
    /// Writes the lens and post-processing half of a preset. Split out because it is the half that
    /// needs a live volume profile: on a camera whose overrides have not been created yet there is
    /// nothing to write, and skipping is correct — <see cref="MatchesCameraMode"/> skips the same
    /// values, so a camera missing its profile is not reported as Custom for want of one.
    /// </summary>
    private void ApplyPresetOptics(BasisCameraModePreset preset)
    {
        var depthOfField = MetaData?.depthOfField;
        if (depthOfField != null)
        {
            depthOfField.mode.overrideState = true;
            depthOfField.mode.value = (UnityEngine.Rendering.Universal.DepthOfFieldMode)Mathf.Clamp(preset.DoFStyle, 1, 2);
            depthOfField.active = preset.DoFEnabled;

            // Written even where the effect is off, so switching it on later gives the look the
            // mode intended rather than whatever the last mode happened to leave behind.
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = preset.Aperture;
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = preset.FocalLength;

            BasisDOFInteractionHandler?.SetDoFState(preset.DoFEnabled);
        }

        var motionBlur = MetaData?.motionBlur;
        if (motionBlur != null)
        {
            motionBlur.intensity.overrideState = true;
            motionBlur.intensity.value = preset.MotionBlur;
            // URP only runs the pass above zero, so the strength doubles as the on/off switch.
            motionBlur.active = preset.MotionBlur > 0f;
        }

    }

    /// <summary>
    /// Pushes what a preset just wrote back into the prop's own HUD.
    ///
    /// <para>⚠️ Not cosmetic. Saving harvests the field of view and the depth aperture <em>from the
    /// HUD sliders</em>, not from the camera — so a preset that writes the camera and leaves the
    /// sliders behind is saved with the old numbers, and the mode quietly degrades to Custom the
    /// next time the file is loaded. <see cref="BasisHandHeldCameraUI.SyncPropControlsFromState"/>
    /// re-seeds every shared control from the live camera, which is exactly that repair.</para>
    ///
    /// <para><see cref="BasisHandHeldCameraUI.SetDepthMode"/> is re-run alongside it because the
    /// HUD's focus cursor and depth sliders show or hide on whether depth of field is running, and
    /// it derives that from the live effect. Both are skipped when the UI has no camera yet, which
    /// is every edit-mode test.</para>
    /// </summary>
    private void SyncPropUiAfterModeChange()
    {
        if (HandHeld == null || HandHeld.HHC == null) return;

        HandHeld.SyncPropControlsFromState();
        HandHeld.SetDepthMode(HandHeld.currentDepthMode);
    }

    /// <summary>True while every value the mode writes still holds on the live camera.</summary>
    public bool MatchesCameraMode(BasisCameraMode mode)
    {
        if (!TryGetPreset(mode, out BasisCameraModePreset preset)) return false;

        // PinSpace is deliberately not compared. It is where the camera happens to be, not how it
        // is configured — grabbing a flying puck back out of the air, or letting go of a photo
        // camera, must not read as "you have left the mode".
        if (autoFollowEnabled != preset.AutoFollow) return false;
        if (cinematicEnabled != preset.Cinematic) return false;
        if (useAutoLeveling != preset.AutoLevel) return false;
        if (useVRHandheldSmoothing != preset.VrStabilisation) return false;
        if (capture360Enabled != preset.Capture360) return false;

        // Compared only where they are written. A mode that leaves the follow settings alone must
        // not be knocked out of itself by them, or editing a section it greys out would drop the
        // camera to Custom for a change that had no effect on the shot.
        if (preset.AutoFollow)
        {
            if (autoFocusFollowSubject != preset.AutoFocusSubject) return false;
            if (autoFollowPlayspace != preset.FollowPlayspace) return false;
            if (autoFollowLookAtPlayer != preset.FollowLookAtPlayer) return false;
            if (Vector3.Distance(autoFollowPositionOffset, preset.FollowOffset) > OffsetTolerance) return false;
        }

        if (captureCamera != null &&
            Mathf.Abs(captureCamera.fieldOfView - preset.Fov) > FovTolerance) return false;

        var depthOfField = MetaData?.depthOfField;
        if (depthOfField != null)
        {
            if (depthOfField.active != preset.DoFEnabled) return false;

            // With the effect off the panel hides the style, aperture and focal length entirely, so
            // their values are whatever was left behind. Comparing them would strand the camera on
            // Custom with nothing on screen to explain why.
            if (preset.DoFEnabled)
            {
                if ((int)depthOfField.mode.value != preset.DoFStyle) return false;
                if (Mathf.Abs(depthOfField.aperture.value - preset.Aperture) > ApertureTolerance) return false;
                if (Mathf.Abs(depthOfField.focalLength.value - preset.FocalLength) > FocalLengthTolerance) return false;
            }
        }

        var motionBlur = MetaData?.motionBlur;
        if (motionBlur != null)
        {
            float liveMotionBlur = motionBlur.active ? motionBlur.intensity.value : 0f;
            if (Mathf.Abs(liveMotionBlur - preset.MotionBlur) > MotionBlurTolerance) return false;
        }

        return true;
    }

    /// <summary>
    /// Re-derives <see cref="CameraMode"/> from the live camera and reports whether it moved.
    ///
    /// <para>The current mode is checked first so a camera that still matches keeps its label
    /// without being re-identified. Only once it has drifted does the rest of the table get a look,
    /// which is what lets a camera arrive back at a mode by hand — set a Photo camera's follow and
    /// its lens the way Follow Me has them and the panel will say Follow Me, because it is.</para>
    /// </summary>
    public bool RefreshCameraMode()
    {
        BasisCameraMode resolved = ResolveCameraMode();
        if (resolved == CameraMode) return false;

        CameraMode = resolved;
        return true;
    }

    private BasisCameraMode ResolveCameraMode()
    {
        if (CameraMode != BasisCameraMode.Custom && MatchesCameraMode(CameraMode))
        {
            return CameraMode;
        }

        for (int Index = 0; Index < BasisCameraModes.Ordered.Length; Index++)
        {
            BasisCameraMode candidate = BasisCameraModes.Ordered[Index];
            if (candidate != BasisCameraMode.Custom && MatchesCameraMode(candidate))
            {
                return candidate;
            }
        }

        return BasisCameraMode.Custom;
    }

    /// <summary>
    /// Restores a saved mode as part of loading a settings file.
    ///
    /// <para>Only the mode's <em>placement</em> is re-armed — follow, the shot rig, and the pin.
    /// Those three are deliberately absent from the file, so without this a camera would come back
    /// labelled Cinematic while sitting inert in your hand. Everything else a preset writes is
    /// persisted in its own right and has just been applied from the file, so re-applying it here
    /// would overwrite the user's saved values with the preset's.</para>
    ///
    /// <para>The label is then re-derived rather than asserted: a file whose values no longer match
    /// the mode it names settles on Custom instead of lying, and a hand-tuned file that happens to
    /// match a preset exactly is promoted to it.</para>
    /// </summary>
    internal void RestoreCameraMode(BasisCameraMode mode)
    {
        if (TryGetPreset(mode, out BasisCameraModePreset preset))
        {
            ApplyPresetPlacement(preset);
        }

        CameraMode = mode;
        RefreshCameraMode();
    }

#if UNITY_INCLUDE_TESTS
    /// <summary>Test-only access to the restore, which is otherwise only reached through a load.</summary>
    public void RestoreCameraModeForTest(BasisCameraMode mode) => RestoreCameraMode(mode);
#endif
}
