using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

public class BasisTouchInputDevice : BasisInput
{
    public BasisAvatarEyeInput Input;
    public Finger Finger;
    public override void DoPollData()
    {
        UnscaledDeviceCoord = Input.UnscaledDeviceCoord;
        ScaledDeviceCoord.position = UnscaledDeviceCoord.position;
        ScaledDeviceCoord.rotation = UnscaledDeviceCoord.rotation;
        if (BasisLocalInputActions.Instance != null)
        {
            BasisLocalInputActions.Instance.InputState.CopyTo(CurrentInputState);
        }
      //  ControlOnlyAsDevice();
        ComputeRaycastDirection(ScaledDeviceCoord.position, ScaledDeviceCoord.rotation, Quaternion.identity);
        if (HasRaycaster)
        {
            if (Finger.isActive)
            {
                BasisPointRaycaster.ScreenPoint = Finger.currentTouch.screenPosition;
                UpdatePlayerControl(true);
            }
            else
            {
                UpdatePlayerControl(false);
            }
        }
    }
    public override void PlayHaptic(float duration = 0.25F, float amplitude = 0.5F, float frequency = 0.5F)
    {

    }

    public override void PlaySoundEffect(string SoundEffectName, float Volume)
    {

    }

    public override void ShowTrackedVisual()
    {

    }
}
