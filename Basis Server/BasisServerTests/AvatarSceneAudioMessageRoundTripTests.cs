using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using AvatarLoadDataMessage = BasisNetworkCore.Serializable.SerializableBasis.AvatarLoadDataMessage;
using static SerializableBasis;

namespace BasisServerTests;

/// <summary>Shared plumbing for the avatar/scene/audio wire-message round-trip tests below.</summary>
internal static class Wire
{
    public static NetDataReader Reader(NetDataWriter w) => new(w.CopyData());

    public static NetDataReader Empty() => new(Array.Empty<byte>());

    public static byte[] RandomBytes(Random rng, int count)
    {
        byte[] bytes = new byte[count];
        rng.NextBytes(bytes);
        return bytes;
    }

    public static int PayloadSize(BitQuality q) => BasisAvatarBitPacking.ConvertToSize(q);
}

/// <summary>
/// AdditionalAvatarData wire contract: [PayloadSize:1][messageIndex:1][payload:PayloadSize],
/// collapsing to a single 0 byte for null or oversized (>255) arrays.
/// </summary>
public class AdditionalAvatarDataWireTests
{
    [Fact]
    public void AdditionalAvatarData_RoundTrip_PreservesAllFields()
    {
        var rng = new Random(42);
        byte[] payload = Wire.RandomBytes(rng, 32);
        var msg = new AdditionalAvatarData { messageIndex = 7, array = payload };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 32, w.Length);

