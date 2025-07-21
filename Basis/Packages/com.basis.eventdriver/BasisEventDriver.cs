using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Eye_Follow;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Transmitters;
using UnityEngine;
using UnityEngine.InputSystem;
public class BasisEventDriver : MonoBehaviour
{
    public float updateInterval = 0.1f; // 100 milliseconds
    public float timeSinceLastUpdate = 0f;
    public bool IsBatchMode = false;
    public void OnEnable()
    {
        if (Application.isBatchMode)
        {
            IsBatchMode = true;
        }
        else
        {
            Application.onBeforeRender += OnBeforeRender;
        }
        BasisSceneFactory.Initalize();
        BasisObjectSyncDriver.Initalization();
    }
    public void OnDestroy()
    {
        BasisObjectSyncDriver.OnDestroy();
        Application.onBeforeRender -= OnBeforeRender;
    }
    public void OnDisable()
    {
        if (BasisDeviceManagement.IsHeadless())
        {
            IsBatchMode = true;
        }
        else
        {
            Application.onBeforeRender -= OnBeforeRender;
        }
    }
    public float DeltaTime;
    public double TimeAsDouble;
    public void Update()
    {
        DeltaTime = Time.deltaTime;
        TimeAsDouble = Time.timeAsDouble;
        BasisNetworkManagement.SimulateNetworkCompute(TimeAsDouble);
        BasisObjectSyncDriver.ScheduleRemoteLerp(DeltaTime);
        InputSystem.Update();
        timeSinceLastUpdate += DeltaTime;

        if (timeSinceLastUpdate >= updateInterval) // Use '>=' to avoid small errors
        {
            timeSinceLastUpdate -= updateInterval; // Subtract interval instead of resetting to zero
            BasisConsoleLogger.QueryLogDisplay();
        }
        if (!BasisDeviceManagement.hasPendingActions) return;

        while (BasisDeviceManagement.mainThreadActions.TryDequeue(out System.Action action))
        {
            action.Invoke();
        }

        // Reset flag once all actions are executed
        BasisDeviceManagement.hasPendingActions = !BasisDeviceManagement.mainThreadActions.IsEmpty;
    }
    public void FixedUpdate()
    {
        BasisSceneFactory.Simulate();
    }
    public void LateUpdate()
    {
        BasisDeviceManagement.OnDeviceManagementLoop?.Invoke();
        if (BasisLocalEyeDriver.RequiresUpdate())
        {
            BasisLocalEyeDriver.Instance.Simulate();
        }
        if (BasisLocalPlayer.PlayerReady)
        {
            BasisLocalPlayer.Instance.SimulateOnLateUpdate();
        }
        BasisLocalMicrophoneDriver.MicrophoneUpdate();
        BasisObjectSyncDriver.TransmitOwnedPickups(TimeAsDouble);
        BasisNetworkManagement.SimulateNetworkApply(TimeAsDouble);
        BasisObjectSyncDriver.CompleteScheduledRemoteLerp();
        if (IsBatchMode)
        {
            OnBeforeRender();
        }
    }
    private void OnBeforeRender()
    {
        if (BasisLocalPlayer.PlayerReady)
        {
            BasisLocalPlayer.Instance.SimulateOnRender(DeltaTime);
            //send out avatar
            BasisNetworkTransmitter.AfterAvatarChanges?.Invoke();
        }
    }
    public void OnApplicationQuit()
    {
        BasisLocalMicrophoneDriver.StopProcessingThread();
    }
}
