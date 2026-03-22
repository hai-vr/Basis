namespace Basis.Network.Core
{
    public static class BasisNetworkCommons
    {
        /// <summary>
        /// this is the maximum Connections that can occur under the hood.
        /// </summary>
        public const int MaxConnections = ushort.MaxValue;

        public const int NetworkIntervalPoll = 2;
        public const int PingInterval = 1500;
        public const int ReceivePollingTime = 50000;
        public const int PacketPoolSize = 4096;
        /// <summary>
        /// when adding a new message we need to increase this
        /// will function up to 64
        /// </summary>
        public const byte TotalChannels = 42;
        /// <summary>
        /// Shout mode voice channel. Non-spatialized audio broadcast to all clients.
        /// </summary>
        public const byte ShoutVoiceChannel = 0;
        /// <summary>
        /// Auth Identity Message
        /// </summary>
        public const byte AuthIdentityChannel = 1;
        /// <summary>
        /// this is normally avatar movement
        /// </summary>
        public const byte PlayerAvatarChannel = 2;
        /// <summary>
        /// this is what people use voice data only can be used once!
        /// </summary>
        public const byte VoiceChannel = 3;
        /// <summary>
        /// this is what people use to send data on the scene network
        /// </summary>
        public const byte SceneChannel = 4;
        /// <summary>
        /// this is what people use to send data on there avatar
        /// </summary>
        public const byte AvatarChannel = 5;
        /// <summary>
        /// Message to create a remote player entity
        /// </summary>
        public const byte CreateRemotePlayerChannel = 6;
        /// <summary>
        /// Message to create a remote player entity
        /// </summary>
        public const byte CreateRemotePlayersForNewPeerChannel = 7;
        /// <summary>
        /// message to swap to a different avatar
        /// </summary>
        public const byte AvatarChangeMessageChannel = 8;
        /// <summary>
        /// Ownership Response is when we get the current owner
        /// </summary>
        public const byte GetCurrentOwnerRequestChannel = 9;
        /// <summary>
        /// changes current owner of a string
        /// </summary>
        public const byte ChangeCurrentOwnerRequestChannel = 10;
        /// <summary>
        /// Remove Current Ownership
        /// </summary>
        public const byte RemoveCurrentOwnerRequestChannel = 11;
        /// <summary>
        /// the audio recipients that can here
        /// </summary>
        public const byte AudioRecipientsChannel = 12;
        /// <summary>
        /// Removes a players entity
        /// </summary>
        public const byte DisconnectionChannel = 13;
        /// <summary>
        /// assign a net id (string to ushort)
        /// </summary>
        public const byte netIDAssignChannel = 14;
        /// <summary>
        /// assign a array of net id (string to ushort)
        /// </summary>
        public const byte NetIDAssignsChannel = 15;
        /// <summary>
        /// load a resource (scene,gameobject,script,asset) whatever the implementation is
        /// </summary>
        public const byte LoadResourceChannel = 16;
        /// <summary>
        /// Unload a Resource
        /// </summary>
        public const byte UnloadResourceChannel = 17;
        /// <summary>
        /// Client sends a admin message and the server needs to respond accordingly
        /// </summary>
        public const byte AdminChannel = 18;
        /// <summary>
        /// Content Share Channel - used to drop content spheres
        /// </summary>
        public const byte ContentShareChannel = 19;
        /// <summary>
        /// Content Share Cleanup Channel - used to remove content spheres
        /// </summary>
        public const byte ContentShareCleanupChannel = 20;
        /// <summary>
        /// requires implementation from a developer,
        /// ground work for hooking in code that only gets delivered to the server
        /// </summary>
        public const byte ServerBoundChannel = 21;
        /// <summary>
        /// this contains all meta data that the player requires
        /// </summary>
        public const byte metaDataChannel = 22;
        /// <summary>
        /// this stores data
        /// </summary>
        public const byte StoreDatabaseChannel = 23;
        /// <summary>
        /// Requests data by id
        /// </summary>
        public const byte RequestStoreDatabaseChannel = 24;
        /// <summary>
        /// Server Statistics Channel
        /// </summary>
        public const byte ServerStatisticsChannel = 25;
        /// <summary>
        /// request is admin from client
        /// </summary>
        public const byte ServerIsAdminChannel = 26;
        /// <summary>
        /// chat text messages displayed above player nameplates
        /// </summary>
        public const byte ChatChannel = 27;
        /// <summary>
        /// PIP camera created/destroyed state (reliable, per-player).
        /// </summary>
        public const byte CameraPIPStateChannel = 28;
        /// <summary>
        /// PIP camera position updates (sequenced, position only - no rotation).
        /// </summary>
        public const byte CameraPIPPositionChannel = 29;

        // ── Per-quality avatar channels (server→client) ──────────────────
        // Layout: quality * 2 + hasAdditional
        //   30 = VeryLow              31 = VeryLow + Additional
        //   32 = Low                  33 = Low + Additional
        //   34 = Medium               35 = Medium + Additional
        //   36 = High                 37 = High + Additional
        public const byte PlayerAvatarVeryLowChannel = 30;
        public const byte PlayerAvatarVeryLowAdditionalChannel = 31;
        public const byte PlayerAvatarLowChannel = 32;
        public const byte PlayerAvatarLowAdditionalChannel = 33;
        public const byte PlayerAvatarMediumChannel = 34;
        public const byte PlayerAvatarMediumAdditionalChannel = 35;
        public const byte PlayerAvatarHighChannel = 36;
        public const byte PlayerAvatarHighAdditionalChannel = 37;

        /// <summary>
        /// Client tells server it has finished preloading a resource (ready or failed).
        /// </summary>
        public const byte PreloadReadyChannel = 38;
        /// <summary>
        /// Server tells all clients to spawn a previously preloaded resource.
        /// </summary>
        public const byte SpawnPreloadedChannel = 39;
        /// <summary>
        /// Generic low-priority events channel. The first byte of the payload
        /// identifies the event type (see EventType constants below).
        /// </summary>
        public const byte EventsChannel = 40;

        // ── Event type sub-bytes for EventsChannel ──
        /// <summary>Camera shutter sound fired when a player takes a photo.</summary>
        public const byte EventType_CameraShutterSound = 0;
        /// <summary>Camera countdown started — remote clients replay the tick/shutter timing.</summary>
        public const byte EventType_CameraCountdown = 1;

        /// <summary>
        /// Maps quality index (0‑3) + additional data presence → channel.
        /// </summary>
        public static byte GetPlayerAvatarChannelForQuality(int qualityIndex, bool hasAdditionalData)
        {
            return (byte)(PlayerAvatarVeryLowChannel + qualityIndex * 2 + (hasAdditionalData ? 1 : 0));
        }

        /// <summary>
        /// All 8 per-quality avatar channels for aggregate congestion checks.
        /// </summary>
        public static readonly byte[] PlayerAvatarQualityChannels = new byte[]
        {
            PlayerAvatarVeryLowChannel,
            PlayerAvatarVeryLowAdditionalChannel,
            PlayerAvatarLowChannel,
            PlayerAvatarLowAdditionalChannel,
            PlayerAvatarMediumChannel,
            PlayerAvatarMediumAdditionalChannel,
            PlayerAvatarHighChannel,
            PlayerAvatarHighAdditionalChannel,
        };
    }
}
