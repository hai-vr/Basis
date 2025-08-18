
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SMModuleHDRURP : BasisSettingsBase
{
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        UniversalRenderPipelineAsset Asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
#if UNITY_ANDROID
        Asset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
        Asset.supportsHDR = false;
#else
        switch (optionValue)
        {
            case "64bit":
                Asset.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits;
                Asset.supportsHDR = true;
                break;
            case "32bit":
                Asset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
                Asset.supportsHDR = true;
                break;
            case "off":
                Asset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
                Asset.supportsHDR = false;
                break;
        }
#endif
    }
}
