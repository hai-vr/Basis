using Basis.Network.Core;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using System;
using System.Collections.Generic;
using static SerializableBasis;

namespace Basis.Network.Server.Generic
{
    public static class BasisNetworkingGeneric
    {
        [ThreadStatic]
        private static List<NetPeer> _targetedClients;

        private static List<NetPeer> GetTargetedList()
        {
            if (_targetedClients == null) _targetedClients = new List<NetPeer>();
            else _targetedClients.Clear();
            return _targetedClients;
        }

        public static void HandleScene(NetPacketReader Reader, DeliveryMethod DeliveryMethod, NetPeer sender)
        {
            SceneDataMessage SceneDataMessage = new SceneDataMessage();
            SceneDataMessage.Deserialize(Reader);
            Reader.Recycle();
            ServerSceneDataMessage serverSceneDataMessage = new ServerSceneDataMessage
            {
                sceneDataMessage = new RemoteSceneDataMessage()
                {
                    messageIndex = SceneDataMessage.messageIndex,
                    payload = SceneDataMessage.payload
                },
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = (ushort)sender.Id,
                }
            };
            byte Channel = BasisNetworkCommons.SceneChannel;
            NetDataWriter Writer = BasisServerReductionSystemEvents.RentWriter();
            if (DeliveryMethod == DeliveryMethod.Unreliable)
            {
                Writer.Put(Channel);
                Channel = BasisNetworkCommons.FallChannel;
            }
            serverSceneDataMessage.Serialize(Writer);
            if (SceneDataMessage.recipientsSize != 0)
            {
                List<NetPeer> targetedClients = GetTargetedList();

                int recipientsLength = SceneDataMessage.recipientsSize;
                for (int index = 0; index < recipientsLength; index++)
                {
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(SceneDataMessage.recipients[index], out NetPeer client))
                    {
                        targetedClients.Add(client);
                    }
                    else
                    {
                        BNL.Log("Missing Peer! " + SceneDataMessage.recipients[index]);
                    }
                }

                if (targetedClients.Count > 0)
                {
                    NetworkServer.BroadcastMessageToClients(Writer, Channel, ref targetedClients, DeliveryMethod);
                }
            }
            else
            {
                NetworkServer.BroadcastMessageToClients(Writer, Channel, sender, NetworkServer.PeerSnapshot, DeliveryMethod);
            }
            BasisServerReductionSystemEvents.ReturnWriter(Writer);
            serverSceneDataMessage.sceneDataMessage.Release();
        }
        public static void HandleAvatar(NetPacketReader Reader, DeliveryMethod DeliveryMethod, NetPeer sender)
        {
            AvatarDataMessage avatarDataMessage = new AvatarDataMessage();
            avatarDataMessage.Deserialize(Reader);
            Reader.Recycle();
            ServerAvatarDataMessage serverAvatarDataMessage = new ServerAvatarDataMessage
            {
                avatarDataMessage = new RemoteAvatarDataMessage()
                {
                    messageIndex = avatarDataMessage.messageIndex,
                    payload = avatarDataMessage.payload,
                    PlayerIdMessage = avatarDataMessage.PlayerIdMessage,
                    AvatarLinkIndex = avatarDataMessage.AvatarLinkIndex,
                },
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = (ushort)sender.Id
                }
            };
            byte Channel = BasisNetworkCommons.AvatarChannel;
            NetDataWriter Writer = BasisServerReductionSystemEvents.RentWriter();
            if (DeliveryMethod == DeliveryMethod.Unreliable)
            {
                Writer.Put(Channel);
                Channel = BasisNetworkCommons.FallChannel;
            }
            serverAvatarDataMessage.Serialize(Writer);
            if (avatarDataMessage.recipientsSize != 0)
            {
                List<NetPeer> targetedClients = GetTargetedList();

                int recipientsLength = avatarDataMessage.recipientsSize;
                for (int index = 0; index < recipientsLength; index++)
                {
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(avatarDataMessage.recipients[index], out NetPeer client))
                    {
                        targetedClients.Add(client);
                    }
                    else
                    {
                        BNL.Log("Missing Peer! " + avatarDataMessage.recipients[index]);
                    }
                }

                if (targetedClients.Count > 0)
                {
                    NetworkServer.BroadcastMessageToClients(Writer, Channel, ref targetedClients, DeliveryMethod);
                }
            }
            else
            {
                NetworkServer.BroadcastMessageToClients(Writer, Channel, sender, NetworkServer.PeerSnapshot, DeliveryMethod);
            }
            BasisServerReductionSystemEvents.ReturnWriter(Writer);
        }
    }
}
