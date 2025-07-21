
#if !UNITY_SERVER
using Basis.Scripts.Networking.NetworkedAvatar;
using LiteNetLib;
using System;
using System.IO;
using UnityEngine;

namespace Basis.VideoPlayer
{
    [RequireComponent(typeof(BasisVideoPlayer))]
    public class BasisVideoPlayerNetworked : BasisNetworkBehaviour
    {
        private BasisVideoPlayer _videoPlayer;
        private string _lastUrl = string.Empty;

        private enum MessageId : byte
        {
            PLAY_URL,
            STOP,
            REQUESTCURRENT
        }

        [Serializable]
        private struct NetworkMessage
        {
            public string SenderId;
            public string Payload;

            public static byte[] Serialize(byte messageId, string senderId, string payload = "")
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                writer.Write(messageId);
                writer.Write(senderId ?? "");
                writer.Write(payload ?? "");
                return stream.ToArray();
            }

            public static bool TryDeserialize(byte[] buffer, out byte messageId, out NetworkMessage message)
            {
                messageId = 0;
                message = default;

                try
                {
                    using var stream = new MemoryStream(buffer);
                    using var reader = new BinaryReader(stream);
                    if (stream.Length < 1)
                        return false;

                    messageId = reader.ReadByte();
                    string sender = reader.ReadString();
                    string payload = reader.ReadString();

                    message = new NetworkMessage
                    {
                        SenderId = sender,
                        Payload = payload
                    };
                    return true;
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError($"Failed to deserialize network message: {ex.Message}", BasisDebug.LogTag.Video);
                    return false;
                }
            }
        }

        public BasisVideoPlayer VideoPlayer => _videoPlayer ??= GetComponent<BasisVideoPlayer>();

        public string Url
        {
            get => VideoPlayer.Url;
            set => VideoPlayer.Url = value;
        }

        public override void OnNetworkReady()
        {
            if (VideoPlayer == null)
            {
                BasisDebug.LogError($"Missing the video player!", BasisDebug.LogTag.Video);
                enabled = false;
            }
        }
        #region Playback Controls
        public void Play()
        {
            BasisDebug.Log($"Playing from cache: {Url ?? "null"}", BasisDebug.LogTag.Video);
            Play(Url);
        }
        public void Stop()
        {
            VideoPlayer.Pause();
        }
        public void SetVolume(float volume)
        {
            VideoPlayer.SetVolume(volume);
        }
        public void SeekPercent(float seek)
        {
            VideoPlayer.SeekPercent(seek);
        }
        public void Play(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                BasisDebug.LogError("URL is null or empty.", BasisDebug.LogTag.Video);
                return;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult) || !uriResult.IsAbsoluteUri)
            {
                BasisDebug.LogError($"Invalid URL: {url}", BasisDebug.LogTag.Video);
                return;
            }
            if (url == _lastUrl)
            {
                if (VideoPlayer.IsPaused)
                {
                    VideoPlayer.Resume();
                }
                return;
            }
            BasisDebug.Log($"Playing: {url}", BasisDebug.LogTag.Video);
            VideoPlayer.Play(url);
            Url = _lastUrl = url;

            SendUrl(url);
        }
        #endregion
        #region Network Messaging
        public override void OnNetworkMessage(ushort playerId, byte[] buffer, DeliveryMethod deliveryMethod)
        {
            if (!NetworkMessage.TryDeserialize(buffer, out byte messageId, out var message))
                return;

            if (message.SenderId != clientIdentifier)
            {
                BasisDebug.Log($"Message not intended for this client. Expected: {clientIdentifier}, Got: {message.SenderId}", BasisDebug.LogTag.Video);
                return;
            }

            switch ((MessageId)messageId)
            {
                case MessageId.PLAY_URL:
                    BasisDebug.Log($"Playing from network: {message.Payload}", BasisDebug.LogTag.Video);
                    Url = _lastUrl = message.Payload;
                    VideoPlayer.Play(message.Payload);
                    break;

                case MessageId.STOP:
                    VideoPlayer.Pause();
                    break;

                case MessageId.REQUESTCURRENT:
                    if (IsOwnedLocallyOnServer)
                    {
                        BasisDebug.Log("Sending URL to newly connected user.", BasisDebug.LogTag.Video);
                        SendUrl(Url, playerId);
                    }
                    else
                    {
                        BasisDebug.Log("Received REQUESTCURRENT from non-owner.", BasisDebug.LogTag.Video);
                    }
                    break;
            }
        }
        public override void OnOwnershipTransfer(BasisNetworkPlayer newOwner)
        {
            BasisDebug.Log($"Ownership transferred to {clientIdentifier} (Owner: {IsOwnedLocallyOnServer}) for '{CurrentOwnerId}'", BasisDebug.LogTag.Video);
            if (!IsOwnedLocallyOnServer)
            {
                BasisDebug.Log($"Requesting current video from owner {CurrentOwnerId}", BasisDebug.LogTag.Video);
                byte[] buffer = NetworkMessage.Serialize((byte)MessageId.REQUESTCURRENT, clientIdentifier);
                SendCustomNetworkEvent(buffer, DeliveryMethod.ReliableOrdered, new ushort[] { CurrentOwnerId });
            }
        }
        public void SendStop(params ushort[] recipients)
        {
            var buffer = NetworkMessage.Serialize((byte)MessageId.STOP, clientIdentifier);
            SendCustomNetworkEvent(buffer, DeliveryMethod.ReliableOrdered, recipients);
        }
        private void SendUrl(string url, params ushort[] recipients)
        {
            if (recipients.Length == 0)
            {
                recipients = null;
            }

            BasisDebug.Log($"Sending URL to {(recipients == null ? "all" : string.Join(", ", recipients))}: {url}", BasisDebug.LogTag.Video);

            var buffer = NetworkMessage.Serialize((byte)MessageId.PLAY_URL, clientIdentifier, url);
            SendCustomNetworkEvent(buffer, DeliveryMethod.ReliableOrdered, recipients);
        }
        #endregion
    }
}
#endif
