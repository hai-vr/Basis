﻿using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices.OpenVR.Structs;
using Basis.Scripts.Device_Management.Devices.Unity_Spatial_Tracking;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Valve.VR;

namespace Basis.Scripts.Device_Management.Devices.OpenVR
{
    [Serializable]
    public class BasisOpenVRManagement : BasisBaseTypeManagement
    {
        public GameObject SteamVR_BehaviourGameobject;
        public SteamVR_Behaviour SteamVR_Behaviour;
        public SteamVR_Render SteamVR_Render;
        public SteamVR SteamVR;
        public Dictionary<string, OpenVRDevice> TypicalDevices = new Dictionary<string, OpenVRDevice>();
        public bool IsInUse = false;
        public static string SteamVRBehaviour = "SteamVR_Behaviour";
        private IEnumerator CheckState()
        {
            while (SteamVR.initializedState == SteamVR.InitializedStates.None)
            {
                Debug.LogError("SteamVR initializedState failed");
                yield return null;
            }

            if (SteamVR.initializedState == SteamVR.InitializedStates.InitializeFailure)
            {
                Debug.LogError("SteamVR failed to initialize");
                yield break;
            }

            SteamVR_Events.DeviceConnected.Listen(OnDeviceConnected);
            SteamVR_Events.System(EVREventType.VREvent_TrackedDeviceRoleChanged).Listen(OnTrackedDeviceRoleChanged);
        }

        private async void OnDeviceConnected(int deviceIndex, bool deviceConnected)
        {
          await  OnDeviceConnected((uint)deviceIndex, deviceConnected);
        }

        private async void OnTrackedDeviceRoleChanged(VREvent_t vrEvent)
        {
         await   OnDeviceConnected(vrEvent.trackedDeviceIndex, true);
        }

        private async Task OnDeviceConnected(uint deviceIndex, bool deviceConnected)
        {
            if (deviceIndex != Valve.VR.OpenVR.k_unTrackedDeviceIndexInvalid)
            {
                Debug.Log($"Device index {deviceIndex} is connected: {deviceConnected}");
                var error = new ETrackedPropertyError();
                var id = new StringBuilder(64);
                Valve.VR.OpenVR.System.GetStringTrackedDeviceProperty(deviceIndex, ETrackedDeviceProperty.Prop_RenderModelName_String, id, 64, ref error);
                ETrackedDeviceClass deviceClass = Valve.VR.OpenVR.System.GetTrackedDeviceClass(deviceIndex);
                string uniqueID = $"{deviceIndex}|{id}";
                string notUnique = id.ToString();
                if (deviceConnected)
                {
                    await CreateTrackerDevice(deviceIndex, deviceClass, uniqueID, notUnique);
                }
                else
                {
                    DestroyPhysicalTrackedDevice(uniqueID);
                }
            }
        }

        private async Task CreateTrackerDevice(uint deviceIndex, ETrackedDeviceClass deviceClass, string uniqueID, string notUniqueID)
        {
            var openVRDevice = new OpenVRDevice
            {
                deviceClass = deviceClass,
                deviceIndex = deviceIndex,
                deviceName = uniqueID,
            };

            switch (deviceClass)
            {
                case ETrackedDeviceClass.HMD:
                    await CreateHMD(GenerateGameobject(uniqueID), openVRDevice, uniqueID, notUniqueID);
                    break;
                case ETrackedDeviceClass.Controller:
                    await CreateController(GenerateGameobject(uniqueID), openVRDevice, uniqueID, notUniqueID);
                    break;
                case ETrackedDeviceClass.TrackingReference:
                    Debug.Log("Was Tracked Reference Returning (lighthouse)");
                    break;
                default:
                    await CreateTracker(GenerateGameobject(uniqueID), openVRDevice, uniqueID, notUniqueID, false, BasisBoneTrackedRole.CenterEye);
                    break;
            }
        }
        public GameObject GenerateGameobject(string uniqueID)
        {
            var gameObject = new GameObject(uniqueID)
            {
                transform = { parent = BasisLocalPlayer.Instance.LocalBoneDriver.transform }
            };
            return gameObject;
        }
        private async Task CreateHMD(GameObject gameObject, OpenVRDevice device, string uniqueID, string notUniqueID)
        {
            if (!TypicalDevices.ContainsKey(uniqueID))
            {
                var spatial = gameObject.AddComponent<BasisOpenVRInputSpatial>();
                spatial.ClassName = nameof(BasisOpenVRInputSpatial);
                bool foundRole = TryAssignRole(device.deviceClass, device.deviceIndex, out BasisBoneTrackedRole role, out SteamVR_Input_Sources source);
                await spatial.Initialize(UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose.Center, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), foundRole, role, source);

                var eye = gameObject.AddComponent<BasisOpenVRInputEye>();
                eye.Initalize();

                BasisDeviceManagement.Instance.TryAdd(spatial);
                TypicalDevices.TryAdd(uniqueID, device);
            }
            else
            {
                await HandleExistingDevice(uniqueID, notUniqueID, nameof(BasisOpenVRInputSpatial), device);
            }
        }

