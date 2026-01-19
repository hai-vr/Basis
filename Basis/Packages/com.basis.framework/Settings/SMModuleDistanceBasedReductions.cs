using System;
using UnityEngine;
using Basis.BasisUI;

public class SMModuleDistanceBasedReductions : BasisSettingsBase
{
    private static float _microphoneRange = 25;
    private static float _hearingRange = 25;
    private static float _AvatarRange = 25;
    private static float _meshLod = 25;

    public static event Action<float> OnMicrophoneRangeChanged;
    public static event Action<float> OnHearingRangeChanged;
    public static event Action<float> OnAvatarRangeChanged;
    public static event Action<float> OnMeshLodChanged;

    /// <summary>
    /// will be value * value returned pre-squared
    /// </summary>
    public static float MicrophoneRange
    {
        get => _microphoneRange;
        set
        {
            _microphoneRange = value;
            OnMicrophoneRangeChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// will be value * value returned pre-squared
    /// </summary>
    public static float HearingRange
    {
        get => _hearingRange;
        set
        {
            _hearingRange = value;
            OnHearingRangeChanged?.Invoke(value);
        }
    }

    public static float AvatarRange
    {
        get => _AvatarRange;
        set
        {
            _AvatarRange = value;
            OnAvatarRangeChanged?.Invoke(value);
        }
    }

    public static float MeshLod
    {
        get => _meshLod;
        set
        {
            _meshLod = value;
            OnMeshLodChanged?.Invoke(value);
        }
    }

    // --- Canonical setting keys (from defaults) ---
    private static string K_MIC_RANGE => BasisSettingsDefaults.MicrophoneRange.BindingKey;       // "microphonerange"
    private static string K_HEARING_RANGE => BasisSettingsDefaults.HearingRange.BindingKey;     // "hearingrange"
    private static string K_AVATAR_RANGE => BasisSettingsDefaults.AvatarRange.BindingKey;       // "avatarrange"
    private static string K_AVATAR_MESH_LOD => BasisSettingsDefaults.AvatarMeshLOD.BindingKey;  // "avatarmeshlod"
    private static string K_GLOBAL_MESH_LOD => BasisSettingsDefaults.GlobalMeshLOD.BindingKey;  // "global meshlod" (note space!)

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        // Preserve your original behavior (case-insensitive matching)
        string key = matchedSettingName.ToLowerInvariant();

        switch (key)
        {
            case var s when s == K_MIC_RANGE.ToLowerInvariant():
                if (SliderReadOption(optionValue, out float newMicrophoneRange))
                {
#if UNITY_SERVER
                    MicrophoneRange = 0;
#else
                    MicrophoneRange = newMicrophoneRange * newMicrophoneRange;
#endif
                    BasisDebug.Log($"Mesh LOD {MicrophoneRange}");
                }
                break;

            case var s when s == K_HEARING_RANGE.ToLowerInvariant():
                if (SliderReadOption(optionValue, out float newHearingRange))
                {
#if UNITY_SERVER
                    HearingRange = 0;
#else
                    HearingRange = newHearingRange * newHearingRange;
                    BasisDebug.Log($"Mesh LOD {HearingRange}");
#endif
                }
                break;

            case var s when s == K_AVATAR_RANGE.ToLowerInvariant():
                if (SliderReadOption(optionValue, out float loadRange))
                {
#if UNITY_SERVER
                    AvatarRange = 0;
#else
                    AvatarRange = loadRange * loadRange;
                    BasisDebug.Log($"Mesh LOD {AvatarRange}");
#endif
                }
                break;

            case var s when s == K_AVATAR_MESH_LOD.ToLowerInvariant():
                if (SliderReadOption(optionValue, out float lod))
                {
#if UNITY_SERVER
                    MeshLod = 0;
#else
                    MeshLod = lod * lod;
                    BasisDebug.Log($"Mesh LOD {MeshLod}");
#endif
                }
                break;

            case var s when s == K_GLOBAL_MESH_LOD.ToLowerInvariant():
                if (SliderReadOption(optionValue, out float globalLOD))
                {
                    QualitySettings.meshLodThreshold = globalLOD;
                    BasisDebug.Log($"Mesh LOD {globalLOD}");
                }
                break;
        }
    }

    public override void ChangedSettings()
    {
    }
}
