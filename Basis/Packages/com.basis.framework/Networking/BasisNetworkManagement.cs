using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using LiteNetLib;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using static SerializableBasis;
namespace Basis.Scripts.Networking
{
    [DefaultExecutionOrder(15001)]
    public sealed class BasisNetworkManagement : MonoBehaviour
    {
        [Header("Connection")]
        public string Ip = "170.64.184.249";
        public ushort Port = 4296;
        [HideInInspector] public string Password = "default_password";
        public bool IsHostMode = false;
        public static BasisNetworkManagement Instance { get; private set; }
        public static SynchronizationContext MainThreadContext { get; internal set; }
        public static bool NetworkRunning { get; private set; }
        public static BasisNetworkTransmitter Transmitter { get; internal set; }
        public static NetPeer LocalPlayerPeer => BasisNetworkConnection.LocalPlayerPeer;

        [SerializeField] public BasisNetworkTransmitter LocalAccessTransmitter;
        public static ServerMetaDataMessage ServerMetaDataMessage = new ServerMetaDataMessage();
        public static Action OnEnableInstanceCreate;
        public static InstantiationParameters instantiationParameters;
        private static int mainThreadId;

        private void OnEnable()
        {
            if (!BasisHelpers.CheckInstance(Instance))
            {
                enabled = false;
                return;
            }

            Instance = this;

            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            BasisRemoteNetworkDriver.Initialize(95, Unity.Collections.Allocator.Persistent);

            BasisAudioTransformDriver.Initialize(1024);
            BasisAudioRemoteSource.Initalize();

            instantiationParameters = new InstantiationParameters(Vector3.zero, Quaternion.identity, BasisDeviceManagement.Instance.transform);

            BasisMuscleRange.Initalize();

            MainThreadContext = SynchronizationContext.Current;

            // Reset & initialize metadata defaults
            BasisNetworkPlayers.ClearAllRegistries(); // new: central place
            ServerMetaDataMessage = new ServerMetaDataMessage
            {
                ClientMetaDataMessage = new ClientMetaDataMessage(),
                SyncInterval = 50,
                BaseMultiplier = 1,
                IncreaseRate = 0.005f,
                SlowestSendRate = 2.5f
            };

            if (BasisDeviceManagement.Instance != null)
            {
                transform.SetParent(BasisDeviceManagement.Instance.transform, false);
            }

            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            OnEnableInstanceCreate?.Invoke();

            BasisNetworkPlayers.PublishReceiversSnapshot();
            NetworkRunning = true;

            // hand configuration to connection layer
            BasisNetworkConnection.Configure(this);
        }
        public static bool IsMainThread()
        {
            return Thread.CurrentThread.ManagedThreadId == mainThreadId;
        }
        public static void Disconnect()
        {
            
        }
        public static async void Shutdown()
        {
            BasisAudioRemoteSource.DeInitalize();
            BasisNetworkPlayers.ClearAllRegistries();
            await BasisNetworkConnection.Shutdown();
            BasisNetworkConnection.DisconnectClientOnly();
        }

        // --- Public wrappers (nice, tiny face for UI buttons/inspector) ----
        public void Connect() => BasisNetworkConnection.Connect(Port, Ip, Password, IsHostMode);

        // Simulation ticks stay here; they call into players/driver
        public static void SimulateNetworkCompute()
        {
            if (!NetworkRunning) return;

            var snapshot = BasisNetworkPlayers.ReceiversSnapshot;
            for (int Index = 0; Index < snapshot.Length; Index++)
            {
                snapshot[Index]?.Compute();
            }

            BasisRemoteNetworkDriver.Compute();
            BasisNetworkProfiler.Update();
        }

        public static void SimulateNetworkApply()
        {
            if (!NetworkRunning) return;

            BasisRemoteNetworkDriver.Apply();

            var snapshot = BasisNetworkPlayers.ReceiversSnapshot;
            for (int Index = 0; Index < snapshot.Length; Index++)
            {
                snapshot[Index]?.Apply();
            }

            BasisAudioTransformDriver.BeginFrame();
            BasisAudioTransformDriver.EndFrame();
        }

        // Scene reset + user-visible disconnect message
        internal static void ResetSceneAndAnnounce(DisconnectInfo info)
        {
            //   SceneManager.LoadScene(0, LoadSceneMode.Single);
            //  await Boot_Sequence.BootSequence.OnAddressablesInitializationComplete();

            BasisNetworkEvents.HandleDisconnectionReason(info);
        }
        public static int GetServerTimeOffsetSeconds()
        {
            var serverTime = RemoteUtcTime();
            return DateTimeToSeconds(serverTime);
        }

        public static int GetServerTimeInMilliseconds()
        {
            DateTime serverTime = RemoteUtcTime();
            int secondsAhead = DateTimeToSeconds(serverTime);
            return secondsAhead;
        }

        /// <summary>Send a message intended for server-only handling.</summary>
        public static void SendServerSideMessage(byte[] payload, DeliveryMethod mode)
        {
            if (payload == null || payload.Length == 0)
            {
                BasisDebug.LogWarning("Attempted to send empty server-side message.");
                return;
            }

            var peer = LocalPlayerPeer;
            if (peer == null)
            {
                BasisDebug.LogError("Local NetPeer was null!", BasisDebug.LogTag.Networking);
                return;
            }

            peer.Send(payload, BasisNetworkCommons.ServerBoundChannel, mode);
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.SceneData, payload.Length);
        }

        public static long RemoteTimeDelta() => LocalPlayerPeer?.RemoteTimeDelta ?? 0;
        public static DateTime RemoteUtcTime() => LocalPlayerPeer?.RemoteUtcTime ?? DateTime.UtcNow;
        public static float TimeSinceLastPacket() => LocalPlayerPeer?.TimeSinceLastPacket ?? float.MaxValue;
        public static NetStatistics Statistics() => LocalPlayerPeer?.Statistics;

        public static int DateTimeToSeconds(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.Now;
            return (int)timeSpan.TotalSeconds;
        }
    }
}
