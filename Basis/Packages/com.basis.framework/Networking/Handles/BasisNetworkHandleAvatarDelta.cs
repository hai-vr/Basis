using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using System;
using static SerializableBasis;

/// <summary>
/// Decodes one server-emitted avatar delta on <see cref="BasisNetworkCommons.DeltaAvatarChannel"/>,
/// reconstructs the full pose payload against the player's last keyframe baseline, and feeds the
/// result through the same decode path as a keyframe. Keyframes (and P2P full frames) still arrive on
/// the per-quality avatar channels and are handled by <see cref="BasisNetworkHandleAvatar"/>; only the
/// server ever emits deltas, so the client never has to encode one.
///
/// Wire (must match BasisServerReductionSystemEvents.PreSerializeDelta):
///   [header:1][playerId:1|2][interval:1][sequence:1][baseSeq:1][delta body][additional?]
/// header bits: quality(2) | hasAdditional&lt;&lt;2 | largeId&lt;&lt;3.
/// </summary>
public static class BasisNetworkHandleAvatarDelta
{
    [ThreadStatic] private static byte[] _reconstruct;
    [ThreadStatic] private static ServerSideSyncPlayerMessage _ssm;
    [ThreadStatic] private static bool _ssmInit;

    public static void Handle(NetDataReader reader)
    {
        int wireBytes = reader.AvailableBytes;
        if (!reader.TryGetByte(out byte header)) return;

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
        // The next keyframe will re-baseline us; this is the correctness guarantee over unreliable UDP.
        if (!player.TryGetKeyframeBaseline(quality, baseSeq, out byte[] baseline))
            return;

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
