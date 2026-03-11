using System;
using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkCore.Serializable;
using Xunit;
using static SerializableBasis;

namespace BasisServerTests
{
    public class PlayerIdMessageTests
    {
        [Theory]
        [InlineData((ushort)0)]
        [InlineData((ushort)1)]
        [InlineData((ushort)12345)]
        [InlineData(ushort.MaxValue)]
        public void SerializeDeserialize_RoundTrip(ushort id)
        {
            var msg = new PlayerIdMessage { playerID = id };
            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new PlayerIdMessage();
            result.Deserialize(reader);

            Assert.Equal(id, result.playerID);
            Assert.True(reader.EndOfData);
        }
    }

    public class ErrorMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.ErrorMessage { Message = "Something went wrong" };
            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new BasisNetworkCore.Serializable.SerializableBasis.ErrorMessage();
            result.Deserialize(reader);

            Assert.Equal("Something went wrong", result.Message);
        }

        [Fact]
        public void Deserialize_EmptyData_DoesNotCrash()
        {
            var reader = new NetDataReader(Array.Empty<byte>());
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.ErrorMessage();
            msg.Deserialize(reader); // should not throw
            Assert.Null(msg.Message);
        }
    }

    public class BytesMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            byte[] data = { 1, 2, 3, 4, 5 };
            var msg = new Basis.Network.Core.Serializable.SerializableBasis.BytesMessage();
            var writer = new NetDataWriter();
            msg.Serialize(writer, data);

            var reader = new NetDataReader(writer.CopyData());
            var result = new Basis.Network.Core.Serializable.SerializableBasis.BytesMessage();
            Assert.True(result.Deserialize(reader, out byte[] resultData));
            Assert.Equal(data, resultData);
        }

        [Fact]
        public void Deserialize_InsufficientData_ReturnsFalse()
        {
            var reader = new NetDataReader(new byte[] { 1 }); // only 1 byte, need 2 for ushort
            var msg = new Basis.Network.Core.Serializable.SerializableBasis.BytesMessage();
            Assert.False(msg.Deserialize(reader, out _));
        }
    }

    public class AdminRequestTests
    {
        [Theory]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.Ban)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.Kick)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.IpAndBan)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.Message)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.MessageAll)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.UnBanIP)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.UnBan)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.TeleportAll)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.AddAdmin)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.RemoveAdmin)]
        [InlineData(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode.TeleportPlayer)]
        public void SerializeDeserialize_AllModes(BasisNetworkCore.Serializable.SerializableBasis.AdminRequestMode mode)
        {
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.AdminRequest();
            var writer = new NetDataWriter();
            msg.Serialize(writer, mode);

            var reader = new NetDataReader(writer.CopyData());
            var result = new BasisNetworkCore.Serializable.SerializableBasis.AdminRequest();
            result.Deserialize(reader);

            Assert.Equal(mode, result.GetAdminRequestMode());
        }

        [Fact]
        public void Deserialize_EmptyData_DoesNotCrash()
        {
            var reader = new NetDataReader(Array.Empty<byte>());
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.AdminRequest();
            msg.Deserialize(reader); // should not throw, just log error
        }
    }

    public class NetIDMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.NetIDMessage { playerID = "player-123-abc" };
            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new BasisNetworkCore.Serializable.SerializableBasis.NetIDMessage();
            result.Deserialize(reader);

            Assert.Equal("player-123-abc", result.playerID);
        }

        [Fact]
        public void Deserialize_EmptyData_DoesNotCrash()
        {
            var reader = new NetDataReader(Array.Empty<byte>());
            var msg = new BasisNetworkCore.Serializable.SerializableBasis.NetIDMessage();
            msg.Deserialize(reader);
            Assert.Null(msg.playerID);
        }
    }

    public class ClientMetaDataMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            var msg = new ClientMetaDataMessage
            {
                playerUUID = "uuid-12345",
                playerDisplayName = "TestPlayer",
                playerPlatform = "Windows"
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new ClientMetaDataMessage();
            result.Deserialize(reader);

            Assert.Equal("uuid-12345", result.playerUUID);
            Assert.Equal("TestPlayer", result.playerDisplayName);
            Assert.Equal("Windows", result.playerPlatform);
        }

        [Fact]
        public void Serialize_NullFields_UsesFallback()
        {
            var msg = new ClientMetaDataMessage
            {
                playerUUID = null,
                playerDisplayName = null,
                playerPlatform = null
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new ClientMetaDataMessage();
            result.Deserialize(reader);

            Assert.Equal("Failure", result.playerUUID);
            Assert.Equal("Failure", result.playerDisplayName);
            Assert.Equal("Failure", result.playerPlatform);
        }

        [Fact]
        public void Serialize_EmptyFields_UsesFallback()
        {
            var msg = new ClientMetaDataMessage
            {
                playerUUID = "",
                playerDisplayName = "",
                playerPlatform = ""
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new ClientMetaDataMessage();
            result.Deserialize(reader);

            Assert.Equal("Failure", result.playerUUID);
            Assert.Equal("Failure", result.playerDisplayName);
            Assert.Equal("Failure", result.playerPlatform);
        }
    }

    public class ClientAvatarChangeMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            var msg = new ClientAvatarChangeMessage
            {
                loadMode = 1,
                byteArray = new byte[] { 10, 20, 30, 40 },
                LocalAvatarIndex = 5
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new ClientAvatarChangeMessage();
            result.Deserialize(reader);

            Assert.Equal(1, result.loadMode);
            Assert.Equal(new byte[] { 10, 20, 30, 40 }, result.byteArray);
            Assert.Equal(5, result.LocalAvatarIndex);
        }

        [Fact]
        public void Serialize_NullByteArray_WritesZeroLength()
        {
            var msg = new ClientAvatarChangeMessage
            {
                loadMode = 0,
                byteArray = null,
                LocalAvatarIndex = 0
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new ClientAvatarChangeMessage();
            result.Deserialize(reader);

            Assert.Equal(0, result.loadMode);
            Assert.Empty(result.byteArray);
            Assert.Equal(0, result.LocalAvatarIndex);
        }
    }

    public class AdditionalAvatarDataTests
    {
        [Fact]
        public void SerializeDeserialize_WithPayload()
        {
            var msg = new AdditionalAvatarData
            {
                messageIndex = 3,
                array = new byte[] { 10, 20, 30 }
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new AdditionalAvatarData();
            result.Deserialize(reader);

            Assert.Equal(3, result.PayloadSize);
            Assert.Equal(3, result.messageIndex);
            Assert.Equal(new byte[] { 10, 20, 30 }, result.array);
        }

        [Fact]
        public void Deserialize_ZeroPayload_ReturnsEarly()
        {
            var writer = new NetDataWriter();
            writer.Put((byte)0); // PayloadSize = 0

            var reader = new NetDataReader(writer.CopyData());
            var result = new AdditionalAvatarData();
            result.Deserialize(reader);

            Assert.Equal(0, result.PayloadSize);
        }
    }

    public class AudioSegmentDataMessageTests
    {
        [Fact]
        public void SerializeDeserialize_WithBuffer()
        {
            var msg = new AudioSegmentDataMessage
            {
                SequenceNumber = 42,
                TotalPlayedInSilence = 5,
                buffer = new byte[] { 1, 2, 3, 4 },
                LengthUsed = 4
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new AudioSegmentDataMessage();
            result.Deserialize(reader);

            Assert.Equal((byte)42, result.SequenceNumber);
            Assert.Equal(5, result.TotalPlayedInSilence);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.buffer);
            Assert.Equal(4, result.LengthUsed);
        }

        [Fact]
        public void SerializeDeserialize_SilenceOnly()
        {
            var msg = new AudioSegmentDataMessage
            {
                SequenceNumber = 100,
                TotalPlayedInSilence = 10,
                buffer = null,
                LengthUsed = 0
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new AudioSegmentDataMessage();
            result.Deserialize(reader);

            Assert.Equal((byte)100, result.SequenceNumber);
            Assert.Equal(10, result.TotalPlayedInSilence);
            Assert.Equal(0, result.LengthUsed);
        }
    }

    public class LocalAvatarSyncMessageTests
    {
        [Theory]
        [InlineData(BasisAvatarBitPacking.BitQuality.VeryLow)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Low)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Medium)]
        [InlineData(BasisAvatarBitPacking.BitQuality.High)]
        public void SerializeDeserialize_AllQualities(BasisAvatarBitPacking.BitQuality quality)
        {
            int payloadSize = BasisAvatarBitPacking.ConvertToSize(quality);
            byte[] payload = new byte[payloadSize];
            new Random(42).NextBytes(payload);

            var msg = new LocalAvatarSyncMessage
            {
                array = payload,
                AdditionalAvatarDatas = null,
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer, quality);

            var reader = new NetDataReader(writer.CopyData());
            var result = new LocalAvatarSyncMessage();
            result.Deserialize(reader);

            Assert.Equal((byte)quality, result.DataQualityLevel);
            Assert.Equal(payload, result.array);
            Assert.Equal(0, result.AdditionalAvatarDataSize);
        }

        [Fact]
        public void SerializeDeserialize_WithAdditionalData()
        {
            var quality = BasisAvatarBitPacking.BitQuality.Medium;
            int payloadSize = BasisAvatarBitPacking.ConvertToSize(quality);
            byte[] payload = new byte[payloadSize];

            var msg = new LocalAvatarSyncMessage
            {
                array = payload,
                LinkedAvatarIndex = 1,
                AdditionalAvatarDatas = new AdditionalAvatarData[]
                {
                    new AdditionalAvatarData { messageIndex = 0, array = new byte[] { 10, 20 } },
                    new AdditionalAvatarData { messageIndex = 1, array = new byte[] { 30 } }
                }
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer, quality);

            var reader = new NetDataReader(writer.CopyData());
            var result = new LocalAvatarSyncMessage();
            result.Deserialize(reader);

            Assert.Equal(2, result.AdditionalAvatarDataSize);
            Assert.Equal(1, result.LinkedAvatarIndex);
            Assert.Equal(2, result.AdditionalAvatarDatas.Length);
        }
    }

    public class OwnershipTransferMessageTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrip()
        {
            var msg = new DarkRift.Basis_Common.Serializable.SerializableBasis.OwnershipTransferMessage
            {
                playerIdMessage = new PlayerIdMessage { playerID = 42 },
                ownershipID = "ownership-key-123"
            };

            var writer = new NetDataWriter();
            msg.Serialize(writer);

            var reader = new NetDataReader(writer.CopyData());
            var result = new DarkRift.Basis_Common.Serializable.SerializableBasis.OwnershipTransferMessage();
            result.Deserialize(reader);

            Assert.Equal(42, result.playerIdMessage.playerID);
            Assert.Equal("ownership-key-123", result.ownershipID);
        }
    }
}
