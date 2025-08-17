using Basis.Scripts.Device_Management;
using UnityEngine;
namespace BattlePhaze.SettingsManager.Integrations
{
    public class SMModuleVerticalSync : BasisSettingsBase
    {
        public override void ValidSettingsChange(string matchedSettingName, string optionValue)
        {
            ChangeVerticalSync(optionValue);
        }
        public void ChangeVerticalSync(string quality)
        {
            Application.targetFrameRate = -1;
            QualitySettings.maxQueuedFrames = -1;
#if UNITY_SERVER
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 25;
#endif
            if (BasisDeviceManagement.StaticCurrentMode == BasisConstants.Desktop)
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
        }
    }
}
