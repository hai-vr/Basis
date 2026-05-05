using Basis.BasisUI;
using Basis.Scripts.Drivers;
using UnityEngine;

public class SMModuleMotionVectorsURP : BasisSettingsBase
{
    private static string K_USE => BasisSettingsDefaults.UseMotionVectors.BindingKey;

    public override void Awake()
    {
        base.Awake();
        ApplyMotionVectors(BasisSettingsDefaults.UseMotionVectors.RawValue);
    }

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_USE)
        {
            ApplyMotionVectors(optionValue == "true");
        }
    }

    public override void ChangedSettings()
    {
    }

    public static void ApplyMotionVectors(bool enable)
    {
        BasisLocalCameraDriver driver = BasisLocalCameraDriver.Instance;
        if (driver == null || driver.Camera == null) return;

        DepthTextureMode mode = driver.Camera.depthTextureMode;
        if (enable)
        {
            mode |= DepthTextureMode.MotionVectors;
        }
        else
        {
            mode &= ~DepthTextureMode.MotionVectors;
        }

        if (driver.Camera.depthTextureMode != mode)
        {
            driver.Camera.depthTextureMode = mode;
        }
    }
}
