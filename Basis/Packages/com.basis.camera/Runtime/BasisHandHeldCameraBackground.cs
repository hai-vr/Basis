using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>What the capture camera puts behind the subject.</summary>
public enum BasisCameraBackgroundMode
{
    /// <summary>The world as it actually is, matching the player's own view.</summary>
    World = 0,
    GreenScreen = 1,
    BlueScreen = 2,
    Black = 3,
    White = 4,
    Magenta = 5,
    Custom = 6,
}

/// <summary>
/// Solid-colour backgrounds for the capture camera. On its own a colour mode only changes what the
/// camera clears to, which still leaves the world in front of it — so the keyable modes also drop
/// the world out of the culling mask and leave just the players and their props on flat colour.
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>Broadcast-standard chroma green (roughly Rec.709 key green).</summary>
    public static readonly Color ChromaGreen = new Color(0f, 0.706f, 0.239f, 1f);
    public static readonly Color ChromaBlue = new Color(0f, 0.278f, 0.678f, 1f);

    [Header("Background")]
    public BasisCameraBackgroundMode backgroundMode = BasisCameraBackgroundMode.World;

    [Tooltip("Colour used by the Custom background mode.")]
    public Color backgroundCustomColor = ChromaGreen;

    [Tooltip("Keeps world geometry in shot on a colour background. Off gives a keyable matte of just players and props.")]
    public bool backgroundKeepsWorld = false;

    /// <summary>Layers kept when the world is dropped: avatars and the props they are holding.</summary>
    private static readonly string[] SubjectLayerNames =
    {
        "LocalPlayerAvatar", "RemotePlayerAvatar", "Player", "Interactable",
    };

    private int cachedWorldCullingMask;
    private bool hasCachedWorldCullingMask;
    private CameraClearFlags cachedWorldClearFlags = CameraClearFlags.Skybox;
    private Color cachedWorldBackgroundColor = Color.black;
    private bool cachedWorldSkyboxEnabled = true;
    private bool hasCachedWorldBackground;

    public bool IsBackgroundKeyable =>
        backgroundMode != BasisCameraBackgroundMode.World && !backgroundKeepsWorld;

    /// <summary>True while a colour background owns the live culling mask.</summary>
    internal bool BackgroundOwnsCullingMask =>
        backgroundMode != BasisCameraBackgroundMode.World && hasCachedWorldCullingMask;

    /// <summary>
    /// The mask the world would render with. Layer toggles must go through this rather than the
    /// camera directly: while a colour background is applied the live mask is the subject matte,
    /// so editing it would be thrown away by the next re-apply and read as a dead toggle.
    /// </summary>
    internal int WorldCullingMask
    {
        get
        {
            if (BackgroundOwnsCullingMask)
            {
                return cachedWorldCullingMask;
            }
            return captureCamera != null ? captureCamera.cullingMask : 0;
        }
        set
        {
            if (BackgroundOwnsCullingMask)
            {
                cachedWorldCullingMask = value;
                ApplyBackgroundMode();
                return;
            }
            if (captureCamera != null)
            {
                captureCamera.cullingMask = value;
            }
        }
    }

    /// <summary>The colour a mode clears to. World has none, so it falls back to the cached one.</summary>
    public static Color ColorForBackgroundMode(BasisCameraBackgroundMode mode, Color custom)
    {
        switch (mode)
        {
            case BasisCameraBackgroundMode.GreenScreen: return ChromaGreen;
            case BasisCameraBackgroundMode.BlueScreen: return ChromaBlue;
            case BasisCameraBackgroundMode.Black: return Color.black;
            case BasisCameraBackgroundMode.White: return Color.white;
            case BasisCameraBackgroundMode.Magenta: return Color.magenta;
            case BasisCameraBackgroundMode.Custom: return custom;
            default: return custom;
        }
    }

    public void SetBackgroundMode(BasisCameraBackgroundMode mode)
    {
        backgroundMode = mode;
        ApplyBackgroundMode();
    }

    public void SetBackgroundCustomColor(Color color)
    {
        backgroundCustomColor = color;
        if (backgroundMode == BasisCameraBackgroundMode.Custom)
        {
            ApplyBackgroundMode();
        }
    }

    public void SetBackgroundKeepsWorld(bool keepsWorld)
    {
        backgroundKeepsWorld = keepsWorld;
        ApplyBackgroundMode();
    }

    /// <summary>
    /// Applies the current mode. Caches the world's own clear settings and culling mask on the
    /// first colour mode so switching back to World restores exactly what the camera had, rather
    /// than a guess at what it should be.
    /// </summary>
    public void ApplyBackgroundMode()
    {
        if (captureCamera == null)
        {
            return;
        }

        if (backgroundMode == BasisCameraBackgroundMode.World)
        {
            RestoreWorldBackground();
            return;
        }

        CacheWorldBackground();

        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = ColorForBackgroundMode(backgroundMode, backgroundCustomColor);

        if (captureCamera.TryGetComponent(out Skybox skybox))
        {
            skybox.enabled = false;
        }

        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            cameraData.renderShadows = backgroundKeepsWorld;
        }

        captureCamera.cullingMask = backgroundKeepsWorld ? cachedWorldCullingMask : BuildSubjectMask(cachedWorldCullingMask);
    }

    private void CacheWorldBackground()
    {
        if (!hasCachedWorldBackground)
        {
            cachedWorldClearFlags = captureCamera.clearFlags;
            cachedWorldBackgroundColor = captureCamera.backgroundColor;
            cachedWorldSkyboxEnabled = !captureCamera.TryGetComponent(out Skybox existing) || existing.enabled;
            hasCachedWorldBackground = true;
        }

        if (!hasCachedWorldCullingMask)
        {
            cachedWorldCullingMask = captureCamera.cullingMask;
            hasCachedWorldCullingMask = true;
        }
    }

    private void RestoreWorldBackground()
    {
        bool restoreSkybox = true;

        if (hasCachedWorldBackground)
        {
            captureCamera.clearFlags = cachedWorldClearFlags;
            captureCamera.backgroundColor = cachedWorldBackgroundColor;
            restoreSkybox = cachedWorldSkyboxEnabled;
            hasCachedWorldBackground = false;
        }
        else
        {
            SyncBackgroundFromMainCamera();
        }

        if (hasCachedWorldCullingMask)
        {
            captureCamera.cullingMask = cachedWorldCullingMask;
            hasCachedWorldCullingMask = false;
        }

        if (captureCamera.TryGetComponent(out Skybox skybox))
        {
            skybox.enabled = restoreSkybox;
        }

        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            cameraData.renderShadows = true;
        }
    }

    /// <summary>
    /// Narrows a culling mask to the subject layers, intersected with what was already being
    /// rendered — so a layer the operator had turned off does not reappear on the matte.
    /// </summary>
    public static int BuildSubjectMask(int worldMask)
    {
        int subject = 0;
        for (int Index = 0; Index < SubjectLayerNames.Length; Index++)
        {
            int layer = LayerMask.NameToLayer(SubjectLayerNames[Index]);
            if (layer >= 0)
            {
                subject |= 1 << layer;
            }
        }

        int result = worldMask & subject;
        return result == 0 ? subject : result;
    }

    /// <summary>
    /// Re-asserts the mode after anything else has written the camera's clear settings — the
    /// nameplate and render-layer toggles both edit the culling mask, and a world sync runs on
    /// every scene change.
    /// </summary>
    public void RefreshBackgroundMode()
    {
        if (backgroundMode == BasisCameraBackgroundMode.World)
        {
            return;
        }
        ApplyBackgroundMode();
    }
}
