using Basis.Scripts.Device_Management;
using UnityEngine;
public class BasisVerticalSyncModule : BasisSettingsBase
{
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        Application.targetFrameRate = -1;
        QualitySettings.maxQueuedFrames = -1;
#if UNITY_SERVER
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 25;
#endif
        if (BasisDeviceManagement.StaticCurrentMode == BasisConstants.Desktop)
        {
            switch (optionValue)
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
