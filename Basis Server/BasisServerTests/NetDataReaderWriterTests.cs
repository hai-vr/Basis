using System;
using System.Text;
using Basis.Network.Core;
using Xunit;

namespace BasisServerTests
{
    public class NetDataReaderWriterTests
    {
        #region Primitive Round-Trip Tests

        [Fact]
        public void RoundTrip_Byte()
        {
            var writer = new NetDataWriter();
            writer.Put((byte)0);
            writer.Put((byte)127);
            writer.Put((byte)255);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(0, reader.GetByte());
            Assert.Equal(127, reader.GetByte());
            Assert.Equal(255, reader.GetByte());
            Assert.True(reader.EndOfData);
        }

        [Fact]
        public void RoundTrip_SByte()
        {
            var writer = new NetDataWriter();
            writer.Put((sbyte)-128);
            writer.Put((sbyte)0);
            writer.Put((sbyte)127);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(-128, reader.GetSByte());
            Assert.Equal(0, reader.GetSByte());
            Assert.Equal(127, reader.GetSByte());
        }

        [Fact]
        public void RoundTrip_Bool()
        {
            var writer = new NetDataWriter();
            writer.Put(true);
            writer.Put(false);

            var reader = new NetDataReader(writer.CopyData());
            Assert.True(reader.GetBool());
            Assert.False(reader.GetBool());
        }

