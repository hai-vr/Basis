using UnityEngine;
using UnityEngine.Rendering.Universal;
using Basis.BasisUI;

public class SMModuleHDRURP : BasisSettingsBase
{
    // --- Canonical setting key (from defaults) ---
    private static string K_HDR_SUPPORT => BasisSettingsDefaults.HDRSupport.BindingKey; // "hdr support"

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        // Only react to the HDR setting
        if (matchedSettingName != K_HDR_SUPPORT)
            return;

        Apply(optionValue);
    }

    /// <summary>
    /// Applies the HDR mode, clamped by the graphics quality level. Also called by
    /// <c>SMModuleQualityAndQualitySetURP</c> when the quality level changes, so this module
    /// stays the only writer of the HDR fields while still respecting the tier.
    ///
    /// <para>The colour buffer is the largest render target in the frame and it is allocated
    /// per eye in stereo, so 64-bit precision is the most expensive bandwidth choice here.
    /// Very Low drops HDR entirely; Low caps at 32-bit however the dropdown is set.</para>
    /// </summary>
    public static void Apply(string optionValue)
    {
#if UNITY_SERVER
        BasisDebug.LogWarning("SMModuleHDRURP: Running on server build. HDR changes will not be applied.", BasisDebug.LogTag.Local);
#else
        UniversalRenderPipelineAsset asset =
            (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
        if (asset == null)
        {
            return;
        }

#if UNITY_ANDROID || UNITY_IOS
        // Mobile platforms: HDR forced off
        SetHDR(asset, false, HDRColorBufferPrecision._32Bits);
#else
        int tier = BasisQualityTier.Current;

        if (tier <= BasisQualityTier.VeryLow)
        {
            SetHDR(asset, false, HDRColorBufferPrecision._32Bits);
            return;
        }

        // Values reach ValidSettingsChange already lowercased, but the raw binding read used by
        // the quality-level refresh keeps its display casing.
        switch (optionValue == null ? string.Empty : optionValue.ToLowerInvariant())
        {
            case "64bit":
                SetHDR(asset, true, tier <= BasisQualityTier.Low
                    ? HDRColorBufferPrecision._32Bits
                    : HDRColorBufferPrecision._64Bits);
                break;

            case "32bit":
                SetHDR(asset, true, HDRColorBufferPrecision._32Bits);
                break;

            case "off":
                SetHDR(asset, false, HDRColorBufferPrecision._32Bits);
                break;
        }
#endif
#endif
    }

#if !UNITY_SERVER
    private static void SetHDR(UniversalRenderPipelineAsset asset, bool enabled, HDRColorBufferPrecision precision)
    {
        asset.hdrColorBufferPrecision = precision;
        asset.supportsHDR = enabled;

        if (Camera.main != null)
        {
            Camera.main.allowHDR = enabled;
        }
    }
#endif

    public override void ChangedSettings()
    {
    }
}
