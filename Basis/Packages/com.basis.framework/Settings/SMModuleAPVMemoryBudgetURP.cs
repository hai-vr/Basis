using System.Reflection;
using Basis.BasisUI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SMModuleAPVMemoryBudgetURP : BasisSettingsBase
{
    private static string K_BUDGET => BasisSettingsDefaults.APVMemoryBudget.BindingKey;
    private static FieldInfo s_field;

    public override void Awake()
    {
        base.Awake();
        ApplyBudget(BasisSettingsDefaults.APVMemoryBudget.RawValue);
    }

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_BUDGET)
        {
            ApplyBudget(optionValue);
        }
    }

    public override void ChangedSettings()
    {
    }

    public static ProbeVolumeTextureMemoryBudget Current
    {
        get
        {
            UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return asset != null ? asset.probeVolumeMemoryBudget : ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        }
    }

    public static void ApplyBudget(string value)
    {
        UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;

        ProbeVolumeTextureMemoryBudget desired = Parse(value);
        if (asset.probeVolumeMemoryBudget == desired) return;

        s_field ??= typeof(UniversalRenderPipelineAsset).GetField("m_ProbeVolumeMemoryBudget", BindingFlags.Instance | BindingFlags.NonPublic);
        if (s_field == null)
        {
            BasisDebug.LogWarning("APVMemoryBudget: could not locate m_ProbeVolumeMemoryBudget on UniversalRenderPipelineAsset.", BasisDebug.LogTag.Video);
            return;
        }

        s_field.SetValue(asset, desired);
    }

    private static ProbeVolumeTextureMemoryBudget Parse(string value)
    {
        switch (value)
        {
            case "low": return ProbeVolumeTextureMemoryBudget.MemoryBudgetLow;
            case "medium": return ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
            case "high": return ProbeVolumeTextureMemoryBudget.MemoryBudgetHigh;
            default: return ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        }
    }
}
