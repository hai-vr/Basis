using Basis.Network.Core;

namespace BasisNetworkServer.Networking;

public static class HVREventLog
{
    public static void PreLog(string message, ushort senderId)
    {
        if (NetworkServer.AuthenticatedPeers.TryGetValue(senderId, out var peer))
        {
            PreLog(message, peer);
        }
        else
        {
            PreLog(message, senderId, "???", "???");
        }
    }
    
    public static void PreLog(string message, NetPeer peer)
    {
        PreLog(message, peer.Id, DidOrDefault(peer), peer.Address.ToString());
    }

    private static void PreLog(string message, int id, string did, string address)
    {
        BNL.Log($"[EVENT] {message} #{id} ({did}) [{address}]");
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