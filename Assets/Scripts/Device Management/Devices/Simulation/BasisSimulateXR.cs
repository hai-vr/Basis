using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class BasisSimulateXR
{
    public List<BasisInputXR> Inputs = new List<BasisInputXR>();
    /// <summary>
    /// generated at runtime
    /// </summary>
    public List<BasisInputXR> TrackedOpenXRInputDevices = new List<BasisInputXR>();
    public void StartSimulation()
    {

    }
    public void StopXR()
    {

    }
#if UNITY_EDITOR
    [MenuItem("Basis/Create Puck Tracker")]
    public static void CreatePuckTracker()
    {
        BasisDeviceManagement.Instance.BasisSimulateXR.CreatePhysicalTrackedDevice("{htc}vr_tracker_vive_3_0", "{htc}vr_tracker_vive_3_0");
    }
#endif
    public void CreatePhysicalTrackedDevice(string UniqueID, string UnUniqueID)
    {
        GameObject gameObject = new GameObject(UniqueID);
        gameObject.transform.parent = BasisLocalPlayer.Instance.LocalBoneDriver.transform;
        BasisInputXR BasisInput = gameObject.AddComponent<BasisInputXR>();
        Initalize(BasisInput, UniqueID, UnUniqueID);
        BasisDeviceManagement.Instance.AllInputDevices.Add(BasisInput);
    }
    public void Initalize(BasisInput BasisInput, string UniqueID, string UnUniqueID)
    {
        BasisInput.ActivateTracking(UniqueID, UnUniqueID);
    }
    public void DestroyXRInput(string ID)
    {
        foreach (var device in Inputs)
        {
            if (device.UniqueID == ID)
            {
                Inputs.Remove(device);
                Object.Destroy(device.gameObject);
                break;
            }
        }
        List<BasisInput> Duplicate = new List<BasisInput>();
        Duplicate.AddRange(BasisDeviceManagement.Instance.AllInputDevices);
        foreach (var device in Duplicate)
        {
            if (device.UniqueID == ID)
            {
                BasisDeviceManagement.Instance.AllInputDevices.Remove(device);
            }
        }
    }
}
public class BasisInputXR : BasisInput
{
    public override void PollData()
    {
    }
}