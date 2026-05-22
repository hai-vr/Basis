#nullable enable

using System;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basis.Contrib.Crypto
{
	/// HKDF-SHA256 (RFC 5869). Used to expand an X25519 shared secret into
	/// directional AEAD keys.
	public static class BasisHkdf
	{
		public static byte[] DeriveKey(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, int length)
		{
			var generator = new HkdfBytesGenerator(new Sha256Digest());
			generator.Init(new HkdfParameters(ikm.ToArray(), salt.ToArray(), info.ToArray()));
			var output = new byte[length];
			generator.GenerateBytes(output, 0, length);
			return output;
		}
	}
}
