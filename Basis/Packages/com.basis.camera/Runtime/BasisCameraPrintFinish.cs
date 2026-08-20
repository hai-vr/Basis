using UnityEngine;

/// <summary>
/// What happens to a picture after it has been taken: the border it is mounted in, and the light
/// that got to the film before the shutter did.
///
/// <para>Geometry only, like <see cref="BasisCameraStampPainter"/> next door and for the same
/// reason — the shape of a print and the falloff of a leak are both worth pinning in a test, and
/// neither needs a texture to be decided. The camera does the one part that needs pixels.</para>
/// </summary>
public static class BasisCameraPrintFinish
{
    // A Polaroid 600 print measures 3.5 x 4.2 inches with a 3.1 x 3.1 inch image in it. The sides
    // and the top take an even 0.2 inch each and the bottom takes the remaining 0.9 — which is the
    // white strip everybody has written a name on, and the reason the frame is recognisable at all
    // rather than just a white edge.
    private const float InstantSideBorder = 0.2f / 3.1f;
    private const float InstantTopBorder = 0.2f / 3.1f;
    private const float InstantBottomBorder = 0.9f / 3.1f;

    /// <summary>
    /// Instant film stock is not paper white — the sheet is a warm off-white when it is new and
    /// yellows from there. Pure white reads as a UI frame drawn over a photograph.
    /// </summary>
    public static readonly Color32 InstantBorderColour = new Color32(242, 239, 230, 255);

    /// <summary>What a fogged frame is exposed by: daylight through a red-orange film base.</summary>
    public static readonly Color32 LeakColour = new Color32(255, 106, 40, 255);

    /// <summary>How far in from the edge the fog reaches, as a share of the shorter side.</summary>
    private const float LeakDepthFraction = 0.28f;

    /// <summary>Exposure added at the very edge. Enough to blow a midtone out, not enough to erase it.</summary>
    private const float LeakPeakStrength = 0.62f;

    /// <summary>
    /// The mount for a finished picture: where the image sits, and how big the print around it is.
    ///
    /// <para>False for a body that hands you the picture rather than a print. The image is placed
    /// rather than scaled — a print is the photograph with a frame around it, so the picture keeps
    /// every pixel it was rendered with and the print is simply bigger.</para>
    /// </summary>
    public static bool TryGetMount(
        BasisCameraPrintBorder border,
        int imageWidth,
        int imageHeight,
        out RectInt imageRect,
        out int printWidth,
        out int printHeight)
    {
        imageRect = default;
        printWidth = 0;
        printHeight = 0;

        if (border != BasisCameraPrintBorder.Instant) return false;
        if (imageWidth <= 0 || imageHeight <= 0) return false;

        // All three measured against the image's WIDTH, not against their own axis. The borders of
        // a real print are a fixed width of stock, so scaling the side border with the width and
        // the bottom border with the height would pull them apart on any frame that is not square.
        int side = Mathf.Max(1, Mathf.RoundToInt(imageWidth * InstantSideBorder));
        int top = Mathf.Max(1, Mathf.RoundToInt(imageWidth * InstantTopBorder));
        int bottom = Mathf.Max(1, Mathf.RoundToInt(imageWidth * InstantBottomBorder));

        printWidth = imageWidth + side * 2;
        printHeight = imageHeight + top + bottom;

        // Bottom-left origin, matching raw texture rows — so the fat border is the one at y = 0 and
        // the image sits above it.
        imageRect = new RectInt(side, bottom, imageWidth, imageHeight);
        return true;
    }

    /// <summary>
    /// Whether the frame just taken is one of the ones that gets fogged.
    ///
    /// <para>The first and the last two of a roll, which is where it happens on a real camera: the
    /// leader is exposed while the film is being loaded, and the last frames sit against the end of
    /// the spool with the back being opened over them. Not random — a leak that could land on any
    /// frame reads as a filter, and one that always lands on the same three reads as a camera.</para>
    /// </summary>
    public static bool ShouldLeak(int exposuresRemaining, int rollSize)
    {
        if (rollSize <= 3) return false;

        return exposuresRemaining == rollSize - 1 || exposuresRemaining <= 1;
    }

    /// <summary>
    /// The band of fog on one frame: which edge it comes in from, how far it reaches, and how hard
    /// it is at the edge.
    ///
    /// <para><paramref name="seed"/> is the frame counter rather than a random number, so the same
    /// frame of the same roll always fogs the same way — which is what makes it a property of the
    /// camera rather than an effect that fires at it.</para>
    /// </summary>
    public static bool TryGetLeak(int seed, int width, int height, out int edge, out int depth, out float strength)
    {
        edge = 0;
        depth = 0;
        strength = 0f;

        if (width <= 0 || height <= 0) return false;

        depth = Mathf.RoundToInt(Mathf.Min(width, height) * LeakDepthFraction);
        if (depth < 2) return false;

        // 0 left, 1 right, 2 bottom, 3 top.
        edge = ((seed % 4) + 4) % 4;
        strength = LeakPeakStrength;
        return true;
    }

    /// <summary>
    /// How much fog reaches one pixel: full at the edge, gone at the depth, and falling off as a
    /// square so the band has no line where it stops.
    /// </summary>
    public static float LeakFalloff(int distanceFromEdge, int depth)
    {
        if (depth <= 0 || distanceFromEdge >= depth) return 0f;
        if (distanceFromEdge <= 0) return 1f;

        float t = 1f - (distanceFromEdge / (float)depth);
        return t * t;
    }
}
