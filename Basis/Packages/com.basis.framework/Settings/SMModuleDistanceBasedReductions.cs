using System;
using UnityEngine;

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
            //  BasisDebug.Log($"Avatar Range {_AvatarRange}");
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
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        switch (matchedSettingName.ToLower())
        {
            case "microphonerange":
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

            case "hearingrange":
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

            case "avatarrange":
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

            case "avatarmeshlod":
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

            case "globalmeshlod": // now robust
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
