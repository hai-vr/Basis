using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using System.Globalization;
public class SMModuleFOVSettings : BasisSettingsBase
{
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        float.TryParse(optionValue, NumberStyles.Any, CultureInfo.InvariantCulture, out SelectedFOV);
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            BasisLocalCameraDriver.Instance.Camera.fieldOfView = SelectedFOV;
        }
    }

    public float SelectedFOV = 60;
}
