using Basis.Config;
using Basis.Contrib.Auth.DecentralizedIds.Newtypes;
using Basis.Contrib.Crypto;
using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.BasisSdk.Players;
using Basis.Utilities;
using BasisNetworkClient;
using System.Text;
using System.Threading;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static Basis.Network.Core.Serializable.SerializableBasis;
using static SerializableBasis;

namespace Basis.Network
{
    public class ClientManager
    {
        public int ClientCount => ConfigManager.ClientCount;
        private readonly CancellationTokenSource cts = new();
        public NetPeer[] FinalPeers;
        public NetworkClient[] FinalClients;
        public static int Size;

        // Cached once — config doesn't change at runtime
        private byte[] _cachedPasswordBytes;
        private byte[] _cachedAvatarBytes;
        private (byte[] bytes, byte loadMode)[] _avatarPool;

        public void Prepare()
        {
            Size = BasisAvatarBitPacking.ConvertToSize(BitQuality.High);
            BNL.Log($"Payload Size for muscles is now {Size}");

            _cachedPasswordBytes = Encoding.UTF8.GetBytes(ConfigManager.Password);
            var avatarInfo = new BasisAvatarNetworkLoad
            {
                URL = ConfigManager.AvatarUrl,
                UnlockPassword = ConfigManager.AvatarPassword
            };
            _cachedAvatarBytes = avatarInfo.EncodeToBytes();

            BuildAvatarPool();

            FinalPeers = new NetPeer[ClientCount];
            FinalClients = new NetworkClient[ClientCount];
        }

        private void BuildAvatarPool()
        {
            if (!ConfigManager.UseRandomAvatarFromKeyStore)
            {
                return;
            }

            var avatars = AvatarKeyStoreLoader.Load(ConfigManager.AvatarKeyStorePath, (byte)ConfigManager.AvatarLoadMode);
            if (avatars.Count == 0)
            {
                BNL.LogWarning("UseRandomAvatarFromKeyStore is on but no avatars were found; falling back to the configured AvatarUrl.");
                return;
            }

            _avatarPool = new (byte[] bytes, byte loadMode)[avatars.Count];
            for (int i = 0; i < avatars.Count; i++)
            {
                var info = new BasisAvatarNetworkLoad
                {
                    URL = avatars[i].Url,
                    UnlockPassword = avatars[i].Password
                };
                _avatarPool[i] = (info.EncodeToBytes(), avatars[i].LoadMode);
                BNL.Log($"Avatar pool [{i}] loadMode={avatars[i].LoadMode} url={avatars[i].Url}");
            }

            BNL.Log($"Loaded {_avatarPool.Length} avatar(s) from keystore for random assignment.");
        }

        private (byte[] bytes, byte loadMode) PickAvatar()
        {
            var pool = _avatarPool;
            if (pool != null && pool.Length > 0)
            {
                return pool[Random.Shared.Next(pool.Length)];
            }

            return (_cachedAvatarBytes, (byte)ConfigManager.AvatarLoadMode);
        }

