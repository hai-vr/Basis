
using System;
using UnityEngine;
public class SMModuleDistanceBasedReductions : BasisSettingsBase
{
    private static float _microphoneRange;
    private static float _hearingRange;
    private static float _AvatarRange;

    public static event Action<float> OnMicrophoneRangeChanged;
    public static event Action<float> OnHearingRangeChanged;
    public static event Action<float> OnAvatarRangeChanged;
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

    /// <summary>
    /// microphone range
    /// hearing range
    /// maximum avatars
    /// </summary>
    /// <param name="Option"></param>
    /// <param name="Manager"></param>
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == "MicrophoneRange".ToLower())
        {
            if (SliderReadOption(optionValue, out float NewMicrophoneRange))
            {
#if UNITY_SERVER
           MicrophoneRange = 0;
#else
                MicrophoneRange = NewMicrophoneRange * NewMicrophoneRange;
#endif
            }
        }
        else if (matchedSettingName == "HearingRange".ToLower())
        {
            if (SliderReadOption(optionValue, out float newHearingRange))
            {
#if UNITY_SERVER
           HearingRange = 0;
#else
                HearingRange = newHearingRange * newHearingRange;
#endif
            }
        }
        else if (matchedSettingName == "AvatarRange".ToLower())
        {
            if (SliderReadOption(optionValue, out float LoadRange))
            {
#if UNITY_SERVER
           AvatarRange = 0;
#else
                AvatarRange = LoadRange * LoadRange;
#endif
            }
        }
    }
}
