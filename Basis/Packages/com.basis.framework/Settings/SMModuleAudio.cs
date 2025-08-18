using System;
using UnityEngine;
using UnityEngine.Audio;
namespace BattlePhaze.SettingsManager.Intergrations
{
    public class SMModuleAudio : BasisSettingsBase
    {
        public AudioMixer Mixer;
        public AudioMixerGroup WorldDefaultMixer;
        public static SMModuleAudio Instance;
        public new void Awake()
        {
            Instance = this;
            base.Awake();
        }
        /// <summary>
        /// 0 to 1 rest or 0 to 100
        /// </summary>
        public static Action<float> MainVolume;
        public static Action<float> MenusVolume;
        public static Action<float> WorldVolume;
        public static Action<float> PlayerVolume;
        public static float ActiveMainVolume;
        public static float ActiveMenusVolume;
        public static float ActiveWorldVolume;
        public static float ActivePlayerVolume;
        public override void ValidSettingsChange(string matchedSettingName, string optionValue)
        {
            switch (matchedSettingName)
            {
                case "main volume":
                    if (SliderReadOption(optionValue, out float NewActiveMainVolume))
                    {
                        ActiveMainVolume = NewActiveMainVolume / 100;
                        MainVolume?.Invoke(ActiveMainVolume);
                        AudioListener.volume = ActiveMainVolume;
                    }

                    break;
                case "menu volume":
                    if (SliderReadOption(optionValue, out float NewActiveMenusVolume))
                    {
                        ActiveMenusVolume = NewActiveMenusVolume;
                        MenusVolume?.Invoke(ActiveMenusVolume);
                        ChangeVolume(NewActiveMenusVolume - 80, matchedSettingName);
                    }

                    break;

                case "world volume":
                    if (SliderReadOption(optionValue, out float NewActiveWorldVolume))
                    {
                        ActiveWorldVolume = NewActiveWorldVolume;
                        WorldVolume?.Invoke(ActiveWorldVolume);
                        ChangeVolume(NewActiveWorldVolume - 80, matchedSettingName);
                    }

                    break;
                case "player volume":
                    if (SliderReadOption(optionValue, out float NewActivePlayerVolume))
                    {
                        ActivePlayerVolume = NewActivePlayerVolume;
                        PlayerVolume?.Invoke(ActivePlayerVolume);
                        ChangeVolume(NewActivePlayerVolume - 80, matchedSettingName);
                    }

                    break;
            }
        }
        public void ChangeVolume(float Value, string Name)
        {
            BasisDebug.Log(Name + "set to" + Value);
            Mixer.SetFloat(Name, Value);
        }
    }
}
