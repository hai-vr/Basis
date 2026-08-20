using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The date a film camera's databack burned into the corner, and the clock a tape deck wrote in the
/// same place, as seven-segment type.
///
/// <para>Seven segments rather than a font: it is what the real thing was, it needs no asset and no
/// atlas, and it reduces to a handful of rectangles — which is the whole reason this class exists as
/// geometry rather than as pixels. <see cref="BuildGlyphs"/> is pure, so the layout can be tested
/// without a texture, and the caller does the one thing that needs the image: filling rectangles.
/// </para>
///
/// <para>Rectangles come back in <b>bottom-left origin</b> pixel coordinates, matching the row order
/// of raw texture data — row zero is the bottom of the picture — so a caller writing straight into a
/// readback buffer needs no flip.</para>
/// </summary>
public static class BasisCameraStampPainter
{
    /// <summary>The orange a film databack printed in, which is the light it exposed the negative with.</summary>
    public static readonly Color32 DateColour = new Color32(255, 138, 32, 255);

    /// <summary>The flat white a recorder's character generator wrote over the picture.</summary>
    public static readonly Color32 TimecodeColour = new Color32(236, 236, 236, 255);

    /// <summary>Cell height as a share of the picture's height.</summary>
    private const float CellHeightFraction = 0.042f;

    /// <summary>Below this a segment is thinner than a pixel and the stamp is mud, so nothing is drawn.</summary>
    private const int MinimumCellHeight = 9;

    private const float CellWidthRatio = 0.58f;
    private const float CellGapRatio = 0.22f;
    private const float StrokeRatio = 0.17f;

    /// <summary>Distance from the picture's edge to the stamp, as a share of the picture's height.</summary>
    private const float MarginFraction = 0.035f;

    /// <summary>Largest share of the picture's width the stamp may take before it is scaled down.</summary>
    private const float MaximumWidthFraction = 0.62f;

    // Segments of a seven-segment cell, in the usual lettering: A across the top, B and C down the
    // right, D across the bottom, E and F up the left, G across the middle.
    private const int SegA = 1 << 0;
    private const int SegB = 1 << 1;
    private const int SegC = 1 << 2;
    private const int SegD = 1 << 3;
    private const int SegE = 1 << 4;
    private const int SegF = 1 << 5;
    private const int SegG = 1 << 6;

    private static readonly int[] DigitSegments =
    {
        SegA | SegB | SegC | SegD | SegE | SegF,          // 0
        SegB | SegC,                                       // 1
        SegA | SegB | SegD | SegE | SegG,                  // 2
        SegA | SegB | SegC | SegD | SegG,                  // 3
        SegB | SegC | SegF | SegG,                         // 4
        SegA | SegC | SegD | SegF | SegG,                  // 5
        SegA | SegC | SegD | SegE | SegF | SegG,           // 6
        SegA | SegB | SegC,                                // 7
        SegA | SegB | SegC | SegD | SegE | SegF | SegG,    // 8
        SegA | SegB | SegC | SegD | SegF | SegG,           // 9
    };

    /// <summary>The text a body stamps at the given moment, or null where it stamps nothing.</summary>
    public static string Compose(BasisCameraStamp stamp, DateTime when)
    {
        switch (stamp)
        {
            // Two-digit year behind an apostrophe, the way every consumer databack printed it. The
            // separators are spaces rather than punctuation because a databack had no punctuation.
            case BasisCameraStamp.Date:
                return "'" + when.ToString("yy MM dd", CultureInfo.InvariantCulture);

            case BasisCameraStamp.Timecode:
                return when.ToString("yy MM dd  HH:mm:ss", CultureInfo.InvariantCulture);

            default:
                return null;
        }
    }

    /// <summary>The colour a stamp is burned in.</summary>
    public static Color32 ColourOf(BasisCameraStamp stamp) =>
        stamp == BasisCameraStamp.Date ? DateColour : TimecodeColour;

