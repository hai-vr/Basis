using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    /// <summary>
    /// Networks the per-player spine proportion scale (see BasisSpineProportionCore) so remotes render the
    /// wearer's torso at its calibrated length rather than the avatar's default. It is a rare, reliable,
    /// per-player value that changes only on (re)calibration, so it rides the generic SceneChannel -- one
    /// float, broadcast when it changes and re-sent to each newly-seen player -- with no per-frame pose-packet
    /// or server wire-format change. The remote applies it by re-spacing that avatar's spine bones
    /// (BasisRemoteAvatarDriver.SetSpineProportionScale). A scale of 1 is a no-op end to end.
    /// </summary>
    public static class BasisSpineProportionNetworking
    {
        /// <summary>
        /// Reserved SceneChannel message index -- sibling to <see cref="Sync.BasisSyncBatchCollector.BatchMessageIndex"/>
        /// (ushort.MaxValue). Must never be assigned to a networked object or it collides with that object's handler.
        /// </summary>
        public const ushort MessageIndex = ushort.MaxValue - 1;

        const float k_SendEpsilon = 0.001f;

        static float _localScale = 1f;
        static bool _hasLocal;
        // A scale that arrived before the sending player's BasisRemotePlayer existed here (join race);
        // applied when that player joins.
        static readonly Dictionary<ushort, float> _pending = new Dictionary<ushort, float>();

        [RuntimeInitializeOnLoadMethod]
        static void Init()
        {
            // ==== SPINE PROPORTION DEFORMATION DISABLED 2026-07-18 (revisit later). Uncomment to register the
            //      send/receive + rejoin-rebroadcast so remotes re-space their spine to the wearer's scale. ====
            // BasisNetworkGenericMessages.RegisterHandler(MessageIndex, OnReceived);
            // BasisNetworkPlayer.OnRemotePlayerJoined -= HandleRemotePlayerJoined;
            // BasisNetworkPlayer.OnRemotePlayerJoined += HandleRemotePlayerJoined;
            // BasisNetworkPlayer.OnRemotePlayerLeft -= HandleRemotePlayerLeft;
            // BasisNetworkPlayer.OnRemotePlayerLeft += HandleRemotePlayerLeft;
        }

        /// <summary>
        /// Called every frame from the local rig with the applied spine scale. Broadcasts to everyone only
        /// when it actually changes (a recalibration, or the toggle/cap sliders) -- otherwise a float compare.
        /// </summary>
        public static void UpdateLocalScale(float scale)
        {
            if (!(scale > 0f)) scale = 1f;
            if (_hasLocal && Mathf.Abs(scale - _localScale) < k_SendEpsilon)
            {
                return;
            }
            _localScale = scale;
            _hasLocal = true;
            Send(null);
        }

        static void HandleRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            if (networkPlayer == null)
            {
                return;
            }
            // Push my current scale to the newcomer so they render me correctly.
            if (_hasLocal)
            {
                Send(new ushort[] { networkPlayer.playerId });
            }
            // Apply their scale if it raced ahead of their player object being created here.
            if (_pending.TryGetValue(networkPlayer.playerId, out float s))
            {
                _pending.Remove(networkPlayer.playerId);
                // ==== SPINE PROPORTION DEFORMATION DISABLED 2026-07-18 (revisit later). ====
                // remotePlayer?.RemoteAvatarDriver?.SetSpineProportionScale(s);
            }
        }

        static void HandleRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            if (networkPlayer != null)
            {
                _pending.Remove(networkPlayer.playerId);
            }
        }

        static void OnReceived(ushort senderId, byte[] payload, DeliveryMethod deliveryMethod)
        {
            if (payload == null || payload.Length < 4)
            {
                return;
            }
            float s = new NetDataReader(payload).GetFloat();
            // Applying touches Transform.localPosition (main-thread only). Server-relayed scene messages
            // dispatch on the polled main thread, but marshal defensively for any off-thread path.
            if (BasisNetworkManagement.IsMainThread())
            {
                Apply(senderId, s);
            }
            else
            {
                Basis.Scripts.Device_Management.BasisDeviceManagement.EnqueueOnMainThread(() => Apply(senderId, s));
            }
        }

        static void Apply(ushort senderId, float s)
        {
            if (BasisNetworkPlayers.Players.TryGetValue(senderId, out BasisNetworkPlayer np) &&
                np != null && np.Player is BasisRemotePlayer remote)
            {
                // ==== SPINE PROPORTION DEFORMATION DISABLED 2026-07-18 (revisit later). ====
                // remote.RemoteAvatarDriver?.SetSpineProportionScale(s);
            }
            else
            {
                _pending[senderId] = s;
            }
        }

        static void Send(ushort[] recipients)
        {
            if (BasisNetworkConnection.LocalPlayerPeer == null)
            {
                return;
            }
            NetDataWriter writer = new NetDataWriter();
            writer.Put(_localScale);
            BasisNetworkGenericMessages.OnNetworkMessageSend(MessageIndex, writer.CopyData(), DeliveryMethod.ReliableOrdered, recipients);
        }
    }
}
