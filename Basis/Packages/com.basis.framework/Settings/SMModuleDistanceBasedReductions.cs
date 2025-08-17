
using System;
using UnityEngine;
public class SMModuleDistanceBasedReductions : MonoBehaviour
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
    /*
    /// <summary>
    /// microphone range
    /// hearing range
    /// maximum avatars
    /// </summary>
    /// <param name="Option"></param>
    /// <param name="Manager"></param>
    public void ReceiveOption(SettingsMenuInput Option, SettingsManager Manager)
    {
        if (NameReturn(0, Option))
        {
            if (SliderReadOption(Option, Manager, out var newMicrophoneRange))
            {
#if UNITY_SERVER
                MicrophoneRange = 0;
#else
                MicrophoneRange = newMicrophoneRange * newMicrophoneRange;
#endif
            }
        }
        else if (NameReturn(1, Option))
        {
            if (SliderReadOption(Option, Manager, out var newHearingRange))
            {
#if UNITY_SERVER
                HearingRange = 0;
#else
    HearingRange = newHearingRange * newHearingRange;
#endif
            }
        }
        else if (NameReturn(2, Option))
        {
            if (SliderReadOption(Option, Manager, out var LoadRange))
            {
#if UNITY_SERVER
                AvatarRange = 0;
#else
   AvatarRange = LoadRange * LoadRange;
#endif
            }
        }
    }
    */
}