    /// <summary>
    /// Lays a stamp out in the bottom-right corner of a picture and returns the rectangles that
    /// draw it.
    ///
    /// <para>False, with nothing added, whenever the stamp cannot be drawn honestly: no text, a
    /// picture too small for the type to survive being written into it, or a stamp so long it would
    /// run past the middle of the frame. A stamp that cannot be read is worse than none — it is a
    /// row of orange smudges over somebody's photograph.</para>
    /// </summary>
    public static bool BuildGlyphs(string text, int imageWidth, int imageHeight, List<RectInt> into)
    {
        if (into == null) return false;
        if (string.IsNullOrEmpty(text) || imageWidth <= 0 || imageHeight <= 0) return false;

        int cellHeight = Mathf.RoundToInt(imageHeight * CellHeightFraction);
        if (cellHeight < MinimumCellHeight) return false;

        int cellWidth = Mathf.Max(3, Mathf.RoundToInt(cellHeight * CellWidthRatio));
        int gap = Mathf.Max(1, Mathf.RoundToInt(cellHeight * CellGapRatio));

        int advance = cellWidth + gap;
        int totalWidth = text.Length * advance - gap;

        // A long stamp on a narrow frame is scaled down rather than cropped: the timecode bodies
        // shoot 640 wide, and a clock that ran off the edge would lose its seconds first.
        if (totalWidth > imageWidth * MaximumWidthFraction)
        {
            float shrink = (imageWidth * MaximumWidthFraction) / totalWidth;
            cellHeight = Mathf.RoundToInt(cellHeight * shrink);
            if (cellHeight < MinimumCellHeight) return false;

            cellWidth = Mathf.Max(3, Mathf.RoundToInt(cellHeight * CellWidthRatio));
            gap = Mathf.Max(1, Mathf.RoundToInt(cellHeight * CellGapRatio));
            advance = cellWidth + gap;
            totalWidth = text.Length * advance - gap;
        }

        int stroke = Mathf.Max(1, Mathf.RoundToInt(cellHeight * StrokeRatio));

        // Two strokes of upright and one of crossbar is the least a cell can be and still be a
        // digit; below it the middle segment merges with the top and bottom into a solid block.
        if (cellHeight < stroke * 3 || cellWidth < stroke * 3) return false;

        int margin = Mathf.Max(2, Mathf.RoundToInt(imageHeight * MarginFraction));
        int originX = imageWidth - margin - totalWidth;
        int originY = margin;
        if (originX < margin) return false;

        int before = into.Count;
        for (int Index = 0; Index < text.Length; Index++)
        {
            AppendGlyph(text[Index], originX + Index * advance, originY, cellWidth, cellHeight, stroke, into);
        }

        return into.Count > before;
    }

    private static void AppendGlyph(char character, int x, int y, int w, int h, int t, List<RectInt> into)
    {
        if (character == ' ') return;

        if (character == ':')
        {
            // Two square pips on the centre line, at the heights the colon of a clock display sits.
            int pipX = x + (w - t) / 2;
            into.Add(new RectInt(pipX, y + h / 4, t, t));
            into.Add(new RectInt(pipX, y + (h * 3) / 4 - t, t, t));
            return;
        }

        if (character == '\'')
        {
            // A single upright tick at the top, which is all a databack's year mark ever was.
            into.Add(new RectInt(x + (w - t) / 2, y + h - t * 2, t, t * 2));
            return;
        }

        if (character < '0' || character > '9') return;

        int segments = DigitSegments[character - '0'];
        int inner = w - t * 2;
        int half = h / 2;

        if ((segments & SegA) != 0) into.Add(new RectInt(x + t, y + h - t, inner, t));
        if ((segments & SegG) != 0) into.Add(new RectInt(x + t, y + half - t / 2, inner, t));
        if ((segments & SegD) != 0) into.Add(new RectInt(x + t, y, inner, t));
        if ((segments & SegF) != 0) into.Add(new RectInt(x, y + half, t, h - half));
        if ((segments & SegB) != 0) into.Add(new RectInt(x + w - t, y + half, t, h - half));
        if ((segments & SegE) != 0) into.Add(new RectInt(x, y, t, half));
        if ((segments & SegC) != 0) into.Add(new RectInt(x + w - t, y, t, half));
    }
}
