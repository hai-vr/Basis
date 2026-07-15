using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using System;
using static SerializableBasis;

/// <summary>
/// Decodes one avatar delta on <see cref="BasisNetworkCommons.DeltaAvatarChannel"/> — server-repacked
/// (any quality) or straight from a P2P peer (High) — reconstructs the full pose payload against the
/// player's last keyframe baseline, and feeds the result through the same decode path as a keyframe.
/// Keyframes (and P2P full frames) still arrive on the per-quality avatar channels and are handled by
/// <see cref="BasisNetworkHandleAvatar"/>. Control frames (header bit 7) carry keyframe requests.
///
/// Wire (must match BasisServerReductionSystemEvents.PreSerializeDelta and the P2P splice in
/// BasisP2PManager.BroadcastAvatarViaP2P):
///   [header:1][playerId:1|2][interval:1][sequence:1][baseSeq:1][delta body][additional?]
/// header bits: quality(2) | hasAdditional&lt;&lt;2 | largeId&lt;&lt;3 | control&lt;&lt;7.
/// </summary>
public static class BasisNetworkHandleAvatarDelta
{
    [ThreadStatic] private static byte[] _reconstruct;
    [ThreadStatic] private static ServerSideSyncPlayerMessage _ssm;
    [ThreadStatic] private static bool _ssmInit;

    // Rate limit for DeltaControlKeyframeRequest per sender: dropped deltas keep arriving at the
    // sender's cadence while our baseline is stale, but one reliable request per second is enough
    // (the server re-keys on the first one; the rest would be redundant). Stopwatch ticks — Unity's
    // profile has no Environment.TickCount64.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, long> _lastKeyframeRequestTicks = new();
    private static readonly long KeyframeRequestMinIntervalTicks = System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Asks the server for a fresh keyframe of <paramref name="playerId"/> after a delta had to be
    /// dropped for lack of a matching baseline. With the v42 adaptive keyframe stretch, periodic
    /// keyframes can be seconds apart on idle senders — recovery is request-driven instead.
    /// </summary>
    private static void RequestKeyframeFromServer(ushort playerId)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastKeyframeRequestTicks.TryGetValue(playerId, out long last) && now - last < KeyframeRequestMinIntervalTicks) return;
        _lastKeyframeRequestTicks[playerId] = now;

        // A P2P-linked sender's avatar data bypasses the server, so ask the peer itself.
        if (BasisP2PManager.GetSessionState(playerId) == BasisP2PManager.P2PSessionState.Connected)
        {
            NetDataWriter p2pWriter = new NetDataWriter(true, 1);
            p2pWriter.Put(BasisNetworkCommons.DeltaControlUplinkKeyframeRequest);
            BasisP2PManager.SendDirectTo(playerId, p2pWriter, BasisNetworkCommons.DeltaAvatarChannel, DeliveryMethod.ReliableOrdered);
            return;
        }

        var peer = BasisNetworkConnection.LocalPlayerPeer;
        if (peer == null) return;
        NetDataWriter writer = new NetDataWriter(true, 3);
        writer.Put(BasisNetworkCommons.DeltaControlKeyframeRequest);
        writer.Put(playerId);
        peer.Send(writer, BasisNetworkCommons.DeltaAvatarChannel, DeliveryMethod.ReliableOrdered);
    }

    public static void Handle(NetDataReader reader)
    {
        int wireBytes = reader.AvailableBytes;
        if (!reader.TryGetByte(out byte header)) return;

        if (BasisNetworkCommons.IsDeltaControlHeader(header))
        {
            // Server (or a P2P peer, via the P2P demux) reports our uplink baseline is missing —
            // make the next avatar send a full keyframe.
            if (header == BasisNetworkCommons.DeltaControlUplinkKeyframeRequest)
            {
                BasisNetworkAvatarCompressor.ForceUplinkKeyframe();
            }
            return;
        }

        byte quality = BasisNetworkCommons.DeltaHeaderQuality(header);
        var q = (BasisAvatarBitPacking.BitQuality)quality;
        if (!BasisAvatarBitPacking.IsValidQuality(q)) return;
        bool hasAdditional = BasisNetworkCommons.DeltaHeaderHasAdditionalData(header);
        bool largeId = BasisNetworkCommons.DeltaHeaderLargeId(header);

        ushort playerId;
        if (largeId)
        {
            if (!reader.TryGetUShort(out playerId)) return;
        }
        else
        {
            if (!reader.TryGetByte(out byte b)) return;
            playerId = b;
        }

        if (!reader.TryGetByte(out byte interval)) return;
        if (!reader.TryGetByte(out byte sequence)) return;
        if (!reader.TryGetByte(out byte baseSeq)) return;

        if (!BasisNetworkPlayers.RemotePlayerReceivers.TryGetValue(playerId, out BasisNetworkReceiver player))
            return;

        // Drop the delta unless we hold the exact keyframe baseline it references (quality + baseSeq).
        // A keyframe re-baselines us; ask for one instead of waiting out the (possibly stretched)
        // periodic cadence — this is the correctness guarantee over unreliable UDP.
        if (!player.TryGetKeyframeBaseline(quality, baseSeq, out byte[] baseline))
        {
            RequestKeyframeFromServer(playerId);
            return;
        }

        int bodyLen = BasisAvatarDeltaCompression.DeltaBodyLength(reader.RawData, reader.Position, reader.AvailableBytes, q);
        if (bodyLen < 0 || bodyLen > reader.AvailableBytes) return;

        int payloadSize = BasisAvatarDeltaCompression.PayloadSize(q);
        byte[] recon = _reconstruct;
        if (recon == null || recon.Length < payloadSize)
        {
            recon = new byte[Math.Max(payloadSize, 256)];
            _reconstruct = recon;
        }

        if (!BasisAvatarDeltaCompression.TryApplyDelta(baseline, reader.RawData, reader.Position, bodyLen, q, recon))
            return;
        reader.SkipBytes(bodyLen);

        if (!_ssmInit)
        {
            _ssm = new ServerSideSyncPlayerMessage();
            _ssmInit = true;
        }
        ServerSideSyncPlayerMessage ssm = _ssm;
        ssm.playerIdMessage.playerID = playerId;
        ssm.interval = interval;
        ssm.sequence = sequence;
        ssm.avatarSerialization.array = recon;
        ssm.avatarSerialization.DataQualityLevel = quality;
        ssm.avatarSerialization.AdditionalAvatarDatas = null;
        ssm.avatarSerialization.AdditionalAvatarDataSize = 0;
        if (hasAdditional)
            ssm.avatarSerialization.DeserializeAdditionalData(reader);
        _ssm = ssm;

        player.AccountReceivedBytes(wireBytes);
        BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
    }
}