        var result = default(AdditionalAvatarData);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal((byte)32, result.PayloadSize);
        Assert.Equal((byte)7, result.messageIndex);
        Assert.Equal(payload, result.array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void AdditionalAvatarData_MaxPayload255_RoundTrips()
    {
        var rng = new Random(43);
        byte[] payload = Wire.RandomBytes(rng, 255);
        var msg = new AdditionalAvatarData { messageIndex = 255, array = payload };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(257, w.Length);

        var result = default(AdditionalAvatarData);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((byte)255, result.PayloadSize);
        Assert.Equal((byte)255, result.messageIndex);
        Assert.Equal(payload, result.array);
    }

    [Fact]
    public void AdditionalAvatarData_NullArray_WritesSinglePayloadSizeZeroByte()
    {
        var msg = new AdditionalAvatarData { messageIndex = 9, array = null };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(1, w.Length);
        Assert.Equal((byte)0, w.Data[0]);

        var result = default(AdditionalAvatarData);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal((byte)0, result.PayloadSize);
        Assert.Null(result.array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void AdditionalAvatarData_ArrayOver255_RejectedAsZeroPayload()
    {
        var msg = new AdditionalAvatarData { messageIndex = 3, array = new byte[256] };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(1, w.Length);
        Assert.Equal((byte)0, w.Data[0]);

        var result = default(AdditionalAvatarData);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((byte)0, result.PayloadSize);
        Assert.Null(result.array);
    }

    [Fact]
    public void AdditionalAvatarData_EmptyReader_NoThrow_ZeroFallback()
    {
        var result = default(AdditionalAvatarData);
        var reader = Wire.Empty();
        var ex = Record.Exception(() => result.Deserialize(reader));
        Assert.Null(ex);
        Assert.Equal((byte)0, result.PayloadSize);
        Assert.Null(result.array);
    }

    [Fact]
    public void AdditionalAvatarData_MissingMessageIndex_NoThrow()
    {
        var reader = new NetDataReader(new byte[] { 5 });
        var result = default(AdditionalAvatarData);
        var ex = Record.Exception(() => result.Deserialize(reader));
        Assert.Null(ex);
        Assert.Equal((byte)5, result.PayloadSize);
        Assert.Null(result.array);
    }

    [Fact]
    public void AdditionalAvatarData_TruncatedPayload_NoThrow_ArrayStaysNull()
    {
        var w = new NetDataWriter();
        w.Put((byte)10);
        w.Put((byte)4);
        w.Put(new byte[] { 1, 2, 3 });
        var result = default(AdditionalAvatarData);
        var ex = Record.Exception(() => result.Deserialize(Wire.Reader(w)));
        Assert.Null(ex);
        Assert.Equal((byte)10, result.PayloadSize);
        Assert.Equal((byte)4, result.messageIndex);
        Assert.Null(result.array);
    }

    [Fact]
    public void AdditionalAvatarData_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new AdditionalAvatarData { messageIndex = 12, array = Wire.RandomBytes(new Random(44), 17) };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(AdditionalAvatarData);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// AudioSegmentDataMessage ([seq:1][silence:1][opus bytes = remainder]) and its
/// ServerAudioSegmentMessage wrapper with byte/ushort player-id variants.
/// </summary>
public class AudioSegmentMessageWireTests
{
    [Fact]
    public void AudioSegmentDataMessage_RoundTrip_PreservesAllFields()
    {
        var rng = new Random(7);
        byte[] audio = Wire.RandomBytes(rng, 60);
        var msg = new AudioSegmentDataMessage
        {
            SequenceNumber = 200,
            TotalPlayedInSilence = 3,
            buffer = audio,
            TotalLength = audio.Length,
            LengthUsed = audio.Length,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(62, w.Length);

        var result = default(AudioSegmentDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((byte)200, result.SequenceNumber);
        Assert.Equal((byte)3, result.TotalPlayedInSilence);
        Assert.Equal(audio, result.buffer);
        Assert.Equal(60, result.TotalLength);
        Assert.Equal(60, result.LengthUsed);
    }

    [Fact]
    public void AudioSegmentDataMessage_ZeroLengthSegment_RoundTripsToNullBuffer()
    {
        var msg = new AudioSegmentDataMessage { SequenceNumber = 5, TotalPlayedInSilence = 9, buffer = null, LengthUsed = 0 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2, w.Length);

        var result = default(AudioSegmentDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((byte)5, result.SequenceNumber);
        Assert.Equal((byte)9, result.TotalPlayedInSilence);
        Assert.Null(result.buffer);
        Assert.Equal(0, result.TotalLength);
        Assert.Equal(0, result.LengthUsed);
    }

    [Fact]
    public void AudioSegmentDataMessage_LengthUsed_LimitsWrittenBytes()
    {
        byte[] pooled = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var msg = new AudioSegmentDataMessage { SequenceNumber = 1, TotalPlayedInSilence = 0, buffer = pooled, TotalLength = 10, LengthUsed = 4 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(6, w.Length);

        var result = default(AudioSegmentDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.buffer);
        Assert.Equal(4, result.TotalLength);
        Assert.Equal(4, result.LengthUsed);
    }

    [Fact]
    public void AudioSegmentDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new AudioSegmentDataMessage
        {
            SequenceNumber = 90,
            TotalPlayedInSilence = 2,
            buffer = Wire.RandomBytes(new Random(8), 16),
            TotalLength = 16,
            LengthUsed = 16,
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(AudioSegmentDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Fact]
    public void ServerAudioSegmentMessage_RoundTrip_UShortIdAndAudio()
    {
        var rng = new Random(21);
        byte[] audio = Wire.RandomBytes(rng, 48);
        var msg = new ServerAudioSegmentMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = ushort.MaxValue },
            audioSegmentData = new AudioSegmentDataMessage
            {
                SequenceNumber = 9,
                TotalPlayedInSilence = 1,
                buffer = audio,
                TotalLength = audio.Length,
                LengthUsed = audio.Length,
            },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 2 + 48, w.Length);

        var result = default(ServerAudioSegmentMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(ushort.MaxValue, result.playerIdMessage.playerID);
        Assert.Equal((byte)9, result.audioSegmentData.SequenceNumber);
        Assert.Equal((byte)1, result.audioSegmentData.TotalPlayedInSilence);
        Assert.Equal(audio, result.audioSegmentData.buffer);
        Assert.Equal(48, result.audioSegmentData.LengthUsed);
    }

    [Fact]
    public void ServerAudioSegmentMessage_SmallIdVariant_RoundTrips()
    {
        byte[] audio = Wire.RandomBytes(new Random(22), 20);
        var msg = new ServerAudioSegmentMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 200 },
            audioSegmentData = new AudioSegmentDataMessage { SequenceNumber = 3, TotalPlayedInSilence = 0, buffer = audio, TotalLength = 20, LengthUsed = 20 },
        };
        var w = new NetDataWriter();
        msg.Serialize(w, largeId: false);
        Assert.Equal(1 + 2 + 20, w.Length);

        var result = default(ServerAudioSegmentMessage);
        result.Deserialize(Wire.Reader(w), largeId: false);
        Assert.Equal((ushort)200, result.playerIdMessage.playerID);
        Assert.Equal(audio, result.audioSegmentData.buffer);
    }

    [Fact]
    public void ServerAudioSegmentMessage_LargeIdVariant_RoundTrips()
    {
        byte[] audio = Wire.RandomBytes(new Random(23), 10);
        var msg = new ServerAudioSegmentMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 40000 },
            audioSegmentData = new AudioSegmentDataMessage { SequenceNumber = 8, TotalPlayedInSilence = 4, buffer = audio, TotalLength = 10, LengthUsed = 10 },
        };
        var w = new NetDataWriter();
        msg.Serialize(w, largeId: true);
        Assert.Equal(2 + 2 + 10, w.Length);

        var result = default(ServerAudioSegmentMessage);
        result.Deserialize(Wire.Reader(w), largeId: true);
        Assert.Equal((ushort)40000, result.playerIdMessage.playerID);
        Assert.Equal(audio, result.audioSegmentData.buffer);
    }

    [Fact]
    public void ServerAudioSegmentMessage_ZeroLengthAudio_SmallId_RoundTrips()
    {
        var msg = new ServerAudioSegmentMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 1 },
            audioSegmentData = new AudioSegmentDataMessage { SequenceNumber = 77, TotalPlayedInSilence = 255, buffer = null, LengthUsed = 0 },
        };
        var w = new NetDataWriter();
        msg.Serialize(w, largeId: false);
        Assert.Equal(3, w.Length);

        var result = default(ServerAudioSegmentMessage);
        result.Deserialize(Wire.Reader(w), largeId: false);
        Assert.Equal((ushort)1, result.playerIdMessage.playerID);
        Assert.Equal((byte)77, result.audioSegmentData.SequenceNumber);
        Assert.Equal((byte)255, result.audioSegmentData.TotalPlayedInSilence);
        Assert.Null(result.audioSegmentData.buffer);
    }
}

/// <summary>
/// AvatarDataMessage: [playerID:2][AvatarLinkIndex:1][messageIndex:1][recipientsSize:2][recipients...][payload = remainder].
/// Null recipients means broadcast (size 0 on the wire, empty array after deserialize).
/// </summary>
public class AvatarDataMessageWireTests
{
    [Fact]
    public void AvatarDataMessage_RoundTrip_PreservesAllFields()
    {
        var rng = new Random(11);
        byte[] payload = Wire.RandomBytes(rng, 20);
        ushort[] recipients = { 1, 500, ushort.MaxValue };
        var msg = new AvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 4242 },
            AvatarLinkIndex = 5,
            messageIndex = 77,
            recipients = recipients,
            payload = payload,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 1 + 1 + 2 + 6 + 20, w.Length);

        var result = default(AvatarDataMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal((ushort)4242, result.PlayerIdMessage.playerID);
        Assert.Equal((byte)5, result.AvatarLinkIndex);
        Assert.Equal((byte)77, result.messageIndex);
        Assert.Equal((ushort)3, result.recipientsSize);
        Assert.Equal(recipients, result.recipients);
        Assert.Equal(payload, result.payload);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void AvatarDataMessage_UShortMaxPlayerId_RoundTrips()
    {
        var msg = new AvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = ushort.MaxValue },
            AvatarLinkIndex = 255,
            messageIndex = 255,
            payload = new byte[] { 42 },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);

        var result = default(AvatarDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(ushort.MaxValue, result.PlayerIdMessage.playerID);
        Assert.Equal((byte)255, result.AvatarLinkIndex);
        Assert.Equal((byte)255, result.messageIndex);
        Assert.Equal(new byte[] { 42 }, result.payload);
    }

    [Fact]
    public void AvatarDataMessage_NullRecipients_DeserializesToEmptyArray_PayloadIntact()
    {
        byte[] payload = Wire.RandomBytes(new Random(12), 8);
        var msg = new AvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 3 },
            AvatarLinkIndex = 1,
            messageIndex = 2,
            recipients = null,
            payload = payload,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);

