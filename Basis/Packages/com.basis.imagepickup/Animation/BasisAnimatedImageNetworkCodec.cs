using System;
using System.IO;
using Basis.Scripts.Common;
using Unity.Collections;

namespace Basis.ImagePickup
{
	internal static class BasisAnimatedImageNetworkCodec
	{
		public const int AnimationHeaderSize =
			1 + BasisGuid128.SerializedSize + 1 + sizeof(int) * 2 + sizeof(long);
		public const int AnimationChunkHeaderSize =
			1 + BasisGuid128.SerializedSize + sizeof(int) * 2;

		public static void WriteGuid(BinaryWriter writer, Guid value)
		{
			BasisGuid128 id = BasisGuid128.FromGuid(value);
			writer.Write(id.Low);
			writer.Write(id.High);
		}

		public static void WriteGuid(
			NativeArray<byte> destination,
			int offset,
			BasisGuid128 value
		)
		{
			for (int i = 0; i < 8; i++)
				destination[offset + i] = (byte)(value.Low >> (i * 8));
			for (int i = 0; i < 8; i++)
				destination[offset + i + 8] = (byte)(value.High >> (i * 8));
		}
	}
}