        [Fact]
        public void RoundTrip_Short()
        {
            var writer = new NetDataWriter();
            writer.Put(short.MinValue);
            writer.Put((short)0);
            writer.Put(short.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(short.MinValue, reader.GetShort());
            Assert.Equal(0, reader.GetShort());
            Assert.Equal(short.MaxValue, reader.GetShort());
        }

        [Fact]
        public void RoundTrip_UShort()
        {
            var writer = new NetDataWriter();
            writer.Put(ushort.MinValue);
            writer.Put((ushort)12345);
            writer.Put(ushort.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(ushort.MinValue, reader.GetUShort());
            Assert.Equal(12345, reader.GetUShort());
            Assert.Equal(ushort.MaxValue, reader.GetUShort());
        }

        [Fact]
        public void RoundTrip_Int()
        {
            var writer = new NetDataWriter();
            writer.Put(int.MinValue);
            writer.Put(0);
            writer.Put(int.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(int.MinValue, reader.GetInt());
            Assert.Equal(0, reader.GetInt());
            Assert.Equal(int.MaxValue, reader.GetInt());
        }

        [Fact]
        public void RoundTrip_UInt()
        {
            var writer = new NetDataWriter();
            writer.Put(uint.MinValue);
            writer.Put(uint.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(uint.MinValue, reader.GetUInt());
            Assert.Equal(uint.MaxValue, reader.GetUInt());
        }

        [Fact]
        public void RoundTrip_Long()
        {
            var writer = new NetDataWriter();
            writer.Put(long.MinValue);
            writer.Put(0L);
            writer.Put(long.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(long.MinValue, reader.GetLong());
            Assert.Equal(0L, reader.GetLong());
            Assert.Equal(long.MaxValue, reader.GetLong());
        }

        [Fact]
        public void RoundTrip_ULong()
        {
            var writer = new NetDataWriter();
            writer.Put(ulong.MinValue);
            writer.Put(ulong.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(ulong.MinValue, reader.GetULong());
            Assert.Equal(ulong.MaxValue, reader.GetULong());
        }

        [Fact]
        public void RoundTrip_Float()
        {
            var writer = new NetDataWriter();
            writer.Put(0f);
            writer.Put(3.14159f);
            writer.Put(float.MinValue);
            writer.Put(float.MaxValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(0f, reader.GetFloat());
            Assert.Equal(3.14159f, reader.GetFloat());
            Assert.Equal(float.MinValue, reader.GetFloat());
            Assert.Equal(float.MaxValue, reader.GetFloat());
        }

        [Fact]
        public void RoundTrip_Double()
        {
            var writer = new NetDataWriter();
            writer.Put(0.0);
            writer.Put(Math.PI);
            writer.Put(double.MinValue);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(0.0, reader.GetDouble());
            Assert.Equal(Math.PI, reader.GetDouble());
            Assert.Equal(double.MinValue, reader.GetDouble());
        }

        [Fact]
        public void RoundTrip_Char()
        {
            var writer = new NetDataWriter();
            writer.Put('A');
            writer.Put('Z');
            writer.Put('\0');

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal('A', reader.GetChar());
            Assert.Equal('Z', reader.GetChar());
            Assert.Equal('\0', reader.GetChar());
        }

        [Fact]
        public void RoundTrip_Guid()
        {
            var writer = new NetDataWriter();
            var guid = Guid.NewGuid();
            writer.Put(guid);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(guid, reader.GetGuid());
        }

        #endregion

        #region String Tests

        [Fact]
        public void RoundTrip_String()
        {
            var writer = new NetDataWriter();
            writer.Put("Hello, World!");

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("Hello, World!", reader.GetString());
        }

        [Fact]
        public void RoundTrip_EmptyString()
        {
            var writer = new NetDataWriter();
            writer.Put("");

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("", reader.GetString());
        }

        [Fact]
        public void RoundTrip_NullString()
        {
            var writer = new NetDataWriter();
            writer.Put((string)null);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("", reader.GetString());
        }

        [Fact]
        public void RoundTrip_UnicodeString()
        {
            var writer = new NetDataWriter();
            writer.Put("こんにちは世界 🌍");

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("こんにちは世界 🌍", reader.GetString());
        }

        [Fact]
        public void String_MaxLength_Truncates()
        {
            var writer = new NetDataWriter();
            writer.Put("Hello, World!", 5);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("Hello", reader.GetString());
        }

        [Fact]
        public void GetString_MaxLength_ReturnsEmpty_WhenExceedsLimit()
        {
            var writer = new NetDataWriter();
            writer.Put("Hello, World!");

            var reader = new NetDataReader(writer.CopyData());
            // GetString(maxLength) returns empty if char count > maxLength
            Assert.Equal("", reader.GetString(3));
        }

        [Fact]
        public void RoundTrip_LargeString()
        {
            var writer = new NetDataWriter();
            string largeStr = new string('X', 10000);
            writer.PutLargeString(largeStr);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(largeStr, reader.GetLargeString());
        }

        [Fact]
        public void LargeString_Empty()
        {
            var writer = new NetDataWriter();
            writer.PutLargeString("");

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("", reader.GetLargeString());
        }

        [Fact]
        public void LargeString_Null()
        {
            var writer = new NetDataWriter();
            writer.PutLargeString(null);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("", reader.GetLargeString());
        }

        #endregion

        #region Array Tests

        [Fact]
        public void RoundTrip_ByteArrayWithLength()
        {
            var writer = new NetDataWriter();
            byte[] data = { 1, 2, 3, 4, 5 };
            writer.PutBytesWithLength(data);

            var reader = new NetDataReader(writer.CopyData());
            byte[] result = reader.GetBytesWithLength();
            Assert.Equal(data, result);
        }

        [Fact]
        public void RoundTrip_UShortArray()
        {
            var writer = new NetDataWriter();
            ushort[] data = { 100, 200, 300, 65535 };
            writer.PutArray(data);

            var reader = new NetDataReader(writer.CopyData());
            ushort[] result = reader.GetUShortArray();
            Assert.Equal(data, result);
        }

        [Fact]
        public void RoundTrip_IntArray()
        {
            var writer = new NetDataWriter();
            int[] data = { -1, 0, 1, int.MaxValue };
            writer.PutArray(data);

            var reader = new NetDataReader(writer.CopyData());
            int[] result = reader.GetIntArray();
            Assert.Equal(data, result);
        }

        [Fact]
        public void RoundTrip_FloatArray()
        {
            var writer = new NetDataWriter();
            float[] data = { 1.5f, -2.5f, 0f };
            writer.PutArray(data);

            var reader = new NetDataReader(writer.CopyData());
            float[] result = reader.GetFloatArray();
            Assert.Equal(data, result);
        }

        [Fact]
        public void RoundTrip_StringArray()
        {
            var writer = new NetDataWriter();
            string[] data = { "hello", "world", "test" };
            writer.PutArray(data);

            var reader = new NetDataReader(writer.CopyData());
            string[] result = reader.GetStringArray();
            Assert.Equal(data, result);
        }

        [Fact]
        public void RoundTrip_BoolArray()
        {
            var writer = new NetDataWriter();
            bool[] data = { true, false, true, true, false };
            writer.PutArray(data);

            var reader = new NetDataReader(writer.CopyData());
            bool[] result = reader.GetBoolArray();
            Assert.Equal(data, result);
        }

        #endregion

        #region Peek Tests

        [Fact]
        public void PeekByte_DoesNotAdvancePosition()
        {
            var reader = new NetDataReader(new byte[] { 42 });
            Assert.Equal(42, reader.PeekByte());
            Assert.Equal(0, reader.Position);
            Assert.Equal(42, reader.GetByte());
            Assert.Equal(1, reader.Position);
        }

        [Fact]
        public void PeekUShort_DoesNotAdvancePosition()
        {
            var writer = new NetDataWriter();
            writer.Put((ushort)1234);
            var reader = new NetDataReader(writer.CopyData());

            Assert.Equal(1234, reader.PeekUShort());
            Assert.Equal(0, reader.Position);
        }

        [Fact]
        public void PeekInt_DoesNotAdvancePosition()
        {
            var writer = new NetDataWriter();
            writer.Put(99999);
            var reader = new NetDataReader(writer.CopyData());

            Assert.Equal(99999, reader.PeekInt());
            Assert.Equal(0, reader.Position);
        }

        [Fact]
        public void PeekString_DoesNotAdvancePosition()
        {
            var writer = new NetDataWriter();
            writer.Put("test");
            var reader = new NetDataReader(writer.CopyData());

            Assert.Equal("test", reader.PeekString());
            Assert.Equal(0, reader.Position);
        }

        [Fact]
        public void PeekBool_DoesNotAdvancePosition()
        {
            var writer = new NetDataWriter();
            writer.Put(true);
            var reader = new NetDataReader(writer.CopyData());

            Assert.True(reader.PeekBool());
            Assert.Equal(0, reader.Position);
        }

        #endregion

        #region TryGet Tests

        [Fact]
        public void TryGetByte_ReturnsFalse_WhenEmpty()
        {
            var reader = new NetDataReader(Array.Empty<byte>());
            Assert.False(reader.TryGetByte(out _));
        }

        [Fact]
        public void TryGetByte_ReturnsTrue_WhenAvailable()
        {
            var reader = new NetDataReader(new byte[] { 42 });
            Assert.True(reader.TryGetByte(out byte val));
            Assert.Equal(42, val);
        }

        [Fact]
        public void TryGetUShort_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1 });
            Assert.False(reader.TryGetUShort(out _));
        }

        [Fact]
        public void TryGetInt_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3 });
            Assert.False(reader.TryGetInt(out _));
        }

        [Fact]
        public void TryGetLong_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
            Assert.False(reader.TryGetLong(out _));
        }

