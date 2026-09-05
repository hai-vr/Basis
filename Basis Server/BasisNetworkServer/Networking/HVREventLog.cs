using System;
using System.IO;
using System.Text.Json;
using Basis.Network.Core;
using BasisPermissions;

namespace BasisNetworkServer.Networking;

public static class HVREventLog
{
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event_log.jsonl");

    private struct LogEntry
    {
        public string Timestamp { get; set; }
        public string EventType { get; set; }
        public string FullName { get; set; }
        public string SimplifiedName { get; set; }
        public string DID { get; set; }
    }

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

    public static void Rec_UserJoined(NetPeer peer)
    {
        LogEvent(peer, "UserJoined");
    }

    public static void Rec_UserLeft(NetPeer peer)
    {
        LogEvent(peer, "UserLeft");
    }

    private static void LogEvent(NetPeer peer, string eventType)
    {
        try
        {
            string did = DidOrDefault(peer);
            string fullName = "???";
            string simplifiedName = "???";

            if (did != "???" && PermissionManager.PermissionIntegration.TryGetPlayerMeta(did, out var meta))
            {
                fullName = meta.playerDisplayName;
                simplifiedName = BasisDisplayNameSanitizer.LimitUsername(fullName);
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                EventType = eventType,
                FullName = fullName,
                SimplifiedName = simplifiedName,
                DID = did
            };

            string jsonLine = JsonSerializer.Serialize(entry);
            File.AppendAllLines(LogFilePath, new[] { jsonLine });
        }
        catch (Exception ex)
        {
            BNL.LogError($"Failed to write to event log: {ex.Message}");
        }
    }
}