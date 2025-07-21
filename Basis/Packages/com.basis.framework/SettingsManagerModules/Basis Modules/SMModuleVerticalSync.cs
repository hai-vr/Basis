using Basis.Scripts.Device_Management;
using UnityEngine;

namespace BattlePhaze.SettingsManager.Integrations
{
    public class SMModuleVerticalSync : SettingsManagerOption
    {
        public string CurrentMode;
        public override void ReceiveOption(SettingsMenuInput option, SettingsManager manager)
        {
            if (NameReturn(0, option))
            {
                ChangeVerticalSync(option.SelectedValue);
            }
        }
        public void Start()
        {
            BasisDeviceManagement.OnBootModeChanged += OnBootModeChanged;
            Application.targetFrameRate = -1;
            QualitySettings.maxQueuedFrames = -1;
#if UNITY_SERVER
            Application.targetFrameRate = 25;
#endif
        }
        public void OnDestroy()
        {
            BasisDeviceManagement.OnBootModeChanged -= OnBootModeChanged;
        }
        public void OnBootModeChanged(string booted)
        {
            ChangeVerticalSync(CurrentMode);
        }
        public void ChangeVerticalSync(string quality)
        {
            CurrentMode = quality;
#if UNITY_SERVER
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 25;
#else
            if (BasisDeviceManagement.CurrentMode == BasisDeviceManagement.Desktop)
            {
                switch (quality)
                {
                    case "on":
                        QualitySettings.vSyncCount = 1;
                        break;
                    case "half":
                        QualitySettings.vSyncCount = 2;
                        break;
                    case "off":
                        QualitySettings.vSyncCount = 0;
                        break;
                }
            }
            else
            {
                QualitySettings.vSyncCount = 0;
            }
        
#endif
        }
    }
}