        [Fact]
        public void TryGetFloat_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1, 2 });
            Assert.False(reader.TryGetFloat(out _));
        }

        [Fact]
        public void TryGetDouble_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3, 4 });
            Assert.False(reader.TryGetDouble(out _));
        }

        [Fact]
        public void TryGetString_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1 });
            Assert.False(reader.TryGetString(out _));
        }

        [Fact]
        public void TryGetBool_ReturnsFalse_WhenEmpty()
        {
            var reader = new NetDataReader(Array.Empty<byte>());
            Assert.False(reader.TryGetBool(out _));
        }

        [Fact]
        public void TryGetChar_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 1 });
            Assert.False(reader.TryGetChar(out _));
        }

        [Fact]
        public void TryGetStringArray_ReturnsFalse_WhenEmpty()
        {
            var reader = new NetDataReader(new byte[] { 0 }); // not enough for ushort
            Assert.False(reader.TryGetStringArray(out _));
        }

        [Fact]
        public void TryGetBytesWithLength_ReturnsFalse_WhenInsufficientData()
        {
            var reader = new NetDataReader(new byte[] { 5, 0 }); // says length=5 but no data
            Assert.False(reader.TryGetBytesWithLength(out _));
        }

        #endregion

        #region Writer Tests

        [Fact]
        public void Writer_Reset_ResetsPosition()
        {
            var writer = new NetDataWriter();
            writer.Put(42);
            Assert.Equal(4, writer.Length);
            writer.Reset();
            Assert.Equal(0, writer.Length);
        }

        [Fact]
        public void Writer_CopyData_ReturnsCorrectSlice()
        {
            var writer = new NetDataWriter();
            writer.Put((byte)1);
            writer.Put((byte)2);

            byte[] copy = writer.CopyData();
            Assert.Equal(2, copy.Length);
            Assert.Equal(1, copy[0]);
            Assert.Equal(2, copy[1]);
        }

        [Fact]
        public void Writer_SetPosition_ReturnsPrevious()
        {
            var writer = new NetDataWriter();
            writer.Put(42);
            int prev = writer.SetPosition(0);
            Assert.Equal(4, prev);
            Assert.Equal(0, writer.Length);
        }

        [Fact]
        public void Writer_AutoResize_GrowsBuffer()
        {
            var writer = new NetDataWriter(true, 4);
            // Write more than initial size
            for (int i = 0; i < 100; i++)
                writer.Put(i);

            Assert.Equal(400, writer.Length);
            var reader = new NetDataReader(writer.CopyData());
            for (int i = 0; i < 100; i++)
                Assert.Equal(i, reader.GetInt());
        }

        [Fact]
        public void Writer_FromBytes_Copy()
        {
            byte[] source = { 1, 2, 3 };
            var writer = NetDataWriter.FromBytes(source, true);
            Assert.Equal(3, writer.Length);

            // Modifying source should not affect writer
            source[0] = 99;
            Assert.Equal(1, writer.Data[0]);
        }

        [Fact]
        public void Writer_FromBytes_NoCopy()
        {
            byte[] source = { 1, 2, 3 };
            var writer = NetDataWriter.FromBytes(source, false);
            Assert.Equal(3, writer.Length);
            Assert.Same(source, writer.Data);
        }

        [Fact]
        public void Writer_FromString()
        {
            var writer = NetDataWriter.FromString("test");
            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal("test", reader.GetString());
        }

        [Fact]
        public void Writer_PutByteArray()
        {
            var writer = new NetDataWriter();
            byte[] data = { 10, 20, 30 };
            writer.Put(data);

            var reader = new NetDataReader(writer.CopyData());
            byte[] result = new byte[3];
            reader.GetBytes(result, 3);
            Assert.Equal(data, result);
        }

        [Fact]
        public void Writer_PutByteArray_WithOffset()
        {
            var writer = new NetDataWriter();
            byte[] data = { 10, 20, 30, 40, 50 };
            writer.Put(data, 1, 3);

            Assert.Equal(3, writer.Length);
            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(20, reader.GetByte());
            Assert.Equal(30, reader.GetByte());
            Assert.Equal(40, reader.GetByte());
        }

        [Fact]
        public void Writer_AsReadOnlySpan()
        {
            var writer = new NetDataWriter();
            writer.Put((byte)1);
            writer.Put((byte)2);

            var span = writer.AsReadOnlySpan();
            Assert.Equal(2, span.Length);
            Assert.Equal(1, span[0]);
            Assert.Equal(2, span[1]);
        }

        #endregion

        #region Reader Tests

        [Fact]
        public void Reader_Properties()
        {
            byte[] data = { 1, 2, 3, 4, 5 };
            var reader = new NetDataReader(data);

            Assert.Equal(5, reader.RawDataSize);
            Assert.Equal(0, reader.Position);
            Assert.Equal(5, reader.AvailableBytes);
            Assert.False(reader.EndOfData);
            Assert.False(reader.IsNull);
        }

        [Fact]
        public void Reader_SetSource_WithOffset()
        {
            byte[] data = { 0, 0, 42, 99, 0 };
            var reader = new NetDataReader(data, 2, 4);

            Assert.Equal(2, reader.UserDataOffset);
            Assert.Equal(2, reader.UserDataSize);
            Assert.Equal(42, reader.GetByte());
            Assert.Equal(99, reader.GetByte());
        }

        [Fact]
        public void Reader_SkipBytes()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3, 4 });
            reader.SkipBytes(2);
            Assert.Equal(2, reader.Position);
            Assert.Equal(3, reader.GetByte());
        }

        [Fact]
        public void Reader_SetPosition()
        {
            var reader = new NetDataReader(new byte[] { 10, 20, 30 });
            reader.GetByte(); // advance to 1
            reader.SetPosition(0); // back to start
            Assert.Equal(10, reader.GetByte());
        }

        [Fact]
        public void Reader_GetRemainingBytes()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3, 4, 5 });
            reader.GetByte(); // skip 1
            byte[] remaining = reader.GetRemainingBytes();
            Assert.Equal(new byte[] { 2, 3, 4, 5 }, remaining);
        }

        [Fact]
        public void Reader_GetBytesSegment()
        {
            var reader = new NetDataReader(new byte[] { 10, 20, 30, 40 });
            reader.GetByte(); // skip first
            var segment = reader.GetBytesSegment(2);
            Assert.Equal(2, segment.Count);
            Assert.Equal(20, segment.Array[segment.Offset]);
        }

        [Fact]
        public void Reader_Clear()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3 });
            reader.Clear();
            Assert.True(reader.IsNull);
            Assert.Equal(0, reader.Position);
            Assert.Equal(0, reader.RawDataSize);
        }

        [Fact]
        public void Reader_GetRemainingBytesSpan()
        {
            var reader = new NetDataReader(new byte[] { 1, 2, 3 });
            reader.GetByte();
            var span = reader.GetRemainingBytesSpan();
            Assert.Equal(2, span.Length);
            Assert.Equal(2, span[0]);
            Assert.Equal(3, span[1]);
        }

        #endregion

        #region Mixed Data Round-Trip

        [Fact]
        public void RoundTrip_MixedData()
        {
            var writer = new NetDataWriter();
            writer.Put((byte)42);
            writer.Put(true);
            writer.Put((ushort)1234);
            writer.Put(999999);
            writer.Put(3.14f);
            writer.Put("hello");
            writer.Put((long)-123456789L);

            var reader = new NetDataReader(writer.CopyData());
            Assert.Equal(42, reader.GetByte());
            Assert.True(reader.GetBool());
            Assert.Equal(1234, reader.GetUShort());
            Assert.Equal(999999, reader.GetInt());
            Assert.Equal(3.14f, reader.GetFloat());
            Assert.Equal("hello", reader.GetString());
            Assert.Equal(-123456789L, reader.GetLong());
            Assert.True(reader.EndOfData);
        }

        #endregion
    }
}
