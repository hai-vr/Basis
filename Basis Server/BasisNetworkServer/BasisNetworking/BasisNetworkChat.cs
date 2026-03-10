using Basis.Network.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using static SerializableBasis;

namespace BasisNetworkServer.BasisNetworking
{
    /// <summary>
    /// Server-side handler for chat messages. Deserializes incoming chat,
    /// applies word filtering, and broadcasts to all other authenticated peers.
    /// </summary>
    public static class BasisNetworkChat
    {
        private static readonly HashSet<string> BlockedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string WordFilterFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Configuration.ConfigFolderName, "chat_word_filter.txt");

        /// <summary>
        /// Loads the word filter list from disk. Each line in the file is a blocked word/phrase.
        /// Creates an empty file if none exists.
        /// </summary>
        public static void LoadWordFilter()
        {
            try
            {
                if (!File.Exists(WordFilterFilePath))
                {
                    // Create empty filter file with instructions
                    string configDir = Path.GetDirectoryName(WordFilterFilePath);
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }
                    File.WriteAllText(WordFilterFilePath,
                        "# Chat word filter - one word or phrase per line\n" +
                        "# Lines starting with # are comments\n" +
                        "# Words are case-insensitive\n");
                    BNL.Log("Created empty chat word filter file: " + WordFilterFilePath);
                    return;
                }

                BlockedWords.Clear();
                string[] lines = File.ReadAllLines(WordFilterFilePath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                    {
                        BlockedWords.Add(trimmed);
                    }
                }
                BNL.Log($"Loaded {BlockedWords.Count} words into chat filter");
            }
            catch (Exception ex)
            {
                BNL.LogError($"Failed to load chat word filter: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the word filter to a message, replacing blocked words with asterisks.
        /// </summary>
        public static string FilterMessage(string message)
        {
            if (BlockedWords.Count == 0 || string.IsNullOrEmpty(message))
            {
                return message;
            }

            foreach (string word in BlockedWords)
            {
                if (string.IsNullOrEmpty(word)) continue;

                string replacement = new string('*', word.Length);
                message = Regex.Replace(message, Regex.Escape(word), replacement, RegexOptions.IgnoreCase);
            }

            return message;
        }

        /// <summary>
        /// Handles an incoming chat message from a client peer.
        /// Deserializes, filters, re-serializes, and broadcasts to all other peers.
        /// </summary>
        public static void HandleChatMessage(NetPacketReader reader, NetPeer sender)
        {
            ChatMessage chatMessage = new ChatMessage();
            chatMessage.Deserialize(reader);
            reader.Recycle();

            // Decode, filter, re-encode
            if (chatMessage.payload != null && chatMessage.payloadSize > 0)
            {
                string text = Encoding.UTF8.GetString(chatMessage.payload, 0, chatMessage.payloadSize);

                // Apply word filter
                text = FilterMessage(text);

                // Truncate if too long after filtering (shouldn't grow, but be safe)
                if (text.Length > 256)
                {
                    text = text.Substring(0, 256);
                }

                byte[] filtered = Encoding.UTF8.GetBytes(text);
                chatMessage.payload = filtered;
                chatMessage.payloadSize = (ushort)filtered.Length;
            }

            // Wrap with sender ID
            ServerChatMessage serverChatMessage = new ServerChatMessage
            {
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = (ushort)sender.Id
                },
                chatMessage = chatMessage
            };

            // Serialize and broadcast to all except sender
            NetDataWriter writer = NetworkServer.RentWriter();
            serverChatMessage.Serialize(writer);
            NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.ChatChannel, sender, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
        }
    }
}
