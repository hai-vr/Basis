using Basis.Network.Core;
using Basis.Network.Server.Generic;
using BasisNetworkServer.BasisNetworking;
using BasisNetworkServer.Security;
using BasisPermissions;
using Xunit;
using static BasisNetworkCore.Serializable.SerializableBasis;
using static SerializableBasis;

namespace BasisServerTests;

[Collection("BasisServer shared network statics")]
public class BasisPlayerRenameTests
{
    private static readonly MapAuthIdentity Identity = new();
    private static int peerIdCounter = 60_000;

    private static (string Uuid, FakeNetPeer Peer) ConnectPlayer(string displayName)
    {
        NetworkServer.AuthIdentity = Identity;
        int id = Interlocked.Increment(ref peerIdCounter);
        string uuid = $"rename-user-{Guid.NewGuid():N}";
        FakeNetPeer peer = new FakeNetPeer(id, "10.9.9.10");
        Identity.Register(uuid, id);
        NetworkServer.AuthenticatedPeers[id] = peer;
        ClientMetaDataMessage meta = new ClientMetaDataMessage { playerUUID = uuid, playerDisplayName = displayName, playerPlatform = "WindowsPlayer" };
        BasisSavedState.AddLastData(peer, new ReadyMessage { playerMetaDataMessage = meta });
        PermissionManager.PermissionIntegration.StorePlayerMeta(uuid, meta);
        NetworkServer.RebuildPeerSnapshot();
        return (uuid, peer);
    }

    private static void RemovePlayer(string uuid, FakeNetPeer peer)
    {
        NetworkServer.AuthenticatedPeers.TryRemove(peer.Id, out _);
        BasisSavedState.RemovePlayer(peer.Id);
        PermissionManager.PermissionIntegration.RemovePlayerMeta(uuid);
        NetworkServer.RebuildPeerSnapshot();
    }

    private static NetPacketReader RenameRequest(FakeNetPeer target, string newName)
    {
        NetDataWriter w = new NetDataWriter();
        new AdminRequest().Serialize(w, AdminRequestMode.RenamePlayer);
        w.Put((ushort)target.Id);
        w.Put(newName);
        byte[] bytes = w.AsReadOnlySpan().ToArray();
        return NetPacketReader.Create(bytes, 0, bytes.Length, () => { });
    }

    private static bool TryReadRename(FakeNetPeer peer, out ushort targetId, out string name, out ushort initiatorId)
    {
        foreach (var sent in peer.Sent)
        {
            NetDataReader r = new NetDataReader(sent.Data);
            AdminRequest req = new AdminRequest();
            req.Deserialize(r);
            if (req.GetAdminRequestMode() != AdminRequestMode.RenamePlayer) continue;
            targetId = r.GetUShort();
            name = r.GetString();
            initiatorId = r.GetUShort();
            return true;
        }
        targetId = 0;
        name = null;
        initiatorId = 0;
        return false;
    }

    private static string StoredName(FakeNetPeer peer) =>
        BasisSavedState.GetLastPlayerMetaData(peer, out ClientMetaDataMessage meta) ? meta.playerDisplayName : null;

    [Fact]
    public void Rename_RequiresTheRenameNode()
    {
        var (adminUuid, adminPeer) = ConnectPlayer("Admin");
        var (targetUuid, targetPeer) = ConnectPlayer("Original");
        PermissionManager perms = PermissionManager.PermissionIntegration.Manager;
        try
        {
            BasisPlayerModeration.OnAdminMessage(adminPeer, RenameRequest(targetPeer, "Renamed"));
            Assert.Equal("Original", StoredName(targetPeer));
            Assert.False(TryReadRename(targetPeer, out _, out _, out _));

            perms.AddUserNode(adminUuid, PermNodes.ModerationRename);
            BasisPlayerModeration.OnAdminMessage(adminPeer, RenameRequest(targetPeer, "Renamed"));
            Assert.Equal("Renamed", StoredName(targetPeer));
            Assert.True(TryReadRename(targetPeer, out _, out _, out _));
        }
        finally
        {
            perms.RemoveUserNode(adminUuid, PermNodes.ModerationRename);
            RemovePlayer(adminUuid, adminPeer);
            RemovePlayer(targetUuid, targetPeer);
        }
    }

    [Fact]
    public void Rename_IsSanitized_StoredForLateJoiners_AndSentToEveryoneConnected()
    {
        var (adminUuid, adminPeer) = ConnectPlayer("Admin");
        var (targetUuid, targetPeer) = ConnectPlayer("Original");
        var (bystanderUuid, bystanderPeer) = ConnectPlayer("Bystander");
        PermissionManager perms = PermissionManager.PermissionIntegration.Manager;
        perms.AddUserNode(adminUuid, PermNodes.ModerationRename);
        try
        {
            BasisPlayerModeration.OnAdminMessage(adminPeer, RenameRequest(targetPeer, "  New​Name\t"));

            Assert.Equal("NewName", StoredName(targetPeer));
            Assert.True(PermissionManager.PermissionIntegration.TryGetPlayerMeta(targetUuid, out ClientMetaDataMessage meta));
            Assert.Equal("NewName", meta.playerDisplayName);

            foreach (FakeNetPeer peer in new[] { adminPeer, targetPeer, bystanderPeer })
            {
                Assert.True(TryReadRename(peer, out ushort targetId, out string name, out ushort initiatorId));
                Assert.Equal((ushort)targetPeer.Id, targetId);
                Assert.Equal("NewName", name);
                Assert.Equal((ushort)adminPeer.Id, initiatorId);
            }
        }
        finally
        {
            perms.RemoveUserNode(adminUuid, PermNodes.ModerationRename);
            RemovePlayer(adminUuid, adminPeer);
            RemovePlayer(targetUuid, targetPeer);
            RemovePlayer(bystanderUuid, bystanderPeer);
        }
    }

    [Fact]
    public void Rename_RefusesBlankNames_AndProtectedTargets()
    {
        var (adminUuid, adminPeer) = ConnectPlayer("Admin");
        var (targetUuid, targetPeer) = ConnectPlayer("Original");
        PermissionManager perms = PermissionManager.PermissionIntegration.Manager;
        perms.AddUserNode(adminUuid, PermNodes.ModerationRename);
        try
        {
            BasisPlayerModeration.OnAdminMessage(adminPeer, RenameRequest(targetPeer, " ​\t"));
            Assert.Equal("Original", StoredName(targetPeer));
            Assert.False(TryReadRename(targetPeer, out _, out _, out _));

            perms.AddUserNode(targetUuid, PermNodes.protection);
            BasisPlayerModeration.OnAdminMessage(adminPeer, RenameRequest(targetPeer, "Renamed"));
            Assert.Equal("Original", StoredName(targetPeer));
            Assert.False(TryReadRename(targetPeer, out _, out _, out _));
        }
        finally
        {
            perms.RemoveUserNode(adminUuid, PermNodes.ModerationRename);
            perms.RemoveUserNode(targetUuid, PermNodes.protection);
            RemovePlayer(adminUuid, adminPeer);
            RemovePlayer(targetUuid, targetPeer);
        }
    }
}
