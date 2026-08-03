using UnityEngine;
using UnityEngine.Rendering.Universal;
using Basis.BasisUI;

public class SMModuleShadowQualityURP : BasisSettingsBase
{
    // --- Canonical setting key (from defaults) ---
    private static string K_SHADOW_QUALITY => BasisSettingsDefaults.ShadowQuality.BindingKey; // "shadow quality"

    // Per-tier values, cheapest first, indexed by BasisQualityTier.
    private static readonly int[] MainShadowmapByTier = { 256, 512, 2048, 4096, 8192 };
    private static readonly int[] AdditionalShadowmapByTier = { 256, 512, 2048, 4096, 8192 };
    private static readonly int[] MaxAdditionalLightsByTier = { 0, 2, 8, 12, 16 };
    // Low previously sat at 150 — the same distance as Ultra — so dropping to Low only ever
    // shrank the shadowmap and left the caster set and cascade count untouched.
    private static readonly float[] ShadowDistanceByTier = { 5f, 50f, 150f, 150f, 150f };
    // Each cascade is a separate render of every shadow caster. Four of them at Very Low, over
    // a five metre distance, was paying for three passes nothing could see.
    private static readonly int[] CascadeCountByTier = { 1, 2, 4, 4, 4 };

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        // Only react to the shadow-quality setting
        if (matchedSettingName != K_SHADOW_QUALITY)
            return;

        Apply(optionValue);
    }

    /// <summary>
    /// Applies the shadow tier, clamped so it can never be more expensive than the graphics
    /// quality level allows. Also called by <c>SMModuleQualityAndQualitySetURP</c> when the
    /// quality level changes, so the clamp refreshes without this module owning two settings.
    /// </summary>
    public static void Apply(string optionValue)
    {
        UniversalRenderPipelineAsset Asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
        if (Asset == null)
        {
            BasisDebug.LogError("Missing Asset Pipeline!");
            return;
        }

        int tier = Mathf.Min(BasisQualityTier.FromOption(optionValue), BasisQualityTier.Current);

        Asset.shadowCascadeCount = CascadeCountByTier[tier];
        Asset.cascade2Split = 0.12f;                          // 12% for 2-cascade setting
        Asset.cascade3Split = new Vector2(0.12f, 0.5f);       // 12% and 50% for 3-cascade setting
        Asset.cascade4Split = new Vector3(0.12f, 0.3f, 0.6f); // 12%, 30%, and 60% for 4-cascade setting

        Asset.mainLightShadowmapResolution = MainShadowmapByTier[tier];
        Asset.additionalLightsShadowmapResolution = AdditionalShadowmapByTier[tier];
        Asset.maxAdditionalLightsCount = MaxAdditionalLightsByTier[tier];
        Asset.shadowDistance = ShadowDistanceByTier[tier];

        BasisDebug.Log($"Shadow tier {tier}: {Asset.mainLightShadowmapResolution}px, " +
            $"{Asset.shadowCascadeCount} cascade(s), {Asset.shadowDistance}m", BasisDebug.LogTag.System);
    }

    public override void ChangedSettings()
    {
    }
}
