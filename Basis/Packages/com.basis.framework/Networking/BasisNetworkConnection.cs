using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using BasisNetworkClient;
using LiteNetLib;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking
{
    /// <summary>
    /// Connection/session management, server runner, time utilities, and send helpers.
    /// </summary>
    public static class BasisNetworkConnection
    {
        // Runtime state
        public static NetPeer LocalPlayerPeer { get; private set; }
        public static NetworkClient NetworkClient { get; private set; } = new NetworkClient();
        public static bool LocalPlayerIsConnected { get; private set; }
        public static BasisNetworkServerRunner BasisNetworkServerRunner = null;

        // config provider
        private static BasisNetworkManagement _mgr;

        internal static void Configure(BasisNetworkManagement manager) => _mgr = manager;

        // --- Logging passthrough -------------------------------------------
        private static void LogErrorOutput(string msg) => BasisDebug.LogError(msg, BasisDebug.LogTag.Networking);
        private static void LogWarningOutput(string msg) => BasisDebug.LogWarning(msg);
        private static void LogOutput(string msg) => BasisDebug.Log(msg, BasisDebug.LogTag.Networking);

        // --- Public API mirrors (unchanged signatures where possible) -------
        public static bool TryGetLocalPlayerID(out ushort localId)
        {
            localId = 0;
            if (LocalPlayerPeer == null) return false;
            localId = (ushort)LocalPlayerPeer.RemoteId;
            return true;
        }

        // --- Connect / Disconnect ------------------------------------------
        public static void Connect(ushort port, string ipString, string primitivePassword, bool isHostMode)
        {
            BNL.LogOutput += LogOutput;
            BNL.LogWarningOutput += LogWarningOutput;
            BNL.LogErrorOutput += LogErrorOutput;

            var uuid = BasisDIDAuthIdentityClient.GetOrSaveDID();

            if (isHostMode)
            {
                ipString = "localhost";
                BasisNetworkServerRunner = new BasisNetworkServerRunner();
                var serverConfig = new Configuration
                {
                    IPv4Address = ipString,
                    HasFileSupport = false,
                    UseNativeSockets = false,
                    UseAuthIdentity = true,
                    UseAuth = true,
                    Password = primitivePassword,
                    EnableStatistics = false
                };
                BasisNetworkServerRunner.Initalize(serverConfig, string.Empty, uuid);
            }

            BasisDebug.Log($"Connecting with Port {port} IpString {ipString}");

            var basisLocalPlayer = BasisLocalPlayer.Instance;
            basisLocalPlayer.UUID = uuid;

            byte[] avatarBytes = BasisBundleConversionNetwork.ConvertBasisLoadableBundleToBytes(basisLocalPlayer.AvatarMetaData);

            var readyMessage = new ReadyMessage
            {
                clientAvatarChangeMessage = new ClientAvatarChangeMessage
                {
                    byteArray = avatarBytes,
                    loadMode = basisLocalPlayer.AvatarLoadMode,
                    LocalAvatarIndex = 0,
                },
                playerMetaDataMessage = new ClientMetaDataMessage
                {
                    playerUUID = basisLocalPlayer.UUID,
                    playerDisplayName = basisLocalPlayer.DisplayName
                }
            };

            BasisNetworkAvatarCompressor.InitalAvatarData(basisLocalPlayer.BasisAvatar.Animator, out var dataSet);
            readyMessage.localAvatarSyncMessage = dataSet.LASM;

            BasisDebug.Log("Network Starting Client");

            LocalPlayerPeer = NetworkClient.StartClient(
                ipString,
                port,
                readyMessage,
                Encoding.UTF8.GetBytes(primitivePassword),
                true);

            NetworkClient.listener.PeerConnectedEvent += PeerConnectedEvent;
            NetworkClient.listener.PeerDisconnectedEvent += BasisNetworkEvents.PeerDisconnectedEvent;
            NetworkClient.listener.NetworkReceiveEvent += BasisNetworkEvents.NetworkReceiveEvent;

            if (LocalPlayerPeer != null)
            {
                BasisDebug.Log("Network Client Started " + LocalPlayerPeer.RemoteId);
            }
            else
            {
                HandleDisconnection(null, new DisconnectInfo { Reason = DisconnectReason.ConnectionFailed });
            }
        }

        private static void PeerConnectedEvent(NetPeer peer)
        {
            BasisDebug.Log("Success! Now setting up Networked Local Player");

            BasisNetworkManagement.MainThreadContext?.Post(_ =>
            {
                try
                {
                    LocalPlayerPeer = peer;
                    ushort localPlayerID = (ushort)peer.RemoteId;

                    _mgr.transform.GetPositionAndRotation(out Vector3 _, out Quaternion _);

                    var transmitter = new BasisNetworkTransmitter(localPlayerID);
                    BasisNetworkManagement.Transmitter = transmitter;
                    _mgr.LocalAccessTransmitter = transmitter;
                    _mgr.LocalAccessTransmitter.Player = BasisLocalPlayer.Instance;

                    if (BasisLocalPlayer.Instance.LocalAvatarDriver != null)
                    {
                        if (BasisLocalAvatarDriver.HasEvents == false)
                        {
                            BasisLocalAvatarDriver.CalibrationComplete += _mgr.LocalAccessTransmitter.OnAvatarCalibrationLocal;
                            BasisLocalAvatarDriver.HasEvents = true;
                        }
                        BasisLocalPlayer.Instance.LocalBoneDriver.FindBone(out _mgr.LocalAccessTransmitter.MouthBone, BasisBoneTrackedRole.Mouth);
                    }
                    else
                    {
                        BasisDebug.LogError("Missing CharacterIKCalibration");
                    }

                    if (!BasisNetworkPlayers.AddPlayer(_mgr.LocalAccessTransmitter))
                    {
                        BasisDebug.LogError($"Cannot add player {localPlayerID}");
                    }

                    _mgr.LocalAccessTransmitter.Initialize();

                    BasisNetworkPlayer.OnLocalPlayerJoined?.Invoke(_mgr.LocalAccessTransmitter, BasisLocalPlayer.Instance);
                    BasisNetworkPlayer.OnPlayerJoined?.Invoke(_mgr.LocalAccessTransmitter);

                    LocalPlayerIsConnected = true;
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError($"Error setting up the local player: {ex.Message} {ex.StackTrace}");
                }
            }, null);
        }

        public static void HandleDisconnection(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            BasisNetworkManagement.MainThreadContext?.Post(async _ =>
            {
                try
                {
                    if (LocalPlayerPeer != null &&
                        BasisNetworkPlayers.Players.TryGetValue((ushort)LocalPlayerPeer.RemoteId, out var networkedPlayer))
                    {
                        if (networkedPlayer?.Player is BasisLocalPlayer local)
                            BasisNetworkPlayer.OnLocalPlayerLeft?.Invoke(networkedPlayer, local);

                        BasisNetworkPlayer.OnPlayerLeft?.Invoke(networkedPlayer);
                    }

                    if (BasisNetworkServerRunner != null)
                    {
                        BasisNetworkServerRunner.Stop();
                        BasisNetworkServerRunner = null;
                    }

                    BasisNetworkPlayers.ClearAllRegistries();

                    BasisDebug.Log($"Client disconnected from server [{peer?.RemoteId}] [{disconnectInfo.Reason}]");

                    await BasisNetworkManagement.ResetSceneAndAnnounce(disconnectInfo);
                }
                finally
                {
                    LocalPlayerPeer = null;
                    LocalPlayerIsConnected = false;
                }
            }, null);
        }

        public static void DisconnectClientOnly()
        {
            try
            {
                NetworkClient?.Disconnect();
            }
            catch { /* swallow */ }
        }

        // --- Shutdown -------------------------------------------------------
        public static async Task Shutdown()
        {
            BasisRemoteNetworkDriver.Shutdown();

            // Reset instance-scoped configuration to safe defaults
            if (_mgr != null)
            {
                _mgr.Ip = "0.0.0.0";
                _mgr.Port = 0;
                _mgr.Password = string.Empty;
                _mgr.IsHostMode = false;
            }

            // Clear delegates / events
            BasisNetworkPlayer.OnOwnershipTransfer = null;
            BasisNetworkPlayer.OnLocalPlayerJoined = null;
            BasisNetworkPlayer.OnRemotePlayerJoined = null;
            BasisNetworkPlayer.OnLocalPlayerLeft = null;
            BasisNetworkPlayer.OnRemotePlayerLeft = null;
            BasisNetworkManagement.OnEnableInstanceCreate = null;

            BasisNetworkPlayers.ClearAllRegistries();

            await BasisNetworkSpawnItem.Reset();

            BasisNetworkManagement.MainThreadContext = null;
            LocalPlayerPeer = null;
            BasisNetworkManagement.Transmitter = null;
            if (_mgr != null) _mgr.LocalAccessTransmitter = null;
            LocalPlayerIsConnected = false;

            BasisAudioTransformDriver.Shutdown();

            // let the MonoBehaviour reset its Instance in OnDestroy; no direct assignment here
            BasisDebug.Log("BasisNetworkManagement has been successfully shutdown.", BasisDebug.LogTag.Networking);
        }
    }
}