        public async Task CreateController(GameObject gameObject, OpenVRDevice device, string uniqueID, string notUniqueID)
        {
            if (!TypicalDevices.ContainsKey(uniqueID))
            {
                var controller = gameObject.AddComponent<BasisOpenVRInputController>();
                controller.ClassName = nameof(BasisOpenVRInputController);
                bool foundRole = TryAssignRole(device.deviceClass, device.deviceIndex, out BasisBoneTrackedRole role, out SteamVR_Input_Sources source);
                await controller.Initialize(device, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), foundRole, role, source);
                BasisDeviceManagement.Instance.TryAdd(controller);
                TypicalDevices.TryAdd(uniqueID, device);
            }
            else
            {
                await HandleExistingDevice(uniqueID, notUniqueID, nameof(BasisOpenVRInputController), device);
            }
        }

        public async Task CreateTracker(GameObject gameObject, OpenVRDevice device, string uniqueID, string notUniqueID, bool autoAssignRole, BasisBoneTrackedRole role)
        {
            if (!TypicalDevices.ContainsKey(uniqueID))
            {
                var input = gameObject.AddComponent<BasisOpenVRInput>();
                input.ClassName = nameof(BasisOpenVRInput);
                await input.Initialize(device, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), autoAssignRole, role);
                BasisDeviceManagement.Instance.TryAdd(input);
                TypicalDevices.TryAdd(uniqueID, device);
            }
            else
            {
                await HandleExistingDevice(uniqueID, notUniqueID, nameof(BasisOpenVRInput), device);
            }
        }

        public bool TryAssignRole(ETrackedDeviceClass deviceClass, uint deviceIndex, out BasisBoneTrackedRole role, out SteamVR_Input_Sources source)
        {
            source = SteamVR_Input_Sources.Any;
            role = BasisBoneTrackedRole.CenterEye;

            if (deviceClass == ETrackedDeviceClass.HMD)
            {
                role = BasisBoneTrackedRole.CenterEye;
                source = SteamVR_Input_Sources.Head;
                return true;
            }

            if (deviceClass == ETrackedDeviceClass.Controller)
            {
                var controllerRole = SteamVR.instance.hmd.GetControllerRoleForTrackedDeviceIndex(deviceIndex);
                if (controllerRole == ETrackedControllerRole.LeftHand)
                {
                    role = BasisBoneTrackedRole.LeftHand;
                    source = SteamVR_Input_Sources.LeftHand;
                    return true;
                }

                if (controllerRole == ETrackedControllerRole.RightHand)
                {
                    role = BasisBoneTrackedRole.RightHand;
                    source = SteamVR_Input_Sources.RightHand;
                    return true;
                }

                role = BasisBoneTrackedRole.LeftHand;
                source = SteamVR_Input_Sources.LeftHand;
                Debug.LogError("Device unknown");
            }

            return false;
        }

        public void DestroyPhysicalTrackedDevice(string id)
        {
            TypicalDevices.Remove(id);
            BasisDeviceManagement.Instance.RemoveDevicesFrom("BasisOpenVRManagement", id);
        }

        private async Task HandleExistingDevice(string uniqueID, string notUniqueID, string className, OpenVRDevice device)
        {
            foreach (BasisInput input in BasisDeviceManagement.Instance.AllInputDevices)
            {
                if (input.UniqueDeviceIdentifier == uniqueID && input.SubSystemIdentifier == uniqueID)
                {
                    if (input.ClassName == className)
                    {
                        if (input is BasisOpenVRInputSpatial spatial)
                        {
                            bool foundRole = TryAssignRole(device.deviceClass, device.deviceIndex, out BasisBoneTrackedRole role, out SteamVR_Input_Sources source);
                            await spatial.Initialize(UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose.Center, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), foundRole, role, source);
                        }
                        else if (input is BasisOpenVRInputController controller)
                        {
                            bool foundRole = TryAssignRole(device.deviceClass, device.deviceIndex, out BasisBoneTrackedRole role, out SteamVR_Input_Sources source);
                            await controller.Initialize(device, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), foundRole, role, source);
                        }
                        else if (input is BasisOpenVRInput basisInput)
                        {
                            await basisInput.Initialize(device, uniqueID, notUniqueID, nameof(BasisOpenVRManagement), false, BasisBoneTrackedRole.CenterEye);
                        }
                        else
                        {
                            Debug.LogError("Some other Class Name " + input.ClassName + " look over this!");
                        }
                        return;
                    }
                    else
                    {
                        DestroyPhysicalTrackedDevice(uniqueID);
                        await OnDeviceConnected(device.deviceIndex, true);
                        return;
                    }
                }
            }
        }

        public override void StopSDK()
        {
            if (SteamVR_BehaviourGameobject != null)
            {
                GameObject.Destroy(SteamVR_BehaviourGameobject);
            }

            foreach (var device in TypicalDevices.Keys.ToList())
            {
                DestroyPhysicalTrackedDevice(device);
            }

            SteamVR_Behaviour = null;
            SteamVR_Render = null;
            IsInUse = false;
        }

        public override async Task BeginLoadSDK()
        {
        }

        public override async Task StartSDK()
        {
            if (IsInUse) return;

            BasisDeviceManagement.Instance.SetCameraRenderState(true);

            Debug.Log("Starting SteamVR Instance...");
            SteamVR = SteamVR.instance;

            if (SteamVR_BehaviourGameobject == null)
            {
                SteamVR_BehaviourGameobject = new GameObject(SteamVRBehaviour);
            }

            SteamVR_Behaviour = BasisHelpers.GetOrAddComponent<SteamVR_Behaviour>(SteamVR_BehaviourGameobject);
            SteamVR_Render = BasisHelpers.GetOrAddComponent<SteamVR_Render>(SteamVR_BehaviourGameobject);
            SteamVR_Behaviour.Initialize(SteamVR_Render, SteamVR_Behaviour);
            StartCoroutine(CheckState());
            IsInUse = true;
        }

        public override string Type()
        {
            return "OpenVRLoader";
        }
    }
}