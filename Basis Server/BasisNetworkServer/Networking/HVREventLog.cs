using Basis.Network.Core;
using BasisPermissions;

namespace BasisNetworkServer.Networking;

public static class HVREventLog
{
    public static string LimitedUsername(NetPeer peer)
    {
        if (NetworkServer.AuthIdentity != null &&
            NetworkServer.AuthIdentity.NetIDToUUID(peer, out string uuid) &&
            PermissionManager.PermissionIntegration.TryGetPlayerMeta(uuid, out var meta))
        {
            return BasisDisplayNameSanitizer.LimitUsername(meta.playerDisplayName);
        }

        return "???";
    }

    public static void ServerLog(string message)
    {
        PreLog(message, -1, "???", "???", "???");
    }

    public static void PreLog(string message, ushort senderId)
    {
        if (NetworkServer.AuthenticatedPeers.TryGetValue(senderId, out var peer))
        {
            PreLog(message, peer);
        }
        else
        {
            PreLog(message, senderId, "???", "???", "???");
        }
    }
    
    public static void PreLog(string message, NetPeer peer)
    {
        PreLog(message, peer.Id, DidOrDefault(peer), peer.Address.ToString(), LimitedUsername(peer));
    }

    private static void PreLog(string message, int id, string did, string address, string username)
    {
        BNL.Log($"[EVENT] {message} #{id} {username} ({did}) [{address}]");
    }

    private static string DidOrDefault(NetPeer peer)
    {
        if (NetworkServer.AuthIdentity.NetIDToUUID(peer, out string did))
        {
            return did;
        }

        return "???";
    }
}