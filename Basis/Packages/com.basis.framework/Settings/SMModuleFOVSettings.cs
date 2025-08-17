using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;

public class SMModuleFOVSettings : MonoBehaviour
{
    public void Awake()
    {
        BasisLocalCameraDriver.InstanceExists += InstanceExists;
        if(BasisLocalCameraDriver.Instance != null)
        {
            InstanceExists();
        }
    }
    public void OnDestroy()
    {
        BasisLocalCameraDriver.InstanceExists -= InstanceExists;
    }
    private void InstanceExists()
    {
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            BasisLocalCameraDriver.Instance.Camera.fieldOfView = SelectedFOV;
        }
    }
    public float SelectedFOV = 60;
}
