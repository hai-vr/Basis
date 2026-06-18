using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.BasisSdk.Highlight
{
    /// <summary>
    /// Shared settings asset for <see cref="BasisHighlightFeature"/>.
    /// Held as a ScriptableObject so the same configuration can be referenced
    /// by multiple URP renderers (e.g. Desktop + Quest) without copy-pasting
    /// values between renderer assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Basis/BasisHighlight Settings", fileName = "BasisHighlightSettings")]
    public class BasisHighlightSettings : ScriptableObject
    {
        public enum DebugMode
        {
            Off = 0, // Normal composite (ring + glow + interior)
            ShowRaw = 1, // Output rawMask channel as grayscale
            ShowDilated = 2, // Output dilated channel as grayscale
            ShowBlurred = 3, // Output glow field as grayscale
            ShowRingOnly = 4, // Output ring contribution only (no glow)
            ShowDilatedMinusRaw = 5, // Output raw `dilated - raw` band, no smoothstep AA
        }

        public enum MaskMsaa
        {
            Off = 1,
            X2 = 2,
            X4 = 4,
            X8 = 8,
        }

        [Tooltip("Material used to draw renderers that have no BasisHighlightOverride. " +
                 "Should use a vanilla object-to-clip-space silhouette shader.")]
        public Material defaultMaskMaterial;

        [Tooltip("Material backing the separable blur + dilation shader.")]
        public Material blurMaterial;

        [Tooltip("Material backing the outline + glow composite shader.")]
        public Material compositeMaterial;

        public Color outlineColor = new Color(0f, 0.9f, 0.85f, 1f);

        [Tooltip("Outline thickness, measured in MASK pixels (not screen pixels). The mask is " +
                 "dilated by this many pixels per axis to form a constant-width ring that follows " +
                 "the silhouette exactly, including concave corners. Capped at 8 (shader-side " +
                 "unroll limit). Apparent screen-pixel width = outlineWidth × maskDownsample.")]
        [Range(1, 8)] public int outlineWidth = 2;

        [Tooltip("Mask render-target downsample factor relative to the camera. 1 = full resolution " +
                 "(sharpest edges, ~4x mask/blur/dilation cost vs half-res). 2 = half resolution " +
                 "(default; good balance). 4 = quarter resolution (cheapest but visibly pixelated " +
                 "edges). Note: dilate and glow radii are in mask-texture pixels, so increasing " +
                 "the downsample makes the same `outlineWidth` value appear thicker on screen.")]
        [Range(1, 4)] public int maskDownsample = 2;

        [Tooltip("MSAA sample count for the silhouette mask render target. Anti-aliases " +
                 "the outline's INNER edge (the side touching the object) at the source, " +
                 "fixing the jagged contour. Off = previous binary behaviour. Cheap on " +
                 "Quest's tiled GPU (resolve happens in tile memory).")]
        public MaskMsaa maskMsaa = MaskMsaa.X4;

        [Tooltip("Ring edge softness. The dilated mask is pre-blurred to give it a 0..1 gradient " +
                 "across a few pixels; this slider controls how much of that gradient is converted " +
                 "to a feathered alpha band. Small = crisp/hard ring, large = soft band.")]
        [Range(0.01f, 0.5f)] public float ringSoftness = 0.15f;

        [Tooltip("Radius (mask pixels) of the gaussian applied to the dilated mask before AA. " +
                 "This is what gives `ringSoftness` something to soften — without it, the binary " +
                 "mask collapses the smoothstep to a hard step. Larger = wider feathered band.")]
        [Range(0f, 4f)] public float ringSoftenRadius = 1.5f;

        [Tooltip("Radius (mask pixels) of a gaussian applied to the INNER edge (the raw mask) " +
                 "before AA. Independent from ringSoftenRadius, so the inner edge (touching the " +
                 "object) can be feathered separately from the outer edge — set this lower than " +
                 "ringSoftenRadius for an asymmetric soft-inner / crisp-outer look. 0 = crisp " +
                 "inner edge (MSAA AA only). The dilation/ring width is unaffected (it still " +
                 "reads the sharp mask).")]
        [Range(0f, 4f)] public float innerSoftenRadius = 1f;

        [Tooltip("Soft-glow halo intensity outside the silhouette. 0 disables glow.")]
        [Range(0f, 2f)] public float glowIntensity = 0.6f;

        [Tooltip("Radius (pixels) of the gaussian that produces the soft glow field. " +
                 "Wider = the halo extends further out from the dilated edge.")]
        [Range(0.1f, 8f)] public float glowRadius = 2f;

        [Tooltip("Number of gaussian iterations applied to the dilated mask to produce the glow. " +
                 "0 disables glow entirely.")]
        [Range(0, 3)] public int glowIterations = 2;

        [Tooltip("Faint tint applied over the silhouette interior. 0 = pure outline only.")]
        [Range(0f, 1f)] public float interiorFill = 0f;

        [Tooltip("When in the URP frame the highlight is drawn.")]
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;

        [Tooltip("Debug output mode. Off = normal composite. Use the others to verify which " +
                 "stage produces non-zero output if the highlight looks wrong.")]
        public DebugMode debugMode = DebugMode.Off;
    }
}
