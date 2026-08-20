using UnityEngine;
using CameraPinSpace = BasisHandHeldCameraInteractable.CameraPinSpace;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

/// <summary>
/// Saved modes on the camera: putting one on, noticing when the camera has drifted off it, and
/// taking a snapshot of the camera to make one out of.
///
/// <para>The built-in modes next door in <see cref="BasisHandHeldCameraModes"/> are a table of
/// values compared field by field, because each one is a small opinion and the comparison has to
/// say which opinion the camera currently holds. A saved mode is a whole settings file, so both
/// halves go through the machinery that already exists for settings files: applying one is the
/// same apply a load runs, and checking one is the same harvest a save runs, compared against what
/// was stored. Nothing here knows what a setting <em>is</em>, which is exactly why a setting added
/// to the camera later is carried by saved modes without this file changing.</para>
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>
    /// The saved mode in control, or null when none is. Kept beside <see cref="CameraMode"/>
    /// rather than folded into it: the built-in label is still worth deriving underneath — a saved
    /// mode whose values happen to be Photo's is still Photo — but the name someone chose is what
    /// the panel shows, because they chose it.
    /// </summary>
    public string UserModeName { get; private set; }

    /// <summary>
    /// Puts a saved mode on: its settings, then where it sits and whether it flies.
    ///
    /// <para>The settings are applied from a <em>copy</em>. <see cref="BasisHandHeldCameraUI"/>
    /// keeps the last file it applied as the baseline that carries values with no live source
    /// forward into the next save — so handing it the stored object directly would leave the next
    /// save writing through into the saved mode, quietly rewriting a mode nobody asked to
    /// change.</para>
    /// </summary>
    public void ApplyUserMode(BasisCameraUserMode mode)
    {
        if (mode?.settings == null || HandHeld == null) return;

        HandHeld.ApplyModeSettings(CopySettings(mode.settings));

        // After the settings, not before: applying a file re-arms the placement the file's own
        // mode label asks for, and the mode being put on is the one that gets the last word.
        ApplyPlacement((CameraPinSpace)mode.pinSpace, null);
        SyncPropUiAfterModeChange();

        // Derived first so the built-in label underneath is honest, then the name is asserted over
        // the top of it — this is the one moment the mode is known rather than worked out.
        RefreshCameraMode();
        UserModeName = mode.name;
    }

    /// <summary>
    /// Restores the saved mode a settings file named, as the last step of loading it.
    ///
    /// <para>Only the pin, for the same reason as <see cref="RestoreCameraMode"/>: the stack is
    /// carried by the settings file, which has just been applied. Then the claim is checked,
    /// because the mode may have been edited or deleted since the file naming it was written.</para>
    /// </summary>
    internal void RestoreUserMode(string name)
    {
        UserModeName = null;
        if (string.IsNullOrEmpty(name)) return;

        BasisCameraUserMode mode = BasisCameraUserModes.Find(name);
        if (mode == null) return;

        ApplyPlacement((CameraPinSpace)mode.pinSpace, null);
        UserModeName = mode.name;

        // The claim, checked. The file has just landed, so harvesting it back is the cheapest
        // honest answer to "is this still that mode" — and it has to be asked, because the mode
        // may have been saved over since the file naming it was written.
        if (HandHeld != null) RefreshUserMode(HandHeld.CaptureSettings());
    }

    /// <summary>
    /// Drops the saved mode's name if the camera no longer holds what it saved, and reports
    /// whether it did. Takes the harvested settings rather than harvesting them, so the save path
    /// — which has just built exactly this object — does not build a second one, and so this can
    /// never re-enter the harvest that calls it.
    /// </summary>
    public bool RefreshUserMode(CameraSettings live)
    {
        if (string.IsNullOrEmpty(UserModeName)) return false;

        BasisCameraUserMode mode = BasisCameraUserModes.Find(UserModeName);
        if (mode != null && mode.Matches(live)) return false;

        UserModeName = null;
        return true;
    }

    /// <summary>
    /// Claims a saved mode's name without applying anything.
    ///
    /// <para>For the one case where the camera already holds the mode's values because the mode
    /// was just taken from it: saving. Running the apply there would rebuild the render target and
    /// re-seed every control to land on the numbers already in place.</para>
    /// </summary>
    public void AdoptUserMode(BasisCameraUserMode mode)
    {
        if (mode == null) return;

        RefreshCameraMode();
        UserModeName = mode.name;
    }

    /// <summary>
    /// Everything the camera is set to right now, as a mode waiting for a name.
    ///
    /// <para>The two labels are cleared on the way out, and so is the frame counter. A mode that
    /// recorded which mode was current would restore that name alongside its own values, so picking
    /// it would announce the mode it was saved from rather than itself.</para>
    /// </summary>
    public BasisCameraUserMode CaptureUserMode(string name, Color tint)
    {
        BasisCameraUserMode mode = new BasisCameraUserMode
        {
            name = name,
            tint = tint,
            pinSpace = (int)PinSpace,
            settings = HandHeld != null ? HandHeld.CaptureSettings() : new CameraSettings(),
        };

        mode.settings.cameraMode = (int)BasisCameraMode.Custom;
        mode.settings.userMode = string.Empty;

        // The body is kept — a mode saved off a disposable is a disposable — but not what was left
        // on the load. A mode is a configuration, and one that handed back the twelve frames its
        // owner happened to have left would be a snapshot of an afternoon instead.
        mode.settings.exposuresRemaining = FullRoll;
        return mode;
    }

    /// <summary>
    /// A settings file that shares nothing with the one it came from — including the shot list,
    /// which is a reference type and would otherwise be edited in place by the live rig.
    /// </summary>
    internal static CameraSettings CopySettings(CameraSettings settings) =>
        settings == null ? null : JsonUtility.FromJson<CameraSettings>(JsonUtility.ToJson(settings));
}
