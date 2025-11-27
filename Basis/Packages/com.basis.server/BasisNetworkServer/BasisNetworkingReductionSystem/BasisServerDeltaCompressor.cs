using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using static SerializableBasis;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public static class BasisServerDeltaCompressor
    {
        private const int AvatarSize = LocalAvatarSyncMessage.AvatarSyncSize;

        // Reuse arrays via shared pool
        private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
        public static ConcurrentDictionary<int, DeltaData> DeltaStorage = new ConcurrentDictionary<int, DeltaData>();

        public class DeltaData
        {
            // 176-byte baseline buffer (rented once, reused)
            public byte[] Baseline;

            // Scratch buffers for indices/values (also rented, reused)
            public byte[] Indices;
            public byte[] Values;

            public bool HasBaseline;
        }

        public static void SendOut(int index, NetPeer peer, ServerSideSyncPlayerMessage tempMsg)
        {
            var data = DeltaStorage.GetOrAdd(index, _ => RentDeltaData());

            var avatar = tempMsg.avatarSerialization.array;
            if (!data.HasBaseline || data.Baseline == null || data.Baseline.Length < AvatarSize)
            {
                // First time: store baseline and send full
                Buffer.BlockCopy(avatar, 0, data.Baseline, 0, AvatarSize);
                data.HasBaseline = true;

                SendOutFull(peer, tempMsg);
                return;
            }

            byte[] baseline = data.Baseline;
            byte[] indices = data.Indices;
            byte[] values = data.Values;

            int changeCount = 0;

            // Single pass: compute changes
            for (int Index = 0; Index < AvatarSize; Index++)
            {
                byte newVal = avatar[Index];
                if (newVal != baseline[Index])
                {
                    indices[changeCount] = (byte)Index;
                    values[changeCount] = newVal;
                    changeCount++;
                }
            }

            int fullCost = AvatarSize;
            int deltaCost = 1 + changeCount * 2; // count + (index,value) * N

            // If no changes, or delta isn't cheaper, send full
            if (changeCount == 0 || deltaCost >= fullCost)
            {
                SendOutFull(peer, tempMsg);

                // Refresh baseline from incoming data
                Buffer.BlockCopy(avatar, 0, baseline, 0, AvatarSize);
                return;
            }

            // Send delta
            SendOutDelta(peer, tempMsg, changeCount, indices, values);

            // Update baseline → now matches what client will reconstruct
            Buffer.BlockCopy(avatar, 0, baseline, 0, AvatarSize);
        }

        private static DeltaData RentDeltaData()
        {
            // We rent arrays slightly >= AvatarSize, no problem as long as we never write past AvatarSize
            return new DeltaData
            {
                Baseline = BytePool.Rent(AvatarSize),
                Indices = BytePool.Rent(AvatarSize),
                Values = BytePool.Rent(AvatarSize),
                HasBaseline = false
            };
        }

        // Call this when a player disconnects so you don’t leak pooled buffers forever
        public static void ReleaseDeltaData(int index)
        {
            if (DeltaStorage.TryRemove(index, out var data))
            {
                if (data.Baseline != null) BytePool.Return(data.Baseline);
                if (data.Indices != null) BytePool.Return(data.Indices);
                if (data.Values != null) BytePool.Return(data.Values);
            }
        }

        private static void SendOutFull(NetPeer peer, ServerSideSyncPlayerMessage msg)
        {
            NetDataWriter writer = BasisServerReductionSystemEvents.RentWriter();

            msg.Serialize(writer);

            peer.Send(writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.ReliableOrdered);
            BasisNetworkStatistics.RecordOutbound(BasisNetworkCommons.PlayerAvatarChannel, writer.Length);
            BasisServerReductionSystemEvents.ReturnWriter(writer);
        }

        private static void SendOutDelta(NetPeer peer,ServerSideSyncPlayerMessage msg,int changeCount,byte[] changedIndices,byte[] changedValues)
        {
            NetDataWriter writer = BasisServerReductionSystemEvents.RentWriter();

            // 1) PlayerIdMessage
            msg.playerIdMessage.Serialize(writer);

            // 2) interval
            writer.Put(msg.interval);

            // 3) delta header + entries
            writer.Put((byte)changeCount);

            for (int i = 0; i < changeCount; i++)
            {
                writer.Put(changedIndices[i]);
                writer.Put(changedValues[i]);
            }

            // 4) AdditionalAvatarData (unchanged from your version)
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

                for (int i = 0; i < additionalSize; i++)
                {
                    lav.AdditionalAvatarDatas[i].Serialize(writer);
                }
            }

            peer.Send(writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
            BasisNetworkStatistics.RecordOutbound(BasisNetworkCommons.PlayerAvatarChannel, writer.Length);
            BasisServerReductionSystemEvents.ReturnWriter(writer);
        }
    }
}
