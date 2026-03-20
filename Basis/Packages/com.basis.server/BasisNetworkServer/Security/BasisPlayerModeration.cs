using Basis.Network.Core;
using BasisNetworkCore;
using BasisPermissions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using static BasisNetworkCore.Serializable.SerializableBasis;
using static BasisPermissions.PermissionManager;

namespace BasisNetworkServer.Security
{
    public static class BasisPlayerModeration
    {
        private static readonly ConcurrentDictionary<string, BannedPlayer> BannedPlayers = new ConcurrentDictionary<string, BannedPlayer>();
        private static readonly ConcurrentDictionary<string, byte> BannedUUIDs = new();
        private static readonly string BanFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Configuration.ConfigFolderName, "banned_players.xml");
        public static bool UseFileOnDisc = true;
        public class BannedPlayer
        {
            public string UUID { get; set; }
            public string BannedIp { get; set; }
            public string Reason { get; set; }
            public bool HasBannedIp { get; set; }
            public string TimeOfBan { get; set; }
        }
        public static bool GetBannedReason(string UUID, out string Reason)
        {
            if (BannedPlayers.TryGetValue(UUID, out BannedPlayer Player))
            {
                Reason = Player.Reason;
                return true;
            }
            else
            {
                Reason = string.Empty;
                return false;
            }
        }
        public static bool IsIpBanned(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                throw new ArgumentException("[Error] IP address cannot be null or empty.");

            return BannedPlayers.Values.Any(bp => bp.HasBannedIp && bp.BannedIp == ip);
        }
        public static void SaveBannedPlayers()
        {
            try
            {
                if (UseFileOnDisc)
                {
                    using FileStream fs = new(BanFilePath, FileMode.Create);
                    new XmlSerializer(typeof(List<BannedPlayer>)).Serialize(fs, BannedPlayers.Values.ToList());
                }
            }
            catch (Exception ex)
            {
                BNL.LogError($"[Error] Failed to save banned players: {ex.Message}");
            }
        }

