using Basis.Network.Core;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Server-wide toggles that admins can flip to globally disable
    /// avatar, prop, or world loading for all non-admin players.
    /// Thread-safe — reads/writes use interlocked operations.
    /// </summary>
    public static class BasisGlobalLockManager
    {
        // 0 = unlocked (loading allowed), 1 = locked (loading blocked)
        private static int _avatarsLocked;
        private static int _propsLocked;
        private static int _worldsLocked;
        private static int _serversLocked;
        private static int _thirdPersonDisabled;
        private static int _additionalAvatarDataLock;
        private static int _cameraMetadataDisallowMask;

        public static bool AvatarsLocked => Interlocked.CompareExchange(ref _avatarsLocked, 0, 0) == 1;
        public static bool PropsLocked => Interlocked.CompareExchange(ref _propsLocked, 0, 0) == 1;
        public static bool WorldsLocked => Interlocked.CompareExchange(ref _worldsLocked, 0, 0) == 1;
        public static bool ServersLocked => Interlocked.CompareExchange(ref _serversLocked, 0, 0) == 1;
        public static bool ThirdPersonDisabled => Interlocked.CompareExchange(ref _thirdPersonDisabled, 0, 0) == 1;
        public static bool AdditionalAvatarDataLock => Interlocked.CompareExchange(ref _additionalAvatarDataLock, 0, 0) == 1;
        public static byte CameraMetadataDisallowMask => (byte)Interlocked.CompareExchange(ref _cameraMetadataDisallowMask, 0, 0);

        /// <summary>
        /// Seed the initial lock state from the server configuration.
        /// Call once at startup before any client threads are running.
        /// </summary>
        public static void InitializeFromConfig(Configuration config)
        {
            Interlocked.Exchange(ref _avatarsLocked, config.AvatarsLocked ? 1 : 0);
            Interlocked.Exchange(ref _propsLocked, config.PropsLocked ? 1 : 0);
            Interlocked.Exchange(ref _worldsLocked, config.WorldsLocked ? 1 : 0);
            Interlocked.Exchange(ref _serversLocked, config.ServersLocked ? 1 : 0);
            Interlocked.Exchange(ref _thirdPersonDisabled, config.ThirdPersonDisabled ? 1 : 0);
            Interlocked.Exchange(ref _additionalAvatarDataLock, config.AdditionalAvatarDataLock ? 1 : 0);
            Interlocked.Exchange(ref _cameraMetadataDisallowMask, config.CameraMetadataDisallowMask);
        }

        /// <summary>
        /// Toggle avatar loading. Returns the new state (true = locked).
        /// </summary>
        public static bool ToggleAvatars() => Toggle(ref _avatarsLocked);

        /// <summary>
        /// Toggle prop loading. Returns the new state (true = locked).
        /// </summary>
        public static bool ToggleProps() => Toggle(ref _propsLocked);

        /// <summary>
        /// Toggle world loading. Returns the new state (true = locked).
        /// </summary>
        public static bool ToggleWorlds() => Toggle(ref _worldsLocked);

        /// <summary>
        /// Toggle server-share dropping. Returns the new state (true = locked).
        /// </summary>
        public static bool ToggleServers() => Toggle(ref _serversLocked);

        /// <summary>
        /// Toggle third-person camera availability. Returns the new state (true = disabled).
        /// </summary>
        public static bool ToggleThirdPerson() => Toggle(ref _thirdPersonDisabled);

        /// <summary>
        /// Toggle the network-side strip of AdditionalAvatarDatas on inbound avatar
        /// sync messages. Returns the new state (true = additional data stripped).
        /// </summary>
        public static bool ToggleAdditionalAvatarDataLock() => Toggle(ref _additionalAvatarDataLock);

        /// <summary>
        /// Set the per-category camera photo-metadata disallow mask (set bit = disallowed).
        /// </summary>
        public static void SetCameraMetadataDisallowMask(byte mask) => Interlocked.Exchange(ref _cameraMetadataDisallowMask, mask);

        private static bool Toggle(ref int field)
        {
            int prev, next;
            do
            {
                prev = field;
                next = prev == 0 ? 1 : 0;
            }
            while (Interlocked.CompareExchange(ref field, next, prev) != prev);
            return next == 1;
        }

        /// <summary>
        /// Sends the current global lock state to a specific peer.
        /// Used when a new player connects so they know what's locked.
        /// </summary>
        public static void SendLockStateToPeer(NetPeer peer)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetLockState);
            writer.Put(AvatarsLocked);
            writer.Put(PropsLocked);
            writer.Put(WorldsLocked);
            writer.Put(ServersLocked);
            // Appended after ServersLocked so older clients reading 4 bools still parse cleanly.
            writer.Put(ThirdPersonDisabled);
            // Appended after ThirdPersonDisabled — older clients parsing 5 bools still work.
            writer.Put(AdditionalAvatarDataLock);
            // Appended after AdditionalAvatarDataLock (1 byte) — older clients parsing 6 bools still work.
            writer.Put(CameraMetadataDisallowMask);
            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
        }

        /// <summary>
        /// Broadcasts the current lock state to all connected clients.
        /// </summary>
        public static void BroadcastLockState()
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetLockState);
            writer.Put(AvatarsLocked);
            writer.Put(PropsLocked);
            writer.Put(WorldsLocked);
            writer.Put(ServersLocked);
            // Appended after ServersLocked so older clients reading 4 bools still parse cleanly.
            writer.Put(ThirdPersonDisabled);
            // Appended after ThirdPersonDisabled — older clients parsing 5 bools still work.
            writer.Put(AdditionalAvatarDataLock);
            // Appended after AdditionalAvatarDataLock (1 byte) — older clients parsing 6 bools still work.
            writer.Put(CameraMetadataDisallowMask);
            NetworkServer.BroadcastMessageToClients(
                writer,
                BasisNetworkCommons.AdminChannel,
                NetworkServer.PeerSnapshot,
                DeliveryMethod.ReliableOrdered
            );
            NetworkServer.ReturnWriter(writer);
        }
    }
}
