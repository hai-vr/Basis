using UnityEngine;
namespace BattlePhaze.SettingsManager.Intergrations
{
    public class SMModuleHDRSupport : MonoBehaviour
    {
        public Camera Camera;
        public void Awake()
        {
            
        }
        public void ChangeSettings(string Option)
        {
           // if (NameReturn(0, Option))
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
                switch (Option)
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
}
