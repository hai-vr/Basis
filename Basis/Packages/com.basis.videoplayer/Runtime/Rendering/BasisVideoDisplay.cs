using UnityEngine;
using UnityEngine.UI;

// uGUI sink for BasisVideoPlayer. Binds the player's OutputTexture to a target
// RawImage and optionally tracks the source aspect ratio via a RectTransform
// AspectRatioFitter (or manual mode where the RawImage size is left alone).
//
// Wire up:
//   * Add to any GameObject that has (or is parented to) a BasisVideoPlayer.
//   * Assign TargetRawImage.
//   * Optionally assign AspectFitter to follow OnVideoSizeChanged.
[DisallowMultipleComponent]
public sealed class BasisVideoDisplay : MonoBehaviour
{
    [Tooltip("Player to subscribe to. If unassigned, GetComponentInParent<BasisVideoPlayer>() is used.")]
    public BasisVideoPlayer Player;

    [Tooltip("RawImage that displays the player's OutputTexture.")]
    public RawImage TargetRawImage;

    [Tooltip("Optional AspectRatioFitter updated whenever the player reports a new VideoSize.")]
    public AspectRatioFitter AspectFitter;

    [Tooltip("If true, the RawImage is cleared back to PlaceholderTexture when the player has no output (before first frame, after Stop).")]
    public bool RestorePlaceholderOnDetach = true;

    [Tooltip("Texture shown while the player is not producing frames. Leave empty to clear the RawImage texture to null.")]
    public Texture PlaceholderTexture;

    private Texture lastBoundTexture;

    private void Reset()
    {
        if (TargetRawImage == null) TargetRawImage = GetComponent<RawImage>();
        if (Player == null) Player = GetComponentInParent<BasisVideoPlayer>();
    }

    private void OnEnable()
    {
        if (Player == null) Player = GetComponentInParent<BasisVideoPlayer>();
        if (Player == null)
        {
            BasisDebug.LogWarning("BasisVideoDisplay: no BasisVideoPlayer found in parents and Player field is empty.", BasisDebug.LogTag.Video);
            return;
        }

        Player.OnOutputTextureChanged += HandleTextureChanged;
        Player.OnEnded += HandleEnded;

        // Apply current state immediately so attaching mid-playback works.
        HandleTextureChanged(Player.OutputTexture);
        if (Player.VideoSize != Vector2Int.zero) ApplyAspect(Player.VideoSize.x, Player.VideoSize.y);
    }

    private void OnDisable()
    {
        if (Player != null)
        {
            Player.OnOutputTextureChanged -= HandleTextureChanged;
            Player.OnEnded -= HandleEnded;
        }
        if (RestorePlaceholderOnDetach && TargetRawImage != null)
        {
            TargetRawImage.texture = PlaceholderTexture;
        }
    }

    private void Update()
    {
        if (Player == null || AspectFitter == null) return;
        var size = Player.VideoSize;
        if (size.x > 0 && size.y > 0) ApplyAspect(size.x, size.y);
    }

    private void HandleTextureChanged(Texture texture)
    {
        if (TargetRawImage == null) return;
        if (texture == null && RestorePlaceholderOnDetach) texture = PlaceholderTexture;
        if (lastBoundTexture == texture) return;
        TargetRawImage.texture = texture;
        lastBoundTexture = texture;
    }

    private void HandleEnded()
    {
        if (!RestorePlaceholderOnDetach || TargetRawImage == null) return;
        TargetRawImage.texture = PlaceholderTexture;
        lastBoundTexture = PlaceholderTexture;
    }

    private void ApplyAspect(int width, int height)
    {
        if (AspectFitter == null || height <= 0) return;
        AspectFitter.aspectRatio = (float)width / height;
    }
}
