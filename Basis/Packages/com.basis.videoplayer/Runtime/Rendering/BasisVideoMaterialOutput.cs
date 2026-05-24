using UnityEngine;

// Renderer/material sink for BasisVideoPlayer. Subscribes to the player's
// OnOutputTextureChanged and binds the current OutputTexture to a target
// Renderer's material property.
//
// Two binding strategies:
//   * UseSharedMaterial = false (default): assigns to TargetRenderer.material,
//     which clones the shared material the first time it's accessed. Safe for
//     per-instance video; avoids stomping other renderers sharing the asset.
//   * UseSharedMaterial = true: assigns to TargetRenderer.sharedMaterial,
//     mutating the project asset. Use only when you intend every renderer
//     sharing this material to receive the same video texture.
//
// On disable, the original texture is restored so editor scenes don't end up
// with the runtime video texture baked into the material.
[DisallowMultipleComponent]
public sealed class BasisVideoMaterialOutput : MonoBehaviour
{
    [Tooltip("Player to subscribe to. If unassigned, GetComponentInParent<BasisVideoPlayer>() is used.")]
    public BasisVideoPlayer Player;

    [Tooltip("Renderer whose material receives the OutputTexture.")]
    public Renderer TargetRenderer;

    [Tooltip("Index into TargetRenderer.materials for multi-material renderers. 0 by default.")]
    [Min(0)] public int MaterialIndex = 0;

    [Tooltip("Shader texture property name to bind the frame texture to. URP uses _BaseMap; legacy BiRP uses _MainTex.")]
    public string TexturePropertyName = "_BaseMap";

    [Tooltip("If true, the renderer's sharedMaterial is mutated — every renderer using that material will see the video. Otherwise a per-instance copy is created via Renderer.material.")]
    public bool UseSharedMaterial = false;

    [Tooltip("Optional fallback bound when the player has no output texture (before first frame, after Stop). Leave empty to leave the material's prior texture in place.")]
    public Texture PlaceholderTexture;

    [Tooltip("If true, the placeholder is rebound whenever the player raises OnEnded.")]
    public bool RestorePlaceholderOnEnded = true;

    [Tooltip("Flip the video vertically. Native GPU textures (D3D11/D3D12) are top-left origin and come in upside-down for Unity sampling, so this defaults ON. Toggle if your content/platform is already the right way up.")]
    public bool FlipVertically = true;

    private static readonly int FallbackPropertyId = Shader.PropertyToID("_MainTex");

    private Texture originalTexture;
    private Vector2 originalScale = Vector2.one;
    private Vector2 originalOffset = Vector2.zero;
    private bool capturedOriginal;
    private int propertyId;

    private void Reset()
    {
        if (TargetRenderer == null) TargetRenderer = GetComponent<Renderer>();
        if (Player == null) Player = GetComponentInParent<BasisVideoPlayer>();
    }

    private void OnEnable()
    {
        if (Player == null) Player = GetComponentInParent<BasisVideoPlayer>();
        if (Player == null)
        {
            BasisDebug.LogWarning("BasisVideoMaterialOutput: no BasisVideoPlayer found in parents and Player field is empty.", BasisDebug.LogTag.Video);
            return;
        }
        if (TargetRenderer == null)
        {
            BasisDebug.LogWarning("BasisVideoMaterialOutput: TargetRenderer is null; cannot bind output texture.", BasisDebug.LogTag.Video);
            return;
        }

        propertyId = !string.IsNullOrEmpty(TexturePropertyName)
            ? Shader.PropertyToID(TexturePropertyName)
            : FallbackPropertyId;

        CaptureOriginal();

        Player.OnOutputTextureChanged += HandleTextureChanged;
        Player.OnEnded += HandleEnded;

        HandleTextureChanged(Player.OutputTexture);
    }

    private void OnDisable()
    {
        if (Player != null)
        {
            Player.OnOutputTextureChanged -= HandleTextureChanged;
            Player.OnEnded -= HandleEnded;
        }
        RestoreOriginal();
    }

    private void HandleTextureChanged(Texture texture)
    {
        if (TargetRenderer == null) return;
        if (texture == null) texture = PlaceholderTexture;
        SetTexture(texture);
    }

    private void HandleEnded()
    {
        if (!RestorePlaceholderOnEnded || TargetRenderer == null) return;
        SetTexture(PlaceholderTexture);
    }

    private void CaptureOriginal()
    {
        if (capturedOriginal) return;
        var material = GetMaterial();
        if (material == null) return;
        if (material.HasProperty(propertyId))
        {
            originalTexture = material.GetTexture(propertyId);
            originalScale = material.GetTextureScale(propertyId);
            originalOffset = material.GetTextureOffset(propertyId);
            capturedOriginal = true;
        }
    }

    private void RestoreOriginal()
    {
        if (!capturedOriginal || TargetRenderer == null) return;
        var material = GetMaterial();
        if (material == null) return;
        if (material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, originalTexture);
            material.SetTextureScale(propertyId, originalScale);
            material.SetTextureOffset(propertyId, originalOffset);
        }
        capturedOriginal = false;
        originalTexture = null;
    }

    private void SetTexture(Texture texture)
    {
        var material = GetMaterial();
        if (material == null) return;
        if (material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, texture);
            // Vertical flip via the texture transform (UVs sample top-to-bottom).
            // Only flip the live video texture, not the placeholder/original.
            bool flip = FlipVertically && texture != null && texture != PlaceholderTexture;
            material.SetTextureScale(propertyId, flip ? new Vector2(originalScale.x, -originalScale.y) : originalScale);
            material.SetTextureOffset(propertyId, flip ? new Vector2(originalOffset.x, originalOffset.y + originalScale.y) : originalOffset);
        }
    }

    // Re-apply when the flip toggle changes in the inspector during play.
    private void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled && Player != null)
            SetTexture(Player.OutputTexture);
    }

    private Material GetMaterial()
    {
        if (TargetRenderer == null) return null;
        if (UseSharedMaterial)
        {
            var shared = TargetRenderer.sharedMaterials;
            if (MaterialIndex < 0 || MaterialIndex >= shared.Length) return null;
            return shared[MaterialIndex];
        }
        // Access .materials once to take ownership of a cloned array, then
        // index into it; accessing .materials repeatedly leaks instances.
        var instances = TargetRenderer.materials;
        if (MaterialIndex < 0 || MaterialIndex >= instances.Length) return null;
        return instances[MaterialIndex];
    }
}
