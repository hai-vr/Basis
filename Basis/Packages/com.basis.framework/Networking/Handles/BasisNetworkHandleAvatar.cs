using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Profiler;
using System;
using System.Collections.Concurrent;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static Basis.Network.Core.Compression.AvatarDeltaCompression;
using static SerializableBasis;
public static class BasisNetworkHandleAvatar
{
    public static ConcurrentQueue<ServerSideSyncPlayerMessage> Message = new ConcurrentQueue<ServerSideSyncPlayerMessage>();

    // Baseline per player (payload bytes only). Size depends on DataQualityLevel.
    private static readonly ConcurrentDictionary<ushort, byte[]> AvatarBaselines = new ConcurrentDictionary<ushort, byte[]>();

    // Track which quality the baseline was built for (so we can decide whether to refresh).
    private static readonly ConcurrentDictionary<ushort, byte> BaselineQuality = new ConcurrentDictionary<ushort, byte>();

    public static void HandleAvatarUpdate(NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        HandleFullAvatarUpdate(reader);
    }

    private static void HandleFullAvatarUpdate(NetPacketReader reader)
    {
        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerSideSyncPlayer, reader.AvailableBytes);

        if (!Message.TryDequeue(out ServerSideSyncPlayerMessage ssm))
            ssm = new ServerSideSyncPlayerMessage();

        // Normal full deserialize – matches ServerSideSyncPlayerMessage.Serialize on server
        ssm.Deserialize(reader);

        ushort playerId = ssm.playerIdMessage.playerID;

        // Cache baseline from first "full" message (or refresh if quality changed; policy below)
        var lav = ssm.avatarSerialization;

        if (lav.array != null)
        {
            var q = (BitQuality)lav.DataQualityLevel;

            if (BasisAvatarBitPacking.IsValidQuality(q))
            {
                int expectedSize = BasisAvatarBitPacking.ConvertToSize(q);

                if (lav.array.Length >= expectedSize)
                {
                    bool hasBaseline = AvatarBaselines.TryGetValue(playerId, out var existing);
                    bool hasQuality = BaselineQuality.TryGetValue(playerId, out var existingQ);

                    // Always refresh baseline on every keyframe receipt so delta
                    // compression stays in sync with server baselines.
                    if (!hasBaseline || existing == null || existing.Length != expectedSize)
                    {
                        existing = new byte[expectedSize];
                        AvatarBaselines[playerId] = existing;
                    }
                    Buffer.BlockCopy(lav.array, 0, existing, 0, expectedSize);
                    BaselineQuality[playerId] = lav.DataQualityLevel;
                }
            }
        }

        if (BasisNetworkPlayers.RemotePlayers.TryGetValue(playerId, out BasisNetworkReceiver player))
        {
            BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
        }
        else
        {
            // Still keep baseline; player may spawn later.
        }

        Message.Enqueue(ssm);
        TrimQueue();
    }

    private static void TrimQueue()
    {
        if (Message.Count > 256)
        {
            while (Message.TryDequeue(out _)) { }
            BasisDebug.LogError("Messages Exceeded 250! Resetting");
        }
    }

    /// <summary>
    /// Handles a delta-compressed avatar update on DeltaPlayerAvatarChannel.
    /// Wire format: [PlayerID:2][interval:1][quality:1][deltaPayload:M][additionalSize:1][additional...]
    /// deltaPayload = [bitmask:4][changed_chunks:N*8]
    /// </summary>
    public static void HandleDeltaAvatarUpdate(NetPacketReader reader)
    {
        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerSideSyncPlayer, reader.AvailableBytes);

        // Read header: playerID + interval (same layout as keyframe)
        ushort playerId = reader.GetUShort();
        byte interval = reader.GetByte();

        // Read quality byte
        byte qualityByte = reader.GetByte();
        var quality = (BitQuality)qualityByte;

        if (!BasisAvatarBitPacking.IsValidQuality(quality))
        {
            // Unknown quality, skip
            return;
        }

        // Must have a baseline to apply delta against
        if (!AvatarBaselines.TryGetValue(playerId, out byte[] baseline) || baseline == null)
        {
            // No baseline yet — skip delta, wait for next keyframe
            return;
        }

        if (!BaselineQuality.TryGetValue(playerId, out byte baselineQ) || baselineQ != qualityByte)
        {
            // Quality mismatch — skip delta, wait for matching keyframe
            return;
        }

        int expectedSize = BasisAvatarBitPacking.ConvertToSize(quality);
        if (baseline.Length != expectedSize)
        {
            return;
        }

        // Decode XOR delta directly from reader into reconstructed payload
        byte[] reconstructed = new byte[expectedSize];
        AvatarDeltaCompression.DecodeDelta(reader, baseline, reconstructed);

        // Read additional avatar data (same format as keyframe)
        byte additionalCount = reader.GetByte();
        AdditionalAvatarData[] additionalDatas = null;
        byte linkedAvatarIndex = 0;
        if (additionalCount > 0 && additionalCount <= 256)
        {
            linkedAvatarIndex = reader.GetByte();
            additionalDatas = new AdditionalAvatarData[additionalCount];
            for (int i = 0; i < additionalCount; i++)
            {
                additionalDatas[i] = new AdditionalAvatarData();
                additionalDatas[i].Deserialize(reader);
            }
        }

        // Build a full ServerSideSyncPlayerMessage and feed it through the normal path
        if (!Message.TryDequeue(out ServerSideSyncPlayerMessage ssm))
            ssm = new ServerSideSyncPlayerMessage();

        ssm.playerIdMessage.playerID = playerId;
        ssm.interval = interval;
        ssm.avatarSerialization.DataQualityLevel = qualityByte;
        ssm.avatarSerialization.array = reconstructed;
        ssm.avatarSerialization.AdditionalAvatarDataSize = additionalCount;
        ssm.avatarSerialization.AdditionalAvatarDatas = additionalDatas;
        ssm.avatarSerialization.LinkedAvatarIndex = linkedAvatarIndex;

        if (BasisNetworkPlayers.RemotePlayers.TryGetValue(playerId, out BasisNetworkReceiver player))
        {
            BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
        }

        Message.Enqueue(ssm);
        TrimQueue();
    }

    public static void HandleAvatarChangeMessage(NetPacketReader reader)
    {
        ServerAvatarChangeMessage msg = new ServerAvatarChangeMessage();
        msg.Deserialize(reader);

        ushort playerId = msg.uShortPlayerId.playerID;
        if (BasisNetworkPlayers.Players.TryGetValue(playerId, out BasisNetworkPlayer player))
        {
            ((BasisNetworkReceiver)player).ReceiveAvatarChangeRequest(msg);
        }
        else
        {
            BasisDebug.Log("Missing Player For Message " + playerId);
        }
    }

    // Optional accessors if other systems need the baseline
    public static bool TryGetBaseline(ushort playerId, out byte[] baseline, out BitQuality quality)
    {
        baseline = null;
        quality = BitQuality.Medium;

        if (!AvatarBaselines.TryGetValue(playerId, out baseline))
            return false;

        if (!BaselineQuality.TryGetValue(playerId, out byte q))
            return true;

        quality = (BitQuality)q;
        return true;
    }
}