        var result = default(AvatarDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)0, result.recipientsSize);
        Assert.NotNull(result.recipients);
        Assert.Empty(result.recipients);
        Assert.Equal(payload, result.payload);
    }

    [Fact]
    public void AvatarDataMessage_RecipientsOnly_NoPayload_DeserializesToNullPayload()
    {
        // recipientsSize == AvailableBytes / 2 exactly: the size guard boundary must pass.
        ushort[] recipients = { 10, 20, 30 };
        var msg = new AvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 6 },
            AvatarLinkIndex = 0,
            messageIndex = 1,
            recipients = recipients,
            payload = null,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);

        var result = default(AvatarDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(recipients, result.recipients);
        Assert.Null(result.payload);
    }

    [Fact]
    public void AvatarDataMessage_OversizedRecipientsSize_ThrowsArgumentException()
    {
        var w = new NetDataWriter();
        w.Put((ushort)1);  // playerID
        w.Put((byte)0);    // AvatarLinkIndex
        w.Put((byte)0);    // messageIndex
        w.Put((ushort)4);  // recipientsSize claims 4 entries
        w.Put((byte)9);    // but only 1 byte remains
        var reader = Wire.Reader(w);
        var msg = default(AvatarDataMessage);
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void AvatarDataMessage_TruncatedAfterPlayerId_ThrowsArgumentException()
    {
        var w = new NetDataWriter();
        w.Put((ushort)9);
        var reader = Wire.Reader(w);
        var msg = default(AvatarDataMessage);
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void AvatarDataMessage_MissingRecipientsSize_NoThrow_NullFallback()
    {
        var w = new NetDataWriter();
        w.Put((ushort)9);
        w.Put((byte)4);
        w.Put((byte)8);
        var msg = default(AvatarDataMessage);
        var ex = Record.Exception(() => msg.Deserialize(Wire.Reader(w)));
        Assert.Null(ex);
        Assert.Equal((byte)4, msg.AvatarLinkIndex);
        Assert.Equal((byte)8, msg.messageIndex);
        Assert.Null(msg.recipients);
        Assert.Null(msg.payload);
    }

    [Fact]
    public void AvatarDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new AvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 100 },
            AvatarLinkIndex = 2,
            messageIndex = 3,
            recipients = new ushort[] { 7, 8 },
            payload = Wire.RandomBytes(new Random(13), 11),
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(AvatarDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// RemoteAvatarDataMessage: [playerID:2][AvatarLinkIndex:1][messageIndex:1][payload = remainder].
/// </summary>
public class RemoteAvatarDataMessageWireTests
{
    [Fact]
    public void RemoteAvatarDataMessage_RoundTrip_PreservesAllFields()
    {
        byte[] payload = Wire.RandomBytes(new Random(14), 25);
        var msg = new RemoteAvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = ushort.MaxValue },
            AvatarLinkIndex = 9,
            messageIndex = 44,
            payload = payload,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 1 + 1 + 25, w.Length);

        var result = default(RemoteAvatarDataMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal(ushort.MaxValue, result.PlayerIdMessage.playerID);
        Assert.Equal((byte)9, result.AvatarLinkIndex);
        Assert.Equal((byte)44, result.messageIndex);
        Assert.Equal(payload, result.payload);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void RemoteAvatarDataMessage_NoPayload_DeserializesToNull()
    {
        var msg = new RemoteAvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 5 },
            AvatarLinkIndex = 1,
            messageIndex = 2,
            payload = null,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(4, w.Length);

        var result = default(RemoteAvatarDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Null(result.payload);
    }

    [Fact]
    public void RemoteAvatarDataMessage_TruncatedHeader_ThrowsArgumentException()
    {
        var w = new NetDataWriter();
        w.Put((ushort)5);
        w.Put((byte)1); // AvatarLinkIndex present, messageIndex missing
        var reader = Wire.Reader(w);
        var msg = default(RemoteAvatarDataMessage);
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void RemoteAvatarDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new RemoteAvatarDataMessage
        {
            PlayerIdMessage = new PlayerIdMessage { playerID = 321 },
            AvatarLinkIndex = 7,
            messageIndex = 6,
            payload = Wire.RandomBytes(new Random(15), 9),
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(RemoteAvatarDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// SceneDataMessage: [messageIndex:2][recipientsSize:2][recipients...][payload = remainder],
/// same recipients semantics as AvatarDataMessage.
/// </summary>
public class SceneDataMessageWireTests
{
    [Fact]
    public void SceneDataMessage_RoundTrip_PreservesAllFields()
    {
        byte[] payload = Wire.RandomBytes(new Random(16), 16);
        ushort[] recipients = { 2, 40000, ushort.MaxValue };
        var msg = new SceneDataMessage
        {
            messageIndex = ushort.MaxValue,
            recipients = recipients,
            payload = payload,
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 2 + 6 + 16, w.Length);

        var result = default(SceneDataMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal(ushort.MaxValue, result.messageIndex);
        Assert.Equal((ushort)3, result.recipientsSize);
        Assert.Equal(recipients, result.recipients);
        Assert.Equal(payload, result.payload);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void SceneDataMessage_NullRecipientsAndPayload_RoundTrips()
    {
        var msg = new SceneDataMessage { messageIndex = 12, recipients = null, payload = null };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(4, w.Length);

        var result = default(SceneDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)12, result.messageIndex);
        Assert.Equal((ushort)0, result.recipientsSize);
        Assert.NotNull(result.recipients);
        Assert.Empty(result.recipients);
        Assert.Null(result.payload);
    }

    [Fact]
    public void SceneDataMessage_OversizedRecipientsSize_ThrowsArgumentException()
    {
        var w = new NetDataWriter();
        w.Put((ushort)1);    // messageIndex
        w.Put((ushort)1000); // recipientsSize claims 1000 entries
        w.Put((byte)1);      // but only 1 byte remains
        var reader = Wire.Reader(w);
        var msg = default(SceneDataMessage);
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void SceneDataMessage_MissingRecipientsSize_NoThrow_NullFallback()
    {
        var w = new NetDataWriter();
        w.Put((ushort)77);
        var msg = default(SceneDataMessage);
        var ex = Record.Exception(() => msg.Deserialize(Wire.Reader(w)));
        Assert.Null(ex);
        Assert.Equal((ushort)77, msg.messageIndex);
        Assert.Null(msg.recipients);
        Assert.Null(msg.payload);
    }

    [Fact]
    public void SceneDataMessage_EmptyReader_ThrowsArgumentException()
    {
        var msg = default(SceneDataMessage);
        var reader = Wire.Empty();
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void SceneDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new SceneDataMessage
        {
            messageIndex = 900,
            recipients = new ushort[] { 4 },
            payload = Wire.RandomBytes(new Random(17), 5),
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(SceneDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// RemoteSceneDataMessage: [messageIndex:2][payload = remainder]; payloadLength tracks the
/// valid prefix of a possibly pooled/oversized payload buffer on the send side.
/// </summary>
public class RemoteSceneDataMessageWireTests
{
    [Fact]
    public void RemoteSceneDataMessage_RoundTrip_PreservesAllFields()
    {
        byte[] payload = Wire.RandomBytes(new Random(18), 24);
        var msg = new RemoteSceneDataMessage { messageIndex = 700, payload = payload, payloadLength = payload.Length };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(26, w.Length);

        var result = default(RemoteSceneDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)700, result.messageIndex);
        Assert.Equal(payload, result.payload);
        Assert.Equal(24, result.payloadLength);
    }

    [Fact]
    public void RemoteSceneDataMessage_NoPayload_LeavesPayloadNull()
    {
        var msg = new RemoteSceneDataMessage { messageIndex = 8, payload = null, payloadLength = 0 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2, w.Length);

        var result = default(RemoteSceneDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)8, result.messageIndex);
        Assert.Null(result.payload);
        Assert.Equal(0, result.payloadLength);
    }

    [Fact]
    public void RemoteSceneDataMessage_PayloadLength_LimitsWrittenBytes()
    {
        byte[] pooled = { 1, 2, 3, 4, 5, 6, 7, 8 };
        var msg = new RemoteSceneDataMessage { messageIndex = 1, payload = pooled, payloadLength = 5 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(7, w.Length);

        var result = default(RemoteSceneDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result.payload);
        Assert.Equal(5, result.payloadLength);
    }

    [Fact]
    public void RemoteSceneDataMessage_EmptyReader_ThrowsArgumentException()
    {
        var msg = default(RemoteSceneDataMessage);
        var reader = Wire.Empty();
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void RemoteSceneDataMessage_Release_ClearsPayload()
    {
        var msg = new RemoteSceneDataMessage { messageIndex = 2, payload = new byte[] { 1, 2 }, payloadLength = 2 };
        msg.Release();
        Assert.Null(msg.payload);
        Assert.Equal(0, msg.payloadLength);
    }

    [Fact]
    public void RemoteSceneDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new RemoteSceneDataMessage { messageIndex = 31, payload = Wire.RandomBytes(new Random(19), 12), payloadLength = 12 };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(RemoteSceneDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// Server wrapper messages that prepend a ushort player id to an inner message:
/// ServerAvatarDataMessage, ServerSceneDataMessage, ServerAvatarChangeMessage.
/// </summary>
public class ServerCompositeMessageWireTests
{
    [Fact]
    public void ServerAvatarDataMessage_RoundTrip_PreservesNestedFields()
    {
        byte[] payload = Wire.RandomBytes(new Random(24), 10);
        var msg = new ServerAvatarDataMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 77 },
            avatarDataMessage = new RemoteAvatarDataMessage
            {
                PlayerIdMessage = new PlayerIdMessage { playerID = 88 },
                AvatarLinkIndex = 3,
                messageIndex = 9,
                payload = payload,
            },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 2 + 1 + 1 + 10, w.Length);

        var result = default(ServerAvatarDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)77, result.playerIdMessage.playerID);
        Assert.Equal((ushort)88, result.avatarDataMessage.PlayerIdMessage.playerID);
        Assert.Equal((byte)3, result.avatarDataMessage.AvatarLinkIndex);
        Assert.Equal((byte)9, result.avatarDataMessage.messageIndex);
        Assert.Equal(payload, result.avatarDataMessage.payload);
    }

    [Fact]
    public void ServerAvatarDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new ServerAvatarDataMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 1 },
            avatarDataMessage = new RemoteAvatarDataMessage
            {
                PlayerIdMessage = new PlayerIdMessage { playerID = 2 },
                AvatarLinkIndex = 0,
                messageIndex = 1,
                payload = Wire.RandomBytes(new Random(25), 7),
            },
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(ServerAvatarDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Fact]
    public void ServerSceneDataMessage_RoundTrip_PreservesNestedFields()
    {
        byte[] payload = Wire.RandomBytes(new Random(26), 6);
        var msg = new ServerSceneDataMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 4 },
            sceneDataMessage = new RemoteSceneDataMessage { messageIndex = 700, payload = payload, payloadLength = payload.Length },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 2 + 6, w.Length);

        var result = default(ServerSceneDataMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)4, result.playerIdMessage.playerID);
        Assert.Equal((ushort)700, result.sceneDataMessage.messageIndex);
        Assert.Equal(payload, result.sceneDataMessage.payload);
        Assert.Equal(6, result.sceneDataMessage.payloadLength);
    }

    [Fact]
    public void ServerSceneDataMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new ServerSceneDataMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = ushort.MaxValue },
            sceneDataMessage = new RemoteSceneDataMessage { messageIndex = 1, payload = Wire.RandomBytes(new Random(27), 3), payloadLength = 3 },
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(ServerSceneDataMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Fact]
    public void ServerAvatarChangeMessage_RoundTrip_PreservesNestedFields()
    {
        byte[] avatarBytes = Wire.RandomBytes(new Random(28), 12);
        var msg = new ServerAvatarChangeMessage
        {
            uShortPlayerId = new PlayerIdMessage { playerID = 123 },
            clientAvatarChangeMessage = new ClientAvatarChangeMessage
            {
                loadMode = 1,
                byteArray = avatarBytes,
                LocalAvatarIndex = 200,
            },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 1 + 2 + 12 + 1, w.Length);

        var result = default(ServerAvatarChangeMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)123, result.uShortPlayerId.playerID);
        Assert.Equal((byte)1, result.clientAvatarChangeMessage.loadMode);
        Assert.Equal(avatarBytes, result.clientAvatarChangeMessage.byteArray);
        Assert.Equal((byte)200, result.clientAvatarChangeMessage.LocalAvatarIndex);
    }

    [Fact]
    public void ServerAvatarChangeMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new ServerAvatarChangeMessage
        {
            uShortPlayerId = new PlayerIdMessage { playerID = 9 },
            clientAvatarChangeMessage = new ClientAvatarChangeMessage
            {
                loadMode = 0,
                byteArray = Wire.RandomBytes(new Random(29), 5),
                LocalAvatarIndex = 3,
            },
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(ServerAvatarChangeMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// ClientAvatarChangeMessage: [loadMode:1][length:2][bytes][LocalAvatarIndex:1];
/// a zero length round-trips to a null byteArray.
/// </summary>
public class ClientAvatarChangeMessageWireTests
{
    [Fact]
    public void ClientAvatarChangeMessage_RoundTrip_PreservesAllFields()
    {
        byte[] avatarBytes = Wire.RandomBytes(new Random(51), 40);
        var msg = new ClientAvatarChangeMessage { loadMode = 2, byteArray = avatarBytes, LocalAvatarIndex = 254 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(1 + 2 + 40 + 1, w.Length);

        var result = default(ClientAvatarChangeMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal((byte)2, result.loadMode);
        Assert.Equal(avatarBytes, result.byteArray);
        Assert.Equal((byte)254, result.LocalAvatarIndex);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void ClientAvatarChangeMessage_NullByteArray_RoundTrips()
    {
        var msg = new ClientAvatarChangeMessage { loadMode = 1, byteArray = null, LocalAvatarIndex = 7 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(4, w.Length);

        var result = default(ClientAvatarChangeMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((byte)1, result.loadMode);
        Assert.Null(result.byteArray);
        Assert.Equal((byte)7, result.LocalAvatarIndex);
    }

    [Fact]
    public void ClientAvatarChangeMessage_EmptyByteArray_SameBytesAsNull_DeserializesToNull()
    {
        var nullMsg = new ClientAvatarChangeMessage { loadMode = 1, byteArray = null, LocalAvatarIndex = 7 };
        var emptyMsg = new ClientAvatarChangeMessage { loadMode = 1, byteArray = Array.Empty<byte>(), LocalAvatarIndex = 7 };
        var w1 = new NetDataWriter();
        nullMsg.Serialize(w1);
        var w2 = new NetDataWriter();
        emptyMsg.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());

        var result = default(ClientAvatarChangeMessage);
        result.Deserialize(Wire.Reader(w2));
        Assert.Null(result.byteArray);
    }

    [Fact]
    public void ClientAvatarChangeMessage_LengthBeyondAvailable_ThrowsArgumentException()
    {
        var w = new NetDataWriter();
        w.Put((byte)1);    // loadMode
        w.Put((ushort)50); // claims 50 bytes
        w.Put(new byte[] { 1, 2, 3 });
        var reader = Wire.Reader(w);
        var msg = default(ClientAvatarChangeMessage);
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }

    [Fact]
    public void ClientAvatarChangeMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var msg = new ClientAvatarChangeMessage { loadMode = 3, byteArray = Wire.RandomBytes(new Random(52), 21), LocalAvatarIndex = 90 };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(ClientAvatarChangeMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// BasisAvatarCloneRequest/Response (bare ushort), PlayerIdMessage byte/ushort id variants,
/// and the AvatarLoadDataMessage serialize layout.
/// </summary>
public class AvatarCloneAndPlayerIdWireTests
{
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData(ushort.MaxValue)]
    public void BasisAvatarCloneRequest_RoundTrip_BoundaryIds(ushort id)
    {
        var msg = new BasisAvatarCloneRequest { requestingUser = id };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2, w.Length);

        var result = default(BasisAvatarCloneRequest);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(id, result.requestingUser);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void BasisAvatarCloneResponse_RoundTrip_BoundaryIds(ushort id)
    {
        var msg = new BasisAvatarCloneResponse { requestingUser = id };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2, w.Length);

        var result = default(BasisAvatarCloneResponse);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal(id, result.requestingUser);
    }

    [Fact]
    public void PlayerIdMessage_DefaultPath_RoundTripsUShort()
    {
        var msg = new PlayerIdMessage { playerID = 4242 };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2, w.Length);

        var result = default(PlayerIdMessage);
        result.Deserialize(Wire.Reader(w));
        Assert.Equal((ushort)4242, result.playerID);
    }

    [Fact]
    public void PlayerIdMessage_SmallIdVariant_WritesSingleByte()
    {
        var msg = new PlayerIdMessage { playerID = 200 };
        var w = new NetDataWriter();
        msg.Serialize(w, largeId: false);
        Assert.Equal(1, w.Length);
        Assert.Equal((byte)200, w.Data[0]);

        var result = default(PlayerIdMessage);
        result.Deserialize(Wire.Reader(w), largeId: false);
        Assert.Equal((ushort)200, result.playerID);
    }

    [Fact]
    public void PlayerIdMessage_LargeIdVariant_RoundTripsUShortMax()
    {
        var msg = new PlayerIdMessage { playerID = ushort.MaxValue };
        var w = new NetDataWriter();
        msg.Serialize(w, largeId: true);
        Assert.Equal(2, w.Length);

        var result = default(PlayerIdMessage);
        result.Deserialize(Wire.Reader(w), largeId: true);
        Assert.Equal(ushort.MaxValue, result.playerID);
    }

    [Fact]
    public void AvatarLoadDataMessage_SerializeLayout_HeaderSenderSizeThenRawPayload()
    {
        var msg = new AvatarLoadDataMessage
        {
            messageIndex = 4,
            WhoSentUsThis = 777,
            payload = new byte[] { 1, 2, 3, 4, 5 },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);

        var r = Wire.Reader(w);
        Assert.Equal((byte)4, r.GetByte());
        Assert.Equal((ushort)777, r.GetUShort());
        Assert.Equal((ushort)5, r.GetUShort());
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, r.GetRemainingBytes());

        var wNull = new NetDataWriter();
        new AvatarLoadDataMessage { messageIndex = 1, WhoSentUsThis = 2, payload = null }.Serialize(wNull);
        Assert.Equal(5, wNull.Length); // header + sender + size 0, no payload bytes
    }

    [Fact]
    public void AvatarLoadDataMessage_EmptyReader_ThrowsArgumentException()
    {
        var msg = default(AvatarLoadDataMessage);
        var reader = Wire.Empty();
        Assert.Throws<ArgumentException>(() => msg.Deserialize(reader));
    }
}

/// <summary>
/// LocalAvatarSyncMessage: quality-in-payload path ([quality:1][payload][additionalSize:1=0]),
/// channel-derived path (bare payload, optional additional section), and the standalone
/// additional-data section [count:1][linkedIndex:1][entries...].
/// </summary>
public class LocalAvatarSyncMessageWireTests
{
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void LocalAvatarSyncMessage_PayloadPath_RoundTrips(BitQuality q)
    {
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(100 + (int)q), size);
        var msg = new LocalAvatarSyncMessage { array = payload };
        var w = new NetDataWriter();
        msg.Serialize(w, q);
        Assert.Equal(1 + size + 1, w.Length);
        Assert.Equal((byte)q, w.Data[0]);

        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal((byte)q, result.DataQualityLevel);
        Assert.Equal(payload, result.array);
        Assert.Equal((byte)0, result.AdditionalAvatarDataSize);
        Assert.Null(result.AdditionalAvatarDatas);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void LocalAvatarSyncMessage_ChannelPathNoAdditional_WritesBarePayload(BitQuality q)
    {
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(200 + (int)q), size);
        var msg = new LocalAvatarSyncMessage { array = payload };
        var w = new NetDataWriter();
        msg.SerializeForChannel(w, q);
        Assert.Equal(size, w.Length);

        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader, (byte)q, hasAdditionalData: false);
        Assert.Equal((byte)q, result.DataQualityLevel);
        Assert.Equal(payload, result.array);
        Assert.Equal((byte)0, result.AdditionalAvatarDataSize);
        Assert.Null(result.AdditionalAvatarDatas);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void LocalAvatarSyncMessage_ChannelPathWithAdditionalData_RoundTrips()
    {
        var q = BitQuality.Medium;
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(30), size);
        var msg = new LocalAvatarSyncMessage
        {
            array = payload,
            AdditionalAvatarDatas = new[]
            {
                new AdditionalAvatarData { messageIndex = 1, array = new byte[] { 10, 20, 30 } },
                new AdditionalAvatarData { messageIndex = 6, array = new byte[] { 42 } },
            },
            LinkedAvatarIndex = 4,
        };
        var w = new NetDataWriter();
        msg.SerializeForChannel(w, q);
        Assert.Equal(size + 2 + 5 + 3, w.Length); // payload + [count][linked] + entries

        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader, (byte)q, hasAdditionalData: true);
        Assert.Equal(payload, result.array);
        Assert.Equal((byte)2, result.AdditionalAvatarDataSize);
        Assert.Equal((byte)4, result.LinkedAvatarIndex);
        Assert.Equal((byte)1, result.AdditionalAvatarDatas[0].messageIndex);
        Assert.Equal(new byte[] { 10, 20, 30 }, result.AdditionalAvatarDatas[0].array);
        Assert.Equal((byte)6, result.AdditionalAvatarDatas[1].messageIndex);
        Assert.Equal(new byte[] { 42 }, result.AdditionalAvatarDatas[1].array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void LocalAvatarSyncMessage_AdditionalOnlySection_RoundTrips_IncludingNullEntry()
    {
        var msg = new LocalAvatarSyncMessage
        {
            AdditionalAvatarDatas = new[]
            {
                new AdditionalAvatarData { messageIndex = 1, array = new byte[] { 5, 6, 7 } },
                new AdditionalAvatarData { messageIndex = 2, array = null },
                new AdditionalAvatarData { messageIndex = 3, array = new byte[] { 9 } },
            },
            LinkedAvatarIndex = 11,
        };
        var w = new NetDataWriter();
        msg.SerializeAdditionalOnly(w);
        Assert.Equal(2 + 5 + 1 + 3, w.Length); // null entry collapses to a single 0 byte

        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Reader(w);
        result.DeserializeAdditionalData(reader);
        Assert.Equal((byte)3, result.AdditionalAvatarDataSize);
        Assert.Equal((byte)11, result.LinkedAvatarIndex);
        Assert.Equal((byte)1, result.AdditionalAvatarDatas[0].messageIndex);
        Assert.Equal(new byte[] { 5, 6, 7 }, result.AdditionalAvatarDatas[0].array);
        Assert.Equal((byte)0, result.AdditionalAvatarDatas[1].PayloadSize);
        Assert.Null(result.AdditionalAvatarDatas[1].array);
        Assert.Equal((byte)3, result.AdditionalAvatarDatas[2].messageIndex);
        Assert.Equal(new byte[] { 9 }, result.AdditionalAvatarDatas[2].array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void LocalAvatarSyncMessage_EmptyAdditionalArray_SerializesSameAsNone()
    {
        var q = BitQuality.VeryLow;
        byte[] payload = Wire.RandomBytes(new Random(33), Wire.PayloadSize(q));
        var none = new LocalAvatarSyncMessage { array = payload };
        var empty = new LocalAvatarSyncMessage { array = payload, AdditionalAvatarDatas = Array.Empty<AdditionalAvatarData>() };
        var w1 = new NetDataWriter();
        none.Serialize(w1, q);
        var w2 = new NetDataWriter();
        empty.Serialize(w2, q);
        Assert.Equal(w1.CopyData(), w2.CopyData());

        var result = default(LocalAvatarSyncMessage);
        result.Deserialize(Wire.Reader(w2));
        Assert.Null(result.AdditionalAvatarDatas);
    }

    [Fact]
    public void LocalAvatarSyncMessage_AdditionalCountOver255_SerializesSameAsNone()
    {
        var q = BitQuality.Low;
        byte[] payload = Wire.RandomBytes(new Random(34), Wire.PayloadSize(q));
        var none = new LocalAvatarSyncMessage { array = payload };
        var oversized = new LocalAvatarSyncMessage { array = payload, AdditionalAvatarDatas = new AdditionalAvatarData[256] };
        var w1 = new NetDataWriter();
        none.Serialize(w1, q);
        var w2 = new NetDataWriter();
        oversized.Serialize(w2, q);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Fact]
    public void LocalAvatarSyncMessage_NullArray_WritesStub_DeserializeNoThrow()
    {
        var msg = new LocalAvatarSyncMessage { array = null };
        var w = new NetDataWriter();
        msg.Serialize(w, BitQuality.High);
        Assert.Equal(2, w.Length);
        Assert.Equal((byte)BitQuality.High, w.Data[0]);
        Assert.Equal((byte)0, w.Data[1]);

        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Reader(w);
        var ex = Record.Exception(() => result.Deserialize(reader));
        Assert.Null(ex);
        Assert.Equal((byte)BitQuality.High, result.DataQualityLevel);
        Assert.Null(result.array);
    }

    [Fact]
    public void LocalAvatarSyncMessage_InvalidQuality_WritesStub_DeserializeNoThrow()
    {
        var msg = new LocalAvatarSyncMessage { array = new byte[4] };
        var w = new NetDataWriter();
        msg.Serialize(w, (BitQuality)9);
        Assert.Equal(2, w.Length);
        Assert.Equal((byte)9, w.Data[0]);

        var result = default(LocalAvatarSyncMessage);
        var ex = Record.Exception(() => result.Deserialize(Wire.Reader(w)));
        Assert.Null(ex);
        Assert.Equal((byte)9, result.DataQualityLevel);
        Assert.Null(result.array);
    }

    [Fact]
    public void LocalAvatarSyncMessage_EmptyReader_DeserializeNoThrow()
    {
        var result = default(LocalAvatarSyncMessage);
        var reader = Wire.Empty();
        var ex = Record.Exception(() => result.Deserialize(reader));
        Assert.Null(ex);
        Assert.Equal((byte)0, result.DataQualityLevel);
        Assert.Null(result.array);
    }

    [Fact]
    public void LocalAvatarSyncMessage_ChannelPathTruncatedPayload_NoThrow()
    {
        var reader = new NetDataReader(new byte[] { 1, 2, 3, 4, 5 });
        var result = default(LocalAvatarSyncMessage);
        var ex = Record.Exception(() => result.Deserialize(reader, (byte)BitQuality.High, hasAdditionalData: false));
        Assert.Null(ex);
        Assert.Equal((byte)BitQuality.High, result.DataQualityLevel);
        Assert.Null(result.array);
    }

    [Fact]
    public void LocalAvatarSyncMessage_PayloadPath_DoubleRoundTrip_IsByteIdentical()
    {
        var q = BitQuality.Medium;
        var msg = new LocalAvatarSyncMessage { array = Wire.RandomBytes(new Random(32), Wire.PayloadSize(q)) };
        var w1 = new NetDataWriter();
        msg.Serialize(w1, q);

        var mid = default(LocalAvatarSyncMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2, (BitQuality)mid.DataQualityLevel);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Fact]
    public void LocalAvatarSyncMessage_ChannelPath_DoubleRoundTrip_IsByteIdentical()
    {
        var q = BitQuality.Low;
        var msg = new LocalAvatarSyncMessage
        {
            array = Wire.RandomBytes(new Random(31), Wire.PayloadSize(q)),
            AdditionalAvatarDatas = new[] { new AdditionalAvatarData { messageIndex = 8, array = new byte[] { 1, 2 } } },
            LinkedAvatarIndex = 6,
        };
        var w1 = new NetDataWriter();
        msg.SerializeForChannel(w1, q);

        var mid = default(LocalAvatarSyncMessage);
        mid.Deserialize(Wire.Reader(w1), (byte)q, hasAdditionalData: true);
        var w2 = new NetDataWriter();
        mid.SerializeForChannel(w2, q);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// ServerSideSyncPlayerMessage: [playerID][interval:1][sequence:1] followed by the
/// LocalAvatarSyncMessage payload (quality byte inline, or derived from the channel).
/// </summary>
public class ServerSideSyncPlayerMessageWireTests
{
    [Theory]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.High)]
    public void ServerSideSyncPlayerMessage_RoundTrip_PreservesAllFields(BitQuality q)
    {
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(300 + (int)q), size);
        var msg = new ServerSideSyncPlayerMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = ushort.MaxValue },
            interval = 33,
            sequence = 250,
            avatarSerialization = new LocalAvatarSyncMessage { array = payload, DataQualityLevel = (byte)q },
        };
        var w = new NetDataWriter();
        msg.Serialize(w);
        Assert.Equal(2 + 1 + 1 + 1 + size + 1, w.Length);

        var result = default(ServerSideSyncPlayerMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader);
        Assert.Equal(ushort.MaxValue, result.playerIdMessage.playerID);
        Assert.Equal((byte)33, result.interval);
        Assert.Equal((byte)250, result.sequence);
        Assert.Equal((byte)q, result.avatarSerialization.DataQualityLevel);
        Assert.Equal(payload, result.avatarSerialization.array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void ServerSideSyncPlayerMessage_ChannelDeserialize_LargeIdNoAdditional()
    {
        var q = BitQuality.VeryLow;
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(40), size);
        var pid = new PlayerIdMessage { playerID = 5000 };
        var lasm = new LocalAvatarSyncMessage { array = payload };
        var w = new NetDataWriter();
        pid.Serialize(w);
        w.Put((byte)55);  // interval
        w.Put((byte)128); // sequence
        lasm.SerializeForChannel(w, q);

        var result = default(ServerSideSyncPlayerMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader, (byte)q, hasAdditionalData: false);
        Assert.Equal((ushort)5000, result.playerIdMessage.playerID);
        Assert.Equal((byte)55, result.interval);
        Assert.Equal((byte)128, result.sequence);
        Assert.Equal(payload, result.avatarSerialization.array);
        Assert.Null(result.avatarSerialization.AdditionalAvatarDatas);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void ServerSideSyncPlayerMessage_ChannelDeserialize_SmallIdWithAdditionalData()
    {
        var q = BitQuality.High;
        int size = Wire.PayloadSize(q);
        byte[] payload = Wire.RandomBytes(new Random(41), size);
        var pid = new PlayerIdMessage { playerID = 42 };
        var lasm = new LocalAvatarSyncMessage
        {
            array = payload,
            AdditionalAvatarDatas = new[] { new AdditionalAvatarData { messageIndex = 2, array = new byte[] { 4, 5, 6 } } },
            LinkedAvatarIndex = 1,
        };
        var w = new NetDataWriter();
        pid.Serialize(w, largeId: false);
        w.Put((byte)120); // interval
        w.Put((byte)7);   // sequence
        lasm.SerializeForChannel(w, q);

        var result = default(ServerSideSyncPlayerMessage);
        var reader = Wire.Reader(w);
        result.Deserialize(reader, (byte)q, hasAdditionalData: true, largeId: false);
        Assert.Equal((ushort)42, result.playerIdMessage.playerID);
        Assert.Equal((byte)120, result.interval);
        Assert.Equal((byte)7, result.sequence);
        Assert.Equal(payload, result.avatarSerialization.array);
        Assert.Equal((byte)1, result.avatarSerialization.AdditionalAvatarDataSize);
        Assert.Equal((byte)1, result.avatarSerialization.LinkedAvatarIndex);
        Assert.Equal(new byte[] { 4, 5, 6 }, result.avatarSerialization.AdditionalAvatarDatas[0].array);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void ServerSideSyncPlayerMessage_DoubleRoundTrip_IsByteIdentical()
    {
        var q = BitQuality.Medium;
        var msg = new ServerSideSyncPlayerMessage
        {
            playerIdMessage = new PlayerIdMessage { playerID = 77 },
            interval = 50,
            sequence = 1,
            avatarSerialization = new LocalAvatarSyncMessage
            {
                array = Wire.RandomBytes(new Random(42), Wire.PayloadSize(q)),
                DataQualityLevel = (byte)q,
            },
        };
        var w1 = new NetDataWriter();
        msg.Serialize(w1);

        var mid = default(ServerSideSyncPlayerMessage);
        mid.Deserialize(Wire.Reader(w1));
        var w2 = new NetDataWriter();
        mid.Serialize(w2);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }
}

/// <summary>
/// VoiceReceiversMessage: [count:1|2][ushort ids...] with byte/ushort count width chosen by
/// channel. Deserialize rents from ArrayPool, so only the UsersLength prefix is meaningful.
/// </summary>
public class VoiceReceiversMessageWireTests
{
    [Fact]
    public void VoiceReceiversMessage_LargeCount_RoundTrips()
    {
        ushort[] users = { 1, 2, 70, ushort.MaxValue };
        var msg = new VoiceReceiversMessage { Users = users, UsersLength = users.Length };
        var w = new NetDataWriter();
        msg.Serialize(w, largeCount: true);
        Assert.Equal(2 + users.Length * 2, w.Length);

        var result = default(VoiceReceiversMessage);
        result.Deserialize(Wire.Reader(w), largeCount: true);
        Assert.Equal(users.Length, result.UsersLength);
        Assert.NotNull(result.Users);
        Assert.True(result.Users.Length >= result.UsersLength); // pooled array may be larger
        Assert.Equal(users, result.Users.AsSpan(0, result.UsersLength).ToArray());
        result.ReturnPool();
    }

    [Fact]
    public void VoiceReceiversMessage_ByteCount_RoundTrips()
    {
        ushort[] users = { 5, 10, 15 };
        var msg = new VoiceReceiversMessage { Users = users, UsersLength = users.Length };
        var w = new NetDataWriter();
        msg.Serialize(w, largeCount: false);
        Assert.Equal(1 + users.Length * 2, w.Length);
        Assert.Equal((byte)3, w.Data[0]);

        var result = default(VoiceReceiversMessage);
        result.Deserialize(Wire.Reader(w), largeCount: false);
        Assert.Equal(users.Length, result.UsersLength);
        Assert.Equal(users, result.Users.AsSpan(0, result.UsersLength).ToArray());
        result.ReturnPool();
    }

    [Fact]
    public void VoiceReceiversMessage_DefaultSerialize_MatchesLargeCount()
    {
        ushort[] users = { 3, 9 };
        var m1 = new VoiceReceiversMessage { Users = users, UsersLength = users.Length };
        var m2 = new VoiceReceiversMessage { Users = users, UsersLength = users.Length };
        var w1 = new NetDataWriter();
        m1.Serialize(w1);
        var w2 = new NetDataWriter();
        m2.Serialize(w2, largeCount: true);
        Assert.Equal(w1.CopyData(), w2.CopyData());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void VoiceReceiversMessage_EmptyUsers_WritesZeroCount(bool largeCount, int expectedBytes)
    {
        var msg = new VoiceReceiversMessage();
        var w = new NetDataWriter();
        msg.Serialize(w, largeCount);
        Assert.Equal(expectedBytes, w.Length);

        var result = default(VoiceReceiversMessage);
        result.Deserialize(Wire.Reader(w), largeCount);
        Assert.NotNull(result.Users);
        Assert.Empty(result.Users);
        Assert.Equal(0, result.UsersLength);
    }

    [Fact]
    public void VoiceReceiversMessage_EmptyReader_NoThrow_EmptyUsers()
    {
        var result = default(VoiceReceiversMessage);
        var reader = Wire.Empty();
        var ex = Record.Exception(() => result.Deserialize(reader, largeCount: true));
        Assert.Null(ex);
        Assert.NotNull(result.Users);
        Assert.Empty(result.Users);
    }

    [Fact]
    public void VoiceReceiversMessage_TruncatedCount_NoThrow_EmptyUsers()
    {
        var reader = new NetDataReader(new byte[] { 42 }); // 1 byte, large channel needs 2
        var result = default(VoiceReceiversMessage);
        var ex = Record.Exception(() => result.Deserialize(reader, largeCount: true));
        Assert.Null(ex);
        Assert.NotNull(result.Users);
        Assert.Empty(result.Users);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void VoiceReceiversMessage_CountExceedsData_NoThrow_NullUsers()
    {
        var w = new NetDataWriter();
        w.Put((byte)10);  // claims 10 recipients
        w.Put((ushort)1); // but only 2 fit
        w.Put((ushort)2);
        var reader = Wire.Reader(w);
        var result = default(VoiceReceiversMessage);
        var ex = Record.Exception(() => result.Deserialize(reader, largeCount: false));
        Assert.Null(ex);
        Assert.Null(result.Users);
        Assert.Equal(0, result.UsersLength);
        Assert.Equal(0, reader.AvailableBytes);
    }

    [Fact]
    public void VoiceReceiversMessage_ByteCountOver255_TruncatesTo255()
    {
        var users = new ushort[300];
        for (int i = 0; i < users.Length; i++)
        {
            users[i] = (ushort)i;
        }
        var msg = new VoiceReceiversMessage { Users = users, UsersLength = users.Length };
        var w = new NetDataWriter();
        msg.Serialize(w, largeCount: false);
        Assert.Equal(1 + 255 * 2, w.Length);
        Assert.Equal((byte)255, w.Data[0]);

        var result = default(VoiceReceiversMessage);
        result.Deserialize(Wire.Reader(w), largeCount: false);
        Assert.Equal(255, result.UsersLength);
        Assert.Equal(users.AsSpan(0, 255).ToArray(), result.Users.AsSpan(0, 255).ToArray());
        result.ReturnPool();
    }
}
