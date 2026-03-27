using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Basis.Network.Core;
using static SerializableBasis;

namespace Basis.Network.Server.Generic
{
    public static class BasisSavedState
    {
        // Thread-safe dictionaries for each type of data
        private static readonly ConcurrentDictionary<int, ClientAvatarChangeMessage> avatarChangeStates = new();
        private static readonly ConcurrentDictionary<int, ClientMetaDataMessage> playerMetaDataMessages = new();
        private static readonly ConcurrentDictionary<int, VoiceReceiversMessage> voiceReceiversMessages = new();
        private static readonly ConcurrentDictionary<int, List<NetPeer>> resolvedVoicePeers = new();
        private static readonly ConcurrentDictionary<int, bool> shoutModeStates = new();

        /// <summary>
        /// Removes all state data for a specific player and purges them
        /// from every other player's cached voice-peer list.
        /// </summary>
        public static void RemovePlayer(int id)
        {
            avatarChangeStates.TryRemove(id, out _);
            playerMetaDataMessages.TryRemove(id, out _);
            voiceReceiversMessages.TryRemove(id, out _);
            resolvedVoicePeers.TryRemove(id, out _);
            shoutModeStates.TryRemove(id, out _);

            // Purge the disconnected peer from all other players' cached lists
            // so voice packets aren't sent to a dead peer until the next recipient update.
            foreach (var kvp in resolvedVoicePeers)
            {
                List<NetPeer> peers = kvp.Value;
                for (int i = peers.Count - 1; i >= 0; i--)
                {
                    if (peers[i].Id == id)
                    {
                        peers.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Adds or updates the ReadyMessage for a player.
        /// </summary>
        public static void AddLastData(NetPeer client, ReadyMessage readyMessage)
        {
            int id = client.Id;
            avatarChangeStates[id] = readyMessage.clientAvatarChangeMessage;
            playerMetaDataMessages[id] = readyMessage.playerMetaDataMessage;

          // BNL.Log($"Updated {id} with AvatarID {readyMessage.clientAvatarChangeMessage.byteArray.Length}");
        }

        /// <summary>
        /// Adds or updates the VoiceReceiversMessage for a player.
        /// Resolves and caches the target NetPeer list so the voice hot path avoids per-packet lookups.
        /// </summary>
        public static void AddLastData(NetPeer client, VoiceReceiversMessage voiceReceiversMessage)
        {
            voiceReceiversMessages[client.Id] = voiceReceiversMessage;

            // Resolve ushort IDs -> NetPeer once here instead of on every voice packet
            var peers = resolvedVoicePeers.GetOrAdd(client.Id, _ => new List<NetPeer>(64));
            peers.Clear();

            if (voiceReceiversMessage.Users != null)
            {
                for (int i = 0; i < voiceReceiversMessage.Users.Length; i++)
                {
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(voiceReceiversMessage.Users[i], out NetPeer found))
                    {
                        peers.Add(found);
                    }
                }
            }
        }

        /// <summary>
        /// Adds or updates the ClientAvatarChangeMessage for a player.
        /// </summary>
        public static void AddLastData(NetPeer client, ClientAvatarChangeMessage avatarChangeMessage)
        {
            avatarChangeStates[client.Id] = avatarChangeMessage;
        }

        /// <summary>
        /// Retrieves the last ClientAvatarChangeMessage for a player.
        /// </summary>
        public static bool GetLastAvatarChangeState(NetPeer client, out ClientAvatarChangeMessage message)
        {
            return avatarChangeStates.TryGetValue(client.Id, out message);
        }

        /// <summary>
        /// Retrieves the last PlayerMetaDataMessage for a player.
        /// </summary>
        public static bool GetLastPlayerMetaData(NetPeer client, out ClientMetaDataMessage message)
        {
            return playerMetaDataMessages.TryGetValue(client.Id, out message);
        }

        /// <summary>
        /// Retrieves the last VoiceReceiversMessage for a player.
        /// </summary>
        public static bool GetLastVoiceReceivers(NetPeer client, out VoiceReceiversMessage message)
        {
            return voiceReceiversMessages.TryGetValue(client.Id, out message);
        }

        /// <summary>
        /// Retrieves the cached resolved peer list for a player's voice receivers.
        /// This list is rebuilt each time the voice receivers message is updated, not per voice packet.
        /// </summary>
        public static bool GetResolvedVoicePeers(NetPeer client, out List<NetPeer> peers)
        {
            return resolvedVoicePeers.TryGetValue(client.Id, out peers);
        }

        /// <summary>
        /// Directly sets the resolved voice peer list for a player.
        /// Used by inverted-list and bitfield modes which resolve peers during deserialization
        /// rather than storing a ushort[] first.
        /// </summary>
        public static List<NetPeer> GetOrCreateResolvedList(int clientId)
        {
            var peers = resolvedVoicePeers.GetOrAdd(clientId, _ => new List<NetPeer>(64));
            peers.Clear();
            return peers;
        }

        /// <summary>
        /// Sets shout mode state for a player.
        /// </summary>
        public static void SetShoutMode(int peerId, bool enabled)
        {
            if (enabled)
            {
                shoutModeStates[peerId] = true;
            }
            else
            {
                shoutModeStates.TryRemove(peerId, out _);
            }
        }

        /// <summary>
        /// Returns true if the player is currently in shout mode.
        /// </summary>
        public static bool IsInShoutMode(int peerId)
        {
            return shoutModeStates.TryGetValue(peerId, out _);
        }

        /// <summary>
        /// Returns all player IDs currently in shout mode.
        /// </summary>
        public static int[] GetAllShoutModePlayers()
        {
            return shoutModeStates.Keys.ToArray();
        }
    }
}
