using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class SMDMicrophone : BasisSettingsBase
{
    // -------- Current fields you already had --------
    public static string[] MicrophoneDevices;
    public static Dictionary<string, string> MicrophoneSelections = new Dictionary<string, string>();
    public static Dictionary<string, float> VolumeSettings = new Dictionary<string, float>();

    public delegate void MicrophoneChangedHandler(string newMicrophone);
    public static event MicrophoneChangedHandler OnMicrophoneChanged;

    private static string selectedMicrophone;
    public static string SelectedMicrophone
    {
        get => selectedMicrophone;
        set
        {
            selectedMicrophone = value;
            OnMicrophoneChanged?.Invoke(selectedMicrophone);
        }
    }

    public delegate void MicrophoneVolumeChangedHandler(float Volume);
    public static event MicrophoneVolumeChangedHandler OnMicrophoneVolumeChanged;

    private static float selectedVolumeMicrophone = 1f;
    public static float SelectedVolumeMicrophone
    {
        get => selectedVolumeMicrophone;
        set
        {
            selectedVolumeMicrophone = Mathf.Clamp01(value);
            OnMicrophoneVolumeChanged?.Invoke(selectedVolumeMicrophone);
        }
    }

    public enum BasisMicrophoneMode
    {
        OnActivation = 0,
        PushToTalk = 1, 
    }

    public delegate void MicrophoneTalkmode(BasisMicrophoneMode BasisMicrophoneMode);
    public static event MicrophoneTalkmode MicrophoneTalkmodeChanged;

    private static BasisMicrophoneMode selectedTalkmode;
    public static BasisMicrophoneMode SelectedTalkmode
    {
        get => selectedTalkmode;
        set
        {
            selectedTalkmode = value;
            MicrophoneTalkmodeChanged?.Invoke(selectedTalkmode);
        }
    }

    public delegate void MicrophoneUseDenoiserChangedHandler(bool useDenoiser);
    public static event MicrophoneUseDenoiserChangedHandler OnMicrophoneUseDenoiserChanged;

    private static bool selectedDenoiserMicrophone;
    public static bool SelectedDenoiserMicrophone
    {
        get => selectedDenoiserMicrophone;
        set
        {
            selectedDenoiserMicrophone = value;
            OnMicrophoneUseDenoiserChanged?.Invoke(selectedDenoiserMicrophone);
        }
    }

    // -------- NEW: Limiter settings --------
    public delegate void LimiterChangedHandler(float threshold, float knee);
    public static event LimiterChangedHandler OnLimiterChanged;

    private static float selectedLimitThreshold = 0.95f; // pre-clip
    public static float SelectedLimitThreshold
    {
        get => selectedLimitThreshold;
        set
        {
            selectedLimitThreshold = Mathf.Clamp(value, 0.0f, 1.0f);
            OnLimiterChanged?.Invoke(selectedLimitThreshold, selectedLimitKnee);
        }
    }

    private static float selectedLimitKnee = 0.05f; // soft-knee width
    public static float SelectedLimitKnee
    {
        get => selectedLimitKnee;
        set
        {
            selectedLimitKnee = Mathf.Clamp(value, 0.0f, 1.0f);
            OnLimiterChanged?.Invoke(selectedLimitThreshold, selectedLimitKnee);
        }
    }

    // -------- NEW: Denoise post-gain + wet/dry --------
    public delegate void DenoiseParamsChangedHandler(float makeupDb, float wet);
    public static event DenoiseParamsChangedHandler OnDenoiseParamsChanged;

    private static float selectedDenoiseMakeupDb = 3f;
    public static float SelectedDenoiseMakeupDb
    {
        get => selectedDenoiseMakeupDb;
        set
        {
            selectedDenoiseMakeupDb = value;
            OnDenoiseParamsChanged?.Invoke(selectedDenoiseMakeupDb, selectedDenoiseWet);
        }
    }

    private static float selectedDenoiseWet = 1f; // 0..1
    public static float SelectedDenoiseWet
    {
        get => selectedDenoiseWet;
        set
        {
            selectedDenoiseWet = Mathf.Clamp01(value);
            OnDenoiseParamsChanged?.Invoke(selectedDenoiseMakeupDb, selectedDenoiseWet);
        }
    }

    // -------- NEW: AGC (automatic gain control) --------
    public delegate void AgcEnabledChangedHandler(bool enabled);
    public static event AgcEnabledChangedHandler OnAgcEnabledChanged;

    public delegate void AgcParamsChangedHandler(float targetRms, float maxGainDb, float attack, float release);
    public static event AgcParamsChangedHandler OnAgcParamsChanged;

    private static bool selectedUseAGC = false;
    public static bool SelectedUseAGC
    {
        get => selectedUseAGC;
        set
        {
            selectedUseAGC = value;
            OnAgcEnabledChanged?.Invoke(selectedUseAGC);
            // also re-emit params so listeners can refresh their state in one shot
            OnAgcParamsChanged?.Invoke(selectedAgcTargetRms, selectAgcMaxGainDb, selectedAgcAttack, selectedAgcRelease);
        }
    }

    private static float selectedAgcTargetRms = 0.06f; // ≈ −24 dBFS
    public static float SelectedAgcTargetRms
    {
        get => selectedAgcTargetRms;
        set
        {
            selectedAgcTargetRms = Mathf.Max(1e-6f, value);
            OnAgcParamsChanged?.Invoke(selectedAgcTargetRms, selectAgcMaxGainDb, selectedAgcAttack, selectedAgcRelease);
        }
    }

    private static float selectAgcMaxGainDb = 10f;
    public static float SelectAgcMaxGainDb
    {
        get => selectAgcMaxGainDb;
        set
        {
            selectAgcMaxGainDb = value;
            OnAgcParamsChanged?.Invoke(selectedAgcTargetRms, selectAgcMaxGainDb, selectedAgcAttack, selectedAgcRelease);
        }
    }

    private static float selectedAgcAttack = 0.10f; // 0..1 (how fast to rise)
    public static float SelectedAgcAttack
    {
        get => selectedAgcAttack;
        set
        {
            selectedAgcAttack = Mathf.Clamp01(value);
            OnAgcParamsChanged?.Invoke(selectedAgcTargetRms, selectAgcMaxGainDb, selectedAgcAttack, selectedAgcRelease);
        }
    }

    private static float selectedAgcRelease = 0.01f; // 0..1 (how fast to fall)
    public static float SelectedAgcRelease
    {
        get => selectedAgcRelease;
        set
        {
            selectedAgcRelease = Mathf.Clamp01(value);
            OnAgcParamsChanged?.Invoke(selectedAgcTargetRms, selectAgcMaxGainDb, selectedAgcAttack, selectedAgcRelease);
        }
    }

    public static Dictionary<string, bool> DenoiserSettings = new Dictionary<string, bool>();
    public static Dictionary<string, float> LimitThresholdSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> LimitKneeSettings = new Dictionary<string, float>();
    public static Dictionary<string, bool> AgcEnabledSettings = new Dictionary<string, bool>();
    public static Dictionary<string, float> AgcTargetRmsSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> AgcMaxGainDbSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> AgcAttackSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> AgcReleaseSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> DenoiseMakeupDbSettings = new Dictionary<string, float>();
    public static Dictionary<string, float> DenoiseWetSettings = new Dictionary<string, float>();
    public static Dictionary<string, BasisMicrophoneMode> BasisMicrophoneModeSettings = new Dictionary<string, BasisMicrophoneMode>();
    // -------- Load / Save --------

    public static void LoadInMicrophoneData(string mode)
    {
        BasisDebug.Log($"Loading microphone and volume for mode: {mode}");
        MicrophoneDevices = Microphone.devices;

        if (string.IsNullOrEmpty(mode))
        {
            BasisDebug.LogError("Missing Device Mode!");
            return;
        }

        // Mic + volume (existing behavior)
        string savedMicrophone = PlayerPrefs.GetString($"{mode}_Microphone", "");
        float savedVolume = PlayerPrefs.GetFloat($"{mode}_Volume", 1.0f);
        if (string.IsNullOrEmpty(savedMicrophone) && MicrophoneDevices.Length > 0)
        {
            savedMicrophone = MicrophoneDevices[0];
        }
        MicrophoneSelections[mode] = savedMicrophone;
        VolumeSettings[mode] = savedVolume;

        // Denoiser enable
        bool savedDenoiser = PlayerPrefs.GetInt($"{mode}_Denoiser", 0) == 1;
        DenoiserSettings[mode] = savedDenoiser;

        int index = PlayerPrefs.GetInt($"{mode}_MicrophoneMode", 0);
        BasisMicrophoneModeSettings[mode] = (BasisMicrophoneMode)index;

        // Limiter
        float savedLimitThreshold = PlayerPrefs.GetFloat($"{mode}_LimitThreshold", 0.95f);
        float savedLimitKnee = PlayerPrefs.GetFloat($"{mode}_LimitKnee", 0.05f);
        LimitThresholdSettings[mode] = savedLimitThreshold;
        LimitKneeSettings[mode] = savedLimitKnee;

        // Denoise params
        float savedDenoiseMakeupDb = PlayerPrefs.GetFloat($"{mode}_DenoiseMakeupDb", 3f);
        float savedDenoiseWet = PlayerPrefs.GetFloat($"{mode}_DenoiseWet", 1f);
        DenoiseMakeupDbSettings[mode] = savedDenoiseMakeupDb;
        DenoiseWetSettings[mode] = savedDenoiseWet;

        // AGC
        bool savedUseAgc = PlayerPrefs.GetInt($"{mode}_UseAGC", 0) == 1;
        float savedAgcTargetRms = PlayerPrefs.GetFloat($"{mode}_AgcTargetRms", 0.06f);
        float savedAgcMaxGainDb = PlayerPrefs.GetFloat($"{mode}_AgcMaxGainDb", 18f);
        float savedAgcAttack = PlayerPrefs.GetFloat($"{mode}_AgcAttack", 0.10f);
        float savedAgcRelease = PlayerPrefs.GetFloat($"{mode}_AgcRelease", 0.01f);

        AgcEnabledSettings[mode] = savedUseAgc;
        AgcTargetRmsSettings[mode] = savedAgcTargetRms;
        AgcMaxGainDbSettings[mode] = savedAgcMaxGainDb;
        AgcAttackSettings[mode] = savedAgcAttack;
        AgcReleaseSettings[mode] = savedAgcRelease;

        // Emit into active Selected* (fires events so listeners refresh)
        SelectedMicrophone = savedMicrophone;
        SelectedVolumeMicrophone = savedVolume;

        SelectedDenoiserMicrophone = savedDenoiser;
        SelectedLimitThreshold = savedLimitThreshold;
        SelectedLimitKnee = savedLimitKnee;

        SelectedDenoiseMakeupDb = savedDenoiseMakeupDb;
        SelectedDenoiseWet = savedDenoiseWet;

        SelectedUseAGC = savedUseAgc;
        SelectedAgcTargetRms = savedAgcTargetRms;
        SelectAgcMaxGainDb = savedAgcMaxGainDb;
        SelectedAgcAttack = savedAgcAttack;
        SelectedAgcRelease = savedAgcRelease;

        SelectedTalkmode = (BasisMicrophoneMode)index;

    }
    public static void SaveMicrophoneModeSettings(string mode, BasisMicrophoneMode MicrophoneMode)
    {
        if (!string.IsNullOrEmpty(mode))
        {
            BasisMicrophoneModeSettings[mode] = MicrophoneMode;
            PlayerPrefs.SetInt($"{mode}_MicrophoneMode", (int)MicrophoneMode);
            PlayerPrefs.Save();

            selectedTalkmode = MicrophoneMode;
        }
        else
        {
            BasisDebug.LogError("Missing Device Mode!");
            return;
        }
    }
    public static void SaveMicrophoneData(string mode, string selectedMicrophone)
    {
        if (string.IsNullOrEmpty(mode))
        {
            BasisDebug.LogError("Missing Device Mode!");
            return;
        }
        BasisDebug.Log($"Saving selected microphone for mode: {mode}");
        MicrophoneSelections[mode] = selectedMicrophone;
        PlayerPrefs.SetString($"{mode}_Microphone", selectedMicrophone);
        PlayerPrefs.Save();
        SelectedMicrophone = selectedMicrophone;
    }

    public static void SaveVolumeSettings(string mode, float volume)
    {
        if (string.IsNullOrEmpty(mode))
        {
            BasisDebug.LogError("Missing Device Mode!");
            return;
        }

        BasisDebug.Log($"Saving volume settings for mode: {mode}");
        VolumeSettings[mode] = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat($"{mode}_Volume", volume);
        PlayerPrefs.Save();
        SelectedVolumeMicrophone = volume;
    }

    public static void SaveDenoiserSetting(string mode, bool useDenoiser)
    {
        if (string.IsNullOrEmpty(mode)) { BasisDebug.LogError("Missing Device Mode!"); return; }
        DenoiserSettings[mode] = useDenoiser;
        PlayerPrefs.SetInt($"{mode}_Denoiser", useDenoiser ? 1 : 0);
        PlayerPrefs.Save();
        SelectedDenoiserMicrophone = useDenoiser;
    }

    public static void SaveLimiterSettings(string mode, float threshold, float knee)
    {
        if (string.IsNullOrEmpty(mode)) { BasisDebug.LogError("Missing Device Mode!"); return; }
        threshold = Mathf.Clamp(threshold, 0f, 1f);
        knee = Mathf.Clamp(knee, 0f, 1f);

        LimitThresholdSettings[mode] = threshold;
        LimitKneeSettings[mode] = knee;

        PlayerPrefs.SetFloat($"{mode}_LimitThreshold", threshold);
        PlayerPrefs.SetFloat($"{mode}_LimitKnee", knee);
        PlayerPrefs.Save();

        SelectedLimitThreshold = threshold;
        SelectedLimitKnee = knee;
    }

    public static void SaveDenoiseParams(string mode, float makeupDb, float wet)
    {
        if (string.IsNullOrEmpty(mode)) { BasisDebug.LogError("Missing Device Mode!"); return; }
        wet = Mathf.Clamp01(wet);

        DenoiseMakeupDbSettings[mode] = makeupDb;
        DenoiseWetSettings[mode] = wet;

        PlayerPrefs.SetFloat($"{mode}_DenoiseMakeupDb", makeupDb);
        PlayerPrefs.SetFloat($"{mode}_DenoiseWet", wet);
        PlayerPrefs.Save();

        SelectedDenoiseMakeupDb = makeupDb;
        SelectedDenoiseWet = wet;
    }

    public static void SaveAgcEnabled(string mode, bool enabled)
    {
        if (string.IsNullOrEmpty(mode)) { BasisDebug.LogError("Missing Device Mode!"); return; }
        AgcEnabledSettings[mode] = enabled;
        PlayerPrefs.SetInt($"{mode}_UseAGC", enabled ? 1 : 0);
        PlayerPrefs.Save();
        SelectedUseAGC = enabled;
    }

    public static void SaveAgcParams(string mode, float targetRms, float maxGainDb, float attack, float release)
    {
        if (string.IsNullOrEmpty(mode)) { BasisDebug.LogError("Missing Device Mode!"); return; }

        targetRms = Mathf.Max(1e-6f, targetRms);
        attack = Mathf.Clamp01(attack);
        release = Mathf.Clamp01(release);

        AgcTargetRmsSettings[mode] = targetRms;
        AgcMaxGainDbSettings[mode] = maxGainDb;
        AgcAttackSettings[mode] = attack;
        AgcReleaseSettings[mode] = release;

        PlayerPrefs.SetFloat($"{mode}_agcTargetRms", targetRms);
        PlayerPrefs.SetFloat($"{mode}_agcMaxGainDb", maxGainDb);
        PlayerPrefs.SetFloat($"{mode}_agcAttack", attack);
        PlayerPrefs.SetFloat($"{mode}_agcRelease", release);
        PlayerPrefs.Save();

        SelectedAgcTargetRms = targetRms;
        SelectAgcMaxGainDb = maxGainDb;
        SelectedAgcAttack = attack;
        SelectedAgcRelease = release;
    }
    private static string K_LIMIT_THRESHOLD => BasisSettingsDefaults.LimitThreshold.BindingKey;
    private static string K_LIMIT_KNEE => BasisSettingsDefaults.LimitKnee.BindingKey;
    private static string K_DENOISE_MAKEUP => BasisSettingsDefaults.DenoiseMakeupDb.BindingKey;
    private static string K_DENOISE_WET => BasisSettingsDefaults.DenoiseWet.BindingKey;

    private static string K_AGC => BasisSettingsDefaults.UseAutomaticGain.BindingKey;
    private static string K_AGC_TARGET => BasisSettingsDefaults.AgcTargetRms.BindingKey;
    private static string K_AGC_MAXGAIN => BasisSettingsDefaults.AgcMaxGainDb.BindingKey;
    private static string K_AGC_ATTACK => BasisSettingsDefaults.AgcAttack.BindingKey;
    private static string K_AGC_RELEASE => BasisSettingsDefaults.AgcRelease.BindingKey;

    private static string K_DENOISER => BasisSettingsDefaults.MicrophoneDenoiser.BindingKey;
    private static string K_MIC_MODE => BasisSettingsDefaults.MicrophoneMode.BindingKey;
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        string mode = BasisDeviceManagement.StaticCurrentMode;
        if (string.IsNullOrEmpty(mode))
        {
            BasisDebug.LogError("Missing Device Mode!");
            return;
        }

        var st = NumberStyles.Float | NumberStyles.AllowThousands;
        var ci = CultureInfo.InvariantCulture;

        try
        {
            switch (matchedSettingName)
            {
                case var s when s == K_DENOISER:
                    if (bool.TryParse(optionValue, out bool den))
                        SaveDenoiserSetting(mode, den);
                    else
                        BasisDebug.LogError($"Unable to parse Denoiser Setting! {optionValue}");
                    break;

                case var s when s == K_LIMIT_THRESHOLD:
                    if (float.TryParse(optionValue, st, ci, out float th))
                        SaveLimiterSettings(mode, th, SelectedLimitKnee);
                    else
                        BasisDebug.LogError($"Bad LimitThreshold: {optionValue}");
                    break;

                case var s when s == K_LIMIT_KNEE:
                    if (float.TryParse(optionValue, st, ci, out float kn))
                        SaveLimiterSettings(mode, SelectedLimitThreshold, kn);
                    else
                        BasisDebug.LogError($"Bad LimitKnee: {optionValue}");
                    break;

                case var s when s == K_DENOISE_MAKEUP:
                    if (float.TryParse(optionValue, st, ci, out float mk))
                        SaveDenoiseParams(mode, mk, SelectedDenoiseWet);
                    else
                        BasisDebug.LogError($"Bad DenoiseMakeupDb: {optionValue}");
                    break;

                case var s when s == K_DENOISE_WET:
                    if (float.TryParse(optionValue, st, ci, out float wet))
                        SaveDenoiseParams(mode, SelectedDenoiseMakeupDb, wet);
                    else
                        BasisDebug.LogError($"Bad DenoiseWet: {optionValue}");
                    break;

                case var s when s == K_AGC:
                    if (bool.TryParse(optionValue, out bool agcOn))
                        SaveAgcEnabled(mode, agcOn);
                    else
                        BasisDebug.LogError($"Bad AGCEnabled: {optionValue}");
                    break;

                case var s when s == K_AGC_TARGET:
                    if (float.TryParse(optionValue, st, ci, out float tr))
                        SaveAgcParams(mode, tr, SelectAgcMaxGainDb, SelectedAgcAttack, SelectedAgcRelease);
                    else
                        BasisDebug.LogError($"Bad AGCTargetRms: {optionValue}");
                    break;

                case var s when s == K_AGC_MAXGAIN:
                    if (float.TryParse(optionValue, st, ci, out float mg))
                        SaveAgcParams(mode, SelectedAgcTargetRms, mg, SelectedAgcAttack, SelectedAgcRelease);
                    else
                        BasisDebug.LogError($"Bad AGCMaxGainDb: {optionValue}");
                    break;

                case var s when s == K_AGC_ATTACK:
                    if (float.TryParse(optionValue, st, ci, out float att))
                        SaveAgcParams(mode, SelectedAgcTargetRms, SelectAgcMaxGainDb, att, SelectedAgcRelease);
                    else
                        BasisDebug.LogError($"Bad AGCAttack: {optionValue}");
                    break;

                case var s when s == K_AGC_RELEASE:
                    if (float.TryParse(optionValue, st, ci, out float rel))
                        SaveAgcParams(mode, SelectedAgcTargetRms, SelectAgcMaxGainDb, SelectedAgcAttack, rel);
                    else
                        BasisDebug.LogError($"Bad AGCRelease: {optionValue}");
                    break;

                case var s when s == K_MIC_MODE:
                    if (Enum.TryParse<BasisMicrophoneMode>(optionValue.Replace(" ", ""), true, out var m))
                        SaveMicrophoneModeSettings(mode, m);
                    else
                        BasisDebug.LogError($"Bad Microphone Mode: {optionValue}");
                    break;
            }
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"ValidSettingsChange error for '{matchedSettingName}': {ex}");
        }
    }
    public override void ChangedSettings()
    {
    }
}