        public async Task StartClientsAsync()
        {
            for (int Index = 0; Index < ClientCount; Index++)
            {
                var name = NameGenerator.GenerateRandomPlayerName();
                var identity = new ConsoleClientIdentity();
                var (avatarBytes, avatarLoadMode) = PickAvatar();

                var readyMessage = new ReadyMessage
                {
                    playerMetaDataMessage = new ClientMetaDataMessage
                    {
                        playerDisplayName = name,
                        playerUUID = identity.Did,
                        playerPlatform = "Headless",
                    },
                    clientAvatarChangeMessage = new ClientAvatarChangeMessage
                    {
                        byteArray = avatarBytes,
                        loadMode = avatarLoadMode,
                        LocalAvatarIndex = 0,
                    },
                    localAvatarSyncMessage = new LocalAvatarSyncMessage
                    {
                        array = MovementSender.Generate().Message.array,
                        AdditionalAvatarDataSize = 0,
                        LinkedAvatarIndex = 0,
                        DataQualityLevel = (byte)BitQuality.High,
                        AdditionalAvatarDatas = null,

                    }
                };
                var netClient = new NetworkClient();
                var peer = netClient.StartClient(ConfigManager.Ip, ConfigManager.Port, readyMessage, _cachedPasswordBytes, CreateConfig(), manualMode: true);

                if (peer != null)
                {
                    peer.Tag = identity;
                    netClient.listener.NetworkReceiveEvent += (p, r, ch, m) => MessageHandler.OnReceive(identity, p, r, ch, m);
                    netClient.listener.PeerDisconnectedEvent += MessageHandler.OnDisconnect;

                    Volatile.Write(ref FinalClients[Index], netClient);
                    Volatile.Write(ref FinalPeers[Index], peer);

                    BNL.Log($"Connecting: {name} ({identity.Did})");
                }

                await Task.Delay(1, cts.Token);
            }
        }
        public async Task ReconnectClientAsync(int index)
        {
            if (index < 0 || index >= FinalClients.Length) return;

            var oldClient = Volatile.Read(ref FinalClients[index]);

            if (oldClient?.listener != null)
            {
                oldClient.listener.PeerDisconnectedEvent -= MessageHandler.OnDisconnect;
            }
            oldClient?.Disconnect();
            BNL.Log($"Disconnected client at index {index}");

            await Task.Delay(3000); // wait before reconnecting

            var name = NameGenerator.GenerateRandomPlayerName();
            var identity = new ConsoleClientIdentity();
            var (avatarBytes, avatarLoadMode) = PickAvatar();

            var readyMessage = new ReadyMessage
            {
                playerMetaDataMessage = new ClientMetaDataMessage
                {
                    playerDisplayName = name,
                    playerUUID = identity.Did,
                    playerPlatform = "Headless",
                },
                clientAvatarChangeMessage = new ClientAvatarChangeMessage
                {
                    byteArray = avatarBytes,
                    loadMode = avatarLoadMode,
                    LocalAvatarIndex = 1,

                },
                localAvatarSyncMessage = new LocalAvatarSyncMessage
                {
                    array = MovementSender.Generate().Message.array,
                    AdditionalAvatarDataSize = 0,
                    LinkedAvatarIndex = 0,
                }
            };

            var netClient = new NetworkClient();
            var peer = netClient.StartClient(ConfigManager.Ip, ConfigManager.Port, readyMessage, _cachedPasswordBytes, CreateConfig(), manualMode: true);

            if (peer != null)
            {
                peer.Tag = identity;
                netClient.listener.NetworkReceiveEvent += (p, r, ch, m) => MessageHandler.OnReceive(identity, p, r, ch, m);
                netClient.listener.PeerDisconnectedEvent += MessageHandler.OnDisconnect;

                Interlocked.Exchange(ref FinalClients[index], netClient);
                Interlocked.Exchange(ref FinalPeers[index], peer);

                BNL.Log($"Reconnected: {name} ({identity.Did}) at index {index}");
            }
        }
        public Task StopClientsAsync()
        {
            if (FinalClients != null)
                foreach (var client in FinalClients) client?.Disconnect();
            return Task.CompletedTask;
        }
        public Configuration CreateConfig()
        {
            Configuration Configuration = new Configuration();
            Basis.Network.Core.BasisTransportConfigStore.Get<Basis.Network.Core.LNLTransportConfig>(
                Basis.Network.Core.BasisNetworkStackRegistry.LiteNetLibId).UseNativeSockets = true;
            Configuration.UseAuthIdentity = true;

            return Configuration;
        }
    }

    public sealed class ConsoleClientIdentity
    {
        private readonly PrivKey _privateKey;

        public volatile bool Authenticated;

        public string Did { get; }

        public ConsoleClientIdentity()
        {
            BasisDIDAuthIdentityClient.ClientKeyCreation(out (PubKey, PrivKey) keys, out Did did);
            _privateKey = keys.Item2;
            Did = did.V;
        }

        public bool TryRespondToChallenge(NetPacketReader reader, out NetDataWriter writer)
        {
            writer = new NetDataWriter();

            BytesMessage challenge = new BytesMessage();
            if (!challenge.Deserialize(reader, out byte[] nonce))
            {
                BNL.LogError("Malformed auth challenge from server");
                return false;
            }

            if (!Ed25519.Sign(_privateKey, new Payload(nonce), out Signature? signature) || signature == null)
            {
                BNL.LogError("Unable to sign auth challenge");
                return false;
            }

            BytesMessage signatureBytes = new BytesMessage();
            BytesMessage fragmentBytes = new BytesMessage();
            signatureBytes.Serialize(writer, signature.V);
            fragmentBytes.Serialize(writer, Encoding.UTF8.GetBytes("N/A"));
            return true;
        }
    }
}