        public static void LoadBannedPlayers()
        {
            if (File.Exists(BanFilePath) == false)
            {
                SaveBannedPlayers();
            }
            try
            {
                List<BannedPlayer> loadedList = new List<BannedPlayer>();
                if (UseFileOnDisc)
                {
                    using FileStream fs = new(BanFilePath, FileMode.Open);
                    var serializer = new XmlSerializer(typeof(List<BannedPlayer>));
                    loadedList = (List<BannedPlayer>)serializer.Deserialize(fs);
                }
                BannedPlayers.Clear();
                BannedUUIDs.Clear();

                foreach (var player in loadedList)
                {
                    BannedPlayers[player.UUID] = player;
                    BannedUUIDs.TryAdd(player.UUID, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to load banned players: {ex.Message}");
            }
        }

        public static string Ban(string UUID, string reason)
        {
            if (string.IsNullOrEmpty(UUID))
                return "[Error] UUID cannot be null or empty.";
            if (string.IsNullOrEmpty(reason))
                return "[Error] Reason cannot be null or empty.";

            if (!NetworkServer.AuthIdentity.UUIDToNetID(UUID, out int peer))
            {
                return $"[Error] Unable to find player: {UUID}";
            }
            if (!NetworkServer.AuthenticatedPeers.TryGetValue(peer, out var peers) || peers == null)
            {
                return $"[Error] Peer not found for player: {UUID}";
            }

            peers.Disconnect(Encoding.UTF8.GetBytes(reason));

            if (BannedUUIDs.ContainsKey(UUID))
            {
                BannedUUIDs.TryRemove(UUID, out _);
            }

            BannedPlayer bannedPlayer = new BannedPlayer
            {
                UUID = UUID,
                Reason = reason,
                HasBannedIp = false,
                TimeOfBan = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                BannedIp = string.Empty
            };

            BannedPlayers[UUID] = bannedPlayer;
            BannedUUIDs.TryAdd(UUID, 0);
            SaveBannedPlayers();

            return $"Player {UUID} banned successfully for reason: {reason}";
        }

        public static string IpBan(string UUID, string reason)
        {
            if (string.IsNullOrEmpty(UUID))
                return "[Error] UUID cannot be null or empty.";
            if (string.IsNullOrEmpty(reason))
                return "[Error] Reason cannot be null or empty.";

            if (!NetworkServer.AuthIdentity.UUIDToNetID(UUID, out int peer))
                return $"[Error] Unable to find player: {UUID}";

            if (!NetworkServer.AuthenticatedPeers.TryGetValue(peer, out var peers) || peers == null)
            {
                return $"[Error] Peer not found for player: {UUID}";
            }

            peers.Disconnect(Encoding.UTF8.GetBytes(reason));
            string ip = peers.Address.ToString();

            if (BannedUUIDs.ContainsKey(UUID))
                return $"[Info] Player {UUID} is already banned.";

            BannedPlayer bannedPlayer = new BannedPlayer
            {
                UUID = UUID,
                BannedIp = ip,
                Reason = reason,
                HasBannedIp = true,
                TimeOfBan = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            BannedPlayers[UUID] = bannedPlayer;
            BannedUUIDs.TryAdd(UUID, 0);
            SaveBannedPlayers();

            return $"Player {UUID} and IP {ip} banned successfully for reason: {reason}";
        }

        public static string Kick(string UUID, string reason)
        {
            if (string.IsNullOrEmpty(UUID))
                return "[Error] UUID cannot be null or empty.";
            if (string.IsNullOrEmpty(reason))
                return "[Error] Reason cannot be null or empty.";

            if (!NetworkServer.AuthIdentity.UUIDToNetID(UUID, out int peer))
                return $"[Error] Unable to find player: {UUID}";

            if (!NetworkServer.AuthenticatedPeers.TryGetValue(peer, out var peers) || peers == null)
            {
                return $"[Error] Peer not found for player: {UUID}";
            }

            peers.Disconnect(Encoding.UTF8.GetBytes(reason));
            return $"Player {UUID} kicked successfully.";
        }

        public static bool IsBanned(string UUID)
        {
            if (string.IsNullOrEmpty(UUID))
                throw new ArgumentException("[Error] UUID cannot be null or empty.");

            return BannedUUIDs.ContainsKey(UUID);
        }

        public static bool Unban(string UUID)
        {
            if (string.IsNullOrEmpty(UUID))
                throw new ArgumentException("[Error] UUID cannot be null or empty.");

            if (!BannedUUIDs.ContainsKey(UUID))
                return false;

            BannedPlayers.TryRemove(UUID, out _);
            BannedUUIDs.TryRemove(UUID, out _);
            SaveBannedPlayers();
            return true;
        }

        public static bool UnbanIp(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                throw new ArgumentException("[Error] IP address cannot be null or empty.");

            var toRemoveList = BannedPlayers.Values.Where(bp => bp.HasBannedIp && bp.BannedIp == ip).ToList();
            if (!toRemoveList.Any())
                return false;

            foreach (var player in toRemoveList)
            {
                BannedPlayers.TryRemove(player.UUID, out _);
                BannedUUIDs.TryRemove(player.UUID, out _);
            }

            SaveBannedPlayers();
            return true;
        }
        public static void CheckIsAdmin(NetPeer peer)
        {
            bool IsPeerAdmin = false;
            if (NetworkServer.AuthIdentity.NetIDToUUID(peer, out string UUID))
            {
                if (NetworkServer.AuthIdentity.IsNetPeerAdmin(UUID))
                {
                    IsPeerAdmin = true;
                }
                else
                {
                    IsPeerAdmin = false;
                }
            }
            else
            {
                IsPeerAdmin = false;
            }
            NetDataWriter Writer = NetworkServer.RentWriter();
            Writer.Put(IsPeerAdmin);
            NetworkServer.TrySend(peer, Writer, BasisNetworkCommons.ServerIsAdminChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(Writer);
        }
        public static void OnAdminMessage(NetPeer peer, NetPacketReader reader)
        {
            if (!NetworkServer.AuthIdentity.NetIDToUUID(peer, out string UUID))
            {
                string msg = $"Netpeer was not in database {peer.Address}";
                BNL.LogError(msg);
                SendBackMessage(peer, msg);
                return;
            }

            AdminRequest AdminRequest = new AdminRequest();
            AdminRequest.Deserialize(reader);
            AdminRequestMode Mode = AdminRequest.GetAdminRequestMode();

            // GetPermissions is allowed for all authenticated users (read-only view)
            if (Mode == AdminRequestMode.GetPermissions)
            {
                HandleGetPermissions(peer, UUID);
                reader.Recycle();
                return;
            }

            // All other admin operations require admin privileges
            if (!NetworkServer.AuthIdentity.IsNetPeerAdmin(UUID))
            {
                string msg = $"Was not admin! {UUID}";
                BNL.LogError(msg);
                SendBackMessage(peer, msg);
                reader.Recycle();
                return;
            }

            switch (Mode)
            {
                case AdminRequestMode.Ban:
                    string ReturnMessage = Ban(reader.GetString(), reader.GetString());
                    SendBackMessage(peer, ReturnMessage);
                    break;
                case AdminRequestMode.Kick:
                    ReturnMessage = Kick(reader.GetString(), reader.GetString());
                    SendBackMessage(peer, ReturnMessage);
                    break;
                case AdminRequestMode.IpAndBan:
                    ReturnMessage = IpBan(reader.GetString(), reader.GetString());
                    SendBackMessage(peer, ReturnMessage);
                    break;
                case AdminRequestMode.Message:
                    ushort RPI = reader.GetUShort();
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(RPI, out NetPeer RemotePeer))
                    {
                        string messagedata = reader.GetString();
                        SendBackMessage(RemotePeer, messagedata);
                        BNL.Log($"sending Message | {messagedata}");
                    }
                    break;
                case AdminRequestMode.MessageAll:
                    NetDataWriter Writer = NetworkServer.RentWriter();
                    AdminRequest OutAdminRequest = new AdminRequest();
                    OutAdminRequest.Serialize(Writer, AdminRequestMode.MessageAll);
                    string Message = reader.GetString();
                    Writer.Put(Message);
                    NetworkServer.BroadcastMessageToClients(Writer, BasisNetworkCommons.AdminChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
                    NetworkServer.ReturnWriter(Writer);
                    BNL.Log($"sending MessageAll | {Message}");
                    break;
                case AdminRequestMode.UnBanIP:
                    if (UnbanIp(reader.GetString()))
                    {
                        ReturnMessage = "Successfully Unbanned";
                    }
                    else
                    {
                        ReturnMessage = "failed to unban no ban existed!";
                    }
                    SendBackMessage(peer, ReturnMessage);
                    break;
                case AdminRequestMode.UnBan:
                    if (Unban(reader.GetString()))
                    {
                        ReturnMessage = "Successfully Unbanned";
                    }
                    else
                    {
                        ReturnMessage = "failed to unban";
                    }
                    SendBackMessage(peer, ReturnMessage);
                    break;
                case AdminRequestMode.TeleportAll:

                    Writer = NetworkServer.RentWriter();
                    OutAdminRequest = new AdminRequest();
                    OutAdminRequest.Serialize(Writer, AdminRequestMode.TeleportAll);
                    ushort PlayerDestination = reader.GetUShort();
                    Writer.Put(PlayerDestination);
                    NetworkServer.BroadcastMessageToClients(Writer, BasisNetworkCommons.AdminChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
                    NetworkServer.ReturnWriter(Writer);
                    BNL.Log($"sending TeleportAll destination is NetID {PlayerDestination}");
                    break;
                case AdminRequestMode.AddAdmin:
                    string AddingAdmin = reader.GetString();
                    if (NetworkServer.AuthIdentity.AddNetPeerAsAdmin(AddingAdmin))
                    {
                        SendBackMessage(peer, $"Added Admin {AddingAdmin}");
                    }
                    else
                    {
                        SendBackMessage(peer, $"Failed to Added Admin {AddingAdmin}");
                    }
                    break;
                case AdminRequestMode.RemoveAdmin:
                    string RemoveAdmin = reader.GetString();
                    if (NetworkServer.AuthIdentity.RemoveNetPeerAsAdmin(RemoveAdmin))
                    {
                        SendBackMessage(peer, $"Removing Admin {RemoveAdmin}");
                    }
                    else
                    {
                        SendBackMessage(peer, $"Failed to Remove Admin {RemoveAdmin}");
                    }
                    break;
                case AdminRequestMode.TeleportPlayer:
                    Writer = NetworkServer.RentWriter();
                    OutAdminRequest = new AdminRequest();
                    OutAdminRequest.Serialize(Writer, AdminRequestMode.TeleportPlayer);
                    PlayerDestination = reader.GetUShort();
                    Writer.Put(PlayerDestination);

                    NetworkServer.TrySend(peer, Writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
                    NetworkServer.ReturnWriter(Writer);
                    break;

                // --- Permission management ---
                case AdminRequestMode.SetUserGroup:
                    HandleSetUserGroup(peer, reader);
                    break;
                case AdminRequestMode.SetUserNode:
                    HandleSetUserNode(peer, reader);
                    break;
                case AdminRequestMode.SetGroupNode:
                    HandleSetGroupNode(peer, reader);
                    break;
                case AdminRequestMode.CreateGroup:
                    HandleCreateGroup(peer, reader);
                    break;
                case AdminRequestMode.DeleteGroup:
                    HandleDeleteGroup(peer, reader);
                    break;
                case AdminRequestMode.SetGroupParent:
                    HandleSetGroupParent(peer, reader);
                    break;

                case AdminRequestMode.EnableShoutMode:
                    HandleShoutMode(peer, reader, true);
                    break;
                case AdminRequestMode.DisableShoutMode:
                    HandleShoutMode(peer, reader, false);
                    break;

                default:
                    BNL.LogError("Missing Mode!");
                    ReturnMessage = "Missing mode";
                    SendBackMessage(peer, ReturnMessage);
                    break;
            }
            reader.Recycle();
        }

        #region Permission Handlers

        /// <summary>
        /// Serializes the full permission store snapshot and sends it back to the requesting peer.
        /// Any authenticated user can call this (read-only).
        /// Also includes a bool indicating whether the requesting user is an admin.
        /// </summary>
        private static void HandleGetPermissions(NetPeer peer, string requestingUUID)
        {
            PermissionStore snapshot = PermissionIntegration.Manager.Snapshot();
            bool isAdmin = NetworkServer.AuthIdentity.IsNetPeerAdmin(requestingUUID);

            NetDataWriter writer = NetworkServer.RentWriter();
            AdminRequest outRequest = new AdminRequest();
            outRequest.Serialize(writer, AdminRequestMode.GetPermissions);
            writer.Put(isAdmin);

            // Serialize groups
            writer.Put(snapshot.Groups.Count);
            foreach (var g in snapshot.Groups.Values)
            {
                writer.Put(g.Name);
                writer.Put(g.Nodes.Count);
                foreach (string node in g.Nodes)
                    writer.Put(node);
                writer.Put(g.Parents.Count);
                foreach (string parent in g.Parents)
                    writer.Put(parent);
            }

            // Serialize users
            writer.Put(snapshot.Users.Count);
            foreach (var u in snapshot.Users.Values)
            {
                writer.Put(u.Uuid);
                writer.Put(u.Groups.Count);
                foreach (string group in u.Groups)
                    writer.Put(group);
                writer.Put(u.Nodes.Count);
                foreach (string node in u.Nodes)
                    writer.Put(node);
            }

            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
            BNL.Log($"Sent permission snapshot to {requestingUUID} (admin={isAdmin})");
        }

        private static void HandleSetUserGroup(NetPeer peer, NetPacketReader reader)
        {
            string uuid = reader.GetString();
            string group = reader.GetString();
            bool add = reader.GetBool();

            if (add)
                PermissionIntegration.Manager.AddUserToGroup(uuid, group);
            else
                PermissionIntegration.Manager.RemoveUserFromGroup(uuid, group);

            SendBackMessage(peer, $"{(add ? "Added" : "Removed")} user {uuid} {(add ? "to" : "from")} group '{group}'");
        }

        private static void HandleSetUserNode(NetPeer peer, NetPacketReader reader)
        {
            string uuid = reader.GetString();
            string node = reader.GetString();
            bool add = reader.GetBool();

            if (add)
                PermissionIntegration.Manager.AddUserNode(uuid, node);
            else
                PermissionIntegration.Manager.RemoveUserNode(uuid, node);

            SendBackMessage(peer, $"{(add ? "Added" : "Removed")} node '{node}' {(add ? "to" : "from")} user {uuid}");
        }

        private static void HandleSetGroupNode(NetPeer peer, NetPacketReader reader)
        {
            string groupName = reader.GetString();
            string node = reader.GetString();
            bool add = reader.GetBool();

            if (add)
                PermissionIntegration.Manager.AddGroupNode(groupName, node);
            else
                PermissionIntegration.Manager.RemoveGroupNode(groupName, node);

            SendBackMessage(peer, $"{(add ? "Added" : "Removed")} node '{node}' {(add ? "to" : "from")} group '{groupName}'");
        }

        private static void HandleCreateGroup(NetPeer peer, NetPacketReader reader)
        {
            string groupName = reader.GetString();
            PermissionIntegration.Manager.GetOrCreateGroup(groupName);
            SendBackMessage(peer, $"Created group '{groupName}'");
        }

        private static void HandleDeleteGroup(NetPeer peer, NetPacketReader reader)
        {
            string groupName = reader.GetString();
            if (PermissionIntegration.Manager.DeleteGroup(groupName))
                SendBackMessage(peer, $"Deleted group '{groupName}'");
            else
                SendBackMessage(peer, $"Failed to delete group '{groupName}' (not found)");
        }

        private static void HandleSetGroupParent(NetPeer peer, NetPacketReader reader)
        {
            string groupName = reader.GetString();
            string parentName = reader.GetString();
            bool add = reader.GetBool();

            if (add)
                PermissionIntegration.Manager.AddGroupParent(groupName, parentName);
            else
                PermissionIntegration.Manager.RemoveGroupParent(groupName, parentName);

            SendBackMessage(peer, $"{(add ? "Added" : "Removed")} parent '{parentName}' {(add ? "to" : "from")} group '{groupName}'");
        }

        private static void HandleShoutMode(NetPeer peer, NetPacketReader reader, bool enable)
        {
            ushort targetPlayerId = reader.GetUShort();

            Basis.Network.Server.Generic.BasisSavedState.SetShoutMode(targetPlayerId, enable);
            BasisServerHandle.BasisServerHandleEvents.BroadcastShoutModeState(targetPlayerId, enable);

            string state = enable ? "enabled" : "disabled";
            BNL.Log($"Shout mode {state} for player {targetPlayerId} by admin {peer.Id}");
            SendBackMessage(peer, $"Shout mode {state} for player {targetPlayerId}");
        }

        #endregion

        public static void SendBackMessage(NetPeer Peer, string ReturnMessage)
        {
            if (string.IsNullOrEmpty(ReturnMessage))
            {
                BNL.LogError("trying to send a empty message to client " + Peer.Id);
                return;
            }
            NetDataWriter Writer = NetworkServer.RentWriter();
            AdminRequest OutAdminRequest = new AdminRequest();
            OutAdminRequest.Serialize(Writer, AdminRequestMode.Message);
            Writer.Put(ReturnMessage);
            NetworkServer.TrySend(Peer, Writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(Writer);
        }
    }
}
