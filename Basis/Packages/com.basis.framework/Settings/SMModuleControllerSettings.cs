public class SMModuleControllerSettings : BasisSettingsBase
{
    public static float JoyStickDeadZone = 0.01f;
    public static float SnapTurnAngle = 45;
    public static bool HasInvertedMouse = false;

    public static float baseXDeadzone = 0.08f;
    public static float extraXDeadzoneAtFullY = 0.35f;
    public static float yDeadzone = 0.10f;
    public static float wingExponent = 1.6f;
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        switch (matchedSettingName)
        {
            case "joystickdeadzone":
                if (SliderReadOption(optionValue, out JoyStickDeadZone))
                {
                    BasisDebug.Log($"JoyStick deadspace is set to {JoyStickDeadZone}");
                }
                break;
            case "snapturnangle":
                if (SliderReadOption(optionValue, out SnapTurnAngle))
                {
                    BasisDebug.Log($"Snap Turn Angle is set to {SnapTurnAngle}");
                }
                break;
            case "baseXDeadzone":
                if (SliderReadOption(optionValue, out baseXDeadzone))
                {
                    BasisDebug.Log($"baseXDeadzone deadspace is set to {baseXDeadzone}");
                }
                break;
            case "extraXDeadzoneAtFullY":
                if (SliderReadOption(optionValue, out extraXDeadzoneAtFullY))
                {
                    BasisDebug.Log($"extraXDeadzoneAtFullY deadspace is set to {extraXDeadzoneAtFullY}");
                }
                break;
            case "yDeadzone":
                if (SliderReadOption(optionValue, out yDeadzone))
                {
                    BasisDebug.Log($"yDeadzone deadspace is set to {yDeadzone}");
                }
                break;
            case "wingExponent":
                if (SliderReadOption(optionValue, out wingExponent))
                {
                    BasisDebug.Log($"wingExponent deadspace is set to {wingExponent}");
                }
                break;
            case "invertmouse":
                if(optionValue == "true")
                {
                    HasInvertedMouse = true;
                }
                else
                {
                    if (optionValue == "false")
                    {
                        HasInvertedMouse = false;
                    }
                }
                break;
        }
    }
    public override void ChangedSettings()
    {
    }
}
