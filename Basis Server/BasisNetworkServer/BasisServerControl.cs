#if !UNITY_2017_1_OR_NEWER
using Basis.Network.Core;
using BasisNetworkServer.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BasisNetworkCore.Serializable.SerializableBasis;
using static SerializableBasis;

namespace Basis.Network.Server
{
    public enum LoadStrategy : byte
    {
        Immediate    = 0,
        Synchronized = 2,
    }

    public record WorldLoadParams(string Url, string Password, bool Persistent, LoadStrategy Strategy);
    public record SwitchWorldParams(string Url, string Password, bool Persistent, string AnnounceMessage, int Delay);
    public record WorldInfo(string NetId, string Url, bool Persistent, bool AdminLocked, LoadStrategy Strategy);
    public record PlayerInfo(int NetId, string Uuid, string DisplayName, string Platform);

    public interface IServerControl
    {
        void AnnounceAll(string message);
        bool AnnouncePlayer(string uuid, string message);
        string LoadWorld(WorldLoadParams p);
        bool UnloadWorld(string netId);
        int ClearAllWorlds();
        IReadOnlyList<WorldInfo> ListWorlds();
        IReadOnlyList<PlayerInfo> ListPlayers();
        string SwitchWorld(SwitchWorldParams p, CancellationToken cancellationToken = default);
    }

    public sealed class BasisServerControl : IServerControl
    {
        public void AnnounceAll(string message)
        {
            var writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.MessageAll);
                writer.Put(message);
                NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.AdminChannel, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
            }
            finally { NetworkServer.ReturnWriter(writer); }
            BNL.Log($"[Control] Announced to all: {message}");
        }

        public bool AnnouncePlayer(string uuid, string message)
        {
            if (NetworkServer.AuthIdentity == null ||
                !NetworkServer.AuthIdentity.UUIDToNetID(uuid, out int id) ||
                !NetworkServer.AuthenticatedPeers.TryGetValue(id, out var peer))
                return false;
            BasisPlayerModeration.SendBackMessage(peer, message);
            BNL.Log($"[Control] Announced to {uuid}: {message}");
            return true;
        }

        public string LoadWorld(WorldLoadParams p)
        {
            var resource = BuildResource(p.Url, p.Password, p.Persistent, p.Strategy);
            if (p.Strategy == LoadStrategy.Synchronized)
                BasisNetworkPreloadResourceManagement.StartSynchronizedLoad(resource);
            else
                BasisNetworkResourceManagement.LoadResource(resource);
            BNL.Log($"[Control] Load world: {p.Url} strategy={p.Strategy} netId={resource.LoadedNetID}");
            return resource.LoadedNetID;
        }

        public bool UnloadWorld(string netId) =>
            BasisNetworkResourceManagement.UnloadResource(new UnLoadResource { LoadedNetID = netId, Mode = 1 });

        public int ClearAllWorlds()
        {
            var peers  = NetworkServer.PeerSnapshot;
            var writer = NetworkServer.RentWriter();
            int count  = 0;
            try
            {
                var scenes = BasisNetworkResourceManagement.UshortNetworkDatabase.Values
                    .Where(r => r.Mode == 1).ToArray();
                foreach (var scene in scenes)
                {
                    BasisNetworkResourceManagement.UshortNetworkDatabase.TryRemove(scene.LoadedNetID, out _);
                    var unload = new UnLoadResource { LoadedNetID = scene.LoadedNetID, Mode = 1 };
                    writer.Reset();
                    unload.Serialize(writer);
                    NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.UnloadResourceChannel, peers, DeliveryMethod.ReliableOrdered);
                    count++;
                }
            }
            finally { NetworkServer.ReturnWriter(writer); }

            // Reset after removal so any synchronized load that slipped in during the loop
            // has its session cleared rather than left pending.
            BasisNetworkPreloadResourceManagement.Reset();

            var clearWriter = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(clearWriter, AdminRequestMode.ClearAllScenes);
                NetworkServer.BroadcastMessageToClients(clearWriter, BasisNetworkCommons.AdminChannel, peers, DeliveryMethod.ReliableOrdered);
            }
            finally { NetworkServer.ReturnWriter(clearWriter); }

            BNL.Log($"[Control] ClearAllWorlds: unloaded {count} scene(s)");
            return count;
        }

        public IReadOnlyList<WorldInfo> ListWorlds() =>
            BasisNetworkResourceManagement.UshortNetworkDatabase.Values
                .Where(r => r.Mode == 1)
                .Select(r => new WorldInfo(r.LoadedNetID, r.CombinedURL, r.Persist, r.IsAdminLocked, (LoadStrategy)r.LoadStrategy))
                .ToList();

        public IReadOnlyList<PlayerInfo> ListPlayers()
        {
            var result = new List<PlayerInfo>();
            foreach (var kv in NetworkServer.AuthenticatedPeers)
            {
                string uuid = string.Empty;
                NetworkServer.AuthIdentity?.NetIDToUUID(kv.Value, out uuid);
                string displayName = string.Empty, platform = string.Empty;
                if (!string.IsNullOrEmpty(uuid) &&
                    BasisPermissions.PermissionManager.PermissionIntegration.TryGetPlayerMeta(uuid, out var meta))
                {
                    displayName = meta.playerDisplayName ?? string.Empty;
                    platform    = meta.playerPlatform   ?? string.Empty;
                }
                result.Add(new PlayerInfo(kv.Key, uuid, displayName, platform));
            }
            return result;
        }

        public string SwitchWorld(SwitchWorldParams p, CancellationToken cancellationToken = default)
        {
            var resource = BuildResource(p.Url, p.Password, p.Persistent, LoadStrategy.Synchronized);
            if (!string.IsNullOrEmpty(p.AnnounceMessage))
                AnnounceAll(p.AnnounceMessage);
            if (p.Delay > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(p.Delay), cancellationToken);
                        BasisNetworkPreloadResourceManagement.StartSynchronizedLoad(resource);
                        BNL.Log($"[Control] Switch world started (post-delay): {p.Url} netId={resource.LoadedNetID}");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception e)
                    {
                        BNL.LogError($"[Control] Delayed switch world failed: {e}");
                    }
                });
            }
            else
            {
                BasisNetworkPreloadResourceManagement.StartSynchronizedLoad(resource);
            }
            BNL.Log($"[Control] Switch world queued: {p.Url} netId={resource.LoadedNetID} delay={p.Delay}s");
            return resource.LoadedNetID;
        }

        private static LocalLoadResource BuildResource(string url, string password, bool persistent, LoadStrategy strategy) =>
            new LocalLoadResource
            {
                LoadedNetID    = Guid.NewGuid().ToString("N"),
                Mode           = 1,
                CombinedURL    = url,
                UnlockPassword = password,
                UUIDOfCreator  = "server",
                IsAdminLocked  = true,
                Persist        = persistent,
                LoadStrategy   = (byte)strategy,
            };
    }
}
#endif
