using UnityEngine;

namespace Basis.Scripts.BasisSdk.Highlight
{
    public enum BasisHighlightOverrideType
    {
        None,
        Material,
        Exclude,
    }

    /// <summary>
    /// Per-renderer authoring for the BasisHighlight feature. Drop this on a
    /// GameObject with a Renderer to control how that renderer is drawn into the
    /// highlight mask when something highlights it via <see cref="BasisHighlightManager"/>:
    /// <list type="bullet">
    ///   <item><see cref="BasisHighlightOverrideType.None"/> – use the feature's
    ///   default/fallback mask (identical to having no component).</item>
    ///   <item><see cref="BasisHighlightOverrideType.Material"/> – draw with
    ///   <see cref="MaskMaterial"/>, a silhouette shader matching this renderer's
    ///   geometry (e.g. one that reproduces a vertex deform so the outline lines up).</item>
    ///   <item><see cref="BasisHighlightOverrideType.Exclude"/> – never highlight
    ///   this renderer, even while it is in the active set.</item>
    /// </list>
    /// The mode and material are inspector-serialized and also mutable at runtime
    /// via <see cref="OverrideType"/> / <see cref="MaskMaterial"/>; their setters
    /// re-register with the manager so the next frame's mask reflects the change.
    /// The renderer is cached in <see cref="OnValidate"/> so registration never
    /// reaches for GetComponent and stale references can't outlive the prefab.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    [DisallowMultipleComponent]
    public class BasisHighlightOverride : MonoBehaviour
    {
        [SerializeField] private BasisHighlightOverrideType overrideType = BasisHighlightOverrideType.Material;
        [SerializeField] private Material maskMaterial;
        [SerializeField, HideInInspector] private Renderer cachedRenderer;

        public BasisHighlightOverrideType OverrideType
        {
            get => overrideType;
            set
            {
                overrideType = value;
                Apply();
            }
        }

        public Material MaskMaterial
        {
            get => maskMaterial;
            set
            {
                maskMaterial = value;
                Apply();
            }
        }

        private void OnValidate()
        {
            // Cache the renderer at edit time so Awake never reaches for GetComponent
            // and stale references can't outlive the prefab.
            cachedRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            if (cachedRenderer == null)
            {
                if (!TryGetComponent(out cachedRenderer))
                {
                    BasisDebug.LogWarning($"{nameof(BasisHighlightOverride)}: No Renderer found on {gameObject.name}, destroying self.");
                    DestroyImmediate(this);
                }
            }

            BasisHighlightManager.RegisterOverride(cachedRenderer, overrideType, maskMaterial);
        }

        private void OnDestroy()
        {
            BasisHighlightManager.UnregisterOverride(cachedRenderer);
        }

        private void Apply()
        {
            if (!Application.isPlaying || cachedRenderer == null)
            {
                return;
            }

            BasisHighlightManager.RegisterOverride(cachedRenderer, overrideType, maskMaterial);
        }
    }
}
