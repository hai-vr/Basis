using UnityEngine;

/// <summary>How the alignment grid divides the frame.</summary>
public enum BasisCameraGridPattern
{
    /// <summary>The rule of thirds — two lines each way, subjects on the intersections.</summary>
    Thirds = 0,

    /// <summary>Quarters, so the frame has a centre line as well as the off-centre pair.</summary>
    Quarters = 1,

    /// <summary>The golden ratio's placement, which the rule of thirds approximates.</summary>
    GoldenRatio = 2,

    /// <summary>Corner to corner both ways, for judging a level horizon and a leading line.</summary>
    Diagonals = 3,

    /// <summary>One line each way through the middle, for centring and nothing else.</summary>
    Centre = 4,
}

/// <summary>
/// The viewfinder's alignment grid. Framing is the one thing a preview this small is still good
/// for, and it is the thing hardest to judge by eye — a horizon a degree off reads as level until
/// there is a straight line beside it.
/// <para>
/// Drawn the way focus peaking is, and for the same reason: into a render texture of its own that
/// only the viewfinder surfaces are pointed at. Every capture path reads <c>renderTexture</c>
/// directly, so the grid cannot reach a photo, a recording or a stream by construction. It is
/// sourced from the peaked picture rather than from the feed, so the two overlays compose, and it
/// draws over the peaks rather than under them since it is the thing being aligned against.
/// </para>
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>Whether the viewfinder is drawing an alignment grid over the shot.</summary>
    public bool viewfinderGridEnabled;

    /// <summary>Which grid is drawn, as an index into <see cref="GridPatternKeys"/>.</summary>
    public int viewfinderGridPattern;

    /// <summary>How strongly the lines are drawn over the picture, 0 to 1.</summary>
    public float viewfinderGridOpacity = DefaultGridOpacity;

    public const float DefaultGridOpacity = 0.6f;

    /// <summary>
    /// Opacity limits. Neither end is an off switch — the toggle owns that — so the low end is
    /// still a line that can be found and the high end stops short of hiding the shot behind it.
    /// </summary>
    public const float MinGridOpacity = 0.1f;
    public const float MaxGridOpacity = 1f;

    private const string GridShaderResource = "BasisGridOverlay";

    /// <summary>Localisation keys for the patterns, in <see cref="BasisCameraGridPattern"/> order.</summary>
    public static readonly string[] GridPatternKeys =
    {
        "camera.grid.thirds",
        "camera.grid.quarters",
        "camera.grid.golden",
        "camera.grid.diagonal",
        "camera.grid.centre",
    };

    private const int EvenShaderPattern = 0;
    private const int GoldenShaderPattern = 1;
    private const int DiagonalShaderPattern = 2;
    private const int CentreShaderPattern = 3;

    /// <summary>Which branch of the shader each pattern takes, in <see cref="GridPatternKeys"/> order.</summary>
    private static readonly int[] GridShaderPatterns =
    {
        EvenShaderPattern,
        EvenShaderPattern,
        GoldenShaderPattern,
        DiagonalShaderPattern,
        CentreShaderPattern,
    };

    /// <summary>
    /// Cells per axis for the evenly divided patterns, in the same order. Zero where the pattern is
    /// not an even division and the shader has no use for it.
    /// </summary>
    private static readonly float[] GridDivisionCounts = { 3f, 4f, 0f, 0f, 0f };

    /// <summary>
    /// Feed height a one-pixel line is drawn at. Above it the line widens in step, so the grid is
    /// the same weight to look at whether the viewfinder is running at 720p or at 4K.
    /// </summary>
    private const float GridReferenceHeight = 720f;
    private const float MinGridLineWidth = 1f;
    private const float MaxGridLineWidth = 6f;

    private RenderTexture gridTexture;
    private Material gridMaterial;
    private bool gridLive;
    private bool gridShaderMissing;

    private static readonly int GridColourProperty = Shader.PropertyToID("_GridColor");
    private static readonly int GridOpacityProperty = Shader.PropertyToID("_GridOpacity");
    private static readonly int GridThicknessProperty = Shader.PropertyToID("_GridThickness");
    private static readonly int GridPatternProperty = Shader.PropertyToID("_GridPattern");
    private static readonly int GridDivisionsProperty = Shader.PropertyToID("_GridDivisions");

    /// <summary>
    /// What the viewfinder surfaces show: the gridded copy while the grid is running, and whatever
    /// focus peaking left otherwise. The last link in the overlay chain — everything that binds a
    /// feed reads this, and every capture path reads <c>renderTexture</c> instead.
    /// </summary>
    public RenderTexture ViewfinderTexture =>
        gridLive && gridTexture != null ? gridTexture : PeakedTexture;

    /// <summary>True while the viewfinder is showing the grid rather than the picture alone.</summary>
    public bool IsViewfinderGridLive => gridLive;

    /// <summary>The pattern a given index selects, clamped so a stale index cannot read off the table.</summary>
    public static int GridPattern(int index) => Mathf.Clamp(index, 0, GridPatternKeys.Length - 1);

    /// <summary>How wide the lines are drawn, in pixels, for a feed of the given height.</summary>
    public static float GridLineThickness(int feedHeight) =>
        Mathf.Clamp(Mathf.Round(feedHeight / GridReferenceHeight), MinGridLineWidth, MaxGridLineWidth);

    public void SetViewfinderGridEnabled(bool enabled)
    {
        if (viewfinderGridEnabled == enabled) return;
        viewfinderGridEnabled = enabled;

        // Switched off means the overlay target has no reader left. Held while it is on, since the
        // feed is rebuilt for every resolution change and the two would otherwise churn together.
        if (!enabled)
        {
            SetGridLive(false);
            ReleaseGridTexture();
        }
    }

    public void SetViewfinderGridPattern(int index) => viewfinderGridPattern = GridPattern(index);

    public void SetViewfinderGridOpacity(float opacity) =>
        viewfinderGridOpacity = Mathf.Clamp(opacity, MinGridOpacity, MaxGridOpacity);

    /// <summary>
    /// Produces this frame's grid. Runs from the camera's render-phase tick after the peaks and
    /// ahead of everything that binds a feed, so a surface set up this frame is pointed at a
    /// texture that already holds a frame rather than at one frame of black.
    /// </summary>
    private void TickViewfinderGrid()
    {
        if (!viewfinderGridEnabled || captureInFlight || renderTexture == null)
        {
            SetGridLive(false);
            return;
        }

        Material material = ResolveGridMaterial();
        if (material == null)
        {
            SetGridLive(false);
            return;
        }

        RenderTexture source = PeakedTexture;
        if (source == null)
        {
            SetGridLive(false);
            return;
        }

        bool rebuilt = EnsureGridTexture(source);
        if (gridTexture == null)
        {
            SetGridLive(false);
            return;
        }

        // The gate the peaks use: a rate-limited camera re-presents the same frame for several
        // frames in a row, and drawing the grid over it again costs the same and changes nothing.
        // A texture that has just been built has nothing in it yet, so that one is drawn regardless.
        if (rebuilt || captureCamera == null || captureCamera.enabled)
        {
            int index = GridPattern(viewfinderGridPattern);

            material.SetColor(GridColourProperty, Color.white);
            material.SetFloat(GridOpacityProperty, Mathf.Clamp(viewfinderGridOpacity, MinGridOpacity, MaxGridOpacity));
            material.SetFloat(GridThicknessProperty, GridLineThickness(source.height));
            material.SetFloat(GridPatternProperty, GridShaderPatterns[index]);
            material.SetFloat(GridDivisionsProperty, GridDivisionCounts[index]);
            Graphics.Blit(source, gridTexture, material);
        }

        gridLive = true;

        // Every frame rather than only on the transition, and change-gated inside: the feed is
        // rebuilt whenever the resolution moves, the overlay is rebuilt to match, and the prop's
        // material would otherwise be left holding the destroyed one.
        BindViewfinderFeed();
    }

    private void SetGridLive(bool live)
    {
        if (gridLive == live) return;
        gridLive = live;

        // The prop's viewfinder holds its texture on a material rather than reading it back every
        // frame the way the other surfaces do, so it is the one that has to be told.
        BindViewfinderFeed();
    }

    private Material ResolveGridMaterial()
    {
        if (gridMaterial != null) return gridMaterial;
        if (gridShaderMissing) return null;

        // From Resources rather than Shader.Find, for the reason the peaking shader is: nothing in
        // a scene or a prefab references it, so a build would strip it and the grid would be
        // missing only in the player.
        Shader shader = Resources.Load<Shader>(GridShaderResource);
        if (shader == null)
        {
            gridShaderMissing = true;
            BasisDebug.LogError(
                $"Grid overlay shader '{GridShaderResource}' could not be loaded — the alignment grid is unavailable.",
                BasisDebug.LogTag.Camera);
            return null;
        }

        gridMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return gridMaterial;
    }

    /// <summary>Matches the grid target to what it draws over. Returns true when it had to be rebuilt.</summary>
    private bool EnsureGridTexture(RenderTexture source)
    {
        if (source == null) return false;

        if (gridTexture != null
            && gridTexture.width == source.width
            && gridTexture.height == source.height
            && gridTexture.graphicsFormat == source.graphicsFormat)
        {
            return false;
        }

        ReleaseGridTexture();

        RenderTextureDescriptor descriptor = source.descriptor;
        descriptor.msaaSamples = 1;
        descriptor.depthBufferBits = 0;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;

        gridTexture = new RenderTexture(descriptor) { name = "BasisGridOverlay" };
        gridTexture.Create();
        return true;
    }

    private void ReleaseGridTexture()
    {
        if (gridTexture == null) return;
        gridTexture.Release();
        Destroy(gridTexture);
        gridTexture = null;
    }

    private void ReleaseViewfinderGrid()
    {
        gridLive = false;
        ReleaseGridTexture();
        if (gridMaterial != null)
        {
            Destroy(gridMaterial);
            gridMaterial = null;
        }
    }
}
