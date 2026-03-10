using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using System.Text;

/// <summary>
/// Handles sending and receiving chat text messages over the network.
/// Chat text is displayed above the remote player's nameplate.
/// </summary>
public static class BasisNetworkHandleChat
{
    /// <summary>
    /// The message index used for chat messages on the scene data channel.
    /// This must be a unique value not used by other scene message handlers.
    /// </summary>
    public const ushort ChatMessageIndex = 2048;

    /// <summary>
    /// Maximum chat message length in characters to prevent abuse.
    /// </summary>
    public const int MaxMessageLength = 256;

    /// <summary>
    /// How long a chat message stays visible (in seconds) before auto-clearing.
    /// </summary>
    public const float MessageDisplayDuration = 10f;

    private static bool _initialized;

    /// <summary>
    /// Fired on the main thread when a chat message is received from a remote player.
    /// Parameters: senderPlayerId, message text.
    /// </summary>
    public static event Action<ushort, string> OnChatMessageReceived;

    /// <summary>
    /// Registers the chat message handler with the network system.
    /// Safe to call multiple times; only registers once.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        BasisNetworkGenericMessages.RegisterHandler(ChatMessageIndex, HandleIncomingChatMessage);
    }

    /// <summary>
    /// Unregisters the chat message handler.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        BasisNetworkGenericMessages.UnregisterHandler(ChatMessageIndex);
    }

    /// <summary>
    /// Sends a chat message to all connected players.
    /// </summary>
    /// <param name="message">The text message to send.</param>
    public static void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (message.Length > MaxMessageLength)
        {
            message = message.Substring(0, MaxMessageLength);
        }

        byte[] payload = Encoding.UTF8.GetBytes(message);
        BasisNetworkGenericMessages.OnNetworkMessageSend(
            ChatMessageIndex,
            payload,
            DeliveryMethod.ReliableSequenced
        );

        // Also display locally on the local player's nameplate if desired
        if (BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId))
        {
            OnChatMessageReceived?.Invoke(localId, message);
        }
    }

    /// <summary>
    /// Clears the local player's chat message for all remote players.
    /// Sends an empty payload to signal clearing.
    /// </summary>
    public static void ClearChatMessage()
    {
        BasisNetworkGenericMessages.OnNetworkMessageSend(
            ChatMessageIndex,
            Array.Empty<byte>(),
            DeliveryMethod.ReliableSequenced
        );

        if (BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId))
        {
            OnChatMessageReceived?.Invoke(localId, string.Empty);
        }
    }

    private static void HandleIncomingChatMessage(ushort senderPlayerId, byte[] payload, DeliveryMethod deliveryMethod)
    {
        string message = (payload != null && payload.Length > 0)
            ? Encoding.UTF8.GetString(payload)
            : string.Empty;

        if (message.Length > MaxMessageLength)
        {
            message = message.Substring(0, MaxMessageLength);
        }

        // Already on main thread (dispatched by BasisNetworkEvents -> EnqueueOnMainThread)
        OnChatMessageReceived?.Invoke(senderPlayerId, message);
        ApplyChatToNamePlate(senderPlayerId, message);
    }

    private static void ApplyChatToNamePlate(ushort senderPlayerId, string message)
    {
        if (BasisNetworkPlayers.Players.TryGetValue(senderPlayerId, out BasisNetworkPlayer networkPlayer))
        {
            if (networkPlayer.Player is BasisRemotePlayer remotePlayer && remotePlayer.RemoteNamePlate != null)
            {
                remotePlayer.RemoteNamePlate.SetChatText(message);
            }
        }
    }
}
