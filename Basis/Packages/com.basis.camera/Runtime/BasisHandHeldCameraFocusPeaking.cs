using UnityEngine;

/// <summary>
/// Focus peaking: the viewfinder tints whatever the lens is actually resolving, so focus can be
/// judged on a small preview where the blur itself is too subtle to see. Detail is measured off
/// the rendered picture rather than off depth, which means it answers the question that matters —
/// what came out sharp — for every reason a subject can be soft, not just depth of field.
/// <para>
/// The overlay is produced into a render texture of its own and only the viewfinder surfaces are
/// pointed at it. Every capture path — the still readback, both recorders, the video sinks and the
/// 360 pass — reads <c>renderTexture</c> directly, so the peaks cannot reach a shot by construction
/// rather than by remembering to switch them off first.
/// </para>
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>Whether the viewfinder is tinting resolved detail.</summary>
    public bool focusPeakingEnabled;

    /// <summary>How readily detail counts as in focus, 0 to 1. Higher paints more of the frame.</summary>
    public float focusPeakingSensitivity = DefaultFocusPeakingSensitivity;

    /// <summary>Which colour the peaks are painted, as an index into <see cref="FocusPeakingColours"/>.</summary>
    public int focusPeakingColour;

    /// <summary>Whether the picture under the peaks is drained of colour so they stand out.</summary>
    public bool focusPeakingGreyPicture;

    public const float DefaultFocusPeakingSensitivity = 0.5f;

    /// <summary>
    /// Sobel response the peak ramp starts at, at the two ends of the sensitivity slider. Neither
    /// end is an off switch — the toggle owns that — so the low end still paints a hard edge and
    /// the high end stops short of painting film grain.
    /// </summary>
    public const float LeastSensitiveThreshold = 0.30f;
    public const float MostSensitiveThreshold = 0.02f;

    /// <summary>How far towards grey the picture is taken while <see cref="focusPeakingGreyPicture"/> is on.</summary>
    private const float GreyPictureStrength = 0.85f;

    private const string FocusPeakingShaderResource = "BasisFocusPeaking";

    /// <summary>
    /// The palette, red first: it is the colour least likely to occur in a face or a skin tone,
    /// which is what makes a peak read as an overlay rather than as part of the subject.
    /// </summary>
    public static readonly Color[] FocusPeakingColours =
    {
        new Color(1f, 0.15f, 0.15f),
        new Color(0.25f, 1f, 0.35f),
        new Color(0.3f, 0.6f, 1f),
        new Color(1f, 0.9f, 0.2f),
        new Color(1f, 1f, 1f),
    };

    /// <summary>Localisation keys for <see cref="FocusPeakingColours"/>, in the same order.</summary>
    public static readonly string[] FocusPeakingColourKeys =
    {
        "camera.focusPeaking.red",
        "camera.focusPeaking.green",
        "camera.focusPeaking.blue",
        "camera.focusPeaking.yellow",
        "camera.focusPeaking.white",
    };

    private RenderTexture focusPeakingTexture;
    private Material focusPeakingMaterial;
    private bool focusPeakingLive;
    private bool focusPeakingShaderMissing;

    private static readonly int PeakColourProperty = Shader.PropertyToID("_PeakColor");
    private static readonly int PeakThresholdProperty = Shader.PropertyToID("_PeakThreshold");
    private static readonly int PeakDesaturateProperty = Shader.PropertyToID("_PeakDesaturate");

    /// <summary>
    /// The peaked copy while peaking is running, and the feed itself otherwise. Falls back on its
    /// own during a capture, when the feed is briefly the capture size and format and nothing
    /// should be paying to overlay it. The alignment grid draws on top of this;
    /// <see cref="ViewfinderTexture"/> is what the viewfinder surfaces actually bind.
    /// </summary>
    private RenderTexture PeakedTexture =>
        focusPeakingLive && focusPeakingTexture != null ? focusPeakingTexture : renderTexture;

    /// <summary>True while the viewfinder is showing peaks rather than the plain feed.</summary>
    public bool IsFocusPeaking => focusPeakingLive;

    /// <summary>The Sobel response a peak starts at for a given sensitivity.</summary>
    public static float FocusPeakingThreshold(float sensitivity) =>
        Mathf.Lerp(LeastSensitiveThreshold, MostSensitiveThreshold, Mathf.Clamp01(sensitivity));

    /// <summary>The colour a peak is painted, clamped to the palette so a stale index cannot go black.</summary>
    public static Color FocusPeakingColour(int index) =>
        FocusPeakingColours[Mathf.Clamp(index, 0, FocusPeakingColours.Length - 1)];

    public void SetFocusPeakingEnabled(bool enabled)
    {
        if (focusPeakingEnabled == enabled) return;
        focusPeakingEnabled = enabled;

        // Switched off means the overlay target has no reader left. Held while it is on, since the
        // feed is rebuilt for every resolution change and the two would otherwise churn together.
        if (!enabled)
        {
            SetFocusPeakingLive(false);
            ReleaseFocusPeakingTexture();
        }
    }

    public void SetFocusPeakingSensitivity(float sensitivity) =>
        focusPeakingSensitivity = Mathf.Clamp01(sensitivity);

    public void SetFocusPeakingColour(int index) =>
        focusPeakingColour = Mathf.Clamp(index, 0, FocusPeakingColours.Length - 1);

    public void SetFocusPeakingGreyPicture(bool grey) => focusPeakingGreyPicture = grey;

    /// <summary>
    /// Produces this frame's overlay. Runs from the camera's render-phase tick, ahead of everything
    /// that binds a feed, so a surface set up this frame is pointed at a texture that already holds
    /// a frame rather than at one frame of black.
    /// </summary>
    private void TickFocusPeaking()
    {
        if (!focusPeakingEnabled || captureInFlight || renderTexture == null)
        {
            SetFocusPeakingLive(false);
            return;
        }

        Material material = ResolveFocusPeakingMaterial();
        if (material == null)
        {
            SetFocusPeakingLive(false);
            return;
        }

        bool rebuilt = EnsureFocusPeakingTexture();
        if (focusPeakingTexture == null)
        {
            SetFocusPeakingLive(false);
            return;
        }

        // A rate-limited camera re-presents the same frame for several frames in a row, and
        // overlaying it again would produce the identical result at full cost. A texture that has
        // just been built has nothing in it yet, so that one is drawn whatever the gate says.
        if (rebuilt || captureCamera == null || captureCamera.enabled)
        {
            material.SetColor(PeakColourProperty, FocusPeakingColour(focusPeakingColour));
            material.SetFloat(PeakThresholdProperty, FocusPeakingThreshold(focusPeakingSensitivity));
            material.SetFloat(PeakDesaturateProperty, focusPeakingGreyPicture ? GreyPictureStrength : 0f);
            Graphics.Blit(renderTexture, focusPeakingTexture, material);
        }

        focusPeakingLive = true;

        // Every frame rather than only on the transition, and change-gated inside: the feed is
        // rebuilt whenever the resolution moves, the overlay is rebuilt to match, and the prop's
        // material would otherwise be left holding the destroyed one.
        BindViewfinderFeed();
    }

    private void SetFocusPeakingLive(bool live)
    {
        if (focusPeakingLive == live) return;
        focusPeakingLive = live;

        // The prop's viewfinder holds its texture on a material rather than reading it back every
        // frame the way the other surfaces do, so it is the one that has to be told.
        BindViewfinderFeed();
    }

    private Material ResolveFocusPeakingMaterial()
    {
        if (focusPeakingMaterial != null) return focusPeakingMaterial;
        if (focusPeakingShaderMissing) return null;

        // From Resources rather than Shader.Find: nothing in a scene or a prefab references this
        // shader, so a build would strip it and the overlay would be missing only in the player.
        Shader shader = Resources.Load<Shader>(FocusPeakingShaderResource);
        if (shader == null)
        {
            focusPeakingShaderMissing = true;
            BasisDebug.LogError(
                $"Focus peaking shader '{FocusPeakingShaderResource}' could not be loaded — the overlay is unavailable.",
                BasisDebug.LogTag.Camera);
            return null;
        }

        focusPeakingMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return focusPeakingMaterial;
    }

    /// <summary>Matches the overlay target to the feed. Returns true when it had to be rebuilt.</summary>
    private bool EnsureFocusPeakingTexture()
    {
        if (renderTexture == null) return false;

        if (focusPeakingTexture != null
            && focusPeakingTexture.width == renderTexture.width
            && focusPeakingTexture.height == renderTexture.height
            && focusPeakingTexture.graphicsFormat == renderTexture.graphicsFormat)
        {
            return false;
        }

        ReleaseFocusPeakingTexture();

        RenderTextureDescriptor descriptor = renderTexture.descriptor;
        descriptor.msaaSamples = 1;
        descriptor.depthBufferBits = 0;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;

        focusPeakingTexture = new RenderTexture(descriptor) { name = "BasisFocusPeaking" };
        focusPeakingTexture.Create();
        return true;
    }

    private void ReleaseFocusPeakingTexture()
    {
        if (focusPeakingTexture == null) return;
        focusPeakingTexture.Release();
        Destroy(focusPeakingTexture);
        focusPeakingTexture = null;
    }

    private void ReleaseFocusPeaking()
    {
        focusPeakingLive = false;
        ReleaseFocusPeakingTexture();
        if (focusPeakingMaterial != null)
        {
            Destroy(focusPeakingMaterial);
            focusPeakingMaterial = null;
        }
    }
}
