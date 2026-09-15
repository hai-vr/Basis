using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.OpenXR;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;
public class BasisOpenXRHeadInput : BasisInput
{
    public BasisOpenXRInputEye BasisOpenXRInputEye;
    public InputActionProperty Position;
    public InputActionProperty Rotation;
    public InputActionProperty TrackingState;

    private InputAction _positionAction;
    private InputAction _rotationAction;
    private InputAction _trackingStateAction;
    private const int TrackingStatePosition = 1;
    private const int TrackingStateRotation = 2;

    public void Initialize(string UniqueID, string UnUniqueID, string subSystems, bool AssignTrackedRole)
    {
        TrackingHardware = BasisTrackingHardware.InsideOut;
        InitializeTracking(UniqueID, UnUniqueID, subSystems, AssignTrackedRole, BasisBoneTrackedRole.CenterEye);

        Position = new InputActionProperty(new InputAction("<XRHMD>/centerEyePosition", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        Rotation = new InputActionProperty(new InputAction("<XRHMD>/centerEyeRotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        TrackingState = new InputActionProperty(new InputAction("<XRHMD>/trackingState", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

        Position.action.Enable();
        Rotation.action.Enable();
        TrackingState.action.Enable();

        _positionAction = Position.action;
        _rotationAction = Rotation.action;
        _trackingStateAction = TrackingState.action;

        BasisOpenXRInputEye = gameObject.AddComponent<BasisOpenXRInputEye>();
        BasisOpenXRInputEye.Initialize();
    }

    private void DisableInputActions()
    {
        Position.action?.Disable();
        Rotation.action?.Disable();
        TrackingState.action?.Disable();
    }

    public new void OnDestroy()
    {
        DisableInputActions();
        BasisOpenXRInputEye?.Shutdown();
        base.OnDestroy();
    }

    public override void LateDoPollData()
    {
        PollPose();
    }
    public override void RenderPollData()
    {
        PollPose();
        ComputeRaycastDirection(ScaledDeviceCoord.position, ScaledDeviceCoord.rotation, Quaternion.identity);
        UpdateInputEvents();

        if (BasisOpenXRInputEye != null)
        {
            BasisOpenXRInputEye.Simulate();
        }
    }
    private void PollPose()
    {
        int state = _trackingStateAction.ReadValue<int>();
        if ((state & TrackingStateRotation) != 0)
        {
            Quaternion rotation = _rotationAction.ReadValue<Quaternion>();
            float lengthSq = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            if (lengthSq > 0.5f && lengthSq < 2f)
            {
                UnscaledDeviceCoord.rotation = rotation;
            }
        }
        if ((state & TrackingStatePosition) != 0)
        {
            Vector3 position = _positionAction.ReadValue<Vector3>();
            if (float.IsFinite(position.x + position.y + position.z))
            {
                ComputeUnscaledDeviceCoord(ref UnscaledDeviceCoord, position);
            }
        }

        ConvertToScaledDeviceCoord();
        ControlOnlyAsDevice();
    }
    public override void ShowTrackedVisual()
    {
        if (BasisVisualTracker == null)
        {
            DeviceSupportInformation Match = BasisDeviceManagement.Instance.BasisDeviceNameMatcher.GetAssociatedDeviceMatchableNames(CommonDeviceIdentifier);
            if (Match.CanDisplayPhysicalTracker)
            {
                LoadModelWithKey(Match.DeviceID);
            }
            else
            {
                if (UseFallbackModel())
                {
                    LoadModelWithKey(FallbackDeviceID);
                }
            }
        }
    }
    public override void PlayHaptic(float duration = 0.25F, float amplitude = 0.5F, float frequency = 0.5F)
    {
       // BasisDebug.LogError("XRHead does not support Haptics Playback");
    }
    public override void PlaySoundEffect(string SoundEffectName, float Volume)
    {
        PlaySoundEffectDefaultImplementation(SoundEffectName, Volume);
    }
}
