using UnityEngine;
namespace BattlePhaze.SettingsManager.Intergrations
{
    public class SMModuleHDRSupport : BasisSettingsBase
    {
        public Camera Camera;
        public override void ValidSettingsChange(string matchedSettingName, string optionValue)
        {
            if (Camera == null)
            {
                Camera = Camera.main;
                if (Camera == null)
                {
                    Camera = FindFirstObjectByType<Camera>();
                    if (Camera == null)
                    {
                        return;
                    }
                }
            }
            switch (optionValue)
            {
                case "true":
                    Camera.allowHDR = true;
                    break;
                case "false":
                    Camera.allowHDR = false;
                    break;
            }
        }
    }
}
