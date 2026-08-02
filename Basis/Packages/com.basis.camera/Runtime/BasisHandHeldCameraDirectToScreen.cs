using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Direct To Screen presentation. The camera ALWAYS renders into its own render texture — this
/// just puts that texture on the main screen. Re-pointing the camera at the backbuffer instead
/// (the old approach) gave the mode a second, different render path, which is why post-processing,
/// MSAA and colour all behaved differently there. With one path they are identical by construction.
///
/// While the mode is on, the feed is also sized to the screen, so it covers it. A fixed 16:9 feed
/// can only be barred, cropped or squashed into a window of another shape; re-rendering at the
/// window's shape is the only one of those that neither throws pixels away nor distorts them, and
/// it costs the same render either way. What the wider frame shows is set by the capture camera's
/// gate fit, which is horizontal: the width of the shot stays put, so a window wider than the
/// capture aspect trims the top and bottom of the frame and a taller one shows more of them. The
/// saved photo is unaffected — it re-renders at the capture resolution when the shutter fires.
///
/// A screen-space overlay canvas is used deliberately: it is composited after all camera rendering,
/// so the capture camera can never see it and there is no feedback loop.
/// </summary>
public partial class BasisHandHeldCamera
{
    private GameObject directToScreenGO;
    private RawImage directToScreenImage;
    private AspectRatioFitter directToScreenFitter;

    /// <summary>Sorting order high enough to sit above the regular UI while mirroring.</summary>
    private const int DirectToScreenSortingOrder = 30000;

    /// <summary>
    /// Ceiling on the screen-matched feed. The camera re-renders this every frame the mode is on,
    /// so a 5K or 8K desktop would otherwise quietly multiply its cost; past this the image is
    /// scaled up to the screen, which is what it did at every size before.
    /// </summary>
    private const int DirectToScreenMaxDimension = 3840;

    /// <summary>
    /// Pixel size the feed should render at to cover the screen exactly. Prefers the overlay
    /// canvas's own rect — that is literally the area being filled — and falls back to
    /// <see cref="Screen"/> for the first frame, before the canvas has laid out.
    /// </summary>
    private bool TryGetDirectToScreenFeedSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        float pixelWidth = Screen.width;
        float pixelHeight = Screen.height;

        if (directToScreenGO != null && directToScreenGO.transform is RectTransform canvasRect)
        {
            Rect rect = canvasRect.rect;
            if (rect.width >= 1f && rect.height >= 1f)
            {
                pixelWidth = rect.width;
                pixelHeight = rect.height;
            }
        }

        if (pixelWidth < 1f || pixelHeight < 1f) return false;

        float clamp = Mathf.Min(1f, DirectToScreenMaxDimension / Mathf.Max(pixelWidth, pixelHeight));
        width = Mathf.Max(16, Mathf.RoundToInt(pixelWidth * clamp));
        height = Mathf.Max(16, Mathf.RoundToInt(pixelHeight * clamp));
        return true;
    }

    private void SetDirectToScreenOverlayActive(bool active)
    {
        if (!active)
        {
            DespawnDirectToScreenOverlay();
            // Back to the authored preview size: nothing is filling the screen with it any more,
            // and leaving it at the window's shape would keep charging for pixels no one sees.
            ApplyPreviewResolution();
            return;
        }

        if (directToScreenGO == null)
        {
            SpawnDirectToScreenOverlay();
        }

        UpdateDirectToScreenTexture();
    }

    private void SpawnDirectToScreenOverlay()
    {
        directToScreenGO = new GameObject("CameraDirectToScreen");

        Canvas canvas = directToScreenGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = DirectToScreenSortingOrder;

        GameObject feed = new GameObject("Feed", typeof(RectTransform));
        feed.transform.SetParent(directToScreenGO.transform, false);

        directToScreenImage = feed.AddComponent<RawImage>();
        directToScreenImage.raycastTarget = false;

        // Centre-anchored, because AspectRatioFitter drives the size and warns on stretched anchors.
        RectTransform rect = directToScreenImage.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        // The feed is sized to the screen, so this normally has nothing to do. It still matters for
        // the frames where it cannot be: a still capture owns the RT at the capture aspect until its
        // readback lands. Fitting bars those frames; stretching would visibly squash them.
        directToScreenFitter = feed.AddComponent<AspectRatioFitter>();
        directToScreenFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        directToScreenFitter.aspectRatio = 16f / 9f;
    }

    /// <summary>
    /// Keeps the feed matched to the screen and the overlay bound to it. Runs every frame the mode
    /// is on, so resizing or moving the window between displays is picked up as it happens rather
    /// than only when the mode is toggled.
    /// </summary>
    private void UpdateDirectToScreenTexture()
    {
        // Sized before it is bound: a resize replaces the RT, and the overlay must show the new one.
        ApplyPreviewResolution();

        if (directToScreenImage == null || renderTexture == null) return;

        if (directToScreenImage.texture != renderTexture)
        {
            directToScreenImage.texture = renderTexture;
        }

        if (directToScreenFitter != null && renderTexture.height > 0)
        {
            float aspect = (float)renderTexture.width / renderTexture.height;
            if (!Mathf.Approximately(directToScreenFitter.aspectRatio, aspect))
            {
                directToScreenFitter.aspectRatio = aspect;
            }
        }
    }

    private void DespawnDirectToScreenOverlay()
    {
        if (directToScreenGO != null)
        {
            Destroy(directToScreenGO);
            directToScreenGO = null;
        }
        directToScreenImage = null;
        directToScreenFitter = null;
    }
}
