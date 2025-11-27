using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using static SerializableBasis;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public static class BasisServerDeltaCompressor
    {
        public class DeltaData
        {
            public ServerSideSyncPlayerMessage LastSyncMessage; // if you still want it
            public byte[] LastAvatarArray;    // 176-byte baseline (LocalAvatarSyncMessage.array)
            public bool HasBaseline;
        }

        // key: index (or you can swap to playerIdMessage.playerID if you prefer)
        public static ConcurrentDictionary<int, DeltaData> DeltaStorage = new ConcurrentDictionary<int, DeltaData>();

        /// <summary>
        /// Entry point: choose full vs delta and send.
        /// </summary>
        public static void SendOut(int index, NetPeer peer, ServerSideSyncPlayerMessage tempMsg)
        {
            var data = DeltaStorage.GetOrAdd(index, _ => new DeltaData());

            // Sanity: avatar array must exist and have correct length
            var avatar = tempMsg.avatarSerialization.array;
            if (avatar == null || avatar.Length != LocalAvatarSyncMessage.AvatarSyncSize)
            {
                // Fallback: just send full as before, no delta.
                SendOutFull(peer, tempMsg);
                data.HasBaseline = false;
                data.LastSyncMessage = tempMsg;
                return;
            }

            if (!data.HasBaseline || data.LastAvatarArray == null || data.LastAvatarArray.Length != LocalAvatarSyncMessage.AvatarSyncSize)
            {
                // No baseline yet: send full and store baseline
                SendOutFull(peer, tempMsg);
                data.LastAvatarArray = (byte[])avatar.Clone();
                data.HasBaseline = true;
                data.LastSyncMessage = tempMsg;
                return;
            }

            // Compute delta vs baseline
            byte[] baseline = data.LastAvatarArray;
            int length = LocalAvatarSyncMessage.AvatarSyncSize;

            // Max changes = 176, and index fits into a byte (0–255)
            byte[] changedIndices = new byte[length];
            byte[] changedValues = new byte[length];
            int changeCount = 0;

            for (int Index = 0; Index < length; Index++)
            {
                if (avatar[Index] != baseline[Index])
                {
                    changedIndices[changeCount] = (byte)Index;
                    changedValues[changeCount] = avatar[Index];
                    changeCount++;
                }
            }

            // Cost model:
            // Full array cost = 176 bytes
            // Delta array cost = 1 (changeCount) + changeCount * 2
            // We only consider array; rest of the message is same in both cases.
            int fullArrayCost = length;
            int deltaArrayCost = 1 + changeCount * 2;

            // Heuristic: if no changes or delta isn't a clear win, send full.
            // You can tweak threshold if you like.
            if (changeCount == 0 || deltaArrayCost >= fullArrayCost)
            {
                SendOutFull(peer, tempMsg);
                // update baseline to the full array we just sent
                Buffer.BlockCopy(avatar, 0, baseline, 0, length);
                data.LastSyncMessage = tempMsg;
                return;
            }

            // Send delta frame
            SendOutDelta(peer, tempMsg, changeCount, changedIndices, changedValues);

            // Update baseline to new avatar bytes (now "last known good")
            Buffer.BlockCopy(avatar, 0, baseline, 0, length);
            data.LastSyncMessage = tempMsg;
        }

        /// <summary>
        /// Old full-frame path, but with a no header for delta just ReliableOrdered.
        /// frameType = 0 => full ServerSideSyncPlayerMessage.
        /// </summary>
        private static void SendOutFull(NetPeer peer, ServerSideSyncPlayerMessage msg)
        {
            NetDataWriter writer = BasisServerReductionSystemEvents.RentWriter();

            // Normal serialization
            msg.Serialize(writer);

            peer.Send(writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.ReliableOrdered);
            BasisNetworkStatistics.RecordOutbound(BasisNetworkCommons.PlayerAvatarChannel, writer.Length);
            BasisServerReductionSystemEvents.ReturnWriter(writer);
        }

        /// <summary>
        /// Send a delta frame for the 176-byte avatar array.
        /// We still send PlayerId + interval + AdditionalAvatarData in a "full" way.
        /// </summary>
        private static void SendOutDelta(NetPeer peer, ServerSideSyncPlayerMessage msg, int changeCount, byte[] changedIndices, byte[] changedValues)
        {
            NetDataWriter writer = BasisServerReductionSystemEvents.RentWriter();

            // 1) PlayerIdMessage
            msg.playerIdMessage.Serialize(writer);

            // 2) interval
            writer.Put(msg.interval);

            // 3) delta for avatarSerialization.array (176 bytes)
            writer.Put((byte)changeCount); // changeCount fits in byte (max 176)
            for (int Index = 0; Index < changeCount; Index++)
            {
                writer.Put(changedIndices[Index]); // index in [0..175]
                writer.Put(changedValues[Index]);  // value
            }

            // 4) AdditionalAvatarData section (same logic as LocalAvatarSyncMessage.Serialize minus array)
            LocalAvatarSyncMessage lav = msg.avatarSerialization;

            if (lav.AdditionalAvatarDatas == null || lav.AdditionalAvatarDatas.Length == 0 || lav.AdditionalAvatarDatas.Length > 256)
            {
                writer.Put((byte)0);
            }
            else
            {
                byte additionalSize = (byte)lav.AdditionalAvatarDatas.Length;
                writer.Put(additionalSize);

                if (additionalSize != 0)
                {
                    writer.Put(lav.LinkedAvatarIndex);
                }

                for (int Index = 0; Index < additionalSize; Index++)
                {
                    AdditionalAvatarData aad = lav.AdditionalAvatarDatas[Index];
                    aad.Serialize(writer);
                }
            }
         //   BNL.Log($"Delta Is {writer.Length}");
            peer.Send(writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
            BasisNetworkStatistics.RecordOutbound(BasisNetworkCommons.PlayerAvatarChannel, writer.Length);
            BasisServerReductionSystemEvents.ReturnWriter(writer);
        }
    }
}
